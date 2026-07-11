using Avalonia.Controls;
using FanControlTrayAppDotNET.UI.Flyout;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanPropertiesWindowLifetimeTests
{
    [Theory]
    [InlineData(WindowCloseReason.Undefined)]
    [InlineData(WindowCloseReason.WindowClosing)]
    public void PinnedWindowRejectsUserClose(WindowCloseReason closeReason)
    {
        bool cancel = FanPropertiesWindow.ShouldCancelPinnedClose(
            forceClose: false,
            isPinned: true,
            closeReason: closeReason);

        Assert.True(cancel);
    }

    [Theory]
    [InlineData(WindowCloseReason.OwnerWindowClosing)]
    [InlineData(WindowCloseReason.ApplicationShutdown)]
    [InlineData(WindowCloseReason.OSShutdown)]
    public void PinnedWindowAllowsOwnerAndShutdownClose(WindowCloseReason closeReason)
    {
        bool cancel = FanPropertiesWindow.ShouldCancelPinnedClose(
            forceClose: false,
            isPinned: true,
            closeReason: closeReason);

        Assert.False(cancel);
    }

    [Fact]
    public void ExplicitForceCloseBypassesPin()
    {
        bool cancel = FanPropertiesWindow.ShouldCancelPinnedClose(
            forceClose: true,
            isPinned: true,
            closeReason: WindowCloseReason.WindowClosing);

        Assert.False(cancel);
    }
}
