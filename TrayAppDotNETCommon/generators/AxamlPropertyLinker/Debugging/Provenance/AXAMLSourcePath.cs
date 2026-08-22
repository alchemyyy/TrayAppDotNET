namespace TrayAppDotNETCommon.AxamlPropertyLinker;

internal static class AXAMLSourcePath
{
    public static string Normalize(string sourcePath, string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return string.Empty;

        string normalizedSourcePath = sourcePath.Replace('\\', '/');
        if (!Path.IsPathFullyQualified(sourcePath) || string.IsNullOrWhiteSpace(projectDirectory))
            return normalizedSourcePath.TrimStart('/');

        string fullProjectDirectory = Path.GetFullPath(projectDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DirectoryInfo projectSourceDirectory = new(fullProjectDirectory);
        DirectoryInfo? projectDirectoryInfo = string.Equals(
            projectSourceDirectory.Name,
            "src",
            StringComparison.OrdinalIgnoreCase)
            ? projectSourceDirectory.Parent
            : projectSourceDirectory;
        DirectoryInfo? repositoryDirectory = projectDirectoryInfo?.Parent;

        if (repositoryDirectory != null)
        {
            string repositoryRelativePath = Path.GetRelativePath(repositoryDirectory.FullName, sourcePath);
            if (!IsOutsideDirectory(repositoryRelativePath))
                return repositoryRelativePath.Replace('\\', '/');
        }

        string projectRelativePath = Path.GetRelativePath(fullProjectDirectory, sourcePath);
        if (!IsOutsideDirectory(projectRelativePath))
            return projectRelativePath.Replace('\\', '/');

        return normalizedSourcePath;
    }

    private static bool IsOutsideDirectory(string relativePath) =>
        relativePath.Equals("..", StringComparison.Ordinal) ||
        relativePath.StartsWith("../", StringComparison.Ordinal) ||
        relativePath.StartsWith("..\\", StringComparison.Ordinal);
}
