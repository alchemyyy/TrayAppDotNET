using CommonTimeConstants = TrayAppDotNETCommon.TimeConstants;

namespace NetworkTrayAppDotNET;

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
    public new const int ToolTipShowDelayDefaultMs = CommonTimeConstants.ToolTipShowDelayDefaultMs;
    public new const int ToolTipShowDelayMinMs = CommonTimeConstants.ToolTipShowDelayMinMs;
    public new const int ToolTipShowDelayMaxMs = CommonTimeConstants.ToolTipShowDelayMaxMs;

    // Auto-update
    public new const int UpdateStaleGraceMs = CommonTimeConstants.UpdateStaleGraceMs;

    // Network polling
    public const int NetworkPollIntervalMs = 3_000;
}
