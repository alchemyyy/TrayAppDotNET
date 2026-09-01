using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class RestoredWindowDragGeometryTests
{
    [Theory]
    [InlineData(false, 250, 285)]
    [InlineData(true, 250, 250)]
    [InlineData(true, 0, 0)]
    public void SearchRangeTracksConfiguredAlignment(
        bool alignToPageArea,
        int pageContentLeft,
        int expectedLeft)
    {
        int left = RestoredWindowDragGeometry.CalculateSearchLeftWithinWindow(
            proposedWindowWidth: 1000,
            searchWidth: 430,
            alignToPageArea,
            pageContentLeft);

        Assert.Equal(expectedLeft, left);
    }

    [Theory]
    [InlineData(false, 250, 247, 715)]
    [InlineData(true, 250, 212, 680)]
    public void SearchRangeIncludesLeadingActionsWithoutMovingTheSearchBox(
        bool alignToPageArea,
        int pageContentLeft,
        int expectedLeft,
        int expectedRight)
    {
        RestoredWindowDragSearchRange range =
            RestoredWindowDragGeometry.CalculateSearchRangeWithinWindow(
                proposedWindowWidth: 1000,
                searchWidth: 430,
                leadingActionWidth: 38,
                alignToPageArea,
                pageContentLeft);

        Assert.Equal(expectedLeft, range.Left);
        Assert.Equal(expectedRight, range.Right);
    }

    [Fact]
    public void SearchRangeMovesLeftOfCaptionButtonsWhenRestoredNarrow()
    {
        RestoredWindowDragSearchRange range =
            RestoredWindowDragGeometry.CalculateSearchRangeWithinWindow(
                proposedWindowWidth: 600,
                searchWidth: 430,
                leadingActionWidth: 0,
                alignToPageArea: false,
                pageContentLeft: 0,
                captionButtonAreaWidth: 138,
                captionSpacing: 8);

        Assert.Equal(expected: 24, range.Left);
        Assert.Equal(expected: 454, range.Right);
    }

    [Theory]
    [InlineData(399)]
    [InlineData(800)]
    public void CursorOutsideSearchBoxDoesNotMoveWindow(int cursorScreenX)
    {
        int offset = RestoredWindowDragGeometry.CalculateHorizontalWindowOffset(
            cursorScreenX,
            proposedWindowLeft: 100,
            searchLeftWithinWindow: 300,
            searchRightWithinWindow: 700,
            outsideMarginPixels: 8);

        Assert.Equal(expected: 0, offset);
    }

    [Fact]
    public void CursorNearLeftEdgeMovesWindowRight()
    {
        int offset = RestoredWindowDragGeometry.CalculateHorizontalWindowOffset(
            cursorScreenX: 450,
            proposedWindowLeft: 100,
            searchLeftWithinWindow: 300,
            searchRightWithinWindow: 700,
            outsideMarginPixels: 8);

        Assert.Equal(expected: 58, offset);
        Assert.Equal(expected: 292, 450 - (100 + offset));
    }

    [Fact]
    public void CursorNearRightEdgeMovesWindowLeft()
    {
        int offset = RestoredWindowDragGeometry.CalculateHorizontalWindowOffset(
            cursorScreenX: 750,
            proposedWindowLeft: 100,
            searchLeftWithinWindow: 300,
            searchRightWithinWindow: 700,
            outsideMarginPixels: 8);

        Assert.Equal(expected: -58, offset);
        Assert.Equal(expected: 708, 750 - (100 + offset));
    }

    [Fact]
    public void EquidistantCursorUsesLeftSide()
    {
        int offset = RestoredWindowDragGeometry.CalculateHorizontalWindowOffset(
            cursorScreenX: 600,
            proposedWindowLeft: 100,
            searchLeftWithinWindow: 300,
            searchRightWithinWindow: 700,
            outsideMarginPixels: 8);

        Assert.Equal(expected: 208, offset);
    }

    [Fact]
    public void InvalidSearchRangeDoesNotMoveWindow()
    {
        int offset = RestoredWindowDragGeometry.CalculateHorizontalWindowOffset(
            cursorScreenX: 500,
            proposedWindowLeft: 100,
            searchLeftWithinWindow: 700,
            searchRightWithinWindow: 300,
            outsideMarginPixels: 8);

        Assert.Equal(expected: 0, offset);
    }
}
