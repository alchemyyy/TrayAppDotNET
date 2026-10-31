using System.Xml.Serialization;
using Avalonia.Media;
using TrayAppDotNETCommon.Visuals;
using CommonAppTheme = TrayAppDotNETCommon.Visuals.AppTheme;

namespace BatteryTrayAppDotNET.Visuals;

[XmlRoot("Theme")]
public sealed class AppTheme : CommonAppTheme
{
    public new static AppTheme Default { get; } = new();

    // Each default reads the current dictionary; Debug reloads update its ThemeColor instances in place
    private static AppThemeResources.BatteryAppThemeAxamlProperties ThemeDefaults =>
        AppThemeResources.Current.AxamlBatteryAppTheme;

    public ThemeColor BatteryChargingFill { get; set; } =
        ThemeDefaults.BatteryChargingFill;

    public ThemeColor BatteryCriticalFill { get; set; } =
        ThemeDefaults.BatteryCriticalFill;

    public ThemeColor BatteryLowFill { get; set; } = ThemeDefaults.BatteryLowFill;

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

    public Color ResolveBatteryFill(BatterySnapshot snapshot, bool isLightTheme)
    {
        if (snapshot.IsCharging) return BatteryChargingFill.For(isLightTheme);
        return snapshot switch
        {
            { IsOnExternalPower: false, ChargePercentage: <= 10 } => BatteryCriticalFill.For(isLightTheme),
            { IsOnExternalPower: false, ChargePercentage: <= 20 } => BatteryLowFill.For(isLightTheme),
            _ => Accent.For(isLightTheme)
        };
    }

    public Color ResolveFlyoutBackground(AppSettings? settings, bool isLightTheme) =>
        settings?.FlyoutBackgroundColor.Resolve(isLightTheme) ?? Background.For(isLightTheme);

    public Color ResolveFlyoutTitleBarBackground(AppSettings? settings, bool isLightTheme) =>
        settings?.FlyoutTitleBarBackgroundColor.Resolve(isLightTheme) ?? FooterBackground.For(isLightTheme);
}
