using Avalonia.Threading;

namespace VolumeTrayAppDotNET;

/// <summary>
/// Strongly-typed slots for the handful of process-singleton services shared between App,
/// the windows, and a few service consumers.
/// Replaces the previous string-keyed <c>Application.Current.Properties</c> lookups - same lifetime
/// (set by App.OnStartup, lives for the process), same ownership story, but the dependency graph
/// is now compile-time greppable and consumers don't need <c>as T</c> / <c>!</c> casts.
/// All slots are nullable: a consumer that runs before its producer (or during a partially-initialised
/// startup that hit an exception) still sees <c>null</c> rather than throwing on cast.
/// </summary>
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
        () => Installation.DetectAll(),
        TADNLog.Log));

    public static TrayAppDotNETStartMenuShortcut StartMenu { get; } = new(new TrayAppDotNETStartMenuShortcutOptions(
        Program.ApplicationName,
        InstallLayout,
        () => Installation.DetectAll(),
        TADNLog.Log));

    public static TrayAppDotNETInstallIdentity InstallIdentity { get; } = new(
        Program.ApplicationName,
        Constants.Publisher,
        Constants.HelpLink,
        AppSettings.GetDefaultDirectory(),
        Startup.ShortcutPath,
        Startup.LegacyRunKeyRegistryPath,
        TADNLog.Log);

    public static TrayAppDotNETInstallationService Installation { get; } = new(new TrayAppDotNETInstallationOptions(
        InstallIdentity,
        InstallLayout,
        TrayAppDotNETInstallPayload.NativeAOTApp(Program.ApplicationName),
        BuildInfo.BuildNumber,
        SyncStartMenu: StartMenu.Sync,
        PostToUIThread: action => Dispatcher.UIThread.Post(action)));

    public static AppTheme? Theme { get; set; }
    public static AppSettings? Settings { get; set; }
    public static DeviceSettings? DeviceSettings { get; set; }
    public static GlobalHotkeyService? HotkeyService { get; set; }
    public static UpdateCheckService? UpdateCheckService { get; set; }
}
