#if HOTAVALONIA_ENABLE
using HotAvalonia;
#endif
#if DEBUG
using AppThemeHotReload = TrayAppDotNETCommon.Visuals.AppThemeHotReload;
using GlyphCatalogHotReload = TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload;
#endif
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using BatteryTrayAppDotNET.Services;
using BatteryTrayAppDotNET.UI.Flyout;
using BatteryTrayAppDotNET.UI.Settings;
using BatteryTrayAppDotNET.UI.Tray;
using Microsoft.Win32;
using TrayAppDotNETCommon.UI.WarmWindows;
using BatteryHotkeyAction = TrayAppDotNETCommon.Models.HotkeyAction;
using BatteryHotkeyFiredEventArgs =
    TrayAppDotNETCommon.Services.HotkeyFiredEventArgs<TrayAppDotNETCommon.Models.HotkeyAction>;
using BatteryHotkeyService =
    TrayAppDotNETCommon.Services.GlobalHotkeyService<TrayAppDotNETCommon.Models.HotkeyAction,
        TrayAppDotNETCommon.Models.HotkeyBinding>;

namespace BatteryTrayAppDotNET;

internal static class BatteryAvaloniaRunner
{
    public static int Run(string[] args) =>
        TrayAppDotNETAppRunner.Run<BatteryAvaloniaApp>(args, AppSettings.GetDefaultPath, configure: Configure);

    private static AppBuilder Configure(AppBuilder builder)
    {
#if HOTAVALONIA_ENABLE
        builder = builder.UseHotReload();
#endif
        return builder;
    }
}

internal sealed class BatteryAvaloniaApp : Application
{
    private TrayAppDotNETAppHost<AppSettings, AppTheme>? _host;
    private AppTheme? _theme;
    private AppSettings? _settings;
    private BatteryMonitorService? _batteryMonitor;
    private BatteryPowerModeService? _powerModes;
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private BatteryTrayIcon? _trayIconRenderer;
    private TrayIconPresenter<TrayIconRenderInput>? _trayIconPresenter;
    private TrayMenuController? _trayMenu;
    private TrayWindowToggle? _trayToggle;
    private readonly BatteryTriggerEngine _triggerEngine = new();
    private BatteryFlyoutWindow? _batteryFlyout;
    private BatterySettingsWindow? _settingsWindow;
    private SettingsFlyoutKeepOpenCoordinator? _settingsFlyoutKeepOpen;
    private TrayAppDotNETWarmWindowSlot<BatteryFlyoutWindow>? _batteryFlyoutWarmSlot;
    private BatteryHotkeyService? _hotkeyService;
    private bool _lastBalloonWasTrigger;
    private bool _isBatteryReportRunning;
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
        RestoreStartupUndockedFlyoutIfRequested();
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
            _powerModes = new BatteryPowerModeService();
            _ = _powerModes.RefreshAsync();
            _batteryMonitor = new BatteryMonitorService(_settings, BatteryLearnedUsageStore.DefaultPath);
            _batteryMonitor.StateChanged += OnBatteryStateChanged;
            _batteryMonitor.Start();
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryAvaloniaApp battery monitor init failed: {ex}");
        }

        try
        {
            _trayIconRenderer = new BatteryTrayIcon(_theme) { IsLightTheme = ResolveEffectiveIsLightTheme() };
            ApplyTrayIconColorOverride();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryAvaloniaApp tray renderer init failed: {ex}");
        }

        _hotkeyService = _host?.StartHotkeyService(
            static prefix => new BatteryHotkeyService(prefix),
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
        _trayIcon.BalloonClicked += OnBalloonClicked;
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

    private void OnBatteryStateChanged()
    {
        RequestTrayRefresh();
        EvaluateTriggers();
    }

    /// <summary>
    /// Feeds the latest reading to the trigger engine. The trigger list is read live, so edits on the Triggers page
    /// apply from the next reading.
    /// </summary>
    private void EvaluateTriggers()
    {
        if (_batteryMonitor == null) return;

        BatterySnapshot snapshot = _batteryMonitor.Snapshot;
        IReadOnlyList<BatteryTriggerFiring> firings = _triggerEngine.Observe(snapshot, _settings?.Triggers ?? []);
        if (firings.Count == 0) return;

        foreach (BatteryTriggerFiring firing in firings)
            TADNLog.Log($"BatteryAvaloniaApp trigger '{firing.Trigger.Title}': {firing.Condition} -> {firing.Action}");
        Dispatcher.UIThread.Post(() => RunTriggerActions(firings, snapshot.ChargePercentage));
    }

    /// <summary>
    /// Runs each distinct action once per reading. Notifications from several triggers are combined into one
    /// balloon, since a second balloon would replace the first.
    /// </summary>
    private void RunTriggerActions(IReadOnlyList<BatteryTriggerFiring> firings, int chargePercentage)
    {
        if (_shuttingDown) return;

        List<BatteryTriggerCondition> notifiedConditions = [];
        HashSet<BatteryTriggerAction> ranActions = [];
        foreach (BatteryTriggerFiring firing in firings)
        {
            if (firing.Action == BatteryTriggerAction.ShowNotification)
            {
                if (!notifiedConditions.Contains(firing.Condition))
                    notifiedConditions.Add(firing.Condition);
                continue;
            }

            if (!ranActions.Add(firing.Action)) continue;

            try { RunTriggerAction(firing.Action); }
            catch (Exception ex) { TADNLog.Log($"BatteryAvaloniaApp.RunTriggerActions {firing.Action}: {ex}"); }
        }

        if (notifiedConditions.Count > 0)
            ShowTriggerNotification(notifiedConditions, chargePercentage);
    }

    private void RunTriggerAction(BatteryTriggerAction action)
    {
        switch (action)
        {
            case BatteryTriggerAction.OpenFlyout:
                ShowBatteryFlyout();
                break;
            case BatteryTriggerAction.OpenSettings:
                OpenSettings();
                break;
            case BatteryTriggerAction.OpenPowerSettings:
                BatteryFlyoutWindow.OpenModernPowerSettings();
                break;
        }
    }

    private void ShowTriggerNotification(List<BatteryTriggerCondition> conditions, int chargePercentage)
    {
        if (_trayIcon == null) return;

        List<string> lines = [];
        for (int i = 1; i < conditions.Count; i++)
            lines.Add(TriggerConditionText(conditions[i]));
        lines.Add(string.Format(L(nameof(AppStrings.TriggerNotification_ChargeFormat)), chargePercentage));

        _lastBalloonWasTrigger = true;
        _trayIcon.ShowBalloon(TriggerConditionText(conditions[0]), string.Join(separator: '\n', lines));
    }

    private static string TriggerConditionText(BatteryTriggerCondition condition) =>
        condition switch
        {
            BatteryTriggerCondition.BatteryBelow20 => L(nameof(AppStrings.Settings_Triggers_Condition_BatteryBelow20)),
            BatteryTriggerCondition.BatteryBelow10 => L(nameof(AppStrings.Settings_Triggers_Condition_BatteryBelow10)),
            BatteryTriggerCondition.BatteryAbove80 => L(nameof(AppStrings.Settings_Triggers_Condition_BatteryAbove80)),
            BatteryTriggerCondition.ChargingStarted => L(nameof(AppStrings.Settings_Triggers_Condition_ChargingStarted)),
            BatteryTriggerCondition.ChargingStopped => L(nameof(AppStrings.Settings_Triggers_Condition_ChargingStopped)),
            BatteryTriggerCondition.ExternalPowerConnected =>
                L(nameof(AppStrings.Settings_Triggers_Condition_ExternalPowerConnected)),
            BatteryTriggerCondition.ExternalPowerDisconnected =>
                L(nameof(AppStrings.Settings_Triggers_Condition_ExternalPowerDisconnected)),
            BatteryTriggerCondition.FullyCharged => L(nameof(AppStrings.Settings_Triggers_Condition_FullyCharged)),
            _ => condition.ToString()
        };

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.StatusChange or PowerModes.Resume)
            Dispatcher.UIThread.Post(() => _batteryMonitor?.ForceRefresh());
    }

    private void OnHotkeyFired(object? sender, BatteryHotkeyFiredEventArgs e)
    {
        try { HandleHotkey(e.Action); }
        catch (Exception ex) { TADNLog.Log($"BatteryAvaloniaApp.OnHotkeyFired: {ex}"); }
    }

    private void HandleHotkey(BatteryHotkeyAction action)
    {
        switch (action)
        {
            case BatteryHotkeyAction.OpenSettings:
                OpenSettings();
                break;
            case BatteryHotkeyAction.OpenFlyout:
                ShowBatteryFlyout();
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
            if (_settings != null) ApplyToolTipDelayToOpenWindows();

            if (_hotkeyService != null && _settings != null)
                _hotkeyService.Apply(_settings.Hotkeys);
            ApplyKeepWarmPolicies();
            RequestTrayRefresh();
        });
    }

    private void ApplyKeepWarmPolicies()
    {
        if (_batteryFlyoutWarmSlot != null || _settings?.KeepFlyoutWarm == true)
            BatteryFlyoutWarmSlot.ApplyKeepWarmPolicy(CreateManagedBatteryFlyout);
        _trayMenu?.ApplyKeepWarmPolicy();
    }

    private void ApplyToolTipDelayToOpenWindows()
    {
        if (_settingsWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_settingsWindow);
        if (_batteryFlyout != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_batteryFlyout);
        if (_trayMenu?.Current is { } trayMenu) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(trayMenu);
    }

    private void ShowUpdateNotification(string title, string body)
    {
        _lastBalloonWasTrigger = false;
        _trayIcon?.ShowBalloon(title, body);
    }

    // The balloon is shared: a trigger balloon opens the flyout, an update balloon opens the About page
    private void OnBalloonClicked()
    {
        if (_lastBalloonWasTrigger)
        {
            ShowBatteryFlyout();
            return;
        }

        if (_host?.UpdateService?.AvailableUpdate == null) return;

        OpenSettings(BatterySettingsPage.About);
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
    /// Invalidates the renderer so tray glyph edits are visible immediately.
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

        BatterySnapshot snapshot = _batteryMonitor?.Snapshot ?? BatterySnapshot.Unknown;
        _trayIconRenderer?.Snapshot = snapshot;
        _trayIconPresenter.Refresh(BuildTrayTooltip(snapshot));
    }

    private static string BuildTrayTooltip(BatterySnapshot snapshot)
    {
        string title = L(nameof(AppStrings.Tray_Tooltip_Title));
        if (!snapshot.BatteryPresent) return title + "\n" + L(nameof(AppStrings.Tray_Tooltip_NoBattery));

        List<string> lines =
        [
            Format(nameof(AppStrings.Tray_Tooltip_Charge_Format), title, snapshot.ChargePercentage),
            L(snapshot.IsFullyCharged
                ? nameof(AppStrings.Tray_Tooltip_PluggedInFull)
                : snapshot.IsCharging
                    ? nameof(AppStrings.Tray_Tooltip_Charging)
                    : snapshot.IsOnExternalPower
                        ? nameof(AppStrings.Tray_Tooltip_PluggedIn)
                        : nameof(AppStrings.Tray_Tooltip_OnBattery))
        ];

        if (snapshot is { EstimatedTimeRemaining: not null, IsFullyCharged: false })
        {
            lines.Add(Format(
                nameof(AppStrings.Tray_Tooltip_TimeRemaining_Format),
                FormatTimeSpan(snapshot.EstimatedTimeRemaining.Value)));
        }

        if (snapshot.CurrentBatteryPowerWatts.HasValue)
        {
            lines.Add(Format(
                nameof(AppStrings.Tray_Tooltip_BatteryPower_Format),
                snapshot.CurrentBatteryPowerWatts.Value));
        }

        if (snapshot.HealthPercent.HasValue)
            lines.Add(Format(nameof(AppStrings.Tray_Tooltip_Health_Format), snapshot.HealthPercent.Value));

        return string.Join(separator: '\n', lines);
    }

    private void OnTrayLeftClick()
    {
        switch (_trayToggle?.Decide(_batteryFlyout) ?? TrayToggleAction.Show)
        {
            case TrayToggleAction.Hide:
                _batteryFlyout?.Hide();
                break;
            // A floating flyout behind another window comes forward where it is instead of docking again
            case TrayToggleAction.Activate:
                _batteryFlyout?.Activate();
                break;
            case TrayToggleAction.Show:
                ShowBatteryFlyout();
                break;
        }
    }

    private void OnTrayLeftDoubleClick() => OpenSettings();

    // The classic notification area may report one Enter twice, so the keyboard only shows the flyout
    private void OnTrayKeySelect() => ShowBatteryFlyout();

    private void OnTrayRightClick(Point point) =>
        Dispatcher.UIThread.Post(() => ShowTrayContextMenu(point));

    private void ShowTrayContextMenu(Point point)
    {
        if (_trayIcon == null || _settings == null || _trayMenu == null) return;

        PixelPoint cursorPoint = new((int)Math.Round(point.X), (int)Math.Round(point.Y));
        _trayMenu.Show(_trayIcon, cursorPoint, _settings.ContextMenuPosition);
    }

    private TrayMenuWindow CreateTrayMenuWindow()
    {
        if (_settings == null)
            throw new InvalidOperationException("Battery tray menu requires settings.");

        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry(L(nameof(AppStrings.Tray_PowerOptions)), OpenPowerOptions)
        {
            Node = UI.ControlMap.TrayMenu.OpenPowerOptions
        });
        entries.Add(new ContextMenuEntry(L(nameof(AppStrings.Tray_BatteryReport)), OpenBatteryReport)
        {
            Node = UI.ControlMap.TrayMenu.OpenBatteryReport
        });
        return new TrayMenuWindow(
            entries,
            TrayMenuWindowOptions.Create(
                _settings,
                _theme,
                UI.ControlMap.TrayMenu.ID,
                TrayMenuStandardEntry.Settings(OpenSettings, UI.ControlMap.TrayMenu.OpenSettings),
                TrayMenuStandardEntry.Exit(ExitApplication, UI.ControlMap.TrayMenu.Exit)) with
            {
                ScrollToBottom = true
            });
    }

    private static void OpenPowerOptions()
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = "control.exe", Arguments = "/name Microsoft.PowerOptions", UseShellExecute = false
            });
        }
        catch (Exception ex) { TADNLog.Log($"BatteryAvaloniaApp.OpenPowerOptions: {ex.Message}"); }
    }

    private void ShowBatteryFlyout(bool activate = true, bool holdOpenForSettings = true)
    {
        if (_batteryMonitor == null || _settings == null || _trayIcon == null) return;

        _batteryFlyout ??= BatteryFlyoutWarmSlot.TakeOrCreate(CreateManagedBatteryFlyout);
        _batteryFlyout.Redock();
        _batteryFlyout.ShowAt(_trayIcon, activate);
        if (holdOpenForSettings && _settingsWindow is { IsVisible: true })
            SettingsFlyoutKeepOpen.HoldOpen();
    }

    private BatteryFlyoutWindow CreateManagedBatteryFlyout()
    {
        if (_batteryMonitor == null || _settings == null)
            throw new InvalidOperationException("Battery flyout requires battery monitor and settings.");

        _powerModes ??= new BatteryPowerModeService();
        BatteryFlyoutWindow flyout = new(_batteryMonitor, _settings, OpenSettings, _powerModes);
        _batteryFlyout = flyout;
        flyout.Closed += OnBatteryFlyoutClosed;
        return flyout;
    }

    private void RestoreStartupUndockedFlyoutIfRequested()
    {
        if (_batteryMonitor == null || _settings == null || _trayIcon == null) return;
        if (!FlyoutDockingController.ShouldRestoreOnStartup(_settings)) return;

        _batteryFlyout ??= BatteryFlyoutWarmSlot.TakeOrCreate(CreateManagedBatteryFlyout);
        _batteryFlyout.ShowAt(_trayIcon);
    }

    private TrayAppDotNETWarmWindowSlot<BatteryFlyoutWindow> BatteryFlyoutWarmSlot =>
        _batteryFlyoutWarmSlot ??= new TrayAppDotNETWarmWindowSlot<BatteryFlyoutWindow>(
            () => _settings?.KeepFlyoutWarm ?? true,
            ex => TADNLog.Log($"Battery flyout keep-warm: {ex.Message}"));

    private SettingsFlyoutKeepOpenCoordinator SettingsFlyoutKeepOpen =>
        _settingsFlyoutKeepOpen ??= new SettingsFlyoutKeepOpenCoordinator(
            () => _settingsWindow,
            () => _batteryFlyout,
            () => ShowBatteryFlyout(activate: false, holdOpenForSettings: false));

    private void ScheduleKeepWarmPriming()
    {
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                if (_settings?.KeepFlyoutWarm == true && _batteryMonitor != null)
                    await BatteryFlyoutWarmSlot.PrimeAsync(CreateManagedBatteryFlyout);
                if (_trayMenu != null)
                    await _trayMenu.PrimeAsync();
            }
            catch (Exception ex)
            {
                TADNLog.Log($"BatteryAvaloniaApp.ScheduleKeepWarmPriming: {ex.Message}");
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void OnBatteryFlyoutClosed(object? sender, EventArgs e)
    {
        if (sender is BatteryFlyoutWindow flyout) flyout.Closed -= OnBatteryFlyoutClosed;
        if (ReferenceEquals(_batteryFlyout, sender)) _batteryFlyout = null;
    }

    private void OpenSettings() => OpenSettings(BatterySettingsPage.General);

    private void OpenSettings(BatterySettingsPage page)
    {
        if (_settings == null) return;

        if (_settingsWindow == null)
        {
            _settingsWindow = new BatterySettingsWindow(_settings, ShowUninstallerWindow);
            SettingsFlyoutKeepOpen.Attach(_settingsWindow);
            _settingsWindow.Closed += OnSettingsWindowClosed;
        }

        SettingsFlyoutKeepOpen.HoldOpen();
        _settingsWindow.SelectPage(page);
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

    private void OpenBatteryReport() => _ = OpenBatteryReportAsync();

    /// <summary>
    /// Writes the powercfg battery report and opens it. The wait for powercfg runs off the UI thread, and a request
    /// made while a report is still being written is ignored, since both runs would write the same file.
    /// </summary>
    private async Task OpenBatteryReportAsync()
    {
        if (_isBatteryReportRunning) return;

        _isBatteryReportRunning = true;
        try
        {
            string reportsDir = Path.Combine(Program.AppLocalAppDataDirectory, path2: "reports");
            Directory.CreateDirectory(reportsDir);
            string reportPath = Path.Combine(reportsDir, path2: "battery-report.html");
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = $"/batteryreport /output \"{reportPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return;

            using CancellationTokenSource timeout = new(TimeConstants.BatteryReportTimeoutMs);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TADNLog.Log(
                    "BatteryAvaloniaApp.OpenBatteryReport: powercfg did not exit within "
                    + $"{TimeConstants.BatteryReportTimeoutMs} ms");
            }

            if (_shuttingDown || !File.Exists(reportPath)) return;

            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = reportPath, UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryAvaloniaApp.OpenBatteryReport: {ex}");
        }
        finally
        {
            _isBatteryReportRunning = false;
        }
    }

    private Task<bool> ShowUninstallerWindow(
        TrayAppDotNETCommon.Models.InstallScope scope,
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
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;

            _hotkeyService = null;
            AppServices.HotkeyService = null;
            AppServices.UpdateCheckService = null;
            if (_settings != null) _settings.Changed -= OnSettingsChanged;
            AppServices.Settings = null;

            if (_batteryMonitor != null)
            {
                _batteryMonitor.StateChanged -= OnBatteryStateChanged;
                Safe.Dispose(_batteryMonitor);
                _batteryMonitor = null;
            }

            if (_theme != null)
            {
                _theme.ThemeChanged -= OnThemeChanged;
                _theme = null;
                AppServices.Theme = null;
            }

            Safe.Dispose(_trayMenu);
            _trayMenu = null;

            Safe.Dispose(_settingsFlyoutKeepOpen);
            _settingsFlyoutKeepOpen = null;

            if (_batteryFlyout != null)
            {
                _batteryFlyout.Closed -= OnBatteryFlyoutClosed;
                try { _batteryFlyout.Close(); }
                catch
                {
                    // ignored
                }

                _batteryFlyout = null;
            }

            Safe.Dispose(_batteryFlyoutWarmSlot);
            _batteryFlyoutWarmSlot = null;
            Safe.Dispose(_powerModes);
            _powerModes = null;

            if (_trayIcon != null)
            {
                _trayIcon.LeftClick -= OnTrayLeftClick;
                _trayIcon.LeftDoubleClick -= OnTrayLeftDoubleClick;
                _trayIcon.KeySelect -= OnTrayKeySelect;
                _trayIcon.RightClick -= OnTrayRightClick;
                _trayIcon.RefreshNeeded -= RequestTrayRefresh;
                _trayIcon.BalloonClicked -= OnBalloonClicked;
            }

            Safe.Dispose(_trayIconPresenter);
            _trayIconPresenter = null;
            _trayIconRenderer = null;
            Safe.Dispose(_trayToggle);
            _trayToggle = null;
            Safe.Dispose(_trayIcon);
            _trayIcon = null;

            if (_settingsWindow != null)
            {
                _settingsWindow.Closed -= OnSettingsWindowClosed;
                try { _settingsWindow.Close(); }
                catch
                {
                    // ignored
                }

                _settingsWindow = null;
            }

            TADNLog.Flush();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryAvaloniaApp.ShutdownServices: {ex}");
        }
    }

    private void ExitApplication() => _host?.Exit();

    private static string FormatTimeSpan(TimeSpan value)
    {
        if (value.TotalDays >= 1)
            return Format(nameof(AppStrings.Tray_Tooltip_Duration_DaysHours_Format), (int)value.TotalDays, value.Hours);
        return value.TotalHours >= 1
            ? Format(nameof(AppStrings.Tray_Tooltip_Duration_HoursMinutes_Format), (int)value.TotalHours, value.Minutes)
            : Format(nameof(AppStrings.Tray_Tooltip_Duration_Minutes_Format), Math.Max(val1: 1, value.Minutes));
    }

    private static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, L(key), args);

    private static string L(string key) => LocalizationManager.Instance[key];
}
