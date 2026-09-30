using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

/// <summary>
/// The application status line only travels between the window and its elevated worker.
/// These pin its shape, and that it never collides with the "[42%] message" / "[FAIL] message" format shared with the apps.
/// </summary>
public sealed class InstallApplicationStatusTests
{
    private const string ApplicationName = "VolumeTrayAppDotNET";

    [Fact]
    public void ToLine_UsesTheDocumentedShape()
    {
        InstallApplicationStatus status = new(ApplicationName, InstallApplicationState.Installing);

        Assert.Equal(expected: "[APP:Installing] VolumeTrayAppDotNET", status.ToLine());
    }

    [Fact]
    public void ToLine_KeepsANameWithLineBreaksOnOneLine()
    {
        InstallApplicationStatus status = new(
            ApplicationName: "Volume\r\nTrayAppDotNET",
            InstallApplicationState.Installed);

        Assert.Equal(expected: "[APP:Installed] Volume  TrayAppDotNET", status.ToLine());
    }

    [Theory]
    [InlineData(InstallApplicationState.Waiting)]
    [InlineData(InstallApplicationState.Installing)]
    [InlineData(InstallApplicationState.Installed)]
    [InlineData(InstallApplicationState.Failed)]
    [InlineData(InstallApplicationState.NotInstalled)]
    public void Line_RoundTripsEveryState(InstallApplicationState state)
    {
        InstallApplicationStatus original = new(ApplicationName, state);

        bool parsed = InstallApplicationStatus.TryParseLine(original.ToLine(), out InstallApplicationStatus? result);

        Assert.True(parsed);
        Assert.Equal(original, result);
    }

    [Fact]
    public void TryParseLine_TrimsSurroundingWhitespace()
    {
        bool parsed = InstallApplicationStatus.TryParseLine(
            line: "  [APP:Failed]   VolumeTrayAppDotNET  ",
            out InstallApplicationStatus? result);

        Assert.True(parsed);
        Assert.Equal(new InstallApplicationStatus(ApplicationName, InstallApplicationState.Failed), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[42%] Installing VolumeTrayAppDotNET")]
    [InlineData("[FAIL] VolumeTrayAppDotNET")]
    [InlineData("Installed to C:\\x")]
    [InlineData("[APP:] VolumeTrayAppDotNET")]
    [InlineData("[APP:Unknown] VolumeTrayAppDotNET")]
    [InlineData("[APP:installing] VolumeTrayAppDotNET")]
    [InlineData("[app:Installing] VolumeTrayAppDotNET")]
    [InlineData("[APP:Installing]")]
    [InlineData("[APP:Installing]   ")]
    [InlineData("[APP:Installing VolumeTrayAppDotNET")]
    [InlineData("APP:Installing] VolumeTrayAppDotNET")]
    public void TryParseLine_RejectsEverythingElse(string? line)
    {
        bool parsed = InstallApplicationStatus.TryParseLine(line, out InstallApplicationStatus? result);

        Assert.False(parsed);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(InstallApplicationState.Waiting)]
    [InlineData(InstallApplicationState.Installing)]
    [InlineData(InstallApplicationState.Installed)]
    [InlineData(InstallApplicationState.Failed)]
    [InlineData(InstallApplicationState.NotInstalled)]
    public void ProgressParser_RejectsStatusLines(InstallApplicationState state)
    {
        // InstallProgressLine mirrors the TrayAppDotNETCommon parser, so neither reads a status line as progress
        string line = new InstallApplicationStatus(ApplicationName, state).ToLine();

        bool parsed = InstallProgressLine.TryParseLine(line, out InstallProgressLine? result);

        Assert.False(parsed);
        Assert.Null(result);
    }
}
