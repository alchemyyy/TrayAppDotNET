using BrightnessTrayAppDotNET.Interop.NightLight;
using BrightnessTrayAppDotNET.Models;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class NightLightPDBTimeoutTests
{
    [Theory]
    [InlineData(5_000)]
    [InlineData(60_000)]
    [InlineData(600_000)]
    public void HelperCommandLineCarriesTheDownloadTimeout(int timeoutMs)
    {
        string[] args = NightLightHelperServer.BuildArguments(parentProcessID: 1234, "pipe", timeoutMs);

        Assert.Contains(NightLightHelperServer.ServerArg, args);
        Assert.Equal(timeoutMs, NightLightHelperServer.ParsePDBDownloadTimeoutMs(args));
    }

    [Fact]
    public void HelperWithoutTimeoutArgumentKeepsItsDefault()
    {
        string[] args =
        [
            NightLightHelperServer.ServerArg,
            NightLightHelperServer.PipeNameArg, "pipe"
        ];

        Assert.Null(NightLightHelperServer.ParsePDBDownloadTimeoutMs(args));
    }

    [Theory]
    [InlineData("1", 5_000)]
    [InlineData("99999999", 600_000)]
    public void HelperClampsOutOfRangeTimeouts(string value, int expectedMs)
    {
        string[] args = [NightLightHelperServer.ServerArg, NightLightHelperServer.PDBDownloadTimeoutArg, value];

        Assert.Equal(expectedMs, NightLightHelperServer.ParsePDBDownloadTimeoutMs(args));
    }

    [Fact]
    public void HelperIgnoresNonNumericTimeout()
    {
        string[] args = [NightLightHelperServer.ServerArg, NightLightHelperServer.PDBDownloadTimeoutArg, "soon"];

        Assert.Null(NightLightHelperServer.ParsePDBDownloadTimeoutMs(args));
    }

    [Theory]
    [InlineData(5_000)]
    [InlineData(60_000)]
    [InlineData(600_000)]
    public void HelperStartWaitOutlastsTheDownload(int timeoutMs)
    {
        int startTimeoutMs = NightLightHelperClient.GetStartTimeoutMs(timeoutMs);

        Assert.Equal(timeoutMs + TimeConstants.NightLightHelperStartGraceMs, startTimeoutMs);
    }

    [Fact]
    public void SettingDefaultsToSixtySeconds() =>
        Assert.Equal(expected: 60, new AppSettings().NightLightPDBDownloadTimeoutSeconds);

    [Theory]
    [InlineData(1, 5)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(300, 300)]
    [InlineData(600, 600)]
    [InlineData(601, 600)]
    public void SettingIsClampedToTheSettingsUIRange(int written, int expected)
    {
        AppSettings settings = new() { NightLightPDBDownloadTimeoutSeconds = written };

        Assert.Equal(expected, settings.NightLightPDBDownloadTimeoutSeconds);
        Assert.InRange(
            settings.NightLightPDBDownloadTimeoutSeconds,
            TimeConstants.PDBSymbolResolverDownloadTimeoutMinSeconds,
            TimeConstants.PDBSymbolResolverDownloadTimeoutMaxSeconds);
    }
}
