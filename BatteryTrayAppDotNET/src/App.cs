using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using BatteryTrayAppDotNET.Localization;
using BatteryTrayAppDotNET.Models;
using BatteryTrayAppDotNET.Services;
using BatteryTrayAppDotNET.UI.Flyout;
using BatteryTrayAppDotNET.UI.Settings;
using BatteryTrayAppDotNET.UI.Tray;
using HotAvalonia;
using Microsoft.Win32;
using TrayAppDotNETCommon.UI.WarmWindows;
using BatteryHotkeyAction = TrayAppDotNETCommon.Models.HotkeyAction;
using BatteryHotkeyFiredEventArgs = TrayAppDotNETCommon.Services.HotkeyFiredEventArgs;
using BatteryHotkeyService = TrayAppDotNETCommon.Services.GlobalHotkeyService;
using BatteryInstallScope = TrayAppDotNETCommon.InstallScope;
using BatteryWatcherMonitor = TrayAppDotNETCommon.Services.WatcherMonitor;

namespace BatteryTrayAppDotNET;

internal static class BatteryAvaloniaRunner
{
    public static int Run(string[] args)
    {
        return TrayAppDotNETAvalonia.StartWithExplicitShutdown<BatteryAvaloniaApp>(
            args,
            builder =>
            {
                builder = TrayAppDotNETAvalonia.UseConfiguredRenderingBackend(builder);
                builder = builder.UseHotReload();

                return builder;
            });
    }
}

internal sealed class BatteryAvaloniaApp : Application
{
    private AppTheme? _theme;
    private AppSettings? _settings;
    private BatteryMonitorService? _batteryMonitor;
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private BatteryTrayIcon? _trayIconRenderer;
    private BatteryFlyoutWindow? _batteryFlyout;
    private BatteryTrayMenuWindow? _trayMenuWindow;
    private BatterySettingsWindow? _settingsWindow;
    private SettingsFlyoutKeepOpenCoordinator? _settingsFlyoutKeepOpen;
    private TrayAppDotNETWarmWindowSlot<BatteryFlyoutWindow>? _batteryFlyoutWarmSlot;
    private TrayAppDotNETWarmWindowSlot<BatteryTrayMenuWindow>? _trayMenuWarmSlot;
    private BatteryHotkeyService? _hotkeyService;
    private BatteryWatcherMonitor? _watcherMonitor;
    private UpdateCheckService? _updateCheckService;
    private int _lastNotifiedUpdateVersion;
    private bool _shuttingDown;

    public override void Initialize()
    {
        TrayAppDotNETAvalonia.InitializeDefaults(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        TADNLog.Initialize();
        TADNLog.Log("BatteryAvaloniaApp.OnFrameworkInitializationCompleted");
        LocalizationManager.Instance.Initialize(
            Strings.ResourceManager,
            culture => Strings.Culture = culture);
        TrayAppDotNETAvalonia.WireCrashHandlers(TADNLog.Shutdown);
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
                LogSettingsLoadFailed = ex => TADNLog.Log($"BatteryAvaloniaApp settings load failed: {ex}"),
                GetThemePath = AppTheme.GetDefaultPath,
                LoadTheme = AppTheme.LoadOrDefault,
                ConfigureTheme = ConfigureTheme,
                LogThemeLoadFailed = ex => TADNLog.Log($"BatteryAvaloniaApp theme load failed: {ex}"),
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
            _batteryMonitor = new BatteryMonitorService();
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
            _trayIconRenderer = new BatteryTrayIcon(_theme)
            {
                IsLightTheme = ResolveEffectiveIsLightTheme(),
            };
            ApplyTrayIconColorOverride();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryAvaloniaApp tray renderer init failed: {ex}");
        }

        try
        {
            _hotkeyService = new BatteryHotkeyService(Program.ApplicationName + ".HotkeySink");
            _hotkeyService.Initialize();
            _hotkeyService.Fired += OnHotkeyFired;
            _hotkeyService.Apply(_settings?.Hotkeys ?? []);
            AppServices.HotkeyService = _hotkeyService;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryAvaloniaApp hotkey init failed: {ex}");
        }

        try
        {
            _watcherMonitor = TrayAppDotNETAvalonia.CreateWatcherMonitor(Program.WatcherPID, ExitApplication);
            _watcherMonitor.Start();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryAvaloniaApp watcher init failed: {ex}");
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
                TADNLog.Log($"BatteryAvaloniaApp update service init failed: {ex}");
            }
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayAppDotNETShellTrayIcon(Constants.TrayIconGUID, Program.ApplicationName + ".TrayIcon")
        {
            IsScrollEnabled = false,
            IsVisible = true,
        };
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.LeftDoubleClick += OnTrayLeftDoubleClick;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.RefreshNeeded += RequestTrayRefresh;
        _trayIcon.BalloonClicked += OnUpdateBalloonClicked;
    }

    private void OnBatteryStateChanged() => RequestTrayRefresh();

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
            if (_settings != null)
                TrayAppDotNETAnimationPolicy.Apply(this, _settings.AnimationMode);
            ApplyTrayIconColorOverride();
            RequestTrayRefresh();
        });

    private void OnSettingsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeVariant();
            ApplyTrayIconColorOverride();
            if (_settings != null)
            {
                TrayAppDotNETAnimationPolicy.Apply(this, _settings.AnimationMode);
                TrayAppDotNETToolTip.ShowDelayMs = _settings.ToolTipShowDelayMs;
                ApplyToolTipDelayToOpenWindows();
            }

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
        if (_trayMenuWarmSlot != null || _settings?.KeepTrayContextMenuWarm == true)
            TrayMenuWarmSlot.ApplyKeepWarmPolicy(CreateTrayMenuWindow);
    }

    private void ApplyToolTipDelayToOpenWindows()
    {
        if (_settingsWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_settingsWindow);
        if (_batteryFlyout != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_batteryFlyout);
        if (_trayMenuWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_trayMenuWindow);
    }

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

        OpenSettings(BatterySettingsPage.About);
    }

    private bool ResolveEffectiveIsLightTheme() => AppTheme.ResolveEffectiveIsLightTheme(_settings);

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

        BatterySnapshot snapshot = _batteryMonitor?.Snapshot ?? BatterySnapshot.Unknown;
        _trayIcon.SetTooltip(BuildTrayTooltip(snapshot));
        if (_trayIconRenderer != null)
        {
            _trayIconRenderer.SetSnapshot(snapshot);
            if (_trayIconRenderer.CreateIcon() is { } icon)
                _trayIcon.SetIcon(icon);
        }
        else if (AppTheme.LoadAppNativeIcon() is { } fallbackIcon)
        {
            using (fallbackIcon)
                _trayIcon.SetIcon(fallbackIcon);
        }
    }

    private static string BuildTrayTooltip(BatterySnapshot snapshot)
    {
        if (!snapshot.BatteryPresent) return $"{Constants.ApplicationName}\nNo battery detected";

        List<string> lines =
        [
            $"{Constants.ApplicationName}: {snapshot.ChargePercentage}%",
            snapshot.IsFullyCharged
                ? "Plugged in, full"
                : snapshot.IsCharging
                    ? "Charging"
                    : snapshot.IsOnExternalPower
                        ? "Plugged in"
                        : "On battery",
        ];

        if (snapshot.EstimatedTimeRemaining.HasValue && !snapshot.IsFullyCharged)
            lines.Add($"Time remaining: {FormatTimeSpan(snapshot.EstimatedTimeRemaining.Value)}");
        if (snapshot.CurrentBatteryPowerWatts.HasValue)
            lines.Add($"Battery power: {snapshot.CurrentBatteryPowerWatts.Value:F1} W");
        if (snapshot.HealthPercent.HasValue)
            lines.Add($"Health: {snapshot.HealthPercent.Value:F0}%");

        return string.Join('\n', lines);
    }

    private void OnTrayLeftClick()
    {
        if (_batteryFlyout is { IsVisible: true })
        {
            _batteryFlyout.Hide();
            return;
        }

        ShowBatteryFlyout();
    }

    private void OnTrayLeftDoubleClick() => OpenSettings();

    private void OnTrayRightClick(Point point) =>
        Dispatcher.UIThread.Post(() => ShowTrayContextMenu(point));

    private void ShowTrayContextMenu(Point point)
    {
        if (_trayIcon == null || _settings == null) return;

        if (_trayMenuWindow is { IsVisible: true })
            _trayMenuWindow.DismissForWarmCache();

        BatteryTrayMenuWindow menuWindow = TrayMenuWarmSlot.TakeOrCreate(CreateTrayMenuWindow);
        _trayMenuWindow = menuWindow;

        PixelPoint cursorPoint = new((int)Math.Round(point.X), (int)Math.Round(point.Y));
        menuWindow.ShowAt(_trayIcon, cursorPoint, _settings.ContextMenuPosition);
    }

    private BatteryTrayMenuWindow CreateTrayMenuWindow()
    {
        if (_settings == null)
            throw new InvalidOperationException("Battery tray menu requires settings.");

        BatteryTrayMenuWindow menuWindow = new(
            _settings,
            TrayMenuPalette(),
            BatteryTrayMenuWindow.OpenPowerOptions,
            OpenBatteryReport,
            OpenSettings,
            ExitApplication);
        _trayMenuWindow = menuWindow;
        menuWindow.Closed += OnTrayMenuClosed;
        return menuWindow;
    }

    private void OnTrayMenuClosed(object? sender, EventArgs e)
    {
        if (sender is BatteryTrayMenuWindow menu)
            menu.Closed -= OnTrayMenuClosed;
        if (ReferenceEquals(_trayMenuWindow, sender))
            _trayMenuWindow = null;
    }

    private SettingsPalette TrayMenuPalette() =>
        BatterySettingsPalette.Create(_theme, _settings, ResolveEffectiveIsLightTheme());

    private void ShowBatteryFlyout(bool activate = true)
    {
        if (_batteryMonitor == null || _settings == null || _trayIcon == null) return;

        _batteryFlyout ??= BatteryFlyoutWarmSlot.TakeOrCreate(CreateManagedBatteryFlyout);
        _batteryFlyout.ShowAt(_trayIcon, activate);
    }

    private BatteryFlyoutWindow CreateManagedBatteryFlyout()
    {
        if (_batteryMonitor == null || _settings == null)
            throw new InvalidOperationException("Battery flyout requires battery monitor and settings.");

        BatteryFlyoutWindow flyout = new(_batteryMonitor, _settings, OpenSettings);
        _batteryFlyout = flyout;
        flyout.Closed += OnBatteryFlyoutClosed;
        return flyout;
    }

    private TrayAppDotNETWarmWindowSlot<BatteryFlyoutWindow> BatteryFlyoutWarmSlot =>
        _batteryFlyoutWarmSlot ??= new TrayAppDotNETWarmWindowSlot<BatteryFlyoutWindow>(
            () => _settings?.KeepFlyoutWarm ?? true,
            ex => TADNLog.Log($"Battery flyout keep-warm: {ex.Message}"));

    private TrayAppDotNETWarmWindowSlot<BatteryTrayMenuWindow> TrayMenuWarmSlot =>
        _trayMenuWarmSlot ??= new TrayAppDotNETWarmWindowSlot<BatteryTrayMenuWindow>(
            () => _settings?.KeepTrayContextMenuWarm ?? true,
            ex => TADNLog.Log($"Battery tray menu keep-warm: {ex.Message}"));

    private SettingsFlyoutKeepOpenCoordinator SettingsFlyoutKeepOpen =>
        _settingsFlyoutKeepOpen ??= new SettingsFlyoutKeepOpenCoordinator(
            () => _settingsWindow,
            () => _batteryFlyout,
            () => ShowBatteryFlyout(activate: false));

    private void ScheduleKeepWarmPriming()
    {
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                if (_settings?.KeepFlyoutWarm == true && _batteryMonitor != null)
                    await BatteryFlyoutWarmSlot.PrimeAsync(CreateManagedBatteryFlyout);
                if (_settings?.KeepTrayContextMenuWarm == true && _trayIcon != null)
                    await TrayMenuWarmSlot.PrimeAsync(CreateTrayMenuWindow);
            }
            catch (Exception ex)
            {
                TADNLog.Log($"BatteryAvaloniaApp.ScheduleKeepWarmPriming: {ex.Message}");
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private void OnBatteryFlyoutClosed(object? sender, EventArgs e)
    {
        if (_batteryFlyout == null) return;
        _batteryFlyout.Closed -= OnBatteryFlyoutClosed;
        _batteryFlyout = null;
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

        _ = Task.Delay(TimeConstants.PostSettingsCloseGCDelayMs).ContinueWith(_ =>
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }, TaskScheduler.Default);
    }

    private void OpenBatteryReport()
    {
        try
        {
            string reportsDir = Path.Combine(Program.AppLocalAppDataDirectory, "reports");
            Directory.CreateDirectory(reportsDir);
            string reportPath = Path.Combine(reportsDir, "battery-report.html");
            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = $"/batteryreport /output \"{reportPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            process.WaitForExit(10_000);
            if (File.Exists(reportPath))
            {
                using Process? _ = Process.Start(new ProcessStartInfo
                {
                    FileName = reportPath,
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryAvaloniaApp.OpenBatteryReport: {ex}");
        }
    }

    private void ShowUninstallerWindow(string installDir, BatteryInstallScope scope)
    {
        BatteryUninstallerWindow window = new(installDir, scope);
        if (_settingsWindow != null) window.Show(_settingsWindow);
        else window.Show();
    }

    private void ShutdownServices()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;

        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;

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

            if (_batteryMonitor != null)
            {
                _batteryMonitor.StateChanged -= OnBatteryStateChanged;
                Safe.Dispose(_batteryMonitor);
                _batteryMonitor = null;
            }

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

            Safe.Dispose(_settingsFlyoutKeepOpen);
            _settingsFlyoutKeepOpen = null;

            if (_batteryFlyout != null)
            {
                _batteryFlyout.Closed -= OnBatteryFlyoutClosed;
                try { _batteryFlyout.Close(); }
                catch { }
                _batteryFlyout = null;
            }

            Safe.Dispose(_batteryFlyoutWarmSlot);
            _batteryFlyoutWarmSlot = null;

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
            TADNLog.Log($"BatteryAvaloniaApp.ShutdownServices: {ex}");
        }
    }

    private void ExitApplication()
    {
        TADNLog.Log("BatteryAvaloniaApp.ExitApplication");
        ShutdownServices();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static string FormatTimeSpan(TimeSpan value)
    {
        if (value.TotalDays >= 1) return $"{(int)value.TotalDays}d {value.Hours}h";
        if (value.TotalHours >= 1) return $"{(int)value.TotalHours}h {value.Minutes}m";
        return $"{Math.Max(1, value.Minutes)}m";
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
