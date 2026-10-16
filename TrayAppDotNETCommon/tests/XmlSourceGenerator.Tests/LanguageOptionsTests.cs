using TrayAppDotNETCommon.UI.Settings;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class LanguageOptionsTests
{
    [Fact]
    public void NoTranslationsOffersOnlyTheNeutralLanguage()
    {
        List<(string Tag, string Text)> options = TrayAppDotNETGeneralSettingsSection.LanguageOptions([]);

        (string tag, string text) = Assert.Single(options);
        Assert.Equal("en", tag);
        Assert.Equal("English", text);
    }

    [Fact]
    public void TranslationsSortByCapitalizedNativeName()
    {
        List<(string Tag, string Text)> options = TrayAppDotNETGeneralSettingsSection.LanguageOptions(["fr", "de"]);

        Assert.Equal(["de", "en", "fr"], options.Select(option => option.Tag));
        Assert.Equal(["Deutsch", "English", "Français"], options.Select(option => option.Text));
    }

    [Fact]
    public void DuplicateAndUnknownNamesAreSkipped()
    {
        List<(string Tag, string Text)> options =
            TrayAppDotNETGeneralSettingsSection.LanguageOptions(["EN", "not a culture", "de", "de"]);

        Assert.Equal(["de", "en"], options.Select(option => option.Tag));
    }
}
