namespace FanControlTrayAppDotNET;

// Central registry of hardcoded time values used across the app. Anything that
// is genuinely user-configurable lives on AppSettings instead -- this file is
// for fixed constants only. All values are in milliseconds; call sites wrap
// with TimeSpan.FromMilliseconds(...) when the consuming API requires TimeSpan.
public static class TimeConstants
{
    // Crash & shutdown drain
    public const int CrashHandlerDrainTimeoutMs = 500;
    public const int ProcessExitDrainTimeoutMs = 200;
    public const int SessionEndingDrainTimeoutMs = 2_000;
    public const int NormalShutdownDrainTimeoutMs = 3_000;
    public const int DrainAdditionalMarginMs = 250;
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
    public const int SettingsDragAnimationDurationMs = 150;
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

    // Auto-update. Default cadence the background UpdateCheckService polls GitHub at;
    // 1 hour stays well clear of GitHub's unauthenticated 60/hr rate limit even shared per-IP.
    public const int UpdateCheckIntervalDefaultMs = 3_600_000;
    public const int UpdateCheckIntervalMinMs = 60_000;

    public const int UpdateCheckIntervalMaxMs = 86_400_000;

    // Extra grace beyond the configured interval before the UI flips "Install update" to "Version stale".
    public const int UpdateStaleGraceMs = 5_000;

    // Per-request HTTP timeout for both the release-metadata GET and the asset download GET.
    public const int UpdateNetworkTimeoutMs = 30_000;

    // Short delay before kicking the very first check on startup so it doesn't compete with
    // the host's startup work for the first few seconds of process life.
    public const int UpdateCheckStartupDelayMs = 5_000;

    // LibreHardwareMonitor polling. Hardware sensor reads are intrinsically polled; LHM has no
    // event surface, so we drive it on a timer. 500ms is a compromise between responsiveness
    // (curves should react to a temp spike inside ~1s) and CPU cost (each tick walks all sensors).
    public const int LHMPollIntervalMs = 500;

    // Process list refresh. ProcessRunningService rebuilds the running-process snapshot at this
    // cadence as a fallback when WMI subscriptions are unavailable; with WMI active, this is the
    // reconciliation sweep cadence to catch any events the subscription missed.
    public const int ProcessListPollIntervalMs = 2_000;

    // Fan-curve evaluation. The curve service samples assigned curves and applies new duty cycles
    // on this cadence. Faster than the brightness app's 5s tick because thermals move quicker
    // than ambient light. Slower than LHM polling so multiple sensor reads inform each decision.
    public const int FanCurveTickIntervalMs = 1_000;
}
