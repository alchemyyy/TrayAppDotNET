using Avalonia.Media;
using VolumeInstallScope = TrayAppDotNETCommon.Models.InstallScope;

namespace VolumeTrayAppDotNET.UI.Settings;

public enum VolumeSettingsPage
{
    General,
    Flyout,
    Devices,
    DeviceAppDrawers,
    TrayIcon,
    Hotkeys,
    Theme,
    About
}

public sealed partial class VolumeSettingsWindow : SettingsWindowCommon<VolumeSettingsPage>
{
    private readonly AppSettings _settings;
    private readonly Lock _uninstallMonitorGate = new();
    private readonly HashSet<PostUninstallRefreshOwner> _uninstallMonitors = [];
    private bool _uninstallMonitoringDisposed;

    public VolumeSettingsWindow()
        : this(new AppSettings(), static (_, _) => { })
    {
    }

    public VolumeSettingsWindow(AppSettings settings, Action<string, VolumeInstallScope> showUninstaller)
    {
        _settings = settings;
        ConfigureSettingsWindow(L("SettingsWindow_Title", "Settings"), AppTheme.LoadAppIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(VolumeSettingsPage page) => base.SelectPage(page);

    protected override SettingsPalette Palette =>
        VolumeSettingsPalette.Create(AppServices.Theme, _settings, ResolveEffectiveIsLight());

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;

    protected override VolumeSettingsPage DefaultPageKey => VolumeSettingsPage.General;

    protected override string HeaderText => L("SettingsWindow_Header", "Settings");

    protected override string OpenSettingsFolderText =>
        L("SettingsWindow_OpenSettingsFolder", "Open settings folder");

    protected override string SettingsFolderPath => AppSettings.GetDefaultDirectory();

    protected override Color ConfirmOverlayBackdrop =>
        (AppServices.Theme ?? AppTheme.Default).FlyoutOverlayBackdrop.For(ResolveEffectiveIsLight());

    protected override void Save()
    {
        _settings.Save();
        _settings.RaiseChanged();
    }

    protected override bool ResolveEffectiveIsLightForBindings() => ResolveEffectiveIsLight();

    protected override void OnSettingsWindowClosed()
    {
        try
        {
            DisposeUninstallMonitors();
        }
        finally
        {
            base.OnSettingsWindowClosed();
        }
    }

    protected override IReadOnlyList<SettingsPageDescriptor<VolumeSettingsPage>> CreatePageDescriptors() =>
    [
        new(VolumeSettingsPage.General, Loc("Settings_Common_Page_General"),
            BuildGeneralPage),
        new(VolumeSettingsPage.Flyout, Loc("Settings_Common_Page_Flyout"),
            BuildFlyoutPage),
        new(VolumeSettingsPage.Devices, Loc("Settings_Common_Page_Devices"),
            BuildDevicesPage),
        new(VolumeSettingsPage.DeviceAppDrawers, Loc("Settings_Common_Page_DeviceAppDrawers"),
            BuildDeviceAppDrawersPage),
        new(VolumeSettingsPage.TrayIcon, Loc("Settings_Common_Page_TrayIcon"),
            BuildTrayIconPage),
        new(VolumeSettingsPage.Hotkeys, Loc("Settings_Common_Page_Hotkeys"),
            BuildHotkeysPage),
        new(VolumeSettingsPage.Theme, Loc("Settings_Common_Page_Theme"),
            BuildThemePage),
        new(VolumeSettingsPage.About, Loc("Settings_Common_Page_About"),
            BuildAboutPage)
    ];

    private bool ResolveEffectiveIsLight() => _settings.ThemeMode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        _ => AppServices.Theme?.IsLightTheme ?? false
    };
}
