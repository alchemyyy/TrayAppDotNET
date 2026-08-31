using Avalonia;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class DetailsGridLayoutTests
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
        int row = DetailsGridLayout.HitTestRow(
            y,
            rowCount: 10,
            HeaderHeight,
            RowHeight);

        Assert.Equal(expectedRow, row);
    }

    [Fact]
    public void GetContentHeightIncludesOneHeaderAndAllRows()
    {
        double height = DetailsGridLayout.GetContentHeight(
            rowCount: 100,
            HeaderHeight,
            RowHeight);

        Assert.Equal(32 + 100 * 26, height);
    }

    [Fact]
    public void VisibleRangeIncludesOneOverscanRowOnEachSide()
    {
        Rect viewport = new(x: 0, y: 292, width: 800, height: 52);

        DetailsGridLayout.GetVisibleRowRange(
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

        DetailsGridLayout.GetRetainedRowRange(
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

        DetailsGridLayout.GetVisibleRowRange(
            viewport,
            rowCount: 1000,
            HeaderHeight,
            RowHeight,
            out int interactiveFirstRow,
            out int interactiveLastRowExclusive);
        DetailsGridLayout.GetRetainedRowRange(
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
}
