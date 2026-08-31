using System.ComponentModel;
using System.Diagnostics;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Invokes Windows shell actions for file-system targets.</summary>
internal static class ShellFileActions
{
    /// <summary>Shows the shell Properties sheet for an existing file or directory.</summary>
    public static bool TryShowProperties(string? path, out string errorMessage)
    {
        if (!OperatingSystem.IsWindows())
        {
            errorMessage = "File properties are only available on Windows.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "The selected item does not have a resolved file-system target.";
            return false;
        }

        string normalizedPath = Path.GetFullPath(path);
        if (!File.Exists(normalizedPath) && !Directory.Exists(normalizedPath))
        {
            errorMessage = $"'{normalizedPath}' no longer exists.";
            return false;
        }

        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = normalizedPath, Verb = "properties", UseShellExecute = true
            };
            using Process? process = Process.Start(startInfo);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or Win32Exception
                                              or NotSupportedException)
        {
            errorMessage = exception.Message;
            return false;
        }
    }
}
