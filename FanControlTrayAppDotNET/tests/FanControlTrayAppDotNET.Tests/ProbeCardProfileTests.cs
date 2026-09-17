using FanControlTrayAppDotNET.Models;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class ProbeCardProfileTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    public void NewCardsBelongOnlyToTheCurrentProfile(int selectedIndex, int expectedMask)
    {
        ProbeCard card = new();
        Assert.True(card.EnsureProfileVisibility(selectedIndex));
        Assert.Equal(expectedMask, card.DisplayProfileMask);
        Assert.False(card.EnsureProfileVisibility(selectedIndex));
    }

    [Fact]
    public void LastProfileCannotBeUnchecked()
    {
        ProbeCard card = new() { DisplayProfileMask = 1 };
        Assert.False(card.TrySetProfileVisibility(0, false));
        Assert.Equal(1, card.DisplayProfileMask);
        Assert.True(card.TrySetProfileVisibility(2, true));
        Assert.Equal(5, card.DisplayProfileMask);
        Assert.True(card.TrySetProfileVisibility(0, false));
        Assert.Equal(4, card.DisplayProfileMask);
        Assert.False(card.TrySetProfileVisibility(2, false));
        Assert.Equal(4, card.DisplayProfileMask);
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(8, 2, 4)]
    [InlineData(9, 2, 1)]
    [InlineData(-1, 0, 7)]
    [InlineData(0, -1, 1)]
    [InlineData(0, 3, 4)]
    public void InvalidVisibilityIsNormalizedWithoutOrphaning(int mask, int selectedIndex, int expectedMask)
    {
        ProbeCard card = new() { DisplayProfileMask = mask };
        Assert.True(card.EnsureProfileVisibility(selectedIndex));
        Assert.Equal(expectedMask, card.DisplayProfileMask);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(32)]
    public void InvalidProfileIndexCannotBeSelected(int profileIndex)
    {
        ProbeCard card = new() { DisplayProfileMask = 1 };
        Assert.False(card.TrySetProfileVisibility(profileIndex, true));
        Assert.False(card.IsVisibleOnProfile(profileIndex));
        Assert.Equal(1, card.DisplayProfileMask);
    }

    [Fact]
    public void InvalidBitsCannotBypassTheLastProfileGuard()
    {
        ProbeCard card = new() { DisplayProfileMask = 9 };
        Assert.False(card.TrySetProfileVisibility(0, false));
        Assert.True(card.IsVisibleOnProfile(0));
    }

    [Theory]
    [InlineData("", 0, "Profile 1")]
    [InlineData(" ", 2, "Profile 3")]
    [InlineData("Gaming", 1, "Gaming")]
    public void ProfileLabelsUseCustomNamesOrNumberedFallback(string name, int profileIndex, string expected)
    {
        FanProfile profile = new() { Name = name };
        Assert.Equal(expected, profile.DisplayName(profileIndex));
    }
}
