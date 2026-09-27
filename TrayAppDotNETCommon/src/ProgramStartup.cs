using System.Diagnostics;
using System.Runtime.ExceptionServices;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.Utils;

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
    Func<string, int, TrayAppDotNETInstallOptions?, IProgress<TrayAppDotNETInstallProgress>?, TrayAppDotNETProgramInstallResult> RunAdminInstallSystem,
    Action<InstallScope?, bool> SyncStartMenu,
    Func<InstallScope, bool, IProgress<TrayAppDotNETInstallProgress>?, TrayAppDotNETProgramInstallResult> PrepareUninstall,
    Func<InstallScope, bool, IProgress<TrayAppDotNETInstallProgress>?, TrayAppDotNETUninstallRun> RunHeadlessUninstall,
    Func<TrayAppDotNETInstallOptions?, IProgress<TrayAppDotNETInstallProgress>?, TrayAppDotNETProgramInstallResult> InstallToLocalAppData,
    Func<TrayAppDotNETInstallOptions?, IProgress<TrayAppDotNETInstallProgress>?, TrayAppDotNETProgramInstallResult> InstallSystemWide,
    Func<string> LocalAppDataInstallExecutable,
    Func<string> ProgramFilesInstallExecutable,
    Action<string>? Log = null,
    Action? FlushLog = null);

public static class TrayAppDotNETProgram
{
    /// <summary>Marks a launch from the sign-in startup shortcut, as opposed to a manual start.</summary>
    public const string AutostartArgument = "--autostart";

    private const string NoWatcherEnvironmentVariable = "TrayAppDotNET_NO_WATCHER";
    private const int ProgressPipeConnectTimeoutMs = 5000;
    private const int ProgressCompletionTimeoutMs = 5000;

    private static SingleInstanceCoordinator? _singleInstanceCoordinator;
    private static ApplicationInstanceCoordinator? _applicationInstanceCoordinator;
    private static TrayAppDotNETProgramOptions? _installerProgramOptions;

    public static int? WatcherPID { get; private set; }

    /// <summary>Gets whether this process was started, directly or through the watcher, by the startup shortcut.</summary>
    public static bool IsStartupLaunch { get; private set; }

    public static bool IsUninstallerMode { get; private set; }

    public static bool IsInstallerMode { get; private set; }

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
        bool isStartupLaunch = HasArg(args, AutostartArgument);

        if (HasArg(args, flag: "--watcher"))
        {
            CrashHandler.Configure(new CrashHandlerOptions(
                applicationName,
                new SingleInstanceIdentity(applicationName, appGuid),
                NoopLog,
                NoopFlush));
            return CrashHandler.RunWatcher(isStartupLaunch);
        }

        if (ShouldLaunchWatcherBeforeConfiguring(args))
        {
            CrashHandler.Configure(new CrashHandlerOptions(
                applicationName,
                new SingleInstanceIdentity(applicationName, appGuid),
                NoopLog,
                NoopFlush));
            return CrashHandler.LaunchWatcherDetached(isStartupLaunch) ? 0 : 1;
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
            ReleaseApplicationInstance();
            ReleaseSingleInstance();
            flush();
        };

        if (UpdateInstaller.TryRun(args, options, out int updateInstallerExitCode))
            return updateInstallerExitCode;

        CrashHandler.Configure(new CrashHandlerOptions(
            options.ApplicationName,
            singleInstanceIdentity,
            log,
            flush));

        if (HasArg(args, TrayAppDotNETInstallOptions.SystemInstallArgument))
            return RunOnStaThreadIfNeeded(() => RunElevatedSystemInstall(args, options, log));

        if (HasArg(args, TrayAppDotNETInstallOptions.SyncStartMenuArgument))
            return RunOnStaThreadIfNeeded(() => RunStartMenuSync(args, options));

        if (HasArg(args, TrayAppDotNETInstallOptions.PrepareUninstallArgument))
            return RunOnStaThreadIfNeeded(() => RunUninstallPreparation(args, options, log));

        if (HasArg(args, flag: "--installer") || HasArg(args, flag: "--install-gui"))
            return RunInstaller(args, options);

        if (HasArg(args, flag: "--uninstall-headless"))
            return RunHeadlessUninstall(args, options, log);

        TrayAppDotNETInstallOptions installOptions = ParseInstallOptions(args, useDefaults: true)!;
        if (HasArg(args, flag: "--installlocal"))
        {
            return RunOnStaThreadIfNeeded(() =>
                RunInstall(scope: "local", options, log, startInstalled: true, installOptions, TrayAppDotNETConsoleProgress.Instance));
        }

        if (HasArg(args, flag: "--installsystem"))
        {
            return RunOnStaThreadIfNeeded(() =>
                RunInstall(scope: "system", options, log, startInstalled: true, installOptions, TrayAppDotNETConsoleProgress.Instance));
        }

        if (HasArg(args, flag: "--install-headless"))
        {
            return RunOnStaThreadIfNeeded(() =>
                RunInstall(
                    TryGetArgValue(args, flag: "--install-headless"),
                    options,
                    log,
                    startInstalled: false,
                    installOptions,
                    TrayAppDotNETConsoleProgress.Instance));
        }

        if (HasArg(args, flag: "--install"))
        {
            return RunOnStaThreadIfNeeded(() =>
                RunInstall(
                    TryGetArgValue(args, flag: "--install"),
                    options,
                    log,
                    startInstalled: false,
                    installOptions,
                    TrayAppDotNETConsoleProgress.Instance));
        }

        string? installDir = TryGetArgValue(args, flag: "--uninstall")
                             ?? TryGetArgValue(args, flag: "--uninstall-gui");
        if (installDir != null)
            return RunUninstall(args, installDir, options);

        bool isWatcher = HasArg(args, flag: "--watcher");
        bool isMonitored = HasArg(args, flag: "--monitored");
        IsStartupLaunch = HasArg(args, AutostartArgument);

        if (isWatcher) return CrashHandler.RunWatcher(IsStartupLaunch);

        if (!isMonitored && !Debugger.IsAttached && !NoWatcherRequested())
            return !CrashHandler.LaunchWatcherDetached(IsStartupLaunch) ? 1 : 0;

        WatcherPID = ParseWatcherPID(args);
        bool shouldOwnSingleInstance =
            !isMonitored ||
            WatcherPID is null;

        if (shouldOwnSingleInstance &&
            !AcquireSingleInstance(singleInstanceIdentity, WatcherPID ?? 0, Environment.ProcessId, log))
            return 1;

        if (!AcquireApplicationInstance(singleInstanceIdentity, log))
        {
            ReleaseSingleInstance();
            return 1;
        }

        try
        {
            return options.RunApplication(args);
        }
        finally
        {
            ReleaseApplicationInstance();
            ReleaseSingleInstance();
        }
    }

    public static int? ParseWatcherPID(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(value: "--watcher-pid", StringComparison.OrdinalIgnoreCase) &&
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
        ReleaseApplicationInstance();
        ReleaseSingleInstance();
        WatcherPID = null;
        IsStartupLaunch = false;
        IsInstallerMode = false;
        IsUninstallerMode = false;
        _installerProgramOptions = null;
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
                monitoredPID,
                log);
            return true;
        }
        catch (Exception ex)
        {
            log($"TrayAppDotNETProgram.AcquireSingleInstance: {ex}");
            return false;
        }
    }

    private static bool AcquireApplicationInstance(SingleInstanceIdentity identity, Action<string> log)
    {
        try
        {
            _applicationInstanceCoordinator = ApplicationInstanceCoordinator.TryAcquire(
                identity,
                TimeConstants.ApplicationInstanceMutexAcquireTimeoutMs);
            if (_applicationInstanceCoordinator != null) return true;

            log(
                "TrayAppDotNETProgram.AcquireApplicationInstance: timed out waiting for the previous "
                + "application process to exit.");
            return false;
        }
        catch (Exception exception)
        {
            log($"TrayAppDotNETProgram.AcquireApplicationInstance: {exception}");
            return false;
        }
    }

    private static void ReleaseApplicationInstance()
    {
        try
        {
            _applicationInstanceCoordinator?.Dispose();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"TrayAppDotNETProgram.ReleaseApplicationInstance: {exception.Message}");
        }
        finally
        {
            _applicationInstanceCoordinator = null;
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

    private static TrayAppDotNETInstallOptions? ParseInstallOptions(string[] args, bool useDefaults)
    {
        string? desktopValue = TryGetArgValue(args, TrayAppDotNETInstallOptions.DesktopShortcutArgument);
        string? startMenuValue = TryGetArgValue(args, TrayAppDotNETInstallOptions.StartMenuShortcutArgument);
        if (desktopValue == null && startMenuValue == null)
            return useDefaults ? new TrayAppDotNETInstallOptions() : null;

        bool createDesktopShortcut = desktopValue == null
            ? false
            : ParseBooleanArgument(TrayAppDotNETInstallOptions.DesktopShortcutArgument, desktopValue);
        bool createStartMenuShortcut = startMenuValue == null
            ? true
            : ParseBooleanArgument(TrayAppDotNETInstallOptions.StartMenuShortcutArgument, startMenuValue);
        return new TrayAppDotNETInstallOptions(createDesktopShortcut, createStartMenuShortcut);
    }

    private static bool ParseBooleanArgument(string name, string value)
    {
        if (bool.TryParse(value, out bool parsed)) return parsed;
        if (value == "1") return true;
        if (value == "0") return false;
        throw new ArgumentException($"{name} must be true or false.");
    }

    private static bool ShouldLaunchWatcherBeforeConfiguring(string[] args) =>
        !Debugger.IsAttached &&
        !NoWatcherRequested() &&
        !HasArg(args, flag: "--monitored") &&
        !HasArg(args, flag: "--install") &&
        !HasArg(args, flag: "--installer") &&
        !HasArg(args, flag: "--install-gui") &&
        !HasArg(args, flag: "--install-headless") &&
        !HasArg(args, flag: "--installlocal") &&
        !HasArg(args, flag: "--installsystem") &&
        !HasArg(args, TrayAppDotNETInstallOptions.SystemInstallArgument) &&
        !HasArg(args, TrayAppDotNETInstallOptions.SyncStartMenuArgument) &&
        !HasArg(args, TrayAppDotNETInstallOptions.PrepareUninstallArgument) &&
        !HasArg(args, flag: "--uninstall") &&
        !HasArg(args, flag: "--uninstall-gui") &&
        !HasArg(args, flag: "--uninstall-headless") &&
        !UpdateInstaller.IsUpdateMode(args);

    private static bool NoWatcherRequested() =>
        IsTruthyEnvironmentValue(Environment.GetEnvironmentVariable(NoWatcherEnvironmentVariable));

    private static bool IsTruthyEnvironmentValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        return !value.Equals(value: "0", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals(value: "false", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals(value: "no", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals(value: "off", StringComparison.OrdinalIgnoreCase);
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

    private static int RunElevatedSystemInstall(
        string[] args,
        TrayAppDotNETProgramOptions options,
        Action<string> log)
    {
        string? sourceExecutable = TryGetArgValue(
            args,
            TrayAppDotNETInstallOptions.SourceExecutableArgument);
        string? buildNumberValue = TryGetArgValue(
            args,
            TrayAppDotNETInstallOptions.BuildNumberArgument);
        if (string.IsNullOrWhiteSpace(sourceExecutable)
            || !int.TryParse(buildNumberValue, out int buildNumber)
            || buildNumber < 0)
        {
            log(
                "TrayAppDotNETProgram.RunElevatedSystemInstall: requires "
                + $"{TrayAppDotNETInstallOptions.SourceExecutableArgument} <executable> and "
                + $"{TrayAppDotNETInstallOptions.BuildNumberArgument} <non-negative integer>");
            return 2;
        }

        TrayAppDotNETInstallOptions? installOptions = ParseInstallOptions(args, useDefaults: false);
        using TrayAppDotNETProgressPipeClient? progressClient = ConnectProgressPipe(args, log);
        TrayAppDotNETProgramInstallResult result = options.RunAdminInstallSystem(
            sourceExecutable,
            buildNumber,
            installOptions,
            progressClient);
        if (!result.Success)
            log($"TrayAppDotNETProgram.RunElevatedSystemInstall: install failed: {result.ErrorMessage}");
        return result.Success ? 0 : 1;
    }

    private static int RunStartMenuSync(string[] args, TrayAppDotNETProgramOptions options)
    {
        InstallScope? removingScope = InstallScopeExtensions.ParseArg(TryGetArgValue(args, flag: "--remove-scope"));
        options.SyncStartMenu(removingScope, arg2: true);
        return 0;
    }

    private static int RunUninstallPreparation(
        string[] args,
        TrayAppDotNETProgramOptions options,
        Action<string> log)
    {
        InstallScope? scope = InstallScopeExtensions.ParseArg(TryGetArgValue(args, flag: "--scope"));
        if (scope is not (InstallScope.LocalAppData or InstallScope.ProgramFiles))
        {
            log("TrayAppDotNETProgram.RunUninstallPreparation: requires local or system scope");
            return 2;
        }

        bool deleteSettings = ParseDeleteSettingsArgument(args);
        using TrayAppDotNETProgressPipeClient? progressClient = ConnectProgressPipe(args, log);
        TrayAppDotNETProgramInstallResult result = options.PrepareUninstall(
            scope.Value,
            deleteSettings,
            progressClient);
        if (!result.Success)
            log($"TrayAppDotNETProgram.RunUninstallPreparation: failed: {result.ErrorMessage}");
        return result.Success ? 0 : 1;
    }

    private static int RunUninstall(string[] args, string installDir, TrayAppDotNETProgramOptions options)
    {
        InstallScope scope = InstallScopeExtensions.ParseArg(TryGetArgValue(args, flag: "--scope"))
                             ?? InstallScope.LocalAppData;
        if (scope == InstallScope.WindowsStore) scope = InstallScope.LocalAppData;

        IsUninstallerMode = true;
        UninstallerInstallDir = installDir;
        UninstallerScope = scope;

        return options.RunApplication(args);
    }

    private static int RunHeadlessUninstall(
        string[] args,
        TrayAppDotNETProgramOptions options,
        Action<string> log)
    {
        InstallScope? scope = InstallScopeExtensions.ParseArg(
            TryGetArgValue(args, flag: "--uninstall-headless"));
        if (scope is not (InstallScope.LocalAppData or InstallScope.ProgramFiles))
        {
            WriteInstallMessage(
                text: "Usage: --uninstall-headless <local|system> [--delete-settings <true|false>]",
                error: true,
                log);
            return 2;
        }

        bool deleteSettings = ParseDeleteSettingsArgument(args);

        TrayAppDotNETUninstallRun run = options.RunHeadlessUninstall(
            scope.Value,
            deleteSettings,
            TrayAppDotNETConsoleProgress.Instance);
        if (run.UserCancelled)
        {
            WriteInstallMessage(text: "System uninstall cancelled (UAC prompt declined)", error: true, log);
            return 1;
        }

        if (run.Process == null)
        {
            log("TrayAppDotNETProgram.RunHeadlessUninstall: uninstall process did not start.");
            return 1;
        }

        using Process process = run.Process;
        process.WaitForExit();
        WaitForProgressCompletion(run.Completion);
        return process.ExitCode;
    }

    private static int RunInstaller(string[] args, TrayAppDotNETProgramOptions options)
    {
        IsInstallerMode = true;
        _installerProgramOptions = options;
        return options.RunApplication(args);
    }

    /// <summary>Executes the choice returned by the shared installer window.</summary>
    public static int RunInstallerSelection(
        InstallScope scope,
        TrayAppDotNETInstallOptions installOptions)
    {
        TrayAppDotNETProgramOptions options = _installerProgramOptions
                                              ?? throw new InvalidOperationException(
                                                  "No installer GUI is active for this process.");
        Action<string> log = options.Log ?? TADNLog.Log;
        string scopeArgument = scope switch
        {
            InstallScope.LocalAppData => "local",
            InstallScope.ProgramFiles => "system",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, message: "Unsupported install scope.")
        };
        return RunInstall(scopeArgument, options, log, startInstalled: true, installOptions);
    }

    internal static int RunInstall(
        string? scope,
        TrayAppDotNETProgramOptions options,
        Action<string> log,
        bool startInstalled,
        TrayAppDotNETInstallOptions? installOptions = null,
        IProgress<TrayAppDotNETInstallProgress>? progress = null)
    {
        if (scope is null) return PrintInstallUsage(reason: "Missing scope argument after --install", log);
        string normalizedScope = scope.ToLowerInvariant();
        if (normalizedScope is not ("local" or "system"))
            return PrintInstallUsage($"Unknown scope '{scope}'", log);

        TrayAppDotNETProgramInstallResult result;
        string installExecutable;
        string failureMessage;
        switch (normalizedScope)
        {
            case "local":
                result = options.InstallToLocalAppData(installOptions, progress);
                installExecutable = options.LocalAppDataInstallExecutable();
                failureMessage = $"Local install failed: {result.ErrorMessage}";
                break;
            case "system":
                result = options.InstallSystemWide(installOptions, progress);
                installExecutable = options.ProgramFilesInstallExecutable();
                failureMessage = result.UserCancelled
                    ? "System install cancelled (UAC prompt declined)"
                    : $"System install failed: {result.ErrorMessage}";
                break;
            default:
                throw new UnreachableException();
        }

        return CompleteInstall(
            result,
            installExecutable,
            startInstalled,
            $"Installed to {installExecutable}",
            failureMessage,
            log);
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
                WindowStyle = ProcessWindowStyle.Hidden
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
            "  --installer" + Environment.NewLine +
            "  --install-headless <system|local>" + Environment.NewLine +
            "  --install <system|local>" + Environment.NewLine +
            "  --installsystem" + Environment.NewLine +
            "  --installlocal" + Environment.NewLine +
            "Scopes:" + Environment.NewLine +
            "  system  Install to %ProgramFiles%\\TrayAppDotNET (triggers UAC)" + Environment.NewLine +
            "  local   Install to %LOCALAPPDATA%\\TrayAppDotNET\\apps (no UAC)" + Environment.NewLine +
            "Options:" + Environment.NewLine +
            "  --desktop-shortcut <true|false>" + Environment.NewLine +
            "  --start-menu-shortcut <true|false>";
        string body = reason is null ? usage : $"{reason}{Environment.NewLine}{Environment.NewLine}{usage}";
        WriteInstallMessage(body, error: true, log);
        return 2;
    }

    private static void WriteInstallMessage(string text, bool error, Action<string> log)
    {
        log($"TrayAppDotNETProgram.RunInstall: {text}");
        // Best effort; the file log already has the message
        _ = TrayAppDotNETConsoleOutput.TryWriteLine(text, error);
    }

    /// <summary>Connects to the launcher's progress pipe when a helper process was given one.</summary>
    private static TrayAppDotNETProgressPipeClient? ConnectProgressPipe(string[] args, Action<string> log)
    {
        string? pipeName = TryGetArgValue(args, TrayAppDotNETInstallOptions.ProgressPipeArgument);
        if (string.IsNullOrWhiteSpace(pipeName)) return null;

        return TrayAppDotNETProgressPipeClient.TryConnect(pipeName, ProgressPipeConnectTimeoutMs, log);
    }

    private static bool ParseDeleteSettingsArgument(string[] args)
    {
        string? deleteSettingsValue = TryGetArgValue(args, TrayAppDotNETInstallOptions.DeleteSettingsArgument);
        if (HasArg(args, TrayAppDotNETInstallOptions.DeleteSettingsArgument) && deleteSettingsValue == null)
        {
            throw new ArgumentException(
                $"{TrayAppDotNETInstallOptions.DeleteSettingsArgument} must be followed by true or false.");
        }

        return deleteSettingsValue != null
               && ParseBooleanArgument(TrayAppDotNETInstallOptions.DeleteSettingsArgument, deleteSettingsValue);
    }

    /// <summary>Lets the final uninstall progress line reach standard output before the process exits.</summary>
    private static void WaitForProgressCompletion(Task completion)
    {
        try
        {
            _ = completion.Wait(ProgressCompletionTimeoutMs);
        }
        catch (AggregateException exception)
        {
            TADNLog.Log($"TrayAppDotNETProgram.WaitForProgressCompletion: {exception.InnerException?.Message}");
        }
    }
}
