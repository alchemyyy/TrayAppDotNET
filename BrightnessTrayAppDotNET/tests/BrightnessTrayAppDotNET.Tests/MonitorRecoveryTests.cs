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
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        string originalID = monitor.ID;
        Assert.Equal("num:3", originalID);
        Assert.Equal("port:DISPLAY\\PORT-A", monitor.EDIDKey);
        // Regression: a successful initial probe used to publish the functional row through Monitors.Add before
        // WasEverDDCCapable was set. A second Refresh could demote that half-published row first, making recovery
        // candidate selection nondeterministically return an empty list.
        Assert.True(monitor.WasEverDDCCapable);

        display.SetRead("DISPLAY\\PORT-A", ok: false, error: "simulated read failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        Assert.Contains(originalID, service.GetStuckRecoveryCandidateIDs());

        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-A", displayNumber: 7, serial: string.Empty));
        display.SetRead("DISPLAY\\PORT-A", ok: true, current: 55, max: 100);

        bool recovered = service.TryRecoverMonitor(originalID);

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
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        string portFallbackID = monitor.ID;
        Assert.Equal("port:DISPLAY\\PORT-B", portFallbackID);
        Assert.Equal("port:DISPLAY\\PORT-B", monitor.EDIDKey);

        display.SetRead("DISPLAY\\PORT-B", ok: false, error: "simulated read failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-B", displayNumber: 1, serial: "SERIAL-B"));
        display.SetRead("DISPLAY\\PORT-B", ok: true, current: 70, max: 100);

        bool recovered = service.TryRecoverMonitor(portFallbackID);

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
        display.ConfigureWriteReadBack(applySuccessfulWrites: true);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.EDIDSerial);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 42;
        await WaitUntil(() => display.HasReadValue(42));

        display.ConfigureWriteReadBack(applySuccessfulWrites: false);
        display.SetRead("DISPLAY\\PORT-C", ok: false, error: "reads wedged");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.WriteSucceeds = true;
        int readsBeforeRecovery = display.GetVcpCalls;
        bool readDegradedResult = service.TryRecoverMonitor(monitor.ID);

        Assert.False(readDegradedResult);
        Assert.True(display.GetVcpCalls >= readsBeforeRecovery + 2);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.True(monitor.IsReadDegraded);
        Assert.Equal("reads wedged", monitor.LastDDCError);
        Assert.Contains(monitor.ID, service.GetStuckRecoveryCandidateIDs());

        display.SetRead("DISPLAY\\PORT-C", ok: true, current: 44, max: 100);
        bool recovered = false;
        await WaitUntil(() => recovered = service.TryRecoverMonitor(monitor.ID));

        Assert.True(recovered);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.False(monitor.IsReadDegraded);
        Assert.Null(monitor.LastDDCError);
        Assert.DoesNotContain(monitor.ID, service.GetStuckRecoveryCandidateIDs());
        Assert.Equal(42, monitor.RoundedBrightness);
    }

    [Fact]
    public async Task WriteTransportProbePromotesWhenImmediatePostWriteReadRecovers()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-N", displayNumber: 14, serial: "SERIAL-N"));
        display.SetRead("DISPLAY\\PORT-N", ok: true, current: 60, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.EDIDSerial);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 42;
        await WaitUntil(() => display.HasReadValue(42));

        display.SetRead("DISPLAY\\PORT-N", ok: false, error: "reads wedged");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.SetReadFailuresBeforeSuccess(
            "DISPLAY\\PORT-N",
            failuresBeforeSuccess: 1,
            current: 42,
            max: 100,
            error: "first recovery read fails");
        bool recovered = service.TryRecoverMonitor(monitor.ID);

        Assert.True(recovered);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.False(monitor.IsReadDegraded);
        Assert.Null(monitor.LastDDCError);
    }

    [Fact]
    public async Task ReadDegradedTransportProbeRequiresConfirmedBusValue()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-S", displayNumber: 19, serial: "SERIAL-S"));
        display.SetRead("DISPLAY\\PORT-S", ok: true, current: 60, max: 100);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.EDIDSerial);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        using (service.SuspendHardwareWrites())
            monitor.Brightness = 42;

        display.SetRead("DISPLAY\\PORT-S", ok: false, error: "reads wedged");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);
        int writesBeforeRecovery = display.SetVcpCalls;

        Assert.False(service.TryRecoverMonitor(monitor.ID));
        Assert.Equal(writesBeforeRecovery, display.SetVcpCalls);
        Assert.False(monitor.IsReadDegraded);
    }

    [Fact]
    public async Task WindowsBrightnessMonitorIsControllableButDoesNotExposePowerControl()
    {
        FakeDisplayService display = new();
        DDCMonitor windowsMonitor = CreateMonitor(
            deviceID: "DISPLAY\\INTERNAL",
            displayNumber: 1,
            serial: "PANEL-1");
        windowsMonitor.BrightnessControlKind = MonitorBrightnessControlKind.Windows;
        windowsMonitor.WindowsBrightnessInstanceName = @"DISPLAY\TST0001\INTERNAL_0";
        windowsMonitor.WindowsBrightnessMethodPath = @"\\.\root\wmi:WmiMonitorBrightnessMethods.InstanceName=""DISPLAY\\TST0001\\INTERNAL_0""";

        display.SetMonitors(windowsMonitor);
        display.SetRead("DISPLAY\\INTERNAL", ok: true, current: 45, max: 100);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.EDIDSerial);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        Assert.False(monitor.SupportsPowerControl);
        Assert.Equal(45, monitor.RoundedBrightness);

        await service.SetPowerStateAsync(monitor, false);
        Assert.Equal(0, display.SetVcpCalls);

        monitor.Brightness = 55;
        await WaitUntil(() => display.SetVcpCalls == 1);
    }

    [Fact]
    public async Task PowerOnRefreshReplaysPreviouslyVerifiedManualTarget()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-Q", displayNumber: 17, serial: "SERIAL-Q"));
        display.SetRead("DISPLAY\\PORT-Q", ok: true, current: 40, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 65;
        await WaitUntil(() => display.HasReadValue(65));

        await service.SetPowerStateAsync(monitor, false);
        Assert.False(monitor.IsPoweredOn);
        display.SetRead("DISPLAY\\PORT-Q", ok: true, current: 20, max: 100);

        await service.SetPowerStateAsync(monitor, true);
        Assert.True(monitor.IsPoweredOn);
        await WaitUntil(
            () => display.GetCurrentValue("DISPLAY\\PORT-Q") == 65,
            timeoutMs: 5000);
    }

    [Fact]
    public async Task FailedPowerOffRestoresCanceledBrightnessTarget()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-T", displayNumber: 20, serial: "SERIAL-T"));
        display.SetRead("DISPLAY\\PORT-T", ok: true, current: 40, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 65;
        await WaitUntil(() => display.HasReadValue(65));
        int writesBeforePowerOff = display.SetVcpCalls;

        display.ConfigureWriteFailures(1);
        await service.SetPowerStateAsync(monitor, false);

        Assert.True(monitor.IsPoweredOn);
        await WaitUntil(
            () => display.SetVcpCalls >= writesBeforePowerOff + 2,
            timeoutMs: 3000);
        Assert.Equal((uint)65, display.GetLastSetValueForCode(VCPConstants.Brightness));
    }

    [Fact]
    public async Task ImmediateBrightnessWriteReappliesWhenAcceptedWriteDoesNotLand()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-D", displayNumber: 4, serial: "SERIAL-D"));
        display.SetRead("DISPLAY\\PORT-D", ok: true, current: 50, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true, successfulWritesToDrop: 1);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 3);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        service.EnqueueDirectBrightnessImmediate(service.Monitors[0], 70);

        await WaitUntil(
            () => display.SetVcpCalls >= 2 && display.HasReadValue(70),
            timeoutMs: 3000);
        Assert.Equal((uint)70, display.GetCurrentValue("DISPLAY\\PORT-D"));
    }

    [Fact]
    public async Task CanceledImmediateBrightnessWriteDoesNotSuppressSameLaterTarget()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-E", displayNumber: 5, serial: "SERIAL-E"));
        display.SetRead("DISPLAY\\PORT-E", ok: true, current: 35, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        using ManualResetEventSlim predicateCalled = new();
        service.EnqueueDirectBrightnessImmediate(service.Monitors[0], 64, () =>
        {
            predicateCalled.Set();
            return false;
        });
        Assert.True(predicateCalled.Wait(1000));

        service.EnqueueDirectBrightness(service.Monitors[0], 64);

        await WaitUntil(() => display.HasReadValue(64));
        Assert.Equal((uint)64, display.GetCurrentValue("DISPLAY\\PORT-E"));
    }

    [Fact]
    public async Task NormalBrightnessWriteReappliesWhenAcceptedWriteDoesNotLand()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-F", displayNumber: 6, serial: "SERIAL-F"));
        display.SetRead("DISPLAY\\PORT-F", ok: true, current: 45, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true, successfulWritesToDrop: 1);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 3);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        service.Monitors[0].Brightness = 72;

        await WaitUntil(
            () => display.SetVcpCalls >= 2 && display.HasReadValue(72),
            timeoutMs: 3000);
        Assert.Equal((uint)72, display.GetCurrentValue("DISPLAY\\PORT-F"));
    }

    [Fact]
    public async Task UnappliedTransportSuccessfulWriteDoesNotStampLastBusBrightness()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-K", displayNumber: 11, serial: "SERIAL-K"));
        display.SetRead("DISPLAY\\PORT-K", ok: true, current: 45, max: 100);

        string storePath = Path.Combine(
            Path.GetTempPath(),
            "BrightnessTrayAppDotNET.Tests",
            $"{Guid.NewGuid():N}.displays.json");
        KnownDisplaysStore store = new(storePath);
        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1,
            knownDisplays: store);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        display.ConfigureWriteReadBack(applySuccessfulWrites: true, successfulWritesToDrop: 1);
        monitor.Brightness = 73;

        await WaitUntil(() => monitor.IsFailed);
        KnownDisplayEntry? knownDisplay = store.Find(monitor.EDIDKey);
        Assert.NotNull(knownDisplay);
        Assert.Null(knownDisplay.LastBusBrightness);
    }

    [Fact]
    public async Task AcquisitionRetryHandleRefreshPreservesResolvedBrightnessVCPCode()
    {
        const byte AlternateBrightnessCode = 0x13;
        FakeDisplayService display = new() { ResetBrightnessCodeOnRefresh = true };
        DDCMonitor monitor = CreateMonitor(
            deviceID: "DISPLAY\\PORT-L",
            displayNumber: 12,
            serial: "SERIAL-L");
        monitor.BrightnessCode = AlternateBrightnessCode;
        display.SetMonitors(monitor);
        display.SetRead("DISPLAY\\PORT-L", ok: false, error: "force handle refresh");

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2);
        await WaitUntil(() => service.Monitors is [{ IsFailed: true }]);

        Assert.True(display.RefreshHandleCalls >= 1);
        Assert.True(display.AllGetVCPFeatureCodesAre(AlternateBrightnessCode));
    }

    [Fact]
    public async Task TargetedRecoveryRestoresPerMonitorBrightnessProjectionBeforePublishing()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-O", displayNumber: 15, serial: "SERIAL-O"));
        display.SetRead("DISPLAY\\PORT-O", ok: true, current: 50, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            configureSettings: settings => settings.MonitorOverrides.Add(new MonitorOverrideEntry
            {
                ID = "edid:SERIAL-O",
                MinBrightness = 60
            }));
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        display.SetRead("DISPLAY\\PORT-O", ok: false, error: "force targeted recovery");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.SetRead("DISPLAY\\PORT-O", ok: true, current: 30, max: 100);
        Assert.True(service.TryRecoverMonitor(monitor.ID));

        service.EnqueueDirectBrightness(monitor, 20);
        await WaitUntil(() => display.HasReadValue(60));
        Assert.Equal((uint)60, display.GetCurrentValue("DISPLAY\\PORT-O"));
    }

    [Fact]
    public async Task ReadDegradedTransportProbeUsesLastVerifiedProjectedBusValue()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-R", displayNumber: 18, serial: "SERIAL-R"));
        display.SetRead("DISPLAY\\PORT-R", ok: true, current: 50, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            configureSettings: settings => settings.MonitorOverrides.Add(new MonitorOverrideEntry
            {
                ID = "edid:SERIAL-R",
                MinBrightness = 60
            }));
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 20;
        await WaitUntil(() => display.HasReadValue(60));

        display.ConfigureWriteReadBack(applySuccessfulWrites: false);
        display.SetRead("DISPLAY\\PORT-R", ok: false, error: "reads wedged");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);
        int writesBeforeRecovery = display.SetVcpCalls;

        Assert.False(service.TryRecoverMonitor(monitor.ID));
        Assert.True(display.SetVcpCalls > writesBeforeRecovery);
        Assert.Equal((uint)60, display.GetLastSetValueForCode(VCPConstants.Brightness));
    }

    [Fact]
    public async Task TopologyRefreshReplaysPreviouslyVerifiedManualTarget()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-G", displayNumber: 7, serial: "SERIAL-G"));
        display.SetRead("DISPLAY\\PORT-G", ok: true, current: 40, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 65;
        await WaitUntil(() => display.HasReadValue(65));
        int writesBeforeReset = display.SetVcpCalls;

        // Simulate a GPU/topology reset that changes hardware without changing the slider target.
        display.SetRead("DISPLAY\\PORT-G", ok: true, current: 20, max: 100);
        service.NotifyTopologyEvent();
        service.Refresh();

        await WaitUntil(
            () => display.SetVcpCalls > writesBeforeReset
                  && display.GetCurrentValue("DISPLAY\\PORT-G") == 65,
            timeoutMs: 5000);
    }

    [Fact]
    public async Task PublicRefreshDoesNotRunEnumerationOnCallerThread()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-P", displayNumber: 16, serial: "SERIAL-P"));
        display.SetRead("DISPLAY\\PORT-P", ok: true, current: 50, max: 100);

        using ManualResetEventSlim enumerationEntered = new();
        using ManualResetEventSlim releaseEnumeration = new();
        using MonitorService service = CreateService(display, MonitorIdentityStrategy.EDIDSerial);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        display.EnumerationEnteredSignal = enumerationEntered;
        display.EnumerationReleaseSignal = releaseEnumeration;
        int enumerationsBeforeRefresh = display.EnumerationCalls;

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            service.Refresh();
            stopwatch.Stop();

            Assert.True(
                stopwatch.ElapsedMilliseconds < 250,
                $"Refresh blocked its caller for {stopwatch.ElapsedMilliseconds} ms.");
            Assert.True(enumerationEntered.Wait(1000));
        }
        finally
        {
            releaseEnumeration.Set();
        }

        await WaitUntil(() => display.EnumerationCalls > enumerationsBeforeRefresh);
    }

    [Fact]
    public async Task TopologyReplayUsesFreshBrightnessMaximum()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-M", displayNumber: 13, serial: "SERIAL-M"));
        display.SetRead("DISPLAY\\PORT-M", ok: true, current: 128, max: 255);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 80;
        await WaitUntil(() => display.GetCurrentValue("DISPLAY\\PORT-M") == 204);
        int writesBeforeReset = display.SetVcpCalls;

        display.SetRead("DISPLAY\\PORT-M", ok: true, current: 20, max: 200);
        service.NotifyTopologyEvent();
        service.Refresh();

        await WaitUntil(
            () => display.SetVcpCalls > writesBeforeReset
                  && display.GetCurrentValue("DISPLAY\\PORT-M") == 160,
            timeoutMs: 5000);
    }

    [Fact]
    public async Task TopologyRefreshInvalidatesVerifiedCurveTargetForReevaluation()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-I", displayNumber: 9, serial: "SERIAL-I"));
        display.SetRead("DISPLAY\\PORT-I", ok: true, current: 40, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2,
            brightnessCurveEnabled: true);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        const int curveTarget = 75;
        service.EnqueueDirectBrightness(monitor, curveTarget);
        await WaitUntil(() => display.HasReadValue(curveTarget));
        int writesBeforeReset = display.SetVcpCalls;

        // EnvironmentalCurveService reevaluates on MonitorsRefreshed. Model that subscription directly so this
        // regression test remains focused on MonitorService's acknowledgement invalidation boundary.
        service.MonitorsRefreshed += ReapplyCurveTarget;
        display.SetRead("DISPLAY\\PORT-I", ok: true, current: 20, max: 100);
        service.NotifyTopologyEvent();
        service.Refresh();

        await WaitUntil(
            () => display.SetVcpCalls > writesBeforeReset
                  && display.GetCurrentValue("DISPLAY\\PORT-I") == curveTarget,
            timeoutMs: 5000);
        service.MonitorsRefreshed -= ReapplyCurveTarget;
        return;

        void ReapplyCurveTarget() => service.EnqueueDirectBrightness(monitor, curveTarget);
    }

    [Fact]
    public async Task ReadDegradedBrightnessWriteStillAttemptsReadBack()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-H", displayNumber: 8, serial: "SERIAL-H"));
        display.SetRead("DISPLAY\\PORT-H", ok: true, current: 60, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 42;
        await WaitUntil(() => display.HasReadValue(42));

        display.ConfigureWriteReadBack(applySuccessfulWrites: false);
        display.SetRead("DISPLAY\\PORT-H", ok: false, error: "reads wedged");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);
        Assert.False(service.TryRecoverMonitor(monitor.ID));
        Assert.True(monitor.IsReadDegraded);

        int readsBeforeWrite = display.GetVcpCalls;
        monitor.Brightness = 43;

        await WaitUntil(() => display.GetVcpCalls > readsBeforeWrite);
    }

    [Fact]
    public async Task NewerBrightnessTargetPreemptsFinalValidationDwell()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-J", displayNumber: 10, serial: "SERIAL-J"));
        display.SetRead("DISPLAY\\PORT-J", ok: true, current: 50, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true, successfulWritesToDrop: 10);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 4,
            validationDwellMs: 1000);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 30;
        await WaitUntil(() => display.SetVcpCalls >= 4);

        display.ConfigureWriteReadBack(applySuccessfulWrites: true);
        Stopwatch stopwatch = Stopwatch.StartNew();
        monitor.Brightness = 80;

        await WaitUntil(() => display.HasReadValue(80), timeoutMs: 1000);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 750,
            $"New target waited {stopwatch.ElapsedMilliseconds} ms behind obsolete validation dwell.");
    }

    private static MonitorService CreateService(
        FakeDisplayService display,
        MonitorIdentityStrategy strategy,
        int validationAttempts = 1,
        bool brightnessCurveEnabled = false,
        int validationDwellMs = 0,
        KnownDisplaysStore? knownDisplays = null,
        Action<AppSettings>? configureSettings = null)
    {
        AppSettings settings = new()
        {
            MonitorIdentityStrategy = strategy,
            ValidationAttempts = validationAttempts,
            ValidationDwellMs = validationDwellMs,
            BrightnessUpdateRateMs = 0,
            DDCOperationTimeoutMs = 0,
            EnvironmentalBrightnessCurveEnabled = brightnessCurveEnabled
        };
        configureSettings?.Invoke(settings);

        KnownDisplaysStore store = knownDisplays ?? new KnownDisplaysStore(Path.Combine(
            Path.GetTempPath(),
            "BrightnessTrayAppDotNET.Tests",
            $"{Guid.NewGuid():N}.displays.json"));
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
            Y = 0
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
        private readonly Dictionary<string, int> _readFailuresRemaining = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _readFailureErrors = new(StringComparer.Ordinal);
        private readonly List<uint> _readValues = [];
        private readonly List<byte> _getVCPFeatureCodes = [];
        private readonly List<(byte Code, uint Value)> _setVCPFeatureCalls = [];
        private List<DDCMonitor> _monitors = [];
        private int _refreshHandleCalls;
        private int _enumerationCalls;
        private int _setVcpCalls;
        private int _getVcpCalls;
        private int _writesToFail;
        private int _successfulWritesToDrop;
        private bool _applySuccessfulWritesToReadBack;

        public bool EnumerationSucceeds { get; set; } = true;
        public bool WriteSucceeds { get; set; } = true;
        public bool ResetBrightnessCodeOnRefresh { get; set; }
        public ManualResetEventSlim? EnumerationEnteredSignal { get; set; }
        public ManualResetEventSlim? EnumerationReleaseSignal { get; set; }
        public int RefreshHandleCalls => Volatile.Read(ref _refreshHandleCalls);
        public int EnumerationCalls => Volatile.Read(ref _enumerationCalls);
        public int SetVcpCalls => Volatile.Read(ref _setVcpCalls);
        public int GetVcpCalls => Volatile.Read(ref _getVcpCalls);
        public int OperationTimeoutMs { get; set; }

        public void ConfigureWriteReadBack(bool applySuccessfulWrites, int successfulWritesToDrop = 0)
        {
            lock (_gate)
            {
                _applySuccessfulWritesToReadBack = applySuccessfulWrites;
                _successfulWritesToDrop = Math.Max(0, successfulWritesToDrop);
            }
        }

        public void ConfigureWriteFailures(int writesToFail)
        {
            lock (_gate)
                _writesToFail = Math.Max(0, writesToFail);
        }

        public bool HasReadValue(uint value)
        {
            lock (_gate)
                return _readValues.Contains(value);
        }

        public uint GetCurrentValue(string key)
        {
            lock (_gate)
                return _reads.TryGetValue(key, out VcpRead read) ? read.Current : 0;
        }

        public bool AllGetVCPFeatureCodesAre(byte code)
        {
            lock (_gate)
                return _getVCPFeatureCodes.Count > 0 && _getVCPFeatureCodes.All(readCode => readCode == code);
        }

        public uint GetLastSetValueForCode(byte code)
        {
            lock (_gate)
                return _setVCPFeatureCalls.Last(call => call.Code == code).Value;
        }

        public void SetMonitors(params DDCMonitor[] monitors)
        {
            lock (_gate)
                _monitors = monitors.Select(Clone).ToList();
        }

        public void SetRead(string key, bool ok, uint current = 50, uint max = 100, string? error = null)
        {
            lock (_gate)
            {
                _reads[key] = new VcpRead(ok, current, max, error);
                _readFailuresRemaining.Remove(key);
                _readFailureErrors.Remove(key);
            }
        }

        public void SetReadFailuresBeforeSuccess(
            string key,
            int failuresBeforeSuccess,
            uint current,
            uint max,
            string? error)
        {
            lock (_gate)
            {
                _reads[key] = new VcpRead(true, current, max, null);
                _readFailuresRemaining[key] = Math.Max(0, failuresBeforeSuccess);
                _readFailureErrors[key] = error;
            }
        }

        public bool TryGetMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error)
        {
            EnumerationEnteredSignal?.Set();
            EnumerationReleaseSignal?.Wait();

            lock (_gate)
            {
                if (!EnumerationSucceeds)
                {
                    monitors = [];
                    error = "simulated enumeration failure";
                    Interlocked.Increment(ref _enumerationCalls);
                    return false;
                }

                monitors = _monitors.Select(Clone).ToList();
                error = null;
                Interlocked.Increment(ref _enumerationCalls);
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
                _getVcpCalls++;
                _getVCPFeatureCodes.Add(code);
                string key = KeyFor(monitor);
                VcpRead read = _reads.TryGetValue(key, out VcpRead configured)
                    ? configured
                    : new VcpRead(true, 50, 100, null);

                if (_readFailuresRemaining.TryGetValue(key, out int failuresRemaining)
                    && failuresRemaining > 0)
                {
                    _readFailuresRemaining[key] = failuresRemaining - 1;
                    currentValue = read.Current;
                    maxValue = read.Max;
                    error = _readFailureErrors.TryGetValue(key, out string? failureError)
                        ? failureError
                        : "simulated sequenced read failure";
                    return false;
                }

                currentValue = read.Current;
                maxValue = read.Max;
                error = read.Error;
                if (read.Ok) _readValues.Add(read.Current);
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
            lock (_gate)
            {
                _setVcpCalls++;
                _setVCPFeatureCalls.Add((code, value));
                if (_writesToFail > 0)
                {
                    _writesToFail--;
                    error = "simulated write failure";
                    return false;
                }

                if (!WriteSucceeds)
                {
                    error = "simulated write failure";
                    return false;
                }

                if (_applySuccessfulWritesToReadBack && code == monitor.BrightnessCode)
                {
                    if (_successfulWritesToDrop > 0)
                    {
                        _successfulWritesToDrop--;
                    }
                    else
                    {
                        string key = KeyFor(monitor);
                        uint maximum = _reads.TryGetValue(key, out VcpRead currentRead) && currentRead.Max > 0
                            ? currentRead.Max
                            : 100;
                        _reads[key] = new VcpRead(true, value, maximum, null);
                    }
                }

                error = null;
                return true;
            }
        }

        public bool RefreshHandle(DDCMonitor monitor)
        {
            lock (_gate)
            {
                _refreshHandleCalls++;
                DDCMonitor? live = _monitors.FirstOrDefault(m =>
                    (!string.IsNullOrEmpty(monitor.DeviceID)
                     && string.Equals(m.DeviceID, monitor.DeviceID, StringComparison.Ordinal))
                    || (!string.IsNullOrEmpty(monitor.EDIDSerial)
                        && string.Equals(m.EDIDSerial, monitor.EDIDSerial, StringComparison.Ordinal))
                    || string.Equals(m.Name, monitor.Name, StringComparison.Ordinal));

                if (live == null) return false;

                CopyInto(live, monitor);
                if (ResetBrightnessCodeOnRefresh)
                    monitor.BrightnessCode = VCPConstants.Brightness;
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
            target.DisplayInstancePath = source.DisplayInstancePath;
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
            target.BrightnessControlKind = source.BrightnessControlKind;
            target.WindowsBrightnessInstanceName = source.WindowsBrightnessInstanceName;
            target.WindowsBrightnessMethodPath = source.WindowsBrightnessMethodPath;
        }

        private readonly record struct VcpRead(bool Ok, uint Current, uint Max, string? Error);
    }
}
