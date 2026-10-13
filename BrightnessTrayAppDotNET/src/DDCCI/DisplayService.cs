using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using BrightnessTrayAppDotNET.DDCCI.Parser;
using BrightnessTrayAppDotNET.DDCCI.Parser.Nodes;
using BrightnessTrayAppDotNET.DDCCI.Tokenizer;
using BrightnessTrayAppDotNET.DDCCI.Tokenizer.Tokens;
using BrightnessTrayAppDotNET.Interop.DDCCI;
using BrightnessTrayAppDotNET.Interop.WindowsBrightness;
using Microsoft.Win32;

namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// Default <see cref="IDisplayService"/> implementation backed by the Windows Monitor Configuration API (dxva2.dll).
/// Each call opens the physical monitor handle for the requested HMONITOR, performs the I/O,
/// then releases via DestroyPhysicalMonitors per the MSDN usage pattern.
///
/// Try-pattern surface: public methods return <c>bool</c> and surface failure via <c>out string? error</c>.
/// Expected DDC failures (I2C transmit errors, monitor not responding, missing capabilities string)
/// aren't programming errors and flow through the same path as any other "bus said no" outcome.
/// The only throw is <see cref="ArgumentNullException"/> for a null monitor, which IS a programmer error.
/// </summary>
public class DisplayService : IDisplayService, IDisposable
{
    private enum MonitorEnumerationPurpose
    {
        Full,
        DDCRecovery,
        HelperResolution
    }

    // DISPLAY_DEVICE_ACTIVE on a monitor device: the GDI source currently presents it
    internal const int DisplayDeviceActive = 0x1;

    // EnumDisplayDevices lists one active monitor per source plus any attached but inactive ones
    private const uint MaximumMonitorDevicesPerSource = 16;

    private readonly bool _useHelperProcess;

    // One helper process per stable monitor identity. A blocked driver call can therefore time out and kill only
    // that monitor's helper instead of holding the single pipe lock in front of every other panel.
    private readonly Dictionary<string, DDCHelperClient>? _helperClients;
    private readonly Lock _helperClientsGate = new();

    // Keys whose helper was retired because the identity stopped enumerating or was superseded, until an enumeration
    // shows them again. Guarded by _helperClientsGate. A command still aimed at one gets a helper that ends with it;
    // a registered one would live until some later enumeration noticed it
    private readonly HashSet<string> _retiredHelperClientKeys = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    public DisplayService() : this(true) { }

    internal DisplayService(bool useHelperProcess)
    {
        _useHelperProcess = useHelperProcess;
        if (useHelperProcess)
            _helperClients = new Dictionary<string, DDCHelperClient>(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public int OperationTimeoutMs
    {
        get;
        set => field = _useHelperProcess
            ? value <= 0
                ? TimeConstants.DisplayServiceOperationTimeoutMs
                : Math.Max(TimeConstants.DDCOperationTimeoutSafetyFloorMs, value)
            : value;
    } = TimeConstants.DisplayServiceOperationTimeoutMs;

    public void Dispose()
    {
        List<DDCHelperClient> helperClients = [];
        lock (_helperClientsGate)
        {
            if (_disposed) return;

            _disposed = true;
            if (_helperClients == null) return;

            helperClients.AddRange(_helperClients.Values);
            _helperClients.Clear();
        }

        foreach (DDCHelperClient helperClient in helperClients)
            helperClient.Dispose();
    }

    public bool TryGetMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error)
    {
        if (!TryGetMonitorsCore(MonitorEnumerationPurpose.Full, out monitors, out error)) return false;

        RetireHelpersOfVanishedMonitors(monitors);
        return true;
    }

    /// <inheritdoc />
    public bool TryGetDDCRecoveryMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error)
    {
        if (!TryGetMonitorsCore(MonitorEnumerationPurpose.DDCRecovery, out monitors, out error)) return false;

        RetireHelpersOfVanishedMonitors(monitors);
        return true;
    }

    /// <summary>
    /// Retires helper processes whose monitor identity no longer enumerates. A replug or devnode change mints a new
    /// helper key, and the old helper process used to live until the app exited. A helper busy with a command ends
    /// when that command finishes; a later command for the same identity runs on a helper that ends with it, until an
    /// enumeration shows the identity again.
    /// </summary>
    private void RetireHelpersOfVanishedMonitors(IReadOnlyList<DDCMonitor> enumerated)
    {
        List<(string Key, DDCHelperClient Client)> vanished = [];
        lock (_helperClientsGate)
        {
            if (_disposed || _helperClients == null) return;

            if (_retiredHelperClientKeys.Count > 0)
                _retiredHelperClientKeys.IntersectWith(SelectStaleHelperClientKeys(_retiredHelperClientKeys, enumerated));

            foreach (string key in SelectStaleHelperClientKeys(_helperClients.Keys, enumerated))
            {
                if (!_helperClients.Remove(key, out DDCHelperClient? client)) continue;

                vanished.Add((key, client));
                _retiredHelperClientKeys.Add(key);
            }
        }

        foreach ((string key, DDCHelperClient client) in vanished)
        {
            TADNLog.Log($"DisplayService: retiring the helper of vanished monitor {key}");

            // Ending the process waits on its exit; keep that off the caller, which can be the UI thread
            _ = Task.Run(client.Retire);
        }
    }

    /// <summary>Helper client keys that no monitor in <paramref name="enumerated"/> maps to.</summary>
    internal static List<string> SelectStaleHelperClientKeys(
        IEnumerable<string> clientKeys,
        IReadOnlyList<DDCMonitor> enumerated)
    {
        HashSet<string> liveKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (DDCMonitor monitor in enumerated) liveKeys.Add(BuildHelperClientKey(monitor));

        return [.. clientKeys.Where(key => !liveKeys.Contains(key))];
    }

    /// <summary>
    /// Enumerates the Win32 identity needed by the DDC helper without parent-only WMI, CCD, and profile work.
    /// </summary>
    internal static bool TryGetDDCMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error) =>
        TryGetMonitorsCore(MonitorEnumerationPurpose.HelperResolution, out monitors, out error);

    private static bool TryGetMonitorsCore(
        MonitorEnumerationPurpose purpose,
        out IReadOnlyList<DDCMonitor> monitors,
        out string? error)
    {
        error = null;
        List<DDCMonitor> list = [];
        bool helperResolutionOnly = purpose == MonitorEnumerationPurpose.HelperResolution;

        if (!User32Monitor.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero))
        {
            error = $"EnumDisplayMonitors failed (Win32: {Marshal.GetLastWin32Error()})";
            monitors = [];
            return false;
        }

        // CCD source-id mapping matches what Windows Settings labels each display with.
        // The trailing-digit parse on \\.\DISPLAY{n} only matches Settings on a freshly-booted machine
        // - Windows bumps that index on every topology event, so after enough hot-plug churn it climbs into high 20s.
        // sourceInfo.id is bound to the GPU output port and stays stable across power-cycles.
        Dictionary<string, int> friendlyByAdapter = helperResolutionOnly
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : CCD.BuildFriendlyDisplayNumberMap();

        foreach (DDCMonitor monitor in list)
        {
            User32Monitor.MonitorInfoEx monitorInfo = new();
            if (User32Monitor.GetMonitorInfo(new HandleRef(wrapper: null, monitor.Handle), monitorInfo))
            {
                monitor.Name = new string(monitorInfo.szDevice).TrimEnd('\0');
                (monitor.DeviceID, monitor.DisplayInstancePath) = ResolveMonitorDevice(monitor.Name);
                if (!helperResolutionOnly)
                    monitor.DisplayNumber = CCD.ResolveFriendlyDisplayNumber(monitor.Name, friendlyByAdapter);

                byte[]? edid = ReadEDID(monitor.DisplayInstancePath);
                if (edid != null)
                {
                    monitor.EDIDSerial = EDIDParser.ExtractSerial(edid);
                    if (!helperResolutionOnly)
                    {
                        monitor.FriendlyName = EDIDParser.ExtractMonitorName(edid);
                        monitor.EDIDManufacturerID = EDIDParser.ExtractManufacturerID(edid);
                        ushort productCode = EDIDParser.ExtractProductCode(edid);
                        monitor.EDIDProductCode = productCode == 0
                            ? string.Empty
                            : productCode.ToString(format: "X4", CultureInfo.InvariantCulture);
                        // Populate per-monitor VCP profile fields (BrightnessCode, PowerOffCommands, ProfileQuirks)
                        // by EDID identity. Misses leave the VESA-standard defaults in place.
                        DDCMonitorDatabase.ApplyProfile(monitor);
                    }
                }
            }
        }

        if (purpose == MonitorEnumerationPurpose.Full)
            AttachWindowsBrightnessTargets(list);
        monitors = list;
        return true;

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref User32Monitor.Rect rect, IntPtr data)
        {
            list.Add(new DDCMonitor { Handle = hMonitor, HDC = hdc, X = rect.left, Y = rect.top });
            return true;
        }
    }

    /// <summary>
    /// Reads the EDID block for the given display instance path
    /// from <c>HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\...\Device Parameters</c>.
    /// The caller reuses the path already resolved for monitor identity so EDID lookup does not repeat
    /// <c>EnumDisplayDevices</c>.
    /// Returns null when path or key is missing; EDID is optional, not load-bearing.
    /// </summary>
    private static byte[]? ReadEDID(string displayInstancePath)
    {
        if (string.IsNullOrEmpty(displayInstancePath)) return null;
        string keyPath = $@"SYSTEM\CurrentControlSet\Enum\{displayInstancePath}\Device Parameters";

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
            return key?.GetValue("EDID") as byte[];
        }
        catch (Exception ex)
        {
            TADNLog.Log($"DisplayService.ReadEDID: failed for '{displayInstancePath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Stable DeviceID and <c>DISPLAY\...\...</c> instance path of the monitor attached to an adapter (e.g.
    /// <c>\\.\DISPLAY1</c>), both read from the device <see cref="SelectMonitorDeviceIndex"/> picks in one pass, so
    /// they describe the same panel even while flags change mid-transient. Windows also lists attached but inactive
    /// monitors under every source, sometimes first, so index 0 could describe another panel. The instance path has
    /// the shape WmiMonitorBrightness.InstanceName uses before its trailing target suffix. The DeviceID falls back to
    /// the adapter name so identity resolution never returns empty - callers can still key on it, they just lose the
    /// "same monitor on same port" invariant.
    /// </summary>
    private static (string DeviceID, string DisplayInstancePath) ResolveMonitorDevice(string adapterName)
    {
        if (string.IsNullOrEmpty(adapterName)) return (string.Empty, string.Empty);

        List<User32Monitor.DisplayDevice> devices = [];
        List<string> interfacePaths = [];
        for (uint index = 0; index < MaximumMonitorDevicesPerSource; index++)
        {
            User32Monitor.DisplayDevice device = new() { cb = Marshal.SizeOf<User32Monitor.DisplayDevice>() };
            if (!User32Monitor.EnumDisplayDevices(adapterName, index, ref device, dwFlags: 0)) break;

            // The interface-name form of the same index must still be the same device
            User32Monitor.DisplayDevice withInterface = new() { cb = Marshal.SizeOf<User32Monitor.DisplayDevice>() };
            bool sameDevice = User32Monitor.EnumDisplayDevices(
                                  adapterName,
                                  index,
                                  ref withInterface,
                                  User32Monitor.EDD_GET_DEVICE_INTERFACE_NAME)
                              && string.Equals(withInterface.DeviceName, device.DeviceName, StringComparison.Ordinal);
            devices.Add(device);
            interfacePaths.Add(sameDevice ? withInterface.DeviceID : string.Empty);
        }

        int selected = SelectMonitorDeviceIndex([.. devices.Select(static device => device.StateFlags)]);
        if (selected < 0) return (adapterName, string.Empty);

        string deviceID = string.IsNullOrEmpty(devices[selected].DeviceID) ? adapterName : devices[selected].DeviceID;
        return (deviceID, ParseDisplayInstancePath(interfacePaths[selected]));
    }

    /// <summary>
    /// Index of the monitor device that describes a source: the first one flagged <c>DISPLAY_DEVICE_ACTIVE</c>, or -1.
    /// While a new monitor's device is still attaching, only other panels' inactive devices may be listed under the
    /// source; none of them describes it, so the caller falls back to the adapter name.
    /// </summary>
    internal static int SelectMonitorDeviceIndex(IReadOnlyList<int> stateFlags)
    {
        for (int index = 0; index < stateFlags.Count; index++)
        {
            if ((stateFlags[index] & DisplayDeviceActive) != 0) return index;
        }

        return -1;
    }

    /// <summary>
    /// Converts a monitor device-interface path (<c>\\?\DISPLAY#hwid#instance#{GUID}</c>) to its
    /// <c>DISPLAY\hwid\instance</c> instance path. Returns empty for any other shape.
    /// </summary>
    internal static string ParseDisplayInstancePath(string interfacePath)
    {
        if (string.IsNullOrEmpty(interfacePath)) return string.Empty;

        // Strip the "\\?\" prefix and trailing "#{GUID}", then swap '#' -> '\'
        const string prefix = @"\\?\";
        if (!interfacePath.StartsWith(prefix, StringComparison.Ordinal)) return string.Empty;

        string body = interfacePath[prefix.Length..];
        int lastHash = body.LastIndexOf('#');
        if (lastHash <= 0) return string.Empty;

        return body[..lastHash].Replace(oldChar: '#', newChar: '\\');
    }

    private static void AttachWindowsBrightnessTargets(List<DDCMonitor> monitors)
    {
        if (!WindowsBrightnessWmi.TryGetActiveTargets(
                out IReadOnlyList<WindowsBrightnessTarget> targets,
                out string? error))
        {
            TADNLog.Log($"DisplayService: Windows brightness enumeration skipped: {error}");
            return;
        }

        if (targets.Count == 0) return;

        HashSet<string> assignedInstances = new(StringComparer.Ordinal);
        foreach (DDCMonitor monitor in monitors)
        {
            if (string.IsNullOrEmpty(monitor.DisplayInstancePath)) continue;

            WindowsBrightnessTarget? target = targets.FirstOrDefault(t =>
                !assignedInstances.Contains(t.InstanceName)
                && string.Equals(t.DisplayInstancePath, monitor.DisplayInstancePath,
                    StringComparison.OrdinalIgnoreCase));
            if (target == null) continue;

            ApplyWindowsBrightnessTarget(monitor, target);
            assignedInstances.Add(target.InstanceName);
        }

        foreach (WindowsBrightnessTarget target in targets)
        {
            if (assignedInstances.Contains(target.InstanceName)) continue;

            DDCMonitor monitor = new()
            {
                Name = $"WindowsBrightness:{target.DisplayInstancePath}",
                DeviceID = target.DisplayInstancePath,
                DisplayInstancePath = target.DisplayInstancePath,
                FriendlyName = "Windows display"
            };
            ApplyWindowsBrightnessTarget(monitor, target);
            monitors.Add(monitor);
            assignedInstances.Add(target.InstanceName);
        }
    }

    private static void ApplyWindowsBrightnessTarget(DDCMonitor monitor, WindowsBrightnessTarget target)
    {
        monitor.BrightnessControlKind = MonitorBrightnessControlKind.Windows;
        monitor.DeviceID = target.DisplayInstancePath;
        monitor.DisplayInstancePath = target.DisplayInstancePath;
        monitor.WindowsBrightnessInstanceName = target.InstanceName;
        monitor.WindowsBrightnessMethodPath = target.MethodPath;
        monitor.BrightnessCode = VCPConstants.Brightness;
    }

    public bool TryGetCapabilities(
        DDCMonitor monitor, out string capabilities, out string? error, CancellationToken ct = default)
    {
        // Two-step DDC sequence: read length, then read bytes.
        // Both failures collapse into the same "no usable capability string" bucket.
        // Capabilities are optional - many monitors don't expose one even when VCP read/write works fine.
        // Soft failure with a descriptive error.
        DDCCallOutcome<string> outcome = RunWithTimeout(
            monitor,
            () => TryWithPhysicalMonitor<string>(monitor, handle =>
            {
                if (!Dxva2.GetCapabilitiesStringLength(handle, out uint length) || length == 0)
                {
                    return DDCCallOutcome<string>.Fail(
                        $"GetCapabilitiesStringLength failed (Win32: {Marshal.GetLastWin32Error()})");
                }

                StringBuilder capabilitiesBuffer = new((int)length);
                if (!Dxva2.CapabilitiesRequestAndCapabilitiesReply(handle, capabilitiesBuffer, length))
                {
                    return DDCCallOutcome<string>.Fail(
                        $"CapabilitiesRequestAndCapabilitiesReply failed (Win32: {Marshal.GetLastWin32Error()})");
                }

                return DDCCallOutcome<string>.Ok(capabilitiesBuffer.ToString());
            }),
            $"GetCapabilities('{monitor.Name}')",
            ct,
            (helper, timeoutMs, helperCancellationToken) =>
                helper.TryGetCapabilities(monitor, timeoutMs, helperCancellationToken));

        capabilities = outcome.Value;
        error = outcome.Error;
        return outcome.Success;
    }

    public bool TryGetVCPCapabilities(
        DDCMonitor monitor, out IReadOnlyList<VCPCapability> capabilities, out string? error,
        CancellationToken ct = default)
    {
        if (monitor.BrightnessControlKind == MonitorBrightnessControlKind.Windows)
        {
            capabilities =
            [
                new VCPCapability
                {
                    Name = "Windows brightness (0x10)", OptCode = VCPConstants.Brightness, Value = 0, MaxValue = 100
                }
            ];
            error = null;
            return true;
        }

        if (!TryGetCapabilities(monitor, out string capsString, out error, ct))
        {
            capabilities = [];
            return false;
        }

        CapabilitiesTokenizer tokenizer = new();
        CapabilitiesParser parser = new();
        INodeFormatter formatter = new NodeFormatter();

        IEnumerable<IToken> tokens = tokenizer.GetTokens(capsString);
        INode root = parser.Parse(tokens);

        IEnumerable<INode> rootChildren = root.Nodes ?? [];
        INode? vcpNode = rootChildren
            .RecursiveSelect(n => n.Nodes ?? [])
            .FirstOrDefault(n => string.Equals(n.Value, b: "vcp", StringComparison.OrdinalIgnoreCase));

        if (vcpNode?.Nodes == null)
        {
            capabilities = [];
            return true;
        }

        capabilities = ReadCapabilities(monitor, vcpNode, formatter, ct).ToList();
        error = null;
        return true;
    }

    public bool TryGetVCPFeature(
        DDCMonitor monitor, byte code, out uint currentValue, out uint maxValue, out string? error,
        CancellationToken ct = default)
    {
        if (monitor.BrightnessControlKind == MonitorBrightnessControlKind.Windows)
            return TryGetWindowsBrightnessFeature(monitor, code, out currentValue, out maxValue, out error);

        DDCCallOutcome<(uint Cur, uint Max)> outcome = RunWithTimeout(
            monitor,
            () => TryWithPhysicalMonitor(monitor, handle =>
            {
                if (!Dxva2.GetVCPFeatureAndVCPFeatureReply(handle, code, IntPtr.Zero, out uint c, out uint m))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    return DDCCallOutcome<(uint, uint)>.Fail(
                        DDCNativeError.Format(operation: "GetVCPFeatureAndVCPFeatureReply", errorCode));
                }

                return DDCCallOutcome<(uint, uint)>.Ok((c, m));
            }),
            $"TryGetVCPFeature('{monitor.Name}', 0x{code:X2})",
            ct,
            (helper, timeoutMs, helperCancellationToken) =>
                helper.TryGetVCPFeature(monitor, code, timeoutMs, helperCancellationToken));

        currentValue = outcome.Value.Cur;
        maxValue = outcome.Value.Max;
        error = outcome.Error;
        return outcome.Success;
    }

    public bool TrySetVCPFeature(
        DDCMonitor monitor, byte code, uint value, out string? error, CancellationToken ct = default)
    {
        if (monitor.BrightnessControlKind == MonitorBrightnessControlKind.Windows)
            return TrySetWindowsBrightnessFeature(monitor, code, value, out error);

        DDCCallOutcome<bool> outcome = RunWithTimeout(
            monitor,
            () => TryWithPhysicalMonitor(monitor, handle =>
            {
                if (!Dxva2.SetVCPFeature(handle, code, value))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    return DDCCallOutcome<bool>.Fail(
                        DDCNativeError.Format(operation: "SetVCPFeature", errorCode));
                }

                return DDCCallOutcome<bool>.Ok(true);
            }),
            $"TrySetVCPFeature('{monitor.Name}', 0x{code:X2}={value})",
            ct,
            (helper, timeoutMs, helperCancellationToken) => helper.TrySetVCPFeature(
                monitor,
                code,
                value,
                timeoutMs,
                helperCancellationToken));

        error = outcome.Error;
        return outcome.Success;
    }

    /// <inheritdoc />
    public void ResetDDCTransport(DDCMonitor monitor)
    {
        lock (_helperClientsGate)
        {
            if (!_useHelperProcess || _helperClients == null || _disposed)
                return;
        }

        string helperClientKey = BuildHelperClientKey(monitor);
        DDCHelperClient? helperClient;
        lock (_helperClientsGate)
        {
            if (_disposed) return;
            _helperClients.Remove(helperClientKey, out helperClient);
        }

        if (helperClient == null) return;

        TADNLog.Log($"DisplayService: resetting DDC helper transport for '{monitor.Name}'");
        helperClient.Dispose();
    }

    /// <inheritdoc />
    public void RetireDDCTransport(DDCMonitor monitor)
    {
        string helperClientKey = BuildHelperClientKey(monitor);
        DDCHelperClient? helperClient;
        lock (_helperClientsGate)
        {
            if (!_useHelperProcess || _helperClients == null || _disposed) return;

            if (!_helperClients.Remove(helperClientKey, out helperClient)) return;

            _retiredHelperClientKeys.Add(helperClientKey);
        }

        TADNLog.Log($"DisplayService: retiring the helper of superseded identity {helperClientKey}");

        // MonitorService calls this from the UI thread; ending the process waits on its exit
        _ = Task.Run(helperClient.Retire);
    }

    private static bool TryGetWindowsBrightnessFeature(
        DDCMonitor monitor,
        byte code,
        out uint currentValue,
        out uint maxValue,
        out string? error)
    {
        currentValue = 0;
        maxValue = 100;
        error = null;

        if (code != VCPConstants.Brightness)
        {
            error = $"Windows brightness backend does not support VCP 0x{code:X2}.";
            return false;
        }

        if (!WindowsBrightnessWmi.TryGetBrightness(
                monitor.WindowsBrightnessInstanceName,
                out int brightness,
                out error))
            return false;

        currentValue = (uint)Math.Clamp(brightness, min: 0, max: 100);
        return true;
    }

    private static bool TrySetWindowsBrightnessFeature(
        DDCMonitor monitor,
        byte code,
        uint value,
        out string? error)
    {
        error = null;

        if (code != VCPConstants.Brightness)
        {
            error = $"Windows brightness backend does not support VCP 0x{code:X2}.";
            return false;
        }

        int percent = (int)Math.Clamp(value, min: 0, max: 100);
        return WindowsBrightnessWmi.TrySetBrightness(monitor.WindowsBrightnessMethodPath, percent, out error);
    }

    private IEnumerable<VCPCapability> ReadCapabilities(
        DDCMonitor monitor, INode vcpNode, INodeFormatter formatter, CancellationToken ct)
    {
        foreach (INode capabilityNode in vcpNode.Nodes!)
        {
            if (!byte.TryParse(
                    capabilityNode.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte code))
                continue;

            string? formatted = formatter.FormatNode(capabilityNode);
            if (formatted == null) continue;

            if (!TryGetVCPFeature(monitor, code, out uint current, out uint max, out _, ct) || max == 0) continue;

            yield return new VCPCapability
            {
                Name = $"{formatted} (0x{capabilityNode.Value})", OptCode = code, Value = current, MaxValue = max
            };
        }
    }

    public bool RefreshHandle(DDCMonitor monitor)
    {
        if (string.IsNullOrEmpty(monitor.Name)) return false;

        Dictionary<string, int> friendlyByAdapter = CCD.BuildFriendlyDisplayNumberMap();
        RefreshHandleIdentity targetIdentity = RefreshHandleIdentity.From(monitor);
        List<(IntPtr Handle, IntPtr HDC, int Left, int Top, string FriendlyName, RefreshHandleIdentity Identity)>
            candidates = [];

        // Every monitor is read before choosing, so the strongest identity rule wins rather than the first match
        _ = User32Monitor.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);

        int selected = SelectRefreshHandleCandidate(
            targetIdentity,
            [.. candidates.Select(static candidate => candidate.Identity)],
            out RefreshHandleMatch match);
        if (selected < 0) return false;

        (IntPtr handle, IntPtr deviceContext, int left, int top, string friendlyName, RefreshHandleIdentity identity) =
            candidates[selected];
        if (match == RefreshHandleMatch.AdapterNameFallback)
        {
            TADNLog.Log(
                $"DisplayService.RefreshHandle: using adapter-name fallback for '{monitor.Name}' "
                + $"(targetDevice='{targetIdentity.DeviceID}', newDevice='{identity.DeviceID}')");
        }

        // The WMI lookup is slow, so it runs before any field changes: a concurrent caller must never see a
        // Windows-brightness panel described as DDC/CI under a different DeviceID (and DDC lock)
        WindowsBrightnessTarget? windowsTarget = targetIdentity.ControlKind == MonitorBrightnessControlKind.Windows
            ? FindWindowsBrightnessTarget(identity.DisplayInstancePath)
            : null;

        monitor.Handle = handle;
        monitor.HDC = deviceContext;
        monitor.Name = identity.Name;
        monitor.DeviceID = windowsTarget?.DisplayInstancePath ?? identity.DeviceID;
        monitor.DisplayInstancePath = windowsTarget?.DisplayInstancePath ?? identity.DisplayInstancePath;
        monitor.DisplayNumber = CCD.ResolveFriendlyDisplayNumber(identity.Name, friendlyByAdapter);
        monitor.X = left;
        monitor.Y = top;
        monitor.EDIDSerial = identity.EDIDSerial;
        monitor.FriendlyName = friendlyName;
        monitor.EDIDManufacturerID = identity.EDIDManufacturerID;
        monitor.EDIDProductCode = identity.EDIDProductCode;
        monitor.BrightnessControlKind = windowsTarget == null
            ? MonitorBrightnessControlKind.DdcCi
            : MonitorBrightnessControlKind.Windows;
        monitor.WindowsBrightnessInstanceName = windowsTarget?.InstanceName ?? string.Empty;
        monitor.WindowsBrightnessMethodPath = windowsTarget?.MethodPath ?? string.Empty;
        DDCMonitorDatabase.ApplyProfile(monitor);
        return true;

        bool Callback(IntPtr hMonitor, IntPtr monitorHDC, ref User32Monitor.Rect rect, IntPtr data)
        {
            User32Monitor.MonitorInfoEx info = new();
            if (!User32Monitor.GetMonitorInfo(new HandleRef(wrapper: null, hMonitor), info)) return true;

            string adapterName = new string(info.szDevice).TrimEnd('\0');
            (string deviceID, string displayInstancePath) = ResolveMonitorDevice(adapterName);
            byte[]? edid = ReadEDID(displayInstancePath);
            string serial = edid == null ? string.Empty : EDIDParser.ExtractSerial(edid);
            string manufacturer = edid == null ? string.Empty : EDIDParser.ExtractManufacturerID(edid);
            string product = string.Empty;
            string monitorName = string.Empty;
            if (edid != null)
            {
                ushort productCode = EDIDParser.ExtractProductCode(edid);
                product = productCode == 0
                    ? string.Empty
                    : productCode.ToString(format: "X4", CultureInfo.InvariantCulture);
                monitorName = EDIDParser.ExtractMonitorName(edid);
            }

            candidates.Add((
                hMonitor,
                monitorHDC,
                rect.left,
                rect.top,
                monitorName,
                new RefreshHandleIdentity(
                    adapterName,
                    deviceID,
                    displayInstancePath,
                    serial,
                    manufacturer,
                    product,
                    MonitorBrightnessControlKind.DdcCi)));
            return true;
        }

        static WindowsBrightnessTarget? FindWindowsBrightnessTarget(string displayInstancePath)
        {
            if (!WindowsBrightnessWmi.TryGetActiveTargets(
                    out IReadOnlyList<WindowsBrightnessTarget> targets,
                    out _))
                return null;

            return targets.FirstOrDefault(t =>
                string.Equals(t.DisplayInstancePath, displayInstancePath, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Index of the enumerated monitor <see cref="RefreshHandle"/> rebinds to, or -1 when none qualifies.
    /// The strongest rule wins across all candidates and the earliest wins among equals, so a stable DeviceID match
    /// on a later monitor beats an adapter-name match on an earlier one.
    /// </summary>
    internal static int SelectRefreshHandleCandidate(
        RefreshHandleIdentity target,
        IReadOnlyList<RefreshHandleIdentity> candidates,
        out RefreshHandleMatch match)
    {
        int selected = -1;
        match = RefreshHandleMatch.None;
        for (int index = 0; index < candidates.Count; index++)
        {
            RefreshHandleMatch candidateMatch = MatchRefreshHandleCandidate(target, candidates[index]);
            if (candidateMatch == RefreshHandleMatch.None) continue;
            if (selected >= 0 && candidateMatch >= match) continue;

            selected = index;
            match = candidateMatch;
        }

        return selected;
    }

    /// <summary>
    /// Strongest identity rule one enumerated monitor satisfies for <see cref="RefreshHandle"/>; lower values are
    /// stronger. An adapter-name match is refused when the EDID shows another panel: GDI names move between panels
    /// on every topology change.
    /// </summary>
    internal static RefreshHandleMatch MatchRefreshHandleCandidate(
        RefreshHandleIdentity target,
        RefreshHandleIdentity candidate)
    {
        bool stableDeviceMatch = HasStableDeviceID(target.DeviceID)
                                 && HasStableDeviceID(candidate.DeviceID)
                                 && string.Equals(candidate.DeviceID, target.DeviceID, StringComparison.Ordinal);
        if (stableDeviceMatch) return RefreshHandleMatch.StableDevice;

        bool windowsDisplayInstanceMatch = target.ControlKind == MonitorBrightnessControlKind.Windows
                                           && !string.IsNullOrEmpty(target.DisplayInstancePath)
                                           && !string.IsNullOrEmpty(candidate.DisplayInstancePath)
                                           && string.Equals(
                                               candidate.DisplayInstancePath,
                                               target.DisplayInstancePath,
                                               StringComparison.OrdinalIgnoreCase);
        if (windowsDisplayInstanceMatch) return RefreshHandleMatch.WindowsDisplayInstance;

        bool EDIDMatch = !string.IsNullOrEmpty(target.EDIDSerial)
                         && string.Equals(candidate.EDIDSerial, target.EDIDSerial, StringComparison.Ordinal)
                         && (string.IsNullOrEmpty(target.EDIDManufacturerID)
                             || string.Equals(
                                 candidate.EDIDManufacturerID,
                                 target.EDIDManufacturerID,
                                 StringComparison.Ordinal))
                         && (string.IsNullOrEmpty(target.EDIDProductCode)
                             || string.Equals(
                                 candidate.EDIDProductCode,
                                 target.EDIDProductCode,
                                 StringComparison.Ordinal));
        if (EDIDMatch) return RefreshHandleMatch.EDID;

        if (!string.Equals(candidate.Name, target.Name, StringComparison.Ordinal)) return RefreshHandleMatch.None;

        bool EDIDContradicts = Differs(target.EDIDSerial, candidate.EDIDSerial)
                               || Differs(target.EDIDManufacturerID, candidate.EDIDManufacturerID)
                               || Differs(target.EDIDProductCode, candidate.EDIDProductCode);
        return EDIDContradicts ? RefreshHandleMatch.None : RefreshHandleMatch.AdapterNameFallback;

        static bool HasStableDeviceID(string deviceID) =>
            !string.IsNullOrEmpty(deviceID)
            && !deviceID.StartsWith(value: @"\\.\", StringComparison.Ordinal);

        static bool Differs(string first, string second) =>
            !string.IsNullOrEmpty(first)
            && !string.IsNullOrEmpty(second)
            && !string.Equals(first, second, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs <paramref name="op"/> through the monitor's DDC helper process when hard timeouts are enabled.
    /// If a dxva2 call hangs, only that per-monitor helper is killed and Windows releases that process's
    /// <c>PHYSICAL_MONITOR</c> handles. The tray process no longer abandons a blocked thread that
    /// owns native monitor handles.
    ///
    /// The parent always clamps <see cref="OperationTimeoutMs"/> to a positive value. Only the helper process
    /// implementation runs inline, where process termination can abort a blocking P/Invoke.
    /// </summary>
    private DDCCallOutcome<T> RunWithTimeout<T>(
        DDCMonitor monitor,
        Func<DDCCallOutcome<T>> op,
        string opLabel,
        CancellationToken ct = default,
        Func<DDCHelperClient, int, CancellationToken, DDCCallOutcome<T>>? helperOp = null)
    {
        lock (_helperClientsGate)
        {
            if (_disposed)
                return DDCCallOutcome<T>.WithError("Display service is disposed.");
        }

        // Pre-check the sequence-level token before launching or touching the helper process.
        if (ct.IsCancellationRequested)
            return DDCCallOutcome<T>.WithError($"DDC op '{opLabel}' cancelled by sequence deadline.");

        int timeoutMs = OperationTimeoutMs;
        if (_useHelperProcess && timeoutMs > 0 && _helperClients != null && helperOp != null)
        {
            string helperClientKey = BuildHelperClientKey(monitor);
            DDCHelperClient? helperClient = null;
            lock (_helperClientsGate)
            {
                if (_disposed) return DDCCallOutcome<T>.WithError("Display service is disposed.");
                if (!_retiredHelperClientKeys.Contains(helperClientKey)
                    && !_helperClients.TryGetValue(helperClientKey, out helperClient))
                {
                    helperClient = new DDCHelperClient();
                    _helperClients.Add(helperClientKey, helperClient);
                }
            }

            // A command for a retired identity, typically one already in flight when its row went away
            if (helperClient == null) return RunOnOneShotHelper();

            DDCCallOutcome<T> outcome = RunOnHelper(helperClient);
            if (outcome.Success || outcome.Error != DDCHelperClient.RetiredBeforeSendError) return outcome;

            // A helper retired between lookup and command refused it unsent. Its identity is going away, so the one
            // retry runs on a helper that is never registered and ends with the command.
            lock (_helperClientsGate)
            {
                if (_disposed) return DDCCallOutcome<T>.WithError("Display service is disposed.");
            }

            return RunOnOneShotHelper();
        }

        try
        {
            return op();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"DisplayService: {opLabel} threw unexpectedly: {ex.Message}");
            return DDCCallOutcome<T>.Fail($"unexpected exception: {ex.Message}");
        }

        DDCCallOutcome<T> RunOnHelper(DDCHelperClient client)
        {
            try { return helperOp!(client, timeoutMs, ct); }
            catch (Exception ex)
            {
                TADNLog.Log($"DisplayService: {opLabel} threw unexpectedly: {ex.Message}");
                return DDCCallOutcome<T>.Fail($"unexpected exception: {ex.Message}");
            }
        }

        DDCCallOutcome<T> RunOnOneShotHelper()
        {
            DDCHelperClient oneShotClient = new();
            try { return RunOnHelper(oneShotClient); }
            finally { _ = Task.Run(oneShotClient.Retire); }
        }
    }

    internal static string BuildHelperClientKey(DDCMonitor monitor)
    {
        if (!string.IsNullOrEmpty(monitor.DeviceID)) return "device:" + monitor.DeviceID;
        if (!string.IsNullOrEmpty(monitor.DisplayInstancePath)) return "instance:" + monitor.DisplayInstancePath;
        if (!string.IsNullOrEmpty(monitor.EDIDSerial)) return "edid:" + monitor.EDIDSerial;
        return "name:" + monitor.Name;
    }

    /// <summary>
    /// Opens the physical monitor(s) behind an HMONITOR, runs <paramref name="op"/> against index 0's handle,
    /// and releases on exit.
    /// Each HMONITOR maps to 1..N PHYSICAL_MONITOR handles;
    /// using index 0 matches the reference implementation and most consumers.
    ///
    /// Non-throwing form: handle-acquisition failures (zero physical monitors, API returned false)
    /// return as <see cref="DDCCallOutcome{T}.Fail"/>.
    /// <paramref name="op"/> also returns a <see cref="DDCCallOutcome{T}"/>
    /// so the inner dxva2 call can propagate failure cleanly.
    /// </summary>
    private static DDCCallOutcome<T> TryWithPhysicalMonitor<T>(DDCMonitor monitor, Func<IntPtr, DDCCallOutcome<T>> op)
    {
        if (!Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Handle, out uint count))
        {
            return DDCCallOutcome<T>.Fail(
                $"GetNumberOfPhysicalMonitorsFromHMONITOR failed for '{monitor.Name}' "
                + $"(Win32: {Marshal.GetLastWin32Error()})");
        }

        if (count == 0)
        {
            return DDCCallOutcome<T>.Fail(
                $"Monitor '{monitor.Name}' has no physical monitor handle (panel disconnected or asleep).");
        }

        Dxva2.PHYSICAL_MONITOR[] array = new Dxva2.PHYSICAL_MONITOR[count];
        if (!Dxva2.GetPhysicalMonitorsFromHMONITOR(monitor.Handle, count, array))
        {
            return DDCCallOutcome<T>.Fail(
                $"GetPhysicalMonitorsFromHMONITOR failed for '{monitor.Name}' (Win32: {Marshal.GetLastWin32Error()})");
        }

        try
        {
            return op(array[0].hPhysicalMonitor);
        }
        finally
        {
            Dxva2.DestroyPhysicalMonitors(count, array);
        }
    }
}

/// <summary>
/// Internal result envelope plumbing success / failure / timeout outcomes through the timeout wrapper
/// without throwing for expected DDC-failure cases.
/// <see cref="Success"/>=true: call completed and <see cref="Value"/> is meaningful.
/// false: <see cref="Error"/> describes why and <see cref="Value"/> is <c>default</c>.
/// </summary>
internal readonly struct DDCCallOutcome<T>
{
    public bool Success { get; }
    public T Value { get; }
    public string? Error { get; }

    private DDCCallOutcome(bool success, T value, string? error)
    {
        Success = success;
        Value = value;
        Error = error;
    }

    public static DDCCallOutcome<T> Ok(T value) => new(success: true, value, error: null);
    public static DDCCallOutcome<T> Fail(string error) => new(success: false, default!, error);

    /// <summary>
    /// Fail outcome with the supplied error string; used by timeout/cancellation paths to stamp op label and duration.
    /// </summary>
    public static DDCCallOutcome<T> WithError(string error) => new(success: false, default!, error);
}

/// <summary>Identity fields <see cref="DisplayService.RefreshHandle"/> compares between a cached and a live monitor.</summary>
internal readonly record struct RefreshHandleIdentity(
    string Name,
    string DeviceID,
    string DisplayInstancePath,
    string EDIDSerial,
    string EDIDManufacturerID,
    string EDIDProductCode,
    MonitorBrightnessControlKind ControlKind)
{
    public static RefreshHandleIdentity From(DDCMonitor monitor) =>
        new(
            monitor.Name,
            monitor.DeviceID,
            monitor.DisplayInstancePath,
            monitor.EDIDSerial,
            monitor.EDIDManufacturerID,
            monitor.EDIDProductCode,
            monitor.BrightnessControlKind);
}

/// <summary>Strongest identity rule an enumerated monitor satisfied in <see cref="DisplayService.RefreshHandle"/>.</summary>
internal enum RefreshHandleMatch
{
    None,
    StableDevice,
    WindowsDisplayInstance,
    EDID,
    AdapterNameFallback
}
