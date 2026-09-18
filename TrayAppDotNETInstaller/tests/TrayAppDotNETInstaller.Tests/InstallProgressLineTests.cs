using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class InstallProgressLineTests
{
    [Fact]
    public void ProgressLine_RoundTrips()
    {
        InstallProgressLine original = InstallProgressLine.At(42, "Copying files");

        string line = original.ToLine();
        bool parsed = InstallProgressLine.TryParseLine(line, out InstallProgressLine? result);

        Assert.Equal("[42%] Copying files", line);
        Assert.True(parsed);
        Assert.Equal(original, result);
    }

    [Fact]
    public void FailureLine_RoundTrips()
    {
        InstallProgressLine original = InstallProgressLine.Failed("Disk full");

        string line = original.ToLine();
        bool parsed = InstallProgressLine.TryParseLine(line, out InstallProgressLine? result);

        Assert.Equal("[FAIL] Disk full", line);
        Assert.True(parsed);
        Assert.NotNull(result);
        Assert.True(result.IsFailure);
        Assert.False(result.IsComplete);
        Assert.Equal("Disk full", result.Message);
    }

    [Fact]
    public void ToLine_ClampsPercentAndCollapsesNewlines()
    {
        InstallProgressLine line = new(150, "first\r\nsecond");

        Assert.Equal("[100%] first  second", line.ToLine());
        Assert.Equal("[0%] x", new InstallProgressLine(-5, "x").ToLine());
    }

    [Fact]
    public void CompleteLine_IsComplete()
    {
        Assert.True(InstallProgressLine.At(100, "done").IsComplete);
        Assert.False(InstallProgressLine.At(99, "almost").IsComplete);
    }

    [Theory]
    [InlineData("  [7%]   padded message  ", 7, "padded message")]
    [InlineData("[0%]", 0, "")]
    [InlineData("[100%] finished", 100, "finished")]
    public void TryParseLine_AcceptsWhitespaceVariants(string line, int expectedPercent, string expectedMessage)
    {
        bool parsed = InstallProgressLine.TryParseLine(line, out InstallProgressLine? result);

        Assert.True(parsed);
        Assert.NotNull(result);
        Assert.Equal(expectedPercent, result.Percent);
        Assert.Equal(expectedMessage, result.Message);
        Assert.False(result.IsFailure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Installed to C:\\x")]
    [InlineData("[42] missing percent sign")]
    [InlineData("[abc%] not a number")]
    [InlineData("[-1%] negative")]
    [InlineData("[] empty token")]
    [InlineData("[fail] wrong case")]
    [InlineData("42% no brackets")]
    public void TryParseLine_RejectsNonProgressLines(string? line)
    {
        bool parsed = InstallProgressLine.TryParseLine(line, out InstallProgressLine? result);

        Assert.False(parsed);
        Assert.Null(result);
    }
}
