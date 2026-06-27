using Avalonia;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Tray;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace BrightnessTrayAppDotNET.UI.Tray;

internal sealed record BrightnessTrayMenuProfile(int Index, string Label, bool IsSelected);

internal sealed class BrightnessTrayMenuWindow : TrayMenuWindow
{
    private const string CheckGlyph = "\uE73E";

    public BrightnessTrayMenuWindow(
        IReadOnlyList<BrightnessTrayMenuProfile> profiles,
        IReadOnlyList<MonitorInfo> monitors,
        AppSettings settings,
        SettingsPalette palette,
        Color shadowColor,
        bool rounded,
        int fontSize,
        Action<int> selectProfile,
        Action powerOffAllMonitors,
        Action<MonitorInfo> powerOffMonitor,
        Action openSettings,
        Action exit)
        : base(
            BuildEntries(
                profiles,
                monitors,
                settings,
                selectProfile,
                powerOffAllMonitors,
                powerOffMonitor,
                openSettings,
                exit),
            new TrayMenuWindowOptions
            {
                Palette = palette, Rounded = rounded, FontSize = fontSize, ShadowColor = shadowColor,
            })
    {
    }

    public void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        ContextMenuPosition placement) =>
        base.ShowAt(trayIcon, cursorPoint, ToCommonPlacement(placement));

    private static List<TrayMenuEntry> BuildEntries(
        IReadOnlyList<BrightnessTrayMenuProfile> profiles,
        IReadOnlyList<MonitorInfo> monitors,
        AppSettings settings,
        Action<int> selectProfile,
        Action powerOffAllMonitors,
        Action<MonitorInfo> powerOffMonitor,
        Action openSettings,
        Action exit)
    {
        TrayMenuEntryBuilder entries = new();

        if (settings.ShowProfileSelectorsInMenu && profiles.Count > 0)
        {
            foreach (BrightnessTrayMenuProfile profile in profiles)
            {
                int capturedIndex = profile.Index;
                entries.Add(profile.Label, () => selectProfile(capturedIndex), profile.IsSelected ? CheckGlyph : null);
            }

            entries.AddSeparator();
        }

        if (settings.ShowAllDisplaysPowerButton)
            entries.Add(L("Tray_PowerOffAllDisplays", "Power off all displays"), powerOffAllMonitors);

        if (settings.ShowMonitorPowerButtons)
        {
            foreach (MonitorInfo monitor in monitors)
            {
                MonitorInfo capturedMonitor = monitor;
                string label = string.Format(
                    L("Tray_PowerOffMonitor_Format", "Power off {0}"),
                    monitor.Name);
                entries.Add(label, () => powerOffMonitor(capturedMonitor));
            }
        }

        if (entries.Count > 0) entries.AddSeparator();

        entries.Add(L("Tray_Settings", "Settings"), openSettings);
        entries.AddSeparator();
        entries.Add(L("Tray_Exit", "Exit"), exit);

        return entries.ToList();
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
