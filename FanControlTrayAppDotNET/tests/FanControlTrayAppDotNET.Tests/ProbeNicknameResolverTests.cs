using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class ProbeNicknameResolverTests
{
    /// <summary>
    /// Verifies first-run probe nickname rules seed the default replacements.
    /// </summary>
    [Fact]
    public void EnsureDefaultProbeNicknameRulesSeedsDefaultRules()
    {
        AppSettings settings = new();

        bool seeded = settings.EnsureDefaultProbeNicknameRules();

        Assert.True(seeded);
        Assert.True(settings.ProbeNicknamesInitialized);
        AssertDefaultProbeNicknameRules(settings.ProbeNicknameRules);

        settings.ProbeNicknameRules[0].ReplacementString = "Package";

        bool reseeded = settings.EnsureDefaultProbeNicknameRules();

        Assert.False(reseeded);
        Assert.Equal("Package", settings.ProbeNicknameRules[0].ReplacementString);
    }

    /// <summary>
    /// Verifies explicit default loading preserves custom probe nickname rules.
    /// </summary>
    [Fact]
    public void LoadDefaultProbeNicknameRulesPreservesCustomRules()
    {
        AppSettings settings = new()
        {
            ProbeNicknamesInitialized = true,
            ProbeNicknameRules =
            [
                new DeviceNicknameRule
                {
                    TargetRegex = "^Custom$",
                    ReplacementString = "Custom",
                },
                new DeviceNicknameRule
                {
                    TargetRegex = "\\(Tdie\\)",
                    ReplacementString = "Die",
                },
            ],
        };

        bool loaded = settings.LoadDefaultProbeNicknameRules();

        Assert.True(loaded);
        Assert.Equal(5, settings.ProbeNicknameRules.Count);
        AssertDefaultProbeNicknameRules(settings.ProbeNicknameRules.GetRange(0, 4));
        Assert.Equal("^Custom$", settings.ProbeNicknameRules[4].TargetRegex);
        Assert.Equal("Custom", settings.ProbeNicknameRules[4].ReplacementString);
    }

    /// <summary>
    /// Verifies probe nickname rules are applied as regex replacements.
    /// </summary>
    [Fact]
    public void CreateAppliesProbeNicknameRules()
    {
        AppSettings settings = new();
        settings.EnsureDefaultProbeNicknameRules();
        ProbeNicknameResolver resolver = ProbeNicknameResolver.Create(settings);

        Assert.Equal("CPU Package", resolver.Resolve("CPU Package (Tdie)"));
    }

    /// <summary>
    /// Verifies a rule list starts with the default probe nickname rules.
    /// </summary>
    private static void AssertDefaultProbeNicknameRules(List<DeviceNicknameRule> rules)
    {
        Assert.Equal(4, rules.Count);
        Assert.Equal("\\(Tdie\\)", rules[0].TargetRegex);
        Assert.Equal(string.Empty, rules[0].ReplacementString);
        Assert.Equal("\\(Tctl/Tdie\\)", rules[1].TargetRegex);
        Assert.Equal(string.Empty, rules[1].ReplacementString);
        Assert.Equal("\\(SMU\\)", rules[2].TargetRegex);
        Assert.Equal(string.Empty, rules[2].ReplacementString);
        Assert.Equal("CPU Core", rules[3].TargetRegex);
        Assert.Equal("Core", rules[3].ReplacementString);
    }
}
