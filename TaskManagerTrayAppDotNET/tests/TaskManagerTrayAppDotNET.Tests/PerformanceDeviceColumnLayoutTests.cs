using Avalonia;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceDeviceColumnLayoutTests
{
    [Fact]
    public void PendingDragThresholdStartsAtFourDipsOnEitherAxis()
    {
        Point start = new(10, 20);

        Assert.False(PerformanceDeviceColumnLayout.HasReachedDragThreshold(start, new Point(13.99, 23.99)));
        Assert.True(PerformanceDeviceColumnLayout.HasReachedDragThreshold(start, new Point(14, 20)));
        Assert.True(PerformanceDeviceColumnLayout.HasReachedDragThreshold(start, new Point(10, 16)));
    }

    [Fact]
    public void InsertionGeometryExcludesTheDraggedRow()
    {
        PerformanceDeviceRowGeometry[] rows =
        [
            new(0, 40),
            new(50, 40),
            new(100, 40)
        ];

        Assert.Equal(0, PerformanceDeviceColumnLayout.GetInsertionIndex(20, rows, 1));
        Assert.Equal(1, PerformanceDeviceColumnLayout.GetInsertionIndex(21, rows, 1));
        Assert.Equal(2, PerformanceDeviceColumnLayout.GetInsertionIndex(121, rows, 1));
        Assert.Equal(1, PerformanceDeviceColumnLayout.GetInsertionIndex(75, rows, 0));
        Assert.Equal(1, PerformanceDeviceColumnLayout.GetInsertionIndex(50, rows, 2));
    }

    [Fact]
    public void MoveUsesTheFinalIndexAfterSourceRemoval()
    {
        string[] stableIDs = ["cpu", "memory", "gpu", "network"];

        List<string> movedUp = PerformanceDeviceColumnLayout.Move(stableIDs, 3, 1);
        List<string> movedDown = PerformanceDeviceColumnLayout.Move(stableIDs, 0, 3);

        Assert.Equal(["cpu", "network", "memory", "gpu"], movedUp);
        Assert.Equal(["memory", "gpu", "network", "cpu"], movedDown);
    }
}
