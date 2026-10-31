using System.Xml.Serialization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Models;

namespace BrightnessTrayAppDotNET.Models;

public enum MasterSliderMode
{
    Lowest,
    Average,
    Highest
}

public enum DisplaySortMode
{
    Arrangement,
    DisplayNumber
}

public enum DisplaySortDirection
{
    Standard,
    Reversed
}

public enum TrayClickAction
{
    Nothing,
    TurnOffAllDisplays,
    TurnOnAllDisplays,
    FullBright,
    FullDim
}

public enum TrayWheelTarget
{
    Nothing,
    Brightness,
    NightLight
}

public enum PowerOffMode
{
    Sleep,
    Soft,
    Hard
}

/// <summary>
/// How the app drives Night Light.
/// <c>SettingsHandler</c> is the default
/// - drives the Settings UI's own setting handler in SettingsHandlers_Display.dll,
/// the most reliable path since it triggers the same SaveSettingsAsync chain the Settings slider does
/// and refreshes both the live filter and the Settings UI cache.
/// It is an explicit choice: when the handler can't be used on this OS build, Night Light reports unsupported
/// instead of silently switching to Registry.
/// <c>Registry</c> forces the CloudStore <c>BlueLightReduction</c> path regardless of availability
/// - useful for debugging or when SettingsHandler is misbehaving on a particular machine.
/// <c>GammaRamp</c> bypasses Windows Night Light: the app writes a warm GDI gamma ramp to every active SDR display
/// and owns the on/off state and strength itself (<see cref="AppSettings.NightLightGammaRampEnabled"/>,
/// <see cref="AppSettings.NightLightGammaRampStrength"/>). Windows Night Light is kept off while the tint is on.
/// <c>Auto</c> is a legacy value retained for XML compatibility with settings written before SettingsHandler existed;
/// it currently behaves the same as Registry.
/// </summary>
public enum NightLightFallbackMode
{
    Auto,
    Registry,
    GammaRamp,
    SettingsHandler
}

/// <summary>
/// Determines which attribute of a physical monitor is used as its <c>Id</c> throughout the app
/// - i.e. how profiles, name overrides, and manual order entries are keyed.
/// Trading off stability against human-friendliness:
///
///   * <c>DisplayNumber</c>: the OS-assigned badge number (1, 2, 3...).
///     Resets on reboot or topology change, but is the "obvious" thing users see in Windows Settings &gt; Display.
///   * <c>HardwarePort</c>: the device instance path (hardware ID + port).
///     Stable across reboots on the same port; changes when a monitor is moved to a different cable/output.
///   * <c>EDIDSerial</c>: the EDID serial number.
///     Stable per physical panel regardless of port - but missing on monitors that don't populate EDID,
///     in which case we fall back to the hardware port.
/// </summary>
public enum MonitorIdentityStrategy
{
    DisplayNumber,
    HardwarePort,
    EDIDSerial
}

public class MonitorOverrideEntry
{
    // Keyed by MonitorInfo.EDIDKey (always EDID-first with port fallback) so this section's
    // per-monitor data survives identity-strategy changes.
    [XmlAttribute]
    public string ID { get; set; } = string.Empty;

    // Empty = no override; the monitor uses its EDID-reported friendly name.
    [XmlAttribute]
    public string Name { get; set; } = string.Empty;

    // -1 = inherit global. Otherwise 0..10000 ms.
    [XmlAttribute]
    public int ValidationDwellMs { get; set; } = -1;

    [XmlAttribute]
    public int BrightnessDwellMs { get; set; } = -1;

    // 0 = no per-monitor floor (the natural slider minimum). 1..100 = active floor.
    [XmlAttribute]
    public int MinBrightness { get; set; }

    // 100 = no per-monitor ceiling (the natural slider maximum). 0..99 = active ceiling.
    [XmlAttribute]
    public int MaxBrightness { get; set; } = 100;

    // Raw VCP command override for the power-off action.
    // Empty = use the resolved profile command. Otherwise either:
    //   "0xD6 0x05" - byte + value pair sent verbatim, or
    //   "0xD6"      - byte only; the value falls back to the profile-default mapping.
    [XmlAttribute]
    public string PowerOffVcpOverride { get; set; } = string.Empty;

    // Raw VCP command override sent for brightness adjustments, same format as PowerOffVcpOverride.
    [XmlAttribute]
    public string BrightnessVcpOverride { get; set; } = string.Empty;

    // Per-monitor brightness norm curve. Empty = no curve (inherit / passthrough);
    // otherwise the editor's control points keyed by X (0..100) and Y (signed offset).
    [XmlArrayItem("P")]
    public List<NormCurvePoint> NormCurvePoints { get; set; } = [];
}

public class CurveStopwatchEntry
{
    [XmlAttribute]
    public string SliderKey { get; set; } = string.Empty;

    [XmlAttribute]
    public int Minutes { get; set; } = 60;

    [XmlAttribute]
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Whether the slider is currently owned by a manual absolute-curve override.
    /// This is independent of <see cref="IsEnabled"/> because the stopwatch is optional.
    /// </summary>
    [XmlAttribute]
    public bool IsCurveReleased { get; set; }

    [XmlAttribute]
    public DateTime EngagedAtUtc { get; set; }

    [XmlAttribute]
    public DateTime ReenableAtUtc { get; set; }
}

/// <summary>
/// Persistent record of every unique display the app has ever enumerated,
/// keyed by the same EDID-first identifier used by the "Display order &amp; overrides" section.
/// Populated by <see cref="Services.MonitorService"/> on each refresh; never trimmed automatically
/// - disconnected monitors stay in the list (rendered dimmed at the bottom of the settings list)
/// so their per-monitor overrides remain visible and editable while they're unplugged.
/// </summary>
public class KnownDisplayEntry
{
    [XmlAttribute]
    public string EDIDKey { get; set; } = string.Empty;

    [XmlAttribute]
    public string OriginalName { get; set; } = string.Empty;

    [XmlAttribute]
    public string EDIDSerial { get; set; } = string.Empty;

    /// <summary>
    /// Records whether this monitor has *ever* answered a DDC/CI brightness query successfully.
    /// Set the first time the read succeeds and never cleared.
    /// When true, <see cref="Services.DDCRecoveryService"/> starts the event-triggered DDC fallback worker
    /// whenever its current <see cref="MonitorInfo.IsHardwareFunctional"/> goes false or read-degraded
    /// - recovers monitors that get stuck "unavailable" after hot-plug, dock undock, KVM rerouting,
    /// or wake-from-sleep races without requiring an app restart.
    /// Distinguishes "DDC chip is alive but having a bad moment" from "no DDC at all"
    /// (laptop internal panels, USB displays) so the fallback worker only hammers hardware that we know can respond.
    /// </summary>
    [XmlAttribute]
    public bool WasEverDDCCapable { get; set; }

    /// <summary>
    /// Last value acknowledged by a read-back from this display's DDC brightness VCP (0-100, null = never verified).
    /// Stamped from <c>MonitorService</c> only after the current target reads back within tolerance,
    /// so it captures confirmed hardware state regardless of who drove it
    /// (slider drag, master propagation, profile load, environmental curve, etc.).
    /// Kept for diagnostics/history. Acquisition no longer hydrates <see cref="MonitorInfo.Brightness"/>
    /// from this value; startup, topology, and fallback recovery read current DDC first, then UI/profile
    /// state decides whether any manual value should be written.
    /// Note: this is the *bus* value, NOT <see cref="MonitorInfo.LastUserBrightness"/>. Under curve mode the
    /// two diverge - the curve writes through <c>EnqueueDirectBrightness</c> which bypasses the Brightness
    /// setter, so LastUserBrightness can stay locked at a stale prior manual drag while the bus moves with
    /// the curve. Persisting the bus value is what gives the user "monitor comes back where I left it visually".
    /// JSON-only; the legacy XML migration path doesn't populate this.
    /// </summary>
    [XmlAttribute]
    public int? LastBusBrightness { get; set; }
}

/// <summary>
/// Root application settings class.
/// </summary>
[XmlRoot("AppSettings")]
public class AppSettings : AppSettingsCommon<HotkeyBinding, BrightnessHotkeyAction>, IFlyoutDockSettings
{
    public const int FlyoutScrollWheelStepMin = 1;
    public const int FlyoutScrollWheelStepMax = 50;
    public const int PrecisionTouchpadUnitsPerScrollStepDefault = 90;
    public const int PrecisionTouchpadUnitsPerScrollStepMin = 1;
    public const int PrecisionTouchpadUnitsPerScrollStepMax = 1000;

    // General
    public bool ApplyBrightnessOnStartup { get; set; } = true;

    /// <summary>
    /// The shared implicit-save switch (see <see cref="AppSettingsCommon.Autosave"/>). Stored as
    /// <c>AutosaveSettings</c> because Brightness's older files use <c>Autosave</c> for <see cref="AutosaveProfiles"/>.
    /// </summary>
    [XmlElement("AutosaveSettings")]
    public override bool Autosave
    {
        get => base.Autosave;
        set => base.Autosave = value;
    }

    /// <summary>
    /// Writes slider changes into the active brightness profile automatically. Stored as <c>Autosave</c> for
    /// compatibility with older files.
    /// </summary>
    [XmlElement("Autosave")]
    public bool AutosaveProfiles { get; set; } = true;

    public bool TrayScrollEnabled { get; set; } = true;
    public bool PrecisionTouchpadScrollEnabled { get; set; } = true;

    public int PrecisionTouchpadUnitsPerScrollStep
    {
        get;
        set => field = Math.Clamp(
            value,
            PrecisionTouchpadUnitsPerScrollStepMin,
            PrecisionTouchpadUnitsPerScrollStepMax);
    } = PrecisionTouchpadUnitsPerScrollStepDefault;

    public TrayWheelTarget TrayWheelAction { get; set; } = TrayWheelTarget.Brightness;
    public TrayWheelTarget TrayCtrlWheelAction { get; set; } = TrayWheelTarget.NightLight;
    public TrayWheelTarget TrayAltWheelAction { get; set; } = TrayWheelTarget.Nothing;
    public bool FlyoutNumberKeysSwitchProfile { get; set; } = true;
    public bool PreserveMasterSliderOffsets { get; set; }
    public TrayClickAction TrayDoubleClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;

    // Context Menu
    public bool ShowProfileSelectorsInMenu { get; set; } = true;
    public bool ShowMonitorPowerButtons { get; set; }
    public bool ShowAllDisplaysPowerButton { get; set; } = true;
    public PowerOffMode PowerOffMode { get; set; } = PowerOffMode.Sleep;

    // Monitor Options
    public int BrightnessUpdateRateMs { get; set; } = TimeConstants.BrightnessUpdateRateDefaultMs;

    /// <summary>
    /// Verification delay (shown under that name): how long a panel gets to apply a brightness or power change
    /// before it is read back, including each time a value that read back wrong is sent again.
    /// Also the wait before the final retry of a refused brightness write.
    /// Per-monitor overrides replace it for one display.
    /// </summary>
    public int ValidationDwellMs { get; set; } = TimeConstants.ValidationDwellDefaultMs;

    /// <summary>
    /// Verification attempts (shown under that name): writes per brightness change,
    /// reads per brightness or power read-back, and reads per acquisition or recovery probe.
    /// A brightness change still refused or unconfirmed after this many marks the display unavailable.
    /// Unreadable replies and acquisition reads retry on the fixed backoff sequence in <c>TimeConstants</c>;
    /// the final acquisition attempt also refreshes HMONITOR.
    /// Higher = more tolerant of transient I2C noise / DPMS-wake races;
    /// lower = faster failure for genuinely stuck monitors.
    /// </summary>
    public int ValidationAttempts { get; set; } = 4;

    /// <summary>
    /// Maximum wall-clock time any single dxva2-backed call (capability fetch, VCP read, VCP write) is allowed to
    /// block end to end, including helper startup and pipe wait, before the per-monitor helper process is killed.
    /// Killing the helper releases its physical monitor handles without abandoning a blocked native thread in the
    /// tray process. Other monitors use independent helpers and remain responsive.
    /// Production clamps this to a positive safety floor. Unbounded inline calls are reserved for the killable
    /// helper process itself; allowing them in the tray process would make a native hang unrecoverable.
    /// </summary>
    public int DDCOperationTimeoutMs { get; set; } = TimeConstants.DDCOperationTimeoutDefaultMs;

    /// <summary>
    /// Allows best-effort DDC SET requests after a monitor stops returning readable replies. This also permits a
    /// current-target write probe to establish the read-degraded state when no read-back-confirmed value is available.
    /// Accepted values remain unverified and are never persisted as confirmed hardware state.
    /// </summary>
    public bool AllowBlindDDCWritesDuringDegradedState { get; set; } = true;

    public MasterSliderMode MasterSliderMode { get; set; } = MasterSliderMode.Average;
    public bool ShowFlyoutMonitorPowerButtons { get; set; }
    public bool ShowFlyoutMonitorNumberBadge { get; set; }
    public bool ShowFlyoutDisplaySettingsButton { get; set; } = true;
    public bool ShowFlyoutFooterPowerButton { get; set; }
    public bool FooterPowerButtonOnlyEnabledMonitors { get; set; }
    public bool ShowMasterSlider { get; set; } = true;
    public bool ShowIndividualSliders { get; set; } = true;
    public bool ShowNightLightSlider { get; set; } = true;

    public int FlyoutScrollWheelStep
    {
        get;
        set => field = Math.Clamp(value, FlyoutScrollWheelStepMin, FlyoutScrollWheelStepMax);
    } = 2;

    // Master switch for the undock feature.
    // When false, the undock button is hidden, and any persisted undocked state is force-redocked
    // the next time the flyout opens - disabling the feature should never leave a free-floating window stranded.
    public bool AllowFlyoutUndock { get; set; } = true;

    // Keep restored floating coordinates inside the monitor that contains them.
    public bool ClampUndockedFlyoutToScreen { get; set; } = true;

    // When true, the flyout reopens in the previous session's docked/undocked state at startup.
    // When false, the flyout always opens docked at launch regardless of FlyoutUndocked;
    // runtime undock/redock still persist normally so flipping this back on resumes restoration.
    public bool RestoreFlyoutUndockedOnStartup { get; set; } = true;

    // Flyout dock state: runtime state, persisted without raising Changed.
    // When FlyoutUndocked is true and FlyoutHasSavedPosition is set, the flyout opens at FlyoutLeft/FlyoutTop
    // and behaves like a free-floating window (always-on-top, doesn't auto-hide on focus loss).
    // Tray-icon click and the redock button both flip this back to docked.
    // The position is only written to disk on drag-release, not while dragging.
    public bool FlyoutUndocked
    {
        get;
        set => SetState(ref field, value);
    }

    /// <summary>
    /// Sticky one-shot acknowledgement for the degraded-display hard power-off click.
    /// False until the user confirms the destructive-action overlay the first time;
    /// after that, subsequent warning-glyph clicks fire the 0x05 power-off without prompting.
    /// </summary>
    public bool HasAcknowledgedHardPowerOffWarning { get; set; }

    public bool FlyoutHasSavedPosition
    {
        get;
        set => SetState(ref field, value);
    }

    public double FlyoutLeft
    {
        get;
        set => SetState(ref field, value);
    }

    public double FlyoutTop
    {
        get;
        set => SetState(ref field, value);
    }

    public bool ShowEnvironmentalCurvesButton { get; set; } = true;
    public bool ShowNightLightKelvinLabel { get; set; }
    public bool InvertNightLightSlider { get; set; }

    /// <summary>Backend selection for night light. See <see cref="NightLightFallbackMode"/>.</summary>
    public NightLightFallbackMode NightLightFallbackMode { get; set; } = NightLightFallbackMode.SettingsHandler;

    /// <summary>
    /// Last non-zero strength (0-100) the user committed to night light.
    /// Restored when the user toggles night light back on while the live strength is 0,
    /// so a "toggle on" never produces an invisible "no-op".
    /// Updated whenever any non-zero strength is written through <see cref="Services.NightLightProvider"/>.
    /// 50 is a sensible first-launch default - mid-warmth that's clearly visible without being too aggressive.
    /// </summary>
    public int NightLightLastNonZeroStrength { get; set; } = 50;

    // NightLightPulseOnStrengthChange was removed (audit_10 F-02): production code uses
    // EnqueueSetStrengthSpaced, which has no pulse parameter, so toggling the UI did nothing.

    /// <summary>
    /// Last computed master-row brightness from the previous session, used as the seed for the
    /// MasterMonitor row at flyout construction time. The flyout's MasterMonitor is constructed
    /// before MonitorService.Refresh has populated the Monitors collection (Phase B is deferred
    /// 1.5s), so until AttachMonitor first runs, the master slider visibly shows whatever literal
    /// we seed here. Without a memo the slider sat at 50, then snapped to the computed value -
    /// persisting and restoring keeps the slider on a sensible value across the settle window.
    /// 100 is the cold-first-launch default: most users keep monitors at full brightness.
    /// </summary>
    public int LastMasterBrightness { get; set; } = 100;

    /// <summary>
    /// When true, dragging the night-light strength all the way to 0 also disables night light
    /// (i.e. flips the on/off state, not just the warmth) instead of leaving an invisible-but-on state behind.
    /// The next toggle-on restores from <see cref="NightLightLastNonZeroStrength"/> via the existing
    /// zero-strength trap so the user gets back the warmth they last used.
    /// Off by default - historical behaviour was to leave the toggle on at zero strength.
    /// </summary>
    public bool TurnOffNightLightAtZeroStrength { get; set; }

    /// <summary>
    /// Gamma-ramp backend on/off state. Windows knows nothing about this tint, so the app persists it and reapplies
    /// it at startup while <see cref="NightLightFallbackMode"/> is <c>GammaRamp</c>.
    /// Switching to that backend copies Windows Night Light's on/off state and strength into these two values;
    /// switching away clears this flag after restoring the identity ramp.
    /// </summary>
    public bool NightLightGammaRampEnabled { get; set; }

    /// <summary>Gamma-ramp backend strength (0-100), same kelvin mapping as Windows Night Light.</summary>
    public int NightLightGammaRampStrength
    {
        get;
        set => field = Math.Clamp(value, min: 0, max: 100);
    } = 50;

    /// <summary>
    /// HTTP timeout (seconds) used by <see cref="BrightnessTrayAppDotNET.Interop.NightLight.PDBSymbolResolver"/>
    /// when fetching SettingsHandlers_Display.dll's PDB from the Microsoft public symbol server.
    /// The resolver only runs after a Windows update introduces an unknown DLL build,
    /// so this fires at most once per build-version transition;
    /// the default 60s is enough for a typical home connection but slow or metered links can raise it
    /// to avoid the Settings handler backend reporting unsupported.
    /// The download runs in the Night Light helper process, which receives this value on its command line and is
    /// given this long plus <see cref="TimeConstants.NightLightHelperStartGraceMs"/> to report ready.
    /// Clamped to <see cref="TimeConstants.PDBSymbolResolverDownloadTimeoutMinSeconds"/> -
    /// <see cref="TimeConstants.PDBSymbolResolverDownloadTimeoutMaxSeconds"/>, the same range the settings UI accepts.
    /// </summary>
    public int NightLightPDBDownloadTimeoutSeconds
    {
        get;
        set => field = Math.Clamp(
            value,
            TimeConstants.PDBSymbolResolverDownloadTimeoutMinSeconds,
            TimeConstants.PDBSymbolResolverDownloadTimeoutMaxSeconds);
    } = TimeConstants.PDBSymbolResolverDownloadTimeoutDefaultSeconds;

    public DisplaySortMode DefaultDisplaySortMode { get; set; } = DisplaySortMode.Arrangement;
    public DisplaySortDirection DefaultDisplaySortDirection { get; set; } = DisplaySortDirection.Standard;
    public MonitorIdentityStrategy MonitorIdentityStrategy { get; set; } = MonitorIdentityStrategy.DisplayNumber;
    public List<string> MonitorOrder { get; set; } = [];

    [XmlArrayItem("Monitor")]
    public List<MonitorOverrideEntry> MonitorOverrides { get; set; } = [];

    /// <summary>
    /// Pre-<c>displays.json</c> list, read only to seed <see cref="Utils.KnownDisplaysStore"/> once; the monitor
    /// service empties it after migrating, so it is no longer persisted.
    /// </summary>
    [XmlArrayItem("Display")]
    public List<KnownDisplayEntry> KnownDisplays { get; set; } = [];

    // Theme
    public TrayAppDotNETTrayIconStyle TrayIconStyle { get; set; } = TrayAppDotNETTrayIconStyle.Dynamic;
    public MasterSliderMode DynamicIconBrightnessTracking { get; set; } = MasterSliderMode.Average;
    public bool DynamicIconTrackEnabledOnly { get; set; }
    public NullableThemeColor TrayIconBrightColor { get; set; } = new();
    public NullableThemeColor TrayIconDimColor { get; set; } = new();
    public NullableThemeColor FooterBackgroundColor { get; set; } = new();

    // Environmental curve colors: curve strokes, current-time marker, twilight / night backdrop bands, grid line color.
    // Backdrops carry a separate Alpha because the system color picker is RGB-only.
    public NullableThemeColor EnvironmentalBrightnessCurveColor { get; set; } = new();
    public NullableThemeColor EnvironmentalNightLightCurveColor { get; set; } = new();
    public NullableThemeColor EnvironmentalCurrentTimeColor { get; set; } = new();
    public NullableThemeColor EnvironmentalTwilightBackdropColor { get; set; } = new();
    public NullableThemeColor EnvironmentalNightBackdropColor { get; set; } = new();
    public NullableThemeColor EnvironmentalGridLineColor { get; set; } = new();

    // Environmental automation - global geo location (curves are per-profile, on BrightnessProfile).
    // Default seed is a representative Pacific-Northwest pin; users override via the map picker
    // or the "Approximate from IP" button. Stored in decimal degrees, +N/+E.
    public double EnvironmentalLatitude { get; set; } = 47.7542814;
    public double EnvironmentalLongitude { get; set; } = -122.2795275;

    // Curve-editor visibility toggles.
    // At least one must remain checked at all times - enforced by the settings UI, not by persistence.
    public bool EnvironmentalShowBrightnessCurve { get; set; } = true;
    public bool EnvironmentalShowNightLightCurve { get; set; } = true;

    // Runtime curve engagement flags
    // - mirror the flyout's per-row curve-toggle buttons so an active curve survives an app restart
    // instead of resetting each session.
    public bool EnvironmentalBrightnessCurveEnabled { get; set; }
    public bool EnvironmentalNightLightCurveEnabled { get; set; }

    // Re-engages brightness curve control after the curve crosses the released master slider value.
    // The delay is a dead window after each manual brightness write, so an immediate click across
    // the current curve target cannot snap the user straight back into curve control.
    public bool AutoEngageEnvironmentalCurveEnabled { get; set; }

    public int AutoEngageEnvironmentalCurveDelaySeconds { get; set; } =
        TimeConstants.AutoEngageEnvironmentalCurveDelayDefaultSeconds;

    [XmlArrayItem("Stopwatch")]
    public List<CurveStopwatchEntry> CurveStopwatches { get; set; } = [];

    // Offset mode: when on, the editor exposes the per-profile *Offset curves
    // (additive/subtractive deltas, -100..+100 Y axis) plus draggable min/max clamp lines.
    // When off, the editor exposes the absolute Brightness/NightLight curves (0..100 Y axis).
    // Both sets are stored independently on each profile so toggling is non-destructive.
    public bool EnvironmentalOffsetMode { get; set; }

    // Cursor readout: when on, the curve editor draws a vertical scrubber at the cursor's X
    // and a small marker on each visible curve labelled with its value at that X.
    // The top-right "time / value" readout is always visible while the cursor is inside the editor
    // regardless of this setting; the toggle controls only the per-curve readouts.
    public bool EnvironmentalShowCursorReadout { get; set; }

    // Sun overlay: when on, the curve editor shades twilight bands (orange) and night bands (greyish blue)
    // behind the curves so the user can see at a glance where each part of the day's brightness curve
    // sits relative to the sun.
    // Daytime is left clear.
    // Suppressed automatically when the geo coordinates are unset (lat == 0 AND lon == 0)
    // or when the SPA calculator can't produce valid times for the location/date (polar extremes).
    public bool EnvironmentalShowSunOverlay { get; set; } = true;

    // Global blend (0-100) between linear interpolation (0) and full monotonic cubic Hermite (100)
    // for the environmental curves.
    // Drives both the editor preview and any downstream sampling so the on-screen shape and the applied values
    // stay in sync.
    public int EnvironmentalCurveSmoothness { get; set; } = 100;

    // How often the runtime curve evaluator re-samples and applies the active curves, in milliseconds.
    // 5s is the default - low enough to feel responsive at twilight transitions
    // (where the curve climbs ~1% per minute even on a steep slope),
    // high enough that the tick is essentially free since unchanged integer values are filtered out
    // before any DDC write fires. Clamped to the range the settings card and the curve service use;
    // 0 is never valid - a zero-interval DispatcherTimer would busy-loop.
    public int EnvironmentalCurveTickIntervalMs
    {
        get;
        set => field = Math.Clamp(
            value,
            TimeConstants.EnvironmentalCurveTickIntervalMinMs,
            TimeConstants.EnvironmentalCurveTickIntervalMaxMs);
    } = TimeConstants.EnvironmentalCurveTickIntervalDefaultMs;

    // The built-in catalog (from SliderThumbGlyphOption.CreateDefaults) is hardcoded and rebuilt from scratch
    // on every load, so the list itself is never persisted.
    // Only the user's current selection is persisted, via the SliderThumb element below
    // - and when that selection names a built-in, the built-in wins;
    // otherwise the loaded option is appended to the catalog so it stays in the dropdown.
    [XmlIgnore]
    public string SliderThumbGlyph { get; set; } = "Capsule";

    [XmlIgnore]
    public List<SliderThumbGlyphOption> SliderThumbOptions { get; set; } = [];

    [XmlElement("SliderThumb")]
    public SliderThumbGlyphOption? SerializedSliderThumb
    {
        get => SliderThumbOptions.FirstOrDefault(o => o.Name == SliderThumbGlyph);
        set => _loadedSliderThumb = value;
    }

    private SliderThumbGlyphOption? _loadedSliderThumb;

    public override void OnTrayXmlDeserialized()
    {
        InitializeSliderThumbCatalog();
        base.OnTrayXmlDeserialized();
    }

    protected override IEnumerable<NullableThemeColor> EnumerateColorOverrides()
    {
        foreach (NullableThemeColor color in base.EnumerateColorOverrides()) yield return color;
        yield return TrayIconBrightColor;
        yield return TrayIconDimColor;
        yield return FooterBackgroundColor;
        yield return EnvironmentalBrightnessCurveColor;
        yield return EnvironmentalNightLightCurveColor;
        yield return EnvironmentalCurrentTimeColor;
        yield return EnvironmentalTwilightBackdropColor;
        yield return EnvironmentalNightBackdropColor;
        yield return EnvironmentalGridLineColor;
    }

    private const string SettingsFileName = "settings.xml";

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, SettingsFileName);
    }

    /// <summary>
    /// The folder that holds settings.xml and other per-app data.
    /// Used by the uninstaller's "delete settings" branch.
    /// </summary>
    public static string GetDefaultDirectory() =>
        Program.AppLocalAppDataDirectory;

    /// <summary>
    /// True when the saved settings say the gamma-ramp Night Light tint is on, so a killed instance may have left
    /// it on screen. Read-only: unlike <see cref="LoadOrDefault()"/>, a missing or unreadable file is never created,
    /// backed up or replaced, which matters on the uninstall path.
    /// </summary>
    public static bool IsPersistedGammaRampTintOn()
    {
        AppSettings settings = ReadOrDefault<AppSettings>(Path.Combine(GetDefaultDirectory(), SettingsFileName));
        return BrightnessTrayAppDotNET.Interop.NightLight.GammaRampNightLight.ShouldRestoreAfterForcedStop(
            settings.NightLightFallbackMode,
            settings.NightLightGammaRampEnabled);
    }

    public static AppSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static AppSettings LoadOrDefault(string path) => LoadOrCreate<AppSettings>(path);

    protected override void InitializeNewSettings() => InitializeSliderThumbCatalog();

    /// <summary>
    /// The set of built-in hotkey bindings seeded for fresh installs and topped up on every launch.
    /// Identity is (Action, Parameter, BindingID): defaults always live on BindingID 0 (the primary
    /// row), so a user-added secondary binding (BindingID >= 1) for the same action does not block
    /// re-seeding the primary row.
    /// </summary>
    protected override IReadOnlyList<HotkeyBinding> CreateDefaultHotkeys() =>
    [
        new()
        {
            Action = BrightnessHotkeyAction.FullBright,
            Parameter = string.Empty,
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Win | HotkeyModifiers.Alt,
            VirtualKey = 0x46, // VK_F
            Enabled = true,
            BindingID = 0
        }
    ];

    /// <summary>
    /// Seeds <see cref="SliderThumbOptions"/> from the built-in catalog, and, if a user-selected option was
    /// loaded from XML, either points <see cref="SliderThumbGlyph"/> at the matching built-in (by Name)
    /// or appends the loaded option to the catalog so it remains visible in the dropdown.
    /// </summary>
    private void InitializeSliderThumbCatalog()
    {
        List<SliderThumbGlyphOption> catalog = SliderThumbGlyphOption.CreateDefaults();

        if (_loadedSliderThumb is { } saved && !string.IsNullOrEmpty(saved.Name))
        {
            if (catalog.All(o => o.Name != saved.Name)) catalog.Add(saved);

            SliderThumbGlyph = saved.Name;
        }

        SliderThumbOptions = catalog;
    }
}
