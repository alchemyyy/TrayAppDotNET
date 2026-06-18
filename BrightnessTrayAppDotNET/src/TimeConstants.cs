namespace BrightnessTrayAppDotNET;

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
    public const int AboutStaleCheckTimerIntervalMs = 1_000;

    // Color picker
    public const int ColorPickerChangeCooldownMs = 50;

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

    // Short delay before kicking the very first check on startup so it doesn't compete with monitor enumeration
    // and other startup work for the first few seconds of process life.
    public const int UpdateCheckStartupDelayMs = 5_000;

    // DDC recovery
    public const int DDCRecoveryRetryIntervalMs = 2_000;

    public const int DDCRecoveryAcquisitionPassTimeoutMs = 15_000;

    // Settle window before the first VCP read on any monitor after a topology event.
    // Covers cold hot-plug, monitor power-on, and cascade refreshes triggered when an unrelated
    // monitor changes power state. Most panels are DDC-ready inside ~500 ms, but some MCUs need
    // longer for their I2C reply pipeline to come up clean - reading too early can desync the
    // pipeline and wedge it into a persistent INVALID_MESSAGE_CHECKSUM state.
    public const int MonitorPostDetectionSettleDelayMs = 1_500;

    // Explicit per-attempt sleep before retries 2/3/4 (attempt 1 fires immediately after the
    // settle window above). All well above the DDC/CI spec's Tg floor of 40 ms.
    public static readonly int[] MonitorReadRetryBackoffSequenceMs = [80, 160, 480];
    public const int MonitorStartupSweep1stDelayMs = 2_000;
    public const int MonitorStartupSweep2ndDelayMs = 5_000;

    // Display events / hotplug
    public const int DisplayEventBurstIntervalMs = 1_000;
    public const int DisplayEventDebounceIntervalMs = 250;
    public const int DisplayServiceOperationTimeoutMs = 3_000;

    // Display identifier overlay
    public const int DisplayIdentifierDefaultDurationMs = 2_500;

    // Brightness flyout
    public const int BrightnessFlyoutPreviewSweepDurationMs = 10_000;

    // ToolTipService.ShowDuration is typed Int32 (milliseconds).
    public const int HardPowerOffTooltipShowDurationMs = 8_000;
    public const int RecoveryTooltipAutoCloseDurationMs = 8_000;

    // Environmental curve editor
    public const int EnvironmentalCurveSaveDebounceMs = 250;
    public const int EnvironmentalHttpClientTimeoutMs = 8_000;
    public const int CurveEditorClockIndicatorRefreshIntervalMs = 60_000;

    // Night light registry
    public const int NightLightSaveNotifyTimeoutMs = 1_500;
    public const int NightLightFallbackDwellMs = 50;
    public const int NightLightInterWriteDelayMs = 15;

    public const int NightLightUIHandleryRegistryEnforceDelayMs = 500;

    // CloudStore path's broker-wait dwell is longer than the registry path's
    // because the broker-mediated Save round-trip is genuinely slower than a raw key write.
    public const int NightLightCloudStoreFallbackDwellMs = 250;

    // PDB symbol resolver
    public const int PDBSymbolResolverDownloadTimeout = 60_000;

    // AppSettings defaults & floors (the values themselves are user-configurable;
    // the defaults and the minimum-allowed floor for the brightness update rate live here
    // so all initial timings sit in one file)
    public const int BrightnessUpdateRateDefaultMs = 50;
    public const int BrightnessUpdateRateMinMs = 10;
    public const int ValidationDwellDefaultMs = 500;
    public const int DDCOperationTimeoutDefaultMs = 3_000;
    public const int EnvironmentalCurveTickIntervalDefaultMs = 5_000;
}
