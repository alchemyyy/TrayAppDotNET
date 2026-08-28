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
            new(0, 100),
            new(100, 0),
            new(200, 100),
            new(125, 125),
            new(100, 200)
        ];

        ProbeCardEditorNavigationDirection direction = (ProbeCardEditorNavigationDirection)directionValue;
        int result = ProbeCardEditorKeyboardNavigation.FindDirectionalTarget(points, 3, direction);

        Assert.Equal(expectedIndex, result);
    }

    [Fact]
    public void FindDirectionalTargetPrefersAlignedControlOverCloserDiagonalControl()
    {
        List<ProbeCardEditorNavigationPoint> points =
        [
            new(0, 0),
            new(100, 0),
            new(20, 30)
        ];

        int result = ProbeCardEditorKeyboardNavigation.FindDirectionalTarget(
            points,
            0,
            ProbeCardEditorNavigationDirection.Right);

        Assert.Equal(1, result);
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
