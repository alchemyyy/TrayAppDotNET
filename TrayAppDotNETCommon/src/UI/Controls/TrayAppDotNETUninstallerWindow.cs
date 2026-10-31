using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>
/// Starts the uninstall the window confirmed: scope, whether to delete settings, the action that exits this process
/// when it is the copy being removed, and the progress sink.
/// </summary>
public delegate TrayAppDotNETUninstallRun TrayAppDotNETRunUninstall(
    InstallScope scope,
    bool deleteSettings,
    Action exitCurrentProcess,
    IProgress<TrayAppDotNETInstallProgress>? progress);

public sealed record TrayAppDotNETUninstallerWindowOptions
{
    public required string ApplicationName { get; init; }
    public required string InstallDirectory { get; init; }
    public required string SettingsDirectory { get; init; }
    public required InstallScope InstallScope { get; init; }
    public required WindowIcon? Icon { get; init; }
    public required SettingsPalette Palette { get; init; }
    public required bool EnableRoundedCorners { get; init; }
    public required Func<string, string> L { get; init; }
    public required TrayAppDotNETRunUninstall RunUninstall { get; init; }

    /// <summary>Exits this process through the app's exit path when it is the installed copy being removed.</summary>
    public Action ExitCurrentProcess { get; init; } = TrayAppDotNETAppLifetime.Exit;

    /// <summary>Optional sink that receives the uninstall stage reports, typically the settings card.</summary>
    public IProgress<TrayAppDotNETInstallProgress>? Progress { get; init; }

    /// <summary>
    /// Builds the options for an app from its install services. A missing settings value keeps rounded corners on,
    /// the default every other window uses.
    /// </summary>
    public static TrayAppDotNETUninstallerWindowOptions Create(
        TrayAppDotNETAppInstallServices install,
        InstallScope scope,
        WindowIcon? icon,
        SettingsPalette palette,
        bool? enableRoundedCorners,
        IProgress<TrayAppDotNETInstallProgress>? progress)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(palette);

        return new TrayAppDotNETUninstallerWindowOptions
        {
            ApplicationName = install.ApplicationName,
            InstallDirectory = install.InstallDirectory(scope),
            SettingsDirectory = install.SettingsDirectory,
            InstallScope = scope,
            Icon = icon,
            Palette = palette,
            EnableRoundedCorners = enableRoundedCorners ?? true,
            L = static key => LocalizationManager.Instance[key],
            Progress = progress,
            RunUninstall = (uninstallScope, deleteSettings, exitCurrentProcess, uninstallProgress) =>
                install.Installation.RunUninstall(uninstallScope, deleteSettings, exitCurrentProcess, uninstallProgress)
        };
    }
}

/// <summary>
/// Shared custom-chrome uninstaller confirmation window. It closes once the uninstall has started; when the UAC
/// prompt is declined or the uninstall cannot start, it stays open and says so.
/// </summary>
public sealed class TrayAppDotNETUninstallerWindow : Window, IDisposable
{
    private const string SettingsChoiceGroupName = "SettingsChoice";

    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TrayAppDotNETUninstallerWindowOptions? _options;
    private RadioButton? _keepSettings;
    private RadioButton? _deleteSettings;
    private SettingsButton? _uninstallButton;
    private SettingsButton? _cancelButton;
    private TextBlock? _errorText;
    private readonly UIResourceScope _windowResources;
    private UIContentGeneration? _contentGeneration;
    private int _disposeState;
    private bool _closed;
    private bool _uninstallStarted;

    public TrayAppDotNETUninstallerWindow(TrayAppDotNETUninstallerWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _windowResources = new UIResourceScope(nameof(TrayAppDotNETUninstallerWindow));

        Title = FormatApplicationName(nameof(CommonStrings.Uninstaller_Title));
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

        _keepSettings = CreateChoiceRadio(true);
        _deleteSettings = CreateChoiceRadio(false);

        this.MapTo(ControlMap.Uninstaller.ID);
        this.MapCommand(ControlMap.Uninstaller.Dismiss, OnDismiss);
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
            ControlNameScope.For(this).AssignLogicalSubtree(root, this);
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

    /// <summary>
    /// Completes when the window closes: true when the uninstall started, false when it was cancelled or never
    /// started.
    /// </summary>
    public Task<bool> Completion => _completion.Task;

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
        Grid.SetRow(body, value: 1);
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
        Grid titleBar = new Grid
        {
            Background = Brushes.Transparent,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        }.MapTo(ControlMap.Uninstaller.TitleBar.ID);
        titleBar.PointerPressed += OnTitleBarPointerPressed;
        resources.Add(() => titleBar.PointerPressed -= OnTitleBarPointerPressed);

        TextBlock title = TrayAppDotNETSettingsUI.Text(
            FormatApplicationName(nameof(CommonStrings.Uninstaller_Title)),
            Options.Palette,
            TrayAppDotNETDialogChromeLayout.TitleFontSize);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = TrayAppDotNETDialogChromeLayout.TitleMargin;
        titleBar.Children.Add(title);

        TrayAppDotNETCaptionCloseButton close = new TrayAppDotNETCaptionCloseButton(Options.Palette)
            .MapTo(ControlMap.Uninstaller.TitleBar.Close);
        TrayAppDotNETToolTip.SetTip(close, L(nameof(CommonStrings.Uninstaller_Caption_Close)));
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
        body.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock header = TrayAppDotNETSettingsUI.SectionHeader(
            FormatApplicationName(nameof(CommonStrings.Uninstaller_SectionHeader)),
            Options.Palette);
        body.Children.Add(header);

        TextBlock description = TrayAppDotNETSettingsUI.DescriptionText(
            UninstallDescription(),
            Options.Palette,
            TrayAppDotNETDialogChromeLayout.DescriptionMargin);
        Grid.SetRow(description, value: 1);
        body.Children.Add(description);

        StackPanel choices = new StackPanel().MapTo(ControlMap.Uninstaller.SettingsChoice.ID);
        choices.Children.Add(BuildOptionCard(
            _keepSettings!,
            L(nameof(CommonStrings.Uninstaller_KeepSettings_Title)),
            L(nameof(CommonStrings.Uninstaller_KeepSettings_Description)),
            resources,
            ControlMap.Uninstaller.SettingsChoice.KeepSettings));
        choices.Children.Add(BuildOptionCard(
            _deleteSettings!,
            L(nameof(CommonStrings.Uninstaller_DeleteSettings_Title)),
            string.Format(
                CultureInfo.CurrentCulture,
                L(nameof(CommonStrings.Uninstaller_DeleteSettings_Description_Format)),
                Options.SettingsDirectory),
            resources,
            ControlMap.Uninstaller.SettingsChoice.DeleteSettings));
        Grid.SetRow(choices, value: 2);
        body.Children.Add(choices);

        // Why an uninstall did not start (UAC declined, the helper failed to launch); hidden until then
        TextBlock errorText = TrayAppDotNETSettingsUI.DescriptionText(
            string.Empty,
            Options.Palette,
            TrayAppDotNETDialogChromeLayout.DescriptionMargin);
        errorText.IsVisible = false;
        _errorText = errorText;
        Grid.SetRow(errorText, value: 3);
        body.Children.Add(errorText);

        StackPanel buttons = BuildButtons(resources);
        Grid.SetRow(buttons, value: 4);
        body.Children.Add(buttons);
        return body;
    }

    private StackPanel BuildButtons(UIResourceScope resources)
    {
        SettingsButton uninstall = TrayAppDotNETSettingsUI.Button(
            L(nameof(CommonStrings.Uninstaller_UninstallButton)),
            Options.Palette).MapTo(ControlMap.Uninstaller.Actions.Uninstall);
        uninstall.Padding = TrayAppDotNETDialogChromeLayout.ActionButtonPadding;

        SettingsButton cancel = TrayAppDotNETSettingsUI.Button(
            L(nameof(CommonStrings.Uninstaller_Cancel)),
            Options.Palette).MapTo(ControlMap.Uninstaller.Actions.Cancel);
        cancel.Padding = TrayAppDotNETDialogChromeLayout.ActionButtonPadding;
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
            Margin = TrayAppDotNETDialogChromeLayout.ActionButtonsMargin,
            Children = { cancel, uninstall }
        };
    }

    private Border BuildOptionCard(
        RadioButton radio,
        string title,
        string description,
        UIResourceScope resources,
        ControlMapNodeID node)
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
        Grid.SetColumn(text, value: 1);
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

        // The card is the option; Enter or Space on it checks its radio like a press
        card.MapTo(node);
        card.MapActivation(_ =>
        {
            if (!_closed) radio.IsChecked = true;
        });
        return card;
    }

    /// <summary>Closes without uninstalling, like Cancel; the control map runs it on Escape.</summary>
    private void OnDismiss()
    {
        if (_closed) return;
        Close();
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
        SetBusy(busy: true);

        // Reports made while starting reach the sink only once the uninstall is known to have started; a declined
        // UAC prompt or a failed start is shown here instead, and the window stays open for another try
        StartupProgressBuffer startupProgress = new(options.Progress);
        bool exitingCurrentProcess = false;
        TrayAppDotNETUninstallRun run;
        try
        {
            run = options.RunUninstall(
                options.InstallScope,
                deleteSettings,
                () =>
                {
                    exitingCurrentProcess = true;
                    _uninstallStarted = true;
                    options.ExitCurrentProcess();
                },
                startupProgress);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"TrayAppDotNETUninstallerWindow: uninstall failed to start: {exception}");
            ShowStartFailure(exception.Message);
            return;
        }

        run.Process?.Dispose();
        if (run.UserCancelled)
        {
            ShowStartFailure(L(nameof(CommonStrings.Uninstall_Progress_Cancelled)));
            return;
        }

        if (run.Process == null && !exitingCurrentProcess)
        {
            ShowStartFailure(run.ErrorMessage ?? L(nameof(CommonStrings.Uninstall_Progress_NotStarted)));
            return;
        }

        _uninstallStarted = true;
        startupProgress.Release();
        _completion.TrySetResult(true);
        if (!_closed) Close();
    }

    private void ShowStartFailure(string message)
    {
        if (_closed) return;

        SetBusy(busy: false);
        if (_errorText == null) return;

        _errorText.Text = message;
        _errorText.IsVisible = true;
    }

    private void SetBusy(bool busy)
    {
        if (_uninstallButton != null)
        {
            _uninstallButton.IsEnabled = !busy;
            _uninstallButton.Text = L(busy
                ? nameof(CommonStrings.Uninstaller_UninstallingButton)
                : nameof(CommonStrings.Uninstaller_UninstallButton));
        }

        _cancelButton?.IsEnabled = !busy;
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        if (_closed) return;
        Close();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _completion.TrySetResult(_uninstallStarted);
        DisposeCore();
    }

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
        _keepSettings = null;
        _deleteSettings = null;
        _uninstallButton = null;
        _cancelButton = null;
        _errorText = null;
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
        string format = L(nameof(CommonStrings.Uninstaller_Description_Format));
        return string.Format(
            CultureInfo.CurrentCulture,
            format,
            Options.InstallDirectory,
            Options.ApplicationName);
    }

    private string FormatApplicationName(string key) =>
        string.Format(CultureInfo.CurrentCulture, L(key), Options.ApplicationName);

    private CornerRadius Rounded(CornerRadius radius) =>
        Options.EnableRoundedCorners ? radius : TrayAppDotNETDialogChromeLayout.ZeroCornerRadius;

    private string L(string key) => Options.L(key);

    /// <summary>
    /// Holds the progress reports made while an uninstall is being started, and passes them and every later one on
    /// only once <see cref="Release"/> confirms the start. Reports of a start that failed are never passed on.
    /// </summary>
    private sealed class StartupProgressBuffer(IProgress<TrayAppDotNETInstallProgress>? sink)
        : IProgress<TrayAppDotNETInstallProgress>
    {
        private readonly Lock _gate = new();
        private List<TrayAppDotNETInstallProgress>? _pending = [];

        public void Report(TrayAppDotNETInstallProgress value)
        {
            if (sink == null) return;

            lock (_gate)
            {
                if (_pending != null)
                {
                    _pending.Add(value);
                    return;
                }
            }

            sink.Report(value);
        }

        public void Release()
        {
            List<TrayAppDotNETInstallProgress>? pending;
            lock (_gate)
            {
                pending = _pending;
                _pending = null;
            }

            if (sink == null || pending == null) return;

            foreach (TrayAppDotNETInstallProgress value in pending) sink.Report(value);
        }
    }
}
