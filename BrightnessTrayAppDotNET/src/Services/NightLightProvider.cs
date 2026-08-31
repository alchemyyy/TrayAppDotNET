using Avalonia.Threading;
using BrightnessTrayAppDotNET.Interop.NightLight;

namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Unified entry point for night-light control.
/// Dispatches to either the registry (CloudStore <c>BlueLightReduction</c> blobs, normal Windows path)
/// or the SettingsHandler DLL path,
/// based on <see cref="AppSettings.NightLightFallbackMode"/> and runtime availability.
/// Callers don't need to know which backend is active - they just call
/// <see cref="SetStrength(int)"/> / <see cref="SetEnabled"/> / <see cref="Toggle"/>
/// / <see cref="GetStrength"/> / <see cref="IsEnabled"/>
/// and the right thing happens.
///
/// Backend resolution is deferred until an explicit write while Night Light is active or being enabled. Reads and
/// capability checks remain non-invasive so merely starting the app cannot initialize Windows' Night Light state.
/// </summary>
internal static class NightLightProvider
{
    private enum Backend { None, Registry, SettingsHandler }

    private static AppSettings? _settings;
    private static readonly Lock _gate = new();
    private static Backend _lastResolvedBackend = Backend.None;
    private static bool _backendCached;
    private static NightLightFallbackMode _lastResolvedFallbackMode = NightLightFallbackMode.Auto;
    private static Timer? _lastStrengthSaveTimer;
    private static bool _lastStrengthSavePending;
    private static bool _lastStrengthSaveDispatchQueued;
    private static long _lastStrengthUpdateTick;

    /// <summary>
    /// Raised after a requested enabled-state transition is confirmed by the active backend.
    /// </summary>
    public static event Action? EnabledStateChanged;

    /// <summary>
    /// Wires the provider to the live <see cref="AppSettings"/>.
    /// Safe to call multiple times - it short-circuits on the same instance.
    /// Subscribes to <see cref="AppSettings.Changed"/>
    /// so a mode flip in the Settings window takes effect immediately, without a restart.
    /// </summary>
    public static void Initialize(AppSettings settings)
    {
        if (ReferenceEquals(_settings, settings)) return;

        FlushPendingLastStrengthSave(true);

        if (_settings != null) _settings.Changed -= OnSettingsChanged;

        _settings = settings;
        _settings.Changed += OnSettingsChanged;
        InvalidateBackendCache();
    }

    /// <summary>
    /// Detaches settings and stops reusable backend timers during app shutdown.
    /// </summary>
    public static void Shutdown()
    {
        AppSettings? settings;
        Timer? lastStrengthSaveTimer;
        bool shouldSaveLastStrength;
        lock (_gate)
        {
            settings = _settings;
            shouldSaveLastStrength = _lastStrengthSavePending && settings != null;
            _lastStrengthSavePending = false;
            _lastStrengthSaveDispatchQueued = false;
            lastStrengthSaveTimer = _lastStrengthSaveTimer;
            _lastStrengthSaveTimer = null;
            _settings = null;
            _backendCached = false;
            _lastResolvedBackend = Backend.None;
        }

        lastStrengthSaveTimer?.Dispose();
        if (shouldSaveLastStrength && settings != null)
            SaveLastStrength(settings);

        if (settings != null) settings.Changed -= OnSettingsChanged;

        CancelBackendResendTimers();
        NightLightRegistry.Shutdown();
        NightLightSettingsHandler.Shutdown();
    }

    /// <summary>
    /// Drops the cached backend resolution so the next public-API call re-probes the registry
    /// and the SettingsHandler DLL.
    /// Settings-driven invalidation (<see cref="AppSettings.NightLightFallbackMode"/> flips)
    /// is handled internally and doesn't need this hook.
    /// </summary>
    public static void InvalidateBackendCache()
    {
        lock (_gate)
        {
            _backendCached = false;
            _lastResolvedBackend = Backend.None;
        }
    }

    private static void OnSettingsChanged()
    {
        // ResolveBackend's only AppSettings input is NightLightFallbackMode; everything else is OS state.
        // Skip the re-probe when that hasn't changed -
        // unrelated settings (theme, brightness rate, hotkeys) raise the same parameterless event.
        NightLightFallbackMode currentMode = _settings?.NightLightFallbackMode ?? NightLightFallbackMode.Auto;
        if (_backendCached && currentMode == _lastResolvedFallbackMode) return;

        InvalidateBackendCache();
    }

    /// <summary>
    /// True when the selected backend can control Night Light on this machine. This probe is deliberately
    /// non-invasive: it never starts the native SettingsHandler helper, because initializing Microsoft's
    /// singleton against a fresh Windows profile can create transient state before the user requests a toggle.
    /// </summary>
    public static bool IsSupported()
    {
        NightLightFallbackMode mode = _settings?.NightLightFallbackMode ?? NightLightFallbackMode.Auto;
        return mode switch
        {
            NightLightFallbackMode.SettingsHandler => NightLightSettingsHandler.CanInitialize(),
            _ => NightLightRegistry.IsSupported()
        };
    }

    /// <summary>Current strength, 0-100. The Windows CloudStore registry is the source of truth.</summary>
    public static int GetStrength() => NightLightRegistry.GetStrength();

    /// <summary>True only when Windows Night Light is initialized and currently on.</summary>
    public static bool IsEnabled() => NightLightRegistry.IsEnabled();

    /// <summary>Sets the strength (0-100) on the active backend; preserves enabled state.</summary>
    public static void SetStrength(int percent) => SetStrength(percent, persistAsLastUserValue: true);

    /// <summary>
    /// As <see cref="SetStrength(int)"/>, but lets curve-driven callers opt out of persisting
    /// <see cref="AppSettings.NightLightLastNonZeroStrength"/>. User-selected values are saved once after
    /// the input burst goes quiet; curve samples opt out so they cannot overwrite user intent.
    /// </summary>
    public static void SetStrength(int percent, bool persistAsLastUserValue)
    {
        // Never resolve or initialize a write backend while Windows Night Light is off. This is the final
        // boundary for startup profile restores, environmental ticks, stale queued UI work, and hotkeys.
        if (!NightLightRegistry.IsEnabled())
        {
            CancelBackendResendTimers();
            return;
        }

        percent = Math.Clamp(percent, min: 0, max: 100);
        Backend backend = GetCachedBackend();
        if (backend == Backend.None) return;

        WriteStrength(backend, percent);

        if (persistAsLastUserValue)
            PersistLastUserStrength(percent);

        // Optional auto-off at zero. Stop queued/resend work before the state transition so a late strength
        // write cannot re-light the filter. ResolveEnableStrength restores the last non-zero warmth on the next
        // explicit enable.
        if (percent == 0
            && _settings is { TurnOffNightLightAtZeroStrength: true }
            && IsEnabled())
        {
            CancelBackendResendTimers();
            SetEnabled(false);
        }
    }

    /// <summary>
    /// Cancels any pending resend/settle-write timers on both possible backends so they can't race a
    /// just-issued off-flip. Cheap and idempotent - the timers are reusable
    /// <see cref="System.Threading.Timer"/>s that get re-armed on the next gesture.
    /// </summary>
    public static void CancelPendingStrengthWrites() => CancelBackendResendTimers();

    private static void CancelBackendResendTimers()
    {
        NightLightRegistry.CancelPendingResend();
        NightLightSettingsHandler.CancelPendingResend();
    }

    /// <summary>
    /// Turns night light on or off on the active backend.
    /// When <paramref name="enableStrength"/> is supplied, the SettingsHandler backend commits it before the
    /// active transition; the registry backend applies it immediately after the state transition because writes
    /// while disabled are intentionally prohibited.
    /// Otherwise, transitioning to enabled while the live strength is 0
    /// silently restores <see cref="AppSettings.NightLightLastNonZeroStrength"/> first -
    /// otherwise the user sees no visible change after toggling on, which feels broken.
    /// Toggling off preserves the live strength so the next toggle-on returns the user's same warmth.
    /// Returns true if the underlying backend wrote the requested state and the readback
    /// matched; false if the registry write failed, the readback diverged, or no backend is
    /// available. Failures are logged via <see cref="TADNLog"/>.
    /// </summary>
    public static bool SetEnabled(
        bool enabled,
        int? enableStrength = null,
        bool persistEnableStrengthAsLastUserValue = true)
    {
        bool wasEnabled = NightLightRegistry.IsEnabled();
        if (wasEnabled == enabled)
        {
            if (!enabled) CancelBackendResendTimers();
            return true;
        }

        // Turning off must remain available even if the selected strength backend cannot initialize. It also
        // must not start the native helper: the registry already contains an initialized live state, and a
        // single state transition is sufficient.
        if (!enabled)
        {
            CancelBackendResendTimers();
            bool disabled = NightLightRegistry.SetEnabled(false);
            CancelBackendResendTimers();
            return CompleteEnabledStateChange(disabled, enabled, Backend.Registry);
        }

        Backend backend = GetCachedBackend();
        int? strengthToApply = ResolveEnableStrength(enableStrength);

        bool ok = backend switch
        {
            Backend.Registry => EnableRegistryBackend(strengthToApply),
            Backend.SettingsHandler => NightLightSettingsHandler.SetEnabled(enabled: true, strengthToApply),
            _ => false
        };

        if (ok && strengthToApply.HasValue && persistEnableStrengthAsLastUserValue)
            PersistLastUserStrength(strengthToApply.Value);

        return CompleteEnabledStateChange(ok, enabled, backend);
    }

    private static int? ResolveEnableStrength(int? requestedStrength)
    {
        if (requestedStrength.HasValue)
            return Math.Clamp(requestedStrength.Value, min: 0, max: 100);

        int currentStrength = NightLightRegistry.GetStrength();
        if (currentStrength > 0) return null;

        return _settings?.NightLightLastNonZeroStrength is { } lastStrength and > 0
            ? Math.Clamp(lastStrength, min: 1, max: 100)
            : 50;
    }

    private static bool EnableRegistryBackend(int? strengthToApply)
    {
        bool enabled = NightLightRegistry.SetEnabled(true);
        if (!enabled) return false;

        if (strengthToApply.HasValue)
            NightLightRegistry.EnqueueSetStrengthSpaced(strengthToApply.Value);

        return true;
    }

    private static bool CompleteEnabledStateChange(bool ok, bool enabled, Backend backend)
    {
        // A backend acknowledgement can race the final CloudStore readback. Never report failure if the
        // requested initialized state is already visible in the registry.
        ok = ok || NightLightRegistry.IsEnabled() == enabled;

        if (!enabled) CancelBackendResendTimers();

        if (!ok)
        {
            TADNLog.Log(
                $"NightLightProvider.SetEnabled({enabled}) returned false on backend {backend} "
                + "(write rejected or readback diverged from request).");
        }
        else
            EnabledStateChanged?.Invoke();

        return ok;
    }

    /// <summary>
    /// Re-fires the current strength on the active backend.
    /// Used by display-topology hooks: after a relink/replug the GPU may have reset gamma,
    /// or the broker broadcast may have only reached some monitors -
    /// re-issuing the latest known strength forces a fresh CloudStore notification chain.
    /// No-op if no backend is active or night light isn't currently on.
    /// </summary>
    public static void Reapply()
    {
        if (!IsSupported() || !IsEnabled()) return;
        SetStrength(GetStrength());
    }

    /// <summary>
    /// Flips the enabled state on the active backend. Returns true if the toggle landed
    /// (post-write readback shows the inverted state), false on write failure, readback
    /// divergence, or no backend available. Failures are logged via <see cref="TADNLog"/>.
    /// Optional <paramref name="enableStrength"/> has the same curve-handoff semantics as
    /// <see cref="SetEnabled(bool, int?, bool)"/>.
    /// </summary>
    public static bool Toggle(
        int? enableStrength = null,
        bool persistEnableStrengthAsLastUserValue = true)
    {
        bool willEnable = !NightLightRegistry.IsEnabled();
        return SetEnabled(
            willEnable,
            willEnable ? enableStrength : null,
            persistEnableStrengthAsLastUserValue);
    }

    private static void WriteStrength(Backend backend, int percent)
    {
        switch (backend)
        {
            case Backend.Registry:
                // Spaced bracket: three SETTINGS writes (kelvin only -> kelvin + IsDragging=true ->
                // kelvin + IsDragging=false) gated by RegNotifyChangeKeyValue waits between them.
                // The IsDragging false->true edge triggers the broker's fb3daf apply lambda which
                // bypasses the wedged +36 inflight gate without flicker. Equivalent to the
                // SettingsHandler bracket but via raw registry writes only - no SettingsHandlers_Display
                // RVA dependency.
                NightLightRegistry.EnqueueSetStrengthSpaced(percent);
                break;
            case Backend.SettingsHandler:
                NightLightSettingsHandler.SetStrength(percent);
                break;
        }
    }

    private static void PersistLastUserStrength(int percent)
    {
        if (percent <= 0) return;

        Timer? saveTimer;
        lock (_gate)
        {
            AppSettings? settings = _settings;
            if (settings == null) return;

            if (settings.NightLightLastNonZeroStrength == percent && !_lastStrengthSavePending)
                return;

            settings.NightLightLastNonZeroStrength = percent;
            _lastStrengthSavePending = true;
            _lastStrengthUpdateTick = Environment.TickCount64;
            _lastStrengthSaveTimer ??= new Timer(
                OnLastStrengthSaveTimerFired,
                state: null,
                Timeout.Infinite,
                Timeout.Infinite);
            saveTimer = _lastStrengthSaveTimer;
        }

        try
        {
            saveTimer.Change(TimeConstants.NightLightLastStrengthSaveDebounceMs, Timeout.Infinite);
        }
        catch (ObjectDisposedException ex)
        {
            TADNLog.Log($"NightLightProvider.PersistLastUserStrength timer disposed: {ex.Message}");
        }
    }

    private static void OnLastStrengthSaveTimerFired(object? state)
    {
        lock (_gate)
        {
            if (!_lastStrengthSavePending || _lastStrengthSaveDispatchQueued) return;
            _lastStrengthSaveDispatchQueued = true;
        }

        try
        {
            Dispatcher.UIThread.Post(
                static () => FlushPendingLastStrengthSave(false),
                DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            lock (_gate)
                _lastStrengthSaveDispatchQueued = false;
            TADNLog.Log($"NightLightProvider last-strength dispatch failed: {ex.Message}");
        }
    }

    private static void FlushPendingLastStrengthSave(bool force)
    {
        AppSettings? settingsToSave = null;
        Timer? timerToRearm = null;
        int remainingDelayMs = 0;

        lock (_gate)
        {
            _lastStrengthSaveDispatchQueued = false;
            if (!_lastStrengthSavePending) return;

            long elapsedMs = Environment.TickCount64 - _lastStrengthUpdateTick;
            if (!force && elapsedMs < TimeConstants.NightLightLastStrengthSaveDebounceMs)
            {
                remainingDelayMs =
                    TimeConstants.NightLightLastStrengthSaveDebounceMs - (int)elapsedMs;
                timerToRearm = _lastStrengthSaveTimer;
            }
            else
            {
                settingsToSave = _settings;
                _lastStrengthSavePending = false;
            }
        }

        if (remainingDelayMs > 0 && timerToRearm != null)
        {
            try { timerToRearm.Change(remainingDelayMs, Timeout.Infinite); }
            catch (ObjectDisposedException ex)
            {
                TADNLog.Log($"NightLightProvider last-strength rearm failed: {ex.Message}");
            }

            return;
        }

        if (settingsToSave != null)
            SaveLastStrength(settingsToSave);
    }

    private static void SaveLastStrength(AppSettings settings)
    {
        try { settings.Save(); }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightProvider last-strength save failed: {ex.Message}");
        }
    }

    // -- Internals -------------------------------------------------------

    private static Backend GetCachedBackend()
    {
        lock (_gate)
        {
            if (_backendCached) return _lastResolvedBackend;

            NightLightFallbackMode mode = _settings?.NightLightFallbackMode ?? NightLightFallbackMode.Auto;
            Backend resolved;
            // Catch-and-fallback so a probe that throws still leaves us with a stable cached answer
            // (Backend.None) instead of re-throwing on every subsequent public-API call.
            try { resolved = ResolveBackend(); }
            catch (Exception ex)
            {
                TADNLog.Log($"NightLightProvider.GetCachedBackend probe: {ex.Message}");
                resolved = Backend.None;
            }

            _lastResolvedBackend = resolved;
            _lastResolvedFallbackMode = mode;
            _backendCached = true;
            return resolved;
        }
    }

    private static Backend ResolveBackend()
    {
        NightLightFallbackMode mode = _settings?.NightLightFallbackMode ?? NightLightFallbackMode.Auto;
        return mode switch
        {
            // SettingsHandler is an explicit user choice - if it isn't usable on this build,
            // report unsupported rather than silently swapping in the registry path.
            // The user picked it for the CloudStore-Save side effects; falling back to raw registry
            // would be a different behavior under the same UI affordance.
            NightLightFallbackMode.SettingsHandler => NightLightSettingsHandler.IsSupported()
                ? Backend.SettingsHandler
                : Backend.None,
            // GammaRamp is a hidden UI affordance with no backing implementation right now -
            // fall through to the registry path so the toggle effectively no-ops.
            _ => NightLightRegistry.IsSupported() ? Backend.Registry : Backend.None
        };
    }
}
