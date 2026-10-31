using System.Diagnostics;
using System.Text.RegularExpressions;
using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class NicknameRegexTests
{
    [Fact]
    public void BacktrackingPatternTimesOutAsNoMatch()
    {
        Assert.True(NicknameRegex.TryCreate("^(a+)+$", "Device", out Regex? regex));
        string input = new string('a', 64) + "!";
        Stopwatch elapsed = Stopwatch.StartNew();

        Assert.False(NicknameRegex.IsMatch(regex, input));
        Assert.Equal(input, NicknameRegex.Replace(regex, input, "x"));
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void InvalidPatternIsSkipped() =>
        Assert.False(NicknameRegex.TryCreate("(unclosed", "Probe", out _));

    [Fact]
    public void ResolverPicksUpARuleEditedInPlace()
    {
        DeviceNicknameRule rule = new() { TargetRegex = "^Tctl$", ReplacementString = "Package" };
        AppSettings settings = new() { ProbeNicknameRules = [rule] };
        Assert.Equal("Package", ProbeNicknameResolver.Create(settings).Resolve("Tctl"));

        // The settings page edits rule objects in place, so the compiled rules key on their content
        rule.ReplacementString = "Die";
        Assert.Equal("Die", ProbeNicknameResolver.Create(settings).Resolve("Tctl"));
    }
}
