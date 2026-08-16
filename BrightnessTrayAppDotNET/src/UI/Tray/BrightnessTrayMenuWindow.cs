using Avalonia;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Tray;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace BrightnessTrayAppDotNET.UI.Tray;

internal sealed record BrightnessTrayMenuProfile(int Index, string Label, bool IsSelected);

internal sealed class BrightnessTrayMenuWindow(
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
    : TrayMenuWindow(BuildEntries(
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
            Palette = palette, Rounded = rounded, FontSize = fontSize, ShadowColor = shadowColor
        })
{
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
            foreach ((int capturedIndex, string label, bool isSelected) in profiles)
            {
                entries.Add(label, () => selectProfile(capturedIndex),
                    isSelected ? GlyphCatalog.CHECK_MARK.Text : null);
            }

            entries.AddSeparator();
        }

        bool hasPowerTargets = monitors.Any(static m => m.SupportsPowerControl);
        if (settings.ShowAllDisplaysPowerButton && hasPowerTargets)
            entries.Add(L(nameof(AppStrings.Tray_PowerOffAllDisplays)), powerOffAllMonitors);

        if (settings.ShowMonitorPowerButtons && hasPowerTargets)
        {
            foreach (MonitorInfo monitor in monitors.Where(static m => m.SupportsPowerControl))
            {
                MonitorInfo capturedMonitor = monitor;
                string label = string.Format(
                    L(nameof(AppStrings.Tray_PowerOffMonitor_Format)),
                    monitor.Name);
                entries.Add(label, () => powerOffMonitor(capturedMonitor));
            }
        }

        if (entries.Count > 0) entries.AddSeparator();

        entries.Add(L(nameof(AppStrings.Tray_Settings)), openSettings);
        entries.AddSeparator();
        entries.Add(L(nameof(AppStrings.Tray_Exit)), exit);

        return entries.ToList();
    }

    private static TrayMenuWindowPlacement ToCommonPlacement(ContextMenuPosition placement) =>
        placement == ContextMenuPosition.Modern
            ? TrayMenuWindowPlacement.Modern
            : TrayMenuWindowPlacement.Classic;

    private static string L(string key) => TrayLocalization.Instance[key];
}
