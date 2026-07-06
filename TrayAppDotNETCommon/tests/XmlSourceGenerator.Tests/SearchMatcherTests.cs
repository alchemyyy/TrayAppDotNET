using TrayAppDotNETCommon.Utils;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class SearchMatcherTests
{
    [Fact]
    public void ScoreMatchesWordsOutOfMiddle()
    {
        SearchMatch match = SearchMatcher.Score("AMD Ryzen 9950X CCD0", "ccd");

        Assert.True(match.IsMatch);
    }

    [Fact]
    public void ScoreMatchesSubsequence()
    {
        SearchMatch match = SearchMatcher.Score("CPU Package Temperature", "cpt");

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

        List<string> ranked = SearchMatcher.FilterAndRank(items, "temp", static item => item);

        Assert.Equal("Temp", ranked[0]);
        Assert.Equal("Temperature Package", ranked[1]);
        Assert.Equal("Package Temperature", ranked[2]);
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

        List<string> ranked = SearchMatcher.FilterAndRank(items, "cpu temp", static item => item);

        Assert.Single(ranked);
        Assert.Equal("CPU Package Temperature", ranked[0]);
    }
}
