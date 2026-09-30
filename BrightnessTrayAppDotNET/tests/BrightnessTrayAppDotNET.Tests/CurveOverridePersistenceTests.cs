using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.UI.Flyout;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class CurveOverridePersistenceTests
{
    private static readonly DateTime TestUtcNow = new(year: 2026, month: 8, day: 20, hour: 12, minute: 0, second: 0,
        DateTimeKind.Utc);

    [Fact]
    public void IndefiniteManualOverrideRestoresWithoutStopwatch()
    {
        CurveStopwatchEntry entry = new() { IsCurveReleased = true };

        bool shouldRestore = BrightnessFlyoutWindow.ShouldRestorePersistedCurveRelease(entry, TestUtcNow);

        Assert.True(shouldRestore);
    }

    [Fact]
    public void ActiveLegacyStopwatchRestoresManualOverride()
    {
        CurveStopwatchEntry entry = new() { IsEnabled = true, ReenableAtUtc = TestUtcNow.AddMinutes(1) };

        bool shouldRestore = BrightnessFlyoutWindow.ShouldRestorePersistedCurveRelease(entry, TestUtcNow);

        Assert.True(shouldRestore);
    }

    [Fact]
    public void ExpiredStopwatchDoesNotRestoreManualOverride()
    {
        CurveStopwatchEntry entry = new() { IsEnabled = true, IsCurveReleased = true, ReenableAtUtc = TestUtcNow };

        bool shouldRestore = BrightnessFlyoutWindow.ShouldRestorePersistedCurveRelease(entry, TestUtcNow);

        Assert.False(shouldRestore);
    }

    [Fact]
    public void RestoredCurveTransitionPreservesManualReleaseOnlyOnStartup()
    {
        SliderState restored = SliderStateMachine.OnCurveRestored(
            SliderState.CurveReleased,
            inDisabledPeriod: false);
        SliderState toggled = SliderStateMachine.OnCurveEngaged(
            SliderState.CurveReleased,
            inDisabledPeriod: false);

        Assert.Equal(SliderState.CurveReleased, restored);
        Assert.Equal(SliderState.CurveActive, toggled);
    }

    [Fact]
    public void FailedRowReengageEndsOnlyReleaseCarriedIntoFailure()
    {
        MonitorInfo released = new() { SliderState = SliderState.CurveReleased };
        MonitorInfo failedReleased = new() { Brightness = 34, CurveTargetBrightness = 19 };
        failedReleased.SliderState = SliderState.CurveReleased;
        failedReleased.SliderState = SliderState.Failed;
        MonitorInfo failedDisabled = new() { SliderState = SliderState.Disabled };
        failedDisabled.SliderState = SliderState.Failed;

        Assert.True(failedReleased.IsFailedWhileCurveReleased);
        Assert.False(released.ReengageFailedCurveRelease(inDisabledPeriod: false));
        Assert.True(failedReleased.ReengageFailedCurveRelease(inDisabledPeriod: false));
        Assert.False(failedReleased.ReengageFailedCurveRelease(inDisabledPeriod: false));
        Assert.False(failedDisabled.ReengageFailedCurveRelease(inDisabledPeriod: false));

        Assert.Equal(SliderState.CurveReleased, released.SliderState);
        Assert.Equal(SliderState.Failed, failedReleased.SliderState);
        Assert.False(failedReleased.IsFailedWhileCurveReleased);
        // The stale pre-release curve target must not become the blind recovery probe value
        Assert.Equal(expected: 34, failedReleased.RecoveryProbeBrightness);
        Assert.Equal(
            SliderState.CurveSleeping,
            failedReleased.ResolveHardwareRecoveredSliderState(curveEngaged: true, inDisabledPeriod: true));
        Assert.Equal(
            SliderState.Disabled,
            failedDisabled.ResolveHardwareRecoveredSliderState(curveEngaged: true, inDisabledPeriod: false));
    }

    [Theory]
    [InlineData(true, true, SliderState.CurveReleased, false, true)]
    [InlineData(true, true, SliderState.CurveReleased, true, true)]
    [InlineData(true, true, SliderState.CurveActive, true, false)]
    [InlineData(true, true, SliderState.CurveActive, false, false)]
    [InlineData(false, true, SliderState.CurveReleased, true, false)]
    [InlineData(true, false, SliderState.CurveReleased, true, false)]
    public void CurveStopwatchSurvivesFailureOnlyWhileItsOverrideDoes(
        bool isCurveEnabled,
        bool isAbsoluteMode,
        SliderState sliderState,
        bool isFailed,
        bool expected)
    {
        MonitorInfo monitor = new() { SliderState = sliderState };
        if (isFailed) monitor.SliderState = SliderState.Failed;

        bool shouldKeep = BrightnessFlyoutWindow.ShouldKeepCurveStopwatch(isCurveEnabled, isAbsoluteMode, monitor);

        Assert.Equal(expected, shouldKeep);
    }

    [Fact]
    public void AppSettingsRoundTripPreservesManualOverrideWithoutStopwatch()
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"BrightnessTrayAppDotNET-{Guid.NewGuid():N}.xml");
        try
        {
            AppSettings settings = new();
            settings.CurveStopwatches.Add(new CurveStopwatchEntry
            {
                SliderKey = "monitor:edid:test", IsCurveReleased = true
            });
            settings.Save(settingsPath);

            AppSettings restored = AppSettings.LoadOrDefault(settingsPath);

            CurveStopwatchEntry entry = Assert.Single(restored.CurveStopwatches);
            Assert.Equal(expected: "monitor:edid:test", entry.SliderKey);
            Assert.True(entry.IsCurveReleased);
            Assert.False(entry.IsEnabled);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }
}
