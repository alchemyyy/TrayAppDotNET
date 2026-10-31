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

    /// <summary>Default palette with no user overrides; windows pass their settings through FromSettingsPalette.</summary>
    public static FanCurveEditorPalette FromTheme(AppTheme theme, bool isLight) =>
        FromSettingsPalette(AppSettingsPalette.Create(theme, settings: null, isLight), theme, isLight);

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
}
