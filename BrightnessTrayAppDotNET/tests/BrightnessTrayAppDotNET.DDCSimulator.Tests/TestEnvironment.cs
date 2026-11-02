using System.Runtime.CompilerServices;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests;

/// <summary>
/// Keeps every simulation away from the installed app's files. Settings, displays.json and the log all resolve
/// through the settings folder, which points at a throwaway directory (and starts the log there) before any test runs.
/// </summary>
internal static class TestEnvironment
{
    public static string RootDirectory { get; private set; } = string.Empty;

    [ModuleInitializer]
    internal static void Redirect() =>
        RootDirectory = TestSettingsDirectory.Redirect("BrightnessTrayAppDotNET.DDCSimulator.Tests");

    /// <summary>Fresh per-world directory for displays.json and other persisted state.</summary>
    public static string CreateWorldDirectory(string name)
    {
        string directory = Path.Combine(RootDirectory, name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
