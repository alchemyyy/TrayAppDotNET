using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Utils;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

/// <summary>
/// Walks a curve through 365 day-by-day sun shifts (Jan 1 2026 -> Jan 1 2027). Each shift keeps a point's proportional
/// position inside its sun segment, and consecutive Jan 1sts have near-identical anchors, so the curve must come back
/// to where it started. Drift past the ceiling means the segment mapping lost injectivity or the anchor set flapped.
/// </summary>
public sealed class SunShifterYearCycleTests
{
    // Well above the synthesized-anchor drift seen at sub-arctic latitudes (about six minutes), small enough to catch
    // dropped anchors or a broken synthesis strategy.
    private const double DriftCeilingDays = 30.0 * 60.0 / 86400.0;

    [Theory]
    // Tropical: every twilight anchor exists every day of the year.
    [InlineData(21.3, -157.8)]
    // App default, Pacific Northwest: astronomical twilight comes and goes around the summer solstice.
    [InlineData(47.7542814, -122.2795275)]
    // Sub-arctic: nautical and astronomical twilight vanish for weeks, civil twilight is unstable at peak summer.
    [InlineData(61.2, -149.9)]
    public void AYearOfDailyShiftsReturnsTheCurveToItsStart(double latitude, double longitude)
    {
        EnvironmentalCurve curve = new()
        {
            // Points near and across sun events, so each one moves on every shift
            Brightness =
            [
                new() { Time = 0.0, Value = 10.0 },
                new() { Time = 0.20, Value = 30.0 },
                new() { Time = 0.30, Value = 50.0 },
                new() { Time = 0.50, Value = 95.0 },
                new() { Time = 0.75, Value = 55.0 },
                new() { Time = 0.90, Value = 20.0 },
                new() { Time = 1.0, Value = 10.0 }
            ],
            NightLight =
            [
                new() { Time = 0.0, Value = 100.0 },
                new() { Time = 0.25, Value = 80.0 },
                new() { Time = 0.5, Value = 0.0 },
                new() { Time = 0.75, Value = 80.0 },
                new() { Time = 1.0, Value = 100.0 }
            ],
            BrightnessOffset = EnvironmentalCurve.CreateDefaultOffset(),
            NightLightOffset = EnvironmentalCurve.CreateDefaultOffset()
        };
        List<(double Time, double Value)> brightness = Snapshot(curve.Brightness);
        List<(double Time, double Value)> nightLight = Snapshot(curve.NightLight);
        List<(double Time, double Value)> brightnessOffset = Snapshot(curve.BrightnessOffset);
        List<(double Time, double Value)> nightLightOffset = Snapshot(curve.NightLightOffset);

        DateTime start = new(2026, 1, 1);
        for (int day = 0; day < 365; day++)
            SunShifter.ApplySunShift(curve, start.AddDays(day), start.AddDays(day + 1), latitude, longitude);

        AssertRoundTripped(brightness, curve.Brightness);
        AssertRoundTripped(nightLight, curve.NightLight);
        AssertRoundTripped(brightnessOffset, curve.BrightnessOffset);
        AssertRoundTripped(nightLightOffset, curve.NightLightOffset);

        // The 0.0 and 1.0 endpoints sit on anchors that are identical on every date
        Assert.Equal(0.0, curve.Brightness[0].Time);
        Assert.Equal(1.0, curve.Brightness[^1].Time);
        Assert.Equal(0.0, curve.NightLight[0].Time);
        Assert.Equal(1.0, curve.NightLight[^1].Time);
    }

    private static List<(double Time, double Value)> Snapshot(List<EnvironmentalCurvePoint> points) =>
        [.. points.Select(static point => (point.Time, point.Value))];

    private static void AssertRoundTripped(List<(double Time, double Value)> expected, List<EnvironmentalCurvePoint> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            // A shift moves times only; values must come back bit-identical
            Assert.Equal(expected[index].Value, actual[index].Value);
            Assert.InRange(Math.Abs(expected[index].Time - actual[index].Time), 0.0, DriftCeilingDays);
        }
    }
}
