#pragma warning disable CA1822

using Avalonia.Controls;
using Avalonia.Media;
using NetworkTrayAppDotNET.Models;
using TrayAppDotNETCommon.UI.Settings;

namespace NetworkTrayAppDotNET.UI;

public enum NetworkSettingsPage
{
    General,
    TrayIcon,
    Network,
    Hotkeys,
    Theme,
    About,
}

public sealed partial class NetworkSettingsWindow : SettingsWindowCommon<NetworkSettingsPage>
{
    private readonly AppSettings _settings;
    private readonly Action<string, InstallScope> _showUninstaller;
    private readonly TrayAppDotNETSettingsColorCardCoordinator _colorCardCoordinator = new();
    private TrayAppDotNETAboutPage? _aboutPage;

    public NetworkSettingsWindow()
        : this(new AppSettings(), static (_, _) => { })
    {
    }

    public NetworkSettingsWindow(AppSettings settings, Action<string, InstallScope> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureSettingsWindow(
            L("SettingsWindow_Title", "Settings"),
            width: 960,
            height: 670,
            minWidth: 720,
            minHeight: 520,
            AppTheme.LoadAppIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(NetworkSettingsPage page) => base.SelectPage(page);

    protected override SettingsPalette Palette =>
        CreatePalette(AppServices.Theme, _settings, ResolveEffectiveIsLight());

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;

    protected override NetworkSettingsPage DefaultPageKey => NetworkSettingsPage.General;

    protected override string HeaderText => L("SettingsWindow_Header", "Settings");

    protected override string OpenSettingsFolderText =>
        L("SettingsWindow_OpenSettingsFolder", "Open settings folder");

    protected override string SettingsFolderPath => AppSettings.GetDefaultDirectory();

    protected override Color ConfirmOverlayBackdrop =>
        (AppServices.Theme ?? AppTheme.Default).FlyoutOverlayBackdrop.For(ResolveEffectiveIsLight());

    protected override IReadOnlyList<SettingsPageDescriptor<NetworkSettingsPage>> CreatePageDescriptors() =>
    [
        new(NetworkSettingsPage.General, L("Settings_Common_Page_General", "General"), BuildGeneralPage),
        new(NetworkSettingsPage.TrayIcon, L("Settings_Common_Page_TrayIcon", "Tray Icon"), BuildTrayIconPage),
        new(NetworkSettingsPage.Network, L("Settings_Common_Page_Network", "Network"), BuildNetworkPage),
        new(NetworkSettingsPage.Hotkeys, L("Settings_Common_Page_Hotkeys", "Hotkeys"), BuildHotkeysPage),
        new(NetworkSettingsPage.Theme, L("Settings_Common_Page_Theme", "Theme"), BuildThemePage),
        new(NetworkSettingsPage.About, L("Settings_Common_Page_About", "About"), BuildAboutPage),
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
            resolvedTheme.SliderProgress.For(isLight),
            resolvedTheme.SliderTrack.For(isLight),
            resolvedTheme.SliderThumb.For(isLight),
            resolvedTheme.CloseButtonHover.For(isLight),
            resolvedTheme.CloseButtonPressed.For(isLight),
            resolvedTheme.CloseButtonGlyphActive.For(isLight));
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
        SettingsPalette palette) =>
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
            RadiusMedium,
            RadiusLarge,
            Loc("Settings_Theme_Reset"),
            ResolveEffectiveIsLight,
            VariantPickerTitle,
            ColorPickerStrings(),
            Save,
            () => RebuildShell(CurrentPageKey),
            () => IsClosing);

    private static string VariantPickerTitle(string title, bool isLight) =>
        string.Format(
            Loc("Settings_Theme_PickerTitle_Format"),
            title,
            Loc(isLight ? "Settings_Theme_PickerTitle_LightVariant" : "Settings_Theme_PickerTitle_DarkVariant"));

    private static TrayAppDotNETColorPickerStrings ColorPickerStrings() =>
        new(
            Loc("ColorPicker_DefaultTitle"),
            Loc("ColorPicker_CloseTooltip"),
            Loc("ColorPicker_ChannelLabel_Hue"),
            Loc("ColorPicker_ChannelLabel_Alpha"),
            Loc("ColorPicker_ChannelLabel_R"),
            Loc("ColorPicker_ChannelLabel_G"),
            Loc("ColorPicker_ChannelLabel_B"),
            Loc("ColorPicker_RgbaHexLabel"),
            Loc("ColorPicker_ArgbHexLabel"),
            Loc("ColorPicker_DefaultButton"),
            Loc("ColorPicker_ResetButton"));

    private bool ResolveEffectiveIsLight() => _settings.ThemeMode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        _ => AppServices.Theme?.IsLightTheme ?? false,
    };
}
