using Avalonia;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessRowHoverGeometryTests
{
    [Theory]
    [InlineData(31.999, -1)]
    [InlineData(32, 0)]
    [InlineData(51.999, 0)]
    [InlineData(52, 1)]
    [InlineData(231.999, 9)]
    [InlineData(232, -1)]
    public void HitTestMapsRowsUsingContentCoordinates(double positionY, int expectedVisibleIndex)
    {
        ProcessRowHoverGeometry geometry = new(
            new Rect(0, 0, 800, 300),
            VisibleRowCount: 10,
            HeaderHeight: 32,
            RowHeight: 20,
            StickyHeaderTop: 0,
            IsEnabled: true);

        int visibleIndex = geometry.HitTest(new Point(100, positionY));

        Assert.Equal(expectedVisibleIndex, visibleIndex);
    }

    [Theory]
    [InlineData(99.999, -1)]
    [InlineData(100, -1)]
    [InlineData(131.999, -1)]
    [InlineData(132, 5)]
    [InlineData(299.999, 13)]
    [InlineData(300, -1)]
    public void HitTestExcludesScrolledViewportAndStickyHeader(
        double positionY,
        int expectedVisibleIndex)
    {
        ProcessRowHoverGeometry geometry = new(
            new Rect(200, 100, 600, 200),
            VisibleRowCount: 20,
            HeaderHeight: 32,
            RowHeight: 20,
            StickyHeaderTop: 100,
            IsEnabled: true);

        int visibleIndex = geometry.HitTest(new Point(300, positionY));

        Assert.Equal(expectedVisibleIndex, visibleIndex);
    }

    [Fact]
    public void HitTestRejectsPointsOutsideHorizontalViewport()
    {
        ProcessRowHoverGeometry geometry = new(
            new Rect(200, 100, 600, 200),
            VisibleRowCount: 20,
            HeaderHeight: 32,
            RowHeight: 20,
            StickyHeaderTop: 100,
            IsEnabled: true);

        Assert.Equal(-1, geometry.HitTest(new Point(199.999, 132)));
        Assert.Equal(-1, geometry.HitTest(new Point(800, 132)));
    }

    [Fact]
    public void HitTestRejectsRowsWhileHeaderInteractionSuppressesHover()
    {
        ProcessRowHoverGeometry geometry = new(
            new Rect(0, 0, 800, 300),
            VisibleRowCount: 10,
            HeaderHeight: 32,
            RowHeight: 20,
            StickyHeaderTop: 0,
            IsEnabled: false);

        Assert.Equal(-1, geometry.HitTest(new Point(100, 32)));
    }

    [Fact]
    public void GetRowBoundsReturnsFullHostWidth()
    {
        ProcessRowHoverGeometry geometry = new(
            new Rect(0, 0, 800, 300),
            VisibleRowCount: 10,
            HeaderHeight: 32,
            RowHeight: 20,
            StickyHeaderTop: 0,
            IsEnabled: true);

        Assert.Equal(new Rect(0, 92, 1200, 20), geometry.GetRowBounds(3, 1200));
        Assert.Equal(default, geometry.GetRowBounds(10, 1200));
    }
}
