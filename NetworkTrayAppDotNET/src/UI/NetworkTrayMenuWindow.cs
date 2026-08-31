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
            Palette = palette,
            Rounded = rounded,
            FontSize = fontSize,
            ContextMenuSettings = settings
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
        entries.Add(networkSettingsText, openNetworkSettings);
        entries.Add(adapterSettingsText, openAdapterSettings);
        entries.AddSeparator();
        entries.Add(settingsText, openSettings);
        entries.AddSeparator();
        entries.Add(exitText, exit);
        return entries.ToList();
    }

    private static ContextMenuPlacement ToCommonPlacement(ContextMenuPosition placement) =>
        placement == ContextMenuPosition.Modern
            ? ContextMenuPlacement.Modern
            : ContextMenuPlacement.Classic;
}
