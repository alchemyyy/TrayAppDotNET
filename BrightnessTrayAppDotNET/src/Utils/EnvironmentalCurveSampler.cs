namespace BrightnessTrayAppDotNET.Utils;

/// <summary>
/// Stateless evaluator for <see cref="EnvironmentalCurve"/> at a given normalised time-of-day.
/// Mirrors the editor's blend (linear &lt;-&gt; monotonic cubic Hermite)
/// so runtime brightness/night-light values land on the same shape the user sees on the canvas.
/// </summary>
public static class EnvironmentalCurveSampler
{
    /// <summary>
    /// Current local time as a [0, 1) day fraction (0 = midnight, 1 = next midnight).
    /// </summary>
    public static double CurrentDayFraction()
    {
        TimeSpan tod = DateTime.Now.TimeOfDay;
        double frac = tod.TotalHours / 24.0;
        if (frac < 0.0) frac = 0.0;
        if (frac >= 1.0) frac = 0.999999;
        return frac;
    }

    /// <summary>
    /// True when <paramref name="t"/> sits inside the curve's disabled window.
    /// Honours the wrap-midnight case (start &gt; end) by treating the union of [start, 1] and [0, end] as disabled.
    /// </summary>
    public static bool IsInDisabledPeriod(EnvironmentalCurve curve, double t)
    {
        if (!curve.DisabledPeriodEnabled) return false;

        double s = curve.DisabledPeriodStart;
        double e = curve.DisabledPeriodEnd;
        if (s == e) return false;

        if (s <= e) return t >= s && t <= e;

        return t >= s || t <= e;
    }

    /// <summary>
    /// Samples a curve point list at <paramref name="t"/> using the editor's linear+cubic blend.
    /// Output is on the same 0..100 scale as <see cref="EnvironmentalCurvePoint.Value"/>; returns 0 for an empty list.
    /// </summary>
    public static double Sample(List<EnvironmentalCurvePoint>? series, double t, double smoothness)
    {
        if (series == null || series.Count == 0) return 0.0;

        List<EnvironmentalCurvePoint> ordered = [.. series.OrderBy(p => p.Time)];
        int n = ordered.Count;
        if (n == 1) return ordered[0].Value;

        double[] timePoints = new double[n];
        double[] valuePoints = new double[n];
        for (int i = 0; i < n; i++)
        {
            timePoints[i] = ordered[i].Time;
            valuePoints[i] = ordered[i].Value;
        }

        double s = Math.Clamp(smoothness, 0.0, 1.0);
        double linear = InterpolateLinear(timePoints, valuePoints, t);
        if (s <= 0.0) return linear;

        double[] tangents = ComputeMonotonicTangents(timePoints, valuePoints);
        double cubic = InterpolateMonotonicCubic(timePoints, valuePoints, tangents, t);
        return linear + (cubic - linear) * s;
    }

    /// <summary>
    /// Piecewise-linear interpolation of (timePoints, valuePoints) at <paramref name="x"/>;
    /// out-of-range inputs clamp to the boundary y.
    /// Internal so the editor's render loop can build the polyline using the same primitive as the runtime evaluator.
    /// </summary>
    internal static double InterpolateLinear(double[] timePoints, double[] valuePoints, double x)
    {
        int n = timePoints.Length;
        if (x <= timePoints[0]) return valuePoints[0];
        if (x >= timePoints[n - 1]) return valuePoints[n - 1];

        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (timePoints[mid] <= x) lo = mid;
            else hi = mid;
        }

        double dx = timePoints[hi] - timePoints[lo];
        double t = dx > 0 ? (x - timePoints[lo]) / dx : 0.0;
        return valuePoints[lo] + t * (valuePoints[hi] - valuePoints[lo]);
    }

    /// <summary>
    /// Per-vertex tangents for piecewise cubic Hermite interpolation (PCHIP),
    /// using Fritsch-Butland for interior points and Fritsch-Carlson three-point one-sided formulas for endpoints.
    /// Tangents are computed once per vertex (not per segment) so the curve is C1 across every control point
    /// and stays monotonic within each segment without needing a per-segment post-clamp.
    /// </summary>
    internal static double[] ComputeMonotonicTangents(double[] timePoints, double[] valuePoints)
    {
        int n = timePoints.Length;
        double[] d = new double[n];
        if (n < 2) return d;

        double[] h = new double[n - 1];
        double[] m = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            h[i] = timePoints[i + 1] - timePoints[i];
            m[i] = h[i] > 0 ? (valuePoints[i + 1] - valuePoints[i]) / h[i] : 0.0;
        }

        if (n == 2)
        {
            d[0] = m[0];
            d[1] = m[0];
            return d;
        }

        for (int i = 1; i < n - 1; i++)
        {
            if (m[i - 1] == 0.0 || m[i] == 0.0 || m[i - 1] * m[i] < 0.0)
            {
                d[i] = 0.0;
                continue;
            }

            double w1 = (2.0 * h[i]) + h[i - 1];
            double w2 = h[i] + (2.0 * h[i - 1]);
            d[i] = (w1 + w2) / ((w1 / m[i - 1]) + (w2 / m[i]));
        }

        d[0] = EndpointTangent(h[0], h[1], m[0], m[1]);
        d[n - 1] = EndpointTangent(h[n - 2], h[n - 3], m[n - 2], m[n - 3]);

        return d;
    }

    private static double EndpointTangent(double hEnd, double hNext, double mEnd, double mNext)
    {
        double d = (((2.0 * hEnd) + hNext) * mEnd - (hEnd * mNext)) / (hEnd + hNext);
        if (d * mEnd <= 0.0) return 0.0;

        double cap = 3.0 * Math.Abs(mEnd);
        if (Math.Abs(d) > cap) return mEnd >= 0.0 ? cap : -cap;

        return d;
    }

    /// <summary>
    /// Cubic Hermite interpolation of (timePoints, valuePoints) at <paramref name="x"/>
    /// using pre-computed <paramref name="tangents"/> from <see cref="ComputeMonotonicTangents"/>;
    /// out-of-range inputs clamp to the boundary y.
    /// Internal so the editor's render loop can reuse the same arrays for many samples
    /// without recomputing tangents per call.
    /// </summary>
    internal static double InterpolateMonotonicCubic(double[] timePoints, double[] valuePoints, double[] tangents,
        double x)
    {
        int n = timePoints.Length;
        if (x <= timePoints[0]) return valuePoints[0];
        if (x >= timePoints[n - 1]) return valuePoints[n - 1];

        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (timePoints[mid] <= x) lo = mid;
            else hi = mid;
        }

        double h = timePoints[hi] - timePoints[lo];
        if (h <= 0) return valuePoints[lo];

        double t = (x - timePoints[lo]) / h;
        double t2 = t * t;
        double t3 = t2 * t;
        double h00 = (2.0 * t3) - (3.0 * t2) + 1.0;
        double h10 = t3 - (2.0 * t2) + t;
        double h01 = (-2.0 * t3) + (3.0 * t2);
        double h11 = t3 - t2;
        return (h00 * valuePoints[lo]) + (h10 * h * tangents[lo]) + (h01 * valuePoints[hi]) + (h11 * h * tangents[hi]);
    }
}
