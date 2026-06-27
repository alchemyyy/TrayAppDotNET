#pragma warning disable CA1822

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NetworkTrayAppDotNET.Models;

namespace NetworkTrayAppDotNET.UI;

public sealed class NetworkUninstallerWindow : Window
{
    private readonly string _installDir;
    private readonly InstallScope _scope;
    private readonly CheckBox _deleteSettings = new() { Content = "Delete app settings" };

    public Process? UninstallProcess { get; private set; }
    public bool ConfirmedUninstall { get; private set; }

    public NetworkUninstallerWindow()
        : this(string.Empty, InstallScope.LocalAppData)
    {
    }

    public NetworkUninstallerWindow(string installDir, InstallScope scope)
    {
        _installDir = installDir;
        _scope = scope;
        Title = Constants.ApplicationName + " Uninstaller";
        Width = 560;
        Height = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = AppTheme.LoadAppIcon();

        TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
        Button uninstall = new() { Content = "Uninstall", MinWidth = 96 };
        Button cancel = new() { Content = "Cancel", MinWidth = 96 };
        uninstall.Click += (_, _) =>
        {
            uninstall.IsEnabled = false;
            cancel.IsEnabled = false;
            ConfirmedUninstall = true;
            AppServices.Startup.RetargetShortcutIfPresent(exclude: _scope);
            UninstallProcess = AppServices.Installation.RunUninstall(_scope, _deleteSettings.IsChecked == true);
            if (UninstallProcess == null)
            {
                status.Text = "Uninstall cancelled or handed off to the running install copy.";
                Close();
                return;
            }

            Close();
        };
        cancel.Click += (_, _) => Close();

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(uninstall);
        buttons.Children.Add(cancel);

        Content = new Border
        {
            Padding = new Thickness(22),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = Constants.ApplicationName, FontSize = 22, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "Uninstall from:", Opacity = 0.72 },
                    new TextBlock { Text = _installDir, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = "Settings folder: " + AppSettings.GetDefaultDirectory(),
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.72
                    },
                    _deleteSettings,
                    status,
                    buttons,
                },
            },
        };
    }
}
