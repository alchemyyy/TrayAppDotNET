using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class FlyoutFrameTests
{
    [Theory]
    [InlineData(true, 8, 7)]
    [InlineData(false, 0, 0)]
    public void FrameUsesSharedGeometryWithoutAnOuterShadow(
        bool enableRoundedCorners,
        double expectedCornerRadius,
        double expectedInnerCornerRadius) =>
        AvaloniaTestHost.Run(() =>
        {
            Grid content = new();
            Thickness framePadding = new(1);
            Thickness contentMargin = new(2);
            Thickness contentPadding = new(3);
            FlyoutFrame frame = new(
                content,
                Colors.Black,
                Colors.Gray,
                enableRoundedCorners,
                framePadding,
                contentMargin,
                contentPadding);

            Assert.Equal(new Thickness(1), frame.BorderThickness);
            Assert.Equal(new CornerRadius(expectedCornerRadius), frame.CornerRadius);
            Assert.Equal(framePadding, frame.Padding);
            Assert.False(frame.ClipToBounds);
            Assert.Equal(0, frame.BoxShadow.Count);

            Border contentSurface = Assert.IsType<Border>(frame.Child);
            Assert.Equal(new CornerRadius(expectedInnerCornerRadius), contentSurface.CornerRadius);
            Assert.Equal(contentMargin, contentSurface.Margin);
            Assert.Equal(contentPadding, contentSurface.Padding);
            Assert.True(contentSurface.ClipToBounds);
            Assert.Same(content, contentSurface.Child);
            Assert.Same(frame.Background, contentSurface.Background);
        });

    [Fact]
    public void FlyoutWindowUsesSharedTransparentWindowDefaults() =>
        AvaloniaTestHost.Run(() =>
        {
            TestFlyoutWindow window = new();
            try
            {
                Assert.Equal(WindowDecorations.None, window.WindowDecorations);
                Assert.Contains(WindowTransparencyLevel.Transparent, window.TransparencyLevelHint);
                Assert.Same(Brushes.Transparent, window.Background);
                Assert.False(window.ShowInTaskbar);
                Assert.False(window.CanResize);
                Assert.True(window.Topmost);
                Assert.Equal(SizeToContent.Height, window.SizeToContent);
            }
            finally
            {
                window.Close();
            }
        });

    private sealed class TestFlyoutWindow : FlyoutWindowCommon
    {
    }
}
