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
    private readonly TaskManagerWindowResources _taskManagerResources = new();
    private bool _allowClose;

    public TaskManagerWindow(
        AppSettings settings,
        AppTheme theme,
        ProcessSnapshotService snapshotService,
        ProcessIconService processIconService)
    {
        _settings = settings;
        _theme = theme;
        _snapshotService = snapshotService;
        _processIconService = processIconService;
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
        new(TaskManagerPage.Settings, "Settings", () => BuildPlaceholderPage("Settings"), SettingsNavigationGlyphs.Settings)
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
        Close();
    }

    private Control BuildDetailsPage()
    {
        ProcessDetailsPage page = new(
            _snapshotService,
            _processIconService,
            _settings,
            Palette,
            _taskManagerResources,
            TerminateProcess,
            StartProcess);
        return OwnPageResource(page);
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

    private bool TerminateProcess(int processID)
    {
        if (CriticalProcessActions.TryTerminate(processID, out string errorMessage)) return true;

        _ = ShowMessage("End task failed", errorMessage);
        return false;
    }

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
