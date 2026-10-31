using TrayAppDotNETCommon.Interop;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class IconExtractionPixelTests
{
    [Fact]
    public void AlphaIsSynthesizedFromTheAndMask()
    {
        // Two pixels of colour with no alpha; the AND mask makes the first opaque (black) and the second transparent
        byte[] color = [10, 20, 30, 0, 40, 50, 60, 0];
        byte[] mask = [0, 0, 0, 0, 255, 255, 255, 0];

        Assert.False(IconExtraction.HasAnyAlpha(color));
        IconExtraction.ApplyMaskAlpha(color, mask);

        Assert.Equal(expected: 255, color[3]);
        Assert.Equal(expected: 0, color[7]);
        Assert.Equal(expected: 10, color[0]);
    }

    [Fact]
    public void ExistingAlphaIsDetected() =>
        Assert.True(IconExtraction.HasAnyAlpha([1, 2, 3, 0, 4, 5, 6, 128]));

    [Fact]
    public void MonochromeIconsBecomeBlackWhiteAndTransparent()
    {
        // 1x2 mask pair: top row is the AND mask (transparent), bottom row the XOR mask (white)
        IconPixels pair = new(Width: 2, Height: 2, Bgra:
        [
            255, 255, 255, 0, 0, 0, 0, 0,
            255, 255, 255, 0, 0, 0, 0, 0
        ]);

        IconPixels? converted = IconExtraction.ConvertMonochromeMask(pair);

        Assert.NotNull(converted);
        Assert.Equal(expected: 1, converted.Value.Height);
        Assert.Equal(expected: 0, converted.Value.Bgra[3]);
        Assert.Equal(expected: 255, converted.Value.Bgra[7]);
        Assert.Equal(expected: 0, converted.Value.Bgra[4]);
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(16, 0)]
    [InlineData(257, 16)]
    [InlineData(16, 257)]
    public void OversizedOrEmptyBitmapsAreRefused(int width, int height) =>
        Assert.False(IconExtraction.IsAcceptedSize(width, height, IconExtraction.DefaultMaximumDimension));

    [Fact]
    public void CappedSizeIsAccepted() =>
        Assert.True(IconExtraction.IsAcceptedSize(256, 256, IconExtraction.DefaultMaximumDimension));
}
