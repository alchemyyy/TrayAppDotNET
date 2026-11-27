using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI.Performance;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class GPUVendorFormattingTests
{
    [Theory]
    [InlineData("32.0.16.1692", "616.92")]
    [InlineData("31.0.15.5222", "552.22")]
    // The fourth field is zero-padded to four digits before the last five digits are taken
    [InlineData("32.0.15.100", "501.00")]
    [InlineData("32.0.15.7", "500.07")]
    [InlineData("27.21.14.6079", "460.79")]
    [InlineData("32.0.16", "")]
    [InlineData("32.0.x.1692", "")]
    [InlineData("32.0.16.16920", "")]
    public void DerivesNVIDIAPublicDriverVersion(string windowsVersion, string expected) =>
        Assert.Equal(
            expected,
            GPUVendorFormatting.FormatPublicDriverVersion(
                GPUVendorFormatting.NVIDIAVendorID,
                windowsVersion,
                amdSoftwareVersion: string.Empty));

    [Fact]
    public void AMDUsesRegistryReleaseAndIntelQuotesTheLastTwoWindowsFields()
    {
        Assert.Equal(
            expected: "24.12.1",
            GPUVendorFormatting.FormatPublicDriverVersion(GPUVendorFormatting.AMDVendorID, "32.0.12033.1030", " 24.12.1 "));
        Assert.Equal(
            expected: string.Empty,
            GPUVendorFormatting.FormatPublicDriverVersion(GPUVendorFormatting.AMDVendorID, "32.0.12033.1030", string.Empty));
        Assert.Equal(
            expected: "101.6913",
            GPUVendorFormatting.FormatPublicDriverVersion(GPUVendorFormatting.IntelVendorID, "32.0.101.6913", string.Empty));
        Assert.Equal(
            expected: string.Empty,
            GPUVendorFormatting.FormatPublicDriverVersion(GPUVendorFormatting.IntelVendorID, "32.0.101", string.Empty));
        Assert.Equal(
            expected: string.Empty,
            GPUVendorFormatting.FormatPublicDriverVersion(0x1414, "10.0.26100.1", string.Empty));
    }

    [Theory]
    [InlineData("616.92", "32.0.16.1692", "616.92 (32.0.16.1692)")]
    [InlineData("101.6913", "32.0.101.6913", "101.6913 (32.0.101.6913)")]
    [InlineData("24.12.1", "24.12.1", "24.12.1")]
    [InlineData("", "10.0.26100.1", "10.0.26100.1")]
    public void DriverVersionDisplayKeepsTheWindowsVersionWhenItDiffers(
        string publicVersion,
        string windowsVersion,
        string expected) =>
        Assert.Equal(expected, GPUVendorFormatting.FormatDriverVersionDisplay(publicVersion, windowsVersion));

    [Theory]
    [InlineData("Version98.3.3b.c0.ab", "98.03.3B.C0.AB")]
    [InlineData("Version 95.2.18.0.1\0", "95.02.18.00.01")]
    [InlineData("98.03.3B.C0.AB", "98.03.3B.C0.AB")]
    // A string that is not a dotted hex version stays as written
    [InlineData("Version98.3.3bc.c0", "Version98.3.3bc.c0")]
    public void NormalizesNVIDIABIOSString(string raw, string expected) =>
        Assert.Equal(expected, GPUVendorFormatting.NormalizeVBIOSVersion(GPUVendorFormatting.NVIDIAVendorID, raw));

    [Fact]
    public void LeavesOtherVendorsBIOSStringsAsWritten() =>
        Assert.Equal(
            expected: "113-D7020100-102",
            GPUVendorFormatting.NormalizeVBIOSVersion(GPUVendorFormatting.AMDVendorID, " 113-D7020100-102\0"));

    [Fact]
    public void ParsesHardwareIDWithSubsystemVendorLast()
    {
        Assert.True(GPUVendorFormatting.TryParseHardwareID(
            @"PCI\VEN_10DE&DEV_2C02&SUBSYS_20961771&REV_A1",
            out GPUHardwareID parsed));
        Assert.Equal(new GPUHardwareID(HasValue: true, 0x10DE, 0x2C02, 0x1771, 0x2096, 0xA1), parsed);
        Assert.Equal(expected: "10DE:2C02 (1771:2096, rev A1)", GPUVendorFormatting.FormatHardwareID(parsed));

        Assert.True(GPUVendorFormatting.TryParseHardwareID(@"PCI\VEN_8086&DEV_7D55&REV_08", out GPUHardwareID noSubsystem));
        Assert.Equal(expected: "8086:7D55 rev 08", GPUVendorFormatting.FormatHardwareID(noSubsystem));

        Assert.False(GPUVendorFormatting.TryParseHardwareID(@"ROOT\BasicRender", out _));
        Assert.False(GPUVendorFormatting.TryParseHardwareID(@"PCI\DEV_2C02", out _));
    }

    [Theory]
    [InlineData(5u, 16u, 5u, 16u, "Gen 5 x16")]
    [InlineData(1u, 16u, 5u, 16u, "Gen 1 x16 (max Gen 5 x16)")]
    [InlineData(4u, 8u, 4u, 16u, "Gen 4 x8 (max Gen 4 x16)")]
    [InlineData(3u, 4u, 0u, 0u, "Gen 3 x4")]
    public void FormatsPCIeLinkAndShowsTheMaximumOnlyWhenBelowIt(
        uint currentGeneration,
        uint currentWidth,
        uint maximumGeneration,
        uint maximumWidth,
        string expected) =>
        Assert.Equal(
            expected,
            GPUVendorFormatting.FormatPCIeLink(
                new GPUPCIeLink(HasValue: true, currentGeneration, currentWidth, maximumGeneration, maximumWidth)));

    [Fact]
    public void MarksALinkThatOnlyReachesAnOnCardSwitch()
    {
        Assert.Equal(
            expected: "Gen 4 x16 (to on-card switch)",
            PerformanceDevicePresentationFactory.FormatGPUPCIeLink(
                new GPUPCIeLink(HasValue: true, 4, 16, 4, 16, BehindOnCardSwitch: true)));
        Assert.Equal(
            expected: "Gen 4 x16",
            PerformanceDevicePresentationFactory.FormatGPUPCIeLink(new GPUPCIeLink(HasValue: true, 4, 16, 4, 16)));
    }

    [Theory]
    [InlineData(15.8, 360.0, "57 W")]
    [InlineData(112.5, 300.0, "338 W")]
    [InlineData(15.8, double.NaN, "16%")]
    [InlineData(112.6, double.NaN, "113%")]
    public void PowerIsAPercentageOfTheDefaultLimitAndIsNotClamped(double percent, double limitWatts, string expected) =>
        Assert.Equal(expected, PerformanceDevicePresentationFactory.FormatGPUPower(percent, limitWatts));

    [Theory]
    [InlineData("Video Decode", "Video Encode", "Video Decode / Encode")]
    [InlineData("Compute", "Copy", "Compute / Copy")]
    [InlineData("Video Processing", "Optical Flow", "Video Processing / Optical Flow")]
    public void CombinesEngineTitlesSharingAFirstWord(string primary, string secondary, string expected) =>
        Assert.Equal(expected, GPUPerformanceDetailsView.CombineEngineTitles(primary, secondary));
}

public sealed class GPUPowerLimitTests
{
    private const string InstanceID = @"PCI\VEN_10DE&DEV_2C02&SUBSYS_20961771&REV_A1\4&1";

    [Fact]
    public void LaterRecordForTheSameGPUReplacesTheEarlierOne()
    {
        List<GPUPowerLimitRecord> normalized = GPUPowerLimitCollection.Normalize(
        [
            new GPUPowerLimitRecord { DeviceInstanceID = InstanceID, VBIOSVersion = "A", DefaultLimitMilliwatts = 300_000, ShowWatts = true },
            null,
            new GPUPowerLimitRecord { DeviceInstanceID = " ", DefaultLimitMilliwatts = 1 },
            new GPUPowerLimitRecord { DeviceInstanceID = InstanceID.ToLowerInvariant(), VBIOSVersion = "B", DefaultLimitMilliwatts = 360_000, ShowWatts = true }
        ]);

        GPUPowerLimitRecord record = Assert.Single(normalized);
        Assert.Equal(expected: "B", record.VBIOSVersion);
        Assert.Equal(expected: 360_000u, record.DefaultLimitMilliwatts);
    }

    [Fact]
    public void WattsNeedTheChoiceAndALimitReadUnderTheCurrentVBIOS()
    {
        GPUPowerLimitRecord record = new()
        {
            DeviceInstanceID = InstanceID,
            VBIOSVersion = "98.03.3B.C0.AB",
            DefaultLimitMilliwatts = 360_000,
            ShowWatts = true
        };

        Assert.True(GPUPowerLimitCollection.TryGetDisplayLimitWatts([record], InstanceID, "98.03.3B.C0.AB", out double watts));
        Assert.Equal(expected: 360, watts);
        Assert.False(GPUPowerLimitCollection.TryGetDisplayLimitWatts([record], InstanceID, "98.03.3B.C0.AC", out _));

        record.ShowWatts = false;
        Assert.False(GPUPowerLimitCollection.TryGetDisplayLimitWatts([record], InstanceID, "98.03.3B.C0.AB", out _));
    }

    [Theory]
    [InlineData("OK 360000", true, 360_000u, "")]
    [InlineData("OK 360000\r\n", true, 360_000u, "")]
    [InlineData("OK 0", false, 0u, "OK 0")]
    [InlineData("ERROR NVML did not report this GPU.", false, 0u, "NVML did not report this GPU.")]
    [InlineData("", false, 0u, "The reader process printed nothing.")]
    public void ParsesTheReaderOutput(string output, bool succeeded, uint milliwatts, string error)
    {
        GPUPowerLimitResult result = GPUPowerLimitReader.ParseHelperOutput(output);

        Assert.Equal(new GPUPowerLimitResult(succeeded, milliwatts, error), result);
    }

    [Fact]
    public void ParsesReaderArgumentsWithAndWithoutAPCIAddress()
    {
        Assert.True(GPUPowerLimitReader.TryParseHelperArguments(
            [GPUPowerLimitReader.HelperArgument, "10DE", "33", "0", "0", InstanceID],
            out GPUPowerLimitTarget target));
        Assert.Equal(
            new GPUPowerLimitTarget(0x10DE, InstanceID, new GPUPCIAddress(HasValue: true, 33, 0, 0)),
            target);

        Assert.True(GPUPowerLimitReader.TryParseHelperArguments(
            [GPUPowerLimitReader.HelperArgument, "1002", "-", "-", "-", InstanceID],
            out GPUPowerLimitTarget withoutAddress));
        Assert.False(withoutAddress.Address.HasValue);

        Assert.False(GPUPowerLimitReader.TryParseHelperArguments(
            [GPUPowerLimitReader.HelperArgument, "10DE", "33"],
            out _));
    }
}
