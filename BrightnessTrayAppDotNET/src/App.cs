#if HOTAVALONIA_ENABLE
using HotAvalonia;
#endif
#if DEBUG
using AppThemeHotReload = TrayAppDotNETCommon.Visuals.AppThemeHotReload;
using GlyphCatalogHotReload = TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload;
#endif
using Avalonia;
using BrightnessTrayAppDotNET.Utils;
using Avalonia.Styling;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.Interop.NightLight;
using BrightnessTrayAppDotNET.UI.Flyout;
using BrightnessTrayAppDotNET.UI.Settings;
using BrightnessTrayAppDotNET.UI.Tray;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Services.Install;
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
using BrightnessInstallScope = TrayAppDotNETCommon.Models.InstallScope;
using TrayAppDotNETCommon.Models;

namespace BrightnessTrayAppDotNET;

internal static class BrightnessAvaloniaRunner
{
    public static int Run(string[] args) =>
        TrayAppDotNETAppRunner.Run<BrightnessAvaloniaApp>(args, AppSettings.GetDefaultPath, configure: Configure);

    private static AppBuilder Configure(AppBuilder builder)
    {
#if HOTAVALONIA_ENABLE
        builder = builder.UseHotReload();
#endif
        return builder;
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
    private TrayIconPresenter<BrightnessTrayIconRenderInput>? _trayIconPresenter;
    private TrayMenuController? _trayMenu;
    private TrayWindowToggle? _trayToggle;
    private BrightnessFlyoutWindow? _brightnessFlyout;
    private BrightnessSettingsWindow? _settingsWindow;
    private SettingsFlyoutKeepOpenCoordinator? _settingsFlyoutKeepOpen;
    private TrayAppDotNETWarmWindowSlot<BrightnessFlyoutWindow>? _brightnessFlyoutWarmSlot;
    private TrayAppDotNETAppHost<AppSettings, AppTheme>? _host;
    private KnownDisplaysStore? _knownDisplays;
    private BrightnessHotkeyService? _hotkeyService;
    private Dictionary<string, double>? _restoreSnapshot;
    private TrayClickAction _appliedAction = TrayClickAction.Nothing;
    private string? _lastTrayValueDiagnostic;
    private DateTime _lastTrayValueDiagnosticUtc;
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
            ShowUpdateNotification = ShowUpdateNotification
        });
        AppServices.UpdateCheckService = host.UpdateService;
        RequestTrayRefresh();
        StartBrightnessRangeProvider();
        RestoreStartupUndockedFlyoutIfRequested();
        ScheduleKeepWarmPriming();

        base.OnFrameworkInitializationCompleted();
    }

    private TrayAppDotNETAppHostOptions<AppSettings, AppTheme> CreateHostOptions() =>
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
            SaveSettingsOnExit = static settings => settings.SaveOnExit(),
            // A process exit outside the normal exit path still gets queued DDC writes out, and takes the gamma tint
            // off screen; its saved on/off state stays, so the next start reapplies it
            ProcessExit = OnProcessExit
        };

    private void OnProcessExit()
    {
        TryDrainQuickly(TimeSpan.FromMilliseconds(TimeConstants.ProcessExitDrainTimeoutMs));
        try { GammaRampNightLight.Deactivate(); }
        catch (Exception ex)
        {
            TADNLog.Log($"BrightnessAvaloniaApp: gamma tint release on process exit failed: {ex.Message}");
        }
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

        // Night Light re-applies a saved tint, so it starts only in tray mode
        try
        {
            NightLightProvider.Initialize(_settings);
            NightLightProvider.EnabledStateChanged += OnNightLightEnabledStateChanged;
        }
        catch (Exception ex) { TADNLog.Log($"BrightnessAvaloniaApp night-light init failed: {ex.Message}"); }

        ApplyPDBDownloadTimeout(_settings);

        try
        {
            _knownDisplays = new KnownDisplaysStore();
            AppServices.KnownDisplays = _knownDisplays;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BrightnessAvaloniaApp known displays init failed: {ex}");
        }

        try
        {
            KnownDisplaysStore knownDisplays = _knownDisplays
                ?? throw new InvalidOperationException("The known-display store could not be created.");
            _monitorService = new MonitorService(new DisplayService(), _settings, knownDisplays);
            AppServices.MonitorService = _monitorService;
            _monitorService.MonitorsRefreshed += OnMonitorsRefreshed;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BrightnessAvaloniaApp monitor service init failed: {ex}");
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
                TADNLog.Log($"BrightnessAvaloniaApp display event init failed: {ex}");
            }

            try
            {
                _ddcRecoveryService = new DDCRecoveryService(_monitorService);
                _ddcRecoveryService.Start();
            }
            catch (Exception ex)
            {
                TADNLog.Log($"BrightnessAvaloniaApp DDC recovery init failed: {ex}");
            }
        }

        try
        {
            AppServices.ProfileManager = new ProfileManager(ProfileManager.GetDefaultPath());
            AttachProfileManager(AppServices.ProfileManager);
        }
        catch (Exception ex) { TADNLog.Log($"BrightnessAvaloniaApp profile manager init failed: {ex}"); }

        try
        {
            _trayIconRenderer = new BrightnessTrayIcon(_theme) { IsLightTheme = ResolveEffectiveIsLightTheme() };
            ApplyTrayIconSettings();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BrightnessAvaloniaApp tray renderer init failed: {ex}");
        }

        _hotkeyService = _host?.StartHotkeyService(
            static prefix => new BrightnessHotkeyService(prefix),
            service =>
            {
                service.Initialize();
                service.Fired += OnHotkeyFired;
                service.Apply(_settings.Hotkeys);
            });
        AppServices.HotkeyService = _hotkeyService;
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
            TADNLog.Log($"BrightnessAvaloniaApp range provider init failed: {ex}");
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
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.LeftDoubleClick += OnTrayLeftDoubleClick;
        _trayIcon.KeySelect += OnTrayKeySelect;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.Scrolled += OnTrayScrolled;
        _trayIcon.PrecisionTouchpadScrolled += OnTrayPrecisionTouchpadScrolled;
        _trayIcon.RefreshNeeded += RequestTrayRefresh;
        _trayIcon.BalloonClicked += OnUpdateBalloonClicked;
        _trayIconPresenter = new TrayIconPresenter<BrightnessTrayIconRenderInput>(
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
        flyout.SettingsRequested += OpenSettings;
        flyout.Closed += OnBrightnessFlyoutClosed;
        return flyout;
    }

    private ProfileManager ResolveProfileManager()
    {
        ProfileManager? profileManager = AppServices.ProfileManager;
        if (profileManager != null) return profileManager;

        profileManager = new ProfileManager(ProfileManager.GetDefaultPath());
        AppServices.ProfileManager = profileManager;
        AttachProfileManager(profileManager);
        return profileManager;
    }

    // The tray menu shows the profile names and the check mark of the selected one
    private void AttachProfileManager(ProfileManager profileManager)
    {
        profileManager.SelectedProfileChanged += OnSelectedProfileChanged;
        profileManager.ProfilesListChanged += InvalidateTrayMenu;
    }

    private void DetachProfileManager(ProfileManager profileManager)
    {
        profileManager.SelectedProfileChanged -= OnSelectedProfileChanged;
        profileManager.ProfilesListChanged -= InvalidateTrayMenu;
    }

    private void OnSelectedProfileChanged(int index) => InvalidateTrayMenu();

    private void InvalidateTrayMenu()
    {
        if (_shuttingDown) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(InvalidateTrayMenu);
            return;
        }

        _trayMenu?.Invalidate();
    }

    private BrightnessFlyoutWindow CreateManagedBrightnessFlyout()
    {
        BrightnessFlyoutWindow flyout = CreateFlyout();
        _brightnessFlyout = flyout;
        AppServices.BrightnessFlyout = flyout;
        return flyout;
    }

    // NOTE: The flyout owns the curve session, so idle eviction would stop curves and orphan hotkeys.
    // It therefore stays warm regardless of KeepFlyoutWarm, and the settings page does not offer that toggle.
    private TrayAppDotNETWarmWindowSlot<BrightnessFlyoutWindow> BrightnessFlyoutWarmSlot =>
        _brightnessFlyoutWarmSlot ??= new TrayAppDotNETWarmWindowSlot<BrightnessFlyoutWindow>(
            static () => true,
            ex => TADNLog.Log($"BrightnessFlyout keep-warm: {ex.Message}"));

    private void ScheduleKeepWarmPriming()
    {
        if (_shuttingDown) return;
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                if (_shuttingDown) return;
                if (_monitorService != null)
                    await BrightnessFlyoutWarmSlot.PrimeAsync(CreateManagedBrightnessFlyout);
                if (_shuttingDown) return;
                if (_trayMenu != null)
                    await _trayMenu.PrimeAsync();
            }
            catch (Exception ex)
            {
                TADNLog.Log($"BrightnessAvaloniaApp.ScheduleKeepWarmPriming: {ex.Message}");
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void RestoreStartupUndockedFlyoutIfRequested()
    {
        if (_settings == null || _monitorService == null || _trayIcon == null) return;

        if (!FlyoutDockingController.ShouldRestoreOnStartup(_settings)) return;

        _brightnessFlyout ??= BrightnessFlyoutWarmSlot.TakeOrCreate(CreateManagedBrightnessFlyout);

        _brightnessFlyout.ShowAt(_trayIcon);
    }

    private void OnHotkeyFired(object? sender, BrightnessHotkeyFiredEventArgs e)
    {
        try { HandleHotkey(e.Action, e.Parameter); }
        catch (Exception ex) { TADNLog.Log($"BrightnessAvaloniaApp.OnHotkeyFired: {ex}"); }
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
                ApplyOrRestoreBrightness(TrayClickAction.FullBright, target: 100);
                break;
            case BrightnessHotkeyAction.FullDim:
                ApplyOrRestoreBrightness(TrayClickAction.FullDim, target: 0);
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

            InvalidateTrayMenu();
            try { RequestTrayRefresh(); }
            catch (Exception ex)
            {
                TADNLog.Log($"BrightnessAvaloniaApp.OnMonitorsRefreshed tray refresh: {ex.Message}");
            }

            if (_hotkeyService == null || _settings == null) return;

            try { _hotkeyService.Apply(_settings.Hotkeys); }
            catch (Exception ex) { TADNLog.Log($"BrightnessAvaloniaApp.OnMonitorsRefreshed hotkeys: {ex.Message}"); }
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

        flyout.NotifyUserBrightnessAdjustment(false);
        try
        {
            foreach (MonitorInfo monitor in monitors)
            {
                double target = curveReleaseTargets != null
                                && curveReleaseTargets.TryGetValue(monitor, out int curveReleaseTarget)
                    ? curveReleaseTarget
                    : monitor.Brightness + delta;
                monitor.Brightness = Math.Clamp(target, min: 0, max: 100);
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

            targets[monitor] = Math.Clamp(monitor.EffectiveRoundedBrightness + direction, min: 0, max: 100);
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
        if (TryRunModifiedClickAction(
                _settings?.TrayCtrlLeftClickAction,
                _settings?.TrayAltLeftClickAction))
            return;

        switch (_trayToggle?.Decide(_brightnessFlyout) ?? TrayToggleAction.Show)
        {
            case TrayToggleAction.Hide:
                _brightnessFlyout?.Hide();
                break;
            // A floating flyout behind another window comes forward where it is instead of docking again
            case TrayToggleAction.Activate:
                _brightnessFlyout?.Activate();
                break;
            case TrayToggleAction.Show:
                ShowBrightnessFlyout();
                break;
        }
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

    // The classic notification area may report one Enter twice, so the keyboard only shows the flyout
    private void OnTrayKeySelect() => ShowBrightnessFlyout();

    private void OnTrayRightClick(Point point) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (TryRunModifiedClickAction(
                    _settings?.TrayCtrlRightClickAction,
                    _settings?.TrayAltRightClickAction))
                return;

            ShowTrayContextMenu(new PixelPoint((int)Math.Round(point.X), (int)Math.Round(point.Y)));
        });

    private void OnTrayScrolled(int notches)
    {
        if (_settings == null) return;

        ApplyTrayWheelDelta(notches * Math.Max(val1: 1, _settings.FlyoutScrollWheelStep));
    }

    private void OnTrayPrecisionTouchpadScrolled(int steps) =>
        ApplyTrayWheelDelta(Math.Sign(steps));

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
        if (TrayClickModifiers.IsCtrlDown()) return settings.TrayCtrlWheelAction;
        return TrayClickModifiers.IsAltDown() ? settings.TrayAltWheelAction : settings.TrayWheelAction;
    }

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
            case TrayClickAction.TurnOffAllDisplays:
                PowerOffAllMonitors();
                break;
            case TrayClickAction.TurnOnAllDisplays:
                PowerOnAllMonitors();
                break;
            case TrayClickAction.FullBright:
                ApplyOrRestoreBrightness(action, target: 100);
                break;
            case TrayClickAction.FullDim:
                ApplyOrRestoreBrightness(action, target: 0);
                break;
        }
    }

    private void ApplyOrRestoreBrightness(TrayClickAction action, int target)
    {
        BrightnessFlyoutWindow? flyout = _brightnessFlyout;
        if (flyout == null || flyout.Monitors.Count == 0) return;

        List<MonitorInfo> monitors = [.. flyout.Monitors.Where(m => m.IsParticipatingInMaster)];
        if (monitors.Count == 0) return;

        flyout.NotifyUserBrightnessAdjustment(false);
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
        if (_trayIcon == null || _settings == null || _trayMenu == null) return;

        _trayMenu.Show(_trayIcon, cursorPoint, _settings.ContextMenuPosition);
    }

    private TrayMenuWindow CreateTrayMenuWindow()
    {
        if (_settings == null)
            throw new InvalidOperationException("Brightness tray menu requires settings.");

        IReadOnlyList<MonitorInfo> monitors = _monitorService?.Monitors.ToArray()
                                              ?? _brightnessFlyout?.Monitors.ToArray()
                                              ?? [];
        return new TrayMenuWindow(
            BrightnessTrayMenuEntries.Build(
                BuildMenuProfiles(),
                monitors,
                _settings,
                SelectProfileFromMenu,
                PowerOffAllMonitors,
                PowerOffMonitor),
            TrayMenuWindowOptions.Create(
                _settings,
                _theme,
                UI.ControlMap.TrayMenu.ID,
                TrayMenuStandardEntry.Settings(OpenSettings, UI.ControlMap.TrayMenu.OpenSettings),
                TrayMenuStandardEntry.Exit(ExitApplication, UI.ControlMap.TrayMenu.Exit)));
    }

    private List<BrightnessTrayMenuProfile> BuildMenuProfiles()
    {
        ProfileManager? profileManager = AppServices.ProfileManager;
        if (profileManager == null || _settings == null) return [];

        int count = Math.Min(
            Math.Max(val1: 0, _theme?.ProfileButtons.ButtonCount ?? 4),
            profileManager.Profiles.Profiles.Count);
        List<BrightnessTrayMenuProfile> profiles = new(count);
        for (int i = 0; i < count; i++)
        {
            string label = profileManager.GetName(i) is { Length: > 0 } name
                ? name
                : string.Format(L(nameof(AppStrings.Tray_Profile_Format)), i + 1);
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
            _ = _monitorService.SetPowerStateAsync(monitor, on: false);
    }

    private void PowerOnAllMonitors()
    {
        if (_brightnessFlyout == null || _monitorService == null) return;

        foreach (MonitorInfo monitor in _brightnessFlyout.Monitors.Where(static m => m.SupportsPowerControl))
            _ = _monitorService.SetPowerStateAsync(monitor, on: true);
    }

    private void PowerOffMonitor(MonitorInfo monitor)
    {
        if (_monitorService == null) return;
        if (!monitor.SupportsPowerControl) return;

        _ = _monitorService.SetPowerStateAsync(monitor, on: false);
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
        if (sender is not BrightnessFlyoutWindow flyout) return;

        flyout.BrightnessUpdated -= RequestTrayRefresh;
        flyout.SettingsRequested -= OpenSettings;
        flyout.Closed -= OnBrightnessFlyoutClosed;
        if (!ReferenceEquals(_brightnessFlyout, flyout)) return;

        _brightnessFlyout = null;
        AppServices.BrightnessFlyout = null;
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

    private Task<bool> ShowUninstallerWindow(
        BrightnessInstallScope scope,
        IProgress<TrayAppDotNETInstallProgress>? progress) =>
        _host?.ShowUninstallerWindow(scope, progress, _settingsWindow) ?? Task.FromResult(false);

    private void OnDisplayTopologyChanged()
    {
        _monitorService?.NotifyTopologyEvent();
        _monitorService?.Refresh();
        NightLightProvider.Reapply();
        RequestTrayRefresh();
    }

#if DEBUG
    /// <summary>
    /// Renders the tray icon again so tray glyph edits are visible immediately.
    /// </summary>
    private void OnGlyphCatalogResourcesReloaded()
    {
        _trayIconPresenter?.Invalidate();
        InvalidateTrayMenu();
        RequestTrayRefresh();
    }

    private void OnAppThemeResourcesReloaded() => _settings?.RaiseChangedForThemeReload();
#endif

    private void RequestTrayRefresh()
    {
        if (_trayIconPresenter == null) return;

        (int brightness, string tooltip) = GetBrightnessAndTooltip();
        _trayIconRenderer?.BrightnessPercent = brightness;
        _trayIconPresenter.Refresh(tooltip);
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
        string tooltip = string.Format(L(nameof(AppStrings.Tray_Tooltip_Brightness_Format)), brightness);

        if (NightLightProvider.IsSupported() && NightLightProvider.IsEnabled())
        {
            tooltip += string.Format(L(nameof(AppStrings.Tray_Tooltip_NightLight_Format)),
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
                _settings?.InvertNightLightSlider == true);
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
        if (nightLightMonitor == null) return Math.Clamp(providerStrength, min: 0, max: 100);

        int sliderStrength = nightLightMonitor.EffectiveRoundedBrightness;
        return invertNightLightSlider ? 100 - sliderStrength : sliderStrength;
    }

    // Builds a multi-monitor snapshot on every refresh, slider drags included, so only Debug builds log it
    [System.Diagnostics.Conditional("DEBUG")]
    private void LogTrayValueDiagnostic(int brightness, string tooltip, List<MonitorInfo> monitors)
    {
        try
        {
            string monitorState = monitors.Count == 0
                ? "<none>"
                : string.Join(separator: " | ", monitors.Select(m =>
                    // A degraded row's writes were acknowledged but never confirmed by a read-back: eff is its intent
                    $"{m.Name}:{m.SliderState}:b={m.RoundedBrightness}:eff={m.EffectiveRoundedBrightness}"
                    + $":failed={m.IsFailed}:degraded={m.IsReadDegraded}:part={m.IsParticipatingInMaster}"));
            string snapshot =
                $"brightness={brightness}; tooltip='{tooltip.Replace(oldValue: "\r", newValue: "\\r").Replace(oldValue: "\n", newValue: "\\n")}'; "
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
            TADNLog.Log("TrayDiag.Value: " + snapshot);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"TrayDiag.Value failed: {ex.Message}");
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
        double value = mode switch
        {
            MasterSliderMode.Lowest => pool.Min(EffectiveValue),
            MasterSliderMode.Highest => pool.Max(EffectiveValue),
            _ => pool.Average(EffectiveValue)
        };

        if (!double.IsFinite(value)) return _settings?.LastMasterBrightness ?? 100;
        return (int)Math.Round(Math.Clamp(value, min: 0.0, max: 100.0));
        static int EffectiveValue(MonitorInfo monitor) => monitor.EffectiveRoundedBrightness;
    }

    private void ShowUpdateNotification(string title, string body)
    {
        // An open flyout already shows the update button
        if (_brightnessFlyout is { IsVisible: true, Position.X: > -1000 }) return;

        _trayIcon?.ShowBalloon(title, body);
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
        if (_host?.UpdateService?.AvailableUpdate == null) return;

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
        if (_monitorService != null)
            BrightnessFlyoutWarmSlot.ApplyKeepWarmPolicy(CreateManagedBrightnessFlyout);
        _trayMenu?.ApplyKeepWarmPolicy();
    }

    private void ApplyToolTipDelayToOpenWindows()
    {
        if (_settingsWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_settingsWindow);
        if (_brightnessFlyout != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_brightnessFlyout);
        if (_trayMenu?.Current is { } trayMenu) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(trayMenu);
    }

    private bool ResolveEffectiveIsLightTheme() => AppTheme.ResolveIsLight(_settings, _theme);

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

    private void ApplyTrayIconSettings()
    {
        if (_trayIconRenderer == null || _settings == null) return;

        bool isLight = ResolveEffectiveIsLightTheme();
        _trayIconRenderer.IsLightTheme = isLight;
        _trayIconRenderer.IconStyle = _settings.TrayIconStyle;
        if (_settings.TrayIconStyle == TrayAppDotNETTrayIconStyle.Static)
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

    // The setting is clamped to the UI's range by AppSettings. The Night Light helper process does the download and
    // receives this value on its command line each time it starts.
    private static void ApplyPDBDownloadTimeout(AppSettings settings) =>
        PDBSymbolResolver.DownloadTimeout = settings.NightLightPDBDownloadTimeoutSeconds * 1_000;

    private void ShutdownServices()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
#if DEBUG
        GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded;
        AppThemeHotReload.ResourcesReloaded -= OnAppThemeResourcesReloaded;
#endif
        bool glyphConsumersClosed = true;

        try
        {
            DisplayIdentifierService.Hide();
            _hotkeyService = null;
            AppServices.HotkeyService = null;
            AppServices.UpdateCheckService = null;

            if (_displayEventManager != null)
            {
                _displayEventManager.DisplayTopologyChanged -= OnDisplayTopologyChanged;
                Safe.Dispose(_displayEventManager);
                _displayEventManager = null;
                AppServices.DisplayEventManager = null;
            }

            Safe.Dispose(_ddcRecoveryService);
            _ddcRecoveryService = null;

            TryDrainQuickly(TimeSpan.FromMilliseconds(TimeConstants.NormalShutdownDrainTimeoutMs));

            NightLightProvider.EnabledStateChanged -= OnNightLightEnabledStateChanged;
            try { NightLightProvider.Shutdown(); }
            catch (Exception ex)
            {
                TADNLog.Log($"BrightnessAvaloniaApp.NightLightProvider.Shutdown failed: {ex.Message}");
            }

            if (_settings != null) _settings.Changed -= OnSettingsChanged;

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
                DetachProfileManager(AppServices.ProfileManager);
                Safe.Dispose(AppServices.ProfileManager);
                AppServices.ProfileManager = null;
            }

            // Shared by the monitor service and the profile manager, so it goes after both
            Safe.Dispose(_knownDisplays);
            _knownDisplays = null;
            AppServices.KnownDisplays = null;

            Safe.Dispose(_brightnessRangeProvider);
            _brightnessRangeProvider = null;
            AppServices.MonitorBrightnessRangeProvider = null;

            if (_theme != null)
            {
                _theme.ThemeChanged -= OnThemeChanged;
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
                    TADNLog.Log($"Brightness flyout warm-slot shutdown failed: {exception.Message}");
                }
            }

            _brightnessFlyoutWarmSlot = null;
            Safe.Dispose(_trayMenu);
            _trayMenu = null;

            if (_settingsWindow != null)
            {
                _settingsWindow.Closed -= OnSettingsWindowClosed;
                try { _settingsWindow.Close(); }
                catch (Exception exception)
                {
                    TADNLog.Log($"Brightness settings-window shutdown failed: {exception.Message}");
                }

                _settingsWindow = null;
            }

            if (_brightnessFlyout != null)
            {
                _brightnessFlyout.BrightnessUpdated -= RequestTrayRefresh;
                _brightnessFlyout.SettingsRequested -= OpenSettings;
                _brightnessFlyout.Closed -= OnBrightnessFlyoutClosed;
                try { _brightnessFlyout.Close(); }
                catch (Exception exception)
                {
                    glyphConsumersClosed = false;
                    TADNLog.Log($"Brightness flyout shutdown failed: {exception.Message}");
                }

                _brightnessFlyout = null;
                AppServices.BrightnessFlyout = null;
            }

            if (glyphConsumersClosed)
                TrayAppDotNETCommon.Visuals.SkiaFlyoutGlyphIcon.DisposeSharedResources();

            if (_trayIcon != null)
            {
                _trayIcon.LeftClick -= OnTrayLeftClick;
                _trayIcon.LeftDoubleClick -= OnTrayLeftDoubleClick;
                _trayIcon.KeySelect -= OnTrayKeySelect;
                _trayIcon.RightClick -= OnTrayRightClick;
                _trayIcon.Scrolled -= OnTrayScrolled;
                _trayIcon.PrecisionTouchpadScrolled -= OnTrayPrecisionTouchpadScrolled;
                _trayIcon.RefreshNeeded -= RequestTrayRefresh;
                _trayIcon.BalloonClicked -= OnUpdateBalloonClicked;
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
            TADNLog.Log($"BrightnessAvaloniaApp.ShutdownServices: {ex}");
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
            TADNLog.Log($"BrightnessAvaloniaApp.TryDrainQuickly: {ex.Message}");
        }
    }

    private void ExitApplication() => _host?.Exit();

    private static string L(string key) => LocalizationManager.Instance[key];
}
