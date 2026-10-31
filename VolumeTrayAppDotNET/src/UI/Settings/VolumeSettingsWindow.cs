using VolumeInstallScope = TrayAppDotNETCommon.Models.InstallScope;
using CommonSettingsNavigationGlyphs = TrayAppDotNETCommon.Visuals.SettingsNavigationGlyphs;

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
    private readonly Func<VolumeInstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> _showUninstaller;

    public VolumeSettingsWindow()
        : this(new AppSettings(), static (_, _) => Task.FromResult(false))
    {
    }

    public VolumeSettingsWindow(
        AppSettings settings,
        Func<VolumeInstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureSettingsWindow(L(nameof(CommonStrings.SettingsWindow_Title)), AppServices.AppIcon.LoadWindowIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(VolumeSettingsPage page) => base.SelectPage(page);

    protected override AppSettingsCommon CommonSettings => _settings;

    protected override AppTheme? SettingsTheme => AppServices.Theme;

    protected override VolumeSettingsPage DefaultPageKey => VolumeSettingsPage.General;

    protected override string HeaderText => L(nameof(CommonStrings.SettingsWindow_Header));

    protected override IReadOnlyList<SettingsPageDescriptor<VolumeSettingsPage>> CreatePageDescriptors() =>
    [
        new(VolumeSettingsPage.General, L(nameof(CommonStrings.Settings_Common_Page_General)),
            BuildGeneralPage,
            CommonSettingsNavigationGlyphs.General),
        new(VolumeSettingsPage.Flyout, L(nameof(CommonStrings.Settings_Common_Page_Flyout)),
            BuildFlyoutPage,
            CommonSettingsNavigationGlyphs.Flyout),
        new(VolumeSettingsPage.Devices, L(nameof(AppStrings.Settings_Common_Page_Devices)),
            BuildDevicesPage,
            CommonSettingsNavigationGlyphs.Devices),
        new(VolumeSettingsPage.DeviceAppDrawers, L(nameof(AppStrings.Settings_Common_Page_DeviceAppDrawers)),
            BuildDeviceAppDrawersPage,
            CommonSettingsNavigationGlyphs.DeviceAppDrawers),
        new(VolumeSettingsPage.TrayIcon, L(nameof(CommonStrings.Settings_Common_Page_TrayIcon)),
            BuildTrayIconPage,
            CommonSettingsNavigationGlyphs.TrayIcon),
        new(VolumeSettingsPage.Hotkeys, L(nameof(CommonStrings.Settings_Common_Page_Hotkeys)),
            BuildHotkeysPage,
            CommonSettingsNavigationGlyphs.Hotkeys),
        new(VolumeSettingsPage.Theme, L(nameof(CommonStrings.Settings_Common_Page_Theme)),
            BuildThemePage,
            CommonSettingsNavigationGlyphs.Theme),
        new(VolumeSettingsPage.About, L(nameof(CommonStrings.Settings_Common_Page_About)),
            BuildAboutPage,
            CommonSettingsNavigationGlyphs.About)
    ];
}
