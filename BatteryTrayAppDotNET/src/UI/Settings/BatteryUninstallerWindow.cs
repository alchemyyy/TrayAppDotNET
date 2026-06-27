using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;
using BatteryInstallScope = TrayAppDotNETCommon.InstallScope;

namespace BatteryTrayAppDotNET.UI.Settings;

public sealed class BatteryUninstallerWindow : Window
{
    private readonly string _installDir;
    private readonly BatteryInstallScope _scope;
    private readonly RadioButton _keepSettings;
    private readonly RadioButton _deleteSettings;

    public Process? UninstallProcess { get; private set; }
    public bool ConfirmedUninstall { get; private set; }

    public BatteryUninstallerWindow()
        : this(string.Empty, BatteryInstallScope.LocalAppData)
    {
    }

    public BatteryUninstallerWindow(string installDir, BatteryInstallScope scope)
    {
        _installDir = installDir;
        _scope = scope;
        Title = L("Uninstaller_Title", "Uninstall BatteryTrayAppDotNET");
        Width = 520;
        Height = 360;
        MinWidth = 420;
        MinHeight = 320;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppTheme.LoadAppIcon();

        SettingsPalette p =
            BatterySettingsPalette.Create(AppServices.Theme, AppServices.Settings, ResolveEffectiveIsLight());
        bool rounded = AppServices.Settings?.EnableRoundedCorners == true;

        _keepSettings = new RadioButton
        {
            IsChecked = true,
            GroupName = "SettingsChoice",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        _deleteSettings = new RadioButton
        {
            GroupName = "SettingsChoice",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };

        SettingsButton uninstall = TrayAppDotNETSettingsUI.Button(L("Uninstaller_UninstallButton", "Uninstall"), p);
        uninstall.Padding = new Thickness(20, 8);
        SettingsButton cancel = TrayAppDotNETSettingsUI.Button(L("Uninstaller_Cancel", "Cancel"), p);
        cancel.Padding = new Thickness(20, 8);
        cancel.Margin = new Thickness(0, 0, 8, 0);

        uninstall.Click += (_, _) =>
        {
            bool deleteSettings = _deleteSettings.IsChecked == true;

            uninstall.IsEnabled = false;
            cancel.IsEnabled = false;
            uninstall.Text = L("Uninstaller_UninstallingButton", "Uninstalling...");

            AppServices.Startup.RetargetShortcutIfPresent(exclude: _scope);
            ConfirmedUninstall = true;
            UninstallProcess = AppServices.Installation.RunUninstall(_scope, deleteSettings);
            Close();
        };
        cancel.Click += (_, _) => Close();

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(uninstall);

        Grid titleBar = BuildTitleBar(p);
        Grid body = BuildBody(p, rounded, buttons);

        Grid chrome = new();
        chrome.RowDefinitions.Add(new RowDefinition(new GridLength(32)));
        chrome.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        chrome.Children.Add(titleBar);
        Grid.SetRow(body, 1);
        chrome.Children.Add(body);

        Border root = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(p.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(rounded ? 8 : 0),
            Child = chrome,
        };

        Content = root;
    }

    private Grid BuildTitleBar(SettingsPalette p)
    {
        Grid titleBar = new() { Background = Brushes.Transparent, };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        TextBlock title = TrayAppDotNETSettingsUI.Text(L("Uninstaller_Title", "Uninstall BatteryTrayAppDotNET"), p, 13);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(16, 0, 0, 0);
        titleBar.Children.Add(title);

        UninstallerCaptionButton close = new(p);
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);
        return titleBar;
    }

    private Grid BuildBody(SettingsPalette p, bool rounded, StackPanel buttons)
    {
        Grid body = new() { Margin = new Thickness(28, 8, 28, 20), };
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock header = TrayAppDotNETSettingsUI.SectionHeader(
            L("Uninstaller_SectionHeader", "Uninstall BatteryTrayAppDotNET"),
            p);
        body.Children.Add(header);

        TextBlock description = TrayAppDotNETSettingsUI.DescriptionText(
            string.Format(
                L("Uninstaller_Description_Format",
                    "This will remove BatteryTrayAppDotNET installed at \"{0}\" and its entry in Windows Settings > Apps. Choose what to do with your settings."),
                _installDir),
            p,
            new Thickness(0, 0, 0, 16));
        Grid.SetRow(description, 1);
        body.Children.Add(description);

        StackPanel choices = new();
        choices.Children.Add(BuildOptionCard(
            _keepSettings,
            L("Uninstaller_KeepSettings_Title", "Keep my settings"),
            L("Uninstaller_KeepSettings_Description", "Leave settings.xml in place so a future install picks them up."),
            p,
            rounded));
        choices.Children.Add(BuildOptionCard(
            _deleteSettings,
            L("Uninstaller_DeleteSettings_Title", "Delete my settings"),
            string.Format(
                L("Uninstaller_DeleteSettings_Description_Format", "Also remove \"{0}\" including settings.xml."),
                AppSettings.GetDefaultDirectory()),
            p,
            rounded));
        Grid.SetRow(choices, 2);
        body.Children.Add(choices);

        Grid.SetRow(buttons, 3);
        body.Children.Add(buttons);
        return body;
    }

    private static bool ResolveEffectiveIsLight() => AppServices.Settings?.ThemeMode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        _ => AppServices.Theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme,
    };

    private static Border BuildOptionCard(RadioButton radio, string title, string description, SettingsPalette p,
        bool rounded)
    {
        StackPanel text = new()
        {
            Children =
            {
                TrayAppDotNETSettingsUI.TitleText(title, p),
                TrayAppDotNETSettingsUI.DescriptionText(description, p),
            },
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Children.Add(radio);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        Border card = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.CardBackground),
            CornerRadius = new CornerRadius(rounded ? 6 : 0),
            Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 0, 0, 6),
            Child = grid,
        };
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            radio.IsChecked = true;
            e.Handled = true;
        };
        return card;
    }

    private static string L(string key, string fallback)
    {
        try
        {
            string value = TrayLocalization.Instance[key];
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}

internal sealed class UninstallerCaptionButton : Border
{
    private readonly SettingsPalette _palette;
    private readonly TextBlock _glyph;
    private bool _isPointerOver;
    private bool _isPressed;

    public UninstallerCaptionButton(SettingsPalette palette)
    {
        _palette = palette;
        Width = 46;
        Height = 32;
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        _glyph = new TextBlock
        {
            Text = GlyphCatalog.EXIT,
            FontFamily = TrayAppDotNETSettingsUI.IconFont,
            FontSize = 10,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        Child = _glyph;

        PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            UpdateVisual();
        };
        PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            _isPressed = false;
            UpdateVisual();
        };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _isPressed = true;
            e.Pointer.Capture(this);
            UpdateVisual();
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (!_isPressed) return;
            _isPressed = false;
            e.Pointer.Capture(null);
            bool clicked = _isPointerOver;
            UpdateVisual();
            if (clicked)
            {
                Click?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            Click?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
    }

    public event EventHandler? Click;

    private void UpdateVisual()
    {
        if (_isPressed)
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CloseButtonPressed);
            _glyph.Foreground = TrayAppDotNETSettingsUI.Brush(_palette.CloseButtonGlyphActive);
            return;
        }

        if (_isPointerOver)
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CloseButtonHover);
            _glyph.Foreground = TrayAppDotNETSettingsUI.Brush(_palette.CloseButtonGlyphActive);
            return;
        }

        Background = Brushes.Transparent;
        _glyph.Foreground = TrayAppDotNETSettingsUI.Brush(_palette.Foreground);
    }
}
