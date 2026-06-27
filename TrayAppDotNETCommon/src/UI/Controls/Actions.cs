using System.Diagnostics;

namespace TrayAppDotNETCommon.UI.Controls;

public static class TrayAppDotNETSettingsActions
{
    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
