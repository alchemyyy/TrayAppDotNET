namespace FanControlTrayAppDotNET.Models;

/// <summary>
/// Keeps fan and group speed units aligned with assigned curve units.
/// </summary>
public static class FanCurveModeSync
{
    /// <summary>
    /// Applies a curve's RPM mode to a direct fan assignment.
    /// </summary>
    public static void ApplyToFan(Fan fan, Curve? curve)
    {
        if (curve == null) return;
        fan.RPMMode = curve.RPMMode;
    }

    /// <summary>
    /// Applies a curve's RPM mode to a group and its current member fans.
    /// </summary>
    public static void ApplyToGroup(FanGroup group, IEnumerable<Fan> fans, Curve? curve)
    {
        if (curve == null) return;

        group.RPMMode = curve.RPMMode;
        foreach (Fan fan in fans)
        {
            if (!string.Equals(fan.Group, group.Name, StringComparison.OrdinalIgnoreCase)) continue;
            fan.RPMMode = curve.RPMMode;
        }
    }

    /// <summary>
    /// Reapplies a changed curve's RPM mode to every assignment that references it.
    /// </summary>
    public static void ApplyToCurveAssignments(Curve curve, IEnumerable<Fan> fans, IEnumerable<FanGroup> groups)
    {
        foreach (Fan fan in fans)
        {
            if (!string.IsNullOrWhiteSpace(fan.Group)) continue;
            if (!string.Equals(fan.AssignedCurveName, curve.CurveName, StringComparison.OrdinalIgnoreCase)) continue;
            fan.RPMMode = curve.RPMMode;
        }

        foreach (FanGroup group in groups)
        {
            if (!string.Equals(group.AssignedCurveName, curve.CurveName, StringComparison.OrdinalIgnoreCase)) continue;
            ApplyToGroup(group, fans, curve);
        }
    }
}
