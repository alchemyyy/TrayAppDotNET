using Avalonia;

namespace TaskManagerTrayAppDotNET.UI;

internal sealed class TaskManagerTrayMenuWindow(
    AppSettings settings,
    SettingsPalette palette,
    Action openTaskManager,
    Action exitApplication)
    : ContextMenuWindow(
        BuildEntries(openTaskManager, exitApplication),
        new ContextMenuWindowOptions
        {
            Palette = palette,
            Rounded = settings.EnableRoundedCorners,
            FontSize = 15,
            ContextMenuSettings = settings
        })
{
    public void ShowAt(TrayAppDotNETShellTrayIcon trayIcon, PixelPoint cursorPoint) =>
        base.ShowAt(trayIcon, cursorPoint, ContextMenuPlacement.Modern);

    private static List<ContextMenuEntry> BuildEntries(Action openTaskManager, Action exitApplication)
    {
        ContextMenuEntryBuilder entries = new();
        entries.Add("Open Task Manager", openTaskManager);
        entries.AddSeparator();
        entries.Add("Exit", exitApplication);
        return entries.ToList();
    }
}
