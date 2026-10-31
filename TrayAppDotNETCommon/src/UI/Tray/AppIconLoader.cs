using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>
/// Loads an app's icon: an <c>app.ico</c> next to the executable when present, otherwise the embedded Avalonia
/// resource every app links as <c>Assets/app.ico</c>.
/// </summary>
public sealed class AppIconLoader(string resourceUri, string fileName = AppIconLoader.DefaultFileName)
{
    public const string DefaultFileName = "app.ico";

    /// <summary>Returns the ICO file bytes, or null when neither the file nor the resource exists.</summary>
    public byte[]? LoadIcoBytes()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(filePath)) return File.ReadAllBytes(filePath);

        Uri uri = new(resourceUri);
        if (!AssetLoader.Exists(uri)) return null;

        using Stream stream = AssetLoader.Open(uri);
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public WindowIcon? LoadWindowIcon()
    {
        try
        {
            byte[]? icoBytes = LoadIcoBytes();
            return icoBytes == null ? null : new WindowIcon(new MemoryStream(icoBytes, writable: false));
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppIconLoader.LoadWindowIcon: {ex.Message}");
            return null;
        }
    }

    /// <summary>Returns a caller-owned icon at <paramref name="size"/> pixels, by default the taskbar's icon size.</summary>
    public NativeIcon? LoadTrayIcon(int? size = null)
    {
        try
        {
            byte[]? icoBytes = LoadIcoBytes();
            return icoBytes == null
                ? null
                : NativeIcon.FromIco(icoBytes, size ?? TrayAppDotNETTrayIconMetrics.GetTaskbarSmallIconSize());
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppIconLoader.LoadTrayIcon: {ex.Message}");
            return null;
        }
    }

    /// <summary>Returns the icon frame nearest <paramref name="size"/> pixels as an Avalonia bitmap.</summary>
    public Bitmap? LoadBitmap(int size)
    {
        try
        {
            byte[]? icoBytes = LoadIcoBytes();
            if (icoBytes == null) return null;

            using MemoryStream pngStream = new(NativeIcon.ExtractICOImage(icoBytes, size), writable: false);
            return new Bitmap(pngStream);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppIconLoader.LoadBitmap: {ex.Message}");
            return null;
        }
    }
}
