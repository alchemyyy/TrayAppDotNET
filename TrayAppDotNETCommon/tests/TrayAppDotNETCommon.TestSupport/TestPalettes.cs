using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.TestSupport;

/// <summary>Settings palettes for tests: distinct colors for every role, so a wrong role shows as a wrong color.</summary>
public static class TestPalettes
{
    /// <summary>The default dark test palette; pass a color to replace that role only.</summary>
    public static SettingsPalette Default(
        Color? background = null,
        Color? foreground = null,
        Color? hoverDeep = null,
        Color? pressedDeep = null,
        Color? controlBackgroundDeep = null) =>
        new(
            background ?? Colors.Black,
            foreground ?? Colors.White,
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
}
