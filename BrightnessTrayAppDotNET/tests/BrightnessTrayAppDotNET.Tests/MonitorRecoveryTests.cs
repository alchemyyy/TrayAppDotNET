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
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-A", displayNumber: 3, string.Empty));
        display.SetRead(key: "DISPLAY\\PORT-A", ok: true, current: 40, max: 100);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.DisplayNumber);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        string originalID = monitor.ID;
        Assert.Equal(expected: "num:3", originalID);
        Assert.Equal(expected: "port:DISPLAY\\PORT-A", monitor.EDIDKey);
        // Regression: a successful initial probe used to publish the functional row through Monitors.Add before
        // WasEverDDCCapable was set. A second Refresh could demote that half-published row first, making recovery
        // candidate selection nondeterministically return an empty list.
        Assert.True(monitor.WasEverDDCCapable);

        display.SetRead(key: "DISPLAY\\PORT-A", ok: false, error: "simulated read failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        Assert.Contains(originalID, service.GetStuckRecoveryCandidateIDs());

        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-A", displayNumber: 7, string.Empty));
        display.SetRead(key: "DISPLAY\\PORT-A", ok: true, current: 55, max: 100);

        int fullEnumerationsBeforeRecovery = display.FullEnumerationCalls;
        int DDCRecoveryEnumerationsBeforeRecovery = display.DDCRecoveryEnumerationCalls;
        bool recovered = service.TryRecoverMonitor(originalID);

        Assert.True(recovered);
        Assert.Equal(fullEnumerationsBeforeRecovery, display.FullEnumerationCalls);
        Assert.Equal(DDCRecoveryEnumerationsBeforeRecovery + 1, display.DDCRecoveryEnumerationCalls);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.False(monitor.IsReadDegraded);
        Assert.Null(monitor.LastDDCError);
        Assert.Equal(originalID, monitor.ID);
        Assert.Equal(expected: 7, monitor.DisplayNumber);
        Assert.Equal(expected: 55, monitor.RoundedBrightness);
    }

    [Fact]
    public async Task TargetedRecoveryRekeysPortFallbackRowWhenEDIDAppears()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-B", displayNumber: 1, string.Empty));
        display.SetRead(key: "DISPLAY\\PORT-B", ok: true, current: 35, max: 100);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.EDIDSerial);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        string portFallbackID = monitor.ID;
        Assert.Equal(expected: "port:DISPLAY\\PORT-B", portFallbackID);
        Assert.Equal(expected: "port:DISPLAY\\PORT-B", monitor.EDIDKey);

        display.SetRead(key: "DISPLAY\\PORT-B", ok: false, error: "simulated read failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-B", displayNumber: 1, serial: "SERIAL-B"));
        display.SetRead(key: "DISPLAY\\PORT-B", ok: true, current: 70, max: 100);

        bool recovered = service.TryRecoverMonitor(portFallbackID);

        Assert.True(recovered);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.Equal(expected: "edid:SERIAL-B", monitor.ID);
        Assert.Equal(expected: "edid:SERIAL-B", monitor.EDIDKey);
        Assert.Equal(expected: "SERIAL-B", monitor.EDIDSerial);
        Assert.Null(monitor.LastDDCError);
    }

    [Fact]
    public async Task ReadDegradedMonitorStaysCandidateAndFullyPromotesWhenReadsReturn()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-C", displayNumber: 2, serial: "SERIAL-C"));
        display.SetRead(key: "DISPLAY\\PORT-C", ok: true, current: 60, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.EDIDSerial);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 42;
        await WaitUntil(() => display.HasReadValue(42));

        display.ConfigureWriteReadBack(false);
        display.SetRead(key: "DISPLAY\\PORT-C", ok: false, error: "reads wedged");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.WriteSucceeds = true;
        int readsBeforeRecovery = display.GetVcpCalls;
        bool readDegradedResult = service.TryRecoverMonitor(monitor.ID);

        Assert.False(readDegradedResult);
        Assert.True(display.GetVcpCalls >= readsBeforeRecovery + 2);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.True(monitor.IsReadDegraded);
        Assert.Equal(expected: "reads wedged", monitor.LastDDCError);
        Assert.Contains(monitor.ID, service.GetStuckRecoveryCandidateIDs());

        display.SetRead(key: "DISPLAY\\PORT-C", ok: true, current: 44, max: 100);
        bool recovered = false;
        await WaitUntil(() => recovered = service.TryRecoverMonitor(monitor.ID));

        Assert.True(recovered);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.False(monitor.IsReadDegraded);
        Assert.Null(monitor.LastDDCError);
        Assert.DoesNotContain(monitor.ID, service.GetStuckRecoveryCandidateIDs());
        Assert.Equal(expected: 42, monitor.RoundedBrightness);
    }

    [Fact]
    public async Task WriteTransportProbePromotesWhenImmediatePostWriteReadRecovers()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-N", displayNumber: 14, serial: "SERIAL-N"));
        display.SetRead(key: "DISPLAY\\PORT-N", ok: true, current: 60, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.EDIDSerial);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 42;
        await WaitUntil(() => display.HasReadValue(42));

        display.SetRead(key: "DISPLAY\\PORT-N", ok: false, error: "reads wedged");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.SetReadFailuresBeforeSuccess(
            key: "DISPLAY\\PORT-N",
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
    public async Task ChecksumRecoveryWriteResynchronizesCurveControlledMonitor()
    {
        const string DeviceID = "DISPLAY\\HDMI-CHECKSUM";
        const int CurveTarget = 42;
        string storePath = Path.Combine(
            Path.GetTempPath(),
            path2: "BrightnessTrayAppDotNET.Tests",
            $"{Guid.NewGuid():N}.displays.json");
        KnownDisplaysStore store = new(storePath);
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 35, serial: "HDMI-CHECKSUM"));
        display.SetRead(DeviceID, ok: true, current: 60, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1,
            brightnessCurveEnabled: true,
            knownDisplays: store);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.CurveTargetBrightness = CurveTarget;
        service.EnqueueDirectBrightness(monitor, CurveTarget);
        await WaitUntil(() => store.Find(monitor.EDIDKey)?.LastBusBrightness == CurveTarget);

        display.ConfigureWriteReadBack(false);
        display.SetRead(
            DeviceID,
            ok: false,
            error: "GetVCPFeatureAndVCPFeatureReply failed (Win32: -1071241845, 0xC026258B)");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);
        int writesBeforeRecovery = display.SetVcpCalls;

        // LG firmware can clear the corrupted reply queue after accepting a same-value brightness SET.
        display.ConfigureWriteReadBack(true);
        bool recovered = service.TryRecoverMonitor(monitor.ID);

        Assert.True(recovered);
        Assert.True(display.SetVcpCalls > writesBeforeRecovery);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.False(monitor.IsReadDegraded);
        Assert.Equal(SliderState.CurveActive, monitor.SliderState);
        Assert.Equal((uint)CurveTarget, display.GetLastSetValueForCode(VCPConstants.Brightness));
        Assert.Null(monitor.LastDDCError);
    }

    [Fact]
    public async Task GenericCurveReadFailureDoesNotSendRecoveryWriteWhenBlindWritesAreDisabled()
    {
        const string DeviceID = "DISPLAY\\HDMI-GENERIC-FAILURE";
        string storePath = Path.Combine(
            Path.GetTempPath(),
            path2: "BrightnessTrayAppDotNET.Tests",
            $"{Guid.NewGuid():N}.displays.json");
        KnownDisplaysStore store = new(storePath);
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(
            DeviceID,
            displayNumber: 36,
            serial: "HDMI-GENERIC-FAILURE"));
        display.SetRead(DeviceID, ok: true, current: 60, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1,
            brightnessCurveEnabled: true,
            knownDisplays: store,
            configureSettings: settings => settings.AllowBlindDDCWritesDuringDegradedState = false);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.CurveTargetBrightness = 42;
        service.EnqueueDirectBrightness(monitor, percent: 42);
        await WaitUntil(() => store.Find(monitor.EDIDKey)?.LastBusBrightness == 42);

        display.SetRead(DeviceID, ok: false, error: "generic read failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);
        int writesBeforeRecovery = display.SetVcpCalls;

        Assert.False(service.TryRecoverMonitor(monitor.ID));
        Assert.Equal(writesBeforeRecovery, display.SetVcpCalls);
    }

    [Fact]
    public async Task BlindWriteOptionPromotesGenericCurveFailureWithoutConfirmedBusValue()
    {
        const string DeviceID = "DISPLAY\\HDMI-BLIND-RECOVERY";
        const int CurveTarget = 37;
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(
            DeviceID,
            displayNumber: 37,
            serial: "HDMI-BLIND-RECOVERY"));
        display.SetRead(DeviceID, ok: true, current: 60, max: 100);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1,
            brightnessCurveEnabled: true,
            configureSettings: settings => settings.AllowBlindDDCWritesDuringDegradedState = true);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.CurveTargetBrightness = CurveTarget;
        display.ConfigureWriteReadBack(false);
        display.SetRead(DeviceID, ok: false, error: "generic read failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);
        int writesBeforeRecovery = display.SetVcpCalls;

        bool recovered = service.TryRecoverMonitor(monitor.ID);

        Assert.False(recovered);
        Assert.True(display.SetVcpCalls > writesBeforeRecovery);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.True(monitor.IsReadDegraded);
        Assert.Equal((uint)CurveTarget, display.GetLastSetValueForCode(VCPConstants.Brightness));
    }

    [Fact]
    public async Task DisabledBlindWritesRejectBrightnessChangesWhileReadDegraded()
    {
        const string DeviceID = "DISPLAY\\HDMI-BLIND-DISABLED";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(
            DeviceID,
            displayNumber: 38,
            serial: "HDMI-BLIND-DISABLED"));
        display.SetRead(DeviceID, ok: true, current: 60, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1,
            configureSettings: settings => settings.AllowBlindDDCWritesDuringDegradedState = false);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 42;
        await WaitUntil(() => display.HasReadValue(42));

        display.ConfigureWriteReadBack(false);
        display.SetRead(DeviceID, ok: false, error: "reads wedged");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);
        Assert.False(service.TryRecoverMonitor(monitor.ID));
        Assert.True(monitor.IsReadDegraded);
        int writesBeforeChange = display.SetVcpCalls;

        monitor.Brightness = 43;
        await Task.Delay(100);

        Assert.Equal(writesBeforeChange, display.SetVcpCalls);
    }

    [Fact]
    public async Task ReadDegradedTransportProbeRequiresConfirmedBusValue()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-S", displayNumber: 19, serial: "SERIAL-S"));
        display.SetRead(key: "DISPLAY\\PORT-S", ok: true, current: 60, max: 100);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            configureSettings: settings => settings.AllowBlindDDCWritesDuringDegradedState = false);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        using (service.SuspendHardwareWrites())
            monitor.Brightness = 42;

        display.SetRead(key: "DISPLAY\\PORT-S", ok: false, error: "reads wedged");
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
        windowsMonitor.WindowsBrightnessMethodPath =
            """
            \\.\root\wmi:WmiMonitorBrightnessMethods.InstanceName="DISPLAY\\TST0001\\INTERNAL_0"
            """;

        display.SetMonitors(windowsMonitor);
        display.SetRead(key: "DISPLAY\\INTERNAL", ok: true, current: 45, max: 100);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.EDIDSerial);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        Assert.False(monitor.SupportsPowerControl);
        Assert.Equal(expected: 45, monitor.RoundedBrightness);

        await service.SetPowerStateAsync(monitor, on: false);
        Assert.Equal(expected: 0, display.SetVcpCalls);

        monitor.Brightness = 55;
        await WaitUntil(() => display.SetVcpCalls == 1);
    }

    [Fact]
    public async Task WindowsBrightnessMonitorKeepsCanonicalIdentityAfterWake()
    {
        const string canonicalDeviceID = @"DISPLAY\AUOD298\4&13BEE726&0&UID8388688";

        FakeDisplayService display = new();
        DDCMonitor beforeSleep = CreateMonitor(
            canonicalDeviceID,
            displayNumber: 1,
            string.Empty);
        beforeSleep.BrightnessControlKind = MonitorBrightnessControlKind.Windows;
        beforeSleep.DisplayInstancePath = canonicalDeviceID;
        beforeSleep.WindowsBrightnessInstanceName = canonicalDeviceID + "_0";
        beforeSleep.WindowsBrightnessMethodPath =
            """
            \\.\root\wmi:WmiMonitorBrightnessMethods.InstanceName="DISPLAY\\AUOD298\\INTERNAL_0"
            """;

        display.SetMonitors(beforeSleep);
        display.SetRead(canonicalDeviceID, ok: true, current: 40, max: 100);

        using MonitorService service = CreateService(display, MonitorIdentityStrategy.HardwarePort);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        Assert.Equal($"port:{canonicalDeviceID}", monitor.ID);
        Assert.Equal($"port:{canonicalDeviceID}", monitor.EDIDKey);

        DDCMonitor afterWake = CreateMonitor(
            canonicalDeviceID,
            displayNumber: 7,
            string.Empty,
            name: @"\\.\DISPLAY7");
        afterWake.BrightnessControlKind = MonitorBrightnessControlKind.Windows;
        afterWake.DisplayInstancePath = canonicalDeviceID;
        afterWake.WindowsBrightnessInstanceName = canonicalDeviceID + "_0";
        afterWake.WindowsBrightnessMethodPath = beforeSleep.WindowsBrightnessMethodPath;

        display.SetMonitors(afterWake);
        display.SetRead(canonicalDeviceID, ok: true, current: 65, max: 100);

        service.Refresh();
        await WaitUntil(() =>
            service.Monitors.Count == 1
            && ReferenceEquals(service.Monitors[0], monitor)
            && monitor is { ID: $"port:{canonicalDeviceID}", DisplayNumber: 7 }
            && display.LastReadKey == canonicalDeviceID);

        Assert.Same(monitor, service.Monitors[0]);
        Assert.Equal($"port:{canonicalDeviceID}", monitor.EDIDKey);
        Assert.Equal(expected: 7, monitor.DisplayNumber);
        Assert.Equal(canonicalDeviceID, display.LastReadKey);
        Assert.True(monitor.IsHardwareFunctional);
        Assert.False(monitor.SupportsPowerControl);
    }

    [Fact]
    public async Task PowerOffReappliesWhenAcceptedWriteStillReadsAsPoweredOn()
    {
        const string DeviceID = "DISPLAY\\HDMI-POWER-RETRY";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 35, serial: "HDMI-POWER-RETRY"));
        display.SetRead(DeviceID, ok: true, current: 40, max: 100);
        display.SetFeatureRead(DeviceID, VCPConstants.PowerMode, ok: true, current: 1, max: 5);
        display.ConfigureFeatureWriteReadBack(
            VCPConstants.PowerMode,
            applySuccessfulWrites: true,
            successfulWritesToDrop: 1);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 4,
            configureSettings: settings => settings.PowerOffMode = PowerOffMode.Hard);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        await service.SetPowerStateAsync(monitor, on: false);

        Assert.False(monitor.IsPoweredOn);
        Assert.True(monitor.SuppressDDCRecoveryForPowerIntent);
        Assert.Equal(expected: 2, display.GetSetVCPFeatureCallCount(VCPConstants.PowerMode));
        Assert.Equal((uint)5, display.GetLastSetValueForCode(VCPConstants.PowerMode));
    }

    [Fact]
    public async Task PowerOffKeepsMonitorOnAfterPersistentReadableMismatch()
    {
        const string DeviceID = "DISPLAY\\HDMI-POWER-MISMATCH";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 36, serial: "HDMI-POWER-MISMATCH"));
        display.SetRead(DeviceID, ok: true, current: 40, max: 100);
        display.SetFeatureRead(DeviceID, VCPConstants.PowerMode, ok: true, current: 1, max: 5);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 3,
            configureSettings: settings => settings.PowerOffMode = PowerOffMode.Hard);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        await service.SetPowerStateAsync(monitor, on: false);

        Assert.True(monitor.IsPoweredOn);
        Assert.False(monitor.SuppressDDCRecoveryForPowerIntent);
        Assert.Equal(expected: 3, display.GetSetVCPFeatureCallCount(VCPConstants.PowerMode));
    }

    [Fact]
    public async Task PowerOffAcceptsMissingReadBackAfterTransportAcceptedWrite()
    {
        const string DeviceID = "DISPLAY\\DP-HARD-OFF";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 37, serial: "DP-HARD-OFF"));
        display.SetRead(DeviceID, ok: true, current: 40, max: 100);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 4,
            configureSettings: settings => settings.PowerOffMode = PowerOffMode.Hard);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        await service.SetPowerStateAsync(monitor, on: false);

        Assert.False(monitor.IsPoweredOn);
        Assert.True(monitor.SuppressDDCRecoveryForPowerIntent);
        Assert.Equal(expected: 1, display.GetSetVCPFeatureCallCount(VCPConstants.PowerMode));
    }

    [Fact]
    public async Task NewerPowerIntentPreventsStalePowerOffReapply()
    {
        const string DeviceID = "DISPLAY\\POWER-SUPERSEDE";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 38, serial: "POWER-SUPERSEDE"));
        display.SetRead(DeviceID, ok: true, current: 40, max: 100);
        display.SetFeatureRead(DeviceID, VCPConstants.PowerMode, ok: true, current: 1, max: 5);
        display.ConfigureFeatureWriteReadBack(VCPConstants.PowerMode, applySuccessfulWrites: true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 4,
            validationDwellMs: 500,
            configureSettings: settings => settings.PowerOffMode = PowerOffMode.Hard);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        Task powerOff = service.SetPowerStateAsync(monitor, on: false);
        await WaitUntil(() => display.GetSetVCPFeatureCallCount(VCPConstants.PowerMode) == 1);
        Task powerOn = service.SetPowerStateAsync(monitor, on: true);
        await Task.WhenAll(powerOff, powerOn);

        Assert.True(monitor.IsPoweredOn);
        Assert.False(monitor.SuppressDDCRecoveryForPowerIntent);
        Assert.Equal(expected: 2, display.GetSetVCPFeatureCallCount(VCPConstants.PowerMode));
        Assert.Equal((uint)1, display.GetLastSetValueForCode(VCPConstants.PowerMode));
    }

    [Fact]
    public async Task PowerOnRefreshReplaysPreviouslyVerifiedManualTarget()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-Q", displayNumber: 17, serial: "SERIAL-Q"));
        display.SetRead(key: "DISPLAY\\PORT-Q", ok: true, current: 40, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 65;
        await WaitUntil(() => display.HasReadValue(65));

        await service.SetPowerStateAsync(monitor, on: false);
        Assert.False(monitor.IsPoweredOn);
        Assert.True(monitor.SuppressDDCRecoveryForPowerIntent);
        display.SetRead(key: "DISPLAY\\PORT-Q", ok: true, current: 20, max: 100);

        await service.SetPowerStateAsync(monitor, on: true);
        Assert.True(monitor.IsPoweredOn);
        Assert.False(monitor.SuppressDDCRecoveryForPowerIntent);
        await WaitUntil(
            () => display.GetCurrentValue("DISPLAY\\PORT-Q") == 65,
            timeoutMs: 5000);
    }

    [Fact]
    public async Task FailedPowerOffRestoresCanceledBrightnessTarget()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-T", displayNumber: 20, serial: "SERIAL-T"));
        display.SetRead(key: "DISPLAY\\PORT-T", ok: true, current: 40, max: 100);
        display.ConfigureWriteReadBack(true);

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
        await service.SetPowerStateAsync(monitor, on: false);

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
        display.SetRead(key: "DISPLAY\\PORT-D", ok: true, current: 50, max: 100);
        display.ConfigureWriteReadBack(applySuccessfulWrites: true, successfulWritesToDrop: 1);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 3);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        service.EnqueueDirectBrightnessImmediate(service.Monitors[0], percent: 70);

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
        display.SetRead(key: "DISPLAY\\PORT-E", ok: true, current: 35, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        using ManualResetEventSlim predicateCalled = new();
        service.EnqueueDirectBrightnessImmediate(service.Monitors[0], percent: 64, () =>
        {
            predicateCalled.Set();
            return false;
        });
        Assert.True(predicateCalled.Wait(1000));

        service.EnqueueDirectBrightness(service.Monitors[0], percent: 64);

        await WaitUntil(() => display.HasReadValue(64));
        Assert.Equal((uint)64, display.GetCurrentValue("DISPLAY\\PORT-E"));
    }

    [Fact]
    public async Task NormalBrightnessWriteReappliesWhenAcceptedWriteDoesNotLand()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-F", displayNumber: 6, serial: "SERIAL-F"));
        display.SetRead(key: "DISPLAY\\PORT-F", ok: true, current: 45, max: 100);
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
        display.SetRead(key: "DISPLAY\\PORT-K", ok: true, current: 45, max: 100);

        string storePath = Path.Combine(
            Path.GetTempPath(),
            path2: "BrightnessTrayAppDotNET.Tests",
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
        display.SetRead(key: "DISPLAY\\PORT-L", ok: false, error: "force handle refresh");

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
        display.SetRead(key: "DISPLAY\\PORT-O", ok: true, current: 50, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            configureSettings: settings => settings.MonitorOverrides.Add(new MonitorOverrideEntry
            {
                ID = "edid:SERIAL-O", MinBrightness = 60
            }));
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        display.SetRead(key: "DISPLAY\\PORT-O", ok: false, error: "force targeted recovery");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.SetRead(key: "DISPLAY\\PORT-O", ok: true, current: 30, max: 100);
        Assert.True(service.TryRecoverMonitor(monitor.ID));

        service.EnqueueDirectBrightness(monitor, percent: 20);
        await WaitUntil(() => display.HasReadValue(60));
        Assert.Equal((uint)60, display.GetCurrentValue("DISPLAY\\PORT-O"));
    }

    [Fact]
    public async Task ReadDegradedTransportProbeUsesLastVerifiedProjectedBusValue()
    {
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(deviceID: "DISPLAY\\PORT-R", displayNumber: 18, serial: "SERIAL-R"));
        display.SetRead(key: "DISPLAY\\PORT-R", ok: true, current: 50, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            configureSettings: settings => settings.MonitorOverrides.Add(new MonitorOverrideEntry
            {
                ID = "edid:SERIAL-R", MinBrightness = 60
            }));
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 20;
        await WaitUntil(() => display.HasReadValue(60));

        display.ConfigureWriteReadBack(false);
        display.SetRead(key: "DISPLAY\\PORT-R", ok: false, error: "reads wedged");
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
        display.SetRead(key: "DISPLAY\\PORT-G", ok: true, current: 40, max: 100);
        display.ConfigureWriteReadBack(true);

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
        display.SetRead(key: "DISPLAY\\PORT-G", ok: true, current: 20, max: 100);
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
        display.SetRead(key: "DISPLAY\\PORT-P", ok: true, current: 50, max: 100);

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
        display.SetRead(key: "DISPLAY\\PORT-M", ok: true, current: 128, max: 255);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 80;
        await WaitUntil(() => display.GetCurrentValue("DISPLAY\\PORT-M") == 204);
        int writesBeforeReset = display.SetVcpCalls;

        display.SetRead(key: "DISPLAY\\PORT-M", ok: true, current: 20, max: 200);
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
        display.SetRead(key: "DISPLAY\\PORT-I", ok: true, current: 40, max: 100);
        display.ConfigureWriteReadBack(true);

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
        display.SetRead(key: "DISPLAY\\PORT-I", ok: true, current: 20, max: 100);
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
        display.SetRead(key: "DISPLAY\\PORT-H", ok: true, current: 60, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 2);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 42;
        await WaitUntil(() => display.HasReadValue(42));

        display.ConfigureWriteReadBack(false);
        display.SetRead(key: "DISPLAY\\PORT-H", ok: false, error: "reads wedged");
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
        display.SetRead(key: "DISPLAY\\PORT-J", ok: true, current: 50, max: 100);
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

        display.ConfigureWriteReadBack(true);
        Stopwatch stopwatch = Stopwatch.StartNew();
        monitor.Brightness = 80;

        await WaitUntil(() => display.HasReadValue(80), timeoutMs: 1000);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 750,
            $"New target waited {stopwatch.ElapsedMilliseconds} ms behind obsolete validation dwell.");
    }

    [Fact]
    public async Task RecoveryWorkerRotatesPoisonedTransportUntilMonitorRecovers()
    {
        const string DeviceID = "DISPLAY\\HDMI-RECOVERY";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 21, serial: "HDMI-RECOVERY"));
        display.SetRead(DeviceID, ok: true, current: 40, max: 100);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1);
        using DDCRecoveryService recoveryService = new(service, retryIntervalMs: 20);
        recoveryService.Start();
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        display.ConfigureRecoveryAfterTransportResets(DeviceID, resetCount: 3, current: 55, max: 100);
        service.Refresh();

        await WaitUntil(
            () => service.Monitors[0] is { IsHardwareFunctional: true, RoundedBrightness: 55 }
                  && display.GetTransportResetCount(DeviceID) >= 3,
            timeoutMs: 3000);

        Assert.False(service.Monitors[0].IsReadDegraded);
        Assert.Null(service.Monitors[0].LastDDCError);
    }

    [Fact]
    public async Task RecoveryWorkerReplaysLatestManualTargetWithoutFlyoutSubscriber()
    {
        const string DeviceID = "DISPLAY\\HDMI-REPLAY";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 22, serial: "HDMI-REPLAY"));
        display.SetRead(DeviceID, ok: true, current: 35, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1);
        using DDCRecoveryService recoveryService = new(service, retryIntervalMs: 20);
        recoveryService.Start();
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 42;
        await WaitUntil(() => display.GetCurrentValue(DeviceID) == 42);

        display.ConfigureWriteFailures(1);
        monitor.Brightness = 70;

        await WaitUntil(
            () => monitor.IsHardwareFunctional && display.GetCurrentValue(DeviceID) == 70,
            timeoutMs: 3000);

        Assert.True(display.GetTransportResetCount(DeviceID) >= 1);
        Assert.Null(monitor.LastDDCError);
    }

    [Fact]
    public async Task ProfileRestoredPoweredOffStateDoesNotCancelDirectRecovery()
    {
        const string DeviceID = "DISPLAY\\HDMI-STALE-POWER";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 31, serial: "HDMI-STALE-POWER"));
        display.SetRead(DeviceID, ok: true, current: 35, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1);
        using DDCRecoveryService recoveryService = new(service, retryIntervalMs: 20);
        recoveryService.Start();
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        // ProfileManager restores this optimistic UI value without issuing a power command. It must not be treated
        // as authoritative runtime power-off intent when a later brightness operation demotes the row.
        monitor.IsPoweredOn = false;
        display.ConfigureWriteFailures(1);
        Stopwatch stopwatch = Stopwatch.StartNew();
        monitor.Brightness = 68;

        await WaitUntil(
            () => monitor.IsHardwareFunctional && display.GetCurrentValue(DeviceID) == 68,
            timeoutMs: 1500);

        Assert.True(
            stopwatch.ElapsedMilliseconds < 1000,
            $"Direct recovery was delayed {stopwatch.ElapsedMilliseconds} ms behind stale power state.");
        Assert.False(monitor.SuppressDDCRecoveryForPowerIntent);
        Assert.Null(monitor.LastDDCError);
    }

    [Fact]
    public async Task RefreshPromotionReplaysLatestManualTargetWithoutAcquisitionEvent()
    {
        const string DeviceID = "DISPLAY\\HDMI-REFRESH-REPLAY";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(
            DeviceID,
            displayNumber: 30,
            serial: "HDMI-REFRESH-REPLAY"));
        display.SetRead(DeviceID, ok: true, current: 35, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 42;
        await WaitUntil(() => display.GetCurrentValue(DeviceID) == 42);

        display.SetRead(DeviceID, ok: false, error: "temporary refresh failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.SetRead(DeviceID, ok: true, current: 20, max: 100);
        service.Refresh();

        await WaitUntil(
            () => monitor.IsHardwareFunctional && display.GetCurrentValue(DeviceID) == 42,
            timeoutMs: 3000);
    }

    [Fact]
    public async Task RefreshRecoveryReplaysFinalSleepingTargetAfterCurveReconciliation()
    {
        const string DeviceID = "DISPLAY\\HDMI-SLEEPING-REPLAY";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(
            DeviceID,
            displayNumber: 32,
            serial: "HDMI-SLEEPING-REPLAY"));
        display.SetRead(DeviceID, ok: true, current: 35, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.Brightness = 42;
        await WaitUntil(() => display.GetCurrentValue(DeviceID) == 42);

        bool curveEngaged = false;
        service.IsBrightnessCurveEnabledQuery = () => curveEngaged;
        service.IsInDisabledPeriodQuery = () => false;
        Action reconcileRecoveredCurveState = () =>
        {
            if (!curveEngaged || !monitor.IsHardwareFunctional) return;
            monitor.SliderState = SliderState.CurveSleeping;
        };
        service.MonitorsRefreshed += reconcileRecoveredCurveState;

        display.SetRead(DeviceID, ok: false, error: "temporary refresh failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        // Model an external wake after an earlier app-issued off command. A readable recovery is newer evidence than
        // the stale runtime gate, and the subscriber's final sleeping state owns the manual brightness target.
        monitor.SuppressDDCRecoveryForPowerIntent = true;
        monitor.IsPoweredOn = false;
        curveEngaged = true;
        display.SetRead(DeviceID, ok: true, current: 73, max: 100);
        service.Refresh();

        await WaitUntil(
            () => monitor.SliderState == SliderState.CurveSleeping
                  && display.GetCurrentValue(DeviceID) == 42,
            timeoutMs: 3000);
        service.MonitorsRefreshed -= reconcileRecoveredCurveState;

        Assert.False(monitor.SuppressDDCRecoveryForPowerIntent);
        Assert.True(monitor.IsPoweredOn);
        Assert.Equal((uint)42, display.GetLastSetValueForCode(VCPConstants.Brightness));
    }

    [Fact]
    public async Task TargetedRecoveryForcesVerifiedCurveTargetWhenRecoveryReadAlreadyMatches()
    {
        const string DeviceID = "DISPLAY\\HDMI-LYING-READ";
        const int CurveTarget = 73;
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(
            DeviceID,
            displayNumber: 33,
            serial: "HDMI-LYING-READ"));
        display.SetRead(DeviceID, ok: true, current: 35, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1,
            brightnessCurveEnabled: true);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.CurveTargetBrightness = CurveTarget;
        service.EnqueueDirectBrightness(monitor, CurveTarget);
        await WaitUntil(() => display.GetCurrentValue(DeviceID) == CurveTarget);

        display.SetRead(DeviceID, ok: false, error: "temporary targeted failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        // Some firmware can return the requested VCP value while the visible panel state is stale. Recovery therefore
        // must perform a fresh SET plus read-back instead of accepting this matching GET as application evidence.
        display.SetRead(DeviceID, ok: true, CurveTarget, max: 100);
        int writesBeforeRecovery = display.SetVcpCalls;
        int readsBeforeRecovery = display.GetVcpCalls;
        Assert.True(service.TryRecoverMonitor(monitor.ID));

        await WaitUntil(
            () => display.SetVcpCalls > writesBeforeRecovery
                  && display.GetVcpCalls >= readsBeforeRecovery + 2,
            timeoutMs: 3000);
        Assert.Equal(SliderState.CurveActive, monitor.SliderState);
        Assert.Equal((uint)CurveTarget, display.GetLastSetValueForCode(VCPConstants.Brightness));
    }

    [Fact]
    public async Task DirectBrightnessIntentSupersedesRuntimePowerOffSuppression()
    {
        const string DeviceID = "DISPLAY\\HDMI-USER-WAKE";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 34, serial: "HDMI-USER-WAKE"));
        display.SetRead(DeviceID, ok: true, current: 40, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        await service.SetPowerStateAsync(monitor, on: false);
        Assert.True(monitor.SuppressDDCRecoveryForPowerIntent);
        int writesAfterPowerOff = display.SetVcpCalls;

        // Background curve traffic cannot contradict an explicit power-off command.
        service.EnqueueDirectBrightness(monitor, percent: 71);
        Assert.Equal(writesAfterPowerOff, display.SetVcpCalls);

        // A slider/profile brightness assignment is explicit newer intent and must be allowed to recover the panel.
        monitor.Brightness = 65;
        await WaitUntil(() => display.GetCurrentValue(DeviceID) == 65, timeoutMs: 3000);

        Assert.False(monitor.SuppressDDCRecoveryForPowerIntent);
        Assert.True(monitor.IsPoweredOn);
    }

    [Fact]
    public async Task CandidateSnapshotFailureDoesNotStopRequestedRecovery()
    {
        const string DeviceID = "DISPLAY\\HDMI-SNAPSHOT";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 29, serial: "HDMI-SNAPSHOT"));
        display.SetRead(DeviceID, ok: true, current: 35, max: 100);
        display.ConfigureWriteReadBack(true);
        InlineMonitorServiceDispatcher dispatcher = new();

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1,
            dispatcher: dispatcher);
        using DDCRecoveryService recoveryService = new(service, retryIntervalMs: 20);
        recoveryService.Start();
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        display.ConfigureWriteFailures(1);
        dispatcher.FailNextGenericInvoke();
        service.Monitors[0].Brightness = 68;

        await WaitUntil(
            () => service.Monitors[0].IsHardwareFunctional && display.GetCurrentValue(DeviceID) == 68,
            timeoutMs: 3000);

        Assert.Null(service.Monitors[0].LastDDCError);
    }

    [Fact]
    public async Task VerificationReadFailuresDoNotFloodHDMIWithReapplyWrites()
    {
        const string DeviceID = "DISPLAY\\HDMI-QUIET";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 23, serial: "HDMI-QUIET"));
        display.SetRead(DeviceID, ok: true, current: 45, max: 100);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 3,
            validationDwellMs: 30);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        display.SetRead(DeviceID, ok: false, error: "simulated HDMI checksum failure");
        service.Monitors[0].Brightness = 70;

        await WaitUntil(() => service.Monitors[0].IsFailed);

        Assert.Equal(expected: 1, display.SetVcpCalls);
        Assert.Equal(expected: 3, display.GetTransportResetCount(DeviceID));
    }

    [Fact]
    public async Task HDMITransportResetDoesNotRecycleHealthyDisplayPortTransport()
    {
        const string HDMIID = "DISPLAY\\HDMI-FAULT";
        const string DisplayPortID = "DISPLAY\\DP-HEALTHY";
        FakeDisplayService display = new();
        display.SetMonitors(
            CreateMonitor(HDMIID, displayNumber: 27, serial: "HDMI-FAULT"),
            CreateMonitor(DisplayPortID, displayNumber: 28, serial: "DP-HEALTHY"));
        display.SetRead(HDMIID, ok: true, current: 50, max: 100);
        display.SetRead(DisplayPortID, ok: true, current: 50, max: 100);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1);
        await WaitUntil(() => service.Monitors.Count == 2 && service.Monitors.All(m => m.IsHardwareFunctional));

        display.SetRead(HDMIID, ok: false, error: "simulated HDMI transport failure");
        service.Refresh();
        await WaitUntil(() => service.Monitors.Single(m => m.EDIDSerial == "HDMI-FAULT").IsFailed);

        Assert.True(service.Monitors.Single(m => m.EDIDSerial == "DP-HEALTHY").IsHardwareFunctional);
        Assert.Equal(expected: 1, display.GetTransportResetCount(HDMIID));
        Assert.Equal(expected: 0, display.GetTransportResetCount(DisplayPortID));
    }

    [Fact]
    public async Task TargetedRecoveryUsesRetainedDisplayInstancePathAfterHDMIIdentityDrift()
    {
        const string DisplayInstancePath = @"DISPLAY\TST0001\HDMI_INSTANCE";
        FakeDisplayService display = new();
        DDCMonitor initial = CreateMonitor(
            deviceID: "DISPLAY\\HDMI-OLD",
            displayNumber: 1,
            string.Empty);
        initial.DisplayInstancePath = DisplayInstancePath;
        display.SetMonitors(initial);
        display.SetRead(key: "DISPLAY\\HDMI-OLD", ok: true, current: 40, max: 100);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.DisplayNumber,
            validationAttempts: 1);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        display.SetRead(key: "DISPLAY\\HDMI-OLD", ok: false, error: "link retraining");
        service.Refresh();
        await WaitUntil(() => service.Monitors[0].IsFailed);

        DDCMonitor retrained = CreateMonitor(
            deviceID: "DISPLAY\\HDMI-NEW",
            displayNumber: 7,
            string.Empty,
            name: @"\\.\DISPLAY7");
        retrained.DisplayInstancePath = DisplayInstancePath;
        display.SetMonitors(retrained);
        display.SetRead(key: "DISPLAY\\HDMI-NEW", ok: true, current: 65, max: 100);

        bool recovered = service.TryRecoverMonitor("num:1");

        Assert.True(recovered);
        Assert.True(service.Monitors[0].IsHardwareFunctional);
        Assert.Equal(expected: 7, service.Monitors[0].DisplayNumber);
        Assert.Equal(expected: "num:1", service.Monitors[0].ID);
    }

    [Fact]
    public async Task PerMonitorBrightnessDwellDoesNotDelayOtherDisplays()
    {
        const string HDMIID = "DISPLAY\\HDMI-SLOW";
        const string DisplayPortID = "DISPLAY\\DP-FAST";
        FakeDisplayService display = new();
        display.SetMonitors(
            CreateMonitor(HDMIID, displayNumber: 24, serial: "HDMI-SLOW"),
            CreateMonitor(DisplayPortID, displayNumber: 25, serial: "DP-FAST"));
        display.SetRead(HDMIID, ok: true, current: 50, max: 100);
        display.SetRead(DisplayPortID, ok: true, current: 50, max: 100);
        display.ConfigureWriteReadBack(true);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1,
            configureSettings: settings =>
            {
                settings.MonitorOverrides.Add(new MonitorOverrideEntry
                {
                    ID = "edid:HDMI-SLOW", BrightnessDwellMs = 1_000
                });
                settings.MonitorOverrides.Add(new MonitorOverrideEntry { ID = "edid:DP-FAST", BrightnessDwellMs = 0 });
            });
        await WaitUntil(() => service.Monitors.Count == 2 && service.Monitors.All(m => m.IsHardwareFunctional));

        MonitorInfo HDMI = service.Monitors.Single(m => m.EDIDSerial == "HDMI-SLOW");
        MonitorInfo displayPort = service.Monitors.Single(m => m.EDIDSerial == "DP-FAST");
        HDMI.Brightness = 10;
        await WaitUntil(() => display.GetCurrentValue(HDMIID) == 10);

        HDMI.Brightness = 20;
        Stopwatch stopwatch = Stopwatch.StartNew();
        displayPort.Brightness = 30;
        await WaitUntil(() => display.GetCurrentValue(DisplayPortID) == 30, timeoutMs: 750);

        Assert.True(stopwatch.ElapsedMilliseconds < 750);
        Assert.NotEqual((uint)20, display.GetCurrentValue(HDMIID));
        await WaitUntil(() => display.GetCurrentValue(HDMIID) == 20, timeoutMs: 2500);
    }

    [Fact]
    public async Task RecoveryPreservesDisabledUserIntent()
    {
        const string DeviceID = "DISPLAY\\HDMI-DISABLED";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 26, serial: "HDMI-DISABLED"));
        display.SetRead(DeviceID, ok: true, current: 50, max: 100);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.SliderState = SliderState.Disabled;
        display.SetRead(DeviceID, ok: false, error: "temporary failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.SetRead(DeviceID, ok: true, current: 55, max: 100);
        Assert.True(service.TryRecoverMonitor(monitor.ID));

        Assert.Equal(SliderState.Disabled, monitor.SliderState);
        Assert.False(monitor.IsParticipatingInMaster);
    }

    [Fact]
    public async Task RecoveryPreservesReleasedCurveIntent()
    {
        const string DeviceID = "DISPLAY\\HDMI-RELEASED";
        FakeDisplayService display = new();
        display.SetMonitors(CreateMonitor(DeviceID, displayNumber: 31, serial: "HDMI-RELEASED"));
        display.SetRead(DeviceID, ok: true, current: 50, max: 100);

        using MonitorService service = CreateService(
            display,
            MonitorIdentityStrategy.EDIDSerial,
            validationAttempts: 1,
            brightnessCurveEnabled: true);
        await WaitUntil(() => service.Monitors is [{ IsHardwareFunctional: true }]);

        MonitorInfo monitor = service.Monitors[0];
        monitor.SliderState = SliderState.CurveReleased;
        display.SetRead(DeviceID, ok: false, error: "temporary failure");
        service.Refresh();
        await WaitUntil(() => monitor.IsFailed);

        display.SetRead(DeviceID, ok: true, current: 55, max: 100);
        Assert.True(service.TryRecoverMonitor(monitor.ID));

        Assert.Equal(SliderState.CurveReleased, monitor.SliderState);
        Assert.True(monitor.IsParticipatingInMaster);
    }

    private static MonitorService CreateService(
        FakeDisplayService display,
        MonitorIdentityStrategy strategy,
        int validationAttempts = 1,
        bool brightnessCurveEnabled = false,
        int validationDwellMs = 0,
        KnownDisplaysStore? knownDisplays = null,
        Action<AppSettings>? configureSettings = null,
        InlineMonitorServiceDispatcher? dispatcher = null)
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
            path2: "BrightnessTrayAppDotNET.Tests",
            $"{Guid.NewGuid():N}.displays.json"));
        return new MonitorService(display, settings, store, dispatcher ?? new InlineMonitorServiceDispatcher());
    }

    private static DDCMonitor CreateMonitor(
        string deviceID,
        int displayNumber,
        string serial,
        string name = @"\\.\DISPLAY1")
    {
        return new DDCMonitor
        {
            Handle = displayNumber,
            HDC = displayNumber + 100,
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
        private int _failNextGenericInvoke;

        public void FailNextGenericInvoke() => Interlocked.Exchange(ref _failNextGenericInvoke, value: 1);

        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public void Invoke(Action action) => action();

        public T Invoke<T>(Func<T> action)
        {
            if (Interlocked.Exchange(ref _failNextGenericInvoke, value: 0) == 1)
                throw new InvalidOperationException("simulated dispatcher snapshot failure");

            return action();
        }
    }

    private sealed class FakeDisplayService : IDisplayService
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, VcpRead> _reads = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Key, byte Code), VcpRead> _featureReads = [];
        private readonly Dictionary<string, int> _readFailuresRemaining = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string?> _readFailureErrors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _transportResetCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _transportResetsUntilRecovery = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VcpRead> _readsAfterTransportRecovery = new(StringComparer.Ordinal);
        private readonly HashSet<byte> _featureWriteReadBackCodes = [];
        private readonly Dictionary<byte, int> _successfulFeatureWritesToDrop = [];
        private readonly List<uint> _readValues = [];
        private readonly List<byte> _getVCPFeatureCodes = [];
        private readonly List<(byte Code, uint Value)> _setVCPFeatureCalls = [];
        private List<DDCMonitor> _monitors = [];
        private int _refreshHandleCalls;
        private int _enumerationCalls;
        private int _fullEnumerationCalls;
        private int _DDCRecoveryEnumerationCalls;
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
        public int FullEnumerationCalls => Volatile.Read(ref _fullEnumerationCalls);
        public int DDCRecoveryEnumerationCalls => Volatile.Read(ref _DDCRecoveryEnumerationCalls);
        public int SetVcpCalls => Volatile.Read(ref _setVcpCalls);
        public int GetVcpCalls => Volatile.Read(ref _getVcpCalls);
        public string LastReadKey { get; private set; } = string.Empty;
        public int OperationTimeoutMs { get; set; }

        public void ConfigureWriteReadBack(bool applySuccessfulWrites, int successfulWritesToDrop = 0)
        {
            lock (_gate)
            {
                _applySuccessfulWritesToReadBack = applySuccessfulWrites;
                _successfulWritesToDrop = Math.Max(val1: 0, successfulWritesToDrop);
            }
        }

        public void ConfigureFeatureWriteReadBack(
            byte code,
            bool applySuccessfulWrites,
            int successfulWritesToDrop = 0)
        {
            lock (_gate)
            {
                if (applySuccessfulWrites)
                    _featureWriteReadBackCodes.Add(code);
                else
                    _featureWriteReadBackCodes.Remove(code);

                _successfulFeatureWritesToDrop[code] = Math.Max(val1: 0, successfulWritesToDrop);
            }
        }

        public void ConfigureWriteFailures(int writesToFail)
        {
            lock (_gate)
                _writesToFail = Math.Max(val1: 0, writesToFail);
        }

        public void ConfigureRecoveryAfterTransportResets(
            string key,
            int resetCount,
            uint current,
            uint max)
        {
            lock (_gate)
            {
                _reads[key] = new VcpRead(Ok: false, Current: 0, Max: 0, Error: "simulated poisoned transport");
                _transportResetsUntilRecovery[key] = Math.Max(val1: 1, resetCount);
                _readsAfterTransportRecovery[key] = new VcpRead(Ok: true, current, max, Error: null);
            }
        }

        public int GetTransportResetCount(string key)
        {
            lock (_gate)
                return _transportResetCounts.GetValueOrDefault(key, defaultValue: 0);
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

        public int GetSetVCPFeatureCallCount(byte code)
        {
            lock (_gate)
                return _setVCPFeatureCalls.Count(call => call.Code == code);
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

        public void SetFeatureRead(
            string key,
            byte code,
            bool ok,
            uint current,
            uint max,
            string? error = null)
        {
            lock (_gate)
                _featureReads[(key, code)] = new VcpRead(ok, current, max, error);
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
                _reads[key] = new VcpRead(Ok: true, current, max, Error: null);
                _readFailuresRemaining[key] = Math.Max(val1: 0, failuresBeforeSuccess);
                _readFailureErrors[key] = error;
            }
        }

        public bool TryGetMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error) =>
            TryGetMonitorsCore(isDDCRecovery: false, out monitors, out error);

        public bool TryGetDDCRecoveryMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? error) =>
            TryGetMonitorsCore(isDDCRecovery: true, out monitors, out error);

        private bool TryGetMonitorsCore(
            bool isDDCRecovery,
            out IReadOnlyList<DDCMonitor> monitors,
            out string? error)
        {
            EnumerationEnteredSignal?.Set();
            EnumerationReleaseSignal?.Wait();

            Interlocked.Increment(ref _enumerationCalls);
            if (isDDCRecovery)
                Interlocked.Increment(ref _DDCRecoveryEnumerationCalls);
            else
                Interlocked.Increment(ref _fullEnumerationCalls);

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
                _getVcpCalls++;
                _getVCPFeatureCodes.Add(code);
                string key = KeyFor(monitor);
                LastReadKey = key;
                bool hasFeatureRead = _featureReads.TryGetValue((key, code), out VcpRead featureRead);
                if (code != monitor.BrightnessCode && !hasFeatureRead)
                {
                    currentValue = 0;
                    maxValue = 0;
                    error = "feature read not configured";
                    return false;
                }

                VcpRead read = hasFeatureRead
                    ? featureRead
                    : _reads.TryGetValue(key, out VcpRead configured)
                        ? configured
                        : new VcpRead(Ok: true, Current: 50, Max: 100, Error: null);

                if (!hasFeatureRead
                    && _readFailuresRemaining.TryGetValue(key, out int failuresRemaining)
                    && failuresRemaining > 0)
                {
                    _readFailuresRemaining[key] = failuresRemaining - 1;
                    currentValue = read.Current;
                    maxValue = read.Max;
                    error = _readFailureErrors.GetValueOrDefault(key, defaultValue: "simulated sequenced read failure");
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
                        _successfulWritesToDrop--;
                    else
                    {
                        string key = KeyFor(monitor);
                        uint maximum = _reads.TryGetValue(key, out VcpRead currentRead) && currentRead.Max > 0
                            ? currentRead.Max
                            : 100;
                        _reads[key] = new VcpRead(Ok: true, value, maximum, Error: null);
                    }
                }

                if (_featureWriteReadBackCodes.Contains(code))
                {
                    int writesToDrop = _successfulFeatureWritesToDrop.GetValueOrDefault(code, defaultValue: 0);
                    if (writesToDrop > 0)
                        _successfulFeatureWritesToDrop[code] = writesToDrop - 1;
                    else
                    {
                        string key = KeyFor(monitor);
                        uint maximum = _featureReads.TryGetValue((key, code), out VcpRead currentRead)
                                       && currentRead.Max > 0
                            ? currentRead.Max
                            : byte.MaxValue;
                        _featureReads[(key, code)] = new VcpRead(Ok: true, value, maximum, Error: null);
                    }
                }

                error = null;
                return true;
            }
        }

        public void ResetDDCTransport(DDCMonitor monitor)
        {
            lock (_gate)
            {
                string key = KeyFor(monitor);
                _transportResetCounts[key] = GetTransportResetCountUnderLock(key) + 1;
                if (!_transportResetsUntilRecovery.TryGetValue(key, out int remaining)) return;

                remaining--;
                if (remaining > 0)
                {
                    _transportResetsUntilRecovery[key] = remaining;
                    return;
                }

                _transportResetsUntilRecovery.Remove(key);
                if (_readsAfterTransportRecovery.Remove(key, out VcpRead recoveredRead))
                    _reads[key] = recoveredRead;
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

        private int GetTransportResetCountUnderLock(string key) =>
            _transportResetCounts.GetValueOrDefault(key, defaultValue: 0);

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
