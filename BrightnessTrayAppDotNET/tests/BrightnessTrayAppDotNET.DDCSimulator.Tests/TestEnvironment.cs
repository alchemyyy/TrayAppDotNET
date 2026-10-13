using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using TrayAppDotNETCommon;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests;

/// <summary>
/// Keeps every simulation away from the installed app's files. Settings, displays.json and the log all resolve
/// through <c>TrayAppDotNETProgram.SettingsDirectoryOverride</c> or <c>TADNLog</c>, so both point at a throwaway
/// directory before any test runs.
/// </summary>
internal static class TestEnvironment
{
    public static string RootDirectory { get; private set; } = string.Empty;

    [ModuleInitializer]
    internal static void Redirect()
    {
        RootDirectory = Path.Combine(
            Path.GetTempPath(),
            "BrightnessTrayAppDotNET.DDCSimulator.Tests",
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(RootDirectory);

        // The override is normally set only by the --settings-dir launch argument, so its setter is private
        PropertyInfo overrideProperty = typeof(TrayAppDotNETProgram).GetProperty(
                                            nameof(TrayAppDotNETProgram.SettingsDirectoryOverride),
                                            BindingFlags.Public | BindingFlags.Static)
                                        ?? throw new InvalidOperationException(
                                            "TrayAppDotNETProgram.SettingsDirectoryOverride is missing.");
        overrideProperty.SetValue(obj: null, RootDirectory);

        TADNLog.Initialize(Path.Combine(RootDirectory, "logs"));
    }

    /// <summary>Fresh per-world directory for displays.json and other persisted state.</summary>
    public static string CreateWorldDirectory(string name)
    {
        string directory = Path.Combine(RootDirectory, name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
