using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessViewportAnchoringTests
{
    [Fact]
    public void ResolveAdjustmentCompensatesForChangedSortRank()
    {
        ProcessViewportAnchor anchor = new(
            new ProcessInstanceKey(ProcessID: 10, CreationTimeTicks: 100),
            RowTop: 112,
            ContentHeight: 432);

        ProcessViewportAnchorAdjustment? adjustment = anchor.ResolveAdjustment(
            nextRowTop: 232,
            nextContentHeight: 432);

        ProcessViewportAnchorAdjustment resolved = Assert.IsType<ProcessViewportAnchorAdjustment>(
            adjustment);
        Assert.Equal(expected: 120, resolved.VerticalOffsetDelta);
        Assert.False(resolved.ContentHeightChanged);
    }

    [Fact]
    public void ResolveAdjustmentPreservesFractionalRowPhase()
    {
        ProcessViewportAnchor anchor = new(
            new ProcessInstanceKey(ProcessID: 10, CreationTimeTicks: 100),
            RowTop: 112.125,
            ContentHeight: 432);

        ProcessViewportAnchorAdjustment? adjustment = anchor.ResolveAdjustment(
            nextRowTop: 131.375,
            nextContentHeight: 432);

        ProcessViewportAnchorAdjustment resolved = Assert.IsType<ProcessViewportAnchorAdjustment>(
            adjustment);
        Assert.Equal(expected: 19.25, resolved.VerticalOffsetDelta);
    }

    [Fact]
    public void ResolveAdjustmentTracksMetricAndExtentChangesTogether()
    {
        ProcessViewportAnchor anchor = new(
            new ProcessInstanceKey(ProcessID: 10, CreationTimeTicks: 100),
            RowTop: 112,
            ContentHeight: 432);

        ProcessViewportAnchorAdjustment? adjustment = anchor.ResolveAdjustment(
            nextRowTop: 132,
            nextContentHeight: 532);

        ProcessViewportAnchorAdjustment resolved = Assert.IsType<ProcessViewportAnchorAdjustment>(
            adjustment);
        Assert.Equal(expected: 20, resolved.VerticalOffsetDelta);
        Assert.True(resolved.ContentHeightChanged);
    }

    [Fact]
    public void ResolveAdjustmentSkipsAnUnchangedRowPosition()
    {
        ProcessViewportAnchor anchor = new(
            new ProcessInstanceKey(ProcessID: 10, CreationTimeTicks: 100),
            RowTop: 112,
            ContentHeight: 432);

        Assert.Null(anchor.ResolveAdjustment(nextRowTop: 112, nextContentHeight: 532));
    }
}
