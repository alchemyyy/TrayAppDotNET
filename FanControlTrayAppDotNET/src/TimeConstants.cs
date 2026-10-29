using CommonTimeConstants = TrayAppDotNETCommon.TimeConstants;

namespace FanControlTrayAppDotNET;

// Central registry of hardcoded time values used across the app. Anything that
// is genuinely user-configurable lives on AppSettings instead -- this file is
// for fixed constants only. Units are part of each constant name; millisecond
// values are wrapped with TimeSpan.FromMilliseconds(...) when APIs require TimeSpan.
public abstract class TimeConstants : CommonTimeConstants
{
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

    // LibreHardwareMonitor polling. Hardware sensor reads are intrinsically polled; LHM has no
    // event surface, so we drive it on a timer. 500ms is a compromise between responsiveness
    // (curves should react to a temp spike inside ~1s) and CPU cost (each tick walks all sensors).
    public const int LHMPollIntervalMs = 500;

    // Curve control fail-safe. A curve data source with no finite reading for this long counts as stale,
    // and its fans go back to hardware control until readings resume. Ten poll intervals tolerates a
    // slow hardware pass without letting a dead sensor drive a fan for long.
    public const int CurveSourceStaleMs = 5_000;

    // Jumpstart holds the startup speed for one poll tick before dropping to the curve target.
    public const int FanJumpstartHoldMs = LHMPollIntervalMs;

    // A Warn Low / Warn High excursion must persist this long before the flyout flags it and the tray
    // notifies, and must stay back in range this long before it clears. Covers fan spin-up/down time
    // and RPM jitter around a threshold.
    public const int FanSpeedWarningDebounceMs = 5_000;

    // Dirty-shutdown marker: the stored boot time (UtcNow - TickCount64) matches the current boot when the two
    // differ by at most this much. Covers clock jitter and small NTP corrections; a reboot moves it by far more.
    public const int BootIdentityToleranceMs = 10_000;

    // Process list refresh. ProcessRunningService rebuilds the running-process snapshot at this
    // cadence as a fallback when WMI subscriptions are unavailable; with WMI active, this is the
    // reconciliation sweep cadence to catch any events the subscription missed.
    public const int ProcessListPollIntervalMs = 2_000;
    public const int BackgroundPollShutdownWaitMs = 2_000;
}
