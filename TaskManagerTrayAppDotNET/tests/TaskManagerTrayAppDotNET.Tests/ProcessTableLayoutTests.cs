using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTableLayoutTests
{
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
            referenceFontSize: AppSettings.GridFontSizeDefault,
            fontSize: AppSettings.GridFontSizeDefault);

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
            referenceFontSize: AppSettings.GridFontSizeDefault,
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
            referenceFontSize: AppSettings.GridFontSizeDefault,
            fontSize: AppSettings.GridFontSizeMinimum);

        for (double fontSize = AppSettings.GridFontSizeMinimum + 0.5;
             fontSize <= AppSettings.GridFontSizeMaximum;
             fontSize += 0.5)
        {
            int fontWeight = ProcessTableLayout.CalculateZoomFontWeight(
                baseFontWeight,
                referenceFontSize: AppSettings.GridFontSizeDefault,
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
            referenceFontSize: AppSettings.GridFontSizeDefault,
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

    private static int CalculateNormalFontWeight(double fontSize) =>
        ProcessTableLayout.CalculateZoomFontWeight(
            DetailsGridFontWeight.Normal,
            AppSettings.GridFontSizeDefault,
            fontSize);
}
