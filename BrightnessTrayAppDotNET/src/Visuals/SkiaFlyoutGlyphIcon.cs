using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace BrightnessTrayAppDotNET.Visuals;

internal abstract class SkiaFlyoutGlyphIcon : Control
{
    private const string IconFontFamily = GlyphCatalog.SEGOE_FLUENT_ICONS;

    private Bitmap? _bitmap;
    private BitmapKey _bitmapKey;

    protected SkiaFlyoutGlyphIcon()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
    }

    public Color IconColor
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            ClearBitmap();
            InvalidateVisual();
        }
    } = AppTheme.Default.IconForeground.For(AppTheme.Default.IsLightTheme);

    protected abstract int StateHash { get; }

    protected virtual int? DesignCanvasSize => null;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double logicalSize = Math.Min(Bounds.Width, Bounds.Height);
        if (logicalSize <= 0) return;

        double renderScaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int pixelSize = Math.Max(1, (int)Math.Ceiling(logicalSize * renderScaling));
        Bitmap bitmap = EnsureBitmap(pixelSize);

        double drawSize = pixelSize / renderScaling;
        Rect dest = new(
            (Bounds.Width - drawSize) / 2.0,
            (Bounds.Height - drawSize) / 2.0,
            drawSize,
            drawSize);
        context.DrawImage(bitmap, dest);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearBitmap();
        base.OnDetachedFromVisualTree(e);
    }

    protected void InvalidateIcon()
    {
        ClearBitmap();
        InvalidateVisual();
    }

    protected abstract void RenderGlyph(SKCanvas canvas, int size, SKColor color);

    protected static SKPath BuildCenteredGlyphLinePath(
        string glyph,
        double fontSize,
        int canvasSize,
        SKFontStyleWeight weight = SKFontStyleWeight.Normal,
        double translateX = 0.0,
        double translateY = 0.0)
    {
        using SKFont font = CreateIconFont(fontSize, weight);
        using SKPaint paint = CreateMeasurePaint();
        float advanceWidth = font.MeasureText(glyph, out _, paint);
        font.GetFontMetrics(out SKFontMetrics metrics);
        float lineHeight = metrics.Descent - metrics.Ascent;
        float x = (canvasSize - advanceWidth) / 2.0f + (float)translateX;
        float y = (canvasSize - lineHeight) / 2.0f - metrics.Ascent + (float)translateY;
        return font.GetTextPath(glyph, new SKPoint(x, y));
    }

    protected static SKPath BuildGlyphPathAtLineOrigin(
        string glyph,
        double fontSize,
        SKFontStyleWeight weight = SKFontStyleWeight.Normal)
    {
        using SKFont font = CreateIconFont(fontSize, weight);
        font.GetFontMetrics(out SKFontMetrics metrics);
        return font.GetTextPath(glyph, new SKPoint(0, -metrics.Ascent));
    }

    protected static SKPath BuildBoundsCenteredGlyphPath(
        string glyph,
        double fontSize,
        int canvasSize,
        SKFontStyleWeight weight = SKFontStyleWeight.Normal,
        double translateX = 0.0,
        double translateY = 0.0)
    {
        using SKPath path = BuildGlyphPathAtLineOrigin(glyph, fontSize, weight);
        SKRect bounds = path.Bounds;
        float x = ((canvasSize - bounds.Width) / 2.0f) - bounds.Left + (float)translateX;
        float y = ((canvasSize - bounds.Height) / 2.0f) - bounds.Top + (float)translateY;
        return TransformPath(path, 1.0, 1.0, 0.0, 0.0, x, y);
    }

    protected static SKPath TransformPath(
        SKPath source,
        double scaleX,
        double scaleY,
        double centerX,
        double centerY,
        double translateX = 0.0,
        double translateY = 0.0)
    {
        float tx = (float)(centerX - (scaleX * centerX) + translateX);
        float ty = (float)(centerY - (scaleY * centerY) + translateY);
        SKMatrix transform = SKMatrix.CreateScaleTranslation(
            (float)scaleX,
            (float)scaleY,
            tx,
            ty);
        SKPath result = new();
        source.Transform(transform, result);
        return result;
    }

    protected static void DrawPath(SKCanvas canvas, SKPath path, SKColor color)
    {
        using SKPaint paint = new() { IsAntialias = true, Color = color, Style = SKPaintStyle.Fill, };
        canvas.DrawPath(path, paint);
    }

    protected static SKPath RectPath(float left, float top, float right, float bottom)
    {
        SKPath path = new();
        path.AddRect(new SKRect(left, top, right, bottom));
        return path;
    }

    protected static SKPath Op(SKPath left, SKPath right, SKPathOp op)
    {
        SKPath result = new();
        if (!left.Op(right, op, result) || result.IsEmpty)
        {
            result.Dispose();
            return new SKPath();
        }

        return result;
    }

    private Bitmap EnsureBitmap(int pixelSize)
    {
        BitmapKey key = new(pixelSize, IconColor, StateHash);
        if (_bitmap != null && _bitmapKey == key) return _bitmap;

        ClearBitmap();
        byte[] png = RenderPng(pixelSize, ToSKColor(IconColor));
        using MemoryStream stream = new(png);
        _bitmap = new Bitmap(stream);
        _bitmapKey = key;
        return _bitmap;
    }

    private byte[] RenderPng(int size, SKColor color)
    {
        int designSize = DesignCanvasSize ?? size;
        SKImageInfo info = new(designSize, designSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKBitmap bitmap = new(info);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);
        RenderGlyph(canvas, designSize, color);

        using SKImage source = SKImage.FromBitmap(bitmap);
        if (designSize == size)
        {
            using SKData sourceData = source.Encode(SKEncodedImageFormat.Png, 100);
            return sourceData.ToArray();
        }

        SKImageInfo scaledInfo = new(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface scaledSurface = SKSurface.Create(scaledInfo);
        scaledSurface.Canvas.Clear(SKColors.Transparent);
        scaledSurface.Canvas.DrawImage(
            source,
            new SKRect(0, 0, size, size),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using SKImage image = scaledSurface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private void ClearBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private static SKFont CreateIconFont(double fontSize, SKFontStyleWeight weight) =>
        new(ResolveIconTypeface(weight), (float)fontSize)
        {
            Edging = SKFontEdging.Antialias, Hinting = SKFontHinting.Normal, Subpixel = false,
        };

    private static SKPaint CreateMeasurePaint() => new() { IsAntialias = true, };

    private static SKTypeface ResolveIconTypeface(SKFontStyleWeight weight) =>
        SKTypeface.FromFamilyName(
            IconFontFamily,
            weight,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);

    private static SKColor ToSKColor(Color color) => new(color.R, color.G, color.B, color.A);

    private readonly record struct BitmapKey(int PixelSize, Color IconColor, int StateHash);
}
