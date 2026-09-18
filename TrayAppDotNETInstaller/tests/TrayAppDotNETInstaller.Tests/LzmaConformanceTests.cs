using TrayAppDotNETInstaller.Compression;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

/// <summary>
/// Conformance vectors for the hand-written LZMA1 decoder. Every stream here was produced by
/// liblzma through Python's stdlib lzma module, so a pass proves the decoder reads real LZMA1
/// output rather than only round-tripping this repository's own encoder.
/// </summary>
/// <remarks>
/// Producer: lzma.compress(data, format=lzma.FORMAT_ALONE, filters=[{id: FILTER_LZMA1, preset: 6,
/// lc: 3, lp: 0, pb: 2, dict_size: 1 &lt;&lt; 20}]). The 13 byte FORMAT_ALONE header is stripped and only
/// the raw LZMA payload is stored below, because that is what LzmaDecoder.Decode consumes.
/// Every stream ends with the LZMA end-of-stream marker (a match at distance 0xFFFFFFFF).
/// The binary vector samples the first 65536 bytes of a release build of
/// TrayAppDotNETInstaller/bin/Release/TrayAppDotNETInstaller.exe.
/// </remarks>
public sealed class LzmaConformanceTests
{
    // The vector whose compressed payload is longest, so halving it leaves a genuinely partial stream
    private const string TruncationVectorName = "binary";

    /// <summary>
    /// One reference stream: the producer's properties, the true plaintext length, and the hash the
    /// decode has to reproduce.
    /// </summary>
    private sealed record LzmaVector(
        string Name,
        string PropertiesHex,
        long UncompressedLength,
        string CompressedBase64,
        string ExpectedSHA256);

    // The payloads are wrapped across lines because Convert.FromBase64String skips white space

    // empty: 0 bytes in, 10 compressed
    private const string EmptyCompressedBase64 =
        """
        AIP/+///wAAAAA==
        """;

    // single: 1 byte in, 11 compressed
    private const string SingleCompressedBase64 =
        """
        ACDB+////+AAAAA=
        """;

    // text: 2816 bytes in, 70 compressed
    private const string TextCompressedBase64 =
        """
        ADoaCM52x+Xp1gc0w9EOv85V4aq94OSPmAHdjeUHVJ5lJV8nOmp+tNNJAQA09aVUYhClVMXKJnjeJ2KVro0LbB//9LtAAA==
        """;

    // repetitive: 100000 bytes in, 90 compressed
    private const string RepetitiveCompressedBase64 =
        """
        ACDv+7/+o7Fe5fg/sqomVfhocEFwFQ+N/R5MG4pCtxn0aRhxrmYjiopNL6MN2X+m44wjEVPgWRjFdYrid/i2lH8MasDedElk4ulc
        U7IE1rH2RLVb//+5qgAA
        """;

    // random: 8192 bytes in, 8302 compressed
    private const string RandomCompressedBase64 =
        """
        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
        """;

    // mixed: 32768 bytes in, 3408 compressed
    private const string MixedCompressedBase64 =
        """
        ACoVRgEmSPE4n6WLaJg7AkybtAHOuBVmObFDahIM0WJ39w9V7bi/0Ql2ZFPFP7Q5wW5OZls0v3arJPyQIFBNIO2yH+btJQ+TIbu6
        tOZlD3p7zWr0uRkPq8IeS6BCBCddj9CeJqrOSSku7VkNhx16euoB5JG5/XWk48+tK9DY+6T2OMqRFzbFbATDimMW8sU4zgIR5tuk
        1U3CJaZPQ++dj4R1nNFvvvbSg+Q01k15PR4bCWaxdH5/Pz7qtXRwdKvZj/D8waEM06HuKCJjYDJcx8piS4TMfDE4lt5EAcbNVFbE
        tGXlMKW7ajyTTm2xiq7CtzJ5ZrHGnsICKKW6Kd6HSsZLN59YruuUz6C76aiw67gUTmpyL8Y3ihnyFNI+rdCwvUReTTThOgKV6dR6
        vDpjyJmLuWYIn/dazQAsroW6gaJLxKnrg1tfufCLI4kIZ6gZRIKq2eLN37DTG8tkdMcM1HBUknE5h6kauPM2koxA8AJ1pDUHMhtj
        BaWY88LW/L8fSOUAhaq/Vd5L6pjHNTz+Da3IOPa7708KKEbrKgdUevz+ErfcVbMBxMaglxzmyLCI4X/biIbHbd92hvaAAugdjSwE
        JGYF/IvAIn4TsHDfmUlGv28PEtrFIoSMx+rtl9KAGM60oaGhM6eM9d2DALhPzPwjOgAMic2ZdOPXsX6pBEOrxya12OPCCZ2fP1xQ
        M7tDIn4e4Nd7lYF5PoBXNfr6iySi0oScRE5+xUs14XVq4RdCY6JQtQvhE4pX6dAi0BNlxe+7meOM0VTv6adG4WpnekL9CTVcJgDI
        DQ3k5SGlmUUgcVCuAnO/VJQbHRnqsxoAf/ViQSbIIWVuwjRj6JWLgv8wipVsPcOLkadqtlDiLDRQMRu2ZgBnMV182OtM5I7o8dor
        cBy02P0NSDxndfTG7gfnKioR8ctNaPRbUeN3iUH766NQEVpMa+I8b9cEhrWVnEyEd8T4Qztrr/zPw2svm4FuYaI6KRqIXm+5lBtF
        aSds2dGudTYuMG5Q4DwANiak+vEqXZE1ybhSrsUDuhy2Ta8pgOt9F3FzFzNCJIJY+XSorWAp95geRvN9/MHE4DYmmMNPGChjMBKZ
        aYe1bBrztUdOWPIIxxtYD6oZAvb60V7oaKnBt0s42g8YAVa1dlplUHic2mUf3IRy4Pxz7TYLgREn1izlfriOQ5Avy/4fQ5FHb8gM
        kg/ahMKOZVRxg8P/IPCDsK4ubuUr+6Z37VLBuACOFdB1xoiFkMzcPeZF5mTZpdIVSTj1iO4JytCWQznrcfo3D/dg0dTM0lzgKcbK
        mvJSRhKwaJDn0on7iHu8HbZp+wVOuTLlS8pPoUsX37EwG8mU4sJ/q6TfanBigVzvX2oIXUyaag8PJ6TGoJffWJYaV6juQLpl+j2B
        rGyCYnrvTNxwDe6BrGddbcWiIrfo9bsoYVzfFUcJuaBZMYrOosl+USrYOKjbwk2SDudafvEnaSYPv7x3pwMBgDElAulT70Yq97SC
        Bq4C3sDWb58vWE8sBja1CiSkJTNWms/mbeUHlGeO0huQ/055x2khNI7ejGJDnMwJhLmBrXEXQnvA70qg0R/KnN6P3wVPMhgO5YCv
        cBP4lcyGrLvbr7eFUePyVCmA2+94rfqfi6OoMnYPgxB3SOn3duOXhjy+5IXPtPGxKbYTWbMPvSEtKkI+8I1LgJ+HSbIXNISVhFtl
        xB88fFqDR+WQBsVR9igKsPpHyVniAk1WDwmJq2/WOVVmTCPIy3q7tz6YLFckKouJtbaqtFo7lGnUVGavNE+Q4y9Tl6xN5YtHvkiG
        Cgxh59JtYSAF+EFecePk3U66hFeDC2W2asCpt8hrU5dK85rkljV4Ab/nm62jy8DnOrarCBU4bn3/xYYcHqcJFzK7Sr10gDL4Mild
        xdVE1LmIeR9r3y5p5KZY3HLorZfmZeAMsqYiCNyilRvbptI4qd4dz9M8hA11qFZ1qTgTG+RwqChrR9RnyWFD3xROQe42WBtHwktC
        tQEngVUPnvh1Xp5vjzwb4vZrTDg/X62jldA3f0xchVtPmQ2Q72nJ+BmwOXAv5qgbYqLMa6sEnvtFlSed1A0ClRMB3zqht0+JvFMe
        OpW1E9XqaTLwOfWgpnXQVsvmeNjGeVmzySlxbqv5Jl8J489NwAkmm0diGgPUEcv6Xvhk1smRSukiA1rPz/RgQyiZ6gpnNItcTAcr
        ZlfBkgJ58bppJhVOUqz1eydEjFOahhYU5WoQkiwnm50jjJY+NTWzkyqBIerCKJzd+KcWaCeRTUUMjAnlUC0V2S94ijUMzLg9isw6
        ea+MF+SyBYPpdf3V6r+WruMVqwwDsK7SHq/4kORjF30HwHu8qf+X194Og69D4X30zeK1AzGFCxpsuNtgHXRq/JR0tiXYHpEr2OBP
        jCKZi61u2GSpge7UlccMZ1FgGoZDYBJukYqymd/yOL9pVfHXznQ04lMHuyzdFQhDJdONODEqkDh//TNm2MYrXYFvutTfwAywNLpa
        4s1ncbvokqTb9DQ/FjMOseqMhQ7K9ti8wnAJqtg1wmPK76K9vuohLisn0fOdxj8udLsrbAtAbywB1Wy6acQh4pRSt/Tn6hw2ZGMI
        73XPG5N1nefLQS4LhkrVaXRfunn9x5srDgSyUOYst308WhdPMLCRUqiQ+zT8PXK8+oqiS8x7d+ju9EV0UejuGQ5dBUidTQQUo7XD
        IM71WApP8PCHE+y0BdCjRzmkAyx6sErHQVbSYD+e/2WAQwRQa6g0PvprjLjWHRn3q/OXNRu5brju+aUvkzILfopO+RjuOsRK9mHH
        AHfe00atyOPYyEkwjy0g0WXvoXdH347feN39lxGtVAPBx8sKMvWvWy2V0ypyVxmWSymPgF9+256IFRU5sBNEHngSbtUHYHQqmXbO
        TXzXAP4kgGsbeE1lr5gSZOX3RcoBUTK9s6uWRL4+ZlCwDQSKnLprFuD787M17NtB7sozO/ZseUD91zaMy5B9/H1bMG7sRz4SLy8z
        oksQjUl/GWyJoqwDAFm8Fhv+DKfQqDne/ymBZM/QdYjPtCXeTgIfjnD96sJKRlLInDV526x+Q5JXmhDkeE9P/jq09VkFAFaru35V
        SXfvFq/AOHgxQdUHmAU4DAQ5EcUF1Q/qBPXbRUdKSFmJYyMO1Ff2QJnqjcY1GOcruHi6VEIRF0+/+JU9VcXJWvmQ7ko74eq1HxMn
        BhWQrC7YpDjE6M9i6TGSk+5SKcN+fkUVnUc8jiaTewwdf8QGIYw8z0nhc36WV/rrmHI9OvIxQ9+4ASxno3nE5zGZz6yQnz+dPW0+
        VB2vRNRmZHlpskBS8y4oicIYBIRVl72xxAlOLbMgneBrC1PmTkyo4eDgUByN0KCrGkUNXE39wBShgBnNHAYJrsQXYJkDnzrGitMC
        SmCKmhzpfz0No5qzRatAynKOyBYlpaVc0v/JMoaJYiOhhp8c/Spb1B+ZsyGGc/GGZfbDXrqqfoxDwGXVZhpVCY7qGd9bEJAwniNG
        ClwNEZ3IbNiW+S3DpVoFTDojfUgGnCmp+y4zvE34d4yxnKSXACTgpMPu/K8BfpT1Mfsm8FI2+bahmdt9RhV6k/L9KqP6JoKDNNDd
        HQnrh1KO9a2MyDNw41jy39TeJaRMwDZrfpmZ9Er5tag96/KEj5Zn3o4/6VMe4reTHGC9LkhJXMzVQItZw0E0w2n4VI0IDrBr46kc
        Q+PM4xFhbMlCdSgBGslSkRhS08vNROCmxo6pZaMFJ+MMA79fQ6FP2DYULOWarfggdaZfMiw8Uj0Vg+UM2Y8u7J8Hf1Sk8klZXsp1
        +uj69zDW7RFmylSlj8shRjPFcdfP3Tzurwo3PKy80wMYvRmaJW4Dd3IOtwiCSZolmi3hJrKfFC4iD+d/Wxux05db+R9EpQ+LMaGA
        FBe2t0787Gj12W3DfuAnxh+dMModkP9IakNOyfsq1LUiHrpBw3Dz4eJI/XaG23iSq4m2ogpO7UsYqgAfeZh41HwrChPcQI+yt/fs
        RL0XGLlgK07Eop9JTG93uzVBe18A5ePWn0gNkcapX9n/aFFM8nUrOwYZAHkoi6SJ5CrCvVKBNgyw24sZJUqOCDcwkyI3xdXXKn7o
        afB2wpsHQ31QMwBSliru2veJYIsvg5NWM1tPt6BZZMJHF9kEB8pzql0VOVKR+48kI3/4DE00lvvv3AP2AQOXCaxF53PNMOQJBxRu
        WPjPtNlLikaWCITcPcg5jx5U+8Jvb24aGA1WHEIlrOQzoHwi+s69cYyWBA26D8U1zuIOexgZQ78Iuvxf+tSiFJFpt/PBuQxEyb0Z
        xnVOR8rMnp33kjWik27NgsOsmpOHMeNmm/wJDMK0XobEOhDXjPGtIFhuSUWBGo5cGB3h62hX09QvvvRHSNldYLXcWtBeu9ZGBya+
        j9NAgIF86vOwqgaYSglgZ4vAkz3QCW9gVf4Nzzug36BqhMXY9InehWpz1yMs9uu8lZy0R4bNioQwAoLILcjXsWpjUoMR8/dy12At
        taBCxigEE/b0OGpzOdozbZmoJxDvRvgOLsynD//8LKOB
        """;

    // binary: 65536 bytes in, 26309 compressed
    private const string BinaryCompressedBase64 =
        """
        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
        """;

    private static readonly LzmaVector[] Vectors =
    [
        new LzmaVector(
            "empty",
            "5D00001000",
            0,
            EmptyCompressedBase64,
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
        new LzmaVector(
            "single",
            "5D00001000",
            1,
            SingleCompressedBase64,
            "559aead08264d5795d3909718cdd05abd49572e84fe55590eef31a88a08fdffd"),
        new LzmaVector(
            "text",
            "5D00001000",
            2816,
            TextCompressedBase64,
            "4bc29d3a9c5a116faba90c4152964c8c0c9616b2688e60470eca11a1920b5d10"),
        new LzmaVector(
            "repetitive",
            "5D00001000",
            100000,
            RepetitiveCompressedBase64,
            "e6631225e83d23bf67657e85109ad5deb3570e1405d7aaa23a2485ae8582c143"),
        new LzmaVector(
            "random",
            "5D00001000",
            8192,
            RandomCompressedBase64,
            "3e0f7e923af49323cbdc08318edc4f0398b2b289c783758c4a2c7bbe7a989d9e"),
        new LzmaVector(
            "mixed",
            "5D00001000",
            32768,
            MixedCompressedBase64,
            "cda3008c2f31905e81ec201bd9492b93520d1a4bfb2651ca37e92644e43c6349"),
        new LzmaVector(
            "binary",
            "5D00001000",
            65536,
            BinaryCompressedBase64,
            "46066245da79ea1940f18a73818cb4f05568972cb14180bfcfafa3a0dfe01ed9"),
    ];

    public static IEnumerable<object[]> VectorNames()
    {
        foreach (LzmaVector vector in Vectors) yield return [vector.Name];
    }

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void Decode_MatchesTheReferenceStream(string name)
    {
        LzmaVector vector = FindVector(name);
        byte[] properties = FrameworkCompatibility.FromHexString(vector.PropertiesHex);
        byte[] compressed = Convert.FromBase64String(vector.CompressedBase64);

        byte[] decoded;
        using (MemoryStream input = new(compressed, writable: false))
        using (MemoryStream output = new())
        {
            LzmaDecoder.Decode(input, output, properties, vector.UncompressedLength);
            decoded = output.ToArray();
        }

        Assert.Equal(vector.UncompressedLength, decoded.LongLength);
        Assert.Equal(
            vector.ExpectedSHA256,
            FrameworkCompatibility.ToHexStringLower(FrameworkCompatibility.ComputeSHA256(decoded)));
    }

    /// <summary>
    /// Half a stream must fail loudly instead of returning a short or invented buffer. The decoder
    /// runs out of input long before it can finish, so the end of the stream is what surfaces.
    /// NOTE: a decoder that rejected the structure before it ran dry would raise InvalidDataException
    /// instead, so align this type if the error handling moves
    /// </summary>
    [Fact]
    public void Decode_ThrowsWhenTheCompressedStreamIsTruncated()
    {
        LzmaVector vector = FindVector(TruncationVectorName);
        byte[] properties = FrameworkCompatibility.FromHexString(vector.PropertiesHex);
        byte[] compressed = Convert.FromBase64String(vector.CompressedBase64);
        byte[] truncated = new byte[compressed.Length / 2];
        Array.Copy(compressed, sourceIndex: 0, truncated, destinationIndex: 0, truncated.Length);

        Assert.Throws<EndOfStreamException>(() =>
        {
            using MemoryStream input = new(truncated, writable: false);
            using MemoryStream output = new();
            LzmaDecoder.Decode(input, output, properties, vector.UncompressedLength);
        });
    }

    private static LzmaVector FindVector(string name)
    {
        foreach (LzmaVector vector in Vectors)
        {
            if (string.Equals(vector.Name, name, StringComparison.Ordinal)) return vector;
        }

        throw new InvalidOperationException($"No conformance vector is named {name}.");
    }
}
