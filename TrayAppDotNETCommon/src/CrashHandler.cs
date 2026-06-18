using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon;

public sealed record CrashHandlerOptions(
    string ApplicationName,
    SingleInstanceIdentity SingleInstanceIdentity,
    Action<string> Log,
    Action FlushLog)
{
    public int CrashRestartDelayMs { get; init; } = 1_000;
    public int RapidRestartDetectionWindowMs { get; init; } = 30_000;
}

/// <summary>
/// Process-level fatal exception wiring plus the watcher process loop.
/// </summary>
public static class CrashHandler
{
    private const int MaxRapidRestarts = 5;
    private const int WatcherHeapHardLimitBytes = 32 * 1024 * 1024;
    private const string WatcherOriginalEnvironmentPrefix = "TrayAppDotNET_WATCHER_ORIGINAL_";
    private const string WatcherOriginalEnvironmentUnset = "__TrayAppDotNET_UNSET__";
    public const int FatalExceptionExitCode = 2;

    private static readonly KeyValuePair<string, string>[] WatcherGcEnvironment =
    [
        new("DOTNET_gcServer", "0"),
        new("DOTNET_gcConcurrent", "0"),
        new("DOTNET_GCConserveMemory", "9"),
        new("DOTNET_GCHeapHardLimit", ToHexEnvironmentValue(WatcherHeapHardLimitBytes)),
        new("DOTNET_GCRetainVM", "0"),
    ];

    private static CrashHandlerOptions? _options;

    public static void Configure(CrashHandlerOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public static void WireCrashHandlers()
    {
        CrashHandlerOptions options = Options;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            options.Log($"FATAL UnhandledException: {args.ExceptionObject}");
            options.FlushLog();
            Environment.Exit(FatalExceptionExitCode);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            options.Log($"FATAL UnobservedTaskException: {args.Exception}");
            options.FlushLog();
            Environment.Exit(FatalExceptionExitCode);
        };
    }

    public static int RunWatcher()
    {
        CrashHandlerOptions options = Options;
        string exePath = Environment.ProcessPath ?? "";
        string? exeDir = Path.GetDirectoryName(exePath);

        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            ShowError("Cannot determine executable path.");
            return 1;
        }

        using SingleInstanceCoordinator coordinator =
            SingleInstanceCoordinator.AcquireOrTakeover(options.SingleInstanceIdentity);
        Span<long> restartTimes = stackalloc long[MaxRapidRestarts];
        int restartCount = 0;

        WatchedProcess childProcess = LaunchApplication(exePath, exeDir ?? ".");
        if (!childProcess.IsValid)
        {
            ShowError($"Failed to start {options.ApplicationName}");
            return 1;
        }

        RecordMonitoredProcess(coordinator, childProcess.Id);

        while (true)
        {
            if (Kernel32.WaitForSingleObject(childProcess.ProcessHandle, Kernel32.INFINITE) != Kernel32.WAIT_OBJECT_0)
                break;

            int exitCode = childProcess.ExitCode;
            childProcess.Dispose();
            childProcess = default;

            if (IsUserExitCode(exitCode)) break;

            long now = Environment.TickCount64;
            RecordRestart(restartTimes, ref restartCount, now, options.RapidRestartDetectionWindowMs);

            if (restartCount >= MaxRapidRestarts)
            {
                ShowError(
                    $"{options.ApplicationName} has crashed repeatedly.\n\n" +
                    "The crash handler will not attempt further restarts.\n" +
                    "Please check for issues and restart manually.");
                break;
            }

            Thread.Sleep(options.CrashRestartDelayMs);

            childProcess = LaunchApplication(exePath, exeDir ?? ".");
            if (!childProcess.IsValid)
            {
                ShowError($"Failed to restart {options.ApplicationName}");
                break;
            }

            RecordMonitoredProcess(coordinator, childProcess.Id);
        }

        childProcess.Dispose();
        return 0;
    }

    public static bool LaunchWatcherDetached()
    {
        string exePath = Environment.ProcessPath ?? "";

        if (string.IsNullOrEmpty(exePath)) return false;

        ProcessStartInfo startInfo = new()
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--watcher");
        ConfigureWatcherEnvironment(startInfo);

        try
        {
            using Process? watcher = Process.Start(startInfo);
            return watcher != null;
        }
        catch (Exception ex)
        {
            Options.Log($"CrashHandler.LaunchWatcherDetached: {ex.Message}");
            return false;
        }
    }

    private static WatchedProcess LaunchApplication(string exePath, string workDir)
    {
        try
        {
            int watcherPID = Environment.ProcessId;
            StringBuilder commandLine = new($"\"{exePath}\" --monitored --watcher-pid {watcherPID}");
            STARTUPINFO startupInfo = new()
            {
                cb = (uint)Marshal.SizeOf<STARTUPINFO>(), dwFlags = STARTF_USESHOWWINDOW, wShowWindow = SW_HIDE
            };

            IntPtr environmentBlock = IntPtr.Zero;
            try
            {
                environmentBlock = Marshal.StringToHGlobalUni(BuildMonitoredEnvironmentBlock());

                if (!CreateProcess(
                    exePath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
                    environmentBlock,
                    workDir,
                    ref startupInfo,
                    out PROCESS_INFORMATION processInfo))
                    return default;

                if (processInfo.hThread != IntPtr.Zero) Kernel32.CloseHandle(processInfo.hThread);
                return new WatchedProcess(processInfo.hProcess, (int)processInfo.dwProcessId);
            }
            finally
            {
                if (environmentBlock != IntPtr.Zero)
                    Marshal.FreeHGlobal(environmentBlock);
            }
        }
        catch (Exception ex)
        {
            Options.Log($"CrashHandler.LaunchApplication: {ex.Message}");
            return default;
        }
    }

    private static void RecordMonitoredProcess(SingleInstanceCoordinator coordinator, int pid)
    {
        coordinator.RecordMonitoredPID(pid);
        TrimWatcherMemory();
    }

    private static void ConfigureWatcherEnvironment(ProcessStartInfo startInfo)
    {
        foreach ((string key, string value) in WatcherGcEnvironment)
        {
            string originalKey = OriginalEnvironmentKey(key);
            startInfo.Environment[originalKey] =
                Environment.GetEnvironmentVariable(key) ?? WatcherOriginalEnvironmentUnset;
            startInfo.Environment[key] = value;
        }
    }

    private static string BuildMonitoredEnvironmentBlock()
    {
        SortedDictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry is { Key: string key, Value: string value })
                environment[key] = value;
        }

        foreach ((string key, _) in WatcherGcEnvironment)
        {
            string originalKey = OriginalEnvironmentKey(key);
            if (environment.TryGetValue(originalKey, out string? originalValue))
            {
                if (string.Equals(originalValue, WatcherOriginalEnvironmentUnset, StringComparison.Ordinal))
                    environment.Remove(key);
                else
                    environment[key] = originalValue;

                environment.Remove(originalKey);
            }
            else
            {
                environment.Remove(key);
            }
        }

        string[] helperKeys = [.. environment.Keys.Where(key => key.StartsWith(WatcherOriginalEnvironmentPrefix, StringComparison.OrdinalIgnoreCase))];
        foreach (string helperKey in helperKeys)
            environment.Remove(helperKey);

        StringBuilder block = new();
        foreach ((string key, string value) in environment)
        {
            if (string.IsNullOrEmpty(key) || key.Contains('=') ||
                key.Contains('\0') || value.Contains('\0'))
                continue;

            block.Append(key);
            block.Append('=');
            block.Append(value);
            block.Append('\0');
        }

        block.Append('\0');
        return block.ToString();
    }

    private static void TrimWatcherMemory()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        catch
        {
        }

        try
        {
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch
        {
        }
    }

    private static string OriginalEnvironmentKey(string key) =>
        WatcherOriginalEnvironmentPrefix + key;

    private static string ToHexEnvironmentValue(int value) =>
        "0x" + value.ToString("X", CultureInfo.InvariantCulture);

    private static bool IsUserExitCode(int exitCode) => exitCode is 0 or 1;

    private static void RecordRestart(Span<long> restartTimes, ref int restartCount, long now, int windowMs)
    {
        if (restartCount < MaxRapidRestarts)
        {
            restartTimes[restartCount++] = now;
        }
        else
        {
            for (int i = 1; i < MaxRapidRestarts; i++)
                restartTimes[i - 1] = restartTimes[i];
            restartTimes[^1] = now;
        }

        int keep = 0;
        for (int i = 0; i < restartCount; i++)
        {
            if ((now - restartTimes[i]) <= windowMs)
                restartTimes[keep++] = restartTimes[i];
        }

        restartCount = keep;
    }

    private static void ShowError(string message)
    {
        CrashHandlerOptions options = Options;
        _ = MessageBox(IntPtr.Zero, message, $"{options.ApplicationName} Crash Handler", 0x10);
    }

    private static CrashHandlerOptions Options =>
        _options ?? throw new InvalidOperationException("CrashHandler.Configure must be called before use.");

    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint STARTF_USESHOWWINDOW = 0x00000001;
    private const ushort SW_HIDE = 0;

    private readonly struct WatchedProcess(IntPtr processHandle, int id) : IDisposable
    {
        public IntPtr ProcessHandle { get; } = processHandle;

        public int Id { get; } = id;

        public bool IsValid => ProcessHandle != IntPtr.Zero;

        public int ExitCode =>
            Kernel32.GetExitCodeProcess(ProcessHandle, out uint exitCode)
                ? unchecked((int)exitCode)
                : FatalExceptionExitCode;

        public void Dispose()
        {
            if (ProcessHandle != IntPtr.Zero)
                Kernel32.CloseHandle(ProcessHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public uint cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
