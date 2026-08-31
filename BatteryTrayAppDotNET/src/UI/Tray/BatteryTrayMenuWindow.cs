using System.Diagnostics;
using Avalonia;
using Avalonia.Media;

namespace BatteryTrayAppDotNET.UI.Tray;

public sealed class BatteryTrayMenuWindow : ContextMenuWindow
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
            new ContextMenuWindowOptions
            {
                Palette = palette,
                Rounded = settings.EnableRoundedCorners,
                FontSize = settings.ContextMenuFontSize,
                ContextMenuSettings = settings,
                SeparatorColor = ResolveSeparatorColor(palette),
                ShadowColor = ResolveMenuShadowColor(),
                ScrollToBottom = true
            })
    {
    }

    internal void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        ContextMenuPosition placement) =>
        base.ShowAt(trayIcon, cursorPoint, ToCommonPlacement(placement));

    private static List<ContextMenuEntry> BuildEntries(
        Action openPowerOptions,
        Action openBatteryReport,
        Action openSettings,
        Action exit)
    {
        ContextMenuEntryBuilder entries = new();
        entries.Add(text: "Power options", openPowerOptions);
        entries.Add(text: "Battery report", openBatteryReport);
        entries.AddSeparator();
        entries.Add(text: "Settings", openSettings);
        entries.AddSeparator();
        entries.Add(text: "Exit", exit);
        return entries.ToList();
    }

    internal static void OpenPowerOptions()
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = "control.exe", Arguments = "/name Microsoft.PowerOptions", UseShellExecute = false
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

    private static ContextMenuPlacement ToCommonPlacement(ContextMenuPosition placement) =>
        placement == ContextMenuPosition.Modern
            ? ContextMenuPlacement.Modern
            : ContextMenuPlacement.Classic;
}
