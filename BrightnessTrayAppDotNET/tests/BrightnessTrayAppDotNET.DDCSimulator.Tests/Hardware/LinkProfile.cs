namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;

/// <summary>Physical path between a GPU connector and the panel's DDC lines.</summary>
internal enum LinkKind
{
    HDMIDirect,
    DisplayPort,
    DisplayPortToHDMIAdapter
}

/// <summary>
/// How a GPU reports a powered-off panel. The user's RTX 5080 with an FRL HDMI link keeps every LG enumerated while
/// it is off (no PnP event, no WM_DISPLAYCHANGE, HMONITOR stays valid); the previous RTX 4080 dropped native DP at once
/// and HDMI 22 to 33 s later.
/// </summary>
internal enum HotPlugVisibility
{
    InvisiblePowerCycles,
    VisiblePowerCycles
}

/// <summary>Per-connector DDC transport behavior. Latencies are the derived figures from the 2026-09-29 recorder logs.</summary>
internal sealed class LinkProfile
{
    public LinkKind Kind;

    // I2C-over-AUX carries each I2C byte in its own AUX transaction at 1 Mb/s plus sync and defers
    public double TransferScale = 1.0;

    // Address NAK on a direct HDMI link surfaces in 3 to 10 ms; through an active adapter in 30 to 40 ms
    public int NAKLatencyMinimumMs = 3;
    public int NAKLatencyMaximumMs = 10;

    // An adapter keeps answering for a panel that went silent, with garbage that takes 150 to 190 ms to arrive
    public bool GarblesWhileSinkSilent;
    public int GarbleLatencyMinimumMs = 150;
    public int GarbleLatencyMaximumMs = 190;

    // Hot-plug detect behavior when the panel powers off or sleeps
    public bool DropsHPDWhenPanelOff;
    public bool DropsHPDInStandby;
    public int HPDDropMinimumMs;
    public int HPDDropMaximumMs;
    public int HPDRiseMinimumMs = 150;
    public int HPDRiseMaximumMs = 700;

    // Per-byte probability of a flipped bit on the wire
    public double BitErrorRate;

    public string Describe() =>
        $"{Kind}{(GarblesWhileSinkSilent ? "+garble" : string.Empty)}"
        + $"{(DropsHPDWhenPanelOff ? $" HPD-drop {HPDDropMinimumMs}-{HPDDropMaximumMs}ms" : " HPD-held")}";

    public LinkProfile Clone() => (LinkProfile)MemberwiseClone();

    public static LinkProfile Create(LinkKind kind, HotPlugVisibility visibility) =>
        kind switch
        {
            LinkKind.HDMIDirect => new LinkProfile
            {
                Kind = kind,
                DropsHPDWhenPanelOff = visibility == HotPlugVisibility.VisiblePowerCycles,
                HPDDropMinimumMs = 22_000,
                HPDDropMaximumMs = 33_000
            },
            LinkKind.DisplayPort => new LinkProfile
            {
                Kind = kind,
                TransferScale = 4.0,
                NAKLatencyMinimumMs = 5,
                NAKLatencyMaximumMs = 15,
                DropsHPDWhenPanelOff = true,
                DropsHPDInStandby = visibility == HotPlugVisibility.VisiblePowerCycles,
                HPDDropMinimumMs = 0,
                HPDDropMaximumMs = 200
            },
            _ => new LinkProfile
            {
                Kind = kind,
                TransferScale = 4.0,
                NAKLatencyMinimumMs = 30,
                NAKLatencyMaximumMs = 40,
                GarblesWhileSinkSilent = true,
                DropsHPDWhenPanelOff = visibility == HotPlugVisibility.VisiblePowerCycles,
                HPDDropMinimumMs = 0,
                HPDDropMaximumMs = 200
            }
        };
}
