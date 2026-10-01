using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI.ProcessesPage;
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
            new(ProcessTableColumnKind.Name, Title: "Name", Left: 0, Width: 100, ProcessTableColumnAlignment.Left),
            new(ProcessTableColumnKind.ProcessID, Title: "PID", Left: 100, Width: 50, ProcessTableColumnAlignment.Right)
        ];

        Assert.Equal(expected: 0, ProcessTableLayout.HitTestColumn(x: 99.9, columns));
        Assert.Equal(expected: 1, ProcessTableLayout.HitTestColumn(x: 100, columns));
        Assert.Equal(expected: -1, ProcessTableLayout.HitTestColumn(x: 150, columns));
    }

    [Fact]
    public void HitTestColumnDividerUsesOneSharedBoundaryTarget()
    {
        ProcessTableColumn[] columns = CreateColumns();

        Assert.Equal(expected: 0, ProcessTableLayout.HitTestColumnDivider(x: 96, columns, hitRadius: 4));
        Assert.Equal(expected: 0, ProcessTableLayout.HitTestColumnDivider(x: 104, columns, hitRadius: 4));
        Assert.Equal(expected: -1, ProcessTableLayout.HitTestColumnDivider(x: 105, columns, hitRadius: 4));
        Assert.Equal(expected: 2, ProcessTableLayout.HitTestColumnDivider(x: 228, columns, hitRadius: 4));
    }

    [Fact]
    public void LiveResizeChangesOneWidthAndOffsetsFollowingColumns()
    {
        ProcessTableColumn[] columns = CreateColumns();
        ProcessTableColumn[] resized = new ProcessTableColumn[columns.Length];

        ProcessTableLayout.WriteResizedColumns(columns, resizedColumnIndex: 1, width: 80, resized);

        Assert.Equal(columns[0], resized[0]);
        Assert.Equal(expected: 80, resized[1].Width);
        Assert.Equal(expected: 100, resized[1].Left);
        Assert.Equal(expected: 180, resized[2].Left);
        Assert.Equal(expected: 100, columns[1].Left);
        Assert.Equal(expected: 150, columns[2].Left);
    }

    [Fact]
    public void ReorderInsertionGeometryExcludesTheDraggedColumn()
    {
        ProcessTableColumn[] columns = CreateColumns();

        Assert.Equal(expected: 0, ProcessTableLayout.GetReorderInsertionIndex(x: 0, columns, sourceColumnIndex: 1));
        Assert.Equal(expected: 1, ProcessTableLayout.GetReorderInsertionIndex(x: 110, columns, sourceColumnIndex: 2));
        Assert.Equal(expected: 2, ProcessTableLayout.GetReorderInsertionIndex(x: 200, columns, sourceColumnIndex: 1));
        Assert.Equal(expected: 150,
            ProcessTableLayout.GetReorderInsertionX(columns, sourceColumnIndex: 0, insertionIndex: 1));
        Assert.Equal(expected: 0,
            ProcessTableLayout.GetReorderInsertionX(columns, sourceColumnIndex: 2, insertionIndex: 0));
        Assert.Equal(expected: 225,
            ProcessTableLayout.GetReorderInsertionX(columns, sourceColumnIndex: 1, insertionIndex: 2));
    }

    private static ProcessTableColumn[] CreateColumns() =>
    [
        new(ProcessTableColumnKind.Name, Title: "Name", Left: 0, Width: 100, ProcessTableColumnAlignment.Left),
        new(ProcessTableColumnKind.ProcessID, Title: "PID", Left: 100, Width: 50, ProcessTableColumnAlignment.Right),
        new(ProcessTableColumnKind.CPU, Title: "CPU", Left: 150, Width: 75, ProcessTableColumnAlignment.Right)
    ];
}
