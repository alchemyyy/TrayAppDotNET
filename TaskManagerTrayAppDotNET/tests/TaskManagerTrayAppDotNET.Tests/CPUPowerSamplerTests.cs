using System.Buffers.Binary;
using System.Text;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class CPUPowerSamplerTests
{
    private static readonly TimeSpan MaximumSpan = TimeSpan.FromSeconds(5);

    [Fact]
    public void ConvertsPicowattHoursOver100NanosecondTicksToWatts()
    {
        // 88,052,495,833 pWh over 2.012 s, read from a Threadripper 9960X's package channel
        Assert.True(CPUPowerSampler.TryCalculateWatts(
            previousEnergy: 111_338_020_162_500,
            previousTime: 1_000_000,
            energy: 111_426_072_658_333,
            time: 1_000_000 + 20_120_000,
            MaximumSpan,
            out double watts));
        Assert.Equal(expected: 157.55, watts, precision: 2);
    }

    [Theory]
    [InlineData(100UL, 10UL, 50UL, 20UL)] // energy counter went backwards
    [InlineData(100UL, 10UL, 200UL, 10UL)] // no time elapsed
    [InlineData(100UL, 10UL, 200UL, 5UL)] // time went backwards
    [InlineData(100UL, 0UL, 200UL, 50_000_001UL)] // longer than the maximum span: sleep or a stalled sampler
    public void ResetsAndGapsProduceNoReading(ulong previousEnergy, ulong previousTime, ulong energy, ulong time) =>
        Assert.False(CPUPowerSampler.TryCalculateWatts(previousEnergy, previousTime, energy, time, MaximumSpan, out _));

    [Fact]
    public void ParsesVersion2ChannelNames()
    {
        byte[] metadata = BuildMetadataV2("RAPL_Package0_PKG", "RAPL_Package0_Core0_CORE");

        Assert.Equal(
            ["RAPL_Package0_PKG", "RAPL_Package0_Core0_CORE"],
            CPUPowerSampler.ParseChannelNames(metadata, version: 2));
        Assert.Empty(CPUPowerSampler.ParseChannelNames(metadata.AsSpan(0, metadata.Length - 4), version: 2));
    }

    [Fact]
    public void ParsesTheVersion1MeteredHardwareName()
    {
        byte[] name = Encoding.Unicode.GetBytes("Apu Energy\0");
        byte[] metadata = new byte[72 + name.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(metadata.AsSpan(70), (ushort)name.Length);
        name.CopyTo(metadata, 72);

        Assert.Equal(["Apu Energy"], CPUPowerSampler.ParseChannelNames(metadata, version: 1));
    }

    [Fact]
    public void SelectsThePackageChannelAndNeverAPartOfIt()
    {
        Assert.Equal(
            expected: 1,
            CPUPowerSampler.SelectPackageChannel(["RAPL_Package0_Core0_CORE", "RAPL_Package1_PKG", "RAPL_Package0_DRAM"]));
        Assert.Equal(
            expected: 2,
            CPUPowerSampler.SelectPackageChannel(["VDDCR_VDD Energy", "VDDCR_SOC Energy", "Current Socket Energy"]));
        Assert.Equal(expected: -1, CPUPowerSampler.SelectPackageChannel(["RAPL_Package0_Core5_CORE"]));
        Assert.Equal(expected: -1, CPUPowerSampler.SelectPackageChannel(["RAPL_PackageX_PKG", "RAPL_Package0_PP0"]));
    }

    private static byte[] BuildMetadataV2(params string[] channelNames)
    {
        List<byte> buffer = [.. new byte[68]];
        BinaryPrimitives.WriteUInt16LittleEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buffer).Slice(66, 2),
            (ushort)channelNames.Length);
        foreach (string channelName in channelNames)
        {
            byte[] name = Encoding.Unicode.GetBytes(channelName + "\0");
            buffer.AddRange(new byte[4]); // EmiMeasurementUnitPicowattHours
            buffer.Add((byte)name.Length);
            buffer.Add((byte)(name.Length >> 8));
            buffer.AddRange(name);
        }

        return [.. buffer];
    }
}
