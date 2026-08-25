using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.UI;
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
    public void SubmenuPositionOpensBesideOwnerWhenRightSideHasSpace()
    {
        PixelRect workArea = new(0, 0, 1000, 800);
        PixelRect ownerBounds = new(200, 300, 160, 30);
        PixelSize menuSize = new(180, 200);

        PixelPoint position = TrayMenuWindow.ResolveSubmenuPosition(
            workArea,
            ownerBounds,
            menuSize,
            edgePadding: 8);

        Assert.Equal(new PixelPoint(360, 300), position);
    }

    [Fact]
    public void SubmenuPositionFlipsLeftAndClampsToBottomEdge()
    {
        PixelRect workArea = new(0, 0, 1000, 800);
        PixelRect ownerBounds = new(900, 750, 80, 30);
        PixelSize menuSize = new(180, 200);

        PixelPoint position = TrayMenuWindow.ResolveSubmenuPosition(
            workArea,
            ownerBounds,
            menuSize,
            edgePadding: 8);

        Assert.Equal(new PixelPoint(720, 592), position);
    }

    [Fact]
    public void ScreenPointPositionClampsMenuInsideWorkArea()
    {
        PixelRect workArea = new(100, 100, 500, 400);
        PixelSize menuSize = new(180, 200);

        PixelPoint insidePosition = TrayMenuWindow.ResolveScreenPointPosition(
            workArea,
            new PixelPoint(250, 180),
            menuSize,
            edgePadding: 8);
        PixelPoint clampedPosition = TrayMenuWindow.ResolveScreenPointPosition(
            workArea,
            new PixelPoint(590, 490),
            menuSize,
            edgePadding: 8);

        Assert.Equal(new PixelPoint(250, 180), insidePosition);
        Assert.Equal(new PixelPoint(412, 292), clampedPosition);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 500)]
    [InlineData(100, 1000)]
    public void ScrollHereCentersThumbAtRequestedTrackPoint(double pointerAxis, double expectedOffset)
    {
        double offset = SettingsScrollBar.CalculateScrollHereOffset(
            pointerAxis,
            trackLength: 100,
            buttonLength: 10,
            thumbLength: 20,
            maximumOffset: 1000);

        Assert.Equal(expectedOffset, offset);
    }

    [Fact]
    public void ScrollBarContextMenuUsesOrientationSpecificCommands() =>
        AvaloniaTestHost.Run(() =>
        {
            SettingsPalette palette = Palette();
            SettingsScrollBarStyle style = new(
                TrackThickness: 20,
                IdleThumbThickness: 4,
                HoverThumbThickness: 12,
                ThumbEndMargin: 4,
                MinimumThumbLength: 24,
                TrackColor: Colors.Transparent,
                IdleThumbColor: Colors.Gray,
                HoverThumbColor: Colors.LightGray,
                DragThumbColor: Colors.White,
                ArrowColor: Colors.White,
                ShowButtonsOnHover: true);
            TrayMenuWindowOptions options = new() { Palette = palette };
            using SettingsScrollBar verticalScrollBar = new(
                Orientation.Vertical,
                style,
                TrayAppDotNETCursors.Arrow,
                options);
            using SettingsScrollBar horizontalScrollBar = new(
                Orientation.Horizontal,
                style,
                TrayAppDotNETCursors.Arrow,
                options);

            string[] verticalCommands = verticalScrollBar.BuildContextMenuEntries(pointerAxis: 50)
                .Select(entry => entry.Text)
                .ToArray();
            string[] horizontalCommands = horizontalScrollBar.BuildContextMenuEntries(pointerAxis: 50)
                .Select(entry => entry.Text)
                .ToArray();

            Assert.Equal(
                ["Scroll Here", "Top", "Bottom", "Page Up", "Page Down", "Scroll Up", "Scroll Down"],
                verticalCommands);
            Assert.Equal(
                [
                    "Scroll Here", "Left Edge", "Right Edge", "Page Left", "Page Right", "Scroll Left",
                    "Scroll Right"
                ],
                horizontalCommands);
        });

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
