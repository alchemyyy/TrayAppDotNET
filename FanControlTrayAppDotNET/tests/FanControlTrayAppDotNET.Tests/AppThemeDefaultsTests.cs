using FanControlTrayAppDotNET.Visuals;
using Xunit;
using AppTheme = FanControlTrayAppDotNET.Visuals.AppTheme;
using ThemeColor = TrayAppDotNETCommon.Visuals.ThemeColor;

namespace FanControlTrayAppDotNET.Tests;

public sealed class AppThemeDefaultsTests
{
    [Fact]
    public void EveryFanThemeColorHasAnAxamlDefault()
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
            Assert.IsType<ThemeColor>(resources[$"FanAppTheme.{propertyName}"]);
    }
}
