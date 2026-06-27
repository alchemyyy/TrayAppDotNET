using System.Xml.Serialization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using TrayAppDotNETCommon.Visuals;
using CommonAppTheme = TrayAppDotNETCommon.Visuals.AppTheme;

namespace BatteryTrayAppDotNET.Visuals;

[XmlRoot("Theme")]
public sealed class AppTheme : TrayAppDotNETCommon.Visuals.AppTheme
{
    public new static AppTheme Default { get; } = new();

    public ThemeColor BatteryChargingFill { get; set; } = new("107C10");
    public ThemeColor BatteryCriticalFill { get; set; } = new("E81123");
    public ThemeColor BatteryLowFill { get; set; } = new("FFB900");

    public static WindowIcon? LoadAppIcon()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, Constants.AppIconFileName);
        if (File.Exists(filePath)) return new WindowIcon(filePath);

        Uri uri = new(Constants.AppIconResourceUri);
        if (AssetLoader.Exists(uri))
        {
            using Stream stream = AssetLoader.Open(uri);
            return new WindowIcon(stream);
        }

        return null;
    }

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
        catch (Exception ex)
        {
            TADNLog.Log($"AppTheme.LoadAppNativeIcon: {ex.Message}");
        }

        return null;
    }

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "theme.xml");
    }

    public static AppTheme LoadOrDefault(string filePath) =>
        CommonAppTheme.LoadOrDefault<AppTheme>(filePath);

    public static AppTheme Load(string filePath) =>
        CommonAppTheme.Load<AppTheme>(filePath);

    public void SaveToDefaultPath() => Save(GetDefaultPath());

    public static bool ResolveEffectiveIsLightTheme(AppSettings? settings)
    {
        bool systemIsLight = AppServices.Theme?.IsLightTheme ?? false;
        if (settings == null) return systemIsLight;

        return settings.ThemeMode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => systemIsLight,
        };
    }

    public Color ResolveBatteryFill(BatterySnapshot snapshot, bool isLightTheme)
    {
        if (snapshot.IsCharging) return BatteryChargingFill.For(isLightTheme);
        if (!snapshot.IsOnExternalPower && snapshot.ChargePercentage <= 10) return BatteryCriticalFill.For(isLightTheme);
        if (!snapshot.IsOnExternalPower && snapshot.ChargePercentage <= 20) return BatteryLowFill.For(isLightTheme);
        return Accent.For(isLightTheme);
    }

    public Color ResolveFlyoutBackground(AppSettings? settings, bool isLightTheme) =>
        settings?.FlyoutBackgroundColor.Resolve(isLightTheme) ?? Background.For(isLightTheme);

    public Color ResolveFlyoutTitleBarBackground(AppSettings? settings, bool isLightTheme) =>
        settings?.FlyoutTitleBarBackgroundColor.Resolve(isLightTheme) ?? FooterBackground.For(isLightTheme);
}
