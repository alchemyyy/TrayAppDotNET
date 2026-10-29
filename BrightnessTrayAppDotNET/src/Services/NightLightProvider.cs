using Avalonia.Threading;
using BrightnessTrayAppDotNET.Interop.NightLight;

namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Unified entry point for night-light control.
/// Dispatches to the registry (CloudStore <c>BlueLightReduction</c> blobs, normal Windows path),
/// the SettingsHandler DLL path, or the app's own gamma-ramp tint,
/// based on <see cref="AppSettings.NightLightFallbackMode"/> and runtime availability.
/// Callers don't need to know which backend is active - they just call
/// <see cref="SetStrength(int)"/> / <see cref="SetEnabled"/> / <see cref="Toggle"/>
/// / <see cref="GetStrength"/> / <see cref="IsEnabled"/>
/// and the right thing happens.
///
/// For the Windows backends, the Windows CloudStore registry is the source of truth for on/off and strength.
/// Backend resolution is deferred until an explicit write while Night Light is active or being enabled. Reads and
/// capability checks remain non-invasive so merely starting the app cannot initialize Windows' Night Light state.
///
/// For the gamma-ramp backend, Windows knows nothing about the tint, so this class owns on/off and strength
/// (<see cref="AppSettings.NightLightGammaRampEnabled"/>, <see cref="AppSettings.NightLightGammaRampStrength"/>),
/// persists them, and reapplies them at startup. Switching to that backend adopts Windows Night Light's current
/// on/off state and strength; switching away restores the identity ramp and leaves Windows Night Light off.
/// </summary>
internal static class NightLightProvider
{
    internal enum Backend { None, Registry, SettingsHandler, GammaRamp }

    private static readonly Func<bool> SettingsHandlerSupportedProbe = NightLightSettingsHandler.IsSupported;
    private static readonly Func<bool> RegistrySupportedProbe = NightLightRegistry.IsSupported;

    private static AppSettings? _settings;
    private static readonly Lock _gate = new();
    private static Backend _lastResolvedBackend = Backend.None;
    private static bool _backendCached;
    private static NightLightFallbackMode _lastResolvedFallbackMode = NightLightFallbackMode.Auto;

    // True while the gamma-ramp backend owns the Night Light state. Changed only on the settings thread.
    private static volatile bool _gammaRampOwnsState;

    private static Timer? _stateSaveTimer;
    private static bool _stateSavePending;
    private static bool _stateSaveDispatchQueued;
    private static long _stateUpdateTick;

    /// <summary>
    /// Raised after a requested enabled-state transition is confirmed by the active backend, and after a backend
    /// switch changes which state the reads report.
    /// </summary>
    public static event Action? EnabledStateChanged;

    /// <summary>
    /// Wires the provider to the live <see cref="AppSettings"/>.
    /// Safe to call multiple times - it short-circuits on the same instance.
    /// Subscribes to <see cref="AppSettings.Changed"/>
    /// so a mode flip in the Settings window takes effect immediately, without a restart.
    /// With the gamma-ramp backend selected and its persisted state on, this puts the tint back on screen.
    /// </summary>
    public static void Initialize(AppSettings settings)
    {
        if (ReferenceEquals(_settings, settings)) return;

        FlushPendingStateSave(true);

        if (_settings != null) _settings.Changed -= OnSettingsChanged;
        ReleaseGammaRamp();

        _settings = settings;
        _settings.Changed += OnSettingsChanged;
        InvalidateBackendCache();
        SyncGammaRampOwnership(adoptWindowsState: false);
    }

    /// <summary>
    /// Detaches settings and stops reusable backend timers during app shutdown. The gamma-ramp tint is taken off
    /// screen, but its persisted on/off state is kept so the next start reapplies it.
    /// </summary>
    public static void Shutdown()
    {
        AppSettings? settings;
        Timer? stateSaveTimer;
        bool shouldSaveState;
        lock (_gate)
        {
            settings = _settings;
            shouldSaveState = _stateSavePending && settings != null;
            _stateSavePending = false;
            _stateSaveDispatchQueued = false;
            stateSaveTimer = _stateSaveTimer;
            _stateSaveTimer = null;
            _settings = null;
            _backendCached = false;
            _lastResolvedBackend = Backend.None;
        }

        stateSaveTimer?.Dispose();
        if (shouldSaveState && settings != null)
            SaveState(settings);

        if (settings != null) settings.Changed -= OnSettingsChanged;

        ReleaseGammaRamp();
        GammaRampNightLight.Shutdown();
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
        SyncGammaRampOwnership(adoptWindowsState: true);

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
    /// The gamma-ramp backend is always available; displays it can't tint (HDR) are skipped when it writes.
    /// </summary>
    public static bool IsSupported()
    {
        if (_gammaRampOwnsState) return true;

        NightLightFallbackMode mode = _settings?.NightLightFallbackMode ?? NightLightFallbackMode.Auto;
        return mode switch
        {
            NightLightFallbackMode.SettingsHandler => NightLightSettingsHandler.CanInitialize(),
            _ => NightLightRegistry.IsSupported()
        };
    }

    /// <summary>
    /// Current strength, 0-100. The Windows CloudStore registry is the source of truth, except under the
    /// gamma-ramp backend, where the app's own persisted strength is.
    /// </summary>
    public static int GetStrength() =>
        GammaRampSettings is { } gammaSettings
            ? gammaSettings.NightLightGammaRampStrength
            : NightLightRegistry.GetStrength();

    /// <summary>
    /// True only when Windows Night Light is initialized and currently on, or, under the gamma-ramp backend,
    /// when the app's own tint is on.
    /// </summary>
    public static bool IsEnabled() =>
        GammaRampSettings is { } gammaSettings
            ? gammaSettings.NightLightGammaRampEnabled
            : NightLightRegistry.IsEnabled();

    /// <summary>Sets the strength (0-100) on the active backend; preserves enabled state.</summary>
    public static void SetStrength(int percent) => SetStrength(percent, persistAsLastUserValue: true);

    /// <summary>
    /// As <see cref="SetStrength(int)"/>, but lets curve-driven callers opt out of persisting
    /// <see cref="AppSettings.NightLightLastNonZeroStrength"/>. User-selected values are saved once after
    /// the input burst goes quiet; curve samples opt out so they cannot overwrite user intent.
    /// </summary>
    public static void SetStrength(int percent, bool persistAsLastUserValue)
    {
        if (GammaRampSettings is { } gammaSettings)
        {
            SetGammaRampStrength(gammaSettings, percent, persistAsLastUserValue);
            return;
        }

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
    /// while disabled are intentionally prohibited; the gamma-ramp backend activates directly at that strength.
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
        if (GammaRampSettings is { } gammaSettings)
            return SetGammaRampEnabled(gammaSettings, enabled, enableStrength, persistEnableStrengthAsLastUserValue);

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

        int currentStrength = GetStrength();
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
        ok = ok || IsEnabled() == enabled;

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
    /// Used by display-topology hooks (which also cover resume and session unlock): after a relink/replug the GPU
    /// may have reset gamma, or the broker broadcast may have only reached some monitors -
    /// re-issuing the latest known strength forces a fresh CloudStore notification chain.
    /// The gamma-ramp backend re-enumerates displays and rewrites every SDR display's ramp.
    /// No-op if no backend is active or night light isn't currently on.
    /// </summary>
    public static void Reapply()
    {
        if (!IsSupported() || !IsEnabled()) return;

        if (_gammaRampOwnsState)
        {
            GammaRampNightLight.Refresh();
            return;
        }

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
        bool willEnable = !IsEnabled();
        return SetEnabled(
            willEnable,
            willEnable ? enableStrength : null,
            persistEnableStrengthAsLastUserValue);
    }

    // -- Gamma-ramp backend ----------------------------------------------

    private static AppSettings? GammaRampSettings => _gammaRampOwnsState ? _settings : null;

    /// <summary>
    /// Moves Night Light ownership between Windows and the gamma-ramp backend when
    /// <see cref="AppSettings.NightLightFallbackMode"/> crosses the GammaRamp boundary.
    /// At startup (<paramref name="adoptWindowsState"/> false) the persisted gamma state is reapplied as-is.
    /// On a live switch to GammaRamp, Windows Night Light's on/off state and strength carry over, so the tint
    /// changes hands at the same warmth; activation then turns Windows Night Light off.
    /// On a switch away, the identity ramp is restored and the gamma state cleared; Windows Night Light stays off
    /// until the user turns it on.
    /// </summary>
    private static void SyncGammaRampOwnership(bool adoptWindowsState)
    {
        AppSettings? settings = _settings;
        if (settings == null) return;

        bool wantGamma = settings.NightLightFallbackMode == NightLightFallbackMode.GammaRamp;
        if (wantGamma == _gammaRampOwnsState) return;

        if (wantGamma)
        {
            if (adoptWindowsState) AdoptWindowsNightLightState(settings);

            _gammaRampOwnsState = true;
            if (settings.NightLightGammaRampEnabled)
                GammaRampNightLight.Activate(settings.NightLightGammaRampStrength);
            TADNLog.Log(
                $"NightLightProvider: gamma-ramp backend owns Night Light (on={settings.NightLightGammaRampEnabled}, "
                + $"strength={settings.NightLightGammaRampStrength})");
        }
        else
        {
            _gammaRampOwnsState = false;
            GammaRampNightLight.Deactivate();
            settings.NightLightGammaRampEnabled = false;
            TADNLog.Log("NightLightProvider: gamma-ramp backend released Night Light");
        }

        if (adoptWindowsState) ScheduleStateSave();
        EnabledStateChanged?.Invoke();
    }

    private static void AdoptWindowsNightLightState(AppSettings settings)
    {
        bool windowsOn = NightLightRegistry.IsEnabled();
        settings.NightLightGammaRampEnabled = windowsOn;
        if (windowsOn)
            settings.NightLightGammaRampStrength = NightLightRegistry.GetStrength();
    }

    // Takes the tint off screen without touching the persisted gamma state (shutdown, settings instance swap)
    private static void ReleaseGammaRamp()
    {
        if (!_gammaRampOwnsState) return;

        _gammaRampOwnsState = false;
        GammaRampNightLight.Deactivate();
    }

    private static void SetGammaRampStrength(AppSettings settings, int percent, bool persistAsLastUserValue)
    {
        // Same boundary as the Windows path: strength writes never turn the tint on
        if (!settings.NightLightGammaRampEnabled) return;

        percent = Math.Clamp(percent, min: 0, max: 100);
        GammaRampNightLight.QueueStrength(percent);
        if (settings.NightLightGammaRampStrength != percent)
        {
            settings.NightLightGammaRampStrength = percent;
            ScheduleStateSave();
        }

        if (persistAsLastUserValue)
            PersistLastUserStrength(percent);

        if (percent == 0 && settings.TurnOffNightLightAtZeroStrength)
            SetEnabled(false);
    }

    private static bool SetGammaRampEnabled(
        AppSettings settings,
        bool enabled,
        int? enableStrength,
        bool persistEnableStrengthAsLastUserValue)
    {
        if (settings.NightLightGammaRampEnabled == enabled) return true;

        if (!enabled)
        {
            GammaRampNightLight.Deactivate();
            settings.NightLightGammaRampEnabled = false;
            ScheduleStateSave();
            return CompleteEnabledStateChange(ok: true, enabled: false, Backend.GammaRamp);
        }

        int? strengthToApply = ResolveEnableStrength(enableStrength);
        if (strengthToApply.HasValue)
            settings.NightLightGammaRampStrength = strengthToApply.Value;

        settings.NightLightGammaRampEnabled = true;
        GammaRampNightLight.Activate(settings.NightLightGammaRampStrength);
        ScheduleStateSave();

        if (strengthToApply.HasValue && persistEnableStrengthAsLastUserValue)
            PersistLastUserStrength(strengthToApply.Value);

        return CompleteEnabledStateChange(ok: true, enabled: true, Backend.GammaRamp);
    }

    // -- Windows backends ------------------------------------------------

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

    // -- Persistence -----------------------------------------------------

    private static void PersistLastUserStrength(int percent)
    {
        if (percent <= 0) return;

        lock (_gate)
        {
            AppSettings? settings = _settings;
            if (settings == null) return;

            if (settings.NightLightLastNonZeroStrength == percent && !_stateSavePending)
                return;

            settings.NightLightLastNonZeroStrength = percent;
        }

        ScheduleStateSave();
    }

    /// <summary>
    /// Saves provider-owned settings (last non-zero strength, gamma-ramp state) once input goes quiet for
    /// <see cref="TimeConstants.NightLightStateSaveDebounceMs"/>. Shutdown flushes a pending save.
    /// </summary>
    private static void ScheduleStateSave()
    {
        Timer? saveTimer;
        lock (_gate)
        {
            if (_settings == null) return;

            _stateSavePending = true;
            _stateUpdateTick = Environment.TickCount64;
            _stateSaveTimer ??= new Timer(
                OnStateSaveTimerFired,
                state: null,
                Timeout.Infinite,
                Timeout.Infinite);
            saveTimer = _stateSaveTimer;
        }

        try
        {
            saveTimer.Change(TimeConstants.NightLightStateSaveDebounceMs, Timeout.Infinite);
        }
        catch (ObjectDisposedException ex)
        {
            TADNLog.Log($"NightLightProvider.ScheduleStateSave timer disposed: {ex.Message}");
        }
    }

    private static void OnStateSaveTimerFired(object? state)
    {
        lock (_gate)
        {
            if (!_stateSavePending || _stateSaveDispatchQueued) return;
            _stateSaveDispatchQueued = true;
        }

        try
        {
            Dispatcher.UIThread.Post(
                static () => FlushPendingStateSave(false),
                DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            lock (_gate)
                _stateSaveDispatchQueued = false;
            TADNLog.Log($"NightLightProvider state-save dispatch failed: {ex.Message}");
        }
    }

    private static void FlushPendingStateSave(bool force)
    {
        AppSettings? settingsToSave = null;
        Timer? timerToRearm = null;
        int remainingDelayMs = 0;

        lock (_gate)
        {
            _stateSaveDispatchQueued = false;
            if (!_stateSavePending) return;

            long elapsedMs = Environment.TickCount64 - _stateUpdateTick;
            if (!force && elapsedMs < TimeConstants.NightLightStateSaveDebounceMs)
            {
                remainingDelayMs =
                    TimeConstants.NightLightStateSaveDebounceMs - (int)elapsedMs;
                timerToRearm = _stateSaveTimer;
            }
            else
            {
                settingsToSave = _settings;
                _stateSavePending = false;
            }
        }

        if (remainingDelayMs > 0 && timerToRearm != null)
        {
            try { timerToRearm.Change(remainingDelayMs, Timeout.Infinite); }
            catch (ObjectDisposedException ex)
            {
                TADNLog.Log($"NightLightProvider state-save rearm failed: {ex.Message}");
            }

            return;
        }

        if (settingsToSave != null)
            SaveState(settingsToSave);
    }

    private static void SaveState(AppSettings settings)
    {
        try { settings.Save(); }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightProvider state save failed: {ex.Message}");
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
            try { resolved = ResolveBackend(mode, SettingsHandlerSupportedProbe, RegistrySupportedProbe); }
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

    /// <summary>
    /// Maps the selected mode to a backend. Probes run only for the mode that needs them.
    /// </summary>
    internal static Backend ResolveBackend(
        NightLightFallbackMode mode,
        Func<bool> isSettingsHandlerSupported,
        Func<bool> isRegistrySupported) =>
        mode switch
        {
            // SettingsHandler is an explicit user choice - if it isn't usable on this build,
            // report unsupported rather than silently swapping in the registry path.
            // The user picked it for the CloudStore-Save side effects; falling back to raw registry
            // would be a different behavior under the same UI affordance.
            NightLightFallbackMode.SettingsHandler => isSettingsHandlerSupported()
                ? Backend.SettingsHandler
                : Backend.None,
            // GammaRamp writes the GPU's gamma LUT itself and needs neither Windows Night Light path,
            // so it's available even when the registry state is uninitialized or the service is stuck.
            NightLightFallbackMode.GammaRamp => Backend.GammaRamp,
            // Registry and the legacy Auto value
            _ => isRegistrySupported() ? Backend.Registry : Backend.None
        };
}
