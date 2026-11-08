using Avalonia.Threading;
using FanControlTrayAppDotNET.Services;

namespace FanControlTrayAppDotNET;

/// <summary>
/// Strongly-typed slots for the handful of process-singleton services shared between App,
/// the windows, and a few service consumers.
/// All slots are nullable: a consumer that runs before its producer (or during a partially-initialised
/// startup that hit an exception) still sees <c>null</c> rather than throwing on cast.
/// </summary>
internal static class AppServices
{
    private const string LibreHardwareMonitorFileName = "LibreHardwareMonitorLib.dll";

    /// <summary>
    /// Always run as administrator, which Fan Control always uses: a plain start of the system install hands off to the
    /// account's scheduled task and comes up elevated without a UAC prompt.
    /// </summary>
    public static TrayAppDotNETElevatedStartTask ElevatedStart { get; } =
        new(Constants.ApplicationName, SystemInstallExecutable);

    public static TrayAppDotNETAppInstallServices Install { get; } = new(new TrayAppDotNETAppInstallServicesOptions
    {
        ApplicationName = Constants.ApplicationName,
        AppGuid = Constants.AppGUID,
        BuildNumber = BuildInfo.BuildNumber,
        PostToUIThread = static action => Dispatcher.UIThread.Post(action),
        ExtraRequiredFiles = [new TrayAppDotNETInstallFile(LibreHardwareMonitorFileName)],
        // The PawnIO driver stays installed (other software may use it); its downloaded setup does not. A system
        // uninstall also deletes every account's Always run as administrator task
        UninstallHook = new TrayAppDotNETUninstallHook
        {
            BeforePrepare = static (scope, _) =>
            {
                ElevatedStart.RemoveForUninstall(scope);
                return null;
            },
            AfterPrepare = static _ => PawnIoDriverInstaller.DeleteDownloadCache()
        },
        // Fan control needs administrator rights, so a system install always registers the task, with no choice
        RunAsAdministrator = new TrayAppDotNETRunAsAdministratorOption
        {
            IsRequired = true,
            SetEnabled = static enable => ElevatedStart.SetEnabledForCurrentUser(enable)
        }
    });

    private static string SystemInstallExecutable() => Install.Layout.ProgramFilesInstallExecutable;

    public static AppIconLoader AppIcon { get; } = new(Constants.AppIconResourceUri);

    public static AppTheme? Theme { get; set; }
    public static AppSettings? Settings { get; set; }
    public static GlobalHotkeyService<HotkeyAction, HotkeyBinding>? HotkeyService { get; set; }

    public static UpdateCheckService? UpdateCheckService { get; set; }

    // Fan-control domain service: LHMService owns the hardware polling loop
    public static LHMService? LHMService { get; set; }
}
