using BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Host;

/// <summary>How the driver assigns a CCD source (and with it a GDI name) when a path activates.</summary>
internal enum SourceAllocation
{
    // Each connector always drives the same source
    ConnectorBound,

    // The lowest free source, so the order monitors come back in decides who gets which source
    FirstFree
}

/// <summary>How \\.\DISPLAYn names relate to sources.</summary>
internal enum GDINaming
{
    MatchSource,

    // Every activation takes a never-used number; the CCD.cs comment reports names climbing into the high 20s
    Climbing
}

/// <summary>What Windows Settings (and so CCD.BuildFriendlyDisplayNumberMap) shows as the display number.</summary>
internal enum DisplayNumbering
{
    SourceBound,
    ConnectorBound,

    // Active paths numbered densely in connector order, so a dropped monitor renumbers the ones after it
    Compact
}

/// <summary>
/// Windows display stack behavior for one simulated machine. Reaction times default to the 2026-09-29 recorder
/// measurements: removal is REMOVE_COMPLETE, path gone at +151 ms, WM_DISPLAYCHANGE at +209 ms; arrival is path active
/// at +424 ms, monitor-interface ARRIVAL at +438 ms, WM_DISPLAYCHANGE at +514 ms.
/// </summary>
internal sealed class HostPolicy
{
    public HotPlugVisibility Visibility = HotPlugVisibility.InvisiblePowerCycles;
    public SourceAllocation SourceAllocation = SourceAllocation.FirstFree;
    public GDINaming GDINaming = GDINaming.MatchSource;
    public DisplayNumbering DisplayNumbering = DisplayNumbering.SourceBound;
    public string InstancePrefix = "5&2dbe5411&0";

    public int RemovalTargetUnavailableMs = 66;
    public int RemovalPathDropMs = 151;
    public int RemovalDisplayChangeMs = 209;
    public int ArrivalTargetAvailableMs = 47;
    public int ArrivalPathActivateMs = 424;
    public int ArrivalInterfaceMs = 438;
    public int ArrivalDisplayChangeMs = 514;
    public int ReactionJitterMs = 40;

    // Measured once 1.9 s after an arrival: the driver collapsed to one active display for 4.3 s and raised
    // WM_DISPLAYCHANGE four times in 16 s
    public double ArrivalTransientProbability;
    public int TransientDelayMs = 1_900;
    public int TransientDurationMs = 4_300;

    // During transients attached-but-inactive monitors are listed under every source, sometimes ahead of the active
    // one, and DisplayService reads index 0
    public double InactiveListedFirstProbability;

    // Right after activation EnumDisplayDevices may not list the monitor yet, so DeviceID falls back to \\.\DISPLAYn
    public int DeviceAttachLagMaximumMs;

    // A devnode seen for the first time can reach EnumDisplayMonitors before its EDID reaches the registry
    public int EDIDRegistryLagMaximumMs;

    // Probability an arrival enumerates first as EDID-less Default_Monitor before the real devnode replaces it
    public double DefaultMonitorProbability;
    public int DefaultMonitorDurationMs = 3_000;

    // Probability that Windows re-activates every path on resume even when no hot-plug state changed
    public double ResumeReactivatesAllProbability = 0.5;

    public double CCDFailureProbability;

    public string Describe() =>
        $"{Visibility} sources={SourceAllocation} gdi={GDINaming} numbers={DisplayNumbering} "
        + $"transient={ArrivalTransientProbability:F2} inactiveFirst={InactiveListedFirstProbability:F2} "
        + $"attachLag={DeviceAttachLagMaximumMs}ms edidLag={EDIDRegistryLagMaximumMs}ms "
        + $"defaultMonitor={DefaultMonitorProbability:F2} ccdFail={CCDFailureProbability:F2}";

    public HostPolicy Clone() => (HostPolicy)MemberwiseClone();
}
