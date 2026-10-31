using CommonTimeConstants = TrayAppDotNETCommon.TimeConstants;

namespace BatteryTrayAppDotNET;

public abstract class TimeConstants : CommonTimeConstants
{
    public new const int DrainPollIntervalMs = CommonTimeConstants.DrainPollIntervalMs;
    public new const int SettingsSaveDebounceMs = CommonTimeConstants.SettingsSaveDebounceMs;

    public new const int ToolTipShowDelayMinMs = CommonTimeConstants.ToolTipShowDelayMinMs;
    public new const int ToolTipShowDelayMaxMs = CommonTimeConstants.ToolTipShowDelayMaxMs;

    public new const int UpdateCheckIntervalDefaultMs = CommonTimeConstants.UpdateCheckIntervalDefaultMs;

    // Longest wait for powercfg to write the battery report before opening whatever file it produced
    public const int BatteryReportTimeoutMs = 10_000;
}
