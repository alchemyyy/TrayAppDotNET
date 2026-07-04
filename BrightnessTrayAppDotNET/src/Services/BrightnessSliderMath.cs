namespace BrightnessTrayAppDotNET.Services;

internal static class BrightnessSliderMath
{
    private const double MinimumPercent = 0.0;
    private const double MaximumPercent = 100.0;

    /// <summary>
    /// Rounds a manual brightness value onto the integer percentage grid used by DDC writes.
    /// </summary>
    public static double RoundManualPercent(double value)
    {
        if (!double.IsFinite(value)) return MinimumPercent;
        return Math.Round(value);
    }

    /// <summary>
    /// Clamps a brightness value to the flyout slider range.
    /// </summary>
    public static double ClampPercent(double value)
    {
        if (!double.IsFinite(value)) return MinimumPercent;
        return Math.Clamp(value, MinimumPercent, MaximumPercent);
    }

    /// <summary>
    /// Converts user slider input to the canonical manual brightness value.
    /// </summary>
    public static double NormalizeManualPercent(double value) =>
        ClampPercent(RoundManualPercent(value));

    /// <summary>
    /// Computes the master slider value from the current hardware-functional rows.
    /// </summary>
    public static double ComputeMasterPercent(
        IEnumerable<MonitorInfo> monitors,
        MasterSliderMode mode,
        double fallback)
    {
        List<double> values = [];
        foreach (MonitorInfo monitor in monitors)
        {
            if (!monitor.IsHardwareFunctional) continue;
            values.Add(monitor.Brightness);
        }

        if (values.Count == 0) return fallback;

        return mode switch
        {
            MasterSliderMode.Lowest => values.Min(),
            MasterSliderMode.Highest => values.Max(),
            _ => values.Average(),
        };
    }
}
