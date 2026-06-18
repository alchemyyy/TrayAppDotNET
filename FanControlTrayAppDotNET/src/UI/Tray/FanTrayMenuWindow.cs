using Avalonia;
using Avalonia.Media;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace FanControlTrayAppDotNET.UI;

public sealed class FanTrayMenuWindow : TrayMenuWindow
{
    public FanTrayMenuWindow(
        AppSettings settings,
        SettingsPalette palette,
        bool rounded,
        int fontSize,
        Action openSettings,
        Action exit)
        : base(
            BuildEntries(openSettings, exit),
            new TrayMenuWindowOptions
            {
                Palette = palette,
                Rounded = rounded,
                FontSize = fontSize,
                ShadowColor = ResolveMenuShadowColor(settings),
            })
    {
    }

    internal void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        ContextMenuPosition placement) =>
        base.ShowAt(trayIcon, cursorPoint, ToCommonPlacement(placement));

    private static List<TrayMenuEntry> BuildEntries(Action openSettings, Action exit)
    {
        TrayMenuEntryBuilder entries = new();
        entries.Add(L("Tray_Settings", "Settings"), openSettings);
        entries.AddSeparator();
        entries.Add(L("Tray_Exit", "Exit"), exit);
        return entries.ToList();
    }

    private static Color ResolveMenuShadowColor(AppSettings settings)
    {
        bool isLight = AppTheme.ResolveEffectiveIsLightTheme(settings);
        return (AppServices.Theme ?? AppTheme.Default).MenuShadow.For(isLight);
    }

    private static TrayMenuWindowPlacement ToCommonPlacement(ContextMenuPosition placement) =>
        placement == ContextMenuPosition.Modern
            ? TrayMenuWindowPlacement.Modern
            : TrayMenuWindowPlacement.Classic;

    private static string L(string key, string fallback)
    {
        try
        {
            string value = TrayLocalization.Instance[key];
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
