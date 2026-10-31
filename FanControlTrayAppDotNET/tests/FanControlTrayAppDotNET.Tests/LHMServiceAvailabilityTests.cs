using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.Services;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class LHMServiceAvailabilityTests
{
    [Fact]
    public void ControlSensorsMissingAfterResumeAreReported()
    {
        HashSet<string> before = new(StringComparer.OrdinalIgnoreCase) { "Hub.Control.Fan_1", "Board.Control.Fan_1" };

        List<string> missing = LHMService.MissingControlKeys(before, ["board.control.fan_1"]);

        Assert.Equal(["Hub.Control.Fan_1"], missing);
        Assert.Empty(LHMService.MissingControlKeys(before, ["Hub.Control.Fan_1", "Board.Control.Fan_1", "New.Control.X"]));
    }

    [Theory]
    [InlineData(false, false, 50.0, 1200, FanState.Disabled)]
    [InlineData(false, true, 0.0, 0, FanState.Disabled)]
    [InlineData(true, true, 50.0, 1200, FanState.Detached)]
    [InlineData(true, false, 50.0, 0, FanState.Detached)]
    [InlineData(true, false, 50.0, 1200, FanState.Normal)]
    [InlineData(true, false, 0.0, 0, FanState.Normal)]
    public void FanWithoutAControlSensorIsUnavailable(
        bool controlAvailable,
        bool forcedNonFunctioning,
        double dutyCycle,
        int rpm,
        FanState expected) =>
        Assert.Equal(expected, LHMService.ResolveFanState(controlAvailable, forcedNonFunctioning, dutyCycle, rpm));
}
