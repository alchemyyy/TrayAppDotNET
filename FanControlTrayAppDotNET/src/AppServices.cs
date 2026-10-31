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

    public static TrayAppDotNETAppInstallServices Install { get; } = new(new TrayAppDotNETAppInstallServicesOptions
    {
        ApplicationName = Constants.ApplicationName,
        AppGuid = Constants.AppGUID,
        BuildNumber = BuildInfo.BuildNumber,
        PostToUIThread = static action => Dispatcher.UIThread.Post(action),
        ExtraRequiredFiles = [new TrayAppDotNETInstallFile(LibreHardwareMonitorFileName)],
        // The PawnIO driver stays installed (other software may use it); its downloaded setup does not
        UninstallHook = new TrayAppDotNETUninstallHook
        {
            AfterPrepare = static _ => PawnIoDriverInstaller.DeleteDownloadCache()
        }
    });

    public static AppIconLoader AppIcon { get; } = new(Constants.AppIconResourceUri);

    public static AppTheme? Theme { get; set; }
    public static AppSettings? Settings { get; set; }
    public static GlobalHotkeyService<HotkeyAction, HotkeyBinding>? HotkeyService { get; set; }

    public static UpdateCheckService? UpdateCheckService { get; set; }

    // Fan-control domain service: LHMService owns the hardware polling loop
    public static LHMService? LHMService { get; set; }
}
