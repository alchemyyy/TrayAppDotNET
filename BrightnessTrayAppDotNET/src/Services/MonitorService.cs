using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.Utils;
using TrayAppDotNETCommon.Services;

namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Legacy per-monitor recovery actions retained for explicit targeted probes.
/// The normal DDC fallback path now calls <see cref="MonitorService.TryRecoverMonitor"/> with
/// <see cref="RefreshHandle"/> so it refreshes only the stuck monitor instead of tearing down
/// healthy rows.
/// </summary>
public enum DDCRecoveryAction
{
    /// <summary>Re-enumerate and re-probe with no extra prep.</summary>
    Probe,

    /// <summary>Re-enumerate, refresh the cached HMONITOR, then re-probe.</summary>
    RefreshHandle,
}

internal interface IMonitorServiceDispatcher
{
    bool CheckAccess();
    void Post(Action action);
    void Invoke(Action action);
    T Invoke<T>(Func<T> action);
}

internal sealed class AvaloniaMonitorServiceDispatcher(Dispatcher dispatcher) : IMonitorServiceDispatcher
{
    public bool CheckAccess() => dispatcher.CheckAccess();
    public void Post(Action action) => dispatcher.Post(action);
    public void Invoke(Action action) => dispatcher.InvokeAsync(action).GetAwaiter().GetResult();
    public T Invoke<T>(Func<T> action) => dispatcher.InvokeAsync(action).GetAwaiter().GetResult();
}

/// <summary>
/// Bridges the DDC/CI layer and the UI's <see cref="MonitorInfo"/> models.
/// Owns the authoritative list of <see cref="MonitorInfo"/> instances - the flyout binds to <see cref="Monitors"/>
/// directly so add/remove from hot-plug flows through UI collection-change notifications without any manual wiring.
///
/// Identity is keyed off <see cref="DDCMonitor.DeviceID"/> (derived from <c>EnumDisplayDevices</c>) so a monitor
/// unplugged and re-plugged on the same port keeps its <see cref="MonitorInfo"/> instance, its profile state, and its
/// place in the UI; only its HMONITOR handle is refreshed.
///
/// Writes are per-monitor throttled: while a write is in flight the latest requested value replaces any earlier queued
/// one, so rapid slider drags never back up an unbounded queue - the final value always lands after one cooldown
/// interval.
/// </summary>
public sealed class MonitorService : IDisposable
{
    private readonly IDisplayService _display;
    private readonly AppSettings _settings;
    private readonly KnownDisplaysStore _knownDisplays;
    private readonly IMonitorServiceDispatcher _dispatcher;

    private readonly ConcurrentDictionary<string, MonitorEntry> _entries = new(StringComparer.Ordinal);

    // Per-monitor latest-pending-wins scheduler.
    // Owns the cooldown between brightness writes; the payloads it runs hold the per-monitor DDC mutex (the lock is
    // for bus atomicity vs other DDC ops, the throttler is for pacing - different concerns).
    private readonly AsyncThrottler<string> _writeThrottler;
    private int _writeCooldownMs;
    private int _validationDwellMs;
    private MonitorIdentityStrategy _activeStrategy;
    private bool _disposed;

    // Per-monitor DDC mutex registry.
    // Every dxva2 call against a given physical monitor goes through WithDDCLock(...) keyed by DeviceID so a recovery
    // probe and a slider-driven write can't interleave on the bus.
    // Layer 1's per-op timeout bounds the caller wait; DisplayService also suppresses same-monitor overlap while an
    // abandoned timed-out dxva2 task is still releasing its physical-monitor handles.
    private readonly Dictionary<string, SemaphoreSlim> _ddcLocks = new(StringComparer.Ordinal);
    private readonly Lock _ddcLocksGate = new();

    // Live count of in-flight DDC ops, maintained by WithDDCLock entry/exit.
    // BeginDrainAsync polls this to know when shutdown can safely tear down the rest of the service.
    private int _activeDDCOps;

    // True once BeginDrainAsync has been called.
    // Public entry-points check this and bail before starting a new op so drain converges instead of being chased by
    // fresh work.
    private volatile bool _draining;

    // Reentrancy guard for Refresh's Phase B probe pass.
    // Incremented on every Refresh before Phase B is started or scheduled; async probe continuations
    // capture the generation and bail when a newer Refresh has already incremented.
    // Without this, two Refreshes within the post-detection settle window (1.5 s) would stack two
    // deferred Phase Bs running on stale captured snapshots - producing duplicate add/probe work and
    // visible churn on the flyout's CollectionChanged path.
    // ScheduleStartupRecoverySweep's +2s/+5s Refreshes go through Refresh() so they participate in
    // this generation naturally - the latest scheduled Phase B wins.
    private int _refreshGen;

    // Wall-clock of the last topology event reported by the caller (via NotifyTopologyEvent).
    // Phase B uses (now - this) to decide whether the monitor MCU still needs a post-arrival
    // settle window. Cold-start Refresh from the ctor leaves this at MinValue so Phase B runs
    // synchronously - the monitors have been connected since boot and don't need a settle.
    // WM_DEVICECHANGE-driven Refresh from DisplayEventManager sets this to UtcNow before calling
    // Refresh, so Phase B defers for the remaining settle window. Event-driven gating, no
    // unconditional 1.5 s delay on the user's startup path.
    private DateTime _lastTopologyEventUtc = DateTime.MinValue;

    /// <summary>
    /// Raised after <see cref="Refresh"/> finishes applying add/remove/handle-refresh mutations.
    /// Always fires on the UI thread.
    /// </summary>
    public event Action? MonitorsRefreshed;

    /// <summary>
    /// Raised for monitors whose DDC channel was newly acquired or recovered during a refresh.
    /// Acquisition itself is read-only; subscribers decide whether a mode-specific write is warranted.
    /// Always fires on the UI thread before <see cref="MonitorsRefreshed"/>.
    /// </summary>
    public event Action<IReadOnlyList<MonitorInfo>>? MonitorsAcquired;

    /// <summary>
    /// Optional caller-supplied predicate: returns true when the brightness environmental curve is
    /// currently engaged. Used by physical brightness acquisition/recovery so hardware reads do not
    /// overwrite curve-owned slider intent. Null query -> false.
    /// </summary>
    public Func<bool>? IsBrightnessCurveEnabledQuery { get; set; }

    /// <summary>
    /// Optional caller-supplied predicate: returns true when the environmental curve's
    /// disabled-period window is currently passing through. Plumbed into
    /// <see cref="SliderStateMachine.OnHardwareRecovered"/> on every promote path so a recovered row
    /// lands directly in CurveActive / CurveSleeping in one PropertyChanged fan-out
    /// instead of going Enabled -> CurveActive on the curve service's harmonize pass and triggering
    /// per-row master jitter. Null query -> false.
    /// </summary>
    public Func<bool>? IsInDisabledPeriodQuery { get; set; }

    /// <summary>
    /// Creates the monitor service and optionally uses an injected known-display store.
    /// </summary>
    public MonitorService(IDisplayService display, AppSettings settings, KnownDisplaysStore? knownDisplays = null)
        : this(display, settings, knownDisplays, new AvaloniaMonitorServiceDispatcher(Dispatcher.UIThread)) { }

    internal MonitorService(
        IDisplayService display,
        AppSettings settings,
        KnownDisplaysStore? knownDisplays,
        IMonitorServiceDispatcher dispatcher)
    {
        _display = display;
        _settings = settings;
        _dispatcher = dispatcher;

        // Optional injection: callers wired up before the displays.json extraction keep working with the
        // two-arg constructor.
        // A default-constructed store points at the same %LocalAppData% folder as settings.xml, so behaviour matches a
        // manually-injected instance.
        _knownDisplays = knownDisplays ?? new KnownDisplaysStore();

        // First-run migration: when displays.json doesn't exist yet, seed the new store from the legacy
        // AppSettings.KnownDisplays list so users upgrading from a build without the extracted store don't lose their
        // accumulated history (or, more importantly, the sticky WasEverDDCCapable flags DDCRecoveryService relies on).
        _knownDisplays.Load(_settings.KnownDisplays);

        _writeCooldownMs = Math.Max(0, settings.BrightnessUpdateRateMs);
        _validationDwellMs = Math.Max(0, settings.ValidationDwellMs);
        _display.OperationTimeoutMs = settings.DDCOperationTimeoutMs;
        _writeThrottler = new AsyncThrottler<string>(_writeCooldownMs, StringComparer.Ordinal);

        // Re-sort the monitor list whenever the sort settings or manual override change.
        _settings.Changed += OnSettingsChanged;

        Refresh();

        // Cold-start recovery: re-Refresh a couple of seconds later so panels whose registry EDID wasn't yet populated
        // when the constructor ran get their proper edid-keyed identity before the user notices a stuck slider.
        // Self-terminates if everything is already healthy.
        ScheduleStartupRecoverySweep();
    }

    private void OnSettingsChanged()
    {
        if (_disposed) return;

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(OnSettingsChanged);
            return;
        }

        // Forward the timeout setting to the DDC layer immediately so a user adjusting it in Settings doesn't have to
        // restart the app. Cheap (just a property write) and safe to do before any other work - it's a per-call read
        // on the DDC side.
        _display.OperationTimeoutMs = _settings.DDCOperationTimeoutMs;

        // Identity-strategy change invalidates every MonitorInfo.ID - do a full re-enumerate so each monitor gets
        // re-keyed under the new strategy.
        // Existing entries will appear "removed" (old id isn't in the new set) and new entries "added" via the normal
        // Refresh reconciliation, which triggers the flyout's CollectionChanged handlers to rewire dependents.
        if (_settings.MonitorIdentityStrategy != _activeStrategy)
        {
            Refresh();
            return;
        }

        ApplyNameOverridesToExisting();
        if (ApplyBrightnessVcpOverridesToExisting())
        {
            Refresh();
            return;
        }

        ApplyBrightnessBoundOverridesToExisting(replayHardware: true);
        ApplyNormCurveOverridesToExisting(replayHardware: true);
        ResortMonitors();
    }

    /// <summary>
    /// Re-applies the per-monitor name override from <see cref="AppSettings.MonitorOverrides"/> onto every
    /// <see cref="MonitorInfo"/> already in <see cref="Monitors"/>.
    /// Called when settings change so a name edit in Settings propagates to the flyout slider live, without waiting
    /// for a hardware refresh.
    /// </summary>
    private void ApplyNameOverridesToExisting()
    {
        Dictionary<string, string> overrides = BuildNameOverrideMap();
        foreach (MonitorInfo info in Monitors) info.Name = ResolveDisplayName(info, overrides);
    }

    private bool ApplyBrightnessVcpOverridesToExisting()
    {
        Dictionary<string, MonitorOverrideEntry> map = BuildMonitorOverrideEntryMap();
        bool changed = false;

        foreach (MonitorEntry entry in _entries.Values)
        {
            byte before = entry.DDC.BrightnessCode;
            entry.DDC.BrightnessCode = VCPConstants.Brightness;
            DDCMonitorDatabase.ApplyProfile(entry.DDC);
            ApplyBrightnessVcpOverride(entry.DDC, entry.EDIDKey, map);
            if (entry.DDC.BrightnessCode == before) continue;

            changed = true;
            Volatile.Write(ref entry.LastEnqueuedPercentage, -1);
            MonitorInfo? info = Monitors.FirstOrDefault(m => m.ID == entry.ID);
            info?.LastKnownBrightnessMax = 100;
        }

        return changed;
    }

    /// <summary>
    /// Pushes the per-monitor min/max brightness overrides
    /// (<see cref="MonitorOverrideEntry.MinBrightness"/> / <see cref="MonitorOverrideEntry.MaxBrightness"/>)
    /// onto every DDC-supported <see cref="MonitorEntry"/>'s floor/ceiling fields.
    /// Lookup is keyed by EDIDKey so the override survives identity-strategy changes.
    /// When a bound actually changes for a monitor, the entry's last-enqueued sentinel is reset
    /// and a fresh write of the current slider position is queued -
    /// so a freshly-tightened floor snaps the panel up to the new minimum
    /// without waiting for the user's next slider drag.
    /// </summary>
    private void ApplyBrightnessBoundOverridesToExisting(bool replayHardware)
    {
        Dictionary<string, MonitorOverrideEntry> map = BuildBrightnessBoundOverrideMap();
        foreach (MonitorInfo info in Monitors) ApplyBrightnessBoundsTo(info, map, replayHardware);
    }

    private Dictionary<string, string> BuildNameOverrideMap() =>
        _settings.MonitorOverrides
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .GroupBy(m => m.ID, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Name, StringComparer.Ordinal);

    private Dictionary<string, MonitorOverrideEntry> BuildMonitorOverrideEntryMap() =>
        _settings.MonitorOverrides
            .GroupBy(m => m.ID, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    private static void ApplyBrightnessVcpOverride(
        DDCMonitor ddc,
        string EDIDKey,
        Dictionary<string, MonitorOverrideEntry> map)
    {
        if (string.IsNullOrEmpty(EDIDKey)) return;
        if (!map.TryGetValue(EDIDKey, out MonitorOverrideEntry? ov)) return;
        if (TryParseVcpCode(ov.BrightnessVcpOverride, out byte code))
        {
            ddc.BrightnessCode = code;
            WPFLog.Log(
                $"MonitorService: brightness VCP override for '{ddc.Name}' "
                + $"raw='{ov.BrightnessVcpOverride}' parsed=0x{code:X2}");
        }
    }

    private static bool TryParseVcpCode(string? text, out byte code)
    {
        code = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string firstToken = text.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries)[0];
        if (firstToken.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return byte.TryParse(firstToken[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code);

        return byte.TryParse(firstToken, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code);
    }

    private static bool TryParsePowerOverride(string? text, (byte Code, byte? Value)? fallback, out byte code,
        out byte value)
    {
        code = 0;
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string[] tokens = text.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        if (!TryParseVcpCode(tokens[0], out code)) return false;

        if (tokens.Length > 1)
        {
            if (!TryParseVcpCode(tokens[1], out value)) return false;
            return true;
        }

        if (fallback is { } f && f.Code == code && f.Value is { } fallbackValue)
        {
            value = fallbackValue;
            return true;
        }

        return false;
    }

    private bool TryResolvePowerOffOverride(DDCMonitor ddc, PowerOffLevel level, out byte code, out byte value)
    {
        (code, value) = ddc.ResolvePowerOff(level);
        string EDIDKey = ComputeEDIDKey(ddc);
        if (string.IsNullOrEmpty(EDIDKey)) return false;
        if (!BuildMonitorOverrideEntryMap().TryGetValue(EDIDKey, out MonitorOverrideEntry? ov)) return false;

        (byte Code, byte Value) fallback = ddc.ResolvePowerOff(level);
        if (!TryParsePowerOverride(ov.PowerOffVcpOverride, (fallback.Code, fallback.Value), out byte parsedCode,
                out byte parsedValue))
            return false;

        code = parsedCode;
        value = parsedValue;
        WPFLog.Log(
            $"MonitorService: power-off VCP override for '{ddc.Name}' "
            + $"raw='{ov.PowerOffVcpOverride}' parsed=0x{code:X2}=0x{value:X2}");
        return true;
    }

    /// <summary>
    /// Builds a lookup of MonitorOverrideEntry rows that carry an active min or max brightness override,
    /// keyed by EDIDKey. Rows whose bounds are at the no-op defaults (min &lt;= 0 and max &gt;= 100) are
    /// excluded so the apply path doesn't have to re-check.
    /// </summary>
    private Dictionary<string, MonitorOverrideEntry> BuildBrightnessBoundOverrideMap() =>
        _settings.MonitorOverrides
            .Where(m => m.MinBrightness > 0 || m.MaxBrightness < 100)
            .GroupBy(m => m.ID, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    /// <summary>
    /// Builds a lookup of MonitorOverrideEntry rows that carry a per-monitor brightness norm curve,
    /// keyed by EDIDKey. Rows with fewer than two points are excluded - the sampler needs at least
    /// two endpoints to define a line, and a single-point list collapses to a constant function.
    /// </summary>
    private Dictionary<string, MonitorOverrideEntry> BuildNormCurveOverrideMap() =>
        _settings.MonitorOverrides
            .Where(m => m.NormCurvePoints.Count >= 2)
            .GroupBy(m => m.ID, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    /// <summary>
    /// Pushes the per-monitor norm curve (<see cref="MonitorOverrideEntry.NormCurvePoints"/>)
    /// onto every DDC-supported <see cref="MonitorEntry"/> as pre-sorted xs/ys arrays
    /// ready for <see cref="EnvironmentalCurveSampler.InterpolateLinear"/>.
    /// Lookup is keyed by EDIDKey so the curve survives identity-strategy changes.
    /// When the resolved curve actually changes for a monitor, the entry's last-enqueued sentinel
    /// is reset and a fresh write of the current slider position is queued -
    /// so a freshly-edited curve takes effect on hardware now,
    /// not when the user happens to touch the slider next.
    /// </summary>
    private void ApplyNormCurveOverridesToExisting(bool replayHardware)
    {
        Dictionary<string, MonitorOverrideEntry> map = BuildNormCurveOverrideMap();
        foreach (MonitorInfo info in Monitors) ApplyNormCurveTo(info, map, replayHardware);
    }

    /// <summary>
    /// Resolves the curve for one monitor from the override map (null when no curve applies)
    /// and writes the pre-sorted xs/ys arrays onto the matching <see cref="MonitorEntry"/>.
    /// Skips monitors that don't have a live entry (currently DDC-unsupported / Failed) -
    /// their curve will be re-applied the next time they promote.
    /// On a real curve change, drops the dedupe sentinel and re-pushes the current slider position
    /// so the new shape takes effect on the bus immediately.
    /// </summary>
    private void ApplyNormCurveTo(
        MonitorInfo info,
        Dictionary<string, MonitorOverrideEntry> map,
        bool replayHardware)
    {
        if (!_entries.TryGetValue(info.ID, out MonitorEntry? entry)) return;

        double[]? xs = null;
        double[]? ys = null;
        if (!string.IsNullOrEmpty(info.EDIDKey)
            && map.TryGetValue(info.EDIDKey, out MonitorOverrideEntry? ov))
        {
            // Sort by X so the sampler's binary search is well-defined.
            // The editor stores points in click-order, not X-order, so this is the projection step.
            List<NormCurvePoint> ordered = [.. ov.NormCurvePoints.OrderBy(p => p.X)];
            int n = ordered.Count;
            xs = new double[n];
            ys = new double[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = ordered[i].X;
                ys[i] = ordered[i].Y;
            }
        }

        NormCurveProjection? existing = Volatile.Read(ref entry.NormCurve);
        if (CurveArraysEqual(existing?.Xs, xs) && CurveArraysEqual(existing?.Ys, ys)) return;

        Volatile.Write(
            ref entry.NormCurve,
            xs == null || ys == null ? null : new NormCurveProjection(xs, ys));

        // Drop the dedupe sentinel: a previously-curved enqueue may have left LastEnqueuedPercentage
        // sitting at the old shaped value, which would short-circuit the upcoming re-push.
        Volatile.Write(ref entry.LastEnqueuedPercentage, -1);

        // Acquisition/probe paths are read-only. They install the curve projection for the next explicit
        // writer but never replay a slider value as a side effect of discovering DDC support.
        if (!replayHardware) return;

        // Don't clobber a curve-owned row with the slider value; the curve owns the bus there
        // and will pick up the new norm-curve shape on its next tick (EnqueueDirectBrightness
        // inside the curve service applies the same per-monitor curve before sampling).
        // This also covers startup before the flyout-owned curve service has harmonized rows into
        // CurveActive: the persisted brightness-curve flag is enough to suppress slider replay.
        if (ShouldSuppressSliderBrightnessWrite(info)) return;

        // Re-enqueue the current slider position so the new curve takes effect on hardware now.
        // EnqueueDirectBrightness applies the just-updated curve (and floor/ceiling) internally.
        EnqueueDirectBrightness(info, info.RoundedBrightness);
    }

    private static bool CurveArraysEqual(double[]? a, double[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }

    /// <summary>
    /// Resolves floor/ceiling for one monitor from the override map (defaults 0/100 when no override
    /// applies) and writes them onto the matching <see cref="MonitorEntry"/>.
    /// Skips monitors that don't have a live entry (currently DDC-unsupported / Failed) - their cap
    /// will be re-applied the next time they promote.
    /// On a real bound change, drops the dedupe sentinel and re-pushes the current slider position
    /// so a tightened cap takes effect on the bus immediately.
    /// </summary>
    private void ApplyBrightnessBoundsTo(
        MonitorInfo info,
        Dictionary<string, MonitorOverrideEntry> map,
        bool replayHardware)
    {
        if (!_entries.TryGetValue(info.ID, out MonitorEntry? entry)) return;

        int floor = 0;
        int ceiling = 100;
        if (!string.IsNullOrEmpty(info.EDIDKey)
            && map.TryGetValue(info.EDIDKey, out MonitorOverrideEntry? ov))
        {
            // Min 0 / max 100 are the no-op defaults; only values that actually narrow the range apply.
            if (ov.MinBrightness > 0) floor = Math.Clamp(ov.MinBrightness, 0, 100);
            if (ov.MaxBrightness is >= 0 and < 100)
                ceiling = Math.Clamp(ov.MaxBrightness, 0, 100);
            // User-input sanity: if min > max, treat min as inactive so the user still has a usable
            // range rather than collapsing the cap to a single point at the (smaller) max.
            if (floor > ceiling) floor = 0;
        }

        if (entry.FloorPercent == floor && entry.CeilingPercent == ceiling) return;

        entry.FloorPercent = floor;
        entry.CeilingPercent = ceiling;

        // Drop the dedupe sentinel: a previously-clamped enqueue may have left LastEnqueuedPercentage
        // sitting at the old floor/ceiling, which would short-circuit the upcoming re-push.
        Volatile.Write(ref entry.LastEnqueuedPercentage, -1);

        // Acquisition/probe paths are read-only. They install the floor/ceiling projection for the next explicit
        // writer but never replay a slider value as a side effect of discovering DDC support.
        if (!replayHardware) return;

        // Don't clobber a curve-owned row with the slider value; the curve owns the bus there
        // and the curve service applies the same floor/ceiling clamp on its own writes.
        // This also covers startup before the flyout-owned curve service has harmonized rows into
        // CurveActive: the persisted brightness-curve flag is enough to suppress slider replay.
        if (ShouldSuppressSliderBrightnessWrite(info)) return;

        // Re-enqueue the current slider position so the new cap takes effect on hardware now,
        // not when the user happens to touch the slider next.
        // EnqueueDirectBrightness applies the just-updated floor/ceiling internally.
        EnqueueDirectBrightness(info, info.RoundedBrightness);
    }

    private static string ResolveDisplayName(MonitorInfo info, Dictionary<string, string> overrides)
    {
        if (overrides.TryGetValue(info.EDIDKey, out string? over) && !string.IsNullOrWhiteSpace(over)) return over;

        if (!string.IsNullOrWhiteSpace(info.OriginalName)) return info.OriginalName;

        if (info.DisplayNumber > 0) return $"Display {info.DisplayNumber}";

        return "Display";
    }

    /// <summary>
    /// Authoritative, observable list of monitor models.
    /// UI components should bind to this collection directly instead of copying it - that way hot-plug add/remove
    /// propagates automatically.
    /// </summary>
    public ObservableCollection<MonitorInfo> Monitors { get; } = [];

    /// <summary>
    /// Minimum interval between successive DDC/CI writes to any single monitor.
    /// Updates mid-session are honored by the next iteration of the write loop.
    /// </summary>
    public int WriteCooldownMs
    {
        get => _writeCooldownMs;
        set
        {
            _writeCooldownMs = Math.Max(0, value);
            _writeThrottler.CooldownMs = _writeCooldownMs;
        }
    }

    /// <summary>
    /// Settle delay used between a settled write and its read-back verification, and again between a re-apply and the
    /// next verification read.
    /// Separate from <see cref="WriteCooldownMs"/> because slider drag cadence and "how long the monitor needs to
    /// commit a value before we can read it back" have different characteristics - some panels accept rapid writes
    /// but take longer to update their internal state for read-back.
    /// </summary>
    public int ValidationDwellMs
    {
        get => _validationDwellMs;
        set => _validationDwellMs = Math.Max(0, value);
    }

    /// <summary>
    /// Called by event sources (currently <see cref="DisplayEventManager"/>) right before a topology-event-driven
    /// Refresh, to indicate that a real monitor arrival / departure / wake just fired. The next Refresh's
    /// Phase B uses this timestamp to gate the post-detection settle: monitors that JUST changed state need
    /// the LG-checksum settle window before being probed; monitors that have been stable since boot do not.
    /// Cold-start, startup recovery sweep, and DDC fallback Refreshes leave this untouched so Phase B
    /// runs synchronously - no unconditional wait on the user's launch path.
    /// </summary>
    public void NotifyTopologyEvent() => _lastTopologyEventUtc = DateTime.UtcNow;

    /// <summary>
    /// Re-enumerates physical monitors and reconciles the <see cref="Monitors"/> collection with the current hardware
    /// topology:
    /// <list type="bullet">
    /// <item>Still-present monitors keep their <see cref="MonitorInfo"/> - only the underlying HMONITOR handle is
    /// swapped in place.</item>
    /// <item>Newly-connected monitors get a fresh <see cref="MonitorInfo"/> appended and their hardware brightness is
    /// sampled to seed the slider.</item>
    /// <item>Detached monitors are removed from the collection; their write loop drains and exits on its next
    /// cooldown tick.</item>
    /// </list>
    /// Safe to call from any thread - work is marshalled onto the UI dispatcher
    /// so <see cref="ObservableCollection{T}"/> notifications fire correctly.
    /// </summary>
    public void Refresh()
    {
        if (_disposed || _draining) return;

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(Refresh);
            return;
        }

        if (!_display.TryGetMonitors(out IReadOnlyList<DDCMonitor> enumeratedRo, out string? enumError))
        {
            WPFLog.Log($"MonitorService.Refresh: enumeration failed: {enumError}");
            return;
        }

        List<DDCMonitor> enumerated = [.. enumeratedRo];

        // Capture previous strategy so we can tell whether existing MonitorInfo IDs need re-keying.
        // Strategy change is the only reason to mutate ID once a MonitorInfo has been minted - physical topology
        // shuffles (power-cycle, hot-plug) keep the ID stable so external state keyed on it (profile entries,
        // _entries, hotkey targets) survives the shuffle.
        MonitorIdentityStrategy previousStrategy = _activeStrategy;
        _activeStrategy = _settings.MonitorIdentityStrategy;
        bool strategyChanged = previousStrategy != _activeStrategy;

        Dictionary<string, DDCMonitor> latestByID = new(StringComparer.Ordinal);
        Dictionary<string, DDCMonitor> latestByEDIDKey = new(StringComparer.Ordinal);
        Dictionary<string, string> EDIDKeyByID = new(StringComparer.Ordinal);
        // Port-form key (port:DeviceID, or port:Name fallback) for every enumerated DDC.
        // Used as the third "still here?" signal to rescue rows whose EDIDKey was minted as port-form
        // on a cold-start probe that ran before the registry EDID landed. The follow-up Refresh
        // (startup recovery sweep) finds the same physical panel under an edid:-prefixed key; without
        // this map the row would look dropped in Phase A and would be destroyed+recreated under the new
        // edid: key, losing SliderState / Offset / LastUserBrightness / subscriptions.
        Dictionary<string, DDCMonitor> latestByPortForm = new(StringComparer.Ordinal);
        Dictionary<string, MonitorOverrideEntry> monitorOverridesByEDID = BuildMonitorOverrideEntryMap();
        foreach (DDCMonitor ddc in enumerated)
        {
            string EDIDKey = ComputeEDIDKey(ddc);
            ApplyBrightnessVcpOverride(ddc, EDIDKey, monitorOverridesByEDID);

            string id = ComputeMonitorID(ddc, _activeStrategy);
            if (string.IsNullOrEmpty(id)) continue;

            // Later HMONITORs win if there are duplicates
            latestByID[id] = ddc;
            EDIDKeyByID[id] = EDIDKey;
            if (!string.IsNullOrEmpty(EDIDKey)) latestByEDIDKey[EDIDKey] = ddc;
            string portForm = ComputePortFormKey(ddc);
            if (!string.IsNullOrEmpty(portForm)) latestByPortForm[portForm] = ddc;
        }

        // Persist a record of every unique display we've seen, keyed by EDIDKey.
        // The settings UI's "Display order & overrides" section reads this to render dimmed rows for displays that
        // aren't currently connected.
        RegisterKnownDisplays(latestByID.Values);

        // Per-monitor name overrides live alongside the other per-monitor data in MonitorOverrides, keyed by EDIDKey
        // (decoupled from the user's chosen MonitorIdentityStrategy so they survive strategy changes).
        Dictionary<string, string> nameOverridesByEDID = BuildNameOverrideMap();

        // 1. Reconcile monitors that are no longer in the enumeration.
        //    EDIDKey is the primary "is this physical panel still here?" signal because it survives display-number
        //    shuffles - a power-cycled panel often comes back with a different OS-assigned display number,
        //    and the old check (latestByID.ContainsKey(existing.ID)) treated that as a removal+addition,
        //    destroying the existing MonitorInfo and any UI state bound to it.
        //    Falls back to ID match for the rare monitor that doesn't expose an EDID.
        //
        //    Two cases for a missing monitor:
        //    a) Known DDC-capable panel (the user has driven it before). Treat the drop as transient -
        //       LG / DisplayPort panels with DP power-saving fully drop from Windows enumeration when the
        //       user hits the power button, and a forced removal + re-add would lose Brightness,
        //       LastUserBrightness, Offset, and the curve baseline, so the panel returns at whatever
        //       hardware default the EEPROM happens to report (often 100). Keep the MonitorInfo, mark
        //       Failed, drop the bus entry; the DDC fallback worker / next Refresh re-promotes the panel in
        //       place when it returns to enumeration, and the curve-driven gate on Brightness sync
        //       preserves the slider value through the cycle.
        //    b) Never DDC-capable (or no EDID at all). Genuinely gone, or never useful - drop normally.
        for (int i = Monitors.Count - 1; i >= 0; i--)
        {
            MonitorInfo existing = Monitors[i];
            bool stillPresent = !string.IsNullOrEmpty(existing.EDIDKey)
                ? latestByEDIDKey.ContainsKey(existing.EDIDKey)
                : latestByID.ContainsKey(existing.ID);
            // EDID-upgrade rescue: a row whose EDIDKey starts with "port:" was minted before EDID was
            // available. If the underlying port is still present in the enumeration (regardless of
            // whether it now reports a real EDID), treat it as still here - Phase B will re-key it in
            // place rather than letting Phase A drop the row and forcing a destroy+recreate. See M-16
            // / audit_08 F-06.
            if (!stillPresent
                && existing.EDIDKey is { Length: > 0 } EDIDKey
                && EDIDKey.StartsWith("port:", StringComparison.Ordinal)
                && latestByPortForm.ContainsKey(EDIDKey))
                stillPresent = true;
            if (stillPresent) continue;

            bool wasEverCapable = !string.IsNullOrEmpty(existing.EDIDKey)
                                  && (_knownDisplays.Find(existing.EDIDKey)?.WasEverDDCCapable ?? false);

            if (wasEverCapable)
            {
                // Park the row in Failed without losing it.
                // SliderState's setter stashes _preFailureSliderState on the first transition into Failed,
                // so a CurveActive panel power-cycled now still recovers as curve-driven and skips the
                // hardware-sync of Brightness on the rebound.
                existing.SliderState = SliderStateMachine.OnHardwareFailed();
                existing.LastDDCError = "Monitor not currently enumerated.";
                if (_entries.TryRemove(existing.ID, out MonitorEntry? droppedEntry))
                {
                    existing.LastKnownBrightnessMax = NormalizeBrightnessMax(droppedEntry.Max);
                    // In-flight write payload owns the (now-stale) DDC handle and will release cleanly;
                    // queued writes can't usefully target a missing panel, drop them.
                    _writeThrottler.Drop(existing.ID);
                }

                WPFLog.Log(
                    $"MonitorService: '{existing.Name}' dropped from enumeration; parking as Failed "
                    + $"(EDIDKey={existing.EDIDKey})");
                continue;
            }

            DetachMonitor(existing);
            Monitors.RemoveAt(i);
        }

        // 2. Refresh handles on surviving monitors; add new ones.
        //    Monitors that don't respond to a DDC/CI brightness query are added as disabled entries
        //    (IsDDCCISupported=false) rather than dropped - the scanner and subsequent refreshes will keep retrying,
        //    and a later refresh that succeeds promotes them in place.
        //
        //    Deferred behind MonitorPostDetectionSettleDelayMs so monitors that just hot-plugged, powered on,
        //    or had their DDC link renegotiated as a cascade of another monitor's power event get a settle window
        //    before we hammer them with VCP reads. Reading too early can desync the monitor MCU's I2C reply
        //    pipeline and wedge it into persistent INVALID_MESSAGE_CHECKSUM. Removal reconcile above stays
        //    immediate because leaving stale handles around invites doomed writes.
        Dictionary<string, DDCMonitor> capturedLatestByID = latestByID;
        Dictionary<string, string> capturedEDIDKeyByID = EDIDKeyByID;
        Dictionary<string, string> capturedNameOverrides = nameOverridesByEDID;
        bool capturedStrategyChanged = strategyChanged;
        // latestByPortForm is consumed by Phase A above; Phase B computes its own per-DDC port form
        // inline via ComputePortFormKey, so no capture is needed here.
        _ = latestByPortForm;

        // Event-gated settle: only delay Phase B if a topology event actually fired within the settle
        // window (LG-checksum protection). Cold-start, startup-sweep, and DDC fallback Refreshes
        // never call NotifyTopologyEvent so _lastTopologyEventUtc is MinValue (or stale by much more
        // than the settle window), and Phase B starts immediately below. No unconditional 1.5 s wait
        // on the user's launch path.
        double elapsedMs = (DateTime.UtcNow - _lastTopologyEventUtc).TotalMilliseconds;
        int remainingSettleMs = TimeConstants.MonitorPostDetectionSettleDelayMs - (int)elapsedMs;
        int scheduledGen = Interlocked.Increment(ref _refreshGen);
        if (remainingSettleMs <= 0)
        {
            // No active settle window - start probing immediately. The DDC read retries run off-dispatcher;
            // row mutations resume on this dispatcher and bail if a newer Refresh superseded this generation.
            _ = RefreshProbePhaseAsync(
                capturedLatestByID,
                capturedEDIDKeyByID,
                capturedNameOverrides,
                capturedStrategyChanged,
                scheduledGen);
            return;
        }

        // The generation captured above lets the deferred continuation detect a fresher Refresh that
        // landed during the settle window and bail without running on a stale snapshot.
        _ = Task.Delay(remainingSettleMs).ContinueWith(delayTask =>
        {
            if (!delayTask.IsCompletedSuccessfully) return;
            if (_disposed || _draining) return;
            // Threadpool-side gen check: if the gen has already moved past the one we scheduled,
            // a fresher Refresh is queued and will fire its own Phase B, so dropping this one is fine.
            if (Volatile.Read(ref _refreshGen) != scheduledGen) return;
            _dispatcher.Post(() =>
            {
                if (_disposed || _draining) return;
                _ = RefreshProbePhaseAsync(
                    capturedLatestByID,
                    capturedEDIDKeyByID,
                    capturedNameOverrides,
                    capturedStrategyChanged,
                    scheduledGen);
            });
        });
    }

    /// <summary>
    /// Per-monitor probe + reconcile + add phase of <see cref="Refresh"/>.
    /// Split out so a settle delay can sit between the (immediate) enumeration/removal phase and this
    /// (deferred) phase. See the comment block in <see cref="Refresh"/> for the rationale.
    /// Runs row mutations on the UI dispatcher; retrying DDC reads are awaited off-dispatcher so
    /// retry backoffs do not block startup or hot-plug UI.
    /// </summary>
    private async Task RefreshProbePhaseAsync(
        Dictionary<string, DDCMonitor> latestByID,
        Dictionary<string, string> EDIDKeyByID,
        Dictionary<string, string> nameOverridesByEDID,
        bool strategyChanged,
        int phaseGen)
    {
        if (!IsRefreshProbePhaseCurrent(phaseGen)) return;

        List<MonitorInfo> acquired = [];

        foreach ((string id, DDCMonitor ddc) in latestByID)
        {
            string EDIDKey = EDIDKeyByID[id];
            string portForm = ComputePortFormKey(ddc);

            // EDIDKey-first match is what makes power-cycles non-destructive: the same physical panel keeps its
            // MonitorInfo (and the UI / _entries / write-loop state attached to it) across topology shuffles where
            // its OS-assigned display number drifts.
            // ID-based match is the fallback for monitors with empty EDIDs.
            // Port-form match is the EDID-upgrade rescue: a row minted on a cold-start probe before the
            // registry EDID landed sits under EDIDKey "port:DeviceID"; the follow-up Refresh sees the same
            // panel with a real EDID and would otherwise treat it as new. See M-16 / audit_08 F-06.
            MonitorInfo? existingInfo = null;
            if (!string.IsNullOrEmpty(EDIDKey)) existingInfo = Monitors.FirstOrDefault(m => m.EDIDKey == EDIDKey);
            existingInfo ??= Monitors.FirstOrDefault(m => m.ID == id);
            bool reKeyingFromPortForm = false;
            if (existingInfo == null && !string.IsNullOrEmpty(portForm))
            {
                MonitorInfo? portMatch = Monitors.FirstOrDefault(m =>
                    !string.IsNullOrEmpty(m.EDIDKey)
                    && m.EDIDKey.StartsWith("port:", StringComparison.Ordinal)
                    && string.Equals(m.EDIDKey, portForm, StringComparison.Ordinal));
                if (portMatch != null && !string.Equals(portMatch.EDIDKey, EDIDKey, StringComparison.Ordinal))
                {
                    existingInfo = portMatch;
                    reKeyingFromPortForm = true;
                }
            }

            if (existingInfo != null)
            {
                // Re-key when the user explicitly changed identity strategy, OR when a port-form
                // EDIDKey is being promoted to its proper edid: identity now that EDID is readable.
                // Both cases mutate ID/EDIDKey in place rather than destroy+recreate, so SliderState,
                // Offset, LastUserBrightness, PropertyChanged subscriptions, and the throttler's
                // queued payload all survive.
                if ((strategyChanged && existingInfo.ID != id) || reKeyingFromPortForm)
                {
                    string oldID = existingInfo.ID;
                    if (oldID != id && _entries.TryRemove(oldID, out MonitorEntry? movingEntry))
                    {
                        movingEntry.ID = id;
                        movingEntry.EDIDKey = EDIDKey;
                        _entries[id] = movingEntry;
                    }

                    existingInfo.ID = id;
                    if (reKeyingFromPortForm)
                    {
                        WPFLog.Log(
                            $"MonitorService: re-keyed '{existingInfo.Name}' from "
                            + $"{(string.IsNullOrEmpty(oldID) ? "<empty>" : oldID)} -> {id} "
                            + $"(EDIDKey upgrade {portForm} -> {EDIDKey})");
                    }
                }

                // Always keep arrangement data fresh - Windows rearrange affects sorting for both supported and
                // unsupported rows.
                existingInfo.DisplayNumber = ddc.DisplayNumber;
                existingInfo.ArrangementX = ddc.X;
                existingInfo.ArrangementY = ddc.Y;
                existingInfo.EDIDKey = EDIDKey;
                existingInfo.OriginalName = ddc.FriendlyName;
                existingInfo.EDIDSerial = ddc.EDIDSerial;
                existingInfo.Name =
                    nameOverridesByEDID.TryGetValue(EDIDKey, out string? existingOverride)
                    && !string.IsNullOrWhiteSpace(existingOverride)
                        ? existingOverride
                        : BuildDefaultName(ddc);

                if (_entries.TryGetValue(existingInfo.ID, out MonitorEntry? entry))
                {
                    // Already supported - refresh the live DDC handles, then re-probe to catch monitors whose DDC
                    // link died while the app wasn't writing to them (no SetVCPFeature failure to trigger demotion).
                    // Without this re-probe, a monitor that silently dropped DDC stays stuck IsDDCCISupported=true
                    // forever and the warning UI / DDC fallback worker never fire.
                    entry.DDC.Handle = ddc.Handle;
                    entry.DDC.HDC = ddc.HDC;
                    entry.DDC.Name = ddc.Name;
                    entry.DDC.DeviceID = ddc.DeviceID;
                    entry.DDC.DisplayNumber = ddc.DisplayNumber;
                    entry.DDC.X = ddc.X;
                    entry.DDC.Y = ddc.Y;
                    entry.DDC.EDIDSerial = ddc.EDIDSerial;
                    entry.DDC.FriendlyName = ddc.FriendlyName;
                    entry.DDC.EDIDManufacturerID = ddc.EDIDManufacturerID;
                    entry.DDC.EDIDProductCode = ddc.EDIDProductCode;
                    entry.DDC.BrightnessCode = ddc.BrightnessCode;
                    entry.DDC.ProfileModelName = ddc.ProfileModelName;
                    entry.DDC.PowerOffCommands = ddc.PowerOffCommands;
                    entry.DDC.ProfileQuirks = ddc.ProfileQuirks;

                    // Use the full retry mechanism (80/160/480 backoff + final-attempt RefreshHandle) so
                    // a single transient read failure (INVALID_DEVICE / INVALID_MESSAGE_CHECKSUM) doesn't
                    // demote a healthy monitor and produce a ~1-2s warning-glyph blink before the DDC
                    // fallback probes it back. Single-shot reads here were responsible for the curve-toggle
                    // and topology-event flicker observed in the field.
                    (bool Ok, uint Current, uint Max, string? Error) probe = await TryReadBrightnessWithRetryAsync(ddc);
                    if (!IsRefreshProbePhaseCurrent(phaseGen)) return;

                    if (probe.Ok)
                    {
                        entry.Max = NormalizeBrightnessMax(probe.Max);
                        existingInfo.LastKnownBrightnessMax = entry.Max;
                        existingInfo.IsReadDegraded = false;
                        existingInfo.LastDDCError = null;
                    }
                    else
                    {
                        existingInfo.LastDDCError = probe.Error;
                        if (existingInfo.IsReadDegraded)
                        {
                            existingInfo.LastKnownBrightnessMax = NormalizeBrightnessMax(entry.Max);
                            WPFLog.Log(
                                $"MonitorService: kept read-degraded '{ddc.Name}' during Refresh re-probe "
                                + $"({probe.Error})");
                        }
                        else
                        {
                            existingInfo.SliderState = SliderStateMachine.OnHardwareFailed();
                            if (_entries.TryRemove(existingInfo.ID, out MonitorEntry? failedEntry))
                                existingInfo.LastKnownBrightnessMax = NormalizeBrightnessMax(failedEntry.Max);
                            // Drop any queued write for this monitor - a fresh value applied to a now-demoted entry would
                            // only generate a doomed retry. An in-flight payload is left to drain on its own (it
                            // captured the entry's DDC handle and will release cleanly).
                            _writeThrottler.Drop(existingInfo.ID);
                            WPFLog.Log(
                                $"MonitorService: demoted '{ddc.Name}' during Refresh re-probe ({probe.Error})");
                        }
                    }
                }
                else
                {
                    // Previously unsupported - attempt promotion with fresh handles
                    (bool Ok, uint Current, uint Max, string? Error) promote =
                        await TryReadBrightnessWithRetryAsync(ddc);
                    if (!IsRefreshProbePhaseCurrent(phaseGen)) return;

                    if (promote.Ok)
                    {
                        int percent = promote.Max == 0
                            ? 0
                            : (int)Math.Round(promote.Current * 100.0 / promote.Max);
                        uint promotedBrightnessMax = NormalizeBrightnessMax(promote.Max);
                        existingInfo.LastKnownBrightnessMax = promotedBrightnessMax;
                        LogProfileIfMatched(ddc);
                        _entries[existingInfo.ID] = new MonitorEntry
                        {
                            ID = existingInfo.ID, EDIDKey = EDIDKey, DDC = ddc, Max = promotedBrightnessMax,
                        };
                        // Acquisition is read-only for slider intent: a hardware read may initialize rows
                        // that have no explicit manual/profile value yet, but it must not overwrite a
                        // user-owned slider baseline or enqueue a write through the public Brightness setter.
                        // Snapshot the curve-state flags once and reuse them for both the bus-sync gate
                        // and the SliderState transition below - same call cost, single source of truth.
                        bool curveEngagedAtPromote = IsBrightnessCurveEnabledForHardware();
                        bool inDisabledAtPromote = IsBrightnessCurveDisabledPeriodActive();

                        if (existingInfo is { HasUserBrightness: false, WasCurveDrivenBeforeFailure: false }
                            && !curveEngagedAtPromote)
                            SyncBrightnessReadOnly(existingInfo, Math.Clamp(percent, 0, 100));
                        // Recovery transitions Failed -> the right curve-aware state in ONE PropertyChanged fan-out.
                        // Plumbing the live curve flags here lets the row land directly in CurveActive / CurveSleeping
                        // when curves are engaged, instead of going Enabled first and getting harmonized after by the
                        // curve service's MonitorsRefreshed handler (which fired a second PropertyChanged per row and
                        // produced visible master jitter on cold start).
                        SliderState recoveredState = SliderStateMachine.OnHardwareRecovered(
                            existingInfo.SliderState, curveEngagedAtPromote, inDisabledAtPromote);
                        SetRecoveredSliderState(existingInfo, recoveredState);
                        existingInfo.LastDDCError = null;
                        acquired.Add(existingInfo);
                        WPFLog.Log($"MonitorService: promoted '{ddc.Name}' to DDC/CI-supported");
                    }
                    else
                        existingInfo.LastDDCError = promote.Error;
                }

                continue;
            }

            // New monitor - try DDC/CI; if it answers, normal path;
            // otherwise add as a disabled row that later refreshes can promote.
            (bool Ok, uint Current, uint Max, string? Error) newRead = await TryReadBrightnessWithRetryAsync(ddc);
            if (!IsRefreshProbePhaseCurrent(phaseGen)) return;

            bool supported = newRead.Ok;
            int newPct = supported && newRead.Max > 0
                ? (int)Math.Round(newRead.Current * 100.0 / newRead.Max)
                : 0;
            uint newBrightnessMax = supported ? NormalizeBrightnessMax(newRead.Max) : 100;

            // New rows start from the current DDC read. Saved/profile manual values are restored by
            // BrightnessFlyout as UI state; LastBusBrightness is deliberately not an acquisition source.
            int seededBrightness = Math.Clamp(newPct, 0, 100);

            SliderState initialSliderState = supported
                ? InitialHardwareFunctionalSliderState()
                : SliderState.Failed;

            MonitorInfo info = new()
            {
                ID = id,
                EDIDKey = EDIDKey,
                OriginalName = ddc.FriendlyName,
                EDIDSerial = ddc.EDIDSerial,
                Name = nameOverridesByEDID.TryGetValue(EDIDKey, out string? over) && !string.IsNullOrWhiteSpace(over)
                    ? over
                    : BuildDefaultName(ddc),
                DisplayNumber = ddc.DisplayNumber,
                ArrangementX = ddc.X,
                ArrangementY = ddc.Y,
                LastKnownBrightnessMax = newBrightnessMax,
                IsPoweredOn = true,
                LastDDCError = supported ? null : newRead.Error,
            };
            info.InitializeBrightnessFromHardware(seededBrightness);
            if (initialSliderState is SliderState.CurveActive or SliderState.CurveSleeping)
                info.SeedCurveTargetBrightnessFromSlider();
            info.SliderState = initialSliderState;

            if (supported)
            {
                LogProfileIfMatched(ddc);
                _entries[id] = new MonitorEntry
                {
                    ID = id, EDIDKey = EDIDKey, DDC = ddc, Max = newBrightnessMax,
                };
            }
            else
                WPFLog.Log($"MonitorService: '{ddc.Name}' added as disabled (no DDC/CI response)");

            // Subscribe regardless -
            // OnMonitorPropertyChanged guards on _entries so unsupported monitors no-op safely,
            // and a later promotion doesn't need to re-wire the handler.
            info.PropertyChanged += OnMonitorPropertyChanged;
            Monitors.Add(info);
            if (supported) acquired.Add(info);
        }

        ResortMonitors();

        // Project per-monitor min/max overrides onto each MonitorInfo's allowed-range bounds.
        // Runs after the loop populates Monitors so newly-added entries are covered too.
        // The MinAllowed/MaxAllowed setters reclamp Brightness through the public Brightness setter,
        // which the OnMonitorPropertyChanged subscription (attached above) picks up
        // and pushes to hardware - so a hot-plugged panel sitting outside the override window
        // is squeezed back in without an explicit replay call here.
        ApplyBrightnessBoundOverridesToExisting(replayHardware: false);

        // Same idea for the per-monitor norm curve: project the persisted points into pre-sorted
        // xs/ys arrays on each MonitorEntry so EnqueueDirectBrightness can sample without re-sorting
        // per write. Hot-plugged panels with a saved curve get re-shaped on their first write.
        ApplyNormCurveOverridesToExisting(replayHardware: false);

        // Record "DDC was observed" facts onto KnownDisplays before notifying listeners.
        // The flag is sticky (never cleared) and drives DDCRecoveryService's candidate selection -
        // only monitors whose hardware is known capable get poked indefinitely.
        // Doubles as a one-time backfill for users upgrading from a build without the flag
        // (KnownDisplays already populated, attribute defaults to false -
        // flips to true on first refresh that finds them DDC-up).
        RecordDDCCapableObservations();

        // Project the (now-current) WasEverDDCCapable flags from KnownDisplays onto the live MonitorInfo models
        // so the flyout's warning-state binding (!IsDDCCISupported && WasEverDDCCapable)
        // reflects reality without each row having to look the entry up itself.
        ProjectWasEverDDCCapableToMonitors();

        if (acquired.Count > 0) MonitorsAcquired?.Invoke(acquired);
        MonitorsRefreshed?.Invoke();
    }

    private bool IsRefreshProbePhaseCurrent(int phaseGen) =>
        !_disposed && !_draining && Volatile.Read(ref _refreshGen) == phaseGen;

    private static uint NormalizeBrightnessMax(uint max) => max > 0 ? max : 100;

    private static uint ScaleBrightnessPercentToRaw(int percent, uint max) =>
        (uint)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * NormalizeBrightnessMax(max));

    private Task<(bool Ok, uint Current, uint Max, string? Error)> TryReadBrightnessWithRetryAsync(DDCMonitor ddc) =>
        Task.Run(() =>
        {
            bool ok = TryReadBrightnessWithRetry(ddc, out uint current, out uint max, out string? error);
            return (ok, current, max, error);
        });

    private bool IsBrightnessCurveEnabledForHardware() =>
        IsBrightnessCurveEnabledQuery?.Invoke() ?? _settings.EnvironmentalBrightnessCurveEnabled;

    private bool IsBrightnessCurveDisabledPeriodActive() => IsInDisabledPeriodQuery?.Invoke() == true;

    private SliderState InitialHardwareFunctionalSliderState()
    {
        if (!IsBrightnessCurveEnabledForHardware()) return SliderState.Enabled;
        return IsBrightnessCurveDisabledPeriodActive() ? SliderState.CurveSleeping : SliderState.CurveActive;
    }

    private static void SetRecoveredSliderState(MonitorInfo monitor, SliderState recoveredState)
    {
        if (recoveredState is SliderState.CurveActive or SliderState.CurveSleeping
            && monitor.SliderState is not (SliderState.CurveActive or SliderState.CurveSleeping)
            && !monitor.HasCurveTargetBrightness)
            monitor.SeedCurveTargetBrightnessFromSlider();

        monitor.SliderState = recoveredState;
    }

    private bool ShouldSuppressSliderBrightnessWrite(MonitorInfo monitor)
    {
        if (monitor.IsMaster || monitor.IsNightLight) return false;
        if (!IsBrightnessCurveEnabledForHardware()) return false;
        if (IsBrightnessCurveDisabledPeriodActive()) return false;

        return monitor.SliderState is SliderState.Enabled or SliderState.CurveActive;
    }

    /// <summary>
    /// Copies the sticky <see cref="KnownDisplayEntry.WasEverDDCCapable"/> flag
    /// onto each live <see cref="MonitorInfo"/> by EDIDKey.
    /// Run after every Refresh and after a successful recovery
    /// so the flyout's warning-state binding picks up state changes immediately.
    /// Idempotent - only assigns when the value differs.
    /// </summary>
    private void ProjectWasEverDDCCapableToMonitors()
    {
        IReadOnlyList<KnownDisplayEntry> known = _knownDisplays.Entries;
        foreach (MonitorInfo m in Monitors)
            m.WasEverDDCCapable = IsKnownDDCCapable(m, known);
    }

    /// <summary>
    /// Reorders <see cref="Monitors"/> in place according to the user's saved manual overrides
    /// followed by the configured default sort.
    /// Overrides from the settings menu (<see cref="AppSettings.MonitorOrder"/>)
    /// come first in the order the user arranged them;
    /// any monitors not in that list (e.g. freshly hot-plugged) fall in after,
    /// ordered by the configured default sort mode and direction.
    /// </summary>
    public void ResortMonitors()
    {
        if (_disposed) return;

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(ResortMonitors);
            return;
        }

        if (Monitors.Count < 2) return;

        List<MonitorInfo> desired = ComputeDesiredOrder();

        for (int target = 0; target < desired.Count; target++)
        {
            int current = Monitors.IndexOf(desired[target]);
            if (current >= 0 && current != target) Monitors.Move(current, target);
        }
    }

    private List<MonitorInfo> ComputeDesiredOrder()
    {
        List<MonitorInfo> remaining = [.. Monitors];
        List<MonitorInfo> ordered = [];

        // Pinned overrides first, in the order the user arranged them.
        // The saved order list stores EDIDKey values
        // (always-EDID identity used by the "Display order & overrides" section),
        // independent of the runtime identity strategy.
        foreach (string id in _settings.MonitorOrder)
        {
            MonitorInfo? match = remaining.FirstOrDefault(m => m.EDIDKey == id);
            if (match == null) continue;

            ordered.Add(match);
            remaining.Remove(match);
        }

        // Remaining monitors follow the configured default sort.
        IEnumerable<MonitorInfo> defaultSorted = _settings.DefaultDisplaySortMode switch
        {
            DisplaySortMode.DisplayNumber => remaining
                .OrderBy(m => m.DisplayNumber)
                .ThenBy(m => m.ID, StringComparer.Ordinal),
            _ => remaining
                .OrderBy(m => m.ArrangementX)
                .ThenBy(m => m.ArrangementY)
                .ThenBy(m => m.ID, StringComparer.Ordinal),
        };

        if (_settings.DefaultDisplaySortDirection == DisplaySortDirection.Reversed)
            defaultSorted = defaultSorted.Reverse();

        ordered.AddRange(defaultSorted);
        return ordered;
    }

    private void DetachMonitor(MonitorInfo info)
    {
        info.PropertyChanged -= OnMonitorPropertyChanged;
        if (_entries.TryGetValue(info.ID, out MonitorEntry? _))
        {
            // Drop any queued write for this monitor -
            // its in-flight Task.Run'd SetVCPFeature may still complete (and may fail, which is fine and logged)
            // but no new work will be picked up for this (now-removed) monitor.
            _writeThrottler.Drop(info.ID);
            _entries.TryRemove(info.ID, out _);
        }
    }

    /// <summary>
    /// Logs the per-monitor VCP profile match (if any) when a monitor is added to <see cref="_entries"/>.
    /// The profile fields themselves are populated upstream in <c>DisplayService.TryGetMonitors</c> via
    /// <see cref="DDCMonitorDatabase.ApplyProfile"/>; this method just surfaces the match in the log
    /// once at registration. Silent for the common "no DB entry, falls back to VESA default" path.
    /// </summary>
    private static void LogProfileIfMatched(DDCMonitor ddc)
    {
        if (!ddc.HasKnownProfile) return;
        WPFLog.Log(
            $"MonitorService: matched '{ddc.Name}' to monitor profile {ddc.EDIDIdentifier} "
            + $"'{ddc.ProfileModelName}'"
            + (ddc.ProfileQuirks.Count > 0 ? $" (quirks: {string.Join("; ", ddc.ProfileQuirks)})" : ""));
    }

    private bool TryReadBrightness(DDCMonitor ddc, out uint current, out uint max, out string? error)
    {
        current = 0;
        max = 0;
        error = null;
        (bool ok, uint cur, uint mx, string? readErr) = WithDDCLock(ddc, () =>
        {
            bool callOk =
                _display.TryGetVCPFeature(ddc, ddc.BrightnessCode, out uint c, out uint m, out string? e);
            return (callOk, c, m, e);
        });
        current = cur;
        max = mx;
        if (ok && max > 0) return true;

        error = readErr ?? "Monitor did not respond to DDC/CI (brightness query returned no usable value).";
        return false;
    }

    /// <summary>
    /// Configurable-attempt retry helper for DDC/CI brightness reads.
    /// Each attempt past the first waits one <see cref="AppSettings.ValidationDwellMs"/> before re-reading,
    /// addressing the usual transient failure modes
    /// - mid-OSD, DPMS-wake races, dropped first VCP packet on a busy I2C bus.
    /// The final attempt also refreshes the cached HMONITOR before reading,
    /// catching stale handles left over from resume-from-sleep or topology shuffles
    /// that <see cref="DisplayEventManager"/> didn't pipe through.
    /// Attempt count comes from <see cref="AppSettings.ValidationAttempts"/>;
    /// clamped to at least 1 so a misconfigured setting can't silently disable reads entirely.
    /// </summary>
    private bool TryReadBrightnessWithRetry(DDCMonitor ddc, out uint current, out uint max, out string? error)
    {
        current = 0;
        max = 0;
        error = null;

        int attempts = Math.Max(1, _settings.ValidationAttempts);

        for (int i = 0; i < attempts; i++)
        {
            int waitMs = ReadRetryBackoffMs(i);
            if (waitMs > 0)
            {
                // RefreshProbePhaseAsync calls this via Task.Run so these retry backoffs do not block
                // the dispatcher. Recovery callers already run on a worker thread.
                try { Thread.Sleep(waitMs); }
                catch
                {
                    /* interrupted - fall through to next attempt */
                }
            }

            // Last-attempt escalation: refresh the HMONITOR cache.
            // Cheap (one EnumDisplayMonitors pass) and rescues monitors with stale handles.
            // Skipped when attempts == 1 because the user explicitly opted into a single-shot read with no retries.
            if (i == attempts - 1 && attempts > 1)
            {
                try
                {
                    if (_display.RefreshHandle(ddc))
                    {
                        WPFLog.Log(
                            $"MonitorService: refreshed HMONITOR for '{ddc.Name}' before final read attempt");
                    }
                }
                catch
                {
                    /* swallow; non-fatal - we still try to read below */
                }
            }

            if (TryReadBrightness(ddc, out current, out max, out error)) return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the sleep (ms) to wait BEFORE read attempt <paramref name="attemptIndex"/> (0-based).
    /// Attempt 0 is immediate.
    /// Subsequent attempts pull from <see cref="TimeConstants.MonitorReadRetryBackoffSequenceMs"/>;
    /// indices past the end of the sequence reuse the last value
    /// so a higher ValidationAttempts setting still gets the slowest pacing on extra retries.
    /// </summary>
    private static int ReadRetryBackoffMs(int attemptIndex)
    {
        if (attemptIndex <= 0) return 0;
        int[] seq = TimeConstants.MonitorReadRetryBackoffSequenceMs;
        int seqIndex = Math.Min(attemptIndex - 1, seq.Length - 1);
        return seq[seqIndex];
    }

    /// <summary>
    /// Produces a human-friendly default name.
    /// Prefers the EDID-provided model string (e.g. "LG ULTRAGEAR+"),
    /// then falls back to "Display N" from the OS-assigned index,
    /// then the raw adapter name.
    /// Users can override via Settings -> Monitors.
    /// </summary>
    private static string BuildDefaultName(DDCMonitor ddc)
    {
        if (!string.IsNullOrWhiteSpace(ddc.FriendlyName)) return ddc.FriendlyName;

        if (ddc.DisplayNumber > 0) return $"Display {ddc.DisplayNumber}";

        return string.IsNullOrEmpty(ddc.Name) ? "Display" : ddc.Name;
    }

    /// <summary>
    /// Resolves the <see cref="MonitorInfo.ID"/> string under the configured identity strategy.
    /// The returned value is prefixed with the strategy name (<c>num:</c>, <c>port:</c>, <c>edid:</c>)
    /// so IDs produced by different strategies can never collide -
    /// switching strategy mid-session cleanly removes the old entries and adds fresh ones
    /// rather than re-using keys with drifting semantics.
    ///
    /// Fallback chain when the requested attribute isn't available on a given monitor
    /// (e.g. EDIDSerial on a display that doesn't populate the serial descriptor): HardwarePort -> adapter name.
    /// That way a monitor always has an ID, even if it's not the one the user asked for.
    /// </summary>
    private static string ComputeMonitorID(DDCMonitor ddc, MonitorIdentityStrategy strategy)
    {
        switch (strategy)
        {
            case MonitorIdentityStrategy.EDIDSerial:
                if (!string.IsNullOrEmpty(ddc.EDIDSerial)) return $"edid:{ddc.EDIDSerial}";

                goto case MonitorIdentityStrategy.HardwarePort;

            case MonitorIdentityStrategy.HardwarePort:
                if (!string.IsNullOrEmpty(ddc.DeviceID)) return $"port:{ddc.DeviceID}";

                return string.IsNullOrEmpty(ddc.Name) ? string.Empty : $"port:{ddc.Name}";

            case MonitorIdentityStrategy.DisplayNumber:
            default:
                if (ddc.DisplayNumber > 0) return $"num:{ddc.DisplayNumber}";

                // No display number (shouldn't happen on real hardware) -
                // fall back to the port-style id so profiles still have something to key on.
                goto case MonitorIdentityStrategy.HardwarePort;
        }
    }

    /// <summary>
    /// EDID-first stable identifier used by the "Display order &amp; overrides" settings section.
    /// Equivalent to <see cref="ComputeMonitorID"/> with the EDIDSerial strategy -
    /// kept independent of <see cref="AppSettings.MonitorIdentityStrategy"/>
    /// so per-monitor overrides bound by this key don't get re-bucketed when the user switches strategy.
    /// </summary>
    private static string ComputeEDIDKey(DDCMonitor ddc) =>
        ComputeMonitorID(ddc, MonitorIdentityStrategy.EDIDSerial);

    /// <summary>
    /// Port-form fallback key (always <c>port:</c>-prefixed) regardless of whether EDID is currently
    /// available. Used to detect "same physical panel, EDID arrived between Refreshes" so a cold-start
    /// row keyed under <c>port:</c> can be re-keyed in place to its proper <c>edid:</c> identity
    /// rather than destroyed and recreated. See M-16 / audit_08 F-06.
    /// </summary>
    private static string ComputePortFormKey(DDCMonitor ddc) =>
        ComputeMonitorID(ddc, MonitorIdentityStrategy.HardwarePort);

    /// <summary>
    /// Adds any newly-seen displays to <see cref="KnownDisplaysStore"/>
    /// and refreshes the friendly-name/serial fields for displays already in the list.
    /// Never removes entries - disconnected displays remain
    /// so the settings UI can render them as dimmed rows with their per-monitor overrides intact.
    /// </summary>
    private void RegisterKnownDisplays(IEnumerable<DDCMonitor> live)
    {
        // RegisterMany handles dedupe + name/serial refresh + a single save when anything changed,
        // so the per-Refresh churn no longer touches settings.xml.
        IEnumerable<KnownDisplayEntry> incoming = live
            .Select(ddc => new KnownDisplayEntry
            {
                EDIDKey = ComputeEDIDKey(ddc), OriginalName = ddc.FriendlyName, EDIDSerial = ddc.EDIDSerial,
            })
            .Where(e => !string.IsNullOrEmpty(e.EDIDKey));
        _knownDisplays.RegisterMany(incoming);
    }

    /// <summary>
    /// Walks the current <see cref="Monitors"/> collection
    /// and stamps <see cref="KnownDisplayEntry.WasEverDDCCapable"/> = true
    /// for every monitor currently reporting DDC/CI support.
    /// Idempotent - only persists when at least one entry actually flips.
    /// Runs on the UI thread (called from <see cref="Refresh"/> just before the <see cref="MonitorsRefreshed"/> event).
    /// </summary>
    private void RecordDDCCapableObservations()
    {
        foreach (MonitorInfo m in Monitors)
        {
            if (!m.IsHardwareFunctional) continue;

            if (string.IsNullOrEmpty(m.EDIDKey)) continue;

            // MarkDDCCapable is idempotent and self-saves only on the false->true transition,
            // so the loop is cheap and emits at most one displays.json write per Refresh.
            if (_knownDisplays.MarkDDCCapable(m.EDIDKey))
            {
                WPFLog.Log(
                    $"MonitorService: recorded DDC/CI capability for '{m.Name}' ({m.EDIDKey})");
            }
        }
    }

    /// <summary>
    /// Cold-boot panels (especially the corruption-prone one in this user's setup)
    /// can be slow enough to negotiate DDC and EDID that the constructor's first <see cref="Refresh"/>
    /// catches them mid-handshake: registry EDID isn't populated yet, so EDIDSerial reads empty,
    /// EDIDKey falls back to <c>port:</c>, and <see cref="GetStuckRecoveryCandidateIDs"/>
    /// can't link the live monitor to its persisted <see cref="KnownDisplayEntry"/>.
    /// The recovery service then short-circuits to "no candidates" and stays asleep until something
    /// else triggers a Refresh (flyout open, hot-plug, etc).
    ///
    /// This sweep gives the panels a couple of seconds to catch up,
    /// then re-Refreshes - the second pass reads a populated registry EDID, reconciles the
    /// port-keyed MonitorInfo to its proper edid-keyed identity, and either lands DDC support
    /// directly or qualifies the entry for the DDC fallback worker.
    /// Self-terminates as soon as every <see cref="KnownDisplayEntry.WasEverDDCCapable"/> panel
    /// is currently DDC-supported, so warm-start launches don't pay anything beyond the gate check.
    /// </summary>
    private void ScheduleStartupRecoverySweep()
    {
        WPFLog.Log("MonitorService: startup recovery sweep scheduled");

        _ = Task.Run(async () =>
        {
            foreach (int delayMs in (int[])
                     [TimeConstants.MonitorStartupSweep1stDelayMs, TimeConstants.MonitorStartupSweep2ndDelayMs])
            {
                try { await Task.Delay(delayMs).ConfigureAwait(false); }
                catch { return; }

                if (_disposed || _draining) return;

                if (AllKnownDDCCapableMonitorsAreSupported())
                {
                    WPFLog.Log("MonitorService: startup recovery sweep skipped (all known DDC monitors supported)");
                    return;
                }

                WPFLog.Log($"MonitorService: startup recovery sweep tick (after {delayMs} ms)");
                try { Refresh(); }
                catch (Exception ex)
                {
                    WPFLog.Log($"MonitorService: startup sweep Refresh failed: {ex.Message}");
                }
            }
        });
    }

    /// <summary>
    /// True when every <see cref="KnownDisplayEntry.WasEverDDCCapable"/> entry in
    /// <see cref="KnownDisplaysStore"/> has a matching live <see cref="MonitorInfo"/>
    /// with <see cref="MonitorInfo.IsHardwareFunctional"/> = true.
    /// Marshals to the UI thread to read <see cref="Monitors"/> safely.
    /// </summary>
    private bool AllKnownDDCCapableMonitorsAreSupported()
    {
        HashSet<string> capable = _knownDisplays.Entries
            .Where(k => k.WasEverDDCCapable && !string.IsNullOrEmpty(k.EDIDKey))
            .Select(k => k.EDIDKey)
            .ToHashSet(StringComparer.Ordinal);
        if (capable.Count == 0) return true;

        return InvokeOnDispatcher(Check);

        bool Check()
        {
            if (_disposed) return true;
            HashSet<string> liveSupported = Monitors
                .Where(m => m.IsHardwareFunctional && !string.IsNullOrEmpty(m.EDIDKey))
                .Select(m => m.EDIDKey)
                .ToHashSet(StringComparer.Ordinal);
            return capable.IsSubsetOf(liveSupported);
        }
    }

    /// <summary>
    /// Returns the <see cref="MonitorInfo.ID"/> of every monitor that's a candidate for the DDC fallback worker:
    /// currently DDC-unavailable, last-known powered on,
    /// and whose hardware was previously observed to support DDC/CI
    /// (per <see cref="KnownDisplayEntry.WasEverDDCCapable"/>).
    /// Self-marshals to the UI thread because <see cref="Monitors"/> is mutated there
    /// (the <see cref="KnownDisplaysStore"/> is internally locked, so it's read off-thread safely).
    /// </summary>
    public List<string> GetStuckRecoveryCandidateIDs()
    {
        if (_disposed) return [];

        return InvokeOnDispatcher(Snapshot);

        List<string> Snapshot()
        {
            if (_disposed) return [];

            HashSet<string> capableKeys = _knownDisplays.Entries
                .Where(k => k.WasEverDDCCapable && !string.IsNullOrEmpty(k.EDIDKey))
                .Select(k => k.EDIDKey)
                .ToHashSet(StringComparer.Ordinal);
            HashSet<string> capableSerials = _knownDisplays.Entries
                .Where(k => k.WasEverDDCCapable && !string.IsNullOrEmpty(k.EDIDSerial))
                .Select(k => k.EDIDSerial)
                .ToHashSet(StringComparer.Ordinal);

            List<string> result = [];
            foreach (MonitorInfo m in Monitors)
            {
                // DDC fallback probes any monitor whose read half is failing - includes the asymmetric
                // IsReadDegraded state where writes work but reads don't, so we can detect when reads come
                // back and full-promote via PromoteRecovered.
                if (m is { IsHardwareFunctional: true, IsReadDegraded: false }) continue;

                if (!m.IsPoweredOn) continue;

                bool knownCapable = m.WasEverDDCCapable
                                    || (!string.IsNullOrEmpty(m.EDIDKey)
                                        && capableKeys.Contains(m.EDIDKey))
                                    || (!string.IsNullOrEmpty(m.EDIDSerial)
                                        && capableSerials.Contains(m.EDIDSerial));
                if (!knownCapable) continue;

                result.Add(m.ID);
            }

            return result;
        }
    }

    private static bool IsKnownDDCCapable(MonitorInfo info, IReadOnlyList<KnownDisplayEntry> known)
    {
        if (known.Count == 0) return false;

        if (!string.IsNullOrEmpty(info.EDIDKey)
            && known.Any(k => k.WasEverDDCCapable
                              && string.Equals(k.EDIDKey, info.EDIDKey, StringComparison.Ordinal)))
            return true;

        return !string.IsNullOrEmpty(info.EDIDSerial)
               && known.Any(k => k.WasEverDDCCapable
                                  && !string.IsNullOrEmpty(k.EDIDSerial)
                                  && string.Equals(k.EDIDSerial, info.EDIDSerial, StringComparison.Ordinal));
    }

    /// <summary>
    /// Attempts a single targeted recovery probe on a monitor that is currently reporting DDC unavailable.
    /// Legacy callers should invoke this from off the UI thread:
    /// the candidate snapshot + enumeration runs on the dispatcher,
    /// the DDC I/O runs on the caller's thread,
    /// and the promotion (if any) marshals back to the dispatcher.
    /// The short-circuit cases - already supported, powered off, or a user write in flight -
    /// return without touching the bus.
    /// </summary>
    /// <returns>
    /// True when the monitor is DDC-supported after the call
    /// (whether by a successful recovery or because it was already supported).
    /// False if the recovery action didn't reconnect the monitor.
    /// </returns>
    public bool TryRecoverMonitor(string monitorID, DDCRecoveryAction action)
    {
        if (_disposed || _draining) return false;

        if (string.IsNullOrEmpty(monitorID)) return false;

        // Snapshot live state on the UI thread:
        // the MonitorInfo lookup, _entries contention check, and HMONITOR re-enumeration
        // all touch UI-thread-owned state
        // (ObservableCollection, _entries dictionary, dispatcher-owned _activeStrategy).
        // The DDC I/O itself is then run on the caller's thread.
        MonitorInfo? info = null;
        DDCMonitor? ddc = null;
        bool alreadySupported = false;

        InvokeOnDispatcher(() =>
        {
            if (_disposed) return;

            info = Monitors.FirstOrDefault(m => m.ID == monitorID);
            if (info == null) return;

            // IsReadDegraded monitors are technically "functional" (writes work, slider operable) but
            // still need read-probing so we can fully promote when reads come back. Only short-circuit
            // for monitors that are both functional AND not read-degraded.
            if (info is { IsHardwareFunctional: true, IsReadDegraded: false })
            {
                alreadySupported = true;
                return;
            }

            // Don't poke a monitor we explicitly commanded to sleep -
            // DDC traffic can wake some panels, which would override the user's intent.
            if (!info.IsPoweredOn) return;

            // Defer if a user-initiated brightness write is in flight on this monitor
            // (only happens when an entry already exists, e.g. a previously-supported monitor is mid-recovery).
            // Avoids racing with the throttler-driven write payload.
            if (_entries.TryGetValue(monitorID, out MonitorEntry? _) && _writeThrottler.IsBusy(monitorID))
                return;

            if (!_display.TryGetMonitors(out IReadOnlyList<DDCMonitor> live, out string? enumError))
            {
                WPFLog.Log($"MonitorService.TryRecoverMonitor: enumeration failed: {enumError}");
                return;
            }

            Dictionary<string, MonitorOverrideEntry> monitorOverridesByEDID = BuildMonitorOverrideEntryMap();
            foreach (DDCMonitor liveMonitor in live)
                ApplyBrightnessVcpOverride(liveMonitor, ComputeEDIDKey(liveMonitor), monitorOverridesByEDID);

            ddc = FindRecoveryTarget(live, info, monitorID);
        });

        if (alreadySupported) return true;

        if (info == null || ddc == null) return false;

        // DDC I/O - caller's thread (must not be UI thread).
        switch (action)
        {
            case DDCRecoveryAction.RefreshHandle:
                _display.RefreshHandle(ddc);
                break;
        }

        // Full retry mechanism here: a single failed read isn't strong evidence the read half is broken,
        // it's almost always a transient blip (INVALID_DEVICE / INVALID_MESSAGE_CHECKSUM under bus
        // contention). Only after the configured retry budget (80/160/480 ms backoff + final-attempt
        // RefreshHandle) actually exhausts do we treat the read half as failed and consider promoting
        // to ReadDegraded.
        if (!TryReadBrightnessWithRetry(ddc, out uint current, out uint max, out string? readError) || max == 0)
        {
            // Read failed - probe the write half before declaring full failure. DDC/CI reads and writes
            // are physically different I2C transactions and fail independently: monitors with wedged reply
            // pipelines, marginal cables, or driver bugs in the read ioctl frequently still accept writes.
            // If the write probe lands, the slider is still usable and we surface that asymmetric state
            // via IsReadDegraded rather than locking the row behind the warning glyph.
            string capturedReadError = readError ?? "Monitor did not respond to DDC/CI.";
            bool writeProbeOk = ShouldAttemptReadDegradedWriteProbe(info)
                                && TryDDCWriteProbe(info, ddc, out _);
            if (writeProbeOk)
            {
                DDCMonitor capturedDDCForDegraded = ddc;
                MonitorInfo capturedInfoForDegraded = info;
                string degradedError = capturedReadError;
                InvokeOnDispatcher(() =>
                    PromoteReadDegraded(capturedInfoForDegraded, capturedDDCForDegraded, degradedError));
                return false;
            }

            // Both halves down - surface the read error (more diagnostic than the write probe error,
            // which is almost always the same Win32 code echoed back from the bus).
            // info isn't null here (checked above), but the assignment must marshal to the dispatcher
            // because MonitorInfo property changes drive UI bindings.
            MonitorInfo failedInfo = info;
            _dispatcher.Post(() =>
            {
                if (_disposed) return;
                // Demote to Failed if we were previously in the asymmetric read-degraded state -
                // the write half just died too, so the slider is no longer trustworthy and the
                // warning glyph should appear with the normal locked-row treatment.
                if (failedInfo.IsReadDegraded)
                {
                    if (_entries.TryRemove(failedInfo.ID, out MonitorEntry? droppedEntry))
                        failedInfo.LastKnownBrightnessMax = NormalizeBrightnessMax(droppedEntry.Max);
                    _writeThrottler.Drop(failedInfo.ID);
                    failedInfo.SliderState = SliderStateMachine.OnHardwareFailed();
                    WPFLog.Log(
                        $"MonitorService: '{failedInfo.Name}' demoted from read-degraded to Failed "
                        + "(write probe now also failing)");
                }

                failedInfo.IsReadDegraded = false;
                failedInfo.LastDDCError = capturedReadError;
            });
            return false;
        }

        // Promote on the UI thread -
        // mutating Monitors / _entries / IsDDCCISupported off-thread would race with Refresh and UI bindings.
        DDCMonitor capturedDDC = ddc;
        MonitorInfo capturedInfo = info;
        InvokeOnDispatcher(() => PromoteRecovered(capturedInfo, capturedDDC, current, max));
        return true;
    }

    private DDCMonitor? FindRecoveryTarget(IReadOnlyList<DDCMonitor> live, MonitorInfo info, string requestedID)
    {
        if (live.Count == 0) return null;

        DDCMonitor? match = live.FirstOrDefault(d => ComputeMonitorID(d, _activeStrategy) == requestedID);
        if (match != null) return match;

        // Same stable-identity rescue order as RefreshProbePhaseAsync:
        // EDIDKey, then port-form fallback, then EDID serial. Targeted recovery used to only try
        // requestedID + EDID serial, leaving port-keyed rows permanently failed after EDID/display-number drift.
        if (!string.IsNullOrEmpty(info.EDIDKey))
        {
            match = live.FirstOrDefault(d =>
                string.Equals(ComputeEDIDKey(d), info.EDIDKey, StringComparison.Ordinal)
                || string.Equals(ComputePortFormKey(d), info.EDIDKey, StringComparison.Ordinal));
            if (match != null) return match;
        }

        if (!string.IsNullOrEmpty(info.EDIDSerial))
        {
            match = live.FirstOrDefault(d =>
                !string.IsNullOrEmpty(d.EDIDSerial)
                && string.Equals(d.EDIDSerial, info.EDIDSerial, StringComparison.Ordinal));
            if (match != null) return match;
        }

        if (!string.IsNullOrEmpty(info.ID) && info.ID.StartsWith("port:", StringComparison.Ordinal))
        {
            match = live.FirstOrDefault(d =>
                string.Equals(ComputePortFormKey(d), info.ID, StringComparison.Ordinal));
            if (match != null) return match;
        }

        return null;
    }

    /// <summary>
    /// Sends the row's current manual brightness to verify the monitor's write half is alive when reads fail.
    /// Used to distinguish full DDC failure (both halves dead) from asymmetric read-degraded state
    /// (writes still land - slider remains usable, no warning glyph required).
    /// The probe doubles as the write that restores the row's current manual/profile target.
    /// This is only attempted for manual rows that already have an explicit user/profile value; acquisition
    /// never sends an arbitrary probe brightness and never probes write-side DDC for curve or disabled rows.
    /// Uses <see cref="MonitorInfo.LastKnownBrightnessMax"/> because the Failed -> ReadDegraded transition
    /// removes the MonitorEntry before this runs, and we can't read capabilities while reads are failing.
    /// Goes through WithDDCLock to coordinate with any in-flight user write on the same panel.
    /// </summary>
    private bool TryDDCWriteProbe(MonitorInfo info, DDCMonitor ddc, out string? error)
    {
        uint probeRaw = ScaleBrightnessPercentToRaw(info.RoundedBrightness, info.LastKnownBrightnessMax);

        (bool ok, string? writeErr) = WithDDCLock(ddc, () =>
        {
            bool wrote = _display.TrySetVCPFeature(ddc, ddc.BrightnessCode, probeRaw, out string? e);
            return (wrote, e);
        });
        error = writeErr;
        return ok;
    }

    private bool ShouldAttemptReadDegradedWriteProbe(MonitorInfo info)
    {
        if (!info.HasUserBrightness) return false;
        if (info.WasDisabledBeforeFailure) return false;
        if (IsBrightnessCurveEnabledForHardware() && !IsBrightnessCurveDisabledPeriodActive()) return false;
        return true;
    }

    /// <summary>
    /// UI-thread half of asymmetric recovery: the monitor's write probe landed even though its read
    /// failed, so flip it out of Failed back into the operable state machine and stamp IsReadDegraded
    /// so the flyout shows the informational glyph without locking the slider. Keeps LastDDCError set
    /// because reads are still broken - the DDC fallback worker will keep retrying so that a future read
    /// success can fully promote via <c>PromoteRecovered</c>.
    /// Installs a MonitorEntry with the last successful VCP max when available; a later
    /// PromoteRecovered will overwrite it with a fresh max.
    /// </summary>
    private void PromoteReadDegraded(MonitorInfo info, DDCMonitor ddc, string readError)
    {
        if (_disposed) return;

        // Don't trample a fully-recovered or fully-functional monitor.
        if (info is { IsHardwareFunctional: true, IsReadDegraded: false }) return;

        RefreshRecoveredMonitorMetadata(info, ddc);

        // Install a minimal entry so the throttler / curve service can route writes to this monitor.
        // Reads are still degraded, so reuse the last known max range captured before failure.
        uint brightnessMax = NormalizeBrightnessMax(info.LastKnownBrightnessMax);
        info.LastKnownBrightnessMax = brightnessMax;
        if (!_entries.ContainsKey(info.ID))
        {
            _entries[info.ID] = new MonitorEntry
            {
                ID = info.ID, EDIDKey = info.EDIDKey, DDC = ddc, Max = brightnessMax,
            };
        }

        // Plumb the live curve flags so a read-degraded promotion under an engaged curve lands directly
        // in CurveActive / CurveSleeping rather than Enabled (same H-03 fan-out rationale as PromoteRecovered).
        bool curveEngagedAtPromote = IsBrightnessCurveEnabledForHardware();
        bool inDisabledAtPromote = IsBrightnessCurveDisabledPeriodActive();
        SliderState recoveredState = SliderStateMachine.OnHardwareRecovered(
            info.SliderState, curveEngagedAtPromote, inDisabledAtPromote);
        SetRecoveredSliderState(info, recoveredState);
        info.IsReadDegraded = true;
        info.LastDDCError = readError;
        info.WasEverDDCCapable = true;
        string EDIDKey = ComputeEDIDKey(ddc);
        if (!string.IsNullOrEmpty(EDIDKey)) _knownDisplays.MarkDDCCapable(EDIDKey);
        WPFLog.Log($"MonitorService: '{ddc.Name}' is read-degraded (write probe landed, reads failing)");
        MonitorsAcquired?.Invoke([info]);
        MonitorsRefreshed?.Invoke();
    }

    /// <summary>
    /// Sends the per-monitor "hard power off" VCP write to a stuck monitor identified by EDID serial.
    /// Used by the warning-glyph click in the flyout:
    /// when DDC/CI is wedged, this is the least invasive thing the app can do for the user -
    /// if writes still get through (often they do even when reads fail with checksum errors,
    /// because writes have no reply to corrupt),
    /// the monitor turns itself off and the user can power-cycle it physically.
    /// The (code, value) pair is resolved via <see cref="DDCMonitor.ResolvePowerOff(PowerOffLevel)"/>:
    /// VESA default is 0xD6=0x05; Dell P/U-series monitors override to 0xE1=0x01 (inverted).
    /// Returns false when no live monitor matches the EDID serial or the VCP write itself throws.
    /// </summary>
    public bool TryHardPowerOffByEDIDSerial(string EDIDSerial, out string? error)
    {
        error = null;
        if (_disposed || _draining)
        {
            error = _draining ? "monitor service is draining for shutdown" : "monitor service disposed";
            return false;
        }

        if (string.IsNullOrEmpty(EDIDSerial))
        {
            error = "no EDID serial available for this monitor";
            return false;
        }

        // Live re-enumeration (rather than reusing a cached DDCMonitor)
        // because the warning-glyph click is the canonical "things have shifted, don't trust the cache" trigger -
        // display numbers and HMONITOR handles can have shuffled since the last refresh.
        if (!_display.TryGetMonitors(out IReadOnlyList<DDCMonitor> live, out string? enumError))
        {
            error = $"enumeration failed: {enumError}";
            return false;
        }

        DDCMonitor? target = live.FirstOrDefault(d =>
            !string.IsNullOrEmpty(d.EDIDSerial)
            && string.Equals(d.EDIDSerial, EDIDSerial, StringComparison.Ordinal));

        if (target == null)
        {
            error = $"no live monitor with EDID serial '{EDIDSerial}'";
            return false;
        }

        // Resolve the per-monitor hard-off command. VESA default is 0xD6=0x05 (write-only opcode that
        // turns the monitor off without sending a reply, so it works even on links where DDC reads come back
        // garbled). Dell P/U-series monitors with inverted 0xE1 override this to 0xE1=0x01 - sending the
        // VESA default to those would not turn them off (in fact 0xE1=0 turns them on).
        // Goes through the per-monitor mutex
        // so it can't interleave with a brightness write or recovery probe in flight at the same instant.
        _ = TryResolvePowerOffOverride(target, PowerOffLevel.Hard, out byte powerCode, out byte powerValue);
        (bool ok, string? writeErr) = WithDDCLock(target, () =>
        {
            bool wrote = _display.TrySetVCPFeature(target, powerCode, powerValue, out string? e);
            return (wrote, e);
        });
        if (!ok)
        {
            error = writeErr ?? "TrySetVCPFeature failed";
            return false;
        }

        return true;
    }

    /// <summary>
    /// UI-thread half of recovery:
    /// installs a fresh <see cref="MonitorEntry"/>,
    /// flips <see cref="MonitorInfo.IsHardwareFunctional"/> back on,
    /// seeds the slider with the read-back brightness,
    /// stamps <see cref="KnownDisplayEntry.WasEverDDCCapable"/>,
    /// and raises <see cref="MonitorsRefreshed"/> so the flyout/tray re-evaluate.
    /// </summary>
    private void PromoteRecovered(MonitorInfo info, DDCMonitor ddc, uint current, uint max)
    {
        if (_disposed) return;

        // Another thread (Refresh, an interleaved recovery tick) may have already promoted this monitor -
        // check before clobbering.
        if (info is { IsHardwareFunctional: true, IsReadDegraded: false }) return;

        RefreshRecoveredMonitorMetadata(info, ddc);

        int pct = max == 0 ? 0 : (int)Math.Round(current * 100.0 / max);
        uint brightnessMax = NormalizeBrightnessMax(max);
        info.LastKnownBrightnessMax = brightnessMax;
        LogProfileIfMatched(ddc);
        _entries[info.ID] = new MonitorEntry
        {
            ID = info.ID, EDIDKey = info.EDIDKey, DDC = ddc, Max = brightnessMax,
        };
        // Acquisition is read-only for slider intent: a hardware read may initialize rows that have
        // no explicit manual/profile value yet, but it must not overwrite a user-owned slider baseline
        // or enqueue a write through the public Brightness setter.
        // Snapshot the curve-state flags once and reuse them for both the bus-sync gate
        // and the SliderState transition below - same call cost, single source of truth.
        bool curveEngagedAtPromote = IsBrightnessCurveEnabledForHardware();
        bool inDisabledAtPromote = IsBrightnessCurveDisabledPeriodActive();

        if (info is { HasUserBrightness: false, WasCurveDrivenBeforeFailure: false }
            && !curveEngagedAtPromote)
            SyncBrightnessReadOnly(info, Math.Clamp(pct, 0, 100));
        // Same Failed -> right-curve-state transition the Refresh-promotion path uses, plumbed with the live
        // curve flags so the row lands in one PropertyChanged fan-out instead of two (see Refresh inline block
        // comment for the master-jitter rationale).
        SliderState recoveredState = SliderStateMachine.OnHardwareRecovered(
            info.SliderState, curveEngagedAtPromote, inDisabledAtPromote);
        SetRecoveredSliderState(info, recoveredState);
        info.IsReadDegraded = false;
        info.LastDDCError = null;
        info.WasEverDDCCapable = true;
        WPFLog.Log($"MonitorService: recovered '{ddc.Name}' to DDC/CI-supported");

        // Belt-and-braces - RecordDDCCapableObservations on the next refresh would catch this anyway,
        // but recovery promotion is a canonical "we just saw DDC respond on this hardware" event,
        // so persist eagerly.
        string EDIDKey = ComputeEDIDKey(ddc);
        if (!string.IsNullOrEmpty(EDIDKey)) _knownDisplays.MarkDDCCapable(EDIDKey);

        MonitorsAcquired?.Invoke([info]);
        MonitorsRefreshed?.Invoke();
    }

    private void RefreshRecoveredMonitorMetadata(MonitorInfo info, DDCMonitor ddc)
    {
        string oldID = info.ID;
        string newID = ComputeMonitorID(ddc, _activeStrategy);
        string newEDIDKey = ComputeEDIDKey(ddc);

        bool EDIDUpgraded = info.EDIDKey.StartsWith("port:", StringComparison.Ordinal)
                            && newEDIDKey.StartsWith("edid:", StringComparison.Ordinal);
        bool shouldRekeyID = !string.IsNullOrEmpty(newID)
                             && !string.Equals(oldID, newID, StringComparison.Ordinal)
                             && (_activeStrategy != MonitorIdentityStrategy.DisplayNumber || EDIDUpgraded);

        if (shouldRekeyID)
        {
            if (_entries.TryRemove(oldID, out MonitorEntry? movingEntry))
            {
                movingEntry.ID = newID;
                movingEntry.EDIDKey = newEDIDKey;
                _entries[newID] = movingEntry;
            }

            _writeThrottler.Drop(oldID);
            info.ID = newID;
            WPFLog.Log(
                $"MonitorService: re-keyed recovered '{info.Name}' from "
                + $"{(string.IsNullOrEmpty(oldID) ? "<empty>" : oldID)} -> {newID}");
        }

        info.EDIDKey = newEDIDKey;
        info.OriginalName = ddc.FriendlyName;
        info.EDIDSerial = ddc.EDIDSerial;
        info.DisplayNumber = ddc.DisplayNumber;
        info.ArrangementX = ddc.X;
        info.ArrangementY = ddc.Y;
        info.Name = ResolveDisplayName(info, BuildNameOverrideMap());

        RegisterKnownDisplays([ddc]);
    }

    /// <summary>
    /// Sends a power VCP write to the monitor, resolved through the per-monitor profile.
    /// ON uses the profile's primary power-on command; OFF uses the level chosen by
    /// <see cref="AppSettings.PowerOffMode"/>. The default profile lands at VESA DPMS (0xD6) with
    /// {2=Sleep, 4=Soft, 5=Hard}; Dell P/U-series monitors with inverted 0xE1 override to 0xE1
    /// with {0=On, 1=Off} - so e.g. asking for "Hard" on those still resolves to a single
    /// monitor-correct write.
    /// Updates <see cref="MonitorInfo.IsPoweredOn"/> on success.
    /// </summary>
    public async Task SetPowerStateAsync(MonitorInfo monitor, bool on)
    {
        if (_disposed || _draining) return;

        if (!_entries.TryGetValue(monitor.ID, out MonitorEntry? entry)) return;

        PowerOffLevel offLevel = _settings.PowerOffMode switch
        {
            PowerOffMode.Soft => PowerOffLevel.Soft,
            PowerOffMode.Hard => PowerOffLevel.Hard,
            _ => PowerOffLevel.Sleep,
        };
        (byte code, byte value) = on
            ? entry.DDC.ResolvePowerOn()
            : TryResolvePowerOffOverride(entry.DDC, offLevel, out byte overrideCode, out byte overrideValue)
                ? (overrideCode, overrideValue)
                : entry.DDC.ResolvePowerOff(offLevel);
        WPFLog.Log(
            $"MonitorService: SetPowerState '{entry.DDC.Name}' on={on}; code=0x{code:X2}; value=0x{value:X2}; "
            + $"mode={_settings.PowerOffMode}");
        (bool ok, string? errorMessage) = await WithDDCLockAsync(entry.DDC, () =>
        {
            bool wrote = _display.TrySetVCPFeature(entry.DDC, code, value, out string? e);
            return (wrote, e);
        }).ConfigureAwait(false);
        if (!ok)
        {
            WPFLog.Log($"MonitorService: SetPowerState failed for '{entry.DDC.Name}': {errorMessage}");
            return;
        }

        if (_dispatcher.CheckAccess())
            monitor.IsPoweredOn = on;
        else
            _dispatcher.Post(() => monitor.IsPoweredOn = on);
    }

    private void OnMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MonitorInfo.Brightness)) return;

        // Suppress the slider->hardware DDC write
        // when a caller has wrapped a Brightness assignment in SuspendHardwareWrites -
        // used by paths that need to restore the slider as pure visual state
        // (e.g. on-load manual-value recovery when a curve is engaged) without writing the bus.
        // Out-of-band callers of EnqueueDirectBrightness are unaffected, by design.
        if (Volatile.Read(ref _hardwareWritesSuspendCount) > 0) return;

        if (sender is not MonitorInfo monitor) return;
        if (ShouldSuppressSliderBrightnessWrite(monitor)) return;

        // Auto-release a CurveActive (or CurveSleeping) row whenever an external write reaches us:
        // tray FullDim/FullBright, scroll-wheel / hotkey delta, profile load, or any other path
        // that assigns MonitorInfo.Brightness without SuspendHardwareWrites. The user's intent
        // wins, and CurveReleased prevents the curve's next tick from immediately overwriting it.
        // Mirrors the slider-drag release at BrightnessFlyout.PreviewMouseLeftButtonDown.
        // Master/night-light are excluded: the flyout owns their manual curve-release transitions,
        // and night-light isn't subscribed to OnMonitorPropertyChanged anyway.
        if (monitor is { IsMaster: false, IsNightLight: false })
            monitor.SliderState = SliderStateMachine.OnUserRelease(monitor.SliderState);

        // Bus-value persistence stamp lives in DoBrightnessWriteAsync, not here - LastUserBrightness
        // captures user intent and can diverge from the bus under curve mode (curve writes bypass the
        // setter), so persisting it would record a value the user no longer sees. The bus stamp
        // captures every successful write regardless of source.
        EnqueueDirectBrightness(monitor, monitor.RoundedBrightness);
    }

    // Counter-based so nested SuspendHardwareWrites scopes compose cleanly.
    // See SuspendHardwareWrites for the rationale; OnMonitorPropertyChanged is the only reader.
    private int _hardwareWritesSuspendCount;

    /// <summary>
    /// Suspends the slider->hardware DDC write that <see cref="OnMonitorPropertyChanged"/> would otherwise enqueue
    /// when <see cref="MonitorInfo.Brightness"/> changes, for the lifetime of the returned scope.
    /// Lets callers update <see cref="MonitorInfo.Brightness"/> as pure visual state without touching the bus -
    /// intended for startup paths that restore manual slider values from the saved profile when a curve is engaged
    /// (the curve owns the hardware; the slider owns user intent).
    /// Counter-based, so nested scopes compose; <see cref="EnqueueDirectBrightness"/> writes are NOT suppressed.
    /// </summary>
    public IDisposable SuspendHardwareWrites()
    {
        Interlocked.Increment(ref _hardwareWritesSuspendCount);
        return new HardwareWriteSuspension(this);
    }

    private void SyncBrightnessReadOnly(MonitorInfo monitor, double value)
    {
        using IDisposable _ = SuspendHardwareWrites();
        monitor.SyncBrightnessFromHardware(value);
    }

    private sealed class HardwareWriteSuspension(MonitorService owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Interlocked.Decrement(ref owner._hardwareWritesSuspendCount);
        }
    }

    /// <summary>
    /// Public alternative to the slider-driven write path.
    /// Queues a brightness write to <paramref name="monitor"/>'s DDC channel
    /// without going through <see cref="MonitorInfo.Brightness"/>'s setter,
    /// so the slider thumb stays at the user's last manual position while the bus moves to <paramref name="percent"/>.
    /// Used by the runtime curve evaluator: the curve owns the hardware,
    /// the slider owns the user's intent,
    /// and the indicator glyph owns the visual cue connecting the two.
    /// Subject to the same per-monitor cooldown and queue-collapse the slider path uses,
    /// so curve drags and slider drags put identical pressure on the bus.
    /// </summary>
    public void EnqueueDirectBrightness(MonitorInfo? monitor, int percent)
    {
        if (_disposed || _draining) return;
        if (monitor == null) return;
        if (!_entries.TryGetValue(monitor.ID, out MonitorEntry? entry)) return;

        // Apply the per-monitor norm curve first: the slider stays on the linear 0..100 range
        // and the curve reshapes which hardware brightness each slider position maps to.
        // No-op when no curve is set (xs/ys are null). Lives ahead of the floor/ceiling clamp so
        // a curve that targets values outside the cap window still respects the user's cap below.
        int shaped = ApplyNormCurve(entry, percent);

        // Clamp first to the absolute 0..100 envelope, then to the per-monitor override window.
        // The slider itself stays on the normalised 0-100 range; this is the single boundary where
        // the per-monitor floor/ceiling actually constrain hardware. Every write path flows through
        // here (slider drag, master propagation, curve writes, topology replay), so the cap is enforced
        // uniformly without the slider, profile, or curve code having to know about it.
        int floor = entry.FloorPercent;
        int ceiling = entry.CeilingPercent;
        if (floor > ceiling) floor = ceiling;
        int pct = Math.Clamp(Math.Clamp(shaped, 0, 100), floor, ceiling);

        // Skip duplicate enqueues.
        // The throttler already collapses bursts queued during a write,
        // but doesn't dedupe across completed writes
        // - so a curve sample that holds the same integer pct across many ticks would re-write the bus every tick.
        // Skipping here drops those redundant payloads at the source,
        // which is also where closure allocations happen.
        // Topology paths that need to force a fresh write reset entry.LastEnqueuedPercentage first.
        if (Interlocked.Exchange(ref entry.LastEnqueuedPercentage, pct) == pct) return;

        // Schedule a payload that closes over (entry, pct). The throttler does latest-pending-wins:
        // a flurry of EnqueueDirectBrightness calls during the cooldown collapse to a single payload
        // running with the freshest pct.
        // After the payload completes the throttler observes _writeCooldownMs
        // before letting the next queued payload run,
        // mirroring the pre-throttler hand-rolled write loop's "write -> wait -> verify -> loop" pacing.
        _ = _writeThrottler.RunAsync(entry.ID, ctx => DoBrightnessWriteAsync(entry, pct, ctx));
    }

    /// <summary>
    /// Re-pushes every DDC-supported monitor's current slider position to the bus.
    /// Used after a display-topology change (hot-plug, resume, session unlock)
    /// where the OS hands us back the same panels but their brightness has been reset by the replug -
    /// without this, the slider stays put while the panel is at its factory/last-flash level.
    /// Goes through the same per-monitor throttler the slider drag uses,
    /// so it composes naturally with any user input that arrives during or shortly after.
    /// </summary>
    public void ReapplySliderState()
    {
        if (_disposed || _draining) return;

        int count = 0;
        foreach (MonitorInfo m in Monitors)
        {
            if (!m.IsHardwareFunctional) continue;
            // Curve-owned rows must not get the slider value replayed here. This guard also covers
            // startup before the flyout-owned curve service has harmonized freshly-added rows into
            // CurveActive; the persisted brightness-curve setting is enough to keep manual replay off
            // until the curve evaluator applies its direct-write target.
            if (ShouldSuppressSliderBrightnessWrite(m)) continue;
            // Topology change just landed - the bus value is unknown / wrong,
            // regardless of what EnqueueDirectBrightness last sent.
            // Clear the dedupe sentinel so the upcoming write isn't skipped on a same-pct match.
            if (_entries.TryGetValue(m.ID, out MonitorEntry? entry))
                Volatile.Write(ref entry.LastEnqueuedPercentage, -1);
            EnqueueDirectBrightness(m, m.RoundedBrightness);
            count++;
        }

        WPFLog.Log($"MonitorService.ReapplySliderState: re-pushed {count} entries");
    }

    /// <summary>
    /// Throttler payload for one brightness target.
    /// Performs write+retry, then a verify read-back when the drag has settled
    /// (i.e. when the throttler hasn't queued a replacement during the write).
    /// Uses <see cref="ThrottlerContext.HasReplacement"/> to bail early during dwell waits
    /// - preserves the pre-throttler write loop's "don't keep verifying a now-stale value" behaviour
    /// even though the underlying mechanism (queued payload vs <c>entry.Pending</c> flag) is different.
    /// </summary>
    private async Task DoBrightnessWriteAsync(MonitorEntry entry, int pct, ThrottlerContext ctx)
    {
        if (_disposed || _draining) return;

        uint raw = ScaleBrightnessPercentToRaw(pct, entry.Max);

        // Retry transient write failures (most commonly the I2C-transmit-error class of Win32Exception,
        // which the bus throws at us when a packet collides or the monitor is mid-OSD / mid-DPMS-wake).
        // Uses ValidationAttempts as the cap; inter-retry waits are scaled -
        // short for the first few retries (covers fast transients without slider sluggishness)
        // and the full ValidationDwellMs on the final attempt
        // (gives a slow monitor real settle time before we give up).
        // Bails out early if a newer payload was queued during this attempt:
        // retrying a now-stale value just hammers the bus.
        int writeAttempts = Math.Max(1, _settings.ValidationAttempts);
        int writeFinalDwellMs = Math.Max(0, _validationDwellMs);
        string? lastWriteError = null;
        bool wrote = false;
        for (int attempt = 0; attempt < writeAttempts; attempt++)
        {
            int waitMs = ScaledRetryDwellMs(attempt, writeAttempts, writeFinalDwellMs);
            if (waitMs > 0)
            {
                if (_disposed || _draining || ctx.HasReplacement)
                {
                    lastWriteError = null;
                    break;
                }

                try { await Task.Delay(waitMs, ctx.CancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }

            (bool ok, string? writeErr) = await WithDDCLockAsync(entry.DDC, () =>
            {
                bool w = _display.TrySetVCPFeature(entry.DDC, entry.DDC.BrightnessCode, raw, out string? e);
                return (w, e);
            }).ConfigureAwait(false);

            if (ok)
            {
                wrote = true;
                lastWriteError = null;
                // Persist the value that just landed on the bus so a future destroy/recreate of the
                // MonitorInfo (full enumeration drop on power-cycle, cable disconnect) restores the
                // user's visible state rather than whatever the panel happens to report at re-enum.
                // Captures every successful write regardless of who drove it - slider drag, master
                // propagation, profile load, curve service. Debounced inside the store.
                if (!string.IsNullOrEmpty(entry.EDIDKey))
                    _knownDisplays.StampLastBusBrightness(entry.EDIDKey, pct);
                break;
            }

            lastWriteError = writeErr;
            WPFLog.Log(
                $"MonitorService: SetVCPFeature attempt {attempt + 1}/{writeAttempts} failed for "
                + $"'{entry.DDC.Name}': {writeErr}");
        }

        if (lastWriteError != null)
        {
            // Cascade guard: if a replacement payload is already queued, defer the demote.
            // A fast-input pile-up that exhausts retries isn't reliable signal that the DDC link is broken -
            // it usually just means the bus needs a beat to clear.
            // The next throttler iteration will write the freshest value, which almost always succeeds.
            // Only demote when retries are exhausted AND no fresher payload is queued.
            if (ctx.HasReplacement)
            {
                WPFLog.Log(
                    $"MonitorService: write retries exhausted for '{entry.DDC.Name}' "
                    + "but a fresher payload is queued; deferring demote");
                return;
            }

            DemoteOnDDCFailure(entry, lastWriteError);
            return;
        }

        // wrote==false here means we bailed for a queued replacement; let the throttler run that next.
        if (!wrote) return;

        // Only verify once the drag has settled.
        // If the throttler has a replacement queued, the next payload will overwrite this value
        // and any verification result would be stale - skip it.
        if (_disposed || ctx.HasReplacement) return;

        await VerifyAppliedAsync(entry, raw, ctx).ConfigureAwait(false);
    }

    /// <summary>
    /// Read-back verification with re-apply on mismatch.
    /// Loops up to <see cref="AppSettings.ValidationAttempts"/> times:
    /// each iteration reads the brightness VCP,
    /// returns on a match (within +/-1 raw unit to absorb monitor-side quantization),
    /// otherwise re-writes the target and waits a scaled dwell before the next attempt.
    /// The dwell ramps from short (catches the common "monitor was busy for a moment" case fast)
    /// up to <see cref="AppSettings.ValidationDwellMs"/> on the final attempt
    /// (gives a slow monitor real settle time before we declare the link unresponsive).
    /// HMONITOR is refreshed once on the first mismatch as a defence against stale handles.
    /// Bails immediately when the throttler has queued a replacement -
    /// whatever we'd verify is about to be superseded by the next payload.
    /// </summary>
    private async Task VerifyAppliedAsync(MonitorEntry entry, uint expectedRaw, ThrottlerContext ctx)
    {
        const long Tolerance = 1;
        int attempts = Math.Max(1, _settings.ValidationAttempts);
        int finalDwellMs = Math.Max(0, _validationDwellMs);

        if (IsReadDegraded(entry.ID))
        {
            WPFLog.Log(
                $"MonitorService: skipping read-back verification for read-degraded '{entry.DDC.Name}'");
            return;
        }

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (_disposed || _draining || ctx.HasReplacement) return;

            (bool read, uint actual, string? readErr) = await WithDDCLockAsync(entry.DDC, () =>
            {
                bool ok = _display.TryGetVCPFeature(
                    entry.DDC, entry.DDC.BrightnessCode, out uint a, out _, out string? e);
                return (ok, a, e);
            }).ConfigureAwait(false);

            switch (read)
            {
                case false:
                    WPFLog.Log($"MonitorService: verify read failed for '{entry.DDC.Name}': {readErr}");
                    break;
                case true when Math.Abs((long)actual - expectedRaw) <= Tolerance:
                    return;
            }

            // Last attempt: don't bother re-applying or settling - we're about to demote.
            if (attempt == attempts - 1) break;

            // First failure only: refresh the cached HMONITOR before re-applying.
            // Catches stale handles that survived a topology change the primary pipeline missed;
            // cheap and only worth doing once since the second cause of mismatches (slow monitor) doesn't need it.
            if (attempt == 0 && _display.RefreshHandle(entry.DDC))
                WPFLog.Log($"MonitorService: refreshed HMONITOR for '{entry.DDC.Name}' mid-verify");

            // Re-apply, then wait the scaled dwell before the next read attempt.
            (bool reApplied, string? reApplyErr) = await WithDDCLockAsync(entry.DDC, () =>
            {
                bool w = _display.TrySetVCPFeature(
                    entry.DDC, entry.DDC.BrightnessCode, expectedRaw, out string? e);
                return (w, e);
            }).ConfigureAwait(false);
            if (!reApplied) WPFLog.Log($"MonitorService: re-apply failed for '{entry.DDC.Name}': {reApplyErr}");

            // Wait for the NEXT attempt (attempt+1).
            // +1 because the helper's "wait before this attempt" semantic gives 0 for index 0;
            // we're computing the wait between this mismatched attempt and the next one.
            int waitMs = ScaledRetryDwellMs(attempt + 1, attempts, finalDwellMs);
            if (waitMs > 0)
            {
                try { await Task.Delay(waitMs, ctx.CancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        // Cascade guard: same logic as the write-retry exhaustion path.
        // If the throttler has a replacement queued, the verify mismatch is likely just bus lag from rapid input
        // rather than a real link failure.
        // Defer the demote and let the next payload supersede this one -
        // verify will get another shot when the user pauses.
        if (ctx.HasReplacement)
        {
            WPFLog.Log(
                $"MonitorService: verify exhausted for '{entry.DDC.Name}' "
                + "but a fresher payload is queued; deferring demote");
            return;
        }

        if (IsReadDegraded(entry.ID))
        {
            WPFLog.Log(
                $"MonitorService: verify exhausted for read-degraded '{entry.DDC.Name}', "
                + "keeping write-capable state");
            return;
        }

        WPFLog.Log(
            $"MonitorService: verification exhausted for '{entry.DDC.Name}' - target raw={expectedRaw}");
        DemoteOnDDCFailure(entry, "Brightness write was not acknowledged after retry - DDC/CI link is unresponsive.");
    }

    private bool IsReadDegraded(string monitorID)
    {
        if (string.IsNullOrEmpty(monitorID)) return false;
        return InvokeOnDispatcher(Snapshot);

        bool Snapshot()
        {
            MonitorInfo? info = Monitors.FirstOrDefault(m => m.ID == monitorID);
            return info?.IsReadDegraded == true;
        }
    }

    /// <summary>
    /// Mid-session DDC failure handler.
    /// Flips the live <see cref="MonitorInfo"/> to the warning state
    /// (<see cref="MonitorInfo.IsHardwareFunctional"/> = false, <see cref="MonitorInfo.LastDDCError"/> populated)
    /// and removes the entry from <see cref="_entries"/>,
    /// mirroring how a never-responsive monitor looks at enumeration time.
    /// Once flipped, the existing flyout warning triggers fire
    /// and <see cref="DDCRecoveryService"/> picks the monitor up as a candidate for its event-triggered fallback worker.
    /// Safe to call from any thread - marshals all state mutations through the dispatcher.
    /// Idempotent because <c>MonitorInfo</c>'s setters short-circuit no-op assignments.
    /// </summary>
    private void DemoteOnDDCFailure(MonitorEntry entry, string error)
    {
        if (_disposed) return;

        string id = entry.ID;
        if (string.IsNullOrEmpty(id)) return;

        // Drop any queued writes for this monitor; the in-flight payload that's calling us
        // will return naturally after we record the demote.
        _writeThrottler.Drop(id);

        void Apply()
        {
            if (_disposed) return;

            // The entry might have been replaced (recovery promote) since we queued -
            // only remove if it's still the same instance.
            if (_entries.TryGetValue(id, out MonitorEntry? current) && ReferenceEquals(current, entry))
                ((ICollection<KeyValuePair<string, MonitorEntry>>)_entries).Remove(KeyValuePair.Create(id, current));

            MonitorInfo? info = Monitors.FirstOrDefault(m => m.ID == id);
            if (info == null) return;

            // Already demoted by another path (e.g. concurrent verify exhaustion racing with a write throw) -
            // don't clobber a fresher error message.
            if (!info.IsHardwareFunctional && !string.IsNullOrEmpty(info.LastDDCError)) return;

            info.SliderState = SliderStateMachine.OnHardwareFailed();
            info.LastDDCError = error;
            WPFLog.Log($"MonitorService: demoted '{entry.DDC.Name}' to DDC/CI-unavailable ({error})");

            // Wake the DDC fallback worker now instead of waiting for another topology/settings event -
            // mirrors what a Refresh-driven add does so the UI feedback is synchronous with the failure.
            MonitorsRefreshed?.Invoke();
        }

        if (_dispatcher.CheckAccess())
            Apply();
        else
            _dispatcher.Post(Apply);
    }

    private void InvokeOnDispatcher(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }

    private T InvokeOnDispatcher<T>(Func<T> action)
    {
        if (_dispatcher.CheckAccess()) return action();
        return _dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _draining = true;

        _settings.Changed -= OnSettingsChanged;

        foreach (MonitorInfo m in Monitors)
            m.PropertyChanged -= OnMonitorPropertyChanged;

        // Flush any pending debounced brightness / offset stamps so a last-moment slider drag
        // doesn't get lost on shutdown, and dispose the timer (H-15). Dispose internally flushes
        // and stops the debounce timer so the System.Threading.Timer is not leaked.
        try { _knownDisplays.Dispose(); }
        catch
        {
            /* best-effort during shutdown */
        }

        // Tear down the throttler - cancels any in-flight payload at its next dwell-await
        // and rejects further enqueues. In-flight DDC ops still finish naturally on their threadpool thread.
        try { _writeThrottler.Dispose(); }
        catch
        {
            /* best-effort during shutdown */
        }

        // Release the per-monitor mutexes. Anything still holding one is in-flight;
        // SemaphoreSlim doesn't track owner so we can't preempt,
        // but the per-op timeout caps how long it'll run.
        lock (_ddcLocksGate)
        {
            foreach (SemaphoreSlim sem in _ddcLocks.Values)
            {
                try { sem.Dispose(); }
                catch
                {
                    /* best-effort during shutdown */
                }
            }

            _ddcLocks.Clear();
        }
    }

    /// <summary>
    /// Draining handshake the rest of the app uses on shutdown.
    /// Sets the <c>_draining</c> flag so every public entry-point bails on new work,
    /// then polls <see cref="_activeDDCOps"/> until it hits zero or <paramref name="timeout"/> elapses.
    /// Returns true on clean drain, false on timeout
    /// (caller should still proceed with shutdown - Layer 1's per-op timeout caps total stuck time).
    ///
    /// Idempotent: calling this multiple times is safe.
    /// Doesn't dispose anything; <see cref="Dispose"/> is the actual teardown step
    /// and should be called after a successful drain.
    /// </summary>
    public async Task<bool> BeginDrainAsync(TimeSpan timeout)
    {
        _draining = true;
        DateTime deadline = DateTime.UtcNow + timeout;

        // Drain the throttler first so its driver loops stop scheduling new work,
        // then wait for any DDC ops they kicked off to finish releasing their physical-monitor handles.
        TimeSpan throttlerBudget = deadline - DateTime.UtcNow;
        if (throttlerBudget > TimeSpan.Zero)
        {
            using CancellationTokenSource cts = new(throttlerBudget);
            try { await _writeThrottler.DrainAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                /* fall through to op-count drain below */
            }
        }

        while (Volatile.Read(ref _activeDDCOps) > 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                WPFLog.Log(
                    $"MonitorService.BeginDrainAsync: timed out with {_activeDDCOps} DDC op(s) still in flight");
                return false;
            }

            await Task.Delay(TimeConstants.DrainPollIntervalMs).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Computes the dwell time to wait BEFORE attempt index <paramref name="attemptIndex"/> (0-based).
    /// Used by the write and verify retry loops - reads have their own explicit sequence in
    /// <see cref="ReadRetryBackoffMs"/>.
    /// Attempt 0 has no wait.
    /// Subsequent attempts ramp from 25ms exponentially - 25, 50, 100, 200... -
    /// capped at half the final dwell so the ramp never exceeds the "give up" wait.
    /// The final attempt always uses the full <paramref name="finalDwellMs"/>,
    /// giving a genuinely slow monitor real settle time as the last-resort try.
    ///
    /// Result for attempts=4, finalDwellMs=500: waits before attempts 1..3 are 25ms, 50ms, 500ms.
    /// Total worst-case retry budget = 575ms, with most transient I2C blips clearing inside the first 25ms retry.
    /// Compare to flat-dwell-everywhere (1500ms worst case) which made the slider feel sluggish on every transient.
    /// </summary>
    private static int ScaledRetryDwellMs(int attemptIndex, int totalAttempts, int finalDwellMs)
    {
        if (attemptIndex <= 0) return 0;
        if (attemptIndex >= totalAttempts - 1) return finalDwellMs;

        // Write-path retry base: cheap exponential ramp 25, 50, 100, 200...
        int ramped = TimeConstants.MonitorWriteRetryBaseMs << (attemptIndex - 1);
        int cap = Math.Max(TimeConstants.MonitorWriteRetryBaseMs, finalDwellMs / 2);
        return Math.Min(ramped, cap);
    }

    /// <summary>
    /// Returns the per-monitor <see cref="SemaphoreSlim"/> used to serialise DDC I/O on a given physical panel.
    /// Keyed by <see cref="DDCMonitor.DeviceID"/> when present (stable per port),
    /// falling back to the adapter <see cref="DDCMonitor.Name"/> for monitors that didn't resolve a DeviceID.
    /// Created on first access - entries persist for the lifetime of the service.
    /// </summary>
    private SemaphoreSlim GetDDCLock(DDCMonitor monitor)
    {
        string key = string.IsNullOrEmpty(monitor.DeviceID) ? monitor.Name : monitor.DeviceID;
        lock (_ddcLocksGate)
        {
            if (!_ddcLocks.TryGetValue(key, out SemaphoreSlim? ddcSemaphore))
            {
                ddcSemaphore = new SemaphoreSlim(1, 1);
                _ddcLocks[key] = ddcSemaphore;
            }

            return ddcSemaphore;
        }
    }

    /// <summary>
    /// Synchronously serialises a DDC func against the monitor's per-panel mutex.
    /// Use from non-async paths (UI-thread Refresh, sync helpers);
    /// for async paths (write loop, verify loop) use <see cref="WithDDCLockAsync{T}"/>
    /// so the await machinery isn't blocked on the wait.
    /// </summary>
    private T WithDDCLock<T>(DDCMonitor monitor, Func<T> func)
    {
        SemaphoreSlim sem = GetDDCLock(monitor);
        sem.Wait();
        Interlocked.Increment(ref _activeDDCOps);
        try { return func(); }
        finally
        {
            Interlocked.Decrement(ref _activeDDCOps);
            sem.Release();
        }
    }

    /// <summary>
    /// Async variant of <see cref="WithDDCLock{T}"/>.
    /// The func itself is sync (Layer 1's RunWithTimeout uses Task.Run + sync Wait internally),
    /// so we explicitly dispatch it via <see cref="Task.Run(Action)"/> here too.
    /// Without that extra hop, an uncontended <c>sem.WaitAsync()</c> can complete inline,
    /// which means the func then runs on the original calling thread -
    /// and if that's the UI thread (true on the kick path from <c>OnMonitorPropertyChanged</c>),
    /// the inner Wait blocks the UI for the whole dxva2 round-trip and the slider feels stuck.
    /// The double Task.Run is cheap (microseconds of dispatch) and guarantees we always yield the calling thread.
    /// </summary>
    private async Task<T> WithDDCLockAsync<T>(DDCMonitor monitor, Func<T> func)
    {
        SemaphoreSlim ddcSemaphore = GetDDCLock(monitor);
        await ddcSemaphore.WaitAsync().ConfigureAwait(false);
        Interlocked.Increment(ref _activeDDCOps);
        try
        {
            return await Task.Run(func).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeDDCOps);
            ddcSemaphore.Release();
        }
    }

    private sealed class MonitorEntry
    {
        public string ID = string.Empty;

        // EDID-first stable identifier (mirrors MonitorInfo.EDIDKey). Cached on the entry so
        // DoBrightnessWriteAsync can stamp KnownDisplaysStore.LastBusBrightness after a successful
        // write without a Monitors collection scan. Empty when the monitor has no EDID serial.
        public string EDIDKey = string.Empty;
        public DDCMonitor DDC = null!;

        public uint Max;

        // Last pct value EnqueueDirectBrightness queued for this entry. -1 means "never enqueued."
        // Used to short-circuit duplicate writes when a flat-ish curve sample lands on the same
        // integer pct as the previous tick - the throttler collapses bursts but doesn't dedupe
        // across completed writes, so without this the env curve sweep can re-write the same
        // value 200 times in 10 seconds. Reset by paths that need to force a fresh write
        // (e.g. ReapplySliderState after a topology change where the bus value is unknown).
        public int LastEnqueuedPercentage = -1;

        // Per-monitor brightness floor/ceiling, projected from AppSettings.MonitorOverrides
        // (MinBrightness / MaxBrightness, keyed by EDIDKey).
        // EnqueueDirectBrightness clamps every payload to [FloorPercent, CeilingPercent] so hardware never
        // sees a value outside the override window - regardless of which path produced it
        // (slider drag, master propagation, curve write, profile apply, replay).
        // The slider itself stays on the normalised 0-100 range; the cap is purely a bus-boundary concern.
        public int FloorPercent;
        public int CeilingPercent = 100;

        // Per-monitor brightness norm curve, projected from AppSettings.MonitorOverrides
        // (NormCurvePoints, keyed by EDIDKey) and pre-sorted by X.
        // Null when no curve is configured - EnqueueDirectBrightness short-circuits the sample
        // call in that case and the slider acts as a 1:1 passthrough.
        // Stored as one atomically swapped reference so background hardware enqueue reads never see
        // mismatched X/Y arrays while settings are applying a new projection on the dispatcher.
        public NormCurveProjection? NormCurve;
    }

    private sealed class NormCurveProjection(double[] xs, double[] ys)
    {
        public readonly double[] Xs = xs;
        public readonly double[] Ys = ys;
    }

    /// <summary>
    /// Maps <paramref name="percent"/> through <paramref name="entry"/>'s per-monitor norm curve.
    /// Returns the input unchanged when no curve is configured (no allocations on the hot path).
    /// Uses linear interpolation to match the editor's default render mode (smoothness = 0);
    /// the cubic Hermite blend stays available on the sampler for a future smoothness setting
    /// but is not exercised here.
    /// </summary>
    private static int ApplyNormCurve(MonitorEntry entry, int percent)
    {
        NormCurveProjection? normCurve = Volatile.Read(ref entry.NormCurve);
        if (normCurve?.Xs is not { Length: >= 2 } xs || normCurve.Ys is not { Length: >= 2 } ys) return percent;

        double y = EnvironmentalCurveSampler.InterpolateLinear(xs, ys, percent);
        return (int)Math.Round(Math.Clamp(y, 0.0, 100.0));
    }
}
