using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace TrayAppDotNETCommon.Visuals;

/// <summary>
/// Renders path-based runtime glyph controls with bounded shared bitmap and typeface caches.
/// </summary>
public abstract class SkiaFlyoutGlyphIcon : Control
{
    private const int BitmapCacheCapacity = 128;
    private const string IconFontFamily = GlyphCatalog.SEGOE_FLUENT_ICONS;

    private static readonly Lock s_cacheLock = new();
    private static readonly Dictionary<BitmapKey, BitmapCacheEntry> s_bitmapCache = [];
    private static readonly Dictionary<SKFontStyleWeight, TypefaceCacheEntry> s_typefaceCache = [];
    private static long s_bitmapAccessSequence;
    private static int s_shutdown;

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
            InvalidateVisual();
        }
    } = Colors.White;

    protected abstract int StateHash { get; }

    protected virtual int? DesignCanvasSize => null;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Volatile.Read(ref s_shutdown) != 0) return;

        double logicalSize = Math.Min(Bounds.Width, Bounds.Height);
        if (logicalSize <= 0) return;

        double renderScaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int pixelSize = Math.Max(val1: 1, (int)Math.Ceiling(logicalSize * renderScaling));
        Bitmap? bitmap = GetOrCreateBitmap(pixelSize);
        if (bitmap == null) return;

        double drawSize = pixelSize / renderScaling;
        Rect dest = new(
            (Bounds.Width - drawSize) / 2.0,
            (Bounds.Height - drawSize) / 2.0,
            drawSize,
            drawSize);
        context.DrawImage(bitmap, dest);
    }

    protected void InvalidateIcon() => InvalidateVisual();

    /// <summary>Disposes every shared Skia flyout glyph resource owned by this renderer.</summary>
    public static void DisposeSharedResources()
    {
        if (Interlocked.Exchange(ref s_shutdown, value: 1) != 0) return;

        List<Bitmap> bitmaps = [];
        List<SKTypeface> typefaces = [];
        lock (s_cacheLock)
        {
            foreach (BitmapCacheEntry entry in s_bitmapCache.Values)
                bitmaps.Add(entry.Bitmap);

            foreach (TypefaceCacheEntry entry in s_typefaceCache.Values)
            {
                if (entry.IsOwned)
                    typefaces.Add(entry.Typeface);
            }

            s_bitmapCache.Clear();
            s_typefaceCache.Clear();
            s_bitmapAccessSequence = 0;
        }

        DisposeBitmaps(bitmaps);
        DisposeTypefaces(typefaces);
    }

    protected abstract void RenderGlyph(SKCanvas canvas, int size, SKColor color);

    protected static SKPath BuildCenteredGlyphLinePath(
        Glyph glyph,
        double fontSize,
        int canvasSize,
        SKFontStyleWeight weight = SKFontStyleWeight.Normal,
        double translateX = 0.0,
        double translateY = 0.0)
    {
        using SKFont font = CreateIconFont(fontSize, weight);
        using SKPaint paint = CreateMeasurePaint();
        float advanceWidth = font.MeasureText(glyph.Text, out _, paint);
        font.GetFontMetrics(out SKFontMetrics metrics);
        float lineHeight = metrics.Descent - metrics.Ascent;
        float x = (canvasSize - advanceWidth) / 2.0f + (float)translateX;
        float y = (canvasSize - lineHeight) / 2.0f - metrics.Ascent + (float)translateY;
        return font.GetTextPath(glyph.Text, new SKPoint(x, y));
    }

    protected static SKPath BuildGlyphPathAtLineOrigin(
        Glyph glyph,
        double fontSize,
        SKFontStyleWeight weight = SKFontStyleWeight.Normal)
    {
        using SKFont font = CreateIconFont(fontSize, weight);
        font.GetFontMetrics(out SKFontMetrics metrics);
        return font.GetTextPath(glyph.Text, new SKPoint(x: 0, -metrics.Ascent));
    }

    protected static SKPath BuildBoundsCenteredGlyphPath(
        Glyph glyph,
        double fontSize,
        int canvasSize,
        SKFontStyleWeight weight = SKFontStyleWeight.Normal,
        double translateX = 0.0,
        double translateY = 0.0)
    {
        using SKPath path = BuildGlyphPathAtLineOrigin(glyph, fontSize, weight);
        SKRect bounds = path.Bounds;
        float x = (canvasSize - bounds.Width) / 2.0f - bounds.Left + (float)translateX;
        float y = (canvasSize - bounds.Height) / 2.0f - bounds.Top + (float)translateY;
        return TransformPath(path, scaleX: 1.0, scaleY: 1.0, centerX: 0.0, centerY: 0.0, x, y);
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
        float tx = (float)(centerX - scaleX * centerX + translateX);
        float ty = (float)(centerY - scaleY * centerY + translateY);
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
        using SKPaint paint = new();
        paint.IsAntialias = true;
        paint.Color = color;
        paint.Style = SKPaintStyle.Fill;
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
        if (left.Op(right, op, result) && !result.IsEmpty) return result;
        result.Dispose();
        return new SKPath();
    }

    private Bitmap? GetOrCreateBitmap(int pixelSize)
    {
        if (Volatile.Read(ref s_shutdown) != 0) return null;

        BitmapKey key = new(GetType(), pixelSize, IconColor, StateHash);
        lock (s_cacheLock)
        {
            if (s_shutdown != 0) return null;
            if (s_bitmapCache.TryGetValue(key, out BitmapCacheEntry? cachedEntry))
            {
                cachedEntry.LastAccessSequence = ++s_bitmapAccessSequence;
                return cachedEntry.Bitmap;
            }
        }

        byte[] png = RenderPng(pixelSize, ToSKColor(IconColor));
        using MemoryStream stream = new(png);
        Bitmap bitmap = new(stream);

        Bitmap? result;
        Bitmap? duplicate = null;
        List<Bitmap> retiredBitmaps = [];
        lock (s_cacheLock)
        {
            if (s_shutdown != 0)
            {
                duplicate = bitmap;
                result = null;
            }
            else if (s_bitmapCache.TryGetValue(key, out BitmapCacheEntry? cachedEntry))
            {
                cachedEntry.LastAccessSequence = ++s_bitmapAccessSequence;
                duplicate = bitmap;
                result = cachedEntry.Bitmap;
            }
            else
            {
                s_bitmapCache[key] = new BitmapCacheEntry(bitmap, ++s_bitmapAccessSequence);
                result = bitmap;
                TrimBitmapCache(retiredBitmaps);
            }
        }

        if (duplicate != null) DisposeBitmap(duplicate);
        DisposeBitmaps(retiredBitmaps);
        return result;
    }

    private static void TrimBitmapCache(List<Bitmap> retiredBitmaps)
    {
        while (s_bitmapCache.Count > BitmapCacheCapacity)
        {
            BitmapKey oldestKey = default;
            BitmapCacheEntry? oldestEntry = null;
            foreach ((BitmapKey candidateKey, BitmapCacheEntry candidateEntry) in s_bitmapCache)
            {
                if (oldestEntry != null && candidateEntry.LastAccessSequence >= oldestEntry.LastAccessSequence)
                    continue;
                oldestKey = candidateKey;
                oldestEntry = candidateEntry;
            }

            if (oldestEntry == null) return;
            s_bitmapCache.Remove(oldestKey);
            retiredBitmaps.Add(oldestEntry.Bitmap);
        }
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
            using SKData sourceData = source.Encode(SKEncodedImageFormat.Png, quality: 100);
            return sourceData.ToArray();
        }

        SKImageInfo scaledInfo = new(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface scaledSurface = SKSurface.Create(scaledInfo);
        scaledSurface.Canvas.Clear(SKColors.Transparent);
        scaledSurface.Canvas.DrawImage(
            source,
            new SKRect(left: 0, top: 0, size, size),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using SKImage image = scaledSurface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return data.ToArray();
    }

    private static SKFont CreateIconFont(double fontSize, SKFontStyleWeight weight) =>
        new(GetOrCreateIconTypeface(weight), (float)fontSize)
        {
            Edging = SKFontEdging.Antialias, Hinting = SKFontHinting.Normal, Subpixel = false
        };

    private static SKPaint CreateMeasurePaint() => new() { IsAntialias = true };

    private static SKTypeface GetOrCreateIconTypeface(SKFontStyleWeight weight)
    {
        if (Volatile.Read(ref s_shutdown) != 0) return SKTypeface.Default;

        lock (s_cacheLock)
        {
            if (s_shutdown != 0) return SKTypeface.Default;
            if (s_typefaceCache.TryGetValue(weight, out TypefaceCacheEntry cachedEntry))
                return cachedEntry.Typeface;
        }

        TypefaceCacheEntry resolvedEntry = ResolveIconTypeface(weight);
        lock (s_cacheLock)
        {
            if (s_shutdown != 0)
            {
                if (resolvedEntry.IsOwned) resolvedEntry.Typeface.Dispose();
                return SKTypeface.Default;
            }

            if (s_typefaceCache.TryGetValue(weight, out TypefaceCacheEntry cachedEntry))
            {
                if (resolvedEntry.IsOwned) resolvedEntry.Typeface.Dispose();
                return cachedEntry.Typeface;
            }

            s_typefaceCache[weight] = resolvedEntry;
            return resolvedEntry.Typeface;
        }
    }

    private static TypefaceCacheEntry ResolveIconTypeface(SKFontStyleWeight weight)
    {
        SKTypeface? typeface = SKTypeface.FromFamilyName(
            IconFontFamily,
            weight,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
        if (typeface != null && typeface.FamilyName.Equals(IconFontFamily, StringComparison.OrdinalIgnoreCase))
            return new TypefaceCacheEntry(typeface, IsOwned: true);

        typeface?.Dispose();
        TADNLog.Log("SkiaFlyoutGlyphIcon.ResolveIconTypeface: icon font unavailable; using Skia default typeface");
        return new TypefaceCacheEntry(SKTypeface.Default, IsOwned: false);
    }

    private static void DisposeBitmaps(List<Bitmap> bitmaps)
    {
        foreach (Bitmap bitmap in bitmaps)
            DisposeBitmap(bitmap);
    }

    private static void DisposeBitmap(Bitmap bitmap)
    {
        try { bitmap.Dispose(); }
        catch (Exception exception) { TADNLog.Log($"SkiaFlyoutGlyphIcon.DisposeBitmap: {exception.Message}"); }
    }

    private static void DisposeTypefaces(List<SKTypeface> typefaces)
    {
        foreach (SKTypeface typeface in typefaces)
        {
            try { typeface.Dispose(); }
            catch (Exception exception) { TADNLog.Log($"SkiaFlyoutGlyphIcon.DisposeTypeface: {exception.Message}"); }
        }
    }

    private static SKColor ToSKColor(Color color) => new(color.R, color.G, color.B, color.A);

    private readonly record struct BitmapKey(Type IconType, int PixelSize, Color IconColor, int StateHash);

    private sealed class BitmapCacheEntry(Bitmap bitmap, long lastAccessSequence)
    {
        public Bitmap Bitmap { get; } = bitmap;
        public long LastAccessSequence { get; set; } = lastAccessSequence;
    }

    private readonly record struct TypefaceCacheEntry(SKTypeface Typeface, bool IsOwned);
}
