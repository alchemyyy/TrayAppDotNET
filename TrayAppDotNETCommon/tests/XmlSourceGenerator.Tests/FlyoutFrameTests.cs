using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.WarmWindows;
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
                Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
                Assert.Equal(0, window.Opacity);
                Assert.Equal(
                    new PixelPoint(
                        TrayAppDotNETWarmWindowDefaults.OffscreenPosition,
                        TrayAppDotNETWarmWindowDefaults.OffscreenPosition),
                    window.Position);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void HiddenShowStagesAnOpaqueWindowOnTheTargetMonitorBeforeCreatingItsNativeSurface() =>
        AvaloniaTestHost.Run(() =>
        {
            PixelPoint stagingPosition = new(2560, 120);
            TestFlyoutWindow window = new()
            {
                Opacity = 1,
                Position = new PixelPoint(0, 0)
            };

            try
            {
                window.ShowHidden(stagingPosition);

                Assert.True(window.IsVisible);
                Assert.Equal(0, window.Opacity);
                Assert.Equal(stagingPosition, window.Position);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void FixedFlyoutWidthSetsExactLogicalConstraints() =>
        AvaloniaTestHost.Run(() =>
        {
            TestFlyoutWindow window = new();
            try
            {
                window.SetLogicalWidth(350);

                Assert.Equal(350, window.Width);
                Assert.Equal(350, window.MinWidth);
                Assert.Equal(350, window.MaxWidth);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void ScalingChangeCorrectsNativeSizeWritebackAfterTheNotification() =>
        AvaloniaTestHost.RunAsync(async () =>
        {
            TestFlyoutWindow window = new()
            {
                Content = new Border { Height = 180 }
            };
            try
            {
                window.SetLogicalWidth(350);
                window.ShowHidden(new PixelPoint(0, 0));
                window.UpdateLayout();

                window.ScalingChanged += (_, _) =>
                {
                    // Simulate the native DPI resize arriving after the common scaling notification handler
                    window.Width = 280;
                    window.Height = 90;
                };
                window.SetRenderScaling(1.5);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);

                Assert.Equal(350, window.Width);
                Assert.Equal(350, window.Bounds.Width);
                Assert.Equal(180, window.Bounds.Height);
                Assert.Equal(1, window.ScalingConstraintApplicationCount);
            }
            finally
            {
                window.Close();
            }
        });

    [Theory]
    [InlineData(SizeToContent.Manual, false)]
    [InlineData(SizeToContent.Width, false)]
    [InlineData(SizeToContent.Height, true)]
    [InlineData(SizeToContent.WidthAndHeight, true)]
    public void RestoringAutomaticHeightClearsOnlyHeightToContentConstraints(
        SizeToContent sizeToContent,
        bool shouldClearHeight) =>
        AvaloniaTestHost.Run(() =>
        {
            TestFlyoutWindow window = new()
            {
                SizeToContent = sizeToContent,
                Height = 240
            };

            try
            {
                window.RestoreHeightSizing();

                if (shouldClearHeight)
                    Assert.True(double.IsNaN(window.Height));
                else
                    Assert.Equal(240, window.Height);
            }
            finally
            {
                window.Close();
            }
        });

    private sealed class TestFlyoutWindow : FlyoutWindowCommon
    {
        public int ScalingConstraintApplicationCount { get; private set; }

        public void ShowHidden(PixelPoint stagingPosition) => ShowHiddenForPositioning(stagingPosition);

        public void SetLogicalWidth(double logicalWidth) => SetFixedFlyoutWidth(logicalWidth);

        public void RestoreHeightSizing() => RestoreAutomaticHeightSizing();

        protected override void ApplyRenderScalingLayoutConstraints() => ScalingConstraintApplicationCount++;
    }
}
