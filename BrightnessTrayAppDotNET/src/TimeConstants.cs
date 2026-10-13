using CommonTimeConstants = TrayAppDotNETCommon.TimeConstants;

namespace BrightnessTrayAppDotNET;

// Central registry of hardcoded time values used across the app. Anything that
// is genuinely user-configurable lives on AppSettings instead -- this file is
// for fixed constants only. Units are part of each constant name; millisecond
// values are wrapped with TimeSpan.FromMilliseconds(...) when APIs require TimeSpan.
public abstract class TimeConstants : CommonTimeConstants
{
    // Crash & shutdown drain
    public const int ProcessExitDrainTimeoutMs = 200;
    public const int NormalShutdownDrainTimeoutMs = 3_000;
    public const int DrainAdditionalMarginMs = 250;
    public new const int DrainPollIntervalMs = CommonTimeConstants.DrainPollIntervalMs;

    // Settings UI
    public new const int AboutStaleCheckTimerIntervalMs = CommonTimeConstants.AboutStaleCheckTimerIntervalMs;

    public new const int TrayMenuSubmenuShowDelayDefaultMs =
        CommonTimeConstants.TrayMenuSubmenuShowDelayDefaultMs;

    public new const int TrayMenuSubmenuShowDelayMinMs =
        CommonTimeConstants.TrayMenuSubmenuShowDelayMinMs;

    public new const int TrayMenuSubmenuShowDelayMaxMs =
        CommonTimeConstants.TrayMenuSubmenuShowDelayMaxMs;

    // Auto-update
    public new const int UpdateCheckIntervalDefaultMs = CommonTimeConstants.UpdateCheckIntervalDefaultMs;
    public new const int UpdateStaleGraceMs = CommonTimeConstants.UpdateStaleGraceMs;

    // DDC recovery
    public const int DDCRecoveryRetryIntervalMs = 2_000;
    public const int DDCRecoveryShutdownDrainTimeoutMs = 1_000;

    // A candidate is probed at the base interval this many times, then the interval doubles up to the maximum.
    // An adapter answering for a dark panel holds the global DDC mutex ~190 ms per read, so a permanently silent
    // panel must not keep the bus busy at the base rate.
    public const int DDCRecoveryFullRateAttempts = 3;
    public const int DDCRecoveryMaximumRetryIntervalMs = 30_000;

    // A row that fails again within this long of recovering continues its backoff instead of being probed at once.
    // A panel that answers reads but does not hold its writes otherwise cycled demote, recover and replay about four
    // times a second. The replay's verification usually fails within a second or two; a cycle slower than the window,
    // as with very long validation dwells, costs little bus time anyway.
    public const int DDCRecoveryRelapseWindowMs = 10_000;

    // A helper process is retired once this many native failures in a row hit its monitor, then at most once per
    // cooldown while the streak lasts. A single garbled LG reply is the bus, not the helper, and a replacement
    // process cost the next command 194-242 ms (measured 2026-10-12), which stalled slider drags.
    public const int DDCHelperFailuresBeforeRetirement = 3;
    public const int DDCHelperRetirementCooldownMs = 300_000;

    // A read-degraded row resends an unconfirmed brightness value at most this often. An adapter in front of a dark
    // panel accepts every SET, and each verification read then holds the shared DDC mutex for a garbled reply.
    public const int ReadDegradedRewriteIntervalMs = 60_000;

    // Settle window before the first VCP read on any monitor after a topology event.
    // Covers cold hot-plug, monitor power-on, and cascade refreshes triggered when an unrelated
    // monitor changes power state. Most panels are DDC-ready inside ~500 ms, but some MCUs need
    // longer for their I2C reply pipeline to come up clean - reading too early can desync the
    // pipeline and wedge it into a persistent INVALID_MESSAGE_CHECKSUM state.
    public const int MonitorPostDetectionSettleDelayMs = 1_500;

    // Explicit per-attempt sleep before retries 2/3/4 (attempt 1 fires immediately after the
    // settle window above). All well above the DDC/CI spec's Tg floor of 40 ms.
    public static readonly int[] MonitorReadRetryBackoffSequenceMs = [80, 160, 480];

    // Some monitor MCUs clear a corrupted DDC reply queue only after accepting a SET. Leave a quiet window
    // before the confirming GET so the recovery transaction cannot consume the stale reply immediately.
    public const int MonitorChecksumRecoveryPostWriteDelayMs = 250;
    public const int MonitorStartupSweep1stDelayMs = 2_000;
    public const int MonitorStartupSweep2ndDelayMs = 5_000;

    // Display events / hotplug
    public const int DisplayEventBurstIntervalMs = 1_000;
    public const int DisplayEventDebounceIntervalMs = 250;
    public const int DisplayServiceOperationTimeoutMs = 3_000;
    public const int DDCOperationTimeoutSafetyFloorMs = 500;

    // DDC helper process: how long the tray waits for a new helper to connect to its command pipe, and how long the
    // helper waits to connect to that pipe
    public const int DDCHelperConnectTimeoutMs = 5_000;
    public const int DDCHelperPipeConnectTimeoutMs = 5_000;

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

    // Night Light helper process
    public const int NightLightHelperStartTimeoutMs = PDBSymbolResolverDownloadTimeout + 5_000;
    public const int NightLightHelperHotPathTimeoutMs = 1_000;
    public const int NightLightHelperStateChangeTimeoutMs = NightLightSaveNotifyTimeoutMs * 4 + 2_000;
    public const int NightLightHelperExitTimeoutMs = 2_000;
    public const int NightLightHelperRecoveryDelayMs = 250;
    public const int NightLightStreamingPreviewReleaseDelayMs = 150;
    public const int NightLightHelperRecycleQuietDelayMs = 200;
    public const int NightLightLastStrengthSaveDebounceMs = 500;

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
    public const int DDCOperationTimeoutMinMs = DDCOperationTimeoutSafetyFloorMs;
    public const int DDCOperationTimeoutMaxMs = 60_000;
    public const int EnvironmentalCurveTickIntervalDefaultMs = 5_000;
    public const int AutoEngageEnvironmentalCurveDelayDefaultSeconds = 60;
    public const int AutoEngageEnvironmentalCurveDelayMinSeconds = 0;
    public const int AutoEngageEnvironmentalCurveDelayMaxSeconds = 86_400;

    // Known display persistence
    public const int KnownDisplayStampDebounceMs = 500;

    // DDC write retry
    public const int MonitorWriteRetryBaseMs = 25;
    public const int MonitorInitialVerificationDwellMaxMs = 50;

    // A brightness write is read back only once its target has stayed current this long, so a slider drag reads back
    // only where it stops. On the user's LGs a read costs as much bus time as a write (GET 63-131 ms, SET 57-117 ms,
    // measured 2026-10-12), and win32k serializes DDC calls across all monitors: reading back every step of a slow
    // drag halved how often each monitor updated
    public const int BrightnessVerificationSettleMs = 300;

    // Least pause before another read-back after an unreadable one, whatever the validation dwell. A reply that fails
    // right after a SET is usually the panel still busy applying it, so a back-to-back retry repeats the failure.
    // Relaunching the helper after every failed read used to insert this pause by accident
    public const int VerificationRetryAfterFailedReadMs = 100;
    public const int BrightnessTargetSupersessionPollIntervalMs = 10;
}
