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

        Assert.Equal(expected: 900, fan.ClampLowForCurve(rpmCurve));
        Assert.Equal(expected: 80, fan.ClampHighForCurve(dutyCycleCurve));
    }
}
