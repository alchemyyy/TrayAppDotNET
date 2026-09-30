using TrayAppDotNETCommon.UI.Tray;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class TaskbarForegroundTrackerTests
{
    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("TopLevelWindowForOverflowXamlIsland")]
    [InlineData("NotifyIconOverflowWindow")]
    public void TaskbarAndOverflowWindowsCountAsTaskbar(string className) =>
        Assert.True(TaskbarForegroundTracker.IsTaskbarWindowClassName(className));

    [Theory]
    [InlineData("")]
    [InlineData("Progman")]
    [InlineData("CabinetWClass")]
    [InlineData("SystemTray_Main")]
    public void OtherShellWindowsDoNotCountAsTaskbar(string className) =>
        Assert.False(TaskbarForegroundTracker.IsTaskbarWindowClassName(className));

    [Fact]
    public void NoWindowWasNeverForeground()
    {
        using TaskbarForegroundTracker tracker = new();

        Assert.False(tracker.WasForegroundBeforeTaskbar(IntPtr.Zero));
    }
}
