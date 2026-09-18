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

    /// <summary>Takes the catalog the process already loaded rather than reading the archive a second time.</summary>
    public App(EmbeddedPayloadCatalog catalog)
    {
        FrameworkCompatibility.ThrowIfNull(catalog, nameof(catalog));

        _catalog = catalog;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        ResourceDictionary theme = new() { Source = new Uri(ThemeDictionaryUri, UriKind.Absolute) };
        Resources.MergedDictionaries.Add(theme);
        SystemTheme.ApplyPalette(Resources);

        InstallerWindow window = new(_catalog, WindhawkDetector.Detect(), SystemProbes.HasSystemBattery());
        MainWindow = window;
        window.Show();
    }
}
