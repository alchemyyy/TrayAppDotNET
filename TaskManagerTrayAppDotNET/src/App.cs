using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
    public static int Run(string[] args)
    {
        return TrayAppDotNETAvalonia.StartWithExplicitShutdown<TaskManagerAvaloniaApp>(
            args,
            builder =>
            {
                builder = TrayAppDotNETAvalonia.UseConfiguredRenderingBackend(
                    builder,
                    AppSettings.GetDefaultPath,
                    TADNLog.Log);
#if HOTAVALONIA_ENABLE
                builder = builder.UseHotReload();
#endif

                return builder;
            });
    }
}

internal sealed class TaskManagerAvaloniaApp : Application
{
    private AppSettings? _settings;
    private AppTheme? _theme;
    private ProcessIconService? _processIconService;
    private ProcessSnapshotService? _snapshotService;
    private PerformanceSnapshotService? _performanceSnapshotService;
    private ProcessTerminationService? _processTerminationService;
    private WindowsTaskManagerHotkeyOverride? _windowsTaskManagerHotkeyOverride;
    private TaskManagerWindow? _taskManagerWindow;
    private TaskManagerTrayMenuWindow? _trayMenuWindow;
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private TaskManagerTrayIcon? _trayIconRenderer;
    private readonly TrayIconRenderQueue _trayIconRenderQueue = new(TADNLog.Log);
    private WatcherMonitor? _watcherMonitor;
    private SystemPerformanceSample _latestSystemPerformanceSample = SystemPerformanceSample.Empty;
    private bool _shuttingDown;

    public override void Initialize() =>
        TrayAppDotNETAvalonia.InitializeDefaults(
            this,
            toolTipShowDelayMs: TimeConstants.ToolTipShowDelayDefaultMs);

    public override void OnFrameworkInitializationCompleted()
    {
        TADNLog.Initialize();
        TADNLog.Log("TaskManagerAvaloniaApp.OnFrameworkInitializationCompleted");
        TrayAppDotNETAvalonia.WireCrashHandlers(TADNLog.Shutdown);
        TrayAppDotNETAvalonia.ConfigureExplicitShutdown(this, ShutdownServices);
        LoadSettingsAndTheme();

        if (Program.IsInstallerMode)
        {
            TrayAppDotNETInstallerRunner.Show(this,
                new TrayAppDotNETInstallerWindowOptions
                {
                    Layout = AppServices.InstallLayout,
                    Icon = null,
                    Palette = CreatePalette(),
                    EnableRoundedCorners = _settings?.EnableRoundedCorners ?? true
                });
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (Program.IsUninstallerMode)
        {
            TrayAppDotNETAvalonia.ConfigureShutdownOnLastWindowClose(this);
            TaskManagerUninstallerWindow uninstaller = new(
                Program.UninstallerInstallDir ?? string.Empty,
                Program.UninstallerScope);
            uninstaller.Show();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        AppSettings settings = _settings
                               ?? throw new InvalidOperationException("Task Manager settings were not loaded.");
        StartWatcherMonitor();
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
            settings.ShowMemoryModuleSerialNumbers);
        _trayIconRenderer = new TaskManagerTrayIcon();
        CreateTaskManagerWindow();
        _windowsTaskManagerHotkeyOverride = new WindowsTaskManagerHotkeyOverride(
            () => Dispatcher.UIThread.Post(ShowTaskManager),
            TADNLog.Log);
        _windowsTaskManagerHotkeyOverride.SetEnabled(settings.OverrideWindowsTaskManagerHotkey);
        _snapshotService.Start();
        _performanceSnapshotService.Start();
        CreateTrayIcon();
        TaskManagerWindow taskManagerWindow = _taskManagerWindow!;
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

    private void LoadSettingsAndTheme()
    {
        TrayAppDotNETLoadResult<AppSettings, AppTheme> loaded = TrayAppDotNETAvalonia.LoadSettingsAndTheme(
            new TrayAppDotNETLoadOptions<AppSettings, AppTheme>
            {
                GetSettingsPath = AppSettings.GetDefaultPath,
                LoadSettings = AppSettings.LoadOrDefault,
                CreateDefaultSettings = static () => new AppSettings(),
                GetRunOnStartup = static settings => settings.RunOnStartup,
                Startup = AppServices.Startup,
                ConfigureSettings = ConfigureSettings,
                LogSettingsLoadFailed = exception =>
                    TADNLog.Log($"TaskManager settings load failed: {exception}"),
                GetThemePath = AppThemeStore.GetDefaultPath,
                LoadTheme = AppThemeStore.LoadOrDefault,
                ConfigureTheme = ConfigureTheme,
                LogThemeLoadFailed = exception =>
                    TADNLog.Log($"TaskManager theme load failed: {exception}")
            });
        _settings = loaded.Settings;
    }

    private void ConfigureSettings(AppSettings settings)
    {
        _settings = settings;
        settings.Changed += OnSettingsChanged;
        AppServices.Settings = settings;
        TrayAppDotNETAnimationPolicy.Apply(this, settings.AnimationMode);
        TrayAppDotNETToolTip.ShowDelayMs = settings.ToolTipShowDelayMs;
    }

    private void ConfigureTheme(AppTheme theme)
    {
        _theme = theme;
        theme.ThemeChanged += OnThemeChanged;
        AppServices.Theme = theme;
        ApplyThemeVariant();
    }

    private void StartWatcherMonitor()
    {
        try
        {
            _watcherMonitor = TrayAppDotNETAvalonia.CreateWatcherMonitor(Program.WatcherPID, ExitApplication);
            _watcherMonitor.Start();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"TaskManager watcher monitor init failed: {exception}");
        }
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
        _trayIcon = new TrayAppDotNETShellTrayIcon(
            Constants.TrayIconGUID,
            Program.ApplicationName + ".TrayIcon");
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.LeftDoubleClick += ShowTaskManager;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.RefreshNeeded += RefreshTrayIcon;
        RefreshTrayIcon();
        _trayIcon.IsVisible = true;
    }

    private void RefreshTrayIcon()
    {
        TrayAppDotNETShellTrayIcon? trayIcon = _trayIcon;
        if (trayIcon == null) return;

        if (_trayIconRenderer != null && _settings != null)
        {
            TaskManagerTrayIcon renderer = _trayIconRenderer;
            TrayGraphDataSource dataSource = _settings.TrayGraphDataSource;
            TaskManagerTrayIconRenderInput input = renderer.CreateRenderInput(
                _settings.TrayGraphStyle,
                dataSource,
                _settings.ShowTrayCPUHighestCoreTrace);
            string tooltip = BuildTrayTooltip(_latestSystemPerformanceSample, dataSource);
            trayIcon.SetTooltip(tooltip);
            _trayIconRenderQueue.Request(
                () => renderer.RenderIcon(input),
                icon => ApplyRenderedTrayIcon(icon, tooltip));
            return;
        }

        NativeIcon? fallbackIcon = AppThemeStore.LoadAppNativeIcon();
        if (fallbackIcon != null)
        {
            trayIcon.SetOwnedIconAndTooltip(fallbackIcon, Constants.DisplayName);
            return;
        }

        trayIcon.SetTooltip(Constants.DisplayName);
        TADNLog.Log("Task Manager tray icon could not be loaded.");
    }

    private void OnSystemPerformanceSampleAvailable()
    {
        if (_snapshotService == null || _trayIconRenderer == null) return;

        SystemPerformanceSample sample = _snapshotService.GetLatestSystemPerformanceSample();
        _latestSystemPerformanceSample = sample;
        _trayIconRenderer.AddSample(sample);
        RefreshTrayIcon();
    }

    /// <summary>Applies a rendered graph icon, disposing it after tray shutdown.</summary>
    private void ApplyRenderedTrayIcon(NativeIcon icon, string tooltip)
    {
        if (_trayIcon == null)
        {
            icon.Dispose();
            return;
        }

        _trayIcon.SetOwnedIconAndTooltip(icon, tooltip);
    }

    private static string BuildTrayTooltip(
        SystemPerformanceSample sample,
        TrayGraphDataSource dataSource)
    {
        string label = dataSource switch
        {
            TrayGraphDataSource.CPUAverage => "CPU usage (average)",
            TrayGraphDataSource.CPUHighestCore => "CPU usage (highest core)",
            TrayGraphDataSource.Memory => "Memory (RAM)",
            _ => "CPU usage (average)"
        };
        int percent = (int)Math.Round(
            Math.Clamp(sample.Select(dataSource), min: 0, max: 100),
            MidpointRounding.AwayFromZero);
        return $"{Constants.DisplayName}\n{label}: {percent}%";
    }

    private void OnTrayLeftClick()
    {
        if (_taskManagerWindow is { IsVisible: true })
        {
            _taskManagerWindow.Hide();
            return;
        }

        ShowTaskManager();
    }

    private void ShowTaskManager() => _taskManagerWindow?.ShowAtDefaultPositionAndActivate();

    private void OnTrayRightClick(Point point) =>
        Dispatcher.UIThread.Post(() => ShowTrayMenu(point));

    private void ShowTrayMenu(Point point)
    {
        if (_trayIcon == null || _settings == null) return;

        if (_trayMenuWindow is { IsVisible: true })
            _trayMenuWindow.Close();

        _trayMenuWindow = new TaskManagerTrayMenuWindow(
            _settings,
            CreatePalette(),
            ShowTaskManager,
            ExitApplication);
        _trayMenuWindow.Closed += OnTrayMenuClosed;
        PixelPoint cursorPoint = new((int)Math.Round(point.X), (int)Math.Round(point.Y));
        _trayMenuWindow.ShowAt(_trayIcon, cursorPoint);
    }

    private void OnTrayMenuClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is TaskManagerTrayMenuWindow menuWindow)
            menuWindow.Closed -= OnTrayMenuClosed;
        if (ReferenceEquals(sender, _trayMenuWindow))
            _trayMenuWindow = null;
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
                TrayAppDotNETAnimationPolicy.Apply(this, _settings.AnimationMode);
                TrayAppDotNETToolTip.ShowDelayMs = _settings.ToolTipShowDelayMs;
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

    private bool ResolveEffectiveIsLightTheme() => _settings?.ThemeMode switch
    {
        TrayAppDotNETThemeMode.Light => true,
        TrayAppDotNETThemeMode.Dark => false,
        _ => _theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme
    };

    private SettingsPalette CreatePalette() =>
        VolumeSettingsPalette.Create(_theme, _settings, ResolveEffectiveIsLightTheme());

    private void ShutdownServices()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;

        try
        {
            Safe.Dispose(_windowsTaskManagerHotkeyOverride);
            _windowsTaskManagerHotkeyOverride = null;

            if (_trayMenuWindow != null)
            {
                _trayMenuWindow.Closed -= OnTrayMenuClosed;
                _trayMenuWindow.Close();
                _trayMenuWindow = null;
            }

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
                _trayIcon.LeftDoubleClick -= ShowTaskManager;
                _trayIcon.RightClick -= OnTrayRightClick;
                _trayIcon.RefreshNeeded -= RefreshTrayIcon;
            }

            Safe.Dispose(_trayIcon);
            _trayIcon = null;
            _trayIconRenderQueue.Dispose();
            Safe.Dispose(_trayIconRenderer);
            _trayIconRenderer = null;
            Safe.Dispose(_watcherMonitor);
            _watcherMonitor = null;

            if (_settings != null)
            {
                _settings.Changed -= OnSettingsChanged;
                _settings.Save();
                _settings = null;
                AppServices.Settings = null;
            }

            if (_theme != null)
            {
                _theme.ThemeChanged -= OnThemeChanged;
                Safe.Dispose(_theme);
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

    private void ExitApplication()
    {
        ShutdownServices();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
