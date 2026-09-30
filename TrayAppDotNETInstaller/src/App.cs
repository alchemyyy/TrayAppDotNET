using System.Windows;
using TrayAppDotNETInstaller.Services;
using TrayAppDotNETInstaller.UI;

namespace TrayAppDotNETInstaller;

/// <summary>
/// The WPF application. It merges the theme dictionary, matches it to the operating system appearance and
/// shows the one window the installer has.
/// </summary>
public sealed class App : Application
{
    // Compiled into this assembly as a Page, so the pack URI carries the assembly name from the project file
    private const string ThemeDictionaryUri =
        "pack://application:,,,/TrayAppDotNETInstaller;component/UI/Theme.xaml";

    private readonly EmbeddedPayloadCatalog _catalog;
    private readonly bool _isExample;
    private readonly bool _exampleFails;

    /// <summary>
    /// Takes the catalog the process already loaded rather than reading the archive a second time.
    /// <paramref name="isExample"/> puts the window in the mode that installs nothing.
    /// </summary>
    public App(EmbeddedPayloadCatalog catalog, bool isExample = false, bool exampleFails = false)
    {
        FrameworkCompatibility.ThrowIfNull(catalog, nameof(catalog));

        _catalog = catalog;
        _isExample = isExample;
        _exampleFails = exampleFails;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        ResourceDictionary theme = new() { Source = new Uri(ThemeDictionaryUri, UriKind.Absolute) };
        Resources.MergedDictionaries.Add(theme);
        SystemTheme.ApplyPalette(Resources);

        // The example window reports no Windhawk and no battery on purpose. Both states are otherwise
        // invisible on a machine that has them, and both drive interface a rework needs to see: the
        // Windhawk notice, and the battery application unselecting itself. It reports existing
        // installations for the same reason, so the installed notice, the row labels and the preset
        // installation type show.
        WindhawkDetection windhawk = _isExample
            ? new WindhawkDetection(IsInstalled: false, Evidence: null)
            : WindhawkDetector.Detect();
        bool hasSystemBattery = !_isExample && SystemProbes.HasSystemBattery();
        IReadOnlyList<DetectedInstallation> installations = _isExample
            ? ExampleMode.CreateDetectedInstallations(_catalog)
            : InstallationDetector.Detect(_catalog);

        InstallerWindow window = new(_catalog, windhawk, hasSystemBattery, installations, _isExample, _exampleFails);
        MainWindow = window;
        window.Show();
    }
}
