using Avalonia.Controls;
using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI.Flyout;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanFlyoutTopLevelOrderTests
{
    [Fact]
    public void MovingFanBelowProbeUsesProbeSlotAndPreservesMixedOrder()
    {
        Fan fan = new() { FansName = "Fan", DataSourceKey = "Fan" };
        using FanFlyoutCell fanCell = new(null, [fan]);
        ProbeCard probeCard = new() { Name = "Probe" };
        Border fanVisual = new();
        Border probeVisual = new();
        List<FanDragSlot> dragSlots =
        [
            new(fanCell, fanVisual, 0, 80, 88, 0, 80),
            new(null, probeVisual, 88, 80, 88, 88, 168, probeCard)
        ];
        FanDragSnapshot snapshot = new(
            dragSlots,
            [],
            fan,
            fanCell,
            fanVisual,
            0,
            88,
            36,
            80,
            0.5);
        FanDragBounds dragBounds = new(58, 138, 80, 98, 98, true);
        List<FanFlyoutWindow.FlyoutTopLevelItem> currentOrder =
        [
            FanFlyoutWindow.FlyoutTopLevelItem.FanCell(fanCell),
            FanFlyoutWindow.FlyoutTopLevelItem.Probe(probeCard)
        ];

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, dragBounds);
        List<FanFlyoutWindow.FlyoutTopLevelItem> moved =
            FanFlyoutWindow.MoveFanTopLevelItem(currentOrder, fan, evaluation.Placement.TopLevelIndex);

        Assert.Equal(FanDragPlacementKind.TopLevel, evaluation.Placement.Kind);
        Assert.Equal(1, evaluation.Placement.TopLevelIndex);
        FanDragSlotOffset probeOffset = Assert.Single(evaluation.Preview.TopLevelOffsets);
        Assert.Equal(1, probeOffset.Index);
        Assert.Equal(-88, probeOffset.Offset);
        Assert.Collection(
            moved,
            item => Assert.Same(probeCard, item.ProbeCard),
            item => Assert.Same(fan, Assert.Single(item.CellArrangement!.Fans)));
    }

    [Fact]
    public void MovingFanIntoGroupPreservesInterleavedProbe()
    {
        Fan fan = new() { FansName = "Fan", DataSourceKey = "Fan" };
        using FanFlyoutCell fanCell = new(null, [fan]);
        ProbeCard probeCard = new() { Name = "Probe" };
        FanGroup group = new() { Name = "Group" };
        using FanFlyoutCell groupCell = new(group, []);
        List<FanFlyoutWindow.FlyoutTopLevelItem> currentOrder =
        [
            FanFlyoutWindow.FlyoutTopLevelItem.FanCell(fanCell),
            FanFlyoutWindow.FlyoutTopLevelItem.Probe(probeCard),
            FanFlyoutWindow.FlyoutTopLevelItem.FanCell(groupCell)
        ];

        List<FanFlyoutWindow.FlyoutTopLevelItem> moved =
            FanFlyoutWindow.MoveFanIntoGroupTopLevelItem(currentOrder, fan, groupCell, targetFanIndex: 0);

        Assert.Collection(
            moved,
            item => Assert.Same(probeCard, item.ProbeCard),
            item =>
            {
                Assert.Same(group, item.CellArrangement!.GroupSettings);
                Assert.Same(fan, Assert.Single(item.CellArrangement.Fans));
            });
    }

    [Fact]
    public void MovingGroupBelowProbeUsesProbeSlotAndPreservesMixedOrder()
    {
        FanGroup group = new() { Name = "Group" };
        using FanFlyoutCell groupCell = new(group, []);
        ProbeCard probeCard = new() { Name = "Probe" };
        Border groupVisual = new();
        Border probeVisual = new();
        List<FanDragSlot> dragSlots =
        [
            new(groupCell, groupVisual, 0, 200, 208, 0, 200),
            new(null, probeVisual, 208, 80, 88, 208, 288, probeCard)
        ];
        FanDragSnapshot snapshot = new(
            dragSlots,
            [],
            null,
            groupCell,
            groupVisual,
            0,
            208,
            36,
            200,
            0.5);
        FanDragBounds dragBounds = new(58, 258, 200, 158, 158, true);
        List<FanFlyoutWindow.FlyoutTopLevelItem> currentOrder =
        [
            FanFlyoutWindow.FlyoutTopLevelItem.FanCell(groupCell),
            FanFlyoutWindow.FlyoutTopLevelItem.Probe(probeCard)
        ];

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, dragBounds);
        List<FanFlyoutWindow.FlyoutTopLevelItem> moved =
            FanFlyoutWindow.MoveGroupTopLevelItem(currentOrder, groupCell, evaluation.Placement.TopLevelIndex);

        Assert.Equal(FanDragPlacementKind.TopLevel, evaluation.Placement.Kind);
        Assert.Equal(1, evaluation.Placement.TopLevelIndex);
        FanDragSlotOffset probeOffset = Assert.Single(evaluation.Preview.TopLevelOffsets);
        Assert.Equal(1, probeOffset.Index);
        Assert.Equal(-208, probeOffset.Offset);
        Assert.Collection(
            moved,
            item => Assert.Same(probeCard, item.ProbeCard),
            item => Assert.Same(groupCell, item.Cell));
    }
}
