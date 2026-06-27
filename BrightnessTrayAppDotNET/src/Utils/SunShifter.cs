using BrightnessTrayAppDotNET.SunriseSunset;

namespace BrightnessTrayAppDotNET.Utils;

/// <summary>
/// Reanchors environmental curve points from one set of sun events to another.
/// Each point keeps its proportional position within its segment
/// (e.g. "30% of the way from civil dawn to sunrise" stays "30% of the way from civil dawn to sunrise"),
/// so the curve drifts in clock-time as the day/night cycle shifts across the year,
/// the user moves between locations,
/// or the timezone-offset interpretation flips between DST-aware and standard-time-year-round.
/// The shift is destructive: <see cref="EnvironmentalCurvePoint.Time"/> values are rewritten in place.
/// </summary>
public static class SunShifter
{
    /// <summary>
    /// Remaps every point on every list in <paramref name="curve"/>
    /// from <paramref name="from"/>'s sun anchors to <paramref name="to"/>'s.
    /// No-op if the two anchors are equivalent,
    /// either side's coordinates are unset or out of range,
    /// or no usable anchor pair can be built
    /// (e.g. one of the dates lands in a polar transition where every twilight event the SPA returns
    /// also vanishes on the other date).
    /// </summary>
    public static void ApplySunShift(EnvironmentalCurve? curve, SunAnchor from, SunAnchor to)
    {
        if (curve == null) return;

        if (AnchorsEqual(from, to)) return;

        // Match the editor's "unset" sentinel and bounds checks on both sides -
        // feeding 0,0 or out-of-range coordinates to the SPA returns garbage rather than throwing.
        // Either side being invalid means we can't build matched anchor arrays,
        // so silently skip and leave the curve untouched.
        if (!CoordsValid(from.Latitude, from.Longitude) || !CoordsValid(to.Latitude, to.Longitude)) return;

        if (!TryBuildAnchorPair(from, to, out double[] fromAnchors, out double[] toAnchors)) return;

        ShiftPoints(curve.Brightness, fromAnchors, toAnchors);
        ShiftPoints(curve.NightLight, fromAnchors, toAnchors);
        ShiftPoints(curve.BrightnessOffset, fromAnchors, toAnchors);
        ShiftPoints(curve.NightLightOffset, fromAnchors, toAnchors);
    }

    /// <summary>
    /// Date-only convenience overload: shifts in place between two dates that share the same location and DST flag.
    /// Used by tests and any caller that hasn't yet adopted the per-axis anchor model.
    /// </summary>
    public static void ApplySunShift(
        EnvironmentalCurve curve,
        DateTime fromDate,
        DateTime toDate,
        double latitude,
        double longitude,
        bool useDaylightSavings = true)
        => ApplySunShift(
            curve,
            new SunAnchor(fromDate, latitude, longitude, useDaylightSavings),
            new SunAnchor(toDate, latitude, longitude, useDaylightSavings));

    /// <summary>
    /// Returns a fresh <see cref="EnvironmentalCurve"/> whose points have been shifted from
    /// <paramref name="from"/>'s sun anchors to <paramref name="to"/>'s; the source is not modified.
    /// Preview-friendly variant of <see cref="ApplySunShift(EnvironmentalCurve, SunAnchor, SunAnchor)"/>,
    /// used to show the live curve without touching the persisted anchor shape.
    /// </summary>
    public static EnvironmentalCurve BuildPreview(EnvironmentalCurve source, SunAnchor from, SunAnchor to)
    {
        EnvironmentalCurve clone = ClonePoints(source);
        ApplySunShift(clone, from, to);
        return clone;
    }

    /// <summary>
    /// Date-only convenience overload of <see cref="BuildPreview(EnvironmentalCurve, SunAnchor, SunAnchor)"/>,
    /// kept for the same reason as the date-only
    /// <see cref="ApplySunShift(EnvironmentalCurve, DateTime, DateTime, double, double, bool)"/> overload.
    /// </summary>
    public static EnvironmentalCurve BuildPreview(
        EnvironmentalCurve source,
        DateTime fromDate,
        DateTime toDate,
        double latitude,
        double longitude,
        bool useDaylightSavings = true)
        => BuildPreview(
            source,
            new SunAnchor(fromDate, latitude, longitude, useDaylightSavings),
            new SunAnchor(toDate, latitude, longitude, useDaylightSavings));

    /// <summary>
    /// Single-value variant of <see cref="ApplySunShift(EnvironmentalCurve, SunAnchor, SunAnchor)"/>:
    /// returns <paramref name="t"/> reanchored from <paramref name="from"/>'s sun events to <paramref name="to"/>'s,
    /// using the same segment-relative remap as the curve point lists.
    /// Pass-through when either side's coordinates are unset/out of range or no usable anchor pair can be built -
    /// same fail-soft behaviour as the list overloads, so a polar-edge day doesn't produce a garbled value.
    /// Used by callers that carry a scalar t outside an <see cref="EnvironmentalCurve"/>
    /// (e.g. the disabled-period bounds, which have an anchor independent of the curve's).
    /// </summary>
    public static double ShiftTime(double t, SunAnchor from, SunAnchor to)
    {
        if (AnchorsEqual(from, to)
            || !CoordsValid(from.Latitude, from.Longitude)
            || !CoordsValid(to.Latitude, to.Longitude)
            || !TryBuildAnchorPair(from, to, out double[] fromAnchors, out double[] toAnchors))
            return t;

        double clamped = Math.Clamp(t, 0.0, 1.0);
        int i = FindSegment(fromAnchors, clamped);
        double segLen = fromAnchors[i + 1] - fromAnchors[i];
        double prop = segLen > 0 ? (clamped - fromAnchors[i]) / segLen : 0.0;
        return toAnchors[i] + prop * (toAnchors[i + 1] - toAnchors[i]);
    }

    private static bool AnchorsEqual(SunAnchor a, SunAnchor b) =>
        a.Date.Date == b.Date.Date
        && a.Latitude == b.Latitude
        && a.Longitude == b.Longitude
        && a.UseDaylightSavings == b.UseDaylightSavings;

    private static bool CoordsValid(double latitude, double longitude) =>
        !((latitude == 0.0 && longitude == 0.0)
          || latitude < -90.0 || latitude > 90.0
          || longitude < -180.0 || longitude > 180.0);

    private static EnvironmentalCurve ClonePoints(EnvironmentalCurve source) => new()
    {
        Brightness = ClonePointList(source.Brightness),
        NightLight = ClonePointList(source.NightLight),
        BrightnessOffset = ClonePointList(source.BrightnessOffset),
        NightLightOffset = ClonePointList(source.NightLightOffset),
        BrightnessOffsetMin = source.BrightnessOffsetMin,
        BrightnessOffsetMax = source.BrightnessOffsetMax,
        NightLightOffsetMin = source.NightLightOffsetMin,
        NightLightOffsetMax = source.NightLightOffsetMax,
        FollowTheSun = source.FollowTheSun,
        BrightnessAnchor = source.BrightnessAnchor,
        UseDaylightSavings = source.UseDaylightSavings,
    };

    private static List<EnvironmentalCurvePoint> ClonePointList(List<EnvironmentalCurvePoint> source)
    {
        List<EnvironmentalCurvePoint> copy = new(source.Count);
        foreach (EnvironmentalCurvePoint p in source)
            copy.Add(new EnvironmentalCurvePoint { Time = p.Time, Value = p.Value });
        return copy;
    }

    /// <summary>
    /// Builds two parallel anchor arrays - both strictly increasing on [0,1] and the same length,
    /// so the i'th from-anchor corresponds to the i'th to-anchor.
    /// The arrays always begin at 0.0 and end at 1.0 (midnight endpoints stay fixed).
    ///
    /// To avoid drift when twilight anchors blink in and out across the year
    /// (e.g. astronomical twilight disappears at mid-latitudes for a few weeks around summer solstice),
    /// missing anchors are SYNTHESIZED at a small offset just inside their next surviving neighbor
    /// instead of being dropped.
    /// That keeps the segment count constant across all 365 days,
    /// so a curve point's "I live in the segment between civil dawn and sunrise" identity survives the year
    /// regardless of whether deeper twilight anchors are real or synthetic on a given day.
    /// Without this, a point in a vanishing-and-returning segment can drift by hours over a year cycle.
    /// </summary>
    private static bool TryBuildAnchorPair(
        SunAnchor from, SunAnchor to,
        out double[] fromAnchors, out double[] toAnchors)
    {
        fromAnchors = [];
        toAnchors = [];

        SunTimes? fromSun = TryGetSun(from.Date, from.Latitude, from.Longitude, from.UseDaylightSavings);
        SunTimes? toSun = TryGetSun(to.Date, to.Latitude, to.Longitude, to.UseDaylightSavings);
        if (fromSun is null || toSun is null) return false;

        double[]? fromArr = BuildAnchorArray(fromSun);
        double[]? toArr = BuildAnchorArray(toSun);
        if (fromArr is null || toArr is null) return false;

        fromAnchors = fromArr;
        toAnchors = toArr;
        return true;
    }

    /// <summary>
    /// Returns the 11-element anchor array, strictly increasing:
    /// [0.0, astroDawn, nauticalDawn, civilDawn, sunrise, solarNoon, sunset, civilDusk, nauticalDusk, astroDusk, 1.0].
    /// Returns <c>null</c> if the day is too pathological to anchor (sunrise, solar noon, or sunset missing,
    /// or synthesis would overflow [0,1]).
    /// </summary>
    private static double[]? BuildAnchorArray(SunTimes sun)
    {
        // Canonical chronological order. Indexes 3 (sunrise), 4 (solar noon), 5 (sunset) are load-bearing -
        // if any are null the day is polar-night-ish and we can't build a sensible anchor list.
        // Twilight anchors at indexes 0..2 and 6..8 may be null; those get synthesized below.
        DateTimeOffset?[] events =
        [
            sun.Twilight?.AstronomicalDawn,
            sun.Twilight?.NauticalDawn,
            sun.Twilight?.CivilDawn,
            sun.Sunrise,
            sun.SolarNoon,
            sun.Sunset,
            sun.Twilight?.CivilDusk,
            sun.Twilight?.NauticalDusk,
            sun.Twilight?.AstronomicalDusk,
        ];

        double?[] values = new double?[9];
        for (int i = 0; i < 9; i++) values[i] = ToDayFraction(events[i]);

        if (values[3] is null || values[4] is null || values[5] is null) return null;

        // 1-second epsilon: large enough to clear FP noise on comparisons,
        // small enough that synthetic anchors stay tightly clustered
        // and don't materially affect points in real-anchor segments.
        const double eps = 1.0 / 86400.0;

        // Dawn side: walk forward through 0,1,2 from the day-start.
        // Each missing anchor lands eps after the previous kept value (or 0.0).
        // Anchoring at 0.0 instead of just-before-sunrise gives synth/real boundary days a smooth match:
        // a real anchor approaches solar midnight (~0.0 morning-side clock-time) just before vanishing,
        // so the synth value picks up where the real one left off.
        double prev = 0.0;
        for (int i = 0; i <= 2; i++)
        {
            if (values[i] is null)
            {
                prev += eps;
                values[i] = prev;
            }
            else
                prev = values[i]!.Value;
        }

        // Dusk side: mirror image, walking backward through 8,7,6 from the day-end.
        // Real dusk anchors approach midnight (~1.0) just before vanishing,
        // so synth at 1.0 - eps continues that trajectory smoothly across the disappearance.
        prev = 1.0;
        for (int i = 8; i >= 6; i--)
        {
            if (values[i] is null)
            {
                prev -= eps;
                values[i] = prev;
            }
            else
                prev = values[i]!.Value;
        }

        // Synthesis-against-real adjacency check:
        // if a real outer anchor sits within a few seconds of midnight on its own side,
        // the synth chain on the OTHER side of it can collide.
        // Genuinely polar territory; bail rather than ship a non-monotonic array.
        // (The trailing strict-monotonicity sanity loop below catches anything else.)
        if (values[0]!.Value <= 0.0 || values[8]!.Value >= 1.0) return null;

        double[] result = new double[11];
        result[0] = 0.0;
        for (int i = 0; i < 9; i++) result[i + 1] = values[i]!.Value;
        result[10] = 1.0;

        // Defensive sanity check - holds by construction,
        // but a violation here would silently produce zero-width segments and divide-by-zero downstream.
        for (int i = 1; i < result.Length; i++)
            if (result[i] <= result[i - 1])
                return null;

        return result;
    }

    private static SunTimes? TryGetSun(DateTime localDate, double latitude, double longitude, bool useDaylightSavings)
    {
        try
        {
            // Noon dodges the DST midnight ambiguity when materialising a date offset.
            // With DST on, use whichever offset is in effect at the target's local noon
            // so spring-forward/fall-back days line up with wall-clock.
            // With DST off, pin to BaseUtcOffset year-round
            // so sun-event clock-times don't shift by an hour at the DST boundaries.
            DateTime localNoon = DateTime.SpecifyKind(localDate.Date.AddHours(12), DateTimeKind.Unspecified);
            TimeSpan offset = useDaylightSavings
                ? TimeZoneInfo.Local.GetUtcOffset(localNoon)
                : TimeZoneInfo.Local.BaseUtcOffset;
            DateTimeOffset reference = new(localNoon, offset);
            return SPACalculator.GetSunTimes(latitude, longitude, reference);
        }
        catch
        {
            return null;
        }
    }

    // Use t.DateTime here, not t.LocalDateTime -
    // the latter would reinterpret the instant in the system's actual timezone (DST-aware)
    // and silently undo the offset we asked TryGetSun to pin to
    // (e.g. standard time when UseDaylightSavings is off).
    private static double? ToDayFraction(DateTimeOffset? when)
    {
        if (when is not { } t) return null;

        double hours = t.DateTime.TimeOfDay.TotalHours;
        if (hours is < 0.0 or > 24.0) return null;

        return Math.Clamp(hours / 24.0, 0.0, 1.0);
    }

    private static void ShiftPoints(List<EnvironmentalCurvePoint>? points, double[] from, double[] to)
    {
        if (points == null || points.Count == 0) return;

        foreach (EnvironmentalCurvePoint p in points)
        {
            double t = Math.Clamp(p.Time, 0.0, 1.0);
            int i = FindSegment(from, t);
            double segLen = from[i + 1] - from[i];
            double prop = segLen > 0 ? (t - from[i]) / segLen : 0.0;
            p.Time = to[i] + prop * (to[i + 1] - to[i]);
        }

        // Both anchor arrays are strictly increasing and the remap is monotonic segment-by-segment,
        // so order is preserved -
        // the sort is cheap insurance against numeric noise pushing two adjacent points to the same fraction.
        points.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    private static int FindSegment(double[] anchors, double t)
    {
        // anchors are strictly increasing, length >= 2, [0]=0.0 and [^1]=1.0.
        for (int i = 0; i < anchors.Length - 1; i++)
            if (t <= anchors[i + 1])
                return i;
        return anchors.Length - 2;
    }
}
