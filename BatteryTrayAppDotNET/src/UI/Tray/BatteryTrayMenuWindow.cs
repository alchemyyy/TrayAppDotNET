using System.Diagnostics;
using Avalonia;
using Avalonia.Media;

namespace BatteryTrayAppDotNET.UI.Tray;

public sealed class BatteryTrayMenuWindow : TrayMenuWindow
{
    internal BatteryTrayMenuWindow(
        AppSettings settings,
        SettingsPalette palette,
        Action openPowerOptions,
        Action openBatteryReport,
        Action openSettings,
        Action exit)
        : base(
            BuildEntries(openPowerOptions, openBatteryReport, openSettings, exit),
            new TrayMenuWindowOptions
            {
                Palette = palette,
                Rounded = settings.EnableRoundedCorners,
                FontSize = settings.ContextMenuFontSize,
                SeparatorColor = ResolveSeparatorColor(palette),
                ShadowColor = ResolveMenuShadowColor(),
                ScrollToBottom = true,
            })
    {
    }

    internal void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        ContextMenuPosition placement) =>
        base.ShowAt(trayIcon, cursorPoint, ToCommonPlacement(placement));

    private static List<TrayMenuEntry> BuildEntries(
        Action openPowerOptions,
        Action openBatteryReport,
        Action openSettings,
        Action exit)
    {
        TrayMenuEntryBuilder entries = new();
        entries.Add("Power options", openPowerOptions);
        entries.Add("Battery report", openBatteryReport);
        entries.AddSeparator();
        entries.Add("Settings", openSettings);
        entries.AddSeparator();
        entries.Add("Exit", exit);
        return entries.ToList();
    }

    internal static void OpenPowerOptions()
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = "control.exe",
                Arguments = "/name Microsoft.PowerOptions",
                UseShellExecute = false,
            });
        }
        catch (Exception ex) { TADNLog.Log($"BatteryTrayMenuWindow.OpenPowerOptions: {ex.Message}"); }
    }

    private static Color ResolveSeparatorColor(SettingsPalette palette)
    {
        bool isLight = AppTheme.ResolveEffectiveIsLightTheme(AppServices.Settings);
        return AppServices.Theme?.Separator.For(isLight) ?? palette.Border;
    }

    private static Color ResolveMenuShadowColor()
    {
        bool isLight = AppTheme.ResolveEffectiveIsLightTheme(AppServices.Settings);
        return (AppServices.Theme ?? AppTheme.Default).MenuShadow.For(isLight);
    }

    private static TrayMenuWindowPlacement ToCommonPlacement(ContextMenuPosition placement) =>
        placement == ContextMenuPosition.Modern
            ? TrayMenuWindowPlacement.Modern
            : TrayMenuWindowPlacement.Classic;
}
