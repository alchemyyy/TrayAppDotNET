using CommonTimeConstants = TrayAppDotNETCommon.TimeConstants;

namespace VolumeTrayAppDotNET;

// Central registry of hardcoded time values used across the app. Anything that
// is genuinely user-configurable lives on AppSettings instead -- this file is
// for fixed constants only. Units are part of each constant name; millisecond
// values are wrapped with TimeSpan.FromMilliseconds(...) when APIs require TimeSpan.
public abstract class TimeConstants : CommonTimeConstants
{
    // Async throttling / settings persistence
    public new const int DrainPollIntervalMs = CommonTimeConstants.DrainPollIntervalMs;
    public new const int SettingsSaveDebounceMs = CommonTimeConstants.SettingsSaveDebounceMs;

    // Settings UI
    public new const int AboutStaleCheckTimerIntervalMs = CommonTimeConstants.AboutStaleCheckTimerIntervalMs;
    public new const int ToolTipShowDelayMinMs = CommonTimeConstants.ToolTipShowDelayMinMs;
    public new const int ToolTipShowDelayMaxMs = CommonTimeConstants.ToolTipShowDelayMaxMs;

    // Auto-update
    public new const int UpdateCheckIntervalDefaultMs = CommonTimeConstants.UpdateCheckIntervalDefaultMs;
    public new const int UpdateStaleGraceMs = CommonTimeConstants.UpdateStaleGraceMs;

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

    // Bluetooth battery active-poll interval. Configuration Manager notifications do not report
    // every battery delta, so without an explicit CM_Get_DevNode_Property re-query the bound UI
    // would freeze on the value read at arrival time. The timer runs only while the flyout is open.
    // 30s is well under typical headset reporting cadence and matches what Windows Settings polls.
    public const int BluetoothBatteryPollIntervalMs = 30_000;

    // VTADN-defined observation window, not a timeout reported by Windows or the Bluetooth device.
    // KSPROPERTY_ONESHOT_RECONNECT only reports that the audio driver accepted an asynchronous
    // request; Windows exposes no completion handle, connecting state, progress, deadline, or
    // documented universal timeout. The 30s value approximates the behavior observed with the
    // target headphones. It drives only attempts initiated by VTADN and the associated countdown.
    // Connections initiated elsewhere cannot show pending progress because VTADN cannot observe
    // their start or deadline. VTADN can subsequently correlate the Classic Bluetooth fConnected
    // flag with Core Audio endpoint state to show Connected - Audio Waiting, then clear that state
    // when the endpoint becomes active.
    public const int BluetoothConnectionAttemptTimeoutMs = 30_000;
    public const int BluetoothConnectionCountdownTickMs = 100;
    public const int BluetoothConnectionStatePollIntervalMs = 500;
    public const int BluetoothConnectionAnimationIntervalMs = 6;

    // RadioMgr.h exposes a synchronous Bluetooth radio state change with a caller-selected
    // timeout. Microsoft recommends one to five seconds; three seconds bounds the background
    // operation without favoring either end of that range. State polling runs only while the
    // flyout is visible so changes made through Windows are reflected by the header button.
    public const int BluetoothRadioStateChangeTimeoutSeconds = 3;
    public const int BluetoothRadioStatePollIntervalMs = 1_000;

    // Device policy / process monitoring
    public const int DefaultDeviceRoleChangeTimeoutMs = 2_000;
    public const int DeviceVisibilityToggleSettleDelayMs = 250;
    public const int ProcessExitWatchRetryDelayMs = 10;
    public const int BluetoothCodecWorkerJoinTimeoutMs = 2_000;

    // Volume-change feedback
    public const int VolumeFeedbackDingDwellPollSliceMs = 10;

    public const int VolumeFeedbackDingMeterBypassGraceMs = 250;

    // A held suppression peak halves every 250ms with no discrete expiration. The decay is
    // time-based so changing the configurable peak sample rate does not change the envelope.
    public const int DingSuppressionPeakHalfLifeMs = 250;
    public const long EndpointSoundPlaybackBufferDurationHns = 2_000_000;
    public const int EndpointSoundPlaybackPollSliceMs = 30;
    public const int EndpointSoundPlaybackMaxDrainMs = 5_000;

    // Optional capture-stream activation used to wake software-only recording peak meters while
    // the flyout is visible. Packets are discarded at a short cadence so capture buffers do not
    // overrun; failed endpoint activations are retried slowly to tolerate transient driver states.
    public const int CaptureMeterActivationDrainIntervalMs = 20;
    public const int CaptureMeterActivationRetryIntervalMs = 2_000;
    public const int CaptureMeterActivationWorkerJoinTimeoutMs = 2_000;

    // App icon retry
    public const int IconRetryIntervalMsDefault = 250;
    public const int IconRetryIntervalMsMin = 50;
    public const int IconRetryIntervalMsMax = 5_000;
}
