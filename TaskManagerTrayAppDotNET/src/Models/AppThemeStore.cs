using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.Models;

internal static class AppThemeStore
{
    private const int ApplicationBitmapSize = 256;

    /// <summary>Loads the generated application icon as an Avalonia bitmap.</summary>
    public static Bitmap? LoadAppBitmap()
    {
        try
        {
            byte[]? ICOBytes = LoadAppICOBytes();
            if (ICOBytes == null) return null;

            byte[] PNGBytes = NativeIcon.ExtractICOImage(ICOBytes, ApplicationBitmapSize);
            using MemoryStream PNGStream = new(PNGBytes, writable: false);
            return new Bitmap(PNGStream);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"AppThemeStore.LoadAppBitmap: {exception.Message}");
            return null;
        }
    }

    public static NativeIcon? LoadAppNativeIcon()
    {
        try
        {
            int size = TrayAppDotNETTrayIconMetrics.GetTaskbarSmallIconSize();
            byte[]? ICOBytes = LoadAppICOBytes();
            return ICOBytes == null ? null : NativeIcon.FromIco(ICOBytes, size);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"AppThemeStore.LoadAppNativeIcon: {exception.Message}");
        }

        return null;
    }

    internal static byte[]? LoadAppICOBytes()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, Constants.AppIconFileName);
        if (File.Exists(filePath)) return File.ReadAllBytes(filePath);

        Uri uri = new(Constants.AppIconResourceUri);
        if (!AssetLoader.Exists(uri)) return null;

        using Stream stream = AssetLoader.Open(uri);
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static string GetDefaultPath()
    {
        string appDirectory = AppSettings.GetDefaultDirectory();
        Directory.CreateDirectory(appDirectory);
        return Path.Combine(appDirectory, path2: "theme.xml");
    }

    public static AppTheme LoadOrDefault(string path) =>
        AppTheme.LoadOrDefault<AppTheme>(path);
}
