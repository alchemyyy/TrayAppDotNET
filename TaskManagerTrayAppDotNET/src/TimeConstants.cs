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

    // How long a second launch waits for the running instance's activation pipe to answer before starting on its own
    public const int ActivationHandOffTimeoutMilliseconds = 1_500;

    public new const int DrainPollIntervalMs = CommonTimeConstants.DrainPollIntervalMs;
    public new const int SettingsSaveDebounceMs = CommonTimeConstants.SettingsSaveDebounceMs;
    public new const int UpdateCheckIntervalDefaultMs = CommonTimeConstants.UpdateCheckIntervalDefaultMs;
}
