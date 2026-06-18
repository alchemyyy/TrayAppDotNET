namespace BrightnessTrayAppDotNET.Models;

/// <summary>
/// Stores the state of a single monitor within a profile.
/// </summary>
public class MonitorState
{
    /// <summary>
    /// Identity-strategy-keyed identifier (e.g. <c>num:1</c>, <c>edid:LG12345</c>, <c>port:MONITOR\...</c>).
    /// Captured at save-time from the live <see cref="MonitorInfo.ID"/>.
    /// Kept on the wire for backward compatibility with profiles.xml files written before <see cref="EDIDKey"/>
    /// existed; <see cref="Services.ProfileManager"/> migrates legacy entries to EDIDKey on load
    /// (eagerly for <c>edid:</c>-prefix IDs, lazily on the first live-monitor match for the rest).
    /// New code must not read or write this property
    /// - the <see cref="ObsoleteAttribute"/> surfaces unintended writes at compile time.
    /// </summary>
    [Obsolete("legacy - migrated to EDIDKey on load")]
    public string ID { get; set; } = string.Empty;

    /// <summary>
    /// EDID-first stable identifier
    /// (the same one used by <c>KnownDisplays</c>, <c>MonitorOrder</c>, and <c>MonitorOverrides</c>).
    /// Empty on legacy profile entries that were written before this field existed;
    /// lookups treat empty as "no EDID claim" and fall back to <see cref="ID"/>.
    /// </summary>
    public string EDIDKey { get; set; } = string.Empty;

    /// <summary>Brightness level (0-100).</summary>
    public int Brightness { get; set; }

    /// <summary>Whether the monitor is powered on.</summary>
    public bool IsPoweredOn { get; set; } = true;

    /// <summary>Whether the slider is enabled.</summary>
    public bool IsSliderEnabled { get; set; } = true;
}

/// <summary>
/// A brightness profile storing the state of all monitors.
/// </summary>
public class BrightnessProfile
{
    /// <summary>Profile index (0-based).</summary>
    public int Index { get; set; }

    /// <summary>
    /// Optional user-supplied name for the profile, edited from the "Rearrange profile data" list.
    /// Null/empty means use the default "Profile" label.
    /// Travels with profile data during <see cref="Services.ProfileManager.SwapProfileData"/> reorders.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Optional custom glyph for the profile button.</summary>
    public string? CustomGlyph { get; set; }

    /// <summary>
    /// The master slider tracking mode captured with this profile.
    /// Applied to <see cref="AppSettings.MasterSliderMode"/> when the profile is selected
    /// so the master's derived value reflects how the user last computed it for this preset.
    /// </summary>
    public MasterSliderMode MasterSliderMode { get; set; } = MasterSliderMode.Average;

    /// <summary>
    /// Night-light strength captured with this profile, normalized 0-100
    /// (0 = no warmth / 6500K, 100 = max warmth / 1200K).
    /// Applied to the night-light registry when the profile is selected with brightness,
    /// alongside per-monitor brightness.
    /// </summary>
    public int NightLight { get; set; }

    /// <summary>States of individual monitors.</summary>
    public List<MonitorState> MonitorStates { get; set; } = [];

    /// <summary>
    /// Per-profile environmental automation curves (brightness + night-light over a 24h cycle).
    /// Geo-coordinates that drive the time-of-day mapping live globally on <see cref="AppSettings"/>;
    /// the curves themselves are profile-scoped so different presets can keep different daily shapes.
    /// </summary>
    public EnvironmentalCurve EnvironmentalCurve { get; set; } = new();
}

/// <summary>
/// Collection of all profiles, persisted as XML.
/// </summary>
public class ProfileCollection
{
    /// <summary>Index of the last selected profile.</summary>
    public int LastSelectedIndex { get; set; }

    /// <summary>All saved profiles.</summary>
    public List<BrightnessProfile> Profiles { get; set; } = [];
}
