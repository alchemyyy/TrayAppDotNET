using Avalonia;

namespace TrayAppDotNETCommon.UI.Controls.Maps;

/// <summary>
/// Web Mercator-like projection for cropped world maps.
/// The latitude range is configurable because bundled map assets often crop Antarctica.
/// </summary>
public readonly record struct MercatorMapProjection(
    double Width,
    double Height,
    double MinimumLatitude,
    double MaximumLatitude,
    double MinimumLongitude = -180.0,
    double MaximumLongitude = 180.0)
{
    public static MercatorMapProjection FromMapSize(
        double width,
        double height,
        double minimumLatitude = -56.0,
        double maximumLatitude = 84.0) =>
        new(width, height, minimumLatitude, maximumLatitude);

    public Point Project(GeoCoordinate coordinate)
    {
        GeoCoordinate clamped = new(
            Math.Clamp(coordinate.Latitude, MinimumLatitude, MaximumLatitude),
            Math.Clamp(coordinate.Longitude, MinimumLongitude, MaximumLongitude));

        double x = (clamped.Longitude - MinimumLongitude) / (MaximumLongitude - MinimumLongitude) * Width;
        double mercatorTop = LatitudeToMercatorY(MaximumLatitude);
        double mercatorBottom = LatitudeToMercatorY(MinimumLatitude);
        double mercatorLatitude = LatitudeToMercatorY(clamped.Latitude);
        double y = (mercatorTop - mercatorLatitude) / (mercatorTop - mercatorBottom) * Height;
        return new Point(x, y);
    }

    public GeoCoordinate Unproject(Point point)
    {
        double x = Math.Clamp(point.X, 0.0, Width);
        double y = Math.Clamp(point.Y, 0.0, Height);

        double longitude = MinimumLongitude + (x / Width * (MaximumLongitude - MinimumLongitude));
        double mercatorTop = LatitudeToMercatorY(MaximumLatitude);
        double mercatorBottom = LatitudeToMercatorY(MinimumLatitude);
        double mercatorLatitude = mercatorTop - (y / Height * (mercatorTop - mercatorBottom));
        double latitude = MercatorYToLatitude(mercatorLatitude);
        return new GeoCoordinate(latitude, longitude);
    }

    public static double LatitudeToMercatorY(double latitudeDegrees) =>
        Math.Log(Math.Tan((Math.PI / 4.0) + (latitudeDegrees * Math.PI / 180.0 / 2.0)));

    public static double MercatorYToLatitude(double mercatorY) =>
        (2.0 * Math.Atan(Math.Exp(mercatorY)) - (Math.PI / 2.0)) * 180.0 / Math.PI;
}
