using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class AppLauncherTests
{
    [Fact]
    public void FormatArguments_PassesAFlagThroughUnquoted()
    {
        string[] arguments = [AppLauncher.HiddenArgument];

        Assert.Equal("--hidden", AppLauncher.FormatArguments(arguments));
    }

    [Fact]
    public void FormatArguments_QuotesAnArgumentWithSpaces()
    {
        string[] arguments = [AppLauncher.HiddenArgument, @"C:\Program Files\TrayAppDotNET"];
        const string expected =
            """
            --hidden "C:\Program Files\TrayAppDotNET"
            """;

        Assert.Equal(expected, AppLauncher.FormatArguments(arguments));
    }

    [Fact]
    public void FormatArguments_IsEmptyWithoutArguments()
    {
        Assert.Equal(string.Empty, AppLauncher.FormatArguments([]));
    }
}
