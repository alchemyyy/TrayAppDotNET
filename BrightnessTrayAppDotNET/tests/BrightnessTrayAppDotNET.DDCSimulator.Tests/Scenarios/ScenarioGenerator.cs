using System.Globalization;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Host;
using BrightnessTrayAppDotNET.Models;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Scenarios;

/// <summary>Kinds of disruption a generated timeline concentrates on.</summary>
internal enum EventFamily
{
    PowerCycles,
    Sleep,
    AppPower,
    CableMoves,
    Transients,
    UserWrites,
    Faults,
    Mixed
}

/// <summary>One point of the combination matrix.</summary>
internal readonly record struct MatrixCell(
    int MonitorCount,
    HotPlugVisibility Visibility,
    DisplayNumbering Numbering,
    MonitorIdentityStrategy Identity,
    EventFamily Family)
{
    public string Describe() => $"{MonitorCount}mon-{Visibility}-{Numbering}-{Identity}-{Family}";
}

/// <summary>
/// Builds worlds and timelines from a seed. <see cref="Generate"/> randomizes every axis; <see cref="GenerateCell"/>
/// pins the main axes to one matrix cell and randomizes the rest, so <see cref="Matrix"/> walks every combination of
/// monitor count, hot-plug visibility, display numbering, identity strategy and event family.
/// </summary>
internal static class ScenarioGenerator
{
    private static readonly string[] LGSerials =
        ["211NTYT6N690", "302NTLE27568", "302NTEPDY724", "205NTTQE5969", "301NTKL04411", "112NTWA77321"];

    public static IEnumerable<MatrixCell> Matrix()
    {
        foreach (int monitorCount in (int[])[1, 2, 3, 4])
        foreach (HotPlugVisibility visibility in Enum.GetValues<HotPlugVisibility>())
        foreach (DisplayNumbering numbering in (DisplayNumbering[])[DisplayNumbering.SourceBound, DisplayNumbering.Compact])
        foreach (MonitorIdentityStrategy identity in Enum.GetValues<MonitorIdentityStrategy>())
        foreach (EventFamily family in Enum.GetValues<EventFamily>())
            yield return new MatrixCell(monitorCount, visibility, numbering, identity, family);
    }

    public static (WorldConfiguration World, ScenarioScript Script) Generate(int seed)
    {
        Random random = new(seed);
        MatrixCell cell = new(
            Pick(random, [(1, 1), (2, 3), (3, 5), (4, 1)]),
            random.NextDouble() < 0.6 ? HotPlugVisibility.InvisiblePowerCycles : HotPlugVisibility.VisiblePowerCycles,
            Pick(random, [(DisplayNumbering.SourceBound, 3), (DisplayNumbering.ConnectorBound, 1), (DisplayNumbering.Compact, 1)]),
            Pick(random,
            [
                (MonitorIdentityStrategy.DisplayNumber, 3), (MonitorIdentityStrategy.HardwarePort, 1),
                (MonitorIdentityStrategy.EDIDSerial, 1)
            ]),
            Pick(random, Enum.GetValues<EventFamily>().Select(family => (family, family == EventFamily.Mixed ? 3 : 1)).ToArray()));
        return Build(cell, seed, random, fullyRandom: true);
    }

    public static (WorldConfiguration World, ScenarioScript Script) GenerateCell(MatrixCell cell, int seed) =>
        Build(cell, seed, new Random(HashCode.Combine(seed, cell)), fullyRandom: false);

    private static (WorldConfiguration World, ScenarioScript Script) Build(
        MatrixCell cell,
        int seed,
        Random random,
        bool fullyRandom)
    {
        WorldConfiguration world = new()
        {
            Seed = seed,
            Name = fullyRandom ? "fuzz" : cell.Describe(),
            Host = BuildHost(cell, random, fullyRandom),
            App = BuildApp(cell, random, fullyRandom)
        };
        for (int index = 0; index < cell.MonitorCount; index++) world.Monitors.Add(BuildMonitor(index, random, fullyRandom));
        if (fullyRandom && random.NextDouble() < 0.1) world.DisplayService.HelperSpawnFailureProbability = 0.05;
        if (cell.Family is EventFamily.CableMoves or EventFamily.Mixed || random.NextDouble() < 0.2)
            world.SpareConnectors.Add(Pick(random, [(LinkKind.DisplayPort, 2), (LinkKind.HDMIDirect, 1)]));

        ScenarioScript script = new($"{world.Name}-{seed.ToString(CultureInfo.InvariantCulture)}");
        TimelineBuilder timeline = new(script, world, random);
        timeline.Populate(cell.Family);
        return (world, script);
    }

    private static HostPolicy BuildHost(MatrixCell cell, Random random, bool fullyRandom)
    {
        HostPolicy host = new()
        {
            Visibility = cell.Visibility,
            DisplayNumbering = cell.Numbering,
            SourceAllocation = random.NextDouble() < 0.75 ? SourceAllocation.FirstFree : SourceAllocation.ConnectorBound,
            GDINaming = random.NextDouble() < 0.2 ? GDINaming.Climbing : GDINaming.MatchSource,
            ResumeReactivatesAllProbability = random.NextDouble()
        };
        if (!fullyRandom && cell.Family != EventFamily.Transients) return host;

        host.ArrivalTransientProbability = Pick(random, [(0.0, 3), (0.3, 1), (1.0, 1)]);
        host.InactiveListedFirstProbability = Pick(random, [(0.0, 2), (0.3, 1), (0.7, 1)]);
        host.DeviceAttachLagMaximumMs = Pick(random, [(0, 3), (300, 1), (1500, 1)]);
        host.EDIDRegistryLagMaximumMs = Pick(random, [(0, 3), (1000, 1), (4000, 1)]);
        host.DefaultMonitorProbability = Pick(random, [(0.0, 4), (0.25, 1)]);
        host.CCDFailureProbability = Pick(random, [(0.0, 4), (0.05, 1)]);
        return host;
    }

    private static AppConfiguration BuildApp(MatrixCell cell, Random random, bool fullyRandom)
    {
        AppConfiguration app = new() { IdentityStrategy = cell.Identity, CurveEnabled = random.NextDouble() < 0.5 };
        if (!fullyRandom) return app;

        app.ValidationAttempts = Pick(random, [(1, 1), (2, 1), (4, 4), (6, 1)]);
        app.ValidationDwellMs = Pick(random, [(0, 1), (100, 1), (500, 3), (2000, 1)]);
        app.BrightnessUpdateRateMs = Pick(random, [(0, 1), (50, 3), (200, 1)]);
        app.DDCOperationTimeoutMs = Pick(random, [(500, 1), (1000, 1), (3000, 4), (10000, 1)]);
        app.AllowBlindDDCWrites = random.NextDouble() < 0.7;
        app.PowerOffMode = Pick(random, [(PowerOffMode.Sleep, 3), (PowerOffMode.Soft, 1), (PowerOffMode.Hard, 1)]);
        return app;
    }

    private static MonitorSetup BuildMonitor(int index, Random random, bool fullyRandom)
    {
        MonitorSetup setup = new()
        {
            PhysicalID = "P" + (index + 1).ToString(CultureInfo.InvariantCulture),
            Link = Pick(random, [(LinkKind.HDMIDirect, 2), (LinkKind.DisplayPort, 2), (LinkKind.DisplayPortToHDMIAdapter, 2)]),
            SerialDescriptor = LGSerials[index % LGSerials.Length],
            NumericSerial = (uint)(1_000_000 + index),
            InitialBrightness = (ushort)random.Next(minValue: 10, maxValue: 91),
            GarblesWhileOff = random.NextDouble() < 0.5
        };
        if (!fullyRandom) return setup;

        setup.Profile = Pick(random,
        [
            (MonitorProfile.LGUltraGear(), 6), (MonitorProfile.Clean(), 2), (MonitorProfile.SlowReplies(), 1),
            (MonitorProfile.Lossy(), 1), (MonitorProfile.WriteOnly(), 1)
        ]);

        // Serial-less and shared-serial EDIDs exist in the wild, and Windows sees identical EDIDs on identical panels
        double serialRoll = random.NextDouble();
        if (serialRoll < 0.05)
        {
            setup.SerialDescriptor = string.Empty;
        }
        else if (serialRoll < 0.08)
        {
            setup.SerialDescriptor = string.Empty;
            setup.NumericSerial = 0;
        }

        setup.Profile.WakesOnDDCWrite = random.NextDouble() < 0.15;
        setup.Profile.RespondsWhenSoftOff = random.NextDouble() < 0.15;
        if (random.NextDouble() < 0.2)
        {
            setup.Profile.BrightnessApplyMinimumMs = 100;
            setup.Profile.BrightnessApplyMaximumMs = 900;
        }

        setup.StartsOff = random.NextDouble() < 0.1;
        if (random.NextDouble() < 0.05) setup.BitErrorRate = random.NextDouble() * 1e-3;
        return setup;
    }

    private static T Pick<T>(Random random, (T Value, int Weight)[] choices)
    {
        int total = choices.Sum(choice => choice.Weight);
        int roll = random.Next(total);
        foreach ((T value, int weight) in choices)
        {
            if (roll < weight) return value;
            roll -= weight;
        }

        return choices[^1].Value;
    }

    /// <summary>Lays out a plausible timeline, tracking intended panel state so every step makes sense when it runs.</summary>
    private sealed class TimelineBuilder(ScenarioScript script, WorldConfiguration world, Random random)
    {
        private readonly List<string> _physicalIDs = [.. world.Monitors.Select(monitor => monitor.PhysicalID)];
        private double _now = 8;
        private double _slept;

        public void Populate(EventFamily family)
        {
            int eventCount = random.Next(minValue: 3, maxValue: 14);
            double horizon = random.Next(minValue: 40, maxValue: 600);
            for (int index = 0; index < eventCount; index++)
            {
                _now += random.NextDouble() * horizon / eventCount + 0.05;
                EventFamily chosen = family == EventFamily.Mixed ? (EventFamily)random.Next((int)EventFamily.Mixed) : family;
                AddEvent(chosen);
            }

            // Bring every panel back so the end state is fully checkable: all on, all cabled, displays powered
            _now += 5;
            script.At(_now + _slept, "restore every panel", static simulated => simulated.RestoreAllPanels());
            script.Tail = TimeSpan.FromSeconds(120);
        }

        private void AddEvent(EventFamily family)
        {
            string target = _physicalIDs[random.Next(_physicalIDs.Count)];
            double at = _now + _slept;
            switch (family)
            {
                case EventFamily.PowerCycles:
                    double offFor = random.NextDouble() < 0.3 ? random.NextDouble() * 3 : 2 + random.NextDouble() * 120;
                    script.At(at, $"{target} power button off", simulated => simulated.PressPowerButtonToward(target, on: false));
                    script.At(at + offFor, $"{target} power button on", simulated => simulated.PressPowerButtonToward(target, on: true));
                    break;

                case EventFamily.Sleep:
                    if (random.NextDouble() < 0.5)
                    {
                        double sleep = Pick(random, [(60.0, 1), (1800.0, 1), (36000.0, 1)]);
                        script.At(at, $"system sleeps {sleep:F0} s", simulated => simulated.SystemSleep(TimeSpan.FromSeconds(sleep)));
                        _slept += sleep;
                    }
                    else
                    {
                        double offFor2 = 5 + random.NextDouble() * 300;
                        script.At(at, "console display off", static simulated => simulated.DisplaysOff());
                        script.At(at + offFor2, "console display on", static simulated => simulated.DisplaysOn());
                    }

                    break;

                case EventFamily.AppPower:
                    switch (random.Next(maxValue: 3))
                    {
                        case 0:
                            script.At(at, $"app powers {target} off", simulated => simulated.AppPower(target, on: false));
                            script.At(at + 2 + random.NextDouble() * 60, $"app powers {target} on", simulated => simulated.AppPower(target, on: true));
                            break;
                        case 1:
                            script.At(at, "app powers every panel off", static simulated => simulated.AppPowerAll(false));
                            script.At(at + 2 + random.NextDouble() * 60, "app powers every panel on", static simulated => simulated.AppPowerAll(true));
                            break;
                        default:
                            script.At(at, $"ctrl-click hard power off {target}", simulated => simulated.HardPowerOffCtrlClick(target));
                            break;
                    }

                    break;

                case EventFamily.CableMoves:
                    double pluggedAfter = 0.5 + random.NextDouble() * 30;
                    script.At(at, $"unplug {target}", simulated => simulated.Unplug(target));
                    script.At(at + pluggedAfter, $"replug {target} into a free connector", simulated => simulated.PlugIntoFreeConnector(target, preferOriginal: random.NextDouble() < 0.5));
                    break;

                case EventFamily.Transients:
                    switch (random.Next(maxValue: 3))
                    {
                        case 0:
                            script.At(at, "driver transient", static simulated => simulated.DriverTransient());
                            break;
                        case 1:
                            script.At(at, "display mode change", static simulated => simulated.ModeChange());
                            break;
                        default:
                            script.At(at, "session unlock", static simulated => simulated.SessionUnlock());
                            break;
                    }

                    break;

                case EventFamily.UserWrites:
                    switch (random.Next(maxValue: 4))
                    {
                        case 0:
                            int value = random.Next(minValue: 0, maxValue: 101);
                            script.At(at, $"user sets {target} to {value}", simulated => simulated.SetBrightness(target, value));
                            break;
                        case 1:
                            int from = random.Next(minValue: 0, maxValue: 101);
                            int to = random.Next(minValue: 0, maxValue: 101);
                            script.At(at, $"user drags {target} {from}->{to}", simulated =>
                                simulated.Drag(target, from, to, steps: 20, TimeSpan.FromMilliseconds(600)));
                            break;
                        case 2:
                            script.At(at, "flyout opens", static simulated => simulated.OpenFlyout());
                            break;
                        default:
                            double curveTarget = random.Next(minValue: 5, maxValue: 96);
                            bool enable = random.NextDouble() < 0.8;
                            script.At(at, $"curve {(enable ? "on" : "off")} at {curveTarget:F0}", simulated =>
                                simulated.SetCurve(enable, _ => curveTarget));
                            break;
                    }

                    break;

                case EventFamily.Faults:
                    switch (random.Next(maxValue: 3))
                    {
                        case 0:
                            script.At(at, $"{target} reply pipeline wedges", simulated => simulated.Monitor(target).ForceWedge());
                            break;
                        case 1:
                            script.At(at, $"{target} replies garble for 20 s", simulated =>
                                simulated.Monitor(target).BeginFlakyWindow(TimeSpan.FromSeconds(20), probability: 0.4));
                            break;
                        default:
                            script.At(at, "marginal cable noise for 15 s", static simulated => simulated.NoisyBus(1e-3, TimeSpan.FromSeconds(15)));
                            break;
                    }

                    break;
            }
        }
    }
}
