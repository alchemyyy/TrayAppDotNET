using Avalonia.Media;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI;
using BrightnessInstallScope = TrayAppDotNETCommon.Models.InstallScope;
using CommonSettingsNavigationGlyphs = TrayAppDotNETCommon.Visuals.SettingsNavigationGlyphs;

namespace BrightnessTrayAppDotNET.UI.Settings;

public enum BrightnessSettingsPage
{
    General,
    Flyout,
    TrayIcon,
    Monitors,
    Hotkeys,
    Environmental,
    Theme,
    About
}

public sealed partial class BrightnessSettingsWindow : SettingsWindowCommon<BrightnessSettingsPage>
{
    private readonly AppSettings _settings;
    private readonly Func<BrightnessInstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> _showUninstaller;
    private readonly ProfileManager? _profileManager;
    private readonly MonitorService? _monitorService;
    private readonly MonitorBrightnessRangeProvider? _brightnessRangeProvider;

    private static BrightnessSettingsUIResources.BrightnessSettingsUIAxamlProperties AXAMLSettingsUI =>
        BrightnessSettingsUIResources.Current.AxamlBrightnessSettingsUI;

    public BrightnessSettingsWindow()
        : this(new AppSettings(), static (_, _) => Task.FromResult(false))
    {
    }

    public BrightnessSettingsWindow(
        AppSettings settings,
        Func<BrightnessInstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        _profileManager = AppServices.ProfileManager;
        _monitorService = AppServices.MonitorService;
        _brightnessRangeProvider = AppServices.MonitorBrightnessRangeProvider;

        ConfigureSettingsWindow(L(nameof(CommonStrings.SettingsWindow_Title)), AppServices.AppIcon.LoadWindowIcon());
        InitializeSettingsShell();
#if DEBUG
        BrightnessSettingsUIResources.ResourcesReloaded += OnSettingsUIResourcesReloaded;
#endif
    }

    protected override void OnSettingsWindowClosed()
    {
#if DEBUG
        BrightnessSettingsUIResources.ResourcesReloaded -= OnSettingsUIResourcesReloaded;
#endif
        base.OnSettingsWindowClosed();
    }

#if DEBUG
    /// <summary>Rebuilds the open shell so its navigation icons pick up reloaded settings-UI values.</summary>
    private void OnSettingsUIResourcesReloaded()
    {
        if (!IsClosing) RebuildShell(CurrentPageKey);
    }
#endif

    internal new void SelectPage(BrightnessSettingsPage page) => base.SelectPage(page);

    protected override AppSettingsCommon CommonSettings => _settings;

    protected override AppTheme? SettingsTheme => AppServices.Theme;

    protected override BrightnessSettingsPage DefaultPageKey => BrightnessSettingsPage.General;

    protected override string HeaderText => L(nameof(CommonStrings.SettingsWindow_Header));

    protected override IReadOnlyList<SettingsPageDescriptor<BrightnessSettingsPage>> CreatePageDescriptors() =>
    [
        new(BrightnessSettingsPage.General, L(nameof(CommonStrings.Settings_Common_Page_General)),
            BuildGeneralPage,
            CommonSettingsNavigationGlyphs.General),
        new(BrightnessSettingsPage.Flyout, L(nameof(CommonStrings.Settings_Common_Page_Flyout)),
            BuildFlyoutPage,
            CommonSettingsNavigationGlyphs.Flyout),
        new(BrightnessSettingsPage.TrayIcon, L(nameof(CommonStrings.Settings_Common_Page_TrayIcon)),
            BuildTrayIconPage,
            CommonSettingsNavigationGlyphs.TrayIcon),
        new(BrightnessSettingsPage.Monitors, L(nameof(AppStrings.Settings_Common_Page_Monitors)),
            BuildMonitorsPage,
            CommonSettingsNavigationGlyphs.MonitorOptions),
        new(BrightnessSettingsPage.Hotkeys, L(nameof(CommonStrings.Settings_Common_Page_Hotkeys)),
            BuildHotkeysPage,
            CommonSettingsNavigationGlyphs.Hotkeys,
            NavigationIconTransform: AXAMLSettingsUI.HotkeysNavigationIconTransform),
        new(BrightnessSettingsPage.Environmental, L(nameof(AppStrings.Settings_Common_Page_Environmental)),
            BuildEnvironmentalPage,
            NavigationIconFactory: BuildEnvironmentalNavigationIcon,
            NavigationIconScale: AXAMLSettingsUI.EnvironmentalNavigationIconScale,
            NavigationIconTransform: AXAMLSettingsUI.EnvironmentalNavigationIconTransform),
        new(BrightnessSettingsPage.Theme, L(nameof(CommonStrings.Settings_Common_Page_Theme)),
            BuildThemePage,
            CommonSettingsNavigationGlyphs.Theme),
        new(BrightnessSettingsPage.About, L(nameof(CommonStrings.Settings_Common_Page_About)),
            BuildAboutPage,
            CommonSettingsNavigationGlyphs.About)
    ];

    private static EnvironmentalCurveGlyphIcon BuildEnvironmentalNavigationIcon(Color color) =>
        new() { IconColor = color };
}
