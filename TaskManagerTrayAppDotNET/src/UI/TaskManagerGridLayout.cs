using Avalonia;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Provides fixed-row geometry and zoom typography shared by Task Manager grids.</summary>
internal static class TaskManagerGridLayout
{
    public const int MinimumZoomFontWeight = 100;
    public const int MaximumZoomFontWeight = 630;

    private const int ViewportOverscanRows = 1;
    private const int RetainedDrawingOverscanRows = 32;
    private const int ReferenceMaximumZoomFontWeight = 900;
    private const double ReferenceZoomFontWeightLinearCoefficient = 320;
    private const double ReferenceZoomFontWeightCubicCoefficient = 80;
    private const double ZoomFontWeightClampDistanceScale = 0.75;

    public static double GetContentHeight(
        int rowCount,
        double headerHeight,
        double rowHeight) =>
        headerHeight + Math.Max(val1: 0, rowCount) * rowHeight;

    public static int HitTestRow(
        double y,
        int rowCount,
        double headerHeight,
        double rowHeight)
    {
        if (rowCount <= 0 || y < headerHeight) return -1;

        int rowIndex = (int)Math.Floor((y - headerHeight) / rowHeight);
        return rowIndex >= 0 && rowIndex < rowCount ? rowIndex : -1;
    }

    public static void GetVisibleRowRange(
        Rect viewport,
        int rowCount,
        double headerHeight,
        double rowHeight,
        out int firstRow,
        out int lastRowExclusive)
    {
        if (rowCount <= 0 || viewport.Height <= 0)
        {
            firstRow = 0;
            lastRowExclusive = 0;
            return;
        }

        double firstRowPosition = Math.Max(val1: 0, viewport.Y - headerHeight);
        double lastRowPosition = Math.Max(val1: 0, viewport.Bottom - headerHeight);
        int unclampedFirst = (int)Math.Floor(firstRowPosition / rowHeight) - ViewportOverscanRows;
        int unclampedLast = (int)Math.Ceiling(lastRowPosition / rowHeight) + ViewportOverscanRows;
        firstRow = Math.Clamp(unclampedFirst, min: 0, rowCount);
        lastRowExclusive = Math.Clamp(unclampedLast, firstRow, rowCount);
    }

    /// <summary>Returns visible rows plus a bounded retained-drawing prefetch margin.</summary>
    public static void GetRetainedRowRange(
        Rect viewport,
        int rowCount,
        double headerHeight,
        double rowHeight,
        out int firstRow,
        out int lastRowExclusive)
    {
        GetVisibleRowRange(
            viewport,
            rowCount,
            headerHeight,
            rowHeight,
            out int firstVisibleRow,
            out int lastVisibleRowExclusive);
        if (firstVisibleRow >= lastVisibleRowExclusive)
        {
            firstRow = firstVisibleRow;
            lastRowExclusive = lastVisibleRowExclusive;
            return;
        }

        firstRow = Math.Max(val1: 0, firstVisibleRow - RetainedDrawingOverscanRows);
        lastRowExclusive = Math.Min(
            rowCount,
            lastVisibleRowExclusive + RetainedDrawingOverscanRows);
    }

    /// <summary>Calculates one rendered line height from its measured font-size ratio.</summary>
    public static double CalculateRowTextHeight(double fontSize, double textHeightScale)
    {
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (!double.IsFinite(textHeightScale) || textHeightScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(textHeightScale));

        return fontSize * textHeightScale;
    }

    /// <summary>Builds row height from rendered text height plus the requested visible gap.</summary>
    public static double CalculateRowHeight(double rowTextHeight, double rowSpacing)
    {
        if (!double.IsFinite(rowTextHeight) || rowTextHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowTextHeight));
        if (!double.IsFinite(rowSpacing))
            throw new ArgumentOutOfRangeException(nameof(rowSpacing));

        return rowTextHeight + Math.Max(val1: 0, rowSpacing);
    }

    /// <summary>
    /// Resolves font weight with a monotonic smoothstep sigmoid.
    /// The configured weight remains fixed at default zoom while the reduced output range is
    /// stretched across the previous clamp span.
    /// </summary>
    public static int CalculateZoomFontWeight(
        TaskManagerGridFontWeight baseFontWeight,
        double referenceFontSize,
        double fontSize)
    {
        if (!Enum.IsDefined(baseFontWeight))
            throw new ArgumentOutOfRangeException(nameof(baseFontWeight));
        if (!double.IsFinite(referenceFontSize) || referenceFontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(referenceFontSize));
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        int baseFontWeightValue = (int)baseFontWeight;
        int effectiveMaximumFontWeight = Math.Max(
            MaximumZoomFontWeight,
            baseFontWeightValue);
        double normalizedZoom = fontSize / referenceFontSize - 1;
        double referenceLowerClampZoom = SolveReferenceZoom(
            MinimumZoomFontWeight - baseFontWeightValue);
        double referenceUpperClampZoom = SolveReferenceZoom(
            ReferenceMaximumZoomFontWeight - baseFontWeightValue);
        double clampZoomSpan = ZoomFontWeightClampDistanceScale
                               * (referenceUpperClampZoom - referenceLowerClampZoom);
        double baseWeightPosition = (double)(baseFontWeightValue - MinimumZoomFontWeight)
                                    / (effectiveMaximumFontWeight - MinimumZoomFontWeight);
        double baseSigmoidPosition = InverseSmoothstep(baseWeightPosition);
        double lowerClampZoom = -baseSigmoidPosition * clampZoomSpan;
        double upperClampZoom = lowerClampZoom + clampZoomSpan;
        if (normalizedZoom <= lowerClampZoom) return MinimumZoomFontWeight;
        if (normalizedZoom >= upperClampZoom) return effectiveMaximumFontWeight;

        double sigmoidPosition = (normalizedZoom - lowerClampZoom) / clampZoomSpan;
        double weightPosition = Smoothstep(sigmoidPosition);
        double fontWeight = MinimumZoomFontWeight
                            + weightPosition
                            * (effectiveMaximumFontWeight - MinimumZoomFontWeight);
        return (int)Math.Round(fontWeight, MidpointRounding.AwayFromZero);
    }

    private static double InverseSmoothstep(double position)
    {
        if (position <= 0) return 0;
        if (position >= 1) return 1;

        return 0.5 - Math.Sin(Math.Asin(1 - 2 * position) / 3);
    }

    private static double Smoothstep(double position) =>
        position * position * (3 - 2 * position);

    private static double SolveReferenceZoom(double weightOffset)
    {
        // Retain the previous cubic response only as calibration for the clamp positions
        double halfOffset = weightOffset / (2 * ReferenceZoomFontWeightCubicCoefficient);
        double linearRatio = ReferenceZoomFontWeightLinearCoefficient
                             / (3 * ReferenceZoomFontWeightCubicCoefficient);
        double discriminantRoot = Math.Sqrt(
            halfOffset * halfOffset + linearRatio * linearRatio * linearRatio);
        return Math.Cbrt(halfOffset + discriminantRoot)
               + Math.Cbrt(halfOffset - discriminantRoot);
    }
}
