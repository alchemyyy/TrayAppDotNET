using TrayAppDotNETCommon.Models;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// App-specific uninstall cleanup that runs inside uninstall preparation (<c>--uninstall-prepare</c>), the step
/// that stops running instances and removes shell integration before the files are deleted.
/// </summary>
public sealed class TrayAppDotNETUninstallHook
{
    /// <summary>
    /// Whether the cleanup needs administrator rights for a scope. Preparation is elevated only for a Program Files
    /// uninstall (a per-user install's executable is user-writable, so it is never run elevated); when this returns
    /// true and preparation is not elevated, the hook is skipped and the skip is logged.
    /// </summary>
    public Func<InstallScope, bool> RequiresElevation { get; init; } = static _ => false;

    /// <summary>
    /// Returns why uninstalling a scope must not start, or null. Runs in the process that starts the uninstall (the
    /// uninstaller window or <c>--uninstall-headless</c>) before the helper starts, so the reason is shown in the
    /// window or printed and nothing is removed. Use it for cleanup the helper is known to be unable to do.
    /// </summary>
    public Func<InstallScope, string?>? CheckUninstall { get; init; }

    /// <summary>
    /// Runs before preparation stops the running instances; gets the scope and whether settings are deleted. Read
    /// anything needed from the settings folder here, since preparation may delete it. Returns null to go on, or
    /// why the uninstall must stop: preparation then removes nothing, reports the reason as the failed stage (the
    /// uninstaller window and <c>--uninstall-headless</c> show it) and exits with an error.
    /// </summary>
    public Func<InstallScope, bool, string?>? BeforePrepare { get; init; }

    /// <summary>Runs after preparation, whether it succeeded or not, once every instance is gone; not after a stop.</summary>
    public Action<InstallScope>? AfterPrepare { get; init; }
}

/// <summary>
/// An app's Always run as administrator setting, applied at install time. Only a system install applies it, in the
/// elevated install process after the files are in place, for the installing account. An optional setting shows a
/// check box with <see cref="Title"/> and <see cref="Warning"/>; a required one (<see cref="IsRequired"/>) shows nothing
/// and is always turned on.
/// </summary>
public sealed class TrayAppDotNETRunAsAdministratorOption
{
    /// <summary>
    /// The app always runs as administrator: a system install always turns the setting on, whatever the install
    /// choices say, and the installer offers no choice.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>The installer check box's label; required unless <see cref="IsRequired"/>.</summary>
    public Func<string>? Title { get; init; }

    /// <summary>The warning the installer shows under the check box; required unless <see cref="IsRequired"/>.</summary>
    public Func<string>? Warning { get; init; }

    /// <summary>Turns the setting on or off for the current account; runs elevated. Returns whether it succeeded.</summary>
    public required Func<bool, bool> SetEnabled { get; init; }

    /// <summary>
    /// The choice a system install applies: on for a required setting, otherwise <paramref name="requested"/>, where
    /// null leaves the setting as it is.
    /// </summary>
    public bool? EffectiveChoice(bool? requested) => IsRequired ? true : requested;
}

public sealed class TrayAppDotNETAppInstallServicesOptions
{
    /// <summary>The app's name, which is also its executable name without extension.</summary>
    public required string ApplicationName { get; init; }

    /// <summary>The app's single-instance GUID (never changes once shipped).</summary>
    public required string AppGuid { get; init; }

    public required int BuildNumber { get; init; }

    /// <summary>Posts an action to the UI thread.</summary>
    public required Action<Action> PostToUIThread { get; init; }

    /// <summary>Files the app ships beside its executable on top of the Native AOT defaults.</summary>
    public IReadOnlyList<TrayAppDotNETInstallFile> ExtraRequiredFiles { get; init; } = [];

    public TrayAppDotNETUninstallHook? UninstallHook { get; init; }

    public TrayAppDotNETRunAsAdministratorOption? RunAsAdministrator { get; init; }
}

/// <summary>
/// The install, uninstall and shell-integration services of one app, built from its identity. Every service is
/// created on first use, so the settings folder (which <see cref="TrayAppDotNETProgram.SettingsDirectoryArgument"/>
/// can replace) is resolved after the command line has been applied.
/// </summary>
public sealed class TrayAppDotNETAppInstallServices
{
    private readonly TrayAppDotNETAppInstallServicesOptions _options;
    private readonly Lazy<TrayAppDotNETInstallLayout> _layout;
    private readonly Lazy<TrayAppDotNETStartupManager> _startup;
    private readonly Lazy<TrayAppDotNETStartMenuShortcut> _startMenu;
    private readonly Lazy<TrayAppDotNETInstallIdentity> _identity;
    private readonly Lazy<TrayAppDotNETInstallationService> _installation;

    public TrayAppDotNETAppInstallServices(TrayAppDotNETAppInstallServicesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApplicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AppGuid);
        ArgumentNullException.ThrowIfNull(options.PostToUIThread);
        if (options.RunAsAdministrator is { IsRequired: false, Title: null } or { IsRequired: false, Warning: null })
            throw new ArgumentException("An optional Always run as administrator setting needs a title and a warning.", nameof(options));
        _options = options;

        _layout = new Lazy<TrayAppDotNETInstallLayout>(() => TrayAppDotNETInstallLayout.Create(
            ApplicationName,
            TrayAppDotNETConstants.SharedRootFolderName,
            LocalAppDataRoot));
        _startup = new Lazy<TrayAppDotNETStartupManager>(() => new TrayAppDotNETStartupManager(
            new TrayAppDotNETStartupOptions(ApplicationName, Layout, DetectInstallations, TADNLog.Log)));
        _startMenu = new Lazy<TrayAppDotNETStartMenuShortcut>(() => new TrayAppDotNETStartMenuShortcut(
            new TrayAppDotNETStartMenuShortcutOptions(ApplicationName, Layout, DetectInstallations, TADNLog.Log)));
        _identity = new Lazy<TrayAppDotNETInstallIdentity>(() => new TrayAppDotNETInstallIdentity(
            ApplicationName,
            TrayAppDotNETConstants.Publisher,
            TrayAppDotNETConstants.HelpLink,
            SettingsDirectory,
            Startup.ShortcutPath,
            Startup.LegacyRunKeyRegistryPath,
            TADNLog.Log));
        _installation = new Lazy<TrayAppDotNETInstallationService>(() => new TrayAppDotNETInstallationService(
            new TrayAppDotNETInstallationOptions(
                Identity,
                Layout,
                CreatePayload(),
                options.BuildNumber,
                StartMenu.Sync,
                options.PostToUIThread)
            {
                CheckUninstall = options.UninstallHook?.CheckUninstall,
                RunAsAdministrator = options.RunAsAdministrator
            }));
    }

    public string ApplicationName => _options.ApplicationName;

    public string AppGuid => _options.AppGuid;

    public int BuildNumber => _options.BuildNumber;

    /// <summary>The shared %LOCALAPPDATA%\TrayAppDotNET root (versions cache, per-user installs, download caches).</summary>
    public static string LocalAppDataRoot =>
        TrayAppDotNETProgram.LocalAppDataRoot(TrayAppDotNETConstants.SharedRootFolderName);

    /// <summary>The app's own data folder (settings, theme, logs), which the settings folder override replaces.</summary>
    public string SettingsDirectory =>
        TrayAppDotNETProgram.AppLocalAppDataDirectory(ApplicationName, TrayAppDotNETConstants.SharedRootFolderName);

    public TrayAppDotNETInstallLayout Layout => _layout.Value;

    public TrayAppDotNETStartupManager Startup => _startup.Value;

    public TrayAppDotNETStartMenuShortcut StartMenu => _startMenu.Value;

    public TrayAppDotNETInstallIdentity Identity => _identity.Value;

    public TrayAppDotNETInstallationService Installation => _installation.Value;

    public TrayAppDotNETUninstallHook? UninstallHook => _options.UninstallHook;

    public TrayAppDotNETRunAsAdministratorOption? RunAsAdministrator => _options.RunAsAdministrator;

    /// <summary>Gets the install folder of a scope.</summary>
    public string InstallDirectory(InstallScope scope) =>
        scope == InstallScope.ProgramFiles ? Layout.ProgramFilesInstallDirectory : Layout.LocalAppDataInstallDirectory;

    /// <summary>Gets the installed executable of a scope.</summary>
    public string InstallExecutable(InstallScope scope) =>
        scope == InstallScope.ProgramFiles
            ? Layout.ProgramFilesInstallExecutable
            : Layout.LocalAppDataInstallExecutable;

    /// <summary>
    /// Runs uninstall preparation with the app's uninstall hook around it. A reason returned by the hook's
    /// <see cref="TrayAppDotNETUninstallHook.BeforePrepare"/> stops it before anything is removed.
    /// </summary>
    public TrayAppDotNETInstallResult PrepareUninstall(
        InstallScope scope,
        bool deleteSettings,
        IProgress<TrayAppDotNETInstallProgress>? progress)
    {
        TrayAppDotNETUninstallHook? hook = _options.UninstallHook;
        if (hook != null && hook.RequiresElevation(scope) && !Environment.IsPrivilegedProcess)
        {
            TADNLog.Log($"{ApplicationName} uninstall hook skipped: {scope} cleanup requires elevation");
            hook = null;
        }

        string? stopReason = null;
        RunHookStep(() => stopReason = hook?.BeforePrepare?.Invoke(scope, deleteSettings), stepName: "before");
        if (stopReason != null)
        {
            TADNLog.Log($"{ApplicationName} uninstall stopped before removing anything: {stopReason}");
            progress?.Report(TrayAppDotNETInstallProgress.Failed(stopReason));
            return new TrayAppDotNETInstallResult(Success: false, stopReason);
        }

        try
        {
            return Installation.PrepareUninstall(scope, deleteSettings, progress);
        }
        finally
        {
            RunHookStep(() => hook?.AfterPrepare?.Invoke(scope), stepName: "after");
        }
    }

    /// <summary>Builds the command-line entry options for <see cref="TrayAppDotNETProgram.Run"/>.</summary>
    public TrayAppDotNETProgramOptions CreateProgramOptions(Func<string[], int> runApplication)
    {
        ArgumentNullException.ThrowIfNull(runApplication);

        return new TrayAppDotNETProgramOptions(
            ApplicationName,
            TrayAppDotNETConstants.SharedRootFolderName,
            AppGuid,
            runApplication,
            (sourceExecutable, buildNumber, installOptions, progress) => TrayAppDotNETProgramInstallResult.From(
                Installation.RunAdminInstallSystem(sourceExecutable, buildNumber, installOptions, progress)),
            (removingScope, allUsers) => StartMenu.Sync(removingScope, allUsers),
            (scope, deleteSettings, progress) =>
                TrayAppDotNETProgramInstallResult.From(PrepareUninstall(scope, deleteSettings, progress)),
            (scope, deleteSettings, progress) => Installation.RunUninstall(
                scope,
                deleteSettings,
                static () => Environment.Exit(0),
                progress),
            (installOptions, progress) => TrayAppDotNETProgramInstallResult.From(
                Installation.InstallToLocalAppData(installOptions: installOptions, progress: progress)),
            (installOptions, progress) => TrayAppDotNETProgramInstallResult.From(
                Installation.InstallSystemWide(installOptions: installOptions, progress: progress)),
            () => Layout.LocalAppDataInstallExecutable,
            () => Layout.ProgramFilesInstallExecutable,
            () => Installation);
    }

    private TrayAppDotNETInstallPayload CreatePayload()
    {
        TrayAppDotNETInstallPayload payload = TrayAppDotNETInstallPayload.NativeAOTApp(ApplicationName);
        if (_options.ExtraRequiredFiles.Count == 0) return payload;

        return payload with { RequiredFiles = [.. payload.RequiredFiles, .. _options.ExtraRequiredFiles] };
    }

    private List<TrayAppDotNETInstallationInfo> DetectInstallations() => Installation.DetectAll();

    private void RunHookStep(Action step, string stepName)
    {
        try
        {
            step();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"{ApplicationName} uninstall hook ({stepName} preparation) failed: {exception}");
        }
    }
}
