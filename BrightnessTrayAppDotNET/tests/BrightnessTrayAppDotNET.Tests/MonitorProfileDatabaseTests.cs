using BrightnessTrayAppDotNET.DDCCI;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

/// <summary>Checks the embedded monitor database and the profile resolution it drives.</summary>
public sealed class MonitorProfileDatabaseTests
{
    [Fact]
    public void TheEmbeddedDatabaseLoads() => Assert.True(DDCMonitorDatabase.LoadedMonitorCount >= 300);

    [Fact]
    public void AKnownMonitorGetsItsProfileIncludingTheInvertedPowerCommand()
    {
        // The Dell P2719HC carries the inverted 0xE1 power quirk
        DDCMonitor monitor = new() { EDIDManufacturerID = "DEL", EDIDProductCode = "4187" };

        Assert.True(DDCMonitorDatabase.ApplyProfile(monitor));
        Assert.True(monitor.HasKnownProfile);
        Assert.Equal("DEL4187", monitor.EDIDIdentifier);
        Assert.Contains("P2719HC", monitor.ProfileModelName, StringComparison.Ordinal);
        Assert.Equal(0x10, monitor.BrightnessCode);

        // Both 0xD6 and 0xE1 are advertised; the primary 0xD6 entry wins for a hard power-off
        Assert.Equal(((byte)0xD6, (byte)0x05), monitor.ResolvePowerOff(PowerOffLevel.Hard));

        Assert.True(monitor.PowerOffCommands.Count >= 2);
        MonitorPowerCommand alternate = monitor.PowerOffCommands[1];
        Assert.Equal(0xE1, alternate.Code);
        Assert.Equal(0, alternate.ValueOn);
        Assert.Equal(1, alternate.ValueHardOff);
        Assert.True(alternate.IsInverted);
    }

    [Theory]
    [InlineData("ZZZ", "FFFF")]
    [InlineData("", "")]
    public void AnUnknownOrMissingEdidKeepsTheVesaDefaults(string manufacturer, string productCode)
    {
        DDCMonitor monitor = new() { EDIDManufacturerID = manufacturer, EDIDProductCode = productCode };

        Assert.False(DDCMonitorDatabase.ApplyProfile(monitor));
        Assert.False(monitor.HasKnownProfile);
        Assert.Equal(((byte)0xD6, (byte)0x05), monitor.ResolvePowerOff(PowerOffLevel.Hard));
        Assert.Equal(0x04, monitor.ResolvePowerOff(PowerOffLevel.Soft).Value);
        Assert.Equal(0x02, monitor.ResolvePowerOff(PowerOffLevel.Sleep).Value);
    }
}
