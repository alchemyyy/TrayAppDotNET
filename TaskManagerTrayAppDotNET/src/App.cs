using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using TaskManagerTrayAppDotNET.UI.Tray;
using TrayAppDotNETCommon.Visuals;
#if HOTAVALONIA_ENABLE
using HotAvalonia;
#endif

namespace TaskManagerTrayAppDotNET;

internal static class TaskManagerAvaloniaRunner
{
    public static int Run(string[] args) =>
        TrayAppDotNETAppRunner.Run<TaskManagerAvaloniaApp>(args, AppSettings.GetDefaultPath, configure: Configure);

    private static AppBuilder Configure(AppBuilder builder)
    {
#if HOTAVALONIA_ENABLE
        builder = builder.UseHotReload();
#endif
        return builder;
    }
}

internal sealed class TaskManagerAvaloniaApp : Application
{
    private TrayAppDotNETAppHost<AppSettings, AppTheme>? _host;
    private AppSettings? _settings;
    private AppTheme? _theme;
    private ProcessIconService? _processIconService;
    private ProcessSnapshotService? _snapshotService;
    private PerformanceSnapshotService? _performanceSnapshotService;
    private ProcessTerminationService? _processTerminationService;
    private WindowsTaskManagerHotkeyOverride? _windowsTaskManagerHotkeyOverride;
    private TaskManagerActivationServer? _taskManagerActivationServer;
    private ElevatedActionCoordinator? _elevatedActionCoordinator;
    private TaskManagerWindow? _taskManagerWindow;
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private TrayWindowToggle? _trayToggle;
    private TaskManagerTrayIcon? _trayIconRenderer;
    private TrayIconPresenter<TaskManagerTrayIconRenderInput>? _trayIconPresenter;
    private TrayMenuController? _trayMenu;
    private bool _shuttingDown;

    public override void Initialize() => TrayAppDotNETAvalonia.InitializeDefaults(this);

    public override void OnFrameworkInitializationCompleted()
    {
        TrayAppDotNETAppHost<AppSettings, AppTheme> host = new(this, CreateHostOptions());
        _host = host;
        AppServices.Host = host;
        host.InitializeShell();
        if (host.TryRunAuxiliaryMode())
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        host.BeginTrayMode(ConfigureSettings, ConfigureTheme);
        AppSettings settings = _settings
                               ?? throw new InvalidOperationException("Task Manager settings were not loaded.");
        _processIconService = new ProcessIconService();
        _processTerminationService = new ProcessTerminationService(TADNLog.Log);
        _ = _processTerminationService.EnsureStandardHelperAsync();
        _snapshotService = new ProcessSnapshotService();
        _snapshotService.SnapshotAvailable += OnSystemPerformanceSampleAvailable;
        _performanceSnapshotService = new PerformanceSnapshotService(
            settings.PerformanceSampleIntervalMilliseconds,
            PerformanceSamplingSettings.CalculateMaximumHistoryCount(
                settings.PerformanceHistoryLengthMinutes,
                settings.PerformanceSampleIntervalMilliseconds),
            settings.ShowMemoryModuleSerialNumbers,
            _snapshotService);
        _trayIconRenderer = new TaskManagerTrayIcon();
        CreateTaskManagerWindow();
        _windowsTaskManagerHotkeyOverride = new WindowsTaskManagerHotkeyOverride(
            () => Dispatcher.UIThread.Post(ShowTaskManagerFromShortcut),
            TADNLog.Log);
        _windowsTaskManagerHotkeyOverride.SetEnabled(settings.OverrideWindowsTaskManagerHotkey);
        // Answer hand-off requests from taskmgr.exe launches redirected here while this instance runs
        _taskManagerActivationServer = new TaskManagerActivationServer(
            () => Dispatcher.UIThread.Post(ShowTaskManagerFromShortcut),
            TADNLog.Log);
        _taskManagerActivationServer.Start();
        // Runs priority/affinity on protected processes through the elevated broker, consented once on demand
        _elevatedActionCoordinator = new ElevatedActionCoordinator(ConfirmEnableElevatedActionsAsync, TADNLog.Log);
        AppServices.ElevatedActions = _elevatedActionCoordinator;
        _snapshotService.Start();
        _performanceSnapshotService.Start();
        CreateTrayIcon();
        host.StartCommonServices(new TrayAppDotNETAppHostStartOptions
        {
            ShutdownApp = ShutdownServices,
            ShowUpdateNotification = (title, body) => _trayIcon?.ShowBalloon(title, body)
        });
        AppServices.UpdateCheckService = host.UpdateService;
        if (host.UpdateService != null) host.UpdateService.StateChanged += OnUpdateStateChanged;
        TaskManagerWindow taskManagerWindow = _taskManagerWindow!;
        if (TrayAppDotNETProgram.IsHiddenLaunch)
        {
            // The installer starts the app this way when it finishes, so it waits in the tray until it is opened
            taskManagerWindow.StartHidden();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (settings.StartMinimized && TrayAppDotNETProgram.IsStartupLaunch)
        {
            // Only sign-in launches honor the setting; a manual start always shows the window
            taskManagerWindow.StartMinimized();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        Task firstFrameReveal = taskManagerWindow.ShowAtDefaultPositionAndActivateAfterFirstFrameAsync();
        base.OnFrameworkInitializationCompleted();
        _ = StartInitialElevationAfterWindowRevealAsync(taskManagerWindow, firstFrameReveal);
    }

    private async Task StartInitialElevationAfterWindowRevealAsync(
        TaskManagerWindow taskManagerWindow,
        Task firstFrameReveal)
    {
        try
        {
            await firstFrameReveal;
            if (_shuttingDown || !ReferenceEquals(_taskManagerWindow, taskManagerWindow)) return;
            taskManagerWindow.StartInitialElevatedTerminationAttempt();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Task Manager initial window reveal failed: {exception}");
        }
    }

    private static TrayAppDotNETAppHostOptions<AppSettings, AppTheme> CreateHostOptions() =>
        new()
        {
            Install = AppServices.Install,
            Strings = AppStrings.ResourceManager,
            SetStringsCulture = static culture => AppStrings.Culture = culture,
            AvailableLanguages = AppLocalizationCultures.Names,
            GetSettingsPath = AppSettings.GetDefaultPath,
            LoadSettings = AppSettings.LoadOrDefault,
            ReadSettingsWithoutSaving = AppSettingsCommon.ReadOrDefault<AppSettings>,
            CreateDefaultSettings = static () => new AppSettings(),
            GetThemePath = AppThemeStore.GetDefaultPath,
            LoadTheme = AppThemeStore.LoadOrDefault,
            LoadAppIcon = static () => AppServices.AppIcon.LoadWindowIcon(),
            CreatePalette = static (theme, settings) =>
                AppSettingsPalette.Create(theme, settings, AppTheme.ResolveIsLight(settings, theme)),
            SaveSettings = static settings => settings.Save(),
            SaveSettingsOnExit = static settings => settings.SaveOnExit()
        };

    private void ConfigureSettings(AppSettings settings)
    {
        _settings = settings;
        settings.Changed += OnSettingsChanged;
        AppServices.Settings = settings;
    }

    private void ConfigureTheme(AppTheme theme)
    {
        _theme = theme;
        theme.ThemeChanged += OnThemeChanged;
        AppServices.Theme = theme;
        ApplyThemeVariant();
    }

    private void CreateTaskManagerWindow()
    {
        if (_settings == null ||
            _theme == null ||
            _processIconService == null ||
            _snapshotService == null ||
            _performanceSnapshotService == null ||
            _processTerminationService == null)
            throw new InvalidOperationException("Task Manager services must be loaded before creating the window.");

        _taskManagerWindow = new TaskManagerWindow(
            _settings,
            _theme,
            _snapshotService,
            _performanceSnapshotService,
            _processIconService,
            _processTerminationService,
            ExitApplication);
    }

    private void CreateTrayIcon()
    {
        _trayToggle = new TrayWindowToggle();
        _trayIcon = new TrayAppDotNETShellTrayIcon(
            Constants.TrayIconGUID,
            Program.ApplicationName + ".TrayIcon");
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.LeftDoubleClick += RevealTaskManagerFromTray;

        // The classic notification area may report one Enter twice, so the keyboard only reveals the window
        _trayIcon.KeySelect += RevealTaskManagerFromTray;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.RefreshNeeded += RefreshTrayIcon;
        _trayIcon.BalloonClicked += OnUpdateBalloonClicked;

        // The live graph changes with every sample, so every refresh renders
        _trayIconPresenter = new TrayIconPresenter<TaskManagerTrayIconRenderInput>(
            _trayIcon,
            _trayIconRenderer,
            static () => AppServices.AppIcon.LoadTrayIcon(),
            new TrayIconPresenterOptions { Dedupe = false });

        // Task Manager's tray menu is never kept warm
        _trayMenu = new TrayMenuController(
            CreateTrayMenuWindow,
            static () => false,
            _settings,
            _theme,
            TADNLog.Log);
#if DEBUG
        TaskManagerWindowResources.ResourcesReloaded += OnTrayMenuResourcesReloaded;
#endif
        RefreshTrayIcon();
        _trayIcon.IsVisible = true;
    }

    private void RefreshTrayIcon()
    {
        if (_trayIconPresenter == null) return;

        if (_trayIconRenderer != null && _settings != null)
        {
            _trayIconRenderer.Style = _settings.TrayGraphStyle;
            _trayIconRenderer.DataSource = _settings.TrayGraphDataSource;
            _trayIconRenderer.ShowCPUHighestCoreTrace = _settings.ShowTrayCPUHighestCoreTrace;
        }

        PerformanceSnapshot performanceSnapshot =
            _performanceSnapshotService?.GetLatestSnapshot() ?? PerformanceSnapshot.Empty;
        _trayIconPresenter.Refresh(
            TaskManagerTrayTooltipFormatter.Format(performanceSnapshot, _settings?.PerformanceDeviceOrder));
    }

    private void OnSystemPerformanceSampleAvailable()
    {
        if (_snapshotService == null || _trayIconRenderer == null) return;

        SystemPerformanceSample sample = _snapshotService.GetLatestSystemPerformanceSample();
        _trayIconRenderer.AddSample(sample);
        RefreshTrayIcon();
    }

    private void OnTrayLeftClick()
    {
        if (_taskManagerWindow == null || _trayToggle == null) return;

        _taskManagerWindow.ToggleFromTray(_trayToggle);
    }

    // The first click of a double click may already have shown the window, so this reveal must not move it
    private void RevealTaskManagerFromTray() => _ = _taskManagerWindow?.ShowInPlaceAndActivateAsync();

    private void ShowTaskManager() => _taskManagerWindow?.ShowAndActivate();

    /// <summary>Brings the window forward in place for Ctrl+Shift+Esc, which arrives without foreground rights.</summary>
    private void ShowTaskManagerFromShortcut()
    {
        if (_taskManagerWindow == null) return;

        _ = _taskManagerWindow.ShowInPlaceAndForceForegroundAsync();
    }

    /// <summary>Shows the one-time in-app consent before the broker's UAC prompt for elevated actions.</summary>
    private Task<bool> ConfirmEnableElevatedActionsAsync()
    {
        TaskManagerWindow? window = _taskManagerWindow;
        return window == null ? Task.FromResult(false) : window.ConfirmAdministratorApprovalAsync();
    }

    private void OnTrayRightClick(Point point) =>
        Dispatcher.UIThread.Post(() => ShowTrayMenu(point));

    private void ShowTrayMenu(Point point)
    {
        if (_trayIcon == null || _trayMenu == null) return;

        PixelPoint cursorPoint = new((int)Math.Round(point.X), (int)Math.Round(point.Y));
        _trayMenu.Show(_trayIcon, cursorPoint, TrayAppDotNETContextMenuPosition.Modern);
    }

    private TrayMenuWindow CreateTrayMenuWindow()
    {
        AppSettings settings = _settings
                               ?? throw new InvalidOperationException("Task Manager tray menu requires settings.");
        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry(L(nameof(AppStrings.Tray_OpenTaskManager)), ShowTaskManager)
        {
            Node = UI.ControlMap.TrayMenu.OpenTaskManager
        });

        // Task Manager has no Settings entry; its window holds the settings pages
        return new TrayMenuWindow(
            entries,
            TrayMenuWindowOptions.Create(
                settings,
                _theme,
                UI.ControlMap.TrayMenu.ID,
                settingsEntry: null,
                TrayMenuStandardEntry.Exit(ExitApplication, UI.ControlMap.TrayMenu.Exit)) with
            {
                FontSize = TaskManagerWindowResources.Current.AxamlTaskManagerTrayMenu.FontSize
            });
    }

#if DEBUG
    private void OnTrayMenuResourcesReloaded() => _trayMenu?.Invalidate();
#endif

    // The window's sidebar update button follows the update state
    private void OnUpdateStateChanged() => _taskManagerWindow?.NotifyUpdateStateChanged();

    private void OnUpdateBalloonClicked()
    {
        if (_host?.UpdateService?.AvailableUpdate == null) return;

        ShowTaskManager();
    }

    private void OnThemeChanged(bool isLightTheme) =>
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeVariant();
            _taskManagerWindow?.RefreshTheme();
            RefreshTrayIcon();
        });

    private void OnSettingsChanged() =>
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeVariant();
            if (_settings != null)
            {
                _windowsTaskManagerHotkeyOverride?.SetEnabled(
                    _settings.OverrideWindowsTaskManagerHotkey);
                _performanceSnapshotService?.UpdateConfiguration(
                    _settings.PerformanceSampleIntervalMilliseconds,
                    PerformanceSamplingSettings.CalculateMaximumHistoryCount(
                        _settings.PerformanceHistoryLengthMinutes,
                        _settings.PerformanceSampleIntervalMilliseconds),
                    _settings.ShowMemoryModuleSerialNumbers);
            }

            _taskManagerWindow?.RefreshTheme();
            RefreshTrayIcon();
        });

    private void ApplyThemeVariant() =>
        RequestedThemeVariant = ResolveEffectiveIsLightTheme()
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

    private bool ResolveEffectiveIsLightTheme() => AppTheme.ResolveIsLight(_settings, _theme);

    private void ShutdownServices()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;

        try
        {
            Safe.Dispose(_windowsTaskManagerHotkeyOverride);
            _windowsTaskManagerHotkeyOverride = null;
            Safe.Dispose(_taskManagerActivationServer);
            _taskManagerActivationServer = null;
            Safe.Dispose(_elevatedActionCoordinator);
            _elevatedActionCoordinator = null;
            AppServices.ElevatedActions = null;

            AppServices.UpdateCheckService = null;
            AppServices.Host = null;

#if DEBUG
            TaskManagerWindowResources.ResourcesReloaded -= OnTrayMenuResourcesReloaded;
#endif
            Safe.Dispose(_trayMenu);
            _trayMenu = null;

            if (_taskManagerWindow != null)
            {
                _taskManagerWindow.RequestPermanentClose();
                _taskManagerWindow = null;
            }

            if (_snapshotService != null)
                _snapshotService.SnapshotAvailable -= OnSystemPerformanceSampleAvailable;
            Safe.Dispose(_snapshotService);
            _snapshotService = null;
            Safe.Dispose(_performanceSnapshotService);
            _performanceSnapshotService = null;
            Safe.Dispose(_processIconService);
            _processIconService = null;
            Safe.Dispose(_processTerminationService);
            _processTerminationService = null;

            if (_trayIcon != null)
            {
                _trayIcon.LeftClick -= OnTrayLeftClick;
                _trayIcon.LeftDoubleClick -= RevealTaskManagerFromTray;
                _trayIcon.KeySelect -= RevealTaskManagerFromTray;
                _trayIcon.RightClick -= OnTrayRightClick;
                _trayIcon.RefreshNeeded -= RefreshTrayIcon;
                _trayIcon.BalloonClicked -= OnUpdateBalloonClicked;
            }

            Safe.Dispose(_trayIconPresenter);
            _trayIconPresenter = null;
            _trayIconRenderer = null;
            Safe.Dispose(_trayToggle);
            _trayToggle = null;
            Safe.Dispose(_trayIcon);
            _trayIcon = null;
            if (_settings != null)
            {
                _settings.Changed -= OnSettingsChanged;
                _settings = null;
                AppServices.Settings = null;
            }

            if (_theme != null)
            {
                _theme.ThemeChanged -= OnThemeChanged;
                _theme = null;
                AppServices.Theme = null;
            }

            TADNLog.Flush();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"TaskManagerAvaloniaApp.ShutdownServices: {exception}");
        }
    }

    private void ExitApplication() => _host?.Exit();

    private static string L(string key) => LocalizationManager.Instance[key];
}
