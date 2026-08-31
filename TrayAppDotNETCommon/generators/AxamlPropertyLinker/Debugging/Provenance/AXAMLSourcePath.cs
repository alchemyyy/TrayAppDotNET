namespace TrayAppDotNETCommon.AxamlPropertyLinker;

internal static class AXAMLSourcePath
{
    public static string Normalize(string sourcePath, string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return string.Empty;

        string normalizedSourcePath = sourcePath.Replace(oldChar: '\\', newChar: '/');
        if (!Path.IsPathFullyQualified(sourcePath) || string.IsNullOrWhiteSpace(projectDirectory))
            return normalizedSourcePath.TrimStart('/');

        string fullProjectDirectory = Path.GetFullPath(projectDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DirectoryInfo projectSourceDirectory = new(fullProjectDirectory);
        DirectoryInfo? projectDirectoryInfo = string.Equals(
            projectSourceDirectory.Name,
            b: "src",
            StringComparison.OrdinalIgnoreCase)
            ? projectSourceDirectory.Parent
            : projectSourceDirectory;
        DirectoryInfo? repositoryDirectory = projectDirectoryInfo?.Parent;

        if (repositoryDirectory != null)
        {
            string repositoryRelativePath = Path.GetRelativePath(repositoryDirectory.FullName, sourcePath);
            if (!IsOutsideDirectory(repositoryRelativePath))
                return repositoryRelativePath.Replace(oldChar: '\\', newChar: '/');
        }

        string projectRelativePath = Path.GetRelativePath(fullProjectDirectory, sourcePath);
        if (!IsOutsideDirectory(projectRelativePath))
            return projectRelativePath.Replace(oldChar: '\\', newChar: '/');

        return normalizedSourcePath;
    }

    private static bool IsOutsideDirectory(string relativePath) =>
        relativePath.Equals(value: "..", StringComparison.Ordinal) ||
        relativePath.StartsWith(value: "../", StringComparison.Ordinal) ||
        relativePath.StartsWith(value: "..\\", StringComparison.Ordinal);
}
