#if HOTAVALONIA_ENABLE
using HotAvalonia;
#endif
#if DEBUG
using AppThemeHotReload = TrayAppDotNETCommon.Visuals.AppThemeHotReload;
using GlyphCatalogHotReload = TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload;
#endif
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using NetworkTrayAppDotNET.Interop;
using NetworkTrayAppDotNET.Models;
using NetworkTrayAppDotNET.Services;
using NetworkTrayAppDotNET.UI.Settings;

namespace NetworkTrayAppDotNET;

internal static class NetworkAvaloniaRunner
{
    // Defaults to the old software-rendering mitigation unless the user selects GPU preferred
    public static int Run(string[] args) =>
        TrayAppDotNETAppRunner.Run<NetworkAvaloniaApp>(
            args,
            AppSettings.GetDefaultPath,
            TrayAppDotNETRenderingBackend.Software,
            Configure);

    private static AppBuilder Configure(AppBuilder builder)
    {
#if HOTAVALONIA_ENABLE
        builder = builder.UseHotReload();
#endif
        return builder;
    }
}

internal sealed class NetworkAvaloniaApp : Application
{
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private NetworkSettingsWindow? _settingsWindow;
    private TrayIconPresenter<TrayIconRenderInput>? _trayIconPresenter;
    private TrayMenuController? _trayMenu;
    private AppSettings? _settings;
    private AppTheme? _theme;
    private TrayAppDotNETAppHost<AppSettings, AppTheme>? _host;
    private GlobalHotkeyService<NetworkHotkeyAction, NetworkHotkeyBinding>? _hotkeyService;
    private NetworkMonitor? _networkMonitor;
    private NetworkTrayIcon? _networkIconRenderer;
    private DispatcherTimer? _refreshTimer;
    private bool _shuttingDown;

    public override void Initialize() => TrayAppDotNETAvalonia.InitializeDefaults(this);

    public override void OnFrameworkInitializationCompleted()
    {
        TrayAppDotNETAppHost<AppSettings, AppTheme> host = new(this, CreateHostOptions());
        _host = host;
        host.InitializeShell();
        if (host.TryRunAuxiliaryMode())
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

#if DEBUG
        GlyphCatalogHotReload.ResourcesReloaded += OnGlyphCatalogResourcesReloaded;
        AppThemeHotReload.ResourcesReloaded += OnAppThemeResourcesReloaded;
#endif
        host.BeginTrayMode(ConfigureSettings, ConfigureTheme);
        _settings = host.Settings;
        StartServices();
        CreateTrayIcon();
        host.StartCommonServices(new TrayAppDotNETAppHostStartOptions
        {
            ShutdownApp = ShutdownServices,
            ShowUpdateNotification = (title, body) => _trayIcon?.ShowBalloon(title, body)
        });
        AppServices.UpdateCheckService = host.UpdateService;
        RequestTrayRefresh();
        ScheduleKeepWarmPriming();
        base.OnFrameworkInitializationCompleted();
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
            GetThemePath = AppTheme.GetDefaultPath,
            LoadTheme = AppTheme.LoadOrDefault,
            LoadAppIcon = static () => AppServices.AppIcon.LoadWindowIcon(),
            CreatePalette = static (theme, settings) =>
                AppSettingsPalette.Create(theme, settings, AppTheme.ResolveIsLight(settings, theme)),
            SaveSettings = static settings => settings.Save(),
            SaveSettingsOnExit = static settings => settings.SaveOnExit()
        };

    private void ConfigureSettings(AppSettings settings)
    {
        settings.Changed += OnSettingsChanged;
        AppServices.Settings = settings;
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
            _networkIconRenderer = new NetworkTrayIcon(_theme) { IsLightTheme = ResolveEffectiveIsLightTheme() };
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
                Interval = TimeSpan.FromMilliseconds(TimeConstants.NetworkPollIntervalMs)
            };
            _refreshTimer.Tick += OnRefreshTimerTick;
            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkAvaloniaApp refresh timer init failed: {ex}");
        }

        _hotkeyService = _host?.StartHotkeyService(
            static prefix => new GlobalHotkeyService<NetworkHotkeyAction, NetworkHotkeyBinding>(prefix),
            service =>
            {
                service.Initialize();
                service.Fired += OnHotkeyFired;
                service.Apply(_settings?.Hotkeys ?? []);
            });
        AppServices.HotkeyService = _hotkeyService;
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayAppDotNETShellTrayIcon(Constants.TrayIconGUID, Program.ApplicationName + ".TrayIcon")
        {
            IsVisible = true
        };
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.LeftDoubleClick += OnTrayLeftDoubleClick;

        // Enter or Space on the keyboard-focused icon opens the same network UI a click does
        _trayIcon.KeySelect += OpenNetworkFlyout;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.RefreshNeeded += RequestTrayRefresh;
        _trayIcon.BalloonClicked += OnUpdateBalloonClicked;
        _trayIconPresenter = new TrayIconPresenter<TrayIconRenderInput>(
            _trayIcon,
            _networkIconRenderer,
            static () => AppServices.AppIcon.LoadTrayIcon());
        _trayMenu = new TrayMenuController(
            CreateTrayMenuWindow,
            () => _settings?.KeepTrayContextMenuWarm ?? true,
            _settings,
            _theme,
            TADNLog.Log);
    }

    private void OnHotkeyFired(object? sender, HotkeyFiredEventArgs<NetworkHotkeyAction> e)
    {
        try { HandleHotkey(e.Action); }
        catch (Exception ex) { TADNLog.Log($"NetworkAvaloniaApp.OnHotkeyFired: {ex}"); }
    }

    private void HandleHotkey(NetworkHotkeyAction action)
    {
        switch (action)
        {
            case NetworkHotkeyAction.OpenSettings:
                OpenSettings();
                break;
            case NetworkHotkeyAction.OpenFlyout:
                OpenNetworkFlyout();
                break;
            case NetworkHotkeyAction.OpenNetworkSettings:
                OpenNetworkSettings();
                break;
            case NetworkHotkeyAction.OpenAdapterSettings:
                OpenAdapterSettings();
                break;
        }
    }

    private void OnNetworkStateChanged(NetworkIconState state) =>
        Dispatcher.UIThread.Post(RequestTrayRefresh);

    private void OnUpdateBalloonClicked()
    {
        if (_host?.UpdateService?.AvailableUpdate == null) return;

        OpenSettings(NetworkSettingsPage.About);
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e) => _networkMonitor?.RequestRefresh();

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
            if (_settings != null) ApplyToolTipDelayToOpenWindows();

            ApplyNetworkColorOverrides();
            if (_hotkeyService != null && _settings != null) _hotkeyService.Apply(_settings.Hotkeys);
            ApplyKeepWarmPolicies();
            RequestTrayRefresh();
        });
    }

    private void ApplyKeepWarmPolicies() => _trayMenu?.ApplyKeepWarmPolicy();

    private void ApplyToolTipDelayToOpenWindows()
    {
        if (_settingsWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_settingsWindow);
        if (_trayMenu?.Current is { } trayMenu) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(trayMenu);
    }

    private bool ResolveEffectiveIsLightTheme() => AppTheme.ResolveIsLight(_settings, _theme);

    private void ApplyThemeVariant()
    {
        RequestedThemeVariant = ResolveEffectiveIsLightTheme()
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

#if DEBUG
    /// <summary>
    /// Renders the tray icon again so tray glyph edits are visible immediately.
    /// </summary>
    private void OnGlyphCatalogResourcesReloaded()
    {
        _trayIconPresenter?.Invalidate();
        RequestTrayRefresh();
    }

    private void OnAppThemeResourcesReloaded() => _settings?.RaiseChangedForThemeReload();
#endif

    private void RequestTrayRefresh()
    {
        if (_trayIconPresenter == null) return;

        if (_networkMonitor != null) _networkIconRenderer?.State = _networkMonitor.CurrentState;
        _trayIconPresenter.Refresh(
            _networkMonitor?.GetTooltipText() ?? L(nameof(AppStrings.Tray_Tooltip_Default)));
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

        ExecuteTrayAction(TrayClickModifiers.Resolve(
            _settings.TrayCtrlDoubleLeftClickAction,
            _settings.TrayAltDoubleLeftClickAction,
            _settings.TrayDoubleClickAction,
            TrayClickAction.Nothing));
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

    private bool TryRunModifiedClickAction(TrayClickAction? ctrl, TrayClickAction? alt)
    {
        TrayClickAction action = TrayClickModifiers.Resolve(
            ctrl ?? TrayClickAction.Nothing,
            alt ?? TrayClickAction.Nothing,
            TrayClickAction.Nothing,
            TrayClickAction.Nothing);
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
        if (_trayIcon == null || _trayMenu == null) return;

        _trayMenu.Show(
            _trayIcon,
            cursorPoint,
            _settings?.ContextMenuPosition ?? TrayAppDotNETContextMenuPosition.Modern);
    }

    private TrayMenuWindow CreateTrayMenuWindow()
    {
        AppSettings settings = _settings
                               ?? throw new InvalidOperationException("Network tray menu requires settings.");
        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry(L(nameof(AppStrings.Tray_NetworkSettings)), OpenNetworkSettings)
        {
            Node = UI.ControlMap.TrayMenu.OpenNetworkSettings
        });
        entries.Add(new ContextMenuEntry(L(nameof(AppStrings.Tray_AdapterSettings)), OpenAdapterSettings)
        {
            Node = UI.ControlMap.TrayMenu.OpenAdapterSettings
        });

        // "App Settings" tells this app's settings apart from Windows' network settings above it
        return new TrayMenuWindow(
            entries,
            TrayMenuWindowOptions.Create(
                settings,
                _theme,
                UI.ControlMap.TrayMenu.ID,
                TrayMenuStandardEntry.Settings(
                    () => OpenSettings(),
                    UI.ControlMap.TrayMenu.OpenSettings,
                    L(nameof(AppStrings.Tray_AppSettings))),
                TrayMenuStandardEntry.Exit(ExitApplication, UI.ControlMap.TrayMenu.Exit)));
    }

    private void ScheduleKeepWarmPriming()
    {
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                if (_trayMenu != null)
                    await _trayMenu.PrimeAsync();
            }
            catch (Exception ex)
            {
                TADNLog.Log($"NetworkAvaloniaApp.ScheduleKeepWarmPriming: {ex.Message}");
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void ApplyNetworkColorOverrides()
    {
        if (_networkIconRenderer == null || _settings == null) return;

        bool isLight = ResolveEffectiveIsLightTheme();
        _networkIconRenderer.IsLightTheme = isLight;
        _networkIconRenderer.IconStyle = _settings.TrayIconStyle;
        if (_settings.TrayIconStyle == TrayAppDotNETTrayIconStyle.Static)
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
        FlyoutStyle flyoutStyle = _settings?.FlyoutStyle ?? AppSettings.DefaultFlyoutStyle;

        bool success = flyoutStyle switch
        {
            FlyoutStyle.Windows10 => ShellFlyout.ShowNetworkFlyoutWin10(),
            FlyoutStyle.Windows11 => ShellFlyout.ShowControlCenter(),
            FlyoutStyle.QuickSettings => TryOpenUri("ms-actioncenter:controlcenter/&showFooter=true"),
            FlyoutStyle.AvailableNetworks => TryOpenUri("ms-availablenetworks:"),
            FlyoutStyle.Settings => TryOpenUri("ms-settings:network-wifi"),
            _ => false
        };

        if (!success) TryOpenUri("ms-availablenetworks:");
    }

    // Explorer opens the link, so an elevated instance does not start Settings elevated
    private static bool TryOpenUri(string uri)
    {
        if (ExplorerProcessLauncher.TryShellExecute(
                uri,
                arguments: null,
                workingDirectory: null,
                verb: null,
                out int errorCode,
                out string errorMessage))
            return true;

        TADNLog.Log($"NetworkAvaloniaApp.TryOpenUri({uri}): {errorMessage} (0x{errorCode:X8})");
        return false;
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
    }

    private Task<bool> ShowUninstallerWindow(InstallScope scope, IProgress<TrayAppDotNETInstallProgress>? progress) =>
        _host?.ShowUninstallerWindow(scope, progress, _settingsWindow) ?? Task.FromResult(false);

    private void ShutdownServices()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
#if DEBUG
        GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded;
        AppThemeHotReload.ResourcesReloaded -= OnAppThemeResourcesReloaded;
#endif

        try
        {
            _hotkeyService = null;
            AppServices.HotkeyService = null;
            AppServices.UpdateCheckService = null;

            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Tick -= OnRefreshTimerTick;
                _refreshTimer = null;
            }

            AdapterSettingsShellMonitor.Shutdown();

            if (_networkMonitor != null)
            {
                _networkMonitor.NetworkStateChanged -= OnNetworkStateChanged;
                Safe.Dispose(_networkMonitor);
                _networkMonitor = null;
            }

            if (_settings != null) _settings.Changed -= OnSettingsChanged;
            AppServices.Settings = null;

            if (_theme != null)
            {
                _theme.ThemeChanged -= OnThemeChanged;
                _theme = null;
                AppServices.Theme = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.LeftClick -= OnTrayLeftClick;
                _trayIcon.LeftDoubleClick -= OnTrayLeftDoubleClick;
                _trayIcon.KeySelect -= OpenNetworkFlyout;
                _trayIcon.RightClick -= OnTrayRightClick;
                _trayIcon.RefreshNeeded -= RequestTrayRefresh;
                _trayIcon.BalloonClicked -= OnUpdateBalloonClicked;
            }

            Safe.Dispose(_trayMenu);
            _trayMenu = null;
            Safe.Dispose(_trayIconPresenter);
            _trayIconPresenter = null;
            _networkIconRenderer = null;
            Safe.Dispose(_trayIcon);
            _trayIcon = null;

            if (_settingsWindow != null)
            {
                _settingsWindow.Closed -= OnSettingsWindowClosed;
                try { _settingsWindow.Close(); }
                catch (Exception ex)
                {
                    TADNLog.Log($"NetworkAvaloniaApp settings window close failed: {ex.Message}");
                }

                _settingsWindow = null;
            }

            TADNLog.Flush();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkAvaloniaApp.ShutdownServices: {ex}");
        }
    }

    private void ExitApplication() => _host?.Exit();

    private static string L(string key)
    {
        try
        {
            return LocalizationManager.Instance[key];
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NetworkAvaloniaApp.L({key}): {ex.Message}");
            return key;
        }
    }
}
