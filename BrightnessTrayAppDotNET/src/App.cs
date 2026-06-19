using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.Interop.NightLight;
using BrightnessTrayAppDotNET.Localization;
using BrightnessTrayAppDotNET.UI.Flyout;
using BrightnessTrayAppDotNET.UI.Settings;
using BrightnessTrayAppDotNET.UI.Tray;
using HotAvalonia;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Tray;
using TrayAppDotNETCommon.UI.WarmWindows;
using TrayAppDotNETCommon.Utils;
using BrightnessHotkeyFiredEventArgs =
    TrayAppDotNETCommon.Services.HotkeyFiredEventArgs<BrightnessTrayAppDotNET.Models.BrightnessHotkeyAction>;
using BrightnessHotkeyService =
    TrayAppDotNETCommon.Services.GlobalHotkeyService<BrightnessTrayAppDotNET.Models.BrightnessHotkeyAction,
        BrightnessTrayAppDotNET.Models.HotkeyBinding>;
using BrightnessInstallScope = TrayAppDotNETCommon.InstallScope;
using BrightnessUpdateCheckService = TrayAppDotNETCommon.Services.UpdateCheckService;
using BrightnessWatcherMonitor = TrayAppDotNETCommon.Services.WatcherMonitor;

namespace BrightnessTrayAppDotNET;

internal static class BrightnessAvaloniaRunner
{
    public static int Run(string[] args)
    {
        return TrayAppDotNETAvalonia.StartWithExplicitShutdown<BrightnessAvaloniaApp>(
            args,
            builder =>
            {
                builder = builder.UseHotReload();

                return builder;
            });
    }
}

internal sealed class BrightnessAvaloniaApp : Application
{
    private const int HotkeyStep = 2;

    private AppTheme? _theme;
    private AppSettings? _settings;
    private MonitorService? _monitorService;
    private DisplayEventManager? _displayEventManager;
    private DDCRecoveryService? _ddcRecoveryService;
    private MonitorBrightnessRangeProvider? _brightnessRangeProvider;
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private BrightnessTrayIcon? _trayIconRenderer;
    private BrightnessTrayMenuWindow? _trayMenuWindow;
    private BrightnessFlyoutWindow? _brightnessFlyout;
    private BrightnessSettingsWindow? _settingsWindow;
    private SettingsFlyoutKeepOpenCoordinator? _settingsFlyoutKeepOpen;
    private TrayAppDotNETWarmWindowSlot<BrightnessFlyoutWindow>? _brightnessFlyoutWarmSlot;
    private TrayAppDotNETWarmWindowSlot<BrightnessTrayMenuWindow>? _trayMenuWarmSlot;
    private BrightnessHotkeyService? _hotkeyService;
    private BrightnessWatcherMonitor? _watcherMonitor;
    private BrightnessUpdateCheckService? _updateCheckService;
    private Dictionary<string, double>? _restoreSnapshot;
    private TrayClickAction _appliedAction = TrayClickAction.Nothing;
    private string? _lastTrayValueDiagnostic;
    private DateTime _lastTrayValueDiagnosticUtc;
    private int _lastNotifiedUpdateVersion;
    private bool _suppressNextTrayClick;
    private bool _shuttingDown;

    public override void Initialize() => TrayAppDotNETAvalonia.InitializeDefaults(this);

    public override void OnFrameworkInitializationCompleted()
    {
        WPFLog.Initialize();
        WPFLog.Log("BrightnessAvaloniaApp.OnFrameworkInitializationCompleted");

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
        StartBrightnessRangeProvider();
        RestoreStartupUndockedFlyoutIfRequested();
        ScheduleKeepWarmPriming();

        base.OnFrameworkInitializationCompleted();
    }

    private void WireCrashHandlers()
    {
        TrayAppDotNETAvalonia.WireCrashHandlers(
            processExit: () =>
            {
                TryDrainQuickly(TimeSpan.FromMilliseconds(TimeConstants.ProcessExitDrainTimeoutMs));
                WPFLog.Shutdown();
            },
            unobservedTaskException: args =>
            {
                args.SetObserved();
                WPFLog.Log($"FATAL UnobservedTaskException: {args.Exception}");
                WPFLog.Flush();
            });
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
                LogSettingsLoadFailed = ex => WPFLog.Log($"BrightnessAvaloniaApp settings load failed: {ex}"),
                GetThemePath = AppTheme.GetDefaultPath,
                LoadTheme = AppTheme.LoadOrDefault,
                ConfigureTheme = ConfigureTheme,
                LogThemeLoadFailed = ex => WPFLog.Log($"BrightnessAvaloniaApp theme load failed: {ex}"),
            });

        _settings = loaded.Settings;

        try { NightLightProvider.Initialize(_settings); }
        catch (Exception ex) { WPFLog.Log($"BrightnessAvaloniaApp night-light init failed: {ex.Message}"); }

        ApplyPDBDownloadTimeout(_settings);
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
        ApplyThemeResources();
    }

    private void StartServices()
    {
        if (_settings == null) return;

        try
        {
            _monitorService = new MonitorService(new DisplayService(), _settings);
            AppServices.MonitorService = _monitorService;
            _monitorService.MonitorsRefreshed += OnMonitorsRefreshed;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessAvaloniaApp monitor service init failed: {ex}");
        }

        if (_monitorService != null)
        {
            try
            {
                _displayEventManager = new DisplayEventManager(_monitorService, ProfileManager.GetDefaultPath());
                _displayEventManager.DisplayTopologyChanged += OnDisplayTopologyChanged;
                _displayEventManager.Start();
                AppServices.DisplayEventManager = _displayEventManager;
            }
            catch (Exception ex)
            {
                WPFLog.Log($"BrightnessAvaloniaApp display event init failed: {ex}");
            }

            try
            {
                _ddcRecoveryService = new DDCRecoveryService(_monitorService);
                _ddcRecoveryService.Start();
                AppServices.DDCRecoveryService = _ddcRecoveryService;
            }
            catch (Exception ex)
            {
                WPFLog.Log($"BrightnessAvaloniaApp DDC recovery init failed: {ex}");
            }
        }

        try { AppServices.ProfileManager = new ProfileManager(); }
        catch (Exception ex) { WPFLog.Log($"BrightnessAvaloniaApp profile manager init failed: {ex}"); }

        try
        {
            _trayIconRenderer = new BrightnessTrayIcon(_theme) { IsLightTheme = ResolveEffectiveIsLightTheme(), };
            ApplyTrayIconSettings();
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessAvaloniaApp tray renderer init failed: {ex}");
        }

        try
        {
            _hotkeyService = new BrightnessHotkeyService(Program.ApplicationName + ".HotkeySink");
            _hotkeyService.Initialize();
            _hotkeyService.Fired += OnHotkeyFired;
            _hotkeyService.Apply(_settings.Hotkeys);
            AppServices.HotkeyService = _hotkeyService;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessAvaloniaApp hotkey init failed: {ex}");
        }

        try
        {
            _watcherMonitor = TrayAppDotNETAvalonia.CreateWatcherMonitor(Program.WatcherPID, ExitApplication);
            _watcherMonitor.Start();
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessAvaloniaApp watcher init failed: {ex}");
        }

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
            WPFLog.Log($"BrightnessAvaloniaApp update service init failed: {ex}");
        }
    }

    private void StartBrightnessRangeProvider()
    {
        if (_monitorService == null) return;

        try
        {
            _brightnessRangeProvider = new MonitorBrightnessRangeProvider(_monitorService);
            AppServices.MonitorBrightnessRangeProvider = _brightnessRangeProvider;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessAvaloniaApp range provider init failed: {ex}");
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayAppDotNETShellTrayIcon(Constants.TrayIconGUID, Program.ApplicationName + ".TrayIcon")
        {
            IsScrollEnabled = _settings?.TrayScrollEnabled ?? true,
        };
        if (AppTheme.LoadAppNativeIcon() is { } initialIcon)
        {
            using (initialIcon)
                _trayIcon.SetIcon(initialIcon);
        }

        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.LeftDoubleClick += OnTrayLeftDoubleClick;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.Scrolled += OnTrayScrolled;
        _trayIcon.RefreshNeeded += RequestTrayRefresh;
        _trayIcon.BalloonClicked += OnUpdateBalloonClicked;
        _trayIcon.IsVisible = true;
    }

    private BrightnessFlyoutWindow CreateFlyout()
    {
        if (_settings == null || _monitorService == null)
            throw new InvalidOperationException("Brightness flyout requires settings and monitor service.");

        BrightnessFlyoutWindow flyout = new(
            AppServices.ProfileManager ?? new ProfileManager(),
            _theme ?? AppTheme.Default,
            _monitorService);
        flyout.BrightnessUpdated += RequestTrayRefresh;
        flyout.FlyoutDeactivated += OnFlyoutDeactivated;
        flyout.SettingsRequested += OpenSettings;
        flyout.Closed += OnBrightnessFlyoutClosed;
        return flyout;
    }

    private BrightnessFlyoutWindow CreateManagedBrightnessFlyout()
    {
        BrightnessFlyoutWindow flyout = CreateFlyout();
        _brightnessFlyout = flyout;
        AppServices.BrightnessFlyout = flyout;
        return flyout;
    }

    private TrayAppDotNETWarmWindowSlot<BrightnessFlyoutWindow> BrightnessFlyoutWarmSlot =>
        _brightnessFlyoutWarmSlot ??= new TrayAppDotNETWarmWindowSlot<BrightnessFlyoutWindow>(
            () => _settings?.KeepFlyoutWarm ?? true,
            ex => WPFLog.Log($"BrightnessFlyout keep-warm: {ex.Message}"));

    private TrayAppDotNETWarmWindowSlot<BrightnessTrayMenuWindow> TrayMenuWarmSlot =>
        _trayMenuWarmSlot ??= new TrayAppDotNETWarmWindowSlot<BrightnessTrayMenuWindow>(
            () => _settings?.KeepTrayContextMenuWarm ?? true,
            ex => WPFLog.Log($"Brightness tray menu keep-warm: {ex.Message}"));

    private void ScheduleKeepWarmPriming()
    {
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                if (_settings?.KeepFlyoutWarm == true && _monitorService != null)
                    await BrightnessFlyoutWarmSlot.PrimeAsync(CreateManagedBrightnessFlyout);
                if (_settings?.KeepTrayContextMenuWarm == true && _trayIcon != null)
                    await TrayMenuWarmSlot.PrimeAsync(CreateTrayMenuWindow);
            }
            catch (Exception ex)
            {
                WPFLog.Log($"BrightnessAvaloniaApp.ScheduleKeepWarmPriming: {ex.Message}");
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void RestoreStartupUndockedFlyoutIfRequested()
    {
        if (_settings == null || _monitorService == null) return;

        if (!_settings.RestoreFlyoutUndockedOnStartup
            || !_settings.FlyoutUndocked
            || !_settings.FlyoutHasSavedPosition
            || !_settings.AllowFlyoutUndock)
            return;

        _brightnessFlyout ??= BrightnessFlyoutWarmSlot.TakeOrCreate(CreateManagedBrightnessFlyout);

        _brightnessFlyout.Show();
        _brightnessFlyout.Activate();
    }

    private void OnFlyoutDeactivated()
    {
        _suppressNextTrayClick = true;
        Dispatcher.UIThread.Post(
            () => _suppressNextTrayClick = false,
            DispatcherPriority.ContextIdle);
    }

    private void OnHotkeyFired(object? sender, BrightnessHotkeyFiredEventArgs e)
    {
        try { HandleHotkey(e.Action, e.Parameter); }
        catch (Exception ex) { WPFLog.Log($"BrightnessAvaloniaApp.OnHotkeyFired: {ex}"); }
    }

    private void HandleHotkey(BrightnessHotkeyAction action, string parameter)
    {
        switch (action)
        {
            case BrightnessHotkeyAction.OpenSettings:
                OpenSettings();
                break;
            case BrightnessHotkeyAction.OpenFlyout:
                ShowBrightnessFlyout();
                break;
            case BrightnessHotkeyAction.FullBright:
                ApplyOrRestoreBrightness(TrayClickAction.FullBright, 100);
                break;
            case BrightnessHotkeyAction.FullDim:
                ApplyOrRestoreBrightness(TrayClickAction.FullDim, 0);
                break;
            case BrightnessHotkeyAction.IncrementMasterBrightness:
                AdjustAllMonitorBrightness(HotkeyStep);
                break;
            case BrightnessHotkeyAction.DecrementMasterBrightness:
                AdjustAllMonitorBrightness(-HotkeyStep);
                break;
            case BrightnessHotkeyAction.ToggleNightLight:
                if (NightLightProvider.IsSupported()) NightLightProvider.Toggle();
                break;
            case BrightnessHotkeyAction.IncrementNightLight:
                AdjustNightLightBrightness(HotkeyStep);
                break;
            case BrightnessHotkeyAction.DecrementNightLight:
                AdjustNightLightBrightness(-HotkeyStep);
                break;
            case BrightnessHotkeyAction.NormalizeBrightnesses:
                _brightnessFlyout?.SyncAllIndividualsToMaster();
                break;
            case BrightnessHotkeyAction.PowerOffAllMonitors:
                PowerOffAllMonitors();
                break;
            case BrightnessHotkeyAction.ProfileSelect:
                if (int.TryParse(parameter, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int slot))
                    _brightnessFlyout?.SelectProfileByIndex(slot);
                break;
            case BrightnessHotkeyAction.MonitorOff:
                MonitorInfo? target = ResolveMonitorTarget(parameter);
                if (target != null) PowerOffMonitor(target);
                break;
        }
    }

    private void OnMonitorsRefreshed()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_shuttingDown) return;

            try { RequestTrayRefresh(); }
            catch (Exception ex) { WPFLog.Log($"BrightnessAvaloniaApp.OnMonitorsRefreshed tray refresh: {ex.Message}"); }

            if (_hotkeyService == null || _settings == null) return;

            try { _hotkeyService.Apply(_settings.Hotkeys); }
            catch (Exception ex) { WPFLog.Log($"BrightnessAvaloniaApp.OnMonitorsRefreshed hotkeys: {ex.Message}"); }
        });
    }

    private void AdjustAllMonitorBrightness(int delta)
    {
        if (_brightnessFlyout == null || _brightnessFlyout.Monitors.Count == 0) return;

        _brightnessFlyout.NotifyUserBrightnessAdjustment();
        foreach (MonitorInfo monitor in _brightnessFlyout.Monitors)
        {
            if (!monitor.IsParticipatingInMaster) continue;
            monitor.Brightness = Math.Clamp(monitor.Brightness + delta, 0, 100);
        }
    }

    private void AdjustNightLightBrightness(int delta)
    {
        if (_brightnessFlyout == null || !NightLightProvider.IsSupported()) return;

        _brightnessFlyout.NotifyUserNightLightAdjustment();
        MonitorInfo nightLight = _brightnessFlyout.NightLightMonitor;
        nightLight.Brightness = Math.Clamp(nightLight.Brightness + delta, 0, 100);
    }

    private MonitorInfo? ResolveMonitorTarget(string parameter)
    {
        if (_brightnessFlyout == null) return null;

        if (HotkeyTarget.TryParseDisplayNumber(parameter, out int displayNumber))
            return _brightnessFlyout.Monitors.FirstOrDefault(m => !m.IsMaster && m.DisplayNumber == displayNumber);

        return HotkeyTarget.TryParseEDID(parameter, out string EDIDKey)
            ? _brightnessFlyout.Monitors.FirstOrDefault(m => !m.IsMaster
                                                             && string.Equals(m.EDIDKey, EDIDKey,
                                                                 StringComparison.Ordinal))
            : null;
    }

    private void OnTrayLeftClick()
    {
        if (_suppressNextTrayClick)
        {
            _suppressNextTrayClick = false;
            return;
        }

        if (TryRunModifiedClickAction(
                _settings?.TrayCtrlLeftClickAction,
                _settings?.TrayAltLeftClickAction))
            return;

        ShowBrightnessFlyout();
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

    private void OnTrayScrolled(int wheelDelta)
    {
        if (_brightnessFlyout == null || _settings == null) return;

        TrayWheelTarget target = ResolveWheelTarget(_settings);
        if (target == TrayWheelTarget.Nothing) return;

        int notches = wheelDelta / 120;
        if (notches == 0) notches = Math.Sign(wheelDelta);
        int delta = notches * Math.Max(1, _settings.FlyoutScrollWheelStep);

        switch (target)
        {
            case TrayWheelTarget.NightLight:
                AdjustNightLightBrightness(delta);
                break;
            case TrayWheelTarget.Brightness:
                AdjustAllMonitorBrightness(delta);
                break;
        }
    }

    private static TrayWheelTarget ResolveWheelTarget(AppSettings settings)
    {
        if (IsCtrlDown()) return settings.TrayCtrlWheelAction;
        return IsAltDown() ? settings.TrayAltWheelAction : settings.TrayWheelAction;
    }

    private static bool IsCtrlDown() => (User32.GetAsyncKeyState(User32.VK_CONTROL) & 0x8000) != 0;
    private static bool IsAltDown() => (User32.GetAsyncKeyState(User32.VK_MENU) & 0x8000) != 0;

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
            case TrayClickAction.TurnOffAllDisplays:
                PowerOffAllMonitors();
                break;
            case TrayClickAction.TurnOnAllDisplays:
                PowerOnAllMonitors();
                break;
            case TrayClickAction.FullBright:
                ApplyOrRestoreBrightness(action, 100);
                break;
            case TrayClickAction.FullDim:
                ApplyOrRestoreBrightness(action, 0);
                break;
        }
    }

    private void ApplyOrRestoreBrightness(TrayClickAction action, int target)
    {
        if (_brightnessFlyout == null || _brightnessFlyout.Monitors.Count == 0) return;

        _brightnessFlyout.NotifyUserBrightnessAdjustment();
        List<MonitorInfo> monitors = [.. _brightnessFlyout.Monitors.Where(m => m.IsParticipatingInMaster)];
        if (monitors.Count == 0) return;

        bool stillInAppliedState =
            _restoreSnapshot != null
            && _appliedAction != TrayClickAction.Nothing
            && monitors.All(m => m.RoundedBrightness == TargetOf(_appliedAction));

        if (stillInAppliedState && _appliedAction == action)
        {
            foreach (MonitorInfo monitor in monitors)
            {
                if (_restoreSnapshot!.TryGetValue(monitor.ID, out double previousBrightness))
                    monitor.Brightness = previousBrightness;
            }

            _restoreSnapshot = null;
            _appliedAction = TrayClickAction.Nothing;
            return;
        }

        if (!stillInAppliedState)
            _restoreSnapshot = monitors.ToDictionary(m => m.ID, m => m.Brightness);

        foreach (MonitorInfo monitor in monitors)
            monitor.Brightness = target;

        _appliedAction = action;
    }

    private static int TargetOf(TrayClickAction action) => action switch
    {
        TrayClickAction.FullBright => 100,
        TrayClickAction.FullDim => 0,
        _ => -1,
    };

    private void ShowTrayContextMenu(PixelPoint cursorPoint)
    {
        if (_trayIcon == null || _settings == null) return;

        if (_trayMenuWindow is { IsVisible: true })
            _trayMenuWindow.DismissForWarmCache();

        BrightnessTrayMenuWindow menuWindow = TrayMenuWarmSlot.TakeOrCreate(CreateTrayMenuWindow);
        _trayMenuWindow = menuWindow;
        menuWindow.ShowAt(_trayIcon, cursorPoint, _settings.ContextMenuPosition);
    }

    private BrightnessTrayMenuWindow CreateTrayMenuWindow()
    {
        if (_settings == null)
            throw new InvalidOperationException("Brightness tray menu requires settings.");

        IReadOnlyList<BrightnessTrayMenuProfile> profiles = BuildMenuProfiles();
        IReadOnlyList<MonitorInfo> monitors = _monitorService?.Monitors.ToArray()
                                              ?? _brightnessFlyout?.Monitors.ToArray()
                                              ?? [];
        BrightnessTrayMenuWindow menuWindow = new(
            profiles,
            monitors,
            _settings,
            CreatePalette(),
            (AppServices.Theme ?? AppTheme.Default).DisplayIdentifierShadow.For(ResolveEffectiveIsLightTheme()),
            _settings.EnableRoundedCorners,
            _settings.ContextMenuFontSize,
            SelectProfileFromMenu,
            PowerOffAllMonitors,
            PowerOffMonitor,
            OpenSettings,
            ExitApplication);

        _trayMenuWindow = menuWindow;
        menuWindow.Closed += OnTrayMenuClosed;
        return menuWindow;
    }

    private void OnTrayMenuClosed(object? sender, EventArgs e)
    {
        if (sender is BrightnessTrayMenuWindow menu)
            menu.Closed -= OnTrayMenuClosed;
        if (ReferenceEquals(_trayMenuWindow, sender))
            _trayMenuWindow = null;
    }

    private List<BrightnessTrayMenuProfile> BuildMenuProfiles()
    {
        ProfileManager? profileManager = AppServices.ProfileManager;
        if (profileManager == null || _settings == null) return [];

        int count = Math.Min(
            Math.Max(0, _theme?.ProfileButtons.ButtonCount ?? 4),
            profileManager.Profiles.Profiles.Count);
        List<BrightnessTrayMenuProfile> profiles = new(count);
        for (int i = 0; i < count; i++)
        {
            string label = profileManager.GetName(i) is { Length: > 0 } name
                ? name
                : string.Format(L("Tray_Profile_Format", "Profile {0}"), i + 1);
            profiles.Add(new BrightnessTrayMenuProfile(i, label, i == profileManager.SelectedIndex));
        }

        return profiles;
    }

    private void EnsureFlyoutForMenu()
    {
        if (_brightnessFlyout != null || _settings == null || _monitorService == null) return;

        _brightnessFlyout = BrightnessFlyoutWarmSlot.TakeOrCreate(CreateManagedBrightnessFlyout);
    }

    private void SelectProfileFromMenu(int index)
    {
        EnsureFlyoutForMenu();
        _brightnessFlyout?.SelectProfileByIndex(index);
    }

    private void PowerOffAllMonitors()
    {
        if (_brightnessFlyout == null || _monitorService == null) return;

        foreach (MonitorInfo monitor in _brightnessFlyout.Monitors)
            _ = _monitorService.SetPowerStateAsync(monitor, false);
    }

    private void PowerOnAllMonitors()
    {
        if (_brightnessFlyout == null || _monitorService == null) return;

        foreach (MonitorInfo monitor in _brightnessFlyout.Monitors)
            _ = _monitorService.SetPowerStateAsync(monitor, true);
    }

    private void PowerOffMonitor(MonitorInfo monitor)
    {
        if (_monitorService == null) return;

        _ = _monitorService.SetPowerStateAsync(monitor, false);
    }

    private void ShowBrightnessFlyout(bool activate = true)
    {
        if (_settings == null || _monitorService == null || _trayIcon == null) return;

        _brightnessFlyout ??= BrightnessFlyoutWarmSlot.TakeOrCreate(CreateManagedBrightnessFlyout);

        _brightnessFlyout.Redock();
        _brightnessFlyout.ShowAt(_trayIcon, activate);
    }

    private SettingsFlyoutKeepOpenCoordinator SettingsFlyoutKeepOpen =>
        _settingsFlyoutKeepOpen ??= new SettingsFlyoutKeepOpenCoordinator(
            () => _settingsWindow,
            () => _brightnessFlyout,
            () => ShowBrightnessFlyout(activate: false));

    private void OnBrightnessFlyoutClosed(object? sender, EventArgs e)
    {
        if (_brightnessFlyout != null)
        {
            _brightnessFlyout.BrightnessUpdated -= RequestTrayRefresh;
            _brightnessFlyout.FlyoutDeactivated -= OnFlyoutDeactivated;
            _brightnessFlyout.SettingsRequested -= OpenSettings;
            _brightnessFlyout.Closed -= OnBrightnessFlyoutClosed;
            _brightnessFlyout = null;
            AppServices.BrightnessFlyout = null;
        }
    }

    private void OpenSettings()
    {
        if (_settings == null) return;

        if (_settingsWindow == null)
        {
            _settingsWindow = new BrightnessSettingsWindow(_settings, ShowUninstallerWindow);
            SettingsFlyoutKeepOpen.Attach(_settingsWindow);
            _settingsWindow.Closed += OnSettingsWindowClosed;
        }

        SettingsFlyoutKeepOpen.HoldOpen();
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        _settingsFlyoutKeepOpen?.Detach();
        if (_settingsWindow != null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            _settingsWindow = null;
        }

        _ = Task.Delay(TimeConstants.PostSettingsCloseGCDelayMs).ContinueWith(
            _ => GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true), TaskScheduler.Default);
    }

    private void ShowUninstallerWindow(string installDir, BrightnessInstallScope scope)
    {
        BrightnessUninstallerWindow window = new(installDir, scope);
        if (_settingsWindow != null) window.Show(_settingsWindow);
        else window.Show();
    }

    private void OnDisplayTopologyChanged()
    {
        _monitorService?.NotifyTopologyEvent();
        _monitorService?.Refresh();
        NightLightProvider.Reapply();
        RequestTrayRefresh();
    }

    private void RequestTrayRefresh()
    {
        if (_trayIcon == null) return;

        (int brightness, string tooltip) = GetBrightnessAndTooltip();
        _trayIcon.SetTooltip(tooltip);

        if (_trayIconRenderer != null)
        {
            _trayIconRenderer.BrightnessPercent = brightness;
            if (_trayIconRenderer.CreateIcon() is { } icon)
                _trayIcon.SetIcon(icon);
            return;
        }

        if (AppTheme.LoadAppNativeIcon() is { } fallbackIcon)
        {
            using (fallbackIcon)
                _trayIcon.SetIcon(fallbackIcon);
        }
    }

    private (int Brightness, string Tooltip) GetBrightnessAndTooltip()
    {
        List<MonitorInfo> monitors = _monitorService?.Monitors is { Count: > 0 } serviceMonitors
            ? [.. serviceMonitors]
            : _brightnessFlyout?.Monitors is { Count: > 0 } flyoutMonitors
                ? [.. flyoutMonitors]
                : [];
        int brightness = monitors.Count > 0
            ? ComputeTrackedIconBrightness(monitors)
            : _settings?.LastMasterBrightness ?? 100;
        string tooltip = string.Format(L("Tray_Tooltip_Brightness_Format", "Brightness: {0}%"), brightness);

        if (NightLightProvider.IsSupported() && NightLightProvider.IsEnabled())
            tooltip += string.Format(L("Tray_Tooltip_NightLight_Format", " - Night light: {0}%"),
                NightLightProvider.GetStrength());

        LogTrayValueDiagnostic(brightness, tooltip, monitors);
        return (brightness, tooltip);
    }

    private void LogTrayValueDiagnostic(int brightness, string tooltip, List<MonitorInfo> monitors)
    {
        try
        {
            string monitorState = monitors.Count == 0
                ? "<none>"
                : string.Join(" | ", monitors.Select(m =>
                    $"{m.Name}:{m.SliderState}:b={m.RoundedBrightness}:eff={m.EffectiveRoundedBrightness}:failed={m.IsFailed}:part={m.IsParticipatingInMaster}"));
            string snapshot =
                $"brightness={brightness}; tooltip='{tooltip.Replace("\r", "\\r").Replace("\n", "\\n")}'; "
                + $"flyoutNull={_brightnessFlyout == null}; flyoutVisible={_brightnessFlyout?.IsVisible.ToString() ?? "<null>"}; "
                + $"tracking={_settings?.DynamicIconBrightnessTracking}; enabledOnly={_settings?.DynamicIconTrackEnabledOnly}; "
                + $"monitors={monitorState}";

            DateTime now = DateTime.UtcNow;
            bool important = string.IsNullOrWhiteSpace(tooltip) || monitors.Count == 0 || brightness <= 1;
            if (!important
                && snapshot == _lastTrayValueDiagnostic
                && now - _lastTrayValueDiagnosticUtc
                < TimeSpan.FromMilliseconds(TimeConstants.TrayValueDiagnosticCooldownMs))
                return;

            _lastTrayValueDiagnostic = snapshot;
            _lastTrayValueDiagnosticUtc = now;
            WPFLog.Log("TrayDiag.Value: " + snapshot);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"TrayDiag.Value failed: {ex.Message}");
        }
    }

    private int ComputeTrackedIconBrightness(IEnumerable<MonitorInfo> monitors)
    {
        bool enabledOnly = _settings?.DynamicIconTrackEnabledOnly ?? false;
        List<MonitorInfo> pool = enabledOnly
            ? [.. monitors.Where(m => m.IsParticipatingInMaster)]
            : [.. monitors.Where(m => !m.IsFailed)];

        if (pool.Count == 0) return _settings?.LastMasterBrightness ?? 100;

        MasterSliderMode mode = _settings?.DynamicIconBrightnessTracking ?? MasterSliderMode.Average;
        static int EffectiveValue(MonitorInfo monitor) => monitor.EffectiveRoundedBrightness;
        double value = mode switch
        {
            MasterSliderMode.Lowest => pool.Min(EffectiveValue),
            MasterSliderMode.Highest => pool.Max(EffectiveValue),
            _ => pool.Average(EffectiveValue),
        };

        if (!double.IsFinite(value)) return _settings?.LastMasterBrightness ?? 100;
        return (int)Math.Round(Math.Clamp(value, 0.0, 100.0));
    }

    private void OnUpdateStateChanged()
    {
        _brightnessFlyout?.NotifyUpdateStateChanged();

        UpdateInfo? info = _updateCheckService?.AvailableUpdate;
        if (info == null || _settings?.ShowUpdateNotificationsEnabled != true) return;
        if (info.Version <= _lastNotifiedUpdateVersion) return;
        if (_brightnessFlyout is { IsVisible: true, Position.X: > -1000 }) return;

        _lastNotifiedUpdateVersion = info.Version;
        _trayIcon?.ShowBalloon(
            L("UpdateNotification_Title", "Update available"),
            string.Format(L("UpdateNotification_BodyFormat", "{0} is available."), info.ReleaseName));
    }

    private void OnUpdateBalloonClicked()
    {
        if (_updateCheckService?.AvailableUpdate == null) return;

        ShowBrightnessFlyout();
        _brightnessFlyout?.RequestUpdatePrompt();
    }

    private void OnThemeChanged(bool isLightTheme) =>
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeVariant();
            ApplyThemeResources();
            ApplyTrayIconSettings();
            RequestTrayRefresh();
        });

    private void OnSettingsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeVariant();
            ApplyThemeResources();
            if (_settings != null)
            {
                ApplyPDBDownloadTimeout(_settings);
                if (_monitorService != null)
                {
                    _monitorService.WriteCooldownMs = _settings.BrightnessUpdateRateMs;
                    _monitorService.ValidationDwellMs = _settings.ValidationDwellMs;
                }

                if (_trayIcon != null)
                    _trayIcon.IsScrollEnabled = _settings.TrayScrollEnabled;
                _hotkeyService?.Apply(_settings.Hotkeys);
            }

            ApplyToolTipDelayToOpenWindows();
            ApplyTrayIconSettings();
            _brightnessFlyout?.NotifyUpdateStateChanged();
            ApplyKeepWarmPolicies();
            RequestTrayRefresh();
        });
    }

    private void ApplyKeepWarmPolicies()
    {
        if (_brightnessFlyoutWarmSlot != null || _settings?.KeepFlyoutWarm == true)
            BrightnessFlyoutWarmSlot.ApplyKeepWarmPolicy(CreateManagedBrightnessFlyout);
        if (_trayMenuWarmSlot != null || _settings?.KeepTrayContextMenuWarm == true)
            TrayMenuWarmSlot.ApplyKeepWarmPolicy(CreateTrayMenuWindow);
    }

    private void ApplyToolTipDelayToOpenWindows()
    {
        if (_settingsWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_settingsWindow);
        if (_brightnessFlyout != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_brightnessFlyout);
        if (_trayMenuWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_trayMenuWindow);
    }

    private bool ResolveEffectiveIsLightTheme() => AppTheme.ResolveEffectiveIsLightTheme(_settings);

    private void ApplyThemeVariant()
    {
        RequestedThemeVariant = ResolveEffectiveIsLightTheme()
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    private void ApplyThemeResources()
    {
        if (_theme == null) return;

        bool isLight = ResolveEffectiveIsLightTheme();
        Resources["EnvironmentalBrightnessCurveBrush"] = TrayAppDotNETSettingsUI.Brush(
            _theme.ResolveEnvironmentalBrightnessCurve(_settings, isLight));
        Resources["EnvironmentalNightLightCurveBrush"] = TrayAppDotNETSettingsUI.Brush(
            _theme.ResolveEnvironmentalNightLightCurve(_settings, isLight));
        Resources["EnvironmentalCurrentTimeBrush"] = TrayAppDotNETSettingsUI.Brush(
            _theme.ResolveEnvironmentalCurrentTime(_settings, isLight));
        Resources["EnvironmentalTwilightBackdropBrush"] = TrayAppDotNETSettingsUI.Brush(
            _theme.ResolveEnvironmentalTwilightBackdrop(_settings, isLight));
        Resources["EnvironmentalNightBackdropBrush"] = TrayAppDotNETSettingsUI.Brush(
            _theme.ResolveEnvironmentalNightBackdrop(_settings, isLight));
        Resources["EnvironmentalGridLineBrush"] = TrayAppDotNETSettingsUI.Brush(
            _theme.ResolveEnvironmentalGridLine(_settings, isLight));
    }

    private SettingsPalette CreatePalette()
    {
        AppTheme theme = _theme ?? AppTheme.Default;
        bool isLight = ResolveEffectiveIsLightTheme();
        return new SettingsPalette(
            theme.ResolveBackground(_settings, isLight),
            theme.ResolveForeground(_settings, isLight),
            theme.Border.For(isLight),
            theme.Hover.For(isLight),
            theme.Pressed.For(isLight),
            theme.CardBackground.For(isLight),
            theme.ControlBackground.For(isLight),
            theme.SecondaryForeground.For(isLight),
            theme.DisabledForeground.For(isLight),
            theme.Accent.For(isLight),
            theme.ToggleSwitchOnTrack.For(isLight),
            theme.ToggleSwitchOnThumb.For(isLight),
            theme.TextBoxFocused.For(isLight),
            theme.SliderProgress.For(isLight),
            theme.SliderTrack.For(isLight),
            theme.SliderThumb.For(isLight),
            theme.CloseButtonHover.For(isLight),
            theme.CloseButtonPressed.For(isLight),
            theme.CloseButtonGlyphActive.For(isLight));
    }

    private void ApplyTrayIconSettings()
    {
        if (_trayIconRenderer == null || _settings == null) return;

        bool isLight = ResolveEffectiveIsLightTheme();
        _trayIconRenderer.IsLightTheme = isLight;
        _trayIconRenderer.IconStyle = _settings.TrayIconStyle;
        if (_settings.TrayIconStyle == TrayIconStyle.Static)
        {
            _trayIconRenderer.CustomColor = _settings.TrayIconColor.Resolve(isLight);
            _trayIconRenderer.BrightColor = null;
            _trayIconRenderer.DimColor = null;
        }
        else
        {
            _trayIconRenderer.CustomColor = null;
            _trayIconRenderer.BrightColor = _settings.TrayIconBrightColor.Resolve(isLight);
            _trayIconRenderer.DimColor = _settings.TrayIconDimColor.Resolve(isLight);
        }
    }

    private static void ApplyPDBDownloadTimeout(AppSettings settings)
    {
        int seconds = settings.NightLightPDBDownloadTimeoutSeconds;
        if (seconds is < 5 or > 600) seconds = 60;
        PDBSymbolResolver.DownloadTimeout = seconds * 1000;
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

            if (_displayEventManager != null)
            {
                _displayEventManager.DisplayTopologyChanged -= OnDisplayTopologyChanged;
                Safe.Dispose(_displayEventManager);
                _displayEventManager = null;
                AppServices.DisplayEventManager = null;
            }

            Safe.Dispose(_ddcRecoveryService);
            _ddcRecoveryService = null;
            AppServices.DDCRecoveryService = null;

            TryDrainQuickly(TimeSpan.FromMilliseconds(TimeConstants.NormalShutdownDrainTimeoutMs));

            if (_settings != null)
            {
                _settings.Changed -= OnSettingsChanged;
                _settings.Save();
            }

            AppServices.Settings = null;

            if (_monitorService != null)
            {
                _monitorService.MonitorsRefreshed -= OnMonitorsRefreshed;
                Safe.Dispose(_monitorService);
                _monitorService = null;
                AppServices.MonitorService = null;
            }

            try { AppServices.ProfileManager?.SaveOnShutdown(); }
            catch (Exception ex)
            {
                WPFLog.Log($"BrightnessAvaloniaApp.ProfileManager.SaveOnShutdown failed: {ex.Message}");
            }

            AppServices.ProfileManager = null;

            Safe.Dispose(_brightnessRangeProvider);
            _brightnessRangeProvider = null;
            AppServices.MonitorBrightnessRangeProvider = null;

            if (_theme != null)
            {
                _theme.ThemeChanged -= OnThemeChanged;
                Safe.Dispose(_theme);
                _theme = null;
                AppServices.Theme = null;
            }

            Safe.Dispose(_settingsFlyoutKeepOpen);
            _settingsFlyoutKeepOpen = null;

            Safe.Dispose(_brightnessFlyoutWarmSlot);
            _brightnessFlyoutWarmSlot = null;
            Safe.Dispose(_trayMenuWarmSlot);
            _trayMenuWarmSlot = null;

            if (_settingsWindow != null)
            {
                _settingsWindow.Closed -= OnSettingsWindowClosed;
                try { _settingsWindow.Close(); }
                catch { }

                _settingsWindow = null;
            }

            if (_brightnessFlyout != null)
            {
                _brightnessFlyout.BrightnessUpdated -= RequestTrayRefresh;
                _brightnessFlyout.FlyoutDeactivated -= OnFlyoutDeactivated;
                _brightnessFlyout.SettingsRequested -= OpenSettings;
                _brightnessFlyout.Closed -= OnBrightnessFlyoutClosed;
                try { _brightnessFlyout.Close(); }
                catch { }

                _brightnessFlyout = null;
                AppServices.BrightnessFlyout = null;
            }

            if (_trayMenuWindow != null)
            {
                try { _trayMenuWindow.Close(); }
                catch { }

                _trayMenuWindow = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.LeftClick -= OnTrayLeftClick;
                _trayIcon.LeftDoubleClick -= OnTrayLeftDoubleClick;
                _trayIcon.RightClick -= OnTrayRightClick;
                _trayIcon.Scrolled -= OnTrayScrolled;
                _trayIcon.RefreshNeeded -= RequestTrayRefresh;
                _trayIcon.BalloonClicked -= OnUpdateBalloonClicked;
            }

            Safe.Dispose(_trayIcon);
            _trayIcon = null;

            Safe.Dispose(_trayIconRenderer);
            _trayIconRenderer = null;

            WPFLog.Flush();
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessAvaloniaApp.ShutdownServices: {ex}");
        }
    }

    private void TryDrainQuickly(TimeSpan cap)
    {
        try
        {
            MonitorService? monitorService = _monitorService;
            if (monitorService == null) return;

            monitorService.BeginDrainAsync(cap).Wait(
                cap + TimeSpan.FromMilliseconds(TimeConstants.DrainAdditionalMarginMs));
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessAvaloniaApp.TryDrainQuickly: {ex.Message}");
        }
    }

    private void ExitApplication()
    {
        WPFLog.Log("BrightnessAvaloniaApp.ExitApplication");
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
