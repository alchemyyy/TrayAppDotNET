using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTableLayoutTests
{
    [Theory]
    [InlineData(ProcessTableColumnKind.Name, false)]
    [InlineData(ProcessTableColumnKind.CommandLine, false)]
    [InlineData(ProcessTableColumnKind.ProcessID, true)]
    [InlineData(ProcessTableColumnKind.Disk, true)]
    [InlineData(ProcessTableColumnKind.Network, true)]
    [InlineData(ProcessTableColumnKind.CPU, true)]
    [InlineData(ProcessTableColumnKind.PrivateMemory, true)]
    public void DefaultSortDirectionFollowsColumnAlignment(
        ProcessTableColumnKind column,
        bool expectedDescending)
    {
        Assert.Equal(
            expectedDescending,
            ProcessTableColumnCatalog.SortsDescendingByDefault(column));
    }

    [Theory]
    [InlineData(43.2, 0, 43.2)]
    [InlineData(10.8, 0, 10.8)]
    [InlineData(10.8, 3.5, 14.3)]
    [InlineData(10.8, -20, 10.8)]
    public void RowHeightUsesRenderedTextHeightAndVisibleSpacing(
        double rowTextHeight,
        double rowSpacing,
        double expectedRowHeight)
    {
        double rowHeight = ProcessTableLayout.CalculateRowHeight(
            rowTextHeight,
            rowSpacing);

        Assert.Equal(expectedRowHeight, rowHeight, precision: 10);
    }

    [Theory]
    [InlineData(8, 1.35, 10.8)]
    [InlineData(32, 1.35, 43.2)]
    public void RowTextHeightScalesFromOneMeasuredFontRatio(
        double fontSize,
        double textHeightScale,
        double expectedTextHeight)
    {
        double rowTextHeight = ProcessTableLayout.CalculateRowTextHeight(
            fontSize,
            textHeightScale);

        Assert.Equal(expectedTextHeight, rowTextHeight, precision: 10);
    }

    [Theory]
    [InlineData(DetailsGridFontWeight.Thin, 100)]
    [InlineData(DetailsGridFontWeight.ExtraLight, 200)]
    [InlineData(DetailsGridFontWeight.Light, 300)]
    [InlineData(DetailsGridFontWeight.SemiLight, 350)]
    [InlineData(DetailsGridFontWeight.Normal, 400)]
    [InlineData(DetailsGridFontWeight.Medium, 500)]
    [InlineData(DetailsGridFontWeight.SemiBold, 600)]
    [InlineData(DetailsGridFontWeight.Bold, 700)]
    [InlineData(DetailsGridFontWeight.ExtraBold, 800)]
    [InlineData(DetailsGridFontWeight.Black, 900)]
    public void ZoomFontWeightUsesConfiguredWeightAtReferenceZoom(
        DetailsGridFontWeight baseFontWeight,
        int expectedFontWeight)
    {
        int fontWeight = ProcessTableLayout.CalculateZoomFontWeight(
            baseFontWeight,
            AppSettings.GridFontSizeDefault,
            AppSettings.GridFontSizeDefault);

        Assert.Equal(expectedFontWeight, fontWeight);
    }

    [Theory]
    [InlineData(5.75, 158)]
    [InlineData(8, 241)]
    [InlineData(17.25, 610)]
    [InlineData(20, ProcessTableLayout.MaximumZoomFontWeight)]
    public void ZoomFontWeightUsesSigmoidResponse(
        double fontSize,
        int expectedFontWeight)
    {
        int fontWeight = ProcessTableLayout.CalculateZoomFontWeight(
            DetailsGridFontWeight.Normal,
            AppSettings.GridFontSizeDefault,
            fontSize);

        Assert.Equal(expectedFontWeight, fontWeight);
    }

    [Fact]
    public void ZoomFontWeightSigmoidFlattensTowardBothClamps()
    {
        int lowerIncrement = CalculateNormalFontWeight(6) - CalculateNormalFontWeight(5);
        int middleIncrement = CalculateNormalFontWeight(13) - CalculateNormalFontWeight(12);
        int upperIncrement = CalculateNormalFontWeight(21) - CalculateNormalFontWeight(20);

        Assert.True(lowerIncrement < middleIncrement);
        Assert.True(upperIncrement < middleIncrement);
    }

    [Theory]
    [InlineData(DetailsGridFontWeight.Thin)]
    [InlineData(DetailsGridFontWeight.ExtraLight)]
    [InlineData(DetailsGridFontWeight.Light)]
    [InlineData(DetailsGridFontWeight.SemiLight)]
    [InlineData(DetailsGridFontWeight.Normal)]
    [InlineData(DetailsGridFontWeight.Medium)]
    [InlineData(DetailsGridFontWeight.SemiBold)]
    [InlineData(DetailsGridFontWeight.Bold)]
    [InlineData(DetailsGridFontWeight.ExtraBold)]
    [InlineData(DetailsGridFontWeight.Black)]
    public void ZoomFontWeightIsMonotonicAcrossSupportedZoomRange(
        DetailsGridFontWeight baseFontWeight)
    {
        int previousFontWeight = ProcessTableLayout.CalculateZoomFontWeight(
            baseFontWeight,
            AppSettings.GridFontSizeDefault,
            AppSettings.GridFontSizeMinimum);

        for (double fontSize = AppSettings.GridFontSizeMinimum + 0.5;
             fontSize <= AppSettings.GridFontSizeMaximum;
             fontSize += 0.5)
        {
            int fontWeight = ProcessTableLayout.CalculateZoomFontWeight(
                baseFontWeight,
                AppSettings.GridFontSizeDefault,
                fontSize);
            Assert.True(fontWeight >= previousFontWeight);
            previousFontWeight = fontWeight;
        }
    }

    [Theory]
    [InlineData(DetailsGridFontWeight.Normal, 2, ProcessTableLayout.MinimumZoomFontWeight)]
    [InlineData(DetailsGridFontWeight.Normal, 20, ProcessTableLayout.MaximumZoomFontWeight)]
    [InlineData(DetailsGridFontWeight.Thin, 0.01, ProcessTableLayout.MinimumZoomFontWeight)]
    [InlineData(DetailsGridFontWeight.Black, 100, (int)DetailsGridFontWeight.Black)]
    public void ZoomFontWeightClampsToSupportedRange(
        DetailsGridFontWeight baseFontWeight,
        double fontSize,
        int expectedFontWeight)
    {
        int fontWeight = ProcessTableLayout.CalculateZoomFontWeight(
            baseFontWeight,
            AppSettings.GridFontSizeDefault,
            fontSize);

        Assert.Equal(expectedFontWeight, fontWeight);
    }

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

    private static int CalculateNormalFontWeight(double fontSize) =>
        ProcessTableLayout.CalculateZoomFontWeight(
            DetailsGridFontWeight.Normal,
            AppSettings.GridFontSizeDefault,
            fontSize);
}
