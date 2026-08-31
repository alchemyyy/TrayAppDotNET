using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessViewportAnchoringTests
{
    [Fact]
    public void SelectedProcessTakesPriorityOverHoveredProcess()
    {
        ProcessInstanceKey selectedProcess = new(10, 100);
        ProcessInstanceKey hoveredProcess = new(20, 200);

        ProcessInstanceKey? anchorProcess = ProcessViewportAnchor.ResolveProcessIdentity(
            selectedProcess,
            hoveredProcess);

        Assert.Equal(selectedProcess, anchorProcess);
    }

    [Fact]
    public void HoveredProcessIsUsedWhenThereIsNoSelection()
    {
        ProcessInstanceKey hoveredProcess = new(20, 200);

        ProcessInstanceKey? anchorProcess = ProcessViewportAnchor.ResolveProcessIdentity(
            null,
            hoveredProcess);

        Assert.Equal(hoveredProcess, anchorProcess);
    }

    [Fact]
    public void ResolveAdjustmentCompensatesForChangedSortRank()
    {
        ProcessViewportAnchor anchor = new(
            new ProcessInstanceKey(10, 100),
            RowTop: 112,
            ContentHeight: 432);

        ProcessViewportAnchorAdjustment? adjustment = anchor.ResolveAdjustment(
            nextRowTop: 232,
            nextContentHeight: 432);

        ProcessViewportAnchorAdjustment resolved = Assert.IsType<ProcessViewportAnchorAdjustment>(
            adjustment);
        Assert.Equal(120, resolved.VerticalOffsetDelta);
        Assert.False(resolved.ContentHeightChanged);
    }

    [Fact]
    public void ResolveAdjustmentTracksMetricAndExtentChangesTogether()
    {
        ProcessViewportAnchor anchor = new(
            new ProcessInstanceKey(10, 100),
            RowTop: 112,
            ContentHeight: 432);

        ProcessViewportAnchorAdjustment? adjustment = anchor.ResolveAdjustment(
            nextRowTop: 132,
            nextContentHeight: 532);

        ProcessViewportAnchorAdjustment resolved = Assert.IsType<ProcessViewportAnchorAdjustment>(
            adjustment);
        Assert.Equal(20, resolved.VerticalOffsetDelta);
        Assert.True(resolved.ContentHeightChanged);
    }

    [Fact]
    public void ResolveAdjustmentSkipsAnUnchangedRowPosition()
    {
        ProcessViewportAnchor anchor = new(
            new ProcessInstanceKey(10, 100),
            RowTop: 112,
            ContentHeight: 432);

        Assert.Null(anchor.ResolveAdjustment(nextRowTop: 112, nextContentHeight: 532));
    }
}
