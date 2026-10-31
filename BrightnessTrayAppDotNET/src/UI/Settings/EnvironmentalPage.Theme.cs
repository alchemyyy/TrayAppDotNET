using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.UI.Settings.Environmental;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Models;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private EnvironmentalCurveEditorPalette BuildEnvironmentalEditorPalette(SettingsPalette p)
    {
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;
        bool isLight = ResolveEffectiveIsLight();
        return EnvironmentalCurveEditorPalette.FromSettingsPalette(
            p,
            theme.ResolveEnvironmentalBrightnessCurve(_settings, isLight),
            theme.ResolveEnvironmentalNightLightCurve(_settings, isLight),
            theme.ResolveEnvironmentalCurrentTime(_settings, isLight),
            theme.ResolveEnvironmentalTwilightBackdrop(_settings, isLight),
            theme.ResolveEnvironmentalNightBackdrop(_settings, isLight),
            theme.ResolveEnvironmentalGridLine(_settings, isLight),
            theme.CurveDisabledBandOverlay.For(isLight),
            theme.EnvironmentalPreviewTint.For(isLight));
    }

    private void WireEnvironmentalColorCallbacks()
    {
        EnvironmentalPageState? state = _environmentalPageState;
        if (state == null || state.CurveColorRedraw != null) return;

        long pageGeneration = state.Generation;
        Action redraw = () => DeferredEnvironmentalCurveRedraw(pageGeneration);
        state.CurveColorRedraw = redraw;
        foreach (NullableThemeColor color in EnumerateEnvironmentalCurveColors())
            color.Subscribe(redraw);
    }

    private void UnwireEnvironmentalColorCallbacks()
    {
        EnvironmentalPageState? state = _environmentalPageState;
        if (state?.CurveColorRedraw is not { } redraw) return;

        state.CurveColorRedraw = null;
        foreach (NullableThemeColor color in EnumerateEnvironmentalCurveColors())
        {
            RunEnvironmentalPageCleanup(
                operation: "UnsubscribeEnvironmentalCurveColor",
                () => color.Unsubscribe(redraw));
        }
    }

    private IEnumerable<NullableThemeColor> EnumerateEnvironmentalCurveColors()
    {
        yield return _settings.EnvironmentalBrightnessCurveColor;
        yield return _settings.EnvironmentalNightLightCurveColor;
        yield return _settings.EnvironmentalCurrentTimeColor;
        yield return _settings.EnvironmentalTwilightBackdropColor;
        yield return _settings.EnvironmentalNightBackdropColor;
        yield return _settings.EnvironmentalGridLineColor;
    }

    private void DeferredEnvironmentalCurveRedraw(long pageGeneration) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!IsCurrentEnvironmentalPage(pageGeneration)) return;
                EnvironmentalCurveEditorPalette palette = BuildEnvironmentalEditorPalette(Palette);
                _environmentalCurveEditor?.Palette = palette;
                ApplyLegendColor(_brightnessLegendItem, palette.BrightnessCurve);
                ApplyLegendColor(_nightLightLegendItem, palette.NightLightCurve);
                ApplyLegendColor(_currentTimeLegendItem, palette.CurrentTime);
            },
            DispatcherPriority.Background);

    private static void ApplyLegendColor(StackPanel? item, Color color)
    {
        if (item?.Children.OfType<Border>().FirstOrDefault() is { } swatch)
            swatch.Background = TrayAppDotNETSettingsUI.Brush(color);
    }
}
