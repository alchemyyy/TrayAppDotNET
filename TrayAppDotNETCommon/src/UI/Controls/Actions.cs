using TrayAppDotNETCommon.Services;

namespace TrayAppDotNETCommon.UI.Controls;

public static class TrayAppDotNETSettingsActions
{
    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        if (!ExplorerProcessLauncher.TryShellExecute(
                folder,
                arguments: null,
                workingDirectory: folder,
                verb: null,
                out _,
                out string errorMessage))
            throw new InvalidOperationException(errorMessage);
    }
}
