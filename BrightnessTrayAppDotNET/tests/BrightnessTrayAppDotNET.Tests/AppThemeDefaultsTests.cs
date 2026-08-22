using BrightnessTrayAppDotNET.Visuals;
using Xunit;
using AppTheme = BrightnessTrayAppDotNET.Visuals.AppTheme;
using ThemeColor = TrayAppDotNETCommon.Visuals.ThemeColor;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class AppThemeDefaultsTests
{
    [Fact]
    public void EveryBrightnessThemeColorHasAnAxamlDefault()
    {
        AppThemeResources resources = new();
        string[] propertyNames =
        [
            .. typeof(AppTheme).GetProperties()
                .Where(static property => property.DeclaringType == typeof(AppTheme)
                                          && property.PropertyType == typeof(ThemeColor))
                .Select(static property => property.Name)
        ];

        Assert.NotEmpty(propertyNames);
        foreach (string propertyName in propertyNames)
            Assert.IsType<ThemeColor>(resources[$"BrightnessAppTheme.{propertyName}"]);

        using AppTheme theme = new();
        Assert.Equal(
            resources.SingleColor(nameof(AppTheme.EnvironmentalMapHudBackdropAlpha)).A,
            theme.EnvironmentalMapHudBackdropAlpha);
    }

    [Fact]
    public void ExplicitMapHudAlphaOverridesTheAxamlDefault()
    {
        using AppTheme theme = new() { EnvironmentalMapHudBackdropAlpha = 128 };

        Assert.Equal(128, theme.EnvironmentalMapHudBackdropAlpha);
    }
}
