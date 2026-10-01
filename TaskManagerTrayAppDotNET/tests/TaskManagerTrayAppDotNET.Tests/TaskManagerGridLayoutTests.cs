using Avalonia;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerGridLayoutTests
{
    private const double HeaderHeight = 32;
    private const double RowHeight = 26;

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
        int row = TaskManagerGridLayout.HitTestRow(
            y,
            rowCount: 10,
            HeaderHeight,
            RowHeight);

        Assert.Equal(expectedRow, row);
    }

    [Fact]
    public void GetContentHeightIncludesOneHeaderAndAllRows()
    {
        double height = TaskManagerGridLayout.GetContentHeight(
            rowCount: 100,
            HeaderHeight,
            RowHeight);

        Assert.Equal(32 + 100 * 26, height);
    }

    [Fact]
    public void VisibleRangeIncludesOneOverscanRowOnEachSide()
    {
        Rect viewport = new(x: 0, y: 292, width: 800, height: 52);

        TaskManagerGridLayout.GetVisibleRowRange(
            viewport,
            rowCount: 100,
            HeaderHeight,
            RowHeight,
            out int firstRow,
            out int lastRowExclusive);

        Assert.Equal(expected: 9, firstRow);
        Assert.Equal(expected: 13, lastRowExclusive);
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
        Rect viewport = new(x: 0, viewportY, width: 800, height: 52);

        TaskManagerGridLayout.GetRetainedRowRange(
            viewport,
            rowCount: 1000,
            HeaderHeight,
            RowHeight,
            out int firstRow,
            out int lastRowExclusive);

        Assert.Equal(expectedFirstRow, firstRow);
        Assert.Equal(expectedLastRowExclusive, lastRowExclusive);
    }

    [Fact]
    public void InteractiveZoomRangeExcludesSettledPrefetchRows()
    {
        Rect viewport = new(x: 0, y: 13032, width: 800, height: 52);

        TaskManagerGridLayout.GetVisibleRowRange(
            viewport,
            rowCount: 1000,
            HeaderHeight,
            RowHeight,
            out int interactiveFirstRow,
            out int interactiveLastRowExclusive);
        TaskManagerGridLayout.GetRetainedRowRange(
            viewport,
            rowCount: 1000,
            HeaderHeight,
            RowHeight,
            out int settledFirstRow,
            out int settledLastRowExclusive);

        Assert.Equal(expected: 499, interactiveFirstRow);
        Assert.Equal(expected: 503, interactiveLastRowExclusive);
        Assert.Equal(expected: 467, settledFirstRow);
        Assert.Equal(expected: 535, settledLastRowExclusive);
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
        double rowHeight = TaskManagerGridLayout.CalculateRowHeight(
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
        double rowTextHeight = TaskManagerGridLayout.CalculateRowTextHeight(
            fontSize,
            textHeightScale);

        Assert.Equal(expectedTextHeight, rowTextHeight, precision: 10);
    }

    [Theory]
    [InlineData(TaskManagerGridFontWeight.Thin, 100)]
    [InlineData(TaskManagerGridFontWeight.ExtraLight, 200)]
    [InlineData(TaskManagerGridFontWeight.Light, 300)]
    [InlineData(TaskManagerGridFontWeight.SemiLight, 350)]
    [InlineData(TaskManagerGridFontWeight.Normal, 400)]
    [InlineData(TaskManagerGridFontWeight.Medium, 500)]
    [InlineData(TaskManagerGridFontWeight.SemiBold, 600)]
    [InlineData(TaskManagerGridFontWeight.Bold, 700)]
    [InlineData(TaskManagerGridFontWeight.ExtraBold, 800)]
    [InlineData(TaskManagerGridFontWeight.Black, 900)]
    public void ZoomFontWeightUsesConfiguredWeightAtReferenceZoom(
        TaskManagerGridFontWeight baseFontWeight,
        int expectedFontWeight)
    {
        int fontWeight = TaskManagerGridLayout.CalculateZoomFontWeight(
            baseFontWeight,
            AppSettings.GridFontSizeDefault,
            AppSettings.GridFontSizeDefault);

        Assert.Equal(expectedFontWeight, fontWeight);
    }

    [Theory]
    [InlineData(5.75, 158)]
    [InlineData(8, 241)]
    [InlineData(17.25, 610)]
    [InlineData(20, TaskManagerGridLayout.MaximumZoomFontWeight)]
    public void ZoomFontWeightUsesSigmoidResponse(
        double fontSize,
        int expectedFontWeight)
    {
        int fontWeight = TaskManagerGridLayout.CalculateZoomFontWeight(
            TaskManagerGridFontWeight.Normal,
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
    [InlineData(TaskManagerGridFontWeight.Thin)]
    [InlineData(TaskManagerGridFontWeight.ExtraLight)]
    [InlineData(TaskManagerGridFontWeight.Light)]
    [InlineData(TaskManagerGridFontWeight.SemiLight)]
    [InlineData(TaskManagerGridFontWeight.Normal)]
    [InlineData(TaskManagerGridFontWeight.Medium)]
    [InlineData(TaskManagerGridFontWeight.SemiBold)]
    [InlineData(TaskManagerGridFontWeight.Bold)]
    [InlineData(TaskManagerGridFontWeight.ExtraBold)]
    [InlineData(TaskManagerGridFontWeight.Black)]
    public void ZoomFontWeightIsMonotonicAcrossSupportedZoomRange(
        TaskManagerGridFontWeight baseFontWeight)
    {
        int previousFontWeight = TaskManagerGridLayout.CalculateZoomFontWeight(
            baseFontWeight,
            AppSettings.GridFontSizeDefault,
            AppSettings.GridFontSizeMinimum);

        for (double fontSize = AppSettings.GridFontSizeMinimum + 0.5;
             fontSize <= AppSettings.GridFontSizeMaximum;
             fontSize += 0.5)
        {
            int fontWeight = TaskManagerGridLayout.CalculateZoomFontWeight(
                baseFontWeight,
                AppSettings.GridFontSizeDefault,
                fontSize);
            Assert.True(fontWeight >= previousFontWeight);
            previousFontWeight = fontWeight;
        }
    }

    [Theory]
    [InlineData(TaskManagerGridFontWeight.Normal, 2, TaskManagerGridLayout.MinimumZoomFontWeight)]
    [InlineData(TaskManagerGridFontWeight.Normal, 20, TaskManagerGridLayout.MaximumZoomFontWeight)]
    [InlineData(TaskManagerGridFontWeight.Thin, 0.01, TaskManagerGridLayout.MinimumZoomFontWeight)]
    [InlineData(TaskManagerGridFontWeight.Black, 100, (int)TaskManagerGridFontWeight.Black)]
    public void ZoomFontWeightClampsToSupportedRange(
        TaskManagerGridFontWeight baseFontWeight,
        double fontSize,
        int expectedFontWeight)
    {
        int fontWeight = TaskManagerGridLayout.CalculateZoomFontWeight(
            baseFontWeight,
            AppSettings.GridFontSizeDefault,
            fontSize);

        Assert.Equal(expectedFontWeight, fontWeight);
    }

    private static int CalculateNormalFontWeight(double fontSize) =>
        TaskManagerGridLayout.CalculateZoomFontWeight(
            TaskManagerGridFontWeight.Normal,
            AppSettings.GridFontSizeDefault,
            fontSize);
}
