using Avalonia.Threading;
using BrightnessTrayAppDotNET.Interop.NightLight;
using BrightnessTrayAppDotNET.UI.Flyout;
using BrightnessTrayAppDotNET.Utils;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.Services.Install;
using BrightnessHotkeyBinding = BrightnessTrayAppDotNET.Models.HotkeyBinding;

namespace BrightnessTrayAppDotNET;

internal static class AppServices
{
    // Read before preparation (which may delete the settings), acted on after every instance is gone
    private static bool _restoreGammaRampAfterUninstall;

    public static TrayAppDotNETAppInstallServices Install { get; } = new(new TrayAppDotNETAppInstallServicesOptions
    {
        ApplicationName = Constants.ApplicationName,
        AppGuid = Constants.AppGUID,
        BuildNumber = BuildInfo.BuildNumber,
        PostToUIThread = static action => Dispatcher.UIThread.Post(action),
        // A gamma ramp outlives its process. A running instance restores it on its own exit; this restores it when
        // that instance had to be killed or the tint was left behind by an earlier crash.
        UninstallHook = new TrayAppDotNETUninstallHook
        {
            BeforePrepare = static (_, _) =>
            {
                _restoreGammaRampAfterUninstall = AppSettings.IsPersistedGammaRampTintOn();
                return null;
            },
            AfterPrepare = static _ =>
            {
                if (_restoreGammaRampAfterUninstall) GammaRampNightLight.RestoreIdentityOnActiveDisplays();
            }
        }
    });

    public static TrayAppDotNETCommon.UI.Tray.AppIconLoader AppIcon { get; } = new(Constants.AppIconResourceUri);

    public static AppTheme? Theme { get; set; }
    public static AppSettings? Settings { get; set; }
    public static ProfileManager? ProfileManager { get; set; }

    /// <summary>The one known-displays store shared by the monitor service and every profile manager.</summary>
    public static KnownDisplaysStore? KnownDisplays { get; set; }

    public static BrightnessFlyoutWindow? BrightnessFlyout { get; set; }
    public static MonitorService? MonitorService { get; set; }
    public static DisplayEventManager? DisplayEventManager { get; set; }
    public static MonitorBrightnessRangeProvider? MonitorBrightnessRangeProvider { get; set; }
    public static GlobalHotkeyService<BrightnessHotkeyAction, BrightnessHotkeyBinding>? HotkeyService { get; set; }
    public static UpdateCheckService? UpdateCheckService { get; set; }
}
