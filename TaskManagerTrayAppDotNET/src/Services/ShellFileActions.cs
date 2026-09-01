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

        return ExplorerProcessLauncher.TryShellExecute(
            normalizedPath,
            arguments: null,
            workingDirectory: Path.GetDirectoryName(normalizedPath),
            verb: "properties",
            out _,
            out errorMessage);
    }
}
