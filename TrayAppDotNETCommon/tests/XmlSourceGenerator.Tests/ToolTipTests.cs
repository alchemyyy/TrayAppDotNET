using Avalonia.Controls;
using TrayAppDotNETCommon.UI;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class ToolTipTests
{
    [Fact]
    public void SetTipUsesNonOccludingTargetPlacement() => AvaloniaTestHost.Run(() =>
    {
        Button target = new();

        TrayAppDotNETToolTip.SetTip(target, "Tooltip");

        Assert.Equal("Tooltip", ToolTip.GetTip(target));
        Assert.Equal(PlacementMode.Custom, ToolTip.GetPlacement(target));
        Assert.Equal(0, ToolTip.GetHorizontalOffset(target));
        Assert.Equal(0, ToolTip.GetVerticalOffset(target));
        Assert.NotNull(ToolTip.GetCustomPopupPlacementCallback(target));
    });
}
