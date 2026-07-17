using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed record TrayAppDotNETKnownIssue(string Title, string Description);

public sealed class TrayAppDotNETAboutPageOptions
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius ButtonRadius { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required Func<string, string, string> Localize { get; init; }
    public required Action Save { get; init; }
    public required string ApplicationName { get; init; }
    public required string Tagline { get; init; }
    public required int BuildNumber { get; init; }
    public required string Publisher { get; init; }
    public required string HelpLink { get; init; }
    public ITrayAppDotNETUpdateSettings? UpdateSettings { get; init; }
    public Func<UpdateCheckService?> UpdateService { get; init; } = static () => null;
    public IReadOnlyList<TrayAppDotNETKnownIssue> KnownIssues { get; init; } = [];

    public Func<string, string, string, string, Task<bool>> ConfirmAsync { get; init; } =
        static (_, _, _, _) => Task.FromResult(false);

    public Action Shutdown { get; init; } = ShutdownDesktopApp;
    public Action FlushLog { get; init; } = TADNLog.Flush;
    public Action<string> Log { get; init; } = static _ => { };
    public Func<Window?> PromptOwner { get; init; } = DefaultPromptOwner;
    public Action? RebuildAboutPage { get; init; }
    public bool SupportsFlyoutUpdateButton { get; init; } = true;
    public int StaleCheckTimerIntervalMs { get; init; } = TimeConstants.AboutStaleCheckTimerIntervalMs;
    public int UpdateStaleGraceMs { get; init; } = TimeConstants.UpdateStaleGraceMs;

    private static void ShutdownDesktopApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static Window? DefaultPromptOwner() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}

public sealed class TrayAppDotNETAboutPage : IDisposable
{
    private static readonly TrayAppDotNETAboutPageResources LayoutResources = new();

    private readonly TrayAppDotNETAboutPageOptions _options;
    private UpdateCheckService? _updateService;
    private DispatcherTimer? _staleTimer;
    private TextBlock? _updateStatusText;
    private SettingsButton? _checkForUpdatesButton;
    private SettingsButton? _skipUpdateButton;
    private SettingsButton? _installUpdateButton;
    private TextBlock? _backdateDescriptionText;
    private SettingsButton? _backdateButton;
    private UpdateInfo? _previousRelease;
    private StackPanel? _root;
    private bool _manualCheckInProgress;
    private bool _skipInProgress;
    private bool _installInProgress;
    private bool _previousReleaseLookupInProgress;
    private bool _previousReleaseLookupFailed;
    private bool _backdateInProgress;
    private bool _disposed;
    private long _refreshGeneration;

    public TrayAppDotNETAboutPage(TrayAppDotNETAboutPageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Localize);
        ArgumentNullException.ThrowIfNull(options.Save);
        _options = options;
    }

    public StackPanel Build()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(TrayAppDotNETAboutPage));
        StopUpdateRefresh();

        TrayAppDotNETAboutPageResources.AboutPageAxamlProperties layout = LayoutResources.AxamlAboutPage;
        SettingsPalette p = _options.Palette;
        StackPanel stack = TrayAppDotNETSettingsCards.PageStack(L("Settings_About_SectionHeader", "About"), p);
        SetRoot(stack);

        TextBlock appName = TrayAppDotNETSettingsUI.Text(_options.ApplicationName, p);
        appName.Margin = layout.AppNameMargin;
        stack.Children.Add(appName);

        TextBlock tagline = TrayAppDotNETSettingsUI.DescriptionText(_options.Tagline, p, layout.TaglineMargin);
        tagline.Opacity = layout.TaglineOpacity;
        stack.Children.Add(tagline);

        stack.Children.Add(AboutRow(L("Settings_About_BuildLabel", "Build"),
            _options.BuildNumber.ToString(CultureInfo.InvariantCulture), p));
        stack.Children.Add(AboutRow(L("Settings_About_RuntimeLabel", "Runtime"),
            RuntimeInformation.FrameworkDescription, p));
        stack.Children.Add(AboutRow(L("Settings_About_AuthorLabel", "Author"), _options.Publisher, p));
        stack.Children.Add(AboutRow(L("Settings_About_GithubLabel", "GitHub"), _options.HelpLink, p,
            _options.HelpLink));

        if (_options.UpdateSettings != null)
            AddUpdatesSection(stack, p);

        if (_options.KnownIssues.Count > 0)
        {
            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_About_KnownIssues_Header", "Known Issues"), p));
            foreach (TrayAppDotNETKnownIssue issue in _options.KnownIssues)
                stack.Children.Add(BuildKnownIssueCard(issue.Title, issue.Description, p));
        }

        return stack;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        StopUpdateRefresh();
        SetRoot(null);
    }

    public void StopUpdateRefresh()
    {
        Interlocked.Increment(ref _refreshGeneration);
        UpdateCheckService? updateService = _updateService;
        _updateService = null;
        if (updateService != null)
        {
            try { updateService.StateChanged -= OnUpdateStateChanged; }
            catch (Exception exception)
            {
                TADNLog.Log($"TrayAppDotNETAboutPage update detachment failed: {exception.Message}");
            }
        }

        DispatcherTimer? staleTimer = _staleTimer;
        _staleTimer = null;
        if (staleTimer != null)
        {
            try { staleTimer.Stop(); }
            catch (Exception exception)
            {
                TADNLog.Log($"TrayAppDotNETAboutPage timer stop failed: {exception.Message}");
            }
            staleTimer.Tick -= OnStaleTimerTick;
        }

        _updateStatusText = null;
        _checkForUpdatesButton = null;
        _skipUpdateButton = null;
        _installUpdateButton = null;
        _backdateDescriptionText = null;
        _backdateButton = null;
        _previousRelease = null;
        _manualCheckInProgress = false;
        _skipInProgress = false;
        _installInProgress = false;
        _previousReleaseLookupInProgress = false;
        _previousReleaseLookupFailed = false;
        _backdateInProgress = false;
    }

    private void AddUpdatesSection(StackPanel stack, SettingsPalette p)
    {
        ITrayAppDotNETUpdateSettings settings = _options.UpdateSettings!;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(L("Settings_About_Updates_Header", "Updates"), p));
        stack.Children.Add(BoolCard(
            L("Settings_About_CheckForUpdates_Title", "Check for updates automatically"),
            L("Settings_About_CheckForUpdates_Description", "Periodically ask GitHub if a newer release is available."),
            settings.CheckForUpdatesEnabled,
            value => settings.CheckForUpdatesEnabled = value,
            afterSave: _options.RebuildAboutPage));
        stack.Children.Add(BoolCard(
            L("Settings_About_ShowUpdateNotifications_Title", "Show notification for updates"),
            L("Settings_About_ShowUpdateNotifications_Description",
                "Raise a tray notification when a new version is detected and the flyout isn't open."),
            settings.ShowUpdateNotificationsEnabled,
            value => settings.ShowUpdateNotificationsEnabled = value));
        if (_options.SupportsFlyoutUpdateButton)
        {
            stack.Children.Add(BoolCard(
                L("Settings_About_ShowUpdateButton_Title", "Show update button in flyout"),
                L("Settings_About_ShowUpdateButton_Description",
                    "Show update affordances in the flyout while a new version is available."),
                settings.ShowUpdateButtonInFlyout,
                value => settings.ShowUpdateButtonInFlyout = value));
        }

        stack.Children.Add(IntCard(
            L("Settings_About_UpdateInterval_Title", "Update check interval"),
            L("Settings_About_UpdateInterval_Description",
                "How often the background update check runs. The timer resets every time you click Check for updates."),
            Math.Clamp(settings.UpdateCheckIntervalMs / 60_000, 1, 1440),
            1,
            1440,
            minutes => settings.UpdateCheckIntervalMs = minutes * 60_000,
            L("Settings_About_UpdateInterval_MinutesSuffix", " min")));
        stack.Children.Add(BuildUpdateActionCard(p));
        stack.Children.Add(BuildBackdateCard(p));
    }

    private Border BuildUpdateActionCard(SettingsPalette p)
    {
        TrayAppDotNETAboutPageResources.AboutPageAxamlProperties layout = LayoutResources.AxamlAboutPage;
        TextBlock description = TrayAppDotNETSettingsUI.DescriptionText(UpdateStatusText(CurrentService), p);

        SettingsButton check = Button(L("Settings_About_CheckForUpdates_Button", "Check for updates"), p);
        SettingsButton skip = Button(L("Settings_About_SkipUpdate_Button", "Skip this release"), p);
        SettingsButton install = Button(UpdateInstallButtonText(CurrentService), p);
        check.Margin = layout.UpdateCheckButtonMargin;
        skip.Margin = layout.UpdateCheckButtonMargin;

        check.Click += async (_, _) => await CheckForUpdatesAsync();
        skip.Click += async (_, _) => await SkipUpdateAsync();
        install.Click += async (_, _) => await InstallUpdateAsync();

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        StackPanel text = new();
        text.Children.Add(TrayAppDotNETSettingsUI.TitleText(L("Settings_About_UpdateActions_Title", "Update actions"),
            p));
        text.Children.Add(description);
        grid.Children.Add(text);

        StackPanel buttons = TrayAppDotNETSettingsUI.Horizontal(check, skip, install);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        StartUpdateRefresh(description, check, skip, install);
        return TrayAppDotNETSettingsCards.RawCard(grid, p, _options.CardRadius);
    }

    private Border BuildBackdateCard(SettingsPalette p)
    {
        TextBlock description = TrayAppDotNETSettingsUI.DescriptionText(
            L("Settings_About_Backdate_Checking", "Checking GitHub for the previous version..."),
            p);
        SettingsButton backdate = Button(L("Settings_About_Backdate_Button", "Backdate"), p);
        backdate.Click += async (_, _) => await BackdateAsync();

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        StackPanel text = new();
        text.Children.Add(TrayAppDotNETSettingsUI.TitleText(
            L("Settings_About_Backdate_Title", "Backdate app"),
            p));
        text.Children.Add(description);
        grid.Children.Add(text);

        Grid.SetColumn(backdate, 1);
        grid.Children.Add(backdate);

        _backdateDescriptionText = description;
        _backdateButton = backdate;
        _previousRelease = null;
        _previousReleaseLookupFailed = false;
        _previousReleaseLookupInProgress = true;
        RefreshBackdateUI();
        _ = LoadPreviousReleaseAsync(Volatile.Read(ref _refreshGeneration));

        return TrayAppDotNETSettingsCards.RawCard(grid, p, _options.CardRadius);
    }

    private async Task LoadPreviousReleaseAsync(long generation)
    {
        UpdateInfo? previousRelease = null;
        bool lookupFailed = false;
        UpdateCheckService? service = CurrentService;
        if (service == null)
        {
            lookupFailed = true;
        }
        else
        {
            try
            {
                previousRelease = await service.GetPreviousReleaseAsync();
            }
            catch (Exception exception)
            {
                lookupFailed = true;
                _options.Log($"SettingsAboutPage.LoadPreviousRelease: {exception.Message}");
            }
        }

        if (_disposed || generation != Volatile.Read(ref _refreshGeneration)) return;

        _previousRelease = previousRelease;
        _previousReleaseLookupFailed = lookupFailed;
        _previousReleaseLookupInProgress = false;
        RefreshBackdateUI();
    }

    private async Task CheckForUpdatesAsync()
    {
        UpdateCheckService? service = CurrentService;
        if (service == null) return;

        _manualCheckInProgress = true;
        RefreshUpdateUI();
        RefreshBackdateUI();
        try
        {
            await service.CheckNowAsync();
        }
        catch (Exception ex)
        {
            _options.Log($"SettingsAboutPage.CheckForUpdates: {ex.Message}");
        }
        finally
        {
            _manualCheckInProgress = false;
            RefreshUpdateUI();
            RefreshBackdateUI();
        }
    }

    private async Task InstallUpdateAsync()
    {
        UpdateCheckService? service = CurrentService;
        UpdateInfo? info = service?.AvailableUpdate;
        if (service == null || info == null) return;

        Window? owner = _options.PromptOwner();
        if (owner == null)
        {
            _options.Log("SettingsAboutPage.InstallUpdate: update prompt owner unavailable.");
            return;
        }

        _installInProgress = true;
        RefreshUpdateUI();
        RefreshBackdateUI();
        try
        {
            _ = await TrayAppDotNETUpdatePromptPresenter.ShowInstallUpdateAsync(
                new TrayAppDotNETUpdatePromptOptions
                {
                    Owner = owner,
                    Service = service,
                    UpdateInfo = info,
                    Palette = _options.Palette,
                    EnableRoundedCorners = _options.CardRadius.TopLeft > 0,
                    Localize = L,
                    Shutdown = _options.Shutdown,
                    FlushLog = _options.FlushLog,
                    Log = _options.Log,
                    SetDownloadInFlight = inFlight =>
                    {
                        _installInProgress = inFlight;
                        RefreshUpdateUI();
                        RefreshBackdateUI();
                    }
                });
        }
        catch (Exception exception)
        {
            _options.Log($"SettingsAboutPage.InstallUpdate: {exception.Message}");
        }
        finally
        {
            _installInProgress = false;
            RefreshUpdateUI();
            RefreshBackdateUI();
        }
    }

    private async Task BackdateAsync()
    {
        UpdateCheckService? service = CurrentService;
        UpdateInfo? previousRelease = _previousRelease;
        if (service == null || previousRelease == null || _backdateInProgress) return;

        Window? owner = _options.PromptOwner();
        if (owner == null)
        {
            _options.Log("SettingsAboutPage.Backdate: update prompt owner unavailable.");
            return;
        }

        _backdateInProgress = true;
        RefreshUpdateUI();
        RefreshBackdateUI();
        try
        {
            _ = await TrayAppDotNETUpdatePromptPresenter.ShowBackdateAsync(
                new TrayAppDotNETUpdatePromptOptions
                {
                    Owner = owner,
                    Service = service,
                    UpdateInfo = previousRelease,
                    Palette = _options.Palette,
                    EnableRoundedCorners = _options.CardRadius.TopLeft > 0,
                    Localize = L,
                    Shutdown = _options.Shutdown,
                    FlushLog = _options.FlushLog,
                    Log = _options.Log,
                    SetDownloadInFlight = inFlight =>
                    {
                        _backdateInProgress = inFlight;
                        RefreshUpdateUI();
                        RefreshBackdateUI();
                    }
                });
        }
        catch (Exception exception)
        {
            _options.Log($"SettingsAboutPage.Backdate: {exception.Message}");
        }
        finally
        {
            _backdateInProgress = false;
            RefreshUpdateUI();
            RefreshBackdateUI();
        }
    }

    private async Task SkipUpdateAsync()
    {
        UpdateCheckService? service = CurrentService;
        UpdateInfo? info = service?.AvailableUpdate;
        if (service == null || info == null || _skipInProgress) return;

        _skipInProgress = true;
        RefreshUpdateUI();
        RefreshBackdateUI();
        try
        {
            await service.SkipReleaseAsync(info);
        }
        catch (Exception ex)
        {
            _options.Log($"SettingsAboutPage.SkipUpdate: {ex.Message}");
        }
        finally
        {
            _skipInProgress = false;
            RefreshUpdateUI();
            RefreshBackdateUI();
        }
    }

    private void StartUpdateRefresh(
        TextBlock statusText,
        SettingsButton checkButton,
        SettingsButton skipButton,
        SettingsButton installButton)
    {
        StopUpdateRefresh();
        Interlocked.Increment(ref _refreshGeneration);

        _updateStatusText = statusText;
        _checkForUpdatesButton = checkButton;
        _skipUpdateButton = skipButton;
        _installUpdateButton = installButton;
        _updateService = _options.UpdateService();
        if (_updateService != null)
            _updateService.StateChanged += OnUpdateStateChanged;

        _staleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_options.StaleCheckTimerIntervalMs) };
        _staleTimer.Tick += OnStaleTimerTick;
        try { _staleTimer.Start(); }
        catch
        {
            StopUpdateRefresh();
            throw;
        }

        RefreshUpdateUI();
    }

    private void SetRoot(StackPanel? root)
    {
        if (_root != null)
            _root.DetachedFromVisualTree -= OnRootDetachedFromVisualTree;

        _root = root;

        if (_root != null)
            _root.DetachedFromVisualTree += OnRootDetachedFromVisualTree;
    }

    private void OnRootDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        StopUpdateRefresh();
        if (ReferenceEquals(sender, _root))
            SetRoot(null);
    }

    private void OnUpdateStateChanged()
    {
        long generation = Volatile.Read(ref _refreshGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || generation != Volatile.Read(ref _refreshGeneration)) return;
            RefreshUpdateUI();
            RefreshBackdateUI();
        });
    }

    private void OnStaleTimerTick(object? sender, EventArgs e)
    {
        // A retired page timer may already have queued a tick before it was stopped
        if (_disposed || !ReferenceEquals(sender, _staleTimer)) return;
        RefreshUpdateUI();
    }

    private void RefreshUpdateUI()
    {
        if (_disposed) return;
        if (_updateStatusText == null || _checkForUpdatesButton == null || _skipUpdateButton == null
            || _installUpdateButton == null)
            return;

        UpdateCheckService? service = CurrentService;
        bool isUpdateAvailable = service?.AvailableUpdate != null;
        _updateStatusText.Text = UpdateStatusText(service);
        _checkForUpdatesButton.IsEnabled = service != null && !_manualCheckInProgress && !_skipInProgress
            && !_installInProgress && !_backdateInProgress;
        _skipUpdateButton.IsVisible = isUpdateAvailable;
        _skipUpdateButton.IsEnabled = isUpdateAvailable && !_manualCheckInProgress && !_skipInProgress
            && !_installInProgress && !_backdateInProgress;
        _installUpdateButton.Text = UpdateInstallButtonText(service);
        _installUpdateButton.IsEnabled = isUpdateAvailable && !_skipInProgress && !_installInProgress
            && !_backdateInProgress;
    }

    private void RefreshBackdateUI()
    {
        if (_disposed || _backdateDescriptionText == null || _backdateButton == null) return;

        if (_previousReleaseLookupInProgress)
        {
            _backdateDescriptionText.Text = L(
                "Settings_About_Backdate_Checking",
                "Checking GitHub for the previous version...");
        }
        else if (_previousReleaseLookupFailed)
        {
            _backdateDescriptionText.Text = L(
                "Settings_About_Backdate_Failed",
                "The previous release could not be checked.");
        }
        else if (_previousRelease is { } previousRelease)
        {
            _backdateDescriptionText.Text = string.Format(
                CultureInfo.CurrentCulture,
                L(
                    "Settings_About_Backdate_AvailableFormat",
                    "Previous available version: {0}. Installing it will restart the app."),
                previousRelease.Version);
        }
        else
        {
            _backdateDescriptionText.Text = L(
                "Settings_About_Backdate_None",
                "No previous release is available for this app.");
        }

        _backdateButton.IsEnabled = _previousRelease != null
            && !_previousReleaseLookupInProgress
            && !_manualCheckInProgress
            && !_skipInProgress
            && !_installInProgress
            && !_backdateInProgress;
    }

    private UpdateCheckService? CurrentService => _updateService ?? _options.UpdateService();

    private string UpdateStatusText(UpdateCheckService? service)
    {
        if (service == null) return L("Settings_About_UpdateStatus_Unavailable", "Update service is not available.");
        if (service.IsChecking) return L("Settings_About_UpdateStatus_Checking", "Checking for updates...");
        if (service.AvailableUpdate is { } update)
        {
            return string.Format(CultureInfo.CurrentCulture,
                L("Settings_About_UpdateStatus_AvailableFormat", "Update available: {0}"), update.ReleaseName);
        }

        if (service.LastCheckTimeUtc == null)
            return L("Settings_About_UpdateStatus_NeverChecked", "No update check has run yet.");
        if (service.LastResult == UpdateCheckResult.Failed)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                L("Settings_About_UpdateStatus_FailedFormat", "Update check failed. Last tried {0}."),
                FormatRelativeTimestamp(service.LastCheckTimeUtc.Value));
        }

        if (service.SkippedUpdateVersion > 0)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                L("Settings_About_UpdateStatus_SkippedFormat",
                    "Release {0} skipped. You'll be notified when a newer release is available."),
                service.SkippedUpdateVersion);
        }

        return string.Format(CultureInfo.CurrentCulture, service.LastResult == UpdateCheckResult.Cancelled
            ? L("Settings_About_UpdateStatus_CancelledFormat", "Update check was canceled {0}.")
            : L("Settings_About_UpdateStatus_LastCheckedFormat", "You're up to date. Last checked {0}."), FormatRelativeTimestamp(service.LastCheckTimeUtc.Value));
    }

    private string UpdateInstallButtonText(UpdateCheckService? service)
    {
        if (service?.AvailableUpdate != null) return L("Settings_About_InstallUpdate_Available", "Install update");
        if (service?.LastResult == UpdateCheckResult.Failed)
            return L("Settings_About_InstallUpdate_CheckFailed", "Check failed");
        if (service != null && ComputeStaleness(service))
            return L("Settings_About_InstallUpdate_Stale", "Version stale");
        return L("Settings_About_InstallUpdate_UpToDate", "Up to date");
    }

    private bool ComputeStaleness(UpdateCheckService service)
    {
        if (service.LastCheckTimeUtc is not { } last || _options.UpdateSettings == null) return false;
        TimeSpan threshold = TimeSpan.FromMilliseconds(
            _options.UpdateSettings.UpdateCheckIntervalMs + _options.UpdateStaleGraceMs);
        return DateTime.UtcNow - last > threshold;
    }

    private string FormatRelativeTimestamp(DateTime utc)
    {
        TimeSpan diff = DateTime.UtcNow - utc;
        if (diff < TimeSpan.FromMilliseconds(TimeConstants.RelativeTimestampJustNowThresholdMs))
            return L("Settings_About_RelativeTime_JustNow", "just now");
        if (diff < TimeSpan.FromMilliseconds(TimeConstants.RelativeTimestampMinutesThresholdMs))
        {
            return string.Format(CultureInfo.CurrentCulture, L("Settings_About_RelativeTime_MinutesFormat", "{0}m ago"),
                Math.Max(1, (int)diff.TotalMinutes));
        }

        if (diff < TimeSpan.FromMilliseconds(TimeConstants.RelativeTimestampHoursThresholdMs))
        {
            return string.Format(CultureInfo.CurrentCulture, L("Settings_About_RelativeTime_HoursFormat", "{0}h ago"),
                Math.Max(1, (int)diff.TotalHours));
        }

        return string.Format(CultureInfo.CurrentCulture, L("Settings_About_RelativeTime_DaysFormat", "{0}d ago"),
            Math.Max(1, (int)diff.TotalDays));
    }

    private Border BoolCard(string title, string description, bool value, Action<bool> set, Action? afterSave = null) =>
        TrayAppDotNETSettingsCards.BoolCard(
            title,
            description,
            value,
            set,
            _options.Palette,
            _options.CardRadius,
            _options.Save,
            afterSave);

    private Border IntCard(string title, string description, int value, int min, int max, Action<int> set,
        string suffix) =>
        TrayAppDotNETSettingsCards.IntCard(
            title,
            description,
            value,
            min,
            max,
            set,
            _options.Palette,
            _options.CardRadius,
            _options.Save,
            suffix);

    private SettingsButton Button(string text, SettingsPalette palette) =>
        TrayAppDotNETSettingsCards.Button(text, palette, _options.ButtonRadius);

    private Border BuildKnownIssueCard(string title, string description, SettingsPalette p)
    {
        StackPanel issue = new();
        issue.Children.Add(TrayAppDotNETSettingsUI.TitleText(title, p));
        issue.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(description, p));
        return TrayAppDotNETSettingsCards.RawCard(issue, p, _options.CardRadius);
    }

    private static StackPanel AboutRow(string label, string value, SettingsPalette p, string? openUrl = null)
    {
        TrayAppDotNETAboutPageResources.AboutPageAxamlProperties layout = LayoutResources.AxamlAboutPage;
        TextBlock labelBlock = TrayAppDotNETSettingsUI.Text(label, p, layout.AboutRowLabelFontSize, FontWeight.SemiBold);
        labelBlock.Width = layout.AboutRowLabelWidth;

        TextBlock valueBlock = TrayAppDotNETSettingsUI.Text(value, p);
        valueBlock.TextWrapping = TextWrapping.Wrap;
        if (!string.IsNullOrEmpty(openUrl))
        {
            valueBlock.TextDecorations = TextDecorations.Underline;
            valueBlock.Cursor = TrayAppDotNETCursors.Hand;
            valueBlock.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(valueBlock).Properties.IsLeftButtonPressed) return;
                using Process? process = Process.Start(new ProcessStartInfo(openUrl) { UseShellExecute = true });
                e.Handled = true;
            };
        }

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = layout.AboutRowMargin,
            Children = { labelBlock, valueBlock }
        };
    }

    private string L(string key, string fallback) => _options.Localize(key, fallback);
}
