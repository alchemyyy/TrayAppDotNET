using Avalonia;
using TaskManagerTrayAppDotNET.UI.Performance;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceStatisticsLayoutTests
{
    private static readonly Size Item = new(100, 50);
    private static readonly Size Metadata = new(150, 120);

    [Fact]
    public void WideLayoutKeepsTheStatisticsBesideTheMetadata()
    {
        List<Rect> items = [];

        double height = PerformanceStatisticsPanel.Layout(
            [Item, Item, Item, Item],
            Metadata,
            width: 600,
            columnSpacing: 20,
            rowSpacing: 8,
            items,
            out Rect metadata);

        Assert.Equal(new Rect(450, 0, 150, 120), metadata);
        Assert.All(items, static item => Assert.Equal(0, item.Y));
        Assert.Equal(120, height);
    }

    [Fact]
    public void RowsBelowTheMetadataUseTheFullWidth()
    {
        List<Rect> items = [];

        // Beside the metadata there is room for one statistic per row, below it for two
        double height = PerformanceStatisticsPanel.Layout(
            [Item, Item, Item, Item, Item],
            Metadata,
            width: 270,
            columnSpacing: 20,
            rowSpacing: 8,
            items,
            out Rect metadata);

        Assert.Equal(new Rect(120, 0, 150, 120), metadata);
        Assert.Equal(
            [new Rect(0, 0, 100, 50), new Rect(0, 50, 100, 50), new Rect(0, 100, 100, 50), new Rect(0, 150, 100, 50),
                new Rect(100, 150, 100, 50)],
            items);
        Assert.Equal(200, height);
    }

    [Fact]
    public void MetadataMovesBelowWhenNoStatisticFitsBesideIt()
    {
        List<Rect> items = [];

        double height = PerformanceStatisticsPanel.Layout(
            [Item, Item, Item],
            Metadata,
            width: 220,
            columnSpacing: 20,
            rowSpacing: 8,
            items,
            out Rect metadata);

        Assert.Equal([new Rect(0, 0, 100, 50), new Rect(100, 0, 100, 50), new Rect(0, 50, 100, 50)], items);
        Assert.Equal(new Rect(0, 108, 150, 120), metadata);
        Assert.Equal(228, height);
    }

    [Theory]
    [InlineData(146, 8, 152)]
    [InlineData(152, 8, 152)]
    [InlineData(146, 0, 146)]
    public void ItemWidthsRoundUpToTheStep(double width, double step, double expected) =>
        Assert.Equal(expected, PerformanceStatisticsPanel.RoundUpItemWidth(width, step));

    [Theory]
    [InlineData(false, 259, true)]
    [InlineData(false, 260, false)]
    [InlineData(true, 270, true)]
    [InlineData(true, 284, false)]
    public void CompactModeStartsBelowTheMinimumAndEndsWithHysteresisToSpare(
        bool isCompact,
        double fullSizeGraphAreaHeight,
        bool expected) =>
        Assert.Equal(
            expected,
            PerformancePage.ShouldUseCompactMode(
                isCompact,
                fullSizeGraphAreaHeight,
                minimumGraphAreaHeight: 260,
                hysteresis: 24));
}
