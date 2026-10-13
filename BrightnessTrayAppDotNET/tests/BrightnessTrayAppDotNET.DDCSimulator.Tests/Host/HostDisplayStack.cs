using System.Globalization;
using System.Text;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Host;

/// <summary>Notifications btadn subscribes to through DisplayEventManager.</summary>
internal enum HostEventKind
{
    MonitorInterfaceArrival,
    MonitorInterfaceRemoval,
    DisplaySettingsChanged,
    PowerResume,
    SessionUnlock
}

/// <summary>
/// A monitor devnode under HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY. Windows keys it by hardware ID and connector
/// UID, not by serial, so two identical monitors swapped between connectors trade devnodes while each devnode's
/// registry EDID is rewritten for whichever unit arrived last.
/// </summary>
internal sealed class DeviceNode
{
    public required string HardwareID { get; init; }
    public required int Index { get; init; }
    public required int ConnectorUID { get; init; }
    public required string InstancePrefix { get; init; }
    public SimulatedMonitor? Monitor { get; set; }
    public byte[]? EDID { get; set; }
    public long EDIDVisibleAtTicks { get; set; }
    public bool Present { get; set; }

    public bool IsDefaultMonitor => HardwareID == HostDisplayStack.DefaultMonitorHardwareID;

    public string DeviceID =>
        $@"MONITOR\{HardwareID}\{{4d36e96e-e325-11ce-bfc1-08002be10318}}\{Index.ToString("D4", CultureInfo.InvariantCulture)}";

    public string InstancePath => $@"DISPLAY\{HardwareID}\{InstancePrefix}&UID{ConnectorUID}";

    public string InterfacePath =>
        $@"\\?\DISPLAY#{HardwareID}#{InstancePrefix}&UID{ConnectorUID}#{{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}}";
}

/// <summary>One EnumDisplayMonitors result.</summary>
internal readonly record struct ActiveMonitorView(IntPtr Handle, string GDIName, int X, int Y, GPUConnector Connector);

/// <summary>One EnumDisplayDevices(source, index) result.</summary>
internal readonly record struct DisplayDeviceView(string DeviceID, string InterfacePath, bool IsActive, DeviceNode Node);

/// <summary>
/// The Windows display stack between the GPU connectors and btadn: hot-plug detect, CCD paths and sources, GDI names,
/// HMONITOR handles, monitor devnodes, the EDID registry and the notifications btadn listens for. Reactions follow the
/// measured event order, and the policy decides the quirks: renumbering, transients, attach lag, EDID-less devnodes.
/// </summary>
internal sealed class HostDisplayStack
{
    public const string DefaultMonitorHardwareID = "Default_Monitor";
    private const int DisplayWidth = 3840;
    private const int HandleStride = 0x10002;

    private readonly Lock _gate = new();
    private readonly SimulationClock _clock;
    private readonly SimulationTrace _trace;
    private readonly Random _random;
    private readonly List<GPUConnector> _connectors = [];
    private readonly Dictionary<GPUConnector, TargetState> _targets = [];
    private readonly Dictionary<(string HardwareID, int ConnectorUID), DeviceNode> _nodes = [];
    private readonly Dictionary<string, int> _nodeCountByHardwareID = new(StringComparer.Ordinal);
    private long _nextMonitorHandle = 0x10001;
    private int _highestGDINumber;
    private bool _systemAsleep;
    private long _transientUntilTicks = long.MinValue;

    public HostDisplayStack(SimulationClock clock, SimulationTrace trace, HostPolicy policy, int seed)
    {
        _clock = clock;
        _trace = trace;
        Policy = policy;
        _random = new Random(seed);
    }

    public HostPolicy Policy { get; }

    public IReadOnlyList<GPUConnector> Connectors => _connectors;

    /// <summary>Raised on a pool thread, outside the stack lock, in the order Windows would deliver it.</summary>
    public event Action<HostEventKind>? EventRaised;

    public GPUConnector AddConnector(LinkProfile link)
    {
        lock (_gate)
        {
            GPUConnector connector = new(_connectors.Count, link, connectorUID: 4352 + 4097 * _connectors.Count);
            _connectors.Add(connector);
            _targets[connector] = new TargetState();
            return connector;
        }
    }

    /// <summary>Cables a monitor before <see cref="Start"/>; afterwards use <see cref="Plug"/>.</summary>
    public void Cable(GPUConnector connector, SimulatedMonitor monitor)
    {
        lock (_gate) connector.Monitor = monitor;
    }

    /// <summary>Brings up the boot topology silently: every cabled monitor whose panel drives hot-plug detect is active.</summary>
    public void Start()
    {
        lock (_gate)
        {
            foreach (GPUConnector connector in _connectors)
            {
                if (!DesiredHPDLocked(connector)) continue;

                TargetState target = _targets[connector];
                target.HPD = true;
                target.TargetAvailable = true;
                AttachNodeLocked(connector, connector.Monitor!, defaultMonitor: false);
                ActivatePathLocked(connector);
            }

            _highestGDINumber = Math.Max(_highestGDINumber, _connectors.Count);
        }

        _trace.Add("host", "boot topology: " + DescribeTopology());
    }

    /// <summary>Reacts to a panel power transition; hot-plug detect follows only when the link drops it.</summary>
    public void OnMonitorPowerChanged(SimulatedMonitor monitor)
    {
        GPUConnector? connector;
        bool desired;
        long version;
        int delayMs;
        lock (_gate)
        {
            connector = _connectors.FirstOrDefault(candidate => ReferenceEquals(candidate.Monitor, monitor));
            if (connector == null) return;

            TargetState target = _targets[connector];
            desired = DesiredHPDLocked(connector);
            if (desired == target.HPD && target.PendingHPD == null) return;
            if (target.PendingHPD == desired) return;

            target.PendingHPD = desired;
            version = ++target.HPDVersion;
            LinkProfile link = connector.Link;
            delayMs = desired
                ? _random.Next(link.HPDRiseMinimumMs, link.HPDRiseMaximumMs + 1)
                : _random.Next(link.HPDDropMinimumMs, link.HPDDropMaximumMs + 1);
        }

        GPUConnector changed = connector;
        _clock.Schedule(TimeSpan.FromMilliseconds(delayMs), () => ApplyHPD(changed, desired, version));
    }

    /// <summary>Pulls the cable: hot-plug detect drops immediately.</summary>
    public SimulatedMonitor? Unplug(GPUConnector connector)
    {
        SimulatedMonitor? monitor;
        long version;
        lock (_gate)
        {
            monitor = connector.Monitor;
            connector.Monitor = null;
            TargetState target = _targets[connector];
            target.PendingHPD = false;
            version = ++target.HPDVersion;
        }

        _trace.Add("host", $"cable pulled from connector {connector.Index} ({monitor?.PhysicalID ?? "empty"})");
        ApplyHPD(connector, asserted: false, version);
        return monitor;
    }

    public void Plug(GPUConnector connector, SimulatedMonitor monitor)
    {
        lock (_gate) connector.Monitor = monitor;
        _trace.Add("host", $"cable plugged: {monitor.PhysicalID} -> connector {connector.Index}");
        OnMonitorPowerChanged(monitor);
    }

    /// <summary>System suspend: panels lose signal at once, and Windows defers every hot-plug reaction until resume.</summary>
    public void EnterSystemSleep()
    {
        List<SimulatedMonitor> monitors;
        lock (_gate)
        {
            _systemAsleep = true;
            monitors = [.. _connectors.Select(connector => connector.Monitor).OfType<SimulatedMonitor>()];
        }

        _trace.Add("host", "system suspend");
        foreach (SimulatedMonitor monitor in monitors) monitor.EnterSleepStandby();
    }

    public void ResumeFromSleep()
    {
        List<(GPUConnector Connector, bool Asserted)> pending = [];
        List<SimulatedMonitor> monitors;
        bool reactivateAll;
        lock (_gate)
        {
            _systemAsleep = false;
            foreach ((GPUConnector connector, TargetState target) in _targets)
            {
                if (!target.ReactionDeferred) continue;

                target.ReactionDeferred = false;
                if (target.HPD != target.PathActive || target.HPD != target.TargetAvailable)
                    pending.Add((connector, target.HPD));
            }

            monitors = [.. _connectors.Select(connector => connector.Monitor).OfType<SimulatedMonitor>()];
            reactivateAll = _random.NextDouble() < Policy.ResumeReactivatesAllProbability;
        }

        _trace.Add("host", "system resume");
        ScheduleReaction(100, () => Raise(HostEventKind.PowerResume));
        foreach ((GPUConnector connector, bool asserted) in pending.OrderBy(change => change.Asserted))
        {
            if (asserted) BeginArrival(connector);
            else BeginRemoval(connector);
        }

        int signalMs;
        lock (_gate) signalMs = _random.Next(minValue: 300, maxValue: 801);
        _clock.Schedule(TimeSpan.FromMilliseconds(signalMs), () =>
        {
            foreach (SimulatedMonitor monitor in monitors) monitor.SetVideoSignal(true);
        });

        if (reactivateAll) ScheduleReaction(1_000, ReactivateAllPaths);
    }

    /// <summary>Console display timeout without suspend: panels drop to standby while btadn keeps running.</summary>
    public void SetDisplaysPowered(bool powered)
    {
        List<SimulatedMonitor> monitors;
        lock (_gate) monitors = [.. _connectors.Select(connector => connector.Monitor).OfType<SimulatedMonitor>()];

        _trace.Add("host", powered ? "console display on" : "console display off");
        foreach (SimulatedMonitor monitor in monitors) monitor.SetVideoSignal(powered);
    }

    public void RaiseSessionUnlock() => Raise(HostEventKind.SessionUnlock);

    /// <summary>A mode change: two WM_DISPLAYCHANGE notifications 0.6 to 0.77 s apart, as measured.</summary>
    public void ChangeDisplayMode()
    {
        int bounceMs;
        lock (_gate) bounceMs = _random.Next(minValue: 595, maxValue: 766);
        ScheduleReaction(70, () => Raise(HostEventKind.DisplaySettingsChanged));
        ScheduleReaction(70 + bounceMs, () => Raise(HostEventKind.DisplaySettingsChanged));
    }

    /// <summary>
    /// Driver transient: every path except the primary deactivates for <paramref name="durationMs"/>, attached
    /// monitors stay listed as inactive, and WM_DISPLAYCHANGE fires four times over the following 16 s.
    /// </summary>
    public void BeginTransient(int durationMs)
    {
        List<GPUConnector> suspended = [];
        lock (_gate)
        {
            GPUConnector? primary = _connectors.FirstOrDefault(connector => _targets[connector].PathActive);
            foreach (GPUConnector connector in _connectors)
            {
                TargetState target = _targets[connector];
                if (!target.PathActive || ReferenceEquals(connector, primary)) continue;

                DeactivatePathLocked(connector);
                target.SuspendedByTransient = true;
                suspended.Add(connector);
            }

            _transientUntilTicks = _clock.ElapsedTicks + TimeSpan.FromMilliseconds(durationMs).Ticks;
        }

        if (suspended.Count == 0) return;

        _trace.Add("host", $"driver transient: {suspended.Count} path(s) inactive for {durationMs} ms");
        Raise(HostEventKind.DisplaySettingsChanged);
        _clock.Schedule(TimeSpan.FromMilliseconds(durationMs), () =>
        {
            lock (_gate)
            {
                // The driver brings paths back in no fixed order, so first-free sources can swap
                foreach (GPUConnector connector in ShuffledLocked(suspended))
                {
                    TargetState target = _targets[connector];
                    if (!target.SuspendedByTransient) continue;

                    target.SuspendedByTransient = false;
                    if (target.HPD && !target.PathActive) ActivatePathLocked(connector);
                }
            }

            _trace.Add("host", "driver transient ended: " + DescribeTopology());
            Raise(HostEventKind.DisplaySettingsChanged);
        });
        ScheduleReaction(durationMs + 4_000, () => Raise(HostEventKind.DisplaySettingsChanged));
        ScheduleReaction(durationMs + 9_000, () => Raise(HostEventKind.DisplaySettingsChanged));
    }

    // Windows API views, each a snapshot at the caller's virtual time

    /// <summary>EnumDisplayMonitors: active paths in source order, with rects relative to the primary at (0,0).</summary>
    public List<ActiveMonitorView> EnumDisplayMonitors()
    {
        lock (_gate)
        {
            List<GPUConnector> active =
            [
                .. _connectors
                    .Where(connector => _targets[connector].PathActive)
                    .OrderBy(connector => _targets[connector].SourceIndex)
                    .ThenBy(connector => connector.Index)
            ];
            int primaryIndex = active.Count == 0 ? 0 : active.Min(connector => connector.Index);

            List<ActiveMonitorView> views = [];
            foreach (GPUConnector connector in active)
            {
                TargetState target = _targets[connector];
                views.Add(new ActiveMonitorView(
                    target.MonitorHandle,
                    GDIName(target),
                    (connector.Index - primaryIndex) * DisplayWidth,
                    Y: 0,
                    connector));
            }

            return views;
        }
    }

    public bool IsHandleValid(IntPtr handle)
    {
        lock (_gate) return _targets.Values.Any(target => target.PathActive && target.MonitorHandle == handle);
    }

    /// <summary>Connector behind an HMONITOR that is still active, as GetPhysicalMonitorsFromHMONITOR resolves it.</summary>
    public GPUConnector? ResolveMonitorHandle(IntPtr handle)
    {
        lock (_gate)
        {
            foreach ((GPUConnector connector, TargetState target) in _targets)
            {
                if (target.PathActive && target.MonitorHandle == handle) return connector;
            }

            return null;
        }
    }

    /// <summary>
    /// EnumDisplayDevices(source, n) for every n. Normally just the active monitor. Attached-but-inactive monitors
    /// (between target availability and path activation, or during a transient) are listed under every source, and
    /// sometimes ahead of the active one.
    /// </summary>
    public List<DisplayDeviceView> EnumDisplayDevices(string gdiName)
    {
        lock (_gate)
        {
            List<DisplayDeviceView> devices = [];
            TargetState? owner = _targets.Values.FirstOrDefault(target =>
                target.PathActive && string.Equals(GDIName(target), gdiName, StringComparison.Ordinal));
            if (owner == null) return devices;

            if (owner.Node != null && _clock.ElapsedTicks >= owner.DeviceListedFromTicks)
                devices.Add(new DisplayDeviceView(owner.Node.DeviceID, owner.Node.InterfacePath, IsActive: true, owner.Node));

            foreach (TargetState target in _targets.Values)
            {
                if (ReferenceEquals(target, owner) || target.PathActive || !target.TargetAvailable || target.Node == null)
                    continue;

                DisplayDeviceView inactive = new(target.Node.DeviceID, target.Node.InterfacePath, IsActive: false, target.Node);
                if (_random.NextDouble() < Policy.InactiveListedFirstProbability) devices.Insert(index: 0, inactive);
                else devices.Add(inactive);
            }

            return devices;
        }
    }

    /// <summary>The EDID value under a devnode's Device Parameters key; it outlives the device being present.</summary>
    public byte[]? ReadEDIDRegistry(string displayInstancePath)
    {
        lock (_gate)
        {
            foreach (DeviceNode node in _nodes.Values)
            {
                if (!string.Equals(node.InstancePath, displayInstancePath, StringComparison.OrdinalIgnoreCase)) continue;

                return _clock.ElapsedTicks >= node.EDIDVisibleAtTicks ? node.EDID : null;
            }

            return null;
        }
    }

    /// <summary>CCD source id plus one, the number Windows Settings shows, keyed by GDI name.</summary>
    public Dictionary<string, int> BuildCCDMap()
    {
        lock (_gate)
        {
            Dictionary<string, int> map = new(StringComparer.Ordinal);
            if (_random.NextDouble() < Policy.CCDFailureProbability) return map;

            List<GPUConnector> active = [.. _connectors.Where(connector => _targets[connector].PathActive)];
            for (int rank = 0; rank < active.Count; rank++)
            {
                GPUConnector connector = active[rank];
                TargetState target = _targets[connector];
                map[GDIName(target)] = Policy.DisplayNumbering switch
                {
                    DisplayNumbering.ConnectorBound => connector.Index + 1,
                    DisplayNumbering.Compact => rank + 1,
                    _ => target.SourceIndex + 1
                };
            }

            return map;
        }
    }

    /// <summary>SetupDiGetClassDevs(monitor class, DIGCF_PRESENT) instance IDs.</summary>
    public List<string> PresentMonitorInstanceIDs()
    {
        lock (_gate) return [.. _nodes.Values.Where(node => node.Present).Select(node => node.InstancePath)];
    }

    // Ground truth for the oracle

    public bool IsMonitorActive(SimulatedMonitor monitor)
    {
        lock (_gate)
        {
            return _connectors.Any(connector =>
                ReferenceEquals(connector.Monitor, monitor) && _targets[connector].PathActive);
        }
    }

    /// <summary>Virtual time the monitor's current path activated, or -1 when it is not active.</summary>
    public long ActiveSinceTicks(SimulatedMonitor monitor)
    {
        lock (_gate)
        {
            foreach (GPUConnector connector in _connectors)
            {
                if (!ReferenceEquals(connector.Monitor, monitor)) continue;

                TargetState target = _targets[connector];
                return target.PathActive ? target.ActivatedAtTicks : -1;
            }

            return -1;
        }
    }

    public GPUConnector? ConnectorOf(SimulatedMonitor monitor)
    {
        lock (_gate) return _connectors.FirstOrDefault(connector => ReferenceEquals(connector.Monitor, monitor));
    }

    /// <summary>Every devnode that ever held <paramref name="monitor"/>, newest first.</summary>
    public List<DeviceNode> NodesOf(SimulatedMonitor monitor)
    {
        lock (_gate) return [.. _nodes.Values.Where(node => ReferenceEquals(node.Monitor, monitor)).Reverse()];
    }

    public bool IsTransientActive
    {
        get
        {
            lock (_gate) return _clock.ElapsedTicks < _transientUntilTicks;
        }
    }

    public string DescribeTopology()
    {
        lock (_gate)
        {
            StringBuilder builder = new();
            foreach (GPUConnector connector in _connectors)
            {
                TargetState target = _targets[connector];
                if (builder.Length > 0) builder.Append(" | ");
                builder.Append("c").Append(connector.Index).Append('=').Append(connector.Monitor?.PhysicalID ?? "-");
                if (!target.PathActive)
                {
                    builder.Append(target.HPD ? " (attached)" : " (off)");
                    continue;
                }

                builder.Append(' ').Append(GDIName(target))
                    .Append(" src").Append(target.SourceIndex)
                    .Append(" h0x").Append(target.MonitorHandle.ToString("X", CultureInfo.InvariantCulture));
                if (target.Node != null) builder.Append(' ').Append(target.Node.HardwareID).Append('#').Append(target.Node.Index);
            }

            return builder.ToString();
        }
    }

    private void ApplyHPD(GPUConnector connector, bool asserted, long version)
    {
        lock (_gate)
        {
            TargetState target = _targets[connector];
            if (version != target.HPDVersion) return;

            target.PendingHPD = null;
            if (target.HPD == asserted) return;

            target.HPD = asserted;
            if (_systemAsleep)
            {
                target.ReactionDeferred = true;
                return;
            }
        }

        if (asserted) BeginArrival(connector);
        else BeginRemoval(connector);
    }

    private void BeginArrival(GPUConnector connector)
    {
        bool defaultFirst;
        bool transient;
        lock (_gate)
        {
            SimulatedMonitor? monitor = connector.Monitor;
            if (monitor == null) return;

            defaultFirst = _random.NextDouble() < Policy.DefaultMonitorProbability;
            transient = _random.NextDouble() < Policy.ArrivalTransientProbability;
            AttachNodeLocked(connector, monitor, defaultFirst);
        }

        _trace.Add("host", $"hot-plug arrival on connector {connector.Index}{(defaultFirst ? " as Default_Monitor" : string.Empty)}");
        ScheduleReaction(Policy.ArrivalTargetAvailableMs, () =>
        {
            lock (_gate)
            {
                TargetState target = _targets[connector];
                if (target.HPD) target.TargetAvailable = true;
            }
        });
        ScheduleReaction(Policy.ArrivalPathActivateMs, () =>
        {
            bool activated = false;
            lock (_gate)
            {
                TargetState target = _targets[connector];
                if (target.HPD && !target.PathActive && !target.SuspendedByTransient)
                {
                    target.TargetAvailable = true;
                    ActivatePathLocked(connector);
                    activated = true;
                }
            }

            if (activated) _trace.Add("host", "path activated: " + DescribeTopology());
        });
        ScheduleReaction(Policy.ArrivalInterfaceMs, () => Raise(HostEventKind.MonitorInterfaceArrival));
        ScheduleReaction(Policy.ArrivalDisplayChangeMs, () => Raise(HostEventKind.DisplaySettingsChanged));
        if (transient) ScheduleReaction(Policy.TransientDelayMs, () => BeginTransient(Policy.TransientDurationMs));
        if (defaultFirst) ScheduleReaction(Policy.DefaultMonitorDurationMs, () => ReplaceDefaultMonitorNode(connector));
    }

    private void BeginRemoval(GPUConnector connector)
    {
        lock (_gate)
        {
            TargetState target = _targets[connector];
            target.Node?.Present = false;
        }

        _trace.Add("host", $"hot-plug removal on connector {connector.Index}");
        Raise(HostEventKind.MonitorInterfaceRemoval);
        ScheduleReaction(Policy.RemovalTargetUnavailableMs, () =>
        {
            lock (_gate)
            {
                TargetState target = _targets[connector];
                if (!target.HPD) target.TargetAvailable = false;
            }
        });
        ScheduleReaction(Policy.RemovalPathDropMs, () =>
        {
            bool dropped = false;
            lock (_gate)
            {
                TargetState target = _targets[connector];
                if (!target.HPD && target.PathActive)
                {
                    DeactivatePathLocked(connector);
                    dropped = true;
                }

                target.SuspendedByTransient = false;
            }

            if (dropped) _trace.Add("host", "path dropped: " + DescribeTopology());
        });
        ScheduleReaction(Policy.RemovalDisplayChangeMs, () => Raise(HostEventKind.DisplaySettingsChanged));
    }

    private void ReplaceDefaultMonitorNode(GPUConnector connector)
    {
        lock (_gate)
        {
            TargetState target = _targets[connector];
            if (!target.HPD || target.Node is not { IsDefaultMonitor: true } placeholder || connector.Monitor == null) return;

            placeholder.Present = false;
            AttachNodeLocked(connector, connector.Monitor, defaultMonitor: false);
            target.DeviceListedFromTicks = _clock.ElapsedTicks;
        }

        _trace.Add("host", $"connector {connector.Index} re-enumerated with its EDID: " + DescribeTopology());
        Raise(HostEventKind.MonitorInterfaceRemoval);
        ScheduleReaction(20, () => Raise(HostEventKind.MonitorInterfaceArrival));
        ScheduleReaction(90, () => Raise(HostEventKind.DisplaySettingsChanged));
    }

    private void ReactivateAllPaths()
    {
        lock (_gate)
        {
            List<GPUConnector> active = [.. _connectors.Where(connector => _targets[connector].PathActive)];
            foreach (GPUConnector connector in active) DeactivatePathLocked(connector);
            foreach (GPUConnector connector in ShuffledLocked(active)) ActivatePathLocked(connector);
        }

        _trace.Add("host", "resume re-activated every path: " + DescribeTopology());
        Raise(HostEventKind.DisplaySettingsChanged);
        ScheduleReaction(700, () => Raise(HostEventKind.DisplaySettingsChanged));
    }

    private static bool DesiredHPDLocked(GPUConnector connector)
    {
        SimulatedMonitor? monitor = connector.Monitor;
        if (monitor == null) return false;

        return monitor.PowerState switch
        {
            PanelPowerState.On => true,
            PanelPowerState.Standby => !connector.Link.DropsHPDInStandby,
            _ => !connector.Link.DropsHPDWhenPanelOff
        };
    }

    private void AttachNodeLocked(GPUConnector connector, SimulatedMonitor monitor, bool defaultMonitor)
    {
        string hardwareID = defaultMonitor ? DefaultMonitorHardwareID : monitor.Profile.HardwareID;
        (string, int) key = (hardwareID, connector.ConnectorUID);
        if (!_nodes.TryGetValue(key, out DeviceNode? node))
        {
            int index = _nodeCountByHardwareID.GetValueOrDefault(hardwareID) + 1;
            _nodeCountByHardwareID[hardwareID] = index;
            int lagMs = Policy.EDIDRegistryLagMaximumMs > 0 ? _random.Next(Policy.EDIDRegistryLagMaximumMs + 1) : 0;
            node = new DeviceNode
            {
                HardwareID = hardwareID,
                Index = index,
                ConnectorUID = connector.ConnectorUID,
                InstancePrefix = Policy.InstancePrefix,
                EDIDVisibleAtTicks = _clock.ElapsedTicks + TimeSpan.FromMilliseconds(lagMs).Ticks
            };
            _nodes[key] = node;
        }

        node.Monitor = monitor;
        node.EDID = defaultMonitor ? null : monitor.EDID;
        node.Present = true;
        _targets[connector].Node = node;
    }

    private void ActivatePathLocked(GPUConnector connector)
    {
        TargetState target = _targets[connector];
        target.PathActive = true;
        target.ActivatedAtTicks = _clock.ElapsedTicks;
        target.SourceIndex = Policy.SourceAllocation == SourceAllocation.ConnectorBound
            ? connector.Index
            : LowestFreeSourceLocked();
        target.GDINumber = Policy.GDINaming == GDINaming.Climbing ? ++_highestGDINumber : target.SourceIndex + 1;
        target.MonitorHandle = new IntPtr(_nextMonitorHandle);
        _nextMonitorHandle += HandleStride;
        int attachLagMs = Policy.DeviceAttachLagMaximumMs > 0 ? _random.Next(Policy.DeviceAttachLagMaximumMs + 1) : 0;
        target.DeviceListedFromTicks = _clock.ElapsedTicks + TimeSpan.FromMilliseconds(attachLagMs).Ticks;
    }

    private void DeactivatePathLocked(GPUConnector connector)
    {
        TargetState target = _targets[connector];
        target.PathActive = false;
        target.SourceIndex = -1;
        target.GDINumber = 0;
        target.MonitorHandle = IntPtr.Zero;
    }

    private List<GPUConnector> ShuffledLocked(IReadOnlyList<GPUConnector> connectors)
    {
        List<GPUConnector> shuffled = [.. connectors];
        for (int index = shuffled.Count - 1; index > 0; index--)
        {
            int swapIndex = _random.Next(index + 1);
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        return shuffled;
    }

    private int LowestFreeSourceLocked()
    {
        HashSet<int> used = [.. _targets.Values.Where(target => target.PathActive).Select(target => target.SourceIndex)];
        int source = 0;
        while (used.Contains(source)) source++;
        return source;
    }

    private static string GDIName(TargetState target) =>
        @"\\.\DISPLAY" + target.GDINumber.ToString(CultureInfo.InvariantCulture);

    private void ScheduleReaction(int baseMs, Action action)
    {
        int jitterMs;
        lock (_gate) jitterMs = Policy.ReactionJitterMs > 0 ? _random.Next(-Policy.ReactionJitterMs, Policy.ReactionJitterMs + 1) : 0;
        _clock.Schedule(TimeSpan.FromMilliseconds(Math.Max(val1: 0, baseMs + jitterMs)), action);
    }

    private void Raise(HostEventKind kind)
    {
        _trace.Add("host", "event " + kind);
        EventRaised?.Invoke(kind);
    }

    private sealed class TargetState
    {
        public bool HPD;
        public bool? PendingHPD;
        public long HPDVersion;
        public bool ReactionDeferred;
        public bool TargetAvailable;
        public bool PathActive;
        public bool SuspendedByTransient;
        public long ActivatedAtTicks;
        public int SourceIndex = -1;
        public int GDINumber;
        public IntPtr MonitorHandle;
        public DeviceNode? Node;
        public long DeviceListedFromTicks;
    }
}
