using Avalonia;
using TaskManagerTrayAppDotNET.UI.Performance;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceGraphHoverLayoutTests
{
    private static readonly Size GraphSize = new(width: 200, height: 100);
    private static readonly Rect GraphBounds = new(GraphSize);
    private static readonly double[] SampleFractions = [0.25, 0.50, 0.75];

    private static readonly PerformanceGraphHoverMetrics SnappedMetrics = new(
        PerformanceGraphHoverLineMode.SnapToSample,
        TextInset: 4,
        TextCursorGap: 6,
        LineClipPadding: 2,
        LineTerminalLength: 3);

    private static readonly PerformanceGraphHoverLabelMetrics Label = new(
        Width: 30,
        Height: 16,
        Extent: 11,
        OverhangAfter: -3);

    [Fact]
    public void SnappedHoverCentersTheLabelOnTheNearestSampleAndPartsTheLineAroundItsInk()
    {
        bool wasPlaced = PerformanceGraphHoverLayout.TryPlace(
            SnappedMetrics,
            GraphSize,
            GraphBounds,
            SampleFractions,
            hasIndicator: true,
            Label,
            new Point(x: 108, y: 60),
            out PerformanceGraphHoverPlacement placement);

        Assert.True(wasPlaced);
        // Ink spans 40..51 inside the label box at 38..54; the line stops 2 short of it on both sides
        Assert.Equal(
            new PerformanceGraphHoverPlacement(
                LineX: 100,
                Height: 100,
                HasLineGap: true,
                UpperLineEnd: 38,
                LineTerminalStart: 35,
                LowerLineStart: 53,
                HasLabel: true,
                LabelOrigin: new Point(x: 85, y: 38)),
            placement);
    }

    [Fact]
    public void SnappedLabelKeepsItsInsetAtTheGraphCorner()
    {
        bool wasPlaced = PerformanceGraphHoverLayout.TryPlace(
            SnappedMetrics,
            GraphSize,
            GraphBounds,
            [0, 0.5, 1],
            hasIndicator: true,
            Label,
            new Point(x: 2, y: 5),
            out PerformanceGraphHoverPlacement placement);

        Assert.True(wasPlaced);
        Assert.Equal(expected: 0, placement.LineX);
        Assert.Equal(new Point(x: 4, y: 4), placement.LabelOrigin);
    }

    [Fact]
    public void FollowingHoverTracksThePointerAndClampsTheLabelToTheGraphEdges()
    {
        PerformanceGraphHoverMetrics metrics = SnappedMetrics with
        {
            LineMode = PerformanceGraphHoverLineMode.FollowPointer
        };

        bool wasPlaced = PerformanceGraphHoverLayout.TryPlace(
            metrics,
            GraphSize,
            GraphBounds,
            [],
            hasIndicator: true,
            Label,
            new Point(x: 195, y: 10),
            out PerformanceGraphHoverPlacement placement);

        Assert.True(wasPlaced);
        Assert.Equal(expected: 195, placement.LineX);
        Assert.False(placement.HasLineGap);
        Assert.Equal(new Point(x: 170, y: 0), placement.LabelOrigin);
    }

    [Theory]
    [InlineData(205, true)]
    [InlineData(210, false)]
    [InlineData(-1, false)]
    public void HoverShowsOnlyInsideTheHoverBounds(double pointerPositionX, bool expectedPlaced)
    {
        Rect hoverBounds = new(x: 0, y: 0, width: 210, height: 110);

        bool wasPlaced = PerformanceGraphHoverLayout.TryPlace(
            SnappedMetrics,
            GraphSize,
            hoverBounds,
            SampleFractions,
            hasIndicator: true,
            label: null,
            new Point(pointerPositionX, y: 50),
            out PerformanceGraphHoverPlacement placement);

        Assert.Equal(expectedPlaced, wasPlaced);
        if (expectedPlaced) Assert.Equal(expected: 150, placement.LineX);
    }
}
