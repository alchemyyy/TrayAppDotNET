using Avalonia;

namespace TrayAppDotNETCommon.UI.Controls.Maps;

public readonly record struct MapViewportTransform(double Scale, Vector Offset)
{
    public static readonly MapViewportTransform Identity = new(1.0, default);

    public Point ViewportToMap(Point viewport) =>
        new(
            (viewport.X - Offset.X) / Scale,
            (viewport.Y - Offset.Y) / Scale);

    public Point MapToViewport(Point map) =>
        new(
            (map.X * Scale) + Offset.X,
            (map.Y * Scale) + Offset.Y);

    public MapViewportTransform ZoomAt(Point viewport, double factor, double minimumScale, double maximumScale)
    {
        Point mapBefore = ViewportToMap(viewport);
        double nextScale = Math.Clamp(Scale * factor, minimumScale, maximumScale);
        Vector nextOffset = new(
            viewport.X - (mapBefore.X * nextScale),
            viewport.Y - (mapBefore.Y * nextScale));
        return new MapViewportTransform(nextScale, nextOffset);
    }

    public MapViewportTransform Pan(Vector delta) =>
        this with { Offset = Offset + delta };

    public static Vector EdgeAutoPanVelocity(
        Point viewport,
        Size viewportSize,
        double edgeFraction,
        double peakSpeed)
    {
        double thresholdX = viewportSize.Width * edgeFraction;
        double thresholdY = viewportSize.Height * edgeFraction;
        double dx = 0.0;
        double dy = 0.0;

        if (thresholdX > 0.0)
        {
            if (viewport.X < thresholdX)
                dx = peakSpeed * EdgeRamp(1.0 - (viewport.X / thresholdX));
            else if (viewport.X > viewportSize.Width - thresholdX)
                dx = -peakSpeed * EdgeRamp((viewport.X - (viewportSize.Width - thresholdX)) / thresholdX);
        }

        if (thresholdY > 0.0)
        {
            if (viewport.Y < thresholdY)
                dy = peakSpeed * EdgeRamp(1.0 - (viewport.Y / thresholdY));
            else if (viewport.Y > viewportSize.Height - thresholdY)
                dy = -peakSpeed * EdgeRamp((viewport.Y - (viewportSize.Height - thresholdY)) / thresholdY);
        }

        return new Vector(dx, dy);
    }

    private static double EdgeRamp(double depth)
    {
        double d = Math.Clamp(depth, 0.0, 1.0);
        return d * d;
    }
}
