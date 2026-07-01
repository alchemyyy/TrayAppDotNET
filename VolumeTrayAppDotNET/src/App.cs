using System.ComponentModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using HotAvalonia;
using TrayAppDotNETCommon.UI.WarmWindows;
using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Localization;
using VolumeTrayAppDotNET.UI.Flyout;
using VolumeTrayAppDotNET.UI.Settings;
using VolumeTrayAppDotNET.UI.Tray;
using VolumeHotkeyAction = TrayAppDotNETCommon.Models.HotkeyAction;
using VolumeHotkeyFiredEventArgs = TrayAppDotNETCommon.Services.HotkeyFiredEventArgs;
using VolumeHotkeyService = TrayAppDotNETCommon.Services.GlobalHotkeyService;
using VolumeInstallScope = TrayAppDotNETCommon.InstallScope;
using VolumeWatcherMonitor = TrayAppDotNETCommon.Services.WatcherMonitor;

namespace VolumeTrayAppDotNET;

internal static class VolumeAvaloniaRunner
{
    public static int Run(string[] args)
    {
        return TrayAppDotNETAvalonia.StartWithExplicitShutdown<VolumeAvaloniaApp>(
            args,
            builder =>
            {
                builder = TrayAppDotNETAvalonia.UseConfiguredRenderingBackend(builder);
                builder = builder.UseHotReload();

                return builder;
            });
    }
}

internal sealed class VolumeAvaloniaApp : Application
{
    private AppTheme? _theme;
    private AppSettings? _settings;
    private DeviceSettings? _deviceSettings;
    private AudioDeviceManager? _audioManager;
    private AudioDevice? _trackedDevice;
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private VolumeTrayIcon? _trayIconRenderer;
    private readonly TrayIconRenderQueue _trayIconRenderQueue = new(TADNLog.Log);
    private VolumeTrayMenuWindow? _trayMenuWindow;
    private VolumeFlyoutWindow? _volumeFlyout;
    private VolumeSettingsWindow? _settingsWindow;
    private SettingsFlyoutKeepOpenCoordinator? _settingsFlyoutKeepOpen;
    private TrayAppDotNETWarmWindowSlot<VolumeFlyoutWindow>? _volumeFlyoutWarmSlot;
    private TrayAppDotNETWarmWindowSlot<VolumeTrayMenuWindow>? _trayMenuWarmSlot;
    private VolumeHotkeyService? _hotkeyService;
    private VolumeWatcherMonitor? _watcherMonitor;
    private UpdateCheckService? _updateCheckService;
    private AppVolumeFeedbackPlayer? _trayVolumeFeedback;
    private int _lastNotifiedUpdateVersion;
    private bool _shuttingDown;

    public override void Initialize() => TrayAppDotNETAvalonia.InitializeDefaults(this);

    public override void OnFrameworkInitializationCompleted()
    {
        TADNLog.Initialize();
        TADNLog.Log("VolumeAvaloniaApp.OnFrameworkInitializationCompleted");
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
                LogSettingsLoadFailed = ex => TADNLog.Log($"VolumeAvaloniaApp settings load failed: {ex}"),
                GetThemePath = AppTheme.GetDefaultPath,
                LoadTheme = AppTheme.LoadOrDefault,
                ConfigureTheme = ConfigureTheme,
                LogThemeLoadFailed = ex => TADNLog.Log($"VolumeAvaloniaApp theme load failed: {ex}"),
            });

        _settings = loaded.Settings;

        try
        {
            _deviceSettings = DeviceSettings.LoadOrDefault();
            AppServices.DeviceSettings = _deviceSettings;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"VolumeAvaloniaApp device settings load failed: {ex}");
            _deviceSettings = new DeviceSettings();
            AppServices.DeviceSettings = _deviceSettings;
        }
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
            if (_settings != null)
            {
                _audioManager = new AudioDeviceManager(Dispatcher.UIThread, _settings);
                _audioManager.PropertyChanged += OnAudioManagerPropertyChanged;
                AttachToTrackedDevice(_audioManager.DefaultDevice);
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"VolumeAvaloniaApp audio manager init failed: {ex}");
        }

        try
        {
            _trayIconRenderer = new VolumeTrayIcon(_theme) { IsLightTheme = ResolveEffectiveIsLightTheme(), };
            ApplyTrayIconColorOverride();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"VolumeAvaloniaApp tray renderer init failed: {ex}");
        }

        try
        {
            _hotkeyService = new VolumeHotkeyService(Program.ApplicationName + ".HotkeySink");
            _hotkeyService.Initialize();
            _hotkeyService.Fired += OnHotkeyFired;
            _hotkeyService.Apply(_settings?.Hotkeys ?? []);
            AppServices.HotkeyService = _hotkeyService;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"VolumeAvaloniaApp hotkey init failed: {ex}");
        }

        try
        {
            _watcherMonitor = TrayAppDotNETAvalonia.CreateWatcherMonitor(Program.WatcherPID, ExitApplication);
            _watcherMonitor.Start();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"VolumeAvaloniaApp watcher init failed: {ex}");
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
                TADNLog.Log($"VolumeAvaloniaApp update service init failed: {ex}");
            }
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TrayAppDotNETShellTrayIcon(Constants.TrayIconGUID, Program.ApplicationName + ".TrayIcon")
        {
            IsScrollEnabled = _settings?.TrayScrollEnabled ?? true, IsVisible = true,
        };
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.LeftDoubleClick += OnTrayLeftDoubleClick;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.Scrolled += OnTrayScrolled;
        _trayIcon.RefreshNeeded += RequestTrayRefresh;
        _trayIcon.BalloonClicked += OnUpdateBalloonClicked;
    }

    private void OnAudioManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioDeviceManager.DefaultDevice))
            AttachToTrackedDevice(_audioManager?.DefaultDevice);
        _trayMenuWarmSlot?.Invalidate();
    }

    private void AttachToTrackedDevice(AudioDevice? device)
    {
        if (ReferenceEquals(_trackedDevice, device)) return;

        if (_trackedDevice != null)
            _trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;

        _trackedDevice = device;

        if (_trackedDevice != null)
            _trackedDevice.PropertyChanged += OnTrackedDevicePropertyChanged;

        RequestTrayRefresh();
    }

    private void OnTrackedDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AudioDevice.Volume) or nameof(AudioDevice.IsMuted))
            RequestTrayRefresh();
    }

    private void OnHotkeyFired(object? sender, VolumeHotkeyFiredEventArgs e)
    {
        try { HandleHotkey(e.Action); }
        catch (Exception ex) { TADNLog.Log($"VolumeAvaloniaApp.OnHotkeyFired: {ex}"); }
    }

    private void HandleHotkey(VolumeHotkeyAction action)
    {
        switch (action)
        {
            case VolumeHotkeyAction.OpenSettings:
                OpenSettings();
                break;
            case VolumeHotkeyAction.OpenFlyout:
                ShowVolumeFlyout();
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

            _trayIcon?.IsScrollEnabled = _settings?.TrayScrollEnabled ?? true;
            if (_hotkeyService != null && _settings != null)
                _hotkeyService.Apply(_settings.Hotkeys);
            ApplyKeepWarmPolicies();
            RequestTrayRefresh();
        });
    }

    private void ApplyKeepWarmPolicies()
    {
        if (_volumeFlyoutWarmSlot != null || _settings?.KeepFlyoutWarm == true)
            VolumeFlyoutWarmSlot.ApplyKeepWarmPolicy(CreateManagedVolumeFlyout);
        if (_trayMenuWarmSlot != null || _settings?.KeepTrayContextMenuWarm == true)
            TrayMenuWarmSlot.ApplyKeepWarmPolicy(CreateTrayMenuWindow);
    }

    private void ApplyToolTipDelayToOpenWindows()
    {
        if (_settingsWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_settingsWindow);
        if (_volumeFlyout != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_volumeFlyout);
        if (_trayMenuWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_trayMenuWindow);
    }

    private void OnUpdateStateChanged()
    {
        _volumeFlyout?.NotifyUpdateStateChanged();

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

        ShowVolumeFlyout();
    }

    private bool ResolveEffectiveIsLightTheme() => _settings?.ThemeMode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        _ => _theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme,
    };

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

        if (_trackedDevice == null)
        {
            string missingTooltip = L("TrayTooltip_NoAudioDevice", "No audio device");
            _audioManager?.RequestMissingDefaultRecovery("tray-null-default");
            if (_trayIconRenderer != null)
            {
                VolumeTrayIcon renderer = _trayIconRenderer;
                renderer.Volume = 0;
                renderer.IsMuted = true;
                if (renderer.TryCreateRenderInput(out TrayIconRenderInput? missingInput) && missingInput != null)
                {
                    _trayIcon.SetTooltip(missingTooltip);
                    _trayIconRenderQueue.Request(
                        () => renderer.RenderIcon(missingInput),
                        icon => ApplyRenderedTrayIcon(icon, missingTooltip));
                    return;
                }
            }
            else if (AppTheme.LoadAppNativeIcon() is { } fallbackIcon)
            {
                using (fallbackIcon)
                    _trayIcon.SetIconAndTooltip(fallbackIcon, missingTooltip);
                return;
            }

            _trayIcon.SetTooltip(missingTooltip);
            return;
        }

        int percent = (int)Math.Round(_trackedDevice.Volume * 100);
        string tooltip = _trackedDevice.IsMuted
            ? string.Format(L("TrayTooltip_Muted_Format", "{0}: muted"), _trackedDevice.FriendlyName)
            : string.Format(L("TrayTooltip_Volume_Format", "{0}: {1}%"), _trackedDevice.FriendlyName, percent);

        if (_trayIconRenderer != null)
        {
            VolumeTrayIcon renderer = _trayIconRenderer;
            renderer.Volume = _trackedDevice.Volume;
            renderer.IsMuted = _trackedDevice.IsMuted;
            if (renderer.TryCreateRenderInput(out TrayIconRenderInput? input) && input != null)
            {
                _trayIcon.SetTooltip(tooltip);
                _trayIconRenderQueue.Request(
                    () => renderer.RenderIcon(input),
                    icon => ApplyRenderedTrayIcon(icon, tooltip));
                return;
            }
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

    private void OnTrayLeftClick()
    {
        if (_volumeFlyout is { IsVisible: true })
        {
            _volumeFlyout.Hide();
            return;
        }

        ShowVolumeFlyout();
    }

    private void OnTrayLeftDoubleClick()
    {
        TrayClickAction action = _settings?.TrayDoubleClickAction ?? TrayClickAction.Nothing;
        if (action == TrayClickAction.OpenSettings) OpenSettings();
        else ShowVolumeFlyout();
    }

    private void OnTrayRightClick(Point point) =>
        Dispatcher.UIThread.Post(() => ShowTrayContextMenu(point));

    private void OnTrayScrolled(int delta)
    {
        AudioDevice? device = _trackedDevice;
        if (device == null) return;

        double currentPercent = device.Volume * 100.0;
        int stepPercent = _settings?.WheelVolumeStepPercent ?? AppSettings.WheelVolumeStepPercentDefault;
        double next = Math.Clamp(currentPercent + (delta > 0 ? stepPercent : -stepPercent), 0, 100);
        if (Math.Abs(currentPercent - next) >= 0.001)
        {
            device.Volume = (float)(next / 100.0);
            if (_settings?.PlayTrayScrollVolumeChangeSound == true)
                (_trayVolumeFeedback ??= new AppVolumeFeedbackPlayer(Dispatcher.UIThread, _settings))
                    .PlayForDevice(device);
        }

        _trayIcon?.ShowTooltip();
    }

    private void ShowTrayContextMenu(Point point)
    {
        if (_trayIcon == null || _settings == null) return;

        if (_trayMenuWindow is { IsVisible: true })
            _trayMenuWindow.DismissForWarmCache();

        VolumeTrayMenuWindow menuWindow = TrayMenuWarmSlot.TakeOrCreate(CreateTrayMenuWindow);
        _trayMenuWindow = menuWindow;

        PixelPoint cursorPoint = new((int)Math.Round(point.X), (int)Math.Round(point.Y));
        menuWindow.ShowAt(_trayIcon, cursorPoint, _settings.ContextMenuPosition);
    }

    private VolumeTrayMenuWindow CreateTrayMenuWindow()
    {
        if (_settings == null)
            throw new InvalidOperationException("Volume tray menu requires settings.");
        AudioDevice[] devices = _audioManager?.Devices.ToArray() ?? [];
        VolumeTrayMenuWindow menuWindow = new(
            devices,
            _settings,
            TrayMenuPalette(),
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
        if (sender is VolumeTrayMenuWindow menu)
            menu.Closed -= OnTrayMenuClosed;
        if (ReferenceEquals(_trayMenuWindow, sender))
            _trayMenuWindow = null;
    }

    private SettingsPalette TrayMenuPalette() =>
        VolumeSettingsPalette.Create(_theme, _settings, ResolveEffectiveIsLightTheme());

    private void ShowVolumeFlyout(bool activate = true)
    {
        if (_audioManager == null || _settings == null || _trayIcon == null) return;

        _volumeFlyout ??= VolumeFlyoutWarmSlot.TakeOrCreate(CreateManagedVolumeFlyout);

        _volumeFlyout.Redock();
        _volumeFlyout.ShowAt(_trayIcon, activate);
    }

    private VolumeFlyoutWindow CreateManagedVolumeFlyout()
    {
        if (_audioManager == null || _settings == null)
            throw new InvalidOperationException("Volume flyout requires audio manager and settings.");

        VolumeFlyoutWindow flyout = new(_audioManager, _settings, OpenSettings);
        _volumeFlyout = flyout;
        flyout.Closed += OnVolumeFlyoutClosed;
        return flyout;
    }

    private TrayAppDotNETWarmWindowSlot<VolumeFlyoutWindow> VolumeFlyoutWarmSlot =>
        _volumeFlyoutWarmSlot ??= new TrayAppDotNETWarmWindowSlot<VolumeFlyoutWindow>(
            () => _settings?.KeepFlyoutWarm ?? true,
            ex => TADNLog.Log($"Volume flyout keep-warm: {ex.Message}"));

    private TrayAppDotNETWarmWindowSlot<VolumeTrayMenuWindow> TrayMenuWarmSlot =>
        _trayMenuWarmSlot ??= new TrayAppDotNETWarmWindowSlot<VolumeTrayMenuWindow>(
            () => _settings?.KeepTrayContextMenuWarm ?? true,
            ex => TADNLog.Log($"Volume tray menu keep-warm: {ex.Message}"));

    private void ScheduleKeepWarmPriming()
    {
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                if (_settings?.KeepFlyoutWarm == true && _audioManager != null)
                    await VolumeFlyoutWarmSlot.PrimeAsync(CreateManagedVolumeFlyout);
                if (_settings?.KeepTrayContextMenuWarm == true && _trayIcon != null)
                    await TrayMenuWarmSlot.PrimeAsync(CreateTrayMenuWindow);
            }
            catch (Exception ex)
            {
                TADNLog.Log($"VolumeAvaloniaApp.ScheduleKeepWarmPriming: {ex.Message}");
            }
        }, DispatcherPriority.ApplicationIdle);
    }

    private SettingsFlyoutKeepOpenCoordinator SettingsFlyoutKeepOpen =>
        _settingsFlyoutKeepOpen ??= new SettingsFlyoutKeepOpenCoordinator(
            () => _settingsWindow,
            () => _volumeFlyout,
            () => ShowVolumeFlyout(activate: false));

    private void OnVolumeFlyoutClosed(object? sender, EventArgs e)
    {
        if (_volumeFlyout == null) return;
        _volumeFlyout.Closed -= OnVolumeFlyoutClosed;
        _volumeFlyout = null;
    }

    private void OpenSettings()
    {
        if (_settings == null) return;

        if (_settingsWindow == null)
        {
            _settingsWindow = new VolumeSettingsWindow(_settings, ShowUninstallerWindow);
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

        _ = Task.Delay(TimeConstants.PostSettingsCloseGCDelayMs).ContinueWith(_ =>
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }, TaskScheduler.Default);
    }

    private void ShowUninstallerWindow(string installDir, VolumeInstallScope scope)
    {
        VolumeUninstallerWindow window = new(installDir, scope);
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

            if (_trackedDevice != null)
            {
                _trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;
                _trackedDevice = null;
            }

            if (_audioManager != null)
            {
                _audioManager.PropertyChanged -= OnAudioManagerPropertyChanged;
                Safe.Dispose(_audioManager);
                _audioManager = null;
            }

            AppServices.DeviceSettings = null;
            _deviceSettings = null;

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

            Safe.Dispose(_trayVolumeFeedback);
            _trayVolumeFeedback = null;

            Safe.Dispose(_volumeFlyoutWarmSlot);
            _volumeFlyoutWarmSlot = null;
            Safe.Dispose(_trayMenuWarmSlot);
            _trayMenuWarmSlot = null;

            if (_volumeFlyout != null)
            {
                _volumeFlyout.Closed -= OnVolumeFlyoutClosed;
                try { _volumeFlyout.Close(); }
                catch { }

                _volumeFlyout = null;
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

            _trayIconRenderQueue.Dispose();
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
            TADNLog.Log($"VolumeAvaloniaApp.ShutdownServices: {ex}");
        }
    }

    private void ExitApplication()
    {
        TADNLog.Log("VolumeAvaloniaApp.ExitApplication");
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
