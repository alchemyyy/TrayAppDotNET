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
            text: "{Comm",
            caretIndex: 5,
            out ProcessSearchColumnToken incompleteToken));
        Assert.Equal(expected: "Comm", incompleteToken.Fragment);
        Assert.Equal(expected: -1, incompleteToken.ClosingBraceIndex);

        Assert.True(ProcessSearchAutocompleteLogic.TryGetColumnToken(
            text: "{Command line}=test",
            caretIndex: 5,
            out ProcessSearchColumnToken closedToken));
        Assert.Equal(expected: "Comm", closedToken.Fragment);
        Assert.Equal(expected: 13, closedToken.ClosingBraceIndex);
    }

    [Fact]
    public void DoesNotOfferColumnsOutsideBracesOrInsideQuotedRegex()
    {
        Assert.False(ProcessSearchAutocompleteLogic.TryGetColumnToken(text: "{Name}=test", caretIndex: 6, out _));
        Assert.False(ProcessSearchAutocompleteLogic.TryGetColumnToken(
            text: "{Name}=~\"foo{bar\"",
            caretIndex: 15,
            out _));
    }

    [Fact]
    public void RanksCanonicalTitlesAndNicknames()
    {
        List<ProcessColumnSetting> settings = ProcessColumnSettings.CreateDefault();
        ProcessColumnSetting commandLine =
            settings.Single(static setting => setting.Column == ProcessTableColumnKind.CommandLine);
        commandLine.Nickname = "Arguments";

        ProcessSearchColumnSuggestion[] canonical =
            ProcessSearchAutocompleteLogic.RankSuggestions(fragment: "command", settings, maximumSuggestionCount: 8);
        ProcessSearchColumnSuggestion[] nickname =
            ProcessSearchAutocompleteLogic.RankSuggestions(fragment: "args", settings, maximumSuggestionCount: 8);

        Assert.Equal(ProcessTableColumnKind.CommandLine, canonical[0].Column);
        Assert.Equal(ProcessTableColumnKind.CommandLine, nickname[0].Column);
        Assert.Contains(expectedSubstring: "Arguments", nickname[0].DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletesIncompleteTokenAndAddsClosingBrace()
    {
        ProcessSearchColumnSuggestion suggestion = new(
            ProcessTableColumnKind.CommandLine,
            ColumnName: "Command line",
            DisplayText: "Command line");

        bool completed = ProcessSearchAutocompleteLogic.TryComplete(
            text: "{Comm",
            caretIndex: 5,
            suggestion,
            out string completedText,
            out int completedCaretIndex);

        Assert.True(completed);
        Assert.Equal(expected: "{Command line}", completedText);
        Assert.Equal(completedText.Length, completedCaretIndex);
    }

    [Fact]
    public void ReplacesWholeClosedTokenWhenCaretMovesBackInside()
    {
        ProcessSearchColumnSuggestion suggestion = new(
            ProcessTableColumnKind.Lifetime,
            ColumnName: "Lifetime",
            DisplayText: "Lifetime");

        bool completed = ProcessSearchAutocompleteLogic.TryComplete(
            text: "{Life typo}>=1h",
            caretIndex: 5,
            suggestion,
            out string completedText,
            out int completedCaretIndex);

        Assert.True(completed);
        Assert.Equal(expected: "{Lifetime}>=1h", completedText);
        Assert.Equal(expected: 10, completedCaretIndex);
    }
}
