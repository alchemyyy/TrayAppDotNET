using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI.ProcessesPage;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessViewportAnchoringTests
{
    private const byte AppsSection = 1;
    private const byte BackgroundSection = 2;

    [Fact]
    public void ResolveAdjustmentCompensatesForChangedSortRank()
    {
        ProcessViewportAnchor anchor = CreateAnchor(rowTop: 112);

        ProcessViewportAnchorAdjustment? adjustment = anchor.ResolveAdjustment(
            nextRowTop: 232,
            nextContentHeight: 432,
            nextSemanticSection: 0,
            followSectionChange: true);

        ProcessViewportAnchorAdjustment resolved = Assert.IsType<ProcessViewportAnchorAdjustment>(
            adjustment);
        Assert.Equal(expected: 120, resolved.VerticalOffsetDelta);
        Assert.False(resolved.ContentHeightChanged);
    }

    [Fact]
    public void ResolveAdjustmentPreservesFractionalRowPhase()
    {
        ProcessViewportAnchor anchor = CreateAnchor(rowTop: 112.125);

        ProcessViewportAnchorAdjustment? adjustment = anchor.ResolveAdjustment(
            nextRowTop: 131.375,
            nextContentHeight: 432,
            nextSemanticSection: 0,
            followSectionChange: true);

        ProcessViewportAnchorAdjustment resolved = Assert.IsType<ProcessViewportAnchorAdjustment>(
            adjustment);
        Assert.Equal(expected: 19.25, resolved.VerticalOffsetDelta);
    }

    [Fact]
    public void ResolveAdjustmentTracksMetricAndExtentChangesTogether()
    {
        ProcessViewportAnchor anchor = CreateAnchor(rowTop: 112);

        ProcessViewportAnchorAdjustment? adjustment = anchor.ResolveAdjustment(
            nextRowTop: 132,
            nextContentHeight: 532,
            nextSemanticSection: 0,
            followSectionChange: true);

        ProcessViewportAnchorAdjustment resolved = Assert.IsType<ProcessViewportAnchorAdjustment>(
            adjustment);
        Assert.Equal(expected: 20, resolved.VerticalOffsetDelta);
        Assert.True(resolved.ContentHeightChanged);
    }

    [Fact]
    public void ResolveAdjustmentSkipsAnUnchangedRowPosition()
    {
        ProcessViewportAnchor anchor = CreateAnchor(rowTop: 112);

        Assert.Null(anchor.ResolveAdjustment(
            nextRowTop: 112,
            nextContentHeight: 532,
            nextSemanticSection: 0,
            followSectionChange: true));
    }

    [Theory]
    [InlineData(AppsSection, false, true)]
    [InlineData(BackgroundSection, false, false)]
    [InlineData(BackgroundSection, true, true)]
    public void ResolveAdjustmentFollowsASectionChangeOnlyWhenAllowed(
        byte nextSemanticSection,
        bool followSectionChange,
        bool expectedAdjustment)
    {
        ProcessViewportAnchor anchor = CreateAnchor(rowTop: 112, semanticSection: AppsSection);

        ProcessViewportAnchorAdjustment? adjustment = anchor.ResolveAdjustment(
            nextRowTop: 512,
            nextContentHeight: 832,
            nextSemanticSection,
            followSectionChange);

        Assert.Equal(expectedAdjustment, adjustment.HasValue);
    }

    private static ProcessViewportAnchor CreateAnchor(double rowTop, byte semanticSection = 0) =>
        new(
            new ProcessInstanceKey(ProcessID: 10, CreationTimeTicks: 100),
            RowTop: rowTop,
            ContentHeight: 432,
            SemanticSection: semanticSection);
}
