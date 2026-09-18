using System.Security.Cryptography;
using System.Text;
using TrayAppDotNETInstaller.Compression;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

/// <summary>
/// Round trips the hand written LZMA coder. These prove the encoder and decoder agree; conformance with the
/// published format is proved separately in LzmaConformanceTests, which decodes streams produced by liblzma.
/// </summary>
public sealed class LzmaRoundTripTests
{
    private const int DeterministicSeed = 20260917;

    /// <summary>lc=3, lp=0, pb=2 packs to 0x5D, the setting every mainstream LZMA encoder defaults to.</summary>
    private const byte LzmaExpectedPropertiesByte = 0x5D;

    [Fact]
    public void Compress_RoundTripsAnEmptyInput()
    {
        byte[] compressed = Compress([], out byte[] properties);

        // With nothing to code the encoder emits no range coded stream at all
        Assert.Empty(compressed);
        Assert.Equal(LzmaExpectedPropertiesByte, properties[0]);
        Assert.Empty(Decompress(compressed, properties, uncompressedLength: 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(16)]
    public void Compress_RoundTripsVeryShortInputs(int length)
    {
        byte[] original = new byte[length];
        for (int index = 0; index < original.Length; index++) original[index] = (byte)(index * 37);

        AssertRoundTrip(original);
    }

    [Fact]
    public void Compress_RoundTripsHighlyRepetitiveInput()
    {
        byte[] original = new byte[200000];
        for (int index = 0; index < original.Length; index++) original[index] = (byte)'A';

        byte[] compressed = AssertRoundTrip(original);

        // A run this long collapses into a handful of long matches
        Assert.True(compressed.Length < 1024, $"A 200 KB run of one byte compressed to {compressed.Length} bytes.");
    }

    [Fact]
    public void Compress_RoundTripsRepeatingText()
    {
        StringBuilder builder = new();
        for (int index = 0; index < 4000; index++)
            builder.Append("the quick brown fox jumps over the lazy dog ");

        byte[] original = Encoding.ASCII.GetBytes(builder.ToString());
        byte[] compressed = AssertRoundTrip(original);

        Assert.True(compressed.Length < original.Length / 50, $"Repeating text compressed only to {compressed.Length} bytes.");
    }

    [Fact]
    public void Compress_RoundTripsIncompressibleRandomInput()
    {
        byte[] original = NextBytes(new Random(DeterministicSeed), 300000);

        byte[] compressed = AssertRoundTrip(original);

        // Random bytes cannot shrink, so the only question is how much the range coder adds. liblzma expands
        // incompressible input by about 1.3 percent on the same settings, so 2 percent is the honest ceiling
        Assert.True(
            compressed.Length < original.Length + (original.Length / 50),
            $"{original.Length} random bytes expanded to {compressed.Length}.");
    }

    [Fact]
    public void Compress_RoundTripsStructuredBinaryInput()
    {
        // Alternating compressible and incompressible regions exercise the parse switching between matches
        // and literals, which is where a state machine mistake would show up
        Random random = new(DeterministicSeed);
        List<byte> builder = [];
        for (int block = 0; block < 60; block++)
        {
            builder.AddRange(NextBytes(random, 2048));
            byte filler = (byte)(block * 11);
            for (int index = 0; index < 4096; index++) builder.Add(filler);
        }

        AssertRoundTrip(builder.ToArray());
    }

    [Fact]
    public void Compress_RoundTripsMatchesLongerThanTheMaximumLength()
    {
        // A single run far longer than the 273 byte maximum forces the parse to chain matches together
        byte[] original = new byte[100000];
        for (int index = 0; index < 64; index++) original[index] = (byte)index;
        for (int index = 64; index < original.Length; index++) original[index] = original[index - 64];

        AssertRoundTrip(original);
    }

    [Fact]
    public void Compress_RoundTripsDistancesBeyondTheAlignedDistanceCutoff()
    {
        // Distances past 128 use direct bits plus the align model, a path short inputs never reach
        Random random = new(DeterministicSeed);
        byte[] prefix = NextBytes(random, 400000);
        byte[] original = new byte[prefix.Length * 2];
        Array.Copy(prefix, sourceIndex: 0, original, destinationIndex: 0, prefix.Length);
        Array.Copy(prefix, sourceIndex: 0, original, prefix.Length, prefix.Length);

        byte[] compressed = AssertRoundTrip(original);

        // The second half is one enormous far away match, so the whole thing codes as barely more than the
        // random prefix
        Assert.True(
            compressed.Length < prefix.Length + prefix.Length / 10,
            $"A duplicated {prefix.Length} byte block compressed to {compressed.Length} bytes.");
    }

    [Fact]
    public void Compress_RoundTripsAnInputLargerThanTheStartingDictionary()
    {
        // Crosses the cyclic match finder chain so stale chain entries are exercised
        Random random = new(DeterministicSeed);
        byte[] pattern = NextBytes(random, 8192);
        byte[] original = new byte[3 * 1024 * 1024];
        for (int index = 0; index < original.Length; index++) original[index] = pattern[index % pattern.Length];

        AssertRoundTrip(original);
    }

    [Fact]
    public void Compress_RoundTripsARealCompiledBinary()
    {
        // The payload the installer actually carries is compiled code, so one real binary is worth more than
        // any synthetic pattern
        byte[] original = File.ReadAllBytes(typeof(LzmaEncoder).Assembly.Location);

        byte[] compressed = AssertRoundTrip(original);

        Assert.True(compressed.Length < original.Length, $"A managed assembly did not compress: {compressed.Length} bytes.");
    }

    [Fact]
    public void Decode_RejectsPropertiesShorterThanTheFormatRequires()
    {
        using MemoryStream input = new([]);
        using MemoryStream output = new();

        Assert.Throws<InvalidDataException>(() => LzmaDecoder.Decode(input, output, new byte[4], uncompressedLength: 1));
    }

    [Fact]
    public void Decode_RejectsAnIllegalPropertiesByte()
    {
        byte[] properties = [0xFF, 0x00, 0x00, 0x01, 0x00];
        using MemoryStream input = new([]);
        using MemoryStream output = new();

        Assert.Throws<InvalidDataException>(() => LzmaDecoder.Decode(input, output, properties, uncompressedLength: 1));
    }

    [Fact]
    public void Decode_RejectsAStreamThatEndsEarly()
    {
        byte[] original = Encoding.ASCII.GetBytes(new string('z', 100000));
        byte[] compressed = Compress(original, out byte[] properties);
        byte[] truncated = new byte[compressed.Length / 2];
        Array.Copy(compressed, sourceIndex: 0, truncated, destinationIndex: 0, truncated.Length);

        using MemoryStream input = new(truncated);
        using MemoryStream output = new();

        Assert.Throws<EndOfStreamException>(() => LzmaDecoder.Decode(input, output, properties, original.Length));
    }

    [Fact]
    public void Decode_RejectsCorruptedCompressedBytes()
    {
        byte[] original = NextBytes(new Random(DeterministicSeed), 40000);
        byte[] compressed = Compress(original, out byte[] properties);
        // Flipping a byte early in the stream desynchronises every symbol after it
        compressed[16] ^= 0xFF;

        using MemoryStream input = new(compressed);
        using MemoryStream output = new();

        Exception? failure = Record.Exception(() => LzmaDecoder.Decode(input, output, properties, original.Length));
        bool rejectedOrDiffered = failure != null || !output.ToArray().SequenceEqual(original);
        Assert.True(rejectedOrDiffered, "A corrupted stream decoded back to the original bytes.");
    }

    private static byte[] AssertRoundTrip(byte[] original)
    {
        byte[] compressed = Compress(original, out byte[] properties);
        Assert.Equal(LzmaExpectedPropertiesByte, properties[0]);

        byte[] decompressed = Decompress(compressed, properties, original.Length);
        Assert.Equal(original.Length, decompressed.Length);
        Assert.Equal(Hash(original), Hash(decompressed));
        return compressed;
    }

    private static byte[] Compress(byte[] original, out byte[] properties)
    {
        using MemoryStream output = new();
        properties = LzmaEncoder.Compress(original, original.Length, output);
        Assert.Equal(5, properties.Length);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] compressed, byte[] properties, long uncompressedLength)
    {
        using MemoryStream input = new(compressed);
        using MemoryStream output = new();
        LzmaDecoder.Decode(input, output, properties, uncompressedLength);
        return output.ToArray();
    }

    private static string Hash(byte[] bytes)
    {
        using SHA256 algorithm = SHA256.Create();
        return BitConverter.ToString(algorithm.ComputeHash(bytes));
    }

    /// <summary>The .NET Framework Random has no NextBytes-returning overload, so the buffer is filled here.</summary>
    private static byte[] NextBytes(Random random, int length)
    {
        byte[] bytes = new byte[length];
        random.NextBytes(bytes);
        return bytes;
    }
}
