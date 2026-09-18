using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class SystemProbesTests
{
    [Theory]
    [InlineData((byte)128, false)]
    [InlineData((byte)255, true)]
    [InlineData((byte)1, true)]
    [InlineData((byte)0, true)]
    [InlineData((byte)8, true)]
    public void InterpretBatteryFlag_OnlyNoSystemBatteryIsFalse(byte flag, bool expected)
    {
        Assert.Equal(expected, SystemProbes.InterpretBatteryFlag(flag));
    }

    [Fact]
    public void HasSystemBattery_DoesNotThrow()
    {
        // The value depends on the machine; only the call path is exercised
        _ = SystemProbes.HasSystemBattery();
        _ = SystemProbes.IsElevated();
    }
}
