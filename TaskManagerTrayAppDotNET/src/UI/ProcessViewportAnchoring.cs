using TaskManagerTrayAppDotNET.Models;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Identifies one process row and its position before a table projection changes.</summary>
internal readonly record struct ProcessViewportAnchor(
    ProcessInstanceKey Process,
    double RowTop,
    double ContentHeight)
{
    private const double PositionEqualityTolerance = 0.01;

    /// <summary>Gives selection priority over the transient hover fallback.</summary>
    public static ProcessInstanceKey? ResolveProcessIdentity(
        ProcessInstanceKey? selectedProcess,
        ProcessInstanceKey? hoveredProcess) =>
        selectedProcess ?? hoveredProcess;

    /// <summary>Creates the scroll correction needed to retain the original visual position.</summary>
    public ProcessViewportAnchorAdjustment? ResolveAdjustment(
        double nextRowTop,
        double nextContentHeight)
    {
        if (!double.IsFinite(RowTop)
            || !double.IsFinite(ContentHeight)
            || !double.IsFinite(nextRowTop)
            || !double.IsFinite(nextContentHeight))
        {
            return null;
        }

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
