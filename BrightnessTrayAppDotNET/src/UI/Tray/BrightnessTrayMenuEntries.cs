using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace BrightnessTrayAppDotNET.UI.Tray;

internal sealed record BrightnessTrayMenuProfile(int Index, string Label, bool IsSelected);

/// <summary>Brightness's own tray menu entries: profile selectors and monitor power actions.</summary>
internal static class BrightnessTrayMenuEntries
{
    public static ContextMenuEntryBuilder Build(
        IReadOnlyList<BrightnessTrayMenuProfile> profiles,
        IReadOnlyList<MonitorInfo> monitors,
        AppSettings settings,
        Action<int> selectProfile,
        Action powerOffAllMonitors,
        Action<MonitorInfo> powerOffMonitor)
    {
        ContextMenuEntryBuilder entries = new();

        if (settings.ShowProfileSelectorsInMenu && profiles.Count > 0)
        {
            foreach ((int capturedIndex, string label, bool isSelected) in profiles)
            {
                entries.Add(new ContextMenuEntry(label, () => selectProfile(capturedIndex))
                {
                    TrailingGlyph = isSelected ? GlyphCatalog.CHECK_MARK.Text : null,
                    Node = ControlMap.TrayMenu.Profile
                });
            }

            entries.AddSeparator();
        }

        bool hasPowerTargets = monitors.Any(static m => m.SupportsPowerControl);
        if (settings.ShowAllDisplaysPowerButton && hasPowerTargets)
        {
            entries.Add(new ContextMenuEntry(L(nameof(AppStrings.Tray_PowerOffAllDisplays)), powerOffAllMonitors)
            {
                Node = ControlMap.TrayMenu.PowerOffAllDisplays
            });
        }

        if (settings.ShowMonitorPowerButtons && hasPowerTargets)
        {
            foreach (MonitorInfo monitor in monitors.Where(static m => m.SupportsPowerControl))
            {
                MonitorInfo capturedMonitor = monitor;
                string label = string.Format(L(nameof(AppStrings.Tray_PowerOffMonitor_Format)), monitor.Name);
                entries.Add(new ContextMenuEntry(label, () => powerOffMonitor(capturedMonitor))
                {
                    Node = ControlMap.TrayMenu.PowerOffDisplay
                });
            }
        }

        return entries;
    }

    private static string L(string key) => TrayLocalization.Instance[key];
}
