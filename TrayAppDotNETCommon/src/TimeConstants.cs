namespace TrayAppDotNETCommon;

// Shared fixed time values for the tray apps. App projects extend this class
// and publicly re-export only the common constants their own code references.
public abstract class TimeConstants
{
    // Crash recovery & watcher
    protected internal const int CrashRestartDelayMs = 1_000;
    protected internal const int RapidRestartDetectionWindowMs = 30_000;
    protected internal const int WatcherLivenessPollIntervalMs = 1_000;

    // Single instance
    protected internal const int SingleInstanceMutexAcquireTimeoutMs = 5_000;
    protected internal const int SingleInstancePidBulletinReadTimeoutMs = 1_000;
    protected internal const int SingleInstancePidBulletinReadRetryMs = 25;

    // Async throttling / settings persistence
    protected internal const int DrainPollIntervalMs = 50;
    protected internal const int SettingsSaveDebounceMs = 400;

    // Settings UI
    protected internal const int PostSettingsCloseGCDelayMs = 10_000;
    protected internal const int AboutStaleCheckTimerIntervalMs = 1_000;
    protected internal const int ColorPickerChangeCooldownMs = 50;
    protected internal const int ToolTipShowDelayDefaultMs = 750;
    protected internal const int ToolTipShowDelayMinMs = 0;
    protected internal const int ToolTipShowDelayMaxMs = 10_000;
    protected internal const int RelativeTimestampJustNowThresholdMs = 60_000;
    protected internal const int RelativeTimestampMinutesThresholdMs = 3_600_000;
    protected internal const int RelativeTimestampHoursThresholdMs = 86_400_000;

    // Warm windows / resource purge
    protected internal const int WarmWindowIdleEvictionDelayMs = 10_000;
    protected internal const int WarmWindowFirstRenderDrainDelayMs = 150;
    protected internal const int WarmWindowSecondCollectionDelayMs = 150;

    // Logging
    // 7 days in ms = 7 * 24 * 60 * 60 * 1000 = 604_800_000.
    protected internal const int LogMaxAgeMs = 604_800_000;
    protected internal const int LogFlushIntervalMs = 2_000;
    protected internal const int LogShutdownTimerWaitMs = 1_000;

    // Auto-update
    // Default cadence the background UpdateCheckService polls GitHub at. 1 hour is a low-traffic compromise:
    // recent enough to surface a fresh release the same workday, infrequent enough to stay well clear of
    // GitHub's unauthenticated 60/hr rate limit even across the per-IP shared quota.
    protected internal const int UpdateCheckIntervalDefaultMs = 3_600_000;
    protected internal const int UpdateCheckIntervalMinMs = 60_000;
    protected internal const int UpdateCheckIntervalMaxMs = 86_400_000;
    protected internal const int UpdateCheckFailureRetryMs = 600_000;

    // Extra grace beyond the configured interval before the UI flips "Install update" to "Version stale".
    protected internal const int UpdateStaleGraceMs = 5_000;

    // Per-request HTTP timeout for both the release-metadata GET and the asset download GET.
    protected internal const int UpdateNetworkTimeoutMs = 30_000;

    // Short delay before kicking the very first check on startup so it doesn't compete with app startup work.
    protected internal const int UpdateCheckStartupDelayMs = 5_000;
    protected internal const int UpdateAssetDownloadMaxAttempts = 3;
    protected internal const int UpdateAssetDownloadInitialBackoffMs = 1_000;
}
