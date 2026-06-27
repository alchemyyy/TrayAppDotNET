using Avalonia.Controls;
using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanDragEngineTests
{
    [Fact]
    public void TopLevelFanMovingDownDoesNotReorderUntilOverlapThresholdIsReached()
    {
        DragRig rig = DragRig.CreateTopLevelOnly();
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);

        FanDragEvaluation beforeThreshold = FanDragEngine.Evaluate(snapshot, BoundsFromTop(55));
        FanDragEvaluation afterThreshold = FanDragEngine.Evaluate(snapshot, BoundsFromTop(58));

        AssertTopLevel(beforeThreshold, 0);
        AssertTopLevel(afterThreshold, 1);
    }

    [Fact]
    public void TopLevelFanMovingUpUsesTheSameOverlapThresholdFromTheLeadingEdge()
    {
        DragRig rig = DragRig.CreateTopLevelOnly();
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanC, rig.CellC, sourceTopLevelIndex: 2);

        FanDragEvaluation notFarEnough = FanDragEngine.Evaluate(snapshot, BoundsFromTop(110, movingDown: false));
        FanDragEvaluation farEnough = FanDragEngine.Evaluate(snapshot, BoundsFromTop(125, movingDown: false));

        AssertTopLevel(notFarEnough, 1);
        AssertTopLevel(farEnough, 2);
    }

    [Fact]
    public void TopLevelFanMovingBackDownAfterPreviewShiftUsesCurrentDragDirection()
    {
        DragRig rig = DragRig.CreateTopLevelOnly();
        List<FanDragSlot> previewShiftedSlots =
        [
            rig.Slots[0] with { Top = 88 },
            rig.Slots[1],
            rig.Slots[2],
        ];
        FanDragSnapshot snapshot = new(
            previewShiftedSlots,
            rig.FanSlots,
            rig.FanB,
            rig.CellB,
            rig.Slots[1].Visual,
            1,
            88,
            36,
            80,
            0.5);

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, BoundsFromTop(70, movingDown: true));
        IReadOnlyList<FanDragDebugMarker> markers =
            FanDragEngine.CalculateDebugMarkers(snapshot, BoundsFromTop(70, movingDown: true));

        AssertTopLevel(evaluation, 1);
        Assert.Contains(markers, marker =>
            marker.Placement is { Kind: FanDragPlacementKind.TopLevel, TopLevelIndex: 1 }
            && Math.Abs(marker.Y - 97.6) < 0.001);
    }

    [Fact]
    public void GroupHeaderSlotPreviewsTopLevelPlacementBeforeTheGroup()
    {
        DragRig rig = DragRig.Create();
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, BoundsFromTop(50));

        AssertTopLevel(evaluation, 0);
        Assert.Null(evaluation.Preview.GroupDropPreviewCell);
    }

    [Fact]
    public void DragIntoGroupStartsAfterTheHeaderZoneAndUsesTheCompactGroupedFanHeight()
    {
        DragRig rig = DragRig.Create();
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, BoundsFromTop(92));

        AssertIntoGroup(evaluation, rig.GroupCell, 1);
        Assert.Same(rig.GroupCell, evaluation.Preview.GroupDropPreviewCell);
        Assert.Equal(1, evaluation.Preview.GroupDropPreviewFanIndex);
    }

    [Fact]
    public void DragIntoGroupStartsAtTheGroupFanInsertionBoundary()
    {
        DragRig rig = DragRig.Create();
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);

        FanDragEvaluation beforeInsertionBoundary = FanDragEngine.Evaluate(snapshot, BoundsFromTop(65));
        FanDragEvaluation afterInsertionBoundary = FanDragEngine.Evaluate(snapshot, BoundsFromTop(67));
        IReadOnlyList<FanDragDebugMarker> markers =
            FanDragEngine.CalculateDebugMarkers(snapshot, BoundsFromTop(67));

        AssertTopLevel(beforeInsertionBoundary, 0);
        AssertIntoGroup(afterInsertionBoundary, rig.GroupCell, 0);
        Assert.Contains(markers, marker =>
            marker.Placement is { Kind: FanDragPlacementKind.IntoGroup, GroupFanIndex: 0 }
            && Math.Abs(marker.Y - 106) < 0.001);
    }

    [Fact]
    public void GroupDropBottomStopsTheHitBoxAtTheOriginalGroupBottomWhenPlaceholderExpanded()
    {
        DragRig rig = DragRig.Create(groupHeight: 236, groupDropBottom: 288);
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, BoundsFromTop(250));

        AssertTopLevel(evaluation, 1);
        Assert.Null(evaluation.Preview.GroupDropPreviewCell);
    }

    [Fact]
    public void IntoGroupPreviewFromSourceAboveClosesTheWholeRootSourceGap()
    {
        DragRig rig = DragRig.Create(includeFanBetweenSourceAndGroup: true);
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);
        FanDragPlacement placement = FanDragPlacement.IntoGroup(rig.GroupCell, 1);

        FanDragPreviewPlan preview = FanDragEngine.CalculatePreviewPlan(snapshot, placement);

        AssertOffsets(preview.TopLevelOffsets, (1, -88), (2, -88), (3, -88));
        Assert.Same(rig.GroupCell, preview.GroupDropPreviewCell);
        Assert.Equal(1, preview.GroupDropPreviewFanIndex);
    }

    [Fact]
    public void FlattenedGroupedFanSlotCanBeTargetedDirectlyAfterTheHeader()
    {
        DragRig rig = DragRig.Create(includeFanBetweenSourceAndGroup: true, groupInsertionOffset: 120);
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, BoundsFromTop(170));

        AssertIntoGroup(evaluation, rig.GroupCell, 0);
        AssertOffsets(evaluation.Preview.TopLevelOffsets, (1, -88), (2, -88), (3, -88));
    }

    [Fact]
    public void TopLevelPlacementFromBelowUsesFlattenedRootOffsets()
    {
        DragRig rig = DragRig.Create(includeFanBetweenSourceAndGroup: true);
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanC, rig.CellC, sourceTopLevelIndex: 3);
        FanDragPlacement placement = FanDragPlacement.TopLevel(1);

        FanDragPreviewPlan preview = FanDragEngine.CalculatePreviewPlan(snapshot, placement);

        AssertOffsets(preview.TopLevelOffsets, (1, 88), (2, 88));
    }

    [Fact]
    public void IntoGroupPreviewUsesBlankGroupPlaceholderAndSourceGapOffsets()
    {
        DragRig rig = DragRig.Create(includeFanBetweenSourceAndGroup: true);
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);
        FanDragPlacement placement = FanDragPlacement.IntoGroup(rig.GroupCell, 1);

        FanDragPreviewPlan preview = FanDragEngine.CalculatePreviewPlan(snapshot, placement);

        AssertOffsets(preview.TopLevelOffsets, (1, -88), (2, -88), (3, -88));
        Assert.Same(rig.GroupCell, preview.GroupDropPreviewCell);
        Assert.Equal(1, preview.GroupDropPreviewFanIndex);
    }

    [Fact]
    public void SameGroupPlaceholderIndexCompensatesForTheExistingSourceRow()
    {
        DragRig rig = DragRig.Create();

        int beforeSource = FanDragEngine.ResolveGroupDropPreviewChildIndex(
            rig.GroupCell,
            groupFanIndex: 0,
            rig.GroupCell,
            rig.GroupFan1,
            childCount: 4);
        int afterSource = FanDragEngine.ResolveGroupDropPreviewChildIndex(
            rig.GroupCell,
            groupFanIndex: 2,
            rig.GroupCell,
            rig.GroupFan1,
            childCount: 4);
        int differentGroup = FanDragEngine.ResolveGroupDropPreviewChildIndex(
            rig.GroupCell,
            groupFanIndex: 2,
            rig.CellA,
            rig.FanA,
            childCount: 4);

        Assert.Equal(1, beforeSource);
        Assert.Equal(4, afterSource);
        Assert.Equal(3, differentGroup);
    }

    [Fact]
    public void SameGroupInsertionExcludesTheDraggedFanSlot()
    {
        DragRig rig = DragRig.Create();
        FanDragSnapshot snapshot = rig.Snapshot(
            rig.GroupFan1,
            rig.GroupCell,
            sourceTopLevelIndex: -1,
            sourceTopLevelControl: null,
            sourceFanSlotHeight: 36);

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, BoundsFromTop(164));

        AssertIntoGroup(evaluation, rig.GroupCell, 1);
    }

    [Fact]
    public void DraggingGroupedFanOutToTopLevelCreatesSpaceFromTheInsertionIndexDownward()
    {
        DragRig rig = DragRig.Create();
        FanDragSnapshot snapshot = rig.Snapshot(
            rig.GroupFan1,
            rig.GroupCell,
            sourceTopLevelIndex: 1,
            sourceTopLevelControl: null,
            hasSourceTopLevelControl: false,
            sourceSlotHeight: 36,
            sourceFanSlotHeight: 36,
            dragPlacementSourceHeight: 36);
        FanDragPlacement placement = FanDragPlacement.TopLevel(2);

        FanDragPreviewPlan preview = FanDragEngine.CalculatePreviewPlan(snapshot, placement);

        AssertOffsets(preview.TopLevelOffsets, (2, 36));
    }

    [Fact]
    public void MoveFanIntoGroupAndBackOutPreservesEveryFanExactlyOnce()
    {
        DragRig rig = DragRig.Create();
        List<FanFlyoutCell> cells = [rig.CellA, rig.GroupCell, rig.CellC];

        rig.FanA.Group = rig.GroupCell.GroupName;
        List<FanFlyoutCell> grouped = FanDragEngine.MoveFanIntoGroup(cells, rig.FanA, rig.GroupCell, 1);

        FanFlyoutCell groupedCell = Assert.Single(grouped, cell => cell.HasGroupHeader);
        Assert.Equal([rig.GroupFan0, rig.FanA, rig.GroupFan1], groupedCell.Fans.ToArray());
        Assert.DoesNotContain(grouped, cell => !cell.HasGroupHeader && cell.Fans.Contains(rig.FanA));

        rig.FanA.Group = null;
        List<FanFlyoutCell> ungrouped = FanDragEngine.MoveFanToTopLevel(grouped, rig.FanA, 1);

        Assert.Equal([rig.GroupCell.GroupName!, rig.FanA.DisplayName, rig.FanC.DisplayName],
            DescribeCells(ungrouped));
        Assert.All([rig.FanA, rig.GroupFan0, rig.GroupFan1, rig.FanC], fan =>
            Assert.Equal(1, ungrouped.Sum(cell => cell.Fans.Count(candidate => ReferenceEquals(candidate, fan)))));
    }

    [Fact]
    public void PointerTraceMovesFromGroupHeaderSlotIntoGroupedFanSlotsWithoutOscillation()
    {
        DragRig rig = DragRig.Create();
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);

        FanDragTrace trace = FanDragEngine.Trace(snapshot,
        [
            BoundsFromTop(50),
            BoundsFromTop(70),
            BoundsFromTop(92),
            BoundsFromTop(110),
            BoundsFromTop(130),
        ]);
        string[] placements = [.. trace.Frames.Select(frame => PlacementLabel(frame.Evaluation))];

        Assert.True(trace.ToCompactString().Contains("group[Group A:2]", StringComparison.Ordinal),
            trace.ToCompactString());
        Assert.Equal(["top:0:<none>", "group:Group A:0", "group:Group A:1", "group:Group A:1", "group:Group A:2"],
            placements);
    }

    [Fact]
    public void PointerTraceWithIntermediateFanCollapsesTowardTheGroupAndDoesNotReturnToSourceGap()
    {
        DragRig rig = DragRig.Create(includeFanBetweenSourceAndGroup: true, groupDropBottom: 376);
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);

        FanDragTrace trace = FanDragEngine.Trace(snapshot,
        [
            BoundsFromTop(134),
            BoundsFromTop(156),
            BoundsFromTop(176),
            BoundsFromTop(196),
        ]);
        string[] placements = [.. trace.Frames.Select(frame => PlacementLabel(frame.Evaluation))];

        Assert.Equal(["top:1:<none>", "group:Group A:0", "group:Group A:0", "group:Group A:1"], placements);
        AssertOffsets(trace.Frames[0].Evaluation.Preview.TopLevelOffsets, (1, -88));
        Assert.All(trace.Frames.Skip(1).Take(2), frame =>
            AssertOffsets(frame.Evaluation.Preview.TopLevelOffsets, (1, -88), (2, -88), (3, -88)));
    }

    [Fact]
    public void PointerTraceLeavesGroupAfterTheOriginalGroupBottomInsteadOfStayingTrappedInExpandedSpace()
    {
        DragRig rig = DragRig.Create(groupHeight: 236, groupDropBottom: 288);
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);

        FanDragTrace trace = FanDragEngine.Trace(snapshot,
        [
            BoundsFromTop(92),
            BoundsFromTop(170),
            BoundsFromTop(250),
        ]);
        string[] placements = [.. trace.Frames.Select(frame => PlacementLabel(frame.Evaluation))];

        Assert.True(trace.ToCompactString().Contains("top[1]", StringComparison.Ordinal), trace.ToCompactString());
        Assert.Equal(["group:Group A:1", "group:Group A:2", "top:1:<none>"], placements);
    }

    [Fact]
    public void EmptyGroupInsertionUsesTheCurrentFanCountWhenThereAreNoRenderedFanRows()
    {
        DragRig rig = DragRig.Create(emptyGroup: true);
        FanDragSnapshot snapshot = rig.Snapshot(rig.FanA, rig.CellA, sourceTopLevelIndex: 0);

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, BoundsFromTop(92));

        AssertIntoGroup(evaluation, rig.GroupCell, 0);
    }

    [Fact]
    public void GroupCardDragNeverAttemptsToInsertIntoAnotherGroup()
    {
        DragRig rig = DragRig.Create();
        FanDragSnapshot snapshot = rig.Snapshot(
            draggedFan: null,
            dragSourceCell: rig.GroupCell,
            sourceTopLevelIndex: 1,
            sourceTopLevelControl: rig.Slots[1].Visual,
            dragPlacementSourceHeight: 200);

        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, BoundsFromTop(100, height: 200));

        AssertTopLevel(evaluation, 1);
        Assert.Null(evaluation.Preview.GroupDropPreviewCell);
    }

    private static FanDragBounds BoundsFromTop(double top, double height = 80, double pointerRatio = 0.5,
        bool movingDown = true)
    {
        double bottom = top + height;
        double pointerY = top + height * pointerRatio;
        return new FanDragBounds(top, bottom, height, top + height / 2.0, pointerY, movingDown);
    }

    private static void AssertTopLevel(FanDragEvaluation evaluation, int index)
    {
        Assert.True(evaluation.Placement.Kind == FanDragPlacementKind.TopLevel,
            evaluation.ToCompactString());
        Assert.Equal(index, evaluation.Placement.TopLevelIndex);
    }

    private static void AssertIntoGroup(FanDragEvaluation evaluation, FanFlyoutCell groupCell, int groupFanIndex)
    {
        Assert.True(evaluation.Placement.Kind == FanDragPlacementKind.IntoGroup,
            evaluation.ToCompactString());
        Assert.Same(groupCell, evaluation.Placement.GroupCell);
        Assert.Equal(groupFanIndex, evaluation.Placement.GroupFanIndex);
    }

    private static void AssertOffsets(IReadOnlyList<FanDragSlotOffset> actual, params (int Index, double Offset)[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Index, actual[i].Index);
            Assert.Equal(expected[i].Offset, actual[i].Offset, precision: 3);
        }
    }

    private static string PlacementLabel(FanDragEvaluation evaluation)
    {
        FanDragPlacement placement = evaluation.Placement;
        return placement.Kind switch
        {
            FanDragPlacementKind.TopLevel => $"top:{placement.TopLevelIndex}:<none>",
            FanDragPlacementKind.IntoGroup => $"group:{placement.GroupCell?.GroupName}:{placement.GroupFanIndex}",
            _ => "none",
        };
    }

    private static string[] DescribeCells(IEnumerable<FanFlyoutCell> cells)
    {
        return
        [
            .. cells.Select(cell => cell.HasGroupHeader
                ? cell.GroupName ?? string.Empty
                : cell.Fans.Single().DisplayName)
        ];
    }

    private sealed class DragRig
    {
        private DragRig(
            Fan fanA,
            Fan fanB,
            Fan fanC,
            Fan groupFan0,
            Fan groupFan1,
            FanFlyoutCell cellA,
            FanFlyoutCell cellB,
            FanFlyoutCell cellC,
            FanFlyoutCell groupCell,
            IReadOnlyList<FanDragSlot> slots,
            IReadOnlyList<FanDragFanSlot> fanSlots)
        {
            FanA = fanA;
            FanB = fanB;
            FanC = fanC;
            GroupFan0 = groupFan0;
            GroupFan1 = groupFan1;
            CellA = cellA;
            CellB = cellB;
            CellC = cellC;
            GroupCell = groupCell;
            Slots = slots;
            FanSlots = fanSlots;
        }

        public Fan FanA { get; }
        public Fan FanB { get; }
        public Fan FanC { get; }
        public Fan GroupFan0 { get; }
        public Fan GroupFan1 { get; }
        public FanFlyoutCell CellA { get; }
        public FanFlyoutCell CellB { get; }
        public FanFlyoutCell CellC { get; }
        public FanFlyoutCell GroupCell { get; }
        public IReadOnlyList<FanDragSlot> Slots { get; }
        public IReadOnlyList<FanDragFanSlot> FanSlots { get; }

        public static DragRig Create(
            bool includeFanBetweenSourceAndGroup = false,
            bool emptyGroup = false,
            double groupHeight = 200,
            double groupInsertionOffset = 36,
            double groupDropBottom = 288)
        {
            Fan fanA = Fan("Fan A");
            Fan fanB = Fan("Fan B");
            Fan fanC = Fan("Fan C");
            Fan groupFan0 = Fan("Grouped 0", "Group A");
            Fan groupFan1 = Fan("Grouped 1", "Group A");

            FanFlyoutCell cellA = new(null, [fanA]);
            FanFlyoutCell cellB = new(null, [fanB]);
            FanFlyoutCell cellC = new(null, [fanC]);
            FanGroup group = new() { Name = "Group A" };
            FanFlyoutCell groupCell = new(group, emptyGroup ? [] : [groupFan0, groupFan1]);

            List<FanDragSlot> slots = [];
            double top = 0;
            slots.Add(Slot(cellA, top, 80, 88));
            top += 88;

            if (includeFanBetweenSourceAndGroup)
            {
                slots.Add(Slot(cellB, top, 80, 88));
                top += 88;
            }

            slots.Add(Slot(groupCell, top, groupHeight, groupHeight + 8, groupInsertionTop: top + groupInsertionOffset,
                groupDropBottom: groupDropBottom));
            top += groupHeight + 8;
            slots.Add(Slot(cellC, top, 80, 88));

            double groupTop = slots.Single(slot => ReferenceEquals(slot.Cell, groupCell)).Top;
            List<FanDragFanSlot> fanSlots = emptyGroup
                ? []
                :
                [
                    FanSlot(groupCell, groupFan0, groupTop + 42, 32, 0),
                    FanSlot(groupCell, groupFan1, groupTop + 78, 32, 1),
                ];

            return new DragRig(fanA, fanB, fanC, groupFan0, groupFan1, cellA, cellB, cellC, groupCell,
                slots, fanSlots);
        }

        public static DragRig CreateTopLevelOnly()
        {
            Fan fanA = Fan("Fan A");
            Fan fanB = Fan("Fan B");
            Fan fanC = Fan("Fan C");
            Fan groupFan0 = Fan("Grouped 0", "Group A");
            Fan groupFan1 = Fan("Grouped 1", "Group A");

            FanFlyoutCell cellA = new(null, [fanA]);
            FanFlyoutCell cellB = new(null, [fanB]);
            FanFlyoutCell cellC = new(null, [fanC]);
            FanGroup group = new() { Name = "Group A" };
            FanFlyoutCell groupCell = new(group, [groupFan0, groupFan1]);

            List<FanDragSlot> slots =
            [
                Slot(cellA, 0, 80, 88),
                Slot(cellB, 88, 80, 88),
                Slot(cellC, 176, 80, 88),
            ];

            return new DragRig(fanA, fanB, fanC, groupFan0, groupFan1, cellA, cellB, cellC, groupCell,
                slots, []);
        }

        public FanDragSnapshot Snapshot(
            Fan? draggedFan,
            FanFlyoutCell? dragSourceCell,
            int sourceTopLevelIndex,
            Control? sourceTopLevelControl = null,
            bool hasSourceTopLevelControl = true,
            double sourceSlotHeight = 88,
            double sourceFanSlotHeight = 36,
            double dragPlacementSourceHeight = 80,
            double pointerOffsetRatio = 0.5)
        {
            Control? sourceControl = hasSourceTopLevelControl
                ? sourceTopLevelControl ?? (sourceTopLevelIndex >= 0 ? Slots[sourceTopLevelIndex].Visual : null)
                : null;
            return new FanDragSnapshot(
                Slots,
                FanSlots,
                draggedFan,
                dragSourceCell,
                sourceControl,
                sourceTopLevelIndex,
                sourceSlotHeight,
                sourceFanSlotHeight,
                dragPlacementSourceHeight,
                pointerOffsetRatio);
        }

        private static Fan Fan(string name, string? group = null) => new()
        {
            FansName = name,
            DataSourceKey = name,
            Group = group,
        };

        private static FanDragSlot Slot(
            FanFlyoutCell cell,
            double top,
            double height,
            double slotHeight,
            double? groupInsertionTop = null,
            double? groupDropBottom = null)
        {
            return new FanDragSlot(
                cell,
                new Border(),
                top,
                height,
                slotHeight,
                groupInsertionTop ?? top,
                groupDropBottom ?? top + height);
        }

        private static FanDragFanSlot FanSlot(FanFlyoutCell cell, Fan fan, double top, double height, int fanIndex) =>
            new(cell, fan, new Border(), top, height, fanIndex);
    }
}
