using Avalonia;
using NetworkTrayAppDotNET.Models;

namespace NetworkTrayAppDotNET.UI;

public sealed class NetworkTrayMenuWindow(
    AppSettings settings,
    SettingsPalette palette,
    bool rounded,
    int fontSize,
    string networkSettingsText,
    string adapterSettingsText,
    string settingsText,
    string exitText,
    Action openNetworkSettings,
    Action openAdapterSettings,
    Action openSettings,
    Action exit)
    : ContextMenuWindow(BuildEntries(
            networkSettingsText,
            adapterSettingsText,
            settingsText,
            exitText,
            openNetworkSettings,
            openAdapterSettings,
            openSettings,
            exit),
        new ContextMenuWindowOptions
        {
            Palette = palette, Rounded = rounded, FontSize = fontSize, ContextMenuSettings = settings
        })
{
    public void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        ContextMenuPosition placement) =>
        base.ShowAt(trayIcon, cursorPoint, ToCommonPlacement(placement));

    private static List<ContextMenuEntry> BuildEntries(
        string networkSettingsText,
        string adapterSettingsText,
        string settingsText,
        string exitText,
        Action openNetworkSettings,
        Action openAdapterSettings,
        Action openSettings,
        Action exit)
    {
        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry(networkSettingsText, openNetworkSettings)
        {
            Node = ControlMap.TrayMenu.OpenNetworkSettings
        });
        entries.Add(new ContextMenuEntry(adapterSettingsText, openAdapterSettings)
        {
            Node = ControlMap.TrayMenu.OpenAdapterSettings
        });
        entries.AddSeparator();
        entries.Add(new ContextMenuEntry(settingsText, openSettings) { Node = ControlMap.TrayMenu.OpenSettings });
        entries.AddSeparator();
        entries.Add(new ContextMenuEntry(exitText, exit) { Node = ControlMap.TrayMenu.Exit });
        return entries.ToList();
    }

    private static ContextMenuPlacement ToCommonPlacement(ContextMenuPosition placement) =>
        placement == ContextMenuPosition.Modern
            ? ContextMenuPlacement.Modern
            : ContextMenuPlacement.Classic;
}
