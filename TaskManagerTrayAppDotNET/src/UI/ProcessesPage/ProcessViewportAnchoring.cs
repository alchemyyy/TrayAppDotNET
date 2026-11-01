namespace TaskManagerTrayAppDotNET.UI.ProcessesPage;

/// <summary>
/// Identifies one process row, its position before a table projection changes, and its semantic section
/// (0 for none, else its classification plus one).
/// </summary>
internal readonly record struct ProcessViewportAnchor(
    ProcessInstanceKey Process,
    double RowTop,
    double ContentHeight,
    byte SemanticSection)
{
    private const double PositionEqualityTolerance = 0.01;

    /// <summary>
    /// Creates the scroll correction needed to retain the original visual position. Without
    /// <paramref name="followSectionChange"/>, a row that moved to another semantic section is not followed.
    /// </summary>
    public ProcessViewportAnchorAdjustment? ResolveAdjustment(
        double nextRowTop,
        double nextContentHeight,
        byte nextSemanticSection,
        bool followSectionChange)
    {
        if (!followSectionChange && nextSemanticSection != SemanticSection) return null;

        if (!double.IsFinite(RowTop)
            || !double.IsFinite(ContentHeight)
            || !double.IsFinite(nextRowTop)
            || !double.IsFinite(nextContentHeight))
            return null;

        double verticalOffsetDelta = nextRowTop - RowTop;
        if (Math.Abs(verticalOffsetDelta) < PositionEqualityTolerance) return null;

        return new ProcessViewportAnchorAdjustment(
            verticalOffsetDelta,
            Math.Abs(nextContentHeight - ContentHeight) >= PositionEqualityTolerance);
    }
}

/// <summary>Requests one additive viewport correction after a process projection changes.</summary>
internal readonly record struct ProcessViewportAnchorAdjustment(
    double VerticalOffsetDelta,
    bool ContentHeightChanged);
