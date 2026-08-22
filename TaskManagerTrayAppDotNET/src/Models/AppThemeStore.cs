using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.Models;

internal static class AppThemeStore
{
    public static string GetDefaultPath()
    {
        string appDirectory = AppSettings.GetDefaultDirectory();
        Directory.CreateDirectory(appDirectory);
        return Path.Combine(appDirectory, "theme.xml");
    }

    public static AppTheme LoadOrDefault(string path) =>
        AppTheme.LoadOrDefault<AppTheme>(path);
}
