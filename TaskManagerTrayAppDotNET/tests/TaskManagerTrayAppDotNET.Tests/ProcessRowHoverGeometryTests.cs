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
            new Rect(x: 0, y: 0, width: 800, height: 300),
            VisibleRowCount: 10,
            HeaderHeight: 32,
            RowHeight: 20,
            StickyHeaderTop: 0,
            IsEnabled: true);

        int visibleIndex = geometry.HitTest(new Point(x: 100, positionY));

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
            new Rect(x: 200, y: 100, width: 600, height: 200),
            VisibleRowCount: 20,
            HeaderHeight: 32,
            RowHeight: 20,
            StickyHeaderTop: 100,
            IsEnabled: true);

        int visibleIndex = geometry.HitTest(new Point(x: 300, positionY));

        Assert.Equal(expectedVisibleIndex, visibleIndex);
    }

    [Fact]
    public void HitTestRejectsPointsOutsideHorizontalViewport()
    {
        ProcessRowHoverGeometry geometry = new(
            new Rect(x: 200, y: 100, width: 600, height: 200),
            VisibleRowCount: 20,
            HeaderHeight: 32,
            RowHeight: 20,
            StickyHeaderTop: 100,
            IsEnabled: true);

        Assert.Equal(expected: -1, geometry.HitTest(new Point(x: 199.999, y: 132)));
        Assert.Equal(expected: -1, geometry.HitTest(new Point(x: 800, y: 132)));
    }

    [Fact]
    public void HitTestRejectsRowsWhileHeaderInteractionSuppressesHover()
    {
        ProcessRowHoverGeometry geometry = new(
            new Rect(x: 0, y: 0, width: 800, height: 300),
            VisibleRowCount: 10,
            HeaderHeight: 32,
            RowHeight: 20,
            StickyHeaderTop: 0,
            IsEnabled: false);

        Assert.Equal(expected: -1, geometry.HitTest(new Point(x: 100, y: 32)));
    }

    [Fact]
    public void GetRowBoundsReturnsFullHostWidth()
    {
        ProcessRowHoverGeometry geometry = new(
            new Rect(x: 0, y: 0, width: 800, height: 300),
            VisibleRowCount: 10,
            HeaderHeight: 32,
            RowHeight: 20,
            StickyHeaderTop: 0,
            IsEnabled: true);

        Assert.Equal(new Rect(x: 0, y: 92, width: 1200, height: 20),
            geometry.GetRowBounds(visibleIndex: 3, hostWidth: 1200));
        Assert.Equal(expected: default, geometry.GetRowBounds(visibleIndex: 10, hostWidth: 1200));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(13, true)]
    [InlineData(14, false)]
    public void IsRowVisibleExcludesStickyHeaderAndViewportClipping(
        int visibleIndex,
        bool expectedVisible)
    {
        ProcessRowHoverGeometry geometry = new(
            new Rect(x: 200, y: 100, width: 600, height: 200),
            VisibleRowCount: 20,
            HeaderHeight: 32,
            RowHeight: 20,
            StickyHeaderTop: 100,
            IsEnabled: false);

        Assert.Equal(expectedVisible, geometry.IsRowVisible(visibleIndex));
    }

    [Fact]
    public void MergedRowsShareOneHitTargetAndHighlightBounds()
    {
        const double headerHeight = 32;
        const double rowHeight = 20;
        ProcessRowHoverGeometry geometry = new(
            Viewport: new Rect(x: 0, y: 0, width: 500, height: 500),
            VisibleRowCount: 8,
            HeaderHeight: headerHeight,
            RowHeight: rowHeight,
            StickyHeaderTop: 0,
            IsEnabled: true,
            FirstMergedRowStart: 2);

        Assert.Equal(expected: 2, geometry.HitTest(new Point(x: 20, y: 80)));
        Assert.Equal(expected: 2, geometry.HitTest(new Point(x: 20, y: 100)));
        Assert.Equal(
            new Rect(x: 0, y: 72, width: 500, height: 40),
            geometry.GetRowBounds(visibleIndex: 2, hostWidth: 500));
        Assert.Equal(
            geometry.GetRowBounds(visibleIndex: 2, hostWidth: 500),
            geometry.GetRowBounds(visibleIndex: 3, hostWidth: 500));
        Assert.Equal(
            new Rect(x: 0, y: 112, width: 500, height: 20),
            geometry.GetRowBounds(visibleIndex: 4, hostWidth: 500));
    }
}
