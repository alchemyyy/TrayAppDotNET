using System.Globalization;
using System.Xml.Serialization;

namespace BrightnessTrayAppDotNET.Models;

/// <summary>
/// Combined anchor state for one end of a sun-shift:
/// the calendar date plus the location and DST interpretation that defined "where the sun is" at that date.
/// Stored on <see cref="EnvironmentalCurve"/> twice
/// (once for the curve, once for the disabled period), and consumed by <c>SunShifter</c> as the from / to pair.
/// Value semantics via <c>readonly record struct</c>: members compared in declaration order.
/// </summary>
public readonly record struct SunAnchor(
    DateTime Date,
    double Latitude,
    double Longitude,
    bool UseDaylightSavings);

/// <summary>
/// One control point on an environmental curve.
/// <see cref="Time"/> is the normalised position on the 24h cycle (0 = midnight, 1 = next midnight).
/// <see cref="Value"/> is the output on a 0..100 scale
/// - interpreted as 0-100% brightness or 0-100 night-light strength depending on which curve it lives on.
/// </summary>
public class EnvironmentalCurvePoint
{
    [XmlAttribute]
    public double Time { get; set; }

    [XmlAttribute]
    public double Value { get; set; }
}

/// <summary>
/// Per-profile environmental curves.
/// Each profile carries two independent series (brightness, night-light) sampled across a 24h cycle,
/// with a parallel "offset" set for the additive/subtractive mode.
/// Storing both sets independently means toggling between absolute and offset mode
/// is a pure swap of which list the editor reads - no lossy remapping when the user flips the switch.
///
/// Brightness defaults to a flat 75 line, night-light to a flat 25 line;
/// offset curves default to a flat midline (Value 50, which maps to "+0" in the editor's -100..+100 display).
/// Offset clamp limits (<see cref="BrightnessOffsetMin"/> etc.) default to the full canvas range
/// so the user sees no clamp until they explicitly drag the dashed limit lines inward.
/// </summary>
public class EnvironmentalCurve
{
    // List properties default to empty - NOT to CreateDefault() - so the explicit XML loader can append only
    // persisted <P> elements. A non-empty initializer would mean every Save->Load round trip stacks another copy
    // of the default edge pair on top of the saved data. EnsureNormalized backfills empties with defaults at use-time instead.
    [XmlArrayItem("P")]
    public List<EnvironmentalCurvePoint> Brightness { get; set; } = [];

    [XmlArrayItem("P")]
    public List<EnvironmentalCurvePoint> NightLight { get; set; } = [];

    [XmlArrayItem("P")]
    public List<EnvironmentalCurvePoint> BrightnessOffset { get; set; } = [];

    [XmlArrayItem("P")]
    public List<EnvironmentalCurvePoint> NightLightOffset { get; set; } = [];

    // Offset clamp limits, stored on the same 0..100 scale as the curves (0 = -100 offset, 50 = +0, 100 = +100).
    // Defaults span the full range so a freshly-toggled offset mode applies no clamp until the user drags the lines in.
    [XmlAttribute]
    public double BrightnessOffsetMin { get; set; } = 0.0;

    [XmlAttribute]
    public double BrightnessOffsetMax { get; set; } = 100.0;

    [XmlAttribute]
    public double NightLightOffsetMin { get; set; } = 0.0;

    [XmlAttribute]
    public double NightLightOffsetMax { get; set; } = 100.0;

    // Monotonically increasing token bumped whenever the curve's shape is mutated in place
    // (point edits / period drags / FollowTheSun toggle / etc.).
    // Used by <see cref="Services.EnvironmentalCurveService"/> as a second cache key
    // alongside <c>ReferenceEquals(stored)</c>:
    // the reference check catches "different curve object" (profile switch / promote-on-edit),
    // and the version check catches "same reference, mutated points" (settings editor in-place edit).
    // <see cref="Services.EnvironmentalCurveService.RequestEvaluation"/> and
    // <see cref="Services.EnvironmentalCurveService.InvalidateCurveCache"/> bump this on every cache nuke
    // so future direct callers don't need to remember a parallel invalidation call.
    // because this is a runtime-only token; persisted curves restart at 0 on load.
    [XmlIgnore]
    public int Version { get; set; }

    // "Follow the sun" reanchors curve points from one set of sun events to another's
    // whenever the editor or runtime catches up.
    // Each point keeps its proportional position within a sun-segment (e.g. "midway between civil dawn and sunrise"),
    // so the curve drifts in clock-time as days lengthen and shorten across the year,
    // as the user moves between locations, or as the timezone offset interpretation flips.
    // The shift is destructive - curve point Time values are rewritten in place
    // and the anchor state (date, latitude, longitude, DST flag) at last manual edit is recorded
    // so the editor can shift from there to the live state on every display.
    [XmlAttribute]
    public bool FollowTheSun { get; set; } = true;

    // Combined anchor state for the brightness/night-light curve under FollowTheSun.
    // because the XML shape is preserved by the four flat properties below
    // (LastSunShiftDate / LastSunShiftLatitude / LastSunShiftLongitude / LastSunShiftUseDaylightSavings),
    // each of which routes through this struct on get/set.
    // Default Date = DateTime.MinValue maps to the "" string sentinel in persisted XML.
    [XmlIgnore]
    public SunAnchor BrightnessAnchor { get; set; } = new(default, 0.0, 0.0, true);

    // ISO yyyy-MM-dd of the last day the sun-shift was applied.
    // Empty string means "never" (legacy curves or freshly-enabled FollowTheSun):
    // on the next catch-up we anchor it to today rather than fabricating a from-date.
    // Stored as string instead of DateTime to keep date-only persistence independent of timezone reinterpretation.
    [XmlAttribute]
    public string LastSunShiftDate
    {
        get => SunAnchorXml.FormatDate(BrightnessAnchor.Date);
        set => BrightnessAnchor = BrightnessAnchor with { Date = SunAnchorXml.ParseDate(value) };
    }

    // Latitude/longitude that were live in app settings the last time this curve was manually edited.
    // The editor compares these to the current settings and shifts the displayed curve
    // from the stored anchor's sun events to today's location's sun events when they differ.
    // 0,0 is the unset sentinel - matches SunShifter's coord-validity bound,
    // so legacy curves (loaded with the default) inherit the live location
    // and produce no location-shift until the first edit stamps real values.
    [XmlAttribute]
    public double LastSunShiftLatitude
    {
        get => BrightnessAnchor.Latitude;
        set => BrightnessAnchor = BrightnessAnchor with { Latitude = value };
    }

    [XmlAttribute]
    public double LastSunShiftLongitude
    {
        get => BrightnessAnchor.Longitude;
        set => BrightnessAnchor = BrightnessAnchor with { Longitude = value };
    }

    // UseDaylightSavings flag in effect when this curve was last manually edited.
    // When the live <see cref="UseDaylightSavings"/> differs the editor shifts the displayed curve
    // between the two timezone-offset interpretations
    // - same mechanism as date and location shifts, just on the DST axis.
    // Defaults to true to match the live default; legacy curves with both equal produce no shift.
    [XmlAttribute]
    public bool LastSunShiftUseDaylightSavings
    {
        get => BrightnessAnchor.UseDaylightSavings;
        set => BrightnessAnchor = BrightnessAnchor with { UseDaylightSavings = value };
    }

    // When true (default), sun-position calculations use the local UTC offset that's currently in effect (DST-aware),
    // so sun events line up with wall-clock time across the year.
    // When false, the timezone's BaseUtcOffset (standard time) is used year-round,
    // so a fixed solar-noon-at-12 curve doesn't jump by an hour twice a year.
    // Per-profile so users can opt out of DST drift without touching the OS timezone.
    [XmlAttribute]
    public bool UseDaylightSavings { get; set; } = true;

    // Disabled period: a window inside the 24h cycle where the curve does not apply.
    // Off by default; the pins (Start / End) live above the graph at 25% / 75%
    // so the first enable lands on a familiar daytime-ish span without an empty config feel.
    // <see cref="DisabledPeriodStart"/> > <see cref="DisabledPeriodEnd"/> is allowed
    // and means the disabled window wraps midnight
    // (e.g. start=0.9, end=0.1 disables the late-night / early-morning slice).
    // Per-profile.
    [XmlAttribute]
    public bool DisabledPeriodEnabled { get; set; } = false;

    [XmlAttribute]
    public double DisabledPeriodStart { get; set; } = 0.25;

    [XmlAttribute]
    public double DisabledPeriodEnd { get; set; } = 0.75;

    // When true, the disabled-period boundaries reanchor to sun events exactly like the curve points do
    // under <see cref="FollowTheSun"/>.
    // The sun calculation already honours <see cref="UseDaylightSavings"/>,
    // so flipping this on does NOT compound with the DST shift
    // - the period stays anchored to "civil dawn", "sunset", etc.
    // Off by default so a fresh disabled period stays in clock-time.
    [XmlAttribute]
    public bool DisabledPeriodFollowTheSun { get; set; } = false;

    // Anchor state for the disabled period under <see cref="DisabledPeriodFollowTheSun"/>.
    // Independent of the curve's <see cref="BrightnessAnchor"/>
    // so the user can freely toggle the period's FTS without disturbing the curve's anchor (and vice versa).
    // Empty-string date / 0,0 coords are the unset sentinels - matches the curve anchor's loading conventions,
    // so a legacy or freshly-enabled period gets stamped to the live state on first observation
    // rather than fabricating a from-state that would trigger a phantom shift.
    // because the four flat LastDisabledPeriodSunShift* properties below preserve the XML shape.
    [XmlIgnore]
    public SunAnchor DisabledPeriodAnchor { get; set; } = new(default, 0.0, 0.0, true);

    [XmlAttribute]
    public string LastDisabledPeriodSunShiftDate
    {
        get => SunAnchorXml.FormatDate(DisabledPeriodAnchor.Date);
        set => DisabledPeriodAnchor = DisabledPeriodAnchor with { Date = SunAnchorXml.ParseDate(value) };
    }

    [XmlAttribute]
    public double LastDisabledPeriodSunShiftLatitude
    {
        get => DisabledPeriodAnchor.Latitude;
        set => DisabledPeriodAnchor = DisabledPeriodAnchor with { Latitude = value };
    }

    [XmlAttribute]
    public double LastDisabledPeriodSunShiftLongitude
    {
        get => DisabledPeriodAnchor.Longitude;
        set => DisabledPeriodAnchor = DisabledPeriodAnchor with { Longitude = value };
    }

    [XmlAttribute]
    public bool LastDisabledPeriodSunShiftUseDaylightSavings
    {
        get => DisabledPeriodAnchor.UseDaylightSavings;
        set => DisabledPeriodAnchor = DisabledPeriodAnchor with { UseDaylightSavings = value };
    }

    // Default flat lines.
    // Brightness sits high (75) so a fresh profile lights the display at a comfortable daytime level;
    // night-light sits low (25) so it nudges warm without slamming amber on day one.
    // Offsets stay at the 50 midline because 50 maps to "+0" in the editor's -100..+100 offset display
    // - any other value would mean a fresh offset curve silently biases brightness/temperature.
    public const double DefaultBrightnessValue = 75.0;
    public const double DefaultNightLightValue = 25.0;
    public const double DefaultOffsetValue = 50.0;

    public static List<EnvironmentalCurvePoint> CreateDefaultBrightness() => CreateFlat(DefaultBrightnessValue);
    public static List<EnvironmentalCurvePoint> CreateDefaultNightLight() => CreateFlat(DefaultNightLightValue);
    public static List<EnvironmentalCurvePoint> CreateDefaultOffset() => CreateFlat(DefaultOffsetValue);

    private static List<EnvironmentalCurvePoint> CreateFlat(double value) =>
    [
        new() { Time = 0.0, Value = value },
        new() { Time = 1.0, Value = value },
    ];

    /// <summary>
    /// Backfills any empty curve list with its series-specific default flat edge pair,
    /// and collapses duplicate edge points (multiple t=0 and/or multiple t=1) down to one each,
    /// keeping the most-recently-written Value.
    /// Idempotent and cheap to call on every load.
    /// Cleans up the historical collection accumulation bug
    /// where property initializers were appended-to rather than replaced on every load.
    /// </summary>
    public void EnsureNormalized()
    {
        EnsureSeries(Brightness, DefaultBrightnessValue);
        EnsureSeries(NightLight, DefaultNightLightValue);
        EnsureSeries(BrightnessOffset, DefaultOffsetValue);
        EnsureSeries(NightLightOffset, DefaultOffsetValue);
    }

    private static void EnsureSeries(List<EnvironmentalCurvePoint> series, double defaultValue)
    {
        if (series.Count == 0)
        {
            series.Add(new EnvironmentalCurvePoint { Time = 0.0, Value = defaultValue });
            series.Add(new EnvironmentalCurvePoint { Time = 1.0, Value = defaultValue });
            return;
        }

        CollapseEdge(series, 0.0);
        CollapseEdge(series, 1.0);
    }

    private static void CollapseEdge(List<EnvironmentalCurvePoint> series, double edgeTime)
    {
        // Walk forward to find the LAST occurrence;
        // that's the value we keep, matching the editor's "last write wins" SyncEdgeYIfEdge invariant.
        double? keepValue = null;
        foreach (EnvironmentalCurvePoint p in series)
            if (p.Time == edgeTime)
                keepValue = p.Value;

        if (keepValue is null) return;

        series.RemoveAll(p => p.Time == edgeTime);
        series.Add(new EnvironmentalCurvePoint { Time = edgeTime, Value = keepValue.Value });
    }
}

/// <summary>
/// Translates between the on-disk string-formatted date (yyyy-MM-dd or "" sentinel)
/// and the in-memory <see cref="SunAnchor.Date"/> <see cref="DateTime"/>.
/// Empty / unparseable strings round-trip through <c>default(DateTime)</c>,
/// which the consumers already treat as the "never anchored" case via the same string-emptiness check
/// they used before the struct collapse.
/// </summary>
internal static class SunAnchorXml
{
    public static string FormatDate(DateTime date) =>
        date == default ? "" : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateTime ParseDate(string? value) =>
        !string.IsNullOrEmpty(value)
        && DateTime.TryParseExact(
            value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            ? parsed
            : default;
}
