using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.Interop.NightLight;
using BrightnessTrayAppDotNET.Utils;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Models;
using TrayAppDotNETCommon.UI.Tray;
using TrayAppDotNETCommon.UI.WarmWindows;
using BrightnessAppTheme = BrightnessTrayAppDotNET.Visuals.AppTheme;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;

namespace BrightnessTrayAppDotNET.UI.Flyout;

public sealed partial class BrightnessFlyoutWindow : FlyoutWindowCommon, INotifyPropertyChanged,
    ITrayAppDotNETWarmResourceOwner
{
    private static readonly FontFamily FlyoutFont = new("Segoe UI");

    private readonly BrightnessFlyoutSession _session;
    private readonly FlyoutWindowDragHelper _dragHelper = new();
    private readonly UIResourceScope _externalResources = new(nameof(BrightnessFlyoutWindow) + ".External");
    private readonly HashSet<MonitorInfo> _subscribedMonitors = [];
    private readonly HashSet<string> _curveStopwatchReengageBlockedByMaster = [];
    private readonly HashSet<MonitorInfo> _deferredManualCurveOverrideResync = [];
    private readonly AnimationGenerationTracker _previewSweepFrames = new();

    private Dictionary<MonitorInfo, ProfilePreviewRowVisuals> _profilePreviewRows = [];
    private FlyoutVisualState? _visualState;

    private TrayAppDotNETShellTrayIcon? _lastTrayIcon;
    private Border? _rootCard;
    private Border? _undockButton;
    private FlyoutUndockButtonController? _undockButtonController;
    private ScrollViewer? _scrollViewer;
    private Border? _confirmOverlay;
    private TextBlock? _confirmTitle;
    private TextBlock? _confirmMessage;
    private FlyoutAxamlProperties? _layout;
    private SettingsButton? _confirmOK;
    private SettingsButton? _confirmCancel;
    private DispatcherTimer? _previewSweepTimer;
    private Stopwatch? _previewSweepStopwatch;
    private DispatcherTimer? _curveStopwatchTimer;
    private bool _isUndocked;
    private bool _isDraggingWindow;
    private bool _suppressPropagation;
    private bool _masterSliderGesturePrepared;
    private bool _hasUnsavedChanges;
    private bool _isUpdateButtonVisible;
    private bool _isUpdateDownloadInFlight;
    private bool _isUpdateDialogOpen;
    private bool _deferredSliderGestureRebuild;
    private bool _isRebuildingVisual;
    private bool _rebuildVisualPending;
    private bool _rebuildVisualQueued;
    private bool _isClosed;
    private long _visibilityGeneration;
    private EnvironmentalCurve? _previewSweepCurveOverride;
    private EnvironmentalCurve? _previewSweepDisabledPeriodOverride;
    private EnvironmentalCurve? _previewDateCurveOverride;
    private EnvironmentalCurve? _previewDateDisabledPeriodOverride;
    private bool _previewDateHardwareActive;
    private bool _previewDateSuspendedCurveService;
    private bool _previewSweepSuspendedCurveService;
    private double _previewSweepStartFraction;
    private int _previewedProfileIndex = -1;

    private ProfileManager _profileManager => _session.ProfileManager;
    private BrightnessAppTheme _theme => _session.Theme;
    private AppSettings? _settings => _session.Settings;
    private MonitorService _monitorService => _session.MonitorService;
    private EnvironmentalCurveService _curveService => _session.CurveService;

    private bool IsWindowAlive => !_isClosed && !WindowResources.CancellationToken.IsCancellationRequested;

    private bool _isBrightnessCurveEnabled
    {
        get => _session.IsBrightnessCurveEnabled;
        set => _session.IsBrightnessCurveEnabled = value;
    }

    private bool _isNightLightCurveEnabled
    {
        get => _session.IsNightLightCurveEnabled;
        set => _session.IsNightLightCurveEnabled = value;
    }

    private bool _isInCurveDisabledPeriod
    {
        get => _session.IsInDisabledPeriod;
        set => _session.IsInDisabledPeriod = value;
    }

    private bool _isNightLightActive
    {
        get => _session.IsNightLightActive;
        set => _session.IsNightLightActive = value;
    }

    private bool _awaitingInitialAsyncMonitorEnrollment
    {
        get => _session.AwaitingInitialAsyncMonitorEnrollment;
        set => _session.AwaitingInitialAsyncMonitorEnrollment = value;
    }

    public BrightnessFlyoutWindow()
        : this(
            ResolveProfileManager(),
            AppServices.Theme ?? BrightnessAppTheme.Default,
            AppServices.MonitorService ?? throw new InvalidOperationException("MonitorService is required."))
    {
    }

    private static ProfileManager ResolveProfileManager()
    {
        ProfileManager? profileManager = AppServices.ProfileManager;
        if (profileManager != null) return profileManager;

        profileManager = new ProfileManager();
        AppServices.ProfileManager = profileManager;
        return profileManager;
    }

    internal BrightnessFlyoutWindow(ProfileManager profileManager, BrightnessAppTheme theme,
        MonitorService monitorService)
    {
        WindowResources.Own(_externalResources);
        _externalResources.Add(UnsubscribeAllMonitors);
        _session = new BrightnessFlyoutSession(
            profileManager,
            theme,
            monitorService,
            AppServices.Settings,
            L("Flyout_MasterRowName", "All displays"),
            L("Flyout_NightLightRowName", "Night light"),
            inDisabled => IsInCurveDisabledPeriod = inDisabled,
            AutoEngageBrightnessCurveManualOverride);

        try
        {
            InitializeComponent();
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

            _profileManager.EnsureProfileCount(Math.Max(1, _theme.ProfileButtons.ButtonCount));
            RestoreInitialProfileState();
            if (Monitors.Count > 0) MasterMonitor.Brightness = ComputeMasterFromEnabledIndividuals();
            CaptureOffsetsFromMaster();

            MasterMonitor.PropertyChanged += OnMonitorPropertyChanged;
            _externalResources.Add(() => MasterMonitor.PropertyChanged -= OnMonitorPropertyChanged);
            foreach (MonitorInfo monitor in Monitors)
                SubscribeMonitor(monitor);
            NightLightMonitor.PropertyChanged += OnNightLightPropertyChanged;
            _externalResources.Add(() => NightLightMonitor.PropertyChanged -= OnNightLightPropertyChanged);
            Monitors.CollectionChanged += OnMonitorsCollectionChanged;
            _externalResources.Add(() => Monitors.CollectionChanged -= OnMonitorsCollectionChanged);
            _monitorService.MonitorsRefreshed += OnInitialMonitorEnrollmentRefreshed;
            _externalResources.Add(() => _monitorService.MonitorsRefreshed -= OnInitialMonitorEnrollmentRefreshed);
            _profileManager.SelectedProfileChanged += OnSelectedProfileChanged;
            _externalResources.Add(() => _profileManager.SelectedProfileChanged -= OnSelectedProfileChanged);
            _profileManager.UnsavedChangesStatusChanged += UpdateSaveButtonState;
            _externalResources.Add(() => _profileManager.UnsavedChangesStatusChanged -= UpdateSaveButtonState);
            _profileManager.ProfilesListChanged += OnProfilesListChanged;
            _externalResources.Add(() => _profileManager.ProfilesListChanged -= OnProfilesListChanged);
            if (_settings != null)
            {
                _settings.Changed += OnSettingsChanged;
                _externalResources.Add(() => _settings.Changed -= OnSettingsChanged);
            }

            UpdateCheckService? updateCheckService = AppServices.UpdateCheckService;
            if (updateCheckService != null)
            {
                updateCheckService.StateChanged += NotifyUpdateStateChanged;
                _externalResources.Add(() => updateCheckService.StateChanged -= NotifyUpdateStateChanged);
            }

            BuildProfileButtonItems();

            _isUndocked = _settings is
            {
                FlyoutUndocked: true,
                FlyoutHasSavedPosition: true,
                AllowFlyoutUndock: true,
                RestoreFlyoutUndockedOnStartup: true
            };

            CheckAndUpdateUnsavedChanges();
            if (_isBrightnessCurveEnabled || _isNightLightCurveEnabled) OnCurveToggleStateChanged();
            RestoreCurveStopwatchesFromSettings();

            KeyDown += OnWindowKeyDown;
            WindowResources.Add(() => KeyDown -= OnWindowKeyDown);
            InitializeComponentState();
            NotifyUpdateStateChanged();
        }
        catch
        {
            UIContentGeneration? failedGeneration = ActiveContentGeneration;
            RunCloseCleanup(nameof(DisposeContentGeneration), () =>
            {
                try { DisposeContentGeneration(); }
                finally { failedGeneration?.Dispose(); }
            });
            RunCloseCleanup(nameof(UIResourceScope.Dispose), _externalResources.Dispose);
            RunCloseCleanup("WindowResources.Dispose", WindowResources.Dispose);
            RunCloseCleanup(nameof(BrightnessFlyoutSession.Dispose), _session.Dispose);
            throw;
        }
    }

    private void InitializeComponentState()
    {
        _layout = AxamlFlyout;

        RebuildVisual();
    }

    private FlyoutAxamlProperties Layout =>
        _layout ?? throw new InvalidOperationException("Brightness flyout layout resources have not been loaded.");

    private int EdgePadding => (int)Math.Round(Layout.EdgePadding);

    private int PixelMinSize => (int)Math.Round(Layout.PixelMinSize);

    public new event PropertyChangedEventHandler? PropertyChanged;
    public event Action? BrightnessUpdated;
    public event Action? SettingsRequested;
    public event Action? FlyoutDeactivated;
    public event Action<bool>? PreviewSweepStateChanged;
    public event Action<double>? PreviewSweepProgress;

    void ITrayAppDotNETWarmResourceOwner.TrimHiddenWarmResources() => TrimHiddenWarmResources();

    void ITrayAppDotNETWarmResourceOwner.DisposeWarmResources() => DisposeWarmResources();

    public ObservableCollection<MonitorInfo> Monitors => _session.Monitors;
    public ObservableCollection<MonitorInfo> AllItems => _session.AllItems;
    public ObservableCollection<ProfileButtonItem> ProfileButtons { get; } = [];
    public MonitorInfo MasterMonitor => _session.MasterMonitor;
    public MonitorInfo NightLightMonitor => _session.NightLightMonitor;

    public bool BrightnessChanged { get; private set; }
    public bool IsUndocked => _isUndocked;
    public int SelectedProfileIndex => _profileManager.SelectedIndex;
    public bool HasUnsavedChanges => _hasUnsavedChanges;
    public bool IsNightLightActive => _isNightLightActive;
    public bool IsCurveAbsoluteMode => _settings?.EnvironmentalOffsetMode != true;
    public bool IsUpdateButtonVisible => _isUpdateButtonVisible;

    public bool IsInCurveDisabledPeriod
    {
        get => _isInCurveDisabledPeriod;
        private set
        {
            if (_isInCurveDisabledPeriod == value) return;
            _isInCurveDisabledPeriod = value;
            OnPropertyChanged();
            RebuildVisual();
        }
    }

    public bool IsBrightnessCurveEnabled
    {
        get => _isBrightnessCurveEnabled;
        private set
        {
            if (_isBrightnessCurveEnabled == value) return;
            bool wasOn = _isBrightnessCurveEnabled;
            _isBrightnessCurveEnabled = value;
            if (_settings != null)
            {
                _settings.EnvironmentalBrightnessCurveEnabled = value;
                _settings.Save();
            }

            OnPropertyChanged();
            OnCurveToggleStateChanged();
            if (wasOn && !value) ResyncBrightnessHardwareToSliders();
            RebuildVisual();
        }
    }

    public bool IsNightLightCurveEnabled
    {
        get => _isNightLightCurveEnabled;
        private set
        {
            if (_isNightLightCurveEnabled == value) return;
            bool wasOn = _isNightLightCurveEnabled;
            _isNightLightCurveEnabled = value;
            if (_settings != null)
            {
                _settings.EnvironmentalNightLightCurveEnabled = value;
                _settings.Save();
            }

            OnPropertyChanged();
            OnCurveToggleStateChanged();
            if (wasOn && !value) ResyncNightLightHardwareToSlider();
            RebuildVisual();
        }
    }

    protected override bool HasOpenChildWindow => _isUpdateDialogOpen;

    protected override bool ShouldAutoHideWhenDeactivated => !_isUndocked;

    protected override void HideFlyout()
    {
        ClearProfilePreview();
        Hide();
        FlyoutDeactivated?.Invoke();
    }

    public void ShowAt(TrayAppDotNETShellTrayIcon trayIcon, bool activate = true)
    {
        if (!IsWindowAlive) return;
        _lastTrayIcon = trayIcon;
        ShowActivated = activate;
        ApplyWorkAreaMaxHeight();
        RebuildVisual();
        if (!IsVisible)
        {
            Opacity = 0;
            Position = OffscreenPosition();
            base.Show();
        }

        long visibilityGeneration = ++_visibilityGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsWindowAlive || visibilityGeneration != _visibilityGeneration || !IsVisible) return;
            AppServices.DisplayEventManager?.RunSingleGatedScan();
            UpdateLayout();
            ApplyWorkAreaMaxHeight();
            PositionNearTray();
            Opacity = 1;
            if (activate) Activate();
        }, DispatcherPriority.Loaded);
    }

    public new void Show()
    {
        if (!IsWindowAlive) return;
        if (ActiveContentGeneration == null) RebuildVisual();
        base.Show();
        AppServices.DisplayEventManager?.RunSingleGatedScan();
        long visibilityGeneration = ++_visibilityGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsWindowAlive || visibilityGeneration != _visibilityGeneration || !IsVisible) return;
            UpdateLayout();
            ApplyWorkAreaMaxHeight();
            PositionNearTray();
            Activate();
        }, DispatcherPriority.Loaded);
    }

    public void ShowWithoutActivating()
    {
        if (!IsWindowAlive) return;
        if (ActiveContentGeneration == null) RebuildVisual();
        ShowActivated = false;
        try
        {
            if (!IsVisible) base.Show();
        }
        finally
        {
            ShowActivated = true;
        }

        AppServices.DisplayEventManager?.RunSingleGatedScan();
        long visibilityGeneration = ++_visibilityGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsWindowAlive || visibilityGeneration != _visibilityGeneration || !IsVisible) return;
            UpdateLayout();
            ApplyWorkAreaMaxHeight();
            PositionNearTray();
        }, DispatcherPriority.Loaded);
    }

    public bool HasFocus() => IsActive;

    public new void Hide()
    {
        _visibilityGeneration++;
        CancelPreviewSweep();
        CancelConfirmOverlay();
        base.Hide();
        TrimHiddenWarmResources();
        NotifyWarmDismissed();
    }

    public override void DismissForWarmCache() => Hide();

    private static void TrimHiddenWarmResources()
    {
        // Keep exactly one current generation warm. Hidden structural changes retire it in QueueRebuildVisual.
    }

    private void DisposeWarmResources() => DisposeContentGeneration();

    public void Redock()
    {
        if (!_isUndocked) return;
        _isUndocked = false;
        if (_settings != null)
        {
            _settings.FlyoutUndocked = false;
            _settings.Save();
        }

        UpdateUndockButtonVisual();
        QueuePositionNearTray();
        OnPropertyChanged(nameof(IsUndocked));
    }

    public void PositionNearTray() => Position = ResolvePosition(_lastTrayIcon);

    private PixelPoint OffscreenPosition() => new(Layout.OffscreenPosition, Layout.OffscreenPosition);

    private PixelRect FallbackWorkArea() => new(
        Layout.FallbackWorkAreaX,
        Layout.FallbackWorkAreaY,
        Layout.FallbackWorkAreaWidth,
        Layout.FallbackWorkAreaHeight);

    public void NotifyUpdateStateChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (!IsWindowAlive) return;
        bool toggleOn = _settings?.ShowUpdateButtonInFlyout ?? true;
        bool available = AppServices.UpdateCheckService?.AvailableUpdate != null;
        _isUpdateButtonVisible = toggleOn && available;
        OnPropertyChanged(nameof(IsUpdateButtonVisible));
        QueueRebuildVisual();
    });

    public void RequestUpdatePrompt()
    {
        if (AppServices.UpdateCheckService?.AvailableUpdate == null) return;
        ShowUpdateConfirmation();
    }

    public void SelectProfileByIndex(int index) => SelectProfileApplyingMode(index);

    public void ToggleNightLight() => ToggleNightLightState();

    /// <summary>
    /// Synchronizes flyout state after the provider confirms a night-light enabled-state transition.
    /// </summary>
    internal void NotifyNightLightEnabledStateChanged()
    {
        if (!IsWindowAlive) return;

        bool isNightLightActive = NightLightProvider.IsSupported() && NightLightProvider.IsEnabled();
        if (_isNightLightActive == isNightLightActive) return;

        _isNightLightActive = isNightLightActive;
        OnPropertyChanged(nameof(IsNightLightActive));
        RebuildVisual();
    }

    internal void SyncAllIndividualsToMaster()
    {
        double target = Math.Round(MasterMonitor.Brightness);
        _suppressPropagation = true;
        try
        {
            foreach (MonitorInfo monitor in Monitors)
            {
                if (!monitor.IsParticipatingInMaster) continue;
                monitor.Brightness = target;
            }
        }
        finally
        {
            _suppressPropagation = false;
        }

        CaptureOffsetsFromMaster();
        ApplyBrightnessCurveImmediatelyIfActive();
        CheckAndUpdateUnsavedChanges();
        BrightnessUpdated?.Invoke();
        RebuildVisual();
    }

    internal void SyncAllToHighestIndividual()
    {
        double target = 0;
        bool any = false;
        foreach (MonitorInfo monitor in Monitors)
        {
            if (!monitor.IsParticipatingInMaster) continue;
            any = true;
            if (monitor.Brightness > target) target = monitor.Brightness;
        }

        if (!any) return;

        target = Math.Round(target);
        _suppressPropagation = true;
        try
        {
            MasterMonitor.Brightness = target;
            foreach (MonitorInfo monitor in Monitors)
            {
                if (!monitor.IsParticipatingInMaster) continue;
                monitor.Brightness = target;
            }
        }
        finally
        {
            _suppressPropagation = false;
        }

        CaptureOffsetsFromMaster();
        ApplyBrightnessCurveImmediatelyIfActive();
        CheckAndUpdateUnsavedChanges();
        BrightnessUpdated?.Invoke();
        RebuildVisual();
    }

    public void NotifyUserBrightnessAdjustment(bool replayCurrentSliderValue = true) =>
        DisengageCurveForUserAdjustment(MasterMonitor, replayCurrentSliderValue);

    public void NotifyUserNightLightAdjustment(bool replayCurrentSliderValue = true) =>
        DisengageCurveForUserAdjustment(NightLightMonitor, replayCurrentSliderValue);

    public void CompleteUserBrightnessAdjustment() => FlushDeferredManualCurveOverrideResync(MasterMonitor);

    public void CompleteUserNightLightAdjustment() => FlushDeferredManualCurveOverrideResync(NightLightMonitor);

    /// <summary>
    /// Applies a user-requested night-light slider delta through the same path as direct slider input.
    /// </summary>
    public void AdjustNightLightBrightness(int delta)
    {
        if (!NightLightProvider.IsSupported()) return;

        NotifyUserNightLightAdjustment(replayCurrentSliderValue: false);
        try
        {
            ApplyNightLightSliderValue(null, NightLightMonitor.Brightness + delta);
        }
        finally
        {
            CompleteUserNightLightAdjustment();
        }
    }

    public void RequestCurveReevaluation() => _curveService.RequestEvaluation();

    /// <summary>
    /// Applies a date-preview curve at the current time until cleared.
    /// </summary>
    public void ApplyPreviewDateCurve(EnvironmentalCurve previewCurve, EnvironmentalCurve disabledPeriodCurve)
    {
        if (!IsBrightnessCurveEnabled && !IsNightLightCurveEnabled) return;

        _previewDateCurveOverride = previewCurve;
        _previewDateDisabledPeriodOverride = disabledPeriodCurve;
        _previewDateHardwareActive = true;
        if (!_previewDateSuspendedCurveService)
        {
            _curveService.Suspend();
            _previewDateSuspendedCurveService = true;
        }

        if (_previewSweepTimer != null)
        {
            _previewSweepCurveOverride = previewCurve;
            _previewSweepDisabledPeriodOverride = disabledPeriodCurve;
        }
        else
            ApplyPreviewDateCurveAtCurrentTime();
    }

    /// <summary>
    /// Clears any active date-preview curve and reapplies the live environmental curves.
    /// </summary>
    public void ClearPreviewDateCurve()
    {
        if (!_previewDateHardwareActive && !_previewDateSuspendedCurveService) return;

        if (_previewSweepTimer != null)
            CancelPreviewSweep();

        _previewDateCurveOverride = null;
        _previewDateDisabledPeriodOverride = null;
        _previewDateHardwareActive = false;
        if (_previewDateSuspendedCurveService)
        {
            _previewDateSuspendedCurveService = false;
            _curveService.Resume();
        }
        else
            _curveService.RequestEvaluation();
    }

    public void TogglePreviewSweep()
        => TogglePreviewSweep(previewCurve: null, disabledPeriodCurve: null);

    /// <summary>
    /// Toggles the 24-hour preview sweep, optionally sampling a caller-supplied preview curve.
    /// </summary>
    public void TogglePreviewSweep(EnvironmentalCurve? previewCurve, EnvironmentalCurve? disabledPeriodCurve)
    {
        if (_previewSweepTimer != null)
        {
            CancelPreviewSweep();
            return;
        }

        if (!IsBrightnessCurveEnabled && !IsNightLightCurveEnabled) return;
        RunPreviewSweep(previewCurve, disabledPeriodCurve);
    }

    public void CancelPreviewSweep()
    {
        if (_previewSweepTimer == null) return;
        FinishPreviewSweep();
    }

    private void RestoreInitialProfileState()
    {
        bool applyOnStartup = _settings?.ApplyBrightnessOnStartup == true;
        bool applyProfileBrightness = applyOnStartup && !_isBrightnessCurveEnabled;
        bool applyProfileNightLight = applyOnStartup && !_isNightLightCurveEnabled;
        SyncSettingsToSelectedProfileMode();
        _profileManager.ApplyCurrentProfile(Monitors, applyProfileBrightness);

        if (applyProfileNightLight
            && _profileManager.SelectedIndex >= 0
            && _profileManager.SelectedIndex < _profileManager.Profiles.Profiles.Count)
        {
            int strength = _profileManager.Profiles.Profiles[_profileManager.SelectedIndex].NightLight;
            NightLightMonitor.Brightness = FlipIfNightLightInverted(strength);
            if (NightLightProvider.IsSupported()) NightLightProvider.SetStrength(strength);
        }

        if (!_isBrightnessCurveEnabled && !_isNightLightCurveEnabled) return;

        using (_monitorService.SuspendHardwareWrites())
        {
            if (_isBrightnessCurveEnabled)
                _profileManager.ApplyCurrentProfile(Monitors, includeBrightness: true);

            if (_isNightLightCurveEnabled
                && _profileManager.SelectedIndex >= 0
                && _profileManager.SelectedIndex < _profileManager.Profiles.Profiles.Count)
            {
                int strength = _profileManager.Profiles.Profiles[_profileManager.SelectedIndex].NightLight;
                NightLightMonitor.Brightness = FlipIfNightLightInverted(strength);
            }
        }
    }

    /// <summary>Rebuilds the flyout and logs failures before they can escape the dispatcher.</summary>
    private void RebuildVisual()
    {
        if (_isClosed) return;
        if (_isDraggingWindow || IsAnySliderGestureActive())
        {
            _rebuildVisualPending = true;
            if (IsAnySliderGestureActive()) _deferredSliderGestureRebuild = true;
            return;
        }

        try { RebuildVisualCore(); }
        catch (Exception ex)
        {
            _rebuildVisualPending = false;
            _rebuildVisualQueued = false;
            WPFLog.Log($"BrightnessFlyoutWindow.RebuildVisual: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Builds the visual tree before replacing the existing content.</summary>
    private void RebuildVisualCore()
    {
        if (_layout == null || _isClosed) return;
        if (_isRebuildingVisual)
        {
            _rebuildVisualPending = true;
            return;
        }

        _isRebuildingVisual = true;
        _rebuildVisualPending = false;
        _rebuildVisualQueued = false;

        UIResourceScope candidateResources = new(
            nameof(BrightnessFlyoutWindow) + ".Content",
            exception => WPFLog.Log(
                $"BrightnessFlyoutWindow content cleanup failed: {exception.GetType().Name}: {exception.Message}"));
        FlyoutVisualState candidate = new();
        candidateResources.Add(() => ReleaseVisualState(candidate));

        try
        {
            bool isLight = BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings);
            SettingsPalette settingsPalette = CreateSettingsPalette(_theme, _settings, isLight);
            FlyoutControlPalette palette = CreateFlyoutPalette(_theme, _settings, settingsPalette, isLight);
            bool rounded = _settings?.EnableRoundedCorners ?? true;

            Grid rootGrid = new();
            rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            ScrollViewer rows = BuildRows(palette, candidate, candidateResources);
            Grid.SetRow(rows, 0);
            rootGrid.Children.Add(rows);

            Border footer = BuildFooter(palette, rounded, candidateResources);
            Grid.SetRow(footer, 1);
            rootGrid.Children.Add(footer);

            AddFloatingButtons(rootGrid, palette, candidate, candidateResources);

            candidate.ConfirmOverlay = BuildConfirmOverlay(settingsPalette, rounded, candidate);
            candidate.ConfirmOverlay.IsVisible = false;
            Grid.SetRowSpan(candidate.ConfirmOverlay, 2);
            rootGrid.Children.Add(candidate.ConfirmOverlay);

            candidate.RootCard = new Border
            {
                Background = TrayAppDotNETFlyoutUI.Brush(_theme.ResolveBackground(_settings, isLight)),
                BorderBrush = TrayAppDotNETFlyoutUI.Brush(_theme.Border.For(isLight)),
                BorderThickness = Layout.RootBorderThickness,
                CornerRadius = Rounded(Layout.RootCornerRadius),
                ClipToBounds = false,
                BoxShadow = new BoxShadows(new BoxShadow
                {
                    OffsetY = Layout.RootShadowOffsetY,
                    Blur = Layout.RootShadowBlur,
                    Color = _theme.FlyoutShadow.For(isLight)
                }),
                Child = new Border
                {
                    Background = TrayAppDotNETFlyoutUI.Brush(_theme.ResolveBackground(_settings, isLight)),
                    CornerRadius = Rounded(Layout.RootInnerCornerRadius),
                    ClipToBounds = true,
                    Padding = Layout.RootInnerPadding,
                    Child = rootGrid
                }
            };
            candidate.RootCard.PointerPressed += OnRootPointerPressed;
            candidateResources.Add(() => candidate.RootCard.PointerPressed -= OnRootPointerPressed);
            candidate.RootCard.PointerMoved += OnRootPointerMoved;
            candidateResources.Add(() => candidate.RootCard.PointerMoved -= OnRootPointerMoved);
            candidate.RootCard.PointerReleased += OnRootPointerReleased;
            candidateResources.Add(() => candidate.RootCard.PointerReleased -= OnRootPointerReleased);
            candidate.RootCard.PointerCaptureLost += OnRootPointerCaptureLost;
            candidateResources.Add(() => candidate.RootCard.PointerCaptureLost -= OnRootPointerCaptureLost);

            UIContentGeneration replacement = new(
                nameof(BrightnessFlyoutWindow),
                candidate.RootCard,
                candidateResources,
                logError: exception => WPFLog.Log(
                    $"BrightnessFlyoutWindow root release failed: {exception.GetType().Name}: {exception.Message}"));
            PublishAndCommitVisualState(candidate, replacement);
        }
        catch
        {
            candidateResources.Dispose();
            throw;
        }
        finally
        {
            _isRebuildingVisual = false;
        }

        FlushPendingRebuildVisual();
    }

    private void PublishAndCommitVisualState(FlyoutVisualState candidate, UIContentGeneration replacement)
    {
        FlyoutVisualState? previous = _visualState;
        try
        {
            ApplyVisualState(candidate);
            CommitContentGeneration(replacement);
        }
        catch
        {
            if (!ReferenceEquals(ActiveContentGeneration, replacement))
            {
                replacement.Dispose();
                ApplyVisualState(previous);
            }

            throw;
        }
    }

    private void ApplyVisualState(FlyoutVisualState? candidate)
    {
        _profilePreviewRows = candidate?.ProfilePreviewRows ?? [];
        _rootCard = candidate?.RootCard;
        _undockButton = candidate?.UndockButton;
        _undockButtonController = candidate?.UndockButtonController;
        _scrollViewer = candidate?.ScrollViewer;
        _confirmOverlay = candidate?.ConfirmOverlay;
        _confirmTitle = candidate?.ConfirmTitle;
        _confirmMessage = candidate?.ConfirmMessage;
        _confirmOK = candidate?.ConfirmOK;
        _confirmCancel = candidate?.ConfirmCancel;
        _visualState = candidate;
    }

    private void ReleaseVisualState(FlyoutVisualState candidate)
    {
        IPointer? capturedPointer = candidate.RootCapturedPointer;
        candidate.RootCapturedPointer = null;
        if (capturedPointer != null)
        {
            try { capturedPointer.Capture(null); }
            catch (Exception exception)
            {
                WPFLog.Log($"BrightnessFlyoutWindow root pointer release failed: {exception.Message}");
            }
        }

        SettingsButton? confirmOK = candidate.ConfirmOK;
        if (confirmOK != null)
        {
            confirmOK.Click -= OnConfirmOKClicked;
            confirmOK.Tag = null;
        }

        SettingsButton? confirmCancel = candidate.ConfirmCancel;
        if (confirmCancel != null)
            confirmCancel.Click -= OnConfirmCancelClicked;

        if (!ReferenceEquals(_visualState, candidate)) return;

        ApplyVisualState(null);
        _isDraggingWindow = false;
    }

    /// <summary>Queues one coalesced visual rebuild and defers hidden warm-window churn.</summary>
    private void QueueRebuildVisual()
    {
        if (!IsWindowAlive) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (IsWindowAlive) QueueRebuildVisual();
            }, DispatcherPriority.Background);
            return;
        }

        if (_layout == null) return;
        if (!IsVisible && !IsWarmPriming)
        {
            _rebuildVisualPending = true;
            DisposeContentGeneration();
            return;
        }

        if (_isRebuildingVisual || _isDraggingWindow || IsAnySliderGestureActive())
        {
            _rebuildVisualPending = true;
            if (IsAnySliderGestureActive()) _deferredSliderGestureRebuild = true;
            return;
        }

        if (_rebuildVisualQueued) return;

        _rebuildVisualQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsWindowAlive) return;
            _rebuildVisualQueued = false;
            RebuildVisual();
        }, DispatcherPriority.Background);
    }

    /// <summary>Flushes a rebuild requested while another rebuild was active.</summary>
    private void FlushPendingRebuildVisual()
    {
        if (!_rebuildVisualPending || _isRebuildingVisual) return;

        _rebuildVisualPending = false;
        QueueRebuildVisual();
    }

    private ScrollViewer BuildRows(
        FlyoutControlPalette palette,
        FlyoutVisualState candidate,
        UIResourceScope resources)
    {
        StackPanel rows = new() { Spacing = 0, Margin = Layout.RowsMargin };

        if ((_settings?.ShowIndividualSliders ?? true) && Monitors.Count > 0)
        {
            foreach (MonitorInfo monitor in Monitors)
                rows.Children.Add(BuildRow(monitor, palette, candidate, resources));
        }
        else if (Monitors.Count == 0)
        {
            TextBlock empty = TrayAppDotNETFlyoutUI.Text(L("Flyout_NoDisplays", "No DDC/CI displays detected"), palette,
                Layout.EmptyDisplaysFontSize, color: palette.SecondaryForeground);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            rows.Children.Add(new Border { Padding = Layout.EmptyDisplaysPadding, Child = empty });
        }

        if (_settings?.ShowMasterSlider ?? true)
            rows.Children.Add(BuildRow(MasterMonitor, palette, candidate, resources));

        if (_settings?.ShowNightLightSlider ?? true)
            rows.Children.Add(BuildRow(NightLightMonitor, palette, candidate, resources));

        candidate.ScrollViewer = new ScrollViewer
        {
            Content = new Border
            {
                Padding = Layout.RowsContentPadding,
                Child = rows
            },
            Margin = Layout.RowsViewportMargin,
            Focusable = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        return candidate.ScrollViewer;
    }

    private Border BuildRow(
        MonitorInfo monitor,
        FlyoutControlPalette palette,
        FlyoutVisualState candidate,
        UIResourceScope resources)
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }
        };

        Border icon = BuildRowIconButton(monitor, palette);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        TextBlock title = TrayAppDotNETFlyoutUI.Text(RowTitle(monitor), palette, Layout.RowTitleFontSize);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        if (monitor.IsCurveStopwatchVisible)
        {
            StackPanel stopwatch = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = Layout.RowStopwatchMargin
            };
            if (monitor.IsCurveStopwatchEnabled)
                stopwatch.Children.Add(BuildCurveStopwatchNumberBox(monitor, resources));
            stopwatch.Children.Add(BuildCurveStopwatchButton(monitor, palette));
            TrayAppDotNETToolTip.SetTip(stopwatch, monitor.CurveStopwatchToolTip);
            Grid.SetColumn(stopwatch, 2);
            grid.Children.Add(stopwatch);
        }

        if (monitor.IsMaster || monitor.IsNightLight)
        {
            Border curve = BuildCurveIconButton(
                palette,
                () => ToggleCurveForRow(monitor),
                Layout.RowActionButtonSize,
                Layout.RowActionButtonSize,
                Layout.RowCurveIconSize,
                margin: Layout.RowCurveButtonMargin,
                tooltip: monitor.IsNightLight
                    ? L("Flyout_NightLightCurve", "Night-light curve")
                    : L("Flyout_BrightnessCurve", "Brightness curve"));
            curve.Opacity = RowCurveEnabled(monitor) ? 1.0 : 0.4;
            Grid.SetColumn(curve, 3);
            grid.Children.Add(curve);
        }

        if (monitor is { IsMaster: false, IsNightLight: false, SupportsPowerControl: true }
            && (_settings?.ShowFlyoutMonitorPowerButtons ?? false))
        {
            Border power = TrayAppDotNETFlyoutUI.IconButton(
                GlyphCatalog.POWER.Text,
                palette,
                e => { _ = _monitorService.SetPowerStateAsync(monitor, !monitor.IsPoweredOn); },
                Layout.RowActionButtonSize,
                Layout.RowActionButtonSize,
                Layout.HeaderButtonFontSize,
                enabled: monitor.IsHardwareFunctional,
                margin: Layout.RowPowerButtonMargin,
                tooltip: L("Flyout_TurnOffDisplay", "Turn off display"));
            Grid.SetColumn(power, 3);
            grid.Children.Add(power);
        }

        bool showCurveModeButton =
            ShouldShowCurveModeButton(RowCurveEnabled(monitor), monitor.SliderState);
        Border? curveModeButton = showCurveModeButton
            ? BuildCurveModeButton(monitor, palette)
            : null;

        Grid sliderRow = new()
        {
            Height = Layout.SliderRowHeight,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = showCurveModeButton ? Layout.CurveModeSliderMargin : Layout.ZeroThickness,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };

        FlyoutSlider slider = CreateSlider(monitor, palette, resources);
        Grid.SetColumn(slider, 0);
        sliderRow.Children.Add(slider);

        TextBlock value = TrayAppDotNETFlyoutUI.Text(ValueText(monitor), palette, Layout.SliderValueFontSize);
        value.MinWidth = Layout.SliderValueMinWidth;
        value.Margin = Layout.SliderValueMargin;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        value.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(value, 1);
        sliderRow.Children.Add(value);

        Grid.SetRow(sliderRow, 1);
        Grid.SetColumn(sliderRow, 0);
        Grid.SetColumnSpan(sliderRow, 4);
        grid.Children.Add(sliderRow);

        Control rowContent = grid;
        Thickness rowMargin = Layout.RowMargin;
        if (curveModeButton != null)
        {
            // Extend the row into the root padding so the complete button surface starts 4 px from the flyout edge
            Grid curveModeRow = new();
            grid.Margin = Layout.CurveModeContentMargin;
            curveModeButton.HorizontalAlignment = HorizontalAlignment.Left;
            curveModeButton.VerticalAlignment = VerticalAlignment.Bottom;
            curveModeRow.Children.Add(grid);
            curveModeRow.Children.Add(curveModeButton);
            rowContent = curveModeRow;
            rowMargin = Layout.CurveModeRowMargin;
        }

        Border row = new()
        {
            Background = Brushes.Transparent,
            Margin = rowMargin,
            Child = rowContent,
            Opacity = RowOpacity(monitor)
        };
        candidate.ProfilePreviewRows[monitor] = new ProfilePreviewRowVisuals(slider, row, value, curveModeButton);
        return row;
    }

    private Border BuildFooter(FlyoutControlPalette palette, bool rounded, UIResourceScope resources)
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        StackPanel profiles = new()
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center
        };
        foreach (ProfileButtonItem item in ProfileButtons)
            profiles.Children.Add(BuildProfileFooterButton(item, palette, resources));
        if (_settings?.Autosave == false)
            profiles.Children.Add(BuildSaveProfileButton(palette));
        Grid.SetColumn(profiles, 0);
        grid.Children.Add(profiles);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center
        };
        if (_settings?.ShowEnvironmentalCurvesButton ?? true)
        {
            actions.Children.Add(BuildCurveIconButton(palette, ToggleEnvironmentalCurves,
                Layout.FooterCurveIconButtonWidth, Layout.FooterCurveIconButtonHeight, Layout.FooterCurveIconSize,
                tooltip: L("Flyout_EnvironmentalCurves", "Environmental curves"),
                opacity: IsBrightnessCurveEnabled || IsNightLightCurveEnabled ? 1.0 : 0.4));
        }

        if ((_settings?.ShowFlyoutFooterPowerButton ?? false)
            && Monitors.Any(static m => m.SupportsPowerControl))
        {
            actions.Children.Add(BuildFooterIconButton(_theme.GlyphPower, palette, PowerOffFooterTargets,
                L("Flyout_TurnOffAllDisplays", "Turn off displays")));
        }

        if (_settings?.ShowFlyoutDisplaySettingsButton ?? true)
        {
            actions.Children.Add(BuildFooterIconButton(
                _theme.GlyphDisplaySettings,
                palette,
                OpenDisplaySettings,
                L("Flyout_DisplaySettings", "Display settings"),
                fontFamily: GlyphCatalog.SEGOE_FLUENT_ICONS,
                fontWeight: FontWeight.Black));
        }

        Border settingsButton = BuildFooterIconButton(
            _theme.GlyphSettings,
            palette,
            () => SettingsRequested?.Invoke(),
            L("Tray_Settings", "Settings"),
            fontFamily: GlyphCatalog.SEGOE_FLUENT_ICONS,
            fontWeight: FontWeight.Black);
        SuppressNextAutoHideWhenPressed(settingsButton);
        actions.Children.Add(settingsButton);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        return new Border
        {
            Background =
                TrayAppDotNETFlyoutUI.Brush(_theme.ResolveFooterBackground(_settings,
                    BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings))),
            CornerRadius = rounded ? Layout.FooterCornerRadius : Layout.ZeroCornerRadius,
            Padding = FooterPadding(),
            Margin = Layout.FooterMargin,
            Child = grid
        };
    }

    private Thickness FooterPadding()
    {
        bool crowded = (_settings?.ShowEnvironmentalCurvesButton ?? true)
                       && (_settings?.ShowFlyoutFooterPowerButton ?? false)
                       && Monitors.Any(static m => m.SupportsPowerControl)
                       && (_settings?.ShowFlyoutDisplaySettingsButton ?? true)
                       && _settings?.Autosave == false;
        return crowded ? Layout.FooterPaddingCrowded : Layout.FooterPaddingNormal;
    }

    private Border BuildProfileFooterButton(
        ProfileButtonItem item,
        FlyoutControlPalette palette,
        UIResourceScope resources)
    {
        Grid content = new() { IsHitTestVisible = false };
        TextBlock label = TrayAppDotNETFlyoutUI.Text(item.Glyph, palette, Layout.ProfileGlyphFontSize, FontWeight.Bold);
        label.FontFamily = FlyoutFont;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        TrayAppDotNETFlyoutUI.ApplyGlyphTextRendering(label);
        content.Children.Add(label);
        if (item.IsSelected)
        {
            Border indicator = new()
            {
                Width = Layout.ProfileIndicatorWidth,
                Height = Layout.ProfileIndicatorHeight,
                CornerRadius = Layout.ProfileIndicatorCornerRadius,
                Background = TrayAppDotNETFlyoutUI.Brush(palette.Foreground),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = Layout.ProfileIndicatorMargin
            };
            content.Children.Add(indicator);
        }

        Border button = TrayAppDotNETFlyoutUI.IconButton(string.Empty, palette,
            _ => SelectProfileApplyingMode(item.Index), Layout.ProfileButtonWidth, Layout.ProfileButtonHeight, 0,
            tooltip: ProfileTooltip(item.Index));
        button.Child = content;
        AttachProfilePreviewHandlers(button, item.Index, resources);
        return button;
    }

    private Border BuildSaveProfileButton(FlyoutControlPalette palette)
    {
        Grid content = new();
        TextBlock glyph = TrayAppDotNETFlyoutUI.IconText(
            _theme.GlyphProfileSave,
            palette,
            Layout.SaveProfileGlyphFontSize,
            GlyphCatalog.SEGOE_FLUENT_ICONS,
            FontWeight.Black);
        glyph.Opacity = HasUnsavedChanges ? 1.0 : 0.4;
        content.Children.Add(glyph);

        Border button = TrayAppDotNETFlyoutUI.IconButton(string.Empty, palette, _ => SaveCurrentProfile(),
            Layout.ProfileButtonWidth, Layout.ProfileButtonHeight, 0, tooltip: L("Flyout_SaveProfile", "Save profile"));
        button.Child = content;
        return button;
    }

    private Border BuildRowIconButton(MonitorInfo monitor, FlyoutControlPalette palette)
    {
        Border button = TrayAppDotNETFlyoutUI.IconButton(
            string.Empty,
            palette,
            _ => OnMonitorIconClick(monitor),
            Layout.RowIconButtonWidth,
            Layout.RowIconButtonHeight,
            0,
            enabled: !monitor.IsNightLight || NightLightProvider.IsSupported(),
            margin: Layout.RowIconMargin,
            tooltip: RowIconTooltip(monitor));

        button.Child = monitor.IsNightLight
            ? new NightLightBulbGlyphIcon
            {
                Width = Layout.NightLightIconSize,
                Height = Layout.NightLightIconSize,
                IconColor = palette.IconForeground
            }
            : TrayAppDotNETFlyoutUI.IconText(
                RowGlyph(monitor),
                palette,
                monitor.IsMaster ? Layout.MasterIconFontSize : Layout.MonitorIconFontSize,
                GlyphCatalog.SEGOE_FLUENT_ICONS,
                FontWeight.Black);

        return button;
    }

    private SettingsNumberBox BuildCurveStopwatchNumberBox(MonitorInfo monitor, UIResourceScope resources)
    {
        bool isLight = BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings);
        SettingsNumberBox number =
            resources.Own(new SettingsNumberBox(
                CreateSettingsPalette(_theme, _settings, isLight), monitor.CurveStopwatchMinutes, 1, 1440,
                Layout.StopwatchBoxWidth, "m")
            {
                Height = Layout.StopwatchBoxHeight,
                VerticalAlignment = VerticalAlignment.Center,
                Step = 5,
                WheelStep = 5,
                LargeStep = 30,
                ExtraLargeStep = 60,
                HandleMouseWheelWhenMouseOver = true
            });
        number.ValueChanged += (_, e) =>
        {
            int value = e.NewValue.HasValue
                ? (int)Math.Round(e.NewValue.Value)
                : monitor.CurveStopwatchMinutes;
            SetCurveStopwatchMinutes(monitor, value);
        };
        return number;
    }

    private Border BuildCurveStopwatchButton(MonitorInfo monitor, FlyoutControlPalette palette)
    {
        Border button = TrayAppDotNETFlyoutUI.IconButton(
            GlyphCatalog.STOPWATCH.Text,
            palette,
            _ => ToggleCurveStopwatch(monitor),
            Layout.StopwatchButtonWidth,
            Layout.StopwatchButtonHeight,
            Layout.StopwatchButtonFontSize,
            margin: Layout.StopwatchButtonMargin,
            tooltip: monitor.CurveStopwatchToolTip,
            fontFamily: TrayAppDotNETCommon.Visuals.TADNFontResolver.ResolveFontFamilyName(GlyphCatalog.STOPWATCH.Font));
        button.Opacity = monitor.IsCurveStopwatchEnabled ? 1.0 : 0.4;
        return button;
    }

    private Border BuildCurveModeButton(MonitorInfo monitor, FlyoutControlPalette palette)
    {
        Glyph glyph = CurveModeGlyph(monitor);
        Border button = TrayAppDotNETFlyoutUI.IconButton(
            glyph.Text,
            palette,
            _ => ToggleCurveModeForRow(monitor),
            Layout.ModeButtonWidth,
            Layout.ModeButtonHeight,
            Layout.ModeButtonFontSize,
            enabled: CanEditSlider(monitor),
            margin: Layout.ModeButtonMargin,
            tooltip: CurveModeTooltip(monitor),
            fontFamily: TrayAppDotNETCommon.Visuals.TADNFontResolver.ResolveFontFamilyName(glyph.Font));
        ApplyCurveModeButtonVisual(monitor, button);
        return button;
    }

    private Border BuildFooterIconButton(
        string glyph,
        FlyoutControlPalette palette,
        Action click,
        string tooltip,
        double opacity = 1.0,
        string? fontFamily = null,
        FontWeight? fontWeight = null)
    {
        Border button = TrayAppDotNETFlyoutUI.IconButton(
            glyph,
            palette,
            _ => click(),
            Layout.FooterIconButtonWidth,
            Layout.FooterIconButtonHeight,
            Layout.FooterIconButtonFontSize,
            tooltip: tooltip,
            fontFamily: fontFamily,
            fontWeight: fontWeight);
        button.Opacity = opacity;
        return button;
    }

    private Border BuildCurveIconButton(
        FlyoutControlPalette palette,
        Action click,
        double width,
        double height,
        double iconSize,
        Thickness? margin = null,
        string? tooltip = null,
        double opacity = 1.0)
    {
        Border button = TrayAppDotNETFlyoutUI.IconButton(string.Empty, palette, _ => click(), width, height, 0,
            margin: margin, tooltip: tooltip);
        button.Child = BuildCurveIconContent(palette, iconSize,
            disabledPeriod: IsInCurveDisabledPeriod && width <= Layout.RowActionButtonSize);
        button.Opacity = opacity;
        return button;
    }

    private Control BuildCurveIconContent(FlyoutControlPalette palette, double size, bool disabledPeriod)
    {
        if (disabledPeriod)
        {
            TextBlock disabled = TrayAppDotNETFlyoutUI.IconText(
                GlyphCatalog.CRESCENT_MOON.Text,
                palette,
                Layout.CurveDisabledGlyphFontSize,
                GlyphCatalog.SEGOE_MDL2_ASSETS);
            disabled.Width = Layout.CurveDisabledGlyphSize;
            disabled.Height = Layout.CurveDisabledGlyphSize;
            return disabled;
        }

        return new EnvironmentalCurveGlyphIcon { Width = size, Height = size, IconColor = palette.IconForeground };
    }

    private void AddFloatingButtons(
        Grid rootGrid,
        FlyoutControlPalette palette,
        FlyoutVisualState candidate,
        UIResourceScope resources)
    {
        if (IsUpdateButtonVisible)
        {
            Border update = TrayAppDotNETFlyoutUI.TextButton("Update", palette, ShowUpdateConfirmation,
                Layout.UpdateButtonFontSize, Layout.UpdateButtonPadding);
            update.Width = Layout.UpdateButtonWidth;
            update.Height = Layout.UpdateButtonHeight;
            update.HorizontalAlignment = HorizontalAlignment.Right;
            update.VerticalAlignment = VerticalAlignment.Top;
            update.Margin = Layout.UpdateButtonMargin;
            Grid.SetRow(update, 0);
            rootGrid.Children.Add(update);
        }

        candidate.UndockButton = BuildUndockButton(palette, candidate, resources);
        candidate.UndockButton.HorizontalAlignment = HorizontalAlignment.Right;
        candidate.UndockButton.VerticalAlignment = VerticalAlignment.Top;
        candidate.UndockButton.Margin = Layout.UndockButtonMargin;
        Grid.SetRow(candidate.UndockButton, 0);
        rootGrid.Children.Add(candidate.UndockButton);
    }

    private FlyoutSlider CreateSlider(
        MonitorInfo monitor,
        FlyoutControlPalette palette,
        UIResourceScope resources)
    {
        bool isLight = BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings);
        bool curveDrivenWithTarget = monitor is
            { SliderState: SliderState.CurveActive, HasCurveTargetBrightness: true };
        FlyoutSlider slider = new()
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(monitor.Brightness, 0, 100),
            TrackColor = palette.SliderTrack,
            ProgressColor = monitor.IsNightLight
                ? _theme.ResolveEnvironmentalNightLightCurve(_settings, isLight)
                : palette.SliderProgress,
            ThumbColor = palette.SliderThumb,
            IndicatorColor = monitor.IsNightLight
                ? _theme.ResolveEnvironmentalNightLightCurve(_settings, isLight)
                : _theme.ResolveEnvironmentalBrightnessCurve(_settings, isLight),
            MeterPeakColor = palette.SliderProgress,
            MeterPeakStereoColor = palette.SliderProgress,
            ProgressValueOverride = curveDrivenWithTarget ? monitor.CurveTargetBrightness : null,
            ProgressOverrideColor = curveDrivenWithTarget ? palette.SliderProgress : null,
            HitTestVerticalPadding = Layout.SliderHitTestVerticalPadding,
            WheelStep = _settings?.FlyoutScrollWheelStep ?? 2,
            CoarseWheelStep = Layout.CoarseWheelStep,
            KeyboardStep = 1,
            LargeKeyboardStep = 10,
            IsEnabled = CanEditSlider(monitor),
            Thumb = ResolveSliderThumbOption(),
            PreviewValue = monitor.ShowPreview ? monitor.PreviewBrightness : null,
            IndicatorValue = ShouldShowCurveIndicator(monitor) ? monitor.CurveTargetBrightness : null,
            IndicatorOpacity = monitor.IsCurveSleeping ? 0.45 : 1.0,
            ThumbOpacity = curveDrivenWithTarget && IsCurveAbsoluteMode ? 0.4 : 1.0
        };
        resources.Own(slider);
        slider.UserAdjustmentStarted += (_, _) =>
        {
            BeginSliderGesture(monitor);
            if (monitor.IsMaster) BeginMasterSliderGesture();
        };
        slider.UserAdjustmentCompleted += (_, _) =>
        {
            monitor.IsDragging = false;
            if (monitor.IsMaster) _masterSliderGesturePrepared = false;
            CompleteSliderGesture(monitor);
        };
        slider.DoubleTapped += (_, e) =>
        {
            if (!monitor.IsCurveReleased) return;

            ReengageCurveReleasedMonitor(monitor);
            if (monitor.IsMaster) ReengageIndividualBrightnessCurveOverridesFromMaster();
            UpdateCurveStopwatchVisibility(monitor);
            _curveService.Evaluate(immediateHardware: true);
            RebuildVisual();
            e.Handled = true;
        };
        slider.ValueChanged += (_, value) => OnSliderValueChanged(slider, monitor, value);
        return slider;
    }

    private Border BuildUndockButton(
        FlyoutControlPalette palette,
        FlyoutVisualState candidate,
        UIResourceScope resources)
    {
        candidate.UndockButtonController = resources.Own(
            new FlyoutUndockButtonController(new FlyoutUndockButtonOptions
        {
            Width = Layout.HeaderButtonSize,
            Height = Layout.HeaderButtonSize,
            FontSize = Layout.HeaderButtonFontSize,
            FontFamily = GlyphCatalog.SEGOE_FLUENT_ICONS,
            FontWeight = FontWeight.Black,
            IsVisible = _settings?.AllowFlyoutUndock ?? true,
            Owner = this,
            DragHelper = _dragHelper,
            Palette = palette,
            CaptureDockedPosition = CaptureDockedPosition,
            IsUndocked = () => _isUndocked,
            SetUndockedFromDrag = SetUndockedFromDrag,
            ToggleUndocked = ToggleUndocked,
            CommitDragPosition = CommitDragPosition,
            DraggingChanged = dragging =>
            {
                _isDraggingWindow = dragging;
                if (!dragging) FlushPendingRebuildVisual();
            },
            UndockTooltip = () => L("Flyout_Undock_Tooltip", "Undock"),
            RedockTooltip = () => L("Flyout_Redock_Tooltip", "Redock"),
            DragThreshold = Layout.DragThreshold,
            CornerRadius = Rounded(Layout.UndockButtonCornerRadius)
        }));
        return candidate.UndockButtonController.Button;
    }

    private Border BuildConfirmOverlay(SettingsPalette palette, bool rounded, FlyoutVisualState candidate)
    {
        candidate.ConfirmTitle = TrayAppDotNETFlyoutUI.Text(string.Empty,
            CreateFlyoutPalette(_theme, _settings, palette, BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings)),
            Layout.ConfirmTitleFontSize, FontWeight.SemiBold);
        candidate.ConfirmMessage = TrayAppDotNETFlyoutUI.Text(string.Empty,
            CreateFlyoutPalette(_theme, _settings, palette, BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings)),
            Layout.ConfirmMessageFontSize, color: palette.SecondaryForeground);
        candidate.ConfirmMessage.TextWrapping = TextWrapping.Wrap;

        candidate.ConfirmOK = TrayAppDotNETSettingsUI.Button("OK", palette);
        candidate.ConfirmCancel = TrayAppDotNETSettingsUI.Button("Cancel", palette);
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = Layout.ConfirmButtonsSpacing
        };
        buttons.Children.Add(candidate.ConfirmCancel);
        buttons.Children.Add(candidate.ConfirmOK);

        StackPanel panel = new() { Spacing = Layout.ConfirmPanelSpacing };
        panel.Children.Add(candidate.ConfirmTitle);
        panel.Children.Add(candidate.ConfirmMessage);
        panel.Children.Add(buttons);

        Border box = new()
        {
            Background = TrayAppDotNETFlyoutUI.Brush(palette.CardBackground),
            BorderBrush = TrayAppDotNETFlyoutUI.Brush(palette.Border),
            BorderThickness = Layout.ConfirmBorderThickness,
            CornerRadius = rounded ? Layout.ConfirmCornerRadius : Layout.ZeroCornerRadius,
            Padding = Layout.ConfirmPadding,
            Width = Layout.ConfirmWidth,
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        return new Border
        {
            Background =
                TrayAppDotNETFlyoutUI.Brush(
                    _theme.FlyoutOverlayBackdrop.For(BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings))),
            Child = box
        };
    }

    private async void ShowUpdateConfirmation()
    {
        if (!IsWindowAlive || _isUpdateDownloadInFlight) return;
        UpdateCheckService? service = AppServices.UpdateCheckService;
        UpdateInfo? info = service?.AvailableUpdate;
        if (service == null || info == null) return;

        _ = await TrayAppDotNETUpdatePromptPresenter.ShowInstallUpdateAsync(new TrayAppDotNETUpdatePromptOptions
        {
            Owner = this,
            Service = service,
            UpdateInfo = info,
            Palette = CreateSettingsPalette(
                _theme,
                _settings,
                BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings)),
            EnableRoundedCorners = _settings?.EnableRoundedCorners == true,
            Localize = L,
            Log = static message => WPFLog.Log(message),
            FlushLog = static () => WPFLog.Flush(),
            Shutdown = static () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                    lifetime.Shutdown();
            },
            SetPromptOpen = open =>
            {
                if (IsWindowAlive) _isUpdateDialogOpen = open;
            },
            SetDownloadInFlight = inFlight =>
            {
                if (IsWindowAlive) _isUpdateDownloadInFlight = inFlight;
            },
            PromptClosed = () =>
            {
                if (IsWindowAlive) NotifyChildWindowClosedFromDeactivation();
            }
        });
    }

    private void ShowConfirmOverlay(
        string title,
        string message,
        string okText,
        string? cancelText,
        Action onOK)
    {
        if (_confirmOverlay == null || _confirmTitle == null || _confirmMessage == null || _confirmOK == null ||
            _confirmCancel == null)
            return;

        _confirmTitle.Text = title;
        _confirmMessage.Text = message;
        _confirmOK.Text = okText;
        _confirmOK.Click -= OnConfirmOKClicked;
        _confirmOK.Click += OnConfirmOKClicked;
        _confirmOK.Tag = onOK;

        if (cancelText == null)
            _confirmCancel.IsVisible = false;
        else
        {
            _confirmCancel.IsVisible = true;
            _confirmCancel.Text = cancelText;
            _confirmCancel.Click -= OnConfirmCancelClicked;
            _confirmCancel.Click += OnConfirmCancelClicked;
        }

        _confirmOverlay.IsVisible = true;
    }

    private void OnConfirmOKClicked(object? sender, EventArgs e)
    {
        if (sender is SettingsButton { Tag: Action action }) action();
    }

    private void OnConfirmCancelClicked(object? sender, EventArgs e) => CancelConfirmOverlay();

    private void CancelConfirmOverlay()
    {
        _confirmOverlay?.IsVisible = false;
        if (_confirmOK != null)
        {
            _confirmOK.Click -= OnConfirmOKClicked;
            _confirmOK.Tag = null;
        }

        if (_confirmCancel != null)
            _confirmCancel.Click -= OnConfirmCancelClicked;
    }

    private void OnSliderValueChanged(FlyoutSlider slider, MonitorInfo monitor, double value)
    {
        if (monitor.IsNightLight)
        {
            ApplyNightLightSliderValue(slider, value);
            return;
        }

        ApplyMonitorSliderValue(slider, monitor, value);
        FlushDeferredManualCurveOverrideResync(monitor);
    }

    private double ApplyMonitorSliderValue(FlyoutSlider? slider, MonitorInfo monitor, double value)
    {
        BrightnessChanged = true;
        BrightnessUpdated?.Invoke();

        double target = BrightnessSliderMath.NormalizeManualPercent(value);
        if (slider != null && Math.Abs(slider.Value - target) >= 0.001)
            slider.Value = target;

        bool shouldWriteModel = Math.Abs(monitor.Brightness - target) >= 0.001
                                || Math.Abs(monitor.LastUserBrightness - target) >= 0.001;
        if (shouldWriteModel)
            monitor.Brightness = target;

        if (monitor.IsMaster && _masterSliderGesturePrepared && shouldWriteModel)
            ApplyMasterToEnabledMonitors();

        return target;
    }

    private void ApplyBrightnessCurveImmediatelyIfActive()
    {
        if (!IsBrightnessCurveEnabled) return;
        _curveService.Evaluate(immediateHardware: true);
    }

    private void ApplyNightLightSliderValue(FlyoutSlider? slider, double value)
    {
        double target = ApplyMonitorSliderValue(slider, NightLightMonitor, value);
        ApplyManualNightLightStrength(target);
        FlushDeferredManualCurveOverrideResync(NightLightMonitor);
    }

    private void ApplyManualNightLightStrength(double sliderTarget)
    {
        if (!NightLightProvider.IsSupported()) return;
        bool isNightLightActive = _isNightLightActive && NightLightProvider.IsEnabled();
        if (!ShouldApplyManualNightLightStrength(
                IsNightLightCurveEnabled,
                _isInCurveDisabledPeriod,
                NightLightMonitor.IsCurveReleased,
                isNightLightActive))
            return;

        int nightLightTarget = FlipIfNightLightInverted((int)sliderTarget);
        NightLightProvider.SetStrength(nightLightTarget);
    }

    internal static bool ShouldApplyManualNightLightStrength(
        bool isNightLightCurveEnabled,
        bool isInCurveDisabledPeriod,
        bool isNightLightCurveReleased,
        bool isNightLightActive)
    {
        if (!isNightLightActive) return false;
        if (!isNightLightCurveEnabled) return true;
        if (isInCurveDisabledPeriod) return true;
        return isNightLightCurveReleased;
    }

    private void BeginSliderGesture(MonitorInfo monitor)
    {
        monitor.IsDragging = true;
        DisengageCurveForUserAdjustment(monitor, replayCurrentSliderValue: false);
    }

    private void CompleteSliderGesture(MonitorInfo monitor)
    {
        FlushDeferredManualCurveOverrideResync(monitor);
        CheckAndUpdateUnsavedChanges();

        if (!_deferredSliderGestureRebuild)
        {
            RefreshSliderRowVisuals();
            return;
        }

        _deferredSliderGestureRebuild = false;
        QueueRebuildVisual();
    }

    private void BeginMasterSliderGesture()
    {
        if (_masterSliderGesturePrepared) return;
        CanonicalizeMonitorBaselinesForMasterGesture();
        CaptureOffsetsFromMaster();
        _masterSliderGesturePrepared = true;
    }

    private void CanonicalizeMonitorBaselinesForMasterGesture()
    {
        bool preserve = _settings?.PreserveMasterSliderOffsets == true;
        bool suspendForCurve = IsBrightnessCurveEnabled
                               && (_isInCurveDisabledPeriod || MasterMonitor.IsCurveReleased);
        IDisposable? hardwareWriteSuspension = suspendForCurve ? _monitorService.SuspendHardwareWrites() : null;
        bool wasSuppressingPropagation = _suppressPropagation;
        _suppressPropagation = true;
        try
        {
            foreach (MonitorInfo monitor in Monitors)
            {
                if (!monitor.IsParticipatingInMaster) continue;

                double source = preserve ? monitor.VirtualBrightness : monitor.LastUserBrightness;
                double roundedSource = BrightnessSliderMath.RoundManualPercent(source);
                double sliderBrightness = BrightnessSliderMath.ClampPercent(roundedSource);

                monitor.Brightness = sliderBrightness;
                monitor.LastUserBrightness = sliderBrightness;
                monitor.VirtualBrightness = preserve ? roundedSource : sliderBrightness;
                UpdateVisibleMonitorSliderValue(monitor, sliderBrightness);
            }

            double masterBrightness =
                BrightnessSliderMath.ClampPercent(ComputeMasterFromEnabledIndividuals());
            MasterMonitor.Brightness = masterBrightness;
            MasterMonitor.LastUserBrightness = masterBrightness;
            MasterMonitor.VirtualBrightness = masterBrightness;
            UpdateVisibleMonitorSliderValue(MasterMonitor, masterBrightness);
        }
        finally
        {
            hardwareWriteSuspension?.Dispose();
            _suppressPropagation = wasSuppressingPropagation;
        }
    }

    private void OnMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsWindowAlive) return;
        if (e.PropertyName == nameof(MonitorInfo.EffectiveRoundedBrightness))
        {
            BrightnessUpdated?.Invoke();
            RefreshSliderRowVisuals();
        }

        if (e.PropertyName is nameof(MonitorInfo.CurveTargetBrightness)
            or nameof(MonitorInfo.HasCurveTargetBrightness))
        {
            RefreshSliderRowVisuals();
            return;
        }

        if (e.PropertyName is not (nameof(MonitorInfo.Brightness)
            or nameof(MonitorInfo.IsPoweredOn)
            or nameof(MonitorInfo.SliderState)))
            return;

        if (e.PropertyName == nameof(MonitorInfo.Brightness)
            && !_suppressPropagation
            && (!IsBrightnessCurveEnabled || _isInCurveDisabledPeriod || !IsCurveAbsoluteMode ||
                MasterMonitor.IsCurveReleased))
        {
            _suppressPropagation = true;
            try
            {
                if (ReferenceEquals(sender, MasterMonitor))
                {
                    if (!MasterMonitor.IsDragging)
                        ApplyMasterToEnabledMonitors();
                }
                else
                    UpdateMasterFromEnabledIndividuals();
            }
            finally
            {
                _suppressPropagation = false;
            }
        }

        if (e.PropertyName == nameof(MonitorInfo.SliderState)
            && sender is MonitorInfo stateChanged)
        {
            if (!ReferenceEquals(sender, MasterMonitor) && !_suppressPropagation)
            {
                _suppressPropagation = true;
                try { UpdateMasterFromEnabledIndividuals(); }
                finally { _suppressPropagation = false; }
            }

            UpdateCurveStopwatchVisibility(stateChanged);
        }

        if (e.PropertyName == nameof(MonitorInfo.Brightness)
            && IsBrightnessCurveEnabled
            && !IsCurveAbsoluteMode
            && !_isInCurveDisabledPeriod)
            _curveService.RequestEvaluation();

        CheckAndUpdateUnsavedChanges();
        RefreshOrRebuildForMonitorChange(e.PropertyName);
    }

    private void OnNightLightPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsWindowAlive) return;
        if (e.PropertyName == nameof(MonitorInfo.EffectiveRoundedBrightness))
        {
            BrightnessUpdated?.Invoke();
            RefreshSliderRowVisuals();
            return;
        }

        if (e.PropertyName is nameof(MonitorInfo.CurveTargetBrightness)
            or nameof(MonitorInfo.HasCurveTargetBrightness))
        {
            RefreshSliderRowVisuals();
            return;
        }

        if (e.PropertyName == nameof(MonitorInfo.SliderState))
        {
            UpdateCurveStopwatchVisibility(NightLightMonitor);
            RefreshOrRebuildForMonitorChange(e.PropertyName);
            return;
        }

        if (e.PropertyName != nameof(MonitorInfo.Brightness)) return;

        if (IsNightLightCurveEnabled && !IsCurveAbsoluteMode && !_isInCurveDisabledPeriod)
            _curveService.RequestEvaluation();

        CheckAndUpdateUnsavedChanges();
        RefreshSliderRowVisuals();
    }

    private void OnMonitorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!IsWindowAlive) return;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                {
                    foreach (MonitorInfo monitor in e.NewItems.OfType<MonitorInfo>())
                        AttachMonitor(monitor);
                }

                break;

            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                {
                    foreach (MonitorInfo monitor in e.OldItems.OfType<MonitorInfo>())
                        DetachMonitor(monitor);
                }

                break;

            case NotifyCollectionChangedAction.Replace:
                if (e.OldItems != null)
                {
                    foreach (MonitorInfo monitor in e.OldItems.OfType<MonitorInfo>())
                        DetachMonitor(monitor);
                }

                if (e.NewItems != null)
                {
                    foreach (MonitorInfo monitor in e.NewItems.OfType<MonitorInfo>())
                        AttachMonitor(monitor);
                }

                break;

            case NotifyCollectionChangedAction.Move:
                if (e is { OldStartingIndex: >= 0, NewStartingIndex: >= 0 }
                    && e.OldStartingIndex != e.NewStartingIndex)
                {
                    int masterIndex = AllItems.IndexOf(MasterMonitor);
                    if (masterIndex < 0) masterIndex = AllItems.Count;
                    if (e.OldStartingIndex < masterIndex && e.NewStartingIndex < masterIndex)
                        AllItems.Move(e.OldStartingIndex, e.NewStartingIndex);
                }

                break;

            case NotifyCollectionChangedAction.Reset:
                foreach (MonitorInfo monitor in MasterMonitor.Dependents.ToList())
                    DetachMonitor(monitor);
                foreach (MonitorInfo monitor in Monitors)
                    AttachMonitor(monitor);
                break;
        }

        CheckAndUpdateUnsavedChanges();
        BrightnessUpdated?.Invoke();
        QueueRebuildVisual();
        QueuePositionNearTray();
    }

    private void OnInitialMonitorEnrollmentRefreshed()
    {
        if (!IsWindowAlive) return;
        if (!_awaitingInitialAsyncMonitorEnrollment || Monitors.Count == 0) return;
        _awaitingInitialAsyncMonitorEnrollment = false;
        _suppressPropagation = true;
        try
        {
            UpdateMasterFromEnabledIndividuals();
            CaptureOffsetsFromMaster();
        }
        finally
        {
            _suppressPropagation = false;
        }
    }

    private void AttachMonitor(MonitorInfo monitor)
    {
        if (MasterMonitor.Dependents.Contains(monitor)) return;

        MasterMonitor.Dependents.Add(monitor);
        int masterIndex = AllItems.IndexOf(MasterMonitor);
        if (masterIndex < 0) AllItems.Add(monitor);
        else AllItems.Insert(masterIndex, monitor);

        SubscribeMonitor(monitor);
        _suppressPropagation = true;
        try
        {
            bool restoreBrightnessProfile = _isBrightnessCurveEnabled || _settings?.ApplyBrightnessOnStartup == true;
            if (restoreBrightnessProfile)
            {
                IDisposable? hardwareWriteSuspension = _isBrightnessCurveEnabled
                    ? _monitorService.SuspendHardwareWrites()
                    : null;
                try { _profileManager.ApplyCurrentProfile([monitor], includeBrightness: true); }
                finally { hardwareWriteSuspension?.Dispose(); }
            }

            InitializeOffsetFromMaster(monitor);
            UpdateMasterFromEnabledIndividuals();
        }
        finally
        {
            _suppressPropagation = false;
        }

        RestoreCurveStopwatchForMonitor(monitor, saveExpired: true);
        UpdateCurveStopwatchVisibility(monitor);
        StartCurveStopwatchTimerIfNeeded();
    }

    private void DetachMonitor(MonitorInfo monitor)
    {
        _deferredManualCurveOverrideResync.Remove(monitor);
        UnsubscribeMonitor(monitor);
        MasterMonitor.Dependents.Remove(monitor);
        AllItems.Remove(monitor);
        _suppressPropagation = true;
        try { UpdateMasterFromEnabledIndividuals(); }
        finally { _suppressPropagation = false; }
    }

    private void SubscribeMonitor(MonitorInfo monitor)
    {
        if (!_subscribedMonitors.Add(monitor)) return;
        monitor.PropertyChanged += OnMonitorPropertyChanged;
    }

    private void UnsubscribeMonitor(MonitorInfo monitor)
    {
        if (!_subscribedMonitors.Remove(monitor)) return;
        monitor.PropertyChanged -= OnMonitorPropertyChanged;
    }

    private void UnsubscribeAllMonitors()
    {
        foreach (MonitorInfo monitor in _subscribedMonitors.ToArray())
            monitor.PropertyChanged -= OnMonitorPropertyChanged;
        _subscribedMonitors.Clear();
    }

    private void OnSelectedProfileChanged(int newIndex)
    {
        if (!IsWindowAlive) return;
        foreach (ProfileButtonItem item in ProfileButtons)
            item.IsSelected = item.Index == newIndex;
        ClearPreviewDateCurve();
        ClearProfilePreview();
        CheckAndUpdateUnsavedChanges();
        _curveService.Evaluate(immediateHardware: true);
        QueueRebuildVisual();
    }

    private void OnProfilesListChanged()
    {
        if (!IsWindowAlive) return;
        ClearPreviewDateCurve();
        BuildProfileButtonItems();
        QueueRebuildVisual();
    }

    private void UpdateSaveButtonState(bool hasUnsavedChanges)
    {
        if (!IsWindowAlive) return;
        if (_hasUnsavedChanges == hasUnsavedChanges) return;
        _hasUnsavedChanges = hasUnsavedChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        if (IsAnySliderGestureActive())
        {
            _deferredSliderGestureRebuild = true;
            return;
        }

        QueueRebuildVisual();
    }

    private void CheckAndUpdateUnsavedChanges()
    {
        MasterSliderMode mode = CurrentMasterSliderMode;
        int nightlight = FlipIfNightLightInverted(NightLightMonitor.RoundedBrightness);
        bool isAnySliderDragging = MasterMonitor.IsDragging || NightLightMonitor.IsDragging ||
                                   Monitors.Any(monitor => monitor.IsDragging);
        if (CanAutosaveProfile(_settings?.Autosave == true, isAnySliderDragging)
            && _profileManager.HasPendingChanges(Monitors, mode, nightlight))
            _profileManager.SaveCurrentState(Monitors, mode, nightlight);

        _profileManager.CheckForUnsavedChanges(Monitors, mode, nightlight);
    }

    internal static bool CanAutosaveProfile(bool autosaveEnabled, bool isAnySliderDragging) =>
        autosaveEnabled && !isAnySliderDragging;

    private void SelectProfileApplyingMode(int index)
    {
        if (index < 0 || index >= _profileManager.Profiles.Profiles.Count) return;
        if (MasterMonitor.IsDragging || NightLightMonitor.IsDragging || Monitors.Any(m => m.IsDragging))
        {
            WPFLog.Log($"BrightnessFlyoutWindow.SelectProfileApplyingMode({index}) skipped: drag in progress");
            return;
        }

        if (_settings != null)
        {
            MasterSliderMode profileMode = _profileManager.Profiles.Profiles[index].MasterSliderMode;
            if (_settings.MasterSliderMode != profileMode)
            {
                _settings.MasterSliderMode = profileMode;
                _settings.Save();
            }
        }

        IDisposable? hardwareWriteSuspension = IsBrightnessCurveEnabled
            ? _monitorService.SuspendHardwareWrites()
            : null;
        try
        {
            using (NightLightMonitor.SuspendNotifications())
            {
                _profileManager.SelectProfile(
                    index,
                    Monitors,
                    strength => NightLightMonitor.Brightness = FlipIfNightLightInverted(strength));
            }

            UpdateMasterFromEnabledIndividuals();
        }
        finally
        {
            hardwareWriteSuspension?.Dispose();
        }

        QueueRebuildVisual();
    }

    private void SaveCurrentProfile()
    {
        _profileManager.SaveCurrentState(
            Monitors,
            CurrentMasterSliderMode,
            FlipIfNightLightInverted(NightLightMonitor.RoundedBrightness));
        CheckAndUpdateUnsavedChanges();
        RebuildVisual();
    }

    private void ShowProfilePreview(int profileIndex)
    {
        if (profileIndex < 0 || profileIndex >= _profileManager.Profiles.Profiles.Count) return;
        if (profileIndex == _profileManager.SelectedIndex)
        {
            ClearProfilePreview();
            return;
        }

        if (_previewedProfileIndex == profileIndex) return;

        BrightnessProfile profile = _profileManager.Profiles.Profiles[profileIndex];
        _previewedProfileIndex = profileIndex;
        MasterMonitor.PreviewBrightness = ComputeMasterPreviewForProfile(profile);
        MasterMonitor.PreviewEnablementDiffers = false;
        MasterMonitor.ShowPreview = true;

        foreach (MonitorInfo monitor in Monitors)
        {
            MonitorState? state = ProfileManager.FindStateForMonitor(profile.MonitorStates, monitor);
            if (state == null)
            {
                monitor.ShowPreview = false;
                monitor.PreviewEnablementDiffers = false;
                continue;
            }

            monitor.PreviewBrightness = state.Brightness;
            monitor.PreviewEnablementDiffers = state.IsSliderEnabled != (monitor.SliderState != SliderState.Disabled);
            monitor.ShowPreview = true;
        }

        RefreshProfilePreviewVisuals();
    }

    private void ClearProfilePreview()
    {
        if (_previewedProfileIndex < 0) return;
        _previewedProfileIndex = -1;
        MasterMonitor.ShowPreview = false;
        MasterMonitor.PreviewEnablementDiffers = false;
        foreach (MonitorInfo monitor in Monitors)
        {
            monitor.ShowPreview = false;
            monitor.PreviewEnablementDiffers = false;
        }

        RefreshProfilePreviewVisuals();
    }

    private void AttachProfilePreviewHandlers(Border button, int profileIndex, UIResourceScope resources)
    {
        EventHandler<PointerEventArgs> entered = (_, _) => ShowProfilePreview(profileIndex);
        EventHandler<PointerEventArgs> exited = (_, _) => ClearProfilePreviewFromButton(profileIndex, button);
        button.PointerEntered += entered;
        resources.Add(() => button.PointerEntered -= entered);
        button.PointerExited += exited;
        resources.Add(() => button.PointerExited -= exited);
    }

    private void ClearProfilePreviewFromButton(int profileIndex, Border button)
    {
        if (_previewedProfileIndex != profileIndex) return;
        if (button.IsPointerOver) return;

        ClearProfilePreview();
    }

    private void RefreshProfilePreviewVisuals()
        => RefreshSliderRowVisuals();

    private void RefreshOrRebuildForMonitorChange(string? propertyName)
    {
        if (propertyName == nameof(MonitorInfo.Brightness))
        {
            RefreshSliderRowVisuals();
            return;
        }

        if (propertyName == nameof(MonitorInfo.SliderState) && IsAnySliderGestureActive())
        {
            _deferredSliderGestureRebuild = true;
            RefreshSliderRowVisuals();
            return;
        }

        RebuildVisual();
    }

    private bool IsAnySliderGestureActive() =>
        MasterMonitor.IsDragging
        || NightLightMonitor.IsDragging
        || Monitors.Any(monitor => monitor.IsDragging);

    private void RefreshSliderRowVisuals()
    {
        if (_profilePreviewRows.Count == 0) return;

        bool isLight = BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings);
        SettingsPalette settingsPalette = CreateSettingsPalette(_theme, _settings, isLight);
        FlyoutControlPalette palette = CreateFlyoutPalette(_theme, _settings, settingsPalette, isLight);

        foreach ((MonitorInfo monitor, ProfilePreviewRowVisuals visuals) in _profilePreviewRows)
        {
            bool curveDrivenWithTarget = monitor is
                { SliderState: SliderState.CurveActive, HasCurveTargetBrightness: true };

            if (!monitor.IsDragging)
                visuals.Slider.Value = Math.Clamp(monitor.Brightness, 0, 100);
            visuals.Slider.ProgressColor = monitor.IsNightLight
                ? _theme.ResolveEnvironmentalNightLightCurve(_settings, isLight)
                : palette.SliderProgress;
            visuals.Slider.IndicatorColor = monitor.IsNightLight
                ? _theme.ResolveEnvironmentalNightLightCurve(_settings, isLight)
                : _theme.ResolveEnvironmentalBrightnessCurve(_settings, isLight);
            visuals.Slider.ProgressValueOverride = curveDrivenWithTarget ? monitor.CurveTargetBrightness : null;
            visuals.Slider.ProgressOverrideColor = curveDrivenWithTarget ? palette.SliderProgress : null;
            visuals.Slider.PreviewValue = monitor.ShowPreview ? monitor.PreviewBrightness : null;
            visuals.Slider.IndicatorValue = ShouldShowCurveIndicator(monitor) ? monitor.CurveTargetBrightness : null;
            visuals.Slider.IndicatorOpacity = monitor.IsCurveSleeping ? 0.45 : 1.0;
            visuals.Slider.ThumbOpacity = curveDrivenWithTarget && IsCurveAbsoluteMode ? 0.4 : 1.0;
            visuals.Slider.IsEnabled = CanEditSlider(monitor);
            visuals.Value.Text = ValueText(monitor);
            visuals.Row.Opacity = RowOpacity(monitor);
            ApplyCurveModeButtonVisual(monitor, visuals.CurveModeButton);
        }
    }

    private double ComputeMasterPreviewForProfile(BrightnessProfile profile)
    {
        List<int> pool = [];
        foreach (MonitorInfo monitor in Monitors)
        {
            if (!monitor.IsHardwareFunctional) continue;
            MonitorState? state = ProfileManager.FindStateForMonitor(profile.MonitorStates, monitor);
            pool.Add(state?.Brightness ?? (int)Math.Round(monitor.Brightness));
        }

        if (pool.Count == 0) return MasterMonitor.Brightness;

        return profile.MasterSliderMode switch
        {
            MasterSliderMode.Lowest => pool.Min(),
            MasterSliderMode.Highest => pool.Max(),
            _ => pool.Average()
        };
    }

    private void ApplyMasterToEnabledMonitors()
    {
        bool suspendForCurve = IsBrightnessCurveEnabled
                               && (_isInCurveDisabledPeriod || MasterMonitor.IsCurveReleased);
        IDisposable? hardwareWriteSuspension = suspendForCurve ? _monitorService.SuspendHardwareWrites() : null;
        bool wasSuppressingPropagation = _suppressPropagation;
        _suppressPropagation = true;
        double masterBrightness = MasterMonitor.Brightness;
        try
        {
            foreach (MonitorInfo monitor in Monitors)
            {
                if (!monitor.IsParticipatingInMaster) continue;
                double unclamped = masterBrightness + monitor.Offset;
                double clamped = BrightnessSliderMath.ClampPercent(unclamped);
                monitor.Brightness = clamped;
                monitor.VirtualBrightness = unclamped;
                UpdateVisibleMonitorSliderValue(monitor, clamped);
                if (suspendForCurve)
                    _monitorService.EnqueueDirectBrightness(monitor, monitor.RoundedBrightness);
            }
        }
        finally
        {
            hardwareWriteSuspension?.Dispose();
            _suppressPropagation = wasSuppressingPropagation;
        }
    }

    private void UpdateVisibleMonitorSliderValue(MonitorInfo monitor, double value)
    {
        if (!_profilePreviewRows.TryGetValue(monitor, out ProfilePreviewRowVisuals? visuals)) return;

        double clamped = Math.Clamp(value, 0, 100);
        if (Math.Abs(visuals.Slider.Value - clamped) < 0.001) return;
        visuals.Slider.Value = clamped;
    }

    private void UpdateMasterFromEnabledIndividuals()
    {
        double next = ComputeMasterFromEnabledIndividuals();
        bool wasSuppressingPropagation = _suppressPropagation;
        _suppressPropagation = true;
        try
        {
            MasterMonitor.Brightness = next;
            UpdateVisibleMonitorSliderValue(MasterMonitor, next);
        }
        finally
        {
            _suppressPropagation = wasSuppressingPropagation;
        }
    }

    private double ComputeMasterFromEnabledIndividuals() =>
        BrightnessSliderMath.ComputeMasterPercent(Monitors, CurrentMasterSliderMode, MasterMonitor.Brightness);

    private void CaptureOffsetsFromMaster()
    {
        bool preserve = _settings?.PreserveMasterSliderOffsets == true;
        foreach (MonitorInfo monitor in Monitors)
        {
            double source = preserve ? monitor.VirtualBrightness : monitor.LastUserBrightness;
            monitor.Offset = source - MasterMonitor.LastUserBrightness;
        }
    }

    private void InitializeOffsetFromMaster(MonitorInfo monitor)
    {
        bool preserve = _settings?.PreserveMasterSliderOffsets == true;
        double source = preserve ? monitor.VirtualBrightness : monitor.LastUserBrightness;
        monitor.Offset = source - MasterMonitor.LastUserBrightness;
    }

    private MasterSliderMode CurrentMasterSliderMode =>
        _settings?.MasterSliderMode ?? MasterSliderMode.Average;

    private void SyncSettingsToSelectedProfileMode()
    {
        if (_settings == null) return;
        int index = _profileManager.SelectedIndex;
        if (index < 0 || index >= _profileManager.Profiles.Profiles.Count) return;

        MasterSliderMode profileMode = _profileManager.Profiles.Profiles[index].MasterSliderMode;
        if (_settings.MasterSliderMode == profileMode) return;

        _settings.MasterSliderMode = profileMode;
        _settings.Save();
    }

    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (!IsWindowAlive) return;
        UpdateMasterFromEnabledIndividuals();
        int providerStrength = NightLightProvider.IsSupported() ? NightLightProvider.GetStrength() : 0;
        int displayValue = FlipIfNightLightInverted(providerStrength);
        if (NightLightMonitor.RoundedBrightness != displayValue) NightLightMonitor.Brightness = displayValue;
        _isNightLightActive = NightLightProvider.IsSupported() && NightLightProvider.IsEnabled();

        if (_isUndocked && _settings?.AllowFlyoutUndock == false) Redock();
        UpdateAllCurveStopwatchVisibility(saveIfDisabled: true);
        _curveService.Start();
        _curveService.Evaluate();
        QueueRebuildVisual();
    });

    private void OnMonitorIconClick(MonitorInfo monitor)
    {
        if (monitor.IsNightLight)
        {
            ToggleNightLightState();
            return;
        }

        bool isWarningState = monitor is { IsMaster: false, WasEverDDCCapable: true, SupportsPowerControl: true }
                              && (monitor.IsFailed || monitor.IsReadDegraded);
        if (isWarningState && IsControlDown())
        {
            if (_settings is { HasAcknowledgedHardPowerOffWarning: false })
            {
                ShowConfirmOverlay(
                    L("Flyout_HardPowerOff_Title", "Power off display"),
                    L("Flyout_HardPowerOff_WarningText",
                        "This sends a hard power-off command to the display. Use it only when DDC/CI recovery is needed."),
                    okText: L("Flyout_HardPowerOff_Confirm", "Power off"),
                    cancelText: L("Flyout_HardPowerOff_Abort", "Cancel"),
                    onOK: () =>
                    {
                        CancelConfirmOverlay();
                        if (_settings != null)
                        {
                            _settings.HasAcknowledgedHardPowerOffWarning = true;
                            _settings.Save();
                        }

                        RunHardPowerOff(monitor);
                    });
                return;
            }

            RunHardPowerOff(monitor);
            return;
        }

        if (monitor.IsMaster)
        {
            if (IsControlDown()) SyncAllToHighestIndividual();
            else SyncAllIndividualsToMaster();
            return;
        }

        SliderState previous = monitor.SliderState;
        monitor.SliderState = previous == SliderState.Disabled
            ? SliderStateMachine.OnUserToggleOn(previous, IsBrightnessCurveEnabled, _isInCurveDisabledPeriod)
            : SliderStateMachine.OnUserToggleOff(previous);

        if (previous == SliderState.Disabled && monitor.IsCurveDriven)
            _curveService.Evaluate(immediateHardware: true);
    }

    private void ToggleNightLightState()
    {
        if (!NightLightProvider.IsSupported()) return;

        bool wasEnabled = NightLightProvider.IsEnabled();
        int? enableStrength = wasEnabled ? null : _curveService.GetNightLightCurveStrengthForEnable();
        NightLightProvider.SetEnabled(
            !wasEnabled,
            enableStrength,
            persistEnableStrengthAsLastUserValue: !enableStrength.HasValue);
        _isNightLightActive = NightLightProvider.IsEnabled();
        OnPropertyChanged(nameof(IsNightLightActive));
        if (_isNightLightActive && enableStrength.HasValue)
            _curveService.Evaluate(immediateHardware: true);
        RebuildVisual();
    }

    private void RunHardPowerOff(MonitorInfo monitor)
    {
        if (!IsWindowAlive) return;
        string EDIDSerial = monitor.EDIDSerial;
        string? lastDDCError = monitor.LastDDCError;
        MonitorService monitorService = _monitorService;
        WeakReference<BrightnessFlyoutWindow> windowReference = new(this);
        CancellationToken cancellationToken = WindowResources.CancellationToken;
        ShowConfirmOverlay(
            L("Flyout_HardPowerOff_Title", "Power off display"),
            L("Flyout_HardPowerOff_InProgress", "Sending hard power-off command..."),
            okText: L("Common_OK", "OK"),
            cancelText: null,
            onOK: CancelConfirmOverlay);

        _ = Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested) return;
            bool ok;
            string? error;
            try
            {
                ok = monitorService.TryHardPowerOffByEDIDSerial(EDIDSerial, out error);
            }
            catch (Exception ex)
            {
                WPFLog.Log($"BrightnessFlyoutWindow.RunHardPowerOff: {ex.Message}");
                ok = false;
                error = ex.Message;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (cancellationToken.IsCancellationRequested ||
                    !windowReference.TryGetTarget(out BrightnessFlyoutWindow? window) ||
                    !window.IsWindowAlive)
                    return;
                string message = ok
                    ? L("Flyout_HardPowerOff_Success", "The hard power-off command was sent.")
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        L("Flyout_HardPowerOff_FailedFormat", "Hard power-off failed: {0}"),
                        !string.IsNullOrWhiteSpace(error)
                            ? error
                            : lastDDCError ?? L("Flyout_HardPowerOff_NoResponseDetail",
                                "No response from the display."));
                window.ShowConfirmOverlay(
                    L("Flyout_HardPowerOff_Title", "Power off display"),
                    message,
                    okText: L("Common_OK", "OK"),
                    cancelText: null,
                    onOK: window.CancelConfirmOverlay);
            });
        });
    }

    private void ToggleCurveForRow(MonitorInfo monitor)
    {
        if (monitor.IsMaster) IsBrightnessCurveEnabled = !IsBrightnessCurveEnabled;
        else if (monitor.IsNightLight) IsNightLightCurveEnabled = !IsNightLightCurveEnabled;
    }

    private void ToggleCurveModeForRow(MonitorInfo monitor)
    {
        if (!ShouldShowCurveModeButton(RowCurveEnabled(monitor), monitor.SliderState)) return;

        switch (monitor.SliderState)
        {
            case SliderState.CurveReleased:
                ReengageCurveReleasedMonitor(monitor);
                if (monitor.IsMaster) ReengageIndividualBrightnessCurveOverridesFromMaster();
                UpdateCurveStopwatchVisibility(monitor);
                _curveService.Evaluate(immediateHardware: true);
                RebuildVisual();
                break;
            case SliderState.CurveActive:
            case SliderState.CurveSleeping:
                ReleaseCurveControlForManualOverride(monitor, replayCurrentSliderValue: true);
                break;
        }
    }

    private void ToggleEnvironmentalCurves()
    {
        bool target = !(IsBrightnessCurveEnabled || IsNightLightCurveEnabled);
        IsBrightnessCurveEnabled = target;
        IsNightLightCurveEnabled = target;
    }

    private void PowerOffFooterTargets()
    {
        bool onlyEnabled = _settings?.FooterPowerButtonOnlyEnabledMonitors ?? false;
        foreach (MonitorInfo monitor in Monitors)
        {
            if (onlyEnabled && !monitor.IsParticipatingInMaster) continue;
            if (!monitor.SupportsPowerControl) continue;
            _ = _monitorService.SetPowerStateAsync(monitor, false);
        }
    }

    private static void OpenDisplaySettings()
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:display",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessFlyoutWindow.OpenDisplaySettings: {ex.Message}");
        }
    }

    private void DisengageCurveForUserAdjustment(MonitorInfo monitor, bool replayCurrentSliderValue = true)
    {
        if (_isInCurveDisabledPeriod) return;
        if (!IsCurveAbsoluteMode) return;

        ReleaseCurveControlForManualOverride(monitor, replayCurrentSliderValue);
    }

    private void ReleaseCurveControlForManualOverride(MonitorInfo monitor, bool replayCurrentSliderValue)
    {
        if (monitor.IsMaster && IsBrightnessCurveEnabled || monitor.IsNightLight && IsNightLightCurveEnabled)
        {
            SliderState previous = monitor.SliderState;
            monitor.SliderState = SliderStateMachine.OnUserRelease(monitor.SliderState);
            UpdateCurveStopwatchVisibility(monitor);
            _curveService.Evaluate();
            if (monitor.SliderState == SliderState.CurveReleased && previous != SliderState.CurveReleased)
                HandleManualCurveOverrideRelease(monitor, replayCurrentSliderValue);
            return;
        }

        if (monitor is { IsMaster: false, IsNightLight: false } && IsBrightnessCurveEnabled)
        {
            SliderState previous = monitor.SliderState;
            monitor.SliderState = SliderStateMachine.OnUserRelease(monitor.SliderState);
            UpdateCurveStopwatchVisibility(monitor);
            if (monitor.SliderState == SliderState.CurveReleased && previous != SliderState.CurveReleased)
                HandleManualCurveOverrideRelease(monitor, replayCurrentSliderValue);
        }
    }

    private void HandleManualCurveOverrideRelease(MonitorInfo monitor, bool replayCurrentSliderValue)
    {
        if (replayCurrentSliderValue)
        {
            _deferredManualCurveOverrideResync.Remove(monitor);
            ResyncManualCurveOverrideToSlider(monitor);
            return;
        }

        _deferredManualCurveOverrideResync.Add(monitor);
    }

    private void FlushDeferredManualCurveOverrideResync(MonitorInfo monitor)
    {
        if (!_deferredManualCurveOverrideResync.Remove(monitor)) return;
        if (!monitor.IsCurveReleased) return;

        ResyncManualCurveOverrideToSlider(monitor);
    }

    private void ResyncManualCurveOverrideToSlider(MonitorInfo monitor)
    {
        if (monitor.IsMaster)
            ResyncBrightnessHardwareToSliders();
        else if (monitor.IsNightLight)
            ResyncNightLightHardwareToSlider();
        else
            _monitorService.EnqueueDirectBrightness(monitor, monitor.RoundedBrightness);

        BrightnessUpdated?.Invoke();
    }

    private void OnCurveToggleStateChanged()
    {
        if (IsBrightnessCurveEnabled) _curveService.EngageBrightnessCurveStates();
        if (IsNightLightCurveEnabled) _curveService.EngageNightLightCurveStates();
        if (!IsBrightnessCurveEnabled) _curveService.DisengageBrightnessCurveStates();
        if (!IsNightLightCurveEnabled) _curveService.DisengageNightLightCurveStates();
        if (IsBrightnessCurveEnabled) CaptureOffsetsFromMaster();
        UpdateAllCurveStopwatchVisibility(saveIfDisabled: true);
        if (_previewDateHardwareActive)
        {
            if (!IsBrightnessCurveEnabled && !IsNightLightCurveEnabled)
                ClearPreviewDateCurve();
            else
                ApplyPreviewDateCurveAtCurrentTime();
            return;
        }

        _curveService.Start();
        _curveService.Evaluate(immediateHardware: IsBrightnessCurveEnabled || IsNightLightCurveEnabled);
    }

    private void ResyncBrightnessHardwareToSliders()
    {
        foreach (MonitorInfo monitor in Monitors)
        {
            if (!monitor.IsParticipatingInMaster) continue;
            _monitorService.EnqueueDirectBrightness(monitor, monitor.RoundedBrightness);
        }
    }

    private void ResyncNightLightHardwareToSlider()
    {
        if (!NightLightProvider.IsSupported() || !NightLightProvider.IsEnabled()) return;
        NightLightProvider.SetStrength(FlipIfNightLightInverted(NightLightMonitor.RoundedBrightness));
    }

    private int FlipIfNightLightInverted(int value) =>
        _settings?.InvertNightLightSlider ?? false ? 100 - value : value;

    private void RunPreviewSweep(EnvironmentalCurve? previewCurve, EnvironmentalCurve? disabledPeriodCurve)
    {
        if (!IsWindowAlive) return;
        _previewSweepCurveOverride = previewCurve ?? _previewDateCurveOverride;
        _previewSweepDisabledPeriodOverride = disabledPeriodCurve ?? _previewDateDisabledPeriodOverride;
        _previewSweepSuspendedCurveService = !_previewDateHardwareActive;
        if (_previewSweepSuspendedCurveService)
            _curveService.Suspend();
        _previewSweepStartFraction = EnvironmentalCurveSampler.CurrentDayFraction();
        _previewSweepStopwatch = Stopwatch.StartNew();
        int rateMs = Math.Max(TimeConstants.BrightnessUpdateRateMinMs,
            _settings?.BrightnessUpdateRateMs ?? TimeConstants.BrightnessUpdateRateDefaultMs);
        DispatcherTimer previewTimer =
            new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(rateMs) };
        previewTimer.Tick += PreviewSweepHardwareTick;
        _previewSweepTimer = previewTimer;
        long animationGeneration = _previewSweepFrames.Start();
        RaisePreviewSweepStateChanged(true);
        if (!ReferenceEquals(_previewSweepTimer, previewTimer) || !IsWindowAlive) return;
        QueuePreviewSweepAnimationFrame(animationGeneration);
        try
        {
            previewTimer.Start();
            PreviewSweepHardwareTick(null, EventArgs.Empty);
        }
        catch
        {
            if (ReferenceEquals(_previewSweepTimer, previewTimer)) FinishPreviewSweep();
            throw;
        }
    }

    private void QueuePreviewSweepAnimationFrame(long generation)
    {
        if (!IsWindowAlive || _previewSweepStopwatch == null || !_previewSweepFrames.TryQueue(generation)) return;
        RequestAnimationFrame(timestamp => OnPreviewSweepAnimationFrame(timestamp, generation));
    }

    private void OnPreviewSweepAnimationFrame(TimeSpan _, long generation)
    {
        if (!IsWindowAlive || !_previewSweepFrames.TryConsume(generation) || _previewSweepStopwatch == null) return;
        double s = _previewSweepStopwatch.Elapsed.TotalMilliseconds /
                   TimeConstants.BrightnessFlyoutPreviewSweepDurationMs;
        if (s > 1.0) s = 1.0;
        RaisePreviewSweepProgress(WrapSweepFraction(s));
        if (s < 1.0)
            QueuePreviewSweepAnimationFrame(generation);
    }

    private void PreviewSweepHardwareTick(object? sender, EventArgs e)
    {
        // A stopped timer can still have a queued tick; ignore ticks from a superseded sweep
        if (sender != null && !ReferenceEquals(sender, _previewSweepTimer)) return;
        if (!IsWindowAlive || _previewSweepStopwatch == null) return;
        double s = _previewSweepStopwatch.Elapsed.TotalMilliseconds /
                   TimeConstants.BrightnessFlyoutPreviewSweepDurationMs;
        bool finished = s >= 1.0;
        if (finished) s = 1.0;
        double t = WrapSweepFraction(s);
        bool applied = _previewSweepCurveOverride != null && _previewSweepDisabledPeriodOverride != null
            ? _curveService.ApplyAt(t, _previewSweepCurveOverride, _previewSweepDisabledPeriodOverride)
            : _curveService.ApplyAt(t);
        if (!applied)
        {
            FinishPreviewSweep();
            return;
        }

        if (finished) FinishPreviewSweep();
    }

    private double WrapSweepFraction(double s)
    {
        double t = (_previewSweepStartFraction + s) % 1.0;
        if (t < 0.0) t += 1.0;
        return t;
    }

    private void FinishPreviewSweep()
    {
        DispatcherTimer? previewTimer = _previewSweepTimer;
        _previewSweepTimer = null;
        if (previewTimer != null)
        {
            try { previewTimer.Stop(); }
            catch (Exception exception)
            {
                WPFLog.Log($"BrightnessFlyoutWindow preview timer stop failed: {exception.Message}");
            }

            previewTimer.Tick -= PreviewSweepHardwareTick;
        }

        _previewSweepStopwatch = null;
        _previewSweepCurveOverride = null;
        _previewSweepDisabledPeriodOverride = null;
        _previewSweepFrames.Invalidate();
        bool resumeLiveCurveService = _previewSweepSuspendedCurveService;
        _previewSweepSuspendedCurveService = false;
        try
        {
            if (resumeLiveCurveService)
                _curveService.Resume();
            else if (_previewDateHardwareActive)
                ApplyPreviewDateCurveAtCurrentTime();
        }
        catch (Exception exception)
        {
            WPFLog.Log($"BrightnessFlyoutWindow.FinishPreviewSweep restore failed: {exception.Message}");
        }

        RaisePreviewSweepStateChanged(false);
    }

    private void RaisePreviewSweepStateChanged(bool isRunning)
    {
        try { PreviewSweepStateChanged?.Invoke(isRunning); }
        catch (Exception exception)
        {
            WPFLog.Log($"BrightnessFlyoutWindow preview state subscriber failed: {exception.Message}");
        }
    }

    private void RaisePreviewSweepProgress(double progress)
    {
        try { PreviewSweepProgress?.Invoke(progress); }
        catch (Exception exception)
        {
            WPFLog.Log($"BrightnessFlyoutWindow preview progress subscriber failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Applies the active date-preview curve at the current local time.
    /// </summary>
    private void ApplyPreviewDateCurveAtCurrentTime()
    {
        if (_previewDateCurveOverride == null || _previewDateDisabledPeriodOverride == null) return;

        double t = EnvironmentalCurveSampler.CurrentDayFraction();
        if (!_curveService.ApplyAt(t, _previewDateCurveOverride, _previewDateDisabledPeriodOverride))
            ClearPreviewDateCurve();
    }

    private void BuildProfileButtonItems()
    {
        ProfileButtons.Clear();
        int buttonCount = Math.Max(1, _theme.ProfileButtons.ButtonCount);
        int selectedIndex = _profileManager.SelectedIndex;
        for (int i = 0; i < buttonCount; i++)
        {
            ProfileButtons.Add(new ProfileButtonItem
            {
                Index = i,
                Glyph = _theme.ProfileButtons.GetGlyph(i, _profileManager.GetCustomGlyph(i)),
                IsSelected = i == selectedIndex
            });
        }
    }

    private const string MasterCurveStopwatchKey = "master";
    private const string NightLightCurveStopwatchKey = "nightlight";

    private static string CurveStopwatchKeyFor(MonitorInfo monitor)
    {
        if (monitor.IsMaster) return MasterCurveStopwatchKey;
        if (monitor.IsNightLight) return NightLightCurveStopwatchKey;
        string key = !string.IsNullOrWhiteSpace(monitor.EDIDKey) ? monitor.EDIDKey : monitor.ID;
        return $"monitor:{key}";
    }

    private CurveStopwatchEntry? FindCurveStopwatchEntry(MonitorInfo monitor)
    {
        if (_settings == null) return null;
        string key = CurveStopwatchKeyFor(monitor);
        return _settings.CurveStopwatches.FirstOrDefault(e =>
            string.Equals(e.SliderKey, key, StringComparison.Ordinal));
    }

    private CurveStopwatchEntry? GetOrCreateCurveStopwatchEntry(MonitorInfo monitor)
    {
        if (_settings == null) return null;
        string key = CurveStopwatchKeyFor(monitor);
        CurveStopwatchEntry? entry =
            _settings.CurveStopwatches.FirstOrDefault(e => string.Equals(e.SliderKey, key, StringComparison.Ordinal));
        if (entry != null) return entry;

        entry = new CurveStopwatchEntry { SliderKey = key, Minutes = TimeConstants.CurveStopwatchDefaultMinutes };
        _settings.CurveStopwatches.Add(entry);
        return entry;
    }

    private void RestoreCurveStopwatchesFromSettings()
    {
        RestoreCurveStopwatchForMonitor(MasterMonitor, saveExpired: true);
        foreach (MonitorInfo monitor in Monitors)
            RestoreCurveStopwatchForMonitor(monitor, saveExpired: true);
        RestoreCurveStopwatchForMonitor(NightLightMonitor, saveExpired: true);

        UpdateAllCurveStopwatchVisibility(saveIfDisabled: true);
        ResyncCurveStopwatchManualOverridesToSliders();
        _curveService.Evaluate(immediateHardware: true);
        ProcessCurveStopwatchDeadlines();
        StartCurveStopwatchTimerIfNeeded();
    }

    private void ResyncCurveStopwatchManualOverridesToSliders()
    {
        if (MasterMonitor.IsCurveReleased && IsBrightnessCurveEnabled)
            ResyncBrightnessHardwareToSliders();
        else
        {
            foreach (MonitorInfo monitor in Monitors)
            {
                if (monitor.SliderState == SliderState.CurveReleased)
                    _monitorService.EnqueueDirectBrightness(monitor, monitor.RoundedBrightness);
            }
        }

        if (NightLightMonitor.IsCurveReleased && IsNightLightCurveEnabled)
            ResyncNightLightHardwareToSlider();
    }

    private void RestoreCurveStopwatchForMonitor(MonitorInfo monitor, bool saveExpired)
    {
        CurveStopwatchEntry? entry = FindCurveStopwatchEntry(monitor);
        monitor.CurveStopwatchMinutes = Math.Max(1, entry?.Minutes ?? TimeConstants.CurveStopwatchDefaultMinutes);
        if (entry is not { IsEnabled: true })
        {
            monitor.IsCurveStopwatchEnabled = false;
            return;
        }

        if (entry.ReenableAtUtc <= DateTime.UtcNow)
        {
            monitor.IsCurveStopwatchEnabled = false;
            if (saveExpired)
            {
                entry.IsEnabled = false;
                entry.EngagedAtUtc = default;
                entry.ReenableAtUtc = default;
                _settings?.Save();
            }

            return;
        }

        if (IsCurveEnabledForStopwatch(monitor))
        {
            SliderState engaged = SliderStateMachine.OnCurveEngaged(monitor.SliderState, _isInCurveDisabledPeriod);
            monitor.SliderState = SliderStateMachine.OnUserRelease(engaged);
        }

        monitor.CurveStopwatchEngagedAtUtc = entry.EngagedAtUtc;
        monitor.CurveStopwatchReenableAtUtc = entry.ReenableAtUtc;
        monitor.IsCurveStopwatchEnabled = true;
    }

    private bool IsCurveEnabledForStopwatch(MonitorInfo monitor) =>
        monitor.IsNightLight ? IsNightLightCurveEnabled : IsBrightnessCurveEnabled;

    private bool IsManualCurveOverride(MonitorInfo monitor) =>
        IsCurveEnabledForStopwatch(monitor)
        && IsCurveAbsoluteMode
        && monitor.SliderState == SliderState.CurveReleased;

    private void UpdateAllCurveStopwatchVisibility(bool saveIfDisabled)
    {
        UpdateCurveStopwatchVisibility(MasterMonitor, saveIfDisabled);
        foreach (MonitorInfo monitor in Monitors)
            UpdateCurveStopwatchVisibility(monitor, saveIfDisabled);
        UpdateCurveStopwatchVisibility(NightLightMonitor, saveIfDisabled);
    }

    private void UpdateCurveStopwatchVisibility(MonitorInfo monitor, bool saveIfDisabled = true)
    {
        bool visible = IsManualCurveOverride(monitor);
        monitor.IsCurveStopwatchVisible = visible;
        if (!visible) _curveStopwatchReengageBlockedByMaster.Remove(CurveStopwatchKeyFor(monitor));

        if (visible || !monitor.IsCurveStopwatchEnabled) return;

        monitor.IsCurveStopwatchEnabled = false;
        monitor.CurveStopwatchEngagedAtUtc = default;
        monitor.CurveStopwatchReenableAtUtc = default;
        if (saveIfDisabled) PersistCurveStopwatch(monitor, enabled: false);
        StartCurveStopwatchTimerIfNeeded();
    }

    private void PersistCurveStopwatch(MonitorInfo monitor, bool enabled)
    {
        CurveStopwatchEntry? entry = GetOrCreateCurveStopwatchEntry(monitor);
        if (entry == null) return;
        entry.Minutes = monitor.CurveStopwatchMinutes;
        entry.IsEnabled = enabled;
        entry.EngagedAtUtc = enabled ? monitor.CurveStopwatchEngagedAtUtc : default;
        entry.ReenableAtUtc = enabled ? monitor.CurveStopwatchReenableAtUtc : default;
        _settings?.Save();
    }

    private void StartCurveStopwatchTimerIfNeeded()
    {
        bool anyEnabled = AllItems.Any(m => m.IsCurveStopwatchEnabled);
        if (!anyEnabled)
        {
            StopCurveStopwatchTimer();
            return;
        }

        if (_curveStopwatchTimer == null)
        {
            DispatcherTimer curveStopwatchTimer =
                new(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(TimeConstants.CurveStopwatchRefreshIntervalMs)
                };
            curveStopwatchTimer.Tick += OnCurveStopwatchTimerTick;
            _curveStopwatchTimer = curveStopwatchTimer;
        }

        if (_curveStopwatchTimer.IsEnabled) return;
        try { _curveStopwatchTimer.Start(); }
        catch
        {
            StopCurveStopwatchTimer();
            throw;
        }
    }

    private void StopCurveStopwatchTimer()
    {
        DispatcherTimer? curveStopwatchTimer = _curveStopwatchTimer;
        _curveStopwatchTimer = null;
        if (curveStopwatchTimer == null) return;
        try { curveStopwatchTimer.Stop(); }
        catch (Exception exception)
        {
            WPFLog.Log($"BrightnessFlyoutWindow curve stopwatch timer stop failed: {exception.Message}");
        }

        curveStopwatchTimer.Tick -= OnCurveStopwatchTimerTick;
    }

    private void OnCurveStopwatchTimerTick(object? sender, EventArgs e)
    {
        // Do not let a queued tick from a stopped timer act on its replacement
        if (!ReferenceEquals(sender, _curveStopwatchTimer)) return;
        ProcessCurveStopwatchDeadlines();
    }

    private void ProcessCurveStopwatchDeadlines()
    {
        bool anyEnabled = false;
        foreach (MonitorInfo monitor in AllItems)
        {
            if (!monitor.IsCurveStopwatchEnabled) continue;
            if (monitor.CurveStopwatchReenableAtUtc <= DateTime.UtcNow)
            {
                ExpireCurveStopwatch(monitor);
                continue;
            }

            monitor.RefreshCurveStopwatchToolTip();
            anyEnabled = true;
        }

        if (!anyEnabled) StopCurveStopwatchTimer();
    }

    private bool IsMasterStopwatchBlockingReengage() =>
        MasterMonitor is { IsCurveStopwatchEnabled: true, SliderState: SliderState.CurveReleased };

    private void ExpireCurveStopwatch(MonitorInfo monitor)
    {
        monitor.IsCurveStopwatchEnabled = false;
        monitor.CurveStopwatchEngagedAtUtc = default;
        monitor.CurveStopwatchReenableAtUtc = default;
        PersistCurveStopwatch(monitor, enabled: false);

        if (monitor.IsMaster)
        {
            ReengageCurveReleasedMonitor(monitor);
            ReengageIndividualBrightnessCurveOverridesFromMaster();
        }
        else if (monitor.IsNightLight || !IsMasterStopwatchBlockingReengage())
            ReengageCurveReleasedMonitor(monitor);
        else
            _curveStopwatchReengageBlockedByMaster.Add(CurveStopwatchKeyFor(monitor));

        UpdateCurveStopwatchVisibility(monitor, saveIfDisabled: false);
        _curveService.Evaluate(immediateHardware: true);
    }

    private void ReengageIndividualBrightnessCurveOverridesFromMaster()
    {
        CaptureOffsetsFromMaster();
        foreach (MonitorInfo monitor in Monitors)
        {
            if (monitor.SliderState != SliderState.CurveReleased) continue;
            ReengageCurveReleasedMonitor(monitor);
            UpdateCurveStopwatchVisibility(monitor);
        }

        _curveStopwatchReengageBlockedByMaster.Clear();
    }

    /// <summary>
    /// Re-enters brightness curve control after the curve crosses the released master value.
    /// Called by the curve evaluator before it writes the current tick target.
    /// </summary>
    private void AutoEngageBrightnessCurveManualOverride()
    {
        if (!MasterMonitor.IsCurveReleased) return;

        ReengageCurveReleasedMonitor(MasterMonitor);
        ReengageIndividualBrightnessCurveOverridesFromMaster();
        UpdateCurveStopwatchVisibility(MasterMonitor);
        BrightnessUpdated?.Invoke();
        QueueRebuildVisual();
    }

    private void ReengageCurveReleasedMonitor(MonitorInfo monitor)
    {
        SliderState next = SliderStateMachine.OnUserReengage(monitor.SliderState, _isInCurveDisabledPeriod);
        if (next is SliderState.CurveActive or SliderState.CurveSleeping
            && monitor.SliderState is not (SliderState.CurveActive or SliderState.CurveSleeping))
            monitor.SeedCurveTargetBrightnessFromSlider();
        monitor.SliderState = next;
    }

    private void ToggleCurveStopwatch(MonitorInfo monitor)
    {
        if (monitor.IsCurveStopwatchEnabled)
        {
            monitor.IsCurveStopwatchEnabled = false;
            monitor.CurveStopwatchEngagedAtUtc = default;
            monitor.CurveStopwatchReenableAtUtc = default;
            _curveStopwatchReengageBlockedByMaster.Remove(CurveStopwatchKeyFor(monitor));
            PersistCurveStopwatch(monitor, enabled: false);
            StartCurveStopwatchTimerIfNeeded();
            RebuildVisual();
            return;
        }

        DateTime now = DateTime.UtcNow;
        int minutes = Math.Max(1, monitor.CurveStopwatchMinutes);
        monitor.CurveStopwatchEngagedAtUtc = now;
        monitor.CurveStopwatchReenableAtUtc = now.AddMinutes(minutes);
        monitor.IsCurveStopwatchEnabled = true;
        _curveStopwatchReengageBlockedByMaster.Remove(CurveStopwatchKeyFor(monitor));
        PersistCurveStopwatch(monitor, enabled: true);
        StartCurveStopwatchTimerIfNeeded();
        RebuildVisual();
    }

    private void SetCurveStopwatchMinutes(MonitorInfo monitor, int value)
    {
        if (monitor is { IsCurveStopwatchVisible: false, IsCurveStopwatchEnabled: false }) return;

        monitor.CurveStopwatchMinutes = Math.Max(1, value);
        if (monitor.IsCurveStopwatchEnabled)
        {
            DateTime engagedAt = monitor.CurveStopwatchEngagedAtUtc == default
                ? DateTime.UtcNow
                : monitor.CurveStopwatchEngagedAtUtc;
            monitor.CurveStopwatchEngagedAtUtc = engagedAt;
            monitor.CurveStopwatchReenableAtUtc = engagedAt.AddMinutes(monitor.CurveStopwatchMinutes);
            monitor.RefreshCurveStopwatchToolTip();
            PersistCurveStopwatch(monitor, enabled: true);
            ProcessCurveStopwatchDeadlines();
            RebuildVisual();
            return;
        }

        PersistCurveStopwatch(monitor, enabled: false);
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        FlyoutVisualState? visualState = _visualState;
        if (visualState == null || !ReferenceEquals(sender, visualState.RootCard)) return;
        if (!_isUndocked) return;
        if (_isDraggingWindow || visualState.RootCapturedPointer != null) return;
        if (_undockButtonController?.IsPointerCaptured == true) return;
        if (TrayAppDotNETFlyoutUI.IsInteractiveDragSource(e.Source as Visual)) return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        if (sender is not Control control) return;

        (PixelPoint dockedPosition, int snapTolerance) = CaptureDockedPosition();
        PixelPoint pointer = control.PointToScreen(e.GetPosition(control));
        _dragHelper.BeginDrag(pointer, Position, dockedPosition, snapTolerance);
        visualState.RootCapturedPointer = e.Pointer;
        _isDraggingWindow = true;
        try { e.Pointer.Capture(control); }
        catch
        {
            if (ReferenceEquals(visualState.RootCapturedPointer, e.Pointer))
                visualState.RootCapturedPointer = null;
            _isDraggingWindow = false;
            try { e.Pointer.Capture(null); }
            catch (Exception releaseException)
            {
                WPFLog.Log($"BrightnessFlyoutWindow capture rollback failed: {releaseException.Message}");
            }
            throw;
        }
        e.Handled = true;
    }

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
        FlyoutVisualState? visualState = _visualState;
        if (visualState == null || !ReferenceEquals(sender, visualState.RootCard)) return;
        if (!ReferenceEquals(e.Pointer, visualState.RootCapturedPointer)) return;
        if (!_isDraggingWindow || !_isUndocked || _undockButtonController?.IsPointerCaptured == true) return;
        if (sender is not Control control) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            EndRootDrag(e.Pointer, commit: true);
            e.Handled = true;
            return;
        }

        PixelPoint pointer = control.PointToScreen(e.GetPosition(control));
        PixelPoint natural = _dragHelper.ComputeNatural(pointer);
        _dragHelper.ApplyDragPosition(this, natural);
        e.Handled = true;
    }

    private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        FlyoutVisualState? visualState = _visualState;
        if (visualState == null || !ReferenceEquals(sender, visualState.RootCard)) return;
        if (!ReferenceEquals(e.Pointer, visualState.RootCapturedPointer)) return;
        if (!_isDraggingWindow || _undockButtonController?.IsPointerCaptured == true) return;
        EndRootDrag(e.Pointer, commit: true);
        e.Handled = true;
    }

    private void OnRootPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        FlyoutVisualState? visualState = _visualState;
        if (visualState == null || !ReferenceEquals(sender, visualState.RootCard)) return;
        if (!ReferenceEquals(e.Pointer, visualState.RootCapturedPointer)) return;

        visualState.RootCapturedPointer = null;
        if (_isDraggingWindow && _undockButtonController?.IsPointerCaptured != true)
            CommitDragPosition();
        _isDraggingWindow = false;
        FlushPendingRebuildVisual();
    }

    private void EndRootDrag(IPointer pointer, bool commit)
    {
        FlyoutVisualState? visualState = _visualState;
        if (visualState == null || !ReferenceEquals(visualState.RootCapturedPointer, pointer)) return;

        _isDraggingWindow = false;
        visualState.RootCapturedPointer = null;
        try { pointer.Capture(null); }
        catch (Exception exception)
        {
            WPFLog.Log($"BrightnessFlyoutWindow pointer release failed: {exception.Message}");
        }
        if (commit) CommitDragPosition();
        FlushPendingRebuildVisual();
    }

    private (PixelPoint DockedPosition, int SnapTolerance) CaptureDockedPosition() =>
        (ResolveDockedPosition(_lastTrayIcon), ResolveSnapTolerance());

    private void ToggleUndocked()
    {
        if (_isUndocked) Redock();
        else UndockToSavedPosition();
    }

    private void SetUndockedFromDrag()
    {
        _isUndocked = true;
        UpdateUndockButtonVisual();
        OnPropertyChanged(nameof(IsUndocked));
    }

    private void CommitDragPosition()
    {
        if (_dragHelper.IsCurrentlySnapped) Redock();
        else SaveUndockedPosition();
    }

    private void UndockToSavedPosition()
    {
        _isUndocked = true;
        if (_settings != null)
        {
            _settings.FlyoutUndocked = true;
            _settings.Save();
        }

        if (_settings?.FlyoutHasSavedPosition == true)
            Position = new PixelPoint((int)Math.Round(_settings.FlyoutLeft), (int)Math.Round(_settings.FlyoutTop));
        UpdateUndockButtonVisual();
        OnPropertyChanged(nameof(IsUndocked));
    }

    private void SaveUndockedPosition()
    {
        _isUndocked = true;
        if (_settings != null)
        {
            _settings.FlyoutUndocked = true;
            _settings.FlyoutHasSavedPosition = true;
            _settings.FlyoutLeft = Position.X;
            _settings.FlyoutTop = Position.Y;
            _settings.Save();
        }

        UpdateUndockButtonVisual();
        OnPropertyChanged(nameof(IsUndocked));
    }

    private void UpdateUndockButtonVisual() => _undockButtonController?.UpdateVisual();

    private PixelPoint ResolvePosition(TrayAppDotNETShellTrayIcon? trayIcon)
    {
        if (_isUndocked && _settings?.FlyoutHasSavedPosition == true)
        {
            PixelPoint saved = new((int)Math.Round(_settings.FlyoutLeft), (int)Math.Round(_settings.FlyoutTop));
            return ClampWindowPosition(saved, ResolveWorkArea(trayIcon));
        }

        return ResolveDockedPosition(trayIcon);
    }

    private PixelPoint ResolveDockedPosition(TrayAppDotNETShellTrayIcon? trayIcon)
    {
        PixelRect workArea = ResolveWorkArea(trayIcon);
        int width = CurrentPixelWidth();
        int height = CurrentPixelHeight();
        int left = trayIcon?.TryGetIconRect(out PixelRect rect) == true
            ? rect.Center.X - width / 2
            : workArea.Right - width - EdgePadding;
        int top = workArea.Bottom - height - EdgePadding;
        return ClampWindowPosition(new PixelPoint(left, top), workArea);
    }

    private PixelRect ResolveWorkArea(TrayAppDotNETShellTrayIcon? trayIcon)
    {
        PixelPoint anchor = Position;
        if (trayIcon?.TryGetIconRect(out PixelRect iconRect) == true)
            anchor = iconRect.Center;
        return (Screens.ScreenFromPoint(anchor) ?? Screens.Primary)?.WorkingArea
               ?? FallbackWorkArea();
    }

    private PixelPoint ClampWindowPosition(PixelPoint target, PixelRect workArea)
    {
        int width = CurrentPixelWidth();
        int height = CurrentPixelHeight();
        int minLeft = workArea.X + EdgePadding;
        int maxLeft = Math.Max(minLeft, workArea.Right - width - EdgePadding);
        int minTop = workArea.Y + EdgePadding;
        int maxTop = Math.Max(minTop, workArea.Bottom - height - EdgePadding);
        return new PixelPoint(Math.Clamp(target.X, minLeft, maxLeft), Math.Clamp(target.Y, minTop, maxTop));
    }

    private int CurrentPixelWidth() =>
        Math.Max(PixelMinSize, (int)Math.Ceiling(Math.Max(Bounds.Width, Width) * RenderScaling));

    private int CurrentPixelHeight() =>
        Math.Max(PixelMinSize, (int)Math.Ceiling(Math.Max(Bounds.Height, PixelMinSize) * RenderScaling));

    private int ResolveSnapTolerance()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        return Math.Max(PixelMinSize,
            (int)Math.Round(Math.Min(workArea.Width, workArea.Height) * Layout.SnapTolerancePercent));
    }

    private void QueuePositionNearTray()
    {
        if (!IsWindowAlive || !IsVisible || _isDraggingWindow) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsWindowAlive || !IsVisible || _isDraggingWindow) return;
            UpdateLayout();
            ApplyWorkAreaMaxHeight();
            PositionNearTray();
        }, DispatcherPriority.Loaded);
    }

    private void ApplyWorkAreaMaxHeight()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        MaxHeight = Math.Max(Layout.WorkAreaMinHeight, workArea.Height / RenderScaling - EdgePadding * 2);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (_settings?.FlyoutNumberKeysSwitchProfile != true || e.KeyModifiers != KeyModifiers.None) return;
        int index = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            Key.D8 or Key.NumPad8 => 7,
            Key.D9 or Key.NumPad9 => 8,
            _ => -1
        };
        if (index < 0 || index >= ProfileButtons.Count) return;
        SelectProfileApplyingMode(index);
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_isClosed)
        {
            base.OnClosed(e);
            return;
        }

        _isClosed = true;
        try
        {
            // External publishers are the roots that can keep this window alive. Remove them before hardware work.
            RunCloseCleanup(nameof(UIResourceScope.Dispose), _externalResources.Dispose);
            UIContentGeneration? closingGeneration = ActiveContentGeneration;
            RunCloseCleanup(nameof(DisposeContentGeneration), () =>
            {
                try { DisposeContentGeneration(); }
                finally { closingGeneration?.Dispose(); }
            });
            RunCloseCleanup(nameof(StopCurveStopwatchTimer), StopCurveStopwatchTimer);

            BrightnessUpdated = null;
            SettingsRequested = null;
            FlyoutDeactivated = null;
            PreviewSweepStateChanged = null;
            PreviewSweepProgress = null;
            PropertyChanged = null;

            RunCloseCleanup(nameof(CancelPreviewSweep), CancelPreviewSweep);
            RunCloseCleanup(nameof(ClearPreviewDateCurve), ClearPreviewDateCurve);

            if (_settings != null)
            {
                RunCloseCleanup("SaveLastMasterBrightness", () =>
                {
                    _settings.LastMasterBrightness = (int)Math.Round(Math.Clamp(MasterMonitor.Brightness, 0, 100));
                    _settings.Save();
                });
            }

            RunCloseCleanup(nameof(BrightnessFlyoutSession.Dispose), _session.Dispose);
        }
        finally
        {
            base.OnClosed(e);
        }
    }

    private static void RunCloseCleanup(string operation, Action cleanup)
    {
        try { cleanup(); }
        catch (Exception exception)
        {
            WPFLog.Log($"BrightnessFlyoutWindow.OnClosed {operation} failed: {exception.Message}");
        }
    }

    private string RowGlyph(MonitorInfo monitor)
    {
        if (monitor.IsNightLight) return GlyphCatalog.LIGHTBULB.Text;
        if (monitor.IsFailed || monitor.IsReadDegraded) return GlyphCatalog.WARNING.Text;
        if (monitor.IsMaster) return monitor.IconGlyph;
        return _theme.GlyphMonitor;
    }

    private string RowTitle(MonitorInfo monitor)
    {
        if (monitor.IsNightLight && (_settings?.ShowNightLightKelvinLabel ?? false))
        {
            int strength = FlipIfNightLightInverted(monitor.RoundedBrightness);
            string suffix = _settings?.TurnOffNightLightAtZeroStrength == true && strength <= 0
                ? L("NightLight_OffSuffix", "Off")
                : $"{NightLightKelvin.PercentToKelvin(strength).ToString(CultureInfo.InvariantCulture)}K";
            return $"{monitor.Name} {suffix}";
        }

        if ((_settings?.ShowFlyoutMonitorNumberBadge ?? false) && monitor.DisplayNumber > 0)
            return $"{monitor.DisplayNumber}: {monitor.Name}";
        return monitor.Name;
    }

    private static string ValueText(MonitorInfo monitor)
    {
        int value = monitor.EffectiveRoundedBrightness;
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string RowIconTooltip(MonitorInfo monitor)
    {
        if (monitor.IsMaster) return L("Flyout_SyncAllDisplays", "Sync displays");
        if (monitor.IsNightLight) return L("Flyout_ToggleNightLight", "Toggle night light");
        if (monitor.IsFailed || monitor.IsReadDegraded)
            return monitor.LastDDCError ?? L("Flyout_DDCCIWarning", "DDC/CI needs recovery");
        return monitor.IsParticipatingInMaster
            ? L("Flyout_DisableFromMaster", "Exclude from master")
            : L("Flyout_EnableForMaster", "Include in master");
    }

    private string ProfileTooltip(int index)
    {
        string fallback = $"Profile {index + 1}";
        string? name = _profileManager.GetName(index);
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private bool RowCurveEnabled(MonitorInfo monitor) =>
        monitor.IsNightLight ? IsNightLightCurveEnabled : IsBrightnessCurveEnabled;

    internal static bool ShouldShowCurveModeButton(bool isCurveEnabled, SliderState sliderState) =>
        isCurveEnabled
        && sliderState is SliderState.CurveActive or SliderState.CurveSleeping or SliderState.CurveReleased;

    private static Glyph CurveModeGlyph(MonitorInfo monitor) =>
        monitor.IsCurveReleased ? GlyphCatalog.LOCK : GlyphCatalog.UNLOCK;

    private static string CurveModeTooltip(MonitorInfo monitor) =>
        monitor.IsCurveReleased
            ? L("Flyout_CurveMode_ManualTooltip", "Manual override")
            : L("Flyout_CurveMode_CurveTooltip", "Curve control");

    private void ApplyCurveModeButtonVisual(MonitorInfo monitor, Border? button)
    {
        if (button?.Child is not TextBlock glyphText) return;

        Glyph glyph = CurveModeGlyph(monitor);
        GlyphApplicator.ApplyTo(glyphText, glyph);
        glyphText.Opacity = monitor.IsCurveReleased ? 1.0 : Layout.ModeButtonCurveOpacity;
        TrayAppDotNETToolTip.SetTip(button, CurveModeTooltip(monitor));
    }

    private static bool CanEditSlider(MonitorInfo monitor)
    {
        if (monitor.IsNightLight) return NightLightProvider.IsSupported();
        if (monitor.IsMaster) return true;
        return monitor.IsHardwareFunctional && monitor.SliderState != SliderState.Disabled;
    }

    private double RowOpacity(MonitorInfo monitor)
    {
        if (monitor.PreviewEnablementDiffers) return 0.7;
        if (monitor.IsNightLight && !_isNightLightActive) return 0.4;
        if (monitor.IsFailed) return 0.4;
        if (monitor.SliderState == SliderState.Disabled) return 0.4;
        return 1.0;
    }

    private bool ShouldShowCurveIndicator(MonitorInfo monitor) =>
        monitor is { HasCurveTargetBrightness: true, IsCurveDriven: true }
        && (_settings?.ShowEnvironmentalCurvesButton ?? true);

    private static string TimeLeftText(MonitorInfo monitor)
    {
        TimeSpan remaining = monitor.CurveStopwatchReenableAtUtc - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h";
        return $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
    }

    private SliderThumbGlyphOption ResolveSliderThumbOption()
    {
        List<SliderThumbGlyphOption> options =
            _settings?.SliderThumbOptions is { Count: > 0 } list
                ? list
                : SliderThumbGlyphOption.CreateDefaults();
        return options.FirstOrDefault(o => o.Name == _settings?.SliderThumbGlyph) ?? options[0];
    }

    private CornerRadius Rounded(CornerRadius radius) =>
        _settings?.EnableRoundedCorners ?? true ? radius : Layout.ZeroCornerRadius;

    private static SettingsPalette CreateSettingsPalette(BrightnessAppTheme theme, AppSettings? settings, bool isLight)
    {
        Color background = theme.ResolveBackground(settings, isLight);
        Color foreground = theme.ResolveForeground(settings, isLight);
        return new SettingsPalette(
            background,
            foreground,
            theme.Border.For(isLight),
            theme.ButtonHover.For(isLight),
            theme.ButtonPressed.For(isLight),
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

    private static FlyoutControlPalette CreateFlyoutPalette(BrightnessAppTheme theme, AppSettings? settings,
        SettingsPalette sp, bool isLight) =>
        new(
            theme.ResolveForeground(settings, isLight),
            theme.SecondaryForeground.For(isLight),
            theme.Border.For(isLight),
            theme.ButtonHover.For(isLight),
            theme.ButtonPressed.For(isLight),
            theme.ControlBackground.For(isLight),
            theme.CardBackground.For(isLight),
            theme.IconForeground.For(isLight),
            sp.SliderTrack,
            sp.SliderProgress,
            sp.SliderThumb);

    private static bool IsControlDown() =>
        (User32.GetAsyncKeyState(User32.VK_CONTROL) & unchecked((short)0x8000)) != 0;

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed record ProfilePreviewRowVisuals(
    FlyoutSlider Slider,
    Border Row,
    TextBlock Value,
    Border? CurveModeButton);

internal sealed class FlyoutVisualState
{
    public Dictionary<MonitorInfo, ProfilePreviewRowVisuals> ProfilePreviewRows { get; } = [];
    public Border RootCard { get; set; } = null!;
    public Border UndockButton { get; set; } = null!;
    public FlyoutUndockButtonController UndockButtonController { get; set; } = null!;
    public ScrollViewer ScrollViewer { get; set; } = null!;
    public Border ConfirmOverlay { get; set; } = null!;
    public TextBlock ConfirmTitle { get; set; } = null!;
    public TextBlock ConfirmMessage { get; set; } = null!;
    public SettingsButton ConfirmOK { get; set; } = null!;
    public SettingsButton ConfirmCancel { get; set; } = null!;
    public IPointer? RootCapturedPointer { get; set; }
}

public sealed class ProfileButtonItem : INotifyPropertyChanged
{
    public required int Index { get; init; }
    public required string Glyph { get; init; }

    public bool IsSelected
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
