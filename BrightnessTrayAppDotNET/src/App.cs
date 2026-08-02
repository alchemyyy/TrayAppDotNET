using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.Interop.NightLight;
using BrightnessTrayAppDotNET.Localization;
using BrightnessTrayAppDotNET.Services;
using BrightnessTrayAppDotNET.UI.Flyout;
using BrightnessTrayAppDotNET.UI.Settings;
using BrightnessTrayAppDotNET.UI.Tray;
using BrightnessTrayAppDotNET.Visuals;
#if HOTAVALONIA_ENABLE
using HotAvalonia;
#endif
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Tray;
using TrayAppDotNETCommon.UI.WarmWindows;
using TrayAppDotNETCommon.Utils;
using GlyphCatalogHotReload = TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload;
using BrightnessHotkeyFiredEventArgs =
    TrayAppDotNETCommon.Services.HotkeyFiredEventArgs<BrightnessTrayAppDotNET.Models.BrightnessHotkeyAction>;
using BrightnessHotkeyService =
    TrayAppDotNETCommon.Services.GlobalHotkeyService<BrightnessTrayAppDotNET.Models.BrightnessHotkeyAction,
        BrightnessTrayAppDotNET.Models.HotkeyBinding>;
using BrightnessInstallScope = TrayAppDotNETCommon.Models.InstallScope;
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
                builder = TrayAppDotNETAvalonia.UseConfiguredRenderingBackend(
                    builder,
                    AppSettings.GetDefaultPath,
                    WPFLog.Log);
#if HOTAVALONIA_ENABLE
                builder = builder.UseHotReload();
#endif

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
    private readonly TrayIconRenderQueue _trayIconRenderQueue = new(WPFLog.Log);
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

        GlyphCatalogHotReload.ResourcesReloaded += OnGlyphCatalogResourcesReloaded;
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
                LogThemeLoadFailed = ex => WPFLog.Log($"BrightnessAvaloniaApp theme load failed: {ex}")
            });

        _settings = loaded.Settings;

        try
        {
            NightLightProvider.Initialize(_settings);
            NightLightProvider.EnabledStateChanged += OnNightLightEnabledStateChanged;
        }
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
            _trayIconRenderer = new BrightnessTrayIcon(_theme) { IsLightTheme = ResolveEffectiveIsLightTheme() };
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
                currentBuild: BuildInfo.BuildNumber,
                saveSettings: _settings.Save);
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
            IsPrecisionTouchpadScrollEnabled = _settings?.PrecisionTouchpadScrollEnabled ?? true,
            PrecisionTouchpadUnitsPerScrollStep = _settings?.PrecisionTouchpadUnitsPerScrollStep
                ?? AppSettings.PrecisionTouchpadUnitsPerScrollStepDefault
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
        _trayIcon.PrecisionTouchpadScrolled += OnTrayPrecisionTouchpadScrolled;
        _trayIcon.RefreshNeeded += RequestTrayRefresh;
        _trayIcon.BalloonClicked += OnUpdateBalloonClicked;
        _trayIcon.IsVisible = true;
    }

    private BrightnessFlyoutWindow CreateFlyout()
    {
        if (_settings == null || _monitorService == null)
            throw new InvalidOperationException("Brightness flyout requires settings and monitor service.");

        ProfileManager profileManager = ResolveProfileManager();
        BrightnessFlyoutWindow flyout = new(
            profileManager,
            _theme ?? AppTheme.Default,
            _monitorService);
        flyout.BrightnessUpdated += RequestTrayRefresh;
        flyout.FlyoutDeactivated += OnFlyoutDeactivated;
        flyout.SettingsRequested += OpenSettings;
        flyout.Closed += OnBrightnessFlyoutClosed;
        return flyout;
    }

    private static ProfileManager ResolveProfileManager()
    {
        ProfileManager? profileManager = AppServices.ProfileManager;
        if (profileManager != null) return profileManager;

        profileManager = new ProfileManager();
        AppServices.ProfileManager = profileManager;
        return profileManager;
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
        if (_shuttingDown) return;
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                if (_shuttingDown) return;
                if (_settings?.KeepFlyoutWarm == true && _monitorService != null)
                    await BrightnessFlyoutWarmSlot.PrimeAsync(CreateManagedBrightnessFlyout);
                if (_shuttingDown) return;
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

        if (!FlyoutDockingController.ShouldRestoreOnStartup(_settings)) return;

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
                if (_brightnessFlyout != null) _brightnessFlyout.ToggleNightLight();
                else if (NightLightProvider.IsSupported()) NightLightProvider.Toggle();
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

    /// <summary>
    /// Applies a master brightness delta to every participating monitor.
    /// Tray-wheel curve releases optionally seed manual brightness from the live curve value
    /// so the handoff does not jump back to the stale manual slider baseline.
    /// </summary>
    private void AdjustAllMonitorBrightness(int delta, bool seedManualFromCurveOnRelease = false)
    {
        BrightnessFlyoutWindow? flyout = _brightnessFlyout;
        if (flyout == null || flyout.Monitors.Count == 0) return;

        List<MonitorInfo> monitors = [.. flyout.Monitors.Where(m => m.IsParticipatingInMaster)];
        if (monitors.Count == 0) return;

        Dictionary<MonitorInfo, int>? curveReleaseTargets = seedManualFromCurveOnRelease
            ? ResolveCurveModeTrayWheelManualTargets(
                flyout.IsBrightnessCurveEnabled,
                flyout.IsCurveAbsoluteMode,
                flyout.MasterMonitor,
                monitors,
                delta)
            : null;

        flyout.NotifyUserBrightnessAdjustment(replayCurrentSliderValue: false);
        try
        {
            foreach (MonitorInfo monitor in monitors)
            {
                double target = curveReleaseTargets != null
                                && curveReleaseTargets.TryGetValue(monitor, out int curveReleaseTarget)
                    ? curveReleaseTarget
                    : monitor.Brightness + delta;
                monitor.Brightness = Math.Clamp(target, 0, 100);
            }
        }
        finally
        {
            flyout.CompleteUserBrightnessAdjustment();
        }
    }

    /// <summary>
    /// Captures the first manual target for a tray-wheel release from absolute curve mode.
    /// The wheel's first detent should move away from the currently visible curve value by direction only,
    /// not by replaying the full scroll step against the stale manual slider value.
    /// </summary>
    internal static Dictionary<MonitorInfo, int>? ResolveCurveModeTrayWheelManualTargets(
        bool isBrightnessCurveEnabled,
        bool isCurveAbsoluteMode,
        MonitorInfo masterMonitor,
        IReadOnlyList<MonitorInfo> monitors,
        int delta)
    {
        if (!isBrightnessCurveEnabled) return null;
        if (!isCurveAbsoluteMode) return null;
        if (masterMonitor.SliderState != SliderState.CurveActive) return null;
        if (!masterMonitor.HasCurveTargetBrightness) return null;

        int direction = Math.Sign(delta);
        if (direction == 0) return null;

        Dictionary<MonitorInfo, int> targets = [];
        foreach (MonitorInfo monitor in monitors)
        {
            if (monitor.SliderState != SliderState.CurveActive) continue;
            if (!monitor.HasCurveTargetBrightness) continue;

            targets[monitor] = Math.Clamp(monitor.EffectiveRoundedBrightness + direction, 0, 100);
        }

        return targets.Count == 0 ? null : targets;
    }

    private void AdjustNightLightBrightness(int delta)
    {
        BrightnessFlyoutWindow? flyout = _brightnessFlyout;

        flyout?.AdjustNightLightBrightness(delta);
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

        if (_brightnessFlyout is { IsVisible: true })
        {
            _brightnessFlyout.Hide();
            return;
        }

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
        if (_settings == null) return;

        int notches = wheelDelta / 120;
        if (notches == 0)
            notches = Math.Sign(wheelDelta);
        int delta = notches * Math.Max(1, _settings.FlyoutScrollWheelStep);
        ApplyTrayWheelDelta(delta);
    }

    private void OnTrayPrecisionTouchpadScrolled(int delta) =>
        ApplyTrayWheelDelta(Math.Sign(delta));

    private void ApplyTrayWheelDelta(int delta)
    {
        if (delta == 0 || _brightnessFlyout == null || _settings == null) return;

        TrayWheelTarget target = ResolveWheelTarget(_settings);
        if (target == TrayWheelTarget.Nothing) return;

        switch (target)
        {
            case TrayWheelTarget.NightLight:
                AdjustNightLightBrightness(delta);
                break;
            case TrayWheelTarget.Brightness:
                AdjustAllMonitorBrightness(delta, seedManualFromCurveOnRelease: true);
                break;
        }

        RequestTrayRefresh();
        _trayIcon?.ShowTooltip();
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
        BrightnessFlyoutWindow? flyout = _brightnessFlyout;
        if (flyout == null || flyout.Monitors.Count == 0) return;

        List<MonitorInfo> monitors = [.. flyout.Monitors.Where(m => m.IsParticipatingInMaster)];
        if (monitors.Count == 0) return;

        flyout.NotifyUserBrightnessAdjustment(replayCurrentSliderValue: false);
        try
        {
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
        finally
        {
            flyout.CompleteUserBrightnessAdjustment();
        }
    }

    private static int TargetOf(TrayClickAction action) => action switch
    {
        TrayClickAction.FullBright => 100,
        TrayClickAction.FullDim => 0,
        _ => -1
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

        foreach (MonitorInfo monitor in _brightnessFlyout.Monitors.Where(static m => m.SupportsPowerControl))
            _ = _monitorService.SetPowerStateAsync(monitor, false);
    }

    private void PowerOnAllMonitors()
    {
        if (_brightnessFlyout == null || _monitorService == null) return;

        foreach (MonitorInfo monitor in _brightnessFlyout.Monitors.Where(static m => m.SupportsPowerControl))
            _ = _monitorService.SetPowerStateAsync(monitor, true);
    }

    private void PowerOffMonitor(MonitorInfo monitor)
    {
        if (_monitorService == null) return;
        if (!monitor.SupportsPowerControl) return;

        _ = _monitorService.SetPowerStateAsync(monitor, false);
    }

    private void ShowBrightnessFlyout(bool activate = true, bool holdOpenForSettings = true)
    {
        if (_settings == null || _monitorService == null || _trayIcon == null) return;

        _brightnessFlyout ??= BrightnessFlyoutWarmSlot.TakeOrCreate(CreateManagedBrightnessFlyout);

        _brightnessFlyout.Redock();
        _brightnessFlyout.ShowAt(_trayIcon, activate);
        if (holdOpenForSettings && _settingsWindow is { IsVisible: true })
            SettingsFlyoutKeepOpen.HoldOpen();
    }

    private SettingsFlyoutKeepOpenCoordinator SettingsFlyoutKeepOpen =>
        _settingsFlyoutKeepOpen ??= new SettingsFlyoutKeepOpenCoordinator(
            () => _settingsWindow,
            () => _brightnessFlyout,
            () => ShowBrightnessFlyout(activate: false, holdOpenForSettings: false));

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

    /// <summary>
    /// Invalidates the renderer so tray glyph edits are visible immediately.
    /// </summary>
    private void OnGlyphCatalogResourcesReloaded()
    {
        _trayIconRenderer?.InvalidateCache();
        RequestTrayRefresh();
    }

    private void RequestTrayRefresh()
    {
        if (_trayIcon == null) return;

        (int brightness, string tooltip) = GetBrightnessAndTooltip();

        if (_trayIconRenderer != null)
        {
            BrightnessTrayIcon renderer = _trayIconRenderer;
            renderer.BrightnessPercent = brightness;
            if (renderer.TryCreateRenderInput(out BrightnessTrayIconRenderInput? input) && input != null)
            {
                _trayIcon.SetTooltip(tooltip);
                _trayIconRenderQueue.Request(
                    () => renderer.RenderIcon(input),
                    icon => ApplyRenderedTrayIcon(icon, tooltip));
                return;
            }

            _trayIcon.SetTooltip(tooltip);
            return;
        }

        if (AppTheme.LoadAppNativeIcon() is { } fallbackIcon)
        {
            using (fallbackIcon)
                _trayIcon.SetIconAndTooltip(fallbackIcon, tooltip);
            return;
        }

        _trayIcon.SetTooltip(tooltip);
    }

    /// <summary>
    /// Applies a rendered tray icon, disposing it if the tray has already shut down.
    /// </summary>
    private void ApplyRenderedTrayIcon(NativeIcon icon, string tooltip)
    {
        if (_trayIcon == null)
        {
            icon.Dispose();
            return;
        }

        _trayIcon.SetOwnedIconAndTooltip(icon, tooltip);
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
        {
            tooltip += string.Format(L("Tray_Tooltip_NightLight_Format", " - Night light: {0}%"),
                GetCurrentNightLightTooltipStrength());
        }

        LogTrayValueDiagnostic(brightness, tooltip, monitors);
        return (brightness, tooltip);
    }

    private int GetCurrentNightLightTooltipStrength()
    {
        if (_brightnessFlyout?.NightLightMonitor is { } nightLightMonitor)
        {
            return ResolveNightLightTooltipStrength(
                nightLightMonitor,
                providerStrength: 0,
                invertNightLightSlider: _settings?.InvertNightLightSlider == true);
        }

        return NightLightProvider.GetStrength();
    }

    /// <summary>
    /// Resolves the live night-light strength for the tray tooltip.
    /// Curve-active rows use their effective target while other states use the manual slider value.
    /// </summary>
    internal static int ResolveNightLightTooltipStrength(
        MonitorInfo? nightLightMonitor,
        int providerStrength,
        bool invertNightLightSlider)
    {
        if (nightLightMonitor == null) return Math.Clamp(providerStrength, 0, 100);

        int sliderStrength = nightLightMonitor.EffectiveRoundedBrightness;
        return invertNightLightSlider ? 100 - sliderStrength : sliderStrength;
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
            _ => pool.Average(EffectiveValue)
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

    private void OnNightLightEnabledStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_shuttingDown) return;
            _brightnessFlyout?.NotifyNightLightEnabledStateChanged();
            RequestTrayRefresh();
        });
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

                ApplyTrayIconScrollSettings();
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
            theme.SearchListItemSelected.For(isLight),
            theme.SearchListItemHover.For(isLight),
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

    private void ApplyTrayIconScrollSettings()
    {
        if (_trayIcon == null || _settings == null) return;

        _trayIcon.IsScrollEnabled = _settings.TrayScrollEnabled;
        _trayIcon.IsPrecisionTouchpadScrollEnabled = _settings.PrecisionTouchpadScrollEnabled;
        _trayIcon.PrecisionTouchpadUnitsPerScrollStep = _settings.PrecisionTouchpadUnitsPerScrollStep;
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
        GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded;
        bool glyphConsumersClosed = true;

        try
        {
            DisplayIdentifierService.Hide();
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

            NightLightProvider.EnabledStateChanged -= OnNightLightEnabledStateChanged;
            try { NightLightProvider.Shutdown(); }
            catch (Exception ex)
            {
                WPFLog.Log($"BrightnessAvaloniaApp.NightLightProvider.Shutdown failed: {ex.Message}");
            }

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

            if (AppServices.ProfileManager != null)
            {
                Safe.Dispose(AppServices.ProfileManager);
                AppServices.ProfileManager = null;
            }

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

            if (_brightnessFlyoutWarmSlot != null)
            {
                try { _brightnessFlyoutWarmSlot.Dispose(); }
                catch (Exception exception)
                {
                    glyphConsumersClosed = false;
                    WPFLog.Log($"Brightness flyout warm-slot shutdown failed: {exception.Message}");
                }
            }
            _brightnessFlyoutWarmSlot = null;
            Safe.Dispose(_trayMenuWarmSlot);
            _trayMenuWarmSlot = null;

            if (_settingsWindow != null)
            {
                _settingsWindow.Closed -= OnSettingsWindowClosed;
                try { _settingsWindow.Close(); }
                catch (Exception exception)
                {
                    WPFLog.Log($"Brightness settings-window shutdown failed: {exception.Message}");
                }

                _settingsWindow = null;
            }

            if (_brightnessFlyout != null)
            {
                _brightnessFlyout.BrightnessUpdated -= RequestTrayRefresh;
                _brightnessFlyout.FlyoutDeactivated -= OnFlyoutDeactivated;
                _brightnessFlyout.SettingsRequested -= OpenSettings;
                _brightnessFlyout.Closed -= OnBrightnessFlyoutClosed;
                try { _brightnessFlyout.Close(); }
                catch (Exception exception)
                {
                    glyphConsumersClosed = false;
                    WPFLog.Log($"Brightness flyout shutdown failed: {exception.Message}");
                }

                _brightnessFlyout = null;
                AppServices.BrightnessFlyout = null;
            }

            if (glyphConsumersClosed)
                SkiaFlyoutGlyphIcon.DisposeSharedResources();

            if (_trayMenuWindow != null)
            {
                try { _trayMenuWindow.Close(); }
                catch (Exception exception)
                {
                    WPFLog.Log($"Brightness tray-menu shutdown failed: {exception.Message}");
                }

                _trayMenuWindow = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.LeftClick -= OnTrayLeftClick;
                _trayIcon.LeftDoubleClick -= OnTrayLeftDoubleClick;
                _trayIcon.RightClick -= OnTrayRightClick;
                _trayIcon.Scrolled -= OnTrayScrolled;
                _trayIcon.PrecisionTouchpadScrolled -= OnTrayPrecisionTouchpadScrolled;
                _trayIcon.RefreshNeeded -= RequestTrayRefresh;
                _trayIcon.BalloonClicked -= OnUpdateBalloonClicked;
            }

            Safe.Dispose(_trayIcon);
            _trayIcon = null;

            _trayIconRenderQueue.Dispose();
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
