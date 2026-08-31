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
