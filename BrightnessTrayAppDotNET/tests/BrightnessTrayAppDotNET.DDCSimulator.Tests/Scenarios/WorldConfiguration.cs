using System.Text;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Host;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;
using BrightnessTrayAppDotNET.Models;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Scenarios;

/// <summary>One physical monitor and the connector it starts on.</summary>
internal sealed class MonitorSetup
{
    public string PhysicalID = "P1";
    public MonitorProfile Profile = MonitorProfile.LGUltraGear();
    public LinkKind Link = LinkKind.HDMIDirect;
    public string SerialDescriptor = "302NTLE27568";
    public uint NumericSerial = 0x0001E240;
    public ushort InitialBrightness = 50;
    public double BitErrorRate;
    public bool GarblesWhileOff = true;
    public bool StartsOff;

    public string Describe() =>
        $"{PhysicalID}: {Profile.ModelName} serial='{SerialDescriptor}'/{NumericSerial} {Link} start={InitialBrightness}"
        + $"{(StartsOff ? " starts-off" : string.Empty)}"
        + $"{(Profile.SupportsDDC ? string.Empty : " no-DDC")}{(Profile.ReadsBroken ? " write-only" : string.Empty)}"
        + $"{(Profile.EarlyReadWedgeProbability > 0 ? $" wedge={Profile.EarlyReadWedgeProbability:F2}" : string.Empty)}"
        + $"{(Profile.ReplyPreparationMaximumMs > DDCTiming.WindowsReplyDelayMs ? $" slow-replies={Profile.ReplyPreparationMinimumMs}-{Profile.ReplyPreparationMaximumMs}ms" : string.Empty)}"
        + $"{(Profile.DropSetProbability > 0 || Profile.StaleReplyProbability > 0 ? $" lossy(drop={Profile.DropSetProbability:F2} stale={Profile.StaleReplyProbability:F2})" : string.Empty)}"
        + $"{(Profile.BrightnessApplyMinimumMs > 0 ? $" slow-apply={Profile.BrightnessApplyMinimumMs}-{Profile.BrightnessApplyMaximumMs}ms" : string.Empty)}"
        + $"{(Profile.RespondsWhenSoftOff ? " answers-soft-off" : string.Empty)}"
        + $"{(Profile.WakesOnDDCWrite ? " wakes-on-write" : string.Empty)}"
        + $"{(BitErrorRate > 0 ? $" ber={BitErrorRate:G2}" : string.Empty)}";
}

/// <summary>btadn settings the scenario runs under.</summary>
internal sealed class AppConfiguration
{
    public MonitorIdentityStrategy IdentityStrategy = MonitorIdentityStrategy.DisplayNumber;
    public int ValidationAttempts = 4;
    public int ValidationDwellMs = TimeConstants.ValidationDwellDefaultMs;
    public int BrightnessUpdateRateMs = TimeConstants.BrightnessUpdateRateDefaultMs;
    public int DDCOperationTimeoutMs = TimeConstants.DDCOperationTimeoutDefaultMs;
    public bool AllowBlindDDCWrites = true;
    public PowerOffMode PowerOffMode = PowerOffMode.Sleep;
    public DisplaySortMode SortMode = DisplaySortMode.Arrangement;
    public bool CurveEnabled;
    public int CurveTickIntervalMs = TimeConstants.EnvironmentalCurveTickIntervalDefaultMs;

    public string Describe() =>
        $"identity={IdentityStrategy} attempts={ValidationAttempts} dwell={ValidationDwellMs}ms "
        + $"rate={BrightnessUpdateRateMs}ms timeout={DDCOperationTimeoutMs}ms blind={AllowBlindDDCWrites} "
        + $"powerOff={PowerOffMode} curve={CurveEnabled}";
}

/// <summary>Everything that defines a simulated machine before any scenario event runs.</summary>
internal sealed class WorldConfiguration
{
    public int Seed = 1;
    public string Name = "world";
    public List<MonitorSetup> Monitors = [];

    // Empty connectors after the cabled ones, for cable moves
    public List<LinkKind> SpareConnectors = [];
    public HostPolicy Host = new();
    public AppConfiguration App = new();
    public DisplayServiceTiming DisplayService = new();
    public QuiescenceOptions Quiescence = new();
    public DateTimeOffset Epoch = new(year: 2026, month: 10, day: 11, hour: 7, minute: 0, second: 0, TimeSpan.Zero);

    // Grace a settled, healthy panel gets before the oracle expects btadn to have a working row for it
    public TimeSpan SettleGrace = TimeSpan.FromSeconds(20);

    // Real time one scenario may spend before it stops early and reports what it reached
    public TimeSpan RealTimeBudget = TimeSpan.FromSeconds(20);

    // Named scenarios keep their full timeline even when they pass; sweeps only keep failing ones
    public bool AlwaysWriteTrace;

    public string Describe()
    {
        StringBuilder builder = new();
        builder.AppendLine($"world '{Name}' seed={Seed}");
        builder.AppendLine("  host: " + Host.Describe());
        builder.AppendLine("  app: " + App.Describe());
        foreach (MonitorSetup monitor in Monitors) builder.AppendLine("  " + monitor.Describe());
        if (SpareConnectors.Count > 0) builder.AppendLine("  spare connectors: " + string.Join(", ", SpareConnectors));
        return builder.ToString();
    }

    /// <summary>The user's desk: three LG ULTRAGEAR+ panels, the primary on direct HDMI and two on DP-to-HDMI adapters.</summary>
    public static WorldConfiguration UsersDesk(int seed = 1)
    {
        WorldConfiguration configuration = new() { Seed = seed, Name = "users-desk" };
        configuration.Monitors.Add(new MonitorSetup
        {
            PhysicalID = "P1", Link = LinkKind.HDMIDirect, SerialDescriptor = "211NTYT6N690", InitialBrightness = 40
        });
        configuration.Monitors.Add(new MonitorSetup
        {
            PhysicalID = "P2",
            Link = LinkKind.DisplayPortToHDMIAdapter,
            SerialDescriptor = "302NTLE27568",
            InitialBrightness = 55
        });
        configuration.Monitors.Add(new MonitorSetup
        {
            PhysicalID = "P3",
            Link = LinkKind.DisplayPortToHDMIAdapter,
            SerialDescriptor = "302NTEPDY724",
            InitialBrightness = 60
        });
        return configuration;
    }

    /// <summary>Clean, quirk-free panels on stable numbering: a baseline where any violation is an app logic gap.</summary>
    public static WorldConfiguration Clean(int monitorCount, int seed = 1)
    {
        WorldConfiguration configuration = new()
        {
            Seed = seed,
            Name = $"clean-{monitorCount}",
            Host = new HostPolicy { SourceAllocation = SourceAllocation.ConnectorBound, ReactionJitterMs = 0 }
        };
        for (int index = 0; index < monitorCount; index++)
        {
            configuration.Monitors.Add(new MonitorSetup
            {
                PhysicalID = "P" + (index + 1),
                Profile = MonitorProfile.Clean(),
                Link = LinkKind.DisplayPort,
                SerialDescriptor = "CLEAN" + (index + 1).ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
                InitialBrightness = (ushort)(30 + 15 * index)
            });
        }

        return configuration;
    }
}
