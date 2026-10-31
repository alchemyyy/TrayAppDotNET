using Avalonia.Controls;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerWindowCloseTests
{
    [Theory]
    [InlineData(WindowCloseReason.ApplicationShutdown, true)]
    [InlineData(WindowCloseReason.OSShutdown, true)]
    [InlineData(WindowCloseReason.WindowClosing, false)]
    [InlineData(WindowCloseReason.OwnerWindowClosing, false)]
    [InlineData(WindowCloseReason.Undefined, false)]
    public void OnlyShutdownClosesBypassHideToTray(WindowCloseReason reason, bool expected) =>
        Assert.Equal(expected, TaskManagerWindow.IsShutdownClose(reason));
}
