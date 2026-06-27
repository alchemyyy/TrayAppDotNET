using CommonTimeConstants = TrayAppDotNETCommon.TimeConstants;

namespace BatteryTrayAppDotNET;

public abstract class TimeConstants : CommonTimeConstants
{
    public new const int DrainPollIntervalMs = CommonTimeConstants.DrainPollIntervalMs;
    public new const int SettingsSaveDebounceMs = CommonTimeConstants.SettingsSaveDebounceMs;

    public new const int PostSettingsCloseGCDelayMs = CommonTimeConstants.PostSettingsCloseGCDelayMs;
    public new const int AboutStaleCheckTimerIntervalMs = CommonTimeConstants.AboutStaleCheckTimerIntervalMs;
    public new const int ToolTipShowDelayMinMs = CommonTimeConstants.ToolTipShowDelayMinMs;
    public new const int ToolTipShowDelayMaxMs = CommonTimeConstants.ToolTipShowDelayMaxMs;

    public new const int UpdateCheckIntervalDefaultMs = CommonTimeConstants.UpdateCheckIntervalDefaultMs;
    public new const int UpdateStaleGraceMs = CommonTimeConstants.UpdateStaleGraceMs;
}
