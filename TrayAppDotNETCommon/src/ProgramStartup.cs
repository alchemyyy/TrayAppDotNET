using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using TrayAppDotNETCommon.Services.Install;

namespace TrayAppDotNETCommon;

public sealed record TrayAppDotNETProgramInstallResult(
    bool Success,
    string? ErrorMessage = null,
    bool UserCancelled = false)
{
    public static TrayAppDotNETProgramInstallResult From(TrayAppDotNETInstallResult result) =>
        new(result.Success, result.ErrorMessage, result.UserCancelled);
}

public sealed record TrayAppDotNETProgramOptions(
    string ApplicationName,
    string SharedRootFolderName,
    string AppGuid,
    Func<string[], int> RunApplication,
    Func<string, int, TrayAppDotNETProgramInstallResult> RunAdminInstallSystem,
    Action<InstallScope?, bool> SyncStartMenu,
    Func<TrayAppDotNETProgramInstallResult> InstallToLocalAppData,
    Func<TrayAppDotNETProgramInstallResult> InstallSystemWide,
    Func<string> LocalAppDataInstallExecutable,
    Func<string> ProgramFilesInstallExecutable,
    Action<string>? Log = null,
    Action? FlushLog = null);

public static class TrayAppDotNETProgram
{
    private const int TerminateRunningCopiesTimeoutMs = 5000;
    private const string NoWatcherEnvironmentVariable = "TrayAppDotNET_NO_WATCHER";
    private const string LegacyBrightnessNoWatcherEnvironmentVariable = "BTAWPF_NO_WATCHER";

    private static SingleInstanceCoordinator? _singleInstanceCoordinator;

    public static int? WatcherPID { get; private set; }

    public static bool IsUninstallerMode { get; private set; }

    public static string? UninstallerInstallDir { get; private set; }

    public static InstallScope UninstallerScope { get; private set; } = InstallScope.LocalAppData;

    public static string LocalAppDataRoot(string sharedRootFolderName) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            sharedRootFolderName);

    public static string AppLocalAppDataDirectory(string applicationName, string sharedRootFolderName) =>
        Path.Combine(LocalAppDataRoot(sharedRootFolderName), applicationName);

    public static int Run(
        string[] args,
        string applicationName,
        string appGuid,
        Func<TrayAppDotNETProgramOptions> createOptions)
    {
        ResetState();

        if (HasArg(args, "--watcher"))
        {
            CrashHandler.Configure(new CrashHandlerOptions(
                applicationName,
                new SingleInstanceIdentity(applicationName, appGuid),
                NoopLog,
                NoopFlush));
            return CrashHandler.RunWatcher();
        }

        if (ShouldLaunchWatcherBeforeConfiguring(args))
        {
            CrashHandler.Configure(new CrashHandlerOptions(
                applicationName,
                new SingleInstanceIdentity(applicationName, appGuid),
                NoopLog,
                NoopFlush));
            return CrashHandler.LaunchWatcherDetached() ? 0 : 1;
        }

        return RunConfigured(args, createOptions());
    }

    public static int Run(string[] args, TrayAppDotNETProgramOptions options)
    {
        ResetState();
        return RunConfigured(args, options);
    }

    private static int RunConfigured(string[] args, TrayAppDotNETProgramOptions options)
    {
        Action<string> log = options.Log ?? TADNLog.Log;
        Action flush = options.FlushLog ?? TADNLog.Flush;
        string appDataDirectory = AppLocalAppDataDirectory(options.ApplicationName, options.SharedRootFolderName);
        SingleInstanceIdentity singleInstanceIdentity = new(options.ApplicationName, options.AppGuid);

        TADNLog.Initialize(appDataDirectory);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            ReleaseSingleInstance();
            flush();
        };
        CrashHandler.Configure(new CrashHandlerOptions(
            options.ApplicationName,
            singleInstanceIdentity,
            log,
            flush));

        if (TryGetArgValue(args, "--admin-action") is { } adminVerb)
            return RunOnStaThreadIfNeeded(() => RunAdminAction(adminVerb, args, options, log));

        if (HasArg(args, "--installlocal"))
            return RunOnStaThreadIfNeeded(() => RunInstall("local", options, log, startInstalled: true));

        if (HasArg(args, "--installsystem"))
            return RunOnStaThreadIfNeeded(() => RunInstall("system", options, log, startInstalled: true));

        if (HasArg(args, "--install"))
            return RunOnStaThreadIfNeeded(() =>
                RunInstall(TryGetArgValue(args, "--install"), options, log, startInstalled: false));

        if (TryGetArgValue(args, "--uninstall") is { } installDir)
            return RunUninstall(args, installDir, options);

        bool isWatcher = HasArg(args, "--watcher");
        bool isMonitored = HasArg(args, "--monitored");

        if (isWatcher) return CrashHandler.RunWatcher();

        if (!isMonitored && !Debugger.IsAttached && !NoWatcherRequested())
        {
            if (!CrashHandler.LaunchWatcherDetached())
                return 1;
            return 0;
        }

        WatcherPID = ParseWatcherPID(args);
        bool shouldOwnSingleInstance =
            !isMonitored ||
            WatcherPID is null;

        if (shouldOwnSingleInstance &&
            !AcquireSingleInstance(singleInstanceIdentity, WatcherPID ?? 0, Environment.ProcessId, log))
            return 1;

        try
        {
            return options.RunApplication(args);
        }
        finally
        {
            ReleaseSingleInstance();
        }
    }

    public static int? ParseWatcherPID(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--watcher-pid", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int pid))
                return pid;
        }

        return null;
    }

    public static string? TryGetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static void ResetState()
    {
        ReleaseSingleInstance();
        WatcherPID = null;
        IsUninstallerMode = false;
        UninstallerInstallDir = null;
        UninstallerScope = InstallScope.LocalAppData;
    }

    private static bool AcquireSingleInstance(
        SingleInstanceIdentity identity,
        int watcherPID,
        int monitoredPID,
        Action<string> log)
    {
        try
        {
            _singleInstanceCoordinator = SingleInstanceCoordinator.AcquireOrTakeover(
                identity,
                watcherPID,
                monitoredPID);
            return true;
        }
        catch (Exception ex)
        {
            log($"TrayAppDotNETProgram.AcquireSingleInstance: {ex}");
            return false;
        }
    }

    private static void ReleaseSingleInstance()
    {
        try { _singleInstanceCoordinator?.Dispose(); }
        catch
        {
            /* ignored */
        }
        finally { _singleInstanceCoordinator = null; }
    }

    private static bool HasArg(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ShouldLaunchWatcherBeforeConfiguring(string[] args) =>
        !Debugger.IsAttached &&
        !NoWatcherRequested() &&
        !HasArg(args, "--monitored") &&
        !HasArg(args, "--install") &&
        !HasArg(args, "--installlocal") &&
        !HasArg(args, "--installsystem") &&
        !HasArg(args, "--admin-action") &&
        !HasArg(args, "--uninstall");

    private static bool NoWatcherRequested() =>
        IsTruthyEnvironmentValue(Environment.GetEnvironmentVariable(NoWatcherEnvironmentVariable)) ||
        IsTruthyEnvironmentValue(Environment.GetEnvironmentVariable(LegacyBrightnessNoWatcherEnvironmentVariable));

    private static bool IsTruthyEnvironmentValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        return !value.Equals("0", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("false", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("no", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("off", StringComparison.OrdinalIgnoreCase);
    }

    private static void NoopLog(string _) { }

    private static void NoopFlush() { }

    private static int RunOnStaThreadIfNeeded(Func<int> run)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return run();

        int exitCode = 0;
        Exception? exception = null;
        Thread staThread = new(() =>
        {
            try { exitCode = run(); }
            catch (Exception ex) { exception = ex; }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();

        if (exception != null) ExceptionDispatchInfo.Capture(exception).Throw();
        return exitCode;
    }

    private static int RunAdminAction(
        string verb,
        string[] args,
        TrayAppDotNETProgramOptions options,
        Action<string> log)
    {
        switch (verb.ToLowerInvariant())
        {
            case "install-system":
            {
                int index = Array.FindIndex(args, a => a.Equals("--admin-action", StringComparison.OrdinalIgnoreCase));
                string sourceExe = index + 2 < args.Length ? args[index + 2] : string.Empty;
                int buildNumber = index + 3 < args.Length && int.TryParse(args[index + 3], out int bn) ? bn : 0;
                TrayAppDotNETProgramInstallResult result = options.RunAdminInstallSystem(sourceExe, buildNumber);
                return result.Success ? 0 : 1;
            }
            case "sync-startmenu":
            {
                InstallScope? removingScope = InstallScopeExtensions.ParseArg(TryGetArgValue(args, "--remove-scope"));
                options.SyncStartMenu(removingScope, true);
                return 0;
            }
            default:
                log($"TrayAppDotNETProgram.RunAdminAction: unknown verb '{verb}'");
                return 1;
        }
    }

    private static int RunUninstall(string[] args, string installDir, TrayAppDotNETProgramOptions options)
    {
        InstallScope scope = InstallScopeExtensions.ParseArg(TryGetArgValue(args, "--scope"))
                             ?? InstallScope.LocalAppData;
        if (scope == InstallScope.WindowsStore) scope = InstallScope.LocalAppData;

        IsUninstallerMode = true;
        UninstallerInstallDir = installDir;
        UninstallerScope = scope;

        return options.RunApplication(args);
    }

    private static int RunInstall(
        string? scope,
        TrayAppDotNETProgramOptions options,
        Action<string> log,
        bool startInstalled)
    {
        if (scope is null) return PrintInstallUsage("Missing scope argument after --install", log);
        string normalizedScope = scope.ToLowerInvariant();
        if (normalizedScope is not ("local" or "system"))
            return PrintInstallUsage($"Unknown scope '{scope}'", log);

        string? terminationError = TerminateRunningApplicationCopies(options.ApplicationName, log);
        if (terminationError != null)
        {
            WriteInstallMessage($"Install failed before copying: {terminationError}", error: true, log);
            return 1;
        }

        switch (normalizedScope)
        {
            case "local":
            {
                TrayAppDotNETProgramInstallResult result = options.InstallToLocalAppData();
                string installExecutable = options.LocalAppDataInstallExecutable();
                return CompleteInstall(
                    result,
                    installExecutable,
                    startInstalled,
                    successMessage: $"Installed to {installExecutable}",
                    failureMessage: $"Local install failed: {result.ErrorMessage}",
                    log: log);
            }
            case "system":
            {
                TrayAppDotNETProgramInstallResult result = options.InstallSystemWide();
                string installExecutable = options.ProgramFilesInstallExecutable();
                string failureMessage = result.UserCancelled
                    ? "System install cancelled (UAC prompt declined)"
                    : $"System install failed: {result.ErrorMessage}";
                return CompleteInstall(
                    result,
                    installExecutable,
                    startInstalled,
                    successMessage: $"Installed to {installExecutable}",
                    failureMessage: failureMessage,
                    log: log);
            }
            default:
                throw new UnreachableException();
        }
    }

    private static string? TerminateRunningApplicationCopies(string applicationName, Action<string> log)
    {
        string processName = Path.GetFileNameWithoutExtension(applicationName);
        if (string.IsNullOrWhiteSpace(processName)) return "Cannot determine application process name";

        int currentPid = Environment.ProcessId;
        List<Process> processes;
        try
        {
            processes = [.. Process.GetProcessesByName(processName).Where(process => process.Id != currentPid)];
        }
        catch (Exception ex)
        {
            log($"TrayAppDotNETProgram.TerminateRunningApplicationCopies: enumerate failed: {ex}");
            return $"Could not enumerate running {applicationName} processes: {ex.Message}";
        }

        if (processes.Count == 0) return null;

        List<string> failures = [];
        foreach (Process process in processes)
        {
            int pid = SafeProcessId(process);
            try
            {
                if (process.HasExited) continue;

                log($"TrayAppDotNETProgram.TerminateRunningApplicationCopies: terminating {applicationName} PID {pid}");
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                log($"TrayAppDotNETProgram.TerminateRunningApplicationCopies: kill PID {pid} failed: {ex}");
                failures.Add($"PID {pid}: {ex.Message}");
            }
        }

        foreach (Process process in processes)
        {
            int pid = SafeProcessId(process);
            try
            {
                if (!process.HasExited && !process.WaitForExit(TerminateRunningCopiesTimeoutMs))
                    failures.Add($"PID {pid}: did not exit within {TerminateRunningCopiesTimeoutMs} ms");
            }
            catch (Exception ex)
            {
                log($"TrayAppDotNETProgram.TerminateRunningApplicationCopies: wait PID {pid} failed: {ex}");
                failures.Add($"PID {pid}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        return failures.Count == 0
            ? null
            : $"Could not terminate all running {applicationName} processes: {string.Join("; ", failures)}";
    }

    private static int SafeProcessId(Process process)
    {
        try { return process.Id; }
        catch { return 0; }
    }

    private static int CompleteInstall(
        TrayAppDotNETProgramInstallResult result,
        string installExecutable,
        bool startInstalled,
        string successMessage,
        string failureMessage,
        Action<string> log)
    {
        if (!result.Success)
        {
            WriteInstallMessage(failureMessage, error: true, log);
            return 1;
        }

        if (!startInstalled)
        {
            WriteInstallMessage(successMessage, error: false, log);
            return 0;
        }

        string? launchError = StartInstalledInstance(installExecutable, log);
        if (launchError == null)
        {
            WriteInstallMessage($"{successMessage}; started installed instance", error: false, log);
            return 0;
        }

        WriteInstallMessage($"{successMessage}; failed to start installed instance: {launchError}", error: true, log);
        return 1;
    }

    private static string? StartInstalledInstance(string installExecutable, Action<string> log)
    {
        try
        {
            if (!File.Exists(installExecutable))
                return $"Installed executable not found: {installExecutable}";

            ProcessStartInfo startInfo = new()
            {
                FileName = installExecutable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            string? workingDirectory = Path.GetDirectoryName(installExecutable);
            if (!string.IsNullOrWhiteSpace(workingDirectory))
                startInfo.WorkingDirectory = workingDirectory;

            using Process? process = Process.Start(startInfo);
            return process == null ? "Process.Start returned null" : null;
        }
        catch (Exception ex)
        {
            log($"TrayAppDotNETProgram.StartInstalledInstance: {ex}");
            return ex.Message;
        }
    }

    private static int PrintInstallUsage(string? reason, Action<string> log)
    {
        string usage =
            "Usage:" + Environment.NewLine +
            "  --install <system|local>" + Environment.NewLine +
            "  --installsystem" + Environment.NewLine +
            "  --installlocal" + Environment.NewLine +
            "Scopes:" + Environment.NewLine +
            "  system  Install to %ProgramFiles%\\TrayAppDotNET (triggers UAC)" + Environment.NewLine +
            "  local   Install to %LOCALAPPDATA%\\TrayAppDotNET (no UAC)";
        string body = reason is null ? usage : $"{reason}{Environment.NewLine}{Environment.NewLine}{usage}";
        WriteInstallMessage(body, error: true, log);
        return 2;
    }

    private static void WriteInstallMessage(string text, bool error, Action<string> log)
    {
        log($"TrayAppDotNETProgram.RunInstall: {text}");
        try
        {
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
                (error ? Console.Error : Console.Out).WriteLine(text);
            }
        }
        catch
        {
            // best effort; the file log already has the message.
        }
    }

    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);
}
