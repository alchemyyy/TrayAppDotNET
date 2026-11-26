using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using TaskManagerTrayAppDotNET.UI.ProcessesPage;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class SearchAutocompleteTests
{
    [Fact]
    public void FindsIncompleteAndClosedTokensAtTheCaret()
    {
        Assert.True(SearchAutocompleteLogic.TryGetColumnToken(
            text: "{Comm",
            caretIndex: 5,
            out SearchColumnToken incompleteToken));
        Assert.Equal(expected: "Comm", incompleteToken.Fragment);
        Assert.Equal(expected: -1, incompleteToken.ClosingBraceIndex);

        Assert.True(SearchAutocompleteLogic.TryGetColumnToken(
            text: "{Command line}=test",
            caretIndex: 5,
            out SearchColumnToken closedToken));
        Assert.Equal(expected: "Comm", closedToken.Fragment);
        Assert.Equal(expected: 13, closedToken.ClosingBraceIndex);
    }

    [Fact]
    public void DoesNotOfferColumnsOutsideBracesOrInsideQuotedRegex()
    {
        Assert.False(SearchAutocompleteLogic.TryGetColumnToken(text: "{Name}=test", caretIndex: 6, out _));
        Assert.False(SearchAutocompleteLogic.TryGetColumnToken(
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
        SearchColumnCatalog catalog = ProcessSearchColumns.CreateCatalog(settings);

        SearchColumnSuggestion[] canonical =
            SearchAutocompleteLogic.RankSuggestions(fragment: "command", catalog, maximumSuggestionCount: 8);
        SearchColumnSuggestion[] nickname =
            SearchAutocompleteLogic.RankSuggestions(fragment: "args", catalog, maximumSuggestionCount: 8);

        Assert.Equal((int)ProcessTableColumnKind.CommandLine, canonical[0].Column);
        Assert.Equal((int)ProcessTableColumnKind.CommandLine, nickname[0].Column);
        Assert.Contains(expectedSubstring: "Arguments", nickname[0].DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletesIncompleteTokenAndAddsClosingBrace()
    {
        SearchColumnSuggestion suggestion = new(
            (int)ProcessTableColumnKind.CommandLine,
            ColumnName: "Command line",
            DisplayText: "Command line");

        bool completed = SearchAutocompleteLogic.TryComplete(
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
        SearchColumnSuggestion suggestion = new(
            (int)ProcessTableColumnKind.Lifetime,
            ColumnName: "Lifetime",
            DisplayText: "Lifetime");

        bool completed = SearchAutocompleteLogic.TryComplete(
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
