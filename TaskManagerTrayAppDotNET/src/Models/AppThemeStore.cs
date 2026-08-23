using TrayAppDotNETCommon.Visuals;
using Avalonia.Platform;

namespace TaskManagerTrayAppDotNET.Models;

internal static class AppThemeStore
{
    public static NativeIcon? LoadAppNativeIcon()
    {
        try
        {
            int size = TrayAppDotNETTrayIconMetrics.GetTaskbarSmallIconSize();
            string filePath = Path.Combine(AppContext.BaseDirectory, Constants.AppIconFileName);
            if (File.Exists(filePath)) return NativeIcon.FromIco(File.ReadAllBytes(filePath), size);

            Uri uri = new(Constants.AppIconResourceUri);
            if (AssetLoader.Exists(uri))
            {
                using Stream stream = AssetLoader.Open(uri);
                using MemoryStream memory = new();
                stream.CopyTo(memory);
                return NativeIcon.FromIco(memory.ToArray(), size);
            }
        }
        catch (Exception exception)
        {
            TADNLog.Log($"AppThemeStore.LoadAppNativeIcon: {exception.Message}");
        }

        return null;
    }

    public static string GetDefaultPath()
    {
        string appDirectory = AppSettings.GetDefaultDirectory();
        Directory.CreateDirectory(appDirectory);
        return Path.Combine(appDirectory, "theme.xml");
    }

    public static AppTheme LoadOrDefault(string path) =>
        AppTheme.LoadOrDefault<AppTheme>(path);
}
