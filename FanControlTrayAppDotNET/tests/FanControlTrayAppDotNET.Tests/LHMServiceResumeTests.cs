using FanControlTrayAppDotNET.Services;
using Microsoft.Win32;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class LHMServiceResumeTests
{
    /// <summary>
    /// Verifies only a resume resets the hardware and re-applies fan control: a suspend has nothing to restore yet,
    /// and a power-source change leaves the fan chip alone.
    /// </summary>
    [Theory]
    [InlineData(PowerModes.Resume, true)]
    [InlineData(PowerModes.Suspend, false)]
    [InlineData(PowerModes.StatusChange, false)]
    public void OnlyResumeReappliesControl(PowerModes mode, bool expected) =>
        Assert.Equal(expected, LHMService.ShouldReapplyControl(mode));
}
