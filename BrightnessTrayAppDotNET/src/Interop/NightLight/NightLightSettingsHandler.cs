using TrayAppDotNETCommon.Services;

namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Drives the night-light kelvin slider via <see cref="NightLightCloudStore"/>,
/// which calls <c>BlueLightSingleton::SetTargetColorTemperature</c> by RVA.
/// That triggers <c>SaveSettingsAsync</c> on SHTaskPool,
/// where the eventual <c>ICloudStore::Save</c> succeeds and bumps the CloudStore version
/// - which is what the BlueLightReductionService watcher fires on,
/// so the live kelvin filter reapplies without flicker.
///
/// This class is the throttler-fronted entry point that <see cref="NightLightProvider"/> dispatches to.
/// Reads (<see cref="GetStrength"/>, <see cref="IsEnabled"/>)
/// and on/off mutations (<see cref="SetEnabled"/>, <see cref="Toggle"/>) delegate to <see cref="NightLightRegistry"/>
/// because the registry is the source of truth for those.
///
/// On top of the cloud-store strength path, every gesture (SetStrength, SetEnabled, Toggle)
/// arms a single shared System.Threading.Timer that fires
/// <see cref="NightLightRegistry.SetStrength"/> against the latest known kelvin
/// once <see cref="TimeConstants.NightLightUIHandleryRegistryEnforceDelayMs"/> of quiet has elapsed.
/// This is a belt-and-suspenders settle-write: the cloud-store bracket should already have updated
/// the same SETTINGS blob, but the registry write guarantees the final value lands and bumps the
/// STATE FILETIME so the broker re-reads.
/// </summary>
internal static class NightLightSettingsHandler
{
    // Callback guards naturally rate-limit this, so 0ms throttling is fine.
    private const string ThrottlerKey = "nightlight";
    private static readonly AsyncThrottler<string> _throttler = new(0, StringComparer.Ordinal);

    // -1 = no recorded strength yet; SetEnabled/Toggle will snapshot the registry on first arm
    // so the deferred fire always has a real value to write.
    private static int _deferredStrengthPercent = -1;
    private static Timer? _deferredRegistryTimer;

    public static bool IsSupported() => NightLightCloudStore.IsSupported();

    /// <summary>Strength 0-100. Source of truth is the registry, same as the other backends.</summary>
    public static int GetStrength() => NightLightRegistry.GetStrength();

    public static bool IsEnabled() => NightLightRegistry.IsEnabled();

    /// <summary>
    /// On/off via the registry path. Also re-arms the deferred registry settle-write so a toggle
    /// pushes any pending strength enforcement out by the full quiet period.
    /// </summary>
    public static bool SetEnabled(bool enabled)
    {
        bool ok = NightLightRegistry.SetEnabled(enabled);
        EnsureDeferredStrengthSeeded();
        ArmDeferredRegistryWrite();
        return ok;
    }

    /// <summary>
    /// Toggles via the registry path. Also re-arms the deferred registry settle-write
    /// for the same reason as <see cref="SetEnabled"/>.
    /// </summary>
    public static bool Toggle()
    {
        bool ok = NightLightRegistry.Toggle();
        EnsureDeferredStrengthSeeded();
        ArmDeferredRegistryWrite();
        return ok;
    }

    /// <summary>
    /// Schedules a kelvin write via <see cref="NightLightCloudStore.SaveSettingsKelvinAsync"/>.
    /// The throttler's length-1 latest-wins queue keeps the most recent slider value pending across the cooldown,
    /// so when you let go the user's final position is what eventually saves.
    /// No-ops when the backend is unavailable.
    ///
    /// The payload is genuinely async (<c>SaveSettingsKelvinAsync</c> yields on the first registry-notify wait),
    /// so the throttler's slot driver also yields on its first turn
    /// - callers running on the UI thread return immediately and the bracket runs on the thread pool.
    ///
    /// Also records the latest kelvin and arms the deferred registry settle-write.
    /// </summary>
    public static void SetStrength(int percent)
    {
        if (!IsSupported()) return;

        int clamped = Math.Clamp(percent, 0, 100);
        Volatile.Write(ref _deferredStrengthPercent, clamped);
        _ = _throttler.RunAsync(ThrottlerKey, _ => RunSetStrengthAsync(clamped));
        ArmDeferredRegistryWrite();
    }

    private static Task<bool> RunSetStrengthAsync(int percent) =>
        NightLightCloudStore.SaveSettingsKelvinAsync(percent);

    /// <summary>
    /// First-time seed for the deferred-strength field. SetStrength always overwrites it with the
    /// user's latest argument; the on/off mutators only seed when the field is still the sentinel,
    /// so a toggle never clobbers an in-flight slider value the user just requested.
    /// </summary>
    private static void EnsureDeferredStrengthSeeded()
    {
        if (Volatile.Read(ref _deferredStrengthPercent) >= 0) return;
        Volatile.Write(ref _deferredStrengthPercent, NightLightRegistry.GetStrength());
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
            timer = Interlocked.CompareExchange(ref _deferredRegistryTimer, candidate, null) ?? candidate;
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

            // Reset the sentinel before the write so a gesture that arrives while we're writing
            // still snapshots fresh state on its EnsureDeferredStrengthSeeded call.
            Volatile.Write(ref _deferredStrengthPercent, -1);
            NightLightRegistry.SetStrength(percent);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"NightLightSettingsHandler.OnDeferredRegistryTimerFired: {ex}");
        }
    }

    /// <summary>
    /// Cancels any pending deferred registry settle-write. Used by the auto-off-at-zero path so the deferred
    /// write doesn't race against the off-state transition that follows.
    /// </summary>
    public static void CancelPendingResend()
    {
        Timer? timer = _deferredRegistryTimer;
        timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }
}
