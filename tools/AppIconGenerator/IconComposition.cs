using SkiaSharp;

namespace TrayAppDotNET.Tools.AppIconGenerator;

/// <summary>Owns the prepared path layers for one icon target.</summary>
internal sealed class IconComposition : IDisposable
{
    private const float OuterMarginFraction = 0.055f;
    private static readonly SKColor ForegroundColor = SKColors.White;

    private readonly List<SKPath> _paths;
    private readonly SKRect _bounds;

    private IconComposition(List<SKPath> paths, SKRect bounds)
    {
        _paths = paths;
        _bounds = bounds;
    }

    /// <summary>Loads and prepares every SVG layer in a target.</summary>
    public static IconComposition Create(IconTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Layers.Count == 0)
            throw new ArgumentException("An icon target must contain at least one SVG layer.", nameof(target));

        List<SKPath> paths = new(target.Layers.Count);
        try
        {
            SKRect bounds = SKRect.Empty;
            foreach (SVGIconLayer layer in target.Layers)
            {
                using SVGDocument document = SVGDocument.LoadEmbedded(layer.ResourceFileName);
                SKPath path = document.CreateNormalizedPath(layer.Destination);
                if (path.IsEmpty)
                {
                    path.Dispose();
                    throw new InvalidDataException(
                        $"SVG layer '{layer.ResourceFileName}' produced no drawable geometry.");
                }

                paths.Add(path);
                bounds = paths.Count == 1 ? path.Bounds : SKRect.Union(bounds, path.Bounds);
            }

            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidDataException($"Target '{target.ShortName}' has invalid geometry bounds.");

            return new IconComposition(paths, bounds);
        }
        catch
        {
            DisposePaths(paths);
            throw;
        }
    }

    /// <summary>Renders a native-size antialiased PNG frame.</summary>
    public byte[] RenderPNG(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, other: 1);

        SKImageInfo imageInfo = new(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKBitmap bitmap = new(imageInfo);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        float margin = size * OuterMarginFraction;
        float availableSize = size - margin * 2.0f;
        float scale = Math.Min(availableSize / _bounds.Width, availableSize / _bounds.Height);
        float translateX = size / 2.0f - _bounds.MidX * scale;
        float translateY = size / 2.0f - _bounds.MidY * scale;
        SKMatrix transform = SKMatrix.CreateScaleTranslation(scale, scale, translateX, translateY);

        using SKPaint paint = new()
        {
            Color = ForegroundColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        foreach (SKPath sourcePath in _paths)
        {
            using SKPath renderedPath = new();
            sourcePath.Transform(transform, renderedPath);
            canvas.DrawPath(renderedPath, paint);
        }

        canvas.Flush();
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encodedImage = image.Encode(SKEncodedImageFormat.Png, quality: 100)
                                    ?? throw new InvalidOperationException("Skia failed to encode an icon PNG.");
        return encodedImage.ToArray();
    }

    public void Dispose() => DisposePaths(_paths);

    private static void DisposePaths(IEnumerable<SKPath> paths)
    {
        foreach (SKPath path in paths)
            path.Dispose();
    }
}
