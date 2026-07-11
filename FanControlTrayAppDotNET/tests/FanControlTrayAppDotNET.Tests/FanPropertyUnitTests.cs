using FanControlTrayAppDotNET.Models;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanPropertyUnitTests
{
    /// <summary>
    /// Verifies each fan property can convert to the assigned curve unit independently.
    /// </summary>
    [Fact]
    public void FanPropertiesConvertToCurveUnitIndependently()
    {
        Fan fan = new()
        {
            MaxRPM = 3000,
            ClampLow = 30,
            ClampLowRPMMode = false,
            ClampHigh = 2400,
            ClampHighRPMMode = true
        };
        Curve rpmCurve = new() { RPMMode = true, MaxRPM = 3000 };
        Curve dutyCycleCurve = new() { RPMMode = false, MaxRPM = 3000 };

        Assert.Equal(900, fan.ClampLowForCurve(rpmCurve));
        Assert.Equal(80, fan.ClampHighForCurve(dutyCycleCurve));
    }

    /// <summary>
    /// Verifies curve assignment no longer forces the fan display unit.
    /// </summary>
    [Fact]
    public void AssignedCurveDoesNotForceFanRPMMode()
    {
        string curveName = $"Unit Test Curve {Guid.NewGuid():N}";
        Curve curve = new()
        {
            CurveName = curveName,
            RPMMode = true
        };
        Curve.Register(curve);
        Fan fan = new()
        {
            RPMMode = false, AssignedCurveName = curveName
        };

        FanCurveModeSync.ApplyToFan(fan, curve);

        Assert.False(fan.RPMMode);
    }
}
