using Avalonia;

namespace TrayAppDotNETCommon.UI.Controls.Curves;

public readonly record struct TimeCurvePlotGeometry(
    double Width,
    double Height,
    double InsetX,
    double InsetTop,
    double InsetBottom,
    TimeCurveValueAxis Axis)
{
    public double PlotWidth => Math.Max(1.0, Width - (2.0 * InsetX));
    public double PlotHeight => Math.Max(1.0, Height - InsetTop - InsetBottom);

    public double ScreenX(double time) =>
        InsetX + (Math.Clamp(time, 0.0, 1.0) * PlotWidth);

    public double ScreenY(double storageValue)
    {
        double display = Axis.ToDisplay(storageValue);
        double normalized = Math.Clamp(
            (display - Axis.DisplayMinimum) / Axis.DisplayRange,
            0.0,
            1.0);
        return InsetTop + ((1.0 - normalized) * PlotHeight);
    }

    public double TimeFromScreenX(double x) =>
        Math.Clamp((x - InsetX) / PlotWidth, 0.0, 1.0);

    public double StorageValueFromScreenY(double y)
    {
        double normalized = 1.0 - Math.Clamp((y - InsetTop) / PlotHeight, 0.0, 1.0);
        double display = Axis.DisplayMinimum + (normalized * Axis.DisplayRange);
        return Math.Clamp(Axis.ToStorage(display), Axis.StorageMinimum, Axis.StorageMaximum);
    }

    public Point ToPoint(double time, double storageValue) => new(ScreenX(time), ScreenY(storageValue));
}

public static class TimeCurveEditorMath
{
    public static bool IsEndpoint<TPoint>(TPoint point, IEnumerable<TPoint> points)
        where TPoint : ITimeCurvePoint
    {
        foreach (TPoint candidate in points)
        {
            if (!ReferenceEquals(candidate, point)) continue;
            return point.Time is <= 0.0 or >= 1.0;
        }

        return false;
    }

    public static void SyncEdgeValue<TPoint>(TPoint point, IList<TPoint> points)
        where TPoint : class, ITimeCurvePoint
    {
        if (point.Time != 0.0 && point.Time != 1.0) return;

        double edgeTime = point.Time;
        foreach (TPoint candidate in points)
        {
            if (ReferenceEquals(candidate, point)) continue;
            if (candidate.Time == edgeTime)
                candidate.Value = point.Value;
        }
    }

    public static void ClampInteriorTime<TPoint>(TPoint point, IList<TPoint> orderedPoints, double minimumGap)
        where TPoint : ITimeCurvePoint
    {
        int index = orderedPoints.IndexOf(point);
        if (index < 0 || point.Time is <= 0.0 or >= 1.0) return;

        double next = Math.Clamp(point.Time, 0.0, 1.0);
        if (index > 0) next = Math.Max(next, orderedPoints[index - 1].Time + minimumGap);
        if (index < orderedPoints.Count - 1) next = Math.Min(next, orderedPoints[index + 1].Time - minimumGap);
        point.Time = Math.Clamp(next, 0.0, 1.0);
    }

    public static TPoint? PickNeighbourAfterRemoval<TPoint>(
        IReadOnlyList<TPoint> points,
        double removedTime)
        where TPoint : class, ITimeCurvePoint
    {
        if (points.Count == 0) return null;

        TPoint? best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (TPoint point in points)
        {
            double distance = Math.Abs(point.Time - removedTime);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = point;
        }

        return best;
    }
}
