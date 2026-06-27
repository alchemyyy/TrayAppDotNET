using Avalonia;
using NetworkTrayAppDotNET.Models;

namespace NetworkTrayAppDotNET.UI;

public sealed class NetworkTrayMenuWindow : TrayMenuWindow
{
    public NetworkTrayMenuWindow(
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
        : base(
            BuildEntries(
                networkSettingsText,
                adapterSettingsText,
                settingsText,
                exitText,
                openNetworkSettings,
                openAdapterSettings,
                openSettings,
                exit),
            new TrayMenuWindowOptions { Palette = palette, Rounded = rounded, FontSize = fontSize, })
    {
    }

    public void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        ContextMenuPosition placement) =>
        base.ShowAt(trayIcon, cursorPoint, ToCommonPlacement(placement));

    private static List<TrayMenuEntry> BuildEntries(
        string networkSettingsText,
        string adapterSettingsText,
        string settingsText,
        string exitText,
        Action openNetworkSettings,
        Action openAdapterSettings,
        Action openSettings,
        Action exit)
    {
        TrayMenuEntryBuilder entries = new();
        entries.Add(networkSettingsText, openNetworkSettings);
        entries.Add(adapterSettingsText, openAdapterSettings);
        entries.AddSeparator();
        entries.Add(settingsText, openSettings);
        entries.AddSeparator();
        entries.Add(exitText, exit);
        return entries.ToList();
    }

    private static TrayMenuWindowPlacement ToCommonPlacement(ContextMenuPosition placement) =>
        placement == ContextMenuPosition.Modern
            ? TrayMenuWindowPlacement.Modern
            : TrayMenuWindowPlacement.Classic;
}
