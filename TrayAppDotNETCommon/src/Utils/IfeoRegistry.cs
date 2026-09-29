using Microsoft.Win32;

namespace TrayAppDotNETCommon.Utils;

/// <summary>
/// Reads, writes, and removes the Image File Execution Options "Debugger" value that redirects a
/// Windows image (for example "taskmgr.exe") to a replacement executable. When a Debugger value
/// exists, CreateProcess launches that executable in place of the original, passing the original
/// image path and arguments along. This is the documented "Replace Task Manager" mechanism used by
/// Sysinternals Process Explorer and System Informer.
///
/// The value lives under the machine-wide 64-bit HKLM hive, so writes and removals require
/// administrator rights. Every method is best-effort: it logs and returns false on failure rather
/// than throwing, matching <see cref="WindowsUninstallRegistry"/>.
/// </summary>
public static class IfeoRegistry
{
    private const string ImageFileExecutionOptionsKeyPath =
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string DebuggerValueName = "Debugger";

    /// <summary>Returns the raw Debugger value for the image, or null when none is set.</summary>
    public static string? ReadDebugger(string imageName, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(imageName)) return null;

        try
        {
            using RegistryKey machine = OpenMachine64();
            using RegistryKey? imageKey = machine.OpenSubKey(ImageSubKeyPath(imageName), writable: false);
            return imageKey?.GetValue(DebuggerValueName) as string;
        }
        catch (Exception exception)
        {
            log?.Invoke($"IfeoRegistry.ReadDebugger({imageName}): {exception.Message}");
            return null;
        }
    }

    /// <summary>Extracts the executable path a Debugger value points at, unquoting and dropping any trailing arguments.</summary>
    public static string? GetDebuggerExecutable(string imageName, Action<string>? log = null) =>
        ExtractExecutable(ReadDebugger(imageName, log));

    /// <summary>Sets the Debugger value for the image to the quoted executable path. Requires elevation.</summary>
    public static bool WriteDebugger(string imageName, string debuggerExecutable, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(imageName) || string.IsNullOrWhiteSpace(debuggerExecutable))
        {
            log?.Invoke("IfeoRegistry.WriteDebugger: the image name and debugger path must both be provided.");
            return false;
        }

        try
        {
            using RegistryKey machine = OpenMachine64();
            using RegistryKey imageKey = machine.CreateSubKey(ImageSubKeyPath(imageName), writable: true);
            // Quote the path so a Program Files install with spaces still resolves correctly
            imageKey.SetValue(DebuggerValueName, $"\"{debuggerExecutable}\"", RegistryValueKind.String);
            return true;
        }
        catch (Exception exception)
        {
            log?.Invoke($"IfeoRegistry.WriteDebugger({imageName}): {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes the image's Debugger value. Requires elevation. When the image subkey is left with no
    /// remaining values or subkeys, the now-empty subkey is removed as well; other IFEO settings
    /// (for example GlobalFlag) are preserved. Returns true when nothing was present to remove.
    /// </summary>
    public static bool RemoveDebugger(string imageName, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(imageName)) return true;

        try
        {
            using RegistryKey machine = OpenMachine64();
            string imageSubKeyPath = ImageSubKeyPath(imageName);
            using (RegistryKey? imageKey = machine.OpenSubKey(imageSubKeyPath, writable: true))
            {
                if (imageKey == null) return true;
                if (imageKey.GetValue(DebuggerValueName) != null)
                    imageKey.DeleteValue(DebuggerValueName, throwOnMissingValue: false);
                if (imageKey.ValueCount != 0 || imageKey.SubKeyCount != 0) return true;
            }

            // The subkey held only our Debugger value, so leave nothing behind
            machine.DeleteSubKey(imageSubKeyPath, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception exception)
        {
            log?.Invoke($"IfeoRegistry.RemoveDebugger({imageName}): {exception.Message}");
            return false;
        }
    }

    /// <summary>Parses the executable path out of a Debugger value, honoring optional surrounding quotes.</summary>
    internal static string? ExtractExecutable(string? debuggerValue)
    {
        if (string.IsNullOrWhiteSpace(debuggerValue)) return null;

        string trimmed = debuggerValue.Trim();
        if (trimmed.StartsWith('"'))
        {
            int closingQuote = trimmed.IndexOf('"', startIndex: 1);
            return closingQuote > 1 ? trimmed[1..closingQuote] : null;
        }

        int firstSpace = trimmed.IndexOf(' ');
        return firstSpace < 0 ? trimmed : trimmed[..firstSpace];
    }

    private static string ImageSubKeyPath(string imageName) =>
        ImageFileExecutionOptionsKeyPath + "\\" + imageName;

    private static RegistryKey OpenMachine64() =>
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
}
