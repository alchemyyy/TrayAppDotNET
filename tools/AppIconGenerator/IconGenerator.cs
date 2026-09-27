namespace TrayAppDotNET.Tools.AppIconGenerator;

/// <summary>Generates the complete application icon set from the SVG sources under resources/app_icons.</summary>
internal static class IconGenerator
{
    private const string ResourcesDirectoryName = "resources";
    private const string AppIconsDirectoryName = "app_icons";
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    /// <summary>Generates and replaces the ICO file for each selected target.</summary>
    public static void Generate(string repositoryRoot, IReadOnlyList<IconTarget> targets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(targets);

        string sourceDirectory = GetSourceDirectory(repositoryRoot);
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"SVG source directory was not found: {sourceDirectory}");

        foreach (IconTarget target in targets)
            GenerateTarget(repositoryRoot, sourceDirectory, target);
    }

    /// <summary>Returns the directory holding the SVG sources for a repository root.</summary>
    public static string GetSourceDirectory(string repositoryRoot) =>
        Path.Combine(repositoryRoot, ResourcesDirectoryName, AppIconsDirectoryName);

    private static void GenerateTarget(string repositoryRoot, string sourceDirectory, IconTarget target)
    {
        string projectDirectory = Path.Combine(repositoryRoot, target.ProjectDirectoryName);
        if (!Directory.Exists(projectDirectory))
            throw new DirectoryNotFoundException(
                $"Project directory for {target.ShortName} was not found: {projectDirectory}");

        List<IconImage> images = new(IconSizes.Length);
        using IconComposition composition = IconComposition.Create(target, sourceDirectory);
        foreach (int iconSize in IconSizes)
            images.Add(new IconImage(iconSize, composition.RenderPNG(iconSize)));

        string outputPath = Path.Combine(projectDirectory, "app.ico");
        ICOWriter.WriteFile(outputPath, images);
        Console.WriteLine($"{target.ShortName,-7} {outputPath}");
    }
}
