namespace BatteryTrayAppDotNET.Models;

public enum BatteryPowerMode
{
    Ultimate,
    Balanced,
    PowerSaver
}

/// <summary>A successful null mode is a custom power scheme; a failed read is unavailable state.</summary>
public readonly record struct BatteryPowerModeReadResult(bool Success, BatteryPowerMode? Mode);
