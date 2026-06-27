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
    public new const int PostSettingsCloseGCDelayMs = CommonTimeConstants.PostSettingsCloseGCDelayMs;
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

    // Bluetooth battery active-poll interval. The PnP watcher emits Updated events on
    // Connected-state changes but not on battery deltas, so without an explicit re-query via
    // CM_Get_DevNode_Property the bound UI would freeze on the value read at Added time. The
    // timer is only running while the flyout is open (no point polling the OS when nothing is
    // bound). 30s is well under typical headset reporting cadence and matches what Windows
    // Settings itself polls at.
    public const int BluetoothBatteryPollIntervalMs = 30_000;

    // Device policy / process monitoring
    public const int DefaultDeviceRoleChangeTimeoutMs = 2_000;
    public const int DeviceVisibilityToggleSettleDelayMs = 250;
    public const int ProcessExitWatchRetryDelayMs = 10;
    public const int BluetoothCodecWorkerJoinTimeoutMs = 2_000;

    // Volume-change feedback
    public const int VolumeFeedbackDingDwellPollSliceMs = 10;
    public const int VolumeFeedbackDingMeterBypassGraceMs = 250;
    public const long EndpointSoundPlaybackBufferDurationHns = 2_000_000;
    public const int EndpointSoundPlaybackPollSliceMs = 30;
    public const int EndpointSoundPlaybackMaxDrainMs = 5_000;

    // App icon retry
    public const int IconRetryIntervalMsDefault = 250;
    public const int IconRetryIntervalMsMin = 50;
    public const int IconRetryIntervalMsMax = 5_000;
}
