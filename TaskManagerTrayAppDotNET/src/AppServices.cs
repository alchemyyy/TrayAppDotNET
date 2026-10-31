using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET;

internal static class AppServices
{
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
            // the uninstall when it cannot be cleared
            CheckUninstall = TaskManagerUninstallGuard.CheckUninstall,
            BeforePrepare = static (scope, _) => TaskManagerUninstallGuard.BeforePrepare(scope)
        }
    });

    public static AppIconLoader AppIcon { get; } = new(Constants.AppIconResourceUri);

    /// <summary>The running app host; the main window opens the uninstaller through it.</summary>
    public static TrayAppDotNETAppHost<AppSettings, AppTheme>? Host { get; set; }

    public static AppTheme? Theme { get; set; }
    public static AppSettings? Settings { get; set; }
    public static UpdateCheckService? UpdateCheckService { get; set; }

    /// <summary>Runs privileged process actions through the elevated broker; set once the UI is up.</summary>
    public static ElevatedActionCoordinator? ElevatedActions { get; set; }
}
