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
/// Backend resolution runs lazily on first use,
/// and is recomputed whenever the user changes the fallback mode.
/// </summary>
internal static class NightLightProvider
{
    private enum Backend { None, Registry, SettingsHandler }

    private static AppSettings? _settings;
    private static readonly Lock _gate = new();
    private static Backend _lastResolvedBackend = Backend.None;
    private static bool _backendCached;
    private static NightLightFallbackMode _lastResolvedFallbackMode = NightLightFallbackMode.Auto;

    /// <summary>
    /// Wires the provider to the live <see cref="AppSettings"/>.
    /// Safe to call multiple times - it short-circuits on the same instance.
    /// Subscribes to <see cref="AppSettings.Changed"/>
    /// so a mode flip in the Settings window takes effect immediately, without a restart.
    /// </summary>
    public static void Initialize(AppSettings settings)
    {
        if (ReferenceEquals(_settings, settings)) return;

        if (_settings != null) _settings.Changed -= OnSettingsChanged;

        _settings = settings;
        _settings.Changed += OnSettingsChanged;
        InvalidateBackendCache();
        _ = GetCachedBackend();
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
        _ = GetCachedBackend();
    }

    /// <summary>
    /// True when at least one backend is usable on the current machine.
    /// </summary>
    public static bool IsSupported() => GetCachedBackend() != Backend.None;

    /// <summary>Current strength, 0-100. Reads from whichever backend owns the truth.</summary>
    public static int GetStrength()
    {
        return GetCachedBackend() switch
        {
            Backend.Registry => NightLightRegistry.GetStrength(),
            Backend.SettingsHandler => NightLightSettingsHandler.GetStrength(),
            _ => 0,
        };
    }

    /// <summary>True when night light is currently on.</summary>
    public static bool IsEnabled()
    {
        return GetCachedBackend() switch
        {
            Backend.Registry => NightLightRegistry.IsEnabled(),
            Backend.SettingsHandler => NightLightSettingsHandler.IsEnabled(),
            _ => false,
        };
    }

    /// <summary>Sets the strength (0-100) on the active backend; preserves enabled state.</summary>
    public static void SetStrength(int percent) => SetStrength(percent, persistAsLastUserValue: true);

    /// <summary>
    /// As <see cref="SetStrength(int)"/>, but lets curve-driven callers opt out of persisting
    /// <see cref="AppSettings.NightLightLastNonZeroStrength"/>. The save is a synchronous XML write
    /// on the calling thread; the env curve service hits the UI thread up to ~100x during a 10s
    /// preview sweep, so persisting transient curve samples would both jitter the dispatcher and
    /// pollute "last user-chosen warmth" with values the user never picked.
    /// </summary>
    public static void SetStrength(int percent, bool persistAsLastUserValue)
    {
        percent = Math.Clamp(percent, 0, 100);
        switch (GetCachedBackend())
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

        if (persistAsLastUserValue
            && percent > 0
            && _settings != null
            && _settings.NightLightLastNonZeroStrength != percent)
        {
            // Update the property under the gate, then Save outside the gate. AppSettings.Save is a
            // synchronous XML write on the calling thread (UI thread on slider drags); holding _gate across
            // it would block backend-cache resolution / settings-changed reentrancy for the full I/O.
            AppSettings settings = _settings;
            lock (_gate) settings.NightLightLastNonZeroStrength = percent;
            try { settings.Save(); }
            catch (Exception ex)
            {
                WPFLog.Log($"NightLightProvider.SetStrength persist last-strength: {ex.Message}");
            }
        }

        // Optional auto-off at zero.
        // Runs after the strength write so the backends settle on 0 first, then we flip the on/off bit.
        // EnsureNonZeroStrengthBeforeEnable on the next toggle-on restores the user's last warmth,
        // so this round-trips cleanly.
        //
        // Order matters: stop the resend timers BEFORE SetEnabled(false). Both backends arm a delayed
        // re-fire that assumes on-state semantics; letting one race the off-flip can re-light the filter
        // moments after we turn it off.
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
    /// Transitioning to enabled while the live strength is 0
    /// silently restores <see cref="AppSettings.NightLightLastNonZeroStrength"/> first -
    /// otherwise the user sees no visible change after toggling on, which feels broken.
    /// Toggling off preserves the live strength so the next toggle-on returns the user's same warmth.
    /// Returns true if the underlying backend wrote the requested state and the readback
    /// matched; false if the registry write failed, the readback diverged, or no backend is
    /// available. Failures are logged via <see cref="WPFLog"/>.
    /// </summary>
    public static bool SetEnabled(bool enabled)
    {
        Backend backend = GetCachedBackend();
        if (enabled) EnsureNonZeroStrengthBeforeEnable(backend);
        else CancelBackendResendTimers();

        bool ok = backend switch
        {
            Backend.Registry => NightLightRegistry.SetEnabled(enabled),
            Backend.SettingsHandler => NightLightSettingsHandler.SetEnabled(enabled),
            _ => false,
        };

        // SettingsHandler.SetEnabled arms its own deferred registry settle-write even on off transitions.
        // Cancel again after the backend call so a just-created timer cannot re-fire into the off state.
        if (!enabled) CancelBackendResendTimers();

        if (!ok)
        {
            WPFLog.Log(
                $"NightLightProvider.SetEnabled({enabled}) returned false on backend {backend} "
                + "(write rejected or readback diverged from request).");
        }

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
    /// divergence, or no backend available. Failures are logged via <see cref="WPFLog"/>.
    /// </summary>
    public static bool Toggle()
    {
        Backend backend = GetCachedBackend();
        bool willEnable = !IsEnabled();
        if (willEnable) EnsureNonZeroStrengthBeforeEnable(backend);
        else CancelBackendResendTimers();

        bool ok = backend switch
        {
            Backend.Registry => NightLightRegistry.Toggle(),
            Backend.SettingsHandler => NightLightSettingsHandler.Toggle(),
            _ => false,
        };

        // See SetEnabled(false): the SettingsHandler backend may arm a settle-write during the off flip.
        if (!willEnable) CancelBackendResendTimers();

        if (!ok)
        {
            WPFLog.Log(
                $"NightLightProvider.Toggle returned false on backend {backend} "
                + "(write rejected or readback didn't show the flip).");
        }

        return ok;
    }

    private static void EnsureNonZeroStrengthBeforeEnable(Backend backend)
    {
        int currentStrength = backend switch
        {
            Backend.Registry => NightLightRegistry.GetStrength(),
            Backend.SettingsHandler => NightLightSettingsHandler.GetStrength(),
            _ => 0,
        };
        if (currentStrength > 0) return;

        int restored = _settings?.NightLightLastNonZeroStrength is { } last and > 0 ? last : 50;
        // Use the spaced/throttled write rather than the bare synchronous SetStrength: the registry path's
        // bare write triggers the wedged-+36-inflight broker bug and visibly flickers on toggle-on. The
        // spaced bracket bypasses that gate via the IsDragging false->true edge. SettingsHandler's
        // SetStrength is already the throttled CloudStore-bracket entry point.
        switch (backend)
        {
            case Backend.Registry:
                NightLightRegistry.EnqueueSetStrengthSpaced(restored);
                break;
            case Backend.SettingsHandler:
                NightLightSettingsHandler.SetStrength(restored);
                break;
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
                WPFLog.Log($"NightLightProvider.GetCachedBackend probe: {ex.Message}");
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
            _ => NightLightRegistry.IsSupported() ? Backend.Registry : Backend.None,
        };
    }
}
