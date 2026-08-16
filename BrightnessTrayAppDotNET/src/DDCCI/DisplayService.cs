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
    private readonly bool _useHelperProcess;
    // One helper process per stable monitor identity. A blocked driver call can therefore time out and kill only
    // that monitor's helper instead of holding the single pipe lock in front of every other panel.
    private readonly Dictionary<string, DDCHelperClient>? _helperClients;
    private readonly Lock _helperClientsGate = new();
    private volatile bool _disposed;

    public DisplayService() : this(useHelperProcess: true) { }

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
        return TryGetMonitorsCore(helperResolutionOnly: false, out monitors, out error);
    }

    /// <summary>
    /// Enumerates the Win32 identity needed by the DDC helper without parent-only WMI, CCD, and profile work.
    /// </summary>
    internal static bool TryGetDDCMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error)
    {
        return TryGetMonitorsCore(helperResolutionOnly: true, out monitors, out error);
    }

    private static bool TryGetMonitorsCore(
        bool helperResolutionOnly,
        out IReadOnlyList<DDCMonitor> monitors,
        out string? error)
    {
        error = null;
        List<DDCMonitor> list = [];

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
            if (User32Monitor.GetMonitorInfo(new HandleRef(null, monitor.Handle), monitorInfo))
            {
                monitor.Name = new string(monitorInfo.szDevice).TrimEnd('\0');
                monitor.DeviceID = ResolveDeviceID(monitor.Name);
                monitor.DisplayInstancePath = ResolveDisplayInstancePath(monitor.Name);
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
                            : productCode.ToString("X4", CultureInfo.InvariantCulture);
                        // Populate per-monitor VCP profile fields (BrightnessCode, PowerOffCommands, ProfileQuirks)
                        // by EDID identity. Misses leave the VESA-standard defaults in place.
                        DDCMonitorDatabase.ApplyProfile(monitor);
                    }
                }
            }
        }

        if (!helperResolutionOnly)
            AttachWindowsBrightnessTargets(list);
        monitors = list;
        return true;

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref User32Monitor.Rect rect, IntPtr data)
        {
            list.Add(new DDCMonitor
            {
                Handle = hMonitor, HDC = hdc, X = rect.left, Y = rect.top
            });
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
            WPFLog.Log($"DisplayService.ReadEDID: failed for '{displayInstancePath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Queries <c>EnumDisplayDevices</c> for the monitor attached to the given adapter (e.g. <c>\\.\DISPLAY1</c>)
    /// and returns its stable DeviceID.
    /// Falls back to the adapter name so identity resolution never returns empty - callers can still key on it,
    /// they just lose the "same monitor on same port" invariant.
    /// </summary>
    private static string ResolveDeviceID(string adapterName)
    {
        if (string.IsNullOrEmpty(adapterName)) return string.Empty;

        User32Monitor.DisplayDevice dd = new() { cb = Marshal.SizeOf<User32Monitor.DisplayDevice>() };
        if (User32Monitor.EnumDisplayDevices(adapterName, 0, ref dd, 0) && !string.IsNullOrEmpty(dd.DeviceID))
            return dd.DeviceID;

        return adapterName;
    }

    /// <summary>
    /// Returns the DISPLAY\...\... instance path for the monitor attached to an adapter.
    /// This is the same shape WmiMonitorBrightness.InstanceName uses before its trailing target suffix.
    /// </summary>
    private static string ResolveDisplayInstancePath(string adapterName)
    {
        if (string.IsNullOrEmpty(adapterName)) return string.Empty;

        User32Monitor.DisplayDevice dd = new() { cb = Marshal.SizeOf<User32Monitor.DisplayDevice>() };
        if (!User32Monitor.EnumDisplayDevices(
                adapterName, 0, ref dd, User32Monitor.EDD_GET_DEVICE_INTERFACE_NAME))
            return string.Empty;

        string interfacePath = dd.DeviceID;
        if (string.IsNullOrEmpty(interfacePath)) return string.Empty;

        // Expected form: "\\?\DISPLAY#<hwid>#<instance>#{GUID}".
        // Strip the "\\?\" prefix and trailing "#{GUID}", then swap '#' -> '\'.
        const string prefix = @"\\?\";
        if (!interfacePath.StartsWith(prefix, StringComparison.Ordinal)) return string.Empty;

        string body = interfacePath[prefix.Length..];
        int lastHash = body.LastIndexOf('#');
        if (lastHash <= 0) return string.Empty;

        return body[..lastHash].Replace('#', '\\');
    }

    private static void AttachWindowsBrightnessTargets(List<DDCMonitor> monitors)
    {
        if (!WindowsBrightnessWmi.TryGetActiveTargets(
                out IReadOnlyList<WindowsBrightnessTarget> targets,
                out string? error))
        {
            WPFLog.Log($"DisplayService: Windows brightness enumeration skipped: {error}");
            return;
        }

        if (targets.Count == 0) return;

        HashSet<string> assignedInstances = new(StringComparer.Ordinal);
        foreach (DDCMonitor monitor in monitors)
        {
            if (string.IsNullOrEmpty(monitor.DisplayInstancePath)) continue;

            WindowsBrightnessTarget? target = targets.FirstOrDefault(t =>
                !assignedInstances.Contains(t.InstanceName)
                && string.Equals(t.DisplayInstancePath, monitor.DisplayInstancePath, StringComparison.OrdinalIgnoreCase));
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
            opLabel: $"GetCapabilities('{monitor.Name}')",
            ct: ct,
            helperOp: (helper, timeoutMs, helperCancellationToken) =>
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
                    Name = "Windows brightness (0x10)",
                    OptCode = VCPConstants.Brightness,
                    Value = 0,
                    MaxValue = 100
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
            .FirstOrDefault(n => string.Equals(n.Value, "vcp", StringComparison.OrdinalIgnoreCase));

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
                    return DDCCallOutcome<(uint, uint)>.Fail(
                        $"GetVCPFeatureAndVCPFeatureReply failed (Win32: {Marshal.GetLastWin32Error()})");
                }

                return DDCCallOutcome<(uint, uint)>.Ok((c, m));
            }),
            opLabel: $"TryGetVCPFeature('{monitor.Name}', 0x{code:X2})",
            ct: ct,
            helperOp: (helper, timeoutMs, helperCancellationToken) =>
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
                    return DDCCallOutcome<bool>.Fail(
                        $"SetVCPFeature failed (Win32: {Marshal.GetLastWin32Error()})");
                }

                return DDCCallOutcome<bool>.Ok(true);
            }),
            opLabel: $"TrySetVCPFeature('{monitor.Name}', 0x{code:X2}={value})",
            ct: ct,
            helperOp: (helper, timeoutMs, helperCancellationToken) => helper.TrySetVCPFeature(
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
            if (!_useHelperProcess || _helperClients == null || _disposed) return;
        }

        string helperClientKey = BuildHelperClientKey(monitor);
        DDCHelperClient? helperClient = null;
        lock (_helperClientsGate)
        {
            if (_disposed) return;
            _helperClients.Remove(helperClientKey, out helperClient);
        }

        if (helperClient == null) return;

        WPFLog.Log($"DisplayService: resetting DDC helper transport for '{monitor.Name}'");
        helperClient.Dispose();
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

        currentValue = (uint)Math.Clamp(brightness, 0, 100);
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

        int percent = (int)Math.Clamp(value, 0, 100);
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
        string targetDeviceID = monitor.DeviceID;
        string targetDisplayInstancePath = monitor.DisplayInstancePath;
        string targetSerial = monitor.EDIDSerial;
        string targetManufacturer = monitor.EDIDManufacturerID;
        string targetProduct = monitor.EDIDProductCode;
        MonitorBrightnessControlKind targetControlKind = monitor.BrightnessControlKind;

        IntPtr updatedHandle = IntPtr.Zero;
        IntPtr updatedHdc = IntPtr.Zero;
        string updatedName = monitor.Name;
        string updatedDeviceID = monitor.DeviceID;
        string updatedDisplayInstancePath = monitor.DisplayInstancePath;
        int updatedDisplayNumber = monitor.DisplayNumber;
        int updatedX = monitor.X;
        int updatedY = monitor.Y;
        string updatedSerial = monitor.EDIDSerial;
        string updatedFriendlyName = monitor.FriendlyName;
        string updatedManufacturer = monitor.EDIDManufacturerID;
        string updatedProduct = monitor.EDIDProductCode;
        MonitorBrightnessControlKind updatedControlKind = monitor.BrightnessControlKind;
        string updatedWindowsBrightnessInstanceName = monitor.WindowsBrightnessInstanceName;
        string updatedWindowsBrightnessMethodPath = monitor.WindowsBrightnessMethodPath;

        if (!User32Monitor.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero))
        {
            // EnumDisplayMonitors returns FALSE when the callback stops early (we do on match),
            // so the real signal is whether freshHandle was assigned.
        }

        if (updatedHandle == IntPtr.Zero) return false;

        monitor.Handle = updatedHandle;
        monitor.HDC = updatedHdc;
        monitor.Name = updatedName;
        monitor.DeviceID = updatedDeviceID;
        monitor.DisplayInstancePath = updatedDisplayInstancePath;
        monitor.DisplayNumber = updatedDisplayNumber;
        monitor.X = updatedX;
        monitor.Y = updatedY;
        monitor.EDIDSerial = updatedSerial;
        monitor.FriendlyName = updatedFriendlyName;
        monitor.EDIDManufacturerID = updatedManufacturer;
        monitor.EDIDProductCode = updatedProduct;
        monitor.BrightnessControlKind = updatedControlKind;
        monitor.WindowsBrightnessInstanceName = updatedWindowsBrightnessInstanceName;
        monitor.WindowsBrightnessMethodPath = updatedWindowsBrightnessMethodPath;
        DDCMonitorDatabase.ApplyProfile(monitor);
        return true;

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref User32Monitor.Rect rect, IntPtr data)
        {
            User32Monitor.MonitorInfoEx info = new();
            if (!User32Monitor.GetMonitorInfo(new HandleRef(null, hMonitor), info)) return true;

            string adapterName = new string(info.szDevice).TrimEnd('\0');
            string deviceID = ResolveDeviceID(adapterName);
            string displayInstancePath = ResolveDisplayInstancePath(adapterName);
            byte[]? edid = ReadEDID(displayInstancePath);
            string serial = edid == null ? string.Empty : EDIDParser.ExtractSerial(edid);
            string manufacturer = edid == null ? string.Empty : EDIDParser.ExtractManufacturerID(edid);
            string product = string.Empty;
            string friendlyName = string.Empty;
            if (edid != null)
            {
                ushort productCode = EDIDParser.ExtractProductCode(edid);
                product = productCode == 0 ? string.Empty : productCode.ToString("X4", CultureInfo.InvariantCulture);
                friendlyName = EDIDParser.ExtractMonitorName(edid);
            }

            bool stableDeviceMatch = HasStableDeviceID(targetDeviceID)
                                     && HasStableDeviceID(deviceID)
                                     && string.Equals(deviceID, targetDeviceID, StringComparison.Ordinal);
            bool windowsDisplayInstanceMatch = targetControlKind == MonitorBrightnessControlKind.Windows
                                              && !string.IsNullOrEmpty(targetDisplayInstancePath)
                                              && !string.IsNullOrEmpty(displayInstancePath)
                                              && string.Equals(
                                                  displayInstancePath,
                                                  targetDisplayInstancePath,
                                                  StringComparison.OrdinalIgnoreCase);
            bool EDIDMatch = !string.IsNullOrEmpty(targetSerial)
                             && string.Equals(serial, targetSerial, StringComparison.Ordinal)
                             && (string.IsNullOrEmpty(targetManufacturer)
                                 || string.Equals(manufacturer, targetManufacturer, StringComparison.Ordinal))
                             && (string.IsNullOrEmpty(targetProduct)
                                 || string.Equals(product, targetProduct, StringComparison.Ordinal));
            bool adapterFallbackMatch = string.Equals(adapterName, monitor.Name, StringComparison.Ordinal);

            if (!stableDeviceMatch && !windowsDisplayInstanceMatch && !EDIDMatch && !adapterFallbackMatch)
                return true;

            if (adapterFallbackMatch && !stableDeviceMatch && !windowsDisplayInstanceMatch && !EDIDMatch)
            {
                WPFLog.Log(
                    $"DisplayService.RefreshHandle: using adapter-name fallback for '{monitor.Name}' "
                    + $"(targetDevice='{targetDeviceID}', newDevice='{deviceID}')");
            }

            updatedHandle = hMonitor;
            updatedHdc = hdc;
            updatedName = adapterName;
            updatedDeviceID = deviceID;
            updatedDisplayInstancePath = displayInstancePath;
            updatedDisplayNumber = CCD.ResolveFriendlyDisplayNumber(adapterName, friendlyByAdapter);
            updatedX = rect.left;
            updatedY = rect.top;
            updatedSerial = serial;
            updatedFriendlyName = friendlyName;
            updatedManufacturer = manufacturer;
            updatedProduct = product;
            ApplyWindowsBrightnessFromCurrentEnumeration();
            return false; // match found - stop enumeration
        }

        void ApplyWindowsBrightnessFromCurrentEnumeration()
        {
            updatedControlKind = MonitorBrightnessControlKind.DdcCi;
            updatedWindowsBrightnessInstanceName = string.Empty;
            updatedWindowsBrightnessMethodPath = string.Empty;

            if (!WindowsBrightnessWmi.TryGetActiveTargets(
                    out IReadOnlyList<WindowsBrightnessTarget> targets,
                    out _))
                return;

            WindowsBrightnessTarget? target = targets.FirstOrDefault(t =>
                string.Equals(t.DisplayInstancePath, updatedDisplayInstancePath, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;

            updatedControlKind = MonitorBrightnessControlKind.Windows;
            updatedDeviceID = target.DisplayInstancePath;
            updatedDisplayInstancePath = target.DisplayInstancePath;
            updatedWindowsBrightnessInstanceName = target.InstanceName;
            updatedWindowsBrightnessMethodPath = target.MethodPath;
        }

        static bool HasStableDeviceID(string deviceID) =>
            !string.IsNullOrEmpty(deviceID)
            && !deviceID.StartsWith(@"\\.\", StringComparison.Ordinal);
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
            if (_disposed) return DDCCallOutcome<T>.WithError("Display service is disposed.");
        }

        // Pre-check the sequence-level token before launching or touching the helper process.
        if (ct.IsCancellationRequested)
            return DDCCallOutcome<T>.WithError($"DDC op '{opLabel}' cancelled by sequence deadline.");

        int timeoutMs = OperationTimeoutMs;
        if (_useHelperProcess && timeoutMs > 0 && _helperClients != null && helperOp != null)
        {
            DDCHelperClient helperClient;
            lock (_helperClientsGate)
            {
                if (_disposed) return DDCCallOutcome<T>.WithError("Display service is disposed.");
                string helperClientKey = BuildHelperClientKey(monitor);
                if (!_helperClients.TryGetValue(helperClientKey, out helperClient!))
                {
                    helperClient = new DDCHelperClient();
                    _helperClients.Add(helperClientKey, helperClient);
                }
            }

            try { return helperOp(helperClient, timeoutMs, ct); }
            catch (Exception ex)
            {
                WPFLog.Log($"DisplayService: {opLabel} threw unexpectedly: {ex.Message}");
                return DDCCallOutcome<T>.Fail($"unexpected exception: {ex.Message}");
            }
        }

        try
        {
            return op();
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DisplayService: {opLabel} threw unexpectedly: {ex.Message}");
            return DDCCallOutcome<T>.Fail($"unexpected exception: {ex.Message}");
        }
    }

    private static string BuildHelperClientKey(DDCMonitor monitor)
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

    public static DDCCallOutcome<T> Ok(T value) => new(true, value, null);
    public static DDCCallOutcome<T> Fail(string error) => new(false, default!, error);

    /// <summary>
    /// Fail outcome with the supplied error string; used by timeout/cancellation paths to stamp op label and duration.
    /// </summary>
    public static DDCCallOutcome<T> WithError(string error) => new(false, default!, error);
}
