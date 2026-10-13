using BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Host;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Scenarios;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;
using BrightnessTrayAppDotNET.Models;
using Xunit;
using Xunit.Abstractions;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests;

/// <summary>
/// Named end-to-end scenarios a correct DDC pipeline must survive with no oracle violation. Each one isolates a
/// display event sequence seen on the user's desk or implied by the measured hardware behavior.
/// </summary>
public sealed class DDCScenarioTests(ITestOutputHelper output)
{
    [Fact]
    public void UsersDeskSettlesAfterColdStart()
    {
        ScenarioResult result = Run(WorldConfiguration.UsersDesk(seed: 1), new ScenarioScript("cold-start"));

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// RTX 5080: an LG powered off with its button stays enumerated and only its DDC goes silent. A brightness change
    /// made while it is off must land once it comes back, even though Windows reports nothing either way.
    /// </summary>
    [Fact]
    public void PanelPowerCycleWhileStillEnumeratedRecoversAndKeepsIntent()
    {
        ScenarioScript script = new ScenarioScript("invisible-power-cycle")
            .At(10, "P3 power button off", world => world.PressPowerButton("P3"))
            .At(30, "user sets P3 to 25 while it is off", world => world.SetBrightness("P3", 25))
            .At(70, "P3 power button on", world => world.PressPowerButton("P3"));
        script.Tail = TimeSpan.FromSeconds(90);

        ScenarioResult result = Run(WorldConfiguration.UsersDesk(seed: 2), script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// Settings display numbers shift when a monitor leaves while another is failed but still enumerated. The failed
    /// row keeps its <c>num:</c> ID, so targeted recovery must not bind it to whichever panel now carries that number.
    /// </summary>
    [Fact]
    public void DisplayNumberShiftDuringRecoveryKeepsRowsOnTheirOwnPanels()
    {
        WorldConfiguration configuration = new()
        {
            Seed = 3,
            Name = "number-shift",
            Host = new HostPolicy { DisplayNumbering = DisplayNumbering.Compact },
            Monitors =
            [
                LG("P1", LinkKind.DisplayPort, "211NTYT6N690", 40),
                LG("P2", LinkKind.HDMIDirect, "302NTLE27568", 55),
                LG("P3", LinkKind.DisplayPort, "302NTEPDY724", 60)
            ]
        };
        ScenarioScript script = new ScenarioScript("number-shift-recovery")
            .At(10, "P2 power button off (HDMI stays enumerated)", world => world.PressPowerButton("P2"))
            .At(15, "P1 power button off (DP drops, numbers compact)", world => world.PressPowerButton("P1"));
        script.Tail = TimeSpan.FromSeconds(60);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// Two DP panels return in the opposite order and swap first-free sources, so their display numbers swap while
    /// their rows keep the old <c>num:</c> IDs. Then one panel's reply pipeline wedges (the LG 0xC026258B state) and
    /// a display-settings change makes Refresh re-probe it, so its row fails while still enumerated. A SET would
    /// resynchronize the LG, so the failure has to come from reads alone.
    /// </summary>
    [Fact]
    public void SwappedDisplayNumbersThenChecksumWedgeKeepsRowsOnTheirOwnPanels()
    {
        WorldConfiguration configuration = new()
        {
            Seed = 4,
            Name = "number-swap",
            Monitors =
            [
                LG("P1", LinkKind.DisplayPort, "211NTYT6N690", 40),
                LG("P2", LinkKind.HDMIDirect, "302NTLE27568", 55),
                LG("P3", LinkKind.DisplayPort, "302NTEPDY724", 60)
            ]
        };
        ScenarioScript script = new ScenarioScript("number-swap-wedge")
            .At(10, "P1 power button off", world => world.PressPowerButton("P1"))
            .At(11, "P3 power button off", world => world.PressPowerButton("P3"))
            .At(20, "P3 power button on (takes the first free source)", world => world.PressPowerButton("P3"))
            .At(22, "P1 power button on", world => world.PressPowerButton("P1"))
            .At(80, "P3 reply pipeline wedges", world => world.Monitor("P3").ForceWedge())
            .At(85, "display mode change", world => world.ModeChange());
        script.Tail = TimeSpan.FromSeconds(60);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>Overnight suspend with the brightness curve engaged; every panel must follow the curve after resume.</summary>
    [Fact]
    public void OvernightSleepResumesUnderTheCurve()
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 5);
        configuration.App.CurveEnabled = true;
        ScenarioScript script = new ScenarioScript("overnight-sleep")
            .At(5, "curve at 20", world => world.SetCurve(true, elapsed => elapsed < TimeSpan.FromHours(1) ? 20 : 70))
            .At(60, "system sleeps for 9 h", world => world.SystemSleep(TimeSpan.FromHours(9)));
        script.Tail = TimeSpan.FromSeconds(120);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>Footer power button: every panel off through DDC, then back on through DDC.</summary>
    [Fact]
    public void AppPowersAllPanelsOffAndBackOn()
    {
        ScenarioScript script = new ScenarioScript("app-power-all")
            .At(20, "app powers every panel off", world => world.AppPowerAll(false))
            .At(60, "app powers every panel on", world => world.AppPowerAll(true));
        script.Tail = TimeSpan.FromSeconds(90);

        ScenarioResult result = Run(WorldConfiguration.UsersDesk(seed: 6), script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// First launch on a machine with no displays.json while one panel is off but enumerated (HDMI keeps hot-plug
    /// detect). The panel is later switched on with no Windows event at all, so only recovery probing can find it.
    /// </summary>
    [Fact]
    public void PanelOffAtFirstLaunchIsAcquiredWhenSwitchedOn()
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 7);
        configuration.Monitors[2].StartsOff = true;
        ScenarioScript script = new ScenarioScript("off-at-first-launch")
            .At(30, "P3 power button on", world => world.PressPowerButton("P3"));
        script.Tail = TimeSpan.FromSeconds(90);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// A cable moved to another GPU connector gets a new devnode and DeviceID but keeps its EDID serial. The helper
    /// process of the old identity must not outlive it.
    /// </summary>
    [Fact]
    public void CableMovedToAnotherConnectorKeepsOneRow()
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 8);
        configuration.SpareConnectors.Add(LinkKind.DisplayPort);
        ScenarioScript script = new ScenarioScript("cable-move")
            .At(15, "unplug P2", world => world.Unplug("P2"))
            .At(20, "plug P2 into the spare connector", world => world.Plug("P2", connectorIndex: 3));
        script.Tail = TimeSpan.FromSeconds(80);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>Two identical LG panels trade connectors; devnodes stay with the connectors, EDIDs move with the panels.</summary>
    [Theory]
    [InlineData(MonitorIdentityStrategy.DisplayNumber)]
    [InlineData(MonitorIdentityStrategy.HardwarePort)]
    [InlineData(MonitorIdentityStrategy.EDIDSerial)]
    public void IdenticalPanelsSwappedBetweenConnectorsKeepTheirRows(MonitorIdentityStrategy strategy)
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 9);
        configuration.App.IdentityStrategy = strategy;
        ScenarioScript script = new ScenarioScript("identical-swap-" + strategy)
            .At(15, "unplug P2 and P3", world =>
            {
                world.Unplug("P2");
                world.Unplug("P3");
            })
            .At(25, "plug P2 and P3 into each other's connectors", world =>
            {
                world.Plug("P2", connectorIndex: 2);
                world.Plug("P3", connectorIndex: 1);
            })
            .At(40, "user sets P2 to 35", world => world.SetBrightness("P2", 35));
        script.Tail = TimeSpan.FromSeconds(80);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// The measured driver transient after an arrival: one display stays active for 4.3 s while attached-but-inactive
    /// monitors are listed under every source, sometimes ahead of the active one.
    /// </summary>
    [Fact]
    public void DriverTransientWithInactiveMonitorsListedFirstKeepsIdentities()
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 10);
        configuration.Host.Visibility = HotPlugVisibility.VisiblePowerCycles;
        configuration.Host.ArrivalTransientProbability = 1.0;
        configuration.Host.InactiveListedFirstProbability = 0.5;
        foreach (MonitorSetup monitor in configuration.Monitors) monitor.Link = LinkKind.DisplayPort;
        ScenarioScript script = new ScenarioScript("transient-inactive-listing")
            .At(10, "P1 power button off", world => world.PressPowerButton("P1"))
            .At(20, "P1 power button on", world => world.PressPowerButton("P1"));
        script.Tail = TimeSpan.FromSeconds(90);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// A panel left off for an hour while Windows keeps it enumerated (the RTX 5080 overnight case). Recovery may keep
    /// probing, but it should not launch a fresh helper process for most of those probes.
    /// </summary>
    [Fact]
    public void PanelOffForAnHourDoesNotChurnHelperProcesses()
    {
        // The evening curve keeps stepping down, so btadn keeps writing and notices the panel went away
        ScenarioScript script = new ScenarioScript("hour-off")
            .At(5, "curve stepping down every 10 minutes", world =>
                world.SetCurve(true, elapsed => Math.Max(10, 80 - 10 * (int)(elapsed.TotalMinutes / 10))))
            .At(10, "P3 power button off", world => world.PressPowerButton("P3"))
            .At(3_610, "P3 power button on", world => world.PressPowerButton("P3"));
        script.Tail = TimeSpan.FromSeconds(90);

        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 11);
        configuration.App.CurveEnabled = true;
        configuration.AlwaysWriteTrace = true;
        using SimulationWorld world = new(configuration);
        ScenarioResult result = world.Run(script);
        output.WriteLine(result.Report);

        Assert.True(result.Passed, result.Report);
        Assert.True(
            world.HelperSpawnsPerAwakeHour <= 60,
            $"{world.HelperSpawnsPerAwakeHour:F0} helper launches per awake hour while one panel was off\n{result.Report}");
    }

    /// <summary>
    /// While an adapter-connected panel is off, the adapter still accepts SETs, so its row turns read-degraded. When the
    /// panel returns, a failed write demotes the row to Failed and an early read wedges the LG reply pipeline. Recovery
    /// must still send the resynchronizing SET, as a restart does through the checksum path.
    /// </summary>
    [Fact]
    public void AdapterPanelWedgedOnReturnIsResynchronized()
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 12);
        configuration.App.CurveEnabled = true;
        configuration.Monitors[2].Profile.EarlyReadWedgeProbability = 1.0;
        ScenarioScript script = new ScenarioScript("adapter-wedge-on-return")
            .At(5, "curve stepping down every minute", world =>
                world.SetCurve(true, elapsed => Math.Max(10, 80 - 5 * (int)elapsed.TotalMinutes)))
            .At(20, "P3 power button off", world => world.PressPowerButton("P3"))
            .At(140, "P3 power button on", world => world.PressPowerButton("P3"));
        script.Tail = TimeSpan.FromSeconds(150);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// Companion to <see cref="AdapterPanelWedgedOnReturnIsResynchronized"/>: the same wedge, then an app restart. A new
    /// row starts clean and recovery resynchronizes the panel through the checksum path, which is what the user saw
    /// when restarting btadn fixed a stuck monitor.
    /// </summary>
    [Fact]
    public void AppRestartResynchronizesTheWedgedAdapterPanel()
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 12);
        configuration.App.CurveEnabled = true;
        configuration.Monitors[2].Profile.EarlyReadWedgeProbability = 1.0;
        ScenarioScript script = new ScenarioScript("adapter-wedge-then-restart")
            .At(5, "curve stepping down every minute", world =>
                world.SetCurve(true, elapsed => Math.Max(10, 80 - 5 * (int)elapsed.TotalMinutes)))
            .At(20, "P3 power button off", world => world.PressPowerButton("P3"))
            .At(140, "P3 power button on", world => world.PressPowerButton("P3"))
            .At(220, "user restarts btadn", world => world.RestartApp())
            .At(222, "curve stepping down every minute", world =>
                world.SetCurve(true, elapsed => Math.Max(10, 80 - 5 * (int)elapsed.TotalMinutes)));
        script.Tail = TimeSpan.FromSeconds(150);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// A panel that answers reads but never takes a SET, as when its picture mode locks brightness. Every replay after
    /// recovery fails its read-back, and the row used to cycle demote, recover and replay about once a second for as
    /// long as the target differed. A row that fails again right after recovering must back off instead.
    /// </summary>
    [Fact]
    public void PanelThatIgnoresSetsBacksOffInsteadOfFlapping()
    {
        // Measured 2026-10-12 on this seed: 681 DDC calls with the relapse backoff, 14,421 without it
        const int MaximumBusCalls = 2_000;
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 13);
        configuration.Monitors[0].Profile.DropSetProbability = 1.0;
        ScenarioScript script = new ScenarioScript("ignores-sets")
            .At(10, "user sets P1 to 75", world => world.SetBrightness("P1", 75));
        script.Tail = TimeSpan.FromSeconds(600);

        configuration.AlwaysWriteTrace = true;
        using SimulationWorld world = new(configuration);
        ScenarioResult result = world.Run(script);
        output.WriteLine(result.Report);

        // The panel never shows the target, so the oracle reports P1; the other panels must stay clean
        Assert.All(result.Violations, violation => Assert.Equal("P1", violation.Key));
        Assert.True(
            world.Controller.Operations <= MaximumBusCalls,
            $"{world.Controller.Operations} DDC calls in ten minutes for a panel that ignores SETs\n{result.Report}");
    }

    /// <summary>
    /// A panel that applies a SET 0.6 to 0.9 s after taking it, read back once per change.
    /// The read-back used to wait at most 300 ms whatever the verification delay, so it saw the old value,
    /// and the row winked out until recovery read it back a moment later.
    /// A verification delay longer than the panel's apply time must confirm every change.
    /// The default delay is shorter than that apply time,
    /// so the same changes must demote the row with the value read back, which proves the scenario reaches a mismatch.
    /// </summary>
    [Fact]
    public void LateApplyingPanelIsConfirmedAfterTheVerificationDelay()
    {
        const int DelayCoveringApplyMs = 1_000;
        (ScenarioResult result, List<TraceEntry> trace) = RunLateApplyingPanel(DelayCoveringApplyMs);

        Assert.True(result.Passed, result.Report);
        Assert.DoesNotContain(trace, IsDemotion);

        (_, List<TraceEntry> shortDelayTrace) = RunLateApplyingPanel(TimeConstants.ValidationDwellDefaultMs);
        Assert.Contains(shortDelayTrace, entry =>
            IsDemotion(entry)
            && entry.Message.Contains("Brightness read back as", StringComparison.Ordinal)
            && entry.Message.Contains("after 1 attempt", StringComparison.Ordinal));

        static bool IsDemotion(TraceEntry entry) =>
            entry.Message.StartsWith("MonitorService: demoted", StringComparison.Ordinal);
    }

    /// <summary>
    /// An adapter keeps accepting SETs for its panel after the panel's own button turns it off,
    /// and every read-back comes back garbled.
    /// Once every read-back of a change has failed, the row must be marked unavailable
    /// with a reason that says so and how many attempts were made.
    /// </summary>
    [Fact]
    public void DarkPanelBehindAdapterIsMarkedUnavailable()
    {
        ScenarioScript script = new ScenarioScript("dark-adapter-panel")
            .At(10, "P3 power button off", world => world.PressPowerButton("P3"))
            .At(20, "user sets P3 to 30", world => world.SetBrightness("P3", 30));
        script.Tail = TimeSpan.FromSeconds(40);

        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 17);
        configuration.AlwaysWriteTrace = true;
        using SimulationWorld world = new(configuration);
        ScenarioResult result = world.Run(script);
        output.WriteLine(result.Report);

        Assert.True(result.Passed, result.Report);
        Assert.Contains(world.Trace.Snapshot(), entry =>
            entry.Ticks > TimeSpan.FromSeconds(20).Ticks
            && entry.Message.StartsWith("MonitorService: demoted", StringComparison.Ordinal)
            && entry.Message.Contains(
                $"Brightness could not be read back after {configuration.App.ValidationAttempts} attempts",
                StringComparison.Ordinal));
    }

    private (ScenarioResult Result, List<TraceEntry> Trace) RunLateApplyingPanel(int verificationDelayMs)
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 16);
        configuration.App.ValidationAttempts = 1;
        configuration.App.ValidationDwellMs = verificationDelayMs;
        configuration.Monitors[0].Profile.BrightnessApplyMinimumMs = 600;
        configuration.Monitors[0].Profile.BrightnessApplyMaximumMs = 900;
        ScenarioScript script = new ScenarioScript("late-apply")
            .At(10, "user sets P1 to 75", world => world.SetBrightness("P1", 75))
            .At(20, "user sets P1 to 30", world => world.SetBrightness("P1", 30))
            .At(30, "user sets P1 to 55", world => world.SetBrightness("P1", 55));
        script.Tail = TimeSpan.FromSeconds(60);

        configuration.AlwaysWriteTrace = true;
        using SimulationWorld world = new(configuration);
        ScenarioResult result = world.Run(script);
        output.WriteLine(result.Report);
        return (result, world.Trace.Snapshot());
    }

    /// <summary>
    /// A value set less than 3 s before the panel's button turns it off is never saved, so the next cold boot shows the
    /// older saved brightness. When the app's power-on races that button press, the booting panel refuses the command;
    /// btadn must still re-probe and replay its target, as it does after an accepted power-on.
    /// </summary>
    [Fact]
    public void RefusedPowerOnRacingThePanelButtonReplaysTheTarget()
    {
        ScenarioScript script = new ScenarioScript("power-on-race")
            .At(10, "user sets P1 to 75", world => world.SetBrightness("P1", 75))
            .At(11, "P1 power button off", world => world.PressPowerButton("P1"))
            .At(40, "P1 power button on as the app powers P1 on", world =>
            {
                // Only P1: another panel's accepted power-on would refresh and replay every row anyway
                world.PressPowerButton("P1");
                world.AppPower("P1", on: true);
            });
        script.Tail = TimeSpan.FromSeconds(90);

        ScenarioResult result = Run(WorldConfiguration.UsersDesk(seed: 14), script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// A slow drag of all three panels, one step every 250 ms. A read-back costs as much shared bus time as a write, and
    /// reading back each step halved how often every panel updated. Only where the drag stops is read back.
    /// </summary>
    [Fact]
    public void SlowDragReadsBackOnlyWhereItStops()
    {
        // Measured 2026-10-12 on this seed: 0 read-backs after a 300 ms settle, 36 with the former 50 ms cap
        const int MaximumReadsDuringDrag = 3;
        string[] panels = ["P1", "P2", "P3"];
        int readsBeforeDrag = 0;
        int readsDuringDrag = 0;
        ScenarioScript script = new ScenarioScript("slow-drag")
            .At(9.9, "count reads", world => readsBeforeDrag = world.Display!.BrightnessReads)
            .At(10, "user drags every panel from 80 to 40 over 6 s", world =>
            {
                foreach (string panel in panels) world.Drag(panel, from: 80, to: 40, steps: 24, TimeSpan.FromSeconds(6));
            })
            .At(15.9, "count reads", world => readsDuringDrag = world.Display!.BrightnessReads - readsBeforeDrag);
        script.Tail = TimeSpan.FromSeconds(60);

        ScenarioResult result = Run(WorldConfiguration.UsersDesk(seed: 15), script);

        Assert.True(result.Passed, result.Report);
        Assert.True(
            readsDuringDrag <= MaximumReadsDuringDrag,
            $"{readsDuringDrag} brightness read-backs during a 6 s drag\n{result.Report}");
    }

    private static MonitorSetup LG(string physicalID, LinkKind link, string serial, ushort brightness) =>
        new() { PhysicalID = physicalID, Link = link, SerialDescriptor = serial, InitialBrightness = brightness };

    private ScenarioResult Run(WorldConfiguration configuration, ScenarioScript script)
    {
        configuration.AlwaysWriteTrace = true;
        using SimulationWorld world = new(configuration);
        ScenarioResult result = world.Run(script);
        output.WriteLine(result.Report);
        return result;
    }
}
