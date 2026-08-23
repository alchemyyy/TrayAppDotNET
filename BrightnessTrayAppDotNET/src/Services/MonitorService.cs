using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.Utils;
using TrayAppDotNETCommon.Services;

namespace BrightnessTrayAppDotNET.Services;

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
/// one, so rapid slider drags never back up an unbounded queue. Target generations prevent an obsolete write from
/// winning, and a target is complete only after matching readback.
/// </summary>
public sealed class MonitorService : IDisposable
{
    private const string RefreshSchedulerKey = "monitor-refresh";

    private readonly IDisplayService _display;
    private readonly AppSettings _settings;
    private readonly KnownDisplaysStore _knownDisplays;
    private readonly IMonitorServiceDispatcher _dispatcher;

    private readonly ConcurrentDictionary<string, MonitorEntry> _entries = new(StringComparer.Ordinal);
    // Failed rows retain only immutable matching data, not handles or transport state. This lets targeted recovery
    // find an HDMI display whose display number or DeviceID drifted while the link was renegotiating.
    private readonly ConcurrentDictionary<string, DDCRecoveryIdentity> _recoveryIdentities = new(StringComparer.Ordinal);

    // Per-monitor latest-pending-wins scheduler.
    // Owns the cooldown between brightness writes; the payloads it runs hold the per-monitor DDC mutex (the lock is
    // for bus atomicity vs other DDC ops, the throttler is for pacing - different concerns).
    private readonly AsyncThrottler<string> _writeThrottler;
    // Mode handoffs must bypass the normal cooldown without running untracked fire-and-forget tasks.
    // A separate zero-cooldown driver gives shutdown a drainable owner; target generations below
    // arbitrate between the normal and immediate drivers.
    private readonly AsyncThrottler<string> _immediateWriteThrottler;
    // Full monitor enumeration touches CCD, the registry, and WMI. Keep it off the dispatcher and collapse topology
    // bursts to the latest request before the UI-owned reconcile phase.
    private readonly AsyncThrottler<string> _refreshThrottler;
    private int _writeCooldownMs;
    private int _validationDwellMs;
    private MonitorIdentityStrategy _activeStrategy;
    private bool _disposed;

    // Per-monitor DDC mutex registry.
    // Every dxva2 call against a given physical monitor goes through WithDDCLock(...) keyed by DeviceID so a recovery
    // probe and a slider-driven write can't interleave on the bus.
    // DisplayService runs timed dxva2 calls in a killable helper process per monitor; this lock keeps app-level
    // operations serialized before they cross that monitor-specific process boundary.
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
    // Incremented for every applied refresh snapshot before Phase B is started or scheduled; async probe
    // continuations capture the generation and bail after a newer snapshot has been applied.
    // Without this, two Refreshes within the post-detection settle window (1.5 s) would stack two
    // deferred Phase Bs running on stale captured snapshots - producing duplicate add/probe work and
    // visible churn on the flyout's CollectionChanged path.
    // ScheduleStartupRecoverySweep's +2s/+5s Refreshes go through Refresh() so they participate naturally -
    // the latest scheduled Phase B wins.
    private int _refreshGen;
    // Public refresh generation. Worker enumeration results must still own this generation when posted to the
    // dispatcher, otherwise a newer topology/settings request superseded their snapshot.
    private long _refreshEnumerationGeneration;

    // Wall-clock of the last topology event reported by the caller (via NotifyTopologyEvent).
    // Phase B uses (now - this) to decide whether the monitor MCU still needs a post-arrival
    // settle window. Cold-start RefreshInitial from the ctor leaves this at MinValue so Phase B starts
    // immediately - the monitors have been connected since boot and don't need a settle.
    // WM_DEVICECHANGE-driven Refresh from DisplayEventManager sets this to UtcNow before calling
    // Refresh, so Phase B defers for the remaining settle window. Event-driven gating, no
    // unconditional 1.5 s delay on the user's startup path.
    private DateTime _lastTopologyEventUtc = DateTime.MinValue;

    // A topology reset or brightness-VCP change invalidates the last acknowledged hardware value.
    // The generation is captured by Refresh and consumed only after its probe phase has installed fresh
    // monitor handles and VCP maxima. This avoids replaying through stale handles while still allowing
    // the same percentage to be written again after hardware reset.
    private long _brightnessReplayGeneration;
    private long _lastCompletedBrightnessReplayGeneration;

    /// <summary>
    /// Raised after <see cref="Refresh"/> finishes applying add/remove/handle-refresh mutations.
    /// Always fires on the UI thread.
    /// </summary>
    public event Action? MonitorsRefreshed;

    /// <summary>
    /// Raised synchronously after a known DDC row enters Failed or read-degraded state.
    /// This is the direct wake-up path for <see cref="DDCRecoveryService"/>; generic refresh notification remains
    /// separate so recovery cannot be skipped by unrelated refresh subscribers or candidate-transition timing.
    /// </summary>
    public event Action<string>? DDCRecoveryRequested;

    /// <summary>
    /// Optional caller-supplied predicate: returns true when the brightness environmental curve is
    /// currently engaged. Used by physical brightness acquisition/recovery so hardware reads do not
    /// overwrite curve-owned slider intent. Null query -> false.
    /// </summary>
    public Func<bool>? IsBrightnessCurveEnabledQuery { get; set; }

    /// <summary>
    /// Optional caller-supplied predicate: returns true when the environmental curve's
    /// disabled-period window is currently passing through. Plumbed into
    /// <see cref="MonitorInfo.ResolveHardwareRecoveredSliderState"/> on every promote path so a recovered row
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
        _immediateWriteThrottler = new AsyncThrottler<string>(0, StringComparer.Ordinal);
        _refreshThrottler = new AsyncThrottler<string>(0, StringComparer.Ordinal);

        // Re-sort the monitor list whenever the sort settings or manual override change.
        _settings.Changed += OnSettingsChanged;

        RefreshInitial();

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

        // Turning blind writes off must invalidate work that was queued while the option was still enabled. A native
        // SET already in progress cannot be recalled, but no pending or verification-stage target may continue.
        if (!_settings.AllowBlindDDCWritesDuringDegradedState)
        {
            foreach (MonitorInfo degradedMonitor in Monitors.Where(static monitor => monitor.IsReadDegraded))
            {
                if (!_entries.TryGetValue(degradedMonitor.ID, out MonitorEntry? degradedEntry)) continue;
                InvalidateBrightnessTarget(degradedEntry);
                DropQueuedBrightnessWrites(degradedMonitor.ID);
            }
        }

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
            RequestBrightnessReplayAfterRefresh();
            Refresh();
            return;
        }

        ApplyDDCTimingOverridesToExisting();
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
            DDCMonitor currentDDC = Volatile.Read(ref entry.DDC);
            DDCMonitor updatedDDC = CloneDDCMonitor(currentDDC);
            byte before = updatedDDC.BrightnessCode;
            updatedDDC.BrightnessCode = VCPConstants.Brightness;
            DDCMonitorDatabase.ApplyProfile(updatedDDC);
            ApplyBrightnessVcpOverride(updatedDDC, entry.EDIDKey, map);
            if (updatedDDC.BrightnessCode == before) continue;

            changed = true;
            Volatile.Write(ref entry.DDC, updatedDDC);
            InvalidateBrightnessTarget(entry);
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
    /// When a bound actually changes for a monitor, the entry's previous target acknowledgement is invalidated
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

    /// <summary>
    /// Projects per-monitor DDC timing overrides onto live entries. Negative values inherit the global setting.
    /// </summary>
    private void ApplyDDCTimingOverridesToExisting()
    {
        Dictionary<string, MonitorOverrideEntry> map = BuildMonitorOverrideEntryMap();
        foreach (MonitorEntry entry in _entries.Values)
        {
            int validationDwellMs = -1;
            int brightnessDwellMs = -1;
            if (!string.IsNullOrEmpty(entry.EDIDKey)
                && map.TryGetValue(entry.EDIDKey, out MonitorOverrideEntry? monitorOverride))
            {
                validationDwellMs = monitorOverride.ValidationDwellMs;
                brightnessDwellMs = monitorOverride.BrightnessDwellMs;
            }

            Volatile.Write(
                ref entry.ValidationDwellMs,
                Math.Clamp(validationDwellMs, -1, TimeConstants.ValidationDwellMaxMs));
            Volatile.Write(
                ref entry.BrightnessDwellMs,
                Math.Clamp(brightnessDwellMs, -1, TimeConstants.BrightnessUpdateRateMaxMs));
        }
    }

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
    /// When the resolved curve actually changes for a monitor, the entry's previous target acknowledgement
    /// is invalidated and a fresh write of the current slider position is queued -
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
    /// On a real curve change, invalidates the old target acknowledgement and re-pushes the current slider position
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

        // The old acknowledgement described a different percent-to-raw transform.
        InvalidateBrightnessTarget(entry);

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
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves floor/ceiling for one monitor from the override map (defaults 0/100 when no override
    /// applies) and writes them onto the matching <see cref="MonitorEntry"/>.
    /// Skips monitors that don't have a live entry (currently DDC-unsupported / Failed) - their cap
    /// will be re-applied the next time they promote.
    /// On a real bound change, invalidates the old target acknowledgement and re-pushes the current slider position
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

        // The old acknowledgement described a different percent-to-raw transform.
        InvalidateBrightnessTarget(entry);

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
    public void NotifyTopologyEvent()
    {
        _lastTopologyEventUtc = DateTime.UtcNow;
        RequestBrightnessReplayAfterRefresh();
    }

    private void RequestBrightnessReplayAfterRefresh() =>
        Interlocked.Increment(ref _brightnessReplayGeneration);

    /// <summary>
    /// Re-enumerates physical monitors and reconciles the <see cref="Monitors"/> collection with the current hardware
    /// topology:
    /// <list type="bullet">
    /// <item>Still-present monitors keep their <see cref="MonitorInfo"/> while the underlying DDC snapshot is replaced
    /// atomically with fresh handles and capabilities.</item>
    /// <item>Newly-connected monitors get a fresh <see cref="MonitorInfo"/> appended and their hardware brightness is
    /// sampled to seed the slider.</item>
    /// <item>Detached monitors are removed from the collection; their write loop drains and exits on its next
    /// cooldown tick.</item>
    /// </list>
    /// Safe to call from any thread. Enumeration runs on a latest-request-wins worker; only the resulting detached
    /// snapshot is marshalled onto the UI dispatcher for <see cref="ObservableCollection{T}"/> reconciliation.
    /// </summary>
    public void Refresh()
    {
        if (_disposed || _draining) return;

        long enumerationGeneration = Interlocked.Increment(ref _refreshEnumerationGeneration);
        _ = _refreshThrottler.RunAsync(
            RefreshSchedulerKey,
            context => RunRefreshEnumerationAsync(enumerationGeneration, context));
    }

    private Task RunRefreshEnumerationAsync(long enumerationGeneration, ThrottlerContext context)
    {
        if (_disposed || _draining || context.CancellationToken.IsCancellationRequested)
            return Task.CompletedTask;

        if (!_display.TryGetMonitors(out IReadOnlyList<DDCMonitor> enumeratedRo, out string? enumError))
        {
            WPFLog.Log($"MonitorService.Refresh: enumeration failed: {enumError}");
            return Task.CompletedTask;
        }

        List<DDCMonitor> enumerated = [.. enumeratedRo];
        if (_disposed
            || _draining
            || context.CancellationToken.IsCancellationRequested
            || enumerationGeneration != Volatile.Read(ref _refreshEnumerationGeneration))
            return Task.CompletedTask;

        _dispatcher.Post(() =>
        {
            if (_disposed
                || _draining
                || enumerationGeneration != Volatile.Read(ref _refreshEnumerationGeneration))
                return;

            ApplyRefreshSnapshot(enumerated);
        });
        return Task.CompletedTask;
    }

    private void RefreshInitial()
    {
        long enumerationGeneration = Interlocked.Increment(ref _refreshEnumerationGeneration);
        if (!_display.TryGetMonitors(out IReadOnlyList<DDCMonitor> enumerated, out string? enumError))
        {
            WPFLog.Log($"MonitorService.RefreshInitial: enumeration failed: {enumError}");
            return;
        }

        if (_disposed || _draining || enumerationGeneration != Volatile.Read(ref _refreshEnumerationGeneration))
            return;

        ApplyRefreshSnapshot(enumerated);
    }

    private void ApplyRefreshSnapshot(IReadOnlyList<DDCMonitor> enumeratedRo)
    {
        if (_disposed || _draining) return;

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
                    RememberRecoveryIdentity(existing.ID, Volatile.Read(ref droppedEntry.DDC));
                    InvalidateBrightnessTarget(droppedEntry);
                    existing.LastKnownBrightnessMax = NormalizeBrightnessMax(droppedEntry.Max);
                    // In-flight write payload owns the (now-stale) DDC handle and will release cleanly;
                    // queued writes can't usefully target a missing panel, drop them.
                    DropQueuedBrightnessWrites(existing.ID);
                }

                WPFLog.Log(
                    $"MonitorService: '{existing.Name}' dropped from enumeration; parking as Failed "
                    + $"(EDIDKey={existing.EDIDKey})");
                RecordDDCCapableObservation(existing);
                DDCRecoveryRequested?.Invoke(existing.ID);
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
        long capturedBrightnessReplayGeneration = Volatile.Read(ref _brightnessReplayGeneration);
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
                scheduledGen,
                capturedBrightnessReplayGeneration);
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
                    scheduledGen,
                    capturedBrightnessReplayGeneration);
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
        int phaseGen,
        long brightnessReplayGeneration)
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
                    if (oldID != id
                        && _recoveryIdentities.TryRemove(oldID, out DDCRecoveryIdentity movingRecoveryIdentity))
                        _recoveryIdentities[id] = movingRecoveryIdentity;

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
                existingInfo.SupportsPowerControl = ddc.SupportsVcpPower;
                existingInfo.Name =
                    nameOverridesByEDID.TryGetValue(EDIDKey, out string? existingOverride)
                    && !string.IsNullOrWhiteSpace(existingOverride)
                        ? existingOverride
                        : BuildDefaultName(ddc);

                if (_entries.TryGetValue(existingInfo.ID, out MonitorEntry? entry))
                {
                    // Already supported - atomically install the fresh DDC snapshot, then re-probe to catch monitors whose DDC
                    // link died while the app wasn't writing to them (no SetVCPFeature failure to trigger demotion).
                    // Without this re-probe, a monitor that silently dropped DDC stays stuck IsDDCCISupported=true
                    // forever and the warning UI / DDC fallback worker never fire.
                    // Never mutate a DDCMonitor that an in-flight helper command may be reading. Reference replacement
                    // lets that command finish against its coherent old identity while subsequent operations use this one.
                    entry.EDIDKey = EDIDKey;
                    Volatile.Write(ref entry.DDC, ddc);

                    // Use the full retry mechanism (80/160/480 backoff + final-attempt RefreshHandle) so
                    // a single transient read failure (INVALID_DEVICE / INVALID_MESSAGE_CHECKSUM) doesn't
                    // demote a healthy monitor and produce a ~1-2s warning-glyph blink before the DDC
                    // fallback probes it back. Single-shot reads here were responsible for the curve-toggle
                    // and topology-event flicker observed in the field.
                    (bool Ok, uint Current, uint Max, string? Error) probe = await TryReadBrightnessWithRetryAsync(
                        ddc,
                        () => IsRefreshProbePhaseCurrent(phaseGen));
                    if (!IsRefreshProbePhaseCurrent(phaseGen)) return;

                    if (probe.Ok)
                    {
                        bool recoveredReadDegraded = existingInfo.IsReadDegraded;
                        _recoveryIdentities.TryRemove(existingInfo.ID, out DDCRecoveryIdentity _);
                        entry.Max = NormalizeBrightnessMax(probe.Max);
                        existingInfo.LastKnownBrightnessMax = entry.Max;
                        if (!existingInfo.HasUserBrightness
                            && !existingInfo.WasCurveDrivenBeforeFailure
                            && !IsBrightnessCurveEnabledForHardware())
                        {
                            int hardwarePercent = (int)Math.Round(probe.Current * 100.0 / entry.Max);
                            SyncBrightnessReadOnly(existingInfo, Math.Clamp(hardwarePercent, 0, 100));
                        }

                        RecordDDCCapableObservation(existingInfo);
                        existingInfo.IsReadDegraded = false;
                        existingInfo.LastDDCError = null;
                        if (recoveredReadDegraded)
                        {
                            PublishRecoveredPowerAvailability(existingInfo, ddc.Name);
                            acquired.Add(existingInfo);
                        }
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
                            {
                                RememberRecoveryIdentity(existingInfo.ID, Volatile.Read(ref failedEntry.DDC));
                                InvalidateBrightnessTarget(failedEntry);
                                existingInfo.LastKnownBrightnessMax = NormalizeBrightnessMax(failedEntry.Max);
                            }
                            // Drop any queued write for this monitor - a fresh value applied to a now-demoted entry would
                            // only generate a doomed retry. An in-flight payload is left to drain on its own (it
                            // captured the entry's DDC handle and will release cleanly).
                            DropQueuedBrightnessWrites(existingInfo.ID);
                            WPFLog.Log(
                                $"MonitorService: demoted '{ddc.Name}' during Refresh re-probe ({probe.Error})");
                            RecordDDCCapableObservation(existingInfo);
                            DDCRecoveryRequested?.Invoke(existingInfo.ID);
                        }
                    }
                }
                else
                {
                    // Previously unsupported - attempt promotion with fresh handles
                    (bool Ok, uint Current, uint Max, string? Error) promote =
                        await TryReadBrightnessWithRetryAsync(ddc, () => IsRefreshProbePhaseCurrent(phaseGen));
                    if (!IsRefreshProbePhaseCurrent(phaseGen)) return;

                    if (promote.Ok)
                    {
                        _recoveryIdentities.TryRemove(existingInfo.ID, out DDCRecoveryIdentity _);
                        int percent = promote.Max == 0
                            ? 0
                            : (int)Math.Round(promote.Current * 100.0 / promote.Max);
                        uint promotedBrightnessMax = NormalizeBrightnessMax(promote.Max);
                        existingInfo.LastKnownBrightnessMax = promotedBrightnessMax;
                        LogProfileIfMatched(ddc);
                        _entries[existingInfo.ID] = new MonitorEntry
                        {
                            ID = existingInfo.ID, EDIDKey = EDIDKey, DDC = ddc, Max = promotedBrightnessMax
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
                        SliderState recoveredState = existingInfo.ResolveHardwareRecoveredSliderState(
                            curveEngagedAtPromote, inDisabledAtPromote);
                        // Publish recovery eligibility before the functional state. Candidate snapshots may run
                        // concurrently under non-Avalonia dispatchers and must never observe a capable row without
                        // its sticky capability bit.
                        RecordDDCCapableObservation(existingInfo);
                        SetRecoveredSliderState(existingInfo, recoveredState);
                        PublishRecoveredPowerAvailability(existingInfo, ddc.Name);
                        existingInfo.LastDDCError = null;
                        acquired.Add(existingInfo);
                        WPFLog.Log($"MonitorService: promoted '{ddc.Name}' to DDC/CI-supported");
                    }
                    else
                    {
                        RememberRecoveryIdentity(existingInfo.ID, ddc);
                        existingInfo.LastDDCError = promote.Error;
                        if (existingInfo.WasEverDDCCapable)
                            DDCRecoveryRequested?.Invoke(existingInfo.ID);
                    }
                }

                continue;
            }

            // New monitor - try DDC/CI; if it answers, normal path;
            // otherwise add as a disabled row that later refreshes can promote.
            (bool supported, uint current, uint max, string? error) = await TryReadBrightnessWithRetryAsync(
                ddc,
                () => IsRefreshProbePhaseCurrent(phaseGen));
            if (!IsRefreshProbePhaseCurrent(phaseGen)) return;

            int newPct = supported && max > 0
                ? (int)Math.Round(current * 100.0 / max)
                : 0;
            uint newBrightnessMax = supported ? NormalizeBrightnessMax(max) : 100;

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
                SupportsPowerControl = ddc.SupportsVcpPower,
                IsPoweredOn = true,
                // A successful probe is the authoritative capability observation. Set the runtime sticky bit
                // before Monitors.Add publishes this row; displays.json persistence happens below.
                WasEverDDCCapable = supported,
                LastDDCError = supported ? null : error
            };
            info.InitializeBrightnessFromHardware(seededBrightness);
            if (initialSliderState is SliderState.CurveActive or SliderState.CurveSleeping)
                info.SeedCurveTargetBrightnessFromSlider();
            if (supported)
                RecordDDCCapableObservation(info);
            info.SliderState = initialSliderState;

            if (supported)
            {
                _recoveryIdentities.TryRemove(id, out DDCRecoveryIdentity _);
                LogProfileIfMatched(ddc);
                _entries[id] = new MonitorEntry
                {
                    ID = id, EDIDKey = EDIDKey, DDC = ddc, Max = newBrightnessMax
                };
            }
            else
            {
                RememberRecoveryIdentity(id, ddc);
                WPFLog.Log(
                    $"MonitorService: '{ddc.Name}' added as disabled (no DDC/CI response: "
                    + $"{error ?? "unknown error"})");
            }

            // Subscribe regardless -
            // OnMonitorPropertyChanged guards on _entries so unsupported monitors no-op safely,
            // and a later promotion doesn't need to re-wire the handler.
            info.PropertyChanged += OnMonitorPropertyChanged;
            Monitors.Add(info);
            if (supported) acquired.Add(info);
        }

        ResortMonitors();

        // Project per-monitor min/max overrides onto the bus-boundary clamp for every live entry.
        // Runs after the loop populates Monitors so newly-added entries are covered too. Acquisition stays
        // read-only here; an explicit manual replay or curve evaluation applies the projection when required.
        ApplyDDCTimingOverridesToExisting();
        ApplyBrightnessBoundOverridesToExisting(replayHardware: false);

        // Same idea for the per-monitor norm curve: project the persisted points into pre-sorted
        // xs/ys arrays on each MonitorEntry so EnqueueDirectBrightness can sample without re-sorting
        // per write. Hot-plugged panels with a saved curve get re-shaped on their first write.
        ApplyNormCurveOverridesToExisting(replayHardware: false);

        ReplayBrightnessTargetsAfterRefresh(brightnessReplayGeneration);

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

        MonitorsRefreshed?.Invoke();
        // Curve reconciliation is synchronous. Resolve and force the final ownership target only after subscribers
        // have moved a recovered row into CurveActive, CurveSleeping, or CurveReleased.
        foreach (MonitorInfo acquiredMonitor in acquired)
            ReplayRecoveredBrightnessIntent(acquiredMonitor);
    }

    private bool IsRefreshProbePhaseCurrent(int phaseGen) =>
        !_disposed && !_draining && Volatile.Read(ref _refreshGen) == phaseGen;

    private static uint NormalizeBrightnessMax(uint max) => max > 0 ? max : 100;

    private static uint ScaleBrightnessPercentToRaw(int percent, uint max) =>
        (uint)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * NormalizeBrightnessMax(max));

    private static DDCMonitor CloneDDCMonitor(DDCMonitor source) =>
        new()
        {
            Handle = source.Handle,
            HDC = source.HDC,
            Name = source.Name,
            DeviceID = source.DeviceID,
            DisplayInstancePath = source.DisplayInstancePath,
            DisplayNumber = source.DisplayNumber,
            EDIDSerial = source.EDIDSerial,
            FriendlyName = source.FriendlyName,
            EDIDManufacturerID = source.EDIDManufacturerID,
            EDIDProductCode = source.EDIDProductCode,
            X = source.X,
            Y = source.Y,
            BrightnessControlKind = source.BrightnessControlKind,
            WindowsBrightnessInstanceName = source.WindowsBrightnessInstanceName,
            WindowsBrightnessMethodPath = source.WindowsBrightnessMethodPath,
            BrightnessCode = source.BrightnessCode,
            ProfileModelName = source.ProfileModelName,
            PowerOffCommands = source.PowerOffCommands,
            ProfileQuirks = source.ProfileQuirks
        };

    private void RememberRecoveryIdentity(string monitorID, DDCMonitor ddc)
    {
        if (string.IsNullOrEmpty(monitorID)) return;

        _recoveryIdentities[monitorID] = new DDCRecoveryIdentity(
            ddc.DeviceID,
            ddc.DisplayInstancePath,
            ddc.EDIDSerial,
            ddc.Name);
    }

    private Task<(bool Ok, uint Current, uint Max, string? Error)> TryReadBrightnessWithRetryAsync(
        DDCMonitor ddc,
        Func<bool>? shouldContinue = null) =>
        Task.Run(() =>
        {
            bool ok = TryReadBrightnessWithRetry(
                ddc,
                out uint current,
                out uint max,
                out string? error,
                shouldContinue);
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
        {
            // A failed or delayed persistence write must never erase a capability observed in this process.
            // "Was ever" is monotonic; projection can promote false to true but cannot demote true to false.
            if (!m.WasEverDDCCapable && IsKnownDDCCapable(m, known))
                m.WasEverDDCCapable = true;
        }
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
                .ThenBy(m => m.ID, StringComparer.Ordinal)
        };

        if (_settings.DefaultDisplaySortDirection == DisplaySortDirection.Reversed)
            defaultSorted = defaultSorted.Reverse();

        ordered.AddRange(defaultSorted);
        return ordered;
    }

    private void DetachMonitor(MonitorInfo info)
    {
        info.PropertyChanged -= OnMonitorPropertyChanged;
        _recoveryIdentities.TryRemove(info.ID, out DDCRecoveryIdentity _);
        if (_entries.TryRemove(info.ID, out MonitorEntry? entry))
        {
            InvalidateBrightnessTarget(entry);
            // Drop any queued write for this monitor -
            // its in-flight SetVCPFeature may still complete (and may fail, which is fine and logged)
            // but no new work will be picked up for this (now-removed) monitor.
            DropQueuedBrightnessWrites(info.ID);
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
            if (!callOk) _display.ResetDDCTransport(ddc);
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
    /// Attempts after the first use the fixed responsive backoff sequence in <see cref="TimeConstants"/>,
    /// addressing the usual transient failure modes
    /// - mid-OSD, DPMS-wake races, dropped first VCP packet on a busy I2C bus.
    /// The final attempt also refreshes the cached HMONITOR before reading,
    /// catching stale handles left over from resume-from-sleep or topology shuffles
    /// that <see cref="DisplayEventManager"/> didn't pipe through.
    /// Attempt count comes from <see cref="AppSettings.ValidationAttempts"/>;
    /// clamped to at least 1 so a misconfigured setting can't silently disable reads entirely.
    /// </summary>
    private bool TryReadBrightnessWithRetry(
        DDCMonitor ddc,
        out uint current,
        out uint max,
        out string? error,
        Func<bool>? shouldContinue = null)
    {
        current = 0;
        max = 0;
        error = null;

        int attempts = Math.Max(1, _settings.ValidationAttempts);

        for (int i = 0; i < attempts; i++)
        {
            if (shouldContinue?.Invoke() == false)
            {
                error = "Brightness read retry was superseded.";
                return false;
            }

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

            if (shouldContinue?.Invoke() == false)
            {
                error = "Brightness read retry was superseded.";
                return false;
            }

            // Last-attempt escalation: refresh the HMONITOR cache.
            // Cheap (one EnumDisplayMonitors pass) and rescues monitors with stale handles.
            // Skipped when attempts == 1 because the user explicitly opted into a single-shot read with no retries.
            if (i == attempts - 1 && attempts > 1)
            {
                try
                {
                    bool refreshed = WithDDCLock(ddc, () => RefreshHandlePreservingBrightnessCode(ddc));
                    if (refreshed)
                    {
                        WPFLog.Log(
                            $"MonitorService: refreshed HMONITOR for '{ddc.Name}' before final read attempt");
                    }
                }
                catch (Exception ex)
                {
                    WPFLog.Log(
                        $"MonitorService: HMONITOR refresh failed for '{ddc.Name}' before final read: {ex.Message}");
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
                EDIDKey = ComputeEDIDKey(ddc), OriginalName = ddc.FriendlyName, EDIDSerial = ddc.EDIDSerial
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
            RecordDDCCapableObservation(m);
        }
    }

    private void RecordDDCCapableObservation(MonitorInfo monitor)
    {
        // This is a monotonic fact. Publish it before any functional-state transition and persist it at the
        // successful probe point so a concurrent demotion cannot make terminal refresh bookkeeping skip it.
        monitor.WasEverDDCCapable = true;
        if (string.IsNullOrEmpty(monitor.EDIDKey)) return;

        // MarkDDCCapable is idempotent and saves only on the false-to-true transition.
        if (_knownDisplays.MarkDDCCapable(monitor.EDIDKey))
        {
            WPFLog.Log(
                $"MonitorService: recorded DDC/CI capability for '{monitor.Name}' ({monitor.EDIDKey})");
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
    /// currently DDC-unavailable, not explicitly powered off during this application lifetime,
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
                // IsReadDegraded compatibility state where write transport was accepted but application could not
                // be confirmed, so we can detect when reads come back and full-promote via PromoteRecovered.
                if (m is { IsHardwareFunctional: true, IsReadDegraded: false }) continue;

                // IsPoweredOn is optimistic and profile-restored, so it can remain false after an external wake.
                // Only a power-off command accepted during this application lifetime is authoritative enough to
                // suppress recovery traffic.
                if (m.SuppressDDCRecoveryForPowerIntent) continue;

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
    /// Call from off the UI thread: only the small candidate snapshot runs on the dispatcher;
    /// enumeration and DDC I/O run on the caller's thread,
    /// and the promotion (if any) marshals back to the dispatcher.
    /// The short-circuit cases - already supported, powered off, or a user write in flight -
    /// return without touching the bus.
    /// </summary>
    /// <returns>
    /// True when the monitor is DDC-supported after the call
    /// (whether by a successful recovery or because it was already supported).
    /// False if the targeted probe did not restore readable DDC state.
    /// </returns>
    public bool TryRecoverMonitor(string monitorID)
    {
        if (_disposed || _draining) return false;

        if (string.IsNullOrEmpty(monitorID)) return false;

        // Snapshot only UI-owned state on the dispatcher. Monitor enumeration includes registry and WMI work and
        // must remain on the recovery caller's worker thread rather than blocking the Avalonia dispatcher.
        MonitorInfo? info = null;
        bool alreadySupported = false;
        bool canProbe = false;
        bool wasReadDegraded = false;
        bool shouldAttemptWriteTransportProbe = false;
        bool mayAttemptChecksumRecoveryWrite = false;
        bool mayAttemptBlindRecoveryWrite = false;
        int writeTransportProbePercent = 0;
        int blindRecoveryWritePercent = 0;
        uint lastKnownBrightnessMax = 100;
        string EDIDKey = string.Empty;
        string EDIDSerial = string.Empty;
        DDCRecoveryIdentity? recoveryIdentity = null;
        MonitorIdentityStrategy identityStrategy = MonitorIdentityStrategy.DisplayNumber;
        Dictionary<string, MonitorOverrideEntry> monitorOverridesByEDID = new(StringComparer.Ordinal);

        InvokeOnDispatcher(() =>
        {
            if (_disposed) return;

            info = Monitors.FirstOrDefault(m => m.ID == monitorID);
            if (info == null) return;

            // IsReadDegraded monitors are technically "functional" (best-effort slider operable) but still need
            // read-probing so we can fully promote when reads come back. Only short-circuit
            // for monitors that are both functional AND not read-degraded.
            if (info is { IsHardwareFunctional: true, IsReadDegraded: false })
            {
                alreadySupported = true;
                return;
            }

            // Don't poke a monitor we explicitly commanded to sleep during this application lifetime. Persisted
            // IsPoweredOn state is not used here because it can be stale after an external wake.
            if (info.SuppressDDCRecoveryForPowerIntent) return;

            // Defer if a user-initiated brightness write is in flight on this monitor
            // (only happens when an entry already exists, e.g. a previously-supported monitor is mid-recovery).
            // Avoids racing with the throttler-driven write payload.
            if (_entries.TryGetValue(monitorID, out MonitorEntry? _) && IsBrightnessWriteBusy(monitorID))
                return;

            EDIDKey = info.EDIDKey;
            EDIDSerial = info.EDIDSerial;
            if (_recoveryIdentities.TryGetValue(monitorID, out DDCRecoveryIdentity rememberedIdentity))
                recoveryIdentity = rememberedIdentity;
            wasReadDegraded = info.IsReadDegraded;
            KnownDisplayEntry? knownDisplay = string.IsNullOrEmpty(info.EDIDKey)
                ? null
                : _knownDisplays.Find(info.EDIDKey);
            // A transport-only probe must be minimally invasive. Require the last read-back-confirmed bus value,
            // which already includes norm/bounds projection; never fall back to an unprojected slider percentage.
            int? lastConfirmedBusBrightness = knownDisplay?.LastBusBrightness;
            writeTransportProbePercent = lastConfirmedBusBrightness ?? 0;
            blindRecoveryWritePercent = Math.Clamp(info.RecoveryProbeBrightness, 0, 100);
            lastKnownBrightnessMax = info.LastKnownBrightnessMax;
            shouldAttemptWriteTransportProbe = lastConfirmedBusBrightness.HasValue
                                               && ShouldAttemptReadDegradedWriteTransportProbe(info);
            // A previously confirmed bus value is also a safe checksum-resynchronization SET. Unlike the generic
            // read-degraded probe, this is allowed for curve-owned rows because a successful confirming GET promotes
            // the row and immediately replays the current curve target. User-disabled rows remain untouched.
            mayAttemptChecksumRecoveryWrite = lastConfirmedBusBrightness.HasValue
                                              && !info.WasDisabledBeforeFailure;
            // This is the explicit opt-in escape hatch for displays whose GET path is permanently broken while SET
            // remains usable. Restrict it to previously proven DDC rows and preserve user-disabled intent.
            mayAttemptBlindRecoveryWrite = _settings.AllowBlindDDCWritesDuringDegradedState
                                           && info.WasEverDDCCapable
                                           && !info.WasDisabledBeforeFailure;
            identityStrategy = _activeStrategy;
            monitorOverridesByEDID = BuildMonitorOverrideEntryMap();
            canProbe = true;
        });

        if (alreadySupported) return true;

        if (info == null || !canProbe) return false;

        if (!_display.TryGetMonitors(out IReadOnlyList<DDCMonitor> live, out string? enumError))
        {
            WPFLog.Log($"MonitorService.TryRecoverMonitor: enumeration failed: {enumError}");
            return false;
        }

        foreach (DDCMonitor liveMonitor in live)
            ApplyBrightnessVcpOverride(liveMonitor, ComputeEDIDKey(liveMonitor), monitorOverridesByEDID);

        DDCMonitor? ddc = FindRecoveryTarget(
            live,
            monitorID,
            EDIDKey,
            EDIDSerial,
            recoveryIdentity,
            identityStrategy);
        if (ddc == null) return false;

        // Full retry mechanism here: a single failed read isn't strong evidence the read half is broken,
        // it's almost always a transient blip (INVALID_DEVICE / INVALID_MESSAGE_CHECKSUM under bus
        // contention). Only after the configured retry budget (80/160/480 ms backoff + final-attempt
        // RefreshHandle) actually exhausts do we treat the read half as failed and consider promoting
        // to ReadDegraded.
        if (!TryReadBrightnessWithRetry(ddc, out uint current, out uint max, out string? readError) || max == 0)
        {
            string capturedReadError = readError ?? "Monitor did not respond to DDC/CI.";
            if (wasReadDegraded)
            {
                MonitorInfo degradedInfo = info;
                _dispatcher.Post(() =>
                {
                    if (!_disposed && Monitors.Contains(degradedInfo) && degradedInfo.IsReadDegraded)
                        degradedInfo.LastDDCError = capturedReadError;
                });
                return false;
            }

            // Read failed - probe write transport before declaring full failure. DDC/CI reads and writes
            // are physically different I2C transactions and fail independently: monitors with wedged reply
            // pipelines, marginal cables, or driver bugs in the read ioctl frequently still accept writes.
            // If the write transport is accepted, retain the explicit best-effort compatibility state rather than
            // claiming application; the immediate post-write read below is the only route to confirmation.
            bool isChecksumRecovery = DDCNativeError.IsInvalidMessageChecksum(capturedReadError);
            bool shouldUseConfirmedProbe = shouldAttemptWriteTransportProbe
                                           || (isChecksumRecovery && mayAttemptChecksumRecoveryWrite);
            bool shouldUseBlindProbe = !shouldUseConfirmedProbe && mayAttemptBlindRecoveryWrite;
            bool shouldWriteProbe = shouldUseConfirmedProbe || shouldUseBlindProbe;
            int selectedProbePercent = shouldUseBlindProbe
                ? blindRecoveryWritePercent
                : writeTransportProbePercent;
            string? writeProbeError = null;
            bool writeTransportAccepted = shouldWriteProbe
                                          && TryDDCWriteTransportProbe(
                                              selectedProbePercent,
                                              lastKnownBrightnessMax,
                                              ddc,
                                              out writeProbeError);
            if (shouldUseBlindProbe)
            {
                WPFLog.Log(
                    $"MonitorService: blind recovery write probe for '{ddc.Name}' "
                    + $"target={selectedProbePercent} result={writeTransportAccepted}"
                    + (writeProbeError == null ? string.Empty : $" error={writeProbeError}"));
            }
            else if (isChecksumRecovery && shouldWriteProbe)
            {
                WPFLog.Log(
                    $"MonitorService: checksum recovery write probe for '{ddc.Name}' "
                    + $"target={selectedProbePercent} result={writeTransportAccepted}"
                    + (writeProbeError == null ? string.Empty : $" error={writeProbeError}"));
            }
            if (writeTransportAccepted)
            {
                // Transport acceptance is not proof that the brightness changed. Make one post-write read attempt
                // even though the preceding acquisition reads failed. If replies resumed, publish full recovery and
                // send the owning manual or curve target through the normal verified pipeline; otherwise enter the
                // best-effort compatibility state without claiming the probe landed.
                if (isChecksumRecovery)
                    Thread.Sleep(TimeConstants.MonitorChecksumRecoveryPostWriteDelayMs);

                if (TryReadBrightness(
                        ddc,
                        out uint postWriteCurrent,
                        out uint postWriteMax,
                        out string? postWriteReadError)
                    && postWriteMax > 0)
                {
                    DDCMonitor readableDDC = ddc;
                    MonitorInfo readableInfo = info;
                    InvokeOnDispatcher(() =>
                        PromoteRecovered(readableInfo, readableDDC, postWriteCurrent, postWriteMax));
                    return true;
                }

                DDCMonitor capturedDDCForDegraded = ddc;
                MonitorInfo capturedInfoForDegraded = info;
                string degradedError = postWriteReadError ?? capturedReadError;
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
                if (!Monitors.Contains(failedInfo)) return;
                // A concurrent Refresh or recovery tick may already have promoted this row while our native probe
                // was running. Never publish an obsolete failure over that newer healthy state.
                if (failedInfo is { IsHardwareFunctional: true, IsReadDegraded: false }) return;
                // Demote to Failed if we were previously in the asymmetric read-degraded state -
                // write transport just failed too, so the slider is no longer trustworthy and the
                // warning glyph should appear with the normal locked-row treatment.
                if (failedInfo.IsReadDegraded)
                {
                    if (_entries.TryRemove(failedInfo.ID, out MonitorEntry? droppedEntry))
                    {
                        InvalidateBrightnessTarget(droppedEntry);
                        failedInfo.LastKnownBrightnessMax = NormalizeBrightnessMax(droppedEntry.Max);
                    }
                    DropQueuedBrightnessWrites(failedInfo.ID);
                    failedInfo.SliderState = SliderStateMachine.OnHardwareFailed();
                    WPFLog.Log(
                        $"MonitorService: '{failedInfo.Name}' demoted from read-degraded to Failed "
                        + "(write transport probe now also failing)");
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

    private static DDCMonitor? FindRecoveryTarget(
        IReadOnlyList<DDCMonitor> live,
        string requestedID,
        string EDIDKey,
        string EDIDSerial,
        DDCRecoveryIdentity? recoveryIdentity,
        MonitorIdentityStrategy identityStrategy)
    {
        if (live.Count == 0) return null;

        DDCMonitor? match = live.FirstOrDefault(d => ComputeMonitorID(d, identityStrategy) == requestedID);
        if (match != null) return match;

        if (recoveryIdentity is { } remembered)
        {
            if (!string.IsNullOrEmpty(remembered.DisplayInstancePath))
            {
                match = live.FirstOrDefault(d =>
                    string.Equals(
                        d.DisplayInstancePath,
                        remembered.DisplayInstancePath,
                        StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            if (!string.IsNullOrEmpty(remembered.DeviceID))
            {
                match = live.FirstOrDefault(d =>
                    string.Equals(d.DeviceID, remembered.DeviceID, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
        }

        // Same stable-identity rescue order as RefreshProbePhaseAsync:
        // EDIDKey, then port-form fallback, then EDID serial. Targeted recovery used to only try
        // requestedID + EDID serial, leaving port-keyed rows permanently failed after EDID/display-number drift.
        if (!string.IsNullOrEmpty(EDIDKey))
        {
            match = live.FirstOrDefault(d =>
                string.Equals(ComputeEDIDKey(d), EDIDKey, StringComparison.Ordinal)
                || string.Equals(ComputePortFormKey(d), EDIDKey, StringComparison.Ordinal));
            if (match != null) return match;
        }

        if (!string.IsNullOrEmpty(EDIDSerial))
        {
            match = live.FirstOrDefault(d =>
                !string.IsNullOrEmpty(d.EDIDSerial)
                && string.Equals(d.EDIDSerial, EDIDSerial, StringComparison.Ordinal));
            if (match != null) return match;
        }

        if (requestedID.StartsWith("port:", StringComparison.Ordinal))
        {
            match = live.FirstOrDefault(d =>
                string.Equals(ComputePortFormKey(d), requestedID, StringComparison.Ordinal));
            if (match != null) return match;
        }

        if (recoveryIdentity is { } adapterIdentity && !string.IsNullOrEmpty(adapterIdentity.Name))
        {
            match = live.FirstOrDefault(d =>
                string.Equals(d.Name, adapterIdentity.Name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return null;
    }

    /// <summary>
    /// Sends a brightness SET as a transport probe after read retries fail.
    /// Transport success distinguishes a completely unavailable DDC path from the explicit read-degraded
    /// compatibility state, but does not prove that the monitor applied the value. The caller immediately attempts
    /// post-write read-back, and normal writes continue to verify without persisting unconfirmed values.
    /// The generic read-degraded path only attempts this for manual rows with an explicit user/profile value.
    /// Checksum recovery may also use it for a curve-owned row when a read-back-confirmed bus value exists; that
    /// value is safe to reassert, and successful recovery immediately replays the current curve target.
    /// When blind degraded-state writes are enabled, a previously DDC-capable row may instead send its current
    /// brightness intent without a confirmed value. User-disabled rows are never write-probed.
    /// Uses <see cref="MonitorInfo.LastKnownBrightnessMax"/> because the Failed -> ReadDegraded transition
    /// removes the MonitorEntry before this runs, and we can't read capabilities while reads are failing.
    /// Goes through WithDDCLock to coordinate with any in-flight user write on the same panel.
    /// </summary>
    private bool TryDDCWriteTransportProbe(
        int probePercent,
        uint lastKnownBrightnessMax,
        DDCMonitor ddc,
        out string? error)
    {
        uint probeRaw = ScaleBrightnessPercentToRaw(probePercent, lastKnownBrightnessMax);

        (bool ok, string? writeErr) = WithDDCLock(ddc, () =>
        {
            bool wrote = _display.TrySetVCPFeature(ddc, ddc.BrightnessCode, probeRaw, out string? e);
            if (!wrote) _display.ResetDDCTransport(ddc);
            return (wrote, e);
        });
        error = writeErr;
        return ok;
    }

    private bool ShouldAttemptReadDegradedWriteTransportProbe(MonitorInfo info)
    {
        if (!info.HasUserBrightness) return false;
        if (info.WasDisabledBeforeFailure) return false;
        if (IsBrightnessCurveEnabledForHardware() && !IsBrightnessCurveDisabledPeriodActive()) return false;
        return true;
    }

    /// <summary>
    /// UI-thread half of asymmetric recovery: the monitor accepted write transport but its post-write read failed,
    /// so flip it out of Failed back into the best-effort state machine and stamp IsReadDegraded
    /// so the flyout shows the informational glyph without locking the slider. Keeps LastDDCError set
    /// because reads are still broken - the DDC fallback worker will keep retrying so that a future read
    /// success can fully promote via <c>PromoteRecovered</c>.
    /// Installs a MonitorEntry with the last successful VCP max when available; a later
    /// PromoteRecovered will overwrite it with a fresh max.
    /// </summary>
    private void PromoteReadDegraded(MonitorInfo info, DDCMonitor ddc, string readError)
    {
        if (_disposed) return;
        if (!Monitors.Contains(info)) return;

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
                ID = info.ID, EDIDKey = info.EDIDKey, DDC = ddc, Max = brightnessMax
            };
        }
        ApplyRecoveredBrightnessProjections(info);

        // Plumb the live curve flags so a read-degraded promotion under an engaged curve lands directly
        // in CurveActive / CurveSleeping rather than Enabled (same H-03 fan-out rationale as PromoteRecovered).
        bool curveEngagedAtPromote = IsBrightnessCurveEnabledForHardware();
        bool inDisabledAtPromote = IsBrightnessCurveDisabledPeriodActive();
        SliderState recoveredState = info.ResolveHardwareRecoveredSliderState(
            curveEngagedAtPromote, inDisabledAtPromote);
        RecordDDCCapableObservation(info);
        SetRecoveredSliderState(info, recoveredState);
        info.IsReadDegraded = true;
        info.LastDDCError = readError;
        RememberRecoveryIdentity(info.ID, ddc);
        WPFLog.Log(
            $"MonitorService: '{ddc.Name}' is read-degraded "
            + "(write transport accepted, application unconfirmed, reads failing)");
        QueueRecoveredBrightnessIntent(info);
        DDCRecoveryRequested?.Invoke(info.ID);
        MonitorsRefreshed?.Invoke();
    }

    /// <summary>
    /// Sends the per-monitor "hard power off" VCP write to a stuck monitor identified by EDID serial.
    /// Used by the degraded-display Ctrl+click action in the flyout:
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
        // because the degraded-display click is the canonical "things have shifted, don't trust the cache" trigger -
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

        if (!target.SupportsVcpPower)
        {
            error = "display does not expose DDC/CI power control";
            return false;
        }

        // Resolve the per-monitor hard-off command. VESA default is 0xD6=0x05 (write-only opcode that
        // turns the monitor off without sending a reply, so it works even on links where DDC reads come back
        // garbled). Dell P/U-series monitors with inverted 0xE1 override this to 0xE1=0x01 - sending the
        // VESA default to those would not turn them off (in fact 0xE1=0 turns them on).
        // Goes through the per-monitor mutex
        // so it can't interleave with a brightness write or recovery probe in flight at the same instant.
        MonitorEntry? activeEntry = _entries.Values.FirstOrDefault(entry =>
            string.Equals(
                Volatile.Read(ref entry.DDC).EDIDSerial,
                EDIDSerial,
                StringComparison.Ordinal));
        if (activeEntry != null)
        {
            InvalidateBrightnessTarget(activeEntry);
            DropQueuedBrightnessWrites(activeEntry.ID);
        }

        _ = TryResolvePowerOffOverride(target, PowerOffLevel.Hard, out byte powerCode, out byte powerValue);
        (bool ok, string? writeErr) = WithDDCLock(target, () =>
        {
            bool wrote = _display.TrySetVCPFeature(target, powerCode, powerValue, out string? e);
            if (!wrote) _display.ResetDDCTransport(target);
            return (wrote, e);
        });
        if (!ok)
        {
            error = writeErr ?? "TrySetVCPFeature failed";
            if (activeEntry != null)
            {
                RequestBrightnessReplayAfterRefresh();
                Refresh();
            }
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
        if (!Monitors.Contains(info)) return;

        // Another thread (Refresh, an interleaved recovery tick) may have already promoted this monitor -
        // check before clobbering.
        if (info is { IsHardwareFunctional: true, IsReadDegraded: false }) return;

        RefreshRecoveredMonitorMetadata(info, ddc);

        int pct = max == 0 ? 0 : (int)Math.Round(current * 100.0 / max);
        uint brightnessMax = NormalizeBrightnessMax(max);
        info.LastKnownBrightnessMax = brightnessMax;
        LogProfileIfMatched(ddc);
        if (_entries.TryGetValue(info.ID, out MonitorEntry? replacedEntry))
        {
            InvalidateBrightnessTarget(replacedEntry);
            DropQueuedBrightnessWrites(info.ID);
        }

        _entries[info.ID] = new MonitorEntry
        {
            ID = info.ID, EDIDKey = info.EDIDKey, DDC = ddc, Max = brightnessMax
        };
        ApplyRecoveredBrightnessProjections(info);
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
        SliderState recoveredState = info.ResolveHardwareRecoveredSliderState(
            curveEngagedAtPromote, inDisabledAtPromote);
        RecordDDCCapableObservation(info);
        SetRecoveredSliderState(info, recoveredState);
        info.IsReadDegraded = false;
        PublishRecoveredPowerAvailability(info, ddc.Name);
        info.LastDDCError = null;
        _recoveryIdentities.TryRemove(info.ID, out DDCRecoveryIdentity _);
        WPFLog.Log($"MonitorService: recovered '{ddc.Name}' to DDC/CI-supported");

        MonitorsRefreshed?.Invoke();
        ReplayRecoveredBrightnessIntent(info);
    }

    private void ApplyRecoveredBrightnessProjections(MonitorInfo info)
    {
        // Targeted recovery bypasses RefreshProbePhaseAsync's final projection pass. Install the same bus-boundary
        // transforms before queueing recovered intent so the first restored manual or curve target cannot use
        // default bounds or a missing norm curve.
        ApplyDDCTimingOverridesToExisting();
        ApplyBrightnessBoundsTo(info, BuildBrightnessBoundOverrideMap(), replayHardware: false);
        ApplyNormCurveTo(info, BuildNormCurveOverrideMap(), replayHardware: false);
    }

    private void QueueRecoveredBrightnessIntent(MonitorInfo info)
    {
        if (!info.IsHardwareFunctional) return;
        if (!info.HasUserBrightness) return;
        if (info.SliderState == SliderState.Disabled) return;
        if (ShouldSuppressSliderBrightnessWrite(info)) return;

        EnqueueDirectBrightness(info, info.RoundedBrightness);
    }

    /// <summary>
    /// Reasserts the target that owns hardware after recovery publication. Recovery GET is evidence that the transport
    /// is readable, not proof that the panel's visible brightness matches its reply, so this deliberately forces one
    /// verified SET even when the returned value or scheduler dedupe claims the target already matches.
    /// </summary>
    private void ReplayRecoveredBrightnessIntent(MonitorInfo info)
    {
        if (!TryResolveRecoveredBrightnessIntent(info, out int percentage, out SliderState expectedState)) return;

        WPFLog.Log(
            $"MonitorService: recovery replay '{info.Name}' state={expectedState} target={percentage}");
        EnqueueDirectBrightnessImmediate(info, percentage, IsStillCurrent);
        return;

        bool IsStillCurrent()
        {
            return TryResolveRecoveredBrightnessIntent(
                       info,
                       out int currentPercentage,
                       out SliderState currentState)
                   && currentState == expectedState
                   && currentPercentage == percentage;
        }
    }

    private static bool TryResolveRecoveredBrightnessIntent(
        MonitorInfo info,
        out int percentage,
        out SliderState state)
    {
        percentage = 0;
        state = info.SliderState;
        if (!info.IsHardwareFunctional) return false;
        if (info.SuppressDDCRecoveryForPowerIntent) return false;

        switch (state)
        {
            case SliderState.CurveActive:
                if (!info.HasCurveTargetBrightness && !info.HasUserBrightness) return false;
                percentage = info.EffectiveRoundedBrightness;
                return true;

            case SliderState.Enabled:
            case SliderState.CurveSleeping:
            case SliderState.CurveReleased:
                if (!info.HasUserBrightness) return false;
                percentage = info.RoundedBrightness;
                return true;

            case SliderState.Disabled:
            case SliderState.Failed:
            default:
                return false;
        }
    }

    private static void PublishRecoveredPowerAvailability(MonitorInfo info, string monitorName)
    {
        if (info.SuppressDDCRecoveryForPowerIntent)
        {
            WPFLog.Log(
                $"MonitorService: clearing stale power-off recovery suppression for '{monitorName}' after recovery");
        }

        info.SuppressDDCRecoveryForPowerIntent = false;
        info.IsPoweredOn = true;
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

            DropQueuedBrightnessWrites(oldID);
            if (_recoveryIdentities.TryRemove(oldID, out DDCRecoveryIdentity movingRecoveryIdentity))
                _recoveryIdentities[newID] = movingRecoveryIdentity;
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
        info.SupportsPowerControl = ddc.SupportsVcpPower;
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
    /// Verifies readable power-state replies and re-applies a transport-accepted write when the monitor still reports
    /// the prior state. Hard-off commonly removes the monitor from the DDC bus, so an unavailable reply after an
    /// accepted write remains a successful but explicitly logged unverified result.
    /// </summary>
    public async Task SetPowerStateAsync(MonitorInfo monitor, bool on)
    {
        if (_disposed || _draining) return;
        if (!monitor.SupportsPowerControl) return;
        if (monitor.IsReadDegraded && !_settings.AllowBlindDDCWritesDuringDegradedState) return;

        if (!_entries.TryGetValue(monitor.ID, out MonitorEntry? entry)) return;
        DDCMonitor ddc = Volatile.Read(ref entry.DDC);
        if (!ddc.SupportsVcpPower) return;

        long powerIntentGeneration = Interlocked.Increment(ref entry.PowerIntentGeneration);
        bool previousRecoverySuppression = monitor.SuppressDDCRecoveryForPowerIntent;

        // A queued brightness re-apply after an off command can wake some monitors or immediately fail and demote
        // the row. Make every older brightness generation stale before sending power-down traffic.
        if (!on)
        {
            InvalidateBrightnessTarget(entry);
            DropQueuedBrightnessWrites(entry.ID);
        }

        PowerOffLevel offLevel = _settings.PowerOffMode switch
        {
            PowerOffMode.Soft => PowerOffLevel.Soft,
            PowerOffMode.Hard => PowerOffLevel.Hard,
            _ => PowerOffLevel.Sleep
        };
        (byte code, byte value) = on
            ? ddc.ResolvePowerOn()
            : TryResolvePowerOffOverride(ddc, offLevel, out byte overrideCode, out byte overrideValue)
                ? (overrideCode, overrideValue)
                : ddc.ResolvePowerOff(offLevel);
        WPFLog.Log(
            $"MonitorService: SetPowerState '{ddc.Name}' on={on}; code=0x{code:X2}; value=0x{value:X2}; "
            + $"mode={_settings.PowerOffMode}");

        PowerStateApplyResult application = await ApplyPowerStateWithVerificationAsync(
                entry,
                monitor,
                powerIntentGeneration,
                on,
                code,
                value)
            .ConfigureAwait(false);
        switch (application.Outcome)
        {
            case PowerStateApplyOutcome.Superseded:
                return;
            case PowerStateApplyOutcome.Failed:
                monitor.SuppressDDCRecoveryForPowerIntent = previousRecoverySuppression;
                WPFLog.Log($"MonitorService: SetPowerState failed for '{ddc.Name}': {application.Error}");
                if (!on)
                {
                    // The panel remained on, but its queued brightness target was invalidated before the power attempt
                    // to guarantee ordering. Re-acquire and replay that intent instead of silently losing it.
                    RequestBrightnessReplayAfterRefresh();
                    Refresh();
                }
                return;
            case PowerStateApplyOutcome.Applied:
                break;
        }

        if (_dispatcher.CheckAccess())
            PublishPowerState();
        else
            _dispatcher.Post(PublishPowerState);

        void PublishPowerState()
        {
            if (_disposed || !Monitors.Contains(monitor)) return;

            monitor.IsPoweredOn = on;
            if (!on) return;

            // A successful wake can reset brightness without emitting a reliable Windows topology event.
            // Route it through the same settled refresh/replay boundary as an external wake so fresh handles and
            // maxima are installed before the current manual or curve target is verified again.
            NotifyTopologyEvent();
            Refresh();
        }
    }

    private async Task<PowerStateApplyResult> ApplyPowerStateWithVerificationAsync(
        MonitorEntry entry,
        MonitorInfo monitor,
        long powerIntentGeneration,
        bool on,
        byte code,
        byte value)
    {
        int attempts = Math.Max(1, _settings.ValidationAttempts);
        int finalDwellMs = ResolveValidationDwellMs(entry);
        string? lastError = null;
        PowerWriteAttempt initialWrite = await TryWritePowerStateAsync(
                entry,
                powerIntentGeneration,
                code,
                value)
            .ConfigureAwait(false);
        if (!initialWrite.WasAttempted)
            return new PowerStateApplyResult(PowerStateApplyOutcome.Superseded, null);
        if (!initialWrite.Success)
            return new PowerStateApplyResult(
                PowerStateApplyOutcome.Failed,
                initialWrite.Error ?? "TrySetVCPFeature failed");

        // Publish the recovery gate as soon as the write is accepted. Hard-off can remove the row before read-back
        // completes; recovery must not race verification and send traffic that wakes the panel.
        monitor.SuppressDDCRecoveryForPowerIntent = !on;
        bool latestWriteAccepted = true;

        int initialVerificationDwellMs = Math.Min(
            finalDwellMs,
            TimeConstants.MonitorInitialVerificationDwellMaxMs);
        if (!await DelayWhilePowerIntentCurrentAsync(
                entry,
                powerIntentGeneration,
                initialVerificationDwellMs).ConfigureAwait(false))
            return new PowerStateApplyResult(PowerStateApplyOutcome.Superseded, null);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            PowerReadBack readBack = await TryReadPowerStateAsync(
                    entry,
                    powerIntentGeneration,
                    code)
                .ConfigureAwait(false);
            if (!readBack.WasAttempted)
                return new PowerStateApplyResult(PowerStateApplyOutcome.Superseded, null);

            if (!readBack.Success)
            {
                if (!latestWriteAccepted)
                    return new PowerStateApplyResult(
                        PowerStateApplyOutcome.Failed,
                        lastError ?? readBack.Error ?? "power write and read-back both failed");

                // Hard-off is intentionally write-only on many monitors and removes the DDC endpoint. Do not turn an
                // expected missing reply into failure or reset the transport after an accepted off command.
                WPFLog.Log(
                    $"MonitorService: power verification unavailable for '{readBack.MonitorName}'; "
                    + $"accepted write remains unverified: {readBack.Error}");
                return new PowerStateApplyResult(PowerStateApplyOutcome.Applied, null);
            }

            if (readBack.Actual == value)
            {
                WPFLog.Log(
                    $"MonitorService: power write verified for '{readBack.MonitorName}'; "
                    + $"code=0x{code:X2}; value=0x{value:X2}");
                return new PowerStateApplyResult(PowerStateApplyOutcome.Applied, null);
            }

            lastError = $"read-back 0x{readBack.Actual:X2}, expected 0x{value:X2}";
            WPFLog.Log(
                $"MonitorService: power verify mismatch {attempt + 1}/{attempts} for "
                + $"'{readBack.MonitorName}': {lastError}");
            if (attempt == attempts - 1) break;

            PowerWriteAttempt reapply = await TryWritePowerStateAsync(
                    entry,
                    powerIntentGeneration,
                    code,
                    value)
                .ConfigureAwait(false);
            if (!reapply.WasAttempted)
                return new PowerStateApplyResult(PowerStateApplyOutcome.Superseded, null);
            latestWriteAccepted = reapply.Success;
            if (!reapply.Success)
            {
                lastError = reapply.Error;
                WPFLog.Log(
                    $"MonitorService: power re-apply {attempt + 2}/{attempts} failed for "
                    + $"'{reapply.MonitorName}': {reapply.Error}");
            }

            int waitMs = ScaledRetryDwellMs(attempt + 1, attempts, finalDwellMs);
            if (!await DelayWhilePowerIntentCurrentAsync(
                    entry,
                    powerIntentGeneration,
                    waitMs).ConfigureAwait(false))
                return new PowerStateApplyResult(PowerStateApplyOutcome.Superseded, null);
        }

        return new PowerStateApplyResult(
            PowerStateApplyOutcome.Failed,
            lastError ?? "power state did not match the requested value");
    }

    private async Task<PowerWriteAttempt> TryWritePowerStateAsync(
        MonitorEntry entry,
        long powerIntentGeneration,
        byte code,
        byte value)
    {
        DDCMonitor ddc = Volatile.Read(ref entry.DDC);
        return await WithDDCLockAsync(ddc, () =>
        {
            if (!IsPowerIntentCurrent(entry, powerIntentGeneration))
                return new PowerWriteAttempt(false, false, ddc.Name, null);

            bool success = _display.TrySetVCPFeature(ddc, code, value, out string? error);
            if (!success) _display.ResetDDCTransport(ddc);
            return new PowerWriteAttempt(true, success, ddc.Name, error);
        }).ConfigureAwait(false);
    }

    private async Task<PowerReadBack> TryReadPowerStateAsync(
        MonitorEntry entry,
        long powerIntentGeneration,
        byte code)
    {
        DDCMonitor ddc = Volatile.Read(ref entry.DDC);
        return await WithDDCLockAsync(ddc, () =>
        {
            if (!IsPowerIntentCurrent(entry, powerIntentGeneration))
                return new PowerReadBack(false, false, ddc.Name, 0, null);

            bool success = _display.TryGetVCPFeature(
                ddc,
                code,
                out uint actual,
                out _,
                out string? error);
            return new PowerReadBack(true, success, ddc.Name, actual, error);
        }).ConfigureAwait(false);
    }

    private bool IsPowerIntentCurrent(MonitorEntry entry, long powerIntentGeneration) =>
        !_disposed
        && !_draining
        && Volatile.Read(ref entry.PowerIntentGeneration) == powerIntentGeneration;

    private async Task<bool> DelayWhilePowerIntentCurrentAsync(
        MonitorEntry entry,
        long powerIntentGeneration,
        int delayMs)
    {
        if (!IsPowerIntentCurrent(entry, powerIntentGeneration)) return false;
        if (delayMs <= 0) return true;

        await Task.Delay(delayMs).ConfigureAwait(false);
        return IsPowerIntentCurrent(entry, powerIntentGeneration);
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
        if (monitor.SuppressDDCRecoveryForPowerIntent)
        {
            // A direct user/profile brightness change is newer intent than an earlier power-off command. Curve writes
            // bypass the Brightness setter and therefore cannot clear this gate in the background.
            monitor.SuppressDDCRecoveryForPowerIntent = false;
            monitor.IsPoweredOn = true;
        }
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
        // captures every read-back-verified target regardless of source.
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
        if (!TryPrepareDirectBrightnessWrite(
                monitor,
                percent,
                force: false,
                out MonitorEntry entry,
                out BrightnessWriteTarget target))
            return;

        // Schedule a payload that closes over (entry, pct). The throttler does latest-pending-wins:
        // a flurry of EnqueueDirectBrightness calls during the cooldown collapse to a single payload
        // running with the freshest pct.
        // After the payload completes the throttler observes _writeCooldownMs
        // before letting the next queued payload run,
        // mirroring the pre-throttler hand-rolled write loop's "write -> wait -> verify -> loop" pacing.
        _ = _writeThrottler.RunAsync(entry.ID, context =>
            DoBrightnessWriteAsync(entry, target, context),
            cooldownOverrideMs: ResolveBrightnessDwellMs(entry));
    }

    /// <summary>
    /// Writes a direct brightness target without waiting for the per-monitor cooldown window.
    /// Used only for mode handoff boundaries where stale manual hardware state must be superseded now
    /// (for example, returning a released row to curve ownership).
    /// The zero-cooldown immediate driver remains drainable and uses the same target generation,
    /// DDC lock, retries, and read-back verification as normal writes.
    /// </summary>
    public void EnqueueDirectBrightnessImmediate(
        MonitorInfo? monitor,
        int percent,
        Func<bool>? shouldWrite = null)
    {
        // Reject an already-stale handoff before it can occupy the dedupe slot. The predicate is checked again
        // under the DDC lock because ownership can still change after this synchronous gate.
        if (shouldWrite?.Invoke() == false) return;

        if (!TryPrepareDirectBrightnessWrite(
                monitor,
                percent,
                force: true,
                out MonitorEntry entry,
                out BrightnessWriteTarget target))
            return;

        // The new generation makes any running normal payload stale. Drop its queued replacement so it cannot
        // consume a cooldown after this handoff; a native call already in progress is allowed to finish, and the
        // immediate target then wins under the per-monitor DDC lock.
        _writeThrottler.Drop(entry.ID);
        _ = _immediateWriteThrottler.RunAsync(entry.ID, context =>
            DoBrightnessWriteAsync(entry, target, context, shouldWrite));
    }

    private bool TryPrepareDirectBrightnessWrite(
        MonitorInfo? monitor,
        int percent,
        bool force,
        out MonitorEntry entry,
        out BrightnessWriteTarget target)
    {
        entry = null!;
        target = default;
        if (_disposed || _draining) return false;
        if (monitor == null) return false;
        if (monitor.SuppressDDCRecoveryForPowerIntent) return false;
        if (monitor.IsReadDegraded && !_settings.AllowBlindDDCWritesDuringDegradedState) return false;
        if (!_entries.TryGetValue(monitor.ID, out MonitorEntry? resolvedEntry)) return false;

        // Apply the per-monitor norm curve first: the slider stays on the linear 0..100 range
        // and the curve reshapes which hardware brightness each slider position maps to.
        // No-op when no curve is set (xs/ys are null). Lives ahead of the floor/ceiling clamp so
        // a curve that targets values outside the cap window still respects the user's cap below.
        int shaped = ApplyNormCurve(resolvedEntry, percent);

        // Clamp first to the absolute 0..100 envelope, then to the per-monitor override window.
        // The slider itself stays on the normalised 0-100 range; this is the single boundary where
        // the per-monitor floor/ceiling actually constrain hardware. Every write path flows through
        // here (slider drag, master propagation, curve writes, topology replay), so the cap is enforced
        // uniformly without the slider, profile, or curve code having to know about it.
        int floor = resolvedEntry.FloorPercent;
        int ceiling = resolvedEntry.CeilingPercent;
        if (floor > ceiling) floor = ceiling;
        int clampedPercent = Math.Clamp(Math.Clamp(shaped, 0, 100), floor, ceiling);

        lock (resolvedEntry.BrightnessTargetGate)
        {
            // A value is deduplicable only while the same target is still guaranteed to run, or after a matching
            // read-back completed. Enqueue-time state alone is not proof: a canceled handoff must remain retryable.
            if (!force
                && ((resolvedEntry.HasPendingBrightnessTarget
                     && resolvedEntry.PendingBrightnessPercentage == clampedPercent)
                    || (!resolvedEntry.HasPendingBrightnessTarget
                        && resolvedEntry.LastVerifiedBrightnessPercentage == clampedPercent)))
                return false;

            long generation = ++resolvedEntry.BrightnessTargetGeneration;
            resolvedEntry.PendingBrightnessPercentage = clampedPercent;
            // Once a different write is accepted, canceled, or fails verification, the old acknowledgement no
            // longer proves current hardware state. Only this generation's matching read-back restores deduplication.
            resolvedEntry.LastVerifiedBrightnessPercentage = -1;
            resolvedEntry.HasPendingBrightnessTarget = true;
            target = new BrightnessWriteTarget(generation, clampedPercent);
        }

        entry = resolvedEntry;
        return true;
    }

    /// <summary>
    /// Re-pushes every DDC-supported monitor's current slider position to the bus.
    /// Used after a display-topology change (hot-plug, resume, session unlock)
    /// where the OS hands us back the same panels but their brightness has been reset by the replug -
    /// without this, the slider stays put while the panel is at its factory/last-flash level.
    /// Goes through the same per-monitor throttler the slider drag uses,
    /// so it composes naturally with any user input that arrives during or shortly after.
    /// </summary>
    private void ReplayBrightnessTargetsAfterRefresh(long replayGeneration)
    {
        if (_disposed || _draining) return;
        if (replayGeneration <= 0) return;
        if (replayGeneration <= Volatile.Read(ref _lastCompletedBrightnessReplayGeneration)) return;
        if (replayGeneration != Volatile.Read(ref _brightnessReplayGeneration)) return;

        Volatile.Write(ref _lastCompletedBrightnessReplayGeneration, replayGeneration);

        foreach (MonitorEntry entry in _entries.Values)
        {
            InvalidateBrightnessTarget(entry);
            DropQueuedBrightnessWrites(entry.ID);
        }

        int count = 0;
        foreach (MonitorInfo m in Monitors)
        {
            if (!m.IsHardwareFunctional) continue;
            if (m.SliderState == SliderState.Disabled) continue;
            if (!m.HasUserBrightness) continue;
            // Curve-owned rows must not get the slider value replayed here. This guard also covers
            // startup before the flyout-owned curve service has harmonized freshly-added rows into
            // CurveActive; the persisted brightness-curve setting is enough to keep manual replay off
            // until the curve evaluator applies its direct-write target.
            if (ShouldSuppressSliderBrightnessWrite(m)) continue;

            if (!TryPrepareDirectBrightnessWrite(
                    m,
                    m.RoundedBrightness,
                    force: true,
                    out MonitorEntry entry,
                    out BrightnessWriteTarget target))
                continue;

            _ = _writeThrottler.RunAsync(entry.ID, context =>
                DoBrightnessWriteAsync(entry, target, context),
                cooldownOverrideMs: ResolveBrightnessDwellMs(entry));
            count++;
        }

        WPFLog.Log(
            $"MonitorService: brightness replay generation {replayGeneration}; "
            + $"queued {count} manual target(s), curve targets await MonitorsRefreshed evaluation");
    }

    /// <summary>
    /// Throttler payload for one brightness target.
    /// Performs write+retry and always enters read-back verification for the newest target.
    /// Explicit target generations, rather than the throttler's transient replacement flag, arbitrate
    /// normal writes, cooldown-bypassing handoffs, topology invalidation, and cancellation.
    /// </summary>
    private async Task DoBrightnessWriteAsync(
        MonitorEntry entry,
        BrightnessWriteTarget target,
        ThrottlerContext context,
        Func<bool>? shouldWrite = null)
    {
        try
        {
            await DoBrightnessWriteCoreAsync(entry, target, context, shouldWrite).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AbandonBrightnessTargetIfCurrent(entry, target);
        }
        catch (Exception ex)
        {
            // AsyncThrottler deliberately contains payload exceptions. Release target ownership here first so
            // the same percentage remains retryable instead of becoming a permanently deduplicated phantom write.
            AbandonBrightnessTargetIfCurrent(entry, target);
            WPFLog.Log(
                $"MonitorService.DoBrightnessWriteAsync: unexpected target failure for '{entry.ID}': {ex.Message}");
        }
    }

    private async Task DoBrightnessWriteCoreAsync(
        MonitorEntry entry,
        BrightnessWriteTarget target,
        ThrottlerContext context,
        Func<bool>? shouldWrite = null)
    {
        if (_disposed || _draining) return;
        if (!IsBrightnessTargetCurrent(entry, target)) return;

        // Retry transient write failures (most commonly the I2C-transmit-error class of Win32Exception,
        // which the bus throws at us when a packet collides or the monitor is mid-OSD / mid-DPMS-wake).
        // Uses ValidationAttempts as the cap; inter-retry waits are scaled -
        // short for the first few retries (covers fast transients without slider sluggishness)
        // and the full ValidationDwellMs on the final attempt
        // (gives a slow monitor real settle time before we give up).
        // Supersession-aware waits poll the explicit generation, so a fresh slider target waits at most
        // one short polling slice rather than the full final dwell before it can take over.
        int writeAttempts = Math.Max(1, _settings.ValidationAttempts);
        int writeFinalDwellMs = ResolveValidationDwellMs(entry);
        string? lastWriteError = null;
        bool wrote = false;
        for (int attempt = 0; attempt < writeAttempts; attempt++)
        {
            int waitMs = ScaledRetryDwellMs(attempt, writeAttempts, writeFinalDwellMs);
            if (!await DelayWhileBrightnessTargetCurrentAsync(
                    entry,
                    target,
                    waitMs,
                    context.CancellationToken).ConfigureAwait(false))
                return;

            BrightnessWriteAttempt write = await TryWriteBrightnessTargetAsync(
                    entry,
                    target,
                    shouldWrite,
                    context.CancellationToken)
                .ConfigureAwait(false);
            if (!write.WasAttempted)
            {
                AbandonBrightnessTargetIfCurrent(entry, target);
                return;
            }

            if (write.Success)
            {
                wrote = true;
                lastWriteError = null;
                break;
            }

            if (_disposed || _draining || !IsBrightnessTargetCurrent(entry, target)) return;

            lastWriteError = write.Error;
            WPFLog.Log(
                $"MonitorService: SetVCPFeature attempt {attempt + 1}/{writeAttempts} failed for "
                + $"'{write.MonitorName}': {write.Error}");
        }

        if (lastWriteError != null)
        {
            if (!IsBrightnessTargetCurrent(entry, target)) return;

            DemoteOnDDCFailure(entry, lastWriteError);
            return;
        }

        if (!wrote) return;

        int initialVerificationDwellMs = Math.Min(
            writeFinalDwellMs,
            TimeConstants.MonitorInitialVerificationDwellMaxMs);
        if (!await DelayWhileBrightnessTargetCurrentAsync(
                entry,
                target,
                initialVerificationDwellMs,
                context.CancellationToken).ConfigureAwait(false))
            return;

        await VerifyAppliedAsync(entry, target, context, shouldWrite).ConfigureAwait(false);
    }

    /// <summary>
    /// Read-back verification with re-apply on mismatch.
    /// Loops up to <see cref="AppSettings.ValidationAttempts"/> times:
    /// each iteration reads the brightness VCP and returns on a match (within +/-1 raw unit to absorb monitor-side
    /// quantization). A readable mismatch justifies re-applying the target. A failed read does not: the affected
    /// helper is reset and the loop observes a quiet backoff before trying another read, avoiding repeated writes
    /// into a desynchronized HDMI reply pipeline.
    /// The dwell ramps from short (catches the common "monitor was busy for a moment" case fast)
    /// up to <see cref="AppSettings.ValidationDwellMs"/> on the final attempt
    /// (gives a slow monitor real settle time before we declare the link unresponsive).
    /// HMONITOR is refreshed once on the first failed or mismatched read as a defence against stale handles.
    /// A stale generation exits without re-applying, while the newest generation owns completion and persistence.
    /// </summary>
    private async Task VerifyAppliedAsync(
        MonitorEntry entry,
        BrightnessWriteTarget target,
        ThrottlerContext context,
        Func<bool>? shouldWrite)
    {
        const long Tolerance = 1;
        int attempts = Math.Max(1, _settings.ValidationAttempts);
        int finalDwellMs = ResolveValidationDwellMs(entry);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (_disposed || _draining || !IsBrightnessTargetCurrent(entry, target)) return;

            BrightnessReadBack readBack = await TryReadBrightnessTargetAsync(
                    entry,
                    target,
                    context.CancellationToken)
                .ConfigureAwait(false);
            if (!readBack.WasAttempted) return;

            bool readable = readBack.Success && readBack.Maximum > 0;
            if (!readable)
            {
                WPFLog.Log(
                    $"MonitorService: verify read failed for '{readBack.MonitorName}': {readBack.Error}");
            }
            else
            {
                Volatile.Write(ref entry.Max, readBack.Maximum);
                uint expectedRaw = ScaleBrightnessPercentToRaw(target.Percentage, readBack.Maximum);
                if (Math.Abs((long)readBack.Actual - expectedRaw) <= Tolerance)
                {
                    if (CompleteBrightnessTargetIfCurrent(entry, target)
                        && !string.IsNullOrEmpty(entry.EDIDKey))
                    {
                        // Persistence represents acknowledged hardware state, never mere SetVCPFeature acceptance.
                        _knownDisplays.StampLastBusBrightness(entry.EDIDKey, target.Percentage);
                    }

                    return;
                }
            }

            // Last attempt: don't bother re-applying or settling - we're about to demote.
            if (attempt == attempts - 1) break;

            // First failure only: refresh the cached HMONITOR before the next transport attempt.
            // Catches stale handles that survived a topology change the primary pipeline missed;
            // cheap and only worth doing once since the second cause of mismatches (slow monitor) doesn't need it.
            if (attempt == 0)
            {
                string? refreshedMonitorName = await TryRefreshBrightnessHandleAsync(entry, target)
                    .ConfigureAwait(false);
                if (refreshedMonitorName != null)
                    WPFLog.Log($"MonitorService: refreshed HMONITOR for '{refreshedMonitorName}' mid-verify");
            }

            // A failed GET does not prove the SET missed. Re-applying on every checksum/transport failure floods
            // fragile HDMI links and can prevent the monitor's reply pipeline from resynchronizing.
            if (readable)
            {
                BrightnessWriteAttempt reapply = await TryWriteBrightnessTargetAsync(
                        entry,
                        target,
                        shouldWrite,
                        context.CancellationToken)
                    .ConfigureAwait(false);
                if (!reapply.WasAttempted)
                {
                    AbandonBrightnessTargetIfCurrent(entry, target);
                    return;
                }

                if (!reapply.Success)
                    WPFLog.Log($"MonitorService: re-apply failed for '{reapply.MonitorName}': {reapply.Error}");
            }

            // Wait for the NEXT attempt (attempt+1).
            // +1 because the helper's "wait before this attempt" semantic gives 0 for index 0;
            // we're computing the wait between this mismatched attempt and the next one.
            int waitMs = ScaledRetryDwellMs(attempt + 1, attempts, finalDwellMs);
            if (!await DelayWhileBrightnessTargetCurrentAsync(
                    entry,
                    target,
                    waitMs,
                    context.CancellationToken).ConfigureAwait(false))
                return;
        }

        if (_disposed || _draining || !IsBrightnessTargetCurrent(entry, target)) return;

        if (IsReadDegraded(entry.ID))
        {
            AbandonBrightnessTargetIfCurrent(entry, target);
            DDCMonitor degradedDDC = Volatile.Read(ref entry.DDC);
            WPFLog.Log(
                $"MonitorService: verification exhausted for read-degraded '{degradedDDC.Name}', "
                + "keeping best-effort state and leaving the target retryable");
            return;
        }

        uint finalExpectedRaw = ScaleBrightnessPercentToRaw(target.Percentage, Volatile.Read(ref entry.Max));
        DDCMonitor currentDDC = Volatile.Read(ref entry.DDC);
        WPFLog.Log(
            $"MonitorService: verification exhausted for '{currentDDC.Name}' - target raw={finalExpectedRaw}");
        DemoteOnDDCFailure(entry, "Brightness write was not acknowledged after retry - DDC/CI link is unresponsive.");
    }

    private async Task<BrightnessWriteAttempt> TryWriteBrightnessTargetAsync(
        MonitorEntry entry,
        BrightnessWriteTarget target,
        Func<bool>? shouldWrite,
        CancellationToken cancellationToken)
    {
        DDCMonitor ddc = Volatile.Read(ref entry.DDC);
        return await WithDDCLockAsync(ddc, () =>
        {
            if (!IsBrightnessTargetCurrent(entry, target) || shouldWrite?.Invoke() == false)
                return BrightnessWriteAttempt.Superseded(ddc.Name);

            uint raw = ScaleBrightnessPercentToRaw(target.Percentage, Volatile.Read(ref entry.Max));
            bool success = _display.TrySetVCPFeature(
                ddc,
                ddc.BrightnessCode,
                raw,
                out string? error,
                cancellationToken);
            if (!success) _display.ResetDDCTransport(ddc);
            return new BrightnessWriteAttempt(true, success, ddc.Name, error);
        }).ConfigureAwait(false);
    }

    private async Task<BrightnessReadBack> TryReadBrightnessTargetAsync(
        MonitorEntry entry,
        BrightnessWriteTarget target,
        CancellationToken cancellationToken)
    {
        DDCMonitor ddc = Volatile.Read(ref entry.DDC);
        return await WithDDCLockAsync(ddc, () =>
        {
            if (!IsBrightnessTargetCurrent(entry, target))
                return BrightnessReadBack.Superseded(ddc.Name);

            bool success = _display.TryGetVCPFeature(
                ddc,
                ddc.BrightnessCode,
                out uint actual,
                out uint maximum,
                out string? error,
                cancellationToken);
            if (!success) _display.ResetDDCTransport(ddc);
            return new BrightnessReadBack(true, success, actual, maximum, ddc.Name, error);
        }).ConfigureAwait(false);
    }

    private async Task<string?> TryRefreshBrightnessHandleAsync(
        MonitorEntry entry,
        BrightnessWriteTarget target)
    {
        DDCMonitor ddc = Volatile.Read(ref entry.DDC);
        return await WithDDCLockAsync(ddc, () =>
        {
            if (!IsBrightnessTargetCurrent(entry, target)) return null;

            bool refreshed = RefreshHandlePreservingBrightnessCode(ddc);
            return refreshed ? ddc.Name : null;
        }).ConfigureAwait(false);
    }

    private bool RefreshHandlePreservingBrightnessCode(DDCMonitor ddc)
    {
        // RefreshHandle reapplies the database profile. Preserve a user-selected brightness VCP override across
        // both acquisition-read and write-verification recovery.
        byte brightnessCode = ddc.BrightnessCode;
        bool refreshed = _display.RefreshHandle(ddc);
        ddc.BrightnessCode = brightnessCode;
        return refreshed;
    }

    private int ResolveValidationDwellMs(MonitorEntry entry)
    {
        int overrideMs = Volatile.Read(ref entry.ValidationDwellMs);
        return overrideMs >= 0 ? overrideMs : Math.Max(0, Volatile.Read(ref _validationDwellMs));
    }

    private int ResolveBrightnessDwellMs(MonitorEntry entry)
    {
        int overrideMs = Volatile.Read(ref entry.BrightnessDwellMs);
        return overrideMs >= 0 ? overrideMs : Math.Max(0, Volatile.Read(ref _writeCooldownMs));
    }

    private async Task<bool> DelayWhileBrightnessTargetCurrentAsync(
        MonitorEntry entry,
        BrightnessWriteTarget target,
        int delayMs,
        CancellationToken cancellationToken)
    {
        int remainingMs = Math.Max(0, delayMs);
        while (remainingMs > 0)
        {
            if (_disposed || _draining || !IsBrightnessTargetCurrent(entry, target)) return false;

            int sliceMs = Math.Min(TimeConstants.BrightnessTargetSupersessionPollIntervalMs, remainingMs);
            try { await Task.Delay(sliceMs, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
            remainingMs -= sliceMs;
        }

        return !_disposed && !_draining && IsBrightnessTargetCurrent(entry, target);
    }

    private static bool IsBrightnessTargetCurrent(MonitorEntry entry, BrightnessWriteTarget target)
    {
        lock (entry.BrightnessTargetGate)
        {
            return entry.HasPendingBrightnessTarget
                   && entry.BrightnessTargetGeneration == target.Generation
                   && entry.PendingBrightnessPercentage == target.Percentage;
        }
    }

    private static bool CompleteBrightnessTargetIfCurrent(MonitorEntry entry, BrightnessWriteTarget target)
    {
        lock (entry.BrightnessTargetGate)
        {
            if (!entry.HasPendingBrightnessTarget
                || entry.BrightnessTargetGeneration != target.Generation
                || entry.PendingBrightnessPercentage != target.Percentage)
                return false;

            entry.HasPendingBrightnessTarget = false;
            entry.PendingBrightnessPercentage = -1;
            entry.LastVerifiedBrightnessPercentage = target.Percentage;
            return true;
        }
    }

    private static void AbandonBrightnessTargetIfCurrent(MonitorEntry entry, BrightnessWriteTarget target)
    {
        lock (entry.BrightnessTargetGate)
        {
            if (!entry.HasPendingBrightnessTarget
                || entry.BrightnessTargetGeneration != target.Generation
                || entry.PendingBrightnessPercentage != target.Percentage)
                return;

            entry.HasPendingBrightnessTarget = false;
            entry.PendingBrightnessPercentage = -1;
        }
    }

    private static void InvalidateBrightnessTarget(MonitorEntry entry)
    {
        lock (entry.BrightnessTargetGate)
        {
            entry.BrightnessTargetGeneration++;
            entry.HasPendingBrightnessTarget = false;
            entry.PendingBrightnessPercentage = -1;
            entry.LastVerifiedBrightnessPercentage = -1;
        }
    }

    private bool IsBrightnessWriteBusy(string monitorID) =>
        _writeThrottler.IsBusy(monitorID) || _immediateWriteThrottler.IsBusy(monitorID);

    private void DropQueuedBrightnessWrites(string monitorID)
    {
        _writeThrottler.Drop(monitorID);
        _immediateWriteThrottler.Drop(monitorID);
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

        InvalidateBrightnessTarget(entry);

        if (_dispatcher.CheckAccess())
            Apply();
        else
            _dispatcher.Post(Apply);
        return;

        void Apply()
        {
            if (_disposed) return;

            // The entry might have been replaced (recovery promote) since we queued -
            // never let an obsolete payload demote the replacement row.
            if (!_entries.TryGetValue(id, out MonitorEntry? current) || !ReferenceEquals(current, entry)) return;
            // Drop queued writes only after proving this entry still owns the row. A recovered replacement may
            // already have queued its replay under the same ID and must not lose that work.
            DropQueuedBrightnessWrites(id);
            ((ICollection<KeyValuePair<string, MonitorEntry>>)_entries).Remove(KeyValuePair.Create(id, current));

            MonitorInfo? info = Monitors.FirstOrDefault(m => m.ID == id);
            if (info == null) return;
            info.LastKnownBrightnessMax = NormalizeBrightnessMax(Volatile.Read(ref entry.Max));
            RememberRecoveryIdentity(id, Volatile.Read(ref entry.DDC));

            // Already demoted by another path (e.g. concurrent verify exhaustion racing with a write throw) -
            // don't clobber a fresher error message.
            if (!info.IsHardwareFunctional && !string.IsNullOrEmpty(info.LastDDCError)) return;

            info.SliderState = SliderStateMachine.OnHardwareFailed();
            // A live MonitorEntry proves that this row was DDC-capable even if persisted identity metadata drifted.
            RecordDDCCapableObservation(info);
            info.LastDDCError = error;
            WPFLog.Log($"MonitorService: demoted '{entry.DDC.Name}' to DDC/CI-unavailable ({error})");

            DDCRecoveryRequested?.Invoke(id);
            // Wake the DDC fallback worker now instead of waiting for another topology/settings event -
            // mirrors what a Refresh-driven add does so the UI feedback is synchronous with the failure.
            MonitorsRefreshed?.Invoke();
        }
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

        _recoveryIdentities.Clear();

        // Flush any pending debounced brightness / offset stamps so a last-moment slider drag
        // doesn't get lost on shutdown, and dispose the timer (H-15). Dispose internally flushes
        // and stops the debounce timer so the System.Threading.Timer is not leaked.
        try { _knownDisplays.Dispose(); }
        catch
        {
            /* best-effort during shutdown */
        }

        // Tear down the throttlers - cancels any in-flight payload at its next dwell-await
        // and rejects further enqueues. In-flight DDC ops are bounded by per-monitor helper process timeouts.
        try { _writeThrottler.Dispose(); }
        catch
        {
            /* best-effort during shutdown */
        }

        try { _immediateWriteThrottler.Dispose(); }
        catch
        {
            /* best-effort during shutdown */
        }

        try { _refreshThrottler.Dispose(); }
        catch
        {
            /* best-effort during shutdown */
        }

        if (_display is IDisposable disposableDisplay)
        {
            try { disposableDisplay.Dispose(); }
            catch
            {
                /* best-effort during shutdown */
            }
        }

        // Release the per-monitor mutexes. Anything still holding one is in-flight;
        // SemaphoreSlim doesn't track owner so we can't preempt,
        // but the monitor's helper process timeout caps how long app-level callers wait.
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
    /// Sets the <c>_draining</c> flag so every public entry-point bails on new work, drains brightness and refresh
    /// schedulers, then polls <see cref="_activeDDCOps"/> until it hits zero or <paramref name="timeout"/> elapses.
    /// Returns true on clean drain, false on timeout.
    /// Caller should still proceed with shutdown; DisplayService kills the affected helper process for a stuck dxva2
    /// call.
    ///
    /// Idempotent: calling this multiple times is safe.
    /// Doesn't dispose anything; <see cref="Dispose"/> is the actual teardown step
    /// and should be called after a successful drain.
    /// </summary>
    public async Task<bool> BeginDrainAsync(TimeSpan timeout)
    {
        _draining = true;
        DateTime deadline = DateTime.UtcNow + timeout;

        // Drain the schedulers first so their driver loops stop scheduling new work,
        // then wait for any DDC ops they kicked off to finish or time out through their helper processes.
        TimeSpan remainingThrottlerBudget = deadline - DateTime.UtcNow;
        TimeSpan throttlerBudget = remainingThrottlerBudget > TimeSpan.Zero
            ? remainingThrottlerBudget
            : TimeSpan.Zero;
        using (CancellationTokenSource cts = new(throttlerBudget))
        {
            try
            {
                await Task.WhenAll(
                        _writeThrottler.DrainAsync(cts.Token),
                        _immediateWriteThrottler.DrainAsync(cts.Token),
                        _refreshThrottler.DrainAsync(cts.Token))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                WPFLog.Log("MonitorService.BeginDrainAsync: brightness scheduler drain timed out");
                return false;
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
    /// The func itself is sync, so we explicitly dispatch it via <see cref="Task.Run(Action)"/>.
    /// Without that extra hop, an uncontended <c>sem.WaitAsync()</c> can complete inline,
    /// which means the func then runs on the original calling thread -
    /// and if that's the UI thread (true on the kick path from <c>OnMonitorPropertyChanged</c>), the helper IPC
    /// wait blocks the UI for the whole DDC round-trip and the slider feels stuck.
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
        // DoBrightnessWriteAsync can stamp KnownDisplaysStore.LastBusBrightness after matching read-back
        // without a Monitors collection scan. Uses the port-form fallback when EDID identity is unavailable.
        public string EDIDKey = string.Empty;
        public DDCMonitor DDC = null!;

        public uint Max;

        // Brightness target lifecycle. Pending state suppresses duplicate queue entries while the target is
        // guaranteed to run; LastVerified suppresses steady curve samples only after matching read-back.
        // The generation makes stale normal/immediate/topology payloads unable to complete or re-apply.
        public readonly Lock BrightnessTargetGate = new();
        public long BrightnessTargetGeneration;
        public int PendingBrightnessPercentage = -1;
        public int LastVerifiedBrightnessPercentage = -1;
        public bool HasPendingBrightnessTarget;

        // Supersedes an older power verification loop before it can re-apply stale power intent.
        public long PowerIntentGeneration;

        // Negative values inherit the global setting; non-negative values are resolved from MonitorOverrides.
        public int ValidationDwellMs = -1;
        public int BrightnessDwellMs = -1;

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

    private readonly record struct BrightnessWriteTarget(long Generation, int Percentage);

    private enum PowerStateApplyOutcome
    {
        Applied,
        Failed,
        Superseded
    }

    private readonly record struct PowerStateApplyResult(PowerStateApplyOutcome Outcome, string? Error);

    private readonly record struct PowerWriteAttempt(
        bool WasAttempted,
        bool Success,
        string MonitorName,
        string? Error);

    private readonly record struct PowerReadBack(
        bool WasAttempted,
        bool Success,
        string MonitorName,
        uint Actual,
        string? Error);

    private readonly record struct DDCRecoveryIdentity(
        string DeviceID,
        string DisplayInstancePath,
        string EDIDSerial,
        string Name);

    private readonly record struct BrightnessWriteAttempt(
        bool WasAttempted,
        bool Success,
        string MonitorName,
        string? Error)
    {
        public static BrightnessWriteAttempt Superseded(string monitorName) =>
            new(false, false, monitorName, null);
    }

    private readonly record struct BrightnessReadBack(
        bool WasAttempted,
        bool Success,
        uint Actual,
        uint Maximum,
        string MonitorName,
        string? Error)
    {
        public static BrightnessReadBack Superseded(string monitorName) =>
            new(false, false, 0, 0, monitorName, null);
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
