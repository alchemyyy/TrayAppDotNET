using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerSearchOverlayGeometryTests
{
    [Fact]
    public void PreservesNormalPositionWithoutCollision()
    {
        double offset = TaskManagerSearchOverlayGeometry.CalculateHorizontalOffset(
            overlayWidth: 800,
            unshiftedSearchRight: 615,
            captionButtonAreaWidth: 138,
            spacing: 8);

        Assert.Equal(expected: 0, offset);
    }

    [Fact]
    public void ShiftsLeftByOnlyTheCollisionWidth()
    {
        double offset = TaskManagerSearchOverlayGeometry.CalculateHorizontalOffset(
            overlayWidth: 600,
            unshiftedSearchRight: 515,
            captionButtonAreaWidth: 138,
            spacing: 8);

        Assert.Equal(expected: -61, offset);
    }

    [Fact]
    public void IgnoresNegativeReservedWidths()
    {
        double offset = TaskManagerSearchOverlayGeometry.CalculateHorizontalOffset(
            overlayWidth: 600,
            unshiftedSearchRight: 515,
            captionButtonAreaWidth: -10,
            spacing: -5);

        Assert.Equal(expected: 0, offset);
    }
}
