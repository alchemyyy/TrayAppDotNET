using TrayAppDotNETCommon.Utils;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class SearchMatcherTests
{
    [Fact]
    public void ScoreMatchesWordsOutOfMiddle()
    {
        SearchMatch match = SearchMatcher.Score(candidate: "AMD Ryzen 9950X CCD0", query: "ccd");

        Assert.True(match.IsMatch);
    }

    [Fact]
    public void ScoreMatchesSubsequence()
    {
        SearchMatch match = SearchMatcher.Score(candidate: "CPU Package Temperature", query: "cpt");

        Assert.True(match.IsMatch);
    }

    [Fact]
    public void FilterAndRankPrefersPrefixOverContains()
    {
        List<string> items =
        [
            "Package Temperature",
            "Temperature Package",
            "Temp"
        ];

        List<string> ranked = SearchMatcher.FilterAndRank(items, query: "temp", static item => item);

        Assert.Equal(expected: "Temp", ranked[0]);
        Assert.Equal(expected: "Temperature Package", ranked[1]);
        Assert.Equal(expected: "Package Temperature", ranked[2]);
    }

    [Fact]
    public void FilterAndRankRequiresEveryToken()
    {
        List<string> items =
        [
            "CPU Package Temperature",
            "GPU Package Temperature",
            "CPU Package Power"
        ];

        List<string> ranked = SearchMatcher.FilterAndRank(items, query: "cpu temp", static item => item);

        Assert.Single(ranked);
        Assert.Equal(expected: "CPU Package Temperature", ranked[0]);
    }
}
