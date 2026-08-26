using Avalonia;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTableLayoutTests
{
    private static readonly ProcessTableMetrics Metrics = new(
        HeaderHeight: 32,
        RowHeight: 26,
        CellPadding: 7,
        FontSize: 13,
        HeaderFontSize: 13,
        ProcessIconSize: 10,
        ProcessIconGap: 8);

    [Theory]
    [InlineData(0, -1)]
    [InlineData(31.9, -1)]
    [InlineData(32, 0)]
    [InlineData(57.9, 0)]
    [InlineData(58, 1)]
    [InlineData(291.9, 9)]
    [InlineData(292, -1)]
    public void HitTestRowMapsContentCoordinates(double y, int expectedRow)
    {
        int row = ProcessTableLayout.HitTestRow(y, rowCount: 10, Metrics);

        Assert.Equal(expectedRow, row);
    }

    [Fact]
    public void GetContentHeightIncludesOneHeaderAndAllRows()
    {
        double height = ProcessTableLayout.GetContentHeight(100, Metrics);

        Assert.Equal(32 + 100 * 26, height);
    }

    [Fact]
    public void VisibleRangeIncludesOneOverscanRowOnEachSide()
    {
        Rect viewport = new(0, 292, 800, 52);

        ProcessTableLayout.GetVisibleRowRange(
            viewport,
            rowCount: 100,
            Metrics,
            out int firstRow,
            out int lastRowExclusive);

        Assert.Equal(9, firstRow);
        Assert.Equal(13, lastRowExclusive);
    }

    [Theory]
    [InlineData(32, 0, 35)]
    [InlineData(13032, 467, 535)]
    [InlineData(25980, 965, 1000)]
    public void RetainedRangeAddsBoundedPrefetchRows(
        double viewportY,
        int expectedFirstRow,
        int expectedLastRowExclusive)
    {
        Rect viewport = new(0, viewportY, 800, 52);

        ProcessTableLayout.GetRetainedRowRange(
            viewport,
            rowCount: 1000,
            Metrics,
            out int firstRow,
            out int lastRowExclusive);

        Assert.Equal(expectedFirstRow, firstRow);
        Assert.Equal(expectedLastRowExclusive, lastRowExclusive);
    }

    [Fact]
    public void HitTestColumnRejectsUnusedTrailingWidth()
    {
        ProcessTableColumn[] columns =
        [
            new(ProcessTableColumnKind.Name, "Name", 0, 100, ProcessTableColumnAlignment.Left),
            new(ProcessTableColumnKind.ProcessID, "PID", 100, 50, ProcessTableColumnAlignment.Right)
        ];

        Assert.Equal(0, ProcessTableLayout.HitTestColumn(99.9, columns));
        Assert.Equal(1, ProcessTableLayout.HitTestColumn(100, columns));
        Assert.Equal(-1, ProcessTableLayout.HitTestColumn(150, columns));
    }

    [Fact]
    public void HitTestColumnDividerUsesOneSharedBoundaryTarget()
    {
        ProcessTableColumn[] columns = CreateColumns();

        Assert.Equal(0, ProcessTableLayout.HitTestColumnDivider(96, columns, 4));
        Assert.Equal(0, ProcessTableLayout.HitTestColumnDivider(104, columns, 4));
        Assert.Equal(-1, ProcessTableLayout.HitTestColumnDivider(105, columns, 4));
        Assert.Equal(2, ProcessTableLayout.HitTestColumnDivider(228, columns, 4));
    }

    [Fact]
    public void LiveResizeChangesOneWidthAndOffsetsFollowingColumns()
    {
        ProcessTableColumn[] columns = CreateColumns();
        ProcessTableColumn[] resized = new ProcessTableColumn[columns.Length];

        ProcessTableLayout.WriteResizedColumns(columns, 1, 80, resized);

        Assert.Equal(columns[0], resized[0]);
        Assert.Equal(80, resized[1].Width);
        Assert.Equal(100, resized[1].Left);
        Assert.Equal(180, resized[2].Left);
        Assert.Equal(100, columns[1].Left);
        Assert.Equal(150, columns[2].Left);
    }

    [Fact]
    public void ReorderInsertionGeometryExcludesTheDraggedColumn()
    {
        ProcessTableColumn[] columns = CreateColumns();

        Assert.Equal(0, ProcessTableLayout.GetReorderInsertionIndex(0, columns, 1));
        Assert.Equal(1, ProcessTableLayout.GetReorderInsertionIndex(110, columns, 2));
        Assert.Equal(2, ProcessTableLayout.GetReorderInsertionIndex(200, columns, 1));
        Assert.Equal(150, ProcessTableLayout.GetReorderInsertionX(columns, 0, 1));
        Assert.Equal(0, ProcessTableLayout.GetReorderInsertionX(columns, 2, 0));
        Assert.Equal(225, ProcessTableLayout.GetReorderInsertionX(columns, 1, 2));
    }

    private static ProcessTableColumn[] CreateColumns() =>
    [
        new(ProcessTableColumnKind.Name, "Name", 0, 100, ProcessTableColumnAlignment.Left),
        new(ProcessTableColumnKind.ProcessID, "PID", 100, 50, ProcessTableColumnAlignment.Right),
        new(ProcessTableColumnKind.CPU, "CPU", 150, 75, ProcessTableColumnAlignment.Right)
    ];
}
