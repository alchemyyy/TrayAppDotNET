using System.Buffers;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using TrayAppDotNETCommon.Contracts;
using TrayAppDotNETCommon.Interop;
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
    Func<TrayAppDotNETInstallationService> Installation,
    Action<string>? Log = null,
    Action? FlushLog = null);

public static class TrayAppDotNETProgram
{
    /// <summary>Marks a launch from the sign-in startup shortcut, as opposed to a manual start.</summary>
    public const string AutostartArgument = "--autostart";

    /// <summary>
    /// Followed by a folder path, replaces the per-app folder under %LOCALAPPDATA%\TrayAppDotNET that holds
    /// settings.xml, the theme, logs and the other per-app data files.
    /// </summary>
    public const string SettingsDirectoryArgument = "--settings-dir";

    private const int UsageErrorExitCode = 2;

    // The launch arguments the watcher passes on to every monitored process it starts, in this order
    private static readonly string[] ForwardedLaunchArguments = [AutostartArgument, TrayAppDotNETInstallContract.HiddenArgument];

    // Characters Windows rejects in a path, wildcards and control characters included
    private static readonly SearchValues<char> InvalidSettingsDirectoryCharacters = SearchValues.Create(
        "\"<>|*?\0\u0001\u0002\u0003\u0004\u0005\u0006\u0007\b\t\n\u000B\f\r\u000E\u000F"
        + "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F");

    private static SingleInstanceCoordinator? _singleInstanceCoordinator;
    private static ApplicationInstanceCoordinator? _applicationInstanceCoordinator;
    private static TrayAppDotNETProgramOptions? _installerProgramOptions;

    public static int? WatcherPID { get; private set; }

    /// <summary>Gets whether this process was started, directly or through the watcher, by the startup shortcut.</summary>
    public static bool IsStartupLaunch { get; private set; }

    /// <summary>
    /// Gets whether this process was started, directly or through the watcher, with
    /// <see cref="TrayAppDotNETInstallContract.HiddenArgument"/>.
    /// </summary>
    public static bool IsHiddenLaunch { get; private set; }

    public static bool IsUninstallerMode { get; private set; }

    public static bool IsInstallerMode { get; private set; }

    public static string? UninstallerInstallDir { get; private set; }

    public static InstallScope UninstallerScope { get; private set; } = InstallScope.LocalAppData;

    /// <summary>Gets the absolute folder passed with <see cref="SettingsDirectoryArgument"/>, or null without one.</summary>
    public static string? SettingsDirectoryOverride { get; private set; }

    /// <summary>
    /// Applies a settings folder override as <see cref="SettingsDirectoryArgument"/> does, for a process that never
    /// goes through <see cref="Run"/>: test assemblies use it so settings, themes, logs and data stores stay out of
    /// the user's folders.
    /// </summary>
    internal static void UseSettingsDirectory(string directory) =>
        SettingsDirectoryOverride = Path.GetFullPath(directory);

    public static string LocalAppDataRoot(string sharedRootFolderName) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            sharedRootFolderName);

    /// <summary>Returns the per-app data folder, which <see cref="SettingsDirectoryArgument"/> can replace.</summary>
    public static string AppLocalAppDataDirectory(string applicationName, string sharedRootFolderName) =>
        SettingsDirectoryOverride ?? Path.Combine(LocalAppDataRoot(sharedRootFolderName), applicationName);

    /// <summary>
    /// Builds the arguments that carry the settings folder override into a relaunch of this app, or null when
    /// there is no override.
    /// </summary>
    public static string? SettingsDirectoryCommandLine() =>
        SettingsDirectoryOverride == null
            ? null
            : SettingsDirectoryArgument + " " + QuoteCommandLineArgument(SettingsDirectoryOverride);

    /// <summary>
    /// Returns the arguments a helper process this app starts (outside <see cref="Run"/>) must receive, so it uses
    /// the same settings folder: the settings folder override, when one is active.
    /// </summary>
    public static IReadOnlyList<string> HelperProcessArguments() => HelperProcessArguments(SettingsDirectoryOverride);

    internal static IReadOnlyList<string> HelperProcessArguments(string? settingsDirectory) =>
        settingsDirectory == null ? [] : [SettingsDirectoryArgument, settingsDirectory];

    /// <summary>
    /// Prepares the start of a helper process: appends <see cref="HelperProcessArguments()"/> and keeps the helper
    /// from starting a crash watcher should its own mode not be recognized.
    /// </summary>
    public static void ConfigureHelperProcess(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        foreach (string argument in HelperProcessArguments()) startInfo.ArgumentList.Add(argument);
        startInfo.Environment[TrayAppDotNETConstants.NoWatcherEnvironmentVariable] = "1";
    }

    /// <summary>
    /// Initializes a helper process before it does anything else: applies the settings folder override its parent
    /// passed, then starts logging to <c>&lt;helperLogName&gt;.log</c> in the app's log folder. Returns false, after
    /// reporting, when the override is unusable; the helper should then exit with a usage error.
    /// </summary>
    public static bool InitializeHelperProcess(string[] args, string applicationName, string helperLogName)
    {
        ArgumentNullException.ThrowIfNull(args);

        string sharedRootFolderName = TrayAppDotNETConstants.SharedRootFolderName;
        if (!TryResolveSettingsDirectory(args, out string? settingsDirectory, out string? errorMessage))
        {
            TADNLog.InitializeHelper(
                Path.Combine(LocalAppDataRoot(sharedRootFolderName), applicationName),
                helperLogName);
            TADNLog.Log($"TrayAppDotNETProgram.InitializeHelperProcess: {errorMessage}");
            TADNLog.Flush();
            return false;
        }

        SettingsDirectoryOverride = settingsDirectory;
        TADNLog.InitializeHelper(AppLocalAppDataDirectory(applicationName, sharedRootFolderName), helperLogName);
        // Helpers end through Environment.Exit when their parent goes away, between periodic flushes
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => TADNLog.Shutdown();
        return true;
    }

    public static int Run(
        string[] args,
        string applicationName,
        string appGuid,
        Func<TrayAppDotNETProgramOptions> createOptions)
    {
        ResetState();
        if (!ApplySettingsDirectoryArgument(args, applicationName)) return UsageErrorExitCode;
        IReadOnlyList<string> launchArguments = SelectForwardedLaunchArguments(args, SettingsDirectoryOverride);

        if (HasArg(args, flag: "--watcher"))
        {
            CrashHandler.Configure(new CrashHandlerOptions(
                applicationName,
                new SingleInstanceIdentity(applicationName, appGuid),
                NoopLog,
                NoopFlush));
            return CrashHandler.RunWatcher(launchArguments);
        }

        if (ShouldLaunchWatcherBeforeConfiguring(args))
        {
            CrashHandler.Configure(new CrashHandlerOptions(
                applicationName,
                new SingleInstanceIdentity(applicationName, appGuid),
                NoopLog,
                NoopFlush));
            return CrashHandler.LaunchWatcherDetached(launchArguments) ? 0 : 1;
        }

        return RunConfigured(args, createOptions());
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

        if (IsHeadlessInstallCommand(args)) CrashHandler.WireCrashHandlers();

        if (HasArg(args, TrayAppDotNETInstallContract.SystemInstallArgument))
            return RunOnStaThreadIfNeeded(() => RunElevatedSystemInstall(args, options, log));

        if (HasArg(args, TrayAppDotNETInstallContract.SyncStartMenuArgument))
            return RunOnStaThreadIfNeeded(() => RunStartMenuSync(args, options, log));

        if (HasArg(args, TrayAppDotNETInstallContract.PrepareUninstallArgument))
            return RunOnStaThreadIfNeeded(() => RunUninstallPreparation(args, options, log));

        if (HasArg(args, flag: "--installer") || HasArg(args, flag: "--install-gui"))
            return RunInstaller(args, options);

        if (HasArg(args, flag: "--uninstall-headless"))
            return RunHeadlessUninstall(args, options, log);

        string? installScope = HasArg(args, flag: "--installlocal") ? TrayAppDotNETInstallContract.LocalScopeArgument
            : HasArg(args, flag: "--installsystem") ? TrayAppDotNETInstallContract.SystemScopeArgument
            : HasArg(args, TrayAppDotNETInstallContract.HeadlessInstallArgument) ? TryGetFlagValue(args, TrayAppDotNETInstallContract.HeadlessInstallArgument)
            : HasArg(args, flag: "--install") ? TryGetFlagValue(args, flag: "--install")
            : null;
        bool isInstall = HasArg(args, flag: "--installlocal") || HasArg(args, flag: "--installsystem")
                         || HasArg(args, TrayAppDotNETInstallContract.HeadlessInstallArgument)
                         || HasArg(args, flag: "--install");
        if (isInstall)
        {
            if (!TryParseInstallOptions(args, out TrayAppDotNETInstallOptions installOptions, out string? error))
                return ReportUsageError(error!, log);

            // --installlocal and --installsystem are the install.bat entry points and start the installed copy
            bool startInstalled = HasArg(args, flag: "--installlocal") || HasArg(args, flag: "--installsystem");
            return RunOnStaThreadIfNeeded(() =>
                RunInstall(installScope, options, log, startInstalled, installOptions, TrayAppDotNETConsoleProgress.Instance));
        }

        if (HasArg(args, flag: "--uninstall") || HasArg(args, flag: "--uninstall-gui"))
        {
            string? installDir = TryGetFlagValue(args, flag: "--uninstall")
                                 ?? TryGetFlagValue(args, flag: "--uninstall-gui");
            return RunUninstall(args, installDir, options);
        }

        bool isMonitored = HasArg(args, flag: "--monitored");
        IsStartupLaunch = HasArg(args, AutostartArgument);
        IsHiddenLaunch = HasArg(args, TrayAppDotNETInstallContract.HiddenArgument);
        IReadOnlyList<string> launchArguments = SelectForwardedLaunchArguments(args, SettingsDirectoryOverride);

        if (!isMonitored && !Debugger.IsAttached && !NoWatcherRequested())
            return !CrashHandler.LaunchWatcherDetached(launchArguments) ? 1 : 0;

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

    /// <summary>
    /// Picks the launch arguments the watcher must pass on out of a command line. The watcher rebuilds the monitored
    /// process's command line from scratch, so anything not listed here never reaches the app.
    /// <paramref name="settingsDirectory"/> is forwarded already resolved, because the monitored process starts in
    /// the executable's folder rather than the original working directory.
    /// </summary>
    internal static IReadOnlyList<string> SelectForwardedLaunchArguments(string[] args, string? settingsDirectory)
    {
        List<string> forwarded = [];
        foreach (string argument in ForwardedLaunchArguments)
        {
            if (HasArg(args, argument)) forwarded.Add(argument);
        }

        if (settingsDirectory != null)
        {
            forwarded.Add(SettingsDirectoryArgument);
            forwarded.Add(settingsDirectory);
        }

        return forwarded;
    }

    /// <summary>
    /// Resolves the <see cref="SettingsDirectoryArgument"/> value to an absolute folder with no trailing separator.
    /// Returns false with a message when the argument is present without a usable path.
    /// </summary>
    internal static bool TryResolveSettingsDirectory(
        string[] args,
        out string? settingsDirectory,
        out string? errorMessage)
    {
        settingsDirectory = null;
        errorMessage = null;
        if (!HasArg(args, SettingsDirectoryArgument)) return true;

        // NOTE: "C:\dir\" parses as C:\dir" under the Windows quoting rules, so stray quotes are stripped
        string value = (TryGetArgValue(args, SettingsDirectoryArgument) ?? string.Empty).Trim().Trim('"');
        // A flag in the value position means the path itself was left out
        if (value.Length == 0 || value.StartsWith(value: "--", StringComparison.Ordinal))
        {
            errorMessage = $"{SettingsDirectoryArgument} must be followed by a folder path.";
            return false;
        }

        string expandedValue = Environment.ExpandEnvironmentVariables(value);
        // A quote inside the value usually means "C:\dir\" swallowed the arguments after it
        if (expandedValue.AsSpan().IndexOfAny(InvalidSettingsDirectoryCharacters) >= 0)
        {
            errorMessage = $"{SettingsDirectoryArgument} path '{value}' contains a character Windows does not allow "
                           + "in a folder path. A path that ends in a backslash must not be quoted.";
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(expandedValue);
            settingsDirectory = Path.TrimEndingDirectorySeparator(fullPath);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            errorMessage = $"{SettingsDirectoryArgument} path '{value}' is invalid: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Quotes one argument so CommandLineToArgvW and the C runtime parse it back unchanged.
    /// </summary>
    internal static string QuoteCommandLineArgument(string argument)
    {
        if (argument.Length > 0 && argument.AsSpan().IndexOfAny(" \t\"") < 0) return argument;

        StringBuilder builder = new(argument.Length + 2);
        builder.Append('"');
        int backslashCount = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            // Backslashes are literal unless a quote follows them, then each one needs its own escape
            int escapedBackslashCount = character == '"' ? backslashCount * 2 + 1 : backslashCount;
            builder.Append('\\', escapedBackslashCount);
            builder.Append(character);
            backslashCount = 0;
        }

        // The closing quote would escape trailing backslashes, so they are doubled
        builder.Append('\\', backslashCount * 2);
        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>Applies <see cref="SettingsDirectoryArgument"/>, reporting an unusable value before startup stops.</summary>
    private static bool ApplySettingsDirectoryArgument(string[] args, string applicationName)
    {
        if (TryResolveSettingsDirectory(args, out string? settingsDirectory, out string? errorMessage))
        {
            SettingsDirectoryOverride = settingsDirectory;
            return true;
        }

        TADNLog.Log($"TrayAppDotNETProgram.ApplySettingsDirectoryArgument: {errorMessage}");
        // The process exits right after this, before the periodic flush
        TADNLog.Flush();
        // A shortcut launch has no console to print to
        if (!TrayAppDotNETConsoleOutput.TryWriteLine(errorMessage!, error: true))
            _ = User32.MessageBox(IntPtr.Zero, errorMessage!, applicationName, User32.MB_ICONERROR);
        return false;
    }

    private static void ResetState()
    {
        ReleaseApplicationInstance();
        ReleaseSingleInstance();
        WatcherPID = null;
        SettingsDirectoryOverride = null;
        IsStartupLaunch = false;
        IsHiddenLaunch = false;
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

    /// <summary>
    /// Reads the install choices. A flag that is absent stays null (leave as is); a flag without a valid true/false
    /// value is a usage error.
    /// </summary>
    internal static bool TryParseInstallOptions(string[] args, out TrayAppDotNETInstallOptions options, out string? error)
    {
        options = new TrayAppDotNETInstallOptions();
        bool? desktop = null;
        bool? startMenu = null;
        bool? runAsAdministrator = null;
        if (HasArg(args, TrayAppDotNETInstallContract.DesktopShortcutArgument)
            && !TryParseBooleanArgument(args, TrayAppDotNETInstallContract.DesktopShortcutArgument, out desktop, out error))
            return false;
        if (HasArg(args, TrayAppDotNETInstallContract.StartMenuShortcutArgument)
            && !TryParseBooleanArgument(args, TrayAppDotNETInstallContract.StartMenuShortcutArgument, out startMenu, out error))
            return false;
        if (HasArg(args, TrayAppDotNETInstallContract.RunAsAdministratorArgument)
            && !TryParseBooleanArgument(args, TrayAppDotNETInstallContract.RunAsAdministratorArgument, out runAsAdministrator, out error))
            return false;

        error = null;
        options = new TrayAppDotNETInstallOptions(desktop, startMenu, runAsAdministrator);
        return true;
    }

    private static bool TryParseBooleanArgument(string[] args, string name, out bool? value, out string? error)
    {
        value = null;
        error = null;
        switch (TryGetArgValue(args, name)?.Trim().ToLowerInvariant())
        {
            case "true" or "1":
                value = true;
                return true;
            case "false" or "0":
                value = false;
                return true;
            default:
                error = $"{name} must be followed by true or false.";
                return false;
        }
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
        !HasArg(args, TrayAppDotNETInstallContract.SystemInstallArgument) &&
        !HasArg(args, TrayAppDotNETInstallContract.SyncStartMenuArgument) &&
        !HasArg(args, TrayAppDotNETInstallContract.PrepareUninstallArgument) &&
        !HasArg(args, flag: "--uninstall") &&
        !HasArg(args, flag: "--uninstall-gui") &&
        !HasArg(args, flag: "--uninstall-headless") &&
        !UpdateInstaller.IsUpdateMode(args);

    private static bool NoWatcherRequested() =>
        IsTruthyEnvironmentValue(
            Environment.GetEnvironmentVariable(TrayAppDotNETConstants.NoWatcherEnvironmentVariable));

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

    /// <summary>Runs <paramref name="run"/> on an STA thread: the calling thread when it is one, else a new one.</summary>
    internal static int RunOnStaThreadIfNeeded(Func<int> run)
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
            TrayAppDotNETInstallContract.SourceExecutableArgument);
        string? buildNumberValue = TryGetArgValue(
            args,
            TrayAppDotNETInstallContract.BuildNumberArgument);
        if (string.IsNullOrWhiteSpace(sourceExecutable)
            || !int.TryParse(buildNumberValue, out int buildNumber)
            || buildNumber < 0)
        {
            log(
                "TrayAppDotNETProgram.RunElevatedSystemInstall: requires "
                + $"{TrayAppDotNETInstallContract.SourceExecutableArgument} <executable> and "
                + $"{TrayAppDotNETInstallContract.BuildNumberArgument} <non-negative integer>");
            return 2;
        }

        if (!TryParseInstallOptions(args, out TrayAppDotNETInstallOptions installOptions, out string? error))
        {
            log($"TrayAppDotNETProgram.RunElevatedSystemInstall: {error}");
            return UsageErrorExitCode;
        }

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

    /// <summary>
    /// <c>--sync-start-menu [--remove-scope &lt;scope&gt;]</c>: rewrites the Start Menu entries of every profile.
    /// Other profiles are only writable elevated, so an unelevated run is refused instead of half applied.
    /// </summary>
    private static int RunStartMenuSync(string[] args, TrayAppDotNETProgramOptions options, Action<string> log)
    {
        InstallScope? removingScope = null;
        if (HasArg(args, flag: "--remove-scope"))
        {
            removingScope = InstallScopeExtensions.ParseArg(TryGetFlagValue(args, flag: "--remove-scope"));
            if (removingScope == null)
                return ReportUsageError("--remove-scope must be followed by local, system or store.", log);
        }

        if (!TrayAppDotNETInstallationService.IsElevated(log))
        {
            WriteInstallMessage(
                $"{TrayAppDotNETInstallContract.SyncStartMenuArgument} updates every profile and must run elevated.",
                error: true,
                log);
            return 1;
        }

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

        if (!TryParseDeleteSettingsArgument(args, out bool deleteSettings, out string? error))
        {
            log($"TrayAppDotNETProgram.RunUninstallPreparation: {error}");
            return UsageErrorExitCode;
        }

        using TrayAppDotNETProgressPipeClient? progressClient = ConnectProgressPipe(args, log);
        TrayAppDotNETProgramInstallResult result = options.PrepareUninstall(
            scope.Value,
            deleteSettings,
            progressClient);
        if (!result.Success)
            log($"TrayAppDotNETProgram.RunUninstallPreparation: failed: {result.ErrorMessage}");
        return result.Success ? 0 : 1;
    }

    private static int RunUninstall(string[] args, string? installDir, TrayAppDotNETProgramOptions options)
    {
        InstallScope scope = InstallScopeExtensions.ParseArg(TryGetArgValue(args, flag: "--scope"))
                             ?? InstallScope.LocalAppData;
        if (scope == InstallScope.WindowsStore) scope = InstallScope.LocalAppData;

        IsUninstallerMode = true;
        // Display only; the folder that is removed always comes from the install layout
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

        if (!TryParseDeleteSettingsArgument(args, out bool deleteSettings, out string? error))
            return ReportUsageError(error!, log);

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
            WriteInstallMessage(run.ErrorMessage ?? "The uninstall process did not start.", error: true, log);
            return 1;
        }

        using Process process = run.Process;
        process.WaitForExit();
        // Completion is bounded: it ends once the helper's own exe is gone or the removal wait runs out
        bool removed = run.Completion.GetAwaiter().GetResult();
        if (removed) return 0;
        return process.ExitCode != 0 ? process.ExitCode : 1;
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
            InstallScope.LocalAppData => TrayAppDotNETInstallContract.LocalScopeArgument,
            InstallScope.ProgramFiles => TrayAppDotNETInstallContract.SystemScopeArgument,
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
        InstallScope? parsedScope = InstallScopeExtensions.ParseArg(scope);
        if (parsedScope is not (InstallScope.LocalAppData or InstallScope.ProgramFiles))
            return PrintInstallUsage($"Unknown scope '{scope}'", log);

        TrayAppDotNETProgramInstallResult result;
        string installExecutable;
        string failureMessage;
        if (parsedScope == InstallScope.LocalAppData)
        {
            result = options.InstallToLocalAppData(installOptions, progress);
            installExecutable = options.LocalAppDataInstallExecutable();
            failureMessage = $"Local install failed: {result.ErrorMessage}";
        }
        else
        {
            result = options.InstallSystemWide(installOptions, progress);
            installExecutable = options.ProgramFilesInstallExecutable();
            failureMessage = result.UserCancelled
                ? "System install cancelled (UAC prompt declined)"
                : $"System install failed: {result.ErrorMessage}";
        }

        return CompleteInstall(
            result,
            installExecutable,
            startInstalled,
            TrayAppDotNETInstallContract.InstalledToPrefix + installExecutable,
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

    /// <summary>
    /// Starts the installed copy. An elevated caller (an elevated console, a System install run from an elevated
    /// window) never hands its token to the tray app: the start goes through the desktop shell, with
    /// <see cref="TrayAppDotNETInstallContract.HiddenArgument"/> so the app starts in the notification area.
    /// </summary>
    private static string? StartInstalledInstance(string installExecutable, Action<string> log)
    {
        if (!File.Exists(installExecutable))
            return $"Installed executable not found: {installExecutable}";

        List<string> arguments = [];
        if (TrayAppDotNETInstallationService.IsElevated(log)) arguments.Add(TrayAppDotNETInstallContract.HiddenArgument);
        return TrayAppDotNETAppLauncher.Launch(installExecutable, arguments, log)
            ? null
            : $"Could not start {installExecutable}";
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
        string? pipeName = TryGetArgValue(args, TrayAppDotNETInstallContract.ProgressPipeArgument);
        if (string.IsNullOrWhiteSpace(pipeName)) return null;

        return TrayAppDotNETProgressPipeClient.TryConnect(pipeName, TimeConstants.ProgressPipeConnectTimeoutMs, log);
    }

    /// <summary>
    /// Reads <c>--delete-settings true|false</c>. False without the flag; a flag without a valid value is a usage
    /// error the caller reports with exit code 2.
    /// </summary>
    private static bool TryParseDeleteSettingsArgument(string[] args, out bool deleteSettings, out string? error)
    {
        deleteSettings = false;
        error = null;
        if (!HasArg(args, TrayAppDotNETInstallContract.DeleteSettingsArgument)) return true;

        if (!TryParseBooleanArgument(args, TrayAppDotNETInstallContract.DeleteSettingsArgument, out bool? value, out error))
            return false;

        deleteSettings = value == true;
        return true;
    }

    private static int ReportUsageError(string error, Action<string> log)
    {
        WriteInstallMessage(error, error: true, log);
        return UsageErrorExitCode;
    }

    /// <summary>The value after <paramref name="flag"/>, or null when it is missing or is the next flag.</summary>
    private static string? TryGetFlagValue(string[] args, string flag)
    {
        string? value = TryGetArgValue(args, flag);
        return value == null || value.StartsWith(value: "--", StringComparison.Ordinal) ? null : value;
    }

    /// <summary>
    /// True for the install and uninstall commands that run without a window. They report a crash as exit code 2
    /// instead of a crash dialog, so install.bat and the installer see the failure.
    /// </summary>
    private static bool IsHeadlessInstallCommand(string[] args) =>
        HasArg(args, TrayAppDotNETInstallContract.SystemInstallArgument)
        || HasArg(args, TrayAppDotNETInstallContract.SyncStartMenuArgument)
        || HasArg(args, TrayAppDotNETInstallContract.PrepareUninstallArgument)
        || HasArg(args, flag: "--uninstall-headless")
        || HasArg(args, flag: "--installlocal")
        || HasArg(args, flag: "--installsystem")
        || HasArg(args, TrayAppDotNETInstallContract.HeadlessInstallArgument)
        || HasArg(args, flag: "--install");
}
