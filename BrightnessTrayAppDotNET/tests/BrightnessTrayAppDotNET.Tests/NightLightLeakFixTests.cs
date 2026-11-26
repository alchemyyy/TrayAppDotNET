using System.Buffers.Binary;
using BrightnessTrayAppDotNET.Interop.NightLight;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class NightLightLeakFixTests
{
    // ViVeTool's /enable /id:62354587 /priority:userpolicy writes the override under this name, which Windows loads at
    // boot; any other name is silently ignored
    [Fact]
    public void OverrideValueNameIsTheFeatureStoreObfuscatedId() =>
        Assert.Equal(expected: "914854031", NightLightLeakFix.ObfuscateFeatureId(NightLightLeakFix.FeatureId));

    [Fact]
    public void ModuleScanFindsTheIdOnlyAsALittleEndianValue()
    {
        using TempDirectory temp = new();
        string littleEndian = temp.Combine("little.dll");
        string bigEndian = temp.Combine("big.dll");
        WriteModule(littleEndian, BinaryPrimitives.WriteUInt32LittleEndian);
        WriteModule(bigEndian, BinaryPrimitives.WriteUInt32BigEndian);

        Assert.True(NightLightLeakFix.FileContainsFeatureId(littleEndian, NightLightLeakFix.FeatureId));
        Assert.False(NightLightLeakFix.FileContainsFeatureId(bigEndian, NightLightLeakFix.FeatureId));
    }

    // The id ends the file, after more than a page of other bytes, so a scan that stops short of the end misses it
    private static void WriteModule(string path, SpanAction writeId)
    {
        byte[] module = new byte[5000];
        Array.Fill(module, value: (byte)0xCC);
        writeId(module.AsSpan(module.Length - sizeof(uint)), NightLightLeakFix.FeatureId);
        File.WriteAllBytes(path, module);
    }

    private delegate void SpanAction(Span<byte> destination, uint value);
}
