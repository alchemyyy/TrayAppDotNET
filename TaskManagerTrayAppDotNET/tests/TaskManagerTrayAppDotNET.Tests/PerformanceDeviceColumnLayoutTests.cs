using Avalonia;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceDeviceColumnLayoutTests
{
    [Fact]
    public void PendingDragThresholdStartsAtFourDipsOnEitherAxis()
    {
        Point start = new(x: 10, y: 20);

        Assert.False(PerformanceDeviceColumnLayout.HasReachedDragThreshold(start, new Point(x: 13.99, y: 23.99)));
        Assert.True(PerformanceDeviceColumnLayout.HasReachedDragThreshold(start, new Point(x: 14, y: 20)));
        Assert.True(PerformanceDeviceColumnLayout.HasReachedDragThreshold(start, new Point(x: 10, y: 16)));
    }

    [Fact]
    public void InsertionGeometryExcludesTheDraggedRow()
    {
        PerformanceDeviceRowGeometry[] rows =
        [
            new(Top: 0, Height: 40),
            new(Top: 50, Height: 40),
            new(Top: 100, Height: 40)
        ];

        Assert.Equal(expected: 0,
            PerformanceDeviceColumnLayout.GetInsertionIndex(draggedMidpointY: 20, rows, sourceIndex: 1));
        Assert.Equal(expected: 1,
            PerformanceDeviceColumnLayout.GetInsertionIndex(draggedMidpointY: 21, rows, sourceIndex: 1));
        Assert.Equal(expected: 2,
            PerformanceDeviceColumnLayout.GetInsertionIndex(draggedMidpointY: 121, rows, sourceIndex: 1));
        Assert.Equal(expected: 1,
            PerformanceDeviceColumnLayout.GetInsertionIndex(draggedMidpointY: 75, rows, sourceIndex: 0));
        Assert.Equal(expected: 1,
            PerformanceDeviceColumnLayout.GetInsertionIndex(draggedMidpointY: 50, rows, sourceIndex: 2));
    }

    [Fact]
    public void MoveUsesTheFinalIndexAfterSourceRemoval()
    {
        string[] stableIDs = ["cpu", "memory", "gpu", "network"];

        List<string> movedUp = PerformanceDeviceColumnLayout.Move(stableIDs, sourceIndex: 3, targetIndex: 1);
        List<string> movedDown = PerformanceDeviceColumnLayout.Move(stableIDs, sourceIndex: 0, targetIndex: 3);

        Assert.Equal(["cpu", "network", "memory", "gpu"], movedUp);
        Assert.Equal(["memory", "gpu", "network", "cpu"], movedDown);
    }
}
