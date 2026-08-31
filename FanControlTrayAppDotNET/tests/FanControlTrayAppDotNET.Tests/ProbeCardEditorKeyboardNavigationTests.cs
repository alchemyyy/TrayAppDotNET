using FanControlTrayAppDotNET.UI.Flyout;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class ProbeCardEditorKeyboardNavigationTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 4)]
    public void FindDirectionalTargetSelectsNearestControlOnRequestedAxis(
        int directionValue,
        int expectedIndex)
    {
        List<ProbeCardEditorNavigationPoint> points =
        [
            new(X: 0, Y: 100),
            new(X: 100, Y: 0),
            new(X: 200, Y: 100),
            new(X: 125, Y: 125),
            new(X: 100, Y: 200)
        ];

        ProbeCardEditorNavigationDirection direction = (ProbeCardEditorNavigationDirection)directionValue;
        int result = ProbeCardEditorKeyboardNavigation.FindDirectionalTarget(points, currentIndex: 3, direction);

        Assert.Equal(expectedIndex, result);
    }

    [Fact]
    public void FindDirectionalTargetPrefersAlignedControlOverCloserDiagonalControl()
    {
        List<ProbeCardEditorNavigationPoint> points =
        [
            new(X: 0, Y: 0),
            new(X: 100, Y: 0),
            new(X: 20, Y: 30)
        ];

        int result = ProbeCardEditorKeyboardNavigation.FindDirectionalTarget(
            points,
            currentIndex: 0,
            ProbeCardEditorNavigationDirection.Right);

        Assert.Equal(expected: 1, result);
    }

    [Theory]
    [InlineData(0, -1, 6, 5)]
    [InlineData(5, 1, 6, 0)]
    [InlineData(2, 1, 6, 3)]
    public void WrapIndexCyclesTabs(int currentIndex, int offset, int count, int expectedIndex)
    {
        int result = ProbeCardEditorKeyboardNavigation.WrapIndex(currentIndex, offset, count);

        Assert.Equal(expectedIndex, result);
    }
}
