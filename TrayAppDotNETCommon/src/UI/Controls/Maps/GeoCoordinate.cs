namespace TrayAppDotNETCommon.UI.Controls.Maps;

public readonly record struct GeoCoordinate(double Latitude, double Longitude)
{
    public static readonly GeoCoordinate Zero = new(Latitude: 0.0, Longitude: 0.0);

    public bool IsWithinWorldBounds =>
        Latitude is >= -90.0 and <= 90.0 &&
        Longitude is >= -180.0 and <= 180.0;

    public GeoCoordinate ClampToWorld() =>
        new(
            Math.Clamp(Latitude, min: -90.0, max: 90.0),
            Math.Clamp(Longitude, min: -180.0, max: 180.0));
}
