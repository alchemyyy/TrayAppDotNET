using TrayAppDotNETInstaller.Localization;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class LocalizationManagerTests
{
    [Fact]
    public void KnownKeyResolvesToResourceText()
    {
        string text = LocalizationManager.Instance[nameof(Strings.Installer_Button_Install)];

        Assert.Equal("Install", text);
    }

    [Fact]
    public void FormatKeysKeepTheirPlaceholders()
    {
        string format = LocalizationManager.Instance[nameof(Strings.Installer_Title_Format)];

        Assert.Contains(expectedSubstring: "{0}", format, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownKeyFallsBackToTheKeyItself()
    {
        Assert.Equal("Installer_Missing_Key", LocalizationManager.Instance["Installer_Missing_Key"]);
        Assert.Equal(string.Empty, LocalizationManager.Instance[string.Empty]);
    }
}
