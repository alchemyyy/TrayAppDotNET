using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET;

internal static class AppServices
{
    /// <summary>
    /// Always run as administrator. A redirected taskmgr.exe start hands off like a plain start: its switches mean
    /// nothing to the app.
    /// </summary>
    public static TrayAppDotNETElevatedStartTask ElevatedStart { get; } = new(
        Constants.ApplicationName,
        SystemInstallExecutable,
        TaskManagerReplacement.IsRedirectedLaunch);

    public static TrayAppDotNETAppInstallServices Install { get; } = new(new TrayAppDotNETAppInstallServicesOptions
    {
        ApplicationName = Constants.ApplicationName,
        AppGuid = Constants.AppGUID,
        BuildNumber = BuildInfo.BuildNumber,
        PostToUIThread = static action => Dispatcher.UIThread.Post(action),
#if !TASK_MANAGER_NATIVE_AOT
        ExtraRequiredFiles = [new TrayAppDotNETInstallFile(Constants.KillHelperFileName)],
#endif
        UninstallHook = new TrayAppDotNETUninstallHook
        {
            // Ctrl+Shift+Esc must never point at a deleted exe: clear the taskmgr.exe redirect before removal, and stop
            // the uninstall when it cannot be cleared. A system uninstall also deletes the Always run as administrator
            // tasks
            CheckUninstall = TaskManagerUninstallGuard.CheckUninstall,
            BeforePrepare = static (scope, _) => TaskManagerUninstallGuard.BeforePrepare(scope)
        },
        // The installer turns Always run as administrator on by default for a system install, with a warning and a
        // check box to leave it off
        RunAsAdministrator = new TrayAppDotNETRunAsAdministratorOption
        {
            Title = static () => LocalizationManager.Instance[nameof(AppStrings.Installer_AlwaysRunAsAdministrator_Title)],
            Warning = static () => LocalizationManager.Instance[nameof(AppStrings.Installer_AlwaysRunAsAdministrator_Warning)],
            SetEnabled = static enable => ElevatedStart.SetEnabledForCurrentUser(enable)
        }
    });

    private static string SystemInstallExecutable() => Install.Layout.ProgramFilesInstallExecutable;

    public static AppIconLoader AppIcon { get; } = new(Constants.AppIconResourceUri);

    /// <summary>The running app host; the main window opens the uninstaller through it.</summary>
    public static TrayAppDotNETAppHost<AppSettings, AppTheme>? Host { get; set; }

    public static AppTheme? Theme { get; set; }
    public static AppSettings? Settings { get; set; }
    public static UpdateCheckService? UpdateCheckService { get; set; }

    /// <summary>Runs privileged process actions through the elevated broker; set once the UI is up.</summary>
    public static ElevatedActionCoordinator? ElevatedActions { get; set; }
}
