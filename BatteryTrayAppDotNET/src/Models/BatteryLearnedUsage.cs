namespace BatteryTrayAppDotNET.Models;

/// <summary>Observed battery usage summarized for persistence: how long it covers and its average discharge rate.</summary>
public readonly record struct BatteryLearnedUsage(double Seconds, double Watts)
{
    public bool IsUsable => Seconds > 0 && double.IsFinite(Seconds) && Watts > 0 && double.IsFinite(Watts);
}
