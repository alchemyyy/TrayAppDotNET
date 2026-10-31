using CommonSettingsNavigationGlyphs = TrayAppDotNETCommon.Visuals.SettingsNavigationGlyphs;
using BatteryInstallScope = TrayAppDotNETCommon.Models.InstallScope;

namespace BatteryTrayAppDotNET.UI.Settings;

public enum BatterySettingsPage
{
    General,
    Triggers,
    Flyout,
    TrayIcon,
    Hotkeys,
    Theme,
    About
}

/// <summary>Battery settings shell; each page lives in the partial file named after its page.</summary>
public sealed partial class BatterySettingsWindow : SettingsWindowCommon<BatterySettingsPage>
{
    private readonly AppSettings _settings;
    private readonly Func<BatteryInstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> _showUninstaller;

    public BatterySettingsWindow()
        : this(new AppSettings(), static (_, _) => Task.FromResult(false))
    {
    }

    public BatterySettingsWindow(
        AppSettings settings,
        Func<BatteryInstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureCompactSettingsWindow(
            L(nameof(CommonStrings.SettingsWindow_Title)),
            AppServices.AppIcon.LoadWindowIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(BatterySettingsPage page) => base.SelectPage(page);

    protected override AppSettingsCommon CommonSettings => _settings;

    protected override AppTheme? SettingsTheme => AppServices.Theme;

    protected override BatterySettingsPage DefaultPageKey => BatterySettingsPage.General;

    protected override string HeaderText => L(nameof(CommonStrings.SettingsWindow_Header));

    protected override IReadOnlyList<SettingsPageDescriptor<BatterySettingsPage>> CreatePageDescriptors() =>
    [
        new(BatterySettingsPage.General, L(nameof(CommonStrings.Settings_Common_Page_General)), BuildGeneralPage,
            CommonSettingsNavigationGlyphs.General),
        new(BatterySettingsPage.Triggers, L(nameof(AppStrings.Settings_Common_Page_Triggers)), BuildTriggersPage,
            CommonSettingsNavigationGlyphs.Triggers),
        new(BatterySettingsPage.Flyout, L(nameof(CommonStrings.Settings_Common_Page_Flyout)), BuildFlyoutPage,
            CommonSettingsNavigationGlyphs.Flyout),
        new(BatterySettingsPage.TrayIcon, L(nameof(CommonStrings.Settings_Common_Page_TrayIcon)), BuildTrayIconPage,
            CommonSettingsNavigationGlyphs.TrayIcon),
        new(BatterySettingsPage.Hotkeys, L(nameof(CommonStrings.Settings_Common_Page_Hotkeys)), BuildHotkeysPage,
            CommonSettingsNavigationGlyphs.Hotkeys),
        new(BatterySettingsPage.Theme, L(nameof(CommonStrings.Settings_Common_Page_Theme)), BuildThemePage,
            CommonSettingsNavigationGlyphs.Theme),
        new(BatterySettingsPage.About, L(nameof(CommonStrings.Settings_Common_Page_About)), BuildAboutPage,
            CommonSettingsNavigationGlyphs.About)
    ];
}
