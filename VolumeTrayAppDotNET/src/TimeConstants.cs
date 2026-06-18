namespace VolumeTrayAppDotNET;

// Central registry of hardcoded time values used across the app. Anything that
// is genuinely user-configurable lives on AppSettings instead -- this file is
// for fixed constants only. All values are in milliseconds; call sites wrap
// with TimeSpan.FromMilliseconds(...) when the consuming API requires TimeSpan.
public static class TimeConstants
{
    // Crash & shutdown drain
    public const int DrainPollIntervalMs = 50;

    // Crash recovery & watcher
    public const int CrashRestartDelayMs = 1_000;
    public const int RapidRestartDetectionWindowMs = 30_000;
    public const int WatcherLivenessPollIntervalMs = 1_000;

    // Single instance
    public const int SingleInstanceMutexAcquireTimeoutMs = 5_000;

    // Tray / Shell
    public const int TaskbarRecreateCheckIntervalMs = 500;

    // Settings UI
    public const int PostSettingsCloseGCDelayMs = 10_000;

    // Color picker
    public const int ColorPickerChangeCooldownMs = 50;

    // Tray icon update throttle default; the host app may override per instance.
    public const int TrayIconUpdateRateDefaultMs = 50;

    // Logging
    // 7 days in ms = 7 * 24 * 60 * 60 * 1000 = 604_800_000.
    public const int LogMaxAgeMs = 604_800_000;
    public const int LogFlushIntervalMs = 2_000;
    public const int LogShutdownTimerWaitMs = 1_000;

    // Auto-update
    // Default cadence the background UpdateCheckService polls GitHub at. 1 hour is a low-traffic compromise:
    // recent enough to surface a fresh release the same workday, infrequent enough to stay well clear of
    // GitHub's unauthenticated 60/hr rate limit even across the per-IP shared quota.
    public const int UpdateCheckIntervalDefaultMs = 3_600_000;
    public const int UpdateCheckIntervalMinMs = 60_000;

    public const int UpdateCheckIntervalMaxMs = 86_400_000;

    // Extra grace beyond the configured interval before the UI flips "Install update" to "Version stale".
    public const int UpdateStaleGraceMs = 5_000;

    // Per-request HTTP timeout for both the release-metadata GET and the asset download GET.
    public const int UpdateNetworkTimeoutMs = 30_000;

    // Short delay before kicking the very first check on startup so it doesn't compete with the
    // audio device manager init and other startup work for the first few seconds of process life.
    public const int UpdateCheckStartupDelayMs = 5_000;
    public const int UpdateAssetDownloadMaxAttempts = 3;
    public const int UpdateAssetDownloadInitialBackoffMs = 1_000;

    // Volume tray shell. One-shot delay after WM_TASKBARCREATED before re-asserting the icon -
    // the new tray needs a moment to settle before NIM_ADD will stick.
    public const int TaskbarRecreateDelayMs = 5_000;

    // Volume slider -> COM write throttle. AsyncThrottler coalesces drag events into a single
    // SetMasterVolume(Level)Scalar call per cooldown so the audio driver isn't hammered.
    // 30ms ~= 33Hz, smooth for a slider drag without flooding WASAPI on rapid mouse movement.
    public const int VolumeWriteRateDefaultMs = 30;

    // Default-device refresh coalescing dwell. A single device disable / default-change can fire
    // up to four IMMNotificationClient callbacks (Console / Multimedia / Communications role
    // transitions plus the state change itself); dwelling this long inside the AsyncThrottler
    // payload before doing the work, then bailing on HasReplacement, collapses the burst into a
    // single UpdateAllDefaults pass. 50ms is short enough to feel instant and long enough to
    // catch the trailing role-change notifications.
    public const int DefaultsRefreshCoalesceDwellMs = 50;

    // CoreAudio can report every endpoint as disabled / not-present during sleep-resume and then
    // miss the final Active/default callback. These waits let the device stack settle before the
    // manager performs a one-shot full enumeration recovery.
    public const int DeviceListRefreshAfterResumeMs = 2_000;
    public const int DeviceListRefreshAfterMissingDefaultMs = 1_000;

    // Trailing-edge debounce window for the volume-change ding. Each scroll/wheel event resets this
    // timer; the ding only fires once the timer elapses with no fresh event arriving. Keeps a fast
    // wheel spin (or rapid slider drag releases) from machine-gunning the beep. long enough
    // to cover a normal scroll cadence and short enough that the ding still feels coupled to the gesture.
    public const int VolumeFeedbackDingDelayMs = 350;

    // Bluetooth battery active-poll interval. The PnP watcher emits Updated events on
    // Connected-state changes but not on battery deltas, so without an explicit re-query via
    // CM_Get_DevNode_Property the bound UI would freeze on the value read at Added time. The
    // timer is only running while the flyout is open (no point polling the OS when nothing is
    // bound). 30s is well under typical headset reporting cadence and matches what Windows
    // Settings itself polls at.
    public const int BluetoothBatteryPollIntervalMs = 30_000;
}
