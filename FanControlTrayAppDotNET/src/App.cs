using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using FanControlTrayAppDotNET.Localization;
using FanControlTrayAppDotNET.Services;
using FanControlTrayAppDotNET.UI;
using HotAvalonia;
using TrayAppDotNETCommon.UI.WarmWindows;
using FanHotkeyAction = TrayAppDotNETCommon.Models.HotkeyAction;
using FanHotkeyFiredEventArgs = TrayAppDotNETCommon.Services.HotkeyFiredEventArgs;
using FanHotkeyService = TrayAppDotNETCommon.Services.GlobalHotkeyService;
using FanInstallScope = TrayAppDotNETCommon.InstallScope;
using FanWatcherMonitor = TrayAppDotNETCommon.Services.WatcherMonitor;

namespace FanControlTrayAppDotNET;

internal static class FanAvaloniaRunner
{
    public static int Run(string[] args)
    {
        return TrayAppDotNETAvalonia.StartWithExplicitShutdown<FanAvaloniaApp>(
            args,
            builder =>
            {
                builder = TrayAppDotNETAvalonia.UseConfiguredRenderingBackend(builder);
                builder = builder.UseHotReload();

                return builder;
            });
    }
}

internal sealed class FanAvaloniaApp : Application
{
    private AppTheme? _theme;
    private AppSettings? _settings;
    private LHMService? _lhmService;
    private ProcessRunningService? _processRunningService;
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private FanTrayIcon? _trayIconRenderer;
    private FanTrayMenuWindow? _trayMenuWindow;
    private FanFlyoutWindow? _fanFlyout;
    private FanSettingsWindow? _settingsWindow;
    private SettingsFlyoutKeepOpenCoordinator? _settingsFlyoutKeepOpen;
    private TrayAppDotNETWarmWindowSlot<FanFlyoutWindow>? _fanFlyoutWarmSlot;
    private TrayAppDotNETWarmWindowSlot<FanTrayMenuWindow>? _trayMenuWarmSlot;
    private FanHotkeyService? _hotkeyService;
    private FanWatcherMonitor? _watcherMonitor;
    private UpdateCheckService? _updateCheckService;
    private int _lastNotifiedUpdateVersion;
    private bool _suppressNextTrayClick;
    private bool _shuttingDown;

    public override void Initialize()
    {
        TrayAppDotNETAvalonia.InitializeDefaults(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        TADNLog.Initialize();
        TADNLog.Log("FanAvaloniaApp.OnFrameworkInitializationCompleted");
        LocalizationManager.Instance.Initialize(
            Strings.ResourceManager,
            culture => Strings.Culture = culture);
        WireCrashHandlers();

        TrayAppDotNETAvalonia.ConfigureExplicitShutdown(this, ShutdownServices);

        if (Program.IsUninstallerMode)
        {
            LoadSettingsAndTheme();
            TrayAppDotNETAvalonia.ConfigureShutdownOnLastWindowClose(this);
            ShowUninstallerWindow(Program.UninstallerInstallDir ?? string.Empty, Program.UninstallerScope);
            base.OnFrameworkInitializationCompleted();
            return;
        }

        LoadSettingsAndTheme();
        StartServices();
        CreateTrayIcon();
        RequestTrayRefresh();
        ScheduleKeepWarmPriming();
        base.OnFrameworkInitializationCompleted();
    }

    private static void WireCrashHandlers()
    {
        TrayAppDotNETAvalonia.WireCrashHandlers(TADNLog.Shutdown);
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
                LogSettingsLoadFailed = ex => TADNLog.Log($"FanAvaloniaApp settings load failed: {ex}"),
                GetThemePath = AppTheme.GetDefaultPath,
                LoadTheme = AppTheme.LoadOrDefault,
                ConfigureTheme = ConfigureTheme,
                LogThemeLoadFailed = ex => TADNLog.Log($"FanAvaloniaApp theme load failed: {ex}"),
            });

        _settings = loaded.Settings;
    }

    private void ConfigureSettings(AppSettings settings)
    {
        settings.Changed += OnSettingsChanged;
        AppServices.Settings = settings;
        TrayAppDotNETAnimationPolicy.Apply(this, TrayAppDotNETAnimationMode.System);
    }

    private void ConfigureTheme(AppTheme theme)
    {
        _theme = theme;
        _theme.ThemeChanged += OnThemeChanged;
        AppServices.Theme = _theme;
        ApplyThemeVariant();
    }

    private void StartServices()
    {
        try
        {
            if (_settings != null)
            {
                PawnIoDriverInstaller.EnsureInstalled();

                _lhmService = new LHMService(Dispatcher.UIThread, _settings);
                _lhmService.PollTickCompleted += RequestTrayRefresh;
                _lhmService.Start();
                AppServices.LHMService = _lhmService;
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanAvaloniaApp LHM init failed: {ex}");
        }

        try
        {
            _processRunningService = new ProcessRunningService(Dispatcher.UIThread);
            _processRunningService.Start();
            AppServices.ProcessRunningService = _processRunningService;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanAvaloniaApp process service init failed: {ex}");
        }

        try
        {
            _trayIconRenderer = new FanTrayIcon(_theme) { IsLightTheme = ResolveEffectiveIsLightTheme(), };
            ApplyTrayIconColorOverride();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanAvaloniaApp tray renderer init failed: {ex}");
        }

        try
        {
            _hotkeyService = new FanHotkeyService(Program.ApplicationName + ".HotkeySink");
            _hotkeyService.Initialize();
            _hotkeyService.Fired += OnHotkeyFired;
            _hotkeyService.Apply(_settings?.Hotkeys ?? []);
            AppServices.HotkeyService = _hotkeyService;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanAvaloniaApp hotkey init failed: {ex}");
        }

        try
        {
            _watcherMonitor = TrayAppDotNETAvalonia.CreateWatcherMonitor(Program.WatcherPID, ExitApplication);
            _watcherMonitor.Start();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanAvaloniaApp watcher init failed: {ex}");
        }

        if (_settings != null)
        {
            try
            {
                _updateCheckService = TrayAppDotNETAvalonia.CreateGitHubUpdateCheckService(
                    _settings,
                    repositoryName: "TrayAppDotNET",
                    applicationName: Program.ApplicationName,
                    currentBuild: BuildInfo.BuildNumber);
                _updateCheckService.StateChanged += OnUpdateStateChanged;
                _updateCheckService.Start();
                AppServices.UpdateCheckService = _updateCheckService;
            }
            catch (Exception ex)
            {
                TADNLog.Log($"FanAvaloniaApp update service init failed: {ex}");
            }
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayAppDotNETShellTrayIcon(Constants.TrayIconGUID, Program.ApplicationName + ".TrayIcon")
        {
            IsScrollEnabled = _settings?.TrayScrollEnabled ?? true, IsVisible = true,
        };
        _trayIcon.LeftMouseDown += OnTrayLeftMouseDown;
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.LeftDoubleClick += OnTrayLeftDoubleClick;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.RefreshNeeded += RequestTrayRefresh;
        _trayIcon.BalloonClicked += OnUpdateBalloonClicked;
    }

    private void OnHotkeyFired(object? sender, FanHotkeyFiredEventArgs e)
    {
        try { HandleHotkey(e.Action); }
        catch (Exception ex) { TADNLog.Log($"FanAvaloniaApp.OnHotkeyFired: {ex}"); }
    }

    private void HandleHotkey(FanHotkeyAction action)
    {
        switch (action)
        {
            case FanHotkeyAction.OpenSettings:
                OpenSettings();
                break;
            case FanHotkeyAction.OpenFlyout:
                ShowFanFlyout();
                break;
        }
    }

    private void OnThemeChanged(bool isLightTheme) =>
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeVariant();
            ApplyTrayIconColorOverride();
            RequestTrayRefresh();
        });

    private void OnSettingsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeVariant();
            ApplyTrayIconColorOverride();
            _trayIcon?.IsScrollEnabled = _settings?.TrayScrollEnabled ?? true;
            if (_hotkeyService != null && _settings != null)
                _hotkeyService.Apply(_settings.Hotkeys);
            ApplyKeepWarmPolicies();
            RequestTrayRefresh();
        });
    }

    private void ApplyKeepWarmPolicies()
    {
        if (_fanFlyoutWarmSlot != null || _settings?.KeepFlyoutWarm == true)
            FanFlyoutWarmSlot.ApplyKeepWarmPolicy(CreateManagedFanFlyout);
        if (_trayMenuWarmSlot != null || _settings?.KeepTrayContextMenuWarm == true)
            TrayMenuWarmSlot.ApplyKeepWarmPolicy(CreateTrayMenuWindow);
    }

    private void OnUpdateStateChanged()
    {
        UpdateInfo? info = _updateCheckService?.AvailableUpdate;
        if (info == null || _settings?.ShowUpdateNotificationsEnabled != true) return;
        if (info.Version <= _lastNotifiedUpdateVersion) return;

        _lastNotifiedUpdateVersion = info.Version;
        _trayIcon?.ShowBalloon(
            L("UpdateNotification_Title", "Update available"),
            string.Format(L("UpdateNotification_BodyFormat", "{0} is available."), info.ReleaseName));
    }

    private void OnUpdateBalloonClicked()
    {
        if (_updateCheckService?.AvailableUpdate == null) return;
        OpenSettings(FanSettingsPage.About);
    }

    private bool ResolveEffectiveIsLightTheme() =>
        AppTheme.ResolveEffectiveIsLightTheme(_settings);

    private void ApplyThemeVariant()
    {
        RequestedThemeVariant = ResolveEffectiveIsLightTheme()
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    private void ApplyTrayIconColorOverride()
    {
        if (_trayIconRenderer == null) return;

        bool isLight = ResolveEffectiveIsLightTheme();
        _trayIconRenderer.IsLightTheme = isLight;
        _trayIconRenderer.TrayIconColorOverride = _settings?.TrayIconColor.Resolve(isLight);
    }

    private void RequestTrayRefresh()
    {
        if (_trayIcon == null) return;

        _trayIcon.SetTooltip(BuildTrayTooltip());
        if (_trayIconRenderer?.CreateIcon() is { } icon)
        {
            _trayIcon.SetIcon(icon);
            return;
        }

        if (_trayIconRenderer == null && AppTheme.LoadAppNativeIcon() is { } fallbackIcon)
        {
            using (fallbackIcon)
                _trayIcon.SetIcon(fallbackIcon);
        }
    }

    private string BuildTrayTooltip()
    {
        string header = L("Tray_Tooltip_Default", Program.ApplicationName);
        List<string> lines = [header];

        bool showCPU = _settings?.ShowCPUTempInTooltip ?? true;
        bool showGPU = _settings?.ShowGPUTempInTooltip ?? true;

        if (showCPU && TryGetTempC("CPU") is { } cpuC)
            lines.Add(string.Format(L("Tray_Tooltip_CPUTemp_Format", "CPU: {0} C"), cpuC));

        if (showGPU && TryGetTempC("GPU") is { } gpuC)
            lines.Add(string.Format(L("Tray_Tooltip_GPUTemp_Format", "GPU: {0} C"), gpuC));

        return string.Join('\n', lines);
    }

    private static int? TryGetTempC(string controllerHint)
    {
        DataSource? best = null;
        foreach (DataSource source in DataSource.DataSources.Values)
        {
            if (source.DataSourceType != DataSourceTypeEnum.Temperature) continue;
            if (source.ControllerName.IndexOf(controllerHint, StringComparison.OrdinalIgnoreCase) < 0) continue;

            if (best == null)
            {
                best = source;
                continue;
            }

            if (source.UserDefinedName.Contains("Package", StringComparison.OrdinalIgnoreCase)
                || source.DataSourceKey.Contains("Package", StringComparison.OrdinalIgnoreCase))
                best = source;
        }

        return best == null ? null : (int)Math.Round(best.Value / 1000.0);
    }

    private void OnTrayLeftMouseDown()
    {
        if (IsControlOrAltDown()) return;
        if (_fanFlyout is { IsUndocked: false, IsVisible: true, IsActive: true })
        {
            _fanFlyout.Hide();
            _suppressNextTrayClick = true;
        }
    }

    private void OnTrayLeftClick()
    {
        if (_suppressNextTrayClick)
        {
            _suppressNextTrayClick = false;
            return;
        }

        if (TryDispatchModifiedTrayAction(
                _settings?.TrayCtrlLeftClickAction ?? TrayClickAction.Nothing,
                _settings?.TrayAltLeftClickAction ?? TrayClickAction.Nothing))
            return;

        ShowFanFlyout();
    }

    private void OnTrayLeftDoubleClick()
    {
        if (TryDispatchModifiedTrayAction(
                _settings?.TrayCtrlDoubleLeftClickAction ?? TrayClickAction.Nothing,
                _settings?.TrayAltDoubleLeftClickAction ?? TrayClickAction.Nothing))
            return;

        TrayClickAction action = _settings?.TrayDoubleClickAction ?? TrayClickAction.OpenSettings;
        if (!TryDispatchTrayAction(action))
            ShowFanFlyout();
    }

    private void OnTrayRightClick(Point point) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (TryDispatchModifiedTrayAction(
                    _settings?.TrayCtrlRightClickAction ?? TrayClickAction.Nothing,
                    _settings?.TrayAltRightClickAction ?? TrayClickAction.Nothing))
                return;

            ShowTrayContextMenu(point);
        });

    private static bool IsControlOrAltDown() =>
        IsKeyDown(User32.VK_CONTROL) || IsKeyDown(User32.VK_MENU);

    private static bool IsKeyDown(int vKey) =>
        (User32.GetAsyncKeyState(vKey) & unchecked((short)0x8000)) != 0;

    private bool TryDispatchModifiedTrayAction(TrayClickAction controlAction, TrayClickAction altAction)
    {
        if (IsKeyDown(User32.VK_CONTROL) && TryDispatchTrayAction(controlAction)) return true;
        if (IsKeyDown(User32.VK_MENU) && TryDispatchTrayAction(altAction)) return true;
        return false;
    }

    private bool TryDispatchTrayAction(TrayClickAction action)
    {
        switch (action)
        {
            case TrayClickAction.OpenSettings:
                OpenSettings();
                return true;
            case TrayClickAction.Nothing:
            default:
                return false;
        }
    }

    private void ShowTrayContextMenu(Point point)
    {
        if (_trayIcon == null || _settings == null) return;

        if (_trayMenuWindow is { IsVisible: true })
            _trayMenuWindow.DismissForWarmCache();

        FanTrayMenuWindow menuWindow = TrayMenuWarmSlot.TakeOrCreate(CreateTrayMenuWindow);
        _trayMenuWindow = menuWindow;

        PixelPoint cursorPoint = new((int)Math.Round(point.X), (int)Math.Round(point.Y));
        menuWindow.ShowAt(_trayIcon, cursorPoint, _settings.ContextMenuPosition);
    }

    private FanTrayMenuWindow CreateTrayMenuWindow()
    {
        if (_settings == null)
            throw new InvalidOperationException("Fan tray menu requires settings.");

        FanTrayMenuWindow menuWindow = new(
            _settings,
            FanSettingsWindow.CreatePalette(_theme, _settings, ResolveEffectiveIsLightTheme()),
            _settings.EnableRoundedCorners,
            _settings.ContextMenuFontSize,
            OpenSettings,
            ExitApplication);
        _trayMenuWindow = menuWindow;
        menuWindow.Closed += OnTrayMenuClosed;
        return menuWindow;
    }

    private void OnTrayMenuClosed(object? sender, EventArgs e)
    {
        if (sender is FanTrayMenuWindow menu)
            menu.Closed -= OnTrayMenuClosed;
        if (ReferenceEquals(_trayMenuWindow, sender))
            _trayMenuWindow = null;
    }

    private void ShowFanFlyout(bool activate = true)
    {
        if (_settings == null || _trayIcon == null) return;

        if (_fanFlyout == null)
            _fanFlyout = FanFlyoutWarmSlot.TakeOrCreate(CreateManagedFanFlyout);

        _fanFlyout.ShowAt(_trayIcon, activate);
    }

    private FanFlyoutWindow CreateManagedFanFlyout()
    {
        if (_settings == null)
            throw new InvalidOperationException("Fan flyout requires settings.");

        FanFlyoutWindow flyout = new(_lhmService, _settings, OpenSettingsFromFlyout);
        _fanFlyout = flyout;
        flyout.Closed += OnFanFlyoutClosed;
        return flyout;
    }

    private TrayAppDotNETWarmWindowSlot<FanFlyoutWindow> FanFlyoutWarmSlot =>
        _fanFlyoutWarmSlot ??= new TrayAppDotNETWarmWindowSlot<FanFlyoutWindow>(
            () => _settings?.KeepFlyoutWarm ?? true,
            ex => TADNLog.Log($"Fan flyout keep-warm: {ex.Message}"));

    private TrayAppDotNETWarmWindowSlot<FanTrayMenuWindow> TrayMenuWarmSlot =>
        _trayMenuWarmSlot ??= new TrayAppDotNETWarmWindowSlot<FanTrayMenuWindow>(
            () => _settings?.KeepTrayContextMenuWarm ?? true,
            ex => TADNLog.Log($"Fan tray menu keep-warm: {ex.Message}"));

    private void ScheduleKeepWarmPriming()
    {
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                if (_settings?.KeepFlyoutWarm == true)
                    await FanFlyoutWarmSlot.PrimeAsync(CreateManagedFanFlyout);
                if (_settings?.KeepTrayContextMenuWarm == true && _trayIcon != null)
                    await TrayMenuWarmSlot.PrimeAsync(CreateTrayMenuWindow);
            }
            catch (Exception ex)
            {
                TADNLog.Log($"FanAvaloniaApp.ScheduleKeepWarmPriming: {ex.Message}");
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private SettingsFlyoutKeepOpenCoordinator SettingsFlyoutKeepOpen =>
        _settingsFlyoutKeepOpen ??= new SettingsFlyoutKeepOpenCoordinator(
            () => _settingsWindow,
            () => _fanFlyout,
            () => ShowFanFlyout(activate: false));

    private void OnFanFlyoutClosed(object? sender, EventArgs e)
    {
        if (_fanFlyout != null)
        {
            _fanFlyout.Closed -= OnFanFlyoutClosed;
            _fanFlyout = null;
        }
    }

    private void OpenSettingsFromFlyout(FanSettingsPage? page) => OpenSettings(page);

    private void OpenSettings() => OpenSettings(null);

    private void OpenSettings(FanSettingsPage? page)
    {
        if (_settings == null) return;

        if (_settingsWindow == null)
        {
            _settingsWindow = new FanSettingsWindow(_settings, ShowUninstallerWindow);
            SettingsFlyoutKeepOpen.Attach(_settingsWindow);
            _settingsWindow.Closed += OnSettingsWindowClosed;
        }

        SettingsFlyoutKeepOpen.HoldOpen();
        if (page.HasValue) _settingsWindow.SelectPage(page.Value);
        _settingsWindow.ShowAtDefaultPositionAndActivate();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        _settingsFlyoutKeepOpen?.Detach();
        if (_settingsWindow != null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            _settingsWindow = null;
        }

        _ = Task.Delay(TimeConstants.PostSettingsCloseGCDelayMs).ContinueWith(_ =>
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }, TaskScheduler.Default);
    }

    private void ShowUninstallerWindow(string installDir, FanInstallScope scope)
    {
        FanUninstallerWindow window = new(installDir, scope);
        if (_settingsWindow != null) window.Show(_settingsWindow);
        else window.Show();
    }

    private void ShutdownServices()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;

        try
        {
            if (_hotkeyService != null)
            {
                _hotkeyService.Fired -= OnHotkeyFired;
                Safe.Dispose(_hotkeyService);
                _hotkeyService = null;
                AppServices.HotkeyService = null;
            }

            Safe.Dispose(_watcherMonitor);
            _watcherMonitor = null;

            if (_updateCheckService != null)
            {
                _updateCheckService.StateChanged -= OnUpdateStateChanged;
                Safe.Dispose(_updateCheckService);
                _updateCheckService = null;
                AppServices.UpdateCheckService = null;
            }

            if (_settings != null)
            {
                _settings.Changed -= OnSettingsChanged;
                _settings.Save();
            }

            AppServices.Settings = null;

            if (_lhmService != null)
            {
                _lhmService.PollTickCompleted -= RequestTrayRefresh;
                Safe.Dispose(_lhmService);
                _lhmService = null;
                AppServices.LHMService = null;
            }

            Safe.Dispose(_processRunningService);
            _processRunningService = null;
            AppServices.ProcessRunningService = null;

            if (_theme != null)
            {
                _theme.ThemeChanged -= OnThemeChanged;
                Safe.Dispose(_theme);
                _theme = null;
                AppServices.Theme = null;
            }

            if (_trayMenuWindow != null)
            {
                try { _trayMenuWindow.Close(); }
                catch { }

                _trayMenuWindow = null;
            }

            Safe.Dispose(_settingsFlyoutKeepOpen);
            _settingsFlyoutKeepOpen = null;

            Safe.Dispose(_fanFlyoutWarmSlot);
            _fanFlyoutWarmSlot = null;
            Safe.Dispose(_trayMenuWarmSlot);
            _trayMenuWarmSlot = null;

            if (_fanFlyout != null)
            {
                _fanFlyout.Closed -= OnFanFlyoutClosed;
                try { _fanFlyout.Close(); }
                catch { }

                _fanFlyout = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.LeftMouseDown -= OnTrayLeftMouseDown;
                _trayIcon.LeftClick -= OnTrayLeftClick;
                _trayIcon.LeftDoubleClick -= OnTrayLeftDoubleClick;
                _trayIcon.RightClick -= OnTrayRightClick;
                _trayIcon.RefreshNeeded -= RequestTrayRefresh;
                _trayIcon.BalloonClicked -= OnUpdateBalloonClicked;
            }

            Safe.Dispose(_trayIcon);
            _trayIcon = null;

            Safe.Dispose(_trayIconRenderer);
            _trayIconRenderer = null;

            if (_settingsWindow != null)
            {
                _settingsWindow.Closed -= OnSettingsWindowClosed;
                try { _settingsWindow.Close(); }
                catch { }

                _settingsWindow = null;
            }

            TADNLog.Flush();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanAvaloniaApp.ShutdownServices: {ex}");
        }
    }

    private void ExitApplication()
    {
        TADNLog.Log("FanAvaloniaApp.ExitApplication");
        ShutdownServices();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static string L(string key, string fallback)
    {
        try
        {
            string value = LocalizationManager.Instance[key];
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
