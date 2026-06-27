namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// DDC/CI surface: enumerate monitors, query their capability strings,
/// and read/write VCP features on a given monitor.
///
/// Every call follows the Try-pattern: <c>bool</c> return + an <c>out string? error</c> describing the failure.
/// Expected DDC failures (transient I2C errors, monitor not responding, capability string absent, etc.)
/// flow through the <c>error</c> out param rather than throwing.
/// Throws only for genuine programming errors (null arguments, disposed instance)
/// or crash-on-purpose situations unrelated to DDC.
/// </summary>
public interface IDisplayService
{
    /// <summary>
    /// Maximum wall-clock time any single dxva2-backed op (capability fetch, VCP read, VCP write) is allowed to block.
    /// Zero or negative disables the wrapper (calls block forever, matching the unwrapped dxva2 contract).
    /// Implementations run the dxva2 call on a threadpool thread and abandon the wait on timeout:
    /// the abandoned task keeps running long enough to release its <c>PHYSICAL_MONITOR</c> handles,
    /// while the caller sees prompt failure.
    /// Settable so <c>MonitorService</c> can flow user settings changes through.
    /// </summary>
    int OperationTimeoutMs { set; }

    /// <summary>
    /// Enumerate all connected monitors reachable via EnumDisplayMonitors.
    /// On failure <paramref name="monitors"/> is empty and <paramref name="error"/> describes why.
    /// </summary>
    bool TryGetMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error);

    /// <summary>
    /// Parse the capability string and return every supported VCP feature
    /// together with its current and maximum values.
    ///
    /// This is a multi-step DDC sequence: one capability-string fetch followed by N per-VCP reads.
    /// Each individual sub-call honours <see cref="OperationTimeoutMs"/> independently,
    /// but the per-call timeout doesn't make the SEQUENCE atomic.
    /// Callers that need atomicity against concurrent writes from elsewhere in the app
    /// must hold the per-monitor mutex (<c>MonitorService.WithDDCLock</c>) for the whole call,
    /// otherwise an interleaved write can land between the capability fetch and the per-VCP probes
    /// and produce a stale list.
    ///
    /// Pass a linked <paramref name="ct"/> to give the entire sequence one shared deadline
    /// rather than relying on per-op timeouts for sequence-length-dependent calls.
    /// </summary>
    bool TryGetVCPCapabilities(
        DDCMonitor monitor, out IReadOnlyList<VCPCapability> capabilities, out string? error,
        CancellationToken ct = default);

    /// <summary>
    /// Read a single VCP feature value from a monitor.
    /// <paramref name="ct"/> is the optional sequence-level deadline (see <see cref="TryGetVCPCapabilities"/>).
    /// </summary>
    bool TryGetVCPFeature(
        DDCMonitor monitor, byte code, out uint currentValue, out uint maxValue, out string? error,
        CancellationToken ct = default);

    /// <summary>
    /// Write a new value for a VCP feature on a monitor.
    /// <paramref name="ct"/> is the optional sequence-level deadline (see <see cref="TryGetVCPCapabilities"/>).
    /// </summary>
    bool TrySetVCPFeature(DDCMonitor monitor, byte code, uint value, out string? error, CancellationToken ct = default);

    /// <summary>
    /// Re-acquires the HMONITOR / HDC for an existing <see cref="DDCMonitor"/> by re-enumerating
    /// and matching stable identity first: <see cref="DDCMonitor.DeviceID"/>, then EDID identity,
    /// then adapter <see cref="DDCMonitor.Name"/> as a logged fallback.
    /// Use as a soft recovery step when DDC/CI calls persistently fail on an otherwise-present monitor
    /// (the handle may have gone stale after a sleep cycle or GPU driver hiccup).
    /// The DDC/CI spec has no "reset the bus" primitive, but a freshly-enumerated handle often unsticks a monitor
    /// whose prior handle is silently black-holing VCP traffic.
    /// Returns true iff a matching monitor was found and its fields updated;
    /// false means the monitor is no longer enumerable (physically removed).
    /// </summary>
    bool RefreshHandle(DDCMonitor monitor);
}
