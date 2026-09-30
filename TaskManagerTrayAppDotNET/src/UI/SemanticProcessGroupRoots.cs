namespace TaskManagerTrayAppDotNET.UI;

/// <summary>
/// Heads a semantic group with its root process, whose row shows the group totals above a Root line that keeps
/// the root process's own usage. Subgroup heads can follow the same pattern for their own subtrees.
/// </summary>
internal static class SemanticProcessGroupRoots
{
    /// <summary>Name of the line that shows the root process's own usage beneath its group totals.</summary>
    public const string RootLineName = "Root";

    /// <summary>Returns the group root whose subtree holds the group's display representative.</summary>
    public static ProcessInstanceKey ResolveRootInstanceKey(
        SemanticProcessForest forest,
        SemanticProcessGroup group)
    {
        ArgumentNullException.ThrowIfNull(forest);
        ArgumentNullException.ThrowIfNull(group);

        ProcessInstanceKey rootInstanceKey = group.RepresentativeInstanceKey;

        // Semantic parents are older members of the same group, so the walk is bounded by the group size
        for (int remainingEdges = group.Nodes.Length; remainingEdges > 0; remainingEdges--)
        {
            if (!forest.TryGetNode(rootInstanceKey, out SemanticProcessNode? node)
                || node?.ParentInstanceKey is not { } parentInstanceKey
                || !forest.TryGetNode(parentInstanceKey, out _))
                break;

            rootInstanceKey = parentInstanceKey;
        }

        return rootInstanceKey;
    }

    /// <summary>
    /// Lists each member's descendants in member order, given each member's parent index or -1. A member without
    /// children gets an empty array.
    /// </summary>
    public static int[][] CollectDescendants(ReadOnlySpan<int> parentMemberIndexes)
    {
        int memberCount = parentMemberIndexes.Length;
        List<int>?[] descendantLists = new List<int>?[memberCount];
        for (int memberIndex = 0; memberIndex < memberCount; memberIndex++)
        {
            // The walk is bounded by the member count so malformed parent cycles cannot hang it
            int ancestorIndex = parentMemberIndexes[memberIndex];
            for (int remainingEdges = memberCount;
                 (uint)ancestorIndex < (uint)memberCount && ancestorIndex != memberIndex && remainingEdges > 0;
                 remainingEdges--)
            {
                (descendantLists[ancestorIndex] ??= []).Add(memberIndex);
                ancestorIndex = parentMemberIndexes[ancestorIndex];
            }
        }

        int[][] descendants = new int[memberCount][];
        for (int memberIndex = 0; memberIndex < memberCount; memberIndex++)
            descendants[memberIndex] = descendantLists[memberIndex]?.ToArray() ?? [];
        return descendants;
    }

    /// <summary>
    /// Keeps a member's semantic parent, otherwise attaches it to the row heading its group. The heading row is
    /// the synthetic group row, or the root process when that root replaces it.
    /// </summary>
    public static ProcessInstanceKey? ResolveParentInstanceKey(
        SemanticProcessNode node,
        ProcessInstanceKey? groupHeadingInstanceKey)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.ParentInstanceKey.HasValue) return node.ParentInstanceKey;

        return groupHeadingInstanceKey == node.Facts.InstanceKey ? null : groupHeadingInstanceKey;
    }
}
