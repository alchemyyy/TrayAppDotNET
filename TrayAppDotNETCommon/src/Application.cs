using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace TrayAppDotNETCommon;

public sealed class TrayAppDotNETApplication : Application
{
    internal static TrayAppDotNETHostOptions? Options { get; set; }

    private TrayIcon? _trayIcon;
    private Window? _settingsWindow;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Default;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        TrayAppDotNETHostOptions options = Options
                                           ?? throw new InvalidOperationException(
                                               "Tray app host options were not configured.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += (_, _) => options.ShutdownServices?.Invoke();
        }

        try { options.InitializeServices?.Invoke(); }
        catch (Exception ex) { options.Log?.Invoke("TrayAppDotNETApplication.InitializeServices: " + ex); }

        if (options.IsUninstallerMode)
            ShowUninstallerWindow(options);
        else
            CreateTrayIcon(options);

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon(TrayAppDotNETHostOptions options)
    {
        NativeMenu menu = [];

        NativeMenuItem settingsItem = new("Settings");
        settingsItem.Click += (_, _) => ShowSettingsWindow(options);
        menu.Items.Add(settingsItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        NativeMenuItem exitItem = new("Exit");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = TryLoadAppIcon(),
            ToolTipText = string.IsNullOrWhiteSpace(options.ToolTipText)
                ? options.ApplicationName
                : options.ToolTipText,
            Menu = menu,
            IsVisible = true,
        };
        _trayIcon.Clicked += (_, _) => ShowSettingsWindow(options);
    }

    private static WindowIcon? TryLoadAppIcon()
    {
        Assembly? entryAssembly = Assembly.GetEntryAssembly();
        string? assemblyName = entryAssembly?.GetName().Name;
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            Uri resourceUri = new($"avares://{assemblyName}/Assets/app.ico");
            if (AssetLoader.Exists(resourceUri))
            {
                using Stream stream = AssetLoader.Open(resourceUri);
                return new WindowIcon(stream);
            }
        }

        string filePath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(filePath)) return new WindowIcon(filePath);
        return null;
    }

    private void ShowSettingsWindow(TrayAppDotNETHostOptions options)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new Window
            {
                Title = options.ApplicationName + " Settings",
                Width = 560,
                Height = 360,
                MinWidth = 420,
                MinHeight = 260,
                Icon = TryLoadAppIcon(),
                Content = BuildSettingsContent(options),
            };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private static Border BuildSettingsContent(TrayAppDotNETHostOptions options)
    {
        TextBlock title = new()
        {
            Text = options.ApplicationName,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 16),
        };

        TextBlock status = new()
        {
            Text = "Avalonia tray shell", Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 18),
        };

        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (options.OpenSettingsFolder != null)
        {
            Button folderButton = new() { Content = "Settings Folder" };
            folderButton.Click += (_, _) => options.OpenSettingsFolder();
            actions.Children.Add(folderButton);
        }

        Button closeButton = new() { Content = "Close" };
        closeButton.Click += (sender, _) =>
        {
            if (TopLevel.GetTopLevel((Control)sender!) is Window window) window.Close();
        };
        actions.Children.Add(closeButton);

        return new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel { Spacing = 0, Children = { title, status, actions, }, },
        };
    }

    private void ShowUninstallerWindow(TrayAppDotNETHostOptions options)
    {
        Window window = new()
        {
            Title = options.ApplicationName + " Uninstaller",
            Width = 520,
            Height = 300,
            MinWidth = 420,
            MinHeight = 260,
            Icon = TryLoadAppIcon(),
            Content = BuildUninstallerContent(options),
        };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = window;
        window.Show();
    }

    private Border BuildUninstallerContent(TrayAppDotNETHostOptions options)
    {
        CheckBox deleteSettings = new() { Content = "Delete settings", Margin = new Thickness(0, 12, 0, 0) };
        TextBlock status = new() { Foreground = Brushes.Gray };

        Button uninstallButton = new() { Content = "Uninstall" };
        uninstallButton.Click += async (_, _) =>
        {
            uninstallButton.IsEnabled = false;
            status.Text = "Uninstalling...";
            try
            {
                if (options.RunUninstallAsync != null)
                    await options.RunUninstallAsync(deleteSettings.IsChecked == true);
                Shutdown();
            }
            catch (Exception ex)
            {
                options.Log?.Invoke("Uninstall failed: " + ex);
                status.Text = "Uninstall failed.";
                uninstallButton.IsEnabled = true;
            }
        };

        Button cancelButton = new() { Content = "Cancel" };
        cancelButton.Click += (_, _) => Shutdown();

        return new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = options.ApplicationName, FontSize = 22, FontWeight = FontWeight.SemiBold, },
                    new TextBlock
                    {
                        Text = options.UninstallerInstallDir ?? string.Empty,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brushes.Gray,
                    },
                    new TextBlock { Text = options.UninstallerScopeText, Foreground = Brushes.Gray, },
                    deleteSettings,
                    status,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { uninstallButton, cancelButton },
                    },
                },
            },
        };
    }

    private void Shutdown()
    {
        try
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            Options?.ShutdownServices?.Invoke();
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            });
        }
    }
}
