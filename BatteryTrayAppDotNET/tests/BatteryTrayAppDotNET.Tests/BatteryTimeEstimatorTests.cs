using BatteryTrayAppDotNET.Models;
using BatteryTrayAppDotNET.Services;
using Xunit;

namespace BatteryTrayAppDotNET.Tests;

public sealed class BatteryTimeEstimatorTests
{
    [Fact]
    public void PresentDischargeConvertsMilliwattHoursAndIgnoresWindowsLifetime()
    {
        BatteryTimeEstimates estimates = new BatteryTimeEstimator().Observe(Discharging(watts: 10), At(0));

        Hours(expected: 2, estimates.PresentDischarge);
        Assert.Null(estimates.PredictedLife);
        Assert.Null(estimates.ChargeTime);
    }

    [Fact]
    public void StepInLoadUsesExponentialWeightAndRecentCheckWindow()
    {
        BatteryTimeEstimator estimator = new(dischargeChecks: 2, halfLifeChecks: 1);
        estimator.Observe(Discharging(watts: 10), At(0));
        BatteryTimeEstimates step = estimator.Observe(Discharging(watts: 30), At(5));
        Hours(20 / ((10 * 0.5 + 30) / 1.5), step.PresentDischarge);

        BatteryTimeEstimates next = estimator.Observe(Discharging(watts: 30), At(10));
        Hours(20.0 / 30, next.PresentDischarge);
    }

    [Fact]
    public void DecayUsesElapsedTimeWhenChecksAreUneven()
    {
        BatteryTimeEstimator estimator = new(dischargeChecks: 24, halfLifeChecks: 1);
        estimator.Observe(Discharging(watts: 10), At(0));
        estimator.Observe(Discharging(watts: 30), At(5));
        BatteryTimeEstimates estimates = estimator.Observe(Discharging(watts: 30), At(15));

        double weightedRate = (10 * 0.125 + 30 * 0.25 + 30) / (0.125 + 0.25 + 1);
        Hours(20 / weightedRate, estimates.PresentDischarge);
    }

    [Fact]
    public void WindowIncludesLastNChecksEvenWhenTheySpanMoreThanNominalCheckDuration()
    {
        BatteryTimeEstimator estimator = new(dischargeChecks: 2, halfLifeChecks: 6);
        estimator.Observe(Discharging(watts: 10), At(0));
        BatteryTimeEstimates estimates = estimator.Observe(Discharging(watts: 30), At(20));

        double olderWeight = Math.Pow(0.5, 4.0 / 6);
        Hours(20 / ((10 * olderWeight + 30) / (olderWeight + 1)), estimates.PresentDischarge);
    }

    [Fact]
    public void ForceRefreshesDoNotOverweightRatesBetweenChecks()
    {
        BatteryTimeEstimator estimator = new();
        estimator.Observe(Discharging(watts: 10), At(0));
        for (int second = 1; second < 5; second++)
            Hours(expected: 2, estimator.Observe(Discharging(watts: 100), At(second)).PresentDischarge);

        Hours(expected: 2, estimator.Observe(Discharging(watts: 10), At(5)).PresentDischarge);
    }

    [Fact]
    public void ForceRefreshStillUpdatesCapacityWithoutAddingRateSample()
    {
        BatteryTimeEstimator estimator = new();
        estimator.Observe(Discharging(watts: 10), At(0));
        BatteryTimeEstimates estimates = estimator.Observe(Discharging(watts: 100, remaining: 10_000), At(1));

        Hours(expected: 1, estimates.PresentDischarge);
    }

    [Fact]
    public void ConfigurationReweightsExistingSamplesWithoutRecordingAnotherCheck()
    {
        BatteryTimeEstimator estimator = new(dischargeChecks: 1, halfLifeChecks: 1);
        estimator.Observe(Discharging(watts: 10), At(0));
        estimator.Observe(Discharging(watts: 30), At(5));

        BatteryTimeEstimates expanded = estimator.Configure(dischargeChecks: 2, halfLifeChecks: 1, At(5));
        Hours(20 / ((10 * 0.5 + 30) / 1.5), expanded.PresentDischarge);

        BatteryTimeEstimates slowerDecay = estimator.Configure(dischargeChecks: 2, halfLifeChecks: 2, At(5));
        double olderWeight = Math.Pow(0.5, 0.5);
        Hours(20 / ((10 * olderWeight + 30) / (olderWeight + 1)), slowerDecay.PresentDischarge);

        BatteryTimeEstimates narrowed = estimator.Configure(dischargeChecks: 1, halfLifeChecks: 2, At(5));
        Hours(20.0 / 30, narrowed.PresentDischarge);
    }

    [Fact]
    public void InvalidConfigurationIsClampedAndNonfiniteHalfLifeUsesDefault()
    {
        BatteryTimeEstimator estimator = new(dischargeChecks: 2, halfLifeChecks: 1);
        estimator.Observe(Discharging(watts: 10), At(0));
        estimator.Observe(Discharging(watts: 30), At(5));

        Hours(20.0 / 30, estimator.Configure(dischargeChecks: 0, halfLifeChecks: 0, At(5)).PresentDischarge);
        Hours(20 / ((10 * 0.25 + 30) / 1.25),
            estimator.Configure(dischargeChecks: 2, halfLifeChecks: 0.1, At(5)).PresentDischarge);
        double weight = Math.Pow(0.5, 1.0 / BatteryTimeEstimator.DefaultHalfLifeChecks);
        Hours(20 / ((10 * weight + 30) / (weight + 1)),
            estimator.Configure(dischargeChecks: 2, halfLifeChecks: double.NaN, At(5)).PresentDischarge);
    }

    [Fact]
    public void PredictionWaitsForTwoMinutesOfObservedUsage()
    {
        BatteryTimeEstimator estimator = new();
        for (int second = 0; second < 120; second += 5)
            Assert.Null(estimator.Observe(Discharging(watts: 10), At(second)).PredictedLife);

        Hours(expected: 2, estimator.Observe(Discharging(watts: 10), At(120)).PredictedLife);
    }

    [Fact]
    public void LearnedPredictionWeightsUsageByObservedDuration()
    {
        BatteryTimeEstimator estimator = new();
        estimator.Observe(Discharging(watts: 10), At(0));
        estimator.Observe(Discharging(watts: 30), At(10));
        estimator.Observe(Discharging(watts: 30), At(65));
        BatteryTimeEstimates estimates = estimator.Observe(Discharging(watts: 30), At(120));

        double learnedRate = (10 * 10.0 + 30 * 110.0) / 120;
        Hours(20 / learnedRate, estimates.PredictedLife);
        Assert.NotEqual(estimates.PredictedLife, estimates.PresentDischarge);
    }

    [Fact]
    public void LearnedPredictionRemainsSeparateFromCurrentWindowSettings()
    {
        BatteryTimeEstimator estimator = LearnConstantUsage(watts: 10);
        BatteryTimeEstimates estimates = estimator.Observe(Discharging(watts: 40), At(125));
        Hours(expected: 2, estimates.PredictedLife);

        BatteryTimeEstimates changed = estimator.Configure(dischargeChecks: 1, halfLifeChecks: 0.5, At(125));
        Hours(expected: 2, changed.PredictedLife);
        Hours(expected: 0.5, changed.PresentDischarge);
    }

    [Fact]
    public void LearnedPredictionDropsUsageOutsideThirtyMinuteWindow()
    {
        BatteryTimeEstimator estimator = new();
        for (int second = 0; second <= 1800; second += 5)
            estimator.Observe(Discharging(watts: 10), At(second));
        for (int second = 1805; second <= 3605; second += 5)
            estimator.Observe(Discharging(watts: 20), At(second));

        Hours(expected: 1, estimator.Estimates.PredictedLife);
    }

    [Fact]
    public void SleepGapResetsCurrentRateAndDoesNotInventObservedUsage()
    {
        BatteryTimeEstimator estimator = LearnConstantUsage(watts: 10);
        BatteryTimeEstimates resumed = estimator.Observe(Discharging(watts: 40), At(190));

        Hours(expected: 0.5, resumed.PresentDischarge);
        Hours(expected: 2, resumed.PredictedLife);

        BatteryTimeEstimates next = estimator.Observe(Discharging(watts: 40), At(195));
        Hours(20 / ((120 * 10.0 + 5 * 40.0) / 125), next.PredictedLife);
    }

    [Fact]
    public void LongUnobservedTimeKeepsTheMostRecentBatteryUsage()
    {
        BatteryTimeEstimator estimator = LearnConstantUsage(watts: 10);
        BatteryTimeEstimates estimates = estimator.Observe(Discharging(watts: 40), At(2000));

        Hours(expected: 2, estimates.PredictedLife);
        Hours(expected: 0.5, estimates.PresentDischarge);
    }

    [Fact]
    public void PowerTransitionsImmediatelyResetRateWindowsAndRetainRecentLearning()
    {
        BatteryTimeEstimator estimator = LearnConstantUsage(watts: 10);
        BatteryTimeEstimates charging = estimator.Observe(Charging(watts: 20), At(121));
        Hours(expected: 1, charging.PredictedLife);
        Assert.Null(charging.PresentDischarge);
        Hours(expected: 1.5, charging.ChargeTime);

        BatteryTimeEstimates battery = estimator.Observe(Discharging(watts: 40), At(122));
        Hours(expected: 2, battery.PredictedLife);
        Hours(expected: 0.5, battery.PresentDischarge);
        Assert.Null(battery.ChargeTime);
    }

    [Fact]
    public void ChargingKeepsPredictingPastThePredictionWindow()
    {
        BatteryTimeEstimator estimator = LearnConstantUsage(watts: 10);
        for (int second = 125; second <= 125 + 3600; second += 5)
            estimator.Observe(Charging(watts: 20), At(second));

        // Time on external power neither adds to nor ages the learned usage
        Hours(expected: 1, estimator.Estimates.PredictedLife);
        Assert.Equal(new BatteryLearnedUsage(Seconds: 120, Watts: 10), estimator.LearnedUsage);
    }

    [Fact]
    public void ExternalPowerPredictsLifeOfTheCurrentCharge()
    {
        BatteryTimeEstimator estimator = LearnConstantUsage(watts: 10);
        BatterySnapshot idle = Charging(watts: null) with { IsCharging = false };
        BatteryTimeEstimates plugged = estimator.Observe(idle, At(125));
        Hours(expected: 1, plugged.PredictedLife);
        Assert.Null(plugged.ChargeTime);

        BatteryTimeEstimates full = estimator.Observe(idle with
        {
            IsFullyCharged = true,
            RemainingCapacityMilliwattHours = 40_000
        }, At(130));
        Hours(expected: 4, full.PredictedLife);
        Assert.Equal(TimeSpan.Zero, full.ChargeTime);
    }

    [Fact]
    public void RestoredUsagePredictsBeforeAnyDischargeIsObserved()
    {
        BatteryTimeEstimator estimator = new();
        estimator.RestoreLearnedUsage(new BatteryLearnedUsage(Seconds: 600, Watts: 10));

        Hours(expected: 1, estimator.Observe(Charging(watts: 20), At(0)).PredictedLife);
    }

    [Fact]
    public void RestoredUsageShorterThanWarmupWaitsForMoreBatteryUse()
    {
        BatteryTimeEstimator estimator = new();
        estimator.RestoreLearnedUsage(new BatteryLearnedUsage(Seconds: 60, Watts: 10));
        Assert.Null(estimator.Observe(Charging(watts: 20), At(0)).PredictedLife);

        for (int second = 5; second < 65; second += 5)
            Assert.Null(estimator.Observe(Discharging(watts: 10), At(second)).PredictedLife);
        Hours(expected: 2, estimator.Observe(Discharging(watts: 10), At(65)).PredictedLife);
    }

    [Fact]
    public void NewDischargeDisplacesRestoredUsageFirst()
    {
        BatteryTimeEstimator estimator = new();
        estimator.RestoreLearnedUsage(new BatteryLearnedUsage(Seconds: 1800, Watts: 10));
        for (int second = 0; second <= 900; second += 5)
            estimator.Observe(Discharging(watts: 30), At(second));

        // Half the window is still restored usage at 10 W; the newer half is 30 W
        Hours(expected: 1, estimator.Estimates.PredictedLife);
        Assert.Equal(1800, estimator.LearnedUsage.GetValueOrDefault().Seconds, precision: 8);
    }

    [Fact]
    public void LearnedUsageSummarizesTheRetainedWindow()
    {
        Assert.Null(new BatteryTimeEstimator().LearnedUsage);

        BatteryLearnedUsage? usage = LearnConstantUsage(watts: 10).LearnedUsage;
        Assert.Equal(new BatteryLearnedUsage(Seconds: 120, Watts: 10), usage);
    }

    [Theory]
    [InlineData(double.NaN, 10)]
    [InlineData(-5, 10)]
    [InlineData(0, 10)]
    [InlineData(600, 0)]
    [InlineData(600, double.PositiveInfinity)]
    public void UnusableRestoredUsageIsIgnored(double seconds, double watts)
    {
        BatteryTimeEstimator estimator = new();
        estimator.RestoreLearnedUsage(new BatteryLearnedUsage(seconds, watts));

        Assert.Null(estimator.LearnedUsage);
    }

    [Fact]
    public void StoreRoundTripsUsageIndependentOfCulture()
    {
        BatteryLearnedUsage usage = new(Seconds: 1234.5, Watts: 9.87654321);
        string text = BatteryLearnedUsageStore.Format(usage);

        Assert.DoesNotContain(",", text);
        Assert.Equal(usage, BatteryLearnedUsageStore.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1800")]
    [InlineData("1800 12 3")]
    [InlineData("1800 twelve")]
    [InlineData("-1 12")]
    [InlineData("1800 NaN")]
    public void StoreRejectsMalformedOrUnusableText(string text)
    {
        Assert.Null(BatteryLearnedUsageStore.Parse(text));
    }

    [Fact]
    public void StoreSavesAndLoadsAFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"BatteryLearnedUsageStore-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "learned-battery-usage.txt");
        try
        {
            Assert.Null(BatteryLearnedUsageStore.Load(path));

            BatteryLearnedUsage usage = new(Seconds: 1800, Watts: 12.5);
            BatteryLearnedUsageStore.Save(path, usage);
            Assert.Equal(usage, BatteryLearnedUsageStore.Load(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BatteryRemovalClearsLearningForReplacementBattery()
    {
        BatteryTimeEstimator estimator = LearnConstantUsage(watts: 10);
        Assert.Equal(BatteryTimeEstimates.Unknown, estimator.Observe(BatterySnapshot.Unknown, At(121)));

        BatteryTimeEstimates replacement = estimator.Observe(Discharging(watts: 10), At(122));
        Assert.Null(replacement.PredictedLife);
        Hours(expected: 2, replacement.PresentDischarge);
    }

    [Fact]
    public void ChargingUsesCapacityDeficitAndItsOwnSmoothedPositiveRate()
    {
        BatteryTimeEstimator estimator = new(dischargeChecks: 1, halfLifeChecks: 0.5);
        Hours(expected: 3, estimator.Observe(Charging(watts: 10), At(0)).ChargeTime);
        BatteryTimeEstimates estimates = estimator.Observe(Charging(watts: 30), At(5));
        double olderWeight = Math.Pow(0.5, 1.0 / BatteryTimeEstimator.DefaultHalfLifeChecks);
        double smoothedWatts = (10 * olderWeight + 30) / (olderWeight + 1);

        Hours(30 / smoothedWatts, estimates.ChargeTime);
        Assert.Null(estimates.PredictedLife);
        Assert.Null(estimates.PresentDischarge);
    }

    [Fact]
    public void IdleExternalPowerHasNoEstimateAndFullChargeHasZeroChargeTime()
    {
        BatteryTimeEstimator estimator = new();
        BatterySnapshot idle = Charging(watts: null) with { IsCharging = false };
        Assert.Equal(BatteryTimeEstimates.Unknown, estimator.Observe(idle, At(0)));

        BatteryTimeEstimates full = estimator.Observe(idle with { IsFullyCharged = true }, At(5));
        Assert.Equal(TimeSpan.Zero, full.ChargeTime);
        Assert.Null(full.PredictedLife);
        Assert.Null(full.PresentDischarge);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidCurrentRateDoesNotReusePreviousRate(float? watts)
    {
        BatteryTimeEstimator discharge = new();
        discharge.Observe(Discharging(watts: 10), At(0));
        Assert.Null(discharge.Observe(Discharging(watts), At(5)).PresentDischarge);

        BatteryTimeEstimator charge = new();
        charge.Observe(Charging(watts: 10), At(0));
        Assert.Null(charge.Observe(Charging(watts), At(5)).ChargeTime);
    }

    [Fact]
    public void InvalidCurrentRateDoesNotDiscardSeparatelyLearnedPrediction()
    {
        BatteryTimeEstimator estimator = LearnConstantUsage(watts: 10);
        BatteryTimeEstimates estimates = estimator.Observe(Discharging(watts: null), At(125));

        Hours(expected: 2, estimates.PredictedLife);
        Assert.Null(estimates.PresentDischarge);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidRemainingCapacityDoesNotProduceTime(float? capacity)
    {
        BatteryTimeEstimator discharge = new();
        Assert.Null(discharge.Observe(Discharging(watts: 10) with
        {
            RemainingCapacityMilliwattHours = capacity
        }, At(0)).PresentDischarge);

        BatteryTimeEstimator charge = new();
        Assert.Null(charge.Observe(Charging(watts: 10) with
        {
            RemainingCapacityMilliwattHours = capacity
        }, At(0)).ChargeTime);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ChargingRequiresUsableFullCapacity(float? capacity)
    {
        BatteryTimeEstimates estimates = new BatteryTimeEstimator().Observe(Charging(watts: 10) with
        {
            FullChargeCapacityMilliwattHours = capacity
        }, At(0));

        Assert.Null(estimates.ChargeTime);
    }

    [Fact]
    public void EmptyAndAlreadyFullCapacityProduceZeroWithoutNegativeDurations()
    {
        BatteryTimeEstimator estimator = new();
        Assert.Equal(TimeSpan.Zero, estimator.Observe(Discharging(watts: 10, remaining: 0), At(0)).PresentDischarge);
        Assert.Equal(TimeSpan.Zero, estimator.Observe(Charging(watts: 10) with
        {
            RemainingCapacityMilliwattHours = 40_000
        }, At(5)).ChargeTime);
        Assert.Equal(TimeSpan.Zero, estimator.Observe(Charging(watts: 10) with
        {
            RemainingCapacityMilliwattHours = 41_000
        }, At(10)).ChargeTime);
    }

    [Fact]
    public void ExtremeCapacityRateRatioDoesNotOverflowTimeSpan()
    {
        BatteryTimeEstimates estimates = new BatteryTimeEstimator().Observe(
            Discharging(watts: float.Epsilon, remaining: float.MaxValue), At(0));

        Assert.Null(estimates.PresentDischarge);
    }

    [Fact]
    public void UnexpectedBackwardClockResetsHistoryInsteadOfUsingNegativeAges()
    {
        BatteryTimeEstimator estimator = LearnConstantUsage(watts: 10);
        BatteryTimeEstimates estimates = estimator.Observe(Discharging(watts: 40), At(10));

        Assert.Null(estimates.PredictedLife);
        Hours(expected: 0.5, estimates.PresentDischarge);
    }

    private static BatteryTimeEstimator LearnConstantUsage(float watts)
    {
        BatteryTimeEstimator estimator = new();
        for (int second = 0; second <= 120; second += 5)
            estimator.Observe(Discharging(watts), At(second));
        return estimator;
    }

    private static BatterySnapshot Discharging(float? watts, float remaining = 20_000) =>
        new(BatteryPresent: true,
            ChargePercentage: 50,
            IsOnExternalPower: false,
            IsCharging: false,
            IsFullyCharged: false,
            ChargeRateWatts: null,
            DischargeRateWatts: watts,
            DesignedCapacityMilliwattHours: 40_000,
            FullChargeCapacityMilliwattHours: 40_000,
            RemainingCapacityMilliwattHours: remaining,
            WindowsEstimatedTimeRemaining: TimeSpan.FromMinutes(7),
            EnergySaverEnabled: false);

    private static BatterySnapshot Charging(float? watts) =>
        Discharging(watts: null, remaining: 10_000) with
        {
            IsOnExternalPower = true,
            IsCharging = true,
            ChargeRateWatts = watts
        };

    private static TimeSpan At(int seconds) => TimeSpan.FromSeconds(seconds);

    private static void Hours(double expected, TimeSpan? actual)
    {
        Assert.True(actual.HasValue, "Expected a usable battery time estimate.");
        Assert.Equal(expected, actual.GetValueOrDefault().TotalHours, precision: 8);
    }
}
