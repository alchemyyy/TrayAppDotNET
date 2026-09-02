namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Drives the night-light kelvin slider through <see cref="NightLightHelperClient"/>. The recyclable helper
/// process is entirely native and owns every SettingsHandlers/CDP allocation.
/// It calls <c>BlueLightSingleton::SetTargetColorTemperature</c> by a validated RVA.
/// That triggers <c>SaveSettingsAsync</c> on SHTaskPool,
/// where the eventual <c>ICloudStore::Save</c> succeeds and bumps the CloudStore version
/// - which is what the BlueLightReductionService watcher fires on,
/// so the live kelvin filter reapplies without flicker.
///
/// This class is the entry point that <see cref="NightLightProvider"/> dispatches to.
/// Reads (<see cref="GetStrength"/>, <see cref="IsEnabled"/>) use <see cref="NightLightRegistry"/> as the source
/// of truth. Explicit active-state changes use the SettingsHandler singleton so fresh Windows profiles receive
/// the initialized marker and the Settings UI observes the same CloudStore save chain as its own toggle.
///
/// On top of the cloud-store strength path, every strength gesture arms a single shared System.Threading.Timer
/// that fires
/// <see cref="NightLightRegistry.SetStrength"/> against the latest known kelvin
/// once <see cref="TimeConstants.NightLightUIHandleryRegistryEnforceDelayMs"/> of quiet has elapsed.
/// This is a belt-and-suspenders settle-write: the cloud-store bracket should already have updated
/// the same SETTINGS blob, but the registry write guarantees the final value lands and bumps the
/// STATE FILETIME so the broker re-reads.
/// </summary>
internal static class NightLightSettingsHandler
{
    private const string SettingsHandlersDllPath = @"C:\Windows\System32\SettingsHandlers_Display.dll";

    // -1 = no recorded strength yet
    private static int _deferredStrengthPercent = -1;
    private static Timer? _deferredRegistryTimer;

    public static bool IsSupported() => NightLightHelperClient.IsSupported();

    /// <summary>
    /// Cheap capability probe that does not load SettingsHandlers_Display or initialize its Night Light
    /// singleton. Actual symbol/backend validation is deferred until the first explicit write.
    /// </summary>
    public static bool CanInitialize() =>
        OperatingSystem.IsWindows() && Environment.Is64BitProcess && File.Exists(SettingsHandlersDllPath);

    /// <summary>Strength 0-100. Source of truth is the registry, same as the other backends.</summary>
    public static int GetStrength() => NightLightRegistry.GetStrength();

    public static bool IsEnabled() => NightLightRegistry.IsEnabled();

    /// <summary>
    /// Drives the Settings UI's own active-state mutator. When enabling with a target strength, the helper
    /// commits that strength before the active transition so a fresh profile cannot flash at a stale value.
    /// </summary>
    public static bool SetEnabled(bool enabled, int? enableStrength = null)
    {
        if (!enabled)
            NightLightHelperClient.CancelPendingStrength();

        return NightLightHelperClient.SetEnabled(enabled, enableStrength);
    }

    /// <summary>
    /// Toggles via the native SettingsHandler active-state path.
    /// </summary>
    public static bool Toggle() => SetEnabled(!NightLightRegistry.IsEnabled());

    /// <summary>
    /// Queues a kelvin write through the recyclable helper process. The main-process queue is length-one and
    /// latest-wins, so slider input returns immediately. The helper acknowledges as soon as the value enters its
    /// MTA streaming queue, keeps native preview mode active across the gesture, and releases preview after input
    /// goes quiet.
    /// No-ops when the backend is unavailable.
    ///
    /// Also records the latest kelvin and arms the deferred registry settle-write.
    /// </summary>
    public static void SetStrength(int percent)
    {
        if (!NightLightRegistry.IsEnabled()) return;

        int clamped = Math.Clamp(percent, min: 0, max: 100);
        if (!NightLightHelperClient.TryQueueSettingsKelvin(clamped)) return;

        Volatile.Write(ref _deferredStrengthPercent, clamped);
        ArmDeferredRegistryWrite();
    }

    /// <summary>
    /// Re-arms the shared deferred-write timer. Same pattern as
    /// <c>NightLightRegistry.SchedulePostSettleResend</c>: lazy-init via Interlocked.CompareExchange,
    /// then Timer.Change to reset the dueTime on every call. Allocations per call after the first: zero.
    /// </summary>
    private static void ArmDeferredRegistryWrite()
    {
        Timer? timer = _deferredRegistryTimer;
        if (timer == null)
        {
            // First-call lazy init. CompareExchange resolves the (rare) creation race so we never end up
            // with two timers; the loser disposes its candidate.
            Timer candidate = new(
                OnDeferredRegistryTimerFired, state: null, Timeout.Infinite, Timeout.Infinite);
            timer = Interlocked.CompareExchange(ref _deferredRegistryTimer, candidate, comparand: null) ?? candidate;
            if (!ReferenceEquals(timer, candidate)) candidate.Dispose();
        }

        timer.Change(TimeConstants.NightLightUIHandleryRegistryEnforceDelayMs, Timeout.Infinite);
    }

    private static void OnDeferredRegistryTimerFired(object? state)
    {
        // System.Threading.Timer callbacks run on a thread pool thread; an unhandled throw here crashes the
        // process. Belt-and-suspenders catch-all so a transient registry-write fault doesn't take the app
        // down.
        try
        {
            int percent = Volatile.Read(ref _deferredStrengthPercent);
            if (percent < 0) return;

            // Reset the sentinel before the write so a gesture arriving during the write owns the next fire
            Volatile.Write(ref _deferredStrengthPercent, value: -1);
            if (!NightLightRegistry.IsEnabled()) return;
            NightLightRegistry.SetStrength(percent);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightSettingsHandler.OnDeferredRegistryTimerFired: {ex}");
        }
    }

    /// <summary>
    /// Cancels any pending deferred registry settle-write. Used by the auto-off-at-zero path so the deferred
    /// write doesn't race against the off-state transition that follows.
    /// </summary>
    public static void CancelPendingResend()
    {
        NightLightHelperClient.CancelPendingStrength();
        Volatile.Write(ref _deferredStrengthPercent, value: -1);
        Timer? timer = _deferredRegistryTimer;
        timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Stops the deferred registry-settle timer and terminates every Night Light helper generation.
    /// </summary>
    public static void Shutdown()
    {
        Volatile.Write(ref _deferredStrengthPercent, value: -1);

        Timer? timer = Interlocked.Exchange(ref _deferredRegistryTimer, value: null);
        if (timer != null)
        {
            try { timer.Change(Timeout.Infinite, Timeout.Infinite); }
            catch (ObjectDisposedException)
            {
                TADNLog.Log("NightLightSettingsHandler.Shutdown: deferred registry timer was already disposed");
            }

            timer.Dispose();
        }

        NightLightHelperClient.Shutdown();
    }
}
