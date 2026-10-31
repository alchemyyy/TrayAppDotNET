using System.Globalization;

namespace TrayAppDotNETCommon.TestSupport;

/// <summary>
/// Keeps a test process away from the user's files. App code saves settings, themes, logs and data stores under
/// <c>TrayAppDotNETProgram.AppLocalAppDataDirectory</c>, which otherwise is the installed app's folder under
/// %LOCALAPPDATA%. Each test assembly calls <see cref="Redirect"/> from a one-line <c>[ModuleInitializer]</c>.
/// </summary>
public static class TestSettingsDirectory
{
    /// <summary>
    /// Points the settings folder at a per-process folder under %TEMP%, as <c>--settings-dir</c> would, starts
    /// <c>TADNLog</c> there (as the app host does with its settings folder), and returns the folder.
    /// </summary>
    public static string Redirect(string testAssemblyName)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            testAssemblyName,
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        TrayAppDotNETProgram.UseSettingsDirectory(directory);
        TADNLog.Initialize(directory);
        return directory;
    }

    /// <summary>
    /// Points the settings folder at a folder that does not exist yet, for a test about what creates it, and restores
    /// the previous folder on dispose.
    /// </summary>
    public static FreshSettingsDirectory UseFresh() => new();

    public sealed class FreshSettingsDirectory : IDisposable
    {
        private readonly string? _previous = TrayAppDotNETProgram.SettingsDirectoryOverride;
        private readonly TempDirectory _temp = new();

        internal FreshSettingsDirectory()
        {
            Path = _temp.Combine("settings");
            TrayAppDotNETProgram.UseSettingsDirectory(Path);
        }

        /// <summary>The settings folder in effect until dispose; not created.</summary>
        public string Path { get; }

        public void Dispose()
        {
            if (_previous != null) TrayAppDotNETProgram.UseSettingsDirectory(_previous);
            _temp.Dispose();
        }
    }
}
