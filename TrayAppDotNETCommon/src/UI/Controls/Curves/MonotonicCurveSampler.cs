namespace TrayAppDotNETCommon.UI.Controls.Curves;

/// <summary>How the end nodes of a monotonic cubic curve get their tangents.</summary>
public enum MonotonicEndTangent
{
    /// <summary>The slope of the first and last segment.</summary>
    Secant,

    /// <summary>
    /// The Fritsch-Carlson three-point one-sided estimate, zeroed when it points against the end segment and capped
    /// at three times that segment's slope. Two-node curves use the secant.
    /// </summary>
    ThreePoint
}

/// <summary>
/// Allocation-free sampler of curves through sorted control nodes: piecewise-linear interpolation, a monotonic cubic
/// Hermite spline (Fritsch-Butland interior tangents), and a blend of the two. Inputs outside the node range clamp to
/// the end values. A runtime evaluator and the editor that draws it use the same calls, so the drawn curve is the one
/// applied.
/// </summary>
public static class MonotonicCurveSampler
{
    /// <summary>Current local time as a [0, 1) day fraction (0 = midnight).</summary>
    public static double CurrentDayFraction(DateTime? now = null)
    {
        double fraction = (now ?? DateTime.Now).TimeOfDay.TotalHours / 24.0;
        if (fraction < 0.0) return 0.0;
        return fraction >= 1.0 ? 0.999999 : fraction;
    }

    /// <summary>
    /// Blends linear (smoothness 0) and monotonic cubic (smoothness 1) interpolation at <paramref name="x"/>.
    /// <paramref name="tangents"/> comes from <see cref="ComputeMonotonicTangents"/> and is unused at smoothness 0.
    /// </summary>
    public static double Sample(
        ReadOnlySpan<double> xs,
        ReadOnlySpan<double> ys,
        ReadOnlySpan<double> tangents,
        double x,
        double smoothness)
    {
        double linear = InterpolateLinear(xs, ys, x);
        double blend = Math.Clamp(smoothness, min: 0.0, max: 1.0);
        if (blend <= 0.0) return linear;

        double cubic = InterpolateMonotonicCubic(xs, ys, tangents, x);
        return linear + (cubic - linear) * blend;
    }

    /// <summary>Piecewise-linear interpolation through sorted nodes; returns 0 without nodes.</summary>
    public static double InterpolateLinear(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, double x)
    {
        int n = xs.Length;
        if (n == 0) return 0.0;
        if (x <= xs[0]) return ys[0];
        if (x >= xs[n - 1]) return ys[n - 1];

        int lo = FindSegment(xs, x);
        double span = xs[lo + 1] - xs[lo];
        if (span <= 0.0) return ys[lo];

        double t = (x - xs[lo]) / span;
        return ys[lo] + t * (ys[lo + 1] - ys[lo]);
    }

    /// <summary>
    /// Writes one tangent per node into <paramref name="tangents"/>: Fritsch-Butland weighted harmonic means inside,
    /// zero at local extrema and flat segments, and <paramref name="endRule"/> at both ends.
    /// </summary>
    public static void ComputeMonotonicTangents(
        ReadOnlySpan<double> xs,
        ReadOnlySpan<double> ys,
        Span<double> tangents,
        MonotonicEndTangent endRule)
    {
        int n = xs.Length;
        if (tangents.Length < n) throw new ArgumentException("One tangent per node is required.", nameof(tangents));
        tangents[..n].Clear();
        if (n < 2) return;

        for (int i = 1; i < n - 1; i++)
        {
            double previous = Slope(xs, ys, i - 1);
            double next = Slope(xs, ys, i);
            if (previous * next <= 0.0) continue;

            double w1 = 2.0 * (xs[i + 1] - xs[i]) + (xs[i] - xs[i - 1]);
            double w2 = xs[i + 1] - xs[i] + 2.0 * (xs[i] - xs[i - 1]);
            tangents[i] = (w1 + w2) / (w1 / previous + w2 / next);
        }

        if (endRule == MonotonicEndTangent.Secant || n == 2)
        {
            tangents[0] = Slope(xs, ys, segment: 0);
            tangents[n - 1] = Slope(xs, ys, n - 2);
            return;
        }

        tangents[0] = ThreePointEndTangent(
            xs[1] - xs[0], xs[2] - xs[1], Slope(xs, ys, segment: 0), Slope(xs, ys, segment: 1));
        tangents[n - 1] = ThreePointEndTangent(
            xs[n - 1] - xs[n - 2], xs[n - 2] - xs[n - 3], Slope(xs, ys, n - 2), Slope(xs, ys, n - 3));
    }

    /// <summary>Cubic Hermite interpolation through sorted nodes with the given tangents; returns 0 without nodes.</summary>
    public static double InterpolateMonotonicCubic(
        ReadOnlySpan<double> xs,
        ReadOnlySpan<double> ys,
        ReadOnlySpan<double> tangents,
        double x)
    {
        int n = xs.Length;
        if (n == 0) return 0.0;
        if (x <= xs[0]) return ys[0];
        if (x >= xs[n - 1]) return ys[n - 1];

        int lo = FindSegment(xs, x);
        double h = xs[lo + 1] - xs[lo];
        if (h <= 0.0) return ys[lo];

        double t = (x - xs[lo]) / h;
        double t2 = t * t;
        double t3 = t2 * t;
        double h00 = 2.0 * t3 - 3.0 * t2 + 1.0;
        double h10 = t3 - 2.0 * t2 + t;
        double h01 = -2.0 * t3 + 3.0 * t2;
        double h11 = t3 - t2;
        return h00 * ys[lo] + h10 * h * tangents[lo] + h01 * ys[lo + 1] + h11 * h * tangents[lo + 1];
    }

    /// <summary>
    /// Returns the first segment whose right node is at or past <paramref name="x"/>, for xs[0] &lt; x &lt; xs[^1].
    /// At a repeated node position this picks the segment ending there, so a vertical step reads its lower node.
    /// </summary>
    private static int FindSegment(ReadOnlySpan<double> xs, double x)
    {
        int lo = 1;
        int hi = xs.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (xs[mid] >= x) hi = mid;
            else lo = mid + 1;
        }

        return lo - 1;
    }

    private static double Slope(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, int segment)
    {
        double dx = xs[segment + 1] - xs[segment];
        return dx > 0.0 ? (ys[segment + 1] - ys[segment]) / dx : 0.0;
    }

    private static double ThreePointEndTangent(double hEnd, double hNext, double mEnd, double mNext)
    {
        double d = ((2.0 * hEnd + hNext) * mEnd - hEnd * mNext) / (hEnd + hNext);
        if (d * mEnd <= 0.0) return 0.0;

        double cap = 3.0 * Math.Abs(mEnd);
        if (Math.Abs(d) > cap) return mEnd >= 0.0 ? cap : -cap;

        return d;
    }
}
