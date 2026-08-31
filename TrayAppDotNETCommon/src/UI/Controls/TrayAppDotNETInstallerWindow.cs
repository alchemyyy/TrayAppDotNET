using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>Configures the shared installer window.</summary>
public sealed record TrayAppDotNETInstallerWindowOptions
{
    public required TrayAppDotNETInstallLayout Layout { get; init; }
    public required WindowIcon? Icon { get; init; }
    public required SettingsPalette Palette { get; init; }
    public required bool EnableRoundedCorners { get; init; }
    public InstallScope InitialScope { get; init; } = InstallScope.LocalAppData;
    public TrayAppDotNETInstallOptions InitialInstallOptions { get; init; } = new();
}

/// <summary>Contains the install choices confirmed by the user.</summary>
public sealed record TrayAppDotNETInstallerWindowResult(
    InstallScope Scope,
    string InstallDirectory,
    TrayAppDotNETInstallOptions InstallOptions);

/// <summary>Shared one-page installer window for TrayAppDotNET applications.</summary>
public sealed class TrayAppDotNETInstallerWindow : Window, IDisposable
{
    private readonly TrayAppDotNETInstallerWindowOptions _options;
    private readonly UIResourceScope _windowResources;
    private readonly SettingsButton _localLocationButton;
    private readonly SettingsButton _systemLocationButton;
    private readonly TextBlock _installPath;
    private readonly CheckBox _desktopShortcut;
    private readonly CheckBox _startMenuShortcut;
    private UIContentGeneration? _contentGeneration;
    private InstallScope _selectedScope;
    private int _disposeState;
    private bool _closed;

    public TrayAppDotNETInstallerWindow(TrayAppDotNETInstallerWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Layout);
        ArgumentNullException.ThrowIfNull(options.Palette);
        ArgumentNullException.ThrowIfNull(options.InitialInstallOptions);
        ValidateScope(options.InitialScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Layout.LocalAppDataInstallDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Layout.ProgramFilesInstallDirectory);

        _options = options;
        _selectedScope = options.InitialScope;
        _windowResources = new UIResourceScope(nameof(TrayAppDotNETInstallerWindow));
        _localLocationButton = BuildLocationButton(L(nameof(CommonStrings.Installer_LocalLocation)));
        _systemLocationButton = BuildLocationButton(L(nameof(CommonStrings.Installer_SystemLocation)));
        _installPath = BuildInstallPathText();
        _desktopShortcut = BuildShortcutCheckBox(
            L(nameof(CommonStrings.Installer_DesktopShortcut)),
            options.InitialInstallOptions.CreateDesktopShortcut);
        _startMenuShortcut = BuildShortcutCheckBox(
            L(nameof(CommonStrings.Installer_StartMenuShortcut)),
            options.InitialInstallOptions.CreateStartMenuShortcut);

        Title = FormatApplicationName(nameof(CommonStrings.Installer_Title_Format));
        Width = TrayAppDotNETDialogChromeLayout.InstallerWindowWidth;
        Height = TrayAppDotNETDialogChromeLayout.InstallerWindowHeight;
        MinWidth = TrayAppDotNETDialogChromeLayout.InstallerWindowMinWidth;
        MinHeight = TrayAppDotNETDialogChromeLayout.InstallerWindowMinHeight;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Icon = options.Icon;

        KeyDown += OnWindowKeyDown;
        _windowResources.Add(() => KeyDown -= OnWindowKeyDown);
        Closed += OnWindowClosed;
        _windowResources.Add(() => Closed -= OnWindowClosed);

        UIResourceScope contentResources = new(nameof(TrayAppDotNETInstallerWindow) + ".Content");
        try
        {
            Border root = BuildRoot(contentResources);
            UIContentGeneration contentGeneration = new(
                nameof(TrayAppDotNETInstallerWindow),
                root,
                contentResources);
            _contentGeneration = contentGeneration;
            ControlNameScope.For(this).AssignLogicalSubtree(root, this);
            Content = root;
            _windowResources.Add(() => RetireContent(contentGeneration));
            UpdateSelectedLocation();
        }
        catch
        {
            contentResources.Dispose();
            DisposeCore();
            throw;
        }
    }

    /// <summary>Gets the install choices after the user confirms, or null after cancellation.</summary>
    public TrayAppDotNETInstallerWindowResult? Result { get; private set; }

    /// <summary>Gets the currently selected local or system install scope.</summary>
    public InstallScope SelectedScope => _selectedScope;

    /// <summary>Gets the full directory displayed for the selected scope.</summary>
    public string SelectedInstallDirectory => _selectedScope switch
    {
        InstallScope.LocalAppData => _options.Layout.LocalAppDataInstallDirectory,
        InstallScope.ProgramFiles => _options.Layout.ProgramFilesInstallDirectory,
        _ => throw new InvalidOperationException($"Unsupported installer scope '{_selectedScope}'.")
    };

    /// <summary>Gets the currently selected shortcut options.</summary>
    public TrayAppDotNETInstallOptions SelectedInstallOptions => new(
        _desktopShortcut.IsChecked == true,
        _startMenuShortcut.IsChecked == true);

    private Border BuildRoot(UIResourceScope resources)
    {
        Grid chrome = new();
        chrome.RowDefinitions.Add(new RowDefinition(new GridLength(TrayAppDotNETDialogChromeLayout.TitleBarHeight)));
        chrome.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        chrome.Children.Add(BuildTitleBar(resources));

        Grid body = BuildBody(resources);
        Grid.SetRow(body, value: 1);
        chrome.Children.Add(body);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(_options.Palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_options.Palette.Border),
            BorderThickness = TrayAppDotNETDialogChromeLayout.RootBorderThickness,
            CornerRadius = Rounded(TrayAppDotNETDialogChromeLayout.RootCornerRadius),
            Child = chrome
        };
    }

    private Grid BuildTitleBar(UIResourceScope resources)
    {
        Grid titleBar = new()
        {
            Background = Brushes.Transparent,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        titleBar.PointerPressed += OnTitleBarPointerPressed;
        resources.Add(() => titleBar.PointerPressed -= OnTitleBarPointerPressed);

        TextBlock title = TrayAppDotNETSettingsUI.Text(
            FormatApplicationName(nameof(CommonStrings.Installer_Title_Format)),
            _options.Palette,
            TrayAppDotNETDialogChromeLayout.TitleFontSize);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = TrayAppDotNETDialogChromeLayout.TitleMargin;
        titleBar.Children.Add(title);

        TrayAppDotNETCaptionCloseButton close = new(_options.Palette);
        TrayAppDotNETToolTip.SetTip(close, L(nameof(CommonStrings.Installer_Caption_Close)));
        TrayAppDotNETToolTip.SuppressWhileEngaged(close);
        close.Click += OnCancelClick;
        resources.Add(() => close.Click -= OnCancelClick);
        Grid.SetColumn(close, value: 1);
        titleBar.Children.Add(close);
        return titleBar;
    }

    private Grid BuildBody(UIResourceScope resources)
    {
        Grid body = new() { Margin = TrayAppDotNETDialogChromeLayout.BodyMargin };
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock header = TrayAppDotNETSettingsUI.SectionHeader(
            FormatApplicationName(nameof(CommonStrings.Installer_SectionHeader_Format)),
            _options.Palette);
        body.Children.Add(header);

        TextBlock description = TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(CommonStrings.Installer_Description)),
            _options.Palette,
            TrayAppDotNETDialogChromeLayout.DescriptionMargin);
        Grid.SetRow(description, value: 1);
        body.Children.Add(description);

        StackPanel location = BuildLocationSelector(resources);
        Grid.SetRow(location, value: 2);
        body.Children.Add(location);

        StackPanel shortcuts = new()
        {
            Children = { BuildShortcutCard(_desktopShortcut), BuildShortcutCard(_startMenuShortcut) }
        };
        Grid.SetRow(shortcuts, value: 3);
        body.Children.Add(shortcuts);

        StackPanel buttons = BuildButtons(resources);
        Grid.SetRow(buttons, value: 4);
        body.Children.Add(buttons);
        return body;
    }

    private StackPanel BuildLocationSelector(UIResourceScope resources)
    {
        TextBlock title = TrayAppDotNETSettingsUI.TitleText(
            L(nameof(CommonStrings.Installer_InstallLocation)),
            _options.Palette);
        title.Margin = TrayAppDotNETDialogChromeLayout.InstallerLocationTitleMargin;

        Grid buttons = new()
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            Margin = TrayAppDotNETDialogChromeLayout.InstallerLocationButtonsMargin
        };
        _localLocationButton.Margin = TrayAppDotNETDialogChromeLayout.InstallerLocationButtonGap;
        _localLocationButton.Click += OnLocalLocationClick;
        resources.Add(() => _localLocationButton.Click -= OnLocalLocationClick);
        buttons.Children.Add(_localLocationButton);

        _systemLocationButton.Click += OnSystemLocationClick;
        resources.Add(() => _systemLocationButton.Click -= OnSystemLocationClick);
        Grid.SetColumn(_systemLocationButton, value: 1);
        buttons.Children.Add(_systemLocationButton);

        Border path = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_options.Palette.ControlBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_options.Palette.Border),
            BorderThickness = TrayAppDotNETDialogChromeLayout.InstallerPathBorderThickness,
            CornerRadius = Rounded(TrayAppDotNETDialogChromeLayout.CardCornerRadius),
            Padding = TrayAppDotNETDialogChromeLayout.InstallerPathPadding,
            Margin = TrayAppDotNETDialogChromeLayout.InstallerPathMargin,
            Child = _installPath
        };

        return new StackPanel { Children = { title, buttons, path } };
    }

    private StackPanel BuildButtons(UIResourceScope resources)
    {
        SettingsButton install = TrayAppDotNETSettingsUI.Button(
            L(nameof(CommonStrings.Installer_InstallButton)),
            _options.Palette);
        install.Padding = TrayAppDotNETDialogChromeLayout.ActionButtonPadding;
        install.Click += OnInstallClick;
        resources.Add(() => install.Click -= OnInstallClick);

        SettingsButton cancel = TrayAppDotNETSettingsUI.Button(
            L(nameof(CommonStrings.Installer_Cancel)),
            _options.Palette);
        cancel.Padding = TrayAppDotNETDialogChromeLayout.ActionButtonPadding;
        cancel.Margin = TrayAppDotNETDialogChromeLayout.CancelButtonMargin;
        cancel.Click += OnCancelClick;
        resources.Add(() => cancel.Click -= OnCancelClick);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = TrayAppDotNETDialogChromeLayout.ActionButtonsMargin,
            Children = { cancel, install }
        };
    }

    private SettingsButton BuildLocationButton(string text) =>
        new(text, _options.Palette)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderBrush = Brushes.Transparent,
            BorderThickness = TrayAppDotNETDialogChromeLayout.InstallerLocationButtonBorderThickness,
            Padding = TrayAppDotNETDialogChromeLayout.InstallerLocationButtonPadding
        };

    private TextBlock BuildInstallPathText()
    {
        TextBlock path = TrayAppDotNETSettingsUI.Text(
            string.Empty,
            _options.Palette,
            TrayAppDotNETDialogChromeLayout.InstallerPathFontSize);
        path.FontFamily = new FontFamily("Cascadia Mono, Consolas, Segoe UI");
        path.Foreground = TrayAppDotNETSettingsUI.Brush(_options.Palette.SecondaryForeground);
        path.TextWrapping = TextWrapping.Wrap;
        return path;
    }

    private CheckBox BuildShortcutCheckBox(string text, bool isChecked) =>
        new()
        {
            Content = TrayAppDotNETSettingsUI.TitleText(text, _options.Palette),
            Cursor = TrayAppDotNETCursors.Hand,
            Foreground = TrayAppDotNETSettingsUI.Brush(_options.Palette.Foreground),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsChecked = isChecked
        };

    private Border BuildShortcutCard(CheckBox checkBox) =>
        new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_options.Palette.CardBackground),
            CornerRadius = Rounded(TrayAppDotNETDialogChromeLayout.CardCornerRadius),
            Padding = TrayAppDotNETDialogChromeLayout.InstallerShortcutPadding,
            Margin = TrayAppDotNETDialogChromeLayout.InstallerShortcutMargin,
            Child = checkBox
        };

    private void OnLocalLocationClick(object? sender, EventArgs e) => SelectLocation(InstallScope.LocalAppData);

    private void OnSystemLocationClick(object? sender, EventArgs e) => SelectLocation(InstallScope.ProgramFiles);

    private void SelectLocation(InstallScope scope)
    {
        if (_closed || _selectedScope == scope) return;

        ValidateScope(scope);
        _selectedScope = scope;
        UpdateSelectedLocation();
    }

    private void UpdateSelectedLocation()
    {
        bool isLocal = _selectedScope == InstallScope.LocalAppData;
        UpdateLocationButton(_localLocationButton, isLocal);
        UpdateLocationButton(_systemLocationButton, !isLocal);
        _installPath.Text = SelectedInstallDirectory;
    }

    private void UpdateLocationButton(SettingsButton button, bool isSelected)
    {
        button.BorderBrush = isSelected
            ? TrayAppDotNETSettingsUI.Brush(_options.Palette.Accent)
            : Brushes.Transparent;
        button.Label.Foreground = isSelected
            ? TrayAppDotNETSettingsUI.Brush(_options.Palette.Accent)
            : TrayAppDotNETSettingsUI.Brush(_options.Palette.Foreground);
    }

    private void OnInstallClick(object? sender, EventArgs e)
    {
        if (_closed) return;

        Result = new TrayAppDotNETInstallerWindowResult(
            _selectedScope,
            SelectedInstallDirectory,
            SelectedInstallOptions);
        Close();
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        if (_closed) return;
        Close();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_closed || e.Key != Key.Escape) return;

        Close();
        e.Handled = true;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_closed || sender is not Control titleBar) return;
        if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed) return;

        BeginMoveDrag(e);
        e.Handled = true;
    }

    private void OnWindowClosed(object? sender, EventArgs e) => DisposeCore();

    /// <summary>Closes the installer when necessary and releases all window-owned resources.</summary>
    public void Dispose()
    {
        if (!_closed && IsVisible)
        {
            try
            {
                Close();
            }
            finally
            {
                DisposeCore();
            }

            return;
        }

        DisposeCore();
    }

    private void DisposeCore()
    {
        if (Interlocked.Exchange(ref _disposeState, value: 1) != 0) return;

        _closed = true;
        _windowResources.Dispose();
        UIContentGeneration? contentGeneration = Interlocked.Exchange(ref _contentGeneration, value: null);
        if (contentGeneration != null)
        {
            try
            {
                if (!contentGeneration.IsDisposed && ReferenceEquals(Content, contentGeneration.Root))
                    Content = null;
            }
            finally
            {
                contentGeneration.Dispose();
            }
        }

        Icon = null;
    }

    private void RetireContent(UIContentGeneration contentGeneration)
    {
        if (ReferenceEquals(_contentGeneration, contentGeneration))
            _contentGeneration = null;
        try
        {
            if (!contentGeneration.IsDisposed && ReferenceEquals(Content, contentGeneration.Root))
                Content = null;
        }
        finally
        {
            contentGeneration.Dispose();
        }
    }

    private string FormatApplicationName(string key) =>
        string.Format(CultureInfo.CurrentCulture, L(key), _options.Layout.ApplicationName);

    private CornerRadius Rounded(CornerRadius radius) =>
        _options.EnableRoundedCorners ? radius : TrayAppDotNETDialogChromeLayout.ZeroCornerRadius;

    private static void ValidateScope(InstallScope scope)
    {
        if (scope is InstallScope.LocalAppData or InstallScope.ProgramFiles) return;

        throw new ArgumentOutOfRangeException(nameof(scope), scope,
            message: "The installer supports local and system scopes only.");
    }

    private static string L(string key) => LocalizationManager.Instance[key];
}
