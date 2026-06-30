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
    public Action<string> Log { get; init; } = static _ => { };
    public Action? RebuildAboutPage { get; init; }
    public int StaleCheckTimerIntervalMs { get; init; } = TimeConstants.AboutStaleCheckTimerIntervalMs;
    public int UpdateStaleGraceMs { get; init; } = TimeConstants.UpdateStaleGraceMs;

    private static void ShutdownDesktopApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}

public sealed class TrayAppDotNETAboutPage
{
    private readonly TrayAppDotNETAboutPageOptions _options;
    private UpdateCheckService? _updateService;
    private DispatcherTimer? _staleTimer;
    private TextBlock? _updateStatusText;
    private SettingsButton? _checkForUpdatesButton;
    private SettingsButton? _installUpdateButton;
    private bool _manualCheckInProgress;
    private bool _installInProgress;

    public TrayAppDotNETAboutPage(TrayAppDotNETAboutPageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Localize);
        ArgumentNullException.ThrowIfNull(options.Save);
        _options = options;
    }

    public StackPanel Build()
    {
        StopUpdateRefresh();

        SettingsPalette p = _options.Palette;
        StackPanel stack = TrayAppDotNETSettingsCards.PageStack(L("Settings_About_SectionHeader", "About"), p);

        TextBlock appName = TrayAppDotNETSettingsUI.Text(_options.ApplicationName, p);
        appName.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Add(appName);

        TextBlock tagline = TrayAppDotNETSettingsUI.DescriptionText(_options.Tagline, p, new Thickness(0, 0, 0, 24));
        tagline.Opacity = 0.7;
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

    public void StopUpdateRefresh()
    {
        if (_updateService != null)
        {
            _updateService.StateChanged -= OnUpdateStateChanged;
            _updateService = null;
        }

        if (_staleTimer != null)
        {
            _staleTimer.Stop();
            _staleTimer.Tick -= OnStaleTimerTick;
            _staleTimer = null;
        }

        _updateStatusText = null;
        _checkForUpdatesButton = null;
        _installUpdateButton = null;
        _manualCheckInProgress = false;
        _installInProgress = false;
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
        stack.Children.Add(BoolCard(
            L("Settings_About_ShowUpdateButton_Title", "Show update button in flyout"),
            L("Settings_About_ShowUpdateButton_Description",
                "Show update affordances in the flyout while a new version is available."),
            settings.ShowUpdateButtonInFlyout,
            value => settings.ShowUpdateButtonInFlyout = value));
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
    }

    private Border BuildUpdateActionCard(SettingsPalette p)
    {
        TextBlock description = TrayAppDotNETSettingsUI.DescriptionText(UpdateStatusText(CurrentService), p);

        SettingsButton check = Button(L("Settings_About_CheckForUpdates_Button", "Check for updates"), p);
        SettingsButton install = Button(UpdateInstallButtonText(CurrentService), p);
        check.Margin = new Thickness(0, 0, 8, 0);

        check.Click += async (_, _) => await CheckForUpdatesAsync();
        install.Click += async (_, _) => await InstallUpdateAsync();

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        StackPanel text = new();
        text.Children.Add(TrayAppDotNETSettingsUI.TitleText(L("Settings_About_UpdateActions_Title", "Update actions"),
            p));
        text.Children.Add(description);
        grid.Children.Add(text);

        StackPanel buttons = TrayAppDotNETSettingsUI.Horizontal(check, install);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        StartUpdateRefresh(description, check, install);
        return TrayAppDotNETSettingsCards.RawCard(grid, p, _options.CardRadius);
    }

    private async Task CheckForUpdatesAsync()
    {
        UpdateCheckService? service = CurrentService;
        if (service == null) return;

        _manualCheckInProgress = true;
        RefreshUpdateUI();
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
        }
    }

    private async Task InstallUpdateAsync()
    {
        UpdateCheckService? service = CurrentService;
        UpdateInfo? info = service?.AvailableUpdate;
        if (service == null || info == null) return;

        bool ok = await _options.ConfirmAsync(
            string.Format(CultureInfo.CurrentCulture, L("UpdateDialog_TitleFormat", "Install {0}?"), info.ReleaseName),
            string.IsNullOrWhiteSpace(info.Changelog)
                ? L("UpdateDialog_NoChangelog", "No changelog was provided for this release.")
                : info.Changelog,
            L("UpdateDialog_Install", "Install"),
            L("UpdateDialog_Cancel", "Cancel"));
        if (!ok) return;

        _installInProgress = true;
        RefreshUpdateUI();

        bool staged = false;
        try
        {
            staged = await service.DownloadAndStageAsync(info);
        }
        catch (Exception ex)
        {
            _options.Log($"SettingsAboutPage.InstallUpdate: {ex.Message}");
        }

        if (staged)
        {
            TADNLog.Flush();
            _options.Shutdown();
        }
        else
        {
            _installInProgress = false;
            RefreshUpdateUI();
        }
    }

    private void StartUpdateRefresh(TextBlock statusText, SettingsButton checkButton, SettingsButton installButton)
    {
        StopUpdateRefresh();

        _updateStatusText = statusText;
        _checkForUpdatesButton = checkButton;
        _installUpdateButton = installButton;
        _updateService = _options.UpdateService();
        if (_updateService != null)
            _updateService.StateChanged += OnUpdateStateChanged;

        _staleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_options.StaleCheckTimerIntervalMs), };
        _staleTimer.Tick += OnStaleTimerTick;
        _staleTimer.Start();

        RefreshUpdateUI();
    }

    private void OnUpdateStateChanged() =>
        Dispatcher.UIThread.Post(RefreshUpdateUI);

    private void OnStaleTimerTick(object? sender, EventArgs e) =>
        RefreshUpdateUI();

    private void RefreshUpdateUI()
    {
        if (_updateStatusText == null || _checkForUpdatesButton == null || _installUpdateButton == null)
            return;

        UpdateCheckService? service = CurrentService;
        _updateStatusText.Text = UpdateStatusText(service);
        _checkForUpdatesButton.IsEnabled = service != null && !_manualCheckInProgress && !_installInProgress;
        _installUpdateButton.Text = UpdateInstallButtonText(service);
        _installUpdateButton.IsEnabled = service?.AvailableUpdate != null && !_installInProgress;
    }

    private UpdateCheckService? CurrentService => _updateService ?? _options.UpdateService();

    private string UpdateStatusText(UpdateCheckService? service)
    {
        if (service == null) return L("Settings_About_UpdateStatus_Unavailable", "Update service is not available.");
        if (service.IsChecking) return L("Settings_About_UpdateStatus_Checking", "Checking for updates...");
        if (service.AvailableUpdate is { } update)
            return string.Format(CultureInfo.CurrentCulture,
                L("Settings_About_UpdateStatus_AvailableFormat", "Update available: {0}"), update.ReleaseName);
        if (service.LastCheckTimeUtc == null)
            return L("Settings_About_UpdateStatus_NeverChecked", "No update check has run yet.");
        if (service.LastResult == UpdateCheckResult.Failed)
            return string.Format(
                CultureInfo.CurrentCulture,
                L("Settings_About_UpdateStatus_FailedFormat", "Update check failed. Last tried {0}."),
                FormatRelativeTimestamp(service.LastCheckTimeUtc.Value));
        if (service.LastResult == UpdateCheckResult.Cancelled)
            return string.Format(
                CultureInfo.CurrentCulture,
                L("Settings_About_UpdateStatus_CancelledFormat", "Update check was canceled {0}."),
                FormatRelativeTimestamp(service.LastCheckTimeUtc.Value));
        return string.Format(
            CultureInfo.CurrentCulture,
            L("Settings_About_UpdateStatus_LastCheckedFormat", "You're up to date. Last checked {0}."),
            FormatRelativeTimestamp(service.LastCheckTimeUtc.Value));
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
            return string.Format(CultureInfo.CurrentCulture, L("Settings_About_RelativeTime_MinutesFormat", "{0}m ago"),
                Math.Max(1, (int)diff.TotalMinutes));
        if (diff < TimeSpan.FromMilliseconds(TimeConstants.RelativeTimestampHoursThresholdMs))
            return string.Format(CultureInfo.CurrentCulture, L("Settings_About_RelativeTime_HoursFormat", "{0}h ago"),
                Math.Max(1, (int)diff.TotalHours));
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
        TextBlock labelBlock = TrayAppDotNETSettingsUI.Text(label, p, 14, FontWeight.SemiBold);
        labelBlock.Width = 80;

        TextBlock valueBlock = TrayAppDotNETSettingsUI.Text(value, p);
        valueBlock.TextWrapping = TextWrapping.Wrap;
        if (!string.IsNullOrEmpty(openUrl))
        {
            valueBlock.TextDecorations = TextDecorations.Underline;
            valueBlock.Cursor = new Cursor(StandardCursorType.Hand);
            valueBlock.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(valueBlock).Properties.IsLeftButtonPressed) return;
                Process.Start(new ProcessStartInfo(openUrl) { UseShellExecute = true });
                e.Handled = true;
            };
        }

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6),
            Children = { labelBlock, valueBlock },
        };
    }

    private string L(string key, string fallback) => _options.Localize(key, fallback);
}
