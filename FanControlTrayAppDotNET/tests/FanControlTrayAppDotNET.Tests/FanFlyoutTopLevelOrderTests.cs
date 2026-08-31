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
        using FanFlyoutCell fanCell = new(groupSettings: null, [fan]);
        ProbeCard probeCard = new() { Name = "Probe" };
        Border fanVisual = new();
        Border probeVisual = new();
        List<FanDragSlot> dragSlots =
        [
            new(fanCell, fanVisual, Top: 0, Height: 80, SlotHeight: 88, GroupInsertionTop: 0, GroupDropBottom: 80),
            new(Cell: null, probeVisual, Top: 88, Height: 80, SlotHeight: 88, GroupInsertionTop: 88,
                GroupDropBottom: 168, probeCard)
        ];
        FanDragSnapshot snapshot = new(
            dragSlots,
            [],
            fan,
            fanCell,
            fanVisual,
            DragSourceTopLevelIndex: 0,
            DragSourceSlotHeight: 88,
            DragSourceFanSlotHeight: 36,
            DragPlacementSourceHeight: 80,
            DragPointerOffsetRatio: 0.5);
        FanDragBounds dragBounds = new(Top: 58, Bottom: 138, Height: 80, Midpoint: 98, PointerY: 98, MovingDown: true);
        List<FanFlyoutWindow.FlyoutTopLevelItem> currentOrder =
        [
            FanFlyoutWindow.FlyoutTopLevelItem.FanCell(fanCell),
            FanFlyoutWindow.FlyoutTopLevelItem.Probe(probeCard)
        ];

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, dragBounds);
        List<FanFlyoutWindow.FlyoutTopLevelItem> moved =
            FanFlyoutWindow.MoveFanTopLevelItem(currentOrder, fan, evaluation.Placement.TopLevelIndex);

        Assert.Equal(FanDragPlacementKind.TopLevel, evaluation.Placement.Kind);
        Assert.Equal(expected: 1, evaluation.Placement.TopLevelIndex);
        FanDragSlotOffset probeOffset = Assert.Single(evaluation.Preview.TopLevelOffsets);
        Assert.Equal(expected: 1, probeOffset.Index);
        Assert.Equal(expected: -88, probeOffset.Offset);
        Assert.Collection(
            moved,
            item => Assert.Same(probeCard, item.ProbeCard),
            item => Assert.Same(fan, Assert.Single(item.CellArrangement!.Fans)));
    }

    [Fact]
    public void MovingFanIntoGroupPreservesInterleavedProbe()
    {
        Fan fan = new() { FansName = "Fan", DataSourceKey = "Fan" };
        using FanFlyoutCell fanCell = new(groupSettings: null, [fan]);
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
            new(groupCell, groupVisual, Top: 0, Height: 200, SlotHeight: 208, GroupInsertionTop: 0,
                GroupDropBottom: 200),
            new(Cell: null, probeVisual, Top: 208, Height: 80, SlotHeight: 88, GroupInsertionTop: 208,
                GroupDropBottom: 288, probeCard)
        ];
        FanDragSnapshot snapshot = new(
            dragSlots,
            [],
            DraggedFan: null,
            groupCell,
            groupVisual,
            DragSourceTopLevelIndex: 0,
            DragSourceSlotHeight: 208,
            DragSourceFanSlotHeight: 36,
            DragPlacementSourceHeight: 200,
            DragPointerOffsetRatio: 0.5);
        FanDragBounds dragBounds =
            new(Top: 58, Bottom: 258, Height: 200, Midpoint: 158, PointerY: 158, MovingDown: true);
        List<FanFlyoutWindow.FlyoutTopLevelItem> currentOrder =
        [
            FanFlyoutWindow.FlyoutTopLevelItem.FanCell(groupCell),
            FanFlyoutWindow.FlyoutTopLevelItem.Probe(probeCard)
        ];

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, dragBounds);
        List<FanFlyoutWindow.FlyoutTopLevelItem> moved =
            FanFlyoutWindow.MoveGroupTopLevelItem(currentOrder, groupCell, evaluation.Placement.TopLevelIndex);

        Assert.Equal(FanDragPlacementKind.TopLevel, evaluation.Placement.Kind);
        Assert.Equal(expected: 1, evaluation.Placement.TopLevelIndex);
        FanDragSlotOffset probeOffset = Assert.Single(evaluation.Preview.TopLevelOffsets);
        Assert.Equal(expected: 1, probeOffset.Index);
        Assert.Equal(expected: -208, probeOffset.Offset);
        Assert.Collection(
            moved,
            item => Assert.Same(probeCard, item.ProbeCard),
            item => Assert.Same(groupCell, item.Cell));
    }
}
