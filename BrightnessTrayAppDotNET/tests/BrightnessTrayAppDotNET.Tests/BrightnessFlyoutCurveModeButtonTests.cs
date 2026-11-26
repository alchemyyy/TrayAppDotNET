using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.UI.Flyout;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class BrightnessFlyoutCurveModeButtonTests
{
    [Theory]
    [InlineData(false, SliderState.Enabled, false)]
    [InlineData(false, SliderState.CurveActive, false)]
    [InlineData(true, SliderState.Enabled, false)]
    [InlineData(true, SliderState.Disabled, false)]
    [InlineData(true, SliderState.Failed, false)]
    [InlineData(true, SliderState.CurveActive, true)]
    [InlineData(true, SliderState.CurveSleeping, true)]
    [InlineData(true, SliderState.CurveReleased, true)]
    public void CurveModeButtonOnlyOccupiesSliderSpaceForCurveControlledRows(
        bool isCurveEnabled,
        SliderState sliderState,
        bool expected)
    {
        bool isVisible = BrightnessFlyoutWindow.ShouldShowCurveModeButton(isCurveEnabled, sliderState);

        Assert.Equal(expected, isVisible);
    }

    [Theory]
    [InlineData(SliderState.CurveActive, 51.0)]
    [InlineData(SliderState.CurveActive, 100.0)]
    [InlineData(SliderState.CurveSleeping, 51.0)]
    [InlineData(SliderState.CurveSleeping, 100.0)]
    public void MasterManualOverrideReleasesCurveDrivenRowEvenWhenSliderMatchesCurve(
        SliderState curveState,
        double curveTarget)
    {
        MonitorInfo row = new() { Brightness = 51, SliderState = curveState, CurveTargetBrightness = curveTarget };
        long manualRevision = row.ManualBrightnessRevision;

        List<MonitorInfo> released = BrightnessFlyoutWindow.ReleaseCurveDrivenRows([row]);

        Assert.Same(row, Assert.Single(released));
        Assert.Equal(SliderState.CurveReleased, row.SliderState);
        Assert.False(row.IsCurveDriven);
        Assert.Equal(expected: 51, row.EffectiveRoundedBrightness);
        Assert.True(row.ManualBrightnessRevision > manualRevision);
    }

    [Theory]
    [InlineData(SliderState.Enabled)]
    [InlineData(SliderState.Disabled)]
    [InlineData(SliderState.Failed)]
    [InlineData(SliderState.CurveReleased)]
    public void MasterManualOverrideLeavesRowsOutsideCurveControlUntouched(SliderState state)
    {
        MonitorInfo row = new() { Brightness = 51, SliderState = state };
        long manualRevision = row.ManualBrightnessRevision;

        List<MonitorInfo> released = BrightnessFlyoutWindow.ReleaseCurveDrivenRows([row]);

        Assert.Empty(released);
        Assert.Equal(state, row.SliderState);
        Assert.Equal(manualRevision, row.ManualBrightnessRevision);
    }

    [Theory]
    [InlineData(SliderState.CurveActive)]
    [InlineData(SliderState.CurveReleased)]
    public void AnyManualDisplayPutsMasterInManualMode(SliderState masterState)
    {
        MonitorInfo curve = new() { SliderState = SliderState.CurveActive };
        MonitorInfo manual = new() { SliderState = SliderState.CurveReleased };

        SliderState next = BrightnessFlyoutWindow.ResolveMasterCurveModeFromIndividuals(
            masterState,
            [curve, manual],
            inDisabledPeriod: false);

        Assert.Equal(SliderState.CurveReleased, next);
    }

    [Fact]
    public void UnreachableDisplayCarryingManualOverrideKeepsMasterManual()
    {
        MonitorInfo curve = new() { SliderState = SliderState.CurveActive };
        MonitorInfo away = new() { SliderState = SliderState.CurveReleased };
        away.SliderState = SliderState.Failed;

        SliderState next = BrightnessFlyoutWindow.ResolveMasterCurveModeFromIndividuals(
            SliderState.CurveReleased,
            [curve, away],
            inDisabledPeriod: false);

        Assert.Equal(SliderState.CurveReleased, next);
    }

    [Theory]
    [InlineData(false, SliderState.CurveActive)]
    [InlineData(true, SliderState.CurveSleeping)]
    public void ManualMasterReturnsToCurveOnceEveryDisplayHas(bool inDisabledPeriod, SliderState expected)
    {
        MonitorInfo curve = new() { SliderState = SliderState.CurveActive };
        MonitorInfo excluded = new() { SliderState = SliderState.Disabled };

        SliderState next = BrightnessFlyoutWindow.ResolveMasterCurveModeFromIndividuals(
            SliderState.CurveReleased,
            [curve, excluded],
            inDisabledPeriod);

        Assert.Equal(expected, next);
    }

    [Theory]
    [InlineData(SliderState.Enabled)]
    [InlineData(SliderState.CurveReleased)]
    public void MasterCurveModeStaysWithoutCurveControlledDisplays(SliderState masterState)
    {
        MonitorInfo excluded = new() { SliderState = SliderState.Disabled };
        MonitorInfo enabled = new() { SliderState = SliderState.Enabled };

        SliderState next = BrightnessFlyoutWindow.ResolveMasterCurveModeFromIndividuals(
            masterState,
            [excluded, enabled],
            inDisabledPeriod: false);

        Assert.Equal(masterState, next);
    }

    [Theory]
    [InlineData(true, true, SliderState.CurveReleased, SliderState.CurveActive, true)]
    [InlineData(true, true, SliderState.CurveReleased, SliderState.CurveSleeping, false)]
    [InlineData(true, true, SliderState.CurveReleased, SliderState.CurveReleased, false)]
    [InlineData(true, true, SliderState.CurveActive, SliderState.CurveActive, false)]
    [InlineData(true, false, SliderState.CurveReleased, SliderState.CurveActive, false)]
    [InlineData(false, true, SliderState.CurveReleased, SliderState.CurveActive, false)]
    public void ManualMasterLeavesOnlyCurveActiveDisplaysAtTheirCurveValue(
        bool isCurveEnabled,
        bool isAbsoluteMode,
        SliderState masterState,
        SliderState rowState,
        bool expected)
    {
        MonitorInfo master = new() { IsMaster = true, SliderState = masterState };
        MonitorInfo row = new() { SliderState = rowState };

        bool held = BrightnessFlyoutWindow.IsCurveHeldUnderManualMaster(isCurveEnabled, isAbsoluteMode, master, row);

        Assert.Equal(expected, held);
    }
}
