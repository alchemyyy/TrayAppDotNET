using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Provides fixed-row geometry and zoom typography shared by Task Manager grids.</summary>
internal static class TaskManagerGridLayout
{
    private const int ViewportOverscanRows = 1;
    private const int RetainedDrawingOverscanRows = 32;
    // Large size so neighboring faces cannot round to the same layout width
    private const double FaceFingerprintFontSize = 100;
    private const string FaceFingerprintText = "Service Host: Local System 0123456789";

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
    /// Resolves font weight for a zoomed font size.
    /// The configured weight holds at the reference size, changes by
    /// <paramref name="fontWeightPerZoomDoubling"/> per doubling of size near it, and eases toward
    /// the minimum or maximum weight.
    /// </summary>
    public static int CalculateZoomFontWeight(
        TaskManagerGridFontWeight baseFontWeight,
        double referenceFontSize,
        double fontSize,
        int minimumFontWeight,
        int maximumFontWeight,
        double fontWeightPerZoomDoubling)
    {
        if (!Enum.IsDefined(baseFontWeight))
            throw new ArgumentOutOfRangeException(nameof(baseFontWeight));
        if (!double.IsFinite(referenceFontSize) || referenceFontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(referenceFontSize));
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (minimumFontWeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumFontWeight));
        if (maximumFontWeight < minimumFontWeight)
            throw new ArgumentOutOfRangeException(nameof(maximumFontWeight));
        if (!double.IsFinite(fontWeightPerZoomDoubling) || fontWeightPerZoomDoubling < 0)
            throw new ArgumentOutOfRangeException(nameof(fontWeightPerZoomDoubling));

        int baseFontWeightValue = (int)baseFontWeight;
        // Log scale so zooming in and out by the same factor changes weight at the same rate
        double zoomDoublings = Math.Log2(fontSize / referenceFontSize);
        // A configured weight outside the range widens that bound so the reference weight holds
        int fontWeightLimit = zoomDoublings >= 0
            ? Math.Max(maximumFontWeight, baseFontWeightValue)
            : Math.Min(minimumFontWeight, baseFontWeightValue);
        double headroom = fontWeightLimit - baseFontWeightValue;
        if (headroom == 0) return baseFontWeightValue;

        // tanh has slope 1 at the origin and approaches 1 asymptotically, so the reference rate
        // holds near the configured weight and the curve never crosses the limit
        double fontWeight = baseFontWeightValue
                            + headroom
                            * Math.Tanh(fontWeightPerZoomDoubling * zoomDoublings / headroom);
        return (int)Math.Round(fontWeight, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Snaps a zoomed weight to a face the font has, rounding away from the configured weight.
    /// Requested weights between faces would otherwise resolve to the configured face, so a
    /// lighter request could never render lighter.
    /// </summary>
    /// <param name="availableFontWeights">Face weights sorted lightest first</param>
    public static int SnapZoomFontWeight(
        int fontWeight,
        TaskManagerGridFontWeight baseFontWeight,
        IReadOnlyList<int> availableFontWeights)
    {
        ArgumentNullException.ThrowIfNull(availableFontWeights);

        int baseFontWeightValue = (int)baseFontWeight;
        if (fontWeight == baseFontWeightValue || availableFontWeights.Count == 0) return fontWeight;

        if (fontWeight < baseFontWeightValue)
        {
            for (int index = availableFontWeights.Count - 1; index >= 0; index--)
            {
                if (availableFontWeights[index] <= fontWeight) return availableFontWeights[index];
            }

            return availableFontWeights[0];
        }

        foreach (int availableFontWeight in availableFontWeights)
        {
            if (availableFontWeight >= fontWeight) return availableFontWeight;
        }

        return availableFontWeights[^1];
    }

    /// <summary>
    /// Returns one requested weight per distinct face a family resolves to, lightest first.
    /// Each value resolves back to its face, which is all snapping needs.
    /// </summary>
    public static int[] ResolveAvailableFontWeights(FontFamily fontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);

        // Reported face weights are unreliable (variable font instances report 400), so faces are
        // told apart by rendered width; weight-invariant widths in a variable font defeat this
        Dictionary<double, int> fontWeightByWidth = [];
        foreach (FontWeight requestedFontWeight in Enum.GetValues<FontWeight>().Distinct().Order())
        {
            Typeface typeface = new(fontFamily, FontStyle.Normal, requestedFontWeight);
            using TextLayout measurement = new(
                FaceFingerprintText,
                typeface,
                FaceFingerprintFontSize,
                Brushes.White);
            fontWeightByWidth.TryAdd(measurement.Width, (int)requestedFontWeight);
        }

        return [.. fontWeightByWidth.Values.Order()];
    }
}
