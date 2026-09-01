namespace TrayAppDotNET.Tools.AppIconGenerator;

internal static class Program
{
    private const string RepositoryMarkerFileName = "TrayAppDotNET.slnx";

    public static int Main(string[] arguments)
    {
        try
        {
            GeneratorOptions options = ParseArguments(arguments);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            string repositoryRoot = ResolveRepositoryRoot(options.RepositoryRoot);
            IReadOnlyList<IconTarget> allTargets = IconTargetCatalog.Create();
            IReadOnlyList<IconTarget> selectedTargets = SelectTargets(allTargets, options.TargetNames);
            IconGenerator.Generate(repositoryRoot, selectedTargets);
            Console.WriteLine($"Generated {selectedTargets.Count} application icon(s).");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"AppIconGenerator: {exception.Message}");
            return 1;
        }
    }

    private static GeneratorOptions ParseArguments(IReadOnlyList<string> arguments)
    {
        GeneratorOptions options = new();
        for (int argumentIndex = 0; argumentIndex < arguments.Count; argumentIndex++)
        {
            string argument = arguments[argumentIndex];
            switch (argument)
            {
                case "--help" or "-h" or "/?":
                    options.ShowHelp = true;
                    break;

                case "--root":
                    options.RepositoryRoot = ReadOptionValue(arguments, ref argumentIndex, argument);
                    break;

                case "--target":
                    options.TargetNames.Add(ReadOptionValue(arguments, ref argumentIndex, argument));
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{argument}'. Use --help for usage.");
            }
        }

        return options;
    }

    private static string ReadOptionValue(
        IReadOnlyList<string> arguments,
        ref int argumentIndex,
        string optionName)
    {
        int valueIndex = argumentIndex + 1;
        if (valueIndex >= arguments.Count || arguments[valueIndex].StartsWith('-'))
            throw new ArgumentException($"{optionName} requires a value.");

        argumentIndex = valueIndex;
        return arguments[valueIndex];
    }

    private static string ResolveRepositoryRoot(string? requestedRoot)
    {
        if (!string.IsNullOrWhiteSpace(requestedRoot))
        {
            string fullRequestedRoot = Path.GetFullPath(requestedRoot);
            ValidateRepositoryRoot(fullRequestedRoot);
            return fullRequestedRoot;
        }

        string? workingDirectoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (workingDirectoryRoot != null) return workingDirectoryRoot;

        string? executableDirectoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        return executableDirectoryRoot
               ?? throw new DirectoryNotFoundException(
                   $"Could not find {RepositoryMarkerFileName}. Pass the repository path with --root.");
    }

    private static string? FindRepositoryRoot(string startingPath)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(startingPath));
        while (directory != null)
        {
            string markerPath = Path.Combine(directory.FullName, RepositoryMarkerFileName);
            if (File.Exists(markerPath)) return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }

    private static void ValidateRepositoryRoot(string repositoryRoot)
    {
        string markerPath = Path.Combine(repositoryRoot, RepositoryMarkerFileName);
        if (!File.Exists(markerPath))
            throw new DirectoryNotFoundException(
                $"Repository marker was not found: {markerPath}");
    }

    private static IReadOnlyList<IconTarget> SelectTargets(
        IReadOnlyList<IconTarget> allTargets,
        IReadOnlyList<string> targetNames)
    {
        if (targetNames.Count == 0) return allTargets;

        List<IconTarget> selectedTargets = new(targetNames.Count);
        foreach (string targetName in targetNames)
        {
            IconTarget? matchedTarget = null;
            foreach (IconTarget candidate in allTargets)
            {
                if (!candidate.Matches(targetName)) continue;
                matchedTarget = candidate;
                break;
            }

            if (matchedTarget == null)
                throw new ArgumentException($"Unknown icon target '{targetName}'.");
            if (!selectedTargets.Contains(matchedTarget)) selectedTargets.Add(matchedTarget);
        }

        return selectedTargets;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Generate TrayAppDotNET app.ico files from embedded SVG sources.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tools/AppIconGenerator/AppIconGenerator.csproj -p:Platform=x64 -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --root <path>       TrayAppDotNET repository root");
        Console.WriteLine("  --target <name>     Generate one target; may be repeated");
        Console.WriteLine("  --help              Show this help");
        Console.WriteLine();
        Console.WriteLine("Targets: BATADN, BTADN, FCTADN, NTADN, TMTADN, VTADN");
    }

    private sealed class GeneratorOptions
    {
        public readonly List<string> TargetNames = [];
        public string? RepositoryRoot;
        public bool ShowHelp;
    }
}
