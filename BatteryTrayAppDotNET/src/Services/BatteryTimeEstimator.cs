using BatteryTrayAppDotNET.Models;

namespace BatteryTrayAppDotNET.Services;

/// <summary>Estimates battery time from monotonic observations without querying hardware or wall-clock time.</summary>
public sealed class BatteryTimeEstimator
{
    public const int CheckIntervalSeconds = 5;
    public const int DefaultDischargeChecks = 24;
    public const double DefaultHalfLifeChecks = 6;
    public const int MaximumDischargeChecks = 720;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(CheckIntervalSeconds);
    private static readonly TimeSpan MaximumSampleGap = TimeSpan.FromMinutes(1);

    // Predicted life learns from the most recent 30 minutes spent on battery, however long ago they were.
    // Time on external power neither adds to nor ages that usage, so the prediction carries through charging.
    private static readonly double PredictionWindowSeconds = TimeSpan.FromMinutes(30).TotalSeconds;
    private static readonly double PredictionWarmupSeconds = TimeSpan.FromMinutes(2).TotalSeconds;

    private readonly List<RateSample> _rateSamples = [];
    private readonly List<UsageInterval> _usageHistory = [];
    private BatterySnapshot _snapshot = BatterySnapshot.Unknown;
    private BatteryMode _mode;
    private TimeSpan? _lastObservation;
    private TimeSpan? _lastCheck;
    private RateSample? _previousDischarge;
    private int _dischargeChecks;
    private double _halfLifeChecks;

    public BatteryTimeEstimator(
        int dischargeChecks = DefaultDischargeChecks,
        double halfLifeChecks = DefaultHalfLifeChecks)
    {
        SetConfiguration(dischargeChecks, halfLifeChecks);
    }

    public BatteryTimeEstimates Estimates { get; private set; } = BatteryTimeEstimates.Unknown;

    /// <summary>The retained battery usage behind Predicted life, or null before any has been observed.</summary>
    public BatteryLearnedUsage? LearnedUsage
    {
        get
        {
            (double seconds, double wattSeconds) = UsageTotals();
            return seconds > 0 ? new BatteryLearnedUsage(seconds, wattSeconds / seconds) : null;
        }
    }

    /// <summary>
    /// Seeds usage learned before this instance existed. It counts as the oldest usage, so new discharge displaces
    /// it first once the prediction window is full.
    /// </summary>
    public BatteryTimeEstimates RestoreLearnedUsage(BatteryLearnedUsage usage)
    {
        if (usage.IsUsable)
        {
            _usageHistory.Insert(index: 0, new UsageInterval(Math.Min(usage.Seconds, PredictionWindowSeconds), usage.Watts));
            TrimUsageToWindow();
        }

        return Estimates = Calculate(_lastObservation ?? TimeSpan.Zero);
    }

    /// <summary>
    /// Accepts at most one rate sample per five seconds. Extra refreshes update current state/capacity only;
    /// a power-source transition immediately starts a new rate window.
    /// </summary>
    public BatteryTimeEstimates Observe(BatterySnapshot snapshot, TimeSpan monotonicTime)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        PrepareTime(monotonicTime);

        BatteryMode mode = ResolveMode(snapshot);
        if (mode != _mode)
        {
            ResetRateWindow();
            _mode = mode;
        }

        if (mode == BatteryMode.Absent) _usageHistory.Clear();
        _snapshot = snapshot;

        if (!_lastCheck.HasValue || monotonicTime - _lastCheck.Value >= CheckInterval)
        {
            double? watts = CurrentRate(snapshot, mode);
            if (mode is BatteryMode.Discharging or BatteryMode.Charging)
            {
                _rateSamples.Add(new RateSample(monotonicTime, watts));
                if (_rateSamples.Count > MaximumDischargeChecks) _rateSamples.RemoveAt(0);
            }

            // Hold the previously observed rate only across contiguous, valid discharge checks.
            // Never attribute charging, unknown-rate intervals, or time spent asleep to battery usage.
            if (mode == BatteryMode.Discharging && watts.HasValue && _previousDischarge is { Watts: { } previousWatts } previous)
                AddUsage((monotonicTime - previous.Time).TotalSeconds, previousWatts);

            _previousDischarge = mode == BatteryMode.Discharging && watts.HasValue
                ? new RateSample(monotonicTime, watts)
                : null;
            _lastCheck = monotonicTime;
        }

        return Estimates = Calculate(monotonicTime);
    }

    /// <summary>Reweights retained observations immediately, without counting a settings change as a check.</summary>
    public BatteryTimeEstimates Configure(int dischargeChecks, double halfLifeChecks, TimeSpan monotonicTime)
    {
        SetConfiguration(dischargeChecks, halfLifeChecks);
        PrepareTime(monotonicTime);
        return Estimates = Calculate(monotonicTime);
    }

    private void SetConfiguration(int dischargeChecks, double halfLifeChecks)
    {
        _dischargeChecks = Math.Clamp(dischargeChecks, min: 1, MaximumDischargeChecks);
        _halfLifeChecks = double.IsFinite(halfLifeChecks)
            ? Math.Clamp(halfLifeChecks, min: 0.5, MaximumDischargeChecks)
            : DefaultHalfLifeChecks;
    }

    private void PrepareTime(TimeSpan now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(now, TimeSpan.Zero);
        if (_lastObservation.HasValue && now < _lastObservation.Value)
        {
            ResetRateWindow();
            _usageHistory.Clear();
        }
        else if (_lastCheck.HasValue && now - _lastCheck.Value > MaximumSampleGap)
        {
            ResetRateWindow();
        }

        _lastObservation = now;
    }

    private void ResetRateWindow()
    {
        _rateSamples.Clear();
        _previousDischarge = null;
        _lastCheck = null;
    }

    private void AddUsage(double seconds, double watts)
    {
        if (!(seconds > 0) || !double.IsFinite(seconds)) return;

        _usageHistory.Add(new UsageInterval(seconds, watts));
        TrimUsageToWindow();
    }

    /// <summary>Drops the oldest usage, splitting the boundary interval, so exactly one window remains.</summary>
    private void TrimUsageToWindow()
    {
        double excess = UsageTotals().Seconds - PredictionWindowSeconds;
        while (excess > 0 && _usageHistory.Count > 0)
        {
            UsageInterval oldest = _usageHistory[0];
            if (oldest.Seconds > excess)
            {
                _usageHistory[0] = oldest with { Seconds = oldest.Seconds - excess };
                return;
            }

            _usageHistory.RemoveAt(0);
            excess -= oldest.Seconds;
        }
    }

    private BatteryTimeEstimates Calculate(TimeSpan now)
    {
        if (_mode == BatteryMode.Absent) return BatteryTimeEstimates.Unknown;

        double? remainingWh = CapacityWh(_snapshot.RemainingCapacityMilliwattHours);
        // Learned usage describes the machine, not the power source, so a charging or full battery still reports
        // how long its current charge would last once unplugged.
        TimeSpan? predictedLife = Duration(remainingWh, LearnedDischargeRate());
        if (_snapshot.IsOnExternalPower && _snapshot.IsFullyCharged)
            return new BatteryTimeEstimates(predictedLife, null, TimeSpan.Zero);

        if (_mode == BatteryMode.Discharging)
        {
            // Keep the learned prediction independent of a transient invalid current-power reading.
            // Such a reading must not present an older usable rate as present discharge activity.
            return new BatteryTimeEstimates(
                predictedLife,
                CurrentRate(_snapshot, _mode).HasValue
                    ? Duration(remainingWh, SmoothedRate(now, _dischargeChecks, _halfLifeChecks))
                    : null,
                null);
        }

        if (_mode != BatteryMode.Charging || !CurrentRate(_snapshot, _mode).HasValue)
            return new BatteryTimeEstimates(predictedLife, null, null);

        double? fullWh = CapacityWh(_snapshot.FullChargeCapacityMilliwattHours);
        if (!remainingWh.HasValue || fullWh is not > 0) return new BatteryTimeEstimates(predictedLife, null, null);

        // Charging has its own fresh rate window and fixed smoothing. Discharge preferences do not alter it.
        double toChargeWh = Math.Max(val1: 0, fullWh.Value - remainingWh.Value);
        return new BatteryTimeEstimates(predictedLife, null,
            Duration(toChargeWh, SmoothedRate(now, DefaultDischargeChecks, DefaultHalfLifeChecks)));
    }

    private double? SmoothedRate(TimeSpan now, int checks, double halfLifeChecks)
    {
        double weightedWatts = 0;
        double weights = 0;
        int start = Math.Max(val1: 0, _rateSamples.Count - checks);
        for (int i = start; i < _rateSamples.Count; i++)
        {
            RateSample sample = _rateSamples[i];
            if (!sample.Watts.HasValue) continue;

            double ageChecks = (now - sample.Time).TotalSeconds / CheckIntervalSeconds;
            double weight = Math.Pow(x: 0.5, ageChecks / halfLifeChecks);
            weightedWatts += sample.Watts.Value * weight;
            weights += weight;
        }

        return weights > 0 ? weightedWatts / weights : null;
    }

    private double? LearnedDischargeRate()
    {
        (double seconds, double wattSeconds) = UsageTotals();
        return seconds >= PredictionWarmupSeconds ? wattSeconds / seconds : null;
    }

    private (double Seconds, double WattSeconds) UsageTotals()
    {
        double seconds = 0;
        double wattSeconds = 0;
        foreach (UsageInterval interval in _usageHistory)
        {
            seconds += interval.Seconds;
            wattSeconds += interval.Watts * interval.Seconds;
        }

        return (seconds, wattSeconds);
    }

    private static BatteryMode ResolveMode(BatterySnapshot snapshot)
    {
        if (!snapshot.BatteryPresent) return BatteryMode.Absent;
        if (snapshot.IsOnExternalPower) return snapshot.IsCharging ? BatteryMode.Charging : BatteryMode.Idle;
        return snapshot.IsCharging ? BatteryMode.Idle : BatteryMode.Discharging;
    }

    private static double? CurrentRate(BatterySnapshot snapshot, BatteryMode mode)
    {
        float? rate = mode switch
        {
            BatteryMode.Discharging => snapshot.DischargeRateWatts,
            BatteryMode.Charging => snapshot.ChargeRateWatts,
            _ => null
        };
        return rate is > 0 && float.IsFinite(rate.Value) ? rate.Value : null;
    }

    private static double? CapacityWh(float? milliwattHours) =>
        milliwattHours is >= 0 && float.IsFinite(milliwattHours.Value) ? milliwattHours.Value / 1000.0 : null;

    private static TimeSpan? Duration(double? wattHours, double? watts)
    {
        if (!wattHours.HasValue || watts is not > 0) return null;
        double hours = wattHours.Value / watts.Value;
        return double.IsFinite(hours) && hours >= 0 && hours < TimeSpan.MaxValue.TotalHours
            ? TimeSpan.FromHours(hours)
            : null;
    }

    private enum BatteryMode { Absent, Idle, Discharging, Charging }
    private readonly record struct RateSample(TimeSpan Time, double? Watts);
    private readonly record struct UsageInterval(double Seconds, double Watts);
}
