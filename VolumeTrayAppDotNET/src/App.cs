#if HOTAVALONIA_ENABLE
using HotAvalonia;
#endif
#if DEBUG
using AppThemeHotReload = TrayAppDotNETCommon.Visuals.AppThemeHotReload;
using GlyphCatalogHotReload = TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload;
#endif
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.WarmWindows;
using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.UI.Flyout;
using VolumeTrayAppDotNET.UI.Settings;
using VolumeTrayAppDotNET.UI.Tray;
using VolumeHotkeyAction = TrayAppDotNETCommon.Models.HotkeyAction;
using VolumeHotkeyFiredEventArgs =
    TrayAppDotNETCommon.Services.HotkeyFiredEventArgs<TrayAppDotNETCommon.Models.HotkeyAction>;
using VolumeHotkeyService =
    TrayAppDotNETCommon.Services.GlobalHotkeyService<TrayAppDotNETCommon.Models.HotkeyAction,
        TrayAppDotNETCommon.Models.HotkeyBinding>;
using VolumeInstallScope = TrayAppDotNETCommon.Models.InstallScope;

namespace VolumeTrayAppDotNET;

internal static class VolumeAvaloniaRunner
{
    public static int Run(string[] args) =>
        TrayAppDotNETAppRunner.Run<VolumeAvaloniaApp>(args, AppSettings.GetDefaultPath, configure: Configure);

    private static AppBuilder Configure(AppBuilder builder)
    {
#if HOTAVALONIA_ENABLE
        builder = builder.UseHotReload();
#endif
        return builder;
    }
}

internal sealed class VolumeAvaloniaApp : Application
{
    private TrayAppDotNETAppHost<AppSettings, AppTheme>? _host;
    private AppTheme? _theme;
    private AppSettings? _settings;
    private DeviceSettings? _deviceSettings;
    private AudioDeviceManager? _audioManager;
    private AudioDevice? _trackedDevice;
    private readonly HashSet<AudioDevice> _trayMenuTrackedDevices = [];
    private TrayAppDotNETShellTrayIcon? _trayIcon;
    private VolumeTrayIcon? _trayIconRenderer;
    private TrayIconPresenter<TrayIconRenderInput>? _trayIconPresenter;
    private TrayMenuController? _trayMenu;
    private TrayWindowToggle? _trayToggle;
    private VolumeFlyoutWindow? _volumeFlyout;
    private VolumeSettingsWindow? _settingsWindow;
    private SettingsFlyoutKeepOpenCoordinator? _settingsFlyoutKeepOpen;
    private TrayAppDotNETWarmWindowSlot<VolumeFlyoutWindow>? _volumeFlyoutWarmSlot;
    private VolumeHotkeyService? _hotkeyService;
    private AppVolumeFeedbackPlayer? _trayVolumeFeedback;
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
        LoadDeviceSettings();
        StartServices();
        CreateTrayIcon();
        host.StartCommonServices(new TrayAppDotNETAppHostStartOptions
        {
            ShutdownApp = ShutdownServices,
            ShowUpdateNotification = (title, body) => _trayIcon?.ShowBalloon(title, body)
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

    private void LoadDeviceSettings()
    {
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
                ((INotifyCollectionChanged)_audioManager.Devices).CollectionChanged += OnDevicesCollectionChanged;
                SyncTrayMenuDeviceSubscriptions();
                AttachToTrackedDevice(_audioManager.DefaultDevice);
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"VolumeAvaloniaApp audio manager init failed: {ex}");
        }

        try
        {
            _trayIconRenderer = new VolumeTrayIcon(_theme) { IsLightTheme = ResolveEffectiveIsLightTheme() };
            ApplyTrayIconColorOverride();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"VolumeAvaloniaApp tray renderer init failed: {ex}");
        }

        _hotkeyService = _host?.StartHotkeyService(
            static prefix => new VolumeHotkeyService(prefix),
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
            IsScrollEnabled = _settings?.TrayScrollEnabled ?? true,
            IsPrecisionTouchpadScrollEnabled = _settings?.PrecisionTouchpadScrollEnabled ?? true,
            PrecisionTouchpadUnitsPerScrollStep = _settings?.PrecisionTouchpadUnitsPerScrollStep
                                                  ?? AppSettings.PrecisionTouchpadUnitsPerScrollStepDefault,
            IsVisible = true
        };
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.LeftDoubleClick += OnTrayLeftDoubleClick;
        _trayIcon.KeySelect += OnTrayKeySelect;
        _trayIcon.RightClick += OnTrayRightClick;
        _trayIcon.Scrolled += OnTrayScrolled;
        _trayIcon.PrecisionTouchpadScrolled += OnTrayPrecisionTouchpadScrolled;
        _trayIcon.RefreshNeeded += RequestTrayRefresh;
        _trayIcon.BalloonClicked += OnUpdateBalloonClicked;
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

    private void OnAudioManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioDeviceManager.DefaultDevice))
            AttachToTrackedDevice(_audioManager?.DefaultDevice);
        InvalidateTrayMenu();
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncTrayMenuDeviceSubscriptions();
        InvalidateTrayMenu();
    }

    private void SyncTrayMenuDeviceSubscriptions()
    {
        HashSet<AudioDevice> currentDevices = [];
        if (_audioManager != null)
        {
            foreach (AudioDevice device in _audioManager.Devices)
                currentDevices.Add(device);
        }

        foreach (AudioDevice trackedDevice in _trayMenuTrackedDevices.ToArray())
        {
            if (currentDevices.Contains(trackedDevice)) continue;
            trackedDevice.PropertyChanged -= OnTrayMenuDevicePropertyChanged;
            _trayMenuTrackedDevices.Remove(trackedDevice);
        }

        foreach (AudioDevice device in currentDevices)
        {
            if (!_trayMenuTrackedDevices.Add(device)) continue;
            device.PropertyChanged += OnTrayMenuDevicePropertyChanged;
        }
    }

    private void OnTrayMenuDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AudioDevice.FriendlyName)
            or nameof(AudioDevice.DeviceDescription)
            or nameof(AudioDevice.InterfaceFriendlyName)
            or nameof(AudioDevice.State)
            or nameof(AudioDevice.IsDefault)
            or nameof(AudioDevice.IsDefaultCommunications)
            or nameof(AudioDevice.IsBluetoothOrphaned))
            InvalidateTrayMenu();
    }

    private void ClearTrayMenuDeviceSubscriptions()
    {
        foreach (AudioDevice device in _trayMenuTrackedDevices)
            device.PropertyChanged -= OnTrayMenuDevicePropertyChanged;
        _trayMenuTrackedDevices.Clear();
    }

    /// <summary>
    /// Rebuilds the tray menu's device entries. Settings and theme changes reach the menu through its controller.
    /// </summary>
    private void InvalidateTrayMenu()
    {
        if (_shuttingDown) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(InvalidateTrayMenu, DispatcherPriority.Background);
            return;
        }

        _trayMenu?.Invalidate();
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
            ApplyTrayIconColorOverride();
            RequestTrayRefresh();
        });

    private void OnSettingsChanged()
    {
        if (_settings != null)
            AppIconResolver.TrimToLimit(_settings.IconLRULimit);

        Dispatcher.UIThread.Post(() =>
        {
            ApplyThemeVariant();
            ApplyTrayIconColorOverride();
            if (_settings != null) ApplyToolTipDelayToOpenWindows();

            ApplyTrayIconScrollSettings();
            if (_hotkeyService != null && _settings != null)
                _hotkeyService.Apply(_settings.Hotkeys);
            ApplyKeepWarmPolicies();
            RequestTrayRefresh();
        });
    }

    private void ApplyTrayIconScrollSettings()
    {
        if (_trayIcon == null) return;

        _trayIcon.IsScrollEnabled = _settings?.TrayScrollEnabled ?? true;
        _trayIcon.IsPrecisionTouchpadScrollEnabled = _settings?.PrecisionTouchpadScrollEnabled ?? true;
        _trayIcon.PrecisionTouchpadUnitsPerScrollStep = _settings?.PrecisionTouchpadUnitsPerScrollStep
                                                        ?? AppSettings.PrecisionTouchpadUnitsPerScrollStepDefault;
    }

    private void ApplyKeepWarmPolicies()
    {
        if (_volumeFlyoutWarmSlot != null || _settings?.KeepFlyoutWarm == true)
            VolumeFlyoutWarmSlot.ApplyKeepWarmPolicy(CreateManagedVolumeFlyout);
        _trayMenu?.ApplyKeepWarmPolicy();
    }

    private void ApplyToolTipDelayToOpenWindows()
    {
        if (_settingsWindow != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_settingsWindow);
        if (_volumeFlyout != null) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(_volumeFlyout);
        if (_trayMenu?.Current is { } trayMenu) TrayAppDotNETToolTip.ApplyShowDelayToSubtree(trayMenu);
    }

    private void OnUpdateBalloonClicked()
    {
        if (_host?.UpdateService?.AvailableUpdate == null) return;

        ShowVolumeFlyout();
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

    private void RequestTrayRefresh()
    {
        if (_trayIconPresenter == null) return;

        AudioDevice? device = _trackedDevice;
        if (device == null)
        {
            _audioManager?.RequestMissingDefaultRecovery("tray-null-default");
            if (_trayIconRenderer != null)
            {
                _trayIconRenderer.Volume = 0;
                _trayIconRenderer.IsMuted = true;
            }

            _trayIconPresenter.Refresh(L(nameof(AppStrings.TrayTooltip_NoAudioDevice)));
            return;
        }

        if (_trayIconRenderer != null)
        {
            _trayIconRenderer.Volume = device.Volume;
            _trayIconRenderer.IsMuted = device.IsMuted;
        }

        int percent = (int)Math.Round(device.Volume * 100);
        _trayIconPresenter.Refresh(device.IsMuted
            ? string.Format(L(nameof(AppStrings.TrayTooltip_Muted_Format)), device.FriendlyName)
            : string.Format(L(nameof(AppStrings.TrayTooltip_Volume_Format)), device.FriendlyName, percent));
    }

    private void OnTrayLeftClick()
    {
        if (TryDispatchTrayAction(TrayClickModifiers.Resolve(
                _settings?.TrayCtrlLeftClickAction ?? TrayClickAction.Nothing,
                _settings?.TrayAltLeftClickAction ?? TrayClickAction.Nothing,
                TrayClickAction.Nothing,
                TrayClickAction.Nothing)))
            return;

        switch (_trayToggle?.Decide(_volumeFlyout) ?? TrayToggleAction.Show)
        {
            case TrayToggleAction.Hide:
                _volumeFlyout?.Hide();
                break;
            // A floating flyout behind another window comes forward where it is instead of docking again
            case TrayToggleAction.Activate:
                _volumeFlyout?.Activate();
                break;
            case TrayToggleAction.Show:
                ShowVolumeFlyout();
                break;
        }
    }

    private void OnTrayLeftDoubleClick()
    {
        TrayClickAction action = TrayClickModifiers.Resolve(
            _settings?.TrayCtrlDoubleLeftClickAction ?? TrayClickAction.Nothing,
            _settings?.TrayAltDoubleLeftClickAction ?? TrayClickAction.Nothing,
            _settings?.TrayDoubleClickAction ?? TrayClickAction.Nothing,
            TrayClickAction.Nothing);
        if (!TryDispatchTrayAction(action))
            ShowVolumeFlyout();
    }

    // The classic notification area may report one Enter twice, so the keyboard only shows the flyout
    private void OnTrayKeySelect() => ShowVolumeFlyout();

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

    private void OnTrayScrolled(int notches)
    {
        if (_settings == null) return;

        TrayWheelVolumeStep action = TrayClickModifiers.Resolve(
            _settings.TrayCtrlWheelAction,
            _settings.TrayAltWheelAction,
            _settings.TrayWheelAction,
            TrayWheelVolumeStep.Nothing);
        int stepPercent = ResolveTrayWheelStepPercent(_settings, action);
        if (stepPercent == 0) return;

        AdjustTrackedDeviceVolume(notches, stepPercent);
    }

    private void OnTrayPrecisionTouchpadScrolled(int steps) =>
        AdjustTrackedDeviceVolume(steps, stepPercent: 1);

    private static int ResolveTrayWheelStepPercent(AppSettings settings, TrayWheelVolumeStep action) =>
        action switch
        {
            TrayWheelVolumeStep.Default => settings.WheelVolumeStepPercent,
            TrayWheelVolumeStep.Fine => settings.WheelVolumeStepFinePercent,
            TrayWheelVolumeStep.Coarse => settings.WheelVolumeStepCoarsePercent,
            _ => 0
        };

    private void AdjustTrackedDeviceVolume(int steps, int stepPercent)
    {
        AudioDevice? device = _trackedDevice;
        if (device == null || steps == 0) return;

        double currentPercent = device.Volume * 100.0;
        double next = Math.Clamp(currentPercent + steps * stepPercent, min: 0, max: 100);
        if (Math.Abs(currentPercent - next) >= 0.001)
        {
            device.Volume = (float)(next / 100.0);
            if (_settings?.PlayTrayScrollVolumeChangeSound == true)
            {
                (_trayVolumeFeedback ??= new AppVolumeFeedbackPlayer(Dispatcher.UIThread, _settings))
                    .PlayForDevice(device);
            }
        }

        _trayIcon?.ShowTooltip();
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
            throw new InvalidOperationException("Volume tray menu requires settings.");

        AudioDevice[] devices = _audioManager?.Devices.ToArray() ?? [];
        return new TrayMenuWindow(
            VolumeTrayMenuEntries.Build(devices, _settings),
            TrayMenuWindowOptions.Create(
                _settings,
                _theme,
                UI.ControlMap.TrayMenu.ID,
                TrayMenuStandardEntry.Settings(OpenSettings, UI.ControlMap.TrayMenu.OpenSettings),
                TrayMenuStandardEntry.Exit(ExitApplication, UI.ControlMap.TrayMenu.Exit)) with
            {
                // The device list can be long; the standard entries sit right above the taskbar
                ScrollToBottom = true
            });
    }

    private void ShowVolumeFlyout(bool activate = true, bool holdOpenForSettings = true)
    {
        if (_audioManager == null || _settings == null || _trayIcon == null) return;

        _volumeFlyout ??= VolumeFlyoutWarmSlot.TakeOrCreate(CreateManagedVolumeFlyout);

        _volumeFlyout.Redock();
        _volumeFlyout.ShowAt(_trayIcon, activate);
        if (holdOpenForSettings && _settingsWindow is { IsVisible: true })
            SettingsFlyoutKeepOpen.HoldOpen();
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

    private void RestoreStartupUndockedFlyoutIfRequested()
    {
        if (_audioManager == null || _settings == null || _trayIcon == null) return;
        if (!FlyoutDockingController.ShouldRestoreOnStartup(_settings)) return;

        _volumeFlyout ??= VolumeFlyoutWarmSlot.TakeOrCreate(CreateManagedVolumeFlyout);
        _volumeFlyout.ShowAt(_trayIcon);
    }

    private TrayAppDotNETWarmWindowSlot<VolumeFlyoutWindow> VolumeFlyoutWarmSlot =>
        _volumeFlyoutWarmSlot ??= new TrayAppDotNETWarmWindowSlot<VolumeFlyoutWindow>(
            () => _settings?.KeepFlyoutWarm ?? true,
            ex => TADNLog.Log($"Volume flyout keep-warm: {ex.Message}"));

    private void ScheduleKeepWarmPriming()
    {
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                if (_settings?.KeepFlyoutWarm == true && _audioManager != null)
                    await VolumeFlyoutWarmSlot.PrimeAsync(CreateManagedVolumeFlyout);
                if (_trayMenu != null)
                    await _trayMenu.PrimeAsync();
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
            () => ShowVolumeFlyout(activate: false, holdOpenForSettings: false));

    private void OnVolumeFlyoutClosed(object? sender, EventArgs e)
    {
        if (sender is VolumeFlyoutWindow flyout) flyout.Closed -= OnVolumeFlyoutClosed;
        if (ReferenceEquals(_volumeFlyout, sender)) _volumeFlyout = null;
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
    }

    private Task<bool> ShowUninstallerWindow(
        VolumeInstallScope scope,
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
            _hotkeyService = null;
            AppServices.HotkeyService = null;
            AppServices.UpdateCheckService = null;

            if (_settings != null) _settings.Changed -= OnSettingsChanged;

            AppServices.Settings = null;

            if (_trackedDevice != null)
            {
                _trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;
                _trackedDevice = null;
            }

            if (_audioManager != null)
            {
                _audioManager.PropertyChanged -= OnAudioManagerPropertyChanged;
                ((INotifyCollectionChanged)_audioManager.Devices).CollectionChanged -= OnDevicesCollectionChanged;
                ClearTrayMenuDeviceSubscriptions();
                Safe.Dispose(_audioManager);
                _audioManager = null;
            }

            CommunicationsDucking.Stop();
            EqualizerAPOMonitor.Dispose();

            // Writes any queued per-device save before the reference goes away
            _deviceSettings?.Flush();
            AppServices.DeviceSettings = null;
            _deviceSettings = null;

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

            Safe.Dispose(_trayVolumeFeedback);
            _trayVolumeFeedback = null;

            Safe.Dispose(_volumeFlyoutWarmSlot);
            _volumeFlyoutWarmSlot = null;

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

            if (_settingsWindow != null)
            {
                _settingsWindow.Closed -= OnSettingsWindowClosed;
                try { _settingsWindow.Close(); }
                catch { }

                _settingsWindow = null;
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"VolumeAvaloniaApp.ShutdownServices: {ex}");
        }
        finally
        {
            try { AppIconResolver.Shutdown(); }
            catch (Exception exception)
            {
                TADNLog.Log($"VolumeAvaloniaApp icon cache shutdown failed: {exception}");
            }

            TADNLog.Flush();
        }
    }

    private void ExitApplication() => _host?.Exit();

    private static string L(string key) => LocalizationManager.Instance[key];
}
