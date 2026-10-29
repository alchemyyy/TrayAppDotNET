namespace BatteryTrayAppDotNET.Services;

/// <summary>A configured trigger whose condition happened on the latest battery reading.</summary>
public readonly record struct BatteryTriggerFiring(
    BatteryTriggerEntry Trigger,
    BatteryTriggerCondition Condition,
    BatteryTriggerAction Action);

/// <summary>
/// Turns consecutive battery readings into trigger firings. Conditions fire on transitions between readings,
/// so the first reading only sets the baseline. The level conditions match the flyout's low/critical coloring
/// (on battery at 20% or less, at 10% or less) and fire at 80% or more while plugged in. After one fires, it
/// re-arms only once the charge is <see cref="RearmMarginPercent"/> points back on the other side, so a reading
/// jittering at the boundary can't fire it twice. Readings without a battery leave the engine idle and drop
/// the baseline.
/// </summary>
public sealed class BatteryTriggerEngine
{
    public const int LowThresholdPercent = 20;
    public const int CriticalThresholdPercent = 10;
    public const int HighThresholdPercent = 80;
    public const int FullPercent = 100;
    public const int RearmMarginPercent = 3;

    private static readonly IReadOnlyList<BatteryTriggerCondition> NoConditions = [];
    private static readonly IReadOnlyList<BatteryTriggerFiring> NoFirings = [];

    private BatterySnapshot? _previous;
    private bool _lowArmed;
    private bool _criticalArmed;
    private bool _highArmed;
    private bool _fullArmed;

    /// <summary>
    /// Records a reading and returns the triggers it fires, in trigger list order. Triggers without a condition
    /// or an action never fire; several triggers sharing a condition each fire.
    /// </summary>
    public IReadOnlyList<BatteryTriggerFiring> Observe(BatterySnapshot snapshot, IReadOnlyList<BatteryTriggerEntry> triggers)
    {
        IReadOnlyList<BatteryTriggerCondition> conditions = ObserveConditions(snapshot);
        if (conditions.Count == 0) return NoFirings;

        List<BatteryTriggerFiring> firings = [];
        foreach (BatteryTriggerEntry trigger in triggers)
        {
            if (trigger is not { Condition: { } condition, Action: { } action }) continue;
            if (conditions.Contains(condition))
                firings.Add(new BatteryTriggerFiring(trigger, condition, action));
        }

        return firings;
    }

    /// <summary>Records a reading and returns the conditions it satisfies, in enum declaration order.</summary>
    public IReadOnlyList<BatteryTriggerCondition> ObserveConditions(BatterySnapshot snapshot)
    {
        if (!snapshot.BatteryPresent)
        {
            _previous = null;
            return NoConditions;
        }

        if (_previous is not { } previous)
        {
            SetBaseline(snapshot);
            return NoConditions;
        }

        _previous = snapshot;
        int level = snapshot.ChargePercentage;
        List<BatteryTriggerCondition> conditions = [];

        if (Crossed(ref _lowArmed, IsAtOrBelow(snapshot, LowThresholdPercent),
                rearm: level >= LowThresholdPercent + RearmMarginPercent))
            conditions.Add(BatteryTriggerCondition.BatteryBelow20);

        if (Crossed(ref _criticalArmed, IsAtOrBelow(snapshot, CriticalThresholdPercent),
                rearm: level >= CriticalThresholdPercent + RearmMarginPercent))
            conditions.Add(BatteryTriggerCondition.BatteryBelow10);

        if (Crossed(ref _highArmed, snapshot.IsOnExternalPower && level >= HighThresholdPercent,
                rearm: level <= HighThresholdPercent - RearmMarginPercent))
            conditions.Add(BatteryTriggerCondition.BatteryAbove80);

        if (!previous.IsCharging && snapshot.IsCharging)
            conditions.Add(BatteryTriggerCondition.ChargingStarted);
        if (previous.IsCharging && !snapshot.IsCharging)
            conditions.Add(BatteryTriggerCondition.ChargingStopped);
        if (!previous.IsOnExternalPower && snapshot.IsOnExternalPower)
            conditions.Add(BatteryTriggerCondition.ExternalPowerConnected);
        if (previous.IsOnExternalPower && !snapshot.IsOnExternalPower)
            conditions.Add(BatteryTriggerCondition.ExternalPowerDisconnected);

        // Full re-arms after unplugging or a real drop, so a battery trickle-charging at 99-100% fires once
        if (Crossed(ref _fullArmed, snapshot.IsFullyCharged,
                rearm: !snapshot.IsFullyCharged
                       && (!snapshot.IsOnExternalPower || level <= FullPercent - RearmMarginPercent)))
            conditions.Add(BatteryTriggerCondition.FullyCharged);

        return conditions;
    }

    // The state found at startup never fires: a level condition starts armed only when the charge is on its far side
    private void SetBaseline(BatterySnapshot snapshot)
    {
        _previous = snapshot;
        _lowArmed = snapshot.ChargePercentage > LowThresholdPercent;
        _criticalArmed = snapshot.ChargePercentage > CriticalThresholdPercent;
        _highArmed = snapshot.ChargePercentage < HighThresholdPercent;
        _fullArmed = !snapshot.IsFullyCharged;
    }

    private static bool IsAtOrBelow(BatterySnapshot snapshot, int thresholdPercent) =>
        !snapshot.IsOnExternalPower && snapshot.ChargePercentage <= thresholdPercent;

    private static bool Crossed(ref bool armed, bool qualifies, bool rearm)
    {
        if (armed)
        {
            if (!qualifies) return false;
            armed = false;
            return true;
        }

        if (rearm) armed = true;
        return false;
    }
}
