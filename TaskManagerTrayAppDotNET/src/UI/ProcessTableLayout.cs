using Avalonia;

namespace TaskManagerTrayAppDotNET.UI;

internal enum ProcessTableColumnKind : byte
{
    Name,
    ProcessID,
    Status,
    UserName,
    CPU,
    PrivateMemory,
    WorkingSet,
    CommandLine
}

internal enum ProcessTableColumnAlignment : byte
{
    Left,
    Right
}

internal readonly record struct ProcessTableMetrics(
    double HeaderHeight,
    double RowHeight,
    double CellPadding,
    double FontSize,
    double HeaderFontSize,
    double ProcessIconSize,
    double ProcessIconGap);

internal readonly record struct ProcessTableColumn(
    ProcessTableColumnKind Kind,
    string Title,
    double Left,
    double Width,
    ProcessTableColumnAlignment Alignment)
{
    public double Right => Left + Width;
}

/// <summary>Pure fixed-row geometry for painting, hit-testing, and viewport culling.</summary>
internal static class ProcessTableLayout
{
    private const int ViewportOverscanRows = 1;

    public static double GetContentHeight(int rowCount, ProcessTableMetrics metrics) =>
        metrics.HeaderHeight + Math.Max(0, rowCount) * metrics.RowHeight;

    public static int HitTestRow(double y, int rowCount, ProcessTableMetrics metrics)
    {
        if (rowCount <= 0 || y < metrics.HeaderHeight) return -1;

        int rowIndex = (int)Math.Floor((y - metrics.HeaderHeight) / metrics.RowHeight);
        return rowIndex >= 0 && rowIndex < rowCount ? rowIndex : -1;
    }

    public static int HitTestColumn(double x, ProcessTableColumn[] columns)
    {
        if (x < 0) return -1;

        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            ProcessTableColumn column = columns[columnIndex];
            if (x >= column.Left && x < column.Right) return columnIndex;
        }

        return -1;
    }

    public static void GetVisibleRowRange(
        Rect viewport,
        int rowCount,
        ProcessTableMetrics metrics,
        out int firstRow,
        out int lastRowExclusive)
    {
        if (rowCount <= 0 || viewport.Height <= 0)
        {
            firstRow = 0;
            lastRowExclusive = 0;
            return;
        }

        double firstRowPosition = Math.Max(0, viewport.Y - metrics.HeaderHeight);
        double lastRowPosition = Math.Max(0, viewport.Bottom - metrics.HeaderHeight);
        int unclampedFirst = (int)Math.Floor(firstRowPosition / metrics.RowHeight) - ViewportOverscanRows;
        int unclampedLast = (int)Math.Ceiling(lastRowPosition / metrics.RowHeight) + ViewportOverscanRows;
        firstRow = Math.Clamp(unclampedFirst, 0, rowCount);
        lastRowExclusive = Math.Clamp(unclampedLast, firstRow, rowCount);
    }
}
