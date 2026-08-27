using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessSearchAutocompleteTests
{
    [Fact]
    public void FindsIncompleteAndClosedTokensAtTheCaret()
    {
        Assert.True(ProcessSearchAutocompleteLogic.TryGetColumnToken(
            "{Comm",
            5,
            out ProcessSearchColumnToken incompleteToken));
        Assert.Equal("Comm", incompleteToken.Fragment);
        Assert.Equal(-1, incompleteToken.ClosingBraceIndex);

        Assert.True(ProcessSearchAutocompleteLogic.TryGetColumnToken(
            "{Command line}=test",
            5,
            out ProcessSearchColumnToken closedToken));
        Assert.Equal("Comm", closedToken.Fragment);
        Assert.Equal(13, closedToken.ClosingBraceIndex);
    }

    [Fact]
    public void DoesNotOfferColumnsOutsideBracesOrInsideQuotedRegex()
    {
        Assert.False(ProcessSearchAutocompleteLogic.TryGetColumnToken("{Name}=test", 6, out _));
        Assert.False(ProcessSearchAutocompleteLogic.TryGetColumnToken(
            "{Name}=~\"foo{bar\"",
            15,
            out _));
    }

    [Fact]
    public void RanksCanonicalTitlesAndNicknames()
    {
        List<ProcessColumnSetting> settings = ProcessColumnSettings.CreateDefault();
        ProcessColumnSetting commandLine = settings.Single(
            static setting => setting.Column == ProcessTableColumnKind.CommandLine);
        commandLine.Nickname = "Arguments";

        ProcessSearchColumnSuggestion[] canonical =
            ProcessSearchAutocompleteLogic.RankSuggestions("command", settings, 8);
        ProcessSearchColumnSuggestion[] nickname =
            ProcessSearchAutocompleteLogic.RankSuggestions("args", settings, 8);

        Assert.Equal(ProcessTableColumnKind.CommandLine, canonical[0].Column);
        Assert.Equal(ProcessTableColumnKind.CommandLine, nickname[0].Column);
        Assert.Contains("Arguments", nickname[0].DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletesIncompleteTokenAndAddsClosingBrace()
    {
        ProcessSearchColumnSuggestion suggestion = new(
            ProcessTableColumnKind.CommandLine,
            "Command line",
            "Command line");

        bool completed = ProcessSearchAutocompleteLogic.TryComplete(
            "{Comm",
            5,
            suggestion,
            out string completedText,
            out int completedCaretIndex);

        Assert.True(completed);
        Assert.Equal("{Command line}", completedText);
        Assert.Equal(completedText.Length, completedCaretIndex);
    }

    [Fact]
    public void ReplacesWholeClosedTokenWhenCaretMovesBackInside()
    {
        ProcessSearchColumnSuggestion suggestion = new(
            ProcessTableColumnKind.Lifetime,
            "Lifetime",
            "Lifetime");

        bool completed = ProcessSearchAutocompleteLogic.TryComplete(
            "{Life typo}>=1h",
            5,
            suggestion,
            out string completedText,
            out int completedCaretIndex);

        Assert.True(completed);
        Assert.Equal("{Lifetime}>=1h", completedText);
        Assert.Equal(10, completedCaretIndex);
    }
}
