using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI;

public enum TaskManagerPage
{
    Processes,
    Performance,
    AppHistory,
    StartupApps,
    Users,
    Details,
    Services,
    Settings
}

/// <summary>The Task Manager shell, built on the shared TrayAppDotNET settings-window chrome.</summary>
internal sealed class TaskManagerWindow : SettingsWindowCommon<TaskManagerPage>
{
    private static readonly Glyph ProcessesGlyph = Glyph.Fluent("\uECAA");
    private static readonly Glyph PerformanceGlyph = Glyph.Fluent("\uE9D9");
    private static readonly Glyph AppHistoryGlyph = Glyph.Fluent("\uE81C");
    private static readonly Glyph StartupAppsGlyph = Glyph.Fluent("\uE768");
    private static readonly Glyph UsersGlyph = Glyph.Fluent("\uE716");
    private static readonly Glyph DetailsGlyph = Glyph.Fluent("\uE8FD");
    private static readonly Glyph ServicesGlyph = Glyph.Fluent("\uEA86");

    private readonly AppSettings _settings;
    private readonly AppTheme _theme;
    private readonly ProcessSnapshotService _snapshotService;
    private readonly ProcessIconService _processIconService;
    private readonly ProcessTerminationService _processTerminationService;
    private readonly TaskManagerWindowResources _taskManagerResources = new();
    private TaskManagerSettingsWindow? _settingsWindow;
    private bool _allowClose;

    public TaskManagerWindow(
        AppSettings settings,
        AppTheme theme,
        ProcessSnapshotService snapshotService,
        ProcessIconService processIconService,
        ProcessTerminationService processTerminationService)
    {
        _settings = settings;
        _theme = theme;
        _snapshotService = snapshotService;
        _processIconService = processIconService;
        _processTerminationService = processTerminationService;
        Resources.MergedDictionaries.Add(_taskManagerResources);

        ConfigureSettingsWindow(Constants.DisplayName, icon: null);
        Width = _taskManagerResources.AxamlTaskManagerWindow.Width;
        Height = _taskManagerResources.AxamlTaskManagerWindow.Height;
        MinWidth = _taskManagerResources.AxamlTaskManagerWindow.MinWidth;
        MinHeight = _taskManagerResources.AxamlTaskManagerWindow.MinHeight;
        Closing += OnWindowClosing;
        InitializeSettingsShell();
    }

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;
    protected override bool UseWindows11SettingsNavigation => true;
    protected override bool ShowSettingsSearchBox => false;
    protected override bool IsFooterNavigationPage(TaskManagerPage pageKey) => pageKey == TaskManagerPage.Settings;
    protected override bool PageOwnsScrolling(TaskManagerPage pageKey) => pageKey == TaskManagerPage.Details;
    protected override bool HandleNavigationRequest(TaskManagerPage pageKey)
    {
        if (pageKey != TaskManagerPage.Settings) return false;
        ShowClassicSettingsWindow();
        return true;
    }
    protected override Thickness ContentPadding => default;
    protected override double SidebarWidth => _taskManagerResources.AxamlTaskManagerWindow.SidebarWidth;
    protected override TaskManagerPage DefaultPageKey => TaskManagerPage.Details;
    protected override string HeaderText => Constants.DisplayName;
    protected override string OpenSettingsFolderText => "Open Task Manager settings folder";
    protected override string SettingsFolderPath => AppSettings.GetDefaultDirectory();

    protected override SettingsPalette ResolvePalette() =>
        VolumeSettingsPalette.Create(_theme, _settings, ResolveEffectiveIsLight());

    protected override bool ResolveEffectiveIsLightForBindings() => ResolveEffectiveIsLight();

    protected override IReadOnlyList<SettingsPageDescriptor<TaskManagerPage>> CreatePageDescriptors() =>
    [
        new(TaskManagerPage.Processes, "Processes", () => BuildPlaceholderPage("Processes"), ProcessesGlyph),
        new(TaskManagerPage.Performance, "Performance", () => BuildPlaceholderPage("Performance"), PerformanceGlyph),
        new(TaskManagerPage.AppHistory, "App history", () => BuildPlaceholderPage("App history"), AppHistoryGlyph),
        new(TaskManagerPage.StartupApps, "Startup apps", () => BuildPlaceholderPage("Startup apps"), StartupAppsGlyph),
        new(TaskManagerPage.Users, "Users", () => BuildPlaceholderPage("Users"), UsersGlyph),
        new(TaskManagerPage.Details, "Details", BuildDetailsPage, DetailsGlyph),
        new(TaskManagerPage.Services, "Services", () => BuildPlaceholderPage("Services"), ServicesGlyph),
        new(TaskManagerPage.Settings, "Settings", BuildSettingsPage, SettingsNavigationGlyphs.Settings)
    ];

    protected override void Save() => _settings.Save();

    protected override void OnSettingsWindowClosed()
    {
        Closing -= OnWindowClosing;
        base.OnSettingsWindowClosed();
    }

    /// <summary>Rebuilds the shared shell after the app theme or settings change.</summary>
    internal void RefreshTheme() => RebuildShell(CurrentPageKey);

    /// <summary>Rebuilds the Details drawing DAG after its visible schema or order changes.</summary>
    internal void RefreshDetailsColumns()
    {
        if (CurrentPageKey == TaskManagerPage.Details)
            RebuildShell(TaskManagerPage.Details);
    }

    /// <summary>Allows app shutdown to close the otherwise warm, hide-on-close window.</summary>
    internal void RequestPermanentClose()
    {
        _allowClose = true;
        if (_settingsWindow != null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            _settingsWindow.Close();
            _settingsWindow = null;
        }
        Close();
    }

    private void ShowClassicSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.ShowAtDefaultPositionAndActivate();
            return;
        }

        _settingsWindow = new TaskManagerSettingsWindow(_settings, ShowUninstallerWindow);
        _settingsWindow.Closed += OnSettingsWindowClosed;
        _settingsWindow.ShowAtDefaultPositionAndActivate();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is TaskManagerSettingsWindow settingsWindow)
            settingsWindow.Closed -= OnSettingsWindowClosed;
        if (ReferenceEquals(sender, _settingsWindow))
            _settingsWindow = null;
    }

    private void ShowUninstallerWindow(string installDirectory, InstallScope scope)
    {
        TaskManagerUninstallerWindow uninstaller = new(installDirectory, scope);
        Window owner = _settingsWindow != null ? _settingsWindow : this;
        uninstaller.Show(owner);
    }

    private Control BuildDetailsPage()
    {
        ProcessDetailsPage page = new(
            _snapshotService,
            _processIconService,
            _settings,
            Palette,
            _taskManagerResources,
            _processTerminationService.Arm,
            TryTerminateProcess,
            ReportMessage,
            StartProcess);
        return OwnPageResource(page);
    }

    private StackPanel BuildSettingsPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack("Settings", palette);
        stack.Margin = _taskManagerResources.AxamlTaskManagerDetails.PlaceholderMargin;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Details", palette));
        stack.Children.Add(BoolCard(
            "Live column resizing",
            "Update Details column widths and positions while dragging a divider. Turn this off to show a resize guide and apply the width on release.",
            _settings.EnableLiveDetailsColumnResizing,
            enabled => _settings.EnableLiveDetailsColumnResizing = enabled,
            palette));
        return stack;
    }

    private StackPanel BuildPlaceholderPage(string pageName)
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(pageName, palette);
        stack.Margin = _taskManagerResources.AxamlTaskManagerDetails.PlaceholderMargin;
        TextBlock description = TrayAppDotNETSettingsUI.DescriptionText(
            "This page is intentionally a shell in the initial implementation.",
            palette);
        stack.Children.Add(RawCard(description, palette));
        return stack;
    }

    private bool TryTerminateProcess(ProcessTerminationTarget target, out string errorMessage) =>
        _processTerminationService.TryTerminate(target, out errorMessage);

    private void ReportMessage(string title, string message) => _ = ShowMessage(title, message);

    private bool StartProcess(string command)
    {
        if (CriticalProcessActions.TryStart(command, out string errorMessage)) return true;

        _ = ShowMessage("Run new task failed", errorMessage);
        return false;
    }

    private bool ResolveEffectiveIsLight() => _settings.ThemeMode switch
    {
        TrayAppDotNETThemeMode.Light => true,
        TrayAppDotNETThemeMode.Dark => false,
        _ => _theme.IsLightTheme
    };

    private void OnWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_allowClose || Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
            return;

        eventArgs.Cancel = true;
        Hide();
    }
}
