using Avalonia;

namespace TaskManagerTrayAppDotNET.UI;

internal sealed class TaskManagerTrayMenuWindow(
    SettingsPalette palette,
    bool enableRoundedCorners,
    Action openTaskManager,
    Action exitApplication)
    : TrayMenuWindow(
        BuildEntries(openTaskManager, exitApplication),
        new TrayMenuWindowOptions
        {
            Palette = palette,
            Rounded = enableRoundedCorners,
            FontSize = 15
        })
{
    public void ShowAt(TrayAppDotNETShellTrayIcon trayIcon, PixelPoint cursorPoint) =>
        base.ShowAt(trayIcon, cursorPoint, TrayMenuWindowPlacement.Modern);

    private static List<TrayMenuEntry> BuildEntries(Action openTaskManager, Action exitApplication)
    {
        TrayMenuEntryBuilder entries = new();
        entries.Add("Open Task Manager", openTaskManager);
        entries.AddSeparator();
        entries.Add("Exit", exitApplication);
        return entries.ToList();
    }
}
