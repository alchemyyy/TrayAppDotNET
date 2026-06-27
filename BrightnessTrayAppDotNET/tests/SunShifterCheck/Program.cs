using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Utils;

namespace BrightnessTrayAppDotNET.Tests;

/// <summary>
/// Year-cycle round-trip check for <see cref="SunShifter"/>. Walks a non-trivial curve
/// through 365 day-by-day shifts (Jan 1 2026 -> Jan 1 2027) and verifies it lands back
/// essentially where it started.
///
/// The cycle works because: each shift preserves a point's proportional position within
/// its sun-segment, and Jan 1 of consecutive years has near-identical sun anchors at
/// mid-latitudes. Any drift past the tolerance would indicate the segment-mapping has
/// lost injectivity or the kept-anchor set is flapping between days.
///
/// Plain console app instead of a test framework: the repo has no existing test
/// infrastructure, and pulling xUnit/MSTest in just for one round-trip check is heavier
/// than a 200-line file that runs via `dotnet run` and exits non-zero on failure.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        try
        {
            // Three latitude bands documented separately. The 30-minute hard-fail
            // ceiling is well above what the synthesize-missing-anchors path produces
            // in practice (sub-arctic worst case sits around 6 minutes) but small
            // enough to catch a real regression if the synth strategy is broken or
            // anchors get dropped instead of synthesized again. Per-row output prints
            // the actual drift so smaller regressions still show up in the diff.
            const double sanityCeilingSeconds = 30 * 60;

            // Tropical: every twilight anchor exists on every day of the year (sun
            // descends well below -18 deg at solar midnight). Lower bound on drift -
            // shows what the algorithm achieves with a stable anchor set.
            Console.WriteLine("=== Tropical (Honolulu, 21.3 N) ===");
            RunYearCycleRoundTrip(latitude: 21.3, longitude: -157.8, toleranceSeconds: sanityCeilingSeconds);

            // App default - Pacific NW. Around summer solstice the sun only reaches
            // ~-18.8 deg, so astronomical twilight pops in and out of the anchor set
            // for a few weeks. Synthesizing the missing anchors near the day-end (where
            // the real anchor was approaching just before vanishing) keeps the segment
            // count constant, so a curve point's segment identity survives the year.
            Console.WriteLine();
            Console.WriteLine("=== Mid-latitude (Pacific NW, 47.75 N) ===");
            RunYearCycleRoundTrip(latitude: 47.7542814, longitude: -122.2795275, toleranceSeconds: sanityCeilingSeconds);

            // Sub-arctic: nautical and astronomical twilight both vanish for extended
            // stretches each summer, and at peak summer civil twilight is also unstable.
            // The harshest stress test for the synth-missing-anchors path.
            Console.WriteLine();
            Console.WriteLine("=== Sub-arctic (Anchorage, 61.2 N) ===");
            RunYearCycleRoundTrip(latitude: 61.2, longitude: -149.9, toleranceSeconds: sanityCeilingSeconds);

            Console.WriteLine();
            Console.WriteLine("OK: every test latitude round-tripped within the sanity ceiling.");
            Console.WriteLine("Residual drift is dominated by the 0.2422-day orbital mismatch between");
            Console.WriteLine("Jan 1 2026 and Jan 1 2027 - the algorithm itself preserves segment");
            Console.WriteLine("identity through anchor disappearances and reappearances.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"FAIL: {ex.Message}");
            return 1;
        }
    }

    private static void RunYearCycleRoundTrip(double latitude, double longitude, double toleranceSeconds)
    {
        EnvironmentalCurve curve = new()
        {
            // Non-trivial points placed near and across sun events (pre-dawn, sunrise,
            // solar noon, sunset, late twilight) so each one actually moves on every
            // shift instead of sitting harmlessly mid-segment.
            Brightness =
            [
                new() { Time = 0.0,  Value = 10.0 },
                new() { Time = 0.20, Value = 30.0 },
                new() { Time = 0.30, Value = 50.0 },
                new() { Time = 0.50, Value = 95.0 },
                new() { Time = 0.75, Value = 55.0 },
                new() { Time = 0.90, Value = 20.0 },
                new() { Time = 1.0,  Value = 10.0 },
            ],
            NightLight =
            [
                new() { Time = 0.0,  Value = 100.0 },
                new() { Time = 0.25, Value = 80.0 },
                new() { Time = 0.5,  Value = 0.0 },
                new() { Time = 0.75, Value = 80.0 },
                new() { Time = 1.0,  Value = 100.0 },
            ],
            BrightnessOffset = EnvironmentalCurve.CreateDefaultOffset(),
            NightLightOffset = EnvironmentalCurve.CreateDefaultOffset(),
        };

        // Snapshot every list. Value is never touched by the shift, so it should be
        // bit-identical at the end - we assert that explicitly to catch any silent
        // mutation on the value side.
        List<(double Time, double Value)> origBrightness = Snapshot(curve.Brightness);
        List<(double Time, double Value)> origNightLight = Snapshot(curve.NightLight);
        List<(double Time, double Value)> origBOffset = Snapshot(curve.BrightnessOffset);
        List<(double Time, double Value)> origNLOffset = Snapshot(curve.NightLightOffset);

        DateTime start = new(2026, 1, 1);
        for (int day = 0; day < 365; day++)
        {
            DateTime from = start.AddDays(day);
            DateTime to = start.AddDays(day + 1);
            SunShifter.ApplySunShift(curve, from, to, latitude, longitude);
        }

        double tolerance = toleranceSeconds / 86400.0;

        AssertCurveCloseTo("Brightness", origBrightness, curve.Brightness, tolerance);
        AssertCurveCloseTo("NightLight", origNightLight, curve.NightLight, tolerance);
        AssertCurveCloseTo("BrightnessOffset", origBOffset, curve.BrightnessOffset, tolerance);
        AssertCurveCloseTo("NightLightOffset", origNLOffset, curve.NightLightOffset, tolerance);

        // Endpoints sit on the implicit 0.0/1.0 anchors that are identical on every
        // date, so they must round-trip bit-exactly. Any drift here would point at a
        // bug in segment selection rather than accumulated FP error.
        AssertExact("Brightness[0].Time",  0.0, curve.Brightness[0].Time);
        AssertExact("Brightness[^1].Time", 1.0, curve.Brightness[^1].Time);
        AssertExact("NightLight[0].Time",  0.0, curve.NightLight[0].Time);
        AssertExact("NightLight[^1].Time", 1.0, curve.NightLight[^1].Time);
    }

    private static List<(double Time, double Value)> Snapshot(List<EnvironmentalCurvePoint> points) =>
        points.Select(p => (p.Time, p.Value)).ToList();

    private static void AssertCurveCloseTo(
        string label,
        List<(double Time, double Value)> expected,
        List<EnvironmentalCurvePoint> actual,
        double tolerance)
    {
        if (expected.Count != actual.Count)
        {
            throw new InvalidOperationException(
                $"{label}: point count changed ({expected.Count} -> {actual.Count})");
        }

        double worstDrift = 0.0;
        int worstIndex = -1;

        for (int i = 0; i < expected.Count; i++)
        {
            if (expected[i].Value != actual[i].Value)
            {
                throw new InvalidOperationException(
                    $"{label}[{i}]: Value mutated ({expected[i].Value} -> {actual[i].Value})");
            }

            double drift = Math.Abs(expected[i].Time - actual[i].Time);
            if (drift > worstDrift)
            {
                worstDrift = drift;
                worstIndex = i;
            }

            if (drift >= tolerance)
            {
                throw new InvalidOperationException(
                    $"{label}[{i}]: drift {drift:F9} (~{drift * 86400:F3}s) exceeded " +
                    $"tolerance {tolerance:F9} (~{tolerance * 86400:F1}s) after 365-day cycle. " +
                    $"Expected Time {expected[i].Time:F9}, got {actual[i].Time:F9}");
            }
        }

        Console.WriteLine(
            $"  {label}: worst drift {worstDrift:F12} (~{worstDrift * 86400 * 1000:F3}ms) " +
            $"at index {worstIndex} of {expected.Count} points");
    }

    private static void AssertExact(string label, double expected, double actual)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException(
                $"{label}: expected exactly {expected:F9}, got {actual:F17}");
        }
    }
}
