namespace BatteryTrayAppDotNET.Models;

public sealed record BatterySnapshot(
    bool BatteryPresent,
    int ChargePercentage,
    bool IsOnExternalPower,
    bool IsCharging,
    bool IsFullyCharged,
    float? ChargeRateWatts,
    float? DischargeRateWatts,
    float? DesignedCapacityMilliwattHours,
    float? FullChargeCapacityMilliwattHours,
    float? RemainingCapacityMilliwattHours,
    TimeSpan? WindowsEstimatedTimeRemaining)
{
    public static BatterySnapshot Unknown { get; } = new(
        BatteryPresent: false,
        ChargePercentage: 100,
        IsOnExternalPower: true,
        IsCharging: false,
        IsFullyCharged: false,
        ChargeRateWatts: null,
        DischargeRateWatts: null,
        DesignedCapacityMilliwattHours: null,
        FullChargeCapacityMilliwattHours: null,
        RemainingCapacityMilliwattHours: null,
        WindowsEstimatedTimeRemaining: null);

    public float? CurrentBatteryPowerWatts =>
        IsCharging ? ChargeRateWatts : DischargeRateWatts;

    public float? HealthPercent
    {
        get
        {
            if (DesignedCapacityMilliwattHours is not > 0 || FullChargeCapacityMilliwattHours is not > 0)
                return null;

            return Math.Clamp(
                FullChargeCapacityMilliwattHours.Value / DesignedCapacityMilliwattHours.Value * 100f,
                0f,
                100f);
        }
    }

    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (WindowsEstimatedTimeRemaining.HasValue) return WindowsEstimatedTimeRemaining;
            if (IsOnExternalPower || RemainingCapacityMilliwattHours is not > 0 || DischargeRateWatts is not > 0)
                return null;

            float hours = RemainingCapacityMilliwattHours.Value / 1000f / DischargeRateWatts.Value;
            return hours is >= 0 and < 720 ? TimeSpan.FromHours(hours) : null;
        }
    }
}
