using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;


namespace VolumeTrayAppDotNET.UI.Flyout;

public sealed partial class VolumeFlyoutWindow : FlyoutWindowCommon
{
    private static readonly FontFamily FlyoutFont = new("Segoe UI");

    private static readonly HashSet<string> DeviceRebuildProperties = new(StringComparer.Ordinal)
    {
        nameof(AudioDevice.IsActive),
        nameof(AudioDevice.IsBluetooth),
        nameof(AudioDevice.State),
        nameof(AudioDevice.BatteryLevel),
        nameof(AudioDevice.LastKnownBatteryLevel),
        nameof(AudioDevice.DefaultFormat),
        nameof(AudioDevice.CurrentCodecName)
    };

    private readonly AudioDeviceManager _audioManager;
    private readonly AppSettings _settings;
    private readonly Action _openSettings;
    private readonly AppVolumeFeedbackPlayer? _feedback;
    private readonly string? _ownAppID;
    private TrayAppDotNETShellTrayIcon? _lastTrayIcon;
    private bool _isRebuilding;
    private bool _isUpdateDownloadInFlight;
    private bool _isUpdateDialogOpen;
    private bool _rebuildPending;
    private bool _rebuildQueued;
    private bool _isClosed;
    private FlyoutAxamlProperties? _layout;
    private VolumeFlyoutContentGeneration? _activeContent;
    private VolumeFlyoutContentGeneration? _buildingContent;
    private readonly FlyoutWindowDragHelper _dragHelper = new();
    private FlyoutDockingController? _dockingController;

    public VolumeFlyoutWindow()
    {
        _audioManager = null!;
        _settings = null!;
        _openSettings = () => { };
        InitializeComponent();
        InitializeComponentState();
    }

    internal VolumeFlyoutWindow(AudioDeviceManager audioManager, AppSettings settings, Action openSettings)
    {
        _audioManager = audioManager;
        _settings = settings;
        _openSettings = openSettings;
        _feedback = new AppVolumeFeedbackPlayer(Dispatcher.UIThread, settings);
        _ownAppID = ResolveOwnAppID();

        InitializeComponent();

        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _dockingController = new FlyoutDockingController(new FlyoutDockingOptions
        {
            Settings = _settings,
            DragHelper = _dragHelper,
            CurrentPosition = () => Position,
            SetPosition = position => Position = position,
            ResolveDockedPosition = () => ResolveDockedPosition(_lastTrayIcon),
            ResolveSavedPosition = ResolveSavedPosition,
            ResolveSnapTolerance = ResolveSnapTolerance,
            StateChanged = OnDockStateChanged
        });

        _settings.Changed += OnSettingsChanged;
        WindowResources.Add(() => _settings.Changed -= OnSettingsChanged);
        _audioManager.PropertyChanged += OnAudioManagerPropertyChanged;
        WindowResources.Add(() => _audioManager.PropertyChanged -= OnAudioManagerPropertyChanged);
        INotifyCollectionChanged devices = (INotifyCollectionChanged)_audioManager.Devices;
        devices.CollectionChanged += OnDevicesCollectionChanged;
        WindowResources.Add(() => devices.CollectionChanged -= OnDevicesCollectionChanged);

        if (AppServices.UpdateCheckService is { } updateService)
        {
            updateService.StateChanged += NotifyUpdateStateChanged;
            WindowResources.Add(() => updateService.StateChanged -= NotifyUpdateStateChanged);
        }

        KeyDown += OnWindowKeyDown;
        WindowResources.Add(() => KeyDown -= OnWindowKeyDown);

        InitializeComponentState();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        Hide();
        e.Handled = true;
    }

    private void InitializeComponentState()
    {
        _layout = AxamlFlyout;

        if (_settings != null && _audioManager != null)
            Rebuild();
    }

    private FlyoutAxamlProperties Layout =>
        _layout ?? throw new InvalidOperationException("Flyout layout resources have not been loaded.");

    private FlyoutDockingController Docking =>
        _dockingController ?? throw new InvalidOperationException("Flyout docking has not been initialized.");

    private int EdgePadding => (int)Math.Round(Layout.EdgePadding);

    private int PixelMinSize => (int)Math.Round(Layout.PixelMinSize);

    public void Redock()
    {
        if (_isClosed) return;
        Docking.Redock();
    }

    public void ShowAt(TrayAppDotNETShellTrayIcon trayIcon, bool activate = true)
    {
        if (_isClosed) return;

        _lastTrayIcon = trayIcon;
        ShowActivated = activate;
        ApplyWorkAreaMaxHeight();
        Rebuild();
        if (!IsVisible)
        {
            Opacity = 0;
            Position = OffscreenPosition();
            Show();
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_isClosed || !IsVisible) return;
            UpdateLayout();
            ApplyWorkAreaMaxHeight();
            PositionNearTray();
            ScrollCellsToBottom();
            StartFlyoutActivity();
            Opacity = 1;
            if (activate) Activate();
        }, DispatcherPriority.Loaded);
    }

    public new void Hide()
    {
        if (_isClosed) return;

        CloseOpenMenu();
        StopFlyoutActivity();
        bool retirePendingContent = _rebuildPending;
        _activeContent?.ActiveVolumeSliderDragCount = 0;
        base.Hide();
        if (retirePendingContent)
        {
            _rebuildPending = true;
            RetireActiveContentGeneration();
        }
        else
        {
            _rebuildPending = false;
        }

        NotifyWarmDismissed();
    }

    protected override bool HasOpenChildWindow => IsFlyoutMenuOpen || _isUpdateDialogOpen;

    protected override bool ShouldAutoHideWhenDeactivated => _dockingController?.IsUndocked != true;

    protected override void HideFlyout() => Hide();

    public void NotifyUpdateStateChanged() => QueueRebuild();

    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_isClosed) return;

        if (Docking.IsUndocked && !_settings.AllowFlyoutUndock)
        {
            Redock();
            return;
        }

        QueueRebuild();
    });

    private void OnAudioManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioDeviceManager.DefaultDevice))
            QueueRebuild();
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        QueueRebuild();

    private void PositionNearTray() => Position = Docking.ResolvePosition();

    private PixelPoint OffscreenPosition() => new(Layout.OffscreenPosition, Layout.OffscreenPosition);

    private PixelRect FallbackWorkArea() => new(
        Layout.FallbackWorkAreaX,
        Layout.FallbackWorkAreaY,
        Layout.FallbackWorkAreaWidth,
        Layout.FallbackWorkAreaHeight);

    private void QueuePositionNearTray(bool scrollToBottom = false)
    {
        if (!IsVisible || _activeContent?.IsDraggingWindow == true) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible || _activeContent?.IsDraggingWindow == true) return;
            UpdateLayout();
            ApplyWorkAreaMaxHeight();
            PositionNearTray();
            if (scrollToBottom) ScrollCellsToBottom();
        }, DispatcherPriority.Loaded);
    }

    private PixelPoint ResolveSavedPosition(PixelPoint savedPosition)
    {
        return TrayPopupPositioning.ClampToSavedMonitor(
            Screens,
            ResolveWorkArea(_lastTrayIcon),
            CurrentPixelSize(),
            savedPosition,
            EdgePadding);
    }

    private PixelPoint ResolveDockedPosition(TrayAppDotNETShellTrayIcon? trayIcon)
    {
        PixelRect? iconRect = null;
        if (trayIcon?.TryGetIconRect(out PixelRect resolvedIconRect) == true)
            iconRect = resolvedIconRect;

        PixelPoint anchor = iconRect?.Center ?? Position;
        PixelRect workArea = TrayWorkArea.Resolve(Screens, anchor, FallbackWorkArea());
        int width = CurrentPixelWidth();
        int height = CurrentPixelHeight();

        return TrayPopupPositioning.ResolveDockedPosition(
            workArea,
            new PixelSize(width, height),
            iconRect,
            EdgePadding);
    }

    private PixelSize CurrentPixelSize() => new(CurrentPixelWidth(), CurrentPixelHeight());

    private int CurrentPixelWidth() =>
        Math.Max(PixelMinSize, (int)Math.Ceiling(Math.Max(Bounds.Width, Width) * RenderScaling));

    private int CurrentPixelHeight() =>
        Math.Max(PixelMinSize, (int)Math.Ceiling(Math.Max(Bounds.Height, MinHeight) * RenderScaling));

    private int ResolveSnapTolerance()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        return Math.Max(PixelMinSize,
            (int)Math.Round(Math.Min(workArea.Width, workArea.Height) * Layout.SnapTolerancePercent));
    }

    private PixelRect ResolveWorkArea(TrayAppDotNETShellTrayIcon? trayIcon)
    {
        PixelPoint anchor = Position;
        if (trayIcon?.TryGetIconRect(out PixelRect iconRect) == true)
            anchor = iconRect.Center;

        return TrayWorkArea.Resolve(Screens, anchor, FallbackWorkArea());
    }

    private void ApplyWorkAreaMaxHeight()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        MaxHeight = Math.Max(Layout.WorkAreaMinHeight, workArea.Height / RenderScaling - EdgePadding * 2);
    }

    private void StartFlyoutActivity()
    {
        if (_isClosed || _audioManager == null || _settings == null) return;

        _audioManager.StartMetering();
        if (_settings.FlyoutCommunicationsButtonVisibility != CommunicationsButtonVisibility.Hidden)
            CommunicationsDucking.Start();
        else
            CommunicationsDucking.Stop();
    }

    private void StopFlyoutActivity()
    {
        _audioManager?.StopMetering();
        CommunicationsDucking.Stop();
        SetAllGroupMetersVisible(false);
    }

    private void ScrollCellsToBottom()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ScrollViewer? scroll = _activeContent?.CellsScrollViewer;
            if (scroll == null) return;

            double maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, maxOffset);
        }, DispatcherPriority.Loaded);
    }

    private void Rebuild()
    {
        if (_isClosed) return;

        try { RebuildCore(); }
        catch (Exception ex)
        {
            _rebuildPending = false;
            _rebuildQueued = false;
            TADNLog.Log($"VolumeFlyoutWindow.Rebuild: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Rebuilds the flyout content transactionally so a failed build keeps the previous UI alive.
    /// </summary>
    private void RebuildCore()
    {
        if (_isClosed || _layout == null) return;
        if (IsContentInteractionActive() || _isRebuilding)
        {
            _rebuildPending = true;
            return;
        }

        _rebuildQueued = false;
        _isRebuilding = true;
        _rebuildPending = false;
        try
        {
            double? previousScrollOffset = _activeContent?.CellsScrollViewer?.Offset.Y;
            VolumeFlyoutContentGeneration candidate = BuildContentGeneration();
            if (_isClosed)
            {
                candidate.Generation.Dispose();
                return;
            }

            CommitContentGeneration(candidate.Generation);
            if (_isClosed || candidate.Generation.IsDisposed)
            {
                _activeContent = null;
                return;
            }

            _activeContent = candidate;

            if (previousScrollOffset.HasValue)
                RestoreCellsScrollOffset(candidate, previousScrollOffset.Value);

            QueuePositionNearTray();
        }
        finally
        {
            _isRebuilding = false;
        }

        FlushPendingRebuild();
    }

    /// <summary>
    /// Builds a complete unpublished generation. Failure retires only the candidate resources.
    /// </summary>
    private VolumeFlyoutContentGeneration BuildContentGeneration()
    {
        UIResourceScope resources = new(
            "VolumeFlyoutWindow.Content",
            exception => TADNLog.Log($"Volume flyout generation cleanup failed: {exception.Message}"));
        VolumeFlyoutContentGeneration candidate = new(resources);
        _buildingContent = candidate;

        try
        {
            bool isLight = ResolveEffectiveIsLight();
            SettingsPalette settingsPalette = VolumeSettingsPalette.Create(AppServices.Theme, _settings, isLight);
            FlyoutPalette flyoutPalette = FlyoutPalette.Create(
                settingsPalette,
                AppServices.Theme,
                _settings,
                isLight);

            List<AudioDevice> devices = FlyoutDeviceOrdering.Build(_audioManager.Devices, _settings);
            StackPanel cellStack = new() { Spacing = 0 };
            for (int index = 0; index < devices.Count; index++)
            {
                cellStack.Children.Add(BuildCell(
                    devices[index],
                    flyoutPalette,
                    isFirst: index == 0,
                    isLast: index == devices.Count - 1));
            }

            Grid body = new() { ClipToBounds = true };
            ScrollViewer scroll = new()
            {
                MaxHeight = ResolveMaxContentHeight(),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Content = cellStack
            };
            candidate.CellsScrollViewer = scroll;
            body.Children.Add(scroll);

            TextBlock empty = Text(
                L("Flyout_NoAudioDevices", "No audio devices"),
                flyoutPalette,
                Layout.EmptyDevicesFontSize);
            empty.Opacity = Layout.EmptyDevicesOpacity;
            empty.Foreground = Brush(flyoutPalette.SecondaryForeground);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            empty.Margin = Layout.EmptyDevicesMargin;
            empty.IsVisible = devices.Count == 0;
            body.Children.Add(empty);

            DockPanel root = new() { LastChildFill = true };
            Control header = BuildHeader(flyoutPalette);
            DockPanel.SetDock(header, _settings.FlyoutHeaderAtBottom ? Dock.Bottom : Dock.Top);
            root.Children.Add(header);
            root.Children.Add(body);

            Border chrome = new()
            {
                Background = Brush(flyoutPalette.Background),
                BorderBrush = Brush(flyoutPalette.Border),
                BorderThickness = Layout.ChromeBorderThickness,
                CornerRadius = Rounded(Layout.ChromeCornerRadius),
                ClipToBounds = false,
                Child = new Border
                {
                    Background = Brush(flyoutPalette.Background),
                    CornerRadius = Rounded(Layout.ChromeInnerCornerRadius),
                    ClipToBounds = true,
                    Margin = Layout.ChromeInnerMargin,
                    Child = root
                }
            };
            chrome.PointerPressed += OnChromePointerPressed;
            chrome.PointerMoved += OnChromePointerMoved;
            chrome.PointerReleased += OnChromePointerReleased;
            chrome.PointerCaptureLost += OnChromePointerCaptureLost;
            resources.Add(() =>
            {
                chrome.PointerPressed -= OnChromePointerPressed;
                chrome.PointerMoved -= OnChromePointerMoved;
                chrome.PointerReleased -= OnChromePointerReleased;
                chrome.PointerCaptureLost -= OnChromePointerCaptureLost;
            });

            // Retire interaction and drag state before controls release pointer capture
            resources.Add(candidate.Dispose);
            candidate.Generation = new UIContentGeneration(
                "VolumeFlyoutWindow.Content",
                chrome,
                resources,
                logError: exception => TADNLog.Log(
                    $"Volume flyout generation release failed: {exception.Message}"));
            return candidate;
        }
        catch
        {
            candidate.Dispose();
            resources.Dispose();
            throw;
        }
        finally
        {
            _buildingContent = null;
        }
    }

    /// <summary>
    /// Queues one coalesced rebuild and retires stale hidden content until the next show.
    /// </summary>
    private void QueueRebuild()
    {
        if (_isClosed) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(QueueRebuild, DispatcherPriority.Background);
            return;
        }

        if (_layout == null) return;
        if (!IsVisible && !IsWarmPriming)
        {
            _rebuildPending = true;
            RetireActiveContentGeneration();
            return;
        }

        if (IsContentInteractionActive() || _isRebuilding)
        {
            _rebuildPending = true;
            return;
        }

        if (_rebuildQueued) return;

        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_isClosed) return;
            _rebuildQueued = false;
            if (!IsVisible && !IsWarmPriming)
            {
                _rebuildPending = true;
                RetireActiveContentGeneration();
                return;
            }

            Rebuild();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Adds a cleanup action to the unpublished generation currently being built.
    /// </summary>
    private void AddCleanup(Action cleanup) => BuildingContent.Resources.Add(cleanup);

    private VolumeFlyoutContentGeneration BuildingContent =>
        _buildingContent ?? throw new InvalidOperationException("No Volume flyout generation is being built.");

    private void RetireActiveContentGeneration()
    {
        _activeContent = null;
        DisposeContentGeneration();
    }

    private static void BeginVolumeSliderDrag(VolumeFlyoutContentGeneration content) =>
        content.ActiveVolumeSliderDragCount++;

    private void EndVolumeSliderDrag(VolumeFlyoutContentGeneration content)
    {
        content.ActiveVolumeSliderDragCount = Math.Max(0, content.ActiveVolumeSliderDragCount - 1);
        if (_isClosed || content.Resources.IsDisposed || !ReferenceEquals(_activeContent, content)) return;
        FlushPendingRebuild();
    }

    private void FlushPendingRebuild()
    {
        if (_isClosed || !_rebuildPending || _isRebuilding || IsContentInteractionActive())
            return;

        _rebuildPending = false;
        QueueRebuild();
    }

    private bool IsContentInteractionActive() =>
        _activeContent is { } content
        && (content.ActiveVolumeSliderDragCount > 0
            || content.IsDraggingWindow
            || content.UndockButtonController?.IsPointerCaptured == true);

    private void RestoreCellsScrollOffset(VolumeFlyoutContentGeneration content, double offset)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_activeContent, content)) return;
            ScrollViewer? scroll = content.CellsScrollViewer;
            if (scroll == null) return;

            double maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(offset, 0, maxOffset));
        }, DispatcherPriority.Loaded);
    }

    private Grid BuildHeader(FlyoutPalette p)
    {
        Grid grid = new() { MinHeight = Layout.HeaderMinHeight, Background = Brush(p.Background) };
        bool bottomHeader = _settings.FlyoutHeaderAtBottom;

        StackPanel left = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = HeaderVerticalAlignment(),
            Margin = bottomHeader ? CenteredHeaderMargin(Layout.HeaderLeftMarginBottom) : Layout.HeaderLeftMarginTop
        };

        Border settingsButton = HeaderIconButton(GlyphCatalog.SETTINGS, p, _openSettings,
            L("Flyout_Settings_Tooltip", "Settings"));
        SuppressNextAutoHideWhenPressed(settingsButton);
        left.Children.Add(settingsButton);
        left.Children.Add(HeaderIconButton(GlyphCatalog.SOUND_SETTINGS, p,
            () => DeviceShellLinks.OpenSoundSettings(_settings.SoundSettingsTarget),
            L("Flyout_SoundSettings_Tooltip", "Sound settings")));
        left.Children.Add(HeaderIconButton(
            DisabledDevicesGlyph,
            p,
            ToggleDisabledDevices,
            L("Flyout_DisabledDevices_Tooltip", "Show/hide disabled devices")));

        if (ShowCommunicationsButton)
        {
            Border communications = HeaderIconButton(
                GlyphCatalog.COMMUNICATIONS_ACTIVITY,
                p,
                e => ToggleCommunicationsDucking(e.KeyModifiers),
                L("Flyout_Communications_Tooltip", "Communications"));
            communications.Opacity = CommunicationsDucking.IsActive() ? 1.0 : 0.4;
            left.Children.Add(communications);
        }

        left.Children.Add(HeaderIconButton(null, p, () => { }, null, enabled: false));
        grid.Children.Add(left);

        if (IsUpdateButtonVisible)
        {
            Border update = TextButton(L("Flyout_Update_ButtonText", "Update!"), p, ShowUpdateConfirmation);
            SuppressNextAutoHideWhenPressed(update);
            update.Width = Layout.HeaderUpdateWidth;
            update.Height = Layout.HeaderUpdateHeight;
            update.BorderThickness = Layout.HeaderUpdateBorderThickness;
            update.CornerRadius = Rounded(Layout.HeaderUpdateCornerRadius);
            if (update.Child is TextBlock updateLabel)
                updateLabel.FontSize = Layout.HeaderUpdateLabelFontSize;
            update.HorizontalAlignment = HorizontalAlignment.Right;
            update.VerticalAlignment = HeaderVerticalAlignment();
            update.Margin = bottomHeader
                ? CenteredHeaderMargin(Layout.HeaderUpdateMarginBottom)
                : Layout.HeaderUpdateMarginTop;
            TrayAppDotNETToolTip.SetTip(update, L("Flyout_Update_Tooltip", "Install update"));
            update.SetValue(ZIndexProperty, Layout.HeaderUpdateZIndex);
            grid.Children.Add(update);
        }

        Border undock = BuildUndockButton(p);
        undock.IsVisible = _settings.AllowFlyoutUndock;
        undock.HorizontalAlignment = HorizontalAlignment.Right;
        undock.VerticalAlignment = HeaderVerticalAlignment();
        undock.Margin = bottomHeader
            ? CenteredHeaderMargin(Layout.HeaderUndockMarginBottom)
            : Layout.HeaderUndockMarginTop;
        grid.Children.Add(undock);

        return grid;
    }

    private VerticalAlignment HeaderVerticalAlignment() =>
        _settings.FlyoutHeaderAtBottom ? VerticalAlignment.Center : VerticalAlignment.Top;

    private static Thickness CenteredHeaderMargin(Thickness margin) =>
        new(margin.Left, 0, margin.Right, 0);

    private Grid BuildCell(AudioDevice device, FlyoutPalette p, bool isFirst, bool isLast)
    {
        List<AudioAppGroup> groups = VisibleGroups(device);
        bool expanded = IsAppDrawerExpanded(device);
        bool drawerVisible = groups.Count > 0 && expanded;
        UpdateGroupMeterVisibility(device, groups, drawerVisible);
        bool appsBottom = _settings.FlyoutDeviceLayout == FlyoutDeviceLayoutStyle.AppsBelowDevice;

        Grid root = new();
        root.Children.Add(new Border
        {
            Background = Brush(p.FooterBackground),
            CornerRadius = Rounded(Layout.DeviceCornerRadius),
            Margin = Layout.DeviceCellOuterMargin,
            IsHitTestVisible = false
        });

        Border contentInset = new() { Padding = Layout.DeviceCellContentPadding };
        Grid content = new();
        content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        bool gridDrawer = UsesGridDrawer(device);
        Control drawer = gridDrawer
            ? BuildIconGridDrawer(device, groups, p)
            : BuildSliderDrawer(device, groups, p);

        Border appBand = new()
        {
            Background = Brush(p.FooterBackground),
            IsVisible = drawerVisible,
            Padding = gridDrawer
                ? Layout.DeviceAppBandGridPadding
                : appsBottom
                    ? Layout.DeviceAppBandSliderBottomPadding
                    : Layout.DeviceAppBandSliderTopPadding,
            Margin = gridDrawer
                ? appsBottom ? Layout.DeviceAppBandGridBottomMargin : Layout.DeviceAppBandGridTopMargin
                : Layout.ZeroThickness,
            CornerRadius = isLast && appsBottom ? FooterBottomRadius : Layout.ZeroCornerRadius,
            Child = drawer
        };
        Grid.SetRow(appBand, appsBottom ? 1 : 0);
        content.Children.Add(appBand);

        Border deviceBand = new()
        {
            Background = Brush(p.FooterBackground),
            Padding = ResolveDeviceBandPadding(appsBottom),
            CornerRadius = ResolveDeviceBandRadius(isLast, appsBottom, drawerVisible),
            Child = BuildDeviceRow(device, groups, p)
        };
        Grid.SetRow(deviceBand, appsBottom ? 0 : 1);
        content.Children.Add(deviceBand);

        contentInset.Child = content;
        root.Children.Add(contentInset);

        root.Children.Add(new Border
        {
            BorderBrush = Brush(p.SliderTrack, 0.4),
            BorderThickness = Layout.DeviceOutlineBorderThickness,
            CornerRadius = Rounded(Layout.DeviceCornerRadius),
            Margin = Layout.DeviceCellOuterMargin,
            IsHitTestVisible = false
        });

        HookDeviceForRebuild(device);
        return root;
    }

    private ScrollViewer BuildSliderDrawer(AudioDevice device, IReadOnlyList<AudioAppGroup> groups, FlyoutPalette p)
    {
        StackPanel stack = new() { Spacing = 0 };
        foreach (AudioAppGroup group in groups)
            stack.Children.Add(BuildAppSliderRow(device, group, p));

        return new ScrollViewer
        {
            MaxHeight = ResolveSliderDrawerMaxHeight(device),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack
        };
    }

    private ScrollViewer BuildIconGridDrawer(AudioDevice device, IReadOnlyList<AudioAppGroup> groups, FlyoutPalette p)
    {
        WrapPanel panel = new()
        {
            ItemWidth = Layout.AppIconGridSlotSize,
            ItemHeight = Layout.AppIconGridSlotSize,
            HorizontalAlignment = _settings.AppDrawerIconsCenterMode == AppDrawerIconsCenterMode.Off
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center,
            Orientation = IsVerticalIconStackDirection ? Orientation.Vertical : Orientation.Horizontal,
            MaxWidth = Layout.AppIconGridSlotSize * Math.Max(1, AppDrawerIconsPerRow)
        };

        IEnumerable<AudioAppGroup> ordered = ResolveGridOrder(groups);
        foreach (AudioAppGroup group in ordered)
            panel.Children.Add(BuildAppIconCell(device, group, p));

        return new ScrollViewer
        {
            MaxHeight = ResolveGridDrawerMaxHeight(device),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel
        };
    }

    private Grid BuildAppSliderRow(AudioDevice device, AudioAppGroup group, FlyoutPalette p)
    {
        Grid grid = new()
        {
            Margin = Layout.AppSliderRowMargin,
            Opacity = ResolveAppOpacity(device, group),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        Control icon = BuildAppIcon(device, group, p, Layout.AppIconImageSize, Layout.AppIconGlyphSize,
            clickable: true);
        icon.Margin = Layout.AppSliderIconMargin;
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        FlyoutSlider slider = BuildVolumeSlider(
            group.Volume,
            group.PeakValues,
            p,
            v => group.Volume = (float)(v / 100.0),
            immediate => _feedback?.PlayForApp(group, immediate));
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);

        (Grid percentHost, TextBlock percent, TextBox percentEdit) = BuildPercentEditor(group.Volume, p);
        WirePercentEditor(percent, percentEdit, slider, v =>
        {
            group.Volume = (float)(v / 100.0);
            _feedback?.PlayForApp(group, immediate: true);
        });
        Grid.SetColumn(percentHost, 2);
        grid.Children.Add(percentHost);

        bool isUserAdjusting = false;
        bool hasDeferredVolume = false;
        slider.UserAdjustmentStarted += (_, _) => isUserAdjusting = true;
        slider.UserAdjustmentCompleted += (_, _) =>
        {
            isUserAdjusting = false;
            if (!hasDeferredVolume) return;
            hasDeferredVolume = false;
            ApplyGroupVolume();
        };
        slider.ValueChanged += (_, value) =>
        {
            if (!isUserAdjusting) return;
            percent.Text = ScalarText((float)(value / 100.0));
        };

        group.PropertyChanged += OnGroupChanged;
        AddCleanup(() => group.PropertyChanged -= OnGroupChanged);
        return grid;

        void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AudioAppGroup.Volume) when isUserAdjusting:
                    hasDeferredVolume = true;
                    return;
                case nameof(AudioAppGroup.Volume):
                    ApplyGroupVolume();
                    break;
                case nameof(AudioAppGroup.PeakValues):
                    slider.PeakValues = SliderPeaks(group.PeakValues);
                    break;
                case nameof(AudioAppGroup.IsMuted) or nameof(AudioAppGroup.State)
                    or nameof(AudioAppGroup.Icon):
                    QueueRebuild();
                    break;
            }
        }

        void ApplyGroupVolume()
        {
            slider.Value = group.Volume * 100.0;
            percent.Text = ScalarText(group.Volume);
        }
    }

    private Grid BuildAppIconCell(AudioDevice device, AudioAppGroup group, FlyoutPalette p)
    {
        double scale = _settings.AppDrawerIconScalePercent / 100.0;
        double imageSize = Layout.AppIconImageSize * scale;
        double glyphSize = Layout.AppIconGlyphSize * scale;
        double pillSize = Math.Min(Layout.AppIconGridSlotSize, imageSize + Layout.AppIconCellPillExtra);

        Grid cell = new()
        {
            Width = Layout.AppIconGridSlotSize,
            Height = Layout.AppIconGridSlotSize,
            Background = Brushes.Transparent,
            Cursor = device.IsCaptureDevice ? TrayAppDotNETCursors.Arrow : TrayAppDotNETCursors.Hand,
            Opacity = ResolveAppOpacity(device, group)
        };
        TrayAppDotNETToolTip.SetTip(cell, group.TooltipText);

        Border hover = new()
        {
            Width = pillSize,
            Height = pillSize,
            CornerRadius = Rounded(Layout.AppIconHoverCornerRadius),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };
        cell.PointerEntered += (_, _) => hover.Background = Brush(p.ButtonHover);
        cell.PointerExited += (_, _) => hover.Background = Brushes.Transparent;
        cell.Children.Add(hover);

        Control icon = BuildAppIcon(device, group, p, imageSize, glyphSize, clickable: false);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        cell.Children.Add(icon);

        switch (device.IsCaptureDevice)
        {
            case true when _settings.CaptureActivityIndicator == CaptureActivityIndicator.ActiveGlyph
                           && group.State == AudioSessionState.Active:
            {
                TextBlock badge = Text(device.IsExclusiveControlHeld ? GlyphCatalog.LOCK : GlyphCatalog.CIRCLE, p,
                    Layout.AppIconCellBadgeFontSize);
                badge.Foreground = Brush(p.IconForeground);
                badge.HorizontalAlignment = HorizontalAlignment.Right;
                badge.VerticalAlignment = VerticalAlignment.Bottom;
                badge.Margin = Layout.AppIconCellBadgeMargin;
                cell.Children.Add(badge);
                break;
            }
            case false:
                cell.PointerReleased += (_, e) =>
                {
                    if (e.InitialPressMouseButton != MouseButton.Left) return;
                    group.IsMuted = !group.IsMuted;
                    e.Handled = true;
                    Rebuild();
                };
                break;
        }

        group.PropertyChanged += OnGroupChanged;
        AddCleanup(() => group.PropertyChanged -= OnGroupChanged);
        return cell;

        void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AudioAppGroup.IsMuted) or nameof(AudioAppGroup.Icon)
                or nameof(AudioAppGroup.State))
                QueueRebuild();
        }
    }

    private Grid BuildAppIcon(
        AudioDevice device,
        AudioAppGroup group,
        FlyoutPalette p,
        double imageSize,
        double glyphSize,
        bool clickable)
    {
        Grid root = new()
        {
            Width = imageSize,
            Height = imageSize,
            Background = Brushes.Transparent,
            Cursor = clickable && !device.IsCaptureDevice
                ? TrayAppDotNETCursors.Hand
                : TrayAppDotNETCursors.Arrow
        };
        TrayAppDotNETToolTip.SetTip(root, group.TooltipText);

        AppIconResolver.IconHandle? iconHandle = group.AcquireIconHandle();
        if (iconHandle != null)
        {
            BuildingContent.Resources.Own(iconHandle);
            root.Children.Add(new Image
            {
                Source = iconHandle.Icon,
                Width = imageSize,
                Height = imageSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        else
        {
            TextBlock fallback = Text(GlyphCatalog.APP_FALLBACK, p, glyphSize);
            fallback.Foreground = Brush(p.IconForeground);
            fallback.HorizontalAlignment = HorizontalAlignment.Center;
            fallback.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(fallback);
        }

        TextBlock muteOverlay = Text(GlyphCatalog.APP_MUTE_OVERLAY, p, glyphSize);
        muteOverlay.Foreground = Brush(p.IconForeground);
        muteOverlay.HorizontalAlignment = HorizontalAlignment.Center;
        muteOverlay.VerticalAlignment = VerticalAlignment.Center;
        muteOverlay.Opacity = Layout.AppIconMuteOverlayOpacity;
        muteOverlay.IsVisible = group.IsMuted;
        muteOverlay.IsHitTestVisible = false;
        root.Children.Add(muteOverlay);

        root.PointerEntered += (_, _) =>
        {
            if (!device.IsCaptureDevice) muteOverlay.IsVisible = true;
        };
        root.PointerExited += (_, _) =>
        {
            if (!group.IsMuted) muteOverlay.IsVisible = false;
        };
        if (clickable && !device.IsCaptureDevice)
        {
            root.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                group.IsMuted = !group.IsMuted;
                e.Handled = true;
                Rebuild();
            };
        }

        return root;
    }

    private Grid BuildDeviceRow(AudioDevice device, IReadOnlyList<AudioAppGroup> groups, FlyoutPalette p)
    {
        Grid grid = new();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid titleRow = BuildDeviceTitleRow(device, groups, p);
        Grid.SetRow(titleRow, DeviceTitleRowIndex);
        grid.Children.Add(titleRow);

        Grid sliderRow = BuildDeviceSliderRow(device, p);
        Grid.SetRow(sliderRow, DeviceSliderRowIndex);
        grid.Children.Add(sliderRow);

        return grid;
    }

    private Grid BuildDeviceTitleRow(AudioDevice device, IReadOnlyList<AudioAppGroup> groups, FlyoutPalette p)
    {
        VolumeFlyoutContentGeneration content = BuildingContent;
        Grid row = new()
        {
            Margin = _settings.FlyoutDeviceTitlePosition == FlyoutDeviceTitlePosition.AboveSlider
                ? Layout.DeviceTitleRowMarginAbove
                : Layout.DeviceTitleRowMarginInline,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        Grid nameStack = new() { Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        if (_settings is { ShowDeviceFormatText: false, ShowDeviceCodecText: false })
            nameStack.RenderTransform = CloneTransform(Layout.DeviceTitleNameNoFormatTransform);

        TextBlock name = Text(device.FriendlyName, p, Layout.DeviceTitleFontSize);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;
        nameStack.PointerPressed += (_, e) =>
        {
            if (e.ClickCount != 2 || e.GetCurrentPoint(nameStack).Properties.PointerUpdateKind !=
                PointerUpdateKind.LeftButtonPressed) return;
            BeginDeviceNameEdit(content, nameStack, device, p);
            e.Handled = true;
        };
        nameStack.Children.Add(name);

        string formatLine = DeviceFormatLine(device);
        if (!string.IsNullOrEmpty(formatLine))
        {
            Canvas formatCanvas = new()
            {
                ClipToBounds = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock format = Text(formatLine, p, Layout.DeviceFormatFontSize);
            format.Background = Brushes.Transparent;
            format.Opacity = Layout.DeviceFormatOpacity;
            format.TextTrimming = TextTrimming.CharacterEllipsis;
            Canvas.SetTop(format, Layout.DeviceFormatCanvasTop);
            formatCanvas.Children.Add(format);
            nameStack.Children.Add(formatCanvas);
        }

        nameStack.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Right) return;
            ShowDefaultFormatMenu(content, nameStack, device, p);
            e.Handled = true;
        };

        row.Children.Add(nameStack);

        int col = 1;
        AddTitleButton(row, col++, BuildBatteryButton(device, p));
        AddTitleButton(row, col++, BuildExclusiveButton(device, p));
        AddTitleButton(row, col++, BuildEqualizerButton(device, p));
        AddTitleButton(row, col++, BuildListenButton(device, p));
        AddTitleButton(row, col++, BuildDeviceStateButton(device, p));
        AddTitleButton(row, col, BuildDrawerButton(device, groups, p));

        device.PropertyChanged += OnDeviceChanged;
        AddCleanup(() => device.PropertyChanged -= OnDeviceChanged);
        return row;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AudioDevice.FriendlyName):
                    name.Text = device.FriendlyName;
                    break;
                case nameof(AudioDevice.IsDefault) or nameof(AudioDevice.IsDefaultCommunications)
                    when !IsDefaultDeviceButtonVisible(device):
                    RunOnUIThread(() => QueueDeviceOrderingRebuild(content));
                    break;
                default:
                {
                    if (DeviceRebuildProperties.Contains(e.PropertyName ?? string.Empty))
                        QueueRebuild();
                    break;
                }
            }
        }
    }

    private Thickness ResolveDeviceBandPadding(bool appsBottom)
    {
        if (_settings.FlyoutDeviceTitlePosition == FlyoutDeviceTitlePosition.AboveSlider)
            return appsBottom
                ? Layout.DeviceBandBottomPaddingSliderAboveTitle
                : Layout.DeviceBandTopPaddingSliderAboveTitle;

        return appsBottom ? Layout.DeviceBandBottomPadding : Layout.DeviceBandTopPadding;
    }

    private Grid BuildDeviceSliderRow(AudioDevice device, FlyoutPalette p)
    {
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        Border mute = BuildDeviceMuteButton(device, p);
        Grid.SetColumn(mute, 0);
        row.Children.Add(mute);

        FlyoutSlider slider = BuildVolumeSlider(
            device.Volume,
            device.PeakValues,
            p,
            v => device.Volume = (float)(v / 100.0),
            immediate => _feedback?.PlayForDevice(device, immediate));
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);

        (Grid percentHost, TextBlock percent, TextBox percentEdit) = BuildPercentEditor(device.Volume, p);
        WirePercentEditor(percent, percentEdit, slider, v =>
        {
            device.Volume = (float)(v / 100.0);
            _feedback?.PlayForDevice(device, immediate: true);
        });
        Grid.SetColumn(percentHost, 2);
        row.Children.Add(percentHost);

        bool isUserAdjusting = false;
        bool hasDeferredVolume = false;
        slider.UserAdjustmentStarted += (_, _) => isUserAdjusting = true;
        slider.UserAdjustmentCompleted += (_, _) =>
        {
            isUserAdjusting = false;
            if (!hasDeferredVolume) return;
            hasDeferredVolume = false;
            ApplyDeviceVolume();
        };
        slider.ValueChanged += (_, value) =>
        {
            if (!isUserAdjusting) return;
            percent.Text = ScalarText((float)(value / 100.0));
        };

        device.PropertyChanged += OnDeviceChanged;
        AddCleanup(() => device.PropertyChanged -= OnDeviceChanged);
        UpdateMutedActiveVisuals();
        return row;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AudioDevice.Volume) when isUserAdjusting:
                    hasDeferredVolume = true;
                    return;
                case nameof(AudioDevice.Volume):
                    ApplyDeviceVolume();
                    break;
                case nameof(AudioDevice.PeakValues):
                    slider.PeakValues = SliderPeaks(device.PeakValues);
                    break;
                case nameof(AudioDevice.IsMuted):
                    RunOnUIThread(UpdateMutedActiveVisuals);
                    break;
                case nameof(AudioDevice.IsActive) or nameof(AudioDevice.State):
                    QueueRebuild();
                    break;
            }
        }

        void UpdateMutedActiveVisuals()
        {
            double opacity = device.IsMuted || !device.IsActive ? 0.4 : 1.0;
            slider.Opacity = opacity;
            percentHost.Opacity = opacity;
        }

        void ApplyDeviceVolume()
        {
            slider.Value = device.Volume * 100.0;
            percent.Text = ScalarText(device.Volume);
        }
    }

    private FlyoutSlider BuildVolumeSlider(
        float scalar,
        MeterPeakValues peaks,
        FlyoutPalette p,
        Action<double> setPercent,
        Action<bool> playFeedback)
    {
        VolumeFlyoutContentGeneration content = BuildingContent;
        FlyoutSlider slider = new()
        {
            Value = scalar * 100.0,
            PeakValues = SliderPeaks(peaks),
            Thumb = ResolveSliderThumb(),
            WheelStepPercent = _settings.WheelVolumeStepPercent,
            HitTestVerticalPadding = Layout.SliderHitTestVerticalPadding,
            TrackColor = p.SliderTrack,
            ProgressColor = p.SliderProgress,
            ThumbColor = p.SliderThumb,
            MeterPeakColor = p.MeterPeak,
            MeterPeakStereoColor = p.MeterPeakStereo,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        content.Resources.Own(slider);

        bool updating = false;
        bool dragging = false;
        slider.DragStarted += (_, _) =>
        {
            if (dragging) return;
            dragging = true;
            BeginVolumeSliderDrag(content);
        };
        slider.DragCompleted += (_, _) =>
        {
            if (dragging)
            {
                dragging = false;
                EndVolumeSliderDrag(content);
            }

            playFeedback(true);
        };
        slider.ValueChanged += (_, value) =>
        {
            if (updating) return;
            updating = true;
            try
            {
                setPercent(value);
                if (!dragging) playFeedback(false);
            }
            finally { updating = false; }
        };
        return slider;
    }

    private static FlyoutSliderPeakValues SliderPeaks(MeterPeakValues peaks) => new(peaks.Min, peaks.Max);

    private SliderThumbGlyphOption ResolveSliderThumb() =>
        _settings.SliderThumbOptions.FirstOrDefault(o => o.Name == _settings.SliderThumbGlyph)
        ?? SliderThumbGlyphOption.CreateDefaults()[0];

    private (Grid Host, TextBlock Label, TextBox Editor) BuildPercentEditor(float scalar, FlyoutPalette p)
    {
        Grid host = new()
        {
            MinWidth = Layout.PercentHostMinWidth,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = Layout.PercentHostMargin
        };

        TextBlock label = Text(ScalarText(scalar), p, Layout.PercentFontSize);
        label.Background = Brushes.Transparent;
        label.TextAlignment = TextAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Cursor = TrayAppDotNETCursors.IBeam;

        TextBox editor = new()
        {
            IsVisible = false,
            MinWidth = Layout.PercentEditorMinWidth,
            FontSize = Layout.PercentFontSize,
            Background = Brush(p.Background),
            Foreground = Brush(p.Foreground),
            CaretBrush = Brush(p.Foreground),
            BorderBrush = Brush(p.Border),
            BorderThickness = Layout.PercentEditorBorderThickness,
            Padding = Layout.PercentEditorPadding,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };

        host.Children.Add(label);
        host.Children.Add(editor);
        return (host, label, editor);
    }

    private void WirePercentEditor(TextBlock label, TextBox editor, FlyoutSlider slider,
        Action<double> setPercent)
    {
        label.PointerPressed += OnLabelPointerPressed;
        editor.KeyDown += OnEditorKeyDown;
        editor.LostFocus += OnEditorLostFocus;
        BuildingContent.Resources.Add(() =>
        {
            label.PointerPressed -= OnLabelPointerPressed;
            editor.KeyDown -= OnEditorKeyDown;
            editor.LostFocus -= OnEditorLostFocus;
            editor.IsVisible = false;
            editor.Text = null;
        });
        return;

        void OnLabelPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (!slider.IsEnabled || !slider.IsVisible) return;

            editor.Text = label.Text;
            label.IsVisible = false;
            editor.IsVisible = true;
            editor.Focus();
            editor.SelectAll();
            e.Handled = true;
        }

        void OnEditorKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CollapsePercentEditor(label, editor);
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Enter) return;
            CommitPercentEditor(label, editor, slider, setPercent);
            e.Handled = true;
        }

        void OnEditorLostFocus(object? sender, RoutedEventArgs e)
        {
            if (!editor.IsVisible) return;
            CommitPercentEditor(label, editor, slider, setPercent);
        }
    }

    private static void CommitPercentEditor(TextBlock label, TextBox editor, FlyoutSlider slider,
        Action<double> setPercent)
    {
        string text = editor.Text ?? string.Empty;
        CollapsePercentEditor(label, editor);

        if (!TryParseSliderPercent(text, out double value)) return;
        double clamped = Math.Clamp(value, 0, 100);
        bool changed = Math.Abs(slider.Value - clamped) > 0.001;
        slider.Value = clamped;
        if (!changed) return;
        setPercent(clamped);
    }

    private static void CollapsePercentEditor(TextBlock label, TextBox editor)
    {
        editor.IsVisible = false;
        label.IsVisible = true;
    }

    private static bool TryParseSliderPercent(string text, out double value)
    {
        string trimmed = text.Trim();
        if (trimmed.EndsWith('%'))
            trimmed = trimmed[..^1].Trim();

        bool parsed = double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                      || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        return parsed && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private Border BuildDeviceMuteButton(AudioDevice device, FlyoutPalette p)
    {
        Glyph normalGlyph = DeviceVolumeGlyph(device);
        bool isPointerOver = false;
        TextBlock glyph = Text(normalGlyph, p, Layout.DeviceMuteGlyphFontSize);
        glyph.Foreground = Brush(p.IconForeground);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        ApplyDeviceMuteGlyphStyle(glyph, normalGlyph);

        Grid slot = new()
        {
            Width = Layout.DeviceMuteSlotWidth,
            Height = Layout.DeviceMuteSlotHeight,
            ClipToBounds = false,
            Children = { glyph }
        };

        Border button = DeviceIconButton(null, p, () => device.IsMuted = !device.IsMuted,
            width: Layout.DeviceMuteButtonWidth, height: Layout.DeviceMuteButtonHeight);
        button.Margin = Layout.DeviceMuteButtonMargin;
        button.Child = slot;
        UpdateVisual();

        button.PointerEntered += (_, _) =>
        {
            isPointerOver = true;
            UpdateVisual();
        };
        button.PointerExited += (_, _) =>
        {
            isPointerOver = false;
            UpdateVisual();
        };

        device.PropertyChanged += OnDeviceChanged;
        AddCleanup(() => device.PropertyChanged -= OnDeviceChanged);

        return button;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(AudioDevice.Volume)
                or nameof(AudioDevice.IsMuted)
                or nameof(AudioDevice.IsActive)
                or nameof(AudioDevice.IsCaptureSleeping)
                or nameof(AudioDevice.IsListeningToThisDevice))) return;

            RunOnUIThread(UpdateVisual);
        }

        void UpdateVisual()
        {
            normalGlyph = DeviceVolumeGlyph(device);
            Glyph visibleGlyph = isPointerOver ? DeviceMuteTogglePreviewGlyph(device) : normalGlyph;
            ApplyDeviceMuteGlyphStyle(glyph, visibleGlyph);

            button.Opacity = device.IsMuted || !device.IsActive ? 0.4 : 1.0;
            TrayAppDotNETToolTip.SetTip(
                button,
                device.IsMuted
                    ? L("Flyout_DeviceUnmute_Tooltip", "Unmute")
                    : L("Flyout_DeviceMute_Tooltip", "Mute"));
        }
    }

    private void ApplyDeviceMuteGlyphStyle(TextBlock glyphHost, Glyph glyph)
    {
        glyphHost.FontSize = Layout.DeviceMuteGlyphFontSize;
        glyphHost.FontWeight = FontWeight.Normal;
        glyphHost.HorizontalAlignment = IsMicrophoneGlyph(glyph)
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Right;
        glyphHost.RenderTransform = null;
        GlyphApplicator.ApplyTo(glyphHost, glyph);
        PreventIconGlyphClipping(glyphHost, Layout.IconGlyphLineHeightPadding);
    }

    private static bool IsMicrophoneGlyph(Glyph glyph) =>
        glyph.Text == GlyphCatalog.MICROPHONE.Text
        || glyph.Text == GlyphCatalog.MICROPHONE_OFF.Text
        || glyph.Text == GlyphCatalog.MICROPHONE_LISTENING.Text
        || glyph.Text == GlyphCatalog.MICROPHONE_SLEEP.Text;

    private Border? BuildBatteryButton(AudioDevice device, FlyoutPalette p)
    {
        bool visible = device.IsBluetooth
                       && (device.IsCaptureDevice
                           ? _settings.ShowBatteryButtonForRecording
                           : _settings.ShowBatteryButtonForPlayback);
        if (!visible) return null;

        int? displayedBatteryLevel = ResolveBluetoothBatteryLevel(
            device.IsDisconnected,
            device.BatteryLevel,
            device.LastKnownBatteryLevel);
        Glyph glyph = displayedBatteryLevel.HasValue
            ? BatteryGlyph(displayedBatteryLevel.Value)
            : GlyphCatalog.BLUETOOTH;

        Border button = DeviceIconButton(glyph, p, e =>
        {
            if (device.IsDisconnected)
                _audioManager.ConnectBluetoothDevice(device);
            else if ((e.KeyModifiers & KeyModifiers.Control) != 0)
                _audioManager.DisconnectBluetoothDevice(device);
        });
        button.Margin = Layout.BluetoothBatteryButtonMargin;
        button.Focusable = false;
        button.Opacity = device.IsActive ? 1.0 : 0.4;
        TrayAppDotNETToolTip.SetTip(button, BluetoothButtonTooltip(device.IsDisconnected, displayedBatteryLevel));
        return button;
    }

    internal static int? ResolveBluetoothBatteryLevel(
        bool isDisconnected,
        int? currentBatteryLevel,
        int? lastKnownBatteryLevel) =>
        isDisconnected ? lastKnownBatteryLevel : currentBatteryLevel;

    private static string BluetoothButtonTooltip(bool isDisconnected, int? displayedBatteryLevel)
    {
        if (isDisconnected)
        {
            return displayedBatteryLevel.HasValue
                ? string.Format(
                    L("Flyout_BatteryButton_Tooltip_Disconnected_Format",
                        "Battery: {0}% (last known)\nClick to connect"),
                    displayedBatteryLevel.Value)
                : L("Flyout_BluetoothButton_Tooltip_Disconnected", "Bluetooth\nClick to connect");
        }

        return displayedBatteryLevel.HasValue
            ? string.Format(
                L("Flyout_BatteryButton_Tooltip_Format", "Battery: {0}%\nCtrl+click to disconnect"),
                displayedBatteryLevel.Value)
            : L("Flyout_BluetoothButton_Tooltip_Connected", "Bluetooth\nCtrl+click to disconnect");
    }

    private Border? BuildExclusiveButton(AudioDevice device, FlyoutPalette p)
    {
        bool visible = device.IsCaptureDevice
            ? _settings.ShowLockButtonForRecording
            : _settings.ShowLockButtonForPlayback;
        if (!visible) return null;

        Border button = DeviceIconButton(ExclusiveButtonGlyph(device), p, device.ToggleAllowExclusiveControl);
        TextBlock? glyph = button.Child as TextBlock;
        UpdateVisual();

        device.PropertyChanged += OnDeviceChanged;
        AddCleanup(() => device.PropertyChanged -= OnDeviceChanged);
        return button;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(AudioDevice.IsExclusiveModeAllowed)
                or nameof(AudioDevice.IsExclusiveControlHeld))) return;

            RunOnUIThread(UpdateVisual);
        }

        void UpdateVisual()
        {
            if (glyph != null) GlyphApplicator.ApplyTo(glyph, ExclusiveButtonGlyph(device));
            button.Opacity = device.IsExclusiveModeAllowed ? 1.0 : 0.4;
            TrayAppDotNETToolTip.SetTip(button, device.IsExclusiveModeAllowed
                ? device.IsExclusiveControlHeld
                    ? L("Flyout_ExclusiveMode_Tooltip_Held", "Exclusive control is held")
                    : L("Flyout_ExclusiveMode_Tooltip_Allowed", "Exclusive mode allowed")
                : L("Flyout_ExclusiveMode_Tooltip_Disallowed", "Exclusive mode disallowed"));
        }
    }

    private Border? BuildEqualizerButton(AudioDevice device, FlyoutPalette p)
    {
        bool visible = device.IsCaptureDevice
            ? _settings.ShowEqualizerAPOButtonForRecording
            : _settings.ShowEqualizerAPOButtonForPlayback;
        if (!visible) return null;

        Border button = DeviceIconButton(null, p, e =>
        {
            try
            {
                if ((e.KeyModifiers & KeyModifiers.Control) != 0)
                    EqualizerAPOMonitor.OpenConfigurationEditor(device);
                else if (device.EqualizerAPOState == EqualizerAPOState.NotAvailable)
                    ShowEqualizerAPONotAvailableDialog();
                else
                    device.ToggleEqualizerAPO();
            }
            catch (Exception ex) { TADNLog.Log($"VolumeFlyout.EqualizerAPO: {ex.Message}"); }
        }, rightClick: _ => EqualizerAPOMonitor.OpenConfigurationEditor(device));

        Grid glyphs = new() { ClipToBounds = false };
        TextBlock equalizer = Text(GlyphCatalog.EQUALIZER, p, Layout.EqualizerFontSize);
        equalizer.Foreground = Brush(p.IconForeground);
        PreventIconGlyphClipping(equalizer, Layout.IconGlyphLineHeightPadding);
        glyphs.Children.Add(equalizer);
        TextBlock badge = Text(GlyphCatalog.SIGNAL_NOT_CONNECTED, p, Layout.EqualizerBadgeFontSize,
            FontWeight.ExtraBold);
        badge.Foreground = Brush(p.IconForeground);
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        badge.VerticalAlignment = VerticalAlignment.Bottom;
        badge.Margin = Layout.EqualizerBadgeMargin;
        PreventIconGlyphClipping(badge, Layout.IconGlyphLineHeightPadding);
        glyphs.Children.Add(badge);

        button.Child = glyphs;
        UpdateVisual();

        device.PropertyChanged += OnDeviceChanged;
        AddCleanup(() => device.PropertyChanged -= OnDeviceChanged);
        return button;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AudioDevice.EqualizerAPOState)) return;
            RunOnUIThread(UpdateVisual);
        }

        void UpdateVisual()
        {
            equalizer.Opacity = device.EqualizerAPOState == EqualizerAPOState.Running ? 1.0 : 0.4;
            badge.IsVisible = device.EqualizerAPOState == EqualizerAPOState.NotAvailable;
            TrayAppDotNETToolTip.SetTip(button, EqualizerTooltip(device.EqualizerAPOState));
        }
    }

    private Border? BuildListenButton(AudioDevice device, FlyoutPalette p)
    {
        if (!device.IsCaptureDevice || !_settings.ShowListenButtonForRecording) return null;

        VolumeFlyoutContentGeneration content = BuildingContent;
        Border? button = null;
        button = DeviceIconButton(GlyphCatalog.EAR_LISTEN, p, e =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
                device.SetListenTarget(null, enable: true);
            else
                device.SetListenEnabled(!device.IsListeningToThisDevice);
        }, rightClick: _ => ShowListenTargetMenu(content, button!, device, p));
        TrayAppDotNETToolTip.SetTip(button, L("Flyout_ListenButton_Tooltip", "Listen to this device"));
        UpdateVisual();

        device.PropertyChanged += OnDeviceChanged;
        AddCleanup(() => device.PropertyChanged -= OnDeviceChanged);
        return button;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AudioDevice.IsListeningToThisDevice)) return;
            RunOnUIThread(UpdateVisual);
        }

        void UpdateVisual() => button.Opacity = device.IsListeningToThisDevice ? 1.0 : 0.4;
    }

    private Border? BuildDeviceStateButton(AudioDevice device, FlyoutPalette p)
    {
        if (!IsDefaultDeviceButtonVisible(device)) return null;

        VolumeFlyoutContentGeneration content = BuildingContent;
        Border button = DeviceIconButton(null, p, e =>
        {
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                device.SetAsDefaultCommunications();
            else if ((e.KeyModifiers & KeyModifiers.Control) != 0)
                device.SetEnabled(!device.IsActive);
            else
                device.SetAsDefault();
        }, rightClick: _ => DeviceShellLinks.OpenDeviceProperties(device));
        TextBlock glyph = Text(DeviceStateGlyph(device), p, Layout.DeviceStateFontSize);
        glyph.Foreground = Brush(p.IconForeground);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        button.Child = glyph;
        TrayAppDotNETToolTip.SetTip(button, L("Flyout_DeviceIcon_Tooltip", "Set as default device"));
        TrackDeviceStateButtonHover(content, button);
        UpdateVisual();

        device.PropertyChanged += OnDeviceChanged;
        AddCleanup(() => device.PropertyChanged -= OnDeviceChanged);
        return button;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(AudioDevice.IsDefault)
                or nameof(AudioDevice.IsDefaultCommunications)
                or nameof(AudioDevice.IsActive)
                or nameof(AudioDevice.State))) return;

            RunOnUIThread(() =>
            {
                UpdateVisual();
                if (e.PropertyName is nameof(AudioDevice.IsDefault) or nameof(AudioDevice.IsDefaultCommunications))
                    QueueDeviceOrderingRebuild(content);
            });
        }

        void UpdateVisual()
        {
            Glyph stateGlyph = DeviceStateGlyph(device);
            glyph.FontSize = Layout.DeviceStateFontSize;
            glyph.FontWeight = FontWeight.Normal;
            glyph.RenderTransform = null;
            GlyphApplicator.ApplyTo(glyph, stateGlyph);
            PreventIconGlyphClipping(glyph, Layout.IconGlyphLineHeightPadding);
            button.Opacity = device.IsActive ? 1.0 : 0.4;
        }
    }

    private bool IsDefaultDeviceButtonVisible(AudioDevice device) =>
        device.IsCaptureDevice
            ? _settings.ShowDefaultDeviceButtonForRecording
            : _settings.ShowDefaultDeviceButtonForPlayback;

    private void TrackDeviceStateButtonHover(VolumeFlyoutContentGeneration content, Control button)
    {
        bool pointerOver = false;
        button.PointerEntered += (_, _) =>
        {
            if (pointerOver) return;
            pointerOver = true;
            content.HoveredDeviceStateButtonCount++;
        };
        button.PointerExited += (_, _) =>
        {
            if (!pointerOver) return;
            pointerOver = false;
            content.HoveredDeviceStateButtonCount = Math.Max(0, content.HoveredDeviceStateButtonCount - 1);
            FlushDeviceOrderingRebuild(content);
        };
        AddCleanup(() =>
        {
            if (!pointerOver) return;
            pointerOver = false;
            content.HoveredDeviceStateButtonCount = Math.Max(0, content.HoveredDeviceStateButtonCount - 1);
        });
    }

    private void QueueDeviceOrderingRebuild(VolumeFlyoutContentGeneration content)
    {
        if (_settings.FlyoutDeviceSort != FlyoutDeviceSortOrder.StateGrouped) return;

        content.DeviceOrderingRebuildPending = true;
        FlushDeviceOrderingRebuild(content);
    }

    private void FlushDeviceOrderingRebuild(VolumeFlyoutContentGeneration content)
    {
        if (!content.DeviceOrderingRebuildPending || content.HoveredDeviceStateButtonCount > 0) return;
        if (!ReferenceEquals(_activeContent, content)) return;

        content.DeviceOrderingRebuildPending = false;
        QueueRebuild();
    }

    private Border BuildDrawerButton(AudioDevice device, IReadOnlyList<AudioAppGroup> groups, FlyoutPalette p)
    {
        bool hasGroups = groups.Count > 0;
        bool expanded = IsAppDrawerExpanded(device);
        Border button = DeviceIconButton(expanded ? GlyphCatalog.CHEVRON_UP_BIG : GlyphCatalog.CHEVRON_DOWN_BIG, p, () =>
        {
            if (!hasGroups) return;
            SetAppDrawerExpanded(device, !expanded);
            Rebuild();
            QueuePositionNearTray();
        }, enabled: hasGroups);
        button.Opacity = hasGroups ? 1.0 : 0.4;
        return button;
    }

    private static void AddTitleButton(Grid row, int column, Control? button)
    {
        if (button == null) return;
        Grid.SetColumn(button, column);
        row.Children.Add(button);
    }

    private Border HeaderIconButton(Glyph? glyph, FlyoutPalette p, Action click, string? tooltip,
        bool enabled = true) =>
        HeaderIconButton(glyph, p, _ => click(), tooltip, enabled);

    private Border HeaderIconButton(Glyph? glyph, FlyoutPalette p, Action<PointerReleasedEventArgs> click,
        string? tooltip, bool enabled = true)
    {
        Border button = IconButton(
            glyph,
            p,
            click,
            Layout.HeaderIconButtonWidth,
            Layout.HeaderIconButtonHeight,
            Layout.HeaderIconFontSize,
            enabled,
            _settings.FlyoutHeaderAtBottom
                ? CenteredHeaderMargin(Layout.HeaderIconButtonMargin)
                : Layout.HeaderIconButtonMargin,
            p.Pressed,
            p.Pressed,
            tooltip);
        button.CornerRadius = Rounded(Layout.HeaderIconButtonCornerRadius);
        if (button.Child is TextBlock text)
            text.LineHeight = Layout.HeaderIconLineHeight;
        return button;
    }

    private Border BuildUndockButton(FlyoutPalette p)
    {
        FlyoutControlPalette palette = new(
            p.Foreground,
            p.SecondaryForeground,
            p.Border,
            p.Pressed,
            p.Pressed,
            p.ControlBackground,
            p.Background,
            p.IconForeground,
            p.SliderTrack,
            p.SliderProgress,
            p.SliderThumb);
        VolumeFlyoutContentGeneration content = BuildingContent;
        FlyoutUndockButtonController controller = content.Resources.Own(
            new FlyoutUndockButtonController(new FlyoutUndockButtonOptions
            {
                Width = Layout.HeaderIconButtonWidth,
                Height = Layout.HeaderIconButtonHeight,
                FontSize = Layout.HeaderUndockFontSize,
                FontWeight = FontWeight.Normal,
                CornerRadius = Rounded(Layout.HeaderIconButtonCornerRadius),
                IsVisible = _settings.AllowFlyoutUndock,
                Owner = this,
                Docking = Docking,
                Palette = palette,
                CanStartInteraction = () =>
                    !_isClosed
                    && !content.Resources.IsDisposed
                    && ReferenceEquals(_activeContent, content),
                DraggingChanged = dragging => content.IsDraggingWindow = dragging,
                InteractionCompleted = committedChange =>
                {
                    if (_isClosed || content.Resources.IsDisposed || !ReferenceEquals(_activeContent, content))
                        return;
                    if (committedChange == FlyoutDockStateChange.PositionSaved) Rebuild();
                    FlushPendingRebuild();
                },
                UndockTooltip = () => L("Flyout_Undock_Tooltip", "Undock"),
                RedockTooltip = () => L("Flyout_Redock_Tooltip", "Redock"),
                DragThreshold = Layout.DragThreshold
            }));
        controller.Glyph.Foreground = Brush(p.IconForeground);
        controller.Glyph.LineHeight = Layout.HeaderUndockLineHeight;
        content.UndockButtonController = controller;
        return controller.Button;
    }

    private Border DeviceIconButton(
        Glyph? glyph,
        FlyoutPalette p,
        Action click,
        double? width = null,
        double? height = null,
        double? fontSize = null,
        bool enabled = true) =>
        DeviceIconButton(glyph, p, _ => click(), null, width, height, fontSize, enabled);

    private Border DeviceIconButton(
        Glyph? glyph,
        FlyoutPalette p,
        Action<PointerReleasedEventArgs> click,
        Action<PointerReleasedEventArgs>? rightClick = null,
        double? width = null,
        double? height = null,
        double? fontSize = null,
        bool enabled = true) =>
        IconButton(
            glyph,
            p,
            click,
            width ?? Layout.DeviceIconButtonWidth,
            height ?? Layout.DeviceIconButtonHeight,
            fontSize ?? Layout.DeviceIconButtonFontSize,
            enabled,
            Layout.DeviceIconButtonMargin,
            p.ButtonHover,
            p.ButtonPressed,
            null,
            rightClick);

    private Border IconButton(
        Glyph? glyph,
        FlyoutPalette p,
        Action<PointerReleasedEventArgs> click,
        double width,
        double height,
        double fontSize,
        bool enabled,
        Thickness margin,
        Color hover,
        Color pressed,
        string? tooltip,
        Action<PointerReleasedEventArgs>? rightClick = null)
    {
        Control content;
        if (glyph == null || fontSize <= 0)
            content = new Grid { IsHitTestVisible = false, ClipToBounds = false };
        else
        {
            TextBlock text = Text(glyph, p, fontSize);
            text.Foreground = Brush(p.IconForeground);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            PreventIconGlyphClipping(text, Layout.IconGlyphLineHeightPadding);
            content = text;
        }

        Border button = new()
        {
            Width = width,
            Height = height,
            Margin = margin,
            CornerRadius = Rounded(Layout.IconButtonCornerRadius),
            Background = Brushes.Transparent,
            ClipToBounds = false,
            Child = content,
            Cursor = enabled ? TrayAppDotNETCursors.Hand : TrayAppDotNETCursors.Arrow,
            IsEnabled = enabled
        };
        if (tooltip != null) TrayAppDotNETToolTip.SetTip(button, tooltip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);

        FlyoutButtonState.Attach(
            button,
            () => Brushes.Transparent,
            () => Brush(hover),
            () => Brush(pressed),
            click,
            enabled,
            rightClick);
        return button;
    }

    private Border TextButton(string text, FlyoutPalette p, Action click)
    {
        TextBlock label = Text(text, p, Layout.TextButtonFontSize, FontWeight.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        Border button = new()
        {
            Background = Brush(p.ControlBackground),
            BorderBrush = Brush(p.Border),
            BorderThickness = Layout.TextButtonBorderThickness,
            CornerRadius = Rounded(Layout.TextButtonCornerRadius),
            Child = label,
            Cursor = TrayAppDotNETCursors.Hand
        };
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
        FlyoutButtonState.Attach(
            button,
            () => Brush(p.ControlBackground),
            () => Brush(p.ButtonHover),
            () => Brush(p.ButtonPressed),
            _ => click());
        return button;
    }

    /// <summary>
    /// Runs a UI action without letting observer update failures escape Avalonia callbacks.
    /// </summary>
    private static void RunOnUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RunUIAction(action);
            return;
        }

        Dispatcher.UIThread.Post(() => RunUIAction(action), DispatcherPriority.Background);
    }

    /// <summary>
    /// Executes one UI update and logs failures.
    /// </summary>
    private static void RunUIAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { TADNLog.Log($"VolumeFlyoutWindow UI action failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private void OnDockStateChanged(FlyoutDockStateChange change)
    {
        _activeContent?.UndockButtonController?.UpdateVisual();
        switch (change)
        {
            case FlyoutDockStateChange.Undocked:
                Rebuild();
                break;
            case FlyoutDockStateChange.Redocked:
                Rebuild();
                QueuePositionNearTray();
                break;
            case FlyoutDockStateChange.UndockedFromDrag:
            case FlyoutDockStateChange.PositionSaved:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
    }

    /// <summary>Shows or hides disabled playback and recording devices as one flyout action.</summary>
    private void ToggleDisabledDevices()
    {
        bool showDisabledDevices = !ShowsDisabledDevices;
        _settings.ShowDisabledPlaybackDevices = showDisabledDevices;
        _settings.ShowDisabledRecordingDevices = showDisabledDevices;
    }

    private static void ToggleCommunicationsDucking(KeyModifiers modifiers)
    {
        CommunicationsDuckingMode mode;
        if ((modifiers & KeyModifiers.Alt) != 0) mode = CommunicationsDuckingMode.Reduce50;
        else if ((modifiers & KeyModifiers.Control) != 0) mode = CommunicationsDuckingMode.Reduce80;
        else
        {
            mode = CommunicationsDucking.IsActive()
                ? CommunicationsDuckingMode.DoNothing
                : CommunicationsDuckingMode.MuteAll;
        }

        CommunicationsDucking.SetMode(mode);
    }

    private async void ShowUpdateConfirmation()
    {
        if (_isUpdateDownloadInFlight) return;
        UpdateCheckService? service = AppServices.UpdateCheckService;
        UpdateInfo? info = service?.AvailableUpdate;
        if (service == null || info == null) return;

        _ = await TrayAppDotNETUpdatePromptPresenter.ShowInstallUpdateAsync(new TrayAppDotNETUpdatePromptOptions
        {
            Owner = this,
            Service = service,
            UpdateInfo = info,
            Palette = VolumeSettingsPalette.Create(AppServices.Theme, _settings, ResolveEffectiveIsLight()),
            EnableRoundedCorners = _settings.EnableRoundedCorners,
            Localize = L,
            Log = static message => TADNLog.Log(message),
            FlushLog = static () => TADNLog.Flush(),
            Shutdown = static () =>
            {
                if (Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            },
            SetPromptOpen = open => _isUpdateDialogOpen = open,
            SetDownloadInFlight = inFlight => _isUpdateDownloadInFlight = inFlight,
            PromptClosed = NotifyChildWindowClosedFromDeactivation
        });
    }

    private void ShowDefaultFormatMenu(
        VolumeFlyoutContentGeneration content,
        Control anchor,
        AudioDevice device,
        FlyoutPalette p)
    {
        List<(int Channels, int Bits, int SampleRate)> formats = device.EnumerateSupportedFormats();
        (int Channels, int Bits, int SampleRate)? current = device.GetCurrentFormat();
        if (formats.Count == 0 && current.HasValue)
            formats = BuildFallbackFormatMenu(current.Value);
        if (formats.Count == 0) return;

        List<FlyoutMenuEntry> entries = new(formats.Count);
        foreach ((int channels, int bits, int rate) in formats)
        {
            int capturedChannels = channels;
            int capturedBits = bits;
            int capturedRate = rate;
            bool isCurrent = current.HasValue
                             && current.Value.Channels == channels
                             && current.Value.Bits == bits
                             && current.Value.SampleRate == rate;
            entries.Add(new FlyoutMenuEntry(
                string.Format(
                    L("Flyout_DeviceFormatMenu_Format", "{0} channel, {1} bit, {2} Hz"),
                    channels,
                    bits,
                    rate),
                isCurrent,
                () => device.SetDeviceFormat(capturedChannels, capturedBits, capturedRate)));
        }

        double maxHeight = formats.Count > Layout.FormatMenuMaxVisibleItems
            ? Layout.FormatMenuMaxVisibleItems * Layout.FormatMenuItemHeight + Layout.FormatMenuPaddingReserve
            : double.PositiveInfinity;
        ShowFlyoutMenu(content, anchor, entries, p, maxHeight);
    }

    private static List<(int Channels, int Bits, int SampleRate)> BuildFallbackFormatMenu(
        (int Channels, int Bits, int SampleRate) current)
    {
        int[] rates = [8000, 11025, 16000, 22050, 32000, 44100, 48000, 88200, 96000, 176400, 192000];
        SortedSet<(int, int, int)> values = [];
        foreach (int rate in rates)
            values.Add((current.Channels, current.Bits, rate));
        values.Add(current);
        return [.. values];
    }

    private void ShowListenTargetMenu(
        VolumeFlyoutContentGeneration content,
        Control anchor,
        AudioDevice captureDevice,
        FlyoutPalette p)
    {
        string? currentTarget = captureDevice.ListenTargetDeviceID;
        List<FlyoutMenuEntry> entries =
        [
            new(
                L("Flyout_ListenMenu_DefaultPlaybackDevice", "Default Playback Device"),
                currentTarget == null,
                () => captureDevice.SetListenTarget(null, enable: true))
        ];

        List<AudioDevice> renderTargets = [];
        foreach (AudioDevice device in _audioManager.Devices)
        {
            if (device is { DataFlow: EDataFlow.eRender, IsActive: true })
                renderTargets.Add(device);
        }

        renderTargets.Sort((a, b) =>
            string.Compare(a.FriendlyName, b.FriendlyName, StringComparison.CurrentCultureIgnoreCase));
        foreach (AudioDevice target in renderTargets)
        {
            string targetId = target.Id;
            entries.Add(new FlyoutMenuEntry(
                target.FriendlyName,
                string.Equals(currentTarget, targetId, StringComparison.Ordinal),
                () => captureDevice.SetListenTarget(targetId, enable: true)));
        }

        ShowFlyoutMenu(content, anchor, entries, p);
    }

    private void ShowFlyoutMenu(
        VolumeFlyoutContentGeneration content,
        Control anchor,
        IReadOnlyList<FlyoutMenuEntry> entries,
        FlyoutPalette p,
        double maxHeight = double.PositiveInfinity)
    {
        if (_isClosed || !ReferenceEquals(_activeContent, content) || content.Resources.IsDisposed) return;

        CloseOpenMenu(content);
        UIResourceScope menuResources = new(
            "VolumeFlyoutWindow.Menu",
            exception => TADNLog.Log($"Volume flyout menu cleanup failed: {exception.Message}"));
        FlyoutMenuWindow menu;
        try
        {
            menu = new FlyoutMenuWindow(
                entries,
                p,
                Layout,
                _settings.ContextMenuFontSize,
                _settings.EnableRoundedCorners,
                maxHeight,
                menuResources);
        }
        catch
        {
            menuResources.Dispose();
            throw;
        }

        FlyoutMenuInteraction interaction = new(
            menu,
            menuResources,
            (completedInteraction, closedFromDeactivation) =>
            {
                content.MenuInteraction.Complete(completedInteraction);
                if (closedFromDeactivation && !_isClosed)
                    NotifyChildWindowClosedFromDeactivation();
            });
        content.MenuInteraction.Replace(interaction);
        try
        {
            interaction.ShowAt(anchor);
        }
        catch
        {
            content.MenuInteraction.Complete(interaction);
            throw;
        }
    }

    private void CloseOpenMenu()
    {
        VolumeFlyoutContentGeneration? content = _activeContent;
        if (content != null) CloseOpenMenu(content);
    }

    private static void CloseOpenMenu(VolumeFlyoutContentGeneration content) =>
        content.MenuInteraction.Clear();

    private bool IsFlyoutMenuOpen => _activeContent?.MenuInteraction.Active?.IsVisible == true;

    private void BeginDeviceNameEdit(
        VolumeFlyoutContentGeneration content,
        Grid host,
        AudioDevice device,
        FlyoutPalette p)
    {
        if (_isClosed || !ReferenceEquals(_activeContent, content) || content.Resources.IsDisposed) return;

        content.DeviceNameEditInteraction.Clear();

        TextBox editor = new()
        {
            Text = device.FriendlyName,
            FontSize = Layout.DeviceNameEditorFontSize,
            Foreground = Brush(p.Foreground),
            Background = Brush(p.ControlBackground),
            BorderBrush = Brush(p.Border),
            BorderThickness = Layout.DeviceNameEditorBorderThickness,
            Padding = Layout.DeviceNameEditorPadding,
            MinHeight = Layout.DeviceNameEditorMinHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            ZIndex = Layout.DeviceNameEditorZIndex
        };
        UIResourceScope interactionResources = new(
            "VolumeFlyoutWindow.DeviceNameEdit",
            exception => TADNLog.Log($"Volume device-name editor cleanup failed: {exception.Message}"));
        content.DeviceNameEditInteraction.Replace(interactionResources);
        try
        {
            host.Children.Add(editor);
            interactionResources.Add(() =>
            {
                if (host.Children.Contains(editor)) host.Children.Remove(editor);
                editor.Text = null;
            });

            editor.KeyDown += OnEditorKeyDown;
            interactionResources.Add(() => editor.KeyDown -= OnEditorKeyDown);
            editor.LostFocus += OnEditorLostFocus;
            interactionResources.Add(() => editor.LostFocus -= OnEditorLostFocus);
            AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            interactionResources.Add(() => RemoveHandler(PointerPressedEvent, OnWindowPointerPressed));
        }
        catch
        {
            content.DeviceNameEditInteraction.Complete(interactionResources);
            throw;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_isClosed || interactionResources.IsDisposed || content.Resources.IsDisposed
                          || !host.Children.Contains(editor))
                return;

            editor.Focus();
            editor.SelectAll();
        }, DispatcherPriority.Input);
        return;

        void OnEditorKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    Commit();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Cancel();
                    e.Handled = true;
                    break;
            }
        }

        void OnEditorLostFocus(object? sender, RoutedEventArgs e) => Commit();

        void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (interactionResources.IsDisposed) return;
            Point point = e.GetPosition(editor);
            if (point is { X: >= 0, Y: >= 0 }
                && point.X <= editor.Bounds.Width
                && point.Y <= editor.Bounds.Height)
                return;

            Commit();
        }

        void Commit()
        {
            if (interactionResources.IsDisposed) return;
            string? customName = editor.Text;
            content.DeviceNameEditInteraction.Complete(interactionResources);
            if (_isClosed || content.Resources.IsDisposed || !ReferenceEquals(_activeContent, content)) return;
            device.SetCustomFriendlyName(customName);
        }

        void Cancel() => content.DeviceNameEditInteraction.Complete(interactionResources);
    }

    private void ShowEqualizerAPONotAvailableDialog()
    {
        string body = L("EqualizerAPO_NotAvailable_Body", "Equalizer APO was not found on this system.");
        string download = L("EqualizerAPO_NotAvailable_DownloadButton", "Download latest x64 installer");
        SettingsPalette palette = VolumeSettingsPalette.Create(AppServices.Theme, _settings, ResolveEffectiveIsLight());
        TrayAppDotNETUpdateConfirmationWindow dialog = new(
            L("EqualizerAPO_NotAvailable_Title", "Equalizer APO not detected"),
            body,
            string.Empty,
            download,
            L("UpdateDialog_Cancel", "Cancel"),
            palette,
            _settings.EnableRoundedCorners) { WindowStartupLocation = WindowStartupLocation.CenterOwner };

        _ = ShowEqualizerDialogAsync(dialog);

        async Task ShowEqualizerDialogAsync(TrayAppDotNETUpdateConfirmationWindow prompt)
        {
            bool accepted = await prompt.ShowDialog<bool>(this);
            if (!accepted) return;
            try
            {
                using Process? _ = Process.Start(new ProcessStartInfo
                {
                    FileName = EqualizerAPOMonitor.LatestInstallerURL, UseShellExecute = true
                });
            }
            catch (Exception ex) { TADNLog.Log($"VolumeFlyout.ShowEqualizerAPONotAvailableDialog: {ex.Message}"); }
        }
    }

    private List<AudioAppGroup> VisibleGroups(AudioDevice device)
    {
        List<AudioAppGroup> groups = [];
        foreach (AudioAppGroup group in device.Groups)
        {
            if (group.State == AudioSessionState.Expired) continue;
            if (group.IsSystemSounds) continue;
            if (_ownAppID != null &&
                string.Equals(group.AppID, _ownAppID, StringComparison.OrdinalIgnoreCase)) continue;
            if (group.Sessions.Count == 0) continue;
            if (_settings.CaptureActivityIndicator == CaptureActivityIndicator.HideInactive
                && device.IsCaptureDevice
                && group.State != AudioSessionState.Active)
                continue;
            groups.Add(group);
        }

        return groups;
    }

    private void HookDeviceForRebuild(AudioDevice device)
    {
        device.PropertyChanged += OnDeviceChanged;
        AddCleanup(() => device.PropertyChanged -= OnDeviceChanged);
        ((INotifyCollectionChanged)device.Groups).CollectionChanged += OnDeviceGroupsChanged;
        AddCleanup(() => ((INotifyCollectionChanged)device.Groups).CollectionChanged -= OnDeviceGroupsChanged);

        foreach (AudioAppGroup group in device.Groups)
            HookGroupForRebuild(group);
        return;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AudioDevice.Groups)) return;
            QueueRebuild();
        }
    }

    private void OnDeviceGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        QueueRebuild();

    private void HookGroupForRebuild(AudioAppGroup group)
    {
        group.PropertyChanged += OnGroupChanged;
        AddCleanup(() => group.PropertyChanged -= OnGroupChanged);
        return;

        void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
        {
            string propertyName = e.PropertyName ?? string.Empty;
            if (propertyName is nameof(AudioAppGroup.State) or nameof(AudioAppGroup.Icon)
                or nameof(AudioAppGroup.DisplayName) or nameof(AudioAppGroup.IsMuted))
                QueueRebuild();
        }
    }

    private static void UpdateGroupMeterVisibility(AudioDevice device, IReadOnlyList<AudioAppGroup> visibleGroups,
        bool drawerVisible)
    {
        foreach (AudioAppGroup group in device.Groups)
            group.IsPeakMeterVisible = drawerVisible && visibleGroups.Contains(group);
    }

    private static string? ResolveOwnAppID()
    {
        try
        {
            string? path = ProcessHelper.GetProcessImagePath((uint)Environment.ProcessId);
            return string.IsNullOrEmpty(path) ? null : path.ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private void SetAllGroupMetersVisible(bool visible)
    {
        foreach (AudioDevice device in _audioManager.Devices)
        {
            foreach (AudioAppGroup group in device.Groups)
                group.IsPeakMeterVisible = visible;
        }
    }

    private bool IsAppDrawerExpanded(AudioDevice device)
    {
        DeviceSettingsEntry? entry = AppServices.DeviceSettings?.Find(device.Id);
        return entry?.IsAppDrawerExpanded ?? _settings.DefaultAppDrawerExpanded;
    }

    private static void SetAppDrawerExpanded(AudioDevice device, bool expanded)
    {
        DeviceSettings? store = AppServices.DeviceSettings;
        if (store == null) return;
        DeviceSettingsEntry entry = store.GetOrCreate(device.Id);
        if (entry.IsAppDrawerExpanded == expanded) return;
        entry.IsAppDrawerExpanded = expanded;
        store.Save();
    }

    private bool UsesGridDrawer(AudioDevice device) =>
        device.IsCaptureDevice && _settings.RecordingAppDrawerDisplayType == AppDrawerDisplayType.Icons;

    private bool ShouldShowCommunicationsButton => _settings.FlyoutCommunicationsButtonVisibility switch
    {
        CommunicationsButtonVisibility.Hidden => false,
        CommunicationsButtonVisibility.WhenDuckingOn => CommunicationsDucking.IsActive(),
        _ => true
    };

    private bool ShowsDisabledDevices =>
        _settings.ShowDisabledPlaybackDevices
        && (!_settings.ShowRecordingDevices
            || !_settings.ShowRecordingDevicesInFlyout
            || _settings.ShowDisabledRecordingDevices);

    private Glyph DisabledDevicesGlyph => ShowsDisabledDevices ? GlyphCatalog.VIEW : GlyphCatalog.HIDE;

    private bool ShowCommunicationsButton => ShouldShowCommunicationsButton;

    private bool IsUpdateButtonVisible =>
        _settings.ShowUpdateButtonInFlyout && AppServices.UpdateCheckService?.AvailableUpdate != null;

    private int DeviceTitleRowIndex =>
        _settings.FlyoutDeviceTitlePosition == FlyoutDeviceTitlePosition.AboveSlider ? 0 : 1;

    private int DeviceSliderRowIndex =>
        _settings.FlyoutDeviceTitlePosition == FlyoutDeviceTitlePosition.AboveSlider ? 1 : 0;

    private int AppDrawerIconsPerRow => Math.Clamp(
        _settings.AppDrawerIconsPerRow,
        AppSettings.AppDrawerIconsPerRowMin,
        AppSettings.AppDrawerIconsPerRowMax);

    private bool IsVerticalIconStackDirection =>
        ResolvedStackDirection is AppDrawerStackDirection.LeftRight or AppDrawerStackDirection.RightLeft;

    private AppDrawerStackDirection ResolvedStackDirection
    {
        get
        {
            if (_settings.AppDrawerStackDirection != AppDrawerStackDirection.Auto)
                return _settings.AppDrawerStackDirection;
            return _settings.FlyoutDeviceLayout == FlyoutDeviceLayoutStyle.AppsAboveDevice
                ? AppDrawerStackDirection.BottomTop
                : AppDrawerStackDirection.TopBottom;
        }
    }

    private IEnumerable<AudioAppGroup> ResolveGridOrder(IReadOnlyList<AudioAppGroup> groups)
    {
        return ResolvedStackDirection is AppDrawerStackDirection.BottomTop or AppDrawerStackDirection.RightLeft
            ? groups.Reverse()
            : groups;
    }

    private double ResolveSliderDrawerMaxHeight(AudioDevice device)
    {
        int n = device.IsCaptureDevice
            ? _settings.RecordingAppDrawerSlidersMaxApps
            : _settings.PlaybackAppDrawerSlidersMaxApps;
        return Math.Max(1, n) * Layout.AppSliderRowHeight;
    }

    private double ResolveGridDrawerMaxHeight(AudioDevice device)
    {
        int n = device.IsCaptureDevice
            ? _settings.RecordingAppDrawerIconsMaxRows
            : _settings.PlaybackAppDrawerIconsMaxRows;
        return Math.Max(1, n) * Layout.AppIconGridSlotSize;
    }

    private double ResolveAppOpacity(AudioDevice device, AudioAppGroup group)
    {
        if (group.IsMuted || device.IsMuted || !device.IsActive) return 0.4;
        if (device.IsCaptureDevice
            && _settings.CaptureActivityIndicator == CaptureActivityIndicator.DimInactive
            && group.State != AudioSessionState.Active)
            return 0.4;
        return 1.0;
    }

    private string DeviceFormatLine(AudioDevice device)
    {
        string format = _settings.ShowDeviceFormatText ? device.DefaultFormat ?? string.Empty : string.Empty;
        string codec = _settings.ShowDeviceCodecText && device.IsBluetooth ? device.CurrentCodecName : string.Empty;

        return format.Length switch
        {
            > 0 when codec.Length > 0 => format + ", " + codec,
            > 0 => format,
            _ => codec
        };
    }

    private CornerRadius ResolveDeviceBandRadius(bool isLast, bool appsBottom, bool drawerVisible)
    {
        if (!isLast) return Layout.ZeroCornerRadius;
        if (!appsBottom) return FooterBottomRadius;
        return drawerVisible ? Layout.ZeroCornerRadius : FooterBottomRadius;
    }

    private CornerRadius FooterBottomRadius => Rounded(Layout.FooterBottomCornerRadius);

    private CornerRadius Rounded(CornerRadius radius) =>
        _settings.EnableRoundedCorners ? radius : Layout.ZeroCornerRadius;

    private static TranslateTransform CloneTransform(TranslateTransform transform) => new()
    {
        X = transform.X, Y = transform.Y
    };

    private double ResolveMaxContentHeight()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        return Math.Max(Layout.WorkAreaMinHeight,
            workArea.Height / RenderScaling - EdgePadding * 2 - Layout.ContentHeightReserve);
    }

    private void OnChromePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isClosed) return;

        VolumeFlyoutContentGeneration? content = _activeContent;
        if (content == null) return;
        if (!Docking.IsUndocked) return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        if (IsInteractiveDragSource(e.Source)) return;
        if (sender is not Control control) return;

        BeginWindowDrag(content, control, e);
        e.Handled = true;
    }

    private void BeginWindowDrag(
        VolumeFlyoutContentGeneration content,
        Control source,
        PointerPressedEventArgs e)
    {
        if (_isClosed || content.Resources.IsDisposed || !ReferenceEquals(_activeContent, content)) return;

        (PixelPoint dockedPosition, int snapTolerance) = Docking.CaptureDockedPosition();
        PixelPoint pointer = source.PointToScreen(e.GetPosition(source));

        _dragHelper.BeginDrag(pointer, Position, dockedPosition, snapTolerance);
        content.IsDraggingWindow = true;
        content.CapturedPointer = e.Pointer;
        try
        {
            e.Pointer.Capture(source);
        }
        catch
        {
            content.IsDraggingWindow = false;
            content.CapturedPointer = null;
            throw;
        }
    }

    private void OnChromePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isClosed) return;

        VolumeFlyoutContentGeneration? content = _activeContent;
        if (content is not { IsDraggingWindow: true }
            || content.UndockButtonController?.IsPointerCaptured == true)
            return;
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            EndWindowDrag(content, e.Pointer, commit: true);
            e.Handled = true;
            return;
        }

        if (sender is not Control control) return;
        PixelPoint pointer = control.PointToScreen(e.GetPosition(control));
        PixelPoint natural = _dragHelper.ComputeNatural(pointer);
        _dragHelper.ApplyDragPosition(this, natural);
        e.Handled = true;
    }

    private void OnChromePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isClosed) return;

        VolumeFlyoutContentGeneration? content = _activeContent;
        if (content is not { IsDraggingWindow: true }
            || content.UndockButtonController?.IsPointerCaptured == true)
            return;
        EndWindowDrag(content, e.Pointer, commit: true);
        e.Handled = true;
    }

    private void OnChromePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        VolumeFlyoutContentGeneration? content = _activeContent;
        if (content is not { IsDraggingWindow: true }
            || content.UndockButtonController?.IsPointerCaptured == true)
            return;
        content.IsDraggingWindow = false;
        content.CapturedPointer = null;
        if (_isClosed || content.Resources.IsDisposed) return;
        Docking.CommitDragPosition();
        FlushPendingRebuild();
    }

    private void EndWindowDrag(VolumeFlyoutContentGeneration content, IPointer pointer, bool commit)
    {
        bool canCommit = commit
                         && !_isClosed
                         && !content.Resources.IsDisposed
                         && ReferenceEquals(_activeContent, content);
        content.IsDraggingWindow = false;
        content.CapturedPointer = null;
        pointer.Capture(null);
        if (canCommit) Docking.CommitDragPosition();
        FlushPendingRebuild();
    }

    private static bool IsInteractiveDragSource(object? source)
    {
        for (Control? control = source as Control; control != null; control = control.GetVisualParent<Control>())
        {
            if (control is FlyoutSlider or TextBox or ScrollViewer or Image)
                return true;
            if (control.Cursor != null)
                return true;
        }

        return false;
    }

    private bool ResolveEffectiveIsLight() => _settings.ThemeMode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        _ => AppServices.Theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme
    };

    private Glyph DeviceVolumeGlyph(AudioDevice device) => device.IsCaptureDevice ? CaptureDeviceVolumeGlyph(device, device.IsMuted) : PlaybackDeviceVolumeGlyph(device, device.IsMuted);

    private Glyph DeviceMuteTogglePreviewGlyph(AudioDevice device)
    {
        bool mutedAfterToggle = !device.IsMuted;
        return device.IsCaptureDevice ? CaptureDeviceVolumeGlyph(device, mutedAfterToggle) : PlaybackDeviceVolumeGlyph(device, mutedAfterToggle);
    }

    private Glyph PlaybackDeviceVolumeGlyph(AudioDevice device, bool muted)
    {
        if (muted) return GlyphCatalog.PLAYBACK_VOLUME_MUTE;
        return _settings.UseDynamicPlaybackVolumeGlyphInFlyout
            ? GlyphCatalog.GetVolumeTier(device.Volume, muted: false)
            : GlyphCatalog.PLAYBACK_VOLUME_LOW;
    }

    private static Glyph CaptureDeviceVolumeGlyph(AudioDevice device, bool muted)
    {
        if (muted) return GlyphCatalog.MICROPHONE_OFF;
        if (device.IsListeningToThisDevice) return GlyphCatalog.MICROPHONE_LISTENING;
        return device.IsCaptureSleeping ? GlyphCatalog.MICROPHONE_SLEEP : GlyphCatalog.MICROPHONE;
    }

    private static Glyph ExclusiveButtonGlyph(AudioDevice device) =>
        device is { IsExclusiveModeAllowed: true, IsExclusiveControlHeld: true }
            ? GlyphCatalog.LOCK
            : GlyphCatalog.UNLOCK;

    private static Glyph DeviceStateGlyph(AudioDevice device)
    {
        if (!device.IsActive) return GlyphCatalog.PLAYBACK_DEVICE_DISABLED;
        if (device.IsDefault) return GlyphCatalog.PLAYBACK_DEVICE_DEFAULT;
        return device.IsDefaultCommunications ? GlyphCatalog.PLAYBACK_DEVICE_DEFAULT_COMMS : GlyphCatalog.PLAYBACK_DEVICE_ENABLED;
    }

    private static Glyph BatteryGlyph(int level)
    {
        int index = (int)Math.Round(level / 10.0);
        index = Math.Clamp(index, 0, 10);
        return index switch
        {
            0 => GlyphCatalog.BT_BATTERY_0,
            1 => GlyphCatalog.BT_BATTERY_1,
            2 => GlyphCatalog.BT_BATTERY_2,
            3 => GlyphCatalog.BT_BATTERY_3,
            4 => GlyphCatalog.BT_BATTERY_4,
            5 => GlyphCatalog.BT_BATTERY_5,
            6 => GlyphCatalog.BT_BATTERY_6,
            7 => GlyphCatalog.BT_BATTERY_7,
            8 => GlyphCatalog.BT_BATTERY_8,
            9 => GlyphCatalog.BT_BATTERY_9,
            _ => GlyphCatalog.BT_BATTERY_10
        };
    }

    private static string EqualizerTooltip(EqualizerAPOState state) => state switch
    {
        EqualizerAPOState.Running => L("Flyout_EqualizerAPO_Tooltip_Running", "Equalizer APO is running"),
        EqualizerAPOState.EnhancementsOff => L("Flyout_EqualizerAPO_Tooltip_EnhancementsOff",
            "Enhancements are disabled"),
        EqualizerAPOState.NotInstalled => L("Flyout_EqualizerAPO_Tooltip_NotInstalled",
            "Equalizer APO is not installed"),
        _ => L("Flyout_EqualizerAPO_Tooltip_NotAvailable", "Equalizer APO is not available")
    };

    private static string ScalarText(float scalar) => $"{(int)Math.Round(scalar * 100)}";

    private static TextBlock Text(string text, FlyoutPalette p, double size, FontWeight? weight = null) => new()
    {
        Text = text,
        FontFamily = FlyoutFont,
        FontSize = size,
        FontWeight = weight ?? FontWeight.Normal,
        Foreground = Brush(p.Foreground),
        TextWrapping = TextWrapping.NoWrap
    };

    private static TextBlock Text(Glyph glyph, FlyoutPalette p, double size, FontWeight? weight = null)
    {
        TextBlock textBlock = Text(glyph.Text, p, size, weight);
        GlyphApplicator.ApplyTo(textBlock, glyph);
        return textBlock;
    }

    private static void PreventIconGlyphClipping(TextBlock glyph, double lineHeightPadding)
    {
        glyph.ClipToBounds = false;
        glyph.LineHeight = Math.Ceiling(glyph.FontSize + lineHeightPadding);
    }

    private static SolidColorBrush Brush(Color color, double opacity = 1.0)
    {
        byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * opacity), 0, 255);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
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

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _rebuildPending = false;
        _rebuildQueued = false;
        _isRebuilding = false;

        try
        {
            // Detach external publishers before any close operation that can fail
            WindowResources.Dispose();
            try
            {
                StopFlyoutActivity();
            }
            finally
            {
                try
                {
                    Safe.Dispose(_feedback);
                }
                finally
                {
                    VolumeFlyoutContentGeneration? buildingContent = _buildingContent;
                    _buildingContent = null;
                    buildingContent?.Resources.Dispose();
                }
            }
        }
        finally
        {
            _buildingContent = null;
            _activeContent = null;
            base.OnClosed(e);
        }
    }

    /// <summary>
    /// Holds every reference and resource whose lifetime is exactly one published visual tree.
    /// </summary>
    private sealed class VolumeFlyoutContentGeneration(UIResourceScope resources) : IDisposable
    {
        public readonly UIResourceScope Resources = resources;
        public UIContentGeneration Generation = null!;
        public ScrollViewer? CellsScrollViewer;
        public FlyoutUndockButtonController? UndockButtonController;
        public readonly ActiveInteractionSlot<FlyoutMenuInteraction> MenuInteraction = new();
        public readonly ActiveInteractionSlot<UIResourceScope> DeviceNameEditInteraction = new();
        public IPointer? CapturedPointer;
        public bool IsDraggingWindow;
        public bool DeviceOrderingRebuildPending;
        public int HoveredDeviceStateButtonCount;
        public int ActiveVolumeSliderDragCount;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            IsDraggingWindow = false;
            ActiveVolumeSliderDragCount = 0;
            MenuInteraction.Dispose();
            DeviceNameEditInteraction.Dispose();

            IPointer? capturedPointer = CapturedPointer;
            CapturedPointer = null;
            try { capturedPointer?.Capture(null); }
            catch (Exception exception)
            {
                TADNLog.Log($"Volume flyout pointer release failed: {exception.Message}");
            }

            CellsScrollViewer = null;
            UndockButtonController = null;
            DeviceOrderingRebuildPending = false;
            HoveredDeviceStateButtonCount = 0;
        }
    }

    /// <summary>Owns at most one live interaction and drops completed owners immediately.</summary>
    private sealed class ActiveInteractionSlot<TInteraction> : IDisposable
        where TInteraction : class, IDisposable
    {
        private TInteraction? _active;
        private bool _disposed;

        public TInteraction? Active => _active;

        public void Replace(TInteraction interaction)
        {
            ArgumentNullException.ThrowIfNull(interaction);
            if (_disposed)
            {
                interaction.Dispose();
                return;
            }

            TInteraction? previous = _active;
            _active = interaction;
            previous?.Dispose();
        }

        public void Complete(TInteraction interaction)
        {
            ArgumentNullException.ThrowIfNull(interaction);
            if (ReferenceEquals(_active, interaction)) _active = null;
            interaction.Dispose();
        }

        public void Clear()
        {
            TInteraction? active = _active;
            _active = null;
            active?.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Clear();
        }
    }

    /// <summary>Owns one menu window and its disposable visual resources.</summary>
    private sealed class FlyoutMenuInteraction : IDisposable
    {
        private FlyoutMenuWindow? _menu;
        private UIResourceScope? _resources;
        private Action<FlyoutMenuInteraction, bool>? _completed;
        private bool _windowClosed;
        private int _disposed;

        public FlyoutMenuInteraction(
            FlyoutMenuWindow menu,
            UIResourceScope resources,
            Action<FlyoutMenuInteraction, bool> completed)
        {
            _menu = menu;
            _resources = resources;
            _completed = completed;
            menu.Closed += OnMenuClosed;
        }

        public bool IsVisible => _menu?.IsVisible == true;

        public void ShowAt(Control anchor)
        {
            FlyoutMenuWindow menu = _menu ?? throw new ObjectDisposedException(nameof(FlyoutMenuInteraction));
            menu.ShowAt(anchor);
        }

        private void OnMenuClosed(object? sender, EventArgs e)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            FlyoutMenuWindow? menu = _menu;
            if (menu == null) return;

            _windowClosed = true;
            Action<FlyoutMenuInteraction, bool>? completed = _completed;
            try
            {
                completed?.Invoke(this, menu.ClosedFromDeactivation);
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Volume flyout menu completion failed: {exception.Message}");
            }
            finally
            {
                if (Volatile.Read(ref _disposed) == 0) Dispose();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            FlyoutMenuWindow? menu = _menu;
            UIResourceScope? resources = _resources;
            _menu = null;
            _resources = null;
            _completed = null;
            if (menu != null) menu.Closed -= OnMenuClosed;

            try
            {
                if (!_windowClosed) menu?.Close();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Volume flyout menu close failed: {exception.Message}");
            }
            finally
            {
                resources?.Dispose();
            }
        }
    }

    private readonly record struct FlyoutPalette(
        Color Background,
        Color Foreground,
        Color Border,
        Color Pressed,
        Color FooterBackground,
        Color ControlBackground,
        Color ButtonHover,
        Color ButtonPressed,
        Color SecondaryForeground,
        Color IconForeground,
        Color SliderProgress,
        Color SliderTrack,
        Color SliderThumb,
        Color MeterPeak,
        Color MeterPeakStereo,
        Color MenuShadow)
    {
        public static FlyoutPalette Create(SettingsPalette settings, AppTheme? theme, AppSettings appSettings,
            bool isLight)
        {
            AppTheme resolvedTheme = theme ?? AppTheme.Default;
            return new FlyoutPalette(
                settings.Background,
                settings.Foreground,
                settings.Border,
                settings.Pressed,
                resolvedTheme.FooterBackground.For(isLight),
                settings.ControlBackground,
                resolvedTheme.ButtonHover.For(isLight),
                resolvedTheme.ButtonPressed.For(isLight),
                settings.SecondaryForeground,
                resolvedTheme.IconForeground.For(isLight),
                settings.SliderProgress,
                settings.SliderTrack,
                settings.SliderThumb,
                appSettings.EffectiveMeterPeakColor,
                appSettings.EffectiveMeterPeakStereoColor,
                resolvedTheme.MenuShadow.For(isLight));
        }
    }

    private sealed record FlyoutMenuEntry(string Text, bool IsCurrent, Action Click);

    private sealed class FlyoutMenuWindow : Window
    {
        private readonly double _maxHeight;
        private readonly FlyoutAxamlProperties _layout;
        private bool _closedFromDeactivation;

        public bool ClosedFromDeactivation => _closedFromDeactivation;

        public FlyoutMenuWindow(
            IReadOnlyList<FlyoutMenuEntry> entries,
            FlyoutPalette palette,
            FlyoutAxamlProperties layout,
            int fontSize,
            bool rounded,
            double maxHeight,
            UIResourceScope resources)
        {
            _maxHeight = maxHeight;
            _layout = layout;
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            CanResize = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;

            StackPanel items = new() { Spacing = 0 };
            foreach (FlyoutMenuEntry entry in entries)
                items.Children.Add(new FlyoutMenuRow(entry, palette, layout, fontSize, rounded, Close));

            SettingsScrollHost scroll = TrayAppDotNETSettingsUI.ScrollHost(
                items,
                MenuSettingsPalette(palette),
                layout.MenuScrollHostPadding);
            resources.Own(scroll);
            scroll.MaxHeight = maxHeight;

            Content = new Border
            {
                Background = Brush(palette.Background),
                BorderBrush = Brush(palette.Border),
                BorderThickness = layout.MenuBorderThickness,
                CornerRadius = rounded ? layout.MenuCornerRadius : layout.ZeroCornerRadius,
                Padding = layout.MenuPadding,
                BoxShadow = new BoxShadows(new BoxShadow
                {
                    OffsetY = layout.MenuShadowOffsetY, Blur = layout.MenuShadowBlur, Color = palette.MenuShadow
                }),
                Child = scroll
            };

            Deactivated += (_, _) =>
            {
                _closedFromDeactivation = true;
                Close();
            };
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
        }

        public void ShowAt(Control anchor)
        {
            PixelPoint anchorBottom = anchor.PointToScreen(new Point(0, anchor.Bounds.Height));
            PixelPoint anchorTop = anchor.PointToScreen(new Point(0, 0));
            Position = new PixelPoint(_layout.OffscreenPosition, _layout.OffscreenPosition);
            Show();

            Dispatcher.UIThread.Post(() =>
            {
                UpdateLayout();
                double scale = RenderScaling;
                int width = Math.Max(PixelMinSize, (int)Math.Ceiling(Bounds.Width * scale));
                int height = Math.Max(PixelMinSize,
                    (int)Math.Ceiling(Math.Min(Bounds.Height, _maxHeight) * scale));

                PixelRect workArea = TrayWorkArea.Resolve(
                    Screens,
                    anchorBottom,
                    new PixelRect(
                        _layout.FallbackWorkAreaX,
                        _layout.FallbackWorkAreaY,
                        _layout.FallbackWorkAreaWidth,
                        _layout.FallbackWorkAreaHeight));
                int left = Math.Clamp(anchorBottom.X, workArea.X + EdgePadding,
                    Math.Max(workArea.X + EdgePadding, workArea.Right - width - EdgePadding));
                int top = anchorBottom.Y;
                if (top + height > workArea.Bottom - EdgePadding)
                    top = anchorTop.Y - height;
                top = Math.Clamp(top, workArea.Y + EdgePadding,
                    Math.Max(workArea.Y + EdgePadding, workArea.Bottom - height - EdgePadding));

                Position = new PixelPoint(left, top);
                Activate();
            }, DispatcherPriority.Loaded);
        }

        private static SettingsPalette MenuSettingsPalette(FlyoutPalette palette) => new(
            palette.Background,
            palette.Foreground,
            palette.Border,
            palette.ButtonHover,
            palette.ButtonPressed,
            palette.FooterBackground,
            palette.ControlBackground,
            palette.SecondaryForeground,
            palette.SecondaryForeground,
            palette.SliderProgress,
            palette.SliderProgress,
            palette.SliderThumb,
            palette.Border,
            palette.SliderThumb,
            palette.SliderTrack,
            palette.SliderProgress,
            palette.SliderTrack,
            palette.SliderThumb,
            palette.ButtonHover,
            palette.ButtonPressed,
            palette.Foreground);

        private int EdgePadding => (int)Math.Round(_layout.EdgePadding);

        private int PixelMinSize => (int)Math.Round(_layout.PixelMinSize);
    }

    private sealed class FlyoutMenuRow : Border
    {
        private readonly FlyoutPalette _palette;
        private readonly Action _close;
        private bool _isPointerOver;

        public FlyoutMenuRow(FlyoutMenuEntry entry, FlyoutPalette palette, FlyoutAxamlProperties layout, int fontSize,
            bool rounded, Action close)
        {
            _palette = palette;
            _close = close;
            CornerRadius = rounded ? layout.MenuRowCornerRadius : layout.ZeroCornerRadius;
            Background = Brushes.Transparent;
            Margin = layout.MenuRowMargin;
            Padding = layout.MenuRowPadding;
            Cursor = TrayAppDotNETCursors.Hand;

            Grid row = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(layout.MenuMarkerColumnWidth)),
                    new ColumnDefinition(GridLength.Star)
                }
            };

            TextBlock marker = entry.IsCurrent
                ? Text(GlyphCatalog.CIRCLE, palette, layout.MenuMarkerFontSize)
                : Text(string.Empty, palette, layout.MenuMarkerFontSize);
            marker.Foreground = Brush(palette.IconForeground);
            marker.VerticalAlignment = VerticalAlignment.Center;
            marker.HorizontalAlignment = HorizontalAlignment.Center;
            row.Children.Add(marker);

            TextBlock label = Text(entry.Text, palette, fontSize);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            Child = row;

            PointerEntered += (_, _) =>
            {
                _isPointerOver = true;
                UpdateBackground(false);
            };
            PointerExited += (_, _) =>
            {
                _isPointerOver = false;
                UpdateBackground(false);
            };
            PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
                UpdateBackground(true);
                e.Handled = true;
            };
            PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                UpdateBackground(false);
                if (_isPointerOver)
                {
                    _close();
                    entry.Click();
                }

                e.Handled = true;
            };
        }

        private void UpdateBackground(bool pressed)
        {
            Background = pressed
                ? Brush(_palette.ButtonPressed)
                : _isPointerOver
                    ? Brush(_palette.ButtonHover)
                    : Brushes.Transparent;
        }
    }
}
