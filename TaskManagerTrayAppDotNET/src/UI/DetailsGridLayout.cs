using Avalonia;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Provides fixed-row geometry shared by Task Manager details grids.</summary>
internal static class DetailsGridLayout
{
    private const int ViewportOverscanRows = 1;
    private const int RetainedDrawingOverscanRows = 32;

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
}
