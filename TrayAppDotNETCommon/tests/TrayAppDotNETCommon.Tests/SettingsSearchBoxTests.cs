using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class SettingsSearchBoxTests
{
    [Fact]
    public void SearchSurfaceUsesDeepPaletteColors() => AvaloniaTestHost.Run(() =>
    {
        SettingsPalette palette = TestPalettes.Default(
            hoverDeep: Colors.DarkCyan,
            pressedDeep: Colors.DarkMagenta,
            controlBackgroundDeep: Colors.DarkGreen);
        using SettingsSearchBox searchBox = new(palette, placeholderText: "Search");
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
        SettingsPalette palette = TestPalettes.Default(
            hoverDeep: Colors.DarkCyan,
            pressedDeep: Colors.DarkMagenta,
            controlBackgroundDeep: Colors.DarkGreen);
        using SettingsSearchBox searchBox = new(palette, placeholderText: "Search");
        TextBox textBox = Assert.Single(searchBox.Children.OfType<TextBox>());
        SolidColorBrush background = Assert.IsType<SolidColorBrush>(textBox.Background);

        palette.UpdateFrom(TestPalettes.Default(
            hoverDeep: Colors.Cyan,
            pressedDeep: Colors.Magenta,
            controlBackgroundDeep: Colors.Green));

        Assert.Same(background, textBox.Background);
        Assert.Equal(Colors.Green, background.Color);
        AssertBrushColor(Colors.Cyan, textBox.Resources["TextControlBackgroundPointerOver"]);
        AssertBrushColor(Colors.Magenta, textBox.Resources["TextControlBackgroundFocused"]);
    });

    private static void AssertBrushColor(Color expected, object? resource)
    {
        SolidColorBrush brush = Assert.IsType<SolidColorBrush>(resource);
        Assert.Equal(expected, brush.Color);
    }
}
