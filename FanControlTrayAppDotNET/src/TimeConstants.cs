using CommonTimeConstants = TrayAppDotNETCommon.TimeConstants;

namespace FanControlTrayAppDotNET;

// Central registry of hardcoded time values used across the app. Anything that
// is genuinely user-configurable lives on AppSettings instead -- this file is
// for fixed constants only. Units are part of each constant name; millisecond
// values are wrapped with TimeSpan.FromMilliseconds(...) when APIs require TimeSpan.
public abstract class TimeConstants : TrayAppDotNETCommon.TimeConstants
{
    // Settings UI
    public new const int PostSettingsCloseGCDelayMs = CommonTimeConstants.PostSettingsCloseGCDelayMs;
    public new const int AboutStaleCheckTimerIntervalMs = CommonTimeConstants.AboutStaleCheckTimerIntervalMs;

    // Auto-update
    public new const int UpdateCheckIntervalDefaultMs = CommonTimeConstants.UpdateCheckIntervalDefaultMs;
    public new const int UpdateStaleGraceMs = CommonTimeConstants.UpdateStaleGraceMs;

    // LibreHardwareMonitor polling. Hardware sensor reads are intrinsically polled; LHM has no
    // event surface, so we drive it on a timer. 500ms is a compromise between responsiveness
    // (curves should react to a temp spike inside ~1s) and CPU cost (each tick walks all sensors).
    public const int LHMPollIntervalMs = 500;

    // Process list refresh. ProcessRunningService rebuilds the running-process snapshot at this
    // cadence as a fallback when WMI subscriptions are unavailable; with WMI active, this is the
    // reconciliation sweep cadence to catch any events the subscription missed.
    public const int ProcessListPollIntervalMs = 2_000;
    public const int BackgroundPollShutdownWaitMs = 2_000;
}
