using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.ContextMenus;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class SettingsVerticalScrollViewportTests
{
    [Fact]
    public void ReservesOnlyTheRightScrollbarTrack() => AvaloniaTestHost.Run(() =>
    {
        Border content = new();
        Color background = Color.FromRgb(r: 0x19, g: 0x19, b: 0x19);
        SettingsScrollBarStyle style = CreateStyle(trackThickness: 16, hoverThumbThickness: 9);
        ContextMenuWindowOptions contextMenuOptions = new() { Palette = CreatePalette() };
        using SettingsVerticalScrollViewport viewport = new(
            content,
            new Thickness(3),
            background,
            style,
            contextMenuOptions);

        Assert.Equal(expected: 2, viewport.ColumnDefinitions.Count);
        Assert.Single(viewport.RowDefinitions);

        ScrollViewer scrollViewer = Assert.Single(viewport.Children.OfType<ScrollViewer>());
        Assert.Equal(expected: 0, Grid.GetColumn(scrollViewer));
        Assert.Equal(expected: 0, Grid.GetRow(scrollViewer));
        Assert.Equal(ScrollBarVisibility.Disabled, scrollViewer.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Hidden, scrollViewer.VerticalScrollBarVisibility);

        Border contentHost = Assert.IsType<Border>(scrollViewer.Content);
        Assert.Same(content, contentHost.Child);
        Assert.Equal(new Thickness(3), contentHost.Padding);
        SolidColorBrush backgroundBrush = Assert.IsType<SolidColorBrush>(contentHost.Background);
        Assert.Equal(background, backgroundBrush.Color);

        SettingsScrollBar scrollBar = Assert.Single(viewport.Children.OfType<SettingsScrollBar>());
        Assert.Equal(expected: 1, Grid.GetColumn(scrollBar));
        Assert.Equal(expected: 0, Grid.GetRow(scrollBar));
        Assert.Equal(expected: 12, scrollBar.Width);
    });

    [Fact]
    public void StyleChangesPreserveTheReservedTrackStructure() => AvaloniaTestHost.Run(() =>
    {
        ContextMenuWindowOptions contextMenuOptions = new() { Palette = CreatePalette() };
        using SettingsVerticalScrollViewport viewport = new(
            new Border(),
            padding: default,
            Colors.Black,
            CreateStyle(trackThickness: 16, hoverThumbThickness: 9),
            contextMenuOptions);
        SettingsScrollBar scrollBar = Assert.Single(viewport.Children.OfType<SettingsScrollBar>());

        viewport.SetScrollBarStyle(CreateStyle(trackThickness: 20, hoverThumbThickness: 12));

        Assert.Equal(expected: 15, scrollBar.Width);
        Assert.Equal(expected: 2, viewport.ColumnDefinitions.Count);
        Assert.Single(viewport.RowDefinitions);
        Assert.Equal(expected: 1, Grid.GetColumn(scrollBar));
    });

    [Theory]
    [InlineData(Orientation.Vertical)]
    [InlineData(Orientation.Horizontal)]
    public void ScrollbarKeepsAStableHitTestSurfaceAcrossRepeatedPointerEntries(Orientation orientation) =>
        AvaloniaTestHost.Run(() =>
        {
            ContextMenuWindowOptions contextMenuOptions = new() { Palette = CreatePalette() };
            using Cursor cursor = new(StandardCursorType.Arrow);
            using SettingsScrollBar scrollBar = new(
                orientation,
                CreateStyle(trackThickness: 16, hoverThumbThickness: 9),
                cursor,
                contextMenuOptions);
            switch (orientation)
            {
                case Orientation.Vertical:
                    scrollBar.HorizontalAlignment = HorizontalAlignment.Right;
                    scrollBar.VerticalAlignment = VerticalAlignment.Stretch;
                    break;
                case Orientation.Horizontal:
                    scrollBar.HorizontalAlignment = HorizontalAlignment.Stretch;
                    scrollBar.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(orientation));
            }

            Window window = new() { Width = 200, Height = 160, Content = scrollBar };

            try
            {
                window.Show();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                Dispatcher.UIThread.RunJobs();

                Point insideCollapsedTrack = PointAtTrackDepth(window.Bounds, orientation, depth: 2);
                Point insideExpandedTrack = PointAtTrackDepth(window.Bounds, orientation, depth: 14);
                Point outsideExpandedTrack = PointAtTrackDepth(window.Bounds, orientation, depth: 40);
                int pointerEntryCount = 0;
                scrollBar.PointerEntered += (_, _) => pointerEntryCount++;
                Assert.Equal(expected: 12, CrossAxisThickness(scrollBar, orientation));

                window.MouseMove(insideCollapsedTrack, RawInputModifiers.None);
                Assert.True(scrollBar.IsPointerOver);
                Assert.Equal(expected: 1, pointerEntryCount);
                Assert.Equal(expected: 12, CrossAxisThickness(scrollBar, orientation));
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                Dispatcher.UIThread.RunJobs();

                window.MouseMove(insideExpandedTrack, RawInputModifiers.None);
                Assert.True(scrollBar.IsPointerOver);
                Assert.Equal(expected: 1, pointerEntryCount);
                Assert.Equal(expected: 12, CrossAxisThickness(scrollBar, orientation));

                window.MouseMove(outsideExpandedTrack, RawInputModifiers.None);
                Assert.False(scrollBar.IsPointerOver);
                Assert.Equal(expected: 12, CrossAxisThickness(scrollBar, orientation));
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                Dispatcher.UIThread.RunJobs();

                window.MouseMove(insideCollapsedTrack, RawInputModifiers.None);

                Assert.True(scrollBar.IsPointerOver);
                Assert.Equal(expected: 2, pointerEntryCount);
                Assert.Equal(expected: 12, CrossAxisThickness(scrollBar, orientation));
            }
            finally
            {
                window.Close();
            }
        });

    private static double CrossAxisThickness(SettingsScrollBar scrollBar, Orientation orientation) =>
        orientation switch
        {
            Orientation.Vertical => scrollBar.Width,
            Orientation.Horizontal => scrollBar.Height,
            _ => throw new ArgumentOutOfRangeException(nameof(orientation))
        };

    private static Point PointAtTrackDepth(Rect windowBounds, Orientation orientation, double depth) =>
        orientation switch
        {
            Orientation.Vertical => new Point(windowBounds.Width - depth, windowBounds.Height / 2),
            Orientation.Horizontal => new Point(windowBounds.Width / 2, windowBounds.Height - depth),
            _ => throw new ArgumentOutOfRangeException(nameof(orientation))
        };

    private static SettingsScrollBarStyle CreateStyle(double trackThickness, double hoverThumbThickness) =>
        new(
            trackThickness,
            IdleThumbThickness: 4,
            hoverThumbThickness,
            ThumbEndMargin: 3,
            MinimumThumbLength: 28,
            Colors.Black,
            Colors.Gray,
            Colors.LightGray,
            Colors.White,
            Colors.White,
            ShowButtonsOnHover: true);

    private static SettingsPalette CreatePalette() => new(
        Colors.Black,
        Colors.White,
        Colors.Gray,
        Colors.DarkGray,
        Colors.DimGray,
        Colors.Black,
        Colors.DarkGray,
        Colors.LightGray,
        Colors.Gray,
        Colors.Blue,
        Colors.Blue,
        Colors.White,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.Gray,
        Colors.White,
        Colors.Red,
        Colors.DarkRed,
        Colors.White);
}

public sealed class SettingsScrollViewportTests
{
#if DEBUG
    [Fact]
    public void HotReloadOffsetsClampWhenViewportHasNoOverflow() => AvaloniaTestHost.Run(() =>
    {
        ContextMenuWindowOptions contextMenuOptions = new() { Palette = CreatePalette() };
        using SettingsScrollViewport viewport = new(
            new Border(),
            padding: default,
            Colors.Black,
            CreateStyle(),
            contextMenuOptions);

        viewport.SetOffsets(horizontalOffset: 75, verticalOffset: 80);

        Assert.Equal(expected: 0, viewport.HorizontalOffset);
        Assert.Equal(expected: 0, viewport.VerticalOffset);
    });
#endif

    [Fact]
    public void VerticalScrollbarTopInsetDoesNotMoveTheViewportOrHorizontalTrack() =>
        AvaloniaTestHost.Run(() =>
        {
            ContextMenuWindowOptions contextMenuOptions = new() { Palette = CreatePalette() };
            using SettingsScrollViewport viewport = new(
                new Border(),
                padding: default,
                Colors.Black,
                CreateStyle(),
                contextMenuOptions);
            SettingsScrollBar verticalScrollBar = viewport.Children
                .OfType<SettingsScrollBar>()
                .Single(scrollBar => Grid.GetColumn(scrollBar) == 1 && Grid.GetRow(scrollBar) == 0);
            SettingsScrollBar horizontalScrollBar = viewport.Children
                .OfType<SettingsScrollBar>()
                .Single(scrollBar => Grid.GetColumn(scrollBar) == 0 && Grid.GetRow(scrollBar) == 1);
            ScrollViewer scrollViewer = Assert.Single(viewport.Children.OfType<ScrollViewer>());

            viewport.SetVerticalScrollBarTopInset(34);

            Assert.Equal(new Thickness(left: 0, top: 34, right: 0, bottom: 0), verticalScrollBar.Margin);
            Assert.Equal(expected: default, horizontalScrollBar.Margin);
            Assert.Equal(expected: default, scrollViewer.Margin);
        });

    [Fact]
    public void OverlayVerticalScrollbarLetsTheViewportRenderThroughItsColumn() =>
        AvaloniaTestHost.Run(() =>
        {
            ContextMenuWindowOptions contextMenuOptions = new() { Palette = CreatePalette() };
            using SettingsScrollViewport viewport = new(
                new Border(),
                padding: default,
                Colors.Black,
                CreateStyle(),
                contextMenuOptions,
                overlayVerticalScrollBar: true);
            ScrollViewer scrollViewer = Assert.Single(viewport.Children.OfType<ScrollViewer>());

            Assert.Equal(expected: 2, Grid.GetColumnSpan(scrollViewer));
        });

    private static SettingsScrollBarStyle CreateStyle() =>
        new(
            TrackThickness: 16,
            IdleThumbThickness: 4,
            HoverThumbThickness: 9,
            ThumbEndMargin: 3,
            MinimumThumbLength: 28,
            Colors.Black,
            Colors.Gray,
            Colors.LightGray,
            Colors.White,
            Colors.White,
            ShowButtonsOnHover: true);

    private static SettingsPalette CreatePalette() => new(
        Colors.Black,
        Colors.White,
        Colors.Gray,
        Colors.DarkGray,
        Colors.DimGray,
        Colors.Black,
        Colors.DarkGray,
        Colors.LightGray,
        Colors.Gray,
        Colors.Blue,
        Colors.Blue,
        Colors.White,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.Gray,
        Colors.White,
        Colors.Red,
        Colors.DarkRed,
        Colors.White);
}
