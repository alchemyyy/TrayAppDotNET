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


namespace VolumeTrayAppDotNET.UI.Flyout;

public sealed partial class VolumeFlyoutWindow : FlyoutWindowCommon
{
    private static readonly FontFamily FlyoutFont = new("Segoe UI");

    private static readonly HashSet<string> DeviceRebuildProperties = new(StringComparer.Ordinal)
    {
        nameof(AudioDevice.IsActive),
        nameof(AudioDevice.State),
        nameof(AudioDevice.BatteryLevel),
        nameof(AudioDevice.DefaultFormat),
        nameof(AudioDevice.CurrentCodecName),
    };

    private readonly AudioDeviceManager _audioManager;
    private readonly AppSettings _settings;
    private readonly Action _openSettings;
    private readonly List<Action> _cleanup = [];
    private readonly AppVolumeFeedbackPlayer? _feedback;
    private readonly string? _ownAppID;
    private FlyoutMenuWindow? _openMenu;
    private TrayAppDotNETShellTrayIcon? _lastTrayIcon;
    private ScrollViewer? _cellsScrollViewer;
    private bool _isUndocked;
    private bool _isRebuilding;
    private bool _isUpdateDownloadInFlight;
    private bool _isDraggingWindow;
    private bool _undockButtonPointerCaptured;
    private bool _undockButtonDragOccurred;
    private int _hoveredDeviceStateButtonCount;
    private bool _deviceOrderingRebuildPending;
    private FlyoutLayout? _layout;
    private Border? _undockButton;
    private TextBlock? _undockButtonGlyph;
    private readonly FlyoutWindowDragHelper _dragHelper = new();

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

        _isUndocked = _settings is
        {
            AllowFlyoutUndock: true, RestoreFlyoutUndockedOnStartup: true, FlyoutUndocked: true,
            FlyoutHasSavedPosition: true
        };

        _settings.Changed += OnSettingsChanged;
        _audioManager.PropertyChanged += OnAudioManagerPropertyChanged;
        ((INotifyCollectionChanged)_audioManager.Devices).CollectionChanged += OnDevicesCollectionChanged;

        if (AppServices.UpdateCheckService is { } updateService)
            updateService.StateChanged += NotifyUpdateStateChanged;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };

        InitializeComponentState();
    }

    private void InitializeComponentState()
    {
        _layout = FlyoutLayout.From(this);

        if (_settings != null && _audioManager != null)
            Rebuild();
    }

    private FlyoutLayout Layout =>
        _layout ?? throw new InvalidOperationException("Flyout layout resources have not been loaded.");

    public void Redock()
    {
        if (!_isUndocked) return;
        _isUndocked = false;
        UpdateUndockButtonVisual();
        _settings.FlyoutUndocked = false;
        _settings.Save();
        Rebuild();
        QueuePositionNearTray();
    }

    public void ShowAt(TrayAppDotNETShellTrayIcon trayIcon, bool activate = true)
    {
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
        CloseOpenMenu();
        StopFlyoutActivity();
        base.Hide();
        NotifyWarmDismissed();
    }

    protected override bool HasOpenChildWindow => IsFlyoutMenuOpen;

    protected override bool ShouldAutoHideWhenDeactivated => !_isUndocked;

    protected override void HideFlyout() => Hide();

    public void NotifyUpdateStateChanged() => Dispatcher.UIThread.Post(Rebuild);

    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_isUndocked && !_settings.AllowFlyoutUndock)
        {
            Redock();
            return;
        }

        Rebuild();
    });

    private void OnAudioManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioDeviceManager.DefaultDevice))
            Dispatcher.UIThread.Post(Rebuild);
    }

    private void OnDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(Rebuild);

    private void PositionNearTray() => Position = ResolvePosition(_lastTrayIcon);

    private PixelPoint OffscreenPosition() => new(Layout.OffscreenPosition, Layout.OffscreenPosition);

    private PixelRect FallbackWorkArea() => new(
        Layout.FallbackWorkAreaX,
        Layout.FallbackWorkAreaY,
        Layout.FallbackWorkAreaWidth,
        Layout.FallbackWorkAreaHeight);

    private void QueuePositionNearTray(bool scrollToBottom = false)
    {
        if (!IsVisible || _isDraggingWindow) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible || _isDraggingWindow) return;
            UpdateLayout();
            ApplyWorkAreaMaxHeight();
            PositionNearTray();
            if (scrollToBottom) ScrollCellsToBottom();
        }, DispatcherPriority.Loaded);
    }

    private PixelPoint ResolvePosition(TrayAppDotNETShellTrayIcon? trayIcon)
    {
        if (_isUndocked && _settings.FlyoutHasSavedPosition)
        {
            PixelPoint saved = new((int)Math.Round(_settings.FlyoutLeft), (int)Math.Round(_settings.FlyoutTop));
            if (!_settings.ClampUndockedFlyoutToScreen) return saved;

            PixelRect savedWorkArea = (Screens.ScreenFromPoint(saved) ?? Screens.Primary)?.WorkingArea
                                      ?? ResolveWorkArea(trayIcon);
            return ClampWindowPosition(saved, savedWorkArea);
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

    private PixelPoint ClampWindowPosition(PixelPoint target, PixelRect workArea)
    {
        int width = CurrentPixelWidth();
        int height = CurrentPixelHeight();

        int minLeft = workArea.X + Layout.EdgePadding;
        int maxLeft = Math.Max(minLeft, workArea.Right - width - Layout.EdgePadding);
        int minTop = workArea.Y + Layout.EdgePadding;
        int maxTop = Math.Max(minTop, workArea.Bottom - height - Layout.EdgePadding);

        return new PixelPoint(
            Math.Clamp(target.X, minLeft, maxLeft),
            Math.Clamp(target.Y, minTop, maxTop));
    }

    private int CurrentPixelWidth() =>
        Math.Max(Layout.PixelMinSizeInt, (int)Math.Ceiling(Math.Max(Bounds.Width, Width) * RenderScaling));

    private int CurrentPixelHeight() =>
        Math.Max(Layout.PixelMinSizeInt, (int)Math.Ceiling(Math.Max(Bounds.Height, MinHeight) * RenderScaling));

    private (PixelPoint DockedPosition, int SnapTolerance) CaptureDockedPosition()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        int snapTolerance = Math.Max(Layout.PixelMinSizeInt,
            (int)Math.Round(Math.Min(workArea.Width, workArea.Height) * Layout.SnapTolerancePercent));
        return (ResolveDockedPosition(_lastTrayIcon), snapTolerance);
    }

    private PixelRect ResolveWorkArea(TrayAppDotNETShellTrayIcon? trayIcon)
    {
        PixelPoint anchor = Position;
        if (trayIcon?.TryGetIconRect(out PixelRect iconRect) == true)
            anchor = iconRect.Center;

        return (Screens.ScreenFromPoint(anchor) ?? Screens.Primary)?.WorkingArea
               ?? FallbackWorkArea();
    }

    private void ApplyWorkAreaMaxHeight()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        MaxHeight = Math.Max(Layout.WorkAreaMinHeight, workArea.Height / RenderScaling - (Layout.EdgePadding * 2));
    }

    private void StartFlyoutActivity()
    {
        _audioManager.StartMetering();
        if (_settings.FlyoutCommunicationsButtonVisibility != CommunicationsButtonVisibility.Hidden)
            CommunicationsDucking.Start();
        else
            CommunicationsDucking.Stop();
    }

    private void StopFlyoutActivity()
    {
        _audioManager.StopMetering();
        CommunicationsDucking.Stop();
        SetAllGroupMetersVisible(false);
    }

    private void ScrollCellsToBottom()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ScrollViewer? scroll = _cellsScrollViewer;
            if (scroll == null) return;

            double maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, maxOffset);
        }, DispatcherPriority.Loaded);
    }

    private void Rebuild()
    {
        if (_layout == null) return;
        if (_isRebuilding) return;
        _isRebuilding = true;
        try
        {
            double? previousScrollOffset = _cellsScrollViewer?.Offset.Y;
            foreach (Action cleanup in _cleanup)
            {
                try { cleanup(); }
                catch { }
            }

            _cleanup.Clear();
            _hoveredDeviceStateButtonCount = 0;
            _deviceOrderingRebuildPending = false;
            CloseOpenMenu();

            bool isLight = ResolveEffectiveIsLight();
            SettingsPalette p = VolumeSettingsPalette.Create(AppServices.Theme, _settings, isLight);
            FlyoutPalette fp = FlyoutPalette.Create(p, AppServices.Theme, _settings, isLight);

            List<AudioDevice> devices = FlyoutDeviceOrdering.Build(_audioManager.Devices, _settings);
            StackPanel cellStack = new() { Spacing = 0 };
            for (int i = 0; i < devices.Count; i++)
                cellStack.Children.Add(BuildCell(devices[i], fp, isFirst: i == 0, isLast: i == devices.Count - 1));

            Grid body = new() { ClipToBounds = true };
            ScrollViewer scroll = new()
            {
                MaxHeight = ResolveMaxContentHeight(),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Content = cellStack,
            };
            _cellsScrollViewer = scroll;
            if (previousScrollOffset.HasValue)
                RestoreCellsScrollOffset(scroll, previousScrollOffset.Value);
            body.Children.Add(scroll);

            TextBlock empty = Text(L("Flyout_NoAudioDevices", "No audio devices"), fp, Layout.EmptyDevicesFontSize);
            empty.Opacity = Layout.EmptyDevicesOpacity;
            empty.Foreground = Brush(fp.SecondaryForeground);
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            empty.Margin = Layout.EmptyDevicesMargin;
            empty.IsVisible = devices.Count == 0;
            body.Children.Add(empty);

            DockPanel root = new() { LastChildFill = true };
            Control header = BuildHeader(fp);
            DockPanel.SetDock(header, _settings.FlyoutHeaderAtBottom ? Dock.Bottom : Dock.Top);
            root.Children.Add(header);
            root.Children.Add(body);

            Border chrome = new()
            {
                Background = Brush(fp.Background),
                BorderBrush = Brush(fp.Border),
                BorderThickness = Layout.ChromeBorderThickness,
                CornerRadius = Rounded(Layout.ChromeCornerRadius),
                ClipToBounds = false,
                Child = new Border
                {
                    Background = Brush(fp.Background),
                    CornerRadius = Rounded(Layout.ChromeInnerCornerRadius),
                    ClipToBounds = true,
                    Margin = Layout.ChromeInnerMargin,
                    Child = root,
                },
            };
            chrome.PointerPressed += OnChromePointerPressed;
            chrome.PointerMoved += OnChromePointerMoved;
            chrome.PointerReleased += OnChromePointerReleased;
            chrome.PointerCaptureLost += OnChromePointerCaptureLost;

            Content = chrome;

            QueuePositionNearTray();
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    private void RestoreCellsScrollOffset(ScrollViewer scroll, double offset)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_cellsScrollViewer, scroll)) return;

            double maxOffset = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(offset, 0, maxOffset));
        }, DispatcherPriority.Loaded);
    }

    private Grid BuildHeader(FlyoutPalette p)
    {
        Grid grid = new() { MinHeight = Layout.HeaderMinHeight, Background = Brush(p.Background), };
        bool bottomHeader = _settings.FlyoutHeaderAtBottom;

        StackPanel left = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = HeaderVerticalAlignment(),
            Margin = bottomHeader ? CenteredHeaderMargin(Layout.HeaderLeftMarginBottom) : Layout.HeaderLeftMarginTop,
        };

        Border settingsButton = HeaderIconButton(GlyphCatalog.SETTINGS, p, _openSettings,
            L("Flyout_Settings_Tooltip", "Settings"));
        SuppressNextAutoHideWhenPressed(settingsButton);
        left.Children.Add(settingsButton);
        left.Children.Add(HeaderIconButton(GlyphCatalog.SOUND_SETTINGS, p,
            () => DeviceShellLinks.OpenSoundSettings(_settings.SoundSettingsTarget),
            L("Flyout_SoundSettings_Tooltip", "Sound settings")));

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

        left.Children.Add(HeaderIconButton(string.Empty, p, () => { }, null, enabled: false));
        grid.Children.Add(left);

        if (IsUpdateButtonVisible)
        {
            Border update = TextButton(L("Flyout_Update_ButtonText", "Update!"), p, ShowUpdateConfirmation);
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
            IsHitTestVisible = false,
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
            Child = drawer,
        };
        Grid.SetRow(appBand, appsBottom ? 1 : 0);
        content.Children.Add(appBand);

        Border deviceBand = new()
        {
            Background = Brush(p.FooterBackground),
            Padding = appsBottom ? Layout.DeviceBandBottomPadding : Layout.DeviceBandTopPadding,
            CornerRadius = ResolveDeviceBandRadius(isLast, appsBottom, drawerVisible),
            Child = BuildDeviceRow(device, groups, p),
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
            IsHitTestVisible = false,
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
            Content = stack,
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
            MaxWidth = Layout.AppIconGridSlotSize * Math.Max(1, AppDrawerIconsPerRow),
        };

        IEnumerable<AudioAppGroup> ordered = ResolveGridOrder(groups);
        foreach (AudioAppGroup group in ordered)
            panel.Children.Add(BuildAppIconCell(device, group, p));

        return new ScrollViewer
        {
            MaxHeight = ResolveGridDrawerMaxHeight(device),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel,
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
                new ColumnDefinition(GridLength.Auto),
            },
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

        group.PropertyChanged += OnGroupChanged;
        _cleanup.Add(() => group.PropertyChanged -= OnGroupChanged);
        return grid;

        void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AudioAppGroup.Volume))
            {
                slider.Value = group.Volume * 100.0;
                percent.Text = ScalarText(group.Volume);
            }
            else if (e.PropertyName == nameof(AudioAppGroup.PeakValues))
                slider.PeakValues = SliderPeaks(group.PeakValues);
            else if (e.PropertyName is nameof(AudioAppGroup.IsMuted) or nameof(AudioAppGroup.State)
                     or nameof(AudioAppGroup.Icon)) Dispatcher.UIThread.Post(Rebuild);
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
            Cursor =
                device.IsCaptureDevice ? new Cursor(StandardCursorType.Arrow) : new Cursor(StandardCursorType.Hand),
            Opacity = ResolveAppOpacity(device, group),
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
            IsHitTestVisible = false,
        };
        cell.PointerEntered += (_, _) => hover.Background = Brush(p.ButtonHover);
        cell.PointerExited += (_, _) => hover.Background = Brushes.Transparent;
        cell.Children.Add(hover);

        Control icon = BuildAppIcon(device, group, p, imageSize, glyphSize, clickable: false);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        cell.Children.Add(icon);

        if (device.IsCaptureDevice && _settings.CaptureActivityIndicator == CaptureActivityIndicator.ActiveGlyph
                                   && group.State == AudioSessionState.Active)
        {
            TextBlock badge = Text(device.IsExclusiveControlHeld ? GlyphCatalog.LOCK : GlyphCatalog.CIRCLE, p,
                Layout.AppIconCellBadgeFontSize);
            badge.FontFamily = TrayAppDotNETSettingsUI.IconFont;
            badge.Foreground = Brush(p.IconForeground);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Bottom;
            badge.Margin = Layout.AppIconCellBadgeMargin;
            cell.Children.Add(badge);
        }

        if (!device.IsCaptureDevice)
        {
            cell.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                group.IsMuted = !group.IsMuted;
                e.Handled = true;
                Rebuild();
            };
        }

        group.PropertyChanged += OnGroupChanged;
        _cleanup.Add(() => group.PropertyChanged -= OnGroupChanged);
        return cell;

        void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AudioAppGroup.IsMuted) or nameof(AudioAppGroup.Icon)
                or nameof(AudioAppGroup.State))
                Dispatcher.UIThread.Post(Rebuild);
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
                ? new Cursor(StandardCursorType.Hand)
                : new Cursor(StandardCursorType.Arrow),
        };
        TrayAppDotNETToolTip.SetTip(root, group.TooltipText);

        if (group.Icon != null)
        {
            root.Children.Add(new Image
            {
                Source = group.Icon,
                Width = imageSize,
                Height = imageSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            TextBlock fallback = Text(GlyphCatalog.APP_FALLBACK, p, glyphSize);
            fallback.FontFamily = TrayAppDotNETSettingsUI.IconFont;
            fallback.Foreground = Brush(p.IconForeground);
            fallback.HorizontalAlignment = HorizontalAlignment.Center;
            fallback.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(fallback);
        }

        TextBlock muteOverlay = Text(GlyphCatalog.APP_MUTE_OVERLAY, p, glyphSize);
        muteOverlay.FontFamily = TrayAppDotNETSettingsUI.IconFont;
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
                new ColumnDefinition(GridLength.Auto),
            },
        };

        Grid nameStack = new() { Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, };
        if (_settings is { ShowDeviceFormatText: false, ShowDeviceCodecText: false })
            nameStack.RenderTransform = CloneTransform(Layout.DeviceTitleNameNoFormatTransform);

        TextBlock name = Text(device.FriendlyName, p, Layout.DeviceTitleFontSize);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.VerticalAlignment = VerticalAlignment.Center;
        nameStack.PointerPressed += (_, e) =>
        {
            if (e.ClickCount != 2 || e.GetCurrentPoint(nameStack).Properties.PointerUpdateKind !=
                PointerUpdateKind.LeftButtonPressed) return;
            BeginDeviceNameEdit(nameStack, device, p);
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
                VerticalAlignment = VerticalAlignment.Center,
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
            ShowDefaultFormatMenu(nameStack, device, p);
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
        _cleanup.Add(() => device.PropertyChanged -= OnDeviceChanged);
        return row;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AudioDevice.FriendlyName))
                name.Text = device.FriendlyName;
            else if ((e.PropertyName is nameof(AudioDevice.IsDefault) or nameof(AudioDevice.IsDefaultCommunications))
                     && !IsDefaultDeviceButtonVisible(device))
                RunOnUiThread(QueueDeviceOrderingRebuild);
            else if (DeviceRebuildProperties.Contains(e.PropertyName ?? string.Empty))
                Dispatcher.UIThread.Post(Rebuild);
        }
    }

    private Grid BuildDeviceSliderRow(AudioDevice device, FlyoutPalette p)
    {
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
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

        device.PropertyChanged += OnDeviceChanged;
        _cleanup.Add(() => device.PropertyChanged -= OnDeviceChanged);
        UpdateMutedActiveVisuals();
        return row;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AudioDevice.Volume))
            {
                slider.Value = device.Volume * 100.0;
                percent.Text = ScalarText(device.Volume);
            }
            else if (e.PropertyName == nameof(AudioDevice.PeakValues))
                slider.PeakValues = SliderPeaks(device.PeakValues);
            else if (e.PropertyName == nameof(AudioDevice.IsMuted))
                RunOnUiThread(UpdateMutedActiveVisuals);
            else if (e.PropertyName is nameof(AudioDevice.IsActive) or nameof(AudioDevice.State))
                Dispatcher.UIThread.Post(Rebuild);
        }

        void UpdateMutedActiveVisuals()
        {
            double opacity = device.IsMuted || !device.IsActive ? 0.4 : 1.0;
            slider.Opacity = opacity;
            percentHost.Opacity = opacity;
        }
    }

    private FlyoutSlider BuildVolumeSlider(
        float scalar,
        MeterPeakValues peaks,
        FlyoutPalette p,
        Action<double> setPercent,
        Action<bool> playFeedback)
    {
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
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        bool updating = false;
        bool dragging = false;
        slider.DragStarted += (_, _) => dragging = true;
        slider.DragCompleted += (_, _) =>
        {
            dragging = false;
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
            Margin = Layout.PercentHostMargin,
        };

        TextBlock label = Text(ScalarText(scalar), p, Layout.PercentFontSize);
        label.Background = Brushes.Transparent;
        label.TextAlignment = TextAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Cursor = new Cursor(StandardCursorType.Ibeam);

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
            TextAlignment = TextAlignment.Right,
        };

        host.Children.Add(label);
        host.Children.Add(editor);
        return (host, label, editor);
    }

    private static void WirePercentEditor(TextBlock label, TextBox editor, FlyoutSlider slider,
        Action<double> setPercent)
    {
        label.PointerPressed += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            if (!slider.IsEnabled || !slider.IsVisible) return;

            editor.Text = label.Text;
            label.IsVisible = false;
            editor.IsVisible = true;
            editor.Focus();
            editor.SelectAll();
            e.Handled = true;
        };

        editor.KeyDown += (_, e) =>
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
        };

        editor.LostFocus += (_, _) =>
        {
            if (!editor.IsVisible) return;
            CommitPercentEditor(label, editor, slider, setPercent);
        };
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
        string normalGlyph = DeviceVolumeGlyph(device);
        bool isPointerOver = false;
        TextBlock glyph = Text(normalGlyph, p, Layout.DeviceMuteGlyphFontSize);
        glyph.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        glyph.Foreground = Brush(p.IconForeground);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        ApplyDeviceMuteGlyphStyle(glyph, normalGlyph);

        Grid slot = new()
        {
            Width = Layout.DeviceMuteSlotWidth,
            Height = Layout.DeviceMuteSlotHeight,
            ClipToBounds = false,
            Children = { glyph },
        };

        Border button = DeviceIconButton(string.Empty, p, () => device.IsMuted = !device.IsMuted,
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
        _cleanup.Add(() => device.PropertyChanged -= OnDeviceChanged);

        return button;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(AudioDevice.Volume)
                or nameof(AudioDevice.IsMuted)
                or nameof(AudioDevice.IsActive)
                or nameof(AudioDevice.IsCaptureSleeping)
                or nameof(AudioDevice.IsListeningToThisDevice))) return;

            RunOnUiThread(UpdateVisual);
        }

        void UpdateVisual()
        {
            normalGlyph = DeviceVolumeGlyph(device);
            string visibleGlyph = isPointerOver ? DeviceMuteTogglePreviewGlyph(device) : normalGlyph;
            glyph.Text = visibleGlyph;
            ApplyDeviceMuteGlyphStyle(glyph, visibleGlyph);

            button.Opacity = device.IsMuted || !device.IsActive ? 0.4 : 1.0;
            TrayAppDotNETToolTip.SetTip(
                button,
                device.IsMuted
                    ? L("Flyout_DeviceUnmute_Tooltip", "Unmute")
                    : L("Flyout_DeviceMute_Tooltip", "Mute"));
        }
    }

    private void ApplyDeviceMuteGlyphStyle(TextBlock glyph, string text)
    {
        if (IsMicrophoneGlyph(text))
        {
            glyph.FontSize = Layout.DeviceMuteMicrophoneGlyphFontSize;
            glyph.FontWeight = FontWeight.ExtraBold;
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.RenderTransform = CloneTransform(Layout.DeviceMuteMicrophoneTransform);
            PreventIconGlyphClipping(glyph, Layout.IconGlyphLineHeightPadding);
            return;
        }

        glyph.FontSize = Layout.DeviceMuteGlyphFontSize;
        glyph.FontWeight = FontWeight.Normal;
        glyph.HorizontalAlignment = HorizontalAlignment.Right;
        glyph.RenderTransform = null;
        PreventIconGlyphClipping(glyph, Layout.IconGlyphLineHeightPadding);
    }

    private static bool IsMicrophoneGlyph(string glyph) =>
        glyph is GlyphCatalog.MICROPHONE
            or GlyphCatalog.MICROPHONE_OFF
            or GlyphCatalog.MICROPHONE_LISTENING
            or GlyphCatalog.MICROPHONE_SLEEP;

    private Border? BuildBatteryButton(AudioDevice device, FlyoutPalette p)
    {
        bool visible = device.BatteryLevel.HasValue
                       && (device.IsCaptureDevice
                           ? _settings.ShowBatteryButtonForRecording
                           : _settings.ShowBatteryButtonForPlayback);
        if (!visible) return null;

        Border button = DeviceIconButton(BatteryGlyph(device.BatteryLevel!.Value), p, () => { });
        button.Focusable = false;
        TrayAppDotNETToolTip.SetTip(button,
            string.Format(L("Flyout_BatteryButton_Tooltip_Format", "{0}% battery"), device.BatteryLevel.Value));
        return button;
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
        _cleanup.Add(() => device.PropertyChanged -= OnDeviceChanged);
        return button;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(AudioDevice.IsExclusiveModeAllowed)
                or nameof(AudioDevice.IsExclusiveControlHeld))) return;

            RunOnUiThread(UpdateVisual);
        }

        void UpdateVisual()
        {
            if (glyph != null) glyph.Text = ExclusiveButtonGlyph(device);
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

        Border button = DeviceIconButton(string.Empty, p, e =>
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
        equalizer.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        equalizer.Foreground = Brush(p.IconForeground);
        PreventIconGlyphClipping(equalizer, Layout.IconGlyphLineHeightPadding);
        glyphs.Children.Add(equalizer);
        TextBlock badge = Text(GlyphCatalog.SIGNAL_NOT_CONNECTED, p, Layout.EqualizerBadgeFontSize,
            FontWeight.ExtraBold);
        badge.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        badge.Foreground = Brush(p.IconForeground);
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        badge.VerticalAlignment = VerticalAlignment.Bottom;
        badge.Margin = Layout.EqualizerBadgeMargin;
        PreventIconGlyphClipping(badge, Layout.IconGlyphLineHeightPadding);
        glyphs.Children.Add(badge);

        button.Child = glyphs;
        UpdateVisual();

        device.PropertyChanged += OnDeviceChanged;
        _cleanup.Add(() => device.PropertyChanged -= OnDeviceChanged);
        return button;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AudioDevice.EqualizerAPOState)) return;
            RunOnUiThread(UpdateVisual);
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

        Border? button = null;
        button = DeviceIconButton(GlyphCatalog.EAR_LISTEN, p, e =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
                device.SetListenTarget(null, enable: true);
            else
                device.SetListenEnabled(!device.IsListeningToThisDevice);
        }, rightClick: _ => ShowListenTargetMenu(button!, device, p));
        TrayAppDotNETToolTip.SetTip(button, L("Flyout_ListenButton_Tooltip", "Listen to this device"));
        UpdateVisual();

        device.PropertyChanged += OnDeviceChanged;
        _cleanup.Add(() => device.PropertyChanged -= OnDeviceChanged);
        return button;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AudioDevice.IsListeningToThisDevice)) return;
            RunOnUiThread(UpdateVisual);
        }

        void UpdateVisual() => button.Opacity = device.IsListeningToThisDevice ? 1.0 : 0.4;
    }

    private Border? BuildDeviceStateButton(AudioDevice device, FlyoutPalette p)
    {
        if (!IsDefaultDeviceButtonVisible(device)) return null;

        Border button = DeviceIconButton(string.Empty, p, e =>
        {
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                device.SetAsDefaultCommunications();
            else if ((e.KeyModifiers & KeyModifiers.Control) != 0)
                device.SetEnabled(!device.IsActive);
            else
                device.SetAsDefault();
        }, rightClick: _ => DeviceShellLinks.OpenDeviceProperties(device));
        TextBlock glyph = Text(
            DeviceStateGlyph(device),
            p,
            DeviceStateGlyph(device) == GlyphCatalog.PLAYBACK_DEVICE_DISABLED
                ? Layout.DeviceStateDisabledFontSize
                : Layout.DeviceStateFontSize);
        glyph.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        glyph.Foreground = Brush(p.IconForeground);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        button.Child = glyph;
        TrayAppDotNETToolTip.SetTip(button, L("Flyout_DeviceIcon_Tooltip", "Set as default device"));
        TrackDeviceStateButtonHover(button);
        UpdateVisual();

        device.PropertyChanged += OnDeviceChanged;
        _cleanup.Add(() => device.PropertyChanged -= OnDeviceChanged);
        return button;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(AudioDevice.IsDefault)
                or nameof(AudioDevice.IsDefaultCommunications)
                or nameof(AudioDevice.IsActive)
                or nameof(AudioDevice.State))) return;

            RunOnUiThread(() =>
            {
                UpdateVisual();
                if (e.PropertyName is nameof(AudioDevice.IsDefault) or nameof(AudioDevice.IsDefaultCommunications))
                    QueueDeviceOrderingRebuild();
            });
        }

        void UpdateVisual()
        {
            string stateGlyph = DeviceStateGlyph(device);
            glyph.Text = stateGlyph;
            glyph.FontSize = stateGlyph == GlyphCatalog.PLAYBACK_DEVICE_DISABLED
                ? Layout.DeviceStateDisabledFontSize
                : Layout.DeviceStateFontSize;
            glyph.RenderTransform = stateGlyph == GlyphCatalog.PLAYBACK_DEVICE_DISABLED
                ? CloneTransform(Layout.DeviceStateDisabledTransform)
                : null;
            PreventIconGlyphClipping(glyph, Layout.IconGlyphLineHeightPadding);
            button.Opacity = device.IsActive ? 1.0 : 0.4;
        }
    }

    private bool IsDefaultDeviceButtonVisible(AudioDevice device) =>
        device.IsCaptureDevice
            ? _settings.ShowDefaultDeviceButtonForRecording
            : _settings.ShowDefaultDeviceButtonForPlayback;

    private void TrackDeviceStateButtonHover(Control button)
    {
        bool pointerOver = false;
        button.PointerEntered += (_, _) =>
        {
            if (pointerOver) return;
            pointerOver = true;
            _hoveredDeviceStateButtonCount++;
        };
        button.PointerExited += (_, _) =>
        {
            if (!pointerOver) return;
            pointerOver = false;
            _hoveredDeviceStateButtonCount = Math.Max(0, _hoveredDeviceStateButtonCount - 1);
            FlushDeviceOrderingRebuild();
        };
        _cleanup.Add(() =>
        {
            if (!pointerOver) return;
            pointerOver = false;
            _hoveredDeviceStateButtonCount = Math.Max(0, _hoveredDeviceStateButtonCount - 1);
        });
    }

    private void QueueDeviceOrderingRebuild()
    {
        if (_settings.FlyoutDeviceSort != FlyoutDeviceSortOrder.StateGrouped) return;

        _deviceOrderingRebuildPending = true;
        FlushDeviceOrderingRebuild();
    }

    private void FlushDeviceOrderingRebuild()
    {
        if (!_deviceOrderingRebuildPending || _hoveredDeviceStateButtonCount > 0) return;

        _deviceOrderingRebuildPending = false;
        Dispatcher.UIThread.Post(Rebuild);
    }

    private Border BuildDrawerButton(AudioDevice device, IReadOnlyList<AudioAppGroup> groups, FlyoutPalette p)
    {
        bool hasGroups = groups.Count > 0;
        bool expanded = IsAppDrawerExpanded(device);
        Border button = DeviceIconButton(expanded ? GlyphCatalog.CHEVRON_UP : GlyphCatalog.CHEVRON_DOWN, p, () =>
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

    private Border HeaderIconButton(string glyph, FlyoutPalette p, Action click, string? tooltip,
        bool enabled = true) =>
        HeaderIconButton(glyph, p, _ => click(), tooltip, enabled);

    private Border HeaderIconButton(string glyph, FlyoutPalette p, Action<PointerReleasedEventArgs> click,
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
        TextBlock text = Text(UndockButtonGlyph(), p, Layout.HeaderUndockFontSize);
        text.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        text.Foreground = Brush(p.IconForeground);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.LineHeight = Layout.HeaderUndockLineHeight;

        Border button = new()
        {
            Width = Layout.HeaderIconButtonWidth,
            Height = Layout.HeaderIconButtonHeight,
            Margin = Layout.HeaderIconButtonMargin,
            CornerRadius = Rounded(Layout.HeaderIconButtonCornerRadius),
            Background = Brushes.Transparent,
            Child = text,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        _undockButton = button;
        _undockButtonGlyph = text;
        _cleanup.Add(() =>
        {
            if (ReferenceEquals(_undockButton, button))
            {
                _undockButton = null;
                _undockButtonGlyph = null;
            }
        });
        UpdateUndockButtonVisual();
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);

        bool pointerInside = false;
        button.PointerEntered += (_, _) =>
        {
            pointerInside = true;
            if (!_isDraggingWindow) button.Background = Brush(p.Pressed);
        };
        button.PointerExited += (_, _) =>
        {
            pointerInside = false;
            if (!_isDraggingWindow) button.Background = Brushes.Transparent;
        };
        button.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(button).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;

            pointerInside = true;
            BeginUndockButtonDrag(button, e);
            button.Background = Brush(p.Pressed);
            e.Handled = true;
        };
        button.PointerMoved += (_, e) =>
        {
            if (!_undockButtonPointerCaptured) return;
            ContinueUndockButtonDrag(button, e);
            e.Handled = true;
        };
        button.PointerReleased += (_, e) =>
        {
            if (!_undockButtonPointerCaptured || e.InitialPressMouseButton != MouseButton.Left) return;

            bool releasedInside = IsPointerInside(button, e);
            FinishUndockButtonDrag(e.Pointer, commitDrag: true, clickWhenNotDragged: releasedInside);
            button.Background = releasedInside ? Brush(p.Pressed) : Brushes.Transparent;
            e.Handled = true;
        };
        button.PointerCaptureLost += (_, _) =>
        {
            if (!_undockButtonPointerCaptured) return;

            FinishUndockButtonDrag(null, commitDrag: _undockButtonDragOccurred, clickWhenNotDragged: false);
            button.Background = pointerInside ? Brush(p.Pressed) : Brushes.Transparent;
        };

        return button;
    }

    private Border DeviceIconButton(
        string glyph,
        FlyoutPalette p,
        Action click,
        double? width = null,
        double? height = null,
        double? fontSize = null,
        bool enabled = true) =>
        DeviceIconButton(glyph, p, _ => click(), null, width, height, fontSize, enabled);

    private Border DeviceIconButton(
        string glyph,
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
        string glyph,
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
        if (string.IsNullOrEmpty(glyph) || fontSize <= 0)
            content = new Grid { IsHitTestVisible = false, ClipToBounds = false };
        else
        {
            TextBlock text = Text(glyph, p, fontSize);
            text.FontFamily = TrayAppDotNETSettingsUI.IconFont;
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
            Cursor = enabled ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow),
            IsEnabled = enabled,
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
            Cursor = new Cursor(StandardCursorType.Hand),
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

    private static bool IsPointerInside(Control control, PointerEventArgs e)
    {
        Point point = e.GetPosition(control);
        return point is { X: >= 0, Y: >= 0 }
               && point.X <= control.Bounds.Width
               && point.Y <= control.Bounds.Height;
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private void BeginUndockButtonDrag(Control source, PointerPressedEventArgs e)
    {
        (PixelPoint dockedPosition, int snapTolerance) = CaptureDockedPosition();
        PixelPoint pointer = source.PointToScreen(e.GetPosition(source));

        _dragHelper.BeginDrag(pointer, Position, dockedPosition, snapTolerance);
        _undockButtonPointerCaptured = true;
        _undockButtonDragOccurred = false;
        _isDraggingWindow = true;
        e.Pointer.Capture(source);
    }

    private void ContinueUndockButtonDrag(Control source, PointerEventArgs e)
    {
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            FinishUndockButtonDrag(e.Pointer, commitDrag: true, clickWhenNotDragged: false);
            return;
        }

        PixelPoint pointer = source.PointToScreen(e.GetPosition(source));
        PixelPoint natural = _dragHelper.ComputeNatural(pointer);

        if (!_undockButtonDragOccurred)
        {
            double thresholdPixels = Layout.DragThreshold * RenderScaling;
            if (!_dragHelper.ExceedsThreshold(natural, thresholdPixels)) return;

            _undockButtonDragOccurred = true;
            _isUndocked = true;
            UpdateUndockButtonVisual();
        }

        _dragHelper.ApplyDragPosition(this, natural);
    }

    private void FinishUndockButtonDrag(IPointer? pointer, bool commitDrag, bool clickWhenNotDragged)
    {
        bool dragOccurred = _undockButtonDragOccurred;
        _undockButtonPointerCaptured = false;
        _undockButtonDragOccurred = false;
        _isDraggingWindow = false;
        pointer?.Capture(null);

        if (dragOccurred)
        {
            if (commitDrag) CommitDragPosition(rebuildAfterSave: true);
            return;
        }

        if (clickWhenNotDragged) ToggleUndocked();
    }

    private void ToggleUndocked()
    {
        if (_isUndocked)
        {
            Redock();
            return;
        }

        UndockToSavedPosition();
    }

    private void UndockToSavedPosition()
    {
        _isUndocked = true;
        UpdateUndockButtonVisual();
        _settings.FlyoutUndocked = true;
        _settings.Save();

        if (_settings.FlyoutHasSavedPosition)
            Position = ResolvePosition(_lastTrayIcon);

        Rebuild();
    }

    private string UndockButtonGlyph() =>
        _isUndocked ? GlyphCatalog.FLYOUT_REDOCK_ACTION : GlyphCatalog.FLYOUT_UNDOCK_ACTION;

    private string UndockButtonTooltip() =>
        _isUndocked ? L("Flyout_Redock_Tooltip", "Redock") : L("Flyout_Undock_Tooltip", "Undock");

    private void UpdateUndockButtonVisual()
    {
        _undockButtonGlyph?.Text = UndockButtonGlyph();
        if (_undockButton != null) TrayAppDotNETToolTip.SetTip(_undockButton, UndockButtonTooltip());
    }

    private static void ToggleCommunicationsDucking(KeyModifiers modifiers)
    {
        CommunicationsDuckingMode mode;
        if ((modifiers & KeyModifiers.Alt) != 0) mode = CommunicationsDuckingMode.Reduce50;
        else if ((modifiers & KeyModifiers.Control) != 0) mode = CommunicationsDuckingMode.Reduce80;
        else
            mode = CommunicationsDucking.IsActive()
                ? CommunicationsDuckingMode.DoNothing
                : CommunicationsDuckingMode.MuteAll;

        CommunicationsDucking.SetMode(mode);
    }

    private async void ShowUpdateConfirmation()
    {
        if (_isUpdateDownloadInFlight) return;
        UpdateCheckService? svc = AppServices.UpdateCheckService;
        UpdateInfo? info = svc?.AvailableUpdate;
        if (svc == null || info == null) return;

        TrayAppDotNETUpdateConfirmationWindow dialog =
            new(info, VolumeSettingsPalette.Create(AppServices.Theme, _settings, ResolveEffectiveIsLight()),
                _settings.EnableRoundedCorners) { WindowStartupLocation = WindowStartupLocation.CenterOwner, };
        bool result = await dialog.ShowDialog<bool>(this);
        if (!result) return;

        _isUpdateDownloadInFlight = true;
        try
        {
            bool staged = await svc.DownloadAndStageAsync(info);
            if (staged && Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                _isUpdateDownloadInFlight = false;
        }
        catch (Exception ex)
        {
            _isUpdateDownloadInFlight = false;
            TADNLog.Log($"VolumeFlyout.InstallAvailableUpdate: {ex.Message}");
        }
    }

    private void ShowDefaultFormatMenu(Control anchor, AudioDevice device, FlyoutPalette p)
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
        ShowFlyoutMenu(anchor, entries, p, maxHeight);
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

    private void ShowListenTargetMenu(Control anchor, AudioDevice captureDevice, FlyoutPalette p)
    {
        string? currentTarget = captureDevice.ListenTargetDeviceID;
        List<FlyoutMenuEntry> entries =
        [
            new(
                L("Flyout_ListenMenu_DefaultPlaybackDevice", "Default Playback Device"),
                currentTarget == null,
                () => captureDevice.SetListenTarget(null, enable: true)),
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

        ShowFlyoutMenu(anchor, entries, p);
    }

    private void ShowFlyoutMenu(Control anchor, IReadOnlyList<FlyoutMenuEntry> entries, FlyoutPalette p,
        double maxHeight = double.PositiveInfinity)
    {
        CloseOpenMenu();
        FlyoutMenuWindow menu = new(entries, p, Layout, _settings.ContextMenuFontSize, _settings.EnableRoundedCorners,
            maxHeight);
        _openMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_openMenu, menu)) _openMenu = null;
            if (menu.ClosedFromDeactivation)
                NotifyChildWindowClosedFromDeactivation();
        };
        menu.ShowAt(anchor);
    }

    private void CloseOpenMenu()
    {
        FlyoutMenuWindow? menu = _openMenu;
        _openMenu = null;
        if (menu?.IsVisible == true) menu.Close();
    }

    private bool IsFlyoutMenuOpen => _openMenu?.IsVisible == true;

    private void BeginDeviceNameEdit(Grid host, AudioDevice device, FlyoutPalette p)
    {
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
        bool closed = false;

        editor.KeyDown += OnEditorKeyDown;
        editor.LostFocus += OnEditorLostFocus;
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);

        host.Children.Add(editor);
        Dispatcher.UIThread.Post(() =>
        {
            if (!host.Children.Contains(editor)) return;
            editor.Focus();
            editor.SelectAll();
        }, DispatcherPriority.Input);

        void OnEditorKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Cancel();
                e.Handled = true;
            }
        }

        void OnEditorLostFocus(object? sender, RoutedEventArgs e) => Commit();

        void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (closed) return;
            Point p = e.GetPosition(editor);
            if (p is { X: >= 0, Y: >= 0 } && p.X <= editor.Bounds.Width && p.Y <= editor.Bounds.Height) return;
            Commit();
        }

        void Commit()
        {
            if (closed) return;
            closed = true;
            CleanupHandlers();
            if (!host.Children.Contains(editor)) return;
            host.Children.Remove(editor);
            device.SetCustomFriendlyName(editor.Text);
        }

        void Cancel()
        {
            if (closed) return;
            closed = true;
            CleanupHandlers();
            if (host.Children.Contains(editor)) host.Children.Remove(editor);
        }

        void CleanupHandlers()
        {
            editor.KeyDown -= OnEditorKeyDown;
            editor.LostFocus -= OnEditorLostFocus;
            RemoveHandler(PointerPressedEvent, OnWindowPointerPressed);
        }
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
            _settings.EnableRoundedCorners) { WindowStartupLocation = WindowStartupLocation.CenterOwner, };

        _ = ShowEqualizerDialogAsync(dialog);

        async Task ShowEqualizerDialogAsync(TrayAppDotNETUpdateConfirmationWindow prompt)
        {
            bool accepted = await prompt.ShowDialog<bool>(this);
            if (!accepted) return;
            try
            {
                using Process? _ = Process.Start(new ProcessStartInfo
                {
                    FileName = EqualizerAPOMonitor.LatestInstallerURL, UseShellExecute = true,
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
        _cleanup.Add(() => device.PropertyChanged -= OnDeviceChanged);
        ((INotifyCollectionChanged)device.Groups).CollectionChanged += OnDeviceGroupsChanged;
        _cleanup.Add(() => ((INotifyCollectionChanged)device.Groups).CollectionChanged -= OnDeviceGroupsChanged);

        foreach (AudioAppGroup group in device.Groups)
            HookGroupForRebuild(group);
        return;

        void OnDeviceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AudioDevice.Groups)) return;
            Dispatcher.UIThread.Post(Rebuild);
        }
    }

    private void OnDeviceGroupsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(Rebuild);

    private void HookGroupForRebuild(AudioAppGroup group)
    {
        group.PropertyChanged += OnGroupChanged;
        _cleanup.Add(() => group.PropertyChanged -= OnGroupChanged);
        return;

        void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
        {
            string propertyName = e.PropertyName ?? string.Empty;
            if (propertyName is nameof(AudioAppGroup.State) or nameof(AudioAppGroup.Icon)
                or nameof(AudioAppGroup.DisplayName) or nameof(AudioAppGroup.IsMuted))
                Dispatcher.UIThread.Post(Rebuild);
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
        if (entry != null) return entry.IsAppDrawerExpanded;
        return _settings.DefaultAppDrawerExpanded;
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
        _ => true,
    };

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

        if (format.Length > 0 && codec.Length > 0) return format + ", " + codec;
        if (format.Length > 0) return format;
        return codec;
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
        X = transform.X, Y = transform.Y,
    };

    private double ResolveMaxContentHeight()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        return Math.Max(Layout.WorkAreaMinHeight,
            workArea.Height / RenderScaling - (Layout.EdgePadding * 2) - Layout.ContentHeightReserve);
    }

    private void OnChromePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isUndocked) return;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        if (IsInteractiveDragSource(e.Source)) return;
        if (sender is not Control control) return;

        BeginWindowDrag(control, e);
        e.Handled = true;
    }

    private void BeginWindowDrag(Control source, PointerPressedEventArgs e)
    {
        (PixelPoint dockedPosition, int snapTolerance) = CaptureDockedPosition();
        PixelPoint pointer = source.PointToScreen(e.GetPosition(source));

        _dragHelper.BeginDrag(pointer, Position, dockedPosition, snapTolerance);
        _isDraggingWindow = true;
        e.Pointer.Capture(source);
    }

    private void OnChromePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingWindow || _undockButtonPointerCaptured) return;
        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            EndWindowDrag(e.Pointer, commit: true);
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
        if (!_isDraggingWindow || _undockButtonPointerCaptured) return;
        EndWindowDrag(e.Pointer, commit: true);
        e.Handled = true;
    }

    private void OnChromePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isDraggingWindow || _undockButtonPointerCaptured) return;
        _isDraggingWindow = false;
        CommitDragPosition();
    }

    private void EndWindowDrag(IPointer pointer, bool commit)
    {
        _isDraggingWindow = false;
        pointer.Capture(null);
        if (commit) CommitDragPosition();
    }

    private void CommitDragPosition(bool rebuildAfterSave = false)
    {
        if (_dragHelper.IsCurrentlySnapped)
        {
            Redock();
            return;
        }

        SaveCurrentFlyoutPosition();
        if (rebuildAfterSave) Rebuild();
    }

    private void SaveCurrentFlyoutPosition()
    {
        if (!_isUndocked) return;
        _settings.FlyoutUndocked = true;
        _settings.FlyoutHasSavedPosition = true;
        _settings.FlyoutLeft = Position.X;
        _settings.FlyoutTop = Position.Y;
        _settings.Save();
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
        _ => AppServices.Theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme,
    };

    private string DeviceVolumeGlyph(AudioDevice device)
    {
        if (device.IsCaptureDevice) return CaptureDeviceVolumeGlyph(device, device.IsMuted);

        return PlaybackDeviceVolumeGlyph(device, device.IsMuted);
    }

    private string DeviceMuteTogglePreviewGlyph(AudioDevice device)
    {
        bool mutedAfterToggle = !device.IsMuted;
        if (device.IsCaptureDevice) return CaptureDeviceVolumeGlyph(device, mutedAfterToggle);

        return PlaybackDeviceVolumeGlyph(device, mutedAfterToggle);
    }

    private string PlaybackDeviceVolumeGlyph(AudioDevice device, bool muted)
    {
        if (muted) return GlyphCatalog.PLAYBACK_VOLUME_MUTE;
        return _settings.UseDynamicPlaybackVolumeGlyphInFlyout
            ? GlyphCatalog.GetVolumeTier(device.Volume, muted: false)
            : GlyphCatalog.PLAYBACK_VOLUME_LOW;
    }

    private static string CaptureDeviceVolumeGlyph(AudioDevice device, bool muted)
    {
        if (muted) return GlyphCatalog.MICROPHONE_OFF;
        if (device.IsListeningToThisDevice) return GlyphCatalog.MICROPHONE_LISTENING;
        if (device.IsCaptureSleeping) return GlyphCatalog.MICROPHONE_SLEEP;
        return GlyphCatalog.MICROPHONE;
    }

    private static string ExclusiveButtonGlyph(AudioDevice device) =>
        device is { IsExclusiveModeAllowed: true, IsExclusiveControlHeld: true }
            ? GlyphCatalog.LOCK
            : GlyphCatalog.UNLOCK;

    private static string DeviceStateGlyph(AudioDevice device)
    {
        if (!device.IsActive) return GlyphCatalog.PLAYBACK_DEVICE_DISABLED;
        if (device.IsDefault) return GlyphCatalog.PLAYBACK_DEVICE_DEFAULT;
        if (device.IsDefaultCommunications) return GlyphCatalog.PLAYBACK_DEVICE_DEFAULT_COMMS;
        return GlyphCatalog.PLAYBACK_DEVICE_ENABLED;
    }

    private static string BatteryGlyph(int level)
    {
        int index = (int)Math.Round(level / 10.0);
        index = Math.Clamp(index, 0, 10);
        return index switch
        {
            0 => GlyphCatalog.BATTERY_0,
            1 => GlyphCatalog.BATTERY_1,
            2 => GlyphCatalog.BATTERY_2,
            3 => GlyphCatalog.BATTERY_3,
            4 => GlyphCatalog.BATTERY_4,
            5 => GlyphCatalog.BATTERY_5,
            6 => GlyphCatalog.BATTERY_6,
            7 => GlyphCatalog.BATTERY_7,
            8 => GlyphCatalog.BATTERY_8,
            9 => GlyphCatalog.BATTERY_9,
            _ => GlyphCatalog.BATTERY_10,
        };
    }

    private static string EqualizerTooltip(EqualizerAPOState state) => state switch
    {
        EqualizerAPOState.Running => L("Flyout_EqualizerAPO_Tooltip_Running", "Equalizer APO is running"),
        EqualizerAPOState.EnhancementsOff => L("Flyout_EqualizerAPO_Tooltip_EnhancementsOff",
            "Enhancements are disabled"),
        EqualizerAPOState.NotInstalled => L("Flyout_EqualizerAPO_Tooltip_NotInstalled",
            "Equalizer APO is not installed"),
        _ => L("Flyout_EqualizerAPO_Tooltip_NotAvailable", "Equalizer APO is not available"),
    };

    private static string ScalarText(float scalar) => $"{(int)Math.Round(scalar * 100)}";

    private static TextBlock Text(string text, FlyoutPalette p, double size, FontWeight? weight = null) => new()
    {
        Text = text,
        FontFamily = FlyoutFont,
        FontSize = size,
        FontWeight = weight ?? FontWeight.Normal,
        Foreground = Brush(p.Foreground),
        TextWrapping = TextWrapping.NoWrap,
    };

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
        base.OnClosed(e);

        foreach (Action cleanup in _cleanup)
        {
            try { cleanup(); }
            catch { }
        }

        _cleanup.Clear();

        if (AppServices.UpdateCheckService is { } updateService)
            updateService.StateChanged -= NotifyUpdateStateChanged;
        if (_settings != null) _settings.Changed -= OnSettingsChanged;
        Safe.Dispose(_feedback);
        CloseOpenMenu();
        if (_audioManager != null)
        {
            _audioManager.PropertyChanged -= OnAudioManagerPropertyChanged;
            ((INotifyCollectionChanged)_audioManager.Devices).CollectionChanged -= OnDevicesCollectionChanged;
        }
    }

    private sealed class FlyoutWindowDragHelper
    {
        private PixelPoint _grabOffset;
        private PixelPoint _startPosition;
        private PixelPoint _dockedPosition;
        private int _snapTolerance;

        public bool IsCurrentlySnapped { get; private set; }

        public void BeginDrag(PixelPoint pointerScreenPosition, PixelPoint windowPosition, PixelPoint dockedPosition,
            int snapTolerance)
        {
            _grabOffset = new PixelPoint(
                pointerScreenPosition.X - windowPosition.X,
                pointerScreenPosition.Y - windowPosition.Y);
            _startPosition = windowPosition;
            _dockedPosition = dockedPosition;
            _snapTolerance = snapTolerance;
            IsCurrentlySnapped = IsWithinSnapTolerance(windowPosition);
        }

        public PixelPoint ComputeNatural(PixelPoint pointerScreenPosition) =>
            new(pointerScreenPosition.X - _grabOffset.X, pointerScreenPosition.Y - _grabOffset.Y);

        public bool ExceedsThreshold(PixelPoint naturalPosition, double threshold)
        {
            int dx = naturalPosition.X - _startPosition.X;
            int dy = naturalPosition.Y - _startPosition.Y;
            return dx * dx + dy * dy >= threshold * threshold;
        }

        public void ApplyDragPosition(Window window, PixelPoint naturalPosition)
        {
            if (IsWithinSnapTolerance(naturalPosition))
            {
                window.Position = _dockedPosition;
                IsCurrentlySnapped = true;
                return;
            }

            window.Position = naturalPosition;
            IsCurrentlySnapped = false;
        }

        private bool IsWithinSnapTolerance(PixelPoint position) =>
            Math.Abs(position.X - _dockedPosition.X) <= _snapTolerance
            && Math.Abs(position.Y - _dockedPosition.Y) <= _snapTolerance;
    }

    private sealed class FlyoutLayout
    {
        public int EdgePadding { get; init; }
        public double DragThreshold { get; init; }
        public double SnapTolerancePercent { get; init; }
        public double WorkAreaMinHeight { get; init; }
        public double ContentHeightReserve { get; init; }
        public double PixelMinSize { get; init; }
        public int PixelMinSizeInt => (int)Math.Round(PixelMinSize);
        public int OffscreenPosition { get; init; }
        public int FallbackWorkAreaX { get; init; }
        public int FallbackWorkAreaY { get; init; }
        public int FallbackWorkAreaWidth { get; init; }
        public int FallbackWorkAreaHeight { get; init; }
        public double IconGlyphLineHeightPadding { get; init; }
        public Thickness ChromeBorderThickness { get; init; }
        public Thickness ChromeInnerMargin { get; init; }
        public CornerRadius ChromeCornerRadius { get; init; }
        public CornerRadius ChromeInnerCornerRadius { get; init; }
        public CornerRadius ZeroCornerRadius { get; init; }
        public Thickness ZeroThickness { get; init; }
        public double HeaderMinHeight { get; init; }
        public Thickness HeaderLeftMarginTop { get; init; }
        public Thickness HeaderLeftMarginBottom { get; init; }
        public double HeaderIconButtonWidth { get; init; }
        public double HeaderIconButtonHeight { get; init; }
        public Thickness HeaderIconButtonMargin { get; init; }
        public CornerRadius HeaderIconButtonCornerRadius { get; init; }
        public double HeaderIconFontSize { get; init; }
        public double HeaderIconLineHeight { get; init; }
        public double HeaderUndockFontSize { get; init; }
        public double HeaderUndockLineHeight { get; init; }
        public Thickness HeaderUpdateMarginTop { get; init; }
        public Thickness HeaderUpdateMarginBottom { get; init; }
        public double HeaderUpdateWidth { get; init; }
        public double HeaderUpdateHeight { get; init; }
        public Thickness HeaderUpdateBorderThickness { get; init; }
        public CornerRadius HeaderUpdateCornerRadius { get; init; }
        public double HeaderUpdateLabelFontSize { get; init; }
        public int HeaderUpdateZIndex { get; init; }
        public Thickness HeaderUndockMarginTop { get; init; }
        public Thickness HeaderUndockMarginBottom { get; init; }
        public Thickness EmptyDevicesMargin { get; init; }
        public double EmptyDevicesFontSize { get; init; }
        public double EmptyDevicesOpacity { get; init; }
        public Thickness DeviceCellOuterMargin { get; init; }
        public Thickness DeviceCellContentPadding { get; init; }
        public Thickness DeviceAppBandGridPadding { get; init; }
        public Thickness DeviceAppBandSliderBottomPadding { get; init; }
        public Thickness DeviceAppBandSliderTopPadding { get; init; }
        public Thickness DeviceAppBandGridBottomMargin { get; init; }
        public Thickness DeviceAppBandGridTopMargin { get; init; }
        public Thickness DeviceBandBottomPadding { get; init; }
        public Thickness DeviceBandTopPadding { get; init; }
        public Thickness DeviceOutlineBorderThickness { get; init; }
        public CornerRadius DeviceCornerRadius { get; init; }
        public CornerRadius FooterBottomCornerRadius { get; init; }
        public double AppIconImageSize { get; init; }
        public double AppIconGlyphSize { get; init; }
        public double AppIconGridSlotSize { get; init; }
        public double AppIconCellPillExtra { get; init; }
        public CornerRadius AppIconHoverCornerRadius { get; init; }
        public Thickness AppIconCellBadgeMargin { get; init; }
        public double AppIconCellBadgeFontSize { get; init; }
        public double AppIconMuteOverlayOpacity { get; init; }
        public double AppSliderRowHeight { get; init; }
        public Thickness AppSliderRowMargin { get; init; }
        public Thickness AppSliderIconMargin { get; init; }
        public Thickness DeviceTitleRowMarginAbove { get; init; }
        public Thickness DeviceTitleRowMarginInline { get; init; }
        public TranslateTransform DeviceTitleNameNoFormatTransform { get; init; } = null!;
        public double DeviceTitleFontSize { get; init; }
        public double DeviceFormatFontSize { get; init; }
        public double DeviceFormatOpacity { get; init; }
        public double DeviceFormatCanvasTop { get; init; }
        public double SliderHitTestVerticalPadding { get; init; }
        public double PercentHostMinWidth { get; init; }
        public Thickness PercentHostMargin { get; init; }
        public double PercentFontSize { get; init; }
        public double PercentEditorMinWidth { get; init; }
        public Thickness PercentEditorBorderThickness { get; init; }
        public Thickness PercentEditorPadding { get; init; }
        public double DeviceMuteSlotWidth { get; init; }
        public double DeviceMuteSlotHeight { get; init; }
        public double DeviceMuteButtonWidth { get; init; }
        public double DeviceMuteButtonHeight { get; init; }
        public Thickness DeviceMuteButtonMargin { get; init; }
        public double DeviceMuteGlyphFontSize { get; init; }
        public double DeviceMuteMicrophoneGlyphFontSize { get; init; }
        public TranslateTransform DeviceMuteMicrophoneTransform { get; init; } = null!;
        public double DeviceIconButtonWidth { get; init; }
        public double DeviceIconButtonHeight { get; init; }
        public double DeviceIconButtonFontSize { get; init; }
        public Thickness DeviceIconButtonMargin { get; init; }
        public CornerRadius IconButtonCornerRadius { get; init; }
        public double EqualizerFontSize { get; init; }
        public double EqualizerBadgeFontSize { get; init; }
        public Thickness EqualizerBadgeMargin { get; init; }
        public double DeviceStateFontSize { get; init; }
        public double DeviceStateDisabledFontSize { get; init; }
        public TranslateTransform DeviceStateDisabledTransform { get; init; } = null!;
        public double TextButtonFontSize { get; init; }
        public Thickness TextButtonBorderThickness { get; init; }
        public CornerRadius TextButtonCornerRadius { get; init; }
        public double FormatMenuItemHeight { get; init; }
        public int FormatMenuMaxVisibleItems { get; init; }
        public double FormatMenuPaddingReserve { get; init; }
        public Thickness MenuScrollHostPadding { get; init; }
        public Thickness MenuBorderThickness { get; init; }
        public Thickness MenuPadding { get; init; }
        public CornerRadius MenuCornerRadius { get; init; }
        public double MenuShadowOffsetY { get; init; }
        public double MenuShadowBlur { get; init; }
        public CornerRadius MenuRowCornerRadius { get; init; }
        public Thickness MenuRowMargin { get; init; }
        public Thickness MenuRowPadding { get; init; }
        public double MenuMarkerColumnWidth { get; init; }
        public double MenuMarkerFontSize { get; init; }
        public double DeviceNameEditorFontSize { get; init; }
        public Thickness DeviceNameEditorBorderThickness { get; init; }
        public Thickness DeviceNameEditorPadding { get; init; }
        public double DeviceNameEditorMinHeight { get; init; }
        public int DeviceNameEditorZIndex { get; init; }

        public static FlyoutLayout From(Control owner) => new()
        {
            EdgePadding = Int(owner, "Flyout.EdgePadding"),
            DragThreshold = Double(owner, "Flyout.DragThreshold"),
            SnapTolerancePercent = Double(owner, "Flyout.SnapTolerancePercent"),
            WorkAreaMinHeight = Double(owner, "Flyout.WorkAreaMinHeight"),
            ContentHeightReserve = Double(owner, "Flyout.ContentHeightReserve"),
            PixelMinSize = Double(owner, "Flyout.PixelMinSize"),
            OffscreenPosition = Int(owner, "Flyout.OffscreenPosition"),
            FallbackWorkAreaX = Int(owner, "Flyout.FallbackWorkAreaX"),
            FallbackWorkAreaY = Int(owner, "Flyout.FallbackWorkAreaY"),
            FallbackWorkAreaWidth = Int(owner, "Flyout.FallbackWorkAreaWidth"),
            FallbackWorkAreaHeight = Int(owner, "Flyout.FallbackWorkAreaHeight"),
            IconGlyphLineHeightPadding = Double(owner, "Flyout.IconGlyphLineHeightPadding"),
            ChromeBorderThickness = Thickness(owner, "Flyout.ChromeBorderThickness"),
            ChromeInnerMargin = Thickness(owner, "Flyout.ChromeInnerMargin"),
            ChromeCornerRadius = CornerRadius(owner, "Flyout.ChromeCornerRadius"),
            ChromeInnerCornerRadius = CornerRadius(owner, "Flyout.ChromeInnerCornerRadius"),
            ZeroCornerRadius = CornerRadius(owner, "Flyout.ZeroCornerRadius"),
            ZeroThickness = Thickness(owner, "Flyout.ZeroThickness"),
            HeaderMinHeight = Double(owner, "Flyout.HeaderMinHeight"),
            HeaderLeftMarginTop = Thickness(owner, "Flyout.HeaderLeftMarginTop"),
            HeaderLeftMarginBottom = Thickness(owner, "Flyout.HeaderLeftMarginBottom"),
            HeaderIconButtonWidth = Double(owner, "Flyout.HeaderIconButtonWidth"),
            HeaderIconButtonHeight = Double(owner, "Flyout.HeaderIconButtonHeight"),
            HeaderIconButtonMargin = Thickness(owner, "Flyout.HeaderIconButtonMargin"),
            HeaderIconButtonCornerRadius = CornerRadius(owner, "Flyout.HeaderIconButtonCornerRadius"),
            HeaderIconFontSize = Double(owner, "Flyout.HeaderIconFontSize"),
            HeaderIconLineHeight = Double(owner, "Flyout.HeaderIconLineHeight"),
            HeaderUndockFontSize = Double(owner, "Flyout.HeaderUndockFontSize"),
            HeaderUndockLineHeight = Double(owner, "Flyout.HeaderUndockLineHeight"),
            HeaderUpdateMarginTop = Thickness(owner, "Flyout.HeaderUpdateMarginTop"),
            HeaderUpdateMarginBottom = Thickness(owner, "Flyout.HeaderUpdateMarginBottom"),
            HeaderUpdateWidth = Double(owner, "Flyout.HeaderUpdateWidth"),
            HeaderUpdateHeight = Double(owner, "Flyout.HeaderUpdateHeight"),
            HeaderUpdateBorderThickness = Thickness(owner, "Flyout.HeaderUpdateBorderThickness"),
            HeaderUpdateCornerRadius = CornerRadius(owner, "Flyout.HeaderUpdateCornerRadius"),
            HeaderUpdateLabelFontSize = Double(owner, "Flyout.HeaderUpdateLabelFontSize"),
            HeaderUpdateZIndex = Int(owner, "Flyout.HeaderUpdateZIndex"),
            HeaderUndockMarginTop = Thickness(owner, "Flyout.HeaderUndockMarginTop"),
            HeaderUndockMarginBottom = Thickness(owner, "Flyout.HeaderUndockMarginBottom"),
            EmptyDevicesMargin = Thickness(owner, "Flyout.EmptyDevicesMargin"),
            EmptyDevicesFontSize = Double(owner, "Flyout.EmptyDevicesFontSize"),
            EmptyDevicesOpacity = Double(owner, "Flyout.EmptyDevicesOpacity"),
            DeviceCellOuterMargin = Thickness(owner, "Flyout.DeviceCellOuterMargin"),
            DeviceCellContentPadding = Thickness(owner, "Flyout.DeviceCellContentPadding"),
            DeviceAppBandGridPadding = Thickness(owner, "Flyout.DeviceAppBandGridPadding"),
            DeviceAppBandSliderBottomPadding = Thickness(owner, "Flyout.DeviceAppBandSliderBottomPadding"),
            DeviceAppBandSliderTopPadding = Thickness(owner, "Flyout.DeviceAppBandSliderTopPadding"),
            DeviceAppBandGridBottomMargin = Thickness(owner, "Flyout.DeviceAppBandGridBottomMargin"),
            DeviceAppBandGridTopMargin = Thickness(owner, "Flyout.DeviceAppBandGridTopMargin"),
            DeviceBandBottomPadding = Thickness(owner, "Flyout.DeviceBandBottomPadding"),
            DeviceBandTopPadding = Thickness(owner, "Flyout.DeviceBandTopPadding"),
            DeviceOutlineBorderThickness = Thickness(owner, "Flyout.DeviceOutlineBorderThickness"),
            DeviceCornerRadius = CornerRadius(owner, "Flyout.DeviceCornerRadius"),
            FooterBottomCornerRadius = CornerRadius(owner, "Flyout.FooterBottomCornerRadius"),
            AppIconImageSize = Double(owner, "Flyout.AppIconImageSize"),
            AppIconGlyphSize = Double(owner, "Flyout.AppIconGlyphSize"),
            AppIconGridSlotSize = Double(owner, "Flyout.AppIconGridSlotSize"),
            AppIconCellPillExtra = Double(owner, "Flyout.AppIconCellPillExtra"),
            AppIconHoverCornerRadius = CornerRadius(owner, "Flyout.AppIconHoverCornerRadius"),
            AppIconCellBadgeMargin = Thickness(owner, "Flyout.AppIconCellBadgeMargin"),
            AppIconCellBadgeFontSize = Double(owner, "Flyout.AppIconCellBadgeFontSize"),
            AppIconMuteOverlayOpacity = Double(owner, "Flyout.AppIconMuteOverlayOpacity"),
            AppSliderRowHeight = Double(owner, "Flyout.AppSliderRowHeight"),
            AppSliderRowMargin = Thickness(owner, "Flyout.AppSliderRowMargin"),
            AppSliderIconMargin = Thickness(owner, "Flyout.AppSliderIconMargin"),
            DeviceTitleRowMarginAbove = Thickness(owner, "Flyout.DeviceTitleRowMarginAbove"),
            DeviceTitleRowMarginInline = Thickness(owner, "Flyout.DeviceTitleRowMarginInline"),
            DeviceTitleNameNoFormatTransform = Transform(owner, "Flyout.DeviceTitleNameNoFormatTransform"),
            DeviceTitleFontSize = Double(owner, "Flyout.DeviceTitleFontSize"),
            DeviceFormatFontSize = Double(owner, "Flyout.DeviceFormatFontSize"),
            DeviceFormatOpacity = Double(owner, "Flyout.DeviceFormatOpacity"),
            DeviceFormatCanvasTop = Double(owner, "Flyout.DeviceFormatCanvasTop"),
            SliderHitTestVerticalPadding = Double(owner, "Flyout.SliderHitTestVerticalPadding"),
            PercentHostMinWidth = Double(owner, "Flyout.PercentHostMinWidth"),
            PercentHostMargin = Thickness(owner, "Flyout.PercentHostMargin"),
            PercentFontSize = Double(owner, "Flyout.PercentFontSize"),
            PercentEditorMinWidth = Double(owner, "Flyout.PercentEditorMinWidth"),
            PercentEditorBorderThickness = Thickness(owner, "Flyout.PercentEditorBorderThickness"),
            PercentEditorPadding = Thickness(owner, "Flyout.PercentEditorPadding"),
            DeviceMuteSlotWidth = Double(owner, "Flyout.DeviceMuteSlotWidth"),
            DeviceMuteSlotHeight = Double(owner, "Flyout.DeviceMuteSlotHeight"),
            DeviceMuteButtonWidth = Double(owner, "Flyout.DeviceMuteButtonWidth"),
            DeviceMuteButtonHeight = Double(owner, "Flyout.DeviceMuteButtonHeight"),
            DeviceMuteButtonMargin = Thickness(owner, "Flyout.DeviceMuteButtonMargin"),
            DeviceMuteGlyphFontSize = Double(owner, "Flyout.DeviceMuteGlyphFontSize"),
            DeviceMuteMicrophoneGlyphFontSize = Double(owner, "Flyout.DeviceMuteMicrophoneGlyphFontSize"),
            DeviceMuteMicrophoneTransform = Transform(owner, "Flyout.DeviceMuteMicrophoneTransform"),
            DeviceIconButtonWidth = Double(owner, "Flyout.DeviceIconButtonWidth"),
            DeviceIconButtonHeight = Double(owner, "Flyout.DeviceIconButtonHeight"),
            DeviceIconButtonFontSize = Double(owner, "Flyout.DeviceIconButtonFontSize"),
            DeviceIconButtonMargin = Thickness(owner, "Flyout.DeviceIconButtonMargin"),
            IconButtonCornerRadius = CornerRadius(owner, "Flyout.IconButtonCornerRadius"),
            EqualizerFontSize = Double(owner, "Flyout.EqualizerFontSize"),
            EqualizerBadgeFontSize = Double(owner, "Flyout.EqualizerBadgeFontSize"),
            EqualizerBadgeMargin = Thickness(owner, "Flyout.EqualizerBadgeMargin"),
            DeviceStateFontSize = Double(owner, "Flyout.DeviceStateFontSize"),
            DeviceStateDisabledFontSize = Double(owner, "Flyout.DeviceStateDisabledFontSize"),
            DeviceStateDisabledTransform = Transform(owner, "Flyout.DeviceStateDisabledTransform"),
            TextButtonFontSize = Double(owner, "Flyout.TextButtonFontSize"),
            TextButtonBorderThickness = Thickness(owner, "Flyout.TextButtonBorderThickness"),
            TextButtonCornerRadius = CornerRadius(owner, "Flyout.TextButtonCornerRadius"),
            FormatMenuItemHeight = Double(owner, "Flyout.FormatMenuItemHeight"),
            FormatMenuMaxVisibleItems = Int(owner, "Flyout.FormatMenuMaxVisibleItems"),
            FormatMenuPaddingReserve = Double(owner, "Flyout.FormatMenuPaddingReserve"),
            MenuScrollHostPadding = Thickness(owner, "Flyout.MenuScrollHostPadding"),
            MenuBorderThickness = Thickness(owner, "Flyout.MenuBorderThickness"),
            MenuPadding = Thickness(owner, "Flyout.MenuPadding"),
            MenuCornerRadius = CornerRadius(owner, "Flyout.MenuCornerRadius"),
            MenuShadowOffsetY = Double(owner, "Flyout.MenuShadowOffsetY"),
            MenuShadowBlur = Double(owner, "Flyout.MenuShadowBlur"),
            MenuRowCornerRadius = CornerRadius(owner, "Flyout.MenuRowCornerRadius"),
            MenuRowMargin = Thickness(owner, "Flyout.MenuRowMargin"),
            MenuRowPadding = Thickness(owner, "Flyout.MenuRowPadding"),
            MenuMarkerColumnWidth = Double(owner, "Flyout.MenuMarkerColumnWidth"),
            MenuMarkerFontSize = Double(owner, "Flyout.MenuMarkerFontSize"),
            DeviceNameEditorFontSize = Double(owner, "Flyout.DeviceNameEditorFontSize"),
            DeviceNameEditorBorderThickness = Thickness(owner, "Flyout.DeviceNameEditorBorderThickness"),
            DeviceNameEditorPadding = Thickness(owner, "Flyout.DeviceNameEditorPadding"),
            DeviceNameEditorMinHeight = Double(owner, "Flyout.DeviceNameEditorMinHeight"),
            DeviceNameEditorZIndex = Int(owner, "Flyout.DeviceNameEditorZIndex"),
        };

        private static object Resource(Control owner, string key)
        {
            object? value = owner.Resources[key];
            return value ?? throw new InvalidOperationException($"Missing flyout layout resource '{key}'.");
        }

        private static double Double(Control owner, string key) =>
            Resource(owner, key) switch
            {
                double value => value,
                int value => value,
                string value => double.Parse(value, CultureInfo.InvariantCulture),
                object value => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            };

        private static int Int(Control owner, string key) => (int)Math.Round(Double(owner, key));

        private static Thickness Thickness(Control owner, string key) =>
            Resource(owner, key) is Thickness value
                ? value
                : throw new InvalidOperationException($"Flyout layout resource '{key}' is not a Thickness.");

        private static CornerRadius CornerRadius(Control owner, string key) =>
            Resource(owner, key) is CornerRadius value
                ? value
                : throw new InvalidOperationException($"Flyout layout resource '{key}' is not a CornerRadius.");

        private static TranslateTransform Transform(Control owner, string key) =>
            Resource(owner, key) is TranslateTransform value
                ? value
                : throw new InvalidOperationException($"Flyout layout resource '{key}' is not a TranslateTransform.");
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
        private readonly FlyoutLayout _layout;
        private bool _closedFromDeactivation;

        public bool ClosedFromDeactivation => _closedFromDeactivation;

        public FlyoutMenuWindow(
            IReadOnlyList<FlyoutMenuEntry> entries,
            FlyoutPalette palette,
            FlyoutLayout layout,
            int fontSize,
            bool rounded,
            double maxHeight)
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
                    OffsetY = layout.MenuShadowOffsetY, Blur = layout.MenuShadowBlur, Color = palette.MenuShadow,
                }),
                Child = scroll,
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
                int width = Math.Max(_layout.PixelMinSizeInt, (int)Math.Ceiling(Bounds.Width * scale));
                int height = Math.Max(_layout.PixelMinSizeInt,
                    (int)Math.Ceiling(Math.Min(Bounds.Height, _maxHeight) * scale));

                PixelRect workArea = (Screens.ScreenFromPoint(anchorBottom) ?? Screens.Primary)?.WorkingArea
                                     ?? new PixelRect(
                                         _layout.FallbackWorkAreaX,
                                         _layout.FallbackWorkAreaY,
                                         _layout.FallbackWorkAreaWidth,
                                         _layout.FallbackWorkAreaHeight);
                int left = Math.Clamp(anchorBottom.X, workArea.X + _layout.EdgePadding,
                    Math.Max(workArea.X + _layout.EdgePadding, workArea.Right - width - _layout.EdgePadding));
                int top = anchorBottom.Y;
                if (top + height > workArea.Bottom - _layout.EdgePadding)
                    top = anchorTop.Y - height;
                top = Math.Clamp(top, workArea.Y + _layout.EdgePadding,
                    Math.Max(workArea.Y + _layout.EdgePadding, workArea.Bottom - height - _layout.EdgePadding));

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
            palette.SliderProgress,
            palette.SliderTrack,
            palette.SliderThumb,
            palette.ButtonHover,
            palette.ButtonPressed,
            palette.Foreground);
    }

    private sealed class FlyoutMenuRow : Border
    {
        private readonly FlyoutPalette _palette;
        private readonly Action _close;
        private bool _isPointerOver;

        public FlyoutMenuRow(FlyoutMenuEntry entry, FlyoutPalette palette, FlyoutLayout layout, int fontSize,
            bool rounded, Action close)
        {
            _palette = palette;
            _close = close;
            CornerRadius = rounded ? layout.MenuRowCornerRadius : layout.ZeroCornerRadius;
            Background = Brushes.Transparent;
            Margin = layout.MenuRowMargin;
            Padding = layout.MenuRowPadding;
            Cursor = new Cursor(StandardCursorType.Hand);

            Grid row = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(layout.MenuMarkerColumnWidth)),
                    new ColumnDefinition(GridLength.Star),
                },
            };

            TextBlock marker = Text(entry.IsCurrent ? GlyphCatalog.CIRCLE : string.Empty, palette,
                layout.MenuMarkerFontSize);
            marker.FontFamily = TrayAppDotNETSettingsUI.IconFont;
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
