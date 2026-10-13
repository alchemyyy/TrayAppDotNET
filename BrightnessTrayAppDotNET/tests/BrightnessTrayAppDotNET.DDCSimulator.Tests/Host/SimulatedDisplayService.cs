using System.Globalization;
using System.Runtime.CompilerServices;
using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.DDCCI.Parser;
using BrightnessTrayAppDotNET.DDCCI.Parser.Nodes;
using BrightnessTrayAppDotNET.DDCCI.Tokenizer;
using BrightnessTrayAppDotNET.DDCCI.Tokenizer.Tokens;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;
using BrightnessTrayAppDotNET.Interop.DDCCI;
using TrayAppDotNETCommon;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Host;

/// <summary>Virtual cost of the work wrapped around each DDC call.</summary>
internal sealed class DisplayServiceTiming
{
    public int EnumerationBaseMicroseconds = 1_500;

    // GetMonitorInfo, two EnumDisplayDevices calls and the registry EDID read per monitor
    public int EnumerationPerMonitorMicroseconds = 900;
    public int CCDQueryMicroseconds = 600;

    // DisplayService.AttachWindowsBrightnessTargets queries root\wmi on every full enumeration; on a desktop it fails
    // with 0x8004100C after a WMI round trip
    public int WindowsBrightnessQueryMinimumMs = 15;
    public int WindowsBrightnessQueryMaximumMs = 45;

    // NativeAOT self-launch with --ddc-helper-server plus the named pipe connect
    public int HelperSpawnMinimumMs = 45;
    public int HelperSpawnMaximumMs = 140;
    public double HelperSpawnFailureProbability;
    public int HelperPipeRoundTripMicroseconds = 120;

    // DisplayService.TryGetDDCMonitors inside the helper, which repeats for every command
    public int HelperEnumerationMicroseconds = 2_500;
}

internal enum HelperCommandKind
{
    GetVCP,
    SetVCP,
    Capabilities
}

/// <summary>One helper command as the oracle sees it: which panel the app meant and which one the helper reached.</summary>
internal readonly record struct HelperRoutingEvent(
    long Ticks,
    HelperCommandKind Kind,
    byte Code,
    uint Value,
    SimulatedMonitor? Intended,
    SimulatedMonitor? Resolved,
    string ParentName,
    string ResolvedName,
    string MatchRule);

/// <summary>
/// <see cref="IDisplayService"/> backed by the simulated host. Enumeration mirrors DisplayService.TryGetMonitorsCore
/// rule for rule (the active monitor device for both DeviceID and interface path, adapter-name fallback, CCD numbers,
/// registry EDID through the real <c>EDIDParser</c>, profile lookup) and retires the helpers of vanished identities.
/// DDC calls go through per-identity helper clients that mirror DDCHelperClient: a serial command slot, lazy spawn
/// with a pipe-connect budget, an end-to-end timeout that kills the helper, and a helper that re-enumerates and
/// resolves the monitor with the real <see cref="DDCHelperServer.FindMatchingMonitor"/> on every command.
/// </summary>
internal sealed class SimulatedDisplayService : IDisplayService, IDisposable
{
    private const int HelperConnectTimeoutMs = 5_000;

    private readonly SimulationClock _clock;
    private readonly SimulationTrace _trace;
    private readonly HostDisplayStack _host;
    private readonly GPUDDCController _controller;
    private readonly DisplayServiceTiming _timing;
    private readonly IReadOnlyList<SimulatedMonitor> _allMonitors;
    private readonly Lock _gate = new();
    private readonly Random _random;
    private readonly Dictionary<string, SimulatedHelperClient> _helpers = new(StringComparer.Ordinal);

    // Mirrors DisplayService._retiredHelperClientKeys, guarded by _gate
    private readonly HashSet<string> _retiredHelperKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConditionalWeakTable<DDCMonitor, SimulatedMonitor> _origins = new();
    private bool _disposed;
    private int _helperSpawns;
    private int _helperKills;
    private int _brightnessReads;
    private int _peakLiveHelpers;

    public SimulatedDisplayService(
        SimulationClock clock,
        SimulationTrace trace,
        HostDisplayStack host,
        GPUDDCController controller,
        DisplayServiceTiming timing,
        IReadOnlyList<SimulatedMonitor> allMonitors,
        int seed)
    {
        _clock = clock;
        _trace = trace;
        _host = host;
        _controller = controller;
        _timing = timing;
        _allMonitors = allMonitors;
        _random = new Random(seed);
    }

    /// <summary>Raised on a pool thread whenever a helper resolves the monitor for a command.</summary>
    public event Action<HelperRoutingEvent>? RoutingObserved;

    /// <summary>Raised for every SetVCPFeature that reaches a panel's connector.</summary>
    public event Action<SimulatedMonitor, byte, uint>? WriteObserved;

    // Mirrors DisplayService with the helper enabled: zero or negative means the default, positive values have a floor
    public int OperationTimeoutMs
    {
        get;
        set => field = value <= 0
            ? TimeConstants.DisplayServiceOperationTimeoutMs
            : Math.Max(TimeConstants.DDCOperationTimeoutSafetyFloorMs, value);
    } = TimeConstants.DisplayServiceOperationTimeoutMs;

    public int HelperSpawns => Volatile.Read(ref _helperSpawns);

    public int HelperKills => Volatile.Read(ref _helperKills);

    /// <summary>Brightness GETs the app has issued, each one a full DDC round trip on the shared bus.</summary>
    public int BrightnessReads => Volatile.Read(ref _brightnessReads);

    public int LiveHelpers
    {
        get
        {
            lock (_gate) return _helpers.Values.Count(helper => helper.ProcessAlive);
        }
    }

    public int PeakLiveHelpers
    {
        get
        {
            lock (_gate) return _peakLiveHelpers;
        }
    }

    public int HelperKeys
    {
        get
        {
            lock (_gate) return _helpers.Count;
        }
    }

    public void Dispose()
    {
        List<SimulatedHelperClient> helpers;
        lock (_gate)
        {
            _disposed = true;
            helpers = [.. _helpers.Values];
            _helpers.Clear();
        }

        foreach (SimulatedHelperClient helper in helpers) DisposeHelper(helper);
    }

    /// <summary>The physical monitor a DDCMonitor described when the simulator created or last refreshed it.</summary>
    public SimulatedMonitor? OriginOf(DDCMonitor monitor)
    {
        if (_origins.TryGetValue(monitor, out SimulatedMonitor? origin)) return origin;
        if (string.IsNullOrEmpty(monitor.EDIDSerial)) return null;

        List<SimulatedMonitor> bySerial = [.. _allMonitors.Where(candidate => candidate.EDIDSerial == monitor.EDIDSerial)];
        return bySerial.Count == 1 ? bySerial[0] : null;
    }

    public bool TryGetMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error)
    {
        if (!TryEnumerateForParent(EnumerationPurpose.Full, out monitors, out error)) return false;

        RetireHelpersOfVanishedMonitors(monitors);
        return true;
    }

    public bool TryGetDDCRecoveryMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error)
    {
        if (!TryEnumerateForParent(EnumerationPurpose.DDCRecovery, out monitors, out error)) return false;

        RetireHelpersOfVanishedMonitors(monitors);
        return true;
    }

    /// <summary>
    /// Mirrors DisplayService.RetireHelpersOfVanishedMonitors over the real
    /// <see cref="DisplayService.SelectStaleHelperClientKeys"/> and DDCHelperClient.Retire: helpers of identities that
    /// no longer enumerate leave the registry, a busy one ends when its command finishes, and the key stays retired
    /// until an enumeration shows it again.
    /// </summary>
    private void RetireHelpersOfVanishedMonitors(IReadOnlyList<DDCMonitor> enumerated)
    {
        List<SimulatedHelperClient> vanished = [];
        lock (_gate)
        {
            if (_disposed) return;

            if (_retiredHelperKeys.Count > 0)
                _retiredHelperKeys.IntersectWith(DisplayService.SelectStaleHelperClientKeys(_retiredHelperKeys, enumerated));

            foreach (string key in DisplayService.SelectStaleHelperClientKeys(_helpers.Keys, enumerated))
            {
                if (!_helpers.Remove(key, out SimulatedHelperClient? helper)) continue;

                vanished.Add(helper);
                _retiredHelperKeys.Add(key);
            }
        }

        foreach (SimulatedHelperClient helper in vanished)
        {
            TADNLog.Log($"DisplayService: retiring the helper of vanished monitor {helper.Key}");
            helper.RetireWhenIdle = true;
            RetireIfIdle(helper);
        }
    }

    private void RetireIfIdle(SimulatedHelperClient helper)
    {
        if (!helper.RetireWhenIdle || !helper.Slot.TryEnter()) return;

        try
        {
            if (!helper.IsDisposed) DisposeHelper(helper);
        }
        finally
        {
            helper.Slot.Exit();
        }
    }

    public bool TryGetVCPCapabilities(
        DDCMonitor monitor,
        out IReadOnlyList<VCPCapability> capabilities,
        out string? error,
        CancellationToken ct = default)
    {
        capabilities = [];
        DDCCallOutcome<HelperReply> outcome = RunHelperCommand(
            monitor,
            HelperCommandKind.Capabilities,
            code: 0,
            value: 0,
            $"GetCapabilities('{monitor.Name}')",
            ct);
        error = outcome.Error;
        if (!outcome.Success) return false;

        // Same walk as DisplayService.TryGetVCPCapabilities over the real tokenizer and parser
        IEnumerable<IToken> tokens = new CapabilitiesTokenizer().GetTokens(outcome.Value.Capabilities);
        INode root = new CapabilitiesParser().Parse(tokens);
        INode? vcpNode = (root.Nodes ?? [])
            .RecursiveSelect(node => node.Nodes ?? [])
            .FirstOrDefault(node => string.Equals(node.Value, b: "vcp", StringComparison.OrdinalIgnoreCase));
        if (vcpNode?.Nodes == null) return true;

        NodeFormatter formatter = new();
        List<VCPCapability> collected = [];
        foreach (INode capabilityNode in vcpNode.Nodes)
        {
            if (!byte.TryParse(capabilityNode.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte code))
                continue;

            string? formatted = formatter.FormatNode(capabilityNode);
            if (formatted == null) continue;
            if (!TryGetVCPFeature(monitor, code, out uint current, out uint maximum, out _, ct) || maximum == 0) continue;

            collected.Add(new VCPCapability
            {
                Name = $"{formatted} (0x{capabilityNode.Value})", OptCode = code, Value = current, MaxValue = maximum
            });
        }

        capabilities = collected;
        error = null;
        return true;
    }

    public bool TryGetVCPFeature(
        DDCMonitor monitor,
        byte code,
        out uint currentValue,
        out uint maxValue,
        out string? error,
        CancellationToken ct = default)
    {
        if (code == monitor.BrightnessCode) Interlocked.Increment(ref _brightnessReads);

        DDCCallOutcome<HelperReply> outcome = RunHelperCommand(
            monitor,
            HelperCommandKind.GetVCP,
            code,
            value: 0,
            $"TryGetVCPFeature('{monitor.Name}', 0x{code:X2})",
            ct);
        currentValue = outcome.Value.Current;
        maxValue = outcome.Value.Maximum;
        error = outcome.Error;
        return outcome.Success;
    }

    public bool TrySetVCPFeature(
        DDCMonitor monitor,
        byte code,
        uint value,
        out string? error,
        CancellationToken ct = default)
    {
        DDCCallOutcome<HelperReply> outcome = RunHelperCommand(
            monitor,
            HelperCommandKind.SetVCP,
            code,
            value,
            $"TrySetVCPFeature('{monitor.Name}', 0x{code:X2}={value})",
            ct);
        error = outcome.Error;
        return outcome.Success;
    }

    public void ResetDDCTransport(DDCMonitor monitor)
    {
        SimulatedHelperClient? helper;
        lock (_gate)
        {
            if (_disposed) return;

            _helpers.Remove(DisplayService.BuildHelperClientKey(monitor), out helper);
        }

        if (helper == null) return;

        TADNLog.Log($"DisplayService: resetting DDC helper transport for '{monitor.Name}'");
        DisposeHelper(helper);
    }

    /// <summary>Mirrors DisplayService.RetireDDCTransport: the helper leaves the registry and ends once idle.</summary>
    public void RetireDDCTransport(DDCMonitor monitor)
    {
        string key = DisplayService.BuildHelperClientKey(monitor);
        SimulatedHelperClient? helper;
        lock (_gate)
        {
            if (_disposed) return;

            if (!_helpers.Remove(key, out helper)) return;

            _retiredHelperKeys.Add(key);
        }

        TADNLog.Log($"DisplayService: retiring the helper of superseded identity {key}");
        helper.RetireWhenIdle = true;
        RetireIfIdle(helper);
    }

    /// <summary>
    /// Mirrors DisplayService.RefreshHandle: reads every enumerated monitor, lets the real
    /// <see cref="DisplayService.SelectRefreshHandleCandidate"/> choose, then rewrites the caller's DDCMonitor in
    /// place.
    /// </summary>
    public bool RefreshHandle(DDCMonitor monitor)
    {
        if (string.IsNullOrEmpty(monitor.Name)) return false;

        _clock.Sleep(TimeSpan.FromMicroseconds(_timing.CCDQueryMicroseconds));
        Dictionary<string, int> friendlyByAdapter = _host.BuildCCDMap();
        RefreshHandleIdentity target = RefreshHandleIdentity.From(monitor);
        _clock.Sleep(TimeSpan.FromMicroseconds(_timing.EnumerationBaseMicroseconds));
        List<(ActiveMonitorView View, ResolvedIdentity Resolved)> candidates = [];
        foreach (ActiveMonitorView view in _host.EnumDisplayMonitors())
        {
            _clock.Sleep(TimeSpan.FromMicroseconds(_timing.EnumerationPerMonitorMicroseconds));
            if (!_host.IsHandleValid(view.Handle)) continue;

            candidates.Add((view, ResolveIdentity(view.GDIName)));
        }

        int selected = DisplayService.SelectRefreshHandleCandidate(
            target,
            [
                .. candidates.Select(static candidate => new RefreshHandleIdentity(
                    candidate.View.GDIName,
                    candidate.Resolved.DeviceID,
                    candidate.Resolved.DisplayInstancePath,
                    candidate.Resolved.Serial,
                    candidate.Resolved.Manufacturer,
                    candidate.Resolved.Product,
                    MonitorBrightnessControlKind.DdcCi))
            ],
            out RefreshHandleMatch match);
        if (selected < 0) return false;

        (ActiveMonitorView chosen, ResolvedIdentity resolved) = candidates[selected];
        if (match == RefreshHandleMatch.AdapterNameFallback)
        {
            TADNLog.Log(
                $"DisplayService.RefreshHandle: using adapter-name fallback for '{monitor.Name}' "
                + $"(targetDevice='{target.DeviceID}', newDevice='{resolved.DeviceID}')");
        }

        monitor.Handle = chosen.Handle;
        monitor.HDC = IntPtr.Zero;
        monitor.Name = chosen.GDIName;
        monitor.DeviceID = resolved.DeviceID;
        monitor.DisplayInstancePath = resolved.DisplayInstancePath;
        monitor.DisplayNumber = CCD.ResolveFriendlyDisplayNumber(chosen.GDIName, friendlyByAdapter);
        monitor.X = chosen.X;
        monitor.Y = chosen.Y;
        monitor.EDIDSerial = resolved.Serial;
        monitor.FriendlyName = resolved.FriendlyName;
        monitor.EDIDManufacturerID = resolved.Manufacturer;
        monitor.EDIDProductCode = resolved.Product;
        monitor.BrightnessControlKind = MonitorBrightnessControlKind.DdcCi;
        monitor.WindowsBrightnessInstanceName = string.Empty;
        monitor.WindowsBrightnessMethodPath = string.Empty;
        DDCMonitorDatabase.ApplyProfile(monitor);
        if (chosen.Connector.Monitor != null) _origins.AddOrUpdate(monitor, chosen.Connector.Monitor);
        return true;
    }

    private bool TryEnumerateForParent(
        EnumerationPurpose purpose,
        out IReadOnlyList<DDCMonitor> monitors,
        out string? error)
    {
        _clock.Sleep(TimeSpan.FromMicroseconds(_timing.EnumerationBaseMicroseconds));
        List<ActiveMonitorView> active = _host.EnumDisplayMonitors();
        _clock.Sleep(TimeSpan.FromMicroseconds(_timing.CCDQueryMicroseconds));
        Dictionary<string, int> friendlyByAdapter = _host.BuildCCDMap();
        _clock.Sleep(TimeSpan.FromMicroseconds(_timing.EnumerationPerMonitorMicroseconds * Math.Max(val1: 1, active.Count)));
        List<DDCMonitor> list = BuildMonitorList(purpose, active, friendlyByAdapter);

        if (purpose == EnumerationPurpose.Full)
        {
            int wmiMs;
            lock (_gate) wmiMs = _random.Next(_timing.WindowsBrightnessQueryMinimumMs, _timing.WindowsBrightnessQueryMaximumMs + 1);
            _clock.Sleep(TimeSpan.FromMilliseconds(wmiMs));
        }

        monitors = list;
        error = null;
        return true;
    }

    /// <summary>Builds DDCMonitor rows exactly the way DisplayService.TryGetMonitorsCore fills them.</summary>
    private List<DDCMonitor> BuildMonitorList(
        EnumerationPurpose purpose,
        List<ActiveMonitorView> active,
        Dictionary<string, int> friendlyByAdapter)
    {
        bool helperResolutionOnly = purpose == EnumerationPurpose.HelperResolution;
        List<DDCMonitor> list = [];
        foreach (ActiveMonitorView view in active)
        {
            DDCMonitor monitor = new() { Handle = view.Handle, HDC = IntPtr.Zero, X = view.X, Y = view.Y };
            list.Add(monitor);

            // GetMonitorInfo fails for an HMONITOR that died after EnumDisplayMonitors; the row stays nameless
            if (!_host.IsHandleValid(view.Handle)) continue;

            ResolvedIdentity resolved = ResolveIdentity(view.GDIName);
            monitor.Name = view.GDIName;
            monitor.DeviceID = resolved.DeviceID;
            monitor.DisplayInstancePath = resolved.DisplayInstancePath;
            if (!helperResolutionOnly)
                monitor.DisplayNumber = CCD.ResolveFriendlyDisplayNumber(monitor.Name, friendlyByAdapter);

            if (resolved.HasEDID)
            {
                monitor.EDIDSerial = resolved.Serial;
                if (!helperResolutionOnly)
                {
                    monitor.FriendlyName = resolved.FriendlyName;
                    monitor.EDIDManufacturerID = resolved.Manufacturer;
                    monitor.EDIDProductCode = resolved.Product;
                    DDCMonitorDatabase.ApplyProfile(monitor);
                }
            }

            if (view.Connector.Monitor != null) _origins.AddOrUpdate(monitor, view.Connector.Monitor);
        }

        return list;
    }

    /// <summary>
    /// DisplayService.ResolveMonitorDevice lists the source's monitor devices once and reads both the DeviceID and the
    /// interface path of the device the real <see cref="DisplayService.SelectMonitorDeviceIndex"/> picks.
    /// </summary>
    private ResolvedIdentity ResolveIdentity(string gdiName)
    {
        List<DisplayDeviceView> devices = _host.EnumDisplayDevices(gdiName);
        int deviceIndex = DisplayService.SelectMonitorDeviceIndex(StateFlagsOf(devices));
        string deviceID = deviceIndex >= 0 && !string.IsNullOrEmpty(devices[deviceIndex].DeviceID)
            ? devices[deviceIndex].DeviceID
            : gdiName;
        string instancePath = deviceIndex >= 0
            ? DisplayService.ParseDisplayInstancePath(devices[deviceIndex].InterfacePath)
            : string.Empty;

        byte[]? edid = string.IsNullOrEmpty(instancePath) ? null : _host.ReadEDIDRegistry(instancePath);
        if (edid == null) return new ResolvedIdentity(deviceID, instancePath, HasEDID: false, string.Empty, string.Empty, string.Empty, string.Empty);

        ushort productCode = EDIDParser.ExtractProductCode(edid);
        return new ResolvedIdentity(
            deviceID,
            instancePath,
            HasEDID: true,
            EDIDParser.ExtractSerial(edid),
            EDIDParser.ExtractManufacturerID(edid),
            productCode == 0 ? string.Empty : productCode.ToString("X4", CultureInfo.InvariantCulture),
            EDIDParser.ExtractMonitorName(edid));
    }

    /// <summary>
    /// Mirrors DisplayService.RunWithTimeout: a command for a retired identity, or one a helper retired between lookup
    /// and command refused unsent, runs on a helper that is never registered and ends with the command.
    /// </summary>
    private DDCCallOutcome<HelperReply> RunHelperCommand(
        DDCMonitor monitor,
        HelperCommandKind kind,
        byte code,
        uint value,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        string key = DisplayService.BuildHelperClientKey(monitor);
        SimulatedHelperClient? helper = null;
        lock (_gate)
        {
            if (_disposed) return DDCCallOutcome<HelperReply>.WithError("Display service is disposed.");

            if (!_retiredHelperKeys.Contains(key) && !_helpers.TryGetValue(key, out helper))
            {
                helper = new SimulatedHelperClient(_clock, key);
                _helpers.Add(key, helper);
            }
        }

        if (helper != null)
        {
            DDCCallOutcome<HelperReply> outcome =
                RunHelperCommandOnce(helper, monitor, kind, code, value, operationLabel, cancellationToken);
            if (outcome.Success || outcome.Error != DDCHelperClient.RetiredBeforeSendError) return outcome;

            lock (_gate)
            {
                if (_disposed) return DDCCallOutcome<HelperReply>.WithError("Display service is disposed.");
            }
        }

        SimulatedHelperClient oneShot = new(_clock, key) { RetireWhenIdle = true };
        return RunHelperCommandOnce(oneShot, monitor, kind, code, value, operationLabel, cancellationToken);
    }

    /// <summary>Parent side of DDCHelperClient.SendCommand, run on the caller's thread.</summary>
    private DDCCallOutcome<HelperReply> RunHelperCommandOnce(
        SimulatedHelperClient helper,
        DDCMonitor monitor,
        HelperCommandKind kind,
        byte code,
        uint value,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return DDCCallOutcome<HelperReply>.WithError($"DDC op '{operationLabel}' cancelled by sequence deadline.");

        int timeoutMs = OperationTimeoutMs;
        long startTicks = _clock.ElapsedTicks;
        helper.Slot.Enter();
        try
        {
            if (helper.IsDisposed) return DDCCallOutcome<HelperReply>.Fail(DDCHelperClient.RetiredBeforeSendError);

            int remainingMs = RemainingMs(timeoutMs, startTicks);
            if (remainingMs == 0)
            {
                return DDCCallOutcome<HelperReply>.WithError(
                    $"DDC op '{operationLabel}' exceeded {timeoutMs}ms end-to-end timeout waiting for its helper slot.");
            }

            if (!helper.ProcessAlive && !TryStartHelper(helper, remainingMs))
                return DDCCallOutcome<HelperReply>.Fail("DDC helper did not connect to its command pipe.");

            remainingMs = RemainingMs(timeoutMs, startTicks);
            if (remainingMs == 0)
            {
                KillHelper(helper, "budget spent before the command was sent");
                return DDCCallOutcome<HelperReply>.WithError(
                    $"DDC op '{operationLabel}' exceeded {timeoutMs}ms end-to-end timeout; helper process killed.");
            }

            HelperExecution execution = new(_clock, kind, code, value, monitor, OriginOf(monitor), helper.ProcessGeneration);
            helper.InFlight = execution;
            StartExecution(helper, execution);
            HelperWaitOutcome outcome = execution.WaitForReply(TimeSpan.FromMilliseconds(remainingMs), cancellationToken);
            helper.InFlight = null;
            switch (outcome)
            {
                case HelperWaitOutcome.Replied:
                    HelperReply reply = execution.Reply;
                    return reply.Success
                        ? DDCCallOutcome<HelperReply>.Ok(reply)
                        : DDCCallOutcome<HelperReply>.Fail(reply.Error ?? "DDC helper failed.");

                case HelperWaitOutcome.TimedOut:
                    KillHelper(helper, $"{operationLabel} timed out");
                    return DDCCallOutcome<HelperReply>.WithError(
                        $"DDC op '{operationLabel}' exceeded {timeoutMs}ms timeout; helper process killed.");

                case HelperWaitOutcome.Cancelled:
                    KillHelper(helper, $"{operationLabel} cancelled");
                    return DDCCallOutcome<HelperReply>.WithError(
                        $"DDC op '{operationLabel}' cancelled by sequence deadline; helper process killed.");

                default:
                    return DDCCallOutcome<HelperReply>.Fail(DDCHelperClient.DisposedClientError);
            }
        }
        finally
        {
            helper.Slot.Exit();

            // DDCHelperClient.SendCommand completes a retirement requested while this command held the client
            RetireIfIdle(helper);
        }
    }

    private bool TryStartHelper(SimulatedHelperClient helper, int remainingMs)
    {
        int spawnMs;
        bool spawnFails;
        lock (_gate)
        {
            spawnMs = _random.Next(_timing.HelperSpawnMinimumMs, _timing.HelperSpawnMaximumMs + 1);
            spawnFails = _random.NextDouble() < _timing.HelperSpawnFailureProbability;
        }

        int connectTimeoutMs = Math.Min(HelperConnectTimeoutMs, remainingMs);
        if (spawnFails || spawnMs > connectTimeoutMs)
        {
            _clock.Sleep(TimeSpan.FromMilliseconds(connectTimeoutMs));
            _trace.Add("helper", $"{helper.Key} did not connect within {connectTimeoutMs} ms");
            return false;
        }

        _clock.Sleep(TimeSpan.FromMilliseconds(spawnMs));
        lock (_gate)
        {
            if (helper.IsDisposed) return false;

            helper.ProcessAlive = true;
            helper.ProcessGeneration++;
            _helperSpawns++;
            _peakLiveHelpers = Math.Max(_peakLiveHelpers, _helpers.Values.Count(candidate => candidate.ProcessAlive));
        }

        _trace.Add("helper", $"spawned helper #{helper.ProcessGeneration} for {helper.Key} ({spawnMs} ms)");
        return true;
    }

    /// <summary>
    /// The helper process side of one command, as a chain of timed steps: receive, re-enumerate, resolve with the
    /// real matcher, open physical monitor handles, run the DDC call, close the handles, reply. A killed helper stops
    /// issuing new steps, but a call already on the bus completes and holds the global DDC mutex until it does.
    /// </summary>
    private void StartExecution(SimulatedHelperClient helper, HelperExecution execution)
    {
        TimeSpan receive = TimeSpan.FromMicroseconds(_timing.HelperPipeRoundTripMicroseconds / 2.0);
        TimeSpan enumeration = TimeSpan.FromMicroseconds(_timing.HelperEnumerationMicroseconds);
        _clock.Schedule(receive + enumeration, () =>
        {
            if (!execution.IsCurrent(helper)) return;

            List<DDCMonitor> helperMonitors = BuildMonitorList(
                EnumerationPurpose.HelperResolution,
                _host.EnumDisplayMonitors(),
                []);
            DDCHelperServer.DDCHelperMonitorIdentity identity = DDCHelperServer.DDCHelperMonitorIdentity.From(execution.ParentMonitor);
            DDCMonitor? match = DDCHelperServer.FindMatchingMonitor(helperMonitors, identity);
            if (match == null)
            {
                Reply(helper, execution, HelperReply.Failure(DDCHelperServer.NoMatchingMonitorError));
                return;
            }

            GPUConnector? resolvedConnector = _host.ResolveMonitorHandle(match.Handle);
            RoutingObserved?.Invoke(new HelperRoutingEvent(
                _clock.ElapsedTicks,
                execution.Kind,
                execution.Code,
                execution.Value,
                execution.Intended,
                resolvedConnector?.Monitor,
                execution.ParentMonitor.Name,
                match.Name,
                DescribeMatchRule(match, identity)));

            // GetNumberOfPhysicalMonitorsFromHMONITOR and GetPhysicalMonitorsFromHMONITOR take the OPM mutex too
            SubmitHandleOperation("GetPhysicalMonitorsFromHMONITOR " + match.Name, () =>
            {
                if (!execution.IsCurrent(helper)) return;

                GPUConnector? connector = _host.ResolveMonitorHandle(match.Handle);
                if (connector == null)
                {
                    Reply(
                        helper,
                        execution,
                        HelperReply.Failure(
                            $"GetNumberOfPhysicalMonitorsFromHMONITOR failed for '{match.Name}' "
                            + $"(Win32: {Win32GraphicsErrors.InvalidHandle})"));
                    return;
                }

                RunDDCOperation(helper, execution, connector, match.Name);
            });
        });
    }

    private void RunDDCOperation(
        SimulatedHelperClient helper,
        HelperExecution execution,
        GPUConnector connector,
        string helperMonitorName)
    {
        DDCOperationKind operationKind = execution.Kind switch
        {
            HelperCommandKind.GetVCP => DDCOperationKind.GetVCP,
            HelperCommandKind.SetVCP => DDCOperationKind.SetVCP,
            _ => DDCOperationKind.Capabilities
        };

        if (execution.Kind == HelperCommandKind.SetVCP && connector.Monitor != null)
            WriteObserved?.Invoke(connector.Monitor, execution.Code, execution.Value);

        _controller.Submit(new DDCOperation
        {
            Kind = operationKind,
            Connector = connector,
            Code = execution.Code,
            Value = (ushort)Math.Min(execution.Value, ushort.MaxValue),
            Label = $"{execution.Kind} 0x{execution.Code:X2} via {helperMonitorName} (c{connector.Index})",
            Completed = result =>
            {
                // DestroyPhysicalMonitors runs whether or not the call succeeded, unless the helper is already dead
                if (!execution.IsCurrent(helper)) return;

                HelperReply reply = BuildReply(execution, result);
                SubmitHandleOperation("DestroyPhysicalMonitors " + helperMonitorName, () => Reply(helper, execution, reply));
            }
        });
    }

    private static HelperReply BuildReply(HelperExecution execution, DDCOperationResult result)
    {
        if (result.Succeeded)
        {
            return execution.Kind == HelperCommandKind.Capabilities
                ? new HelperReply(Success: true, Current: 0, Maximum: 0, result.Capabilities, Error: null)
                : new HelperReply(Success: true, result.Current, result.Maximum, Capabilities: string.Empty, Error: null);
        }

        string error = execution.Kind switch
        {
            HelperCommandKind.GetVCP => DDCNativeError.Format(operation: "GetVCPFeatureAndVCPFeatureReply", result.ErrorCode),
            HelperCommandKind.SetVCP => DDCNativeError.Format(operation: "SetVCPFeature", result.ErrorCode),
            _ => $"CapabilitiesRequestAndCapabilitiesReply failed (Win32: {result.ErrorCode.ToString(CultureInfo.InvariantCulture)})"
        };
        return HelperReply.Failure(error);
    }

    private void SubmitHandleOperation(string label, Action completed) =>
        _controller.Submit(new DDCOperation
        {
            Kind = DDCOperationKind.HandleOperation,
            Connector = null,
            Label = label,
            Completed = _ => completed()
        });

    private void Reply(SimulatedHelperClient helper, HelperExecution execution, HelperReply reply)
    {
        TimeSpan pipe = TimeSpan.FromMicroseconds(_timing.HelperPipeRoundTripMicroseconds / 2.0);
        _clock.Schedule(pipe, () =>
        {
            if (execution.IsCurrent(helper)) execution.Complete(reply);
        });
    }

    private void KillHelper(SimulatedHelperClient helper, string reason)
    {
        lock (_gate)
        {
            if (!helper.ProcessAlive) return;

            helper.ProcessAlive = false;
            _helperKills++;
        }

        _trace.Add("helper", $"killed helper #{helper.ProcessGeneration} for {helper.Key}: {reason}");
    }

    private void DisposeHelper(SimulatedHelperClient helper)
    {
        HelperExecution? inFlight;
        lock (_gate)
        {
            helper.IsDisposed = true;
            inFlight = helper.InFlight;
            if (helper.ProcessAlive)
            {
                helper.ProcessAlive = false;
                _helperKills++;
            }
        }

        // The pipe breaks under the in-flight command; DDCHelperClient's retry loop then sees the disposed flag
        inFlight?.Abandon();
    }

    private static List<int> StateFlagsOf(List<DisplayDeviceView> devices) =>
        [.. devices.Select(static device => device.IsActive ? DisplayService.DisplayDeviceActive : 0)];

    private int RemainingMs(int timeoutMs, long startTicks)
    {
        if (timeoutMs <= 0) return Timeout.Infinite;

        long elapsedMs = (long)Math.Ceiling(TimeSpan.FromTicks(_clock.ElapsedTicks - startTicks).TotalMilliseconds);
        return (int)Math.Max(val1: 0, timeoutMs - elapsedMs);
    }

    private static string DescribeMatchRule(DDCMonitor match, DDCHelperServer.DDCHelperMonitorIdentity identity)
    {
        if (!string.IsNullOrEmpty(identity.DeviceID)
            && !identity.DeviceID.StartsWith(value: @"\\.\", StringComparison.Ordinal)
            && string.Equals(match.DeviceID, identity.DeviceID, StringComparison.OrdinalIgnoreCase))
            return "DeviceID";
        if (!string.IsNullOrEmpty(identity.DisplayInstancePath)
            && string.Equals(match.DisplayInstancePath, identity.DisplayInstancePath, StringComparison.OrdinalIgnoreCase))
            return "DisplayInstancePath";
        if (!string.IsNullOrEmpty(identity.EDIDSerial) && string.Equals(match.EDIDSerial, identity.EDIDSerial, StringComparison.Ordinal))
            return "EDIDSerial";
        return "Name";
    }

    private enum EnumerationPurpose
    {
        Full,
        DDCRecovery,
        HelperResolution
    }

    private readonly record struct ResolvedIdentity(
        string DeviceID,
        string DisplayInstancePath,
        bool HasEDID,
        string Serial,
        string Manufacturer,
        string Product,
        string FriendlyName);

    private enum HelperWaitOutcome
    {
        Replied,
        TimedOut,
        Cancelled,
        Abandoned
    }

    private readonly record struct HelperReply(bool Success, uint Current, uint Maximum, string Capabilities, string? Error)
    {
        public static HelperReply Failure(string error) => new(Success: false, Current: 0, Maximum: 0, string.Empty, error);
    }

    private sealed class SimulatedHelperClient(SimulationClock clock, string key)
    {
        public string Key { get; } = key;
        public SimulationGate Slot { get; } = new(clock);

        // Guarded by the display service gate
        public bool IsDisposed;
        public bool ProcessAlive;
        public int ProcessGeneration;
        public HelperExecution? InFlight;

        public volatile bool RetireWhenIdle;
    }

    private sealed class HelperExecution(
        SimulationClock clock,
        HelperCommandKind kind,
        byte code,
        uint value,
        DDCMonitor parentMonitor,
        SimulatedMonitor? intended,
        int processGeneration)
    {
        private readonly Lock _gate = new();
        private readonly SimulationWaitHandle _released = new(clock);
        private HelperWaitOutcome? _outcome;
        private bool _abandoned;

        public HelperCommandKind Kind { get; } = kind;
        public byte Code { get; } = code;
        public uint Value { get; } = value;
        public DDCMonitor ParentMonitor { get; } = parentMonitor;
        public SimulatedMonitor? Intended { get; } = intended;
        public HelperReply Reply { get; private set; }

        /// <summary>True while the helper process that received this command is still alive and waiting.</summary>
        public bool IsCurrent(SimulatedHelperClient helper)
        {
            lock (_gate)
            {
                if (_abandoned || _outcome != null) return false;
            }

            return helper.ProcessAlive && helper.ProcessGeneration == processGeneration;
        }

        public void Complete(HelperReply reply)
        {
            lock (_gate)
            {
                if (_outcome != null) return;

                Reply = reply;
                _outcome = HelperWaitOutcome.Replied;
            }

            _released.Release();
        }

        public void Abandon()
        {
            lock (_gate)
            {
                _abandoned = true;
                if (_outcome != null) return;

                _outcome = HelperWaitOutcome.Abandoned;
            }

            _released.Release();
        }

        public HelperWaitOutcome WaitForReply(TimeSpan budget, CancellationToken cancellationToken)
        {
            using ITimer deadline = clock.Schedule(budget, () => Resolve(HelperWaitOutcome.TimedOut));
            using CancellationTokenRegistration registration =
                cancellationToken.Register(() => Resolve(HelperWaitOutcome.Cancelled));
            _released.Wait();
            lock (_gate)
            {
                _abandoned = true;
                return _outcome ?? HelperWaitOutcome.Abandoned;
            }
        }

        private void Resolve(HelperWaitOutcome outcome)
        {
            lock (_gate)
            {
                if (_outcome != null) return;

                _outcome = outcome;
            }

            _released.Release();
        }
    }
}
