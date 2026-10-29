using BrightnessTrayAppDotNET.Interop.NightLight;
using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Services;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

// Pure math and resolution only: nothing here touches a real display's gamma ramp or the provider's live state.
public sealed class NightLightGammaRampTests
{
    private const int Channel = GammaRampMath.EntriesPerChannel;

    [Fact]
    public void WhitePointAtNoTintIsExactlyNeutral()
    {
        (double red, double green, double blue) = GammaRampMath.KelvinToChannelScale(NightLightKelvin.MaxKelvin);

        Assert.Equal(expected: 1.0, red);
        Assert.Equal(expected: 1.0, green);
        Assert.Equal(expected: 1.0, blue);
    }

    [Fact]
    public void BlackbodyApproximationMatchesKnownWhitePoints()
    {
        (double red6500, double green6500, double blue6500) = GammaRampMath.KelvinToRgb(6500);
        (double red1200, double green1200, double blue1200) = GammaRampMath.KelvinToRgb(1200);

        Assert.Equal(expected: 255.0, red6500);
        Assert.InRange(green6500, low: 250.0, high: 255.0);
        Assert.InRange(blue6500, low: 245.0, high: 255.0);
        Assert.Equal(expected: 255.0, red1200);
        Assert.InRange(green1200, low: 80.0, high: 92.0);
        Assert.Equal(expected: 0.0, blue1200);
    }

    [Fact]
    public void StrengthZeroBuildsIdentityRamp()
    {
        ushort[] ramp = new ushort[GammaRampMath.RampLength];

        GammaRampMath.BuildRamp(strength: 0, ramp);

        for (int channel = 0; channel < 3; channel++)
        {
            for (int i = 0; i < Channel; i++)
                Assert.Equal(expected: (ushort)(i * 257), ramp[channel * Channel + i]);
        }
    }

    [Fact]
    public void ChannelsNeverBrightenAsStrengthRises()
    {
        ushort[] previous = new ushort[GammaRampMath.RampLength];
        ushort[] current = new ushort[GammaRampMath.RampLength];
        GammaRampMath.BuildRamp(strength: 0, previous);

        for (int strength = 1; strength <= 100; strength++)
        {
            GammaRampMath.BuildRamp(strength, current);
            for (int k = 0; k < GammaRampMath.RampLength; k++)
                Assert.True(current[k] <= previous[k], $"strength {strength} entry {k}: {current[k]} > {previous[k]}");

            (previous, current) = (current, previous);
        }
    }

    [Fact]
    public void FullStrengthKeepsRedRemovesBlueAndDimsGreen()
    {
        ushort[] ramp = new ushort[GammaRampMath.RampLength];

        GammaRampMath.BuildRamp(strength: 100, ramp);

        Assert.Equal(expected: ushort.MaxValue, ramp[Channel - 1]);
        Assert.InRange(ramp[Channel * 2 - 1], low: (ushort)1, high: (ushort)(ushort.MaxValue / 2));
        Assert.Equal(expected: (ushort)0, ramp[Channel * 3 - 1]);
    }

    [Fact]
    public void EachChannelRisesWithInput()
    {
        ushort[] ramp = new ushort[GammaRampMath.RampLength];

        GammaRampMath.BuildRamp(strength: 60, ramp);

        for (int channel = 0; channel < 3; channel++)
        {
            for (int i = 1; i < Channel; i++)
                Assert.True(ramp[channel * Channel + i] >= ramp[channel * Channel + i - 1]);
        }
    }

    [Fact]
    public void BuildRampRejectsWrongLength() =>
        Assert.Throws<ArgumentException>(() => GammaRampMath.BuildRamp(strength: 50, new ushort[256]));

    [Fact]
    public void AcceptedRampIsWrittenOnce()
    {
        ThresholdWriter writer = new(maxAccepted: 100);

        GammaRampWriteResult result =
            GammaRampMath.ApplyWithBackoff(requestedStrength: 70, maxAcceptedStrength: 100, ref writer);

        Assert.Equal(expected: 70, result.AppliedStrength);
        Assert.Equal(expected: 1, result.Attempts);
        Assert.Equal(new[] { 70 }, writer.Writes);
    }

    [Fact]
    public void RefusedRampBacksOffToStrongestAcceptedStrength()
    {
        ThresholdWriter writer = new(maxAccepted: 37);

        GammaRampWriteResult result =
            GammaRampMath.ApplyWithBackoff(requestedStrength: 80, maxAcceptedStrength: 100, ref writer);

        Assert.True(result.Succeeded);
        Assert.Equal(expected: 37, result.AppliedStrength);
        Assert.Equal(expected: 37, writer.LastAccepted);
        Assert.Equal(writer.Writes.Count, result.Attempts);
        Assert.True(result.Attempts <= 1 + GammaRampMath.MaxBackoffProbes);
    }

    [Fact]
    public void BackoffConvergesForEveryThresholdWithinBound()
    {
        for (int threshold = 0; threshold <= 100; threshold++)
        {
            for (int requested = 0; requested <= 100; requested += 7)
            {
                ThresholdWriter writer = new(threshold);

                GammaRampWriteResult result =
                    GammaRampMath.ApplyWithBackoff(requested, maxAcceptedStrength: 100, ref writer);

                int expected = Math.Min(requested, threshold);
                Assert.Equal(expected, result.AppliedStrength);
                // What's on screen is the last write the fake accepted
                Assert.Equal(expected, writer.LastAccepted);
                Assert.True(result.Attempts <= 2 + GammaRampMath.MaxBackoffProbes);
            }
        }
    }

    [Fact]
    public void BackoffFallsBackToIdentityWhenOnlyIdentityIsAccepted()
    {
        ThresholdWriter writer = new(maxAccepted: 0);

        GammaRampWriteResult result =
            GammaRampMath.ApplyWithBackoff(requestedStrength: 100, maxAcceptedStrength: 100, ref writer);

        Assert.Equal(expected: 0, result.AppliedStrength);
        Assert.Equal(expected: 0, writer.Writes[^1]);
    }

    [Fact]
    public void BackoffReportsFailureWhenEveryRampIsRefused()
    {
        ThresholdWriter writer = new(maxAccepted: -1);

        GammaRampWriteResult result =
            GammaRampMath.ApplyWithBackoff(requestedStrength: 90, maxAcceptedStrength: 100, ref writer);

        Assert.False(result.Succeeded);
        Assert.Equal(expected: -1, result.AppliedStrength);
        Assert.True(result.Attempts <= 2 + GammaRampMath.MaxBackoffProbes);
        Assert.Null(writer.LastAccepted);
    }

    [Fact]
    public void KnownCapSkipsTheRefusedStrength()
    {
        ThresholdWriter writer = new(maxAccepted: 40);

        GammaRampWriteResult result =
            GammaRampMath.ApplyWithBackoff(requestedStrength: 90, maxAcceptedStrength: 40, ref writer);

        Assert.Equal(expected: 40, result.AppliedStrength);
        Assert.Equal(new[] { 40 }, writer.Writes);
    }

    [Fact]
    public void ReadbackDeviationIsLargestEntryDifference()
    {
        ushort[] written = new ushort[GammaRampMath.RampLength];
        ushort[] readBack = new ushort[GammaRampMath.RampLength];
        GammaRampMath.BuildRamp(strength: 40, written);
        written.CopyTo(readBack, index: 0);
        readBack[300] = (ushort)(readBack[300] + 600);

        Assert.Equal(expected: 0, GammaRampMath.MaxDeviation(written, written));
        Assert.Equal(expected: 600, GammaRampMath.MaxDeviation(written, readBack));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void WindowsNightLightTurnOffWaitsOutOnlyAFailedAttempt(
        bool windowsOn,
        bool offFailed,
        bool force,
        bool expected) =>
        Assert.Equal(expected, GammaRampNightLight.ShouldTurnOffWindowsNightLight(windowsOn, offFailed, force));

    [Theory]
    // Seeing Windows Night Light off always clears the latch
    [InlineData(false, false, false, true, false)]
    // Only a failed attempt latches; success leaves the next check free to act
    [InlineData(true, true, false, false, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, true, true, false)]
    // No attempt keeps whatever the latch was
    [InlineData(true, false, false, true, true)]
    [InlineData(true, false, false, false, false)]
    public void WindowsNightLightLatchSetsOnlyOnFailure(
        bool windowsOn,
        bool attempted,
        bool turnedOff,
        bool offFailed,
        bool expected) =>
        Assert.Equal(
            expected,
            GammaRampNightLight.NextWindowsNightLightOffFailed(windowsOn, attempted, turnedOff, offFailed));

    [Fact]
    public void WindowsNightLightComingBackRightAfterASuccessfulTurnOffIsTurnedOffAgain()
    {
        // Watchdog ticks where Windows Night Light is on every time (its schedule keeps re-enabling it)
        // and every turn-off succeeds: each tick must act.
        Assert.Equal(3, SimulateWatchdogTicks(ticks: 3, writeSucceeds: true));
    }

    [Fact]
    public void FailedWindowsNightLightTurnOffIsNotRetriedEveryTick() =>
        Assert.Equal(1, SimulateWatchdogTicks(ticks: 4, writeSucceeds: false));

    [Fact]
    public void FailedEnumerationKeepsTrackedDisplays()
    {
        List<GammaRampNightLight.DisplayRampState> displays =
        [
            new(@"\\.\DISPLAY1") { HasWritten = true, MaxAcceptedStrength = 30 },
            new(@"\\.\DISPLAY2") { HasWritten = true }
        ];

        Assert.False(GammaRampNightLight.MergeDisplays(displays, enumerated: null));
        Assert.False(GammaRampNightLight.MergeDisplays(displays, []));

        Assert.Equal(2, displays.Count);
        Assert.All(displays, d => Assert.True(d.HasWritten && d.IsPresent));
        Assert.Equal(expected: 30, displays[0].MaxAcceptedStrength);
    }

    [Fact]
    public void WrittenDisplayThatLeavesTheActiveSetIsKeptForRestore()
    {
        GammaRampNightLight.DisplayRampState written =
            new(@"\\.\DISPLAY1") { HasWritten = true, HasExpectedRamp = true };
        GammaRampNightLight.DisplayRampState untouched = new(@"\\.\DISPLAY2");
        List<GammaRampNightLight.DisplayRampState> displays = [written, untouched];

        Assert.True(GammaRampNightLight.MergeDisplays(displays, [new GammaRampDisplay(@"\\.\DISPLAY3", false)]));

        Assert.Equal(2, displays.Count);
        Assert.Equal(@"\\.\DISPLAY3", displays[0].DeviceName);
        Assert.True(displays[0].IsPresent);
        Assert.Same(written, displays[1]);
        Assert.False(written.IsPresent);
        Assert.True(written.HasWritten);
        Assert.False(written.HasExpectedRamp);
        Assert.DoesNotContain(untouched, displays);
    }

    [Fact]
    public void ReturningDisplayReusesItsStateWithCapReset()
    {
        GammaRampNightLight.DisplayRampState state =
            new(@"\\.\DISPLAY1") { HasWritten = true, IsPresent = false, MaxAcceptedStrength = 20 };
        List<GammaRampNightLight.DisplayRampState> displays = [state];

        Assert.True(GammaRampNightLight.MergeDisplays(displays, [new GammaRampDisplay(@"\\.\display1", true)]));

        Assert.Same(state, Assert.Single(displays));
        Assert.True(state.IsPresent);
        Assert.True(state.IsAdvancedColor);
        Assert.True(state.HasWritten);
        Assert.Equal(expected: 100, state.MaxAcceptedStrength);
    }

    [Theory]
    [InlineData(NightLightFallbackMode.GammaRamp, true, true)]
    [InlineData(NightLightFallbackMode.GammaRamp, false, false)]
    [InlineData(NightLightFallbackMode.SettingsHandler, true, false)]
    [InlineData(NightLightFallbackMode.Registry, true, false)]
    public void UninstallRestoresOnlyWhenTheGammaTintWasOn(NightLightFallbackMode mode, bool enabled, bool expected) =>
        Assert.Equal(expected, GammaRampNightLight.ShouldRestoreAfterForcedStop(mode, enabled));

    // Drives the pure edge rule the way the watchdog does with Windows Night Light on at every tick.
    private static int SimulateWatchdogTicks(int ticks, bool writeSucceeds)
    {
        bool offFailed = false;
        int attempts = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            bool attempt = GammaRampNightLight.ShouldTurnOffWindowsNightLight(true, offFailed, force: false);
            if (attempt) attempts++;
            offFailed = GammaRampNightLight.NextWindowsNightLightOffFailed(true, attempt, writeSucceeds, offFailed);
        }

        return attempts;
    }

    [Fact]
    public void GammaRampModeResolvesToGammaRampWithoutProbingWindows()
    {
        NightLightProvider.Backend backend = NightLightProvider.ResolveBackend(
            NightLightFallbackMode.GammaRamp,
            static () => throw new InvalidOperationException("SettingsHandler probed"),
            static () => throw new InvalidOperationException("Registry probed"));

        Assert.Equal(NightLightProvider.Backend.GammaRamp, backend);
    }

    [Theory]
    [InlineData(NightLightFallbackMode.SettingsHandler, true, true, nameof(NightLightProvider.Backend.SettingsHandler))]
    [InlineData(NightLightFallbackMode.SettingsHandler, false, true, nameof(NightLightProvider.Backend.None))]
    [InlineData(NightLightFallbackMode.Registry, true, true, nameof(NightLightProvider.Backend.Registry))]
    [InlineData(NightLightFallbackMode.Registry, true, false, nameof(NightLightProvider.Backend.None))]
    [InlineData(NightLightFallbackMode.Auto, false, true, nameof(NightLightProvider.Backend.Registry))]
    [InlineData(NightLightFallbackMode.GammaRamp, false, false, nameof(NightLightProvider.Backend.GammaRamp))]
    public void EachModeResolvesToItsBackend(
        NightLightFallbackMode mode,
        bool settingsHandlerSupported,
        bool registrySupported,
        string expectedBackend)
    {
        NightLightProvider.Backend backend = NightLightProvider.ResolveBackend(
            mode,
            () => settingsHandlerSupported,
            () => registrySupported);

        Assert.Equal(Enum.Parse<NightLightProvider.Backend>(expectedBackend), backend);
    }

    [Fact]
    public void GammaRampStrengthSettingIsClamped()
    {
        AppSettings settings = new();
        Assert.False(settings.NightLightGammaRampEnabled);
        Assert.Equal(expected: 50, settings.NightLightGammaRampStrength);

        settings.NightLightGammaRampStrength = 140;
        Assert.Equal(expected: 100, settings.NightLightGammaRampStrength);

        settings.NightLightGammaRampStrength = -5;
        Assert.Equal(expected: 0, settings.NightLightGammaRampStrength);
    }

    private sealed class ThresholdWriter(int maxAccepted) : IGammaRampWriter
    {
        public List<int> Writes { get; } = [];
        public int? LastAccepted { get; private set; }

        public bool TryWrite(int strength)
        {
            Writes.Add(strength);
            bool accepted = strength <= maxAccepted;
            if (accepted) LastAccepted = strength;
            return accepted;
        }
    }
}
