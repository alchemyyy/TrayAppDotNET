using SkiaSharp;

namespace BrightnessTrayAppDotNET.Visuals;

internal sealed class EnvironmentalCurveGlyphIcon : SkiaFlyoutGlyphIcon
{
    private const int MaskDesignCanvasSize = 64;
    private const double SquareScale = 0.5;
    private const double SquareQuadrantCenter = 0.75;
    private const double SquareNudgeFraction = 0.15;
    private const double CircleScale = 0.75;
    private const double MaskResultScale = 0.85;
    private const double MaskResultShiftLeftFraction = 0.14;
    private const double MaskResultShiftUpFraction = 0.11;
    private const double MoonScale = 0.60;
    private const double MoonShiftLeftFraction = 0.08;
    private const double MoonShiftUpFraction = 0.14;
    private const SKFontStyleWeight CompositeGlyphWeight = SKFontStyleWeight.ExtraBold;

    protected override int StateHash => 0;

    protected override int? DesignCanvasSize => MaskDesignCanvasSize;

    protected override void RenderGlyph(SKCanvas canvas, int size, SKColor color)
    {
        double center = size / 2.0;
        using SKPath sun = BuildCenteredGlyphLinePath(GlyphCatalog.ECLIPSED_SUN, size, size, CompositeGlyphWeight);

        double squareSize = size * SquareScale;
        using SKPath square = BuildScaledGlyphAt(
            GlyphCatalog.FILLED_SQUARE,
            size,
            CompositeGlyphWeight,
            SquareScale,
            SquareScale,
            (size * SquareQuadrantCenter) + (squareSize * SquareNudgeFraction),
            (size * SquareQuadrantCenter) - (squareSize * SquareNudgeFraction));

        using SKPath circle = BuildScaledGlyphAt(
            GlyphCatalog.FILLED_CIRCLE_2,
            size,
            CompositeGlyphWeight,
            CircleScale,
            CircleScale,
            center,
            center);

        using SKPath squareMinusCircle = Op(square, circle, SKPathOp.Difference);
        using SKPath sunMinusSquareMask = Op(sun, squareMinusCircle, SKPathOp.Difference);
        using SKPath shiftedMaskResult = TransformPath(
            sunMinusSquareMask,
            MaskResultScale,
            MaskResultScale,
            center,
            center,
            -size * MaskResultScale * MaskResultShiftLeftFraction,
            -size * MaskResultScale * MaskResultShiftUpFraction);

        using SKPath moon = BuildScaledGlyphBottomRight(
            GlyphCatalog.CRESCENT_MOON,
            size,
            CompositeGlyphWeight,
            MoonScale,
            -size * MoonScale * MoonShiftLeftFraction,
            -size * MoonScale * MoonShiftUpFraction);

        DrawPath(canvas, shiftedMaskResult, color);
        DrawPath(canvas, moon, color);
    }

    private static SKPath BuildScaledGlyphAt(
        string glyph,
        int size,
        SKFontStyleWeight weight,
        double scaleX,
        double scaleY,
        double centerX,
        double centerY)
    {
        using SKPath glyphPath = BuildGlyphPathAtLineOrigin(glyph, size, weight);
        SKRect bounds = glyphPath.Bounds;
        double glyphCenterX = bounds.Left + (bounds.Width / 2.0);
        double glyphCenterY = bounds.Top + (bounds.Height / 2.0);
        return TransformPath(
            glyphPath,
            scaleX,
            scaleY,
            glyphCenterX,
            glyphCenterY,
            centerX - glyphCenterX,
            centerY - glyphCenterY);
    }

    private static SKPath BuildScaledGlyphBottomRight(
        string glyph,
        int size,
        SKFontStyleWeight weight,
        double scale,
        double translateX,
        double translateY)
    {
        using SKPath glyphPath = BuildGlyphPathAtLineOrigin(glyph, size, weight);
        SKRect bounds = glyphPath.Bounds;
        double glyphCenterX = bounds.Left + (bounds.Width / 2.0);
        double glyphCenterY = bounds.Top + (bounds.Height / 2.0);

        using SKPath scaled = TransformPath(glyphPath, scale, scale, glyphCenterX, glyphCenterY);
        SKRect scaledBounds = scaled.Bounds;
        return TransformPath(
            glyphPath,
            scale,
            scale,
            glyphCenterX,
            glyphCenterY,
            size - scaledBounds.Right + translateX,
            size - scaledBounds.Bottom + translateY);
    }
}
