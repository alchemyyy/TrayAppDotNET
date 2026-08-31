using Avalonia.Media;

namespace FanControlTrayAppDotNET.UI.Curves;

public readonly record struct FanCurveEditorPalette(
    Color Background,
    Color Foreground,
    Color SecondaryForeground,
    Color CardBackground,
    Color Border,
    Color GridLine,
    Color Curve,
    Color EffectiveCurve,
    Color CurrentValue,
    Color DisabledBand,
    Color Accent)
{
    public static FanCurveEditorPalette Default { get; } =
        FromTheme(AppTheme.Default, AppTheme.Default.IsLightTheme);

    public static FanCurveEditorPalette FromTheme(AppTheme theme, bool isLight) =>
        FromSettingsPalette(CreateSettingsPalette(theme, isLight), theme, isLight);

    public static FanCurveEditorPalette FromSettingsPalette(SettingsPalette palette, AppTheme? theme, bool isLight)
    {
        AppTheme resolvedTheme = theme ?? AppTheme.Default;
        return new FanCurveEditorPalette(
            palette.Background,
            palette.Foreground,
            palette.SecondaryForeground,
            palette.CardBackground,
            palette.Border,
            resolvedTheme.CurveEditorGridLine.For(isLight),
            palette.Accent,
            resolvedTheme.CurveEditorEffectiveCurve.For(isLight),
            palette.Foreground,
            resolvedTheme.CurveEditorDisabledBand.For(isLight),
            palette.Accent);
    }

    private static SettingsPalette CreateSettingsPalette(AppTheme theme, bool isLight) =>
        new(
            theme.Background.For(isLight),
            theme.Foreground.For(isLight),
            theme.Border.For(isLight),
            theme.Hover.For(isLight),
            theme.Pressed.For(isLight),
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
            theme.CloseButtonGlyphActive.For(isLight));
}
