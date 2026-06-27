using SkiaSharp;

namespace BrightnessTrayAppDotNET.Visuals;

internal sealed class NightLightBulbGlyphIcon : SkiaFlyoutGlyphIcon
{
    private const int MaskDesignCanvasSize = 64;
    private const double BulbScale = 0.6;
    private const double RayScale = BulbScale * 1.55;
    private const double RayCircleClipScale = 1.35;
    private const double RayTranslateYFraction = -0.08;
    private const double RaySquishY = 0.9;
    private const double RayKeepTopFraction = 0.55;
    private const double GlobalTranslateYFraction = 0.04;
    private const SKFontStyleWeight CompositeGlyphWeight = SKFontStyleWeight.ExtraBold;

    public double GlyphScale { get; init; } = 1.25;
    public double VerticalOffset { get; init; }

    protected override int StateHash => HashCode.Combine(GlyphScale, VerticalOffset);

    protected override int? DesignCanvasSize => MaskDesignCanvasSize;

    protected override void RenderGlyph(SKCanvas canvas, int size, SKColor color)
    {
        double enlargedSize = size * RayScale;
        double translateY = size * RayTranslateYFraction;
        double center = size / 2.0;

        using SKPath rays = BuildCenteredGlyphLinePath(
            GlyphCatalog.ECLIPSED_SUN,
            enlargedSize,
            size,
            CompositeGlyphWeight,
            translateY: translateY);
        using SKPath sunCircle = BuildBoundsCenteredGlyphPath(
            GlyphCatalog.FILLED_CIRCLE_SMALL,
            enlargedSize * RayCircleClipScale,
            size,
            CompositeGlyphWeight,
            translateY: translateY);
        using SKPath raysMinusCircle = Op(rays, sunCircle, SKPathOp.Difference);

        double sunCenterY = center + translateY;
        using SKPath squishedRays = TransformPath(raysMinusCircle, 1.0, RaySquishY, center, sunCenterY);
        using SKPath topPortion = RectPath(0, 0, size, (float)(size * RayKeepTopFraction));
        using SKPath topRays = Op(squishedRays, topPortion, SKPathOp.Intersect);

        using SKPath bulb = BuildCenteredGlyphLinePath(
            GlyphCatalog.LIGHTBULB,
            size * BulbScale,
            size,
            CompositeGlyphWeight);

        double globalTranslateY = size * (GlobalTranslateYFraction + VerticalOffset);
        using SKPath transformedRays = TransformPath(
            topRays,
            GlyphScale,
            GlyphScale,
            center,
            center,
            0,
            globalTranslateY);
        using SKPath transformedBulb = TransformPath(
            bulb,
            GlyphScale,
            GlyphScale,
            center,
            center,
            0,
            globalTranslateY);

        DrawPath(canvas, transformedRays, color);
        DrawPath(canvas, transformedBulb, color);
    }
}
