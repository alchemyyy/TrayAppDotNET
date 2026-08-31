using Avalonia.Threading;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET;

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
        DetectInstallations,
        TADNLog.Log));

    public static TrayAppDotNETStartMenuShortcut StartMenu { get; } = new(new TrayAppDotNETStartMenuShortcutOptions(
        Program.ApplicationName,
        InstallLayout,
        DetectInstallations,
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
        CreateInstallPayload(),
        BuildInfo.BuildNumber,
        StartMenu.Sync,
        action => Dispatcher.UIThread.Post(action)));

    public static AppTheme? Theme { get; set; }
    public static AppSettings? Settings { get; set; }

    private static List<TrayAppDotNETInstallationInfo> DetectInstallations() =>
        Installation.DetectAll();

    private static TrayAppDotNETInstallPayload CreateInstallPayload()
    {
        TrayAppDotNETInstallPayload payload = TrayAppDotNETInstallPayload.NativeAOTApp(Program.ApplicationName);
        TrayAppDotNETInstallFile[] requiredFiles =
        [
            .. payload.RequiredFiles,
            new(Constants.KillHelperFileName)
        ];
        return payload with { RequiredFiles = requiredFiles };
    }
}
