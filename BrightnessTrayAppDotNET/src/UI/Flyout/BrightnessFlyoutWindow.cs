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
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Models;
using TrayAppDotNETCommon.UI.Tray;
using BrightnessAppTheme = BrightnessTrayAppDotNET.Visuals.AppTheme;

namespace BrightnessTrayAppDotNET.UI.Flyout;

public sealed partial class BrightnessFlyoutWindow : FlyoutWindowCommon, INotifyPropertyChanged
{
    private static readonly FontFamily FlyoutFont = new("Segoe UI");

    private readonly BrightnessFlyoutSession _session;
    private readonly FlyoutWindowDragHelper _dragHelper = new();
    private readonly HashSet<string> _curveStopwatchReengageBlockedByMaster = [];
    private readonly Dictionary<MonitorInfo, ProfilePreviewRowVisuals> _profilePreviewRows = [];

    private TrayAppDotNETShellTrayIcon? _lastTrayIcon;
    private Border? _rootCard;
    private Border? _undockButton;
    private FlyoutUndockButtonController? _undockButtonController;
    private ScrollViewer? _scrollViewer;
    private Border? _confirmOverlay;
    private TextBlock? _confirmTitle;
    private TextBlock? _confirmMessage;
    private FlyoutLayout? _layout;
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
    private bool _deferredSliderGestureRebuild;
    private bool _previewSweepAnimationFrameQueued;
    private double _previewSweepStartFraction;
    private int _previewedProfileIndex = -1;

    private ProfileManager _profileManager => _session.ProfileManager;
    private BrightnessAppTheme _theme => _session.Theme;
    private AppSettings? _settings => _session.Settings;
    private MonitorService _monitorService => _session.MonitorService;
    private EnvironmentalCurveService _curveService => _session.CurveService;

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
            AppServices.ProfileManager ?? new ProfileManager(),
            AppServices.Theme ?? BrightnessAppTheme.Default,
            AppServices.MonitorService ?? throw new InvalidOperationException("MonitorService is required."))
    {
    }

    internal BrightnessFlyoutWindow(ProfileManager profileManager, BrightnessAppTheme theme,
        MonitorService monitorService)
    {
        _session = new BrightnessFlyoutSession(
            profileManager,
            theme,
            monitorService,
            AppServices.Settings,
            L("Flyout_MasterRowName", "All displays"),
            L("Flyout_NightLightRowName", "Night light"),
            inDisabled => IsInCurveDisabledPeriod = inDisabled);

        InitializeComponent();
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _profileManager.EnsureProfileCount(Math.Max(1, _theme.ProfileButtons.ButtonCount));
        RestoreInitialProfileState();
        if (Monitors.Count > 0) MasterMonitor.Brightness = ComputeMasterFromEnabledIndividuals();
        CaptureOffsetsFromMaster();

        MasterMonitor.PropertyChanged += OnMonitorPropertyChanged;
        foreach (MonitorInfo monitor in Monitors)
            monitor.PropertyChanged += OnMonitorPropertyChanged;
        NightLightMonitor.PropertyChanged += OnNightLightPropertyChanged;
        Monitors.CollectionChanged += OnMonitorsCollectionChanged;
        _monitorService.MonitorsAcquired += OnMonitorsAcquired;
        _monitorService.MonitorsRefreshed += OnInitialMonitorEnrollmentRefreshed;
        _profileManager.SelectedProfileChanged += OnSelectedProfileChanged;
        _profileManager.UnsavedChangesStatusChanged += UpdateSaveButtonState;
        _profileManager.ProfilesListChanged += OnProfilesListChanged;
        if (_settings != null) _settings.Changed += OnSettingsChanged;
        if (AppServices.UpdateCheckService != null)
            AppServices.UpdateCheckService.StateChanged += NotifyUpdateStateChanged;

        BuildProfileButtonItems();

        _isUndocked = _settings is
        {
            FlyoutUndocked: true,
            FlyoutHasSavedPosition: true,
            AllowFlyoutUndock: true,
            RestoreFlyoutUndockedOnStartup: true,
        };

        CheckAndUpdateUnsavedChanges();
        if (_isBrightnessCurveEnabled || _isNightLightCurveEnabled) OnCurveToggleStateChanged();
        RestoreCurveStopwatchesFromSettings();

        KeyDown += OnWindowKeyDown;
        Closed += OnClosed;
        InitializeComponentState();
        NotifyUpdateStateChanged();
    }

    private void InitializeComponentState()
    {
        _layout = FlyoutLayout.From(this);

        if (_session != null)
            RebuildVisual();
    }

    private FlyoutLayout Layout =>
        _layout ?? throw new InvalidOperationException("Brightness flyout layout resources have not been loaded.");

    public new event PropertyChangedEventHandler? PropertyChanged;
    public event Action? BrightnessUpdated;
    public event Action? SettingsRequested;
    public event Action? FlyoutDeactivated;
    public event Action<bool>? PreviewSweepStateChanged;
    public event Action<double>? PreviewSweepProgress;

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

    protected override bool HasOpenChildWindow => false;

    protected override bool ShouldAutoHideWhenDeactivated => !_isUndocked;

    protected override void HideFlyout()
    {
        ClearProfilePreview();
        Hide();
        FlyoutDeactivated?.Invoke();
    }

    public void ShowAt(TrayAppDotNETShellTrayIcon trayIcon, bool activate = true)
    {
        _lastTrayIcon = trayIcon;
        ApplyWorkAreaMaxHeight();
        RebuildVisual();
        if (!IsVisible)
        {
            Opacity = 0;
            Position = OffscreenPosition();
            Show();
        }

        Dispatcher.UIThread.Post(() =>
        {
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
        base.Show();
        AppServices.DisplayEventManager?.RunSingleGatedScan();
        Dispatcher.UIThread.Post(() =>
        {
            UpdateLayout();
            ApplyWorkAreaMaxHeight();
            PositionNearTray();
            Activate();
        }, DispatcherPriority.Loaded);
    }

    public void ShowWithoutActivating()
    {
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
        Dispatcher.UIThread.Post(() =>
        {
            UpdateLayout();
            ApplyWorkAreaMaxHeight();
            PositionNearTray();
        }, DispatcherPriority.Loaded);
    }

    public bool HasFocus() => IsActive;

    public new void Hide()
    {
        CancelPreviewSweep();
        CancelConfirmOverlay();
        base.Hide();
        NotifyWarmDismissed();
    }

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
        bool toggleOn = _settings?.ShowUpdateButtonInFlyout ?? true;
        bool available = AppServices.UpdateCheckService?.AvailableUpdate != null;
        _isUpdateButtonVisible = toggleOn && available;
        OnPropertyChanged(nameof(IsUpdateButtonVisible));
        RebuildVisual();
    });

    public void RequestUpdatePrompt()
    {
        if (AppServices.UpdateCheckService?.AvailableUpdate == null) return;
        ShowUpdateConfirmation();
    }

    public void SelectProfileByIndex(int index) => SelectProfileApplyingMode(index);

    internal void SyncAllIndividualsToMaster()
    {
        double target = Math.Round(MasterMonitor.Brightness);
        _suppressPropagation = true;
        IDisposable? hardwareWriteSuspension = IsBrightnessCurveEnabled
            ? _monitorService.SuspendHardwareWrites()
            : null;
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
            hardwareWriteSuspension?.Dispose();
            _suppressPropagation = false;
        }

        CaptureOffsetsFromMaster();
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
        IDisposable? hardwareWriteSuspension = IsBrightnessCurveEnabled
            ? _monitorService.SuspendHardwareWrites()
            : null;
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
            hardwareWriteSuspension?.Dispose();
            _suppressPropagation = false;
        }

        CaptureOffsetsFromMaster();
        CheckAndUpdateUnsavedChanges();
        BrightnessUpdated?.Invoke();
        RebuildVisual();
    }

    public void NotifyUserBrightnessAdjustment() => DisengageCurveForUserAdjustment(MasterMonitor);

    public void NotifyUserNightLightAdjustment() => DisengageCurveForUserAdjustment(NightLightMonitor);

    public void RequestCurveReevaluation() => _curveService.RequestEvaluation();

    public void TogglePreviewSweep()
    {
        if (_previewSweepTimer != null)
        {
            CancelPreviewSweep();
            return;
        }

        if (!IsBrightnessCurveEnabled && !IsNightLightCurveEnabled) return;
        RunPreviewSweep();
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

    private void RebuildVisual()
    {
        if (_layout == null) return;

        _profilePreviewRows.Clear();

        bool isLight = BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings);
        SettingsPalette settingsPalette = CreateSettingsPalette(_theme, _settings, isLight);
        FlyoutControlPalette palette = CreateFlyoutPalette(_theme, _settings, settingsPalette, isLight);
        bool rounded = _settings?.EnableRoundedCorners ?? true;

        Grid rootGrid = new();
        rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        rootGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        ScrollViewer rows = BuildRows(palette);
        Grid.SetRow(rows, 0);
        rootGrid.Children.Add(rows);

        Border footer = BuildFooter(palette, rounded);
        Grid.SetRow(footer, 1);
        rootGrid.Children.Add(footer);

        AddFloatingButtons(rootGrid, palette);

        _confirmOverlay = BuildConfirmOverlay(settingsPalette, rounded);
        _confirmOverlay.IsVisible = false;
        Grid.SetRowSpan(_confirmOverlay, 2);
        rootGrid.Children.Add(_confirmOverlay);

        _rootCard = new Border
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
                Color = Color.FromArgb(0x99, 0, 0, 0),
            }),
            Child = new Border
            {
                Background = TrayAppDotNETFlyoutUI.Brush(_theme.ResolveBackground(_settings, isLight)),
                CornerRadius = Rounded(Layout.RootInnerCornerRadius),
                ClipToBounds = true,
                Padding = Layout.RootInnerPadding,
                Child = rootGrid,
            },
        };
        _rootCard.PointerPressed += OnRootPointerPressed;
        _rootCard.PointerMoved += OnRootPointerMoved;
        _rootCard.PointerReleased += OnRootPointerReleased;
        _rootCard.PointerCaptureLost += OnRootPointerCaptureLost;
        Content = _rootCard;
    }

    private ScrollViewer BuildRows(FlyoutControlPalette palette)
    {
        StackPanel rows = new() { Spacing = 0, Margin = Layout.RowsMargin };

        if ((_settings?.ShowIndividualSliders ?? true) && Monitors.Count > 0)
        {
            foreach (MonitorInfo monitor in Monitors)
                rows.Children.Add(BuildRow(monitor, palette));
        }
        else if (Monitors.Count == 0)
        {
            TextBlock empty = TrayAppDotNETFlyoutUI.Text(L("Flyout_NoDisplays", "No DDC/CI displays detected"), palette,
                Layout.EmptyDisplaysFontSize, color: palette.SecondaryForeground);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            rows.Children.Add(new Border { Padding = Layout.EmptyDisplaysPadding, Child = empty });
        }

        if (_settings?.ShowMasterSlider ?? true)
            rows.Children.Add(BuildRow(MasterMonitor, palette));

        if (_settings?.ShowNightLightSlider ?? true)
            rows.Children.Add(BuildRow(NightLightMonitor, palette));

        _scrollViewer = new ScrollViewer
        {
            Content = rows,
            Focusable = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        return _scrollViewer;
    }

    private Border BuildRow(MonitorInfo monitor, FlyoutControlPalette palette)
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), },
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
                Margin = Layout.RowStopwatchMargin,
            };
            if (monitor.IsCurveStopwatchEnabled)
                stopwatch.Children.Add(BuildCurveStopwatchNumberBox(monitor));
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
                Layout.RowIconSize,
                Layout.RowIconSize,
                Layout.RowCurveIconSize,
                margin: Layout.RowCurveButtonMargin,
                tooltip: monitor.IsNightLight
                    ? L("Flyout_NightLightCurve", "Night-light curve")
                    : L("Flyout_BrightnessCurve", "Brightness curve"));
            curve.Opacity = RowCurveEnabled(monitor) ? 1.0 : 0.4;
            Grid.SetColumn(curve, 3);
            grid.Children.Add(curve);
        }

        if (monitor is { IsMaster: false, IsNightLight: false } && (_settings?.ShowFlyoutMonitorPowerButtons ?? false))
        {
            Border power = TrayAppDotNETFlyoutUI.IconButton(
                GlyphCatalog.POWER,
                palette,
                e => { _ = _monitorService.SetPowerStateAsync(monitor, !monitor.IsPoweredOn); },
                Layout.RowIconSize,
                Layout.RowIconSize,
                Layout.HeaderButtonFontSize,
                enabled: monitor.IsHardwareFunctional,
                margin: Layout.RowPowerButtonMargin,
                tooltip: L("Flyout_TurnOffDisplay", "Turn off display"));
            Grid.SetColumn(power, 3);
            grid.Children.Add(power);
        }

        Grid sliderRow = new()
        {
            Height = Layout.SliderRowHeight,
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), },
        };

        FlyoutSlider slider = CreateSlider(monitor, palette);
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

        Border row = new()
        {
            Background = Brushes.Transparent, Margin = Layout.RowMargin, Child = grid, Opacity = RowOpacity(monitor)
        };
        _profilePreviewRows[monitor] = new ProfilePreviewRowVisuals(slider, row, value);
        return row;
    }

    private Border BuildFooter(FlyoutControlPalette palette, bool rounded)
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        StackPanel profiles = new()
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (ProfileButtonItem item in ProfileButtons)
            profiles.Children.Add(BuildProfileFooterButton(item, palette));
        if (_settings?.Autosave == false)
            profiles.Children.Add(BuildSaveProfileButton(palette));
        Grid.SetColumn(profiles, 0);
        grid.Children.Add(profiles);

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
        };
        if (_settings?.ShowEnvironmentalCurvesButton ?? true)
            actions.Children.Add(BuildCurveIconButton(palette, ToggleEnvironmentalCurves,
                Layout.FooterCurveIconButtonWidth, Layout.FooterCurveIconButtonHeight, Layout.FooterCurveIconSize,
                tooltip: L("Flyout_EnvironmentalCurves", "Environmental curves"),
                opacity: IsBrightnessCurveEnabled || IsNightLightCurveEnabled ? 1.0 : 0.4));
        if (_settings?.ShowFlyoutFooterPowerButton ?? false)
            actions.Children.Add(BuildFooterIconButton(_theme.GlyphPower, palette, PowerOffFooterTargets,
                L("Flyout_TurnOffAllDisplays", "Turn off displays")));
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

        actions.Children.Add(BuildFooterIconButton(
            _theme.GlyphSettings,
            palette,
            () => SettingsRequested?.Invoke(),
            L("Tray_Settings", "Settings"),
            fontFamily: GlyphCatalog.SEGOE_FLUENT_ICONS,
            fontWeight: FontWeight.Black));
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
            Child = grid,
        };
    }

    private Thickness FooterPadding()
    {
        bool crowded = (_settings?.ShowEnvironmentalCurvesButton ?? true)
                       && (_settings?.ShowFlyoutFooterPowerButton ?? false)
                       && (_settings?.ShowFlyoutDisplaySettingsButton ?? true)
                       && (_settings?.Autosave == false);
        return crowded ? Layout.FooterPaddingCrowded : Layout.FooterPaddingNormal;
    }

    private Border BuildProfileFooterButton(ProfileButtonItem item, FlyoutControlPalette palette)
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
                Margin = Layout.ProfileIndicatorMargin,
            };
            content.Children.Add(indicator);
        }

        Border button = TrayAppDotNETFlyoutUI.IconButton(string.Empty, palette,
            _ => SelectProfileApplyingMode(item.Index), Layout.ProfileButtonWidth, Layout.ProfileButtonHeight, 0,
            tooltip: ProfileTooltip(item.Index));
        button.Child = content;
        AttachProfilePreviewHandlers(button, item.Index);
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
            Layout.RowIconSize,
            Layout.RowIconSize,
            0,
            enabled: !monitor.IsNightLight || NightLightProvider.IsSupported(),
            margin: Layout.RowIconMargin,
            tooltip: RowIconTooltip(monitor));

        button.Child = monitor.IsNightLight
            ? new NightLightBulbGlyphIcon
            {
                Width = Layout.NightLightIconSize,
                Height = Layout.NightLightIconSize,
                IconColor = palette.IconForeground,
            }
            : TrayAppDotNETFlyoutUI.IconText(
                RowGlyph(monitor),
                palette,
                monitor.IsMaster ? Layout.MasterIconFontSize : Layout.MonitorIconFontSize,
                GlyphCatalog.SEGOE_FLUENT_ICONS,
                FontWeight.Black);

        return button;
    }

    private SettingsNumberBox BuildCurveStopwatchNumberBox(MonitorInfo monitor)
    {
        bool isLight = BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings);
        SettingsNumberBox number =
            new(CreateSettingsPalette(_theme, _settings, isLight), monitor.CurveStopwatchMinutes, 1, 1440,
                Layout.StopwatchBoxWidth, "m")
            {
                Height = Layout.StopwatchBoxHeight,
                VerticalAlignment = VerticalAlignment.Center,
                Step = 5,
                WheelStep = 5,
                LargeStep = 30,
                ExtraLargeStep = 60,
                HandleMouseWheelWhenMouseOver = true,
            };
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
            "\uE916",
            palette,
            _ => ToggleCurveStopwatch(monitor),
            Layout.StopwatchButtonWidth,
            Layout.StopwatchButtonHeight,
            Layout.StopwatchButtonFontSize,
            margin: Layout.StopwatchButtonMargin,
            tooltip: monitor.CurveStopwatchToolTip,
            fontFamily: GlyphCatalog.SEGOE_MDL2_ASSETS);
        button.Opacity = monitor.IsCurveStopwatchEnabled ? 1.0 : 0.4;
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
            disabledPeriod: IsInCurveDisabledPeriod && width <= Layout.RowIconSize);
        button.Opacity = opacity;
        return button;
    }

    private Control BuildCurveIconContent(FlyoutControlPalette palette, double size, bool disabledPeriod)
    {
        if (disabledPeriod)
        {
            TextBlock disabled = TrayAppDotNETFlyoutUI.IconText(
                GlyphCatalog.CRESCENT_MOON,
                palette,
                Layout.CurveDisabledGlyphFontSize,
                GlyphCatalog.SEGOE_MDL2_ASSETS);
            disabled.Width = Layout.CurveDisabledGlyphSize;
            disabled.Height = Layout.CurveDisabledGlyphSize;
            return disabled;
        }

        return new EnvironmentalCurveGlyphIcon { Width = size, Height = size, IconColor = palette.IconForeground, };
    }

    private void AddFloatingButtons(Grid rootGrid, FlyoutControlPalette palette)
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

        _undockButton = BuildUndockButton(palette);
        _undockButton.HorizontalAlignment = HorizontalAlignment.Right;
        _undockButton.VerticalAlignment = VerticalAlignment.Top;
        _undockButton.Margin = Layout.UndockButtonMargin;
        Grid.SetRow(_undockButton, 0);
        rootGrid.Children.Add(_undockButton);
    }

    private FlyoutSlider CreateSlider(MonitorInfo monitor, FlyoutControlPalette palette)
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
            ThumbOpacity = curveDrivenWithTarget && IsCurveAbsoluteMode ? 0.4 : 1.0,
        };
        slider.UserAdjustmentStarted += (_, _) =>
        {
            BeginSliderGesture(monitor);
            if (monitor.IsMaster) BeginMasterSliderGesture();
        };
        slider.UserAdjustmentCompleted += (_, _) =>
        {
            monitor.IsDragging = false;
            if (monitor.IsMaster) _masterSliderGesturePrepared = false;
            CompleteSliderGesture();
        };
        slider.DoubleTapped += (_, e) =>
        {
            if (!monitor.IsCurveReleased) return;

            ReengageCurveReleasedMonitor(monitor);
            if (monitor.IsMaster) ReengageIndividualBrightnessCurveOverridesFromMaster();
            UpdateCurveStopwatchVisibility(monitor);
            _curveService.Evaluate();
            RebuildVisual();
            e.Handled = true;
        };
        slider.ValueChanged += (_, value) => OnSliderValueChanged(monitor, value);
        return slider;
    }

    private Border BuildUndockButton(FlyoutControlPalette palette)
    {
        _undockButtonController = new FlyoutUndockButtonController(new FlyoutUndockButtonOptions
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
            DraggingChanged = dragging => _isDraggingWindow = dragging,
            UndockTooltip = () => L("Flyout_Undock_Tooltip", "Undock"),
            RedockTooltip = () => L("Flyout_Redock_Tooltip", "Redock"),
            DragThreshold = Layout.DragThreshold,
            CornerRadius = Rounded(Layout.UndockButtonCornerRadius),
        });
        return _undockButtonController.Button;
    }

    private Border BuildConfirmOverlay(SettingsPalette palette, bool rounded)
    {
        _confirmTitle = TrayAppDotNETFlyoutUI.Text(string.Empty,
            CreateFlyoutPalette(_theme, _settings, palette, BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings)),
            Layout.ConfirmTitleFontSize, FontWeight.SemiBold);
        _confirmMessage = TrayAppDotNETFlyoutUI.Text(string.Empty,
            CreateFlyoutPalette(_theme, _settings, palette, BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings)),
            Layout.ConfirmMessageFontSize, color: palette.SecondaryForeground);
        _confirmMessage.TextWrapping = TextWrapping.Wrap;

        _confirmOK = TrayAppDotNETSettingsUI.Button("OK", palette);
        _confirmCancel = TrayAppDotNETSettingsUI.Button("Cancel", palette);
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = Layout.ConfirmButtonsSpacing
        };
        buttons.Children.Add(_confirmCancel);
        buttons.Children.Add(_confirmOK);

        StackPanel panel = new() { Spacing = Layout.ConfirmPanelSpacing };
        panel.Children.Add(_confirmTitle);
        panel.Children.Add(_confirmMessage);
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
            VerticalAlignment = VerticalAlignment.Center,
        };

        return new Border
        {
            Background =
                TrayAppDotNETFlyoutUI.Brush(
                    _theme.FlyoutOverlayBackdrop.For(BrightnessAppTheme.ResolveEffectiveIsLightTheme(_settings))),
            Child = box,
        };
    }

    private void ShowUpdateConfirmation()
    {
        if (_isUpdateDownloadInFlight) return;
        var update = AppServices.UpdateCheckService?.AvailableUpdate;
        if (update == null) return;

        ShowConfirmOverlay(
            "Install update",
            $"Download and install {update.ReleaseName} ({update.TagName})?",
            okText: "Install",
            cancelText: "Cancel",
            onOK: () =>
            {
                CancelConfirmOverlay();
                StartUpdateDownload();
            });
    }

    private void StartUpdateDownload()
    {
        if (_isUpdateDownloadInFlight) return;
        var service = AppServices.UpdateCheckService;
        var info = service?.AvailableUpdate;
        if (service == null || info == null) return;

        _isUpdateDownloadInFlight = true;
        _ = Task.Run(async () =>
        {
            bool ok = false;
            try
            {
                ok = await service.DownloadAndStageAsync(info).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WPFLog.Log($"BrightnessFlyoutWindow.StartUpdateDownload: {ex.Message}");
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ok)
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                        lifetime.Shutdown();
                }
                else
                {
                    _isUpdateDownloadInFlight = false;
                    ShowConfirmOverlay(
                        "Update failed",
                        "The update could not be downloaded. Check the log for details.",
                        okText: "OK",
                        cancelText: null,
                        onOK: CancelConfirmOverlay);
                }
            });
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

    private void OnSliderValueChanged(MonitorInfo monitor, double value)
    {
        BrightnessChanged = true;
        BrightnessUpdated?.Invoke();

        double clamped = Math.Clamp(value, 0, 100);
        if (Math.Abs(monitor.Brightness - clamped) >= 0.001)
            monitor.Brightness = clamped;

        if (monitor.IsMaster && _masterSliderGesturePrepared)
            ApplyMasterToEnabledMonitors();

        if (monitor.IsNightLight
            && NightLightProvider.IsSupported()
            && _isNightLightActive
            && !(IsNightLightCurveEnabled && !_isInCurveDisabledPeriod && !NightLightMonitor.IsCurveReleased))
        {
            int target = FlipIfNightLightInverted((int)Math.Round(clamped));
            NightLightProvider.SetStrength(target);
        }
    }

    private void BeginSliderGesture(MonitorInfo monitor)
    {
        monitor.IsDragging = true;
        DisengageCurveForUserAdjustment(monitor);
    }

    private void CompleteSliderGesture()
    {
        if (!_deferredSliderGestureRebuild)
        {
            RefreshSliderRowVisuals();
            return;
        }

        _deferredSliderGestureRebuild = false;
        RebuildVisual();
    }

    private void BeginMasterSliderGesture()
    {
        if (_masterSliderGesturePrepared) return;
        FloorMonitorBaselinesForMasterGesture();
        CaptureOffsetsFromMaster();
        _masterSliderGesturePrepared = true;
    }

    private void FloorMonitorBaselinesForMasterGesture()
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
                double flooredSource = Math.Floor(source);
                double sliderBrightness = Math.Clamp(flooredSource, 0.0, 100.0);

                monitor.Brightness = sliderBrightness;
                monitor.LastUserBrightness = sliderBrightness;
                monitor.VirtualBrightness = preserve ? flooredSource : sliderBrightness;
                UpdateVisibleMonitorSliderValue(monitor, sliderBrightness);
            }
        }
        finally
        {
            _suppressPropagation = wasSuppressingPropagation;
            hardwareWriteSuspension?.Dispose();
        }
    }

    private void OnMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
                    ApplyMasterToEnabledMonitors();
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
        RebuildVisual();
        QueuePositionNearTray();
    }

    private void OnInitialMonitorEnrollmentRefreshed()
    {
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

    private void OnMonitorsAcquired(IReadOnlyList<MonitorInfo> acquired)
    {
        if (acquired.Count == 0 || IsBrightnessCurveEnabled) return;

        foreach (MonitorInfo monitor in acquired)
        {
            if (!monitor.IsHardwareFunctional) continue;
            if (monitor.SliderState == SliderState.Disabled) continue;
            if (!monitor.HasUserBrightness) continue;
            _monitorService.EnqueueDirectBrightness(monitor, monitor.RoundedBrightness);
        }
    }

    private void AttachMonitor(MonitorInfo monitor)
    {
        if (MasterMonitor.Dependents.Contains(monitor)) return;

        MasterMonitor.Dependents.Add(monitor);
        int masterIndex = AllItems.IndexOf(MasterMonitor);
        if (masterIndex < 0) AllItems.Add(monitor);
        else AllItems.Insert(masterIndex, monitor);

        monitor.PropertyChanged += OnMonitorPropertyChanged;
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
        monitor.PropertyChanged -= OnMonitorPropertyChanged;
        MasterMonitor.Dependents.Remove(monitor);
        AllItems.Remove(monitor);
        _suppressPropagation = true;
        try { UpdateMasterFromEnabledIndividuals(); }
        finally { _suppressPropagation = false; }
    }

    private void OnSelectedProfileChanged(int newIndex)
    {
        foreach (ProfileButtonItem item in ProfileButtons)
            item.IsSelected = item.Index == newIndex;
        ClearProfilePreview();
        CheckAndUpdateUnsavedChanges();
        _curveService.Evaluate();
        RebuildVisual();
    }

    private void OnProfilesListChanged()
    {
        BuildProfileButtonItems();
        RebuildVisual();
    }

    private void UpdateSaveButtonState(bool hasUnsavedChanges)
    {
        if (_hasUnsavedChanges == hasUnsavedChanges) return;
        _hasUnsavedChanges = hasUnsavedChanges;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        if (IsAnySliderGestureActive())
        {
            _deferredSliderGestureRebuild = true;
            return;
        }

        RebuildVisual();
    }

    private void CheckAndUpdateUnsavedChanges()
    {
        MasterSliderMode mode = CurrentMasterSliderMode;
        int nightlight = FlipIfNightLightInverted(NightLightMonitor.RoundedBrightness);
        if (_settings?.Autosave == true
            && _profileManager.HasPendingChanges(Monitors, mode, nightlight))
            _profileManager.SaveCurrentState(Monitors, mode, nightlight);

        _profileManager.CheckForUnsavedChanges(Monitors, mode, nightlight);
    }

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

        RebuildVisual();
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

    private void AttachProfilePreviewHandlers(Border button, int profileIndex)
    {
        button.PointerEntered += (_, _) => ShowProfilePreview(profileIndex);
        button.PointerExited += (_, _) => ClearProfilePreviewFromButton(profileIndex, button);
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
            _ => pool.Average(),
        };
    }

    private void ApplyMasterToEnabledMonitors()
    {
        bool suspendForCurve = IsBrightnessCurveEnabled
                               && (_isInCurveDisabledPeriod || MasterMonitor.IsCurveReleased);
        IDisposable? hardwareWriteSuspension = suspendForCurve ? _monitorService.SuspendHardwareWrites() : null;
        try
        {
            foreach (MonitorInfo monitor in Monitors)
            {
                if (!monitor.IsParticipatingInMaster) continue;
                double unclamped = MasterMonitor.Brightness + monitor.Offset;
                double clamped = Math.Clamp(unclamped, 0, 100);
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
        List<MonitorInfo> connected = [.. Monitors.Where(m => m.IsHardwareFunctional)];
        if (connected.Count == 0) return;

        MasterMonitor.Brightness = CurrentMasterSliderMode switch
        {
            MasterSliderMode.Lowest => connected.Min(m => m.Brightness),
            MasterSliderMode.Highest => connected.Max(m => m.Brightness),
            _ => connected.Average(m => m.Brightness),
        };
    }

    private double ComputeMasterFromEnabledIndividuals()
    {
        List<MonitorInfo> connected = [.. Monitors.Where(m => m.IsHardwareFunctional)];
        if (connected.Count == 0) return MasterMonitor.Brightness;

        return CurrentMasterSliderMode switch
        {
            MasterSliderMode.Lowest => connected.Min(m => m.Brightness),
            MasterSliderMode.Highest => connected.Max(m => m.Brightness),
            _ => connected.Average(m => m.Brightness),
        };
    }

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
        UpdateMasterFromEnabledIndividuals();
        int providerStrength = NightLightProvider.IsSupported() ? NightLightProvider.GetStrength() : 0;
        int displayValue = FlipIfNightLightInverted(providerStrength);
        if (NightLightMonitor.RoundedBrightness != displayValue) NightLightMonitor.Brightness = displayValue;
        _isNightLightActive = NightLightProvider.IsSupported() && NightLightProvider.IsEnabled();

        if (_isUndocked && _settings?.AllowFlyoutUndock == false) Redock();
        UpdateAllCurveStopwatchVisibility(saveIfDisabled: true);
        _curveService.Start();
        _curveService.Evaluate();
        RebuildVisual();
    });

    private void OnMonitorIconClick(MonitorInfo monitor)
    {
        if (monitor.IsNightLight)
        {
            if (!NightLightProvider.IsSupported()) return;
            if (!NightLightProvider.IsEnabled()
                && _curveService.GetActiveNightLightCurveStrength() is { } curveStrength and > 0)
                NightLightProvider.SetStrength(curveStrength, persistAsLastUserValue: false);

            NightLightProvider.Toggle();
            _isNightLightActive = NightLightProvider.IsEnabled();
            OnPropertyChanged(nameof(IsNightLightActive));
            RebuildVisual();
            return;
        }

        bool isWarningState = monitor is { IsMaster: false, WasEverDDCCapable: true }
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

        bool wasCurveDriven =
            previous is SliderState.CurveActive or SliderState.CurveSleeping or SliderState.CurveReleased;
        if (wasCurveDriven && monitor.SliderState == SliderState.Disabled)
            _monitorService.EnqueueDirectBrightness(monitor, monitor.RoundedBrightness);
    }

    private void RunHardPowerOff(MonitorInfo monitor)
    {
        string EDIDSerial = monitor.EDIDSerial;
        ShowConfirmOverlay(
            L("Flyout_HardPowerOff_Title", "Power off display"),
            L("Flyout_HardPowerOff_InProgress", "Sending hard power-off command..."),
            okText: L("Common_OK", "OK"),
            cancelText: null,
            onOK: CancelConfirmOverlay);

        _ = Task.Run(() =>
        {
            bool ok;
            string? error;
            try
            {
                ok = _monitorService.TryHardPowerOffByEDIDSerial(EDIDSerial, out error);
            }
            catch (Exception ex)
            {
                WPFLog.Log($"BrightnessFlyoutWindow.RunHardPowerOff: {ex.Message}");
                ok = false;
                error = ex.Message;
            }

            Dispatcher.UIThread.Post(() =>
            {
                string message = ok
                    ? L("Flyout_HardPowerOff_Success", "The hard power-off command was sent.")
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        L("Flyout_HardPowerOff_FailedFormat", "Hard power-off failed: {0}"),
                        !string.IsNullOrWhiteSpace(error)
                            ? error
                            : monitor.LastDDCError ?? L("Flyout_HardPowerOff_NoResponseDetail",
                                "No response from the display."));
                ShowConfirmOverlay(
                    L("Flyout_HardPowerOff_Title", "Power off display"),
                    message,
                    okText: L("Common_OK", "OK"),
                    cancelText: null,
                    onOK: CancelConfirmOverlay);
            });
        });
    }

    private void ToggleCurveForRow(MonitorInfo monitor)
    {
        if (monitor.IsMaster) IsBrightnessCurveEnabled = !IsBrightnessCurveEnabled;
        else if (monitor.IsNightLight) IsNightLightCurveEnabled = !IsNightLightCurveEnabled;
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
            _ = _monitorService.SetPowerStateAsync(monitor, false);
        }
    }

    private static void OpenDisplaySettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "ms-settings:display", UseShellExecute = true, });
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessFlyoutWindow.OpenDisplaySettings: {ex.Message}");
        }
    }

    private void DisengageCurveForUserAdjustment(MonitorInfo monitor)
    {
        if (_isInCurveDisabledPeriod) return;
        if (!IsCurveAbsoluteMode) return;

        if (monitor.IsMaster && IsBrightnessCurveEnabled)
        {
            SliderState previous = monitor.SliderState;
            monitor.SliderState = SliderStateMachine.OnUserRelease(monitor.SliderState);
            UpdateCurveStopwatchVisibility(monitor);
            _curveService.Evaluate();
            if (monitor.SliderState == SliderState.CurveReleased && previous != SliderState.CurveReleased)
                ResyncManualCurveOverrideToSlider(monitor);
            return;
        }

        if (monitor.IsNightLight && IsNightLightCurveEnabled)
        {
            SliderState previous = monitor.SliderState;
            monitor.SliderState = SliderStateMachine.OnUserRelease(monitor.SliderState);
            UpdateCurveStopwatchVisibility(monitor);
            _curveService.Evaluate();
            if (monitor.SliderState == SliderState.CurveReleased && previous != SliderState.CurveReleased)
                ResyncManualCurveOverrideToSlider(monitor);
            return;
        }

        if (monitor is { IsMaster: false, IsNightLight: false } && IsBrightnessCurveEnabled)
        {
            SliderState previous = monitor.SliderState;
            monitor.SliderState = SliderStateMachine.OnUserRelease(monitor.SliderState);
            UpdateCurveStopwatchVisibility(monitor);
            if (monitor.SliderState == SliderState.CurveReleased && previous != SliderState.CurveReleased)
                ResyncManualCurveOverrideToSlider(monitor);
        }
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
        _curveService.Start();
        _curveService.Evaluate();
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
        (_settings?.InvertNightLightSlider ?? false) ? 100 - value : value;

    private void RunPreviewSweep()
    {
        _curveService.Suspend();
        _previewSweepStartFraction = EnvironmentalCurveSampler.CurrentDayFraction();
        _previewSweepStopwatch = Stopwatch.StartNew();
        int rateMs = Math.Max(TimeConstants.BrightnessUpdateRateMinMs,
            _settings?.BrightnessUpdateRateMs ?? TimeConstants.BrightnessUpdateRateDefaultMs);
        _previewSweepTimer =
            new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(rateMs) };
        _previewSweepTimer.Tick += PreviewSweepHardwareTick;
        PreviewSweepStateChanged?.Invoke(true);
        QueuePreviewSweepAnimationFrame();
        _previewSweepTimer.Start();
        PreviewSweepHardwareTick(null, EventArgs.Empty);
    }

    private void QueuePreviewSweepAnimationFrame()
    {
        if (_previewSweepAnimationFrameQueued || _previewSweepStopwatch == null) return;
        _previewSweepAnimationFrameQueued = true;
        RequestAnimationFrame(OnPreviewSweepAnimationFrame);
    }

    private void OnPreviewSweepAnimationFrame(TimeSpan _)
    {
        _previewSweepAnimationFrameQueued = false;
        if (_previewSweepStopwatch == null) return;
        double s = _previewSweepStopwatch.Elapsed.TotalMilliseconds /
                   TimeConstants.BrightnessFlyoutPreviewSweepDurationMs;
        if (s > 1.0) s = 1.0;
        PreviewSweepProgress?.Invoke(WrapSweepFraction(s));
        if (s < 1.0)
            QueuePreviewSweepAnimationFrame();
    }

    private void PreviewSweepHardwareTick(object? sender, EventArgs e)
    {
        if (_previewSweepStopwatch == null) return;
        double s = _previewSweepStopwatch.Elapsed.TotalMilliseconds /
                   TimeConstants.BrightnessFlyoutPreviewSweepDurationMs;
        bool finished = s >= 1.0;
        if (finished) s = 1.0;
        double t = WrapSweepFraction(s);
        if (!_curveService.ApplyAt(t))
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
        if (_previewSweepTimer != null)
        {
            _previewSweepTimer.Stop();
            _previewSweepTimer.Tick -= PreviewSweepHardwareTick;
            _previewSweepTimer = null;
        }

        _previewSweepStopwatch = null;
        _previewSweepAnimationFrameQueued = false;
        PreviewSweepStateChanged?.Invoke(false);
        _curveService.Resume();
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
                IsSelected = i == selectedIndex,
            });
        }
    }

    private const string MasterCurveStopwatchKey = "master";
    private const string NightLightCurveStopwatchKey = "nightlight";
    private const int DefaultCurveStopwatchMinutes = 60;

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

        entry = new CurveStopwatchEntry { SliderKey = key, Minutes = DefaultCurveStopwatchMinutes, };
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
        _curveService.Evaluate();
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
        monitor.CurveStopwatchMinutes = Math.Max(1, entry?.Minutes ?? DefaultCurveStopwatchMinutes);
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
            _curveStopwatchTimer =
                new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _curveStopwatchTimer.Tick += OnCurveStopwatchTimerTick;
        }

        if (!_curveStopwatchTimer.IsEnabled) _curveStopwatchTimer.Start();
    }

    private void StopCurveStopwatchTimer()
    {
        if (_curveStopwatchTimer == null) return;
        _curveStopwatchTimer.Stop();
        _curveStopwatchTimer.Tick -= OnCurveStopwatchTimerTick;
        _curveStopwatchTimer = null;
    }

    private void OnCurveStopwatchTimerTick(object? sender, EventArgs e) => ProcessCurveStopwatchDeadlines();

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
        else if (monitor.IsNightLight)
            ReengageCurveReleasedMonitor(monitor);
        else if (!IsMasterStopwatchBlockingReengage())
            ReengageCurveReleasedMonitor(monitor);
        else
            _curveStopwatchReengageBlockedByMaster.Add(CurveStopwatchKeyFor(monitor));

        UpdateCurveStopwatchVisibility(monitor, saveIfDisabled: false);
        _curveService.Evaluate();
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
        if (!_isUndocked) return;
        if (_undockButtonController?.IsPointerCaptured == true) return;
        if (TrayAppDotNETFlyoutUI.IsInteractiveDragSource(e.Source as Visual)) return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        if (sender is not Control control) return;

        (PixelPoint dockedPosition, int snapTolerance) = CaptureDockedPosition();
        PixelPoint pointer = control.PointToScreen(e.GetPosition(control));
        _dragHelper.BeginDrag(pointer, Position, dockedPosition, snapTolerance);
        e.Pointer.Capture(control);
        _isDraggingWindow = true;
        e.Handled = true;
    }

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
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
        if (!_isDraggingWindow || _undockButtonController?.IsPointerCaptured == true) return;
        EndRootDrag(e.Pointer, commit: true);
        e.Handled = true;
    }

    private void OnRootPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDraggingWindow && _undockButtonController?.IsPointerCaptured != true)
            CommitDragPosition();
        _isDraggingWindow = false;
    }

    private void EndRootDrag(IPointer pointer, bool commit)
    {
        _isDraggingWindow = false;
        pointer.Capture(null);
        if (commit) CommitDragPosition();
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
            : workArea.Right - width - Layout.EdgePadding;
        int top = workArea.Bottom - height - Layout.EdgePadding;
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
        int minLeft = workArea.X + Layout.EdgePadding;
        int maxLeft = Math.Max(minLeft, workArea.Right - width - Layout.EdgePadding);
        int minTop = workArea.Y + Layout.EdgePadding;
        int maxTop = Math.Max(minTop, workArea.Bottom - height - Layout.EdgePadding);
        return new PixelPoint(Math.Clamp(target.X, minLeft, maxLeft), Math.Clamp(target.Y, minTop, maxTop));
    }

    private int CurrentPixelWidth() =>
        Math.Max(Layout.PixelMinSize, (int)Math.Ceiling(Math.Max(Bounds.Width, Width) * RenderScaling));

    private int CurrentPixelHeight() =>
        Math.Max(Layout.PixelMinSize, (int)Math.Ceiling(Math.Max(Bounds.Height, Layout.PixelMinSize) * RenderScaling));

    private int ResolveSnapTolerance()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        return Math.Max(Layout.PixelMinSize,
            (int)Math.Round(Math.Min(workArea.Width, workArea.Height) * Layout.SnapTolerancePercent));
    }

    private void QueuePositionNearTray()
    {
        if (!IsVisible || _isDraggingWindow) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible || _isDraggingWindow) return;
            UpdateLayout();
            ApplyWorkAreaMaxHeight();
            PositionNearTray();
        }, DispatcherPriority.Loaded);
    }

    private void ApplyWorkAreaMaxHeight()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        MaxHeight = Math.Max(Layout.WorkAreaMinHeight, workArea.Height / RenderScaling - Layout.EdgePadding * 2);
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
            _ => -1,
        };
        if (index < 0 || index >= ProfileButtons.Count) return;
        SelectProfileApplyingMode(index);
        e.Handled = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CancelPreviewSweep();
        StopCurveStopwatchTimer();
        MasterMonitor.PropertyChanged -= OnMonitorPropertyChanged;
        foreach (MonitorInfo monitor in Monitors)
            monitor.PropertyChanged -= OnMonitorPropertyChanged;
        NightLightMonitor.PropertyChanged -= OnNightLightPropertyChanged;
        Monitors.CollectionChanged -= OnMonitorsCollectionChanged;
        _monitorService.MonitorsAcquired -= OnMonitorsAcquired;
        _monitorService.MonitorsRefreshed -= OnInitialMonitorEnrollmentRefreshed;
        _profileManager.SelectedProfileChanged -= OnSelectedProfileChanged;
        _profileManager.UnsavedChangesStatusChanged -= UpdateSaveButtonState;
        _profileManager.ProfilesListChanged -= OnProfilesListChanged;
        if (_settings != null) _settings.Changed -= OnSettingsChanged;
        if (AppServices.UpdateCheckService != null)
            AppServices.UpdateCheckService.StateChanged -= NotifyUpdateStateChanged;
        try { _session.Dispose(); }
        catch (Exception ex) { WPFLog.Log($"BrightnessFlyoutWindow.OnClosed: {ex.Message}"); }

        if (_settings != null)
        {
            _settings.LastMasterBrightness = (int)Math.Round(Math.Clamp(MasterMonitor.Brightness, 0, 100));
            _settings.Save();
        }
    }

    private string RowGlyph(MonitorInfo monitor)
    {
        if (monitor.IsNightLight) return GlyphCatalog.LIGHTBULB;
        if (monitor.IsFailed || monitor.IsReadDegraded) return GlyphCatalog.WARNING;
        if (monitor.IsMaster) return monitor.IconGlyph;
        return _theme.GlyphMonitor;
    }

    private string RowTitle(MonitorInfo monitor)
    {
        if (monitor.IsNightLight && (_settings?.ShowNightLightKelvinLabel ?? false))
        {
            int strength = FlipIfNightLightInverted(monitor.RoundedBrightness);
            string suffix = (_settings?.TurnOffNightLightAtZeroStrength == true && strength <= 0)
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
        (_settings?.EnableRoundedCorners ?? true) ? radius : Layout.ZeroCornerRadius;

    private sealed record FlyoutLayout(
        int EdgePadding,
        double DragThreshold,
        double SnapTolerancePercent,
        double SliderHitTestVerticalPadding,
        double SliderRowHeight,
        double RowIconSize,
        double HeaderButtonSize,
        double HeaderButtonFontSize,
        double UpdateButtonWidth,
        double UpdateButtonHeight,
        double CoarseWheelStep,
        double WorkAreaMinHeight,
        int PixelMinSize,
        int OffscreenPosition,
        int FallbackWorkAreaX,
        int FallbackWorkAreaY,
        int FallbackWorkAreaWidth,
        int FallbackWorkAreaHeight,
        Thickness ZeroThickness,
        CornerRadius ZeroCornerRadius,
        Thickness RootBorderThickness,
        CornerRadius RootCornerRadius,
        CornerRadius RootInnerCornerRadius,
        Thickness RootInnerPadding,
        double RootShadowOffsetY,
        double RootShadowBlur,
        Thickness RowsMargin,
        Thickness EmptyDisplaysPadding,
        double EmptyDisplaysFontSize,
        double RowTitleFontSize,
        Thickness RowStopwatchMargin,
        Thickness RowCurveButtonMargin,
        double RowCurveIconSize,
        Thickness RowPowerButtonMargin,
        double SliderValueFontSize,
        double SliderValueMinWidth,
        Thickness SliderValueMargin,
        Thickness RowMargin,
        CornerRadius FooterCornerRadius,
        Thickness FooterMargin,
        Thickness FooterPaddingCrowded,
        Thickness FooterPaddingNormal,
        double FooterIconButtonWidth,
        double FooterIconButtonHeight,
        double FooterIconButtonFontSize,
        double FooterCurveIconButtonWidth,
        double FooterCurveIconButtonHeight,
        double FooterCurveIconSize,
        double ProfileGlyphFontSize,
        double ProfileIndicatorWidth,
        double ProfileIndicatorHeight,
        CornerRadius ProfileIndicatorCornerRadius,
        Thickness ProfileIndicatorMargin,
        double ProfileButtonWidth,
        double ProfileButtonHeight,
        double SaveProfileGlyphFontSize,
        Thickness RowIconMargin,
        double NightLightIconSize,
        double MasterIconFontSize,
        double MonitorIconFontSize,
        double StopwatchBoxHeight,
        double StopwatchBoxWidth,
        double StopwatchButtonWidth,
        double StopwatchButtonHeight,
        double StopwatchButtonFontSize,
        Thickness StopwatchButtonMargin,
        double CurveDisabledGlyphFontSize,
        double CurveDisabledGlyphSize,
        Thickness UpdateButtonPadding,
        double UpdateButtonFontSize,
        Thickness UpdateButtonMargin,
        Thickness UndockButtonMargin,
        CornerRadius UndockButtonCornerRadius,
        double ConfirmTitleFontSize,
        double ConfirmMessageFontSize,
        double ConfirmButtonsSpacing,
        double ConfirmPanelSpacing,
        Thickness ConfirmBorderThickness,
        CornerRadius ConfirmCornerRadius,
        Thickness ConfirmPadding,
        double ConfirmWidth)
    {
        public static FlyoutLayout From(Control owner)
        {
            HotReloadResourceReader r = new(owner, "Flyout");
            return new FlyoutLayout(
                r.Int("EdgePadding"),
                r.Double("DragThreshold"),
                r.Double("SnapTolerancePercent"),
                r.Double("SliderHitTestVerticalPadding"),
                r.Double("SliderRowHeight"),
                r.Double("RowIconSize"),
                r.Double("HeaderButtonSize"),
                r.Double("HeaderButtonFontSize"),
                r.Double("UpdateButtonWidth"),
                r.Double("UpdateButtonHeight"),
                r.Double("CoarseWheelStep"),
                r.Double("WorkAreaMinHeight"),
                r.Int("PixelMinSize"),
                r.Int("OffscreenPosition"),
                r.Int("FallbackWorkAreaX"),
                r.Int("FallbackWorkAreaY"),
                r.Int("FallbackWorkAreaWidth"),
                r.Int("FallbackWorkAreaHeight"),
                r.Thickness("ZeroThickness"),
                r.CornerRadius("ZeroCornerRadius"),
                r.Thickness("RootBorderThickness"),
                r.CornerRadius("RootCornerRadius"),
                r.CornerRadius("RootInnerCornerRadius"),
                r.Thickness("RootInnerPadding"),
                r.Double("RootShadowOffsetY"),
                r.Double("RootShadowBlur"),
                r.Thickness("RowsMargin"),
                r.Thickness("EmptyDisplaysPadding"),
                r.Double("EmptyDisplaysFontSize"),
                r.Double("RowTitleFontSize"),
                r.Thickness("RowStopwatchMargin"),
                r.Thickness("RowCurveButtonMargin"),
                r.Double("RowCurveIconSize"),
                r.Thickness("RowPowerButtonMargin"),
                r.Double("SliderValueFontSize"),
                r.Double("SliderValueMinWidth"),
                r.Thickness("SliderValueMargin"),
                r.Thickness("RowMargin"),
                r.CornerRadius("FooterCornerRadius"),
                r.Thickness("FooterMargin"),
                r.Thickness("FooterPaddingCrowded"),
                r.Thickness("FooterPaddingNormal"),
                r.Double("FooterIconButtonWidth"),
                r.Double("FooterIconButtonHeight"),
                r.Double("FooterIconButtonFontSize"),
                r.Double("FooterCurveIconButtonWidth"),
                r.Double("FooterCurveIconButtonHeight"),
                r.Double("FooterCurveIconSize"),
                r.Double("ProfileGlyphFontSize"),
                r.Double("ProfileIndicatorWidth"),
                r.Double("ProfileIndicatorHeight"),
                r.CornerRadius("ProfileIndicatorCornerRadius"),
                r.Thickness("ProfileIndicatorMargin"),
                r.Double("ProfileButtonWidth"),
                r.Double("ProfileButtonHeight"),
                r.Double("SaveProfileGlyphFontSize"),
                r.Thickness("RowIconMargin"),
                r.Double("NightLightIconSize"),
                r.Double("MasterIconFontSize"),
                r.Double("MonitorIconFontSize"),
                r.Double("StopwatchBoxHeight"),
                r.Double("StopwatchBoxWidth"),
                r.Double("StopwatchButtonWidth"),
                r.Double("StopwatchButtonHeight"),
                r.Double("StopwatchButtonFontSize"),
                r.Thickness("StopwatchButtonMargin"),
                r.Double("CurveDisabledGlyphFontSize"),
                r.Double("CurveDisabledGlyphSize"),
                r.Thickness("UpdateButtonPadding"),
                r.Double("UpdateButtonFontSize"),
                r.Thickness("UpdateButtonMargin"),
                r.Thickness("UndockButtonMargin"),
                r.CornerRadius("UndockButtonCornerRadius"),
                r.Double("ConfirmTitleFontSize"),
                r.Double("ConfirmMessageFontSize"),
                r.Double("ConfirmButtonsSpacing"),
                r.Double("ConfirmPanelSpacing"),
                r.Thickness("ConfirmBorderThickness"),
                r.CornerRadius("ConfirmCornerRadius"),
                r.Thickness("ConfirmPadding"),
                r.Double("ConfirmWidth"));
        }
    }

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

internal sealed record ProfilePreviewRowVisuals(FlyoutSlider Slider, Border Row, TextBlock Value);

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
