namespace TrayAppDotNETCommon.UI.Controls.Curves;

/// <summary>
/// Stateless evaluator for normalized time curves.
/// Blends piecewise-linear interpolation with monotonic cubic Hermite interpolation.
/// </summary>
public static class TimeCurveSampler
{
    public static double CurrentDayFraction(DateTime? now = null)
    {
        TimeSpan timeOfDay = (now ?? DateTime.Now).TimeOfDay;
        double fraction = timeOfDay.TotalHours / 24.0;
        if (fraction < 0.0) fraction = 0.0;
        if (fraction >= 1.0) fraction = 0.999999;
        return fraction;
    }

    public static double Sample<TPoint>(
        IEnumerable<TPoint>? points,
        double time,
        double smoothness)
        where TPoint : ITimeCurvePoint
    {
        if (points is null) return 0.0;

        List<TPoint> ordered = [.. points.OrderBy(static p => p.Time)];
        int count = ordered.Count;
        if (count == 0) return 0.0;
        if (count == 1) return ordered[0].Value;

        double[] timePoints = new double[count];
        double[] valuePoints = new double[count];
        for (int i = 0; i < count; i++)
        {
            timePoints[i] = ordered[i].Time;
            valuePoints[i] = ordered[i].Value;
        }

        double blend = Math.Clamp(smoothness, 0.0, 1.0);
        double linear = InterpolateLinear(timePoints, valuePoints, time);
        if (blend <= 0.0) return linear;

        double[] tangents = ComputeMonotonicTangents(timePoints, valuePoints);
        double cubic = InterpolateMonotonicCubic(timePoints, valuePoints, tangents, time);
        return linear + ((cubic - linear) * blend);
    }

    public static double InterpolateLinear(
        IReadOnlyList<double> timePoints,
        IReadOnlyList<double> valuePoints,
        double x)
    {
        int count = Math.Min(timePoints.Count, valuePoints.Count);
        if (count == 0) return 0.0;
        if (count == 1) return valuePoints[0];
        if (x <= timePoints[0]) return valuePoints[0];
        if (x >= timePoints[count - 1]) return valuePoints[count - 1];

        int low = 0;
        int high = count - 1;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (timePoints[middle] <= x) low = middle;
            else high = middle;
        }

        double dx = timePoints[high] - timePoints[low];
        double t = dx > 0.0 ? (x - timePoints[low]) / dx : 0.0;
        return valuePoints[low] + (t * (valuePoints[high] - valuePoints[low]));
    }

    public static double[] ComputeMonotonicTangents(
        IReadOnlyList<double> timePoints,
        IReadOnlyList<double> valuePoints)
    {
        int count = Math.Min(timePoints.Count, valuePoints.Count);
        double[] tangents = new double[count];
        if (count < 2) return tangents;

        double[] intervals = new double[count - 1];
        double[] slopes = new double[count - 1];
        for (int i = 0; i < count - 1; i++)
        {
            intervals[i] = timePoints[i + 1] - timePoints[i];
            slopes[i] = intervals[i] > 0.0
                ? (valuePoints[i + 1] - valuePoints[i]) / intervals[i]
                : 0.0;
        }

        if (count == 2)
        {
            tangents[0] = slopes[0];
            tangents[1] = slopes[0];
            return tangents;
        }

        for (int i = 1; i < count - 1; i++)
        {
            if (slopes[i - 1] == 0.0 ||
                slopes[i] == 0.0 ||
                slopes[i - 1] * slopes[i] < 0.0)
            {
                tangents[i] = 0.0;
                continue;
            }

            double w1 = (2.0 * intervals[i]) + intervals[i - 1];
            double w2 = intervals[i] + (2.0 * intervals[i - 1]);
            tangents[i] = (w1 + w2) / ((w1 / slopes[i - 1]) + (w2 / slopes[i]));
        }

        tangents[0] = EndpointTangent(intervals[0], intervals[1], slopes[0], slopes[1]);
        tangents[count - 1] = EndpointTangent(
            intervals[count - 2],
            intervals[count - 3],
            slopes[count - 2],
            slopes[count - 3]);

        return tangents;
    }

    public static double InterpolateMonotonicCubic(
        IReadOnlyList<double> timePoints,
        IReadOnlyList<double> valuePoints,
        IReadOnlyList<double> tangents,
        double x)
    {
        int count = Math.Min(Math.Min(timePoints.Count, valuePoints.Count), tangents.Count);
        if (count == 0) return 0.0;
        if (count == 1) return valuePoints[0];
        if (x <= timePoints[0]) return valuePoints[0];
        if (x >= timePoints[count - 1]) return valuePoints[count - 1];

        int low = 0;
        int high = count - 1;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (timePoints[middle] <= x) low = middle;
            else high = middle;
        }

        double h = timePoints[high] - timePoints[low];
        if (h <= 0.0) return valuePoints[low];

        double t = (x - timePoints[low]) / h;
        double t2 = t * t;
        double t3 = t2 * t;
        double h00 = (2.0 * t3) - (3.0 * t2) + 1.0;
        double h10 = t3 - (2.0 * t2) + t;
        double h01 = (-2.0 * t3) + (3.0 * t2);
        double h11 = t3 - t2;

        return (h00 * valuePoints[low]) +
               (h10 * h * tangents[low]) +
               (h01 * valuePoints[high]) +
               (h11 * h * tangents[high]);
    }

    private static double EndpointTangent(double hEnd, double hNext, double mEnd, double mNext)
    {
        double tangent = (((2.0 * hEnd) + hNext) * mEnd - (hEnd * mNext)) / (hEnd + hNext);
        if (tangent * mEnd <= 0.0) return 0.0;

        double cap = 3.0 * Math.Abs(mEnd);
        if (Math.Abs(tangent) > cap) return mEnd >= 0.0 ? cap : -cap;

        return tangent;
    }
}
