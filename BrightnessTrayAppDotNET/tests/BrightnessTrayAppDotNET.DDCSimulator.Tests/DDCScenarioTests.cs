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
    /// The user's nights (btadn log 2026-10-27): the app hard-powers all three panels off, and in the morning their own
    /// buttons switch them back on. Windows reports neither on the RTX 5080, so only recovery probes can notice. The
    /// rows used to keep the power-off intent until a restart, which refused every curve write and master drag.
    /// </summary>
    [Fact]
    public void ButtonPowerOnAfterAppHardOffResumesTheCurve()
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 16);
        configuration.App.CurveEnabled = true;
        configuration.App.PowerOffMode = PowerOffMode.Hard;
        ScenarioScript script = new ScenarioScript("app-hard-off-button-on")
            .At(5, "curve at 17 when powered off, 12 overnight, 90 from the morning", world =>
                world.SetCurve(true, elapsed => elapsed.TotalMinutes < 1 ? 17 : elapsed.TotalMinutes < 30 ? 12 : 90))
            .At(20, "app powers every panel off (hard)", world => world.AppPowerAll(false))
            .At(1_900, "P1 power button on", world => world.PressPowerButton("P1"))
            .At(1_902, "P2 power button on", world => world.PressPowerButton("P2"))
            .At(1_905, "P3 power button on", world => world.PressPowerButton("P3"));
        script.Tail = TimeSpan.FromSeconds(150);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// The user's morning (btadn log 2026-10-29): after the app's hard-off, the button power-on leaves the panel behind
    /// a DP-to-HDMI converter with a wedged reply pipeline that answers garbage (0x89, 0x85) instead of checksum
    /// failures. Reads alone never cleared it for five minutes; the restarted app's first SET did. Recovery must send
    /// that SET without waiting for a restart.
    /// </summary>
    [Fact]
    public void GarbageWedgeAfterButtonPowerOnFromAppHardOffIsResynchronized()
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 17);
        configuration.App.CurveEnabled = true;
        configuration.App.PowerOffMode = PowerOffMode.Hard;
        configuration.Monitors[1].Profile.WedgeGarblesReplies = true;
        ScenarioScript script = new ScenarioScript("app-hard-off-garbage-wedge")
            .At(5, "curve at 17 when powered off, 12 overnight, 90 from the morning", world =>
                world.SetCurve(true, elapsed => elapsed.TotalMinutes < 1 ? 17 : elapsed.TotalMinutes < 10 ? 12 : 90))
            .At(20, "app powers every panel off (hard)", world => world.AppPowerAll(false))
            .At(600, "power buttons on; P2 comes back with its reply pipeline wedged", world =>
            {
                foreach (string panel in UsersDeskPanels) world.PressPowerButton(panel);
                world.Monitor("P2").ForceWedge();
            });
        script.Tail = TimeSpan.FromSeconds(150);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// Safety companion to the hard-off scenarios: after a soft-off the LG stays on the bus in standby, and a SET wakes
    /// some panels from that. Recovery may only read it, must keep the intent while the panel reports it is not on, and
    /// must still notice once the panel is switched back on.
    /// </summary>
    [Fact]
    public void SoftPowerOffIsOnlyReadUntilThePanelIsSwitchedBackOn()
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 18);
        configuration.App.CurveEnabled = true;
        configuration.App.PowerOffMode = PowerOffMode.Soft;
        ScenarioScript script = new ScenarioScript("app-soft-off-button-on")
            .At(5, "curve at 17 overnight, 90 from the morning", world =>
                world.SetCurve(true, elapsed => elapsed.TotalMinutes < 10 ? 17 : 90))
            .At(20, "app powers every panel off (soft)", world => world.AppPowerAll(false))
            .At(600, "power buttons: standby to off", world =>
            {
                foreach (string panel in UsersDeskPanels) world.PressPowerButton(panel);
            })
            .At(605, "power buttons: off to on", world =>
            {
                foreach (string panel in UsersDeskPanels) world.PressPowerButton(panel);
            });
        script.Tail = TimeSpan.FromSeconds(150);

        ScenarioResult result = Run(configuration, script);

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// The other side of the 2026-10-29 fix: all night the adapters in front of the hard-off panels answer garbage and
    /// acknowledge SETs. Neither may pass for the panel coming back: every row keeps its power-off intent and nothing
    /// turns read-degraded. Probing all three dark panels must not churn helper processes either.
    /// </summary>
    [Fact]
    public void HardOffPanelsStayPoweredOffOvernight()
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 19);
        configuration.App.CurveEnabled = true;
        configuration.App.PowerOffMode = PowerOffMode.Hard;
        configuration.AlwaysWriteTrace = true;
        List<RowSnapshot> rowsAnHourLater = [];
        ScenarioScript script = new ScenarioScript("app-hard-off-overnight")
            .At(5, "curve stepping down every 10 minutes", world =>
                world.SetCurve(true, elapsed => Math.Max(10, 80 - 10 * (int)(elapsed.TotalMinutes / 10))))
            .At(20, "app powers every panel off (hard)", world => world.AppPowerAll(false))
            .At(3_620, "rows an hour later", world => world.Dispatcher.Post(() =>
                rowsAnHourLater.AddRange(world.Service!.Monitors.Select(RowSnapshot.From))));
        script.Tail = TimeSpan.FromSeconds(30);

        using SimulationWorld world = new(configuration);
        ScenarioResult result = world.Run(script);
        output.WriteLine(result.Report);

        Assert.True(result.Passed, result.Report);
        Assert.Equal(expected: 3, rowsAnHourLater.Count);
        Assert.All(rowsAnHourLater, row =>
        {
            Assert.Equal(PowerOffIntent.HardOff, row.PowerOffIntent);
            Assert.False(row.IsReadDegraded, row.Describe());
        });
        Assert.True(
            world.HelperSpawnsPerAwakeHour <= 60,
            $"{world.HelperSpawnsPerAwakeHour:F0} helper launches per awake hour while three panels were off\n"
            + result.Report);
    }

    /// <summary>
    /// A panel behind an adapter comes back from its own power cycle with its reply pipeline wedged into garbage, as
    /// DISPLAY2's was, while blind writes are off. Its row failed while the panel was dark, so the resynchronizing SET
    /// of its last confirmed bus value is the only write recovery may send. It used to be sent for checksum failures
    /// only, which left this row failed until a restart.
    /// </summary>
    [Fact]
    public void GarbageWedgeIsResynchronizedWithoutBlindWrites()
    {
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 12);
        configuration.App.CurveEnabled = true;
        configuration.App.AllowBlindDDCWrites = false;
        configuration.Monitors[2].Profile.WedgeGarblesReplies = true;
        ScenarioScript script = new ScenarioScript("adapter-garbage-wedge-on-return")
            .At(5, "curve stepping down every minute", world =>
                world.SetCurve(true, elapsed => Math.Max(10, 80 - 5 * (int)elapsed.TotalMinutes)))
            .At(20, "P3 power button off", world => world.PressPowerButton("P3"))
            .At(140, "P3 power button on; its reply pipeline comes back wedged", world =>
            {
                world.PressPowerButton("P3");
                world.Monitor("P3").ForceWedge();
            });
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

    /// <summary>
    /// The user's converter panel on 2026-10-26: while it is dark its DP-to-HDMI adapter acknowledges every SET and
    /// answers reads with garbage, so its row turns read-degraded on an acknowledgement alone. The panel then boots,
    /// answers reads with its saved brightness and drops SETs for a while, as DISPLAY1 did at 10:53:32-34. Once a
    /// read-back shows the panel ignored a SET it acknowledged, the row must stop posing as best-effort working: it is
    /// marked unavailable at once and not made read-degraded again before a write is confirmed.
    /// </summary>
    [Fact]
    public void ConverterPanelThatDropsAcknowledgedSetsIsNotKeptReadDegraded()
    {
        const double PowerOnSeconds = 60;
        const double UserSetSeconds = 62.2;
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 19);

        // P3 sits behind a DP-to-HDMI adapter. Its MCU answers exactly 2 s after the button, and every read from then
        // on succeeds, so each read-back of a dropped SET names the saved brightness
        MonitorProfile panel = configuration.Monitors[2].Profile;
        panel.ColdBootMinimumMs = 2_000;
        panel.ColdBootMaximumMs = 2_000;
        panel.IgnoresSetsAfterColdBootMs = 20_000;
        panel.BootFlakyProbabilityMinimum = 0;
        panel.BootFlakyProbabilityMaximum = 0;
        panel.EarlyReadWedgeProbability = 0;

        List<(long Ticks, bool ReadDegraded)> samples = [];
        Lock samplesGate = new();
        ITimer? sampler = null;
        ScenarioScript script = new ScenarioScript("converter-drops-acknowledged-sets")
            .At(10, "P3 power button off", world => world.PressPowerButton("P3"))
            .At(20, "user sets P3 to 30 while it is dark", world => world.SetBrightness("P3", 30))
            .At(PowerOnSeconds, "P3 power button on; sample its row every 100 ms", world =>
            {
                world.PressPowerButton("P3");
                sampler = world.Clock.CreateTimer(
                    _ => world.Dispatcher.Post(() =>
                    {
                        MonitorInfo? row = world.FindRow("P3");
                        if (row == null) return;

                        lock (samplesGate) samples.Add((world.Clock.ElapsedTicks, row.IsReadDegraded));
                    }),
                    state: null,
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(100));
            })
            .At(UserSetSeconds, "user sets P3 to 35 while it still drops SETs", world => world.SetBrightness("P3", 35));
        script.Tail = TimeSpan.FromSeconds(90);

        configuration.AlwaysWriteTrace = true;
        using SimulationWorld world = new(configuration);
        ScenarioResult result = world.Run(script);
        sampler?.Dispose();
        output.WriteLine(result.Report);

        // Only P3 is written after the boot, so the first exhausted verification is the read-back of its dropped 35
        List<TraceEntry> exhausted =
        [
            .. world.Trace.Snapshot().Where(entry =>
                entry.Ticks > TimeSpan.FromSeconds(UserSetSeconds).Ticks
                && entry.Message.StartsWith("MonitorService: verification exhausted", StringComparison.Ordinal))
        ];
        Assert.NotEmpty(exhausted);
        long contradictedAt = exhausted[0].Ticks;
        long settledAt = contradictedAt + TimeSpan.FromMilliseconds(100).Ticks;
        long windowEnd = contradictedAt + TimeSpan.FromSeconds(15).Ticks;
        lock (samplesGate)
        {
            // The row went in read-degraded on the adapter's acknowledgement while the panel was dark
            Assert.Contains(samples, sample => sample.Ticks < contradictedAt && sample.ReadDegraded);

            List<(long Ticks, bool ReadDegraded)> afterContradiction =
                [.. samples.Where(sample => sample.Ticks > settledAt && sample.Ticks <= windowEnd)];
            Assert.NotEmpty(afterContradiction);
            Assert.DoesNotContain(afterContradiction, sample => sample.ReadDegraded);
        }

        Assert.True(result.Passed, result.Report);
    }

    /// <summary>
    /// The app hard-powers every panel off, the RTX 5080 keeps them enumerated, and their own buttons switch them back
    /// on. The session unlock that follows re-probes the adapter panels while they are still booting, so their reads
    /// come back as garbage and their rows are marked unavailable, as on the user's desk (2026-10-12).
    /// Recovery skipped rows the app had powered off, so both stayed unavailable until btadn restarted.
    /// Every row must work again and hold no power-off intent.
    /// </summary>
    [Fact]
    public void ButtonPowerOnAfterAppPowerOffReacquiresEveryPanel()
    {
        string[] panels = ["P1", "P2", "P3"];
        WorldConfiguration configuration = WorldConfiguration.UsersDesk(seed: 18);
        configuration.App.PowerOffMode = PowerOffMode.Hard;
        configuration.App.CurveEnabled = true;
        foreach (MonitorSetup monitor in configuration.Monitors)
        {
            if (monitor.Link != LinkKind.DisplayPortToHDMIAdapter) continue;

            // Still booting when the unlock's re-probe reaches them
            monitor.Profile.ColdBootMinimumMs = 4_000;
            monitor.Profile.ColdBootMaximumMs = 6_000;
        }

        ScenarioScript script = new ScenarioScript("app-off-button-on")
            .At(5, "curve holding 40", world => world.SetCurve(true, _ => 40))
            .At(10, "app powers every panel off", world => world.AppPowerAll(on: false))
            .At(60, "every panel's power button on", world =>
            {
                foreach (string panel in panels) world.PressPowerButton(panel);
            })
            .At(61, "session unlock", world => world.SessionUnlock());
        script.Tail = TimeSpan.FromSeconds(120);

        configuration.AlwaysWriteTrace = true;
        using SimulationWorld world = new(configuration);
        ScenarioResult result = world.Run(script);
        output.WriteLine(result.Report);

        Assert.True(result.Passed, result.Report);
        Assert.Contains(world.Trace.Snapshot(), entry =>
            entry.Ticks > TimeSpan.FromSeconds(60).Ticks
            && entry.Message.StartsWith("MonitorService: demoted", StringComparison.Ordinal));
        Assert.All(world.Service!.Monitors, row =>
        {
            Assert.True(row.IsHardwareFunctional, world.DescribeRows());
            Assert.False(row.SuppressDDCRecoveryForPowerIntent, world.DescribeRows());
        });
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

    private static readonly string[] UsersDeskPanels = ["P1", "P2", "P3"];

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
