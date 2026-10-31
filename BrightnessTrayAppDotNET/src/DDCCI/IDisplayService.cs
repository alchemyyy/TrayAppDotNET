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
    /// Production clamps zero or negative values to a safe positive timeout. Unbounded inline execution is limited
    /// to the killable helper process.
    /// The production implementation runs timed DDC calls in killable per-monitor helper processes so a timeout
    /// can release that helper's <c>PHYSICAL_MONITOR</c> handles without blocking unrelated monitors.
    /// Settable so <c>MonitorService</c> can flow user settings changes through.
    /// </summary>
    int OperationTimeoutMs { set; }

    /// <summary>
    /// Enumerate all connected monitors reachable via EnumDisplayMonitors.
    /// On failure <paramref name="monitors"/> is empty and <paramref name="error"/> describes why.
    /// </summary>
    bool TryGetMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error);

    /// <summary>
    /// Enumerate connected monitors with the identity and profile data needed for targeted DDC recovery.
    /// This path must not query the Windows brightness WMI provider because recovery can repeat indefinitely
    /// while a known DDC monitor remains unavailable.
    /// </summary>
    bool TryGetDDCRecoveryMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error);

    /// <summary>
    /// Read a single VCP feature value from a monitor.
    /// <paramref name="ct"/> is an optional deadline shared by a sequence of calls.
    /// </summary>
    bool TryGetVCPFeature(
        DDCMonitor monitor, byte code, out uint currentValue, out uint maxValue, out string? error,
        CancellationToken ct = default);

    /// <summary>
    /// Write a new value for a VCP feature on a monitor.
    /// <paramref name="ct"/> is an optional deadline shared by a sequence of calls.
    /// </summary>
    bool TrySetVCPFeature(DDCMonitor monitor, byte code, uint value, out string? error, CancellationToken ct = default);

    /// <summary>
    /// Discards process-local DDC transport state for one monitor.
    /// Production kills and removes the monitor's helper process so the next operation starts with a fresh dxva2
    /// process context. Implementations without persistent transport state may treat this as a no-op.
    /// </summary>
    void ResetDDCTransport(DDCMonitor monitor);

    /// <summary>
    /// Releases the DDC transport of a monitor identity the caller no longer uses. Unlike
    /// <see cref="ResetDDCTransport"/> a command already running on it finishes first. Implementations without
    /// persistent transport state may treat this as a no-op.
    /// </summary>
    void RetireDDCTransport(DDCMonitor monitor);

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
