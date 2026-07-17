using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Tray;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class TrayMenuWindowTests
{
    [Fact]
    public void OverlayPositionUsesSpaceBelowTopMountedAnchor()
    {
        PixelRect containingBounds = new(100, 100, 350, 500);
        PixelRect anchorBounds = new(210, 100, 37, 38);
        PixelSize menuSize = new(180, 80);

        PixelPoint position = TrayMenuWindow.ResolveOverlayPosition(
            containingBounds,
            anchorBounds,
            menuSize);

        Assert.Equal(new PixelPoint(210, 138), position);
        Assert.Equal(
            containingBounds.Bottom - anchorBounds.Bottom,
            TrayMenuWindow.ResolveOverlayAvailableHeight(containingBounds, anchorBounds));
    }

    [Fact]
    public void OverlayPositionUsesSpaceAboveBottomMountedAnchorAndStaysInsideFlyout()
    {
        PixelRect containingBounds = new(100, 100, 350, 500);
        PixelRect anchorBounds = new(430, 562, 37, 38);
        PixelSize menuSize = new(180, 80);

        PixelPoint position = TrayMenuWindow.ResolveOverlayPosition(
            containingBounds,
            anchorBounds,
            menuSize);

        Assert.Equal(new PixelPoint(270, 482), position);
        Assert.Equal(
            anchorBounds.Y - containingBounds.Y,
            TrayMenuWindow.ResolveOverlayAvailableHeight(containingBounds, anchorBounds));
    }

    [Fact]
    public void PointerReleaseSelectionKeepsMenuOpenThroughAction() =>
        AvaloniaTestHost.Run(() =>
        {
            bool invoked = false;
            bool wasVisibleDuringAction = false;
            TrayMenuWindow? menu = null;
            menu = new TrayMenuWindow(
                [
                    new TrayMenuEntry(
                        "Add Group Card",
                        () =>
                        {
                            invoked = true;
                            wasVisibleDuringAction = menu!.IsVisible;
                        })
                ],
                new TrayMenuWindowOptions
                {
                    Palette = Palette(),
                    InvokeOnPointerReleased = true,
                    InvokeBeforeClose = true
                });

            try
            {
                menu.Show();
                menu.UpdateLayout();
                Point itemPoint = new(menu.Bounds.Width / 2, menu.Bounds.Height / 2);
                menu.MouseMove(itemPoint, RawInputModifiers.None);
                menu.MouseDown(itemPoint, MouseButton.Left, RawInputModifiers.None);

                Assert.False(invoked);
                Assert.True(menu.IsVisible);

                menu.MouseUp(itemPoint, MouseButton.Left, RawInputModifiers.None);

                Assert.True(invoked);
                Assert.True(wasVisibleDuringAction);
                Assert.True(menu.ClosedFromSelection);
                Assert.False(menu.ClosedFromDeactivation);
                Assert.False(menu.IsVisible);
            }
            finally
            {
                if (menu.IsVisible)
                    menu.Close();
            }
        });

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
