using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class ColorPickerWindowChromeTests
{
    [Theory]
    [InlineData(true, 8)]
    [InlineData(false, 0)]
    public void PickerUsesTransparentCustomChrome(bool enableRoundedCorners, double expectedRadius) =>
        AvaloniaTestHost.Run(() =>
        {
            using TrayAppDotNETColorPickerWindow picker = new(
                title: "Color",
                hasAlpha: true,
                Colors.Blue,
                Colors.Red,
                TestPalettes.Default(),
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
            DefaultTitle: "Color",
            CloseTooltip: "Close",
            HueLabel: "Hue",
            AlphaLabel: "Alpha",
            RedLabel: "Red",
            GreenLabel: "Green",
            BlueLabel: "Blue",
            RgbaHexLabel: "RGBA",
            ArgbHexLabel: "ARGB",
            DefaultButton: "Default",
            ResetButton: "Reset");
}
