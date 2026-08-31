using Avalonia;
using TrayAppDotNETCommon.UI.Tray;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class TrayPopupPositioningTests
{
    private const int EdgePadding = 8;

    [Fact]
    public void TopTaskbarPlacesPopupBelowTaskbar()
    {
        PixelRect workArea = new(x: 0, y: 48, width: 1920, height: 1032);
        PixelRect iconRect = new(x: 948, y: 8, width: 24, height: 24);
        PixelSize popupSize = new(width: 350, height: 600);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(x: 785, y: 56), position);
    }

    [Fact]
    public void BottomTaskbarPlacesPopupAboveTaskbar()
    {
        PixelRect workArea = new(x: 0, y: 0, width: 1920, height: 1032);
        PixelRect iconRect = new(x: 948, y: 1040, width: 24, height: 24);
        PixelSize popupSize = new(width: 350, height: 600);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(x: 785, y: 424), position);
    }

    [Fact]
    public void LeftTaskbarPlacesPopupRightOfTaskbar()
    {
        PixelRect workArea = new(x: 48, y: 0, width: 1872, height: 1080);
        PixelRect iconRect = new(x: 8, y: 528, width: 24, height: 24);
        PixelSize popupSize = new(width: 350, height: 400);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(x: 56, y: 340), position);
    }

    [Fact]
    public void RightTaskbarPlacesPopupLeftOfTaskbar()
    {
        PixelRect workArea = new(x: 0, y: 0, width: 1872, height: 1080);
        PixelRect iconRect = new(x: 1880, y: 528, width: 24, height: 24);
        PixelSize popupSize = new(width: 350, height: 400);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(x: 1514, y: 340), position);
    }

    [Fact]
    public void NegativeCoordinateMonitorRetainsPhysicalCoordinates()
    {
        PixelRect workArea = new(x: -1920, y: 40, width: 1920, height: 1040);
        PixelRect iconRect = new(x: -1000, y: 8, width: 24, height: 24);
        PixelSize popupSize = new(width: 400, height: 500);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(x: -1188, y: 48), position);
    }

    [Fact]
    public void MissingIconUsesBottomRightFallback()
    {
        PixelRect workArea = new(x: -1920, y: 0, width: 1920, height: 1040);
        PixelSize popupSize = new(width: 350, height: 500);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            trayIconRect: null,
            EdgePadding);

        Assert.Equal(new PixelPoint(x: -358, y: 532), position);
    }

    [Fact]
    public void OversizedPopupCollapsesToMinimumPaddedPosition()
    {
        PixelRect workArea = new(x: 100, y: 100, width: 200, height: 100);
        PixelRect iconRect = new(x: 180, y: 90, width: 24, height: 24);
        PixelSize popupSize = new(width: 400, height: 300);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(x: 108, y: 108), position);
    }

    [Fact]
    public void FloatingPopupClampsToSavedMonitorWorkArea()
    {
        PixelRect workArea = new(x: -1920, y: 40, width: 1920, height: 1040);
        PixelSize popupSize = new(width: 400, height: 500);

        PixelPoint position = TrayPopupPositioning.ClampToWorkArea(
            workArea,
            popupSize,
            new PixelPoint(x: -2100, y: 900),
            EdgePadding);

        Assert.Equal(new PixelPoint(x: -1912, y: 572), position);
    }

    [Fact]
    public void FloatingPopupInsideWorkAreaKeepsSavedPosition()
    {
        PixelRect workArea = new(x: 0, y: 0, width: 1920, height: 1040);
        PixelSize popupSize = new(width: 350, height: 500);
        PixelPoint savedPosition = new(x: 700, y: 300);

        PixelPoint position = TrayPopupPositioning.ClampToWorkArea(
            workArea,
            popupSize,
            savedPosition,
            EdgePadding);

        Assert.Equal(savedPosition, position);
    }
}
