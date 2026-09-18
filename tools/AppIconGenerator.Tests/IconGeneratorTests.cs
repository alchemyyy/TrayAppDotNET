using System.Buffers.Binary;
using System.Text;
using SkiaSharp;
using TrayAppDotNET.Tools.AppIconGenerator;
using Xunit;

namespace AppIconGenerator.Tests;

public sealed class IconGeneratorTests
{
    private static readonly byte[] PNGSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    [Fact]
    public void CatalogContainsEveryTrayApplication()
    {
        IReadOnlyList<IconTarget> targets = IconTargetCatalog.Create();

        Assert.Equal(7, targets.Count);
        Assert.Equal(
            ["BATADN", "BTADN", "FCTADN", "NTADN", "TMTADN", "VTADN", "TADN"],
            targets.Select(static target => target.ShortName));
    }

    [Fact]
    public void EveryTargetRendersAnExpectedSizePNG()
    {
        const int renderSize = 32;
        IReadOnlyList<IconTarget> targets = IconTargetCatalog.Create();

        foreach (IconTarget target in targets)
        {
            using IconComposition composition = IconComposition.Create(target);
            byte[] PNGBytes = composition.RenderPNG(renderSize);

            Assert.True(PNGBytes.AsSpan(0, PNGSignature.Length).SequenceEqual(PNGSignature));
            Assert.Equal(renderSize, BinaryPrimitives.ReadInt32BigEndian(PNGBytes.AsSpan(16, 4)));
            Assert.Equal(renderSize, BinaryPrimitives.ReadInt32BigEndian(PNGBytes.AsSpan(20, 4)));
        }
    }

    [Fact]
    public void StrokeOnlyPathProducesHollowRingGeometry()
    {
        const string svgMarkup = """
            <svg viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
            <path d="M10 10 H90 V90 H10 Z" style="fill:none;stroke:#ffffff;stroke-width:10" />
            </svg>
            """;
        using MemoryStream svgStream = new(Encoding.UTF8.GetBytes(svgMarkup));
        using SVGDocument document = SVGDocument.Load(svgStream, "stroke-only-square-test.svg");
        using SKPath normalizedPath = document.CreateNormalizedPath(NormalizedRectangle.FullCanvas);
        using SKBitmap bitmap = RenderNormalizedPathToBitmap(normalizedPath, renderSize: 64);

        // The stroke ring leaves the interior hollow: the center pixel must stay transparent.
        Assert.Equal(0, bitmap.GetPixel(32, 32).Alpha);
        // The ring itself, well inside each stroke band, must be opaque on every side.
        Assert.True(bitmap.GetPixel(32, 6).Alpha > 200, "Top edge of the stroke ring should be opaque.");
        Assert.True(bitmap.GetPixel(32, 57).Alpha > 200, "Bottom edge of the stroke ring should be opaque.");
        Assert.True(bitmap.GetPixel(6, 32).Alpha > 200, "Left edge of the stroke ring should be opaque.");
        Assert.True(bitmap.GetPixel(57, 32).Alpha > 200, "Right edge of the stroke ring should be opaque.");
    }

    [Fact]
    public void NonSquareViewBoxIsContainedAndCentered()
    {
        const string svgMarkup = """
            <svg viewBox="0 0 200 100" xmlns="http://www.w3.org/2000/svg">
            <path d="M0 0 H200 V100 H0 Z" fill="#ffffff" />
            </svg>
            """;
        using MemoryStream svgStream = new(Encoding.UTF8.GetBytes(svgMarkup));
        using SVGDocument document = SVGDocument.Load(svgStream, "wide-viewbox-test.svg");
        using SKPath normalizedPath = document.CreateNormalizedPath(NormalizedRectangle.FullCanvas);
        SKRect bounds = normalizedPath.Bounds;

        const float tolerance = 0.0001f;
        // A 2:1 view box must fill the full canvas width and be contained (not stretched) to half
        // the canvas height, centered top-to-bottom rather than pinned to one edge.
        Assert.True(Math.Abs(bounds.Left - 0f) < tolerance, $"Left was {bounds.Left}.");
        Assert.True(Math.Abs(bounds.Right - 1f) < tolerance, $"Right was {bounds.Right}.");
        Assert.True(Math.Abs(bounds.Height - 0.5f) < tolerance, $"Height was {bounds.Height}, expected about half the width.");
        Assert.True(Math.Abs(bounds.Top - 0.25f) < tolerance, $"Top was {bounds.Top}, expected the shorter axis centered.");
        Assert.True(Math.Abs(bounds.Bottom - 0.75f) < tolerance, $"Bottom was {bounds.Bottom}, expected the shorter axis centered.");
    }

    /// <summary>Rasterizes a normalized [0,1] path onto a transparent size-by-size white-fill canvas.</summary>
    private static SKBitmap RenderNormalizedPathToBitmap(SKPath normalizedPath, int renderSize)
    {
        SKImageInfo imageInfo = new(renderSize, renderSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        SKBitmap bitmap = new(imageInfo);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Transparent);

        SKMatrix transform = SKMatrix.CreateScale(renderSize, renderSize);
        using SKPath scaledPath = new();
        normalizedPath.Transform(transform, scaledPath);

        using SKPaint paint = new()
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawPath(scaledPath, paint);
        canvas.Flush();
        return bitmap;
    }

    [Fact]
    public void ICOWriterEncodes256PixelDimensionsAsZero()
    {
        byte[] firstPNG = [.. PNGSignature, 1, 2, 3];
        byte[] secondPNG = [.. PNGSignature, 4, 5, 6, 7];
        List<IconImage> images =
        [
            new IconImage(16, firstPNG),
            new IconImage(256, secondPNG)
        ];
        using MemoryStream stream = new();

        ICOWriter.Write(stream, images);
        byte[] ICOBytes = stream.ToArray();

        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(ICOBytes.AsSpan(0, 2)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(ICOBytes.AsSpan(2, 2)));
        Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(ICOBytes.AsSpan(4, 2)));
        Assert.Equal(16, ICOBytes[6]);
        Assert.Equal(0, ICOBytes[22]);
        Assert.Equal(38, BinaryPrimitives.ReadInt32LittleEndian(ICOBytes.AsSpan(18, 4)));
        Assert.Equal(38 + firstPNG.Length, BinaryPrimitives.ReadInt32LittleEndian(ICOBytes.AsSpan(34, 4)));
    }
}
