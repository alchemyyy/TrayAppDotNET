using TrayAppDotNETCommon.UI.Controls.Curves;

namespace BrightnessTrayAppDotNET.Utils;

/// <summary>
/// Evaluates an <see cref="EnvironmentalCurve"/> at a normalised time of day through the Common
/// <see cref="MonotonicCurveSampler"/> with three-point end tangents, the same calls the curve editor draws with.
/// </summary>
public static class EnvironmentalCurveSampler
{
    /// <summary>The end-tangent rule of every environmental curve, runtime and editor alike.</summary>
    public const MonotonicEndTangent EndTangent = MonotonicEndTangent.ThreePoint;

    /// <summary>
    /// True when <paramref name="t"/> sits inside the curve's disabled window, including a window that wraps
    /// midnight.
    /// </summary>
    public static bool IsInDisabledPeriod(EnvironmentalCurve curve, double t) =>
        curve.DisabledPeriodEnabled
        && new NormalizedTimeRange(curve.DisabledPeriodStart, curve.DisabledPeriodEnd).Contains(t);

    /// <summary>
    /// Samples a curve point list at <paramref name="t"/> with the linear and cubic blend. Output is on the same
    /// 0..100 scale as <see cref="EnvironmentalCurvePoint.Value"/>; returns 0 for an empty list.
    /// </summary>
    public static double Sample(List<EnvironmentalCurvePoint>? series, double t, double smoothness)
    {
        if (series == null || series.Count == 0) return 0.0;
        if (series.Count == 1) return series[0].Value;

        int n = series.Count;
        double[] timePoints = new double[n];
        double[] valuePoints = new double[n];
        List<EnvironmentalCurvePoint> ordered = [.. series.OrderBy(static p => p.Time)];
        for (int i = 0; i < n; i++)
        {
            timePoints[i] = ordered[i].Time;
            valuePoints[i] = ordered[i].Value;
        }

        double[] tangents = new double[n];
        MonotonicCurveSampler.ComputeMonotonicTangents(timePoints, valuePoints, tangents, EndTangent);
        return MonotonicCurveSampler.Sample(timePoints, valuePoints, tangents, t, smoothness);
    }
}
