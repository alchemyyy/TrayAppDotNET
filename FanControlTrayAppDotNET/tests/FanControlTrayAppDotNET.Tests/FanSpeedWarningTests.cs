using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.Services;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanSpeedWarningTests
{
    // Environment.TickCount64-style monotonic milliseconds
    private const long T0 = 10_000_000;

    /// <summary>
    /// Verifies the 0% / 100% defaults never warn, at either end of the duty range.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(100.0)]
    [InlineData(50.0)]
    public void DefaultThresholdsNeverWarn(double dutyCycle)
    {
        Fan fan = new() { CurrentDutyCycle = dutyCycle };

        Assert.Equal(FanSpeedWarning.None, FanSpeedWarnings.Classify(fan));
    }

    /// <summary>
    /// Verifies a speed equal to a threshold doesn't warn; only strictly below Warn Low or above Warn High does.
    /// </summary>
    [Fact]
    public void DutyThresholdEdgesAreExclusive()
    {
        Fan fan = new() { WarnLow = 30, WarnHigh = 70 };

        fan.CurrentDutyCycle = 30.0;
        Assert.Equal(FanSpeedWarning.None, FanSpeedWarnings.Classify(fan));
        fan.CurrentDutyCycle = 29.9;
        Assert.Equal(FanSpeedWarning.Low, FanSpeedWarnings.Classify(fan));
        fan.CurrentDutyCycle = 70.0;
        Assert.Equal(FanSpeedWarning.None, FanSpeedWarnings.Classify(fan));
        fan.CurrentDutyCycle = 70.1;
        Assert.Equal(FanSpeedWarning.High, FanSpeedWarnings.Classify(fan));
    }

    /// <summary>
    /// Verifies RPM thresholds compare measured RPM and stay quiet until an RPM sensor is paired.
    /// </summary>
    [Fact]
    public void RPMThresholdsNeedAnRPMSensor()
    {
        Fan fan = new() { WarnLow = 500, WarnLowRPMMode = true, CurrentDutyCycle = 40.0 };

        Assert.Equal(FanSpeedWarning.None, FanSpeedWarnings.Classify(fan));

        fan.HasRPMSensor = true;
        Assert.Equal(FanSpeedWarning.Low, FanSpeedWarnings.Classify(fan));

        fan.CurrentRPM = 500;
        Assert.Equal(FanSpeedWarning.None, FanSpeedWarnings.Classify(fan));

        fan.WarnHigh = 1200;
        fan.WarnHighRPMMode = true;
        fan.CurrentRPM = 1201;
        Assert.Equal(FanSpeedWarning.High, FanSpeedWarnings.Classify(fan));
        Assert.Equal(("1201 RPM", "1200 RPM"), FanSpeedWarnings.Describe(fan, FanSpeedWarning.High));
    }

    /// <summary>
    /// Verifies a fan tagged non-functioning never warns.
    /// </summary>
    [Fact]
    public void NonFunctioningFansDoNotWarn()
    {
        Fan fan = new() { WarnLow = 30, CurrentDutyCycle = 10.0, ForcedNonFunctioning = true };

        Assert.Equal(FanSpeedWarning.None, FanSpeedWarnings.Classify(fan));
    }

    /// <summary>
    /// Verifies an excursion must persist for the debounce window, notifies once, and clears only after
    /// staying back in range for the same window.
    /// </summary>
    [Fact]
    public void TrackerNotifiesOncePerDebouncedExcursion()
    {
        int debounceMs = TimeConstants.FanSpeedWarningDebounceMs;
        FanSpeedWarningTracker tracker = new();

        Assert.False(tracker.Update(FanSpeedWarning.Low, T0));
        Assert.False(tracker.Update(FanSpeedWarning.Low, T0 + debounceMs - 1));
        Assert.Equal(FanSpeedWarning.None, tracker.Active);

        Assert.True(tracker.Update(FanSpeedWarning.Low, T0 + debounceMs));
        Assert.Equal(FanSpeedWarning.Low, tracker.Active);
        Assert.False(tracker.Update(FanSpeedWarning.Low, T0 + debounceMs * 2));

        long back = T0 + debounceMs * 3;
        Assert.False(tracker.Update(FanSpeedWarning.None, back));
        Assert.False(tracker.Update(FanSpeedWarning.Low, back + debounceMs / 2));
        Assert.False(tracker.Update(FanSpeedWarning.None, back + debounceMs));
        Assert.Equal(FanSpeedWarning.Low, tracker.Active);

        long cleared = back + debounceMs * 2;
        Assert.False(tracker.Update(FanSpeedWarning.None, cleared));
        Assert.Equal(FanSpeedWarning.None, tracker.Active);

        Assert.False(tracker.Update(FanSpeedWarning.High, cleared + 1));
        Assert.True(tracker.Update(FanSpeedWarning.High, cleared + 1 + debounceMs));
        Assert.Equal(FanSpeedWarning.High, tracker.Active);
    }
}
