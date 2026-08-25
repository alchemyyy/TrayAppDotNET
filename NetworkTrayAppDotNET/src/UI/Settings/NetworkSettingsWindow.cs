#pragma warning disable CA1822

using Avalonia.Controls;
using Avalonia.Media;
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
    private readonly Action<string, InstallScope> _showUninstaller;
    private readonly TrayAppDotNETSettingsColorCardCoordinator _colorCardCoordinator = new();
    private readonly List<TrayAppDotNETAboutPage> _aboutPageGenerations = [];
    private TrayAppDotNETAboutPage? _aboutPage;

    public NetworkSettingsWindow()
        : this(new AppSettings(), static (_, _) => { })
    {
    }

    public NetworkSettingsWindow(AppSettings settings, Action<string, InstallScope> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureSettingsWindow(L(nameof(AppStrings.SettingsWindow_Title)), AppTheme.LoadAppIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(NetworkSettingsPage page) => base.SelectPage(page);

    protected override SettingsPalette ResolvePalette() =>
        CreatePalette(AppServices.Theme, _settings, ResolveEffectiveIsLight());

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;

    protected override bool UseWindows11SettingsNavigation => _settings.UseWindows11SettingsNavigation;

    protected override NetworkSettingsPage DefaultPageKey => NetworkSettingsPage.General;

    protected override string HeaderText => L(nameof(AppStrings.SettingsWindow_Header));

    protected override string OpenSettingsFolderText =>
        L(nameof(AppStrings.SettingsWindow_OpenSettingsFolder));

    protected override string SettingsFolderPath => AppSettings.GetDefaultDirectory();

    protected override Color ConfirmOverlayBackdrop =>
        (AppServices.Theme ?? AppTheme.Default).FlyoutOverlayBackdrop.For(ResolveEffectiveIsLight());

    protected override IReadOnlyList<SettingsPageDescriptor<NetworkSettingsPage>> CreatePageDescriptors() =>
    [
        new(NetworkSettingsPage.General, L(nameof(AppStrings.Settings_Common_Page_General)), BuildGeneralPage,
            CommonSettingsNavigationGlyphs.General),
        new(NetworkSettingsPage.TrayIcon, L(nameof(AppStrings.Settings_Common_Page_TrayIcon)), BuildTrayIconPage,
            CommonSettingsNavigationGlyphs.TrayIcon),
        new(NetworkSettingsPage.Hotkeys, L(nameof(AppStrings.Settings_Common_Page_Hotkeys)), BuildHotkeysPage,
            CommonSettingsNavigationGlyphs.Hotkeys),
        new(NetworkSettingsPage.Theme, L(nameof(AppStrings.Settings_Common_Page_Theme)), BuildThemePage,
            CommonSettingsNavigationGlyphs.Theme),
        new(NetworkSettingsPage.About, L(nameof(AppStrings.Settings_Common_Page_About)), BuildAboutPage,
            CommonSettingsNavigationGlyphs.About)
    ];

    protected override void OnSettingsWindowClosed()
    {
        StopAboutUpdateRefresh();
        _colorCardCoordinator.CloseOpenColorPickers();
    }

    internal void StopAboutUpdateRefresh()
    {
        _aboutPage?.StopUpdateRefresh();
        _aboutPage = null;
    }

    internal static SettingsPalette CreatePalette(AppTheme? theme, AppSettings? settings, bool isLight)
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
            resolvedTheme.SearchListItemSelected.For(isLight),
            resolvedTheme.SearchListItemHover.For(isLight),
            resolvedTheme.SliderProgress.For(isLight),
            resolvedTheme.SliderTrack.For(isLight),
            resolvedTheme.SliderThumb.For(isLight),
            resolvedTheme.CloseButtonHover.For(isLight),
            resolvedTheme.CloseButtonPressed.For(isLight),
            resolvedTheme.CloseButtonGlyphActive.For(isLight),
            hoverDeep: resolvedTheme.HoverDeep.For(isLight),
            pressedDeep: resolvedTheme.PressedDeep.For(isLight),
            controlBackgroundDeep: resolvedTheme.ControlBackgroundDeep.For(isLight));
    }

    protected override void Save()
    {
        _settings.Save();
        _settings.RaiseChanged();
    }

    private Border ColorCard(
        string name,
        string title,
        string description,
        string lightTooltip,
        string darkTooltip,
        NullableThemeColor color,
        Color lightFallback,
        Color darkFallback,
        SettingsPalette palette,
        IReadOnlyList<string>? searchKeywords = null) =>
        _colorCardCoordinator.ColorCard(
            this,
            name,
            title,
            description,
            lightTooltip,
            darkTooltip,
            color,
            lightFallback,
            darkFallback,
            palette,
            EnableRoundedCorners,
            RadiusMedium,
            RadiusLarge,
            Loc(nameof(CommonStrings.Settings_Theme_Reset)),
            ResolveEffectiveIsLight,
            VariantPickerTitle,
            ColorPickerStrings(),
            Save,
            RefreshPalette,
            IsSettingsWindowClosing,
            searchKeywords);

    private bool IsSettingsWindowClosing() => IsClosing;

    private static string VariantPickerTitle(string title, bool isLight) =>
        string.Format(
            Loc(nameof(CommonStrings.Settings_Theme_PickerTitle_Format)),
            title,
            Loc(isLight
                ? nameof(CommonStrings.Settings_Theme_PickerTitle_LightVariant)
                : nameof(CommonStrings.Settings_Theme_PickerTitle_DarkVariant)));

    private static TrayAppDotNETColorPickerStrings ColorPickerStrings() =>
        new(
            Loc(nameof(AppStrings.ColorPicker_DefaultTitle)),
            Loc(nameof(CommonStrings.ColorPicker_CloseTooltip)),
            Loc(nameof(CommonStrings.ColorPicker_ChannelLabel_Hue)),
            Loc(nameof(CommonStrings.ColorPicker_ChannelLabel_Alpha)),
            Loc(nameof(CommonStrings.ColorPicker_ChannelLabel_R)),
            Loc(nameof(CommonStrings.ColorPicker_ChannelLabel_G)),
            Loc(nameof(CommonStrings.ColorPicker_ChannelLabel_B)),
            Loc(nameof(CommonStrings.ColorPicker_RGBAHexLabel)),
            Loc(nameof(CommonStrings.ColorPicker_ARGBHexLabel)),
            Loc(nameof(CommonStrings.ColorPicker_DefaultButton)),
            Loc(nameof(CommonStrings.ColorPicker_ResetButton)));

    private bool ResolveEffectiveIsLight() => _settings.ThemeMode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        _ => AppServices.Theme?.IsLightTheme ?? false
    };
}
