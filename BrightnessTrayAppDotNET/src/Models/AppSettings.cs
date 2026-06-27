using System.Xml.Serialization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Serialization;
using TrayAppDotNETCommon.UI.Models;

namespace BrightnessTrayAppDotNET.Models;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public enum TrayIconStyle
{
    Dynamic,
    Static,
}

public enum MasterSliderMode
{
    Lowest,
    Average,
    Highest,
}

public enum DisplaySortMode
{
    Arrangement,
    DisplayNumber,
}

public enum DisplaySortDirection
{
    Standard,
    Reversed,
}

public enum TrayClickAction
{
    Nothing,
    TurnOffAllDisplays,
    TurnOnAllDisplays,
    FullBright,
    FullDim,
}

public enum TrayWheelTarget
{
    Nothing,
    Brightness,
    NightLight,
}

public enum PowerOffMode
{
    Sleep,
    Soft,
    Hard,
}

/// <summary>
/// Where the tray right-click menu appears.
/// <c>Classic</c> opens at the cursor position (the OS default for tray menus).
/// <c>Modern</c> docks the menu in the bottom-right corner of the primary work area with an 8px inset,
/// matching the Windows 11 system-flyout pattern used by the brightness flyout itself.
/// </summary>
public enum ContextMenuPosition
{
    Classic,
    Modern,
}

/// <summary>
/// How the app drives Windows Night Light.
/// <c>SettingsHandler</c> is the default
/// - drives the Settings UI's own setting handler in SettingsHandlers_Display.dll,
/// the most reliable path since it triggers the same SaveSettingsAsync chain the Settings slider does
/// and refreshes both the live filter and the Settings UI cache.
/// The resolver falls back to Registry automatically if SettingsHandler isn't available on the OS.
/// <c>Registry</c> forces the CloudStore <c>BlueLightReduction</c> path regardless of availability
/// - useful for debugging or when SettingsHandler is misbehaving on a particular machine.
/// <c>GammaRamp</c> is reserved for a hidden UI toggle and currently has no backing implementation;
/// the resolver treats it as Registry.
/// <c>Auto</c> is a legacy value retained for XML compatibility with settings written before SettingsHandler existed;
/// it currently behaves the same as Registry.
/// </summary>
public enum NightLightFallbackMode
{
    Auto,
    Registry,
    GammaRamp,
    SettingsHandler,
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
    EDIDSerial,
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
    public int MinBrightness { get; set; } = 0;

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
    public bool WasEverDDCCapable { get; set; } = false;

    /// <summary>
    /// Last value successfully written to this display's DDC brightness VCP (0-100, null = never written).
    /// Stamped from <see cref="Services.MonitorService.DoBrightnessWriteAsync"/> after a successful bus write,
    /// so it captures whatever the user actually sees on screen regardless of who drove it
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
public class AppSettings : ITrayAppDotNETUpdateSettings, ITrayAppDotNETKeepWarmSettings,
    ITrayXmlSerializationCallbacks
{
    // General
    public bool RunOnStartup { get; set; } = true;
    public bool ApplyBrightnessOnStartup { get; set; } = true;
    public bool Autosave { get; set; } = true;
    public bool TrayScrollEnabled { get; set; } = true;
    public TrayWheelTarget TrayWheelAction { get; set; } = TrayWheelTarget.Brightness;
    public TrayWheelTarget TrayCtrlWheelAction { get; set; } = TrayWheelTarget.NightLight;
    public TrayWheelTarget TrayAltWheelAction { get; set; } = TrayWheelTarget.Nothing;
    public bool FlyoutNumberKeysSwitchProfile { get; set; } = true;
    public bool PreserveMasterSliderOffsets { get; set; } = false;
    public TrayClickAction TrayDoubleClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;

    // Context Menu
    public bool ShowProfileSelectorsInMenu { get; set; } = true;
    public bool ShowMonitorPowerButtons { get; set; } = false;
    public bool ShowAllDisplaysPowerButton { get; set; } = true;
    public PowerOffMode PowerOffMode { get; set; } = PowerOffMode.Sleep;
    public ContextMenuPosition ContextMenuPosition { get; set; } = ContextMenuPosition.Modern;
    public bool KeepFlyoutWarm { get; set; } = true;
    public bool KeepTrayContextMenuWarm { get; set; } = true;

    // Monitor Options
    public int BrightnessUpdateRateMs { get; set; } = TimeConstants.BrightnessUpdateRateDefaultMs;
    public int ValidationDwellMs { get; set; } = TimeConstants.ValidationDwellDefaultMs;

    /// <summary>
    /// Number of read attempts <c>MonitorService.TryReadBrightnessWithRetry</c> makes before giving up on
    /// a monitor's DDC/CI link.
    /// Each subsequent attempt waits one <see cref="ValidationDwellMs"/> before re-reading;
    /// the final attempt also refreshes the cached HMONITOR as a last-ditch escalation against stale handles.
    /// Higher = more tolerant of transient I2C noise / DPMS-wake races;
    /// lower = faster failure for genuinely stuck monitors.
    /// </summary>
    public int ValidationAttempts { get; set; } = 4;

    /// <summary>
    /// Maximum wall-clock time any single dxva2-backed call (capability fetch, VCP read, VCP write) is allowed to
    /// block before the wrapper returns failure to the caller and abandons the wait.
    /// The abandoned dxva2 call still finishes naturally on a threadpool thread so its physical monitor handles
    /// are released - only the synchronous wait is cut short.
    /// Defends against driver-layer hangs that would otherwise pile up threads forever and block app shutdown.
    /// Zero or negative disables the wrapper (calls block forever, matching the unwrapped contract).
    /// </summary>
    public int DDCOperationTimeoutMs { get; set; } = TimeConstants.DDCOperationTimeoutDefaultMs;

    public MasterSliderMode MasterSliderMode { get; set; } = MasterSliderMode.Average;
    public bool ShowFlyoutMonitorPowerButtons { get; set; } = false;
    public bool ShowFlyoutMonitorNumberBadge { get; set; } = false;
    public bool ShowFlyoutDisplaySettingsButton { get; set; } = true;
    public bool ShowFlyoutFooterPowerButton { get; set; } = false;
    public bool FooterPowerButtonOnlyEnabledMonitors { get; set; } = false;
    public bool ShowMasterSlider { get; set; } = true;
    public bool ShowIndividualSliders { get; set; } = true;
    public bool ShowNightLightSlider { get; set; } = true;
    public int FlyoutScrollWheelStep { get; set; } = 2;

    // Master switch for the undock feature.
    // When false, the undock button is hidden, and any persisted undocked state is force-redocked
    // the next time the flyout opens - disabling the feature should never leave a free-floating window stranded.
    public bool AllowFlyoutUndock { get; set; } = true;

    // When true, the flyout reopens in the previous session's docked/undocked state at startup.
    // When false, the flyout always opens docked at launch regardless of FlyoutUndocked;
    // runtime undock/redock still persist normally so flipping this back on resumes restoration.
    public bool RestoreFlyoutUndockedOnStartup { get; set; } = true;

    // Flyout dock state.
    // When FlyoutUndocked is true and FlyoutHasSavedPosition is set, the flyout opens at FlyoutLeft/FlyoutTop
    // and behaves like a free-floating window (always-on-top, doesn't auto-hide on focus loss).
    // Tray-icon click and the redock button both flip this back to docked.
    // The position is only written to disk on drag-release, not while dragging.
    public bool FlyoutUndocked { get; set; } = false;

    /// <summary>
    /// Sticky one-shot acknowledgement for the warning-triangle hard power-off click.
    /// False until the user confirms the destructive-action overlay the first time;
    /// after that, subsequent warning-glyph clicks fire the 0x05 power-off without prompting.
    /// </summary>
    public bool HasAcknowledgedHardPowerOffWarning { get; set; } = false;

    public bool FlyoutHasSavedPosition { get; set; } = false;
    public double FlyoutLeft { get; set; } = 0;
    public double FlyoutTop { get; set; } = 0;
    public bool ShowEnvironmentalCurvesButton { get; set; } = true;
    public bool ShowNightLightKelvinLabel { get; set; } = false;
    public bool InvertNightLightSlider { get; set; } = false;

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
    /// Legacy setting retained for settings-file compatibility.
    /// DDC fallback acquisition now retries indefinitely while failed/read-degraded candidates exist.
    /// </summary>
    public int MaxRecoveryAttempts { get; set; } = 60;

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
    public bool TurnOffNightLightAtZeroStrength { get; set; } = false;

    /// <summary>
    /// HTTP timeout (seconds) used by <see cref="BrightnessTrayAppDotNET.Interop.NightLight.PDBSymbolResolver"/>
    /// when fetching SettingsHandlers_Display.dll's PDB from the Microsoft public symbol server.
    /// The resolver only runs after a Windows update introduces an unknown DLL build,
    /// so this fires at most once per build-version transition;
    /// the default 60s is enough for a typical home connection but slow or metered links can raise it
    /// to avoid a fallthrough to the registry/gamma backend.
    /// </summary>
    public int NightLightPDBDownloadTimeoutSeconds { get; set; } = 60;

    // Auto-update.
    // CheckForUpdatesEnabled gates the background poll loop entirely; flipping it off cancels any in-flight
    // wait without disposing the service. ShowUpdateButtonInFlyout controls only the floating Update! glyph
    // on top of the flyout, leaving the in-Settings actions reachable even when the flyout affordance is off.
    // ShowUpdateNotificationsEnabled drives the tray balloon shown when the flyout is closed and a fresh
    // version is detected; defaults to off so the first-run experience doesn't ambush new users.
    public bool CheckForUpdatesEnabled { get; set; } = true;
    public bool ShowUpdateNotificationsEnabled { get; set; } = false;
    public bool ShowUpdateButtonInFlyout { get; set; } = true;
    public int UpdateCheckIntervalMs { get; set; } = TimeConstants.UpdateCheckIntervalDefaultMs;

    public DisplaySortMode DefaultDisplaySortMode { get; set; } = DisplaySortMode.Arrangement;
    public DisplaySortDirection DefaultDisplaySortDirection { get; set; } = DisplaySortDirection.Standard;
    public MonitorIdentityStrategy MonitorIdentityStrategy { get; set; } = MonitorIdentityStrategy.DisplayNumber;
    public List<string> MonitorOrder { get; set; } = [];
    [XmlArrayItem("Monitor")]
    public List<MonitorOverrideEntry> MonitorOverrides { get; set; } = [];

    [XmlArrayItem("Display")]
    public List<KnownDisplayEntry> KnownDisplays { get; set; } = [];

    // Empty by default; defaults are seeded by EnsureDefaultHotkeys() after construction or load.
    // The previous in-place initializer collided with the old "append to existing list" load behavior:
    // the loader adds <Binding> elements to the list returned by the getter, so any default
    // listed here would duplicate every time the saved settings.xml was reloaded.
    [XmlArrayItem("Binding")]
    public List<HotkeyBinding> Hotkeys { get; set; } = [];

    // Theme
    public int ContextMenuFontSize { get; set; } = 15;
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    public NullableThemeColor TextColor { get; set; } = new();
    public NullableThemeColor BackgroundColor { get; set; } = new();
    public TrayIconStyle TrayIconStyle { get; set; } = TrayIconStyle.Dynamic;
    public MasterSliderMode DynamicIconBrightnessTracking { get; set; } = MasterSliderMode.Average;
    public bool DynamicIconTrackEnabledOnly { get; set; } = false;
    public NullableThemeColor TrayIconColor { get; set; } = new();
    public NullableThemeColor TrayIconBrightColor { get; set; } = new();
    public NullableThemeColor TrayIconDimColor { get; set; } = new();
    public NullableThemeColor FooterBackgroundColor { get; set; } = new();
    public bool EnableRoundedCorners { get; set; } = true;

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
    public bool EnvironmentalBrightnessCurveEnabled { get; set; } = false;
    public bool EnvironmentalNightLightCurveEnabled { get; set; } = false;
    [XmlArrayItem("Stopwatch")]
    public List<CurveStopwatchEntry> CurveStopwatches { get; set; } = [];

    // Offset mode: when on, the editor exposes the per-profile *Offset curves
    // (additive/subtractive deltas, -100..+100 Y axis) plus draggable min/max clamp lines.
    // When off, the editor exposes the absolute Brightness/NightLight curves (0..100 Y axis).
    // Both sets are stored independently on each profile so toggling is non-destructive.
    public bool EnvironmentalOffsetMode { get; set; } = false;

    // Cursor readout: when on, the curve editor draws a vertical scrubber at the cursor's X
    // and a small marker on each visible curve labelled with its value at that X.
    // The top-right "time / value" readout is always visible while the cursor is inside the editor
    // regardless of this setting; the toggle controls only the per-curve readouts.
    public bool EnvironmentalShowCursorReadout { get; set; } = false;

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
    // before any DDC write fires.
    // Range is policed by the settings UI;
    // 0 isn't a valid value here - a zero-interval DispatcherTimer would busy-loop.
    public int EnvironmentalCurveTickIntervalMs { get; set; } = TimeConstants.EnvironmentalCurveTickIntervalDefaultMs;

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

    /// <summary>
    /// Raised when any setting is changed through the settings window.
    /// </summary>
    public event Action? Changed;

    public void RaiseChanged() => Changed?.Invoke();

    public AppSettings() => WireColorCallbacks();

    public void OnTrayXmlSerializing()
    {
    }

    public void OnTrayXmlDeserializing()
    {
    }

    public void OnTrayXmlDeserialized()
    {
        WireColorCallbacks();
        InitializeSliderThumbCatalog();
    }

    /// <summary>
    /// Bridges every <see cref="NullableThemeColor"/> override on this instance to the global
    /// <see cref="Changed"/> event, so any color edit (committed hex or live-preview Temporary*) flows out
    /// through the same notification path as every other setting change.
    /// Idempotent: Unsubscribe runs first, so re-wiring after loading replaces the ctor-wired instances
    /// post-load can't double-fire.
    /// Specific listeners that want per-color granularity (e.g. the curve editor reacting only to its own curve color)
    /// should attach via <see cref="NullableThemeColor.Subscribe"/> directly.
    /// </summary>
    public void WireColorCallbacks()
    {
        Action onChanged = RaiseChanged;
        foreach (NullableThemeColor color in EnumerateColorOverrides())
        {
            color.Unsubscribe(onChanged);
            color.Subscribe(onChanged);
        }
    }

    private IEnumerable<NullableThemeColor> EnumerateColorOverrides()
    {
        yield return TextColor;
        yield return BackgroundColor;
        yield return TrayIconColor;
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

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "settings.xml");
    }

    /// <summary>
    /// The folder that holds settings.xml and other per-app data.
    /// Used by the uninstaller's "delete settings" branch.
    /// </summary>
    public static string GetDefaultDirectory() =>
        Program.AppLocalAppDataDirectory;

    public void Save() => Save(GetDefaultPath());

    public void Save(string path)
        => TrayXmlSerializer.TryWriteFile(
            path,
            this,
            ex => WPFLog.Log($"AppSettings.Save: {ex.Message}"));

    public static AppSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static AppSettings LoadOrDefault(string path)
    {
        try
        {
            if (TrayXmlSerializer.TryReadFile(path, out AppSettings? loaded))
            {
                bool changed = loaded.DedupeHotkeysByIdentity();
                changed |= loaded.EnsureDefaultHotkeys();
                if (changed) loaded.Save(path);
                return loaded;
            }
        }
        catch
        {
            // fall through to default
        }

        AppSettings defaults = new();
        defaults.InitializeSliderThumbCatalog();
        defaults.EnsureDefaultHotkeys();
        defaults.Save(path);
        return defaults;
    }

    /// <summary>
    /// The set of built-in hotkey bindings seeded for fresh installs and topped up on every launch.
    /// Identity is (Action, Parameter, BindingID): defaults always live on BindingID 0 (the primary
    /// row), so a user-added secondary binding (BindingID >= 1) for the same action does not block
    /// re-seeding the primary row.
    /// </summary>
    private static IReadOnlyList<HotkeyBinding> CreateDefaultHotkeys() =>
    [
        new()
        {
            Action = BrightnessHotkeyAction.FullBright,
            Parameter = string.Empty,
            Modifiers = User32.MOD_CONTROL | User32.MOD_WIN | User32.MOD_ALT,
            VirtualKey = 0x46, // VK_F
            Enabled = true,
            BindingID = 0,
        },
    ];

    /// <summary>
    /// True if the binding occupies the same identity slot as one of the built-in defaults
    /// (same Action, Parameter, and BindingID). Used by the settings UI to decide whether
    /// removing a binding should hard-delete it or keep it as a tombstone (RemovedByUser=true)
    /// so the default doesn't reappear on the next launch.
    /// </summary>
    public static bool IsDefaultHotkeyIdentity(BrightnessHotkeyAction action, string parameter, int bindingID)
    {
        foreach (HotkeyBinding d in CreateDefaultHotkeys())
            if (d.Matches(action, parameter, bindingID))
                return true;
        return false;
    }

    /// <summary>
    /// Removes redundant hotkey rows that share the same identity tuple (Action, Parameter, BindingID),
    /// keeping the first occurrence. Cleans up the duplicate rows that older builds accumulated
    /// when the default list was re-seeded on every load.
    /// Returns true when at least one row was dropped (caller should persist).
    /// </summary>
    public bool DedupeHotkeysByIdentity()
    {
        HashSet<(BrightnessHotkeyAction, string, int)> seen = [];
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < Hotkeys.Count; readIndex++)
        {
            HotkeyBinding b = Hotkeys[readIndex];
            (BrightnessHotkeyAction, string, int) key = (b.Action, b.Parameter, b.BindingID);
            if (!seen.Add(key)) continue;

            if (writeIndex != readIndex) Hotkeys[writeIndex] = b;
            writeIndex++;
        }

        if (writeIndex == Hotkeys.Count) return false;

        Hotkeys.RemoveRange(writeIndex, Hotkeys.Count - writeIndex);
        return true;
    }

    /// <summary>
    /// Adds any built-in default hotkey bindings that aren't already represented in <see cref="Hotkeys"/>.
    /// "Represented" means: an existing entry with the same (Action, Parameter, BindingID) - including
    /// tombstoned entries with RemovedByUser=true - so a user who has explicitly removed a default
    /// is not re-seeded.
    /// Returns true when at least one default was newly added (caller should persist).
    /// </summary>
    public bool EnsureDefaultHotkeys()
    {
        bool added = false;
        foreach (HotkeyBinding d in CreateDefaultHotkeys())
        {
            bool present = false;
            foreach (HotkeyBinding existing in Hotkeys)
            {
                if (!existing.Matches(d.Action, d.Parameter, d.BindingID)) continue;

                present = true;
                break;
            }

            if (present) continue;

            Hotkeys.Add(new HotkeyBinding
            {
                Action = d.Action,
                Parameter = d.Parameter,
                Modifiers = d.Modifiers,
                VirtualKey = d.VirtualKey,
                Enabled = d.Enabled,
                BindingID = d.BindingID,
            });
            added = true;
        }

        return added;
    }

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
