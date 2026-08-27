using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Tray;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class SettingsVerticalScrollViewportTests
{
    [Fact]
    public void ReservesOnlyTheRightScrollbarTrack() => AvaloniaTestHost.Run(() =>
    {
        Border content = new();
        Color background = Color.FromRgb(0x19, 0x19, 0x19);
        SettingsScrollBarStyle style = CreateStyle(trackThickness: 16, hoverThumbThickness: 9);
        TrayMenuWindowOptions contextMenuOptions = new() { Palette = CreatePalette() };
        using SettingsVerticalScrollViewport viewport = new(
            content,
            new Thickness(3),
            background,
            style,
            contextMenuOptions);

        Assert.Equal(2, viewport.ColumnDefinitions.Count);
        Assert.Single(viewport.RowDefinitions);

        ScrollViewer scrollViewer = Assert.Single(viewport.Children.OfType<ScrollViewer>());
        Assert.Equal(0, Grid.GetColumn(scrollViewer));
        Assert.Equal(0, Grid.GetRow(scrollViewer));
        Assert.Equal(ScrollBarVisibility.Disabled, scrollViewer.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Hidden, scrollViewer.VerticalScrollBarVisibility);

        Border contentHost = Assert.IsType<Border>(scrollViewer.Content);
        Assert.Same(content, contentHost.Child);
        Assert.Equal(new Thickness(3), contentHost.Padding);
        SolidColorBrush backgroundBrush = Assert.IsType<SolidColorBrush>(contentHost.Background);
        Assert.Equal(background, backgroundBrush.Color);

        SettingsScrollBar scrollBar = Assert.Single(viewport.Children.OfType<SettingsScrollBar>());
        Assert.Equal(1, Grid.GetColumn(scrollBar));
        Assert.Equal(0, Grid.GetRow(scrollBar));
        Assert.Equal(11, scrollBar.Width);
    });

    [Fact]
    public void StyleChangesPreserveTheReservedTrackStructure() => AvaloniaTestHost.Run(() =>
    {
        TrayMenuWindowOptions contextMenuOptions = new() { Palette = CreatePalette() };
        using SettingsVerticalScrollViewport viewport = new(
            new Border(),
            default,
            Colors.Black,
            CreateStyle(trackThickness: 16, hoverThumbThickness: 9),
            contextMenuOptions);
        SettingsScrollBar scrollBar = Assert.Single(viewport.Children.OfType<SettingsScrollBar>());

        viewport.SetScrollBarStyle(CreateStyle(trackThickness: 20, hoverThumbThickness: 12));

        Assert.Equal(12, scrollBar.Width);
        Assert.Equal(2, viewport.ColumnDefinitions.Count);
        Assert.Single(viewport.RowDefinitions);
        Assert.Equal(1, Grid.GetColumn(scrollBar));
    });

    private static SettingsScrollBarStyle CreateStyle(double trackThickness, double hoverThumbThickness) =>
        new(
            TrackThickness: trackThickness,
            IdleThumbThickness: 4,
            HoverThumbThickness: hoverThumbThickness,
            ThumbEndMargin: 3,
            MinimumThumbLength: 28,
            TrackColor: Colors.Black,
            IdleThumbColor: Colors.Gray,
            HoverThumbColor: Colors.LightGray,
            DragThumbColor: Colors.White,
            ArrowColor: Colors.White,
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
