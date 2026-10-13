using System.Text;
using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Host;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;
using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Services;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Scenarios;

internal enum ViolationKind
{
    // Two live rows own the same physical panel (the screenshot's two "3: LG ULTRAGEAR+" rows)
    DuplicateRow,

    // Two live rows share an ID, so _entries and every ID lookup alias them
    DuplicateRowID,

    // A live row survives for a panel Windows stopped enumerating longer than the grace ago
    StaleRow,

    // A settled, healthy, enumerated panel has no live row
    MissingRow,

    // A settled, healthy panel's row stays Failed or read-degraded
    StuckUnavailable,

    // A row's DDC entry resolves to a different physical panel than the row describes
    MisboundEntry,

    // A helper resolved a different panel than the DDCMonitor it was handed described
    MisroutedCommand,

    // A brightness SET reached a panel the app powered off, with no user brightness action since
    WriteToPoweredOffPanel,

    // A settled panel's visible brightness differs from the target that owns it
    BrightnessNotConverged,

    // An LG reply pipeline stayed wedged although btadn holds a confirmed brightness it could resynchronize with
    WedgeNeverResynchronized,

    // An exception escaped to the UI thread, a timer, or an app catch-all logger
    UnhandledException,

    // More live helper processes than panels
    HelperProcessLeak
}

internal readonly record struct Violation(long Ticks, ViolationKind Kind, string Key, string Detail)
{
    public override string ToString() =>
        $"{TimeSpan.FromTicks(Ticks).TotalSeconds,9:F3}s {Kind}: {Detail}";
}

/// <summary>Snapshot of one live row, taken on the UI thread.</summary>
internal readonly record struct RowSnapshot(
    MonitorInfo Row,
    string ID,
    string EDIDKey,
    int DisplayNumber,
    SliderState State,
    bool IsReadDegraded,
    int RoundedBrightness,
    bool HasUserBrightness,
    bool IsPowerSuppressed,
    string? LastDDCError)
{
    public static RowSnapshot From(MonitorInfo row) =>
        new(
            row,
            row.ID,
            row.EDIDKey,
            row.DisplayNumber,
            row.SliderState,
            row.IsReadDegraded,
            row.RoundedBrightness,
            row.HasUserBrightness,
            row.SuppressDDCRecoveryForPowerIntent,
            row.LastDDCError);

    public string Describe() =>
        $"{ID} [{EDIDKey}] #{DisplayNumber} {State}{(IsReadDegraded ? "+degraded" : string.Empty)} b={RoundedBrightness}";
}

/// <summary>
/// Ground-truth checks over btadn's state. Rows are mapped to physical panels through their EDIDKey (serial or devnode),
/// entries through the real helper matcher against the live topology, and expectations only apply to panels that have
/// been enumerated, powered and DDC-healthy for longer than the settle grace.
/// </summary>
internal sealed class SimulationOracle(SimulationClock clock, SimulationTrace trace)
{
    private readonly Lock _gate = new();
    private readonly List<Violation> _violations = [];
    private readonly List<string> _observations = [];
    private readonly HashSet<(ViolationKind Kind, string Key)> _reported = [];
    private readonly Dictionary<SimulatedMonitor, long> _lastUserActionTicks = [];

    // The last power state the app was asked to put each panel in, through SetPowerStateAsync
    private readonly Dictionary<SimulatedMonitor, bool> _lastAppPowerIntentOn = [];

    // When a helper command last resolved to each panel
    private readonly Dictionary<SimulatedMonitor, long> _lastCommandTicks = [];
    private long _lastPowerActionTicks = long.MinValue;

    public IReadOnlyList<Violation> Violations
    {
        get
        {
            lock (_gate) return [.. _violations];
        }
    }

    public IReadOnlyList<string> Observations
    {
        get
        {
            lock (_gate) return [.. _observations];
        }
    }

    public void Report(ViolationKind kind, string key, string detail)
    {
        lock (_gate)
        {
            if (!_reported.Add((kind, key))) return;

            _violations.Add(new Violation(clock.ElapsedTicks, kind, key, detail));
        }

        trace.Add("oracle", $"VIOLATION {kind}: {detail}");
    }

    public void Observe(string observation)
    {
        lock (_gate)
        {
            if (_observations.Contains(observation)) return;

            _observations.Add(observation);
        }

        trace.Add("oracle", "observation: " + observation);
    }

    public void NoteUserBrightnessAction(SimulatedMonitor? monitor)
    {
        lock (_gate)
        {
            if (monitor == null) return;

            _lastUserActionTicks[monitor] = clock.ElapsedTicks;
        }
    }

    public void NotePowerAction()
    {
        lock (_gate) _lastPowerActionTicks = clock.ElapsedTicks;
    }

    public void NoteAppPowerIntent(SimulatedMonitor? monitor, bool on)
    {
        lock (_gate)
        {
            if (monitor == null) return;

            _lastAppPowerIntentOn[monitor] = on;
        }
    }

    /// <summary>The power state the app was last asked to give the panel, or null when it never was.</summary>
    private bool? LastAppPowerIntentOn(SimulatedMonitor monitor)
    {
        lock (_gate) return _lastAppPowerIntentOn.TryGetValue(monitor, out bool on) ? on : null;
    }

    public void ObserveRouting(HelperRoutingEvent routing)
    {
        if (routing.Resolved != null)
        {
            lock (_gate) _lastCommandTicks[routing.Resolved] = routing.Ticks;
        }

        if (routing.Intended == null || routing.Resolved == null) return;
        if (ReferenceEquals(routing.Intended, routing.Resolved)) return;

        Report(
            ViolationKind.MisroutedCommand,
            $"{routing.Intended.PhysicalID}->{routing.Resolved.PhysicalID}:{routing.Kind}",
            $"{routing.Kind} 0x{routing.Code:X2}={routing.Value} meant for {routing.Intended.PhysicalID} "
            + $"('{routing.ParentName}') reached {routing.Resolved.PhysicalID} ('{routing.ResolvedName}') by {routing.MatchRule} match");
    }

    /// <summary>
    /// A brightness SET reached a panel. Called on the UI thread with btadn's rows for that panel, so the check uses
    /// btadn's own power intent: a row whose accepted power-off still suppresses traffic must not be written.
    /// </summary>
    public void CheckBrightnessWrite(SimulatedMonitor monitor, uint value, IReadOnlyList<RowSnapshot> ownedRows)
    {
        RowSnapshot[] suppressed = [.. ownedRows.Where(row => row.IsPowerSuppressed)];
        if (suppressed.Length == 0) return;

        Report(
            ViolationKind.WriteToPoweredOffPanel,
            monitor.PhysicalID,
            $"brightness SET {value} sent to {monitor.PhysicalID} while its row {suppressed[0].Describe()} "
            + "still holds the app's power-off intent");
    }

    /// <summary>Checks that hold at every instant: no panel shown twice, no ID shared. Runs on the UI thread.</summary>
    public void CheckContinuous(IReadOnlyList<RowSnapshot> rows, OwnershipResolver ownership)
    {
        foreach (IGrouping<string, RowSnapshot> sameID in rows.GroupBy(row => row.ID).Where(group => group.Count() > 1))
        {
            Report(
                ViolationKind.DuplicateRowID,
                sameID.Key,
                $"{sameID.Count()} live rows share ID {sameID.Key}: " + string.Join("; ", sameID.Select(row => row.Describe())));
        }

        Dictionary<SimulatedMonitor, List<RowSnapshot>> byOwner = [];
        foreach (RowSnapshot row in rows)
        {
            SimulatedMonitor? owner = ownership.OwnerOf(row.EDIDKey);
            if (owner == null) continue;

            if (!byOwner.TryGetValue(owner, out List<RowSnapshot>? owned)) byOwner[owner] = owned = [];
            owned.Add(row);
        }

        foreach ((SimulatedMonitor owner, List<RowSnapshot> owned) in byOwner)
        {
            if (owned.Count < 2) continue;

            Report(
                ViolationKind.DuplicateRow,
                owner.PhysicalID,
                $"{owned.Count} live rows show {owner.PhysicalID}: " + string.Join("; ", owned.Select(row => row.Describe())));
        }
    }

    // DDCRecoveryService backs a long-failing row off to one probe per maximum interval; a row stuck past that and the
    // probe itself never got retried
    private static readonly long RecoveryBackoffAllowanceTicks =
        TimeSpan.FromMilliseconds(TimeConstants.DDCRecoveryMaximumRetryIntervalMs + 5_000).Ticks;

    /// <summary>Checks that hold once the topology has been quiet and the panels healthy for the settle grace.</summary>
    public void CheckSettled(SettledState state)
    {
        long now = clock.ElapsedTicks;
        long graceTicks = state.Grace.Ticks;
        CheckContinuous(state.Rows, state.Ownership);

        Dictionary<SimulatedMonitor, List<RowSnapshot>> owned = [];
        List<RowSnapshot> unowned = [];
        foreach (RowSnapshot row in state.Rows)
        {
            SimulatedMonitor? owner = state.Ownership.OwnerOf(row.EDIDKey);
            if (owner == null)
            {
                unowned.Add(row);
                continue;
            }

            if (!owned.TryGetValue(owner, out List<RowSnapshot>? list)) owned[owner] = list = [];
            list.Add(row);
        }

        foreach (RowSnapshot row in unowned) Observe($"row {row.Describe()} has no stable physical identity");

        foreach (SimulatedMonitor monitor in state.Monitors)
        {
            bool active = state.Host.IsMonitorActive(monitor);
            List<RowSnapshot> rows = owned.GetValueOrDefault(monitor) ?? [];
            if (!active)
            {
                long sinceInactive = state.LastActiveTicks.GetValueOrDefault(monitor, now);
                if (rows.Count > 0 && now - sinceInactive > graceTicks)
                {
                    Report(
                        ViolationKind.StaleRow,
                        monitor.PhysicalID,
                        $"{monitor.PhysicalID} is not enumerated but still has row(s): {string.Join("; ", rows.Select(row => row.Describe()))}");
                }

                continue;
            }

            // MonitorService resynchronizes a checksum-wedged reply pipeline with a SET of the last confirmed value,
            // unless the app was last told to keep the panel off and the panel was woken some other way
            long wedgedSince = monitor.WedgedSinceTicks;
            if (wedgedSince >= 0
                && now - wedgedSince > TimeSpan.FromSeconds(60).Ticks
                && monitor.PowerState == PanelPowerState.On
                && rows.Count > 0
                && state.LastBusBrightness(rows[0].EDIDKey) != null
                && LastAppPowerIntentOn(monitor) != false)
            {
                Report(
                    ViolationKind.WedgeNeverResynchronized,
                    monitor.PhysicalID,
                    $"{monitor.PhysicalID} has answered with checksum failures since "
                    + $"{TimeSpan.FromTicks(wedgedSince).TotalSeconds:F1}s; its row {rows[0].Describe()} has a confirmed "
                    + $"bus value of {state.LastBusBrightness(rows[0].EDIDKey)} but no resynchronizing SET was sent");
            }

            if (!IsSettled(state, monitor, now, graceTicks)) continue;

            if (rows.Count == 0)
            {
                if (unowned.Count == 0)
                {
                    Report(
                        ViolationKind.MissingRow,
                        monitor.PhysicalID,
                        $"{monitor.Describe()} is enumerated and healthy but has no row");
                }

                continue;
            }

            RowSnapshot only = rows[0];
            bool unavailable = only.State == SliderState.Failed || only.IsReadDegraded;
            if (only.IsPowerSuppressed)
            {
                long lastPower;
                lock (_gate) lastPower = _lastPowerActionTicks;

                // The intent may outlive a button press, but never the app's own later power-on request
                if (LastAppPowerIntentOn(monitor) == true && now - lastPower >= graceTicks)
                {
                    Report(
                        ViolationKind.StuckUnavailable,
                        monitor.PhysicalID,
                        $"{monitor.Describe()} has been healthy since "
                        + $"{TimeSpan.FromTicks(monitor.HealthySinceTicks(now)).TotalSeconds:F1}s but its row "
                        + $"{only.Describe()} still holds a power-off intent although the app was last asked to power it on");
                    continue;
                }

                // A working row keeps the intent through a button power-on that nothing reports to the app.
                // An unavailable row is still probed, so recovery must notice the panel answering again
                if (!unavailable)
                {
                    Observe($"{monitor.PhysicalID} is on but its row is still suppressed by an earlier app power-off");
                    continue;
                }
            }

            if (unavailable)
            {
                if (IsSettled(state, monitor, now, graceTicks + RecoveryBackoffAllowanceTicks)
                    && !IsStillProbingUnreliablePanel(state, monitor, now))
                {
                    Report(
                        ViolationKind.StuckUnavailable,
                        monitor.PhysicalID,
                        $"{monitor.Describe()} has been healthy since "
                        + $"{TimeSpan.FromTicks(monitor.HealthySinceTicks(now)).TotalSeconds:F1}s but its row is {only.Describe()} "
                        + $"(last error: {only.LastDDCError})");
                }

                continue;
            }

            CheckConvergence(state, monitor, only, now, graceTicks);
        }

        foreach (MonitorService.MonitorBinding binding in state.Bindings)
        {
            List<RowSnapshot> boundRows = [.. state.Rows.Where(candidate => candidate.ID == binding.ID)];
            if (boundRows.Count == 0)
            {
                Observe($"entry {binding.ID} has no live row");
                continue;
            }

            RowSnapshot row = boundRows[0];
            SimulatedMonitor? owner = state.Ownership.OwnerOf(row.EDIDKey);
            SimulatedMonitor? reached = state.Ownership.ResolveLikeHelper(binding.DDC);
            if (owner == null || reached == null || ReferenceEquals(owner, reached)) continue;

            Report(
                ViolationKind.MisboundEntry,
                binding.ID,
                $"row {row.Describe()} shows {owner.PhysicalID} but its DDC entry "
                + $"('{binding.DDC.Name}', {binding.DDC.DeviceID}, serial '{binding.DDC.EDIDSerial}') drives {reached.PhysicalID}");
        }

        if (state.LiveHelpers > state.Monitors.Count)
        {
            Report(
                ViolationKind.HelperProcessLeak,
                "helpers",
                $"{state.LiveHelpers} live helper processes ({state.HelperKeys} keys) for {state.Monitors.Count} panels");
        }
    }

    private void CheckConvergence(SettledState state, SimulatedMonitor monitor, RowSnapshot row, long now, long graceTicks)
    {
        if (row.State == SliderState.Disabled) return;

        long lastUser;
        long lastPower;
        lock (_gate)
        {
            lastUser = _lastUserActionTicks.GetValueOrDefault(monitor, long.MinValue);
            lastPower = _lastPowerActionTicks;
        }

        if (now - lastUser < graceTicks || now - lastPower < graceTicks) return;
        if (row.State == SliderState.CurveActive && now - state.Curve.LastTargetChangeTicks < graceTicks) return;

        int expected = row.State == SliderState.CurveActive ? state.Curve.ExpectedRowTarget(row.Row) : row.RoundedBrightness;
        int actual = monitor.VisibleBrightnessPercent();
        if (Math.Abs(actual - expected) <= 1) return;

        Report(
            ViolationKind.BrightnessNotConverged,
            monitor.PhysicalID,
            $"{monitor.PhysicalID} shows {actual} but its {row.State} row {row.ID} targets {expected}");
    }

    /// <summary>
    /// True for a panel whose replies miss the host's 50 ms read window more often than not while btadn reads once per
    /// attempt, as long as btadn still sent it a command within the longest recovery backoff. Every replay's
    /// verification is then close to a coin flip, so the row can cycle between recovered and unavailable without btadn
    /// having given up; the check falls back to requiring that it keeps probing.
    /// </summary>
    private bool IsStillProbingUnreliablePanel(SettledState state, SimulatedMonitor monitor, long now)
    {
        if (state.ValidationAttempts > 1 || monitor.Profile.ReplyPreparationMaximumMs <= DDCTiming.WindowsReplyDelayMs)
            return false;

        long lastCommand;
        lock (_gate)
        {
            if (!_lastCommandTicks.TryGetValue(monitor, out lastCommand)) return false;
        }

        if (now - lastCommand > RecoveryBackoffAllowanceTicks) return false;

        Observe($"{monitor.PhysicalID} answers too few single reads to stay recovered; btadn keeps probing it");
        return true;
    }

    private static bool IsSettled(SettledState state, SimulatedMonitor monitor, long now, long graceTicks)
    {
        if (state.Host.IsTransientActive) return false;

        long activeSince = state.Host.ActiveSinceTicks(monitor);
        long healthySince = monitor.HealthySinceTicks(now);
        return activeSince >= 0
               && healthySince >= 0
               && now - activeSince >= graceTicks
               && now - healthySince >= graceTicks;
    }

    public string Summarize()
    {
        StringBuilder builder = new();
        foreach (Violation violation in Violations) builder.AppendLine("  " + violation);
        foreach (string observation in Observations) builder.AppendLine("  note: " + observation);
        return builder.ToString();
    }
}

/// <summary>Inputs for <see cref="SimulationOracle.CheckSettled"/>.</summary>
internal sealed class SettledState
{
    public required IReadOnlyList<RowSnapshot> Rows { get; init; }
    public required IReadOnlyList<MonitorService.MonitorBinding> Bindings { get; init; }
    public required IReadOnlyList<SimulatedMonitor> Monitors { get; init; }
    public required HostDisplayStack Host { get; init; }
    public required OwnershipResolver Ownership { get; init; }
    public required CurveDriver Curve { get; init; }
    public required IReadOnlyDictionary<SimulatedMonitor, long> LastActiveTicks { get; init; }
    public required TimeSpan Grace { get; init; }
    public required int LiveHelpers { get; init; }
    public required int HelperKeys { get; init; }

    // AppSettings.ValidationAttempts: reads per acquisition probe and per verification
    public required int ValidationAttempts { get; init; }

    // displays.json LastBusBrightness for an EDIDKey: the read-back-confirmed value btadn may reassert
    public required Func<string, int?> LastBusBrightness { get; init; }
}

/// <summary>Maps btadn identities back to physical panels using simulator ground truth.</summary>
internal sealed class OwnershipResolver(IReadOnlyList<SimulatedMonitor> monitors, HostDisplayStack host)
{
    /// <summary>
    /// <c>edid:serial</c> maps to the unique panel with that serial; <c>port:DeviceID</c> to the panel that last
    /// occupied that devnode. Adapter-name keys and shared serials have no stable owner.
    /// </summary>
    public SimulatedMonitor? OwnerOf(string EDIDKey)
    {
        if (EDIDKey.StartsWith(value: "edid:", StringComparison.Ordinal))
        {
            string serial = EDIDKey[5..];
            List<SimulatedMonitor> matches = [.. monitors.Where(monitor => monitor.EDIDSerial == serial)];
            return matches.Count == 1 ? matches[0] : null;
        }

        if (!EDIDKey.StartsWith(value: "port:", StringComparison.Ordinal)) return null;

        string deviceID = EDIDKey[5..];
        foreach (SimulatedMonitor monitor in monitors)
        {
            if (host.NodesOf(monitor).Any(node => string.Equals(node.DeviceID, deviceID, StringComparison.OrdinalIgnoreCase)))
                return monitor;
        }

        return null;
    }

    /// <summary>Which panel a helper command for <paramref name="ddc"/> would reach right now, by the real matcher.</summary>
    public SimulatedMonitor? ResolveLikeHelper(DDCMonitor ddc)
    {
        List<DDCMonitor> helperView = [];
        Dictionary<DDCMonitor, GPUConnector> connectors = [];
        foreach (ActiveMonitorView view in host.EnumDisplayMonitors())
        {
            List<DisplayDeviceView> devices = host.EnumDisplayDevices(view.GDIName);
            int deviceIndex = DisplayService.SelectMonitorDeviceIndex(
                [.. devices.Select(static device => device.IsActive ? DisplayService.DisplayDeviceActive : 0)]);
            string deviceID = deviceIndex >= 0 ? devices[deviceIndex].DeviceID : view.GDIName;
            string instancePath = deviceIndex >= 0
                ? DisplayService.ParseDisplayInstancePath(devices[deviceIndex].InterfacePath)
                : string.Empty;
            byte[]? edid = string.IsNullOrEmpty(instancePath) ? null : host.ReadEDIDRegistry(instancePath);
            DDCMonitor candidate = new()
            {
                Handle = view.Handle,
                Name = view.GDIName,
                DeviceID = deviceID,
                DisplayInstancePath = instancePath,
                EDIDSerial = edid == null ? string.Empty : EDIDParser.ExtractSerial(edid)
            };
            helperView.Add(candidate);
            connectors[candidate] = view.Connector;
        }

        DDCMonitor? match = DDCHelperServer.FindMatchingMonitor(helperView, DDCHelperServer.DDCHelperMonitorIdentity.From(ddc));
        return match == null ? null : connectors[match].Monitor;
    }
}
