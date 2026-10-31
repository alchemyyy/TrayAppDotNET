using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.Services;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanCurveControlTests
{
    private const string SourceKey = "Test_Board.Temperature.CPU";
    private const double Tolerance = 0.0001;

    // Environment.TickCount64-style monotonic milliseconds
    private const long T0 = 10_000_000;
    private const long FirstPoll = T0 - 1_000;

    /// <summary>
    /// Verifies a duty-cycle curve drives the fan at the curve's value for the current reading.
    /// </summary>
    [Fact]
    public void CurveTargetIsWrittenAsDutyCycle()
    {
        FanCurveCommand command = Evaluate(CreateFan(), CreateDutyCurve(), CreateSource(40.0, T0), new FanCurveControlState(), T0);

        Assert.Equal(FanCurveAction.Drive, command.Action);
        Assert.Equal(FanCurveFallback.None, command.Fallback);
        Assert.Equal(expected: 40.0, command.DutyCyclePercent, Tolerance);
    }

    /// <summary>
    /// Verifies Offset is added before Min/Max Duty clamp, so the clamps bound the offset result.
    /// </summary>
    [Fact]
    public void OffsetAppliesBeforeClamps()
    {
        Curve curve = CreateDutyCurve();
        DataSource source = CreateSource(40.0, T0);

        Fan raised = CreateFan();
        raised.Offset = 20;
        raised.ClampHigh = 50;
        Assert.Equal(expected: 50.0, Duty(raised, curve, source, new FanCurveControlState(), T0), Tolerance);

        Fan lowered = CreateFan();
        lowered.Offset = -20;
        lowered.ClampLow = 30;
        Assert.Equal(expected: 30.0, Duty(lowered, curve, source, new FanCurveControlState(), T0), Tolerance);

        Fan unclamped = CreateFan();
        unclamped.Offset = 15;
        Assert.Equal(expected: 55.0, Duty(unclamped, curve, source, new FanCurveControlState(), T0), Tolerance);
    }

    /// <summary>
    /// Verifies Max Delta limits each step to DeltaMax times the elapsed monotonic seconds, both directions.
    /// </summary>
    [Fact]
    public void MaxDeltaScalesWithElapsedTime()
    {
        Fan fan = CreateFan();
        fan.DeltaMax = 10;
        Curve curve = CreateDutyCurve();
        FanCurveControlState state = new();

        DataSource source = CreateSource(20.0, T0);
        Assert.Equal(expected: 20.0, Duty(fan, curve, source, state, T0), Tolerance);

        long t1 = T0 + 500;
        Sample(source, 80.0, t1);
        Assert.Equal(expected: 25.0, Duty(fan, curve, source, state, t1), Tolerance);

        long t2 = t1 + 2_000;
        Sample(source, 80.0, t2);
        Assert.Equal(expected: 45.0, Duty(fan, curve, source, state, t2), Tolerance);

        long t3 = t2 + 1_000;
        Sample(source, 0.0, t3);
        Assert.Equal(expected: 35.0, Duty(fan, curve, source, state, t3), Tolerance);
    }

    /// <summary>
    /// Verifies a Max Delta of 0 means no rate limit rather than freezing the fan.
    /// </summary>
    [Fact]
    public void MaxDeltaZeroDisablesTheLimit()
    {
        Fan fan = CreateFan();
        fan.DeltaMax = 0;
        Curve curve = CreateDutyCurve();
        FanCurveControlState state = new();
        DataSource source = CreateSource(20.0, T0);
        Duty(fan, curve, source, state, T0);

        long t1 = T0 + 500;
        Sample(source, 90.0, t1);

        Assert.Equal(expected: 90.0, Duty(fan, curve, source, state, t1), Tolerance);
    }

    /// <summary>
    /// Verifies Jumpstart kicks a stopped fan to its startup speed for one poll tick, then drops to target.
    /// </summary>
    [Fact]
    public void JumpstartKicksStoppedFanForOneTick()
    {
        Fan fan = CreateFan();
        fan.StartupSpeed = 50;
        Curve curve = CreateDutyCurve();
        FanCurveControlState state = new();
        DataSource source = CreateSource(20.0, T0);

        Assert.Equal(expected: 50.0, Duty(fan, curve, source, state, T0), Tolerance);

        long t1 = T0 + TimeConstants.LHMPollIntervalMs;
        Sample(source, 20.0, t1);
        Assert.Equal(expected: 20.0, Duty(fan, curve, source, state, t1), Tolerance);
    }

    /// <summary>
    /// Verifies Jumpstart is skipped when the target already reaches the startup speed or the fan is spinning.
    /// </summary>
    [Fact]
    public void JumpstartSkippedWhenTargetIsHigherOrFanIsRunning()
    {
        Curve curve = CreateDutyCurve();

        Fan stoppedHighTarget = CreateFan();
        stoppedHighTarget.StartupSpeed = 50;
        Assert.Equal(expected: 60.0,
            Duty(stoppedHighTarget, curve, CreateSource(60.0, T0), new FanCurveControlState(), T0), Tolerance);

        Fan running = CreateFan();
        running.StartupSpeed = 50;
        running.HasRPMSensor = true;
        running.CurrentRPM = 900;
        Assert.Equal(expected: 20.0,
            Duty(running, curve, CreateSource(20.0, T0), new FanCurveControlState(), T0), Tolerance);
    }

    /// <summary>
    /// Verifies a curve that commands 0% and then a low speed restarts the fan through Jumpstart, and the
    /// kick never becomes the Max Delta baseline.
    /// </summary>
    [Fact]
    public void JumpstartAppliesAfterCommandedStopAndIsNotTheDeltaBaseline()
    {
        Fan fan = CreateFan();
        fan.StartupSpeed = 50;
        fan.DeltaMax = 10;
        fan.HasRPMSensor = true;
        fan.CurrentRPM = 1200;
        Curve curve = CreateDutyCurve();
        FanCurveControlState state = new();
        DataSource source = CreateSource(0.0, T0);

        Assert.Equal(expected: 0.0, Duty(fan, curve, source, state, T0), Tolerance);

        long t1 = T0 + 500;
        Sample(source, 40.0, t1);
        Assert.Equal(expected: 50.0, Duty(fan, curve, source, state, t1), Tolerance);

        long t2 = t1 + 500;
        Sample(source, 40.0, t2);
        Assert.Equal(expected: 10.0, Duty(fan, curve, source, state, t2), Tolerance);
    }

    /// <summary>
    /// Verifies Max Duty is a hard bound that also caps the Jumpstart kick.
    /// </summary>
    [Fact]
    public void MaxDutyCapsJumpstart()
    {
        Fan fan = CreateFan();
        fan.StartupSpeed = 50;
        fan.ClampHigh = 40;

        Assert.Equal(expected: 40.0,
            Duty(fan, CreateDutyCurve(), CreateSource(20.0, T0), new FanCurveControlState(), T0), Tolerance);
    }

    /// <summary>
    /// Verifies raising Min Duty takes effect on the next write even when Max Delta would ramp slower, and the
    /// ramp continues from the bound.
    /// </summary>
    [Fact]
    public void MinDutyIsAHardBoundOverMaxDelta()
    {
        Fan fan = CreateFan();
        fan.DeltaMax = 2;
        Curve curve = CreateDutyCurve();
        FanCurveControlState state = new();
        DataSource source = CreateSource(10.0, T0);
        Assert.Equal(expected: 10.0, Duty(fan, curve, source, state, T0), Tolerance);

        fan.ClampLow = 40;
        long t1 = T0 + 500;
        Sample(source, 10.0, t1);
        Assert.Equal(expected: 40.0, Duty(fan, curve, source, state, t1), Tolerance);
        Assert.Equal(expected: 40.0, state.LastPipelineDutyCycle, Tolerance);
    }

    /// <summary>
    /// Verifies RPM curves and RPM-unit properties convert to duty cycle against the fan's RPM reference.
    /// </summary>
    [Fact]
    public void RPMCurveAndRPMPropertiesConvertToDutyCycle()
    {
        Curve curve = CreateRPMCurve();
        DataSource source = CreateSource(50.0, T0);

        Assert.Equal(expected: 50.0, Duty(CreateFan(), curve, source, new FanCurveControlState(), T0), Tolerance);

        Fan offset = CreateFan();
        offset.Offset = 300;
        offset.OffsetRPMMode = true;
        Assert.Equal(expected: 60.0, Duty(offset, curve, source, new FanCurveControlState(), T0), Tolerance);

        Fan dutyClamp = CreateFan();
        dutyClamp.ClampHigh = 40;
        Assert.Equal(expected: 40.0, Duty(dutyClamp, curve, source, new FanCurveControlState(), T0), Tolerance);

        Fan rpmDelta = CreateFan();
        rpmDelta.DeltaMax = 300;
        rpmDelta.DeltaMaxRPMMode = true;
        FanCurveControlState state = new();
        Duty(rpmDelta, curve, source, state, T0);
        long t1 = T0 + 1_000;
        Sample(source, 100.0, t1);
        Assert.Equal(expected: 60.0, Duty(rpmDelta, curve, source, state, t1), Tolerance);
    }

    /// <summary>
    /// Verifies the written duty never leaves 0-100, even when the curve's RPM exceeds the fan's maximum.
    /// </summary>
    [Fact]
    public void OutputStaysWithinDutyCycleRange()
    {
        Fan smallFan = CreateFan();
        smallFan.MaxRPM = 1500;
        Assert.Equal(expected: 100.0,
            Duty(smallFan, CreateRPMCurve(), CreateSource(100.0, T0), new FanCurveControlState(), T0), Tolerance);

        Fan negative = CreateFan();
        negative.Offset = -100;
        Assert.Equal(expected: 0.0,
            Duty(negative, CreateDutyCurve(), CreateSource(30.0, T0), new FanCurveControlState(), T0), Tolerance);
    }

    /// <summary>
    /// Verifies a missing, non-live, invalid, or stale source hands the fan back to hardware control.
    /// </summary>
    [Fact]
    public void BadSourceHandsFanBackToHardware()
    {
        Fan fan = CreateFan();
        Curve curve = CreateDutyCurve();

        AssertHandBack(FanCurveFallback.SourceUnavailable,
            Evaluate(fan, curve, source: null, new FanCurveControlState(), T0));

        DataSource notLive = CreateSource(40.0, T0);
        notLive.IsLiveHardwareSensor = false;
        AssertHandBack(FanCurveFallback.SourceUnavailable, Evaluate(fan, curve, notLive, new FanCurveControlState(), T0));

        DataSource invalid = CreateSource(40.0, T0);
        invalid.RecordSample(T0, valid: false);
        AssertHandBack(FanCurveFallback.SourceUnavailable, Evaluate(fan, curve, invalid, new FanCurveControlState(), T0));

        DataSource stale = CreateSource(40.0, T0 - TimeConstants.CurveSourceStaleMs - 1);
        AssertHandBack(FanCurveFallback.SourceUnavailable, Evaluate(fan, curve, stale, new FanCurveControlState(), T0));

        DataSource edge = CreateSource(40.0, T0 - TimeConstants.CurveSourceStaleMs);
        Assert.Equal(FanCurveAction.Drive, Evaluate(fan, curve, edge, new FanCurveControlState(), T0).Action);
    }

    /// <summary>
    /// Verifies a source with no reading since start writes nothing before the first poll tick, and only starts
    /// aging toward the stale window from that tick.
    /// </summary>
    [Fact]
    public void NeverSampledSourceIsPendingUntilFirstPoll()
    {
        Fan fan = CreateFan();
        Curve curve = CreateDutyCurve();
        DataSource unsampled = new()
        {
            DataSourceKey = SourceKey, DataSourceType = DataSourceTypeEnum.Temperature, IsLiveHardwareSensor = true
        };
        FanCurveControlState state = new();

        FanCurveCommand beforePoll = FanCurveController.Evaluate(fan, curve, unsampled, state, T0, DataSource.NoSample);
        Assert.Equal(FanCurveAction.Pending, beforePoll.Action);
        Assert.Equal(FanCurveFallback.None, beforePoll.Fallback);

        // Discovery may not have reached the source's hardware yet: still pending, not a hand-back
        DataSource notYetDiscovered = new() { DataSourceKey = SourceKey, IsLiveHardwareSensor = false };
        Assert.Equal(FanCurveAction.Pending,
            FanCurveController.Evaluate(fan, curve, notYetDiscovered, state, T0, DataSource.NoSample).Action);
        Assert.Equal(FanCurveAction.Pending,
            FanCurveController.Evaluate(fan, curve, source: null, state, T0, DataSource.NoSample).Action);
        AssertHandBack(FanCurveFallback.SourceUnavailable,
            FanCurveController.Evaluate(fan, curve, notYetDiscovered, new FanCurveControlState(), T0, T0));

        Assert.Equal(FanCurveAction.Pending,
            FanCurveController.Evaluate(fan, curve, unsampled, state, T0 + TimeConstants.CurveSourceStaleMs, T0).Action);

        AssertHandBack(FanCurveFallback.SourceUnavailable,
            FanCurveController.Evaluate(fan, curve, unsampled, state, T0 + TimeConstants.CurveSourceStaleMs + 1, T0));
    }

    /// <summary>
    /// Verifies a transform that turns a valid reading into NaN is reported and makes the source unavailable
    /// instead of storing a saturated 0.
    /// </summary>
    [Fact]
    public void NonFiniteTransformMarksSourceUnavailable()
    {
        DataSource source = CreateSource(40.0, T0);
        source.TransformString = "Sqrt(-1)";

        bool valid = source.SetValue(55_000);
        source.RecordSample(T0 + 500, valid);

        Assert.False(valid);
        Assert.Equal(expected: 40_000L, source.Value);
        AssertHandBack(FanCurveFallback.SourceUnavailable,
            Evaluate(CreateFan(), CreateDutyCurve(), source, new FanCurveControlState(), T0 + 500));

        source.TransformString = "x * 2";
        Assert.True(source.SetValue(30_000));
        Assert.Equal(expected: 60_000L, source.Value);
    }

    /// <summary>
    /// Verifies a transform sees the reading in display units, so a documented example like "x * 1.8 + 32"
    /// converts 20 C to 68 F (stored as 68 000 milli-units), and that a broken transform passes the raw value through.
    /// </summary>
    [Fact]
    public void TransformEvaluatesInDisplayUnits()
    {
        DataSource source = CreateSource(20.0, T0);
        source.TransformString = "x * 1.8 + 32";

        Assert.True(source.SetValue(20_000));
        Assert.Equal(expected: 68_000L, source.Value);

        source.TransformString = "x +";
        Assert.True(source.SetValue(25_000));
        Assert.Equal(expected: 25_000L, source.Value);
    }

    /// <summary>
    /// Verifies an empty curve and a missing curve or data source both hand the fan back.
    /// </summary>
    [Fact]
    public void UnusableOrMissingCurveHandsFanBackToHardware()
    {
        Fan fan = CreateFan();
        DataSource source = CreateSource(40.0, T0);

        Curve empty = CreateDutyCurve();
        empty.CurveNodes.Clear();
        AssertHandBack(FanCurveFallback.CurveUnusable, Evaluate(fan, empty, source, new FanCurveControlState(), T0));

        AssertHandBack(FanCurveFallback.None, Evaluate(fan, curve: null, source, new FanCurveControlState(), T0));

        Curve noSource = CreateDutyCurve();
        noSource.SelectedDataSourceKey = string.Empty;
        AssertHandBack(FanCurveFallback.None, Evaluate(fan, noSource, source, new FanCurveControlState(), T0));
    }

    /// <summary>
    /// Verifies control resumes automatically when the source recovers, starting fresh with no ramp from the
    /// pre-failure value.
    /// </summary>
    [Fact]
    public void ControlResumesWhenSourceRecovers()
    {
        Fan fan = CreateFan();
        fan.DeltaMax = 10;
        Curve curve = CreateDutyCurve();
        FanCurveControlState state = new();
        DataSource source = CreateSource(20.0, T0);
        Duty(fan, curve, source, state, T0);

        long failed = T0 + 10_000;
        AssertHandBack(FanCurveFallback.SourceUnavailable, Evaluate(fan, curve, source, state, failed));
        Assert.False(state.HasCommand);

        long recovered = failed + 500;
        Sample(source, 80.0, recovered);
        FanCurveCommand command = Evaluate(fan, curve, source, state, recovered);

        Assert.Equal(FanCurveAction.Drive, command.Action);
        Assert.Equal(FanCurveFallback.None, command.Fallback);
        Assert.Equal(expected: 80.0, command.DutyCyclePercent, Tolerance);
    }

    /// <summary>
    /// Verifies the curve's HysteresisMs holds a committed target until the dwell time has passed.
    /// </summary>
    [Fact]
    public void HysteresisHoldsCommittedTarget()
    {
        Fan fan = CreateFan();
        Curve curve = CreateDutyCurve();
        curve.HysteresisMs = 2_000;
        FanCurveControlState state = new();
        DataSource source = CreateSource(40.0, T0);
        Duty(fan, curve, source, state, T0);

        long t1 = T0 + 1_000;
        Sample(source, 60.0, t1);
        Assert.Equal(expected: 40.0, Duty(fan, curve, source, state, t1), Tolerance);

        long t2 = T0 + 2_000;
        Sample(source, 60.0, t2);
        Assert.Equal(expected: 60.0, Duty(fan, curve, source, state, t2), Tolerance);
    }

    /// <summary>
    /// Verifies a hysteresis commit is dropped when the fan switches to another curve or the curve's unit flips.
    /// </summary>
    [Fact]
    public void HysteresisCommitDroppedOnCurveOrUnitChange()
    {
        Fan fan = CreateFan();
        Curve first = CreateDutyCurve();
        first.HysteresisMs = 2_000;
        FanCurveControlState state = new();
        DataSource source = CreateSource(40.0, T0);
        Assert.Equal(expected: 40.0, Duty(fan, first, source, state, T0), Tolerance);

        Curve second = CreateDutyCurve();
        second.HysteresisMs = 2_000;
        long t1 = T0 + 500;
        Sample(source, 60.0, t1);
        Assert.Equal(expected: 60.0, Duty(fan, second, source, state, t1), Tolerance);

        // Same curve, now in RPM: 70 on the X axis is 70 RPM, i.e. 70 / 3000 of the fan's range
        second.RPMMode = true;
        long t2 = t1 + 500;
        Sample(source, 70.0, t2);
        Assert.Equal(expected: 70.0 / 3000.0 * 100.0, Duty(fan, second, source, state, t2), Tolerance);
    }

    /// <summary>
    /// Verifies the shared evaluator samples exactly like Curve.Evaluate and rejects empty curves.
    /// </summary>
    [Fact]
    public void SharedEvaluatorMatchesCurveSampling()
    {
        Curve curve = CreateDutyCurve();
        curve.SmoothingFactor = 50;
        curve.CurveNodes.Insert(index: 1, new CurveNode(x: 50.0, y: 30.0));
        DataSource source = CreateSource(37.5, T0);

        Assert.True(FanCurveEvaluator.TryEvaluate(curve, source, out double target));
        Assert.Equal(curve.Evaluate(37.5), target, Tolerance);

        curve.CurveNodes.Clear();
        Assert.False(FanCurveEvaluator.TryEvaluate(curve, source, out _));
    }

    /// <summary>
    /// Verifies a curve fail-safe hands back to firmware when its defaults are trusted, writes the fan's Max Duty
    /// when they aren't, and leaves plain "no curve" hand-backs and normal drives alone.
    /// </summary>
    [Fact]
    public void FirmwareTrustDecidesFailSafeWrite()
    {
        Fan fan = CreateFan();
        fan.ClampHigh = 80;
        FanCurveCommand failSafe = FanCurveCommand.HandBack(FanCurveFallback.SourceUnavailable);

        Assert.Equal(failSafe, FanCurveController.ApplyFirmwareTrust(fan, failSafe, firmwareDefaultsTrusted: true));

        FanCurveCommand untrusted = FanCurveController.ApplyFirmwareTrust(fan, failSafe, firmwareDefaultsTrusted: false);
        Assert.Equal(FanCurveAction.Drive, untrusted.Action);
        Assert.Equal(FanCurveFallback.SourceUnavailable, untrusted.Fallback);
        Assert.Equal(expected: 80.0, untrusted.DutyCyclePercent, Tolerance);

        Fan rpmLimit = CreateFan();
        rpmLimit.ClampHigh = 2400;
        rpmLimit.ClampHighRPMMode = true;
        FanCurveCommand unusable = FanCurveCommand.HandBack(FanCurveFallback.CurveUnusable);
        Assert.Equal(expected: 80.0,
            FanCurveController.ApplyFirmwareTrust(rpmLimit, unusable, firmwareDefaultsTrusted: false).DutyCyclePercent,
            Tolerance);

        FanCurveCommand noCurve = FanCurveCommand.HandBack(FanCurveFallback.None);
        Assert.Equal(noCurve, FanCurveController.ApplyFirmwareTrust(fan, noCurve, firmwareDefaultsTrusted: false));

        FanCurveCommand drive = FanCurveCommand.Drive(35.0);
        Assert.Equal(drive, FanCurveController.ApplyFirmwareTrust(fan, drive, firmwareDefaultsTrusted: false));
    }

    /// <summary>
    /// Verifies Min Duty and Max Duty also bind Manual targets, while Offset does not.
    /// </summary>
    [Fact]
    public void ManualTargetHonorsMinAndMaxDuty()
    {
        Fan low = CreateFan();
        low.CurrentControlMode = FanControlMode.Manual;
        low.FanDisplayedValue = 10;
        low.ClampLow = 30;
        low.Offset = 20;
        Assert.Equal(expected: 30.0, FanCurveController.ResolveManualDutyCyclePercent(low), Tolerance);

        Fan high = CreateFan();
        high.CurrentControlMode = FanControlMode.Manual;
        high.FanDisplayedValue = 90;
        high.ClampHigh = 70;
        Assert.Equal(expected: 70.0, FanCurveController.ResolveManualDutyCyclePercent(high), Tolerance);

        Fan inRange = CreateFan();
        inRange.CurrentControlMode = FanControlMode.Manual;
        inRange.FanDisplayedValue = 45;
        inRange.Offset = 20;
        Assert.Equal(expected: 45.0, FanCurveController.ResolveManualDutyCyclePercent(inRange), Tolerance);
    }

    private static FanCurveCommand Evaluate(Fan fan, Curve? curve, DataSource? source, FanCurveControlState state, long nowMs) =>
        FanCurveController.Evaluate(fan, curve, source, state, nowMs, FirstPoll);

    private static double Duty(Fan fan, Curve curve, DataSource source, FanCurveControlState state, long nowMs)
    {
        FanCurveCommand command = Evaluate(fan, curve, source, state, nowMs);
        Assert.Equal(FanCurveAction.Drive, command.Action);
        return command.DutyCyclePercent;
    }

    private static void AssertHandBack(FanCurveFallback expected, FanCurveCommand command)
    {
        Assert.Equal(FanCurveAction.HandBack, command.Action);
        Assert.Equal(expected, command.Fallback);
    }

    // Jumpstart and Max Delta off unless a test turns them on; RPM reference pinned at 3000.
    private static Fan CreateFan() => new()
    {
        DataSourceKey = "Test_Board.Control.Fan_#1",
        MaxRPM = 3000,
        StartupSpeed = 0,
        DeltaMax = 0
    };

    private static Curve CreateDutyCurve() => new()
    {
        CurveName = "Test Duty Curve",
        CurveNodes = [new CurveNode(x: 0.0, y: 0.0), new CurveNode(x: 100.0, y: 100.0)],
        SmoothingFactor = 0,
        RPMMode = false,
        SelectedDataSourceKey = SourceKey
    };

    private static Curve CreateRPMCurve() => new()
    {
        CurveName = "Test RPM Curve",
        CurveNodes = [new CurveNode(x: 0.0, y: 0.0), new CurveNode(x: 100.0, y: 3000.0)],
        SmoothingFactor = 0,
        RPMMode = true,
        MaxRPM = 3000,
        SelectedDataSourceKey = SourceKey
    };

    private static DataSource CreateSource(double reading, long sampledMs)
    {
        DataSource source = new()
        {
            DataSourceKey = SourceKey,
            DataSourceType = DataSourceTypeEnum.Temperature,
            IsLiveHardwareSensor = true
        };
        Sample(source, reading, sampledMs);
        return source;
    }

    private static void Sample(DataSource source, double reading, long sampledMs) =>
        source.RecordSample(sampledMs, source.SetValue((long)Math.Round(reading * 1000.0)));
}
