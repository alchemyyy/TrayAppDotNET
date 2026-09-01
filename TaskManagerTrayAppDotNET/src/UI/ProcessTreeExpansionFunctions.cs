namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Provides bounded process-tree relationship checks for expansion commands.</summary>
internal static class ProcessTreeExpansionFunctions
{
    public static bool IsDescendantOrSelf(
        ReadOnlySpan<int> parentRowIndexes,
        int candidateRowIndex,
        int rootRowIndex)
    {
        if ((uint)candidateRowIndex >= (uint)parentRowIndexes.Length
            || (uint)rootRowIndex >= (uint)parentRowIndexes.Length)
            return false;

        int ancestorRowIndex = candidateRowIndex;
        int remainingEdges = parentRowIndexes.Length;
        while (ancestorRowIndex >= 0 && remainingEdges > 0)
        {
            if (ancestorRowIndex == rootRowIndex) return true;

            int parentRowIndex = parentRowIndexes[ancestorRowIndex];
            if ((uint)parentRowIndex >= (uint)parentRowIndexes.Length) return false;
            ancestorRowIndex = parentRowIndex;
            remainingEdges--;
        }

        return false;
    }
}
