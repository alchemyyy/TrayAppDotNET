using Avalonia.Controls;
using BrightnessTrayAppDotNET.UI.Settings.Environmental;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class EnvironmentalMapPickerWindowTests
{
    [Theory]
    [InlineData(WindowCloseReason.Undefined)]
    [InlineData(WindowCloseReason.WindowClosing)]
    public void UserCloseKeepsReusablePickerAlive(WindowCloseReason closeReason)
    {
        bool cancel = EnvironmentalMapPickerWindow.ShouldCancelCloseForReuse(
            isRetiring: false,
            closeReason: closeReason);

        Assert.True(cancel);
    }

    [Theory]
    [InlineData(WindowCloseReason.OwnerWindowClosing)]
    [InlineData(WindowCloseReason.ApplicationShutdown)]
    [InlineData(WindowCloseReason.OSShutdown)]
    public void OwnerAndApplicationCloseCanRetirePicker(WindowCloseReason closeReason)
    {
        bool cancel = EnvironmentalMapPickerWindow.ShouldCancelCloseForReuse(
            isRetiring: false,
            closeReason: closeReason);

        Assert.False(cancel);
    }

    [Fact]
    public void ExplicitPageRetirementCanClosePicker()
    {
        bool cancel = EnvironmentalMapPickerWindow.ShouldCancelCloseForReuse(
            isRetiring: true,
            closeReason: WindowCloseReason.WindowClosing);

        Assert.False(cancel);
    }
}
