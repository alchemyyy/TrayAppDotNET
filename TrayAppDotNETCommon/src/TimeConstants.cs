namespace TrayAppDotNETCommon;

// Shared fixed time values for the tray apps. App projects extend this class
// and publicly re-export only the common constants their own code references.
public abstract class TimeConstants
{
    // Crash recovery & watcher
    protected internal const int CrashRestartDelayMs = 1_000;
    protected internal const int RapidRestartDetectionWindowMs = 30_000;

    // After an app exits because the Windows session is ending, its watcher waits this long and relaunches it only
    // if it is still alive and Windows no longer reports the session as shutting down (the logoff was cancelled)
    protected internal const int SessionEndRelaunchDelayMs = 5_000;
    protected internal const int SessionEndShutdownPollIntervalMs = 1_000;

    // Single instance
    protected internal const int SingleInstanceMutexAcquireTimeoutMs = 5_000;
    protected internal const int ApplicationInstanceMutexAcquireTimeoutMs = 10_000;
    protected internal const int SingleInstancePidBulletinReadTimeoutMs = 1_000;
    protected internal const int SingleInstancePidBulletinReadRetryMs = 25;

    // Graceful exit: how long a requester waits for an app to run its own exit after asking, before killing it, and
    // how long it then waits for a killed process to end
    protected internal const int GracefulExitRequestTimeoutMs = 5_000;
    protected internal const int GracefulExitKillWaitMs = 5_000;

    // Elevated install helpers: connecting to the launcher's progress pipe
    protected internal const int ProgressPipeConnectTimeoutMs = 5_000;

    // Async throttling / settings persistence
    protected internal const int DrainPollIntervalMs = 50;
    protected internal const int SettingsSaveDebounceMs = 400;
    protected internal const int SettingsFileReplaceRetryCount = 6;
    protected internal const int SettingsFileReplaceRetryDelayMs = 50;

    // Settings UI
    protected internal const int AboutStaleCheckTimerIntervalMs = 1_000;
    protected internal const int ColorPickerChangeCooldownMs = 50;
    // A Windows light/dark switch that arrives while a settings control is mid-edit waits for the edit to end; this is
    // how often the settings window checks
    protected internal const int SettingsThemeRebuildRetryMs = 250;
    protected internal const int ToolTipShowDelayDefaultMs = 750;
    protected internal const int ToolTipShowDelayMinMs = 0;
    protected internal const int ToolTipShowDelayMaxMs = 10_000;
    protected internal const int TrayMenuSubmenuShowDelayDefaultMs = 150;
    protected internal const int TrayMenuSubmenuShowDelayMinMs = 0;
    protected internal const int TrayMenuSubmenuShowDelayMaxMs = 5_000;
    protected internal const int RelativeTimestampJustNowThresholdMs = 60_000;
    protected internal const int RelativeTimestampMinutesThresholdMs = 3_600_000;
    protected internal const int RelativeTimestampHoursThresholdMs = 86_400_000;

    // Warm windows / resource purge
    protected internal const int WarmWindowIdleEvictionDelayMs = 10_000;

    // Logging
    // 7 days in ms = 7 * 24 * 60 * 60 * 1000 = 604_800_000.
    protected internal const int LogMaxAgeMs = 604_800_000;
    protected internal const int LogFlushIntervalMs = 2_000;
    protected internal const int LogShutdownTimerWaitMs = 1_000;

    // Pause between attempts at a log write that failed, before the batch goes back to the buffer
    protected internal const int LogWriteRetryDelayMs = 20;

    // Auto-update
    // Default cadence the background UpdateCheckService polls the version endpoint at. One hour surfaces a fresh
    // release the same workday without generating unnecessary traffic.
    protected internal const int UpdateCheckIntervalDefaultMs = 3_600_000;
    protected internal const int UpdateCheckIntervalMinMs = 60_000;
    protected internal const int UpdateCheckIntervalMaxMs = 86_400_000;
    protected internal const int UpdateCheckFailureRetryMs = 600_000;

    // Extra grace beyond the configured interval before the UI flips "Install update" to "Version stale".
    protected internal const int UpdateStaleGraceMs = 5_000;

    // Per-request HTTP timeout for both the release-metadata GET and the asset download GET.
    protected internal const int UpdateNetworkTimeoutMs = 30_000;
    protected internal const int UpdateManualCheckTimeoutMs = 45_000;

    // Quiet period after the last change to the shared versions manifest before a watching app reads it. One atomic
    // replace raises several watcher events, and waiting for them to settle turns them into a single read.
    protected internal const int UpdateManifestCacheChangeQuietPeriodMs = 250;

    // Short delay before kicking the very first check on startup so it doesn't compete with app startup work.
    protected internal const int UpdateCheckStartupDelayMs = 5_000;
    protected internal const int UpdateAssetDownloadMaxAttempts = 3;
    protected internal const int UpdateAssetDownloadInitialBackoffMs = 1_000;
    // A download that receives no bytes for this long is abandoned and retried; HttpClient.Timeout stops applying once
    // the response headers arrive, so a mid-body stall would otherwise hang the update forever
    protected internal const int UpdateAssetDownloadInactivityTimeoutMs = 30_000;
    protected internal const int UpdateFileRetryDelayMs = 250;
    protected internal const int UpdateProcessRetryDelayMs = 250;
    protected internal const int UpdateLogRetryDelayMs = 50;
    protected internal const int UpdateCleanupRetryDelayMs = 1_000;
    protected internal const int UninstallProcessRetryDelayMs = 250;
    protected internal const int UninstallFileRetryDelayMs = 1_000;
    // How long an uninstall waits for the detached cleanup to delete the helper's own exe and the settings folder
    // after the helper exits, before it reports what is left
    protected internal const int UninstallCleanupWaitMs = 30_000;

    // How long an install waits for another app's install to finish copying into the same folder. The bundle
    // installer queues up to six apps behind one another, each copy taking a few seconds, so two minutes only
    // trips when a holder has hung
    protected internal const int InstallDirectoryLockTimeoutMs = 120_000;
}
