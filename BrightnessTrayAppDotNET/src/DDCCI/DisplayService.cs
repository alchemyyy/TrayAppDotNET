using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using BrightnessTrayAppDotNET.DDCCI.Parser;
using BrightnessTrayAppDotNET.DDCCI.Parser.Nodes;
using BrightnessTrayAppDotNET.DDCCI.Tokenizer;
using BrightnessTrayAppDotNET.DDCCI.Tokenizer.Tokens;
using BrightnessTrayAppDotNET.Interop.DDCCI;
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
public class DisplayService : IDisplayService
{
    private readonly Lock _abandonedOpsGate = new();
    private readonly Dictionary<string, Task> _abandonedOpsByMonitor = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public int OperationTimeoutMs { get; set; } = TimeConstants.DisplayServiceOperationTimeoutMs;

    public bool TryGetMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error)
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
        Dictionary<string, int> friendlyByAdapter = CCD.BuildFriendlyDisplayNumberMap();

        foreach (DDCMonitor monitor in list)
        {
            User32Monitor.MonitorInfoEx monitorInfo = new();
            if (User32Monitor.GetMonitorInfo(new HandleRef(null, monitor.Handle), monitorInfo))
            {
                monitor.Name = new string(monitorInfo.szDevice).TrimEnd('\0');
                monitor.DisplayNumber = CCD.ResolveFriendlyDisplayNumber(monitor.Name, friendlyByAdapter);
                monitor.DeviceID = ResolveDeviceID(monitor.Name);

                byte[]? edid = ReadEDID(monitor.Name);
                if (edid != null)
                {
                    monitor.EDIDSerial = EDIDParser.ExtractSerial(edid);
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

        monitors = list;
        return true;

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref User32Monitor.Rect rect, IntPtr data)
        {
            list.Add(new DDCMonitor
            {
                Handle = hMonitor, HDC = hdc, X = rect.left, Y = rect.top,
            });
            return true;
        }
    }

    /// <summary>
    /// Reads the EDID block for the monitor attached to the given adapter
    /// from <c>HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\...\Device Parameters</c>.
    /// Uses <c>EnumDisplayDevices</c> with <c>EDD_GET_DEVICE_INTERFACE_NAME</c> so the returned DeviceID
    /// is the device-interface path: the same instance path Windows uses for the Enum subtree,
    /// just with <c>#</c> separators instead of <c>\</c>.
    /// Returns null when path or key is missing; EDID is optional, not load-bearing.
    /// </summary>
    private static byte[]? ReadEDID(string adapterName)
    {
        if (string.IsNullOrEmpty(adapterName)) return null;

        User32Monitor.DisplayDevice dd = new() { cb = Marshal.SizeOf<User32Monitor.DisplayDevice>() };
        if (!User32Monitor.EnumDisplayDevices(
                adapterName, 0, ref dd, User32Monitor.EDD_GET_DEVICE_INTERFACE_NAME))
            return null;

        string interfacePath = dd.DeviceID;
        if (string.IsNullOrEmpty(interfacePath)) return null;

        // Expected form: "\\?\DISPLAY#<hwid>#<instance>#{GUID}"
        // Strip the "\\?\" prefix and the trailing "#{GUID}", then swap '#' -> '\'.
        const string prefix = @"\\?\";
        if (!interfacePath.StartsWith(prefix, StringComparison.Ordinal)) return null;

        string body = interfacePath[prefix.Length..];
        int lastHash = body.LastIndexOf('#');
        if (lastHash <= 0) return null;

        string regPath = body[..lastHash].Replace('#', '\\');
        string keyPath = $@"SYSTEM\CurrentControlSet\Enum\{regPath}\Device Parameters";

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
            return key?.GetValue("EDID") as byte[];
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DisplayService.ReadEDID: failed for '{adapterName}': {ex.Message}");
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
            ct: ct);

        capabilities = outcome.Value;
        error = outcome.Error;
        return outcome.Success;
    }

    public bool TryGetVCPCapabilities(
        DDCMonitor monitor, out IReadOnlyList<VCPCapability> capabilities, out string? error,
        CancellationToken ct = default)
    {
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
            ct: ct);

        currentValue = outcome.Value.Cur;
        maxValue = outcome.Value.Max;
        error = outcome.Error;
        return outcome.Success;
    }

    public bool TrySetVCPFeature(
        DDCMonitor monitor, byte code, uint value, out string? error, CancellationToken ct = default)
    {
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
            ct: ct);

        error = outcome.Error;
        return outcome.Success;
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
        string targetSerial = monitor.EDIDSerial;
        string targetManufacturer = monitor.EDIDManufacturerID;
        string targetProduct = monitor.EDIDProductCode;

        IntPtr updatedHandle = IntPtr.Zero;
        IntPtr updatedHdc = IntPtr.Zero;
        string updatedName = monitor.Name;
        string updatedDeviceID = monitor.DeviceID;
        int updatedDisplayNumber = monitor.DisplayNumber;
        int updatedX = monitor.X;
        int updatedY = monitor.Y;
        string updatedSerial = monitor.EDIDSerial;
        string updatedFriendlyName = monitor.FriendlyName;
        string updatedManufacturer = monitor.EDIDManufacturerID;
        string updatedProduct = monitor.EDIDProductCode;

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
        monitor.DisplayNumber = updatedDisplayNumber;
        monitor.X = updatedX;
        monitor.Y = updatedY;
        monitor.EDIDSerial = updatedSerial;
        monitor.FriendlyName = updatedFriendlyName;
        monitor.EDIDManufacturerID = updatedManufacturer;
        monitor.EDIDProductCode = updatedProduct;
        DDCMonitorDatabase.ApplyProfile(monitor);
        return true;

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref User32Monitor.Rect rect, IntPtr data)
        {
            User32Monitor.MonitorInfoEx info = new();
            if (!User32Monitor.GetMonitorInfo(new HandleRef(null, hMonitor), info)) return true;

            string adapterName = new string(info.szDevice).TrimEnd('\0');
            string deviceID = ResolveDeviceID(adapterName);
            byte[]? edid = ReadEDID(adapterName);
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
            bool EDIDMatch = !string.IsNullOrEmpty(targetSerial)
                             && string.Equals(serial, targetSerial, StringComparison.Ordinal)
                             && (string.IsNullOrEmpty(targetManufacturer)
                                 || string.Equals(manufacturer, targetManufacturer, StringComparison.Ordinal))
                             && (string.IsNullOrEmpty(targetProduct)
                                 || string.Equals(product, targetProduct, StringComparison.Ordinal));
            bool adapterFallbackMatch = string.Equals(adapterName, monitor.Name, StringComparison.Ordinal);

            if (!stableDeviceMatch && !EDIDMatch && !adapterFallbackMatch) return true;

            if (adapterFallbackMatch && !stableDeviceMatch && !EDIDMatch)
            {
                WPFLog.Log(
                    $"DisplayService.RefreshHandle: using adapter-name fallback for '{monitor.Name}' "
                    + $"(targetDevice='{targetDeviceID}', newDevice='{deviceID}')");
            }

            updatedHandle = hMonitor;
            updatedHdc = hdc;
            updatedName = adapterName;
            updatedDeviceID = deviceID;
            updatedDisplayNumber = CCD.ResolveFriendlyDisplayNumber(adapterName, friendlyByAdapter);
            updatedX = rect.left;
            updatedY = rect.top;
            updatedSerial = serial;
            updatedFriendlyName = friendlyName;
            updatedManufacturer = manufacturer;
            updatedProduct = product;
            return false; // match found - stop enumeration
        }

        static bool HasStableDeviceID(string deviceID) =>
            !string.IsNullOrEmpty(deviceID)
            && !deviceID.StartsWith(@"\\.\", StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs <paramref name="op"/> on a threadpool thread, waiting up to <see cref="OperationTimeoutMs"/>
    /// (or until <paramref name="ct"/> is signalled).
    /// On timeout or cancellation, abandons the wait and returns a fail outcome
    /// - the task keeps running until the underlying dxva2 call returns,
    /// at which point <see cref="TryWithPhysicalMonitor{T}"/>'s finally block releases the
    /// <c>PHYSICAL_MONITOR</c> handles.
    /// Cleanup is preserved even after the caller leaves.
    ///
    /// <paramref name="op"/> reports failure via <see cref="DDCCallOutcome{T}"/> rather than throwing.
    /// Genuinely unexpected exceptions (NullReferenceException etc.) still propagate
    /// - those signal a real programming error and shouldn't be swallowed.
    ///
    /// With <see cref="OperationTimeoutMs"/> &lt;= 0 AND <c>default</c> <paramref name="ct"/>, runs inline.
    /// A cancellable <paramref name="ct"/> is honoured even when the per-op timeout is disabled,
    /// so a sequence-level deadline can still terminate the chain.
    /// </summary>
    private DDCCallOutcome<T> RunWithTimeout<T>(
        DDCMonitor monitor, Func<DDCCallOutcome<T>> op, string opLabel, CancellationToken ct = default)
    {
        // Pre-check the sequence-level token: a budgeted-out sequence short-circuits without paying for a Task.Run.
        if (ct.IsCancellationRequested)
            return DDCCallOutcome<T>.WithError($"DDC op '{opLabel}' cancelled by sequence deadline.");

        string monitorKey = DDCGateKey(monitor);
        if (TryGetActiveAbandonedOp(monitorKey, out _))
        {
            string message =
                $"DDC op '{opLabel}' deferred because a prior timed-out op for '{monitorKey}' "
                + "is still releasing physical-monitor handles.";
            WPFLog.Log($"DisplayService: {message}");
            return DDCCallOutcome<T>.WithError(message);
        }

        int timeoutMs = OperationTimeoutMs;
        if (timeoutMs <= 0 && !ct.CanBeCanceled)
        {
            try { return op(); }
            catch (Exception ex)
            {
                WPFLog.Log($"DisplayService: {opLabel} threw unexpectedly: {ex.Message}");
                return DDCCallOutcome<T>.Fail($"unexpected exception: {ex.Message}");
            }
        }

        Task<DDCCallOutcome<T>> operationTask = Task.Run(() =>
        {
            try { return op(); }
            catch (Exception ex)
            {
                WPFLog.Log($"DisplayService: {opLabel} threw unexpectedly: {ex.Message}");
                return DDCCallOutcome<T>.Fail($"unexpected exception: {ex.Message}");
            }
        }, ct);

        // Wait for whichever fires first: op completion, sequence deadline, or per-op timeout.
        // task.Wait(int, CancellationToken) returns false on timeout and throws OperationCanceledException
        // when the token is signalled, letting us distinguish the two failure modes for the trace message.
        int effectiveTimeoutMs = timeoutMs > 0 ? timeoutMs : Timeout.Infinite;
        try
        {
            if (operationTask.Wait(effectiveTimeoutMs, ct)) return operationTask.Result;
        }
        catch (OperationCanceledException)
        {
            WPFLog.Log(
                $"DisplayService: {opLabel} cancelled by sequence deadline; "
                + "abandoning op (handles will be released when it eventually completes).");
            TrackAbandoned(monitorKey, operationTask, opLabel);
            return DDCCallOutcome<T>.WithError($"DDC op '{opLabel}' cancelled by sequence deadline.");
        }

        WPFLog.Log(
            $"DisplayService: {opLabel} exceeded {timeoutMs}ms timeout; "
            + "abandoning op (handles will be released when it eventually completes).");
        TrackAbandoned(monitorKey, operationTask, opLabel);
        return DDCCallOutcome<T>.WithError($"DDC op '{opLabel}' exceeded {timeoutMs}ms timeout.");
    }

    /// <summary>
    /// Tracks an abandoned op until it finishes, preventing later DDC calls from opening fresh
    /// physical-monitor handles against the same panel while the timed-out dxva2 call is still unwinding.
    /// </summary>
    private void TrackAbandoned<T>(string monitorKey, Task<DDCCallOutcome<T>> task, string opLabel)
    {
        lock (_abandonedOpsGate)
            _abandonedOpsByMonitor[monitorKey] = task;

        _ = task.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                WPFLog.Log(
                    $"DisplayService: abandoned op '{opLabel}' faulted post-abandonment: "
                    + $"{t.Exception?.GetBaseException().Message}");
            }
            else
            {
                WPFLog.Log(
                    $"DisplayService: abandoned op '{opLabel}' completed post-abandonment (handles released).");
            }

            lock (_abandonedOpsGate)
            {
                if (_abandonedOpsByMonitor.TryGetValue(monitorKey, out Task? active)
                    && ReferenceEquals(active, task))
                    _abandonedOpsByMonitor.Remove(monitorKey);
            }
        }, TaskScheduler.Default);
    }

    private bool TryGetActiveAbandonedOp(string monitorKey, out Task? task)
    {
        lock (_abandonedOpsGate)
        {
            if (!_abandonedOpsByMonitor.TryGetValue(monitorKey, out task))
                return false;

            if (!task.IsCompleted) return true;

            _abandonedOpsByMonitor.Remove(monitorKey);
            task = null;
            return false;
        }
    }

    private static string DDCGateKey(DDCMonitor monitor)
    {
        if (!string.IsNullOrWhiteSpace(monitor.DeviceID)) return monitor.DeviceID;
        if (!string.IsNullOrWhiteSpace(monitor.EDIDSerial)) return $"edid:{monitor.EDIDSerial}";
        return string.IsNullOrWhiteSpace(monitor.Name) ? "<unknown-monitor>" : monitor.Name;
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
