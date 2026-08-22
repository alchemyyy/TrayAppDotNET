using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET;

internal static class TaskManagerAvaloniaRunner
{
    public static int Run(string[] args) =>
        TrayAppDotNETAvalonia.StartWithExplicitShutdown<TaskManagerAvaloniaApp>(
            args,
            builder => TrayAppDotNETAvalonia.UseConfiguredRenderingBackend(
                builder,
                AppSettings.GetDefaultPath,
                TADNLog.Log,
                TrayAppDotNETRenderingBackend.Software));
}

internal sealed class TaskManagerAvaloniaApp : Application
{
    private readonly TrayIconRenderer _trayIconRenderer = new(new TrayIconRenderOptions
    {
        IconFontFamilies =
        [
            TADNFontResolver.SegoeFluentIconsFamilyName,
            TADNFontResolver.SegoeMDL2AssetsFamilyName
        ],
        FallbackIcon = null,
        Log = TADNLog.Log
    });

    private AppSettings? _settings;
    private AppTheme? _theme;
    private ProcessSnapshotService? _snapshotService;
    private TaskManagerWindow? _taskManagerWindow;
    private TaskManagerTrayMenuWindow? _trayMenuWindow;
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private WatcherMonitor? _watcherMonitor;
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

        StartWatcherMonitor();
        _snapshotService = new ProcessSnapshotService();
        _snapshotService.Start();
        CreateTaskManagerWindow();
        CreateTrayIcon();
        _taskManagerWindow!.ShowAtDefaultPositionAndActivate();
        base.OnFrameworkInitializationCompleted();
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
        if (_settings == null || _theme == null || _snapshotService == null)
            throw new InvalidOperationException("Task Manager services must be loaded before creating the window.");

        _taskManagerWindow = new TaskManagerWindow(_settings, _theme, _snapshotService);
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayAppDotNETShellTrayIcon(
            Constants.TrayIconGUID,
            Program.ApplicationName + ".TrayIcon")
        {
            IsVisible = true
        };
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.LeftDoubleClick += ShowTaskManager;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.RefreshNeeded += RefreshTrayIcon;
        RefreshTrayIcon();
    }

    private void RefreshTrayIcon()
    {
        TrayAppDotNETShellTrayIcon? trayIcon = _trayIcon;
        if (trayIcon == null) return;

        bool isLight = ResolveEffectiveIsLightTheme();
        AppTheme theme = _theme ?? AppTheme.Default;
        TrayIconRenderInput input = new(
            new TrayIconGlyphLayer(null, SettingsNavigationGlyphs.MonitorOptions.Text),
            theme.ResolveForeground(_settings, isLight),
            BackdropOpacity: 0);
        NativeIcon? icon = _trayIconRenderer.Render(input);
        if (icon == null)
        {
            trayIcon.SetTooltip(Constants.DisplayName);
            return;
        }

        trayIcon.SetIconAndTooltip(icon, Constants.DisplayName);
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

    private void ShowTaskManager()
    {
        if (_taskManagerWindow == null) return;
        _taskManagerWindow.ShowAtDefaultPositionAndActivate();
    }

    private void OnTrayRightClick(Point point) =>
        Dispatcher.UIThread.Post(() => ShowTrayMenu(point));

    private void ShowTrayMenu(Point point)
    {
        if (_trayIcon == null || _settings == null) return;

        if (_trayMenuWindow is { IsVisible: true })
            _trayMenuWindow.Close();

        _trayMenuWindow = new TaskManagerTrayMenuWindow(
            CreatePalette(),
            _settings.EnableRoundedCorners,
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

            Safe.Dispose(_snapshotService);
            _snapshotService = null;

            if (_trayIcon != null)
            {
                _trayIcon.LeftClick -= OnTrayLeftClick;
                _trayIcon.LeftDoubleClick -= ShowTaskManager;
                _trayIcon.RightClick -= OnTrayRightClick;
                _trayIcon.RefreshNeeded -= RefreshTrayIcon;
            }

            Safe.Dispose(_trayIcon);
            _trayIcon = null;
            _trayIconRenderer.Dispose();
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
