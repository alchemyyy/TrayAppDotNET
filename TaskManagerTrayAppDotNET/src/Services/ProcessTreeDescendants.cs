namespace TaskManagerTrayAppDotNET.Services;

/// <summary>One process from a system snapshot, reduced to what tree collection needs.</summary>
internal readonly record struct ProcessTreeNode(int ProcessID, int ParentProcessID, long CreationTimeFileTime);

/// <summary>Collects the descendants of a process while validating every parent/child edge.</summary>
/// <remarks>
/// A parent PID is only a number: once the real parent exits, Windows can hand that PID to an unrelated process,
/// and the orphan's recorded parent then points at the newcomer. An edge counts only when both creation times are
/// known and the child was created strictly after its parent, which is the rule the displayed tree uses
/// (<see cref="SemanticProcessTreeBuilder"/>). A failing edge drops the child together with its whole subtree,
/// because nothing below it can be reached through a valid chain.
/// </remarks>
internal static class ProcessTreeDescendants
{
    /// <summary>Returns the validated descendants of the root in breadth-first order (parents before children).</summary>
    public static List<ProcessTreeNode> Collect(
        int rootProcessID,
        long rootCreationTimeFileTime,
        IReadOnlyCollection<ProcessTreeNode> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        List<ProcessTreeNode> descendants = [];
        if (rootCreationTimeFileTime == 0) return descendants;

        Dictionary<int, List<ProcessTreeNode>> childrenByParent = new(processes.Count);
        foreach (ProcessTreeNode process in processes)
        {
            if (process.ProcessID <= 0 || process.ProcessID == process.ParentProcessID) continue;
            if (!childrenByParent.TryGetValue(process.ParentProcessID, out List<ProcessTreeNode>? children))
            {
                children = [];
                childrenByParent.Add(process.ParentProcessID, children);
            }

            children.Add(process);
        }

        HashSet<int> visited = [rootProcessID];
        ProcessTreeNode root = new(rootProcessID, ParentProcessID: -1, rootCreationTimeFileTime);
        int parentIndex = -1;
        while (true)
        {
            ProcessTreeNode parent = parentIndex < 0 ? root : descendants[parentIndex];
            if (childrenByParent.TryGetValue(parent.ProcessID, out List<ProcessTreeNode>? children))
            {
                for (int childIndex = 0; childIndex < children.Count; childIndex++)
                {
                    ProcessTreeNode child = children[childIndex];
                    if (IsValidEdge(parent.CreationTimeFileTime, child.CreationTimeFileTime)
                        && visited.Add(child.ProcessID))
                        descendants.Add(child);
                }
            }

            parentIndex++;
            if (parentIndex >= descendants.Count) break;
        }

        return descendants;
    }

    /// <summary>Returns whether a child with the given creation time can really belong to the given parent.</summary>
    public static bool IsValidEdge(long parentCreationTimeFileTime, long childCreationTimeFileTime) =>
        parentCreationTimeFileTime != 0
        && childCreationTimeFileTime != 0
        && childCreationTimeFileTime > parentCreationTimeFileTime;
}
