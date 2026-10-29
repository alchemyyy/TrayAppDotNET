namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Night Light by GDI gamma ramp: writes a warm 3x256 ramp to every active SDR display with
/// <c>SetDeviceGammaRamp</c> instead of going through Windows Night Light.
/// Windows knows nothing about this tint, so on/off and strength are owned by
/// <see cref="Services.NightLightProvider"/> (persisted in <see cref="AppSettings"/>); this class only puts the
/// requested ramp on screen and keeps it there.
///
/// Rules:
/// <list type="bullet">
/// <item>Advanced color (HDR) sources are skipped and logged; the GDI ramp has no effect there.</item>
/// <item>A refused ramp (Windows limits how far a ramp may stray from identity unless
/// <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\GdiIcmGammaRange</c> is 256) backs off to the strongest
/// strength that display accepts, see <see cref="GammaRampMath.ApplyWithBackoff{TWriter}"/>. That cap is cached
/// per display until the next activation or display refresh.</item>
/// <item>A watchdog compares each display's live ramp with the one written and rewrites it when something else
/// (a game, a driver, a color-management loader) replaced it.</item>
/// <item>While the tint is active, Windows Night Light is turned off on activation, on every display refresh, and
/// whenever the watchdog sees it on; the two would otherwise stack on the same lookup table. A failed turn-off is
/// not retried until Windows Night Light is seen off again, so a profile where the registry write fails is not
/// rewritten every tick. All turn-off attempts are serialized, because a turn-off is a registry toggle and two
/// concurrent ones would switch it back on.</item>
/// <item>Only displays this class wrote to are restored to the identity ramp on deactivation, so a calibration
/// ramp on a display that was never tinted stays untouched. A written display stays tracked for that restore even
/// when it drops out of the active set or display enumeration fails.</item>
/// <item>A killed process can't restore anything, so the uninstall path calls
/// <see cref="RestoreIdentityOnActiveDisplays"/> after the installer has stopped every instance.</item>
/// </list>
/// Slider-rate writes go through a latest-wins queue drained on the thread pool; activation, refresh, and
/// deactivation run synchronously on the caller.
/// </summary>
internal static class GammaRampNightLight
{
    // Above any driver quantization of a 16-bit ramp, far below the smallest visible tint step
    private const int WatchdogRampTolerance = 512;

    private static readonly Lock _gate = new();
    private static readonly Lock _requestGate = new();
    private static readonly List<DisplayRampState> _displays = [];
    private static readonly ushort[] _writeRamp = new ushort[GammaRampMath.RampLength];
    private static readonly ushort[] _readbackRamp = new ushort[GammaRampMath.RampLength];
    private static readonly WaitCallback DrainCallback = static _ => DrainQueuedStrength();
    private static readonly TimerCallback WatchdogCallback = static _ => OnWatchdogTick();

    private static bool _active;
    private static int _strength;
    private static Timer? _watchdogTimer;
    private static long _lastWatchdogRestoreLogTick;

    // Bumped on every activate/deactivate so a queued write taken before an off/on cycle can't land after it
    private static int _activationGeneration;

    private static bool _hasPendingStrength;
    private static int _pendingStrength;
    private static int _pendingGeneration;
    private static bool _drainScheduled;

    private static readonly Lock _windowsNightLightGate = new();

    // A turn-off failed during the current Windows Night Light on-period; guarded by _windowsNightLightGate
    private static bool _windowsNightLightOffFailed;

    /// <summary>True while a tint is supposed to be on screen.</summary>
    public static bool IsActive
    {
        get
        {
            lock (_gate)
                return _active;
        }
    }

    /// <summary>
    /// Turns the tint on at <paramref name="strength"/> (0-100): turns Windows Night Light off when it's on,
    /// enumerates displays, writes every SDR display, and starts the watchdog.
    /// </summary>
    public static void Activate(int strength)
    {
        ClearPendingStrength();
        EnforceWindowsNightLightOff(force: true);

        lock (_gate)
        {
            _active = true;
            _activationGeneration++;
            _strength = Math.Clamp(strength, min: 0, max: 100);
            RefreshDisplaysLocked();
            ApplyLocked(_strength);
            ArmWatchdogLocked();
        }

        TADNLog.Log($"GammaRampNightLight: active at strength {strength}");
    }

    /// <summary>
    /// Queues a strength change while active. Latest value wins; the write runs on the thread pool so slider
    /// drags never wait on the driver. No-op while inactive.
    /// </summary>
    public static void QueueStrength(int strength)
    {
        bool schedule;
        lock (_requestGate)
        {
            _pendingStrength = Math.Clamp(strength, min: 0, max: 100);
            _pendingGeneration = Volatile.Read(ref _activationGeneration);
            _hasPendingStrength = true;
            schedule = !_drainScheduled;
            _drainScheduled = true;
        }

        if (schedule) ThreadPool.UnsafeQueueUserWorkItem(DrainCallback, state: null);
    }

    /// <summary>
    /// Re-enumerates displays and rewrites the current tint. Called after display topology changes, resume, and
    /// session unlock, any of which can reset the ramp or add a display. No-op while inactive.
    /// </summary>
    public static void Refresh()
    {
        if (!IsActive) return;

        EnforceWindowsNightLightOff(force: true);
        lock (_gate)
        {
            if (!_active) return;
            RefreshDisplaysLocked();
            ApplyLocked(_strength);
        }
    }

    /// <summary>
    /// Turns the tint off: stops the watchdog and restores the identity ramp on every display this backend wrote.
    /// Safe to call when already inactive.
    /// </summary>
    public static void Deactivate()
    {
        ClearPendingStrength();
        bool wasActive;
        lock (_gate)
        {
            wasActive = _active;
            _active = false;
            _activationGeneration++;
            _watchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            RestoreIdentityLocked();
        }

        if (wasActive) TADNLog.Log("GammaRampNightLight: inactive, identity ramps restored");
    }

    /// <summary>Deactivates and releases the watchdog timer. Used on app exit.</summary>
    public static void Shutdown()
    {
        Deactivate();
        Timer? timer;
        lock (_gate)
        {
            timer = _watchdogTimer;
            _watchdogTimer = null;
        }

        timer?.Dispose();
    }

    /// <summary>
    /// Restores the identity ramp on every active SDR display, whoever tinted it. Used by the uninstall path, which
    /// runs in its own process after the installer killed every instance (so no instance could restore its own
    /// ramps). Callers decide from the persisted settings that the gamma tint was on, see
    /// <see cref="ShouldRestoreAfterForcedStop"/>, so displays of users who never used this backend keep their
    /// calibration ramps.
    /// </summary>
    public static void RestoreIdentityOnActiveDisplays()
    {
        List<GammaRampDisplay>? displays = GammaRampNative.EnumerateActiveDisplays();
        if (displays == null)
        {
            TADNLog.Log("GammaRampNightLight: uninstall restore skipped, display enumeration failed");
            return;
        }

        lock (_gate)
        {
            foreach (GammaRampDisplay display in displays)
            {
                if (display.IsAdvancedColor) continue;
                WriteIdentityLocked(new DisplayRampState(display.DeviceName) { HasWritten = true });
            }
        }

        TADNLog.Log($"GammaRampNightLight: uninstall restored identity ramps on {displays.Count} display(s)");
    }

    /// <summary>True when persisted settings say the gamma tint was on, so a killed instance may have left it.</summary>
    internal static bool ShouldRestoreAfterForcedStop(NightLightFallbackMode mode, bool gammaRampEnabled) =>
        mode == NightLightFallbackMode.GammaRamp && gammaRampEnabled;

    /// <summary>
    /// Whether to try turning Windows Night Light off: it's on, and either the caller forces it (activation,
    /// display refresh) or no turn-off has failed during the current on-period.
    /// </summary>
    internal static bool ShouldTurnOffWindowsNightLight(bool windowsNightLightOn, bool offFailed, bool force) =>
        windowsNightLightOn && (force || !offFailed);

    /// <summary>
    /// Next value of the failure latch. Only a failed attempt latches; a successful one leaves the next check free
    /// to act if Windows Night Light comes straight back on. Seeing it off clears the latch.
    /// </summary>
    internal static bool NextWindowsNightLightOffFailed(
        bool windowsNightLightOn,
        bool attempted,
        bool turnedOff,
        bool offFailed) =>
        windowsNightLightOn && (attempted ? !turnedOff : offFailed);

    /// <summary>
    /// Folds a display enumeration into the tracked states. A failed (null) or empty enumeration changes nothing,
    /// so written displays are never forgotten. Enumerated displays are marked present with their back-off cap
    /// reset; a written display missing from the set stays tracked as absent so deactivation still restores it.
    /// Returns false when the enumeration was not usable.
    /// </summary>
    internal static bool MergeDisplays(List<DisplayRampState> displays, IReadOnlyList<GammaRampDisplay>? enumerated)
    {
        if (enumerated is not { Count: > 0 }) return false;

        List<DisplayRampState> previous = [.. displays];
        displays.Clear();

        foreach (GammaRampDisplay display in enumerated)
        {
            DisplayRampState state =
                FindState(previous, display.DeviceName) ?? new DisplayRampState(display.DeviceName);
            state.IsPresent = true;
            state.IsAdvancedColor = display.IsAdvancedColor;
            state.MaxAcceptedStrength = 100;
            state.LoggedRefusal = false;
            displays.Add(state);
        }

        foreach (DisplayRampState state in previous)
        {
            if (!state.HasWritten || FindState(displays, state.DeviceName) != null) continue;

            state.IsPresent = false;
            state.HasExpectedRamp = false;
            displays.Add(state);
        }

        return true;
    }

    private static DisplayRampState? FindState(List<DisplayRampState> states, string deviceName)
    {
        foreach (DisplayRampState state in states)
        {
            if (string.Equals(state.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                return state;
        }

        return null;
    }

    private static void ClearPendingStrength()
    {
        lock (_requestGate)
            _hasPendingStrength = false;
    }

    private static void DrainQueuedStrength()
    {
        while (true)
        {
            int strength;
            int generation;
            lock (_requestGate)
            {
                if (!_hasPendingStrength)
                {
                    _drainScheduled = false;
                    return;
                }

                strength = _pendingStrength;
                generation = _pendingGeneration;
                _hasPendingStrength = false;
            }

            try
            {
                lock (_gate)
                {
                    if (!_active || generation != _activationGeneration) continue;
                    _strength = strength;
                    ApplyLocked(strength);
                }
            }
            catch (Exception ex)
            {
                TADNLog.Log($"GammaRampNightLight.DrainQueuedStrength: {ex}");
            }
        }
    }

    private static void OnWatchdogTick()
    {
        try
        {
            if (!IsActive) return;

            EnforceWindowsNightLightOff(force: false);

            int restored = 0;
            string? lastRestoredDevice = null;
            lock (_gate)
            {
                if (!_active) return;

                foreach (DisplayRampState display in _displays)
                {
                    if (!display.IsPresent || display.IsAdvancedColor || !display.HasExpectedRamp) continue;
                    if (!HasDriftedFromExpected(display)) continue;

                    WriteDisplayLocked(display, _strength);
                    restored++;
                    lastRestoredDevice = display.DeviceName;
                }
            }

            long now = Environment.TickCount64;
            if (restored > 0
                && now - _lastWatchdogRestoreLogTick >= TimeConstants.NightLightGammaRampWatchdogLogIntervalMs)
            {
                _lastWatchdogRestoreLogTick = now;
                TADNLog.Log(
                    $"GammaRampNightLight: watchdog rewrote {restored} display ramp(s) reset by other software"
                    + $" (last: {lastRestoredDevice})");
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"GammaRampNightLight.OnWatchdogTick: {ex}");
        }
    }

    // True when the display's live ramp no longer matches what was written. An unreadable ramp counts as unchanged.
    private static bool HasDriftedFromExpected(DisplayRampState display)
    {
        IntPtr hdc = GammaRampNative.OpenDisplayDC(display.DeviceName);
        if (hdc == IntPtr.Zero) return false;

        try
        {
            if (!GammaRampNative.GetDeviceGammaRamp(hdc, _readbackRamp)) return false;
            return GammaRampMath.MaxDeviation(_readbackRamp, display.ExpectedRamp) > WatchdogRampTolerance;
        }
        finally
        {
            GammaRampNative.CloseDisplayDC(hdc);
        }
    }

    private static void EnforceWindowsNightLightOff(bool force)
    {
        try
        {
            // SetEnabled(false) is a toggle when it sees Night Light on; the UI-thread refresh and the watchdog
            // must not both toggle, or the second flip turns Windows Night Light back on
            lock (_windowsNightLightGate)
            {
                bool windowsOn = NightLightRegistry.IsEnabled();
                bool attempt = ShouldTurnOffWindowsNightLight(windowsOn, _windowsNightLightOffFailed, force);
                bool turnedOff = false;
                if (attempt)
                {
                    NightLightRegistry.CancelPendingResend();
                    NightLightSettingsHandler.CancelPendingResend();
                    turnedOff = NightLightRegistry.SetEnabled(false);
                    TADNLog.Log(turnedOff
                        ? "GammaRampNightLight: turned Windows Night Light off to avoid a double tint"
                        : "GammaRampNightLight: Windows Night Light is on and could not be turned off;"
                          + " tints will stack");
                }

                _windowsNightLightOffFailed =
                    NextWindowsNightLightOffFailed(windowsOn, attempt, turnedOff, _windowsNightLightOffFailed);
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"GammaRampNightLight.EnforceWindowsNightLightOff: {ex.Message}");
        }
    }

    private static void ArmWatchdogLocked()
    {
        _watchdogTimer ??= new Timer(WatchdogCallback, state: null, Timeout.Infinite, Timeout.Infinite);
        _watchdogTimer.Change(
            TimeConstants.NightLightGammaRampWatchdogIntervalMs,
            TimeConstants.NightLightGammaRampWatchdogIntervalMs);
    }

    private static void RefreshDisplaysLocked()
    {
        List<GammaRampDisplay>? enumerated = GammaRampNative.EnumerateActiveDisplays();
        if (!MergeDisplays(_displays, enumerated))
        {
            TADNLog.Log(
                $"GammaRampNightLight: display enumeration {(enumerated == null ? "failed" : "found no displays")};"
                + $" keeping {_displays.Count} known display(s)");
            return;
        }

        foreach (DisplayRampState state in _displays)
        {
            if (!state.IsPresent || !state.IsAdvancedColor) continue;

            TADNLog.Log($"GammaRampNightLight: skipping {state.DeviceName}, it runs in HDR (advanced color)");
            // Put identity back on a display that went HDR while tinted, so no tint waits for its return to SDR
            if (state.HasWritten) WriteIdentityLocked(state);
        }
    }

    private static void ApplyLocked(int strength)
    {
        foreach (DisplayRampState display in _displays)
        {
            if (!display.IsPresent || display.IsAdvancedColor) continue;
            WriteDisplayLocked(display, strength);
        }
    }

    private static void WriteDisplayLocked(DisplayRampState display, int strength)
    {
        IntPtr hdc = GammaRampNative.OpenDisplayDC(display.DeviceName);
        if (hdc == IntPtr.Zero)
        {
            TADNLog.Log($"GammaRampNightLight: CreateDC failed for {display.DeviceName}");
            return;
        }

        try
        {
            DisplayRampWriter writer = new(hdc, _writeRamp);
            GammaRampWriteResult result =
                GammaRampMath.ApplyWithBackoff(strength, display.MaxAcceptedStrength, ref writer);
            display.HasWritten = true;

            if (!result.Succeeded)
            {
                display.HasExpectedRamp = false;
                if (!display.LoggedRefusal)
                {
                    display.LoggedRefusal = true;
                    TADNLog.Log(
                        $"GammaRampNightLight: {display.DeviceName} refused every ramp, including identity"
                        + $" ({result.Attempts} attempts)");
                }

                return;
            }

            int requested = Math.Min(Math.Clamp(strength, min: 0, max: 100), display.MaxAcceptedStrength);
            if (result.AppliedStrength < requested)
            {
                display.MaxAcceptedStrength = result.AppliedStrength;
                TADNLog.Log(
                    $"GammaRampNightLight: {display.DeviceName} refused strength {requested}; strongest accepted is "
                    + $"{result.AppliedStrength} ({result.Attempts} attempts). Windows limits gamma ramps unless "
                    + @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\GdiIcmGammaRange is 256.");
            }

            display.HasExpectedRamp = GammaRampNative.GetDeviceGammaRamp(hdc, display.ExpectedRamp);
        }
        finally
        {
            GammaRampNative.CloseDisplayDC(hdc);
        }
    }

    private static void RestoreIdentityLocked()
    {
        foreach (DisplayRampState display in _displays)
        {
            if (display.HasWritten) WriteIdentityLocked(display);
        }
    }

    private static void WriteIdentityLocked(DisplayRampState display)
    {
        display.HasExpectedRamp = false;
        IntPtr hdc = GammaRampNative.OpenDisplayDC(display.DeviceName);
        if (hdc == IntPtr.Zero)
        {
            TADNLog.Log($"GammaRampNightLight: CreateDC failed restoring {display.DeviceName}");
            return;
        }

        try
        {
            GammaRampMath.BuildRamp(strength: 0, _writeRamp);
            if (GammaRampNative.SetDeviceGammaRamp(hdc, _writeRamp))
                display.HasWritten = false;
            else
                TADNLog.Log($"GammaRampNightLight: {display.DeviceName} refused the identity ramp");
        }
        finally
        {
            GammaRampNative.CloseDisplayDC(hdc);
        }
    }

    private readonly struct DisplayRampWriter(IntPtr hdc, ushort[] ramp) : IGammaRampWriter
    {
        public bool TryWrite(int strength)
        {
            GammaRampMath.BuildRamp(strength, ramp);
            return GammaRampNative.SetDeviceGammaRamp(hdc, ramp);
        }
    }

    internal sealed class DisplayRampState(string deviceName)
    {
        public string DeviceName { get; } = deviceName;
        public ushort[] ExpectedRamp { get; } = new ushort[GammaRampMath.RampLength];
        public bool IsAdvancedColor { get; set; }

        // Strongest strength this display accepted since the last refresh; 100 until a refusal says otherwise
        public int MaxAcceptedStrength { get; set; } = 100;

        // This backend changed the display's ramp and hasn't put identity back yet
        public bool HasWritten { get; set; }
        public bool HasExpectedRamp { get; set; }
        public bool LoggedRefusal { get; set; }

        // In the latest usable enumeration. A written display that left it is kept, absent, for identity restore.
        public bool IsPresent { get; set; } = true;
    }
}
