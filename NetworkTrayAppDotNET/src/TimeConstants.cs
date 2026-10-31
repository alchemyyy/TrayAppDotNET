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
    public new const int ToolTipShowDelayMinMs = CommonTimeConstants.ToolTipShowDelayMinMs;
    public new const int ToolTipShowDelayMaxMs = CommonTimeConstants.ToolTipShowDelayMaxMs;

    // Auto-update
    public new const int UpdateCheckIntervalDefaultMs = CommonTimeConstants.UpdateCheckIntervalDefaultMs;

    // Network polling
    public const int NetworkPollIntervalMs = 3_000;

    // Without a change, the forced collection that releases C#/WinRT connection-profile wrappers runs at most this often
    public const int NetworkProjectionReleaseIntervalMs = 5 * 60 * 1_000;
}
