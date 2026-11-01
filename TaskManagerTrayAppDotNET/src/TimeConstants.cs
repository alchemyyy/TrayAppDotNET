using CommonTimeConstants = TrayAppDotNETCommon.TimeConstants;

namespace TaskManagerTrayAppDotNET;

public abstract class TimeConstants : CommonTimeConstants
{
    public const double DynamicRefreshBudgetMilliseconds = 1.25;

    // Keeps rapid Task Manager grid zoom input in visible-row mode until the wheel burst becomes quiet
    public const int TaskManagerGridZoomSettleDelayMilliseconds = 120;

    // Bounds the handoff delay when AsyncThrottler reports newer Task Manager grid zoom work
    public const int TaskManagerGridZoomReplacementPollMilliseconds = 8;

    // How long the sidebar's zoom and stretch indicator stays up after the last table zoom or stretch gesture
    public const int GridMetricIndicatorDecayMilliseconds = 2_000;

    public new const int DrainPollIntervalMs = CommonTimeConstants.DrainPollIntervalMs;
    public new const int SettingsSaveDebounceMs = CommonTimeConstants.SettingsSaveDebounceMs;
    public new const int UpdateCheckIntervalDefaultMs = CommonTimeConstants.UpdateCheckIntervalDefaultMs;
}
