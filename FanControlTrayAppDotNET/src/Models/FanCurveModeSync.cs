namespace FanControlTrayAppDotNET.Models;

/// <summary>
/// Preserves fan and group speed unit choices when curve assignments change.
/// </summary>
public static class FanCurveModeSync
{
    /// <summary>
    /// Keeps a direct fan assignment from forcing the fan's display unit.
    /// </summary>
    public static void ApplyToFan(Fan fan, Curve? curve)
    {
    }

    /// <summary>
    /// Keeps a group assignment from forcing group or fan display units.
    /// </summary>
    public static void ApplyToGroup(FanGroup group, IEnumerable<Fan> fans, Curve? curve)
    {
    }

    /// <summary>
    /// Curve mode changes are converted at use sites instead of mutating assignments.
    /// </summary>
    public static void ApplyToCurveAssignments(Curve curve, IEnumerable<Fan> fans, IEnumerable<FanGroup> groups)
    {
    }
}
