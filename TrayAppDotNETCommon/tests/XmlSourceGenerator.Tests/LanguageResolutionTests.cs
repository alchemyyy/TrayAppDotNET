using System.Globalization;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class LanguageResolutionTests
{
    private const string SettingsFileName = "settings.xml";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Auto")]
    [InlineData("auto")]
    public void AutoAndBlankResolveToTheWindowsDisplayLanguage(string? language) =>
        Assert.Same(LocalizationManager.SystemUICulture, LocalizationManager.ResolveCulture(language, ["de"]));

    [Fact]
    public void NeutralLanguageResolvesWithoutTranslations() =>
        Assert.Equal("en", LocalizationManager.ResolveCulture("en", []).Name);

    [Theory]
    [InlineData("de")]
    [InlineData("DE")]
    [InlineData(" de ")]
    public void ShippedTranslationResolvesToItsCulture(string language) =>
        Assert.Equal("de", LocalizationManager.ResolveCulture(language, ["de"]).Name);

    [Fact]
    public void LanguageWithoutShippedTranslationFallsBackToAuto() =>
        Assert.Same(LocalizationManager.SystemUICulture, LocalizationManager.ResolveCulture("fr", ["de"]));

    [Fact]
    public void UnknownCultureNameFallsBackToAuto() =>
        Assert.Same(
            LocalizationManager.SystemUICulture,
            LocalizationManager.ResolveCulture("not a culture", ["not a culture"]));

    [Fact]
    public void StartupReadsTopLevelLanguageSetting() =>
        Assert.Equal(
            "de",
            LoadCulture("<AppSettings><Language>de</Language></AppSettings>", ["de"]).Name);

    [Fact]
    public void StartupIgnoresNestedLanguageElements() =>
        Assert.Same(
            LocalizationManager.SystemUICulture,
            LoadCulture("<AppSettings><Profile><Language>de</Language></Profile></AppSettings>", ["de"]));

    [Fact]
    public void StartupWithoutSettingsFileUsesAuto()
    {
        string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), SettingsFileName);

        Assert.Same(
            LocalizationManager.SystemUICulture,
            TrayAppDotNETAvalonia.LoadConfiguredCulture(() => missingPath, ["de"]));
    }

    [Fact]
    public void RenderingBackendStillReadsTopLevelSettingOnly()
    {
        Assert.Equal(
            TrayAppDotNETRenderingBackend.Software,
            WithSettingsFile(
                "<AppSettings><RenderingBackend>Software</RenderingBackend></AppSettings>",
                path => TrayAppDotNETAvalonia.LoadRenderingBackendOrDefault(() => path)));
        Assert.Equal(
            TrayAppDotNETRenderingBackend.GPUPreferred,
            WithSettingsFile(
                "<AppSettings><Profile><RenderingBackend>Software</RenderingBackend></Profile></AppSettings>",
                path => TrayAppDotNETAvalonia.LoadRenderingBackendOrDefault(() => path)));
    }

    private static CultureInfo LoadCulture(string settingsXML, IReadOnlyList<string> availableLanguages) =>
        WithSettingsFile(
            settingsXML,
            path => TrayAppDotNETAvalonia.LoadConfiguredCulture(() => path, availableLanguages));

    private static T WithSettingsFile<T>(string settingsXML, Func<string, T> read)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("TADNLanguageTests");
        try
        {
            string path = Path.Combine(directory.FullName, SettingsFileName);
            File.WriteAllText(path, settingsXML);
            return read(path);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
