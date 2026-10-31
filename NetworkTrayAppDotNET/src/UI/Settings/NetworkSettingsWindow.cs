#pragma warning disable CA1822

using Avalonia.Controls;
using NetworkTrayAppDotNET.Models;
using TrayAppDotNETCommon.UI.Settings;
using CommonSettingsNavigationGlyphs = TrayAppDotNETCommon.Visuals.SettingsNavigationGlyphs;

namespace NetworkTrayAppDotNET.UI.Settings;

public enum NetworkSettingsPage
{
    General,
    TrayIcon,
    Hotkeys,
    Theme,
    About
}

public sealed partial class NetworkSettingsWindow : SettingsWindowCommon<NetworkSettingsPage>
{
    private readonly AppSettings _settings;
    private readonly Func<InstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> _showUninstaller;

    public NetworkSettingsWindow()
        : this(new AppSettings(), static (_, _) => Task.FromResult(false))
    {
    }

    public NetworkSettingsWindow(
        AppSettings settings,
        Func<InstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureSettingsWindow(L(nameof(CommonStrings.SettingsWindow_Title)), AppServices.AppIcon.LoadWindowIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(NetworkSettingsPage page) => base.SelectPage(page);

    protected override AppSettingsCommon CommonSettings => _settings;

    protected override AppTheme? SettingsTheme => AppServices.Theme;

    protected override NetworkSettingsPage DefaultPageKey => NetworkSettingsPage.General;

    protected override string HeaderText => L(nameof(CommonStrings.SettingsWindow_Header));

    protected override IReadOnlyList<SettingsPageDescriptor<NetworkSettingsPage>> CreatePageDescriptors() =>
    [
        new(NetworkSettingsPage.General, L(nameof(CommonStrings.Settings_Common_Page_General)), BuildGeneralPage,
            CommonSettingsNavigationGlyphs.General),
        new(NetworkSettingsPage.TrayIcon, L(nameof(CommonStrings.Settings_Common_Page_TrayIcon)), BuildTrayIconPage,
            CommonSettingsNavigationGlyphs.TrayIcon),
        new(NetworkSettingsPage.Hotkeys, L(nameof(CommonStrings.Settings_Common_Page_Hotkeys)), BuildHotkeysPage,
            CommonSettingsNavigationGlyphs.Hotkeys),
        new(NetworkSettingsPage.Theme, L(nameof(CommonStrings.Settings_Common_Page_Theme)), BuildThemePage,
            CommonSettingsNavigationGlyphs.Theme),
        new(NetworkSettingsPage.About, L(nameof(CommonStrings.Settings_Common_Page_About)), BuildAboutPage,
            CommonSettingsNavigationGlyphs.About)
    ];
}
