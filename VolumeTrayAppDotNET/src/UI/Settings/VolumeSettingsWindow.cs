using Avalonia.Controls;
using Avalonia.Media;
using VolumeInstallScope = TrayAppDotNETCommon.InstallScope;

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
    About,
}

public sealed partial class VolumeSettingsWindow : SettingsWindowCommon<VolumeSettingsPage>
{
    private readonly AppSettings _settings;

    public VolumeSettingsWindow()
        : this(new AppSettings(), static (_, _) => { })
    {
    }

    public VolumeSettingsWindow(AppSettings settings, Action<string, VolumeInstallScope> showUninstaller)
    {
        _settings = settings;
        ConfigureSettingsWindow(
            L("SettingsWindow_Title", "Settings"),
            width: 960,
            height: 670,
            minWidth: 720,
            minHeight: 520,
            AppTheme.LoadAppIcon());
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

    protected override IReadOnlyList<SettingsPageDescriptor<VolumeSettingsPage>> CreatePageDescriptors() =>
    [
        new(VolumeSettingsPage.General, Loc("Settings_Common_Page_General"),
            () => BuildSettingsPage(VolumeSettingsPage.General, BuildGeneralPage)),
        new(VolumeSettingsPage.Flyout, Loc("Settings_Common_Page_Flyout"),
            () => BuildSettingsPage(VolumeSettingsPage.Flyout, BuildFlyoutPage)),
        new(VolumeSettingsPage.Devices, Loc("Settings_Common_Page_Devices"),
            () => BuildSettingsPage(VolumeSettingsPage.Devices, BuildDevicesPage)),
        new(VolumeSettingsPage.DeviceAppDrawers, Loc("Settings_Common_Page_DeviceAppDrawers"),
            () => BuildSettingsPage(VolumeSettingsPage.DeviceAppDrawers, BuildDeviceAppDrawersPage)),
        new(VolumeSettingsPage.TrayIcon, Loc("Settings_Common_Page_TrayIcon"),
            () => BuildSettingsPage(VolumeSettingsPage.TrayIcon, BuildTrayIconPage)),
        new(VolumeSettingsPage.Hotkeys, Loc("Settings_Common_Page_Hotkeys"),
            () => BuildSettingsPage(VolumeSettingsPage.Hotkeys, BuildHotkeysPage)),
        new(VolumeSettingsPage.Theme, Loc("Settings_Common_Page_Theme"),
            () => BuildSettingsPage(VolumeSettingsPage.Theme, BuildThemePage)),
        new(VolumeSettingsPage.About, Loc("Settings_Common_Page_About"),
            () => BuildSettingsPage(VolumeSettingsPage.About, BuildAboutPage)),
    ];

    private bool ResolveEffectiveIsLight() => _settings.ThemeMode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        _ => AppServices.Theme?.IsLightTheme ?? false,
    };

    private Control BuildSettingsPage(VolumeSettingsPage page, Func<Control> buildPage)
    {
        if (page != VolumeSettingsPage.About)
            StopAboutUpdateRefresh();

        return buildPage();
    }

    protected override void OnSettingsWindowClosed()
    {
        StopAboutUpdateRefresh();
        base.OnSettingsWindowClosed();
    }
}
