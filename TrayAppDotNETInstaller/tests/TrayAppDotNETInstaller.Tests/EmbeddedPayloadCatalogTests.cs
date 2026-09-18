using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class EmbeddedPayloadCatalogTests
{
    [Fact]
    public void TryParsePayloadFileName_ParsesApplicationAndVersion()
    {
        const string fileName = "VolumeTrayAppDotNET_12.zip";

        bool parsed = EmbeddedPayloadCatalog.TryParsePayloadFileName(fileName, out EmbeddedPayload? payload);

        Assert.True(parsed);
        Assert.NotNull(payload);
        Assert.Equal("VolumeTrayAppDotNET", payload.ApplicationName);
        Assert.Equal(12, payload.Version);
        Assert.Equal(fileName, payload.FileName);
    }

    [Fact]
    public void TryParsePayloadFileName_ToleratesProfileTokenAfterVersion()
    {
        bool parsed = EmbeddedPayloadCatalog.TryParsePayloadFileName(
            "BatteryTrayAppDotNET_7_x64.zip",
            out EmbeddedPayload? payload);

        Assert.True(parsed);
        Assert.NotNull(payload);
        Assert.Equal("BatteryTrayAppDotNET", payload.ApplicationName);
        Assert.Equal(7, payload.Version);
    }

    [Theory]
    [InlineData("VolumeTrayAppDotNET_12.tadn", "VolumeTrayAppDotNET", 12)]
    [InlineData("BatteryTrayAppDotNET_7_x64.tadn", "BatteryTrayAppDotNET", 7)]
    [InlineData("VolumeTrayAppDotNET_12.TADN", "VolumeTrayAppDotNET", 12)]
    public void TryParsePayloadFileName_ParsesTheSolidPayloadExtension(
        string fileName,
        string expectedApplicationName,
        int expectedVersion)
    {
        // A stamped installer carries solid payloads; only a development Payloads directory holds zips
        bool parsed = EmbeddedPayloadCatalog.TryParsePayloadFileName(fileName, out EmbeddedPayload? payload);

        Assert.True(parsed);
        Assert.NotNull(payload);
        Assert.Equal(expectedApplicationName, payload.ApplicationName);
        Assert.Equal(expectedVersion, payload.Version);
        Assert.Equal(fileName, payload.FileName);
    }

    [Theory]
    [InlineData("VolumeTrayAppDotNET.zip")]
    [InlineData("VolumeTrayAppDotNET_.zip")]
    [InlineData("VolumeTrayAppDotNET_abc.zip")]
    [InlineData("_12.zip")]
    [InlineData("VolumeTrayAppDotNET_-3.zip")]
    public void TryParsePayloadFileName_RejectsMissingVersion(string fileName)
    {
        bool parsed = EmbeddedPayloadCatalog.TryParsePayloadFileName(fileName, out EmbeddedPayload? payload);

        Assert.False(parsed);
        Assert.Null(payload);
    }

    [Theory]
    [InlineData("VolumeTrayAppDotNET_12.txt")]
    [InlineData("VolumeTrayAppDotNET_12")]
    [InlineData("VolumeTrayAppDotNET_12.tadn.bak")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParsePayloadFileName_RejectsWrongExtension(string? fileName)
    {
        bool parsed = EmbeddedPayloadCatalog.TryParsePayloadFileName(fileName, out EmbeddedPayload? payload);

        Assert.False(parsed);
        Assert.Null(payload);
    }

    [Fact]
    public void FromFileNames_SortsByNameAndIgnoresUnrecognizedNames()
    {
        string[] fileNames =
        [
            "VolumeTrayAppDotNET_3.zip",
            "TrayAppDotNETInstaller.exe",
            "BatteryTrayAppDotNET_5.zip",
            "Broken.zip"
        ];

        EmbeddedPayloadCatalog catalog = EmbeddedPayloadCatalog.FromFileNames(fileNames);

        Assert.True(catalog.IsBundle);
        Assert.Equal(2, catalog.Payloads.Count);
        Assert.Equal("BatteryTrayAppDotNET", catalog.Payloads[0].ApplicationName);
        Assert.Equal("VolumeTrayAppDotNET", catalog.Payloads[1].ApplicationName);
        Assert.NotNull(catalog.Find("volumetrayappdotnet"));
        Assert.Null(catalog.Find("Missing"));
    }

    [Fact]
    public void SinglePayload_IsNotBundle()
    {
        EmbeddedPayloadCatalog catalog = EmbeddedPayloadCatalog.FromFileNames(["VolumeTrayAppDotNET_3.zip"]);

        Assert.False(catalog.IsBundle);
        Assert.Single(catalog.Payloads);
    }

    [Fact]
    public void OpenPayload_ThrowsWhenTheCatalogCarriesNothing()
    {
        EmbeddedPayloadCatalog catalog = EmbeddedPayloadCatalog.FromFileNames(["VolumeTrayAppDotNET_3.zip"]);

        Assert.Throws<InvalidOperationException>(() => catalog.OpenPayload(catalog.Payloads[0]));
    }
}
