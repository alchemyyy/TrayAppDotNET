using Avalonia;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace TaskManagerTrayAppDotNET.UI;

internal sealed class TaskManagerTrayMenuWindow : ContextMenuWindow
{
    public TaskManagerTrayMenuWindow(
        AppSettings settings,
        SettingsPalette palette,
        Action openTaskManager,
        Action exitApplication)
        : base(
            BuildEntries(openTaskManager, exitApplication),
            CreateOptions(settings, palette))
    {
        this.MapTo(ControlMap.TrayMenu.ID);
#if DEBUG
        TaskManagerWindowResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
        Closed += OnWindowClosed;
#endif
    }

    private static ContextMenuWindowOptions CreateOptions(
        AppSettings settings,
        SettingsPalette palette) =>
        new()
        {
            Palette = palette,
            Rounded = settings.EnableRoundedCorners,
            FontSize = TaskManagerWindowResources.Current.AxamlTaskManagerTrayMenu.FontSize,
            ContextMenuSettings = settings
        };

    public void ShowAt(TrayAppDotNETShellTrayIcon trayIcon, PixelPoint cursorPoint) =>
        base.ShowAt(trayIcon, cursorPoint, ContextMenuPlacement.Modern);

#if DEBUG
    private void OnAXAMLResourcesReloaded() => CloseForWarmEviction();

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        TaskManagerWindowResources.ResourcesReloaded -= OnAXAMLResourcesReloaded;
        Closed -= OnWindowClosed;
    }
#endif

    private static List<ContextMenuEntry> BuildEntries(Action openTaskManager, Action exitApplication)
    {
        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry(Text: "Open Task Manager", openTaskManager)
        {
            Node = ControlMap.TrayMenu.OpenTaskManager
        });
        entries.AddSeparator();
        entries.Add(new ContextMenuEntry(Text: "Exit", exitApplication) { Node = ControlMap.TrayMenu.Exit });
        return entries.ToList();
    }
}
