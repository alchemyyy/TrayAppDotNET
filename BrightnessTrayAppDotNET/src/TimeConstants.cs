using CommonTimeConstants = TrayAppDotNETCommon.TimeConstants;

namespace BrightnessTrayAppDotNET;

// Central registry of hardcoded time values used across the app. Anything that
// is genuinely user-configurable lives on AppSettings instead -- this file is
// for fixed constants only. Units are part of each constant name; millisecond
// values are wrapped with TimeSpan.FromMilliseconds(...) when APIs require TimeSpan.
public abstract class TimeConstants : TrayAppDotNETCommon.TimeConstants
{
    // Crash & shutdown drain
    public const int ProcessExitDrainTimeoutMs = 200;
    public const int NormalShutdownDrainTimeoutMs = 3_000;
    public const int DrainAdditionalMarginMs = 250;
    public new const int DrainPollIntervalMs = CommonTimeConstants.DrainPollIntervalMs;

    // Settings UI
    public new const int PostSettingsCloseGCDelayMs = CommonTimeConstants.PostSettingsCloseGCDelayMs;
    public new const int AboutStaleCheckTimerIntervalMs = CommonTimeConstants.AboutStaleCheckTimerIntervalMs;

    // Auto-update
    public new const int UpdateCheckIntervalDefaultMs = CommonTimeConstants.UpdateCheckIntervalDefaultMs;
    public new const int UpdateStaleGraceMs = CommonTimeConstants.UpdateStaleGraceMs;

    // DDC recovery
    public const int DDCRecoveryRetryIntervalMs = 2_000;

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
    public const int CurveStopwatchDefaultMinutes = 60;
    public const int CurveStopwatchRefreshIntervalMs = 1_000;
    public const int TrayValueDiagnosticCooldownMs = 30_000;

    // Environmental curve editor
    public const int EnvironmentalCurveSaveDebounceMs = 250;
    public const int EnvironmentalHttpClientTimeoutMs = 8_000;
    public const int CurveEditorClockIndicatorRefreshIntervalMs = 60_000;

    // Night light registry
    public const int NightLightSaveNotifyTimeoutMs = 1_500;
    public const int NightLightFallbackDwellMs = 50;
    public const int NightLightInterWriteDelayMs = 15;

    public const int NightLightUIHandleryRegistryEnforceDelayMs = 500;
    public const ulong NightLightMaxFutureSkewSeconds = 24 * 60 * 60;

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
    public const int BrightnessUpdateRateMaxMs = 10_000;
    public const int ValidationDwellDefaultMs = 500;
    public const int ValidationDwellMinMs = 0;
    public const int ValidationDwellMaxMs = 10_000;
    public const int DDCOperationTimeoutDefaultMs = 3_000;
    public const int DDCOperationTimeoutMinMs = 0;
    public const int DDCOperationTimeoutMaxMs = 60_000;
    public const int EnvironmentalCurveTickIntervalDefaultMs = 5_000;

    // Known display persistence
    public const int KnownDisplayStampDebounceMs = 500;

    // DDC write retry
    public const int MonitorWriteRetryBaseMs = 25;
}
