using SkiaSharp;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// Renders a cataloged composite glyph through the shared runtime Skia glyph pipeline.
/// </summary>
public sealed class SkiaCompositeGlyphIcon(CompositeGlyph compositeGlyph) : SkiaFlyoutGlyphIcon
{
    private readonly CompositeGlyph _compositeGlyph = compositeGlyph
                                                      ?? throw new ArgumentNullException(nameof(compositeGlyph));

    public CompositeGlyph CompositeGlyph => _compositeGlyph;

    protected override int StateHash => _compositeGlyph.StateHash;

    protected override int? DesignCanvasSize => _compositeGlyph.DesignCanvasSize;

    protected override void RenderGlyph(SKCanvas canvas, int size, SKColor color)
    {
        SKPath? combinedPath = null;
        try
        {
            foreach (CompositeGlyphLayer layer in _compositeGlyph.Layers)
            {
                using SKPath sourcePath = BuildGlyphPathAtLineOrigin(layer.Glyph, size);
                using SKPath transformedPath = TransformPath(
                    sourcePath,
                    layer.ScaleX,
                    layer.ScaleY,
                    centerX: 0.0,
                    centerY: 0.0,
                    layer.TranslateX,
                    layer.TranslateY);

                if (combinedPath == null)
                {
                    combinedPath = new SKPath(transformedPath);
                    continue;
                }

                SKPath unionPath = new();
                if (!combinedPath.Op(transformedPath, SKPathOp.Union, unionPath))
                {
                    unionPath.AddPath(combinedPath);
                    unionPath.AddPath(transformedPath);
                }

                combinedPath.Dispose();
                combinedPath = unionPath;
            }

            if (combinedPath == null || combinedPath.IsEmpty) return;

            SKRect bounds = combinedPath.Bounds;
            float margin = (float)(size * _compositeGlyph.OuterMarginFraction);
            float availableSize = size - margin * 2.0f;
            float scale = Math.Min(availableSize / bounds.Width, availableSize / bounds.Height);
            float translateX = size / 2.0f - bounds.MidX * scale;
            float translateY = size / 2.0f - bounds.MidY * scale;
            using SKPath fittedPath = TransformPath(
                combinedPath,
                scale,
                scale,
                centerX: 0.0,
                centerY: 0.0,
                translateX,
                translateY);
            DrawPath(canvas, fittedPath, color);
        }
        finally
        {
            combinedPath?.Dispose();
        }
    }
}
