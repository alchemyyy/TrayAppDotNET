using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon;

public sealed record CrashHandlerOptions(
    string ApplicationName,
    SingleInstanceIdentity SingleInstanceIdentity,
    Action<string> Log,
    Action FlushLog)
{
    public int CrashRestartDelayMs { get; init; } = TimeConstants.CrashRestartDelayMs;
    public int RapidRestartDetectionWindowMs { get; init; } = TimeConstants.RapidRestartDetectionWindowMs;
}

/// <summary>What the watcher does after its monitored app exits with a given code.</summary>
internal enum WatcherExitAction
{
    /// <summary>The user or another process ended the app on purpose; the watcher exits too.</summary>
    Stop,

    /// <summary>The app crashed; restart it, subject to the rapid-restart limit.</summary>
    RestartAfterCrash,

    /// <summary>
    /// The app exited because Windows asked the session to end, and the watcher saw the session shutting down when
    /// the app exited. Relaunch it only once Windows no longer reports the session as shutting down (the logoff or
    /// shutdown was cancelled); the relaunch counts toward the rapid-restart limit like a crash restart.
    /// </summary>
    RelaunchIfSessionContinues
}

/// <summary>
/// Process-level exception policy plus the watcher process loop.
/// Policy: an unhandled exception on any thread is fatal (logged, then exit code
/// <see cref="FatalExceptionExitCode"/> so the watcher restarts the app); an unobserved task exception is logged in
/// full and marked observed, never fatal, because it surfaces at an unpredictable time when the finalizer runs.
/// UI dispatcher exceptions are logged in full and handled (<see cref="DescribeException"/>).
/// </summary>
public static class CrashHandler
{
    internal const int MaxRapidRestarts = 5;
    private const int WatcherHeapHardLimitBytes = 32 * 1024 * 1024;
    private const string WatcherOriginalEnvironmentPrefix = "TrayAppDotNET_WATCHER_ORIGINAL_";
    private const string WatcherOriginalEnvironmentUnset = "__TrayAppDotNET_UNSET__";
    private const int SM_SHUTTINGDOWN = 0x2000;
    public const int FatalExceptionExitCode = 2;

    /// <summary>
    /// Exit code of an app that exited because the Windows session is ending. Native runtimes also exit with 3 (for
    /// example the C runtime's abort), so the watcher honors it only while it sees the session shutting down itself.
    /// </summary>
    public const int SessionEndingExitCode = 3;

    private static readonly KeyValuePair<string, string>[] WatcherGcEnvironment =
    [
        new(key: "DOTNET_gcServer", value: "0"),
        new(key: "DOTNET_gcConcurrent", value: "0"),
        new(key: "DOTNET_GCConserveMemory", value: "9"),
        new(key: "DOTNET_GCHeapHardLimit", ToHexEnvironmentValue(WatcherHeapHardLimitBytes)),
        new(key: "DOTNET_GCRetainVM", value: "0")
    ];

    private static CrashHandlerOptions? _options;

    public static void Configure(CrashHandlerOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Logs a recoverable failure through the configured crash log sink.</summary>
    public static void LogNonFatal(string message)
    {
        CrashHandlerOptions options = Options;
        options.Log(message);
        options.FlushLog();
    }

    public static void WireCrashHandlers()
    {
        CrashHandlerOptions options = Options;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            options.Log($"FATAL UnhandledException: {args.ExceptionObject}");
            options.FlushLog();
            Environment.Exit(FatalExceptionExitCode);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => LogUnobservedTaskException(options, args);
    }

    /// <summary>Logs an unobserved task exception with its full detail and marks it observed.</summary>
    internal static void LogUnobservedTaskException(CrashHandlerOptions options, UnobservedTaskExceptionEventArgs args)
    {
        args.SetObserved();
        options.Log($"UnobservedTaskException: {DescribeException(args.Exception)}");
        options.FlushLog();
    }

    /// <summary>The log text for a recoverable exception: type, message, stack trace and inner exceptions.</summary>
    public static string DescribeException(Exception exception) => exception.ToString();

    /// <summary>
    /// Maps a monitored app's exit code to what its watcher does next. <paramref name="sessionShuttingDown"/> is
    /// what the watcher observed when the app exited; without it the session-ending code is an ordinary crash.
    /// </summary>
    internal static WatcherExitAction ClassifyExitCode(int exitCode, bool sessionShuttingDown) =>
        exitCode switch
        {
            0 or 1 => WatcherExitAction.Stop,
            SessionEndingExitCode when sessionShuttingDown => WatcherExitAction.RelaunchIfSessionContinues,
            _ => WatcherExitAction.RestartAfterCrash
        };

    /// <summary>
    /// Waits out a session end: sleeps the relaunch delay, then polls until Windows stops reporting the session as
    /// shutting down. If the session does end, Windows terminates this process while it waits.
    /// </summary>
    internal static void WaitForSessionToContinue(Func<bool> isSessionShuttingDown, Action<int> sleep)
    {
        sleep(TimeConstants.SessionEndRelaunchDelayMs);
        while (isSessionShuttingDown())
            sleep(TimeConstants.SessionEndShutdownPollIntervalMs);
    }

    internal static bool IsSessionShuttingDown() => User32.GetSystemMetrics(SM_SHUTTINGDOWN) != 0;

    /// <summary>
    /// Runs the watcher loop. <paramref name="launchArguments"/>, the arguments
    /// <see cref="TrayAppDotNETProgram.SelectForwardedLaunchArguments"/> picks, are forwarded to every monitored
    /// process it starts.
    /// </summary>
    public static int RunWatcher(IReadOnlyList<string> launchArguments)
    {
        CrashHandlerOptions options = Options;
        string exePath = Environment.ProcessPath ?? "";
        string? exeDir = Path.GetDirectoryName(exePath);

        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            ShowError(CommonStrings.CrashHandler_NoExecutablePath);
            return 1;
        }

        using SingleInstanceCoordinator coordinator =
            SingleInstanceCoordinator.AcquireOrTakeover(options.SingleInstanceIdentity);
        Span<long> restartTimes = stackalloc long[MaxRapidRestarts];
        int restartCount = 0;

        WatchedProcess childProcess = LaunchApplication(exePath, exeDir ?? ".", launchArguments);
        if (!childProcess.IsValid)
        {
            ShowError(Format(CommonStrings.CrashHandler_StartFailed_Format, options.ApplicationName));
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

            WatcherExitAction action = ClassifyExitCode(
                exitCode,
                exitCode == SessionEndingExitCode && IsSessionShuttingDown());
            if (action == WatcherExitAction.Stop) break;

            // Every relaunch counts, so an app that keeps dying at startup cannot loop forever
            long now = Environment.TickCount64;
            RecordRestart(restartTimes, ref restartCount, now, options.RapidRestartDetectionWindowMs);
            if (restartCount >= MaxRapidRestarts)
            {
                ShowError(Format(CommonStrings.CrashHandler_CrashedRepeatedly_Format, options.ApplicationName));
                break;
            }

            if (action == WatcherExitAction.RelaunchIfSessionContinues)
                WaitForSessionToContinue(IsSessionShuttingDown, Thread.Sleep);
            else
                Thread.Sleep(options.CrashRestartDelayMs);

            childProcess = LaunchApplication(exePath, exeDir ?? ".", launchArguments);
            if (!childProcess.IsValid)
            {
                ShowError(Format(CommonStrings.CrashHandler_RestartFailed_Format, options.ApplicationName));
                break;
            }

            RecordMonitoredProcess(coordinator, childProcess.Id);
        }

        childProcess.Dispose();
        return 0;
    }

    /// <summary>Starts the watcher process, which passes <paramref name="launchArguments"/> on to the app it monitors.</summary>
    public static bool LaunchWatcherDetached(IReadOnlyList<string> launchArguments)
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
        foreach (string argument in launchArguments) startInfo.ArgumentList.Add(argument);
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

    private static WatchedProcess LaunchApplication(string exePath, string workDir, IReadOnlyList<string> launchArguments)
    {
        try
        {
            int watcherPID = Environment.ProcessId;
            StringBuilder commandLine = new($"\"{exePath}\" --monitored --watcher-pid {watcherPID}");
            // Crash restarts keep the original launch arguments, so a startup or hidden launch never reopens a
            // window and a settings folder override stays in effect
            foreach (string argument in launchArguments)
                commandLine.Append(' ').Append(TrayAppDotNETProgram.QuoteCommandLineArgument(argument));
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
                        bInheritHandles: false,
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
                environment.Remove(key);
        }

        string[] helperKeys =
        [
            .. environment.Keys.Where(key =>
                key.StartsWith(WatcherOriginalEnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
        ];
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
            ManagedHeap.CompactAndDecommit();
        }
        catch
        {
        }

        try
        {
            using Process currentProcess = Process.GetCurrentProcess();
            EmptyWorkingSet(currentProcess.Handle);
        }
        catch
        {
        }
    }

    private static string OriginalEnvironmentKey(string key) =>
        WatcherOriginalEnvironmentPrefix + key;

    private static string ToHexEnvironmentValue(int value) =>
        "0x" + value.ToString(format: "X", CultureInfo.InvariantCulture);

    internal static void RecordRestart(Span<long> restartTimes, ref int restartCount, long now, int windowMs)
    {
        if (restartCount < MaxRapidRestarts)
            restartTimes[restartCount++] = now;
        else
        {
            for (int i = 1; i < MaxRapidRestarts; i++)
                restartTimes[i - 1] = restartTimes[i];
            restartTimes[^1] = now;
        }

        int keep = 0;
        for (int i = 0; i < restartCount; i++)
        {
            if (now - restartTimes[i] <= windowMs)
                restartTimes[keep++] = restartTimes[i];
        }

        restartCount = keep;
    }

    private static void ShowError(string message)
    {
        CrashHandlerOptions options = Options;
        _ = User32.MessageBox(
            IntPtr.Zero,
            message,
            Format(CommonStrings.CrashHandler_Title_Format, options.ApplicationName),
            User32.MB_ICONERROR);
    }

    private static string Format(string format, string argument) =>
        string.Format(CultureInfo.CurrentCulture, format, argument);

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
}
