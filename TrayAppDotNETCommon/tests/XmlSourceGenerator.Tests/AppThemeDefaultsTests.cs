using Avalonia.Media;
using TrayAppDotNETCommon.Visuals;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class AppThemeDefaultsTests
{
    [Fact]
    public void BackgroundUsesNeutralLightAndDarkDefaults()
    {
        using AppTheme theme = new();

        Assert.Equal("#F3F3F3", theme.Background.LightHex);
        Assert.Equal("#202020", theme.Background.DarkHex);
    }

    [Fact]
    public void InteractiveColorsUseModeAppropriateNeutralDefaults()
    {
        using AppTheme theme = new();

        Assert.Equal("#222222", theme.Accent.LightHex);
        Assert.Equal("#DDDDDD", theme.Accent.DarkHex);
        Assert.Equal("#DFDFDF", theme.SearchListItemSelected.LightHex);
        Assert.Equal("#454545", theme.SearchListItemSelected.DarkHex);
        Assert.Equal("#E9E9E9", theme.SearchListItemHover.LightHex);
        Assert.Equal("#292929", theme.SearchListItemHover.DarkHex);
    }

    [Fact]
    public void StructuralFallbacksRemainAchromatic()
    {
        using AppTheme theme = new();
        (string Name, ThemeColor Color)[] structuralColors =
        [
            (nameof(theme.Background), theme.Background),
            (nameof(theme.Foreground), theme.Foreground),
            (nameof(theme.Border), theme.Border),
            (nameof(theme.Separator), theme.Separator),
            (nameof(theme.Hover), theme.Hover),
            (nameof(theme.Pressed), theme.Pressed),
            (nameof(theme.ControlBackground), theme.ControlBackground),
            (nameof(theme.ControlBorder), theme.ControlBorder),
            (nameof(theme.DisabledForeground), theme.DisabledForeground),
            (nameof(theme.Accent), theme.Accent),
            (nameof(theme.Acrylic), theme.Acrylic),
            (nameof(theme.SecondaryForeground), theme.SecondaryForeground),
            (nameof(theme.FooterBackground), theme.FooterBackground),
            (nameof(theme.SliderTrack), theme.SliderTrack),
            (nameof(theme.SliderProgress), theme.SliderProgress),
            (nameof(theme.SliderThumb), theme.SliderThumb),
            (nameof(theme.ButtonHover), theme.ButtonHover),
            (nameof(theme.ButtonPressed), theme.ButtonPressed),
            (nameof(theme.IconForeground), theme.IconForeground),
            (nameof(theme.CardBackground), theme.CardBackground),
            (nameof(theme.TextBoxFocused), theme.TextBoxFocused),
            (nameof(theme.SearchListItemSelected), theme.SearchListItemSelected),
            (nameof(theme.SearchListItemHover), theme.SearchListItemHover),
            (nameof(theme.ToggleSwitchOnTrack), theme.ToggleSwitchOnTrack),
            (nameof(theme.ToggleSwitchOnThumb), theme.ToggleSwitchOnThumb),
            (nameof(theme.CloseButtonGlyphActive), theme.CloseButtonGlyphActive),
            (nameof(theme.FlyoutOverlayBackdrop), theme.FlyoutOverlayBackdrop),
            (nameof(theme.FlyoutShadow), theme.FlyoutShadow),
            (nameof(theme.MenuShadow), theme.MenuShadow)
        ];

        foreach ((string name, ThemeColor color) in structuralColors)
        {
            AssertAchromatic(name, "light", color.Light);
            AssertAchromatic(name, "dark", color.Dark);
        }
    }

    private static void AssertAchromatic(string name, string variant, Color color)
    {
        Assert.True(
            color.R == color.G && color.G == color.B,
            $"{name} {variant} fallback must remain neutral, but was {color}.");
    }
}
