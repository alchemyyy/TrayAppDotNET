using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTableLayoutTests
{
    [Theory]
    [InlineData(11.5, 19, 10)]
    [InlineData(23, 38, 20)]
    [InlineData(17.25, 19, 10)]
    [InlineData(11.5, 28.5, 10)]
    [InlineData(8, 14, 6.956521739130435)]
    public void ProcessIconScaleFollowsTheLimitingZoomMetric(
        double fontSize,
        double rowHeight,
        double expectedIconSize)
    {
        double iconSize = ProcessTableLayout.ScaleProcessIconSize(
            baseIconSize: 10,
            baseFontSize: 11.5,
            baseRowHeight: 19,
            fontSize,
            rowHeight);

        Assert.Equal(expectedIconSize, iconSize, precision: 10);
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
