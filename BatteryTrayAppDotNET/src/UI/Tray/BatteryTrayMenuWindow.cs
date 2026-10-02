using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.ControlMapping;

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
            }) =>
        this.MapTo(ControlMap.TrayMenu.ID);

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
        entries.Add(new ContextMenuEntry(Text: "Power options", openPowerOptions)
        {
            Node = ControlMap.TrayMenu.OpenPowerOptions
        });
        entries.Add(new ContextMenuEntry(Text: "Battery report", openBatteryReport)
        {
            Node = ControlMap.TrayMenu.OpenBatteryReport
        });
        entries.AddSeparator();
        entries.Add(new ContextMenuEntry(Text: "Settings", openSettings) { Node = ControlMap.TrayMenu.OpenSettings });
        entries.AddSeparator();
        entries.Add(new ContextMenuEntry(Text: "Exit", exit) { Node = ControlMap.TrayMenu.Exit });
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
