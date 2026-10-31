using CommonSettingsNavigationGlyphs = TrayAppDotNETCommon.Visuals.SettingsNavigationGlyphs;
using FanInstallScope = TrayAppDotNETCommon.Models.InstallScope;

namespace FanControlTrayAppDotNET.UI.Settings;

public enum FanSettingsPage
{
    General,
    FanProperties,
    Flyout,
    TrayIcon,
    Hotkeys,
    Theme,
    About
}

/// <summary>Fan Control settings shell; each page lives in the partial file named after its page.</summary>
public sealed partial class FanSettingsWindow : SettingsWindowCommon<FanSettingsPage>
{
    private readonly AppSettings _settings;
    private readonly Func<FanInstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> _showUninstaller;

    public FanSettingsWindow()
        : this(new AppSettings(), static (_, _) => Task.FromResult(false))
    {
    }

    public FanSettingsWindow(
        AppSettings settings,
        Func<FanInstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureSettingsWindow(L(nameof(CommonStrings.SettingsWindow_Title)), AppServices.AppIcon.LoadWindowIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(FanSettingsPage page) => base.SelectPage(page);

    protected override AppSettingsCommon CommonSettings => _settings;

    protected override AppTheme? SettingsTheme => AppServices.Theme;

    protected override FanSettingsPage DefaultPageKey => FanSettingsPage.General;

    protected override string HeaderText => L(nameof(CommonStrings.SettingsWindow_Header));

    protected override IReadOnlyList<SettingsPageDescriptor<FanSettingsPage>> CreatePageDescriptors() =>
    [
        new(FanSettingsPage.General, L(nameof(CommonStrings.Settings_Common_Page_General)),
            BuildGeneralPage,
            CommonSettingsNavigationGlyphs.General),
        new(FanSettingsPage.FanProperties, L(nameof(AppStrings.Settings_Common_Page_FanProperties)),
            BuildFanPropertiesPage,
            GlyphCatalog.FAN),
        new(FanSettingsPage.Flyout, L(nameof(CommonStrings.Settings_Common_Page_Flyout)),
            BuildFlyoutPage,
            CommonSettingsNavigationGlyphs.Flyout),
        new(FanSettingsPage.TrayIcon, L(nameof(CommonStrings.Settings_Common_Page_TrayIcon)),
            BuildTrayIconPage,
            CommonSettingsNavigationGlyphs.TrayIcon),
        new(FanSettingsPage.Hotkeys, L(nameof(CommonStrings.Settings_Common_Page_Hotkeys)),
            BuildHotkeysPage,
            CommonSettingsNavigationGlyphs.Hotkeys),
        new(FanSettingsPage.Theme, L(nameof(CommonStrings.Settings_Common_Page_Theme)),
            BuildThemePage,
            CommonSettingsNavigationGlyphs.Theme),
        new(FanSettingsPage.About, L(nameof(CommonStrings.Settings_Common_Page_About)),
            BuildAboutPage,
            CommonSettingsNavigationGlyphs.About)
    ];
}
