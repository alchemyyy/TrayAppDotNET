using CommonTimeConstants = TrayAppDotNETCommon.TimeConstants;

namespace TaskManagerTrayAppDotNET;

public abstract class TimeConstants : CommonTimeConstants
{
    public const double DynamicRefreshBudgetMilliseconds = 1.25;

    // Keeps rapid details-grid zoom input in visible-row mode until the wheel burst becomes quiet
    public const int DetailsGridZoomSettleDelayMilliseconds = 120;

    // Bounds the handoff delay when AsyncThrottler reports newer details-grid zoom work
    public const int DetailsGridZoomReplacementPollMilliseconds = 8;

    public new const int DrainPollIntervalMs = CommonTimeConstants.DrainPollIntervalMs;
    public new const int SettingsSaveDebounceMs = CommonTimeConstants.SettingsSaveDebounceMs;
    public new const int ToolTipShowDelayDefaultMs = CommonTimeConstants.ToolTipShowDelayDefaultMs;
    public new const int TrayMenuSubmenuShowDelayDefaultMs =
        CommonTimeConstants.TrayMenuSubmenuShowDelayDefaultMs;
    public new const int TrayMenuSubmenuShowDelayMinMs =
        CommonTimeConstants.TrayMenuSubmenuShowDelayMinMs;
    public new const int TrayMenuSubmenuShowDelayMaxMs =
        CommonTimeConstants.TrayMenuSubmenuShowDelayMaxMs;
    public new const int UpdateCheckIntervalDefaultMs = CommonTimeConstants.UpdateCheckIntervalDefaultMs;
}
