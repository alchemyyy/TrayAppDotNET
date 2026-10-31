#if HOTAVALONIA_ENABLE
using HotAvalonia;
#endif
#if DEBUG
using AppThemeHotReload = TrayAppDotNETCommon.Visuals.AppThemeHotReload;
using GlyphCatalogHotReload = TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload;
#endif
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using FanControlTrayAppDotNET.Services;
using FanControlTrayAppDotNET.UI.Flyout;
using FanControlTrayAppDotNET.UI.Settings;
using FanControlTrayAppDotNET.UI.Tray;
using TrayAppDotNETCommon.UI.WarmWindows;
using FanHotkeyAction = TrayAppDotNETCommon.Models.HotkeyAction;
using FanHotkeyFiredEventArgs =
    TrayAppDotNETCommon.Services.HotkeyFiredEventArgs<TrayAppDotNETCommon.Models.HotkeyAction>;
using FanHotkeyService =
    TrayAppDotNETCommon.Services.GlobalHotkeyService<TrayAppDotNETCommon.Models.HotkeyAction,
        TrayAppDotNETCommon.Models.HotkeyBinding>;
using FanInstallScope = TrayAppDotNETCommon.Models.InstallScope;

namespace FanControlTrayAppDotNET;

internal static class FanAvaloniaRunner
{
    public static int Run(string[] args) =>
        TrayAppDotNETAppRunner.Run<FanAvaloniaApp>(args, AppSettings.GetDefaultPath, configure: Configure);

    private static AppBuilder Configure(AppBuilder builder)
    {
#if HOTAVALONIA_ENABLE
        builder = builder.UseHotReload();
#endif
        return builder;
    }
}

internal sealed class FanAvaloniaApp : Application
{
    private TrayAppDotNETAppHost<AppSettings, AppTheme>? _host;
    private readonly CancellationTokenSource _hardwareStartCancellation = new();
    private AppTheme? _theme;
    private AppSettings? _settings;
    private LHMService? _lhmService;

    // An LHM service still opening the hardware; disposing it at exit waits for the open, then hands the fans back
    private LHMService? _startingLHMService;
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private FanTrayIcon? _trayIconRenderer;
    private TrayIconPresenter<TrayIconRenderInput>? _trayIconPresenter;
    private TrayMenuController? _trayMenu;
    private TrayWindowToggle? _trayToggle;
    private FanFlyoutWindow? _fanFlyout;
    private FanSettingsWindow? _settingsWindow;
    private SettingsFlyoutKeepOpenCoordinator? _settingsFlyoutKeepOpen;
    private TrayAppDotNETWarmWindowSlot<FanFlyoutWindow>? _fanFlyoutWarmSlot;
    private FanHotkeyService? _hotkeyService;

    // The tray has one balloon slot and one click event; this says what the last balloon was about.
    private bool _lastBalloonIsUpdate;
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
        // The icon shows before any driver or hardware work, which can take seconds or wait on a UAC prompt
        CreateTrayIcon();
        host.StartCommonServices(new TrayAppDotNETAppHostStartOptions
        {
            ShutdownApp = ShutdownServices,
            ShowUpdateNotification = ShowUpdateNotification
        });
        AppServices.UpdateCheckService = host.UpdateService;
        RequestTrayRefresh();
        _ = StartHardwareAsync();
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
            ReadSettingsWithoutSaving = AppSettings.ReadOrDefault<AppSettings>,
            CreateDefaultSettings = static () => new AppSettings(),
            GetThemePath = AppTheme.GetDefaultPath,
            LoadTheme = AppTheme.LoadOrDefault,
            LoadAppIcon = static () => AppServices.AppIcon.LoadWindowIcon(),
            CreatePalette = static (theme, settings) =>
                AppSettingsPalette.Create(theme, settings, AppTheme.ResolveIsLight(settings, theme)),
            SaveSettings = static settings => settings.Save(),
            SaveSettingsOnExit = static settings => settings.SaveOnExit()
        };

    /// <summary>
    /// Makes sure PawnIO is installed, then opens the hardware, all off the UI thread. The flyout and the warm windows
    /// follow once the fans are known.
    /// </summary>
    private async Task StartHardwareAsync()
    {
        CancellationToken cancellationToken = _hardwareStartCancellation.Token;
        try
        {
            await Task.Run(
                () => PawnIoDriverInstaller.EnsureInstalledAsync(userRequested: false, cancellationToken),
                cancellationToken);
            await StartLHMServiceAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The app is exiting
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanAvaloniaApp hardware start failed: {ex}");
        }

        if (_shuttingDown) return;

        RequestTrayRefresh();
        RestoreStartupUndockedFlyoutIfRequested();
        ScheduleKeepWarmPriming();
    }

    private async Task StartLHMServiceAsync(CancellationToken cancellationToken)
    {
        if (_settings == null || _shuttingDown) return;

        LHMService service = new(Dispatcher.UIThread, _settings);
        _startingLHMService = service;
        try
        {
            await service.StartAsync(cancellationToken);
        }
        catch
        {
            if (ReferenceEquals(_startingLHMService, service))
            {
                _startingLHMService = null;
                Safe.Dispose(service);
            }

            throw;
        }

        if (!ReferenceEquals(_startingLHMService, service)) return;

        _startingLHMService = null;
        service.PollTickCompleted += RequestTrayRefresh;
        service.FanSpeedWarningRaised += OnFanSpeedWarningRaised;
        _lhmService = service;
        AppServices.LHMService = service;
    }

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
            _trayIconRenderer = new FanTrayIcon(_theme) { IsLightTheme = ResolveEffectiveIsLightTheme() };
            ApplyTrayIconColorOverride();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanAvaloniaApp tray renderer init failed: {ex}");
        }

        _hotkeyService = _host?.StartHotkeyService(
            static prefix => new FanHotkeyService(prefix),
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
        _trayIcon.KeySelect += OnTrayKeySelect;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.RefreshNeeded += RequestTrayRefresh;
        _trayIcon.BalloonClicked += OnTrayBalloonClicked;
        _trayIconPresenter = new TrayIconPresenter<TrayIconRenderInput>(
            _trayIcon,
            _trayIconRenderer,
            static () => AppServices.AppIcon.LoadTrayIcon());
        _trayToggle = new TrayWindowToggle();
        _trayMenu = new TrayMenuController(
            CreateTrayMenuWindow,
            () => _settings?.KeepTrayContextMenuWarm ?? true,
            _settings,
            _theme,
            TADNLog.Log);
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
        _trayMenu?.ApplyKeepWarmPolicy();
    }

    private void ShowUpdateNotification(string title, string body)
    {
        _lastBalloonIsUpdate = true;
        _trayIcon?.ShowBalloon(title, body);
    }

    private void OnFanSpeedWarningRaised(Fan fan, FanSpeedWarning warning)
    {
        if (_trayIcon == null || warning == FanSpeedWarning.None) return;

        (string measured, string threshold) = FanSpeedWarnings.Describe(fan, warning);
        string format = warning == FanSpeedWarning.Low
            ? L(nameof(AppStrings.Notification_SpeedWarningLow_BodyFormat))
            : L(nameof(AppStrings.Notification_SpeedWarningHigh_BodyFormat));
        _lastBalloonIsUpdate = false;
        _trayIcon.ShowBalloon(
            L(nameof(AppStrings.Notification_SpeedWarning_Title)),
            string.Format(CultureInfo.CurrentCulture, format, fan.DisplayName, measured, threshold));
    }

    private void OnTrayBalloonClicked()
    {
        if (!_lastBalloonIsUpdate)
        {
            ShowFanFlyout();
            return;
        }

        if (_host?.UpdateService?.AvailableUpdate == null) return;
        OpenSettings(FanSettingsPage.About);
    }

    private bool ResolveEffectiveIsLightTheme() => AppTheme.ResolveIsLight(_settings, _theme);

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

    private void RequestTrayRefresh() => _trayIconPresenter?.Refresh(BuildTrayTooltip());

    private string BuildTrayTooltip()
    {
        string header = L(nameof(AppStrings.Tray_Tooltip_Default));
        List<string> lines = [header];

        bool showCPU = _settings?.ShowCPUTempInTooltip ?? true;
        bool showGPU = _settings?.ShowGPUTempInTooltip ?? true;

        if (showCPU && TryGetTempC("CPU") is { } cpuC)
            lines.Add(string.Format(L(nameof(AppStrings.Tray_Tooltip_CPUTemp_Format)), cpuC));

        if (showGPU && TryGetTempC("GPU") is { } gpuC)
            lines.Add(string.Format(L(nameof(AppStrings.Tray_Tooltip_GPUTemp_Format)), gpuC));

        return string.Join(separator: '\n', lines);
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

            if (source.UserDefinedName.Contains(value: "Package", StringComparison.OrdinalIgnoreCase)
                || source.DataSourceKey.Contains(value: "Package", StringComparison.OrdinalIgnoreCase))
                best = source;
        }

        return best == null ? null : (int)Math.Round(best.Value / 1000.0);
    }

    private void OnTrayLeftClick()
    {
        if (TryDispatchTrayAction(TrayClickModifiers.Resolve(
                _settings?.TrayCtrlLeftClickAction ?? TrayClickAction.Nothing,
                _settings?.TrayAltLeftClickAction ?? TrayClickAction.Nothing,
                TrayClickAction.Nothing,
                TrayClickAction.Nothing)))
            return;

        switch (_trayToggle?.Decide(_fanFlyout) ?? TrayToggleAction.Show)
        {
            case TrayToggleAction.Hide:
                _fanFlyout?.Hide();
                break;
            // A floating flyout behind another window comes forward where it is instead of docking again
            case TrayToggleAction.Activate:
                _fanFlyout?.Activate();
                break;
            case TrayToggleAction.Show:
                ShowFanFlyout();
                break;
        }
    }

    private void OnTrayLeftDoubleClick()
    {
        TrayClickAction action = TrayClickModifiers.Resolve(
            _settings?.TrayCtrlDoubleLeftClickAction ?? TrayClickAction.Nothing,
            _settings?.TrayAltDoubleLeftClickAction ?? TrayClickAction.Nothing,
            _settings?.TrayDoubleClickAction ?? TrayClickAction.OpenSettings,
            TrayClickAction.Nothing);
        if (!TryDispatchTrayAction(action))
            ShowFanFlyout();
    }

    // The classic notification area may report one Enter twice, so the keyboard only shows the flyout
    private void OnTrayKeySelect() => ShowFanFlyout();

    private void OnTrayRightClick(Point point) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (TryDispatchTrayAction(TrayClickModifiers.Resolve(
                    _settings?.TrayCtrlRightClickAction ?? TrayClickAction.Nothing,
                    _settings?.TrayAltRightClickAction ?? TrayClickAction.Nothing,
                    TrayClickAction.Nothing,
                    TrayClickAction.Nothing)))
                return;

            ShowTrayContextMenu(point);
        });

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
        if (_trayIcon == null || _settings == null || _trayMenu == null) return;

        PixelPoint cursorPoint = new((int)Math.Round(point.X), (int)Math.Round(point.Y));
        _trayMenu.Show(_trayIcon, cursorPoint, _settings.ContextMenuPosition);
    }

    private TrayMenuWindow CreateTrayMenuWindow()
    {
        if (_settings == null)
            throw new InvalidOperationException("Fan tray menu requires settings.");

        return new TrayMenuWindow(
            new ContextMenuEntryBuilder(),
            TrayMenuWindowOptions.Create(
                _settings,
                _theme,
                UI.ControlMap.TrayMenu.ID,
                TrayMenuStandardEntry.Settings(OpenSettings, UI.ControlMap.TrayMenu.Settings),
                TrayMenuStandardEntry.Exit(ExitApplication, UI.ControlMap.TrayMenu.Exit)));
    }

    private void ShowFanFlyout(bool activate = true, bool holdOpenForSettings = true)
    {
        if (_settings == null || _trayIcon == null) return;

        _fanFlyout ??= FanFlyoutWarmSlot.TakeOrCreate(CreateManagedFanFlyout);

        _fanFlyout.Redock();
        _fanFlyout.ShowAt(_trayIcon, activate);
        if (holdOpenForSettings && _settingsWindow is { IsVisible: true })
            SettingsFlyoutKeepOpen.HoldOpen();
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

    private void RestoreStartupUndockedFlyoutIfRequested()
    {
        if (_settings == null || _trayIcon == null) return;
        if (!FlyoutDockingController.ShouldRestoreOnStartup(_settings)) return;

        _fanFlyout ??= FanFlyoutWarmSlot.TakeOrCreate(CreateManagedFanFlyout);
        _fanFlyout.ShowAt(_trayIcon);
    }

    private TrayAppDotNETWarmWindowSlot<FanFlyoutWindow> FanFlyoutWarmSlot =>
        _fanFlyoutWarmSlot ??= new TrayAppDotNETWarmWindowSlot<FanFlyoutWindow>(
            () => _settings?.KeepFlyoutWarm ?? true,
            ex => TADNLog.Log($"Fan flyout keep-warm: {ex.Message}"));

    private void ScheduleKeepWarmPriming()
    {
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                if (_settings?.KeepFlyoutWarm == true)
                    await FanFlyoutWarmSlot.PrimeAsync(CreateManagedFanFlyout);
                if (_trayMenu != null)
                    await _trayMenu.PrimeAsync();
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
            () => ShowFanFlyout(activate: false, holdOpenForSettings: false));

    private void OnFanFlyoutClosed(object? sender, EventArgs e)
    {
        if (sender is FanFlyoutWindow flyout) flyout.Closed -= OnFanFlyoutClosed;
        if (ReferenceEquals(_fanFlyout, sender)) _fanFlyout = null;
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
    }

    private Task<bool> ShowUninstallerWindow(
        FanInstallScope scope,
        IProgress<TrayAppDotNETInstallProgress>? progress) =>
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
            _hardwareStartCancellation.Cancel();
            _hotkeyService = null;
            AppServices.HotkeyService = null;
            AppServices.UpdateCheckService = null;

            CloseManagedWindowsForShutdown();

            if (_settings != null) _settings.Changed -= OnSettingsChanged;
            AppServices.Settings = null;

            // Waits for a hardware open still in progress, then closes it, which hands the fans back to firmware
            Safe.Dispose(_startingLHMService);
            _startingLHMService = null;

            if (_lhmService != null)
            {
                _lhmService.PollTickCompleted -= RequestTrayRefresh;
                _lhmService.FanSpeedWarningRaised -= OnFanSpeedWarningRaised;
                Safe.Dispose(_lhmService);
                _lhmService = null;
                AppServices.LHMService = null;
            }

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
                _trayIcon.KeySelect -= OnTrayKeySelect;
                _trayIcon.RightClick -= OnTrayRightClick;
                _trayIcon.RefreshNeeded -= RequestTrayRefresh;
                _trayIcon.BalloonClicked -= OnTrayBalloonClicked;
            }

            Safe.Dispose(_trayIconPresenter);
            _trayIconPresenter = null;
            _trayIconRenderer = null;
            Safe.Dispose(_trayToggle);
            _trayToggle = null;
            Safe.Dispose(_trayIcon);
            _trayIcon = null;

            TADNLog.Flush();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanAvaloniaApp.ShutdownServices: {ex}");
        }
    }

    /// <summary>
    /// Closes UI windows before app services they subscribe to are disposed.
    /// </summary>
    private void CloseManagedWindowsForShutdown()
    {
        Safe.Dispose(_settingsFlyoutKeepOpen);
        _settingsFlyoutKeepOpen = null;

        if (_settingsWindow != null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            CloseWindowSafely(_settingsWindow, windowName: "settings window");
            _settingsWindow = null;
        }

        if (_fanFlyout != null)
        {
            _fanFlyout.Closed -= OnFanFlyoutClosed;
            CloseWindowSafely(_fanFlyout, windowName: "fan flyout");
            _fanFlyout = null;
        }

        Safe.Dispose(_trayMenu);
        _trayMenu = null;
        Safe.Dispose(_fanFlyoutWarmSlot);
        _fanFlyoutWarmSlot = null;
    }

    /// <summary>
    /// Closes a window while preserving shutdown progress if Avalonia rejects the close.
    /// </summary>
    private static void CloseWindowSafely(Window window, string windowName)
    {
        try { window.Close(); }
        catch (Exception ex)
        {
            TADNLog.Log($"FanAvaloniaApp failed to close {windowName}: {ex.Message}");
        }
    }

    private void ExitApplication() => _host?.Exit();

    private static string L(string key) => LocalizationManager.Instance[key];
}
