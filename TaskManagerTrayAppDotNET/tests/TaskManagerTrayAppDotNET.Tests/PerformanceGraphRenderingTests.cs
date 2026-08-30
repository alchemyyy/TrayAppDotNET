using Avalonia.Media;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceGraphRenderingTests
{
    [Fact]
    public void UnderfillColorDarkensComponentsAndAppliesOpacity()
    {
        Color lineColor = Color.FromArgb(byte.MaxValue, 10, 40, 100);

        Color underfillColor = PerformanceGraphRendering.CreateUnderfillColor(
            lineColor,
            opacity: 0.16,
            darkenAmount: 20);

        Assert.Equal((byte)41, underfillColor.A);
        Assert.Equal((byte)0, underfillColor.R);
        Assert.Equal((byte)20, underfillColor.G);
        Assert.Equal((byte)80, underfillColor.B);
    }

    [Fact]
    public void UnderfillOpacityScalesTheLineAlpha()
    {
        Color lineColor = Color.FromArgb(128, 100, 110, 120);

        Color underfillColor = PerformanceGraphRendering.CreateUnderfillColor(
            lineColor,
            opacity: 0.5,
            darkenAmount: 0);

        Assert.Equal((byte)64, underfillColor.A);
        Assert.Equal(lineColor.R, underfillColor.R);
        Assert.Equal(lineColor.G, underfillColor.G);
        Assert.Equal(lineColor.B, underfillColor.B);
    }
}
