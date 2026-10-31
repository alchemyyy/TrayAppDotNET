using Avalonia.Media;
using TaskManagerTrayAppDotNET.Models;
using TrayAppDotNETCommon.Visuals;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerThemeFileTests
{
    [Fact]
    public void ThemeXmlIsApplied()
    {
        using TempDirectory temp = new();
        string path = temp.Combine("theme.xml");
        File.WriteAllText(path, """<Theme><Accent LightHex="#FF112233" DarkHex="#FF445566" /></Theme>""");

        using AppTheme theme = AppThemeStore.LoadOrDefault(path);

        Assert.IsType<TaskManagerAppTheme>(theme);
        Assert.Equal(Color.Parse("#FF112233"), theme.Accent.Light);
        Assert.Equal(Color.Parse("#FF445566"), theme.Accent.Dark);
    }
}
