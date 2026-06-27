using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using HotAvalonia;
using Microsoft.Win32;
using NetworkTrayAppDotNET.Interop;
using NetworkTrayAppDotNET.Localization;
using NetworkTrayAppDotNET.Models;
using NetworkTrayAppDotNET.Services;
using NetworkTrayAppDotNET.UI;
using TrayAppDotNETCommon.UI.WarmWindows;
using CommonUser32 = TrayAppDotNETCommon.Interop.User32;

namespace NetworkTrayAppDotNET;

internal static class NetworkAvaloniaRunner
{
    public static int Run(string[] args)
    {
        return TrayAppDotNETAvalonia.StartWithExplicitShutdown<NetworkAvaloniaApp>(
            args,
            // Preserve the old WPF RenderMode.SoftwareOnly mitigation for the native network flyout path.
            builder =>
            {
                builder = builder.With(new Win32PlatformOptions
                {
                    RenderingMode = [Win32RenderingMode.Software],
                    CompositionMode = [Win32CompositionMode.RedirectionSurface],
                });

                builder = builder.UseHotReload();

                return builder;
            });
    }
}

internal sealed class NetworkAvaloniaApp : Application
{
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private NetworkTrayMenuWindow? _trayMenuWindow;
    private NetworkSettingsWindow? _settingsWindow;
    private TrayAppDotNETWarmWindowSlot<NetworkTrayMenuWindow>? _trayMenuWarmSlot;
    private AppSettings? _settings;
    private AppTheme? _theme;
    private GlobalHotkeyService? _hotkeyService;
    private WatcherMonitor? _watcherMonitor;
    private UpdateCheckService? _updateCheckService;
    private NetworkMonitor? _networkMonitor;
    private NetworkTrayIcon? _networkIconRenderer;
    private DispatcherTimer? _refreshTimer;
    private bool _shuttingDown;
    private int _lastNotifiedUpdateVersion;

    public override void Initialize()
    {
        TrayAppDotNETAvalonia.InitializeDefaults(
            this,
            toolTipShowDelayMs: TimeConstants.ToolTipShowDelayDefaultMs);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        TADNLog.Initialize();
        TADNLog.Log("NetworkAvaloniaApp.OnFrameworkInitializationCompleted");
        LocalizationManager.Instance.Initialize(Strings.ResourceManager, culture => Strings.Culture = culture);
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

    private static void WireCrashHandlers() => TrayAppDotNETAvalonia.WireCrashHandlers(TADNLog.Shutdown);

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
                LogSettingsLoadFailed = ex => TADNLog.Log($"NetworkAvaloniaApp settings load failed: {ex}"),
                GetThemePath = AppTheme.GetDefaultPath,
                LoadTheme = AppTheme.LoadOrDefault,
                ConfigureTheme = ConfigureTheme,
                LogThemeLoadFailed = ex => TADNLog.Log($"NetworkAvaloniaApp theme load failed: {ex}"),
            });

        _settings = loaded.Settings;
    }

    private void ConfigureSettings(AppSettings settings)
    {
        settings.Changed += OnSettingsChanged;
        AppServices.Settings = settings;
        TrayAppDotNETAnimationPolicy.Apply(this, settings.AnimationMode);
        TrayAppDotNETToolTip.ShowDelayMs = settings.ToolTipShowDelayMs;
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
            _networkIconRenderer = new NetworkTrayIcon(_theme) { IsLightTheme = ResolveEffectiveIsLightTheme(), };
            ApplyNetworkColorOverrides();

            _networkMonitor = new NetworkMonitor();
            _networkMonitor.NetworkStateChanged += OnNetworkStateChanged;
            _networkMonitor.Initialize();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkAvaloniaApp network monitor init failed: {ex}");
        }

        try
        {
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(TimeConstants.NetworkPollIntervalMs),
            };
            _refreshTimer.Tick += OnRefreshTimerTick;
            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkAvaloniaApp refresh timer init failed: {ex}");
        }

        try
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkAvaloniaApp display settings hook failed: {ex}");
        }

        try
        {
            _hotkeyService = new GlobalHotkeyService();
            _hotkeyService.Initialize();
            _hotkeyService.Fired += OnHotkeyFired;
            _hotkeyService.Apply(_settings?.Hotkeys ?? []);
            AppServices.HotkeyService = _hotkeyService;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkAvaloniaApp hotkey init failed: {ex}");
        }

        try
        {
            _watcherMonitor = TrayAppDotNETAvalonia.CreateWatcherMonitor(Program.WatcherPID, ExitApplication);
            _watcherMonitor.Start();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkAvaloniaApp watcher init failed: {ex}");
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
                TADNLog.Log($"NetworkAvaloniaApp update service init failed: {ex}");
            }
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayAppDotNETShellTrayIcon(Constants.TrayIconGUID, Program.ApplicationName + ".TrayIcon")
        {
            IsVisible = true,
        };
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.LeftDoubleClick += OnTrayLeftDoubleClick;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.RefreshNeeded += RequestTrayRefresh;
        _trayIcon.BalloonClicked += OnUpdateBalloonClicked;
    }

    private void OnHotkeyFired(object? sender, HotkeyFiredEventArgs e)
    {
        try { HandleHotkey(e.Action); }
        catch (Exception ex) { TADNLog.Log($"NetworkAvaloniaApp.OnHotkeyFired: {ex}"); }
    }

    private void HandleHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.OpenSettings:
                OpenSettings();
                break;
            case HotkeyAction.OpenFlyout:
                OpenNetworkFlyout();
                break;
            case HotkeyAction.OpenNetworkSettings:
                OpenNetworkSettings();
                break;
            case HotkeyAction.OpenAdapterSettings:
                OpenAdapterSettings();
                break;
        }
    }

    private void OnNetworkStateChanged(NetworkIconState state) =>
        Dispatcher.UIThread.Post(RequestTrayRefresh);

    private void OnUpdateStateChanged()
    {
        _settingsWindow?.StopAboutUpdateRefresh();

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

        OpenSettings(NetworkSettingsPage.About);
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        try { _networkMonitor?.RefreshState(); }
        catch (Exception ex) { TADNLog.Log($"NetworkAvaloniaApp refresh tick failed: {ex.Message}"); }

        RequestTrayRefresh();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            _networkIconRenderer?.InvalidateCache();
            RequestTrayRefresh();
        });

    private void OnThemeChanged(bool isLightTheme) =>
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeVariant();
            ApplyNetworkColorOverrides();
            RequestTrayRefresh();
        });

    private void OnSettingsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeVariant();
            if (_settings != null)
            {
                TrayAppDotNETAnimationPolicy.Apply(this, _settings.AnimationMode);
                TrayAppDotNETToolTip.ShowDelayMs = _settings.ToolTipShowDelayMs;
                ApplyToolTipDelayToOpenWindows();
            }

            ApplyNetworkColorOverrides();
            if (_hotkeyService != null && _settings != null) _hotkeyService.Apply(_settings.Hotkeys);
            ApplyKeepWarmPolicies();
            RequestTrayRefresh();
        });
    }

    private void ApplyKeepWarmPolicies()
    {
        if (_trayMenuWarmSlot != null || _settings?.KeepTrayContextMenuWarm == true)
            TrayMenuWarmSlot.ApplyKeepWarmPolicy(CreateTrayMenuWindow);
    }

    private void ApplyToolTipDelayToOpenWindows()
    {
        if (_settingsWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_settingsWindow);
        if (_trayMenuWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_trayMenuWindow);
    }

    private bool ResolveEffectiveIsLightTheme() => _settings?.ThemeMode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        _ => _theme?.IsLightTheme ?? false,
    };

    private void ApplyThemeVariant()
    {
        RequestedThemeVariant = ResolveEffectiveIsLightTheme()
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    private void RequestTrayRefresh()
    {
        if (_trayIcon == null) return;
        _trayIcon.SetTooltip(_networkMonitor?.GetTooltipText()
                             ?? L("Tray_Tooltip_Default", Constants.ApplicationName));

        if (_networkMonitor != null && _networkIconRenderer != null)
        {
            _networkIconRenderer.State = _networkMonitor.CurrentState;
            if (_networkIconRenderer.CreateIcon() is { } icon) _trayIcon.SetIcon(icon);
        }
        else if (AppTheme.LoadAppNativeIcon() is { } fallbackIcon)
        {
            using (fallbackIcon)
                _trayIcon.SetIcon(fallbackIcon);
        }
    }

    private void OnTrayLeftClick()
    {
        if (TryRunModifiedClickAction(
                _settings?.TrayCtrlLeftClickAction,
                _settings?.TrayAltLeftClickAction))
            return;

        OpenNetworkFlyout();
    }

    private void OnTrayLeftDoubleClick()
    {
        if (_settings == null) return;

        ExecuteTrayAction(ModifierOf(
            _settings.TrayCtrlDoubleLeftClickAction,
            _settings.TrayAltDoubleLeftClickAction,
            _settings.TrayDoubleClickAction));
    }

    private void OnTrayRightClick(Point point) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (TryRunModifiedClickAction(
                    _settings?.TrayCtrlRightClickAction,
                    _settings?.TrayAltRightClickAction))
                return;

            ShowTrayContextMenu(new PixelPoint((int)Math.Round(point.X), (int)Math.Round(point.Y)));
        });

    private static bool IsCtrlDown() => (CommonUser32.GetAsyncKeyState(CommonUser32.VK_CONTROL) & 0x8000) != 0;

    private static bool IsAltDown() => (CommonUser32.GetAsyncKeyState(CommonUser32.VK_MENU) & 0x8000) != 0;

    private static TrayClickAction ModifierOf(TrayClickAction ctrl, TrayClickAction alt, TrayClickAction fallback)
    {
        if (IsCtrlDown() && ctrl != TrayClickAction.Nothing) return ctrl;
        if (IsAltDown() && alt != TrayClickAction.Nothing) return alt;
        return fallback;
    }

    private bool TryRunModifiedClickAction(TrayClickAction? ctrl, TrayClickAction? alt)
    {
        TrayClickAction action = TrayClickAction.Nothing;
        if (IsCtrlDown() && ctrl is { } ctrlAction && ctrlAction != TrayClickAction.Nothing)
            action = ctrlAction;
        else if (IsAltDown() && alt is { } altAction && altAction != TrayClickAction.Nothing)
            action = altAction;

        if (action == TrayClickAction.Nothing) return false;

        ExecuteTrayAction(action);
        return true;
    }

    private void ExecuteTrayAction(TrayClickAction action)
    {
        switch (action)
        {
            case TrayClickAction.OpenSettings:
                OpenSettings();
                break;
            case TrayClickAction.OpenAdapterSettings:
                OpenAdapterSettings();
                break;
        }
    }

    private void ShowTrayContextMenu(PixelPoint cursorPoint)
    {
        if (_trayIcon == null) return;

        if (_trayMenuWindow is { IsVisible: true })
            _trayMenuWindow.DismissForWarmCache();

        NetworkTrayMenuWindow menuWindow = TrayMenuWarmSlot.TakeOrCreate(CreateTrayMenuWindow);
        _trayMenuWindow = menuWindow;

        ContextMenuPosition placement = _settings?.ContextMenuPosition ?? ContextMenuPosition.Classic;
        menuWindow.ShowAt(_trayIcon, cursorPoint, placement);
    }

    private NetworkTrayMenuWindow CreateTrayMenuWindow()
    {
        NetworkTrayMenuWindow menuWindow = new(
            TrayMenuPalette(),
            rounded: _settings?.EnableRoundedCorners ?? true,
            fontSize: _settings?.ContextMenuFontSize ?? 15,
            networkSettingsText: L("Tray_NetworkSettings", "Network settings"),
            adapterSettingsText: L("Tray_AdapterSettings", "Adapter settings"),
            settingsText: L("Tray_Settings", "Settings"),
            exitText: L("Tray_Exit", "Exit"),
            openNetworkSettings: OpenNetworkSettings,
            openAdapterSettings: OpenAdapterSettings,
            openSettings: () => OpenSettings(),
            exit: ExitApplication);
        _trayMenuWindow = menuWindow;
        menuWindow.Closed += OnTrayMenuClosed;
        return menuWindow;
    }

    private void OnTrayMenuClosed(object? sender, EventArgs e)
    {
        if (sender is NetworkTrayMenuWindow menu)
            menu.Closed -= OnTrayMenuClosed;
        if (ReferenceEquals(_trayMenuWindow, sender))
            _trayMenuWindow = null;
    }

    private TrayAppDotNETWarmWindowSlot<NetworkTrayMenuWindow> TrayMenuWarmSlot =>
        _trayMenuWarmSlot ??= new TrayAppDotNETWarmWindowSlot<NetworkTrayMenuWindow>(
            () => _settings?.KeepTrayContextMenuWarm ?? true,
            ex => TADNLog.Log($"Network tray menu keep-warm: {ex.Message}"));

    private void ScheduleKeepWarmPriming()
    {
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                if (_settings?.KeepTrayContextMenuWarm == true && _trayIcon != null)
                    await TrayMenuWarmSlot.PrimeAsync(CreateTrayMenuWindow);
            }
            catch (Exception ex)
            {
                TADNLog.Log($"NetworkAvaloniaApp.ScheduleKeepWarmPriming: {ex.Message}");
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private SettingsPalette TrayMenuPalette()
    {
        bool isLight = ResolveEffectiveIsLightTheme();
        return NetworkSettingsWindow.CreatePalette(_theme, _settings, isLight);
    }

    private void ApplyNetworkColorOverrides()
    {
        if (_networkIconRenderer == null || _settings == null) return;

        bool isLight = ResolveEffectiveIsLightTheme();
        _networkIconRenderer.IsLightTheme = isLight;
        _networkIconRenderer.IconStyle = _settings.TrayIconStyle;
        if (_settings.TrayIconStyle == TrayIconStyle.Static)
        {
            _networkIconRenderer.CustomColor = _settings.TrayIconColor.Resolve(isLight);
            _networkIconRenderer.ConnectedColorOverride = null;
            _networkIconRenderer.NoInternetColorOverride = null;
            _networkIconRenderer.DisconnectedColorOverride = null;
        }
        else
        {
            _networkIconRenderer.CustomColor = null;
            _networkIconRenderer.ConnectedColorOverride = _settings.NetworkConnectedColor.Resolve(isLight);
            _networkIconRenderer.NoInternetColorOverride = _settings.NetworkNoInternetColor.Resolve(isLight);
            _networkIconRenderer.DisconnectedColorOverride = _settings.NetworkDisconnectedColor.Resolve(isLight);
        }
    }

    private void OpenNetworkFlyout()
    {
        FlyoutStyle flyoutStyle = _settings?.FlyoutStyle ?? FlyoutStyle.AvailableNetworks;

        bool success = flyoutStyle switch
        {
            FlyoutStyle.Windows10 => ShellFlyout.ShowNetworkFlyoutWin10(),
            FlyoutStyle.Windows11 => ShellFlyout.ShowControlCenter(),
            FlyoutStyle.QuickSettings => TryOpenUri("ms-actioncenter:controlcenter/&showFooter=true"),
            FlyoutStyle.AvailableNetworks => TryOpenUri("ms-availablenetworks:"),
            FlyoutStyle.Settings => TryOpenUri("ms-settings:network-wifi"),
            _ => false,
        };

        if (!success) TryOpenUri("ms-availablenetworks:");
    }

    private static bool TryOpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void OpenNetworkSettings() => TryOpenUri("ms-settings:network");

    private void OpenAdapterSettings()
    {
        AdapterSettingsStyle style = _settings?.AdapterSettingsStyle ?? AdapterSettingsStyle.Explorer;

        try
        {
            switch (style)
            {
                case AdapterSettingsStyle.Explorer:
                    AdapterSettingsShellMonitor.OpenAndMonitorExplorerShell();
                    break;
                case AdapterSettingsStyle.ControlPanel:
                    AdapterSettingsShellMonitor.OpenAndMonitorControlPanel();
                    break;
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkAvaloniaApp.OpenAdapterSettings: {ex}");
        }
    }

    private void OpenSettings(NetworkSettingsPage? page = null)
    {
        if (_settings == null) return;

        if (_settingsWindow == null)
        {
            _settingsWindow = new NetworkSettingsWindow(_settings, ShowUninstallerWindow);
            _settingsWindow.Closed += OnSettingsWindowClosed;
        }

        if (page.HasValue) _settingsWindow.SelectPage(page.Value);
        _settingsWindow.ShowAtDefaultPositionAndActivate();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
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

    private void ShowUninstallerWindow(string installDir, InstallScope scope)
    {
        NetworkUninstallerWindow window = new(installDir, scope);
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

            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Tick -= OnRefreshTimerTick;
                _refreshTimer = null;
            }

            try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; }
            catch { }

            if (_networkMonitor != null)
            {
                _networkMonitor.NetworkStateChanged -= OnNetworkStateChanged;
                Safe.Dispose(_networkMonitor);
                _networkMonitor = null;
            }

            Safe.Dispose(_networkIconRenderer);
            _networkIconRenderer = null;

            if (_settings != null) _settings.Changed -= OnSettingsChanged;
            AppServices.Settings = null;

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

            Safe.Dispose(_trayMenuWarmSlot);
            _trayMenuWarmSlot = null;

            if (_trayIcon != null)
            {
                _trayIcon.LeftClick -= OnTrayLeftClick;
                _trayIcon.LeftDoubleClick -= OnTrayLeftDoubleClick;
                _trayIcon.RightClick -= OnTrayRightClick;
                _trayIcon.RefreshNeeded -= RequestTrayRefresh;
                _trayIcon.BalloonClicked -= OnUpdateBalloonClicked;
            }

            Safe.Dispose(_trayIcon);
            _trayIcon = null;

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
            TADNLog.Log($"NetworkAvaloniaApp.ShutdownServices: {ex}");
        }
    }

    private void ExitApplication()
    {
        TADNLog.Log("NetworkAvaloniaApp.ExitApplication");
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
