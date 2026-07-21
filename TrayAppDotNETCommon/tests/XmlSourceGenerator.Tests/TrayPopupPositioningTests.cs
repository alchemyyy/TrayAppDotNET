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
        PixelRect workArea = new(0, 48, 1920, 1032);
        PixelRect iconRect = new(948, 8, 24, 24);
        PixelSize popupSize = new(350, 600);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(785, 56), position);
    }

    [Fact]
    public void BottomTaskbarPlacesPopupAboveTaskbar()
    {
        PixelRect workArea = new(0, 0, 1920, 1032);
        PixelRect iconRect = new(948, 1040, 24, 24);
        PixelSize popupSize = new(350, 600);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(785, 424), position);
    }

    [Fact]
    public void LeftTaskbarPlacesPopupRightOfTaskbar()
    {
        PixelRect workArea = new(48, 0, 1872, 1080);
        PixelRect iconRect = new(8, 528, 24, 24);
        PixelSize popupSize = new(350, 400);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(56, 340), position);
    }

    [Fact]
    public void RightTaskbarPlacesPopupLeftOfTaskbar()
    {
        PixelRect workArea = new(0, 0, 1872, 1080);
        PixelRect iconRect = new(1880, 528, 24, 24);
        PixelSize popupSize = new(350, 400);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(1514, 340), position);
    }

    [Fact]
    public void NegativeCoordinateMonitorRetainsPhysicalCoordinates()
    {
        PixelRect workArea = new(-1920, 40, 1920, 1040);
        PixelRect iconRect = new(-1000, 8, 24, 24);
        PixelSize popupSize = new(400, 500);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(-1188, 48), position);
    }

    [Fact]
    public void MissingIconUsesBottomRightFallback()
    {
        PixelRect workArea = new(-1920, 0, 1920, 1040);
        PixelSize popupSize = new(350, 500);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            null,
            EdgePadding);

        Assert.Equal(new PixelPoint(-358, 532), position);
    }

    [Fact]
    public void OversizedPopupCollapsesToMinimumPaddedPosition()
    {
        PixelRect workArea = new(100, 100, 200, 100);
        PixelRect iconRect = new(180, 90, 24, 24);
        PixelSize popupSize = new(400, 300);

        PixelPoint position = TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            popupSize,
            iconRect,
            EdgePadding);

        Assert.Equal(new PixelPoint(108, 108), position);
    }
}
