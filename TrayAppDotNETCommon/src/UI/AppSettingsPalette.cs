using Avalonia.Media;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// The one factory for <see cref="SettingsPalette"/>: settings windows, uninstallers, tray menus, flyout dialogs and
/// curve editors of every app build their palette here, so a new palette color is added in one place.
/// </summary>
public static class AppSettingsPalette
{
    /// <summary>
    /// Core overload for callers that resolved their own surface colors (an app-specific background override, a
    /// flyout background). Every other slot, including the Deep colors, comes from <paramref name="theme"/>.
    /// </summary>
    public static SettingsPalette Create(AppTheme theme, Color background, Color foreground, bool isLight) =>
        Create(theme, background, foreground, isLight, theme.Hover.For(isLight), theme.Pressed.For(isLight));

    /// <summary>
    /// Palette for the window background and text color the user chose in <paramref name="settings"/>, or the theme
    /// defaults when they set none. <paramref name="theme"/> falls back to <see cref="AppTheme.Default"/>.
    /// </summary>
    public static SettingsPalette Create(AppTheme? theme, AppSettingsCommon? settings, bool isLight)
    {
        AppTheme resolvedTheme = theme ?? AppTheme.Default;
        return Create(
            resolvedTheme,
            resolvedTheme.ResolveBackground(settings, isLight),
            resolvedTheme.ResolveForeground(settings, isLight),
            isLight);
    }

    /// <summary>
    /// Flyout variant: hover and pressed use the flyout button colors (<see cref="AppTheme.ButtonHover"/>,
    /// <see cref="AppTheme.ButtonPressed"/>) instead of the settings-row ones.
    /// </summary>
    public static SettingsPalette CreateForFlyout(AppTheme theme, AppSettingsCommon? settings, bool isLight) =>
        Create(
            theme,
            theme.ResolveBackground(settings, isLight),
            theme.ResolveForeground(settings, isLight),
            isLight,
            theme.ButtonHover.For(isLight),
            theme.ButtonPressed.For(isLight));

    private static SettingsPalette Create(
        AppTheme theme,
        Color background,
        Color foreground,
        bool isLight,
        Color hover,
        Color pressed) =>
        new(
            background,
            foreground,
            theme.Border.For(isLight),
            hover,
            pressed,
            theme.CardBackground.For(isLight),
            theme.ControlBackground.For(isLight),
            theme.SecondaryForeground.For(isLight),
            theme.DisabledForeground.For(isLight),
            theme.Accent.For(isLight),
            theme.ToggleSwitchOnTrack.For(isLight),
            theme.ToggleSwitchOnThumb.For(isLight),
            theme.TextBoxFocused.For(isLight),
            theme.SearchListItemSelected.For(isLight),
            theme.SearchListItemHover.For(isLight),
            theme.SliderProgress.For(isLight),
            theme.SliderTrack.For(isLight),
            theme.SliderThumb.For(isLight),
            theme.CloseButtonHover.For(isLight),
            theme.CloseButtonPressed.For(isLight),
            theme.CloseButtonGlyphActive.For(isLight),
            theme.HoverDeep.For(isLight),
            theme.PressedDeep.For(isLight),
            theme.ControlBackgroundDeep.For(isLight));
}
