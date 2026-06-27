using System.Diagnostics;
using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Services;
using BrightnessTrayAppDotNET.Utils;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class MonitorRecoveryTests
{
    [Fact]
    public async Task TargetedRecoveryMatchesPortFormWhenDisplayNumberDriftsAndSerialIsMissing()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-A", displayNumber: 3, serial: string.Empty));
        display.SetRead("DISPLAY\\PORT-A", ok: true, current: 40, max: 100);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.DisplayNumber);
        await WaitUntil(() => service.Monitors.Count == 1 && service.Monitors[0].IsHardwareFunctional);

        MonitorInfo monitor = service.Monitors[0];
        string originalID = monitor.ID;
        Assert.Equal("num:3", originalID);
        Assert.Equal("port:DISPLAY\\PORT-A", monitor.EDIDKey);

        display.SetRead("DISPLAY\\PORT-A", ok: false, error: "simulated read failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        Assert.Contains(originalID, service.GetStuckRecoveryCandidateIDs());

        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-A", displayNumber: 7, serial: string.Empty));
        display.SetRead("DISPLAY\\PORT-A", ok: true, current: 55, max: 100);

        bool recovered = service.TryRecoverMonitor(originalID, DDCRecoveryAction.RefreshHandle);

        Assert.True(recovered);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.False(monitor.IsReadDegraded);
        Assert.Null(monitor.LastDDCError);
        Assert.Equal(originalID, monitor.ID);
        Assert.Equal(7, monitor.DisplayNumber);
        Assert.Equal(55, monitor.RoundedBrightness);
    }

    [Fact]
    public async Task TargetedRecoveryRekeysPortFallbackRowWhenEDIDAppears()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-B", displayNumber: 1, serial: string.Empty));
        display.SetRead("DISPLAY\\PORT-B", ok: true, current: 35, max: 100);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.EDIDSerial);
        await WaitUntil(() => service.Monitors.Count == 1 && service.Monitors[0].IsHardwareFunctional);

        MonitorInfo monitor = service.Monitors[0];
        string portFallbackID = monitor.ID;
        Assert.Equal("port:DISPLAY\\PORT-B", portFallbackID);
        Assert.Equal("port:DISPLAY\\PORT-B", monitor.EDIDKey);

        display.SetRead("DISPLAY\\PORT-B", ok: false, error: "simulated read failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-B", displayNumber: 1, serial: "SERIAL-B"));
        display.SetRead("DISPLAY\\PORT-B", ok: true, current: 70, max: 100);

        bool recovered = service.TryRecoverMonitor(portFallbackID, DDCRecoveryAction.RefreshHandle);

        Assert.True(recovered);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.Equal("edid:SERIAL-B", monitor.ID);
        Assert.Equal("edid:SERIAL-B", monitor.EDIDKey);
        Assert.Equal("SERIAL-B", monitor.EDIDSerial);
        Assert.Null(monitor.LastDDCError);
    }

    [Fact]
    public async Task ReadDegradedMonitorStaysCandidateAndFullyPromotesWhenReadsReturn()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-C", displayNumber: 2, serial: "SERIAL-C"));
        display.SetRead("DISPLAY\\PORT-C", ok: true, current: 60, max: 100);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.EDIDSerial);
        await WaitUntil(() => service.Monitors.Count == 1 && service.Monitors[0].IsHardwareFunctional);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 42;

        display.SetRead("DISPLAY\\PORT-C", ok: false, error: "reads wedged");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.WriteSucceeds = true;
        bool readDegradedResult = service.TryRecoverMonitor(monitor.ID, DDCRecoveryAction.RefreshHandle);

        Assert.False(readDegradedResult);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.True(monitor.IsReadDegraded);
        Assert.Equal("reads wedged", monitor.LastDDCError);
        Assert.Contains(monitor.ID, service.GetStuckRecoveryCandidateIDs());

        display.SetRead("DISPLAY\\PORT-C", ok: true, current: 44, max: 100);
        bool recovered = false;
        await WaitUntil(() => recovered = service.TryRecoverMonitor(monitor.ID, DDCRecoveryAction.RefreshHandle));

        Assert.True(recovered);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.False(monitor.IsReadDegraded);
        Assert.Null(monitor.LastDDCError);
        Assert.DoesNotContain(monitor.ID, service.GetStuckRecoveryCandidateIDs());
        Assert.Equal(42, monitor.RoundedBrightness);
    }

    private static MonitorService CreateService(FakeDisplayService display, MonitorIdentityStrategy strategy)
    {
        AppSettings settings = new()
        {
            MonitorIdentityStrategy = strategy,
            ValidationAttempts = 1,
            ValidationDwellMs = 0,
            BrightnessUpdateRateMs = 0,
            DDCOperationTimeoutMs = 0,
        };

        string storePath = Path.Combine(
            Path.GetTempPath(),
            "BrightnessTrayAppDotNET.Tests",
            $"{Guid.NewGuid():N}.displays.json");

        KnownDisplaysStore store = new(storePath);
        return new MonitorService(display, settings, store, new InlineMonitorServiceDispatcher());
    }

    private static DDCMonitor CreateMonitor(
        string deviceID,
        int displayNumber,
        string serial,
        string name = @"\\.\DISPLAY1")
    {
        return new DDCMonitor
        {
            Handle = (IntPtr)displayNumber,
            HDC = (IntPtr)(displayNumber + 100),
            Name = name,
            DeviceID = deviceID,
            DisplayNumber = displayNumber,
            EDIDSerial = serial,
            FriendlyName = $"Test Display {displayNumber}",
            EDIDManufacturerID = string.IsNullOrEmpty(serial) ? string.Empty : "TST",
            EDIDProductCode = string.IsNullOrEmpty(serial) ? string.Empty : "0001",
            X = displayNumber * 100,
            Y = 0,
        };
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Condition was not reached before timeout.");

            await Task.Delay(10);
        }
    }

    private sealed class InlineMonitorServiceDispatcher : IMonitorServiceDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public void Invoke(Action action) => action();
        public T Invoke<T>(Func<T> action) => action();
    }

    private sealed class FakeDisplayService : IDisplayService
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, VcpRead> _reads = new(StringComparer.Ordinal);
        private List<DDCMonitor> _monitors = [];

        public bool EnumerationSucceeds { get; set; } = true;
        public bool WriteSucceeds { get; set; } = true;
        public int RefreshHandleCalls { get; private set; }
        public int SetVcpCalls { get; private set; }
        public int OperationTimeoutMs { get; set; }

        public void SetMonitors(params DDCMonitor[] monitors)
        {
            lock (_gate)
                _monitors = monitors.Select(Clone).ToList();
        }

        public void SetRead(string key, bool ok, uint current = 50, uint max = 100, string? error = null)
        {
            lock (_gate)
                _reads[key] = new VcpRead(ok, current, max, error);
        }

        public bool TryGetMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error)
        {
            lock (_gate)
            {
                if (!EnumerationSucceeds)
                {
                    monitors = [];
                    error = "simulated enumeration failure";
                    return false;
                }

                monitors = _monitors.Select(Clone).ToList();
                error = null;
                return true;
            }
        }

        public bool TryGetVCPCapabilities(
            DDCMonitor monitor,
            out IReadOnlyList<VCPCapability> capabilities,
            out string? error,
            CancellationToken ct = default)
        {
            capabilities = [];
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
            lock (_gate)
            {
                VcpRead read = _reads.TryGetValue(KeyFor(monitor), out VcpRead configured)
                    ? configured
                    : new VcpRead(true, 50, 100, null);

                currentValue = read.Current;
                maxValue = read.Max;
                error = read.Error;
                return read.Ok;
            }
        }

        public bool TrySetVCPFeature(
            DDCMonitor monitor,
            byte code,
            uint value,
            out string? error,
            CancellationToken ct = default)
        {
            lock (_gate) SetVcpCalls++;
            error = WriteSucceeds ? null : "simulated write failure";
            return WriteSucceeds;
        }

        public bool RefreshHandle(DDCMonitor monitor)
        {
            lock (_gate)
            {
                RefreshHandleCalls++;
                DDCMonitor? live = _monitors.FirstOrDefault(m =>
                    (!string.IsNullOrEmpty(monitor.DeviceID)
                     && string.Equals(m.DeviceID, monitor.DeviceID, StringComparison.Ordinal))
                    || (!string.IsNullOrEmpty(monitor.EDIDSerial)
                        && string.Equals(m.EDIDSerial, monitor.EDIDSerial, StringComparison.Ordinal))
                    || string.Equals(m.Name, monitor.Name, StringComparison.Ordinal));

                if (live == null) return false;

                CopyInto(live, monitor);
                return true;
            }
        }

        private static string KeyFor(DDCMonitor monitor)
        {
            if (!string.IsNullOrEmpty(monitor.DeviceID)) return monitor.DeviceID;
            if (!string.IsNullOrEmpty(monitor.EDIDSerial)) return monitor.EDIDSerial;
            return monitor.Name;
        }

        private static DDCMonitor Clone(DDCMonitor source)
        {
            DDCMonitor clone = new();
            CopyInto(source, clone);
            return clone;
        }

        private static void CopyInto(DDCMonitor source, DDCMonitor target)
        {
            target.Handle = source.Handle;
            target.HDC = source.HDC;
            target.Name = source.Name;
            target.DeviceID = source.DeviceID;
            target.DisplayNumber = source.DisplayNumber;
            target.EDIDSerial = source.EDIDSerial;
            target.FriendlyName = source.FriendlyName;
            target.EDIDManufacturerID = source.EDIDManufacturerID;
            target.EDIDProductCode = source.EDIDProductCode;
            target.X = source.X;
            target.Y = source.Y;
            target.BrightnessCode = source.BrightnessCode;
            target.ProfileModelName = source.ProfileModelName;
            target.PowerOffCommands = source.PowerOffCommands;
            target.ProfileQuirks = source.ProfileQuirks;
        }

        private readonly record struct VcpRead(bool Ok, uint Current, uint Max, string? Error);
    }
}
