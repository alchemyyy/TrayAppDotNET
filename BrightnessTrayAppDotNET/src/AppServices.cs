using Avalonia.Threading;
using BrightnessTrayAppDotNET.UI.Flyout;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.Services.Install;
using BrightnessHotkeyBinding = BrightnessTrayAppDotNET.Models.HotkeyBinding;

namespace BrightnessTrayAppDotNET;

internal static class AppServices
{
    public static TrayAppDotNETInstallLayout InstallLayout { get; } =
        TrayAppDotNETInstallLayout.Create(
            Program.ApplicationName,
            Program.SharedRootFolderName,
            Program.LocalAppDataRoot);

    public static TrayAppDotNETStartupManager Startup { get; } = new(new TrayAppDotNETStartupOptions(
        Program.ApplicationName,
        InstallLayout,
        () => Installation!.DetectAll(),
        WPFLog.Log));

    public static TrayAppDotNETStartMenuShortcut StartMenu { get; } = new(new TrayAppDotNETStartMenuShortcutOptions(
        Program.ApplicationName,
        InstallLayout,
        () => Installation!.DetectAll(),
        WPFLog.Log));

    public static TrayAppDotNETInstallIdentity InstallIdentity { get; } = new(
        Program.ApplicationName,
        Constants.Publisher,
        Constants.HelpLink,
        AppSettings.GetDefaultDirectory(),
        Startup.ShortcutPath,
        Startup.LegacyRunKeyRegistryPath,
        WPFLog.Log);

    public static TrayAppDotNETInstallationService Installation { get; } = new(new TrayAppDotNETInstallationOptions(
        InstallIdentity,
        InstallLayout,
        TrayAppDotNETInstallPayload.NativeAOTApp(Program.ApplicationName),
        BuildInfo.BuildNumber,
        SyncStartMenu: StartMenu.Sync,
        PostToUIThread: action => Dispatcher.UIThread.Post(action)));

    public static AppTheme? Theme { get; set; }
    public static AppSettings? Settings { get; set; }
    public static ProfileManager? ProfileManager { get; set; }
    public static BrightnessFlyoutWindow? BrightnessFlyout { get; set; }
    public static MonitorService? MonitorService { get; set; }
    public static DisplayEventManager? DisplayEventManager { get; set; }
    public static DDCRecoveryService? DDCRecoveryService { get; set; }
    public static MonitorBrightnessRangeProvider? MonitorBrightnessRangeProvider { get; set; }
    public static GlobalHotkeyService<BrightnessHotkeyAction, BrightnessHotkeyBinding>? HotkeyService { get; set; }
    public static UpdateCheckService? UpdateCheckService { get; set; }
}
