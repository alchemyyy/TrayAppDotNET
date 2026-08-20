using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.Visuals;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class SettingsNavItemTests
{
    private const string GeneralGlyph = "\uE71D";

    [Fact]
    public void ClassicStylePreservesOriginalIndicatorGutter() => AvaloniaTestHost.Run(() =>
    {
        SettingsPalette palette = CreatePalette(Colors.Black, Colors.White);
        SettingsNavItem item = new(
            "General",
            palette,
            navigationGlyph: Glyph.SegoeFluent(GeneralGlyph));
        item.IsSelected = true;

        Border surface = Assert.IsType<Border>(item.Child);
        Grid row = Assert.IsType<Grid>(surface.Child);
        Border indicator = Assert.Single(row.Children.OfType<Border>());
        TextBlock label = Assert.Single(row.Children.OfType<TextBlock>());
        SolidColorBrush indicatorBrush = Assert.IsType<SolidColorBrush>(indicator.Background);

        Assert.Equal(SettingsUILayout.NavItemPadding, surface.Padding);
        Assert.Equal(SettingsUILayout.NavIndicatorMargin, indicator.Margin);
        Assert.Equal(1, Grid.GetColumn(label));
        Assert.Equal("General", label.Text);
        Assert.Equal(Colors.White, indicatorBrush.Color);
    });

    [Fact]
    public void Windows11StyleUsesEdgeIndicatorAndGlyphSlot() => AvaloniaTestHost.Run(() =>
    {
        SettingsPalette palette = CreatePalette(Colors.Black, Colors.White);
        SettingsNavItem item = new(
            "General",
            palette,
            useWindows11Style: true,
            navigationGlyph: Glyph.SegoeFluent(GeneralGlyph));
        item.IsSelected = true;

        Border surface = Assert.IsType<Border>(item.Child);
        Grid row = Assert.IsType<Grid>(surface.Child);
        Border indicator = Assert.Single(row.Children.OfType<Border>());
        Grid content = Assert.Single(row.Children.OfType<Grid>());
        TextBlock icon = Assert.Single(
            content.Children.OfType<TextBlock>(),
            static textBlock => textBlock.Text == GeneralGlyph);
        TextBlock label = Assert.Single(
            content.Children.OfType<TextBlock>(),
            static textBlock => textBlock.Text == "General");
        SolidColorBrush indicatorBrush = Assert.IsType<SolidColorBrush>(indicator.Background);

        Assert.Equal(SettingsUILayout.Windows11NavItemPadding, surface.Padding);
        Assert.Equal(new Thickness(0), indicator.Margin);
        Assert.Equal(SettingsUILayout.Windows11NavIndicatorColor, indicatorBrush.Color);
        Assert.Equal(SettingsUILayout.Windows11NavContentMargin, content.Margin);
        Assert.Equal(SettingsUILayout.Windows11NavIconSize, icon.Width);
        Assert.Equal(SettingsUILayout.Windows11NavIconSize, icon.Height);
        Assert.Equal(SettingsUILayout.Windows11NavIconFontSize, icon.FontSize);
        Assert.Equal(SettingsUILayout.Windows11NavIconMargin, icon.Margin);
        Assert.Equal(0, Grid.GetColumn(icon));
        Assert.Equal(1, Grid.GetColumn(label));
        Assert.Equal(SettingsUILayout.Windows11NavLabelMargin, label.Margin);
    });

    [Fact]
    public void CustomNavigationIconFollowsPaletteRefresh() => AvaloniaTestHost.Run(() =>
    {
        SettingsPalette palette = CreatePalette(Colors.Black, Colors.White);
        TestSettingsNavigationIcon icon = new() { IconColor = palette.Foreground };
        SettingsNavItem item = new(
            "Environmental",
            palette,
            useWindows11Style: true,
            customNavigationIcon: icon);

        palette.UpdateFrom(CreatePalette(Colors.Black, Colors.Lime));
        item.RefreshPalette();

        Assert.Equal(Colors.Lime, icon.IconColor);
    });

    private sealed class TestSettingsNavigationIcon : Control, ISettingsNavigationIcon
    {
        public Color IconColor { get; set; }
    }

    private static SettingsPalette CreatePalette(Color background, Color foreground) =>
        new(
            background,
            foreground,
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
