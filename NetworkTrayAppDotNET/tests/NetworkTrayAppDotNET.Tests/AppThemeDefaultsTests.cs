using NetworkTrayAppDotNET.Visuals;
using Xunit;
using AppTheme = NetworkTrayAppDotNET.Visuals.AppTheme;
using ThemeColor = TrayAppDotNETCommon.Visuals.ThemeColor;

namespace NetworkTrayAppDotNET.Tests;

public sealed class AppThemeDefaultsTests
{
    [Fact]
    public void EveryNetworkThemeColorDefaultsToItsAxamlValue()
    {
        AppThemeResources resources = new();
        using AppTheme theme = new();
        System.Reflection.PropertyInfo[] properties =
        [
            .. typeof(AppTheme).GetProperties()
                .Where(static property => property.DeclaringType == typeof(AppTheme)
                                          && property.PropertyType == typeof(ThemeColor))
        ];

        Assert.NotEmpty(properties);
        foreach (System.Reflection.PropertyInfo property in properties)
        {
            ThemeColor expected = Assert.IsType<ThemeColor>(resources[$"NetworkAppTheme.{property.Name}"]);
            ThemeColor actual = Assert.IsType<ThemeColor>(property.GetValue(theme));
            Assert.Equal(expected.LightHex, actual.LightHex);
            Assert.Equal(expected.DarkHex, actual.DarkHex);
        }
    }
}
