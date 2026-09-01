using System.Buffers.Binary;
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

        Assert.Equal(6, targets.Count);
        Assert.Equal(
            ["BATADN", "BTADN", "FCTADN", "NTADN", "TMTADN", "VTADN"],
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
