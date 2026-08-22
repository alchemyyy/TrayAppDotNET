using CommonTimeConstants = TrayAppDotNETCommon.TimeConstants;

namespace TaskManagerTrayAppDotNET;

public abstract class TimeConstants : CommonTimeConstants
{
    public new const int DrainPollIntervalMs = CommonTimeConstants.DrainPollIntervalMs;
    public new const int SettingsSaveDebounceMs = CommonTimeConstants.SettingsSaveDebounceMs;
    public new const int ToolTipShowDelayDefaultMs = CommonTimeConstants.ToolTipShowDelayDefaultMs;
    public new const int UpdateCheckIntervalDefaultMs = CommonTimeConstants.UpdateCheckIntervalDefaultMs;
}
