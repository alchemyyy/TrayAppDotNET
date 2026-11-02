using Avalonia;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>How a graph's hover line follows the pointer.</summary>
internal enum PerformanceGraphHoverLineMode : byte
{
    /// <summary>Snaps to the nearest sample, centers the label on it, and parts around the label.</summary>
    SnapToSample,

    /// <summary>Follows the pointer and runs behind the label.</summary>
    FollowPointer
}

/// <summary>Numeric layout rules for one graph's hover line and metric label.</summary>
/// <param name="LineMode">How the line follows the pointer.</param>
/// <param name="TextInset">Edge inset a snapped label keeps when space permits.</param>
/// <param name="TextCursorGap">Gap between the label's bottom and the pointer.</param>
/// <param name="LineClipPadding">Gap a snapped line leaves around the label's ink.</param>
/// <param name="LineTerminalLength">Length of the solid segment that ends a snapped line above the label.</param>
internal readonly record struct PerformanceGraphHoverMetrics(
    PerformanceGraphHoverLineMode LineMode,
    double TextInset,
    double TextCursorGap,
    double LineClipPadding,
    double LineTerminalLength);

/// <summary>Layout metrics of a hover label shaped on the UI thread.</summary>
internal readonly record struct PerformanceGraphHoverLabelMetrics(
    double Width,
    double Height,
    double Extent,
    double OverhangAfter);

/// <summary>Resolved hover line and label position for one pointer sample.</summary>
/// <param name="LineX">Horizontal position of the vertical line.</param>
/// <param name="Height">Graph height the line spans.</param>
/// <param name="HasLineGap">Whether the line parts around the label instead of spanning the full height.</param>
/// <param name="UpperLineEnd">End of the dashed segment above the label gap.</param>
/// <param name="LineTerminalStart">Start of the solid segment that ends the upper dashed segment.</param>
/// <param name="LowerLineStart">Start of the dashed segment below the label gap.</param>
/// <param name="HasLabel">Whether the label is drawn.</param>
/// <param name="LabelOrigin">Top-left corner of the label's layout box.</param>
internal readonly record struct PerformanceGraphHoverPlacement(
    double LineX,
    double Height,
    bool HasLineGap,
    double UpperLineEnd,
    double LineTerminalStart,
    double LowerLineStart,
    bool HasLabel,
    Point LabelOrigin);

/// <summary>Places a performance graph's hover line and metric label for one pointer position.</summary>
/// <remarks>Runs on the render thread for every sampled cursor position, so it allocates nothing.</remarks>
internal static class PerformanceGraphHoverLayout
{
    /// <summary>
    /// Resolves the hover for a graph-local pointer position. Returns false when the pointer is outside the hover
    /// bounds or the graph has nothing to indicate.
    /// </summary>
    public static bool TryPlace(
        in PerformanceGraphHoverMetrics metrics,
        Size graphSize,
        Rect hoverBounds,
        ReadOnlySpan<double> sampleFractions,
        bool hasIndicator,
        PerformanceGraphHoverLabelMetrics? label,
        Point pointerPosition,
        out PerformanceGraphHoverPlacement placement)
    {
        placement = default;
        double width = graphSize.Width;
        double height = graphSize.Height;
        if (!hasIndicator
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || width <= 0
            || height <= 0
            || !IsInside(hoverBounds, pointerPosition))
            return false;

        bool snapsToSample = metrics.LineMode == PerformanceGraphHoverLineMode.SnapToSample;
        double lineX;
        if (snapsToSample)
        {
            if (!PerformanceHistoryGraphLayout.TryGetHoverSample(
                    sampleFractions,
                    pointerPosition.X,
                    width,
                    out _,
                    out lineX))
                return false;
        }
        else
            lineX = Math.Clamp(pointerPosition.X / width, min: 0, max: 1) * width;

        placement = new PerformanceGraphHoverPlacement(
            lineX,
            height,
            HasLineGap: false,
            UpperLineEnd: height,
            LineTerminalStart: height,
            LowerLineStart: height,
            HasLabel: false,
            LabelOrigin: default);
        if (label is not { } labelMetrics) return true;

        double preferredTextTop = pointerPosition.Y - metrics.TextCursorGap - labelMetrics.Height;
        if (!snapsToSample)
        {
            placement = placement with
            {
                HasLabel = true,
                LabelOrigin = new Point(
                    Math.Clamp(
                        lineX - labelMetrics.Width / 2,
                        min: 0,
                        Math.Max(val1: 0, width - labelMetrics.Width)),
                    Math.Clamp(
                        preferredTextTop,
                        min: 0,
                        Math.Max(val1: 0, height - labelMetrics.Height)))
            };
            return true;
        }

        double textLeft = ClampLabelCoordinate(
            lineX - labelMetrics.Width / 2,
            width,
            labelMetrics.Width,
            metrics.TextInset);
        double textTop = ClampLabelCoordinate(
            preferredTextTop,
            height,
            labelMetrics.Height,
            metrics.TextInset);
        double inkBottom = textTop + labelMetrics.Height + labelMetrics.OverhangAfter;
        double inkTop = inkBottom - labelMetrics.Extent;
        double upperLineEnd = Math.Clamp(inkTop - metrics.LineClipPadding, min: 0, height);
        placement = placement with
        {
            HasLineGap = true,
            UpperLineEnd = upperLineEnd,
            LineTerminalStart = Math.Max(val1: 0, upperLineEnd - metrics.LineTerminalLength),
            LowerLineStart = Math.Clamp(inkBottom + metrics.LineClipPadding, min: 0, height),
            HasLabel = true,
            LabelOrigin = new Point(textLeft, textTop)
        };
        return true;
    }

    /// <summary>Clamps one label coordinate while preserving an edge inset when space permits.</summary>
    private static double ClampLabelCoordinate(
        double preferredCoordinate,
        double availableLength,
        double labelLength,
        double inset)
    {
        double maximumCoordinate = Math.Max(val1: 0, availableLength - labelLength);
        double edgeInset = Math.Min(inset, maximumCoordinate / 2);
        return Math.Clamp(
            preferredCoordinate,
            edgeInset,
            maximumCoordinate - edgeInset);
    }

    private static bool IsInside(Rect bounds, Point point) =>
        point.X >= bounds.X
        && point.X < bounds.Right
        && point.Y >= bounds.Y
        && point.Y < bounds.Bottom;
}
