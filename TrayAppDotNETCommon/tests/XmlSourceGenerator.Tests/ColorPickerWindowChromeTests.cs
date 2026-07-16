using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class ColorPickerWindowChromeTests
{
    [Theory]
    [InlineData(true, 8)]
    [InlineData(false, 0)]
    public void PickerUsesTransparentCustomChrome(bool enableRoundedCorners, double expectedRadius) =>
        AvaloniaTestHost.Run(() =>
        {
            using TrayAppDotNETColorPickerWindow picker = new(
                "Color",
                hasAlpha: true,
                Colors.Blue,
                Colors.Red,
                Palette(),
                Strings(),
                enableRoundedCorners);

            Assert.Same(Brushes.Transparent, picker.Background);
            Assert.Contains(WindowTransparencyLevel.Transparent, picker.TransparencyLevelHint);

            Border root = Assert.IsType<Border>(picker.Content);
            Assert.Equal(new CornerRadius(expectedRadius), root.CornerRadius);
            Assert.Equal(enableRoundedCorners, root.ClipToBounds);
        });

    private static TrayAppDotNETColorPickerStrings Strings() =>
        new(
            "Color",
            "Close",
            "Hue",
            "Alpha",
            "Red",
            "Green",
            "Blue",
            "RGBA",
            "ARGB",
            "Default",
            "Reset");

    private static SettingsPalette Palette() => new(
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
