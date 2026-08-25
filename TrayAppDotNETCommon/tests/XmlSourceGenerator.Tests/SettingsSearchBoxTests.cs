using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class SettingsSearchBoxTests
{
    [Fact]
    public void SearchSurfaceUsesDeepPaletteColors() => AvaloniaTestHost.Run(() =>
    {
        SettingsPalette palette = Palette(
            Colors.DarkCyan,
            Colors.DarkMagenta,
            Colors.DarkGreen);
        using SettingsSearchBox searchBox = new(palette, "Search");
        TextBox textBox = Assert.Single(searchBox.Children.OfType<TextBox>());
        SettingsButton clearButton = Assert.Single(searchBox.Children.OfType<SettingsButton>());

        AssertBrushColor(Colors.DarkGreen, textBox.Background);
        AssertBrushColor(Colors.DarkGreen, textBox.Resources["TextControlBackground"]);
        AssertBrushColor(Colors.DarkCyan, textBox.Resources["TextControlBackgroundPointerOver"]);
        AssertBrushColor(Colors.DarkMagenta, textBox.Resources["TextControlBackgroundFocused"]);
        AssertBrushColor(Colors.DarkMagenta, textBox.Resources["TextControlBackgroundPressed"]);
        AssertBrushColor(Colors.DarkGreen, clearButton.Background);
    });

    [Fact]
    public void DeepPaletteRefreshUpdatesExistingSearchBrushes() => AvaloniaTestHost.Run(() =>
    {
        SettingsPalette palette = Palette(Colors.DarkCyan, Colors.DarkMagenta, Colors.DarkGreen);
        using SettingsSearchBox searchBox = new(palette, "Search");
        TextBox textBox = Assert.Single(searchBox.Children.OfType<TextBox>());
        SolidColorBrush background = Assert.IsType<SolidColorBrush>(textBox.Background);

        palette.UpdateFrom(Palette(Colors.Cyan, Colors.Magenta, Colors.Green));

        Assert.Same(background, textBox.Background);
        Assert.Equal(Colors.Green, background.Color);
        AssertBrushColor(Colors.Cyan, textBox.Resources["TextControlBackgroundPointerOver"]);
        AssertBrushColor(Colors.Magenta, textBox.Resources["TextControlBackgroundFocused"]);
    });

    private static SettingsPalette Palette(Color hoverDeep, Color pressedDeep, Color controlBackgroundDeep) =>
        new(
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
            Colors.White,
            hoverDeep,
            pressedDeep,
            controlBackgroundDeep);

    private static void AssertBrushColor(Color expected, object? resource)
    {
        SolidColorBrush brush = Assert.IsType<SolidColorBrush>(resource);
        Assert.Equal(expected, brush.Color);
    }
}
