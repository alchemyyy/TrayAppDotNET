#if DEBUG
using TrayAppDotNETCommon.Contracts;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Normalizes compiler-provided paths without retaining a developer-specific repository root.</summary>
internal static class DebugSourcePath
{
    private static readonly string[] ProjectDirectoryNames = [.. TrayAppDotNETAppCatalog.ApplicationNames, "TrayAppDotNETCommon"];

    public static string Normalize(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath)) return "<unknown>";

        string normalizedPath = sourceFilePath.Replace(oldChar: '\\', newChar: '/');
        foreach (string projectDirectoryName in ProjectDirectoryNames)
        {
            string relativePrefix = projectDirectoryName + "/";
            if (normalizedPath.StartsWith(relativePrefix, StringComparison.OrdinalIgnoreCase))
                return normalizedPath;

            string marker = "/" + projectDirectoryName + "/";
            int projectStart = normalizedPath.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (projectStart >= 0)
                return normalizedPath[(projectStart + 1)..];
        }

        return Path.GetFileName(normalizedPath);
    }
}
#endif
