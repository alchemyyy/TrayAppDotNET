using System.Xml.Serialization;
using Avalonia.Media;
using CommonAppTheme = TrayAppDotNETCommon.Visuals.AppTheme;

namespace VolumeTrayAppDotNET.Visuals;

[XmlRoot("Theme")]
public sealed class AppTheme : CommonAppTheme
{
    public new static AppTheme Default { get; } = new();

    // Each default reads the current dictionary; Debug reloads update its ThemeColor instances in place
    private static AppThemeResources.VolumeAppThemeAxamlProperties ThemeDefaults =>
        AppThemeResources.Current.AxamlVolumeAppTheme;

    public static Color MeterPeakColorDefault => ThemeDefaults.MeterPeakColorDefault;

    public static Color MeterPeakStereoColorDefault =>
        ThemeDefaults.MeterPeakStereoColorDefault;

    public static string MeterPeakColorDefaultHex => ToArgbHex(MeterPeakColorDefault);
    public static string MeterPeakStereoColorDefaultHex => ToArgbHex(MeterPeakStereoColorDefault);

    internal static string LegacyMeterPeakColorDefaultHex =>
        ToArgbHex(ThemeDefaults.LegacyMeterPeakColorDefault);

    internal static string LegacyMeterPeakStereoColorDefaultHex =>
        ToArgbHex(ThemeDefaults.LegacyMeterPeakStereoColorDefault);

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, path2: "theme.xml");
    }

    public static AppTheme LoadOrDefault(string filePath) =>
        CommonAppTheme.LoadOrDefault<AppTheme>(
            filePath,
            static message => TADNLog.Log($"AppTheme.LoadOrDefault: {message}"));

    public static AppTheme Load(string filePath) =>
        CommonAppTheme.Load<AppTheme>(filePath);

    private static string ToArgbHex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
