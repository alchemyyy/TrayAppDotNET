using System.Collections.ObjectModel;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Classifies devices shown on the Performance page.</summary>
public enum PerformanceDeviceKind
{
    CPU,
    Memory,
    GPU,
    Network,
    Disk
}

/// <summary>Provides the stable identity and fallback sort key used to order one device row.</summary>
internal readonly record struct PerformanceDeviceOrderItem(
    string ID,
    PerformanceDeviceKind Kind,
    int SortKey);

/// <summary>Normalizes and resolves the persisted Performance-page device order.</summary>
internal static class PerformanceDeviceOrdering
{
    private static readonly ReadOnlyCollection<PerformanceDeviceKind> DefaultPriorityValues =
        Array.AsReadOnly(
        [
            PerformanceDeviceKind.CPU,
            PerformanceDeviceKind.Memory,
            PerformanceDeviceKind.GPU,
            PerformanceDeviceKind.Network,
            PerformanceDeviceKind.Disk
        ]);

    private static readonly IComparer<PerformanceDeviceOrderItem> FallbackItemComparer =
        Comparer<PerformanceDeviceOrderItem>.Create(static (left, right) =>
        {
            int sortKeyComparison = left.SortKey.CompareTo(right.SortKey);
            return sortKeyComparison != 0
                ? sortKeyComparison
                : StringComparer.Ordinal.Compare(left.ID, right.ID);
        });

    public static IReadOnlyList<PerformanceDeviceKind> DefaultPriority => DefaultPriorityValues;

    /// <summary>Returns an independent copy of the default kind priority.</summary>
    public static List<PerformanceDeviceKind> CreateDefaultPriority() => [.. DefaultPriorityValues];

    /// <summary>Preserves valid first occurrences and appends missing kinds in default order.</summary>
    public static List<PerformanceDeviceKind> NormalizePriority(
        IEnumerable<PerformanceDeviceKind>? priority)
    {
        List<PerformanceDeviceKind> normalized = new(DefaultPriorityValues.Count);
        HashSet<PerformanceDeviceKind> used = [];
        if (priority != null)
        {
            foreach (PerformanceDeviceKind kind in priority)
            {
                if (!Enum.IsDefined(kind) || !used.Add(kind)) continue;
                normalized.Add(kind);
            }
        }

        foreach (PerformanceDeviceKind kind in DefaultPriorityValues)
        {
            if (used.Add(kind))
                normalized.Add(kind);
        }

        // Future enum members remain usable even before they receive a deliberate default position
        foreach (PerformanceDeviceKind kind in Enum.GetValues<PerformanceDeviceKind>())
        {
            if (used.Add(kind))
                normalized.Add(kind);
        }

        return normalized;
    }

    /// <summary>Removes empty and duplicate stable IDs while preserving their first occurrence.</summary>
    public static List<string> NormalizeExplicitOrder(IEnumerable<string>? deviceIDs)
    {
        List<string> normalized = [];
        HashSet<string> used = new(StringComparer.Ordinal);
        if (deviceIDs == null) return normalized;

        foreach (string? deviceID in deviceIDs)
        {
            string? normalizedID = NormalizeDeviceID(deviceID);
            if (normalizedID == null || !used.Add(normalizedID)) continue;
            normalized.Add(normalizedID);
        }

        return normalized;
    }

    /// <summary>
    /// Replays matching explicit IDs, then merges unconfigured devices beside their kind without
    /// changing the relative order of explicitly configured rows.
    /// </summary>
    public static List<PerformanceDeviceOrderItem> Resolve(
        IReadOnlyList<PerformanceDeviceOrderItem> items,
        IEnumerable<PerformanceDeviceKind>? priority,
        IEnumerable<string>? explicitDeviceIDs)
    {
        ArgumentNullException.ThrowIfNull(items);

        List<PerformanceDeviceOrderItem> liveItems = NormalizeItems(items);
        List<PerformanceDeviceKind> normalizedPriority = NormalizePriority(priority);
        List<string> normalizedExplicitOrder = NormalizeExplicitOrder(explicitDeviceIDs);
        Dictionary<string, PerformanceDeviceOrderItem> liveItemsByID = new(StringComparer.Ordinal);
        foreach (PerformanceDeviceOrderItem item in liveItems)
            liveItemsByID.Add(item.ID, item);

        List<PerformanceDeviceOrderItem> resolved = new(liveItems.Count);
        HashSet<string> explicitlyOrderedIDs = new(StringComparer.Ordinal);
        foreach (string deviceID in normalizedExplicitOrder)
        {
            if (!liveItemsByID.TryGetValue(deviceID, out PerformanceDeviceOrderItem item)) continue;
            resolved.Add(item);
            explicitlyOrderedIDs.Add(deviceID);
        }

        foreach (PerformanceDeviceKind kind in normalizedPriority)
        {
            List<PerformanceDeviceOrderItem> fallbackItems = [];
            foreach (PerformanceDeviceOrderItem item in liveItems)
            {
                if (item.Kind != kind || explicitlyOrderedIDs.Contains(item.ID)) continue;
                fallbackItems.Add(item);
            }

            if (fallbackItems.Count == 0) continue;
            fallbackItems.Sort(FallbackItemComparer);
            int insertionIndex = FindFallbackInsertionIndex(resolved, kind, normalizedPriority);
            resolved.InsertRange(insertionIndex, fallbackItems);
        }

        return resolved;
    }

    /// <summary>Moves the identified visible row and returns the complete persisted stable-ID order.</summary>
    public static List<string> Move(
        IReadOnlyList<PerformanceDeviceOrderItem> resolvedItems,
        IEnumerable<string>? explicitDeviceIDs,
        string deviceID,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(resolvedItems);

        string? normalizedDeviceID = NormalizeDeviceID(deviceID);
        if (normalizedDeviceID == null) return NormalizeExplicitOrder(explicitDeviceIDs);

        for (int sourceIndex = 0; sourceIndex < resolvedItems.Count; sourceIndex++)
        {
            string? candidateID = NormalizeDeviceID(resolvedItems[sourceIndex].ID);
            if (!string.Equals(candidateID, normalizedDeviceID, StringComparison.Ordinal)) continue;
            return MoveAt(resolvedItems, explicitDeviceIDs, sourceIndex, targetIndex);
        }

        return NormalizeExplicitOrder(explicitDeviceIDs);
    }

    /// <summary>
    /// Moves one visible row by final index and retains IDs for devices that are currently absent.
    /// </summary>
    public static List<string> MoveAt(
        IReadOnlyList<PerformanceDeviceOrderItem> resolvedItems,
        IEnumerable<string>? explicitDeviceIDs,
        int sourceIndex,
        int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(resolvedItems);

        List<string> visibleDeviceIDs = CreateVisibleDeviceIDs(resolvedItems);
        if ((uint)sourceIndex >= (uint)visibleDeviceIDs.Count)
            return NormalizeExplicitOrder(explicitDeviceIDs);

        int clampedTargetIndex = Math.Clamp(targetIndex, min: 0, visibleDeviceIDs.Count - 1);
        string movedDeviceID = visibleDeviceIDs[sourceIndex];
        visibleDeviceIDs.RemoveAt(sourceIndex);
        visibleDeviceIDs.Insert(clampedTargetIndex, movedDeviceID);
        return ReconcileWithStaleExplicitIDs(visibleDeviceIDs, explicitDeviceIDs);
    }

    /// <summary>Persists a caller-resolved visible order while retaining temporarily absent IDs.</summary>
    public static List<string> MergeVisibleOrder(
        IEnumerable<string> visibleDeviceIDs,
        IEnumerable<string>? explicitDeviceIDs)
    {
        ArgumentNullException.ThrowIfNull(visibleDeviceIDs);
        List<string> normalizedVisibleOrder = NormalizeExplicitOrder(visibleDeviceIDs);
        return ReconcileWithStaleExplicitIDs(normalizedVisibleOrder, explicitDeviceIDs);
    }

    private static List<PerformanceDeviceOrderItem> NormalizeItems(
        IReadOnlyList<PerformanceDeviceOrderItem> items)
    {
        List<PerformanceDeviceOrderItem> normalized = new(items.Count);
        HashSet<string> used = new(StringComparer.Ordinal);
        foreach (PerformanceDeviceOrderItem item in items)
        {
            string? normalizedID = NormalizeDeviceID(item.ID);
            if (normalizedID == null || !Enum.IsDefined(item.Kind) || !used.Add(normalizedID)) continue;
            normalized.Add(item with { ID = normalizedID });
        }

        return normalized;
    }

    private static List<string> CreateVisibleDeviceIDs(
        IReadOnlyList<PerformanceDeviceOrderItem> resolvedItems)
    {
        List<string> visibleDeviceIDs = new(resolvedItems.Count);
        HashSet<string> used = new(StringComparer.Ordinal);
        foreach (PerformanceDeviceOrderItem item in resolvedItems)
        {
            string? normalizedID = NormalizeDeviceID(item.ID);
            if (normalizedID == null || !used.Add(normalizedID)) continue;
            visibleDeviceIDs.Add(normalizedID);
        }

        return visibleDeviceIDs;
    }

    private static int FindFallbackInsertionIndex(
        IReadOnlyList<PerformanceDeviceOrderItem> resolved,
        PerformanceDeviceKind kind,
        IReadOnlyList<PerformanceDeviceKind> priority)
    {
        int lastSameKindIndex = -1;
        for (int itemIndex = 0; itemIndex < resolved.Count; itemIndex++)
        {
            if (resolved[itemIndex].Kind == kind)
                lastSameKindIndex = itemIndex;
        }

        if (lastSameKindIndex >= 0) return lastSameKindIndex + 1;

        int kindRank = PriorityRank(kind, priority);
        int insertionIndex = 0;
        for (int itemIndex = 0; itemIndex < resolved.Count; itemIndex++)
        {
            if (PriorityRank(resolved[itemIndex].Kind, priority) <= kindRank)
                insertionIndex = itemIndex + 1;
        }

        return insertionIndex;
    }

    private static int PriorityRank(
        PerformanceDeviceKind kind,
        IReadOnlyList<PerformanceDeviceKind> priority)
    {
        for (int priorityIndex = 0; priorityIndex < priority.Count; priorityIndex++)
        {
            if (priority[priorityIndex] == kind)
                return priorityIndex;
        }

        return int.MaxValue;
    }

    private static List<string> ReconcileWithStaleExplicitIDs(
        IReadOnlyList<string> visibleDeviceIDs,
        IEnumerable<string>? explicitDeviceIDs)
    {
        List<string> normalizedExplicitOrder = NormalizeExplicitOrder(explicitDeviceIDs);
        HashSet<string> visibleIDSet = new(visibleDeviceIDs, StringComparer.Ordinal);
        Dictionary<string, List<string>> staleIDsBeforeLiveID = new(StringComparer.Ordinal);
        List<string> pendingStaleIDs = [];

        foreach (string explicitDeviceID in normalizedExplicitOrder)
        {
            if (!visibleIDSet.Contains(explicitDeviceID))
            {
                pendingStaleIDs.Add(explicitDeviceID);
                continue;
            }

            if (pendingStaleIDs.Count == 0) continue;
            staleIDsBeforeLiveID[explicitDeviceID] = [.. pendingStaleIDs];
            pendingStaleIDs.Clear();
        }

        List<string> reconciled = new(visibleDeviceIDs.Count + pendingStaleIDs.Count);
        foreach (string visibleDeviceID in visibleDeviceIDs)
        {
            if (staleIDsBeforeLiveID.TryGetValue(visibleDeviceID, out List<string>? staleIDs))
                reconciled.AddRange(staleIDs);
            reconciled.Add(visibleDeviceID);
        }

        reconciled.AddRange(pendingStaleIDs);
        return reconciled;
    }

    private static string? NormalizeDeviceID(string? deviceID) =>
        string.IsNullOrWhiteSpace(deviceID) ? null : deviceID.Trim();
}
