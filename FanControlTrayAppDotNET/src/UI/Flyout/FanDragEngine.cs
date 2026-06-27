using System.Globalization;
using System.Text;
using Avalonia.Controls;
using FanControlTrayAppDotNET.Models;

namespace FanControlTrayAppDotNET.UI;

internal static class FanDragEngine
{
    private const double SlotPassRatio = 0.62;

    public static FanDragEvaluation Evaluate(FanDragSnapshot snapshot, FanDragBounds drag)
    {
        FanDragPlacement placement = CalculateDragPlacement(snapshot, drag);
        FanDragPreviewPlan preview = CalculatePreviewPlan(snapshot, placement);
        return new FanDragEvaluation(snapshot, drag, placement, preview);
    }

    public static FanDragTrace Trace(FanDragSnapshot snapshot, IEnumerable<FanDragBounds> frames)
    {
        List<FanDragTraceFrame> evaluated = [];
        int index = 0;
        foreach (FanDragBounds frame in frames)
            evaluated.Add(new FanDragTraceFrame(index++, Evaluate(snapshot, frame)));

        return new FanDragTrace(evaluated);
    }

    public static IReadOnlyList<FanDragDebugMarker> CalculateDebugMarkers(FanDragSnapshot snapshot, FanDragBounds drag)
    {
        List<FanDragTarget> targets = BuildTargets(snapshot);
        List<FanDragDebugMarker> markers = [];
        for (int i = 0; i < targets.Count; i++)
        {
            FanDragTarget target = targets[i];
            if (target.IsSource) continue;

            markers.AddRange(target.DebugMarkers(snapshot, drag, SlotPassRatio));
        }

        return
        [
            .. markers
                .Where(marker => double.IsFinite(marker.Y))
                .OrderBy(marker => marker.Y)
        ];
    }

    public static FanDragPlacement CalculateDragPlacement(FanDragSnapshot snapshot, FanDragBounds drag)
    {
        List<FanDragTarget> targets = BuildTargets(snapshot);
        if (targets.Count == 0) return FanDragPlacement.TopLevel(0);

        FanDragPlacement placement = targets[0].PlacementBefore;
        for (int i = 0; i < targets.Count; i++)
        {
            FanDragTarget target = targets[i];
            if (target.IsSource) continue;

            switch (target.HitTest(snapshot, drag, SlotPassRatio))
            {
                case FanDragTargetHit.After:
                    placement = target.PlacementAfter;
                    continue;
                case FanDragTargetHit.Inside:
                    return target.PlacementBefore;
                default:
                    return placement;
            }
        }

        return placement;
    }

    public static FanDragPreviewPlan CalculatePreviewPlan(FanDragSnapshot snapshot, FanDragPlacement placement)
    {
        if (placement is { Kind: FanDragPlacementKind.IntoGroup, GroupCell: not null })
        {
            return new FanDragPreviewPlan(
                CalculateIntoGroupPreviewOffsets(snapshot),
                placement.GroupCell,
                placement.GroupFanIndex);
        }

        return new FanDragPreviewPlan(
            CalculateTopLevelPreviewOffsets(snapshot, placement.TopLevelIndex),
            null,
            0);
    }

    public static IReadOnlyList<FanDragSlotOffset> CalculateTopLevelPreviewOffsets(
        FanDragSnapshot snapshot,
        int targetIndex)
    {
        if (snapshot.Slots.Count == 0) return [];

        double sourceExtent = SourceRootExtent(snapshot);
        if (!HasRootSource(snapshot))
            return OffsetRange(snapshot, Math.Clamp(targetIndex, 0, snapshot.Slots.Count), snapshot.Slots.Count,
                sourceExtent);

        int sourceIndex = snapshot.DragSourceTopLevelIndex;
        int target = Math.Clamp(targetIndex, 0, snapshot.Slots.Count - 1);
        if (target < sourceIndex)
            return OffsetRange(snapshot, target, sourceIndex, sourceExtent);

        if (target > sourceIndex)
            return OffsetRange(snapshot, sourceIndex + 1, target + 1, -sourceExtent);

        return [];
    }

    public static IReadOnlyList<FanDragSlotOffset> CalculateIntoGroupPreviewOffsets(FanDragSnapshot snapshot)
    {
        if (!HasRootSource(snapshot)) return [];
        return OffsetRange(snapshot, snapshot.DragSourceTopLevelIndex + 1, snapshot.Slots.Count,
            -SourceRootExtent(snapshot));
    }

    public static int ResolveGroupDropPreviewChildIndex(
        FanFlyoutCell groupCell,
        int groupFanIndex,
        FanFlyoutCell? dragSourceCell,
        Fan? draggedFan,
        int childCount)
    {
        int insertionIndex = 1 + Math.Max(0, groupFanIndex);
        if (draggedFan != null && IsSameGroup(dragSourceCell, groupCell))
        {
            int sourceFanIndex = IndexOfFan(groupCell, draggedFan);
            if (sourceFanIndex >= 0 && sourceFanIndex <= groupFanIndex)
                insertionIndex++;
        }

        return Math.Clamp(insertionIndex, 1, childCount);
    }

    public static List<FanFlyoutCell> MoveFanToTopLevel(List<FanFlyoutCell> cells, Fan fan, int targetIndex)
    {
        List<FanFlyoutCell> result = [];
        foreach (FanFlyoutCell cell in cells)
        {
            if (cell.Fans.Contains(fan))
            {
                if (cell.HasGroupHeader)
                {
                    List<Fan> remaining = [.. cell.Fans.Where(f => !ReferenceEquals(f, fan))];
                    result.Add(new FanFlyoutCell(cell.GroupSettings, remaining));
                }

                continue;
            }

            result.Add(cell);
        }

        if (fan.Group == null)
            result.Insert(Math.Clamp(targetIndex, 0, result.Count), new FanFlyoutCell(null, [fan]));
        return result;
    }

    public static List<FanFlyoutCell> MoveFanIntoGroup(
        List<FanFlyoutCell> cells,
        Fan fan,
        FanFlyoutCell targetGroup,
        int targetFanIndex)
    {
        List<FanFlyoutCell> result = [];
        foreach (FanFlyoutCell cell in cells)
        {
            if (IsSameGroup(cell, targetGroup))
            {
                List<Fan> fans = [.. cell.Fans.Where(candidate => !ReferenceEquals(candidate, fan))];
                fans.Insert(Math.Clamp(targetFanIndex, 0, fans.Count), fan);
                result.Add(new FanFlyoutCell(cell.GroupSettings, fans));
                continue;
            }

            if (cell.Fans.Contains(fan))
            {
                if (cell.HasGroupHeader)
                {
                    List<Fan> remaining = [.. cell.Fans.Where(candidate => !ReferenceEquals(candidate, fan))];
                    result.Add(new FanFlyoutCell(cell.GroupSettings, remaining));
                }

                continue;
            }

            result.Add(cell);
        }

        return result;
    }

    public static int IndexOfFan(FanFlyoutCell cell, Fan fan)
    {
        for (int i = 0; i < cell.Fans.Count; i++)
            if (ReferenceEquals(cell.Fans[i], fan))
                return i;

        return -1;
    }

    public static bool IsSameGroup(FanFlyoutCell? left, FanFlyoutCell? right)
    {
        if (left?.GroupSettings != null && right?.GroupSettings != null)
        {
            return ReferenceEquals(left.GroupSettings, right.GroupSettings)
                   || string.Equals(left.GroupName, right.GroupName, StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(left?.GroupName)
               && string.Equals(left.GroupName, right?.GroupName, StringComparison.OrdinalIgnoreCase);
    }

    public static int IndexOfDragSlot(FanDragSnapshot snapshot, FanFlyoutCell cell)
    {
        for (int i = 0; i < snapshot.Slots.Count; i++)
        {
            FanFlyoutCell candidate = snapshot.Slots[i].Cell;
            if (ReferenceEquals(candidate, cell)
                || candidate.GroupSettings != null
                && cell.GroupSettings != null
                && ReferenceEquals(candidate.GroupSettings, cell.GroupSettings)
                || !candidate.HasGroupHeader
                && !cell.HasGroupHeader
                && candidate.Fans.Count == 1
                && cell.Fans.Count == 1
                && ReferenceEquals(candidate.Fans[0], cell.Fans[0]))
                return i;
        }

        return -1;
    }

    public static List<FanDragFanSlot> GroupFanSlots(
        FanDragSnapshot snapshot,
        FanFlyoutCell groupCell,
        bool excludeDraggedFan)
    {
        return
        [
            .. snapshot.FanSlots
                .Where(slot => IsSameGroup(slot.Cell, groupCell)
                               && (!excludeDraggedFan || snapshot.DraggedFan == null
                                   || !ReferenceEquals(slot.Fan, snapshot.DraggedFan)))
                .OrderBy(slot => slot.Top)
        ];
    }

    public static FanDragBounds DragBoundsForHeight(FanDragSnapshot snapshot, FanDragBounds drag, double height)
    {
        double adjustedHeight = Math.Max(1, height);
        double pointerOffset = Math.Clamp(snapshot.DragPointerOffsetRatio * adjustedHeight, 0.0, adjustedHeight);
        double top = drag.PointerY - pointerOffset;
        return new FanDragBounds(top, top + adjustedHeight, adjustedHeight, top + adjustedHeight / 2.0,
            drag.PointerY, drag.MovingDown);
    }

    private static List<FanDragTarget> BuildTargets(FanDragSnapshot snapshot)
    {
        List<FanDragTarget> targets = [];
        for (int rootIndex = 0; rootIndex < snapshot.Slots.Count; rootIndex++)
        {
            FanDragSlot root = snapshot.Slots[rootIndex];
            bool rootIsSource = IsRootSource(snapshot, rootIndex);
            if (!root.Cell.HasGroupHeader || snapshot.DraggedFan == null)
            {
                targets.Add(RootTarget(snapshot, root, rootIndex, rootIsSource));
                continue;
            }

            AddGroupTargets(snapshot, targets, root, rootIndex, rootIsSource);
        }

        return [.. targets.OrderBy(slot => slot.Range.Top)];
    }

    private static void AddGroupTargets(
        FanDragSnapshot snapshot,
        List<FanDragTarget> targets,
        FanDragSlot root,
        int rootIndex,
        bool rootIsSource)
    {
        FanDragPlacement rootPlacement = TopLevelPlacementForRootInsertion(snapshot, rootIndex);
        targets.Add(new FanDragTarget(
            FanDragTargetKind.GroupHeader,
            FanDragRange.FromBounds(root.Top, root.GroupInsertionTop),
            rootPlacement,
            rootPlacement,
            rootIsSource));

        List<FanDragFanSlot> fanSlots = GroupFanSlots(snapshot, root.Cell, excludeDraggedFan: true);
        for (int i = 0; i < fanSlots.Count; i++)
        {
            FanDragFanSlot fanSlot = fanSlots[i];
            double top = i == 0 ? Math.Min(root.GroupInsertionTop, fanSlot.Top) : fanSlot.Top;
            targets.Add(GroupFanTarget(snapshot, root.Cell, fanSlot, top));
        }

        FanDragRange appendRange = GroupAppendRange(root, fanSlots);
        if (appendRange.Height <= 1) return;

        int appendIndex = AdjustGroupFanInsertionIndex(snapshot, root.Cell, root.Cell.Fans.Count);
        targets.Add(new FanDragTarget(
            FanDragTargetKind.GroupAppend,
            appendRange,
            FanDragPlacement.IntoGroup(root.Cell, appendIndex),
            TopLevelPlacementForRootInsertion(snapshot, rootIndex + 1),
            false));
    }

    private static FanDragTarget RootTarget(
        FanDragSnapshot snapshot,
        FanDragSlot root,
        int rootIndex,
        bool isSource) =>
        new(
            FanDragTargetKind.Root,
            FanDragRange.FromTopAndHeight(root.Top, root.Height),
            TopLevelPlacementForRootInsertion(snapshot, rootIndex),
            TopLevelPlacementForRootInsertion(snapshot, rootIndex + 1),
            isSource);

    private static FanDragTarget GroupFanTarget(
        FanDragSnapshot snapshot,
        FanFlyoutCell groupCell,
        FanDragFanSlot fanSlot,
        double top) =>
        new(
            FanDragTargetKind.GroupFan,
            FanDragRange.FromBounds(top, fanSlot.Top + fanSlot.Height),
            FanDragPlacement.IntoGroup(groupCell, AdjustGroupFanInsertionIndex(snapshot, groupCell, fanSlot.FanIndex)),
            FanDragPlacement.IntoGroup(groupCell, AdjustGroupFanInsertionIndex(snapshot, groupCell, fanSlot.FanIndex + 1)),
            ReferenceEquals(fanSlot.Fan, snapshot.DraggedFan));

    private static FanDragRange GroupAppendRange(FanDragSlot root, List<FanDragFanSlot> fanSlots)
    {
        double top = fanSlots.Count == 0
            ? root.GroupInsertionTop
            : Math.Max(root.GroupInsertionTop, fanSlots[^1].Top + fanSlots[^1].Height);
        return FanDragRange.FromBounds(top, root.GroupDropBottom);
    }

    private static bool HasRootSource(FanDragSnapshot snapshot) =>
        snapshot.DragSourceTopLevelIndex >= 0 && snapshot.DragSourceTopLevelControl != null;

    private static bool IsRootSource(FanDragSnapshot snapshot, int rootIndex) =>
        HasRootSource(snapshot) && rootIndex == snapshot.DragSourceTopLevelIndex;

    private static double SourceRootExtent(FanDragSnapshot snapshot) =>
        Math.Max(1, snapshot.DragSourceSlotHeight);

    private static List<FanDragSlotOffset> OffsetRange(
        FanDragSnapshot snapshot,
        int start,
        int end,
        double offset)
    {
        int clampedStart = Math.Clamp(start, 0, snapshot.Slots.Count);
        int clampedEnd = Math.Clamp(end, clampedStart, snapshot.Slots.Count);
        if (clampedStart == clampedEnd || offset == 0) return [];

        List<FanDragSlotOffset> offsets = [];
        for (int i = clampedStart; i < clampedEnd; i++)
            offsets.Add(new FanDragSlotOffset(i, offset));
        return offsets;
    }

    private static FanDragPlacement TopLevelPlacementForRootInsertion(FanDragSnapshot snapshot, int rootInsertionIndex) =>
        FanDragPlacement.TopLevel(AdjustTopLevelInsertionIndex(snapshot, rootInsertionIndex));

    private static int AdjustTopLevelInsertionIndex(FanDragSnapshot snapshot, int rootInsertionIndex)
    {
        int insertion = rootInsertionIndex;
        if (snapshot.DragSourceTopLevelControl != null
            && snapshot.DragSourceTopLevelIndex >= 0
            && snapshot.DragSourceTopLevelIndex < insertion)
            insertion--;

        int max = snapshot.Slots.Count - (snapshot.DragSourceTopLevelControl != null ? 1 : 0);
        return Math.Clamp(insertion, 0, max);
    }

    private static int AdjustGroupFanInsertionIndex(
        FanDragSnapshot snapshot,
        FanFlyoutCell groupCell,
        int groupFanIndex)
    {
        int insertion = groupFanIndex;
        if (snapshot.DraggedFan != null && IsSameGroup(snapshot.DragSourceCell, groupCell))
        {
            int sourceFanIndex = IndexOfFan(groupCell, snapshot.DraggedFan);
            if (sourceFanIndex >= 0 && sourceFanIndex < insertion)
                insertion--;
        }

        int max = groupCell.Fans.Count;
        if (snapshot.DraggedFan != null && groupCell.Fans.Any(fan => ReferenceEquals(fan, snapshot.DraggedFan)))
            max--;
        return Math.Clamp(insertion, 0, Math.Max(0, max));
    }
}

internal enum FanDragTargetKind
{
    Root,
    GroupHeader,
    GroupFan,
    GroupAppend,
}

internal enum FanDragTargetHit
{
    Before,
    Inside,
    After,
}

internal sealed record FanDragTarget(
    FanDragTargetKind Kind,
    FanDragRange Range,
    FanDragPlacement PlacementBefore,
    FanDragPlacement PlacementAfter,
    bool IsSource)
{
    public FanDragTargetHit HitTest(
        FanDragSnapshot snapshot,
        FanDragBounds drag,
        double slotPassRatio)
    {
        if (Kind == FanDragTargetKind.GroupAppend)
            return Range.HitTestPoint(drag.PointerY);

        FanDragBounds comparison = Kind == FanDragTargetKind.GroupFan
            ? FanDragEngine.DragBoundsForHeight(snapshot, drag, snapshot.DragSourceFanSlotHeight)
            : drag;
        if (Range.IsAfter(comparison, slotPassRatio))
            return FanDragTargetHit.After;

        return Range.Overlaps(comparison) ? FanDragTargetHit.Inside : FanDragTargetHit.Before;
    }

    public IReadOnlyList<FanDragDebugMarker> DebugMarkers(
        FanDragSnapshot snapshot,
        FanDragBounds drag,
        double slotPassRatio)
    {
        if (Kind == FanDragTargetKind.GroupAppend)
        {
            return
            [
                new FanDragDebugMarker(Range.Top, PlacementBefore),
                new FanDragDebugMarker(Range.Bottom, PlacementAfter),
            ];
        }

        double height = Kind == FanDragTargetKind.GroupFan
            ? Math.Max(1, snapshot.DragSourceFanSlotHeight)
            : Math.Max(1, drag.Height);
        double pointerOffset = Kind == FanDragTargetKind.GroupFan
            ? Math.Clamp(snapshot.DragPointerOffsetRatio * height, 0.0, height)
            : Math.Clamp(drag.PointerY - drag.Top, 0.0, height);
        double threshold = Math.Min(height, Range.Height) * slotPassRatio;

        double enterY = drag.MovingDown
            ? Range.Top - (height - pointerOffset)
            : Range.Bottom + pointerOffset;
        double passY = drag.MovingDown
            ? Range.Top + threshold - (height - pointerOffset)
            : Range.Bottom - threshold + pointerOffset;

        return
        [
            new FanDragDebugMarker(enterY, PlacementBefore),
            new FanDragDebugMarker(passY, PlacementAfter),
        ];
    }
}

internal readonly record struct FanDragRange(double Top, double Bottom)
{
    public double Height => Math.Max(1, Bottom - Top);

    public static FanDragRange FromTopAndHeight(double top, double height) =>
        new(top, top + Math.Max(1, height));

    public static FanDragRange FromBounds(double top, double bottom) =>
        FromTopAndHeight(top, bottom - top);

    public FanDragTargetHit HitTestPoint(double y)
    {
        if (y > Bottom) return FanDragTargetHit.After;
        return y >= Top ? FanDragTargetHit.Inside : FanDragTargetHit.Before;
    }

    public bool Overlaps(FanDragBounds bounds) =>
        Math.Min(bounds.Bottom, Bottom) > Math.Max(bounds.Top, Top);

    public bool IsAfter(FanDragBounds bounds, double passRatio)
    {
        double threshold = Math.Min(bounds.Height, Height) * passRatio;
        return bounds.MovingDown
            ? bounds.Bottom - Top >= threshold
            : Bottom - bounds.Top < threshold;
    }
}

internal sealed record FanDragSnapshot(
    IReadOnlyList<FanDragSlot> Slots,
    IReadOnlyList<FanDragFanSlot> FanSlots,
    Fan? DraggedFan,
    FanFlyoutCell? DragSourceCell,
    Control? DragSourceTopLevelControl,
    int DragSourceTopLevelIndex,
    double DragSourceSlotHeight,
    double DragSourceFanSlotHeight,
    double DragPlacementSourceHeight,
    double DragPointerOffsetRatio)
{
    public string ToCompactString()
    {
        string dragged = DraggedFan?.DisplayName ?? "<none>";
        return string.Create(CultureInfo.InvariantCulture,
            $"slots={Slots.Count};fanSlots={FanSlots.Count};dragged={dragged};sourceTop={DragSourceTopLevelIndex};sourceSlot={DragSourceSlotHeight:0.##};sourceFanSlot={DragSourceFanSlotHeight:0.##};ratio={DragPointerOffsetRatio:0.###}");
    }
}

internal sealed record FanDragEvaluation(
    FanDragSnapshot Snapshot,
    FanDragBounds Bounds,
    FanDragPlacement Placement,
    FanDragPreviewPlan Preview)
{
    public string ToCompactString()
    {
        StringBuilder builder = new();
        builder.Append(Snapshot.ToCompactString());
        builder.Append(";drag=");
        builder.Append(Bounds.ToCompactString());
        builder.Append(";placement=");
        builder.Append(Placement.ToCompactString());
        builder.Append(";preview=");
        builder.Append(Preview.ToCompactString());
        return builder.ToString();
    }
}

internal sealed record FanDragTrace(IReadOnlyList<FanDragTraceFrame> Frames)
{
    public string ToCompactString() => string.Join(Environment.NewLine,
        Frames.Select(frame => frame.ToCompactString()));
}

internal sealed record FanDragTraceFrame(int Index, FanDragEvaluation Evaluation)
{
    public string ToCompactString() => $"{Index}: {Evaluation.ToCompactString()}";
}

internal readonly record struct FanDragDebugMarker(double Y, FanDragPlacement Placement);

internal sealed record FanDragPreviewPlan(
    IReadOnlyList<FanDragSlotOffset> TopLevelOffsets,
    FanFlyoutCell? GroupDropPreviewCell,
    int GroupDropPreviewFanIndex)
{
    public string ToCompactString()
    {
        string offsets = TopLevelOffsets.Count == 0
            ? "none"
            : string.Join(",", TopLevelOffsets.Select(offset =>
                string.Create(CultureInfo.InvariantCulture, $"{offset.Index}:{offset.Offset:0.##}")));
        string group = GroupDropPreviewCell?.GroupName ?? "<none>";
        return $"offsets={offsets};groupPreview={group}@{GroupDropPreviewFanIndex}";
    }
}

internal readonly record struct FanDragSlotOffset(int Index, double Offset);

internal enum FanDragPlacementKind
{
    None,
    TopLevel,
    IntoGroup,
}

internal enum FanDragGhostStyle
{
    None,
    TopLevelFan,
    GroupedFan,
    Group,
}

internal readonly record struct FanDragPlacement(
    FanDragPlacementKind Kind,
    int TopLevelIndex,
    FanFlyoutCell? GroupCell,
    int GroupFanIndex)
{
    public static FanDragPlacement None => new(FanDragPlacementKind.None, 0, null, 0);

    public static FanDragPlacement TopLevel(int index) =>
        new(FanDragPlacementKind.TopLevel, index, null, 0);

    public static FanDragPlacement IntoGroup(FanFlyoutCell groupCell, int groupFanIndex) =>
        new(FanDragPlacementKind.IntoGroup, 0, groupCell, groupFanIndex);

    public string ToCompactString()
    {
        return Kind switch
        {
            FanDragPlacementKind.TopLevel => string.Create(CultureInfo.InvariantCulture, $"top[{TopLevelIndex}]"),
            FanDragPlacementKind.IntoGroup => $"group[{GroupCell?.GroupName ?? "<none>"}:{GroupFanIndex}]",
            _ => "none",
        };
    }
}

internal sealed record FanDragSlot(
    FanFlyoutCell Cell,
    Control Visual,
    double Top,
    double Height,
    double SlotHeight,
    double GroupInsertionTop,
    double GroupDropBottom);

internal sealed record FanDragFanSlot(
    FanFlyoutCell Cell,
    Fan Fan,
    Control Visual,
    double Top,
    double Height,
    int FanIndex);

internal readonly record struct FanDragBounds(
    double Top,
    double Bottom,
    double Height,
    double Midpoint,
    double PointerY,
    bool MovingDown)
{
    public string ToCompactString() => string.Create(CultureInfo.InvariantCulture,
        $"top={Top:0.##},bottom={Bottom:0.##},height={Height:0.##},mid={Midpoint:0.##},pointer={PointerY:0.##},down={MovingDown}");
}
