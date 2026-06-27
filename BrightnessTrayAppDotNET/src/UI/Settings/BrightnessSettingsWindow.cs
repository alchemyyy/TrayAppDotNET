using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using BrightnessInstallScope = TrayAppDotNETCommon.InstallScope;

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
    About,
}

public sealed partial class BrightnessSettingsWindow : SettingsWindowCommon<BrightnessSettingsPage>
{
    private const int ContextMenuFontSizeMin = 8;
    private const int ContextMenuFontSizeMax = 48;

    private readonly AppSettings _settings;
    private readonly Action<string, BrightnessInstallScope> _showUninstaller;
    private readonly ProfileManager? _profileManager;
    private readonly MonitorService? _monitorService;
    private readonly MonitorBrightnessRangeProvider? _brightnessRangeProvider;

    public BrightnessSettingsWindow()
        : this(new AppSettings(), static (_, _) => { })
    {
    }

    public BrightnessSettingsWindow(AppSettings settings)
        : this(settings, static (_, _) => { })
    {
    }

    public BrightnessSettingsWindow(AppSettings settings, Action<string, BrightnessInstallScope> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        _profileManager = AppServices.ProfileManager;
        _monitorService = AppServices.MonitorService;
        _brightnessRangeProvider = AppServices.MonitorBrightnessRangeProvider;

        ConfigureSettingsWindow(
            L("SettingsWindow_Title", "Settings"),
            width: 960,
            height: 670,
            minWidth: 720,
            minHeight: 520,
            AppTheme.LoadAppIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(BrightnessSettingsPage page) => base.SelectPage(page);

    protected override SettingsPalette Palette =>
        CreatePalette(AppServices.Theme, _settings, ResolveEffectiveIsLight());

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;

    protected override BrightnessSettingsPage DefaultPageKey => BrightnessSettingsPage.General;

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

    protected override IReadOnlyList<SettingsPageDescriptor<BrightnessSettingsPage>> CreatePageDescriptors() =>
    [
        new(BrightnessSettingsPage.General, Loc("Settings_Common_Page_General"),
            () => BuildSettingsPage(BrightnessSettingsPage.General, BuildGeneralPage)),
        new(BrightnessSettingsPage.Flyout, Loc("Settings_Common_Page_Flyout"),
            () => BuildSettingsPage(BrightnessSettingsPage.Flyout, BuildFlyoutPage)),
        new(BrightnessSettingsPage.TrayIcon, Loc("Settings_Common_Page_TrayIcon"),
            () => BuildSettingsPage(BrightnessSettingsPage.TrayIcon, BuildTrayIconPage)),
        new(BrightnessSettingsPage.Monitors, Loc("Settings_Common_Page_Monitors"),
            () => BuildSettingsPage(BrightnessSettingsPage.Monitors, BuildMonitorsPage)),
        new(BrightnessSettingsPage.Hotkeys, Loc("Settings_Common_Page_Hotkeys"),
            () => BuildSettingsPage(BrightnessSettingsPage.Hotkeys, BuildHotkeysPage)),
        new(BrightnessSettingsPage.Environmental, L("Settings_Common_Page_Environmental", "Environmental"),
            () => BuildSettingsPage(BrightnessSettingsPage.Environmental, BuildEnvironmentalPage)),
        new(BrightnessSettingsPage.Theme, Loc("Settings_Common_Page_Theme"),
            () => BuildSettingsPage(BrightnessSettingsPage.Theme, BuildThemePage)),
        new(BrightnessSettingsPage.About, Loc("Settings_Common_Page_About"),
            () => BuildSettingsPage(BrightnessSettingsPage.About, BuildAboutPage)),
    ];

    internal static SettingsPalette CreatePalette(AppTheme? theme, AppSettings settings, bool isLight)
    {
        AppTheme resolvedTheme = theme ?? AppTheme.Default;
        return new SettingsPalette(
            resolvedTheme.ResolveBackground(settings, isLight),
            resolvedTheme.ResolveForeground(settings, isLight),
            resolvedTheme.Border.For(isLight),
            resolvedTheme.Hover.For(isLight),
            resolvedTheme.Pressed.For(isLight),
            resolvedTheme.CardBackground.For(isLight),
            resolvedTheme.ControlBackground.For(isLight),
            resolvedTheme.SecondaryForeground.For(isLight),
            resolvedTheme.DisabledForeground.For(isLight),
            resolvedTheme.Accent.For(isLight),
            resolvedTheme.ToggleSwitchOnTrack.For(isLight),
            resolvedTheme.ToggleSwitchOnThumb.For(isLight),
            resolvedTheme.TextBoxFocused.For(isLight),
            resolvedTheme.SliderProgress.For(isLight),
            resolvedTheme.SliderTrack.For(isLight),
            resolvedTheme.SliderThumb.For(isLight),
            resolvedTheme.CloseButtonHover.For(isLight),
            resolvedTheme.CloseButtonPressed.For(isLight),
            resolvedTheme.CloseButtonGlyphActive.For(isLight));
    }

    private bool ResolveEffectiveIsLight() => AppTheme.ResolveEffectiveIsLightTheme(_settings);

    private Control BuildSettingsPage(BrightnessSettingsPage page, Func<Control> buildPage)
    {
        if (page != BrightnessSettingsPage.Environmental)
            StopEnvironmentalPageSession();
        if (page != BrightnessSettingsPage.About)
            StopAboutUpdateRefresh();
        return buildPage();
    }

    protected override void OnSettingsWindowClosed()
    {
        StopEnvironmentalPageSession();
        StopAboutUpdateRefresh();
        base.OnSettingsWindowClosed();
    }
}
