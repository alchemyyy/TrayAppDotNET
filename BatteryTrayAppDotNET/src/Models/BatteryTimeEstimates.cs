namespace BatteryTrayAppDotNET.Models;

/// <summary>Independent estimates from recent observed battery power; null means insufficient usable data.</summary>
public sealed record BatteryTimeEstimates(
    TimeSpan? PredictedLife,
    TimeSpan? PresentDischarge,
    TimeSpan? ChargeTime)
{
    public static BatteryTimeEstimates Unknown { get; } = new(null, null, null);
}
