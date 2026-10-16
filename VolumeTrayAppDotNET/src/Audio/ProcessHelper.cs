using System.Diagnostics;
using TrayAppDotNETCommon.Utils;

namespace VolumeTrayAppDotNET.Audio;

// Display names for audio sessions. Icon extraction lives in AppIconResolver, and the PID to image path
// lookup is the common ProcessImagePath, which also reaches UWP and other restricted processes.
internal static class ProcessHelper
{

    /// <summary>
    /// Best-effort display name for a session. Order:
    ///  1. Process FileVersionInfo.FileDescription (e.g. "Discord")
    ///  2. Process exe filename without extension (e.g. "Discord")
    ///  3. localized "Unknown"
    /// </summary>
    public static string GetDisplayNameForProcess(uint processId)
    {
        string? path = ProcessImagePath.TryGet((int)processId);
        if (string.IsNullOrEmpty(path)) return AudioLocalization.UnknownAppName;

        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription!;
        }
        catch
        {
            // FileVersionInfo can throw FileNotFound (UWP placeholder paths) or access denied.
            // Fall through to filename-only.
        }

        return Path.GetFileNameWithoutExtension(path);
    }
}
