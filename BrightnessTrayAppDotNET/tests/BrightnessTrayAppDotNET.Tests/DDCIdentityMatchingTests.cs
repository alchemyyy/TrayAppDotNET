using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.Services;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

/// <summary>
/// Pure identity decisions that route DDC traffic: which enumerated monitor a helper command or a handle refresh
/// binds to, which monitor device describes a GDI source, which helpers outlived their monitor, and when Device
/// Manager shows a monitor no row accounts for.
/// </summary>
public sealed class DDCIdentityMatchingTests
{
    private const string DeviceA = @"MONITOR\TST0001\{4d36e96e-e325-11ce-bfc1-08002be10318}\0001";
    private const string DeviceB = @"MONITOR\TST0001\{4d36e96e-e325-11ce-bfc1-08002be10318}\0002";

    [Fact]
    public void HelperPrefersALaterDeviceIDMatchOverAnEarlierNameMatch()
    {
        DDCMonitor sameName = Monitor(@"\\.\DISPLAY2", DeviceB, serial: string.Empty);
        DDCMonitor sameDevice = Monitor(@"\\.\DISPLAY5", DeviceA, serial: string.Empty);

        DDCMonitor? match = DDCHelperServer.FindMatchingMonitor(
            [sameName, sameDevice],
            new DDCHelperServer.DDCHelperMonitorIdentity(DeviceA, edidSerial: string.Empty, @"\\.\DISPLAY2", string.Empty));

        Assert.Same(sameDevice, match);
    }

    [Fact]
    public void HelperRefusesANameMatchWhoseStableDeviceIDDiffers()
    {
        DDCMonitor otherPanel = Monitor(@"\\.\DISPLAY2", DeviceB, serial: string.Empty);

        DDCMonitor? match = DDCHelperServer.FindMatchingMonitor(
            [otherPanel],
            new DDCHelperServer.DDCHelperMonitorIdentity(DeviceA, edidSerial: string.Empty, @"\\.\DISPLAY2", string.Empty));

        Assert.Null(match);
    }

    [Fact]
    public void HelperRefusesEveryRuleWhenTheEDIDSerialContradicts()
    {
        DDCMonitor otherPanel = Monitor(@"\\.\DISPLAY2", DeviceA, serial: "OTHER-SERIAL");

        DDCMonitor? match = DDCHelperServer.FindMatchingMonitor(
            [otherPanel],
            new DDCHelperServer.DDCHelperMonitorIdentity(DeviceA, "OWN-SERIAL", @"\\.\DISPLAY2", string.Empty));

        Assert.Null(match);
    }

    [Fact]
    public void HelperAcceptsANameMatchWhenTheDeviceIDIsAnAdapterFallback()
    {
        DDCMonitor panel = Monitor(@"\\.\DISPLAY2", DeviceB, serial: string.Empty);

        DDCMonitor? match = DDCHelperServer.FindMatchingMonitor(
            [panel],
            new DDCHelperServer.DDCHelperMonitorIdentity(@"\\.\DISPLAY2", string.Empty, @"\\.\DISPLAY2", string.Empty));

        Assert.Same(panel, match);
    }

    [Fact]
    public void HandleRefreshPrefersALaterStableDeviceMatchOverAnEarlierAdapterName()
    {
        RefreshHandleIdentity target = Identity(@"\\.\DISPLAY2", DeviceA, serial: string.Empty);

        int selected = DisplayService.SelectRefreshHandleCandidate(
            target,
            [Identity(@"\\.\DISPLAY2", DeviceB, serial: string.Empty), Identity(@"\\.\DISPLAY7", DeviceA, serial: string.Empty)],
            out RefreshHandleMatch match);

        Assert.Equal(expected: 1, selected);
        Assert.Equal(RefreshHandleMatch.StableDevice, match);
    }

    [Fact]
    public void HandleRefreshRefusesAnAdapterNameMatchOnAnotherPanel()
    {
        RefreshHandleIdentity target = Identity(@"\\.\DISPLAY2", DeviceA, "OWN-SERIAL");

        int selected = DisplayService.SelectRefreshHandleCandidate(
            target,
            [Identity(@"\\.\DISPLAY2", DeviceB, "OTHER-SERIAL")],
            out RefreshHandleMatch match);

        Assert.Equal(expected: -1, selected);
        Assert.Equal(RefreshHandleMatch.None, match);
    }

    [Fact]
    public void HelperNeedsTheSameHMONITORWhenTheNameIsTheOnlyIdentity()
    {
        DDCMonitor renamedSource = Monitor(@"\\.\DISPLAY2", @"\\.\DISPLAY2", serial: string.Empty);
        renamedSource.Handle = 0x20003;

        DDCMonitor? stale = DDCHelperServer.FindMatchingMonitor(
            [renamedSource],
            new DDCHelperServer.DDCHelperMonitorIdentity(@"\\.\DISPLAY2", string.Empty, @"\\.\DISPLAY2", string.Empty, 0x10001));
        DDCMonitor? current = DDCHelperServer.FindMatchingMonitor(
            [renamedSource],
            new DDCHelperServer.DDCHelperMonitorIdentity(@"\\.\DISPLAY2", string.Empty, @"\\.\DISPLAY2", string.Empty, 0x20003));

        Assert.Null(stale);
        Assert.Same(renamedSource, current);
    }

    [Fact]
    public void OnlyAnActiveMonitorDeviceDescribesTheSource()
    {
        Assert.Equal(expected: 1, DisplayService.SelectMonitorDeviceIndex([0, DisplayService.DisplayDeviceActive]));
        Assert.Equal(expected: -1, DisplayService.SelectMonitorDeviceIndex([0, 0]));
        Assert.Equal(expected: -1, DisplayService.SelectMonitorDeviceIndex([]));
    }

    [Fact]
    public void HelpersOfIdentitiesThatNoLongerEnumerateAreStale()
    {
        DDCMonitor live = Monitor(@"\\.\DISPLAY1", DeviceA, serial: string.Empty);

        List<string> stale = DisplayService.SelectStaleHelperClientKeys(
            ["device:" + DeviceA.ToLowerInvariant(), "device:" + DeviceB, @"device:\\.\DISPLAY3"],
            [live]);

        string[] expected = ["device:" + DeviceB, @"device:\\.\DISPLAY3"];
        Assert.Equal(expected, stale);
    }

    [Fact]
    public void MonitorGapComparesExactDevnodesAndLetsPathlessRowsAccountForOne()
    {
        HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase)
        {
            @"DISPLAY\TST0001\5&1&UID100", @"DISPLAY\TST0001\5&1&UID200"
        };
        HashSet<string> known = new(StringComparer.OrdinalIgnoreCase) { @"display\tst0001\5&1&uid100" };

        Assert.True(DisplayEventManager.HasMonitorGap(reported, new MonitorService.MonitorPresence(known, 0)));
        Assert.False(DisplayEventManager.HasMonitorGap(reported, new MonitorService.MonitorPresence(known, 1)));

        known.Add(@"DISPLAY\TST0001\5&1&UID200");
        Assert.False(DisplayEventManager.HasMonitorGap(reported, new MonitorService.MonitorPresence(known, 0)));
    }

    [Fact]
    public void MonitorGapSignatureIgnoresCasingAndOrder()
    {
        MonitorService.MonitorPresence none = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
        HashSet<string> setupAPIForm = new(StringComparer.OrdinalIgnoreCase)
        {
            @"DISPLAY\TST0001\5&967BC41&1&UID200", @"DISPLAY\TST0001\5&967BC41&1&UID100"
        };
        HashSet<string> interfaceForm = new(StringComparer.OrdinalIgnoreCase)
        {
            @"display\tst0001\5&967bc41&1&uid100", @"display\tst0001\5&967bc41&1&uid200"
        };

        Assert.Equal(
            DisplayEventManager.DescribeMonitorGap(setupAPIForm, none),
            DisplayEventManager.DescribeMonitorGap(interfaceForm, none));
    }

    private static DDCMonitor Monitor(string name, string deviceID, string serial) =>
        new() { Name = name, DeviceID = deviceID, EDIDSerial = serial };

    private static RefreshHandleIdentity Identity(string name, string deviceID, string serial) =>
        new(name, deviceID, string.Empty, serial, string.Empty, string.Empty, MonitorBrightnessControlKind.DdcCi);
}
