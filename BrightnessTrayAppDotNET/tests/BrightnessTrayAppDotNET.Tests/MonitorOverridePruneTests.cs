using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.UI.Settings;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class MonitorOverridePruneTests
{
    [Fact]
    public void FreshOverrideOverridesNothing() =>
        Assert.True(BrightnessSettingsWindow.IsDefaultMonitorOverride(new MonitorOverrideEntry { ID = "edid:A" }));

    // -1 inherits the global dwell, so an explicit 0 ms is a real override that must survive pruning
    [Fact]
    public void ZeroValidationDwellIsKept() =>
        Assert.False(BrightnessSettingsWindow.IsDefaultMonitorOverride(
            new MonitorOverrideEntry { ID = "edid:A", ValidationDwellMs = 0 }));

    [Fact]
    public void ZeroBrightnessDwellIsKept() =>
        Assert.False(BrightnessSettingsWindow.IsDefaultMonitorOverride(
            new MonitorOverrideEntry { ID = "edid:A", BrightnessDwellMs = 0 }));
}
