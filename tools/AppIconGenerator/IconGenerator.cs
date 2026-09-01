namespace TrayAppDotNET.Tools.AppIconGenerator;

/// <summary>Generates the complete application icon set from embedded SVG sources.</summary>
internal static class IconGenerator
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    /// <summary>Generates and replaces the ICO file for each selected target.</summary>
    public static void Generate(string repositoryRoot, IReadOnlyList<IconTarget> targets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(targets);

        foreach (IconTarget target in targets)
            GenerateTarget(repositoryRoot, target);
    }

    private static void GenerateTarget(string repositoryRoot, IconTarget target)
    {
        string projectDirectory = Path.Combine(repositoryRoot, target.ProjectDirectoryName);
        if (!Directory.Exists(projectDirectory))
            throw new DirectoryNotFoundException(
                $"Project directory for {target.ShortName} was not found: {projectDirectory}");

        List<IconImage> images = new(IconSizes.Length);
        using IconComposition composition = IconComposition.Create(target);
        foreach (int iconSize in IconSizes)
            images.Add(new IconImage(iconSize, composition.RenderPNG(iconSize)));

        string outputPath = Path.Combine(projectDirectory, "app.ico");
        ICOWriter.WriteFile(outputPath, images);
        Console.WriteLine($"{target.ShortName,-7} {outputPath}");
    }
}
