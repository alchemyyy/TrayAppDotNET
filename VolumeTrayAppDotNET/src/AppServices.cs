using Avalonia.Threading;

namespace VolumeTrayAppDotNET;

/// <summary>
/// Strongly-typed slots for the handful of process-singleton services shared between App,
/// the windows, and a few service consumers.
/// All slots are nullable: a consumer that runs before its producer (or during a partially-initialised
/// startup that hit an exception) still sees <c>null</c> rather than throwing on cast.
/// </summary>
internal static class AppServices
{
    public static TrayAppDotNETAppInstallServices Install { get; } = new(new TrayAppDotNETAppInstallServicesOptions
    {
        ApplicationName = Constants.ApplicationName,
        AppGuid = Constants.AppGUID,
        BuildNumber = BuildInfo.BuildNumber,
        PostToUIThread = static action => Dispatcher.UIThread.Post(action)
    });

    public static AppIconLoader AppIcon { get; } = new(Constants.AppIconResourceUri);

    public static AppTheme? Theme { get; set; }
    public static AppSettings? Settings { get; set; }
    public static DeviceSettings? DeviceSettings { get; set; }
    public static GlobalHotkeyService<HotkeyAction, HotkeyBinding>? HotkeyService { get; set; }
    public static UpdateCheckService? UpdateCheckService { get; set; }
}
