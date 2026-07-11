using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Models;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class TrayAppDotNETDialogChromeLayout
{
    private static readonly Lazy<DialogChromeResources> Resources = new(static () => new DialogChromeResources());

    private static DialogChromeResources AXAMLResources => Resources.Value;

    public static double UninstallerWindowWidth => AXAMLResources.AxamlDialogChrome.UninstallerWindowWidth;
    public static double UninstallerWindowHeight => AXAMLResources.AxamlDialogChrome.UninstallerWindowHeight;
    public static double UninstallerWindowMinWidth => AXAMLResources.AxamlDialogChrome.UninstallerWindowMinWidth;
    public static double UninstallerWindowMinHeight => AXAMLResources.AxamlDialogChrome.UninstallerWindowMinHeight;
    public static double TitleBarHeight => AXAMLResources.AxamlDialogChrome.TitleBarHeight;
    public static Thickness RootBorderThickness => AXAMLResources.AxamlDialogChrome.RootBorderThickness;
    public static CornerRadius RootCornerRadius => AXAMLResources.AxamlDialogChrome.RootCornerRadius;
    public static CornerRadius CardCornerRadius => AXAMLResources.AxamlDialogChrome.CardCornerRadius;
    public static CornerRadius ZeroCornerRadius => AXAMLResources.AxamlDialogChrome.ZeroCornerRadius;
    public static Thickness TitleMargin => AXAMLResources.AxamlDialogChrome.TitleMargin;
    public static Thickness BodyMargin => AXAMLResources.AxamlDialogChrome.BodyMargin;
    public static Thickness DescriptionMargin => AXAMLResources.AxamlDialogChrome.DescriptionMargin;
    public static Thickness OptionRadioMargin => AXAMLResources.AxamlDialogChrome.OptionRadioMargin;
    public static Thickness OptionCardPadding => AXAMLResources.AxamlDialogChrome.OptionCardPadding;
    public static Thickness OptionCardMargin => AXAMLResources.AxamlDialogChrome.OptionCardMargin;
    public static Thickness ButtonPadding => AXAMLResources.AxamlDialogChrome.ButtonPadding;
    public static Thickness CancelButtonMargin => AXAMLResources.AxamlDialogChrome.CancelButtonMargin;
    public static Thickness ButtonsMargin => AXAMLResources.AxamlDialogChrome.ButtonsMargin;
}

public sealed record TrayAppDotNETUninstallerWindowOptions
{
    public required string ApplicationName { get; init; }
    public required string InstallDirectory { get; init; }
    public required string SettingsDirectory { get; init; }
    public required InstallScope InstallScope { get; init; }
    public required WindowIcon? Icon { get; init; }
    public required SettingsPalette Palette { get; init; }
    public required bool EnableRoundedCorners { get; init; }
    public required Func<string, string, string> Localize { get; init; }
    public required Action<InstallScope> RetargetStartupShortcut { get; init; }
    public required Func<InstallScope, bool, Process?> RunUninstall { get; init; }
}

/// <summary>
/// Shared custom-chrome uninstaller confirmation window.
/// </summary>
public class TrayAppDotNETUninstallerWindow : Window, IDisposable
{
    private const int UninstallProcessOwnershipGraceMs = 5000;
    private const string SettingsChoiceGroupName = "SettingsChoice";

    private TrayAppDotNETUninstallerWindowOptions? _options;
    private RadioButton? _keepSettings;
    private RadioButton? _deleteSettings;
    private SettingsButton? _uninstallButton;
    private SettingsButton? _cancelButton;
    private readonly UIResourceScope _windowResources;
    private UIContentGeneration? _contentGeneration;
    private UninstallProcessOwner? _uninstallProcessOwner;
    private int _disposeState;
    private bool _closed;
    private bool _uninstallStarted;

    public TrayAppDotNETUninstallerWindow(TrayAppDotNETUninstallerWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _windowResources = new UIResourceScope(nameof(TrayAppDotNETUninstallerWindow));

        Title = Localize("Uninstaller_Title", $"Uninstall {options.ApplicationName}");
        Width = TrayAppDotNETDialogChromeLayout.UninstallerWindowWidth;
        Height = TrayAppDotNETDialogChromeLayout.UninstallerWindowHeight;
        MinWidth = TrayAppDotNETDialogChromeLayout.UninstallerWindowMinWidth;
        MinHeight = TrayAppDotNETDialogChromeLayout.UninstallerWindowMinHeight;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Icon = options.Icon;

        _keepSettings = CreateChoiceRadio(isChecked: true);
        _deleteSettings = CreateChoiceRadio(isChecked: false);

        KeyDown += OnWindowKeyDown;
        _windowResources.Add(() => KeyDown -= OnWindowKeyDown);
        Closed += OnWindowClosed;
        _windowResources.Add(() => Closed -= OnWindowClosed);

        UIResourceScope contentResources = new(nameof(TrayAppDotNETUninstallerWindow) + ".Content");
        try
        {
            Border root = BuildRoot(contentResources);
            UIContentGeneration contentGeneration = new(
                nameof(TrayAppDotNETUninstallerWindow),
                root,
                contentResources);
            _contentGeneration = contentGeneration;
            Content = root;
            _windowResources.Add(() => RetireContent(contentGeneration));
        }
        catch
        {
            contentResources.Dispose();
            DisposeCore();
            throw;
        }
    }

    public Process? UninstallProcess
    {
        get => Interlocked.Exchange(ref _uninstallProcessOwner, null)?.Transfer();
    }

    public bool ConfirmedUninstall { get; private set; }

    private static RadioButton CreateChoiceRadio(bool isChecked) =>
        new()
        {
            IsChecked = isChecked,
            GroupName = SettingsChoiceGroupName,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = TrayAppDotNETDialogChromeLayout.OptionRadioMargin
        };

    private TrayAppDotNETUninstallerWindowOptions Options =>
        _options ?? throw new ObjectDisposedException(nameof(TrayAppDotNETUninstallerWindow));

    private Border BuildRoot(UIResourceScope resources)
    {
        Grid chrome = new();
        chrome.RowDefinitions.Add(new RowDefinition(new GridLength(TrayAppDotNETDialogChromeLayout.TitleBarHeight)));
        chrome.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        chrome.Children.Add(BuildTitleBar(resources));

        Grid body = BuildBody(resources);
        Grid.SetRow(body, 1);
        chrome.Children.Add(body);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(Options.Palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(Options.Palette.Border),
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
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        titleBar.PointerPressed += OnTitleBarPointerPressed;
        resources.Add(() => titleBar.PointerPressed -= OnTitleBarPointerPressed);

        TextBlock title = TrayAppDotNETSettingsUI.Text(
            Localize("Uninstaller_Title", $"Uninstall {Options.ApplicationName}"),
            Options.Palette,
            13);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = TrayAppDotNETDialogChromeLayout.TitleMargin;
        titleBar.Children.Add(title);

        TrayAppDotNETCaptionCloseButton close = new(Options.Palette);
        TrayAppDotNETToolTip.SetTip(close, Localize("Uninstaller_Caption_Close", "Close"));
        TrayAppDotNETToolTip.SuppressWhileEngaged(close);
        close.Click += OnCancelClick;
        resources.Add(() => close.Click -= OnCancelClick);
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);
        return titleBar;
    }

    private Grid BuildBody(UIResourceScope resources)
    {
        Grid body = new() { Margin = TrayAppDotNETDialogChromeLayout.BodyMargin };
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock header = TrayAppDotNETSettingsUI.SectionHeader(
            Localize("Uninstaller_SectionHeader", $"Uninstall {Options.ApplicationName}"),
            Options.Palette);
        body.Children.Add(header);

        TextBlock description = TrayAppDotNETSettingsUI.DescriptionText(
            UninstallDescription(),
            Options.Palette,
            TrayAppDotNETDialogChromeLayout.DescriptionMargin);
        Grid.SetRow(description, 1);
        body.Children.Add(description);

        StackPanel choices = new();
        choices.Children.Add(BuildOptionCard(
            _keepSettings!,
            Localize("Uninstaller_KeepSettings_Title", "Keep my settings"),
            Localize("Uninstaller_KeepSettings_Description",
                "Leave settings.xml in place so a future install picks them up."),
            resources));
        choices.Children.Add(BuildOptionCard(
            _deleteSettings!,
            Localize("Uninstaller_DeleteSettings_Title", "Delete my settings"),
            string.Format(
                CultureInfo.CurrentCulture,
                Localize("Uninstaller_DeleteSettings_Description_Format",
                    "Also remove \"{0}\" including settings.xml."),
                Options.SettingsDirectory),
            resources));
        Grid.SetRow(choices, 2);
        body.Children.Add(choices);

        StackPanel buttons = BuildButtons(resources);
        Grid.SetRow(buttons, 3);
        body.Children.Add(buttons);
        return body;
    }

    private StackPanel BuildButtons(UIResourceScope resources)
    {
        SettingsButton uninstall = TrayAppDotNETSettingsUI.Button(
            Localize("Uninstaller_UninstallButton", "Uninstall"),
            Options.Palette);
        uninstall.Padding = TrayAppDotNETDialogChromeLayout.ButtonPadding;

        SettingsButton cancel = TrayAppDotNETSettingsUI.Button(
            Localize("Uninstaller_Cancel", "Cancel"),
            Options.Palette);
        cancel.Padding = TrayAppDotNETDialogChromeLayout.ButtonPadding;
        cancel.Margin = TrayAppDotNETDialogChromeLayout.CancelButtonMargin;

        _uninstallButton = uninstall;
        _cancelButton = cancel;
        uninstall.Click += OnUninstallClick;
        resources.Add(() => uninstall.Click -= OnUninstallClick);
        cancel.Click += OnCancelClick;
        resources.Add(() => cancel.Click -= OnCancelClick);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = TrayAppDotNETDialogChromeLayout.ButtonsMargin,
            Children = { cancel, uninstall }
        };
    }

    private void TrackUninstallProcess(Process? process)
    {
        UninstallProcessOwner? replacement = process == null
            ? null
            : new UninstallProcessOwner(process, UninstallProcessOwnershipGraceMs);
        UninstallProcessOwner? previous = Interlocked.Exchange(ref _uninstallProcessOwner, replacement);
        previous?.Dispose();
    }

    private Border BuildOptionCard(
        RadioButton radio,
        string title,
        string description,
        UIResourceScope resources)
    {
        StackPanel text = new()
        {
            Children =
            {
                TrayAppDotNETSettingsUI.TitleText(title, Options.Palette),
                TrayAppDotNETSettingsUI.DescriptionText(description, Options.Palette)
            }
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Children.Add(radio);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        Border card = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(Options.Palette.CardBackground),
            CornerRadius = Rounded(TrayAppDotNETDialogChromeLayout.CardCornerRadius),
            Padding = TrayAppDotNETDialogChromeLayout.OptionCardPadding,
            Margin = TrayAppDotNETDialogChromeLayout.OptionCardMargin,
            Child = grid
        };
        EventHandler<PointerPressedEventArgs> pointerPressed = (_, e) =>
        {
            if (_closed) return;
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;

            radio.IsChecked = true;
            e.Handled = true;
        };
        card.PointerPressed += pointerPressed;
        resources.Add(() => card.PointerPressed -= pointerPressed);
        return card;
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

    private void OnUninstallClick(object? sender, EventArgs e)
    {
        if (_closed || _uninstallStarted) return;

        TrayAppDotNETUninstallerWindowOptions options = Options;
        bool deleteSettings = _deleteSettings?.IsChecked == true;
        _uninstallStarted = true;
        if (_uninstallButton != null)
        {
            _uninstallButton.IsEnabled = false;
            _uninstallButton.Text = Localize("Uninstaller_UninstallingButton", "Uninstalling...");
        }

        if (_cancelButton != null)
            _cancelButton.IsEnabled = false;

        options.RetargetStartupShortcut(options.InstallScope);
        ConfirmedUninstall = true;
        TrackUninstallProcess(options.RunUninstall(options.InstallScope, deleteSettings));
        Close();
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        if (_closed) return;
        Close();
    }

    private void OnWindowClosed(object? sender, EventArgs e) => DisposeCore();

    /// <summary>Closes the dialog when necessary and releases all window-owned resources.</summary>
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
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;

        _closed = true;
        _windowResources.Dispose();
        UIContentGeneration? contentGeneration = Interlocked.Exchange(ref _contentGeneration, null);
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
        _keepSettings = null;
        _deleteSettings = null;
        _uninstallButton = null;
        _cancelButton = null;
        _options = null;
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

    private string UninstallDescription()
    {
        string fallback = $"This will remove {Options.ApplicationName} installed at \"{{0}}\" and its entry in Windows Settings > Apps. Choose what to do with your settings.";
        string format = Localize("Uninstaller_Description_Format", fallback);
        return string.Format(CultureInfo.CurrentCulture, format, Options.InstallDirectory);
    }

    private CornerRadius Rounded(CornerRadius radius) =>
        Options.EnableRoundedCorners ? radius : TrayAppDotNETDialogChromeLayout.ZeroCornerRadius;

    private string Localize(string key, string fallback) => Options.Localize(key, fallback);

    private sealed class UninstallProcessOwner : IDisposable
    {
        private readonly object _gate = new();
        private readonly int _ownershipGraceMilliseconds;
        private Process? _process;
        private System.Threading.Timer? _disposalTimer;
        private bool _finished;

        public UninstallProcessOwner(Process process, int ownershipGraceMilliseconds)
        {
            ArgumentNullException.ThrowIfNull(process);
            _process = process;
            _ownershipGraceMilliseconds = ownershipGraceMilliseconds;

            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += OnProcessExited;
                if (process.HasExited)
                    ScheduleUnclaimedDisposal();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public Process? Transfer()
        {
            Process? process;
            System.Threading.Timer? disposalTimer;
            lock (_gate)
            {
                if (_finished) return null;

                _finished = true;
                process = _process;
                _process = null;
                disposalTimer = _disposalTimer;
                _disposalTimer = null;
            }

            disposalTimer?.Dispose();
            DetachExitedHandler(process);
            return process;
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            lock (_gate)
            {
                if (_finished || !ReferenceEquals(sender, _process)) return;
            }

            ScheduleUnclaimedDisposal();
        }

        private void ScheduleUnclaimedDisposal()
        {
            lock (_gate)
            {
                if (_finished || _disposalTimer != null) return;

                _disposalTimer = new System.Threading.Timer(
                    static state => ((UninstallProcessOwner)state!).Dispose(),
                    this,
                    _ownershipGraceMilliseconds,
                    Timeout.Infinite);
            }
        }

        public void Dispose()
        {
            Process? process;
            System.Threading.Timer? disposalTimer;
            lock (_gate)
            {
                if (_finished) return;

                _finished = true;
                process = _process;
                _process = null;
                disposalTimer = _disposalTimer;
                _disposalTimer = null;
            }

            disposalTimer?.Dispose();
            DetachExitedHandler(process);
            try
            {
                process?.Dispose();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Uninstall process disposal failed: {exception.Message}");
            }
        }

        private void DetachExitedHandler(Process? process)
        {
            if (process == null) return;

            try
            {
                process.Exited -= OnProcessExited;
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Uninstall process event detachment failed: {exception.Message}");
            }
        }
    }
}
