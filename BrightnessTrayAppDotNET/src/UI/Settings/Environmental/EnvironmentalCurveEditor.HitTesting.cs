using Avalonia;
using BrightnessTrayAppDotNET.Utils;

namespace BrightnessTrayAppDotNET.UI.Settings.Environmental;

public sealed partial class EnvironmentalCurveEditor
{
    private bool TryHitThumb(Point pos, Rect plot, out (Series series, EnvironmentalCurvePoint point) hit)
    {
        if (_showNightLight && TryHitThumbInSeries(pos, plot, Series.NightLight, _nightLight, out hit)) return true;
        if (_showBrightness && TryHitThumbInSeries(pos, plot, Series.Brightness, _brightness, out hit)) return true;

        hit = default;
        return false;
    }

    private static bool TryHitThumbInSeries(
        Point pos,
        Rect plot,
        Series series,
        List<EnvironmentalCurvePoint> points,
        out (Series series, EnvironmentalCurvePoint point) hit)
    {
        for (int i = points.Count - 1; i >= 0; i--)
        {
            EnvironmentalCurvePoint point = points[i];
            Point center = new(ScreenX(point.Time, plot), ScreenY(point.Value, plot));
            double radius = ThumbSize / 2.0 + ThumbHitPadding;
            if (Math.Abs(pos.X - center.X) <= radius && Math.Abs(pos.Y - center.Y) <= radius)
            {
                hit = (series, point);
                return true;
            }
        }

        hit = default;
        return false;
    }

    private bool TryHitDisabledPin(Point pos, Rect plot, out DisabledPin hit)
    {
        double pinY = TopInset / 2.0;
        double radius = ThumbSize / 2.0 + ThumbHitPadding;
        double startX = ScreenX(_disabledPeriodStart, plot);
        double endX = ScreenX(_disabledPeriodEnd, plot);

        if (Math.Abs(pos.X - endX) <= radius && Math.Abs(pos.Y - pinY) <= radius)
        {
            hit = DisabledPin.End;
            return true;
        }

        if (Math.Abs(pos.X - startX) <= radius && Math.Abs(pos.Y - pinY) <= radius)
        {
            hit = DisabledPin.Start;
            return true;
        }

        hit = default;
        return false;
    }

    private bool TryHitLimitLine(Point pos, Rect plot, out (Series series, LimitKind kind) hit)
    {
        foreach (LimitLabelHit labelHit in _limitLabelHits)
        {
            if (!labelHit.Rect.Contains(pos)) continue;
            hit = (labelHit.Series, labelHit.Kind);
            return true;
        }

        double best = double.PositiveInfinity;
        (Series series, LimitKind kind) bestHit = default;

        void Consider(Series series, LimitKind kind, double value)
        {
            double dy = Math.Abs(pos.Y - ScreenY(value, plot));
            if (dy <= LimitLineHitTolerance && dy < best)
            {
                best = dy;
                bestHit = (series, kind);
            }
        }

        if (_curveData != null)
        {
            if (_showBrightness)
            {
                Consider(Series.Brightness, LimitKind.Min, _curveData.BrightnessOffsetMin);
                Consider(Series.Brightness, LimitKind.Max, _curveData.BrightnessOffsetMax);
            }

            if (_showNightLight)
            {
                Consider(Series.NightLight, LimitKind.Min, _curveData.NightLightOffsetMin);
                Consider(Series.NightLight, LimitKind.Max, _curveData.NightLightOffsetMax);
            }
        }

        if (double.IsInfinity(best))
        {
            hit = default;
            return false;
        }

        hit = bestHit;
        return true;
    }

    private List<EnvironmentalCurvePoint>? PickClosestVisibleSeries(double t, double clickY, Rect plot)
    {
        if (!_showBrightness && !_showNightLight) return null;
        if (_showBrightness && !_showNightLight) return _brightness;
        if (!_showBrightness && _showNightLight) return _nightLight;

        double brightnessDist = DistanceToCurve(_brightness, t, clickY, plot);
        double nightDist = DistanceToCurve(_nightLight, t, clickY, plot);
        if (double.IsNaN(brightnessDist) && double.IsNaN(nightDist)) return _brightness;
        if (double.IsNaN(brightnessDist)) return _nightLight;
        if (double.IsNaN(nightDist)) return _brightness;
        return brightnessDist <= nightDist ? _brightness : _nightLight;
    }

    private double DistanceToCurve(List<EnvironmentalCurvePoint> series, double t, double clickY, Rect plot)
    {
        double value = SampleCurveAt(series, t);
        return double.IsNaN(value) ? double.NaN : Math.Abs(clickY - ScreenY(value, plot));
    }

    private double SampleCurveAt(List<EnvironmentalCurvePoint> series, double t)
    {
        if (series.Count == 0) return double.NaN;
        return EnvironmentalCurveSampler.Sample(series, t, _smoothness);
    }
}
