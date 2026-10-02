using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using TrayAppDotNETCommon;

namespace FanControlTrayAppDotNET.Tests;

/// <summary>
/// Points the app's settings folder at a throwaway directory before any test runs. Flyout and settings code saves
/// through <c>AppSettings.Save()</c>, which otherwise writes the installed app's settings.xml under %LOCALAPPDATA%.
/// </summary>
internal static class TestSettingsDirectory
{
    [ModuleInitializer]
    internal static void Redirect()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "FanControlTrayAppDotNET.Tests",
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);

        // The override is normally set only by the --settings-dir launch argument, so its setter is private
        PropertyInfo overrideProperty = typeof(TrayAppDotNETProgram).GetProperty(
                                            nameof(TrayAppDotNETProgram.SettingsDirectoryOverride),
                                            BindingFlags.Public | BindingFlags.Static)
                                        ?? throw new InvalidOperationException(
                                            "TrayAppDotNETProgram.SettingsDirectoryOverride is missing.");
        overrideProperty.SetValue(obj: null, directory);
    }
}
