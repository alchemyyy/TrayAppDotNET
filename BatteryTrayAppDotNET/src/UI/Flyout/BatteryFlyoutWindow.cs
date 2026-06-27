using System.Globalization;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BatteryTrayAppDotNET.Models;
using BatteryTrayAppDotNET.Services;
using Microsoft.Win32;

namespace BatteryTrayAppDotNET.UI.Flyout;

public sealed class BatteryFlyoutWindow : FlyoutWindowCommon
{
    private const int EdgePadding = 8;
    private const int OffscreenPosition = -32000;
    private const double DragThreshold = 4;
    private const double SnapTolerancePercent = 0.02;
    private const int PixelMinSize = 1;
    private const double TitleBarHeight = 40;
    private const double TitleBarActionButtonWidth = 40;
    private const double TitleBarActionButtonHeight = 32;
    private const double TitleBarActionIconFontSize = 18;
    private const double TitleBarPowerIconFontSize = TitleBarActionIconFontSize - 4;
    private const double TitleBarUndockButtonSize = 32;
    private const double TitleBarUndockIconFontSize = 20;
    private const double TitleBarUndockLineHeight = 26;
    private const int PowerCfgTimeoutMs = 5_000;
    private const double FlyoutWidth = 350;
    private const double BatteryTitleFontSize = 19;
    private const double BatteryTitleTopOffset = -4;
    private const double BatteryContentWidth = FlyoutWidth - 26;
    private const double BatteryBarHorizontalInset = 19;
    private const double BatteryBarWidth = FlyoutWidth - (2 * BatteryBarHorizontalInset);
    private const double BatteryBarHeight = 14;
    private const string UltimatePowerSchemeGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string UltimatePowerSchemeName = "Ultimate Performance";
    private const string BalancedPowerSchemeGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private const string PowerSaverPowerSchemeGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";
    private const string EnergySaverSubgroupGuid = "de830923-a562-41af-a086-e3a2c6bad2da";
    private const string EnergySaverBatteryThresholdGuid = "e69653ca-cf7f-4f05-aa73-cb833fa90ad4";
    private const int EnergySaverNeverThreshold = 0;
    private const int EnergySaverAlwaysThreshold = 100;

    private static readonly Thickness TitleBarPadding = new(12, 4, 12, 4);
    private static readonly Thickness FloatingUndockMargin = new(0, 8, 8, 0);
    private static readonly CornerRadius TitleBarTopCornerRadius = new(7, 7, 0, 0);
    private static readonly CornerRadius TitleBarBottomCornerRadius = new(0, 0, 7, 7);
    private static readonly CornerRadius TitleBarButtonCornerRadius = new(4);
    private static readonly Color BatteryBarFill = Color.FromRgb(245, 242, 232);

    private enum FlyoutPowerMode
    {
        Ultimate,
        Balanced,
        PowerSaver,
    }

    private readonly BatteryMonitorService _batteryMonitor;
    private readonly AppSettings _settings;
    private readonly Action _openSettings;
    private readonly FlyoutWindowDragHelper _dragHelper = new();
    private TrayAppDotNETShellTrayIcon? _lastTrayIcon;
    private FlyoutUndockButtonController? _undockButtonController;
    private bool _isPowerModeChanging;
    private bool _isEnergySaverChanging;
    private bool _isUndocked;
    private bool _isDraggingWindow;

    public BatteryFlyoutWindow(BatteryMonitorService batteryMonitor, AppSettings settings, Action openSettings)
    {
        _batteryMonitor = batteryMonitor;
        _settings = settings;
        _openSettings = openSettings;

        Width = FlyoutWidth;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;

        _isUndocked = _settings is
        {
            AllowFlyoutUndock: true,
            RestoreFlyoutUndockedOnStartup: true,
            FlyoutUndocked: true,
            FlyoutHasSavedPosition: true,
        };

        _batteryMonitor.StateChanged += OnBatteryStateChanged;
        _settings.Changed += OnSettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Closed += OnClosed;
        Rebuild();
    }

    public void ShowAt(TrayAppDotNETShellTrayIcon trayIcon, bool activate = true)
    {
        _lastTrayIcon = trayIcon;
        ShowActivated = activate;
        if (_isUndocked && !_settings.AllowFlyoutUndock)
            Redock();
        ApplyWorkAreaMaxHeight();
        Rebuild();

        if (!IsVisible)
        {
            Opacity = 1;
            Position = new PixelPoint(OffscreenPosition, OffscreenPosition);
            Show();
        }

        Dispatcher.UIThread.Post(() =>
        {
            UpdateLayout();
            ApplyWorkAreaMaxHeight();
            PositionNearTray();
            if (activate) Activate();
        }, DispatcherPriority.Loaded);
    }

    public new void Hide()
    {
        base.Hide();
        NotifyWarmDismissed();
    }

    public void Redock()
    {
        if (!_isUndocked) return;
        _isUndocked = false;
        _settings.FlyoutUndocked = false;
        _settings.Save();
        UpdateUndockButtonVisual();
        Rebuild();
        QueuePositionNearTray();
    }

    protected override bool ShouldAutoHideWhenDeactivated => !_isUndocked;

    protected override void HideFlyout() => Hide();

    private void OnBatteryStateChanged() => Dispatcher.UIThread.Post(Rebuild);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is not (PowerModes.StatusChange or PowerModes.Resume)) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible) return;
            _batteryMonitor.ForceRefresh();
            Rebuild();
        }, DispatcherPriority.Background);
    }

    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_isUndocked && !_settings.AllowFlyoutUndock)
        {
            Redock();
            return;
        }

        Rebuild();
    });

    private void Rebuild()
    {
        bool isLight = AppTheme.ResolveEffectiveIsLightTheme(_settings);
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;
        SettingsPalette p = BatterySettingsPalette.Create(theme, _settings, isLight);
        FlyoutControlPalette fp = ToFlyoutPalette(p, theme, isLight);
        Color flyoutBackground = theme.ResolveFlyoutBackground(_settings, isLight);
        Color titleBarBackground = theme.ResolveFlyoutTitleBarBackground(_settings, isLight);
        BatterySnapshot snapshot = _batteryMonitor.Snapshot;
        _undockButtonController = null;

        StackPanel body = new()
        {
            Width = BatteryContentWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 16),
            Spacing = 10,
        };

        TextBlock title = Text($"Battery: {FormatChargePercent(snapshot)}", p, BatteryTitleFontSize, FontWeight.SemiBold);
        title.Margin = new Thickness(0, BatteryTitleTopOffset, 0, 0);
        body.Children.Add(title);

        body.Children.Add(BatteryBar(snapshot, p));

        TextBlock status = Text(BuildStatus(snapshot), p, 14);
        status.HorizontalAlignment = HorizontalAlignment.Center;
        status.Foreground = Brush(p.SecondaryForeground);
        body.Children.Add(status);

        body.Children.Add(Separator(p));

        if (snapshot.EstimatedTimeRemaining.HasValue && !snapshot.IsFullyCharged)
        {
            body.Children.Add(DetailBlock(
                snapshot.IsCharging ? "Time until full" : "Estimated life",
                FormatTimeSpan(snapshot.EstimatedTimeRemaining.Value),
                p));
            body.Children.Add(Separator(p));
        }

        Grid details = new()
        {
            RowSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };

        AddDetailRow(details, 0, "Power source", snapshot.IsOnExternalPower ? "External" : "Battery", p);
        AddDetailRow(details, 1, "Battery power", FormatPower(snapshot.CurrentBatteryPowerWatts), p);
        AddDetailRow(details, 2, "Remaining", FormatCapacity(snapshot.RemainingCapacityMilliwattHours), p);
        AddDetailRow(details, 3, "Full charge", FormatCapacity(snapshot.FullChargeCapacityMilliwattHours), p);
        AddDetailRow(details, 4, "Designed", FormatCapacity(snapshot.DesignedCapacityMilliwattHours), p);
        AddDetailRow(details, 5, "Health", snapshot.HealthPercent.HasValue ? $"{snapshot.HealthPercent.Value:F0}%" : "N/A", p);
        body.Children.Add(BuildBottomSection(details, snapshot, fp, p));

        DockPanel root = new() { LastChildFill = true };
        Control header = BuildHeader(fp, titleBarBackground);
        DockPanel.SetDock(header, _settings.FlyoutHeaderAtBottom ? Dock.Bottom : Dock.Top);
        root.Children.Add(header);
        root.Children.Add(body);

        Grid content = new();
        content.Children.Add(root);
        if (_settings.FlyoutHeaderAtBottom && _settings.AllowFlyoutUndock)
        {
            Border floatingUndock = BuildUndockButton(fp, FloatingUndockMargin);
            floatingUndock.HorizontalAlignment = HorizontalAlignment.Right;
            floatingUndock.VerticalAlignment = VerticalAlignment.Top;
            content.Children.Add(floatingUndock);
        }

        Border chrome = new()
        {
            Background = Brush(flyoutBackground),
            BorderBrush = Brush(p.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(_settings.EnableRoundedCorners ? 8 : 0),
            Child = content,
        };
        chrome.PointerPressed += OnChromePointerPressed;
        chrome.PointerMoved += OnChromePointerMoved;
        chrome.PointerReleased += OnChromePointerReleased;
        chrome.PointerCaptureLost += OnChromePointerCaptureLost;

        Content = chrome;
        QueuePositionNearTray();
    }

    private Border BuildHeader(FlyoutControlPalette p, Color titleBarBackground)
    {
        bool bottomHeader = _settings.FlyoutHeaderAtBottom;
        Grid grid = new();

        if (bottomHeader)
        {
            StackPanel actions = BuildTitleBarActions(p, settingsLast: true);
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(actions);
        }
        else
        {
            StackPanel left = BuildTitleBarActions(p, settingsLast: false);
            left.HorizontalAlignment = HorizontalAlignment.Left;
            left.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(left);

            if (_settings.AllowFlyoutUndock)
            {
                Border undock = BuildUndockButton(p);
                undock.HorizontalAlignment = HorizontalAlignment.Right;
                undock.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(undock);
            }
        }

        return new Border
        {
            Height = TitleBarHeight,
            Background = Brush(titleBarBackground),
            CornerRadius = Rounded(bottomHeader ? TitleBarBottomCornerRadius : TitleBarTopCornerRadius),
            Padding = TitleBarPadding,
            Child = grid,
        };
    }

    private StackPanel BuildTitleBarActions(FlyoutControlPalette p, bool settingsLast)
    {
        Border settingsButton = BuildTitleBarIconButton(
            GlyphCatalog.SETTINGS,
            p,
            TitleBarActionIconFontSize,
            _openSettings,
            L("Flyout_Settings_Tooltip", "Settings"));
        SuppressNextAutoHideWhenPressed(settingsButton);

        Border powerButton = BuildTitleBarIconButton(
            GlyphCatalog.POWER,
            p,
            TitleBarPowerIconFontSize,
            OpenModernPowerSettings,
            L("Flyout_PowerSettings_Tooltip", "Power settings"));

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (settingsLast)
        {
            actions.Children.Add(powerButton);
            actions.Children.Add(settingsButton);
        }
        else
        {
            actions.Children.Add(settingsButton);
            actions.Children.Add(powerButton);
        }

        return actions;
    }

    private Border BuildTitleBarIconButton(
        string glyph,
        FlyoutControlPalette p,
        double fontSize,
        Action click,
        string tooltip)
    {
        TextBlock text = new()
        {
            Text = glyph,
            FontFamily = TrayAppDotNETSettingsUI.IconFont,
            FontSize = fontSize,
            FontWeight = FontWeight.Normal,
            Foreground = Brush(p.IconForeground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            LineHeight = Math.Ceiling(fontSize + 6),
            IsHitTestVisible = false,
        };

        Border button = new()
        {
            Width = TitleBarActionButtonWidth,
            Height = TitleBarActionButtonHeight,
            CornerRadius = Rounded(TitleBarButtonCornerRadius),
            Background = Brushes.Transparent,
            ClipToBounds = false,
            Child = text,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        TrayAppDotNETToolTip.SetTip(button, tooltip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
        FlyoutButtonState.Attach(
            button,
            () => Brushes.Transparent,
            () => Brush(p.Hover),
            () => Brush(p.Pressed),
            _ => click());
        return button;
    }

    private Border BuildUndockButton(FlyoutControlPalette p, Thickness? margin = null)
    {
        _undockButtonController = new FlyoutUndockButtonController(new FlyoutUndockButtonOptions
        {
            Owner = this,
            DragHelper = _dragHelper,
            Palette = p,
            CaptureDockedPosition = CaptureDockedPosition,
            IsUndocked = () => _isUndocked,
            SetUndockedFromDrag = SetUndockedFromDrag,
            ToggleUndocked = ToggleUndocked,
            CommitDragPosition = CommitDragPosition,
            DraggingChanged = dragging => _isDraggingWindow = dragging,
            UndockTooltip = () => L("Flyout_Undock_Tooltip", "Undock"),
            RedockTooltip = () => L("Flyout_Redock_Tooltip", "Redock"),
            Width = TitleBarUndockButtonSize,
            Height = TitleBarUndockButtonSize,
            FontSize = TitleBarUndockIconFontSize,
            FontWeight = FontWeight.Normal,
            DragThreshold = DragThreshold,
            IsVisible = _settings.AllowFlyoutUndock,
            Margin = margin ?? new Thickness(0),
            CornerRadius = Rounded(TitleBarButtonCornerRadius),
        });
        _undockButtonController.Glyph.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        _undockButtonController.Glyph.FontWeight = FontWeight.Normal;
        _undockButtonController.Glyph.Foreground = Brush(p.IconForeground);
        _undockButtonController.Glyph.LineHeight = TitleBarUndockLineHeight;
        UseDefaultGlyphRendering(_undockButtonController.Glyph);
        return _undockButtonController.Button;
    }

    private static void UseDefaultGlyphRendering(TextBlock glyph)
    {
        TextOptions.SetTextRenderingMode(glyph, TextRenderingMode.Unspecified);
        TextOptions.SetTextHintingMode(glyph, TextHintingMode.Unspecified);
        TextOptions.SetBaselinePixelAlignment(glyph, BaselinePixelAlignment.Unspecified);
    }

    private CornerRadius Rounded(CornerRadius radius) =>
        _settings.EnableRoundedCorners ? radius : new CornerRadius(0);

    private void PositionNearTray() => Position = ResolvePosition(_lastTrayIcon);

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
               ?? new PixelRect(0, 0, 1920, 1080);
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

    private void ApplyWorkAreaMaxHeight()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        MaxHeight = workArea.Height / RenderScaling - (2 * EdgePadding);
    }

    private (PixelPoint DockedPosition, int SnapTolerance) CaptureDockedPosition() =>
        (ResolveDockedPosition(_lastTrayIcon), ResolveSnapTolerance());

    private int ResolveSnapTolerance()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        return Math.Max(
            PixelMinSize,
            (int)Math.Round(Math.Min(workArea.Width, workArea.Height) * SnapTolerancePercent));
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

    private void SetUndockedFromDrag()
    {
        _isUndocked = true;
        UpdateUndockButtonVisual();
    }

    private void CommitDragPosition()
    {
        if (_dragHelper.IsCurrentlySnapped)
        {
            Redock();
            return;
        }

        SaveCurrentFlyoutPosition();
    }

    private void UndockToSavedPosition()
    {
        _isUndocked = true;
        _settings.FlyoutUndocked = true;
        _settings.Save();
        if (_settings.FlyoutHasSavedPosition)
            Position = ResolvePosition(_lastTrayIcon);
        UpdateUndockButtonVisual();
        Rebuild();
    }

    private void SaveCurrentFlyoutPosition()
    {
        if (!_isUndocked) return;
        _settings.FlyoutUndocked = true;
        _settings.FlyoutHasSavedPosition = true;
        _settings.FlyoutLeft = Position.X;
        _settings.FlyoutTop = Position.Y;
        _settings.Save();
        UpdateUndockButtonVisual();
    }

    private void UpdateUndockButtonVisual() => _undockButtonController?.UpdateVisual();

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

    private void OnChromePointerPressed(object? sender, PointerPressedEventArgs e)
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

    private void OnChromePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingWindow || !_isUndocked || _undockButtonController?.IsPointerCaptured == true) return;
        if (sender is not Control control) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            EndChromeDrag(e.Pointer, commit: true);
            e.Handled = true;
            return;
        }

        PixelPoint pointer = control.PointToScreen(e.GetPosition(control));
        PixelPoint natural = _dragHelper.ComputeNatural(pointer);
        _dragHelper.ApplyDragPosition(this, natural);
        e.Handled = true;
    }

    private void OnChromePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingWindow || _undockButtonController?.IsPointerCaptured == true) return;
        EndChromeDrag(e.Pointer, commit: true);
        e.Handled = true;
    }

    private void OnChromePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDraggingWindow && _undockButtonController?.IsPointerCaptured != true)
            CommitDragPosition();
        _isDraggingWindow = false;
    }

    private void EndChromeDrag(IPointer pointer, bool commit)
    {
        _isDraggingWindow = false;
        pointer.Capture(null);
        if (commit) CommitDragPosition();
    }

    private Grid BuildBottomSection(
        Grid details,
        BatterySnapshot snapshot,
        FlyoutControlPalette p,
        SettingsPalette settingsPalette)
    {
        details.VerticalAlignment = VerticalAlignment.Top;

        Grid section = new()
        {
            ColumnSpacing = 14,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };

        Control powerControls = BuildPowerControls(snapshot, p, settingsPalette);
        section.Children.Add(powerControls);

        Grid.SetColumn(details, 1);
        section.Children.Add(details);

        return section;
    }

    private StackPanel BuildPowerControls(
        BatterySnapshot snapshot,
        FlyoutControlPalette p,
        SettingsPalette settingsPalette)
    {
        FlyoutPowerMode? activeMode = QueryActivePowerMode();
        bool? energySaverAlways = QueryEnergySaverAlwaysEnabled();
        bool energySaverEnabled = snapshot.EnergySaverEnabled || energySaverAlways == true;

        StackPanel panel = new()
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Top,
        };

        panel.Children.Add(BuildToggleRow(
            L("Flyout_EnergySaver", "Energy Saver"),
            p,
            settingsPalette,
            energySaverEnabled,
            SetEnergySaver,
            energySaverAlways.HasValue
                ? L("Flyout_EnergySaver_Tooltip", "Set Energy Saver to Always or Never")
                : L("Flyout_EnergySaver_Unavailable_Tooltip", "Energy Saver threshold is unavailable"),
            enabled: !_isEnergySaverChanging && energySaverAlways.HasValue));

        TextBlock label = TrayAppDotNETFlyoutUI.Text(
            L("Flyout_PowerMode_Label", "Power Mode"),
            p,
            12,
            FontWeight.SemiBold,
            p.SecondaryForeground);
        label.Margin = new Thickness(0, 2, 0, 0);
        panel.Children.Add(label);

        panel.Children.Add(BuildPowerModeRow(
            L("Flyout_PowerMode_Ultimate", "Ultimate"),
            FlyoutPowerMode.Ultimate,
            activeMode,
            p,
            settingsPalette));
        panel.Children.Add(BuildPowerModeRow(
            L("Flyout_PowerMode_Balanced", "Balanced"),
            FlyoutPowerMode.Balanced,
            activeMode,
            p,
            settingsPalette));
        panel.Children.Add(BuildPowerModeRow(
            L("Flyout_PowerMode_PowerSaver", "Power Saver"),
            FlyoutPowerMode.PowerSaver,
            activeMode,
            p,
            settingsPalette));

        return panel;
    }

    private Grid BuildPowerModeRow(
        string text,
        FlyoutPowerMode mode,
        FlyoutPowerMode? activeMode,
        FlyoutControlPalette p,
        SettingsPalette settingsPalette)
    {
        bool selected = activeMode == mode;
        return BuildToggleRow(
            text,
            p,
            settingsPalette,
            selected,
            enabled =>
            {
                if (enabled) SetPowerMode(mode);
                else Dispatcher.UIThread.Post(Rebuild, DispatcherPriority.Background);
            },
            text,
            enabled: !_isPowerModeChanging,
            labelIndent: 2);
    }

    private static Grid BuildToggleRow(
        string text,
        FlyoutControlPalette p,
        SettingsPalette settingsPalette,
        bool isChecked,
        Action<bool> changed,
        string tooltip,
        bool enabled = true,
        double labelIndent = 0)
    {
        TextBlock label = TrayAppDotNETFlyoutUI.Text(text, p, 12, FontWeight.SemiBold);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(labelIndent, 0, 0, 0);
        label.TextTrimming = TextTrimming.CharacterEllipsis;

        SettingsToggle toggle = TrayAppDotNETSettingsUI.Toggle(
            settingsPalette,
            isChecked,
            (_, value) => changed(value));
        toggle.IsEnabled = enabled;
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        toggle.VerticalAlignment = VerticalAlignment.Center;
        TrayAppDotNETToolTip.SetTip(toggle, tooltip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(toggle);

        Grid row = new()
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        row.Children.Add(label);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    private static Border BatteryBar(BatterySnapshot snapshot, SettingsPalette p)
    {
        Grid bar = new()
        {
            Width = BatteryBarWidth,
            Height = BatteryBarHeight,
            ClipToBounds = true,
        };

        bar.Children.Add(new Border
        {
            Background = Brush(p.ControlBackground),
            BorderBrush = Brush(p.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        });

        double fillWidth = Math.Max(0, (BatteryBarWidth - 2) * snapshot.ChargePercentage / 100.0);
        bar.Children.Add(new Border
        {
            Width = fillWidth,
            Height = BatteryBarHeight - 2,
            Margin = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush(BatteryBarFill),
            CornerRadius = new CornerRadius(3),
        });

        return new Border
        {
            Width = BatteryBarWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = bar,
        };
    }

    private static string FormatChargePercent(BatterySnapshot snapshot) =>
        snapshot.BatteryPresent ? $"{snapshot.ChargePercentage}%" : "--";

    private static StackPanel DetailBlock(string label, string value, SettingsPalette p)
    {
        StackPanel panel = new() { Spacing = 3 };
        TextBlock labelText = Text(label, p, 13);
        labelText.Foreground = Brush(p.SecondaryForeground);
        panel.Children.Add(labelText);
        panel.Children.Add(Text(value, p, 18, FontWeight.SemiBold));
        return panel;
    }

    private static void AddDetailRow(Grid grid, int row, string label, string value, SettingsPalette p)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        TextBlock labelText = Text(label + ":", p, 12);
        labelText.Foreground = Brush(p.SecondaryForeground);
        labelText.Margin = new Thickness(0, 0, 14, 0);
        Grid.SetRow(labelText, row);
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

        TextBlock valueText = Text(value, p, 12);
        valueText.TextAlignment = TextAlignment.Right;
        Grid.SetRow(valueText, row);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(valueText);
    }

    private static Border Separator(SettingsPalette p) =>
        new()
        {
            Height = 1,
            Background = Brush(p.Border),
            Opacity = 0.75,
        };

    private static TextBlock Text(string text, SettingsPalette p, double size, FontWeight weight = FontWeight.Normal) =>
        new()
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = size,
            FontWeight = weight,
            Foreground = Brush(p.Foreground),
        };

    private static SolidColorBrush Brush(Color color) => new(color);

    private static FlyoutControlPalette ToFlyoutPalette(SettingsPalette p, AppTheme theme, bool isLight) =>
        new(
            p.Foreground,
            p.SecondaryForeground,
            p.Border,
            p.Hover,
            p.Pressed,
            p.ControlBackground,
            p.CardBackground,
            theme.IconForeground.For(isLight),
            p.SliderTrack,
            p.SliderProgress,
            p.SliderThumb);

    private static FlyoutPowerMode? QueryActivePowerMode()
    {
        string? output = RunPowerCfgOutput("/getactivescheme");
        if (string.IsNullOrWhiteSpace(output)) return null;

        if (PowerSchemeMatches(output, UltimatePowerSchemeGuid, UltimatePowerSchemeName))
            return FlyoutPowerMode.Ultimate;
        if (output.Contains(BalancedPowerSchemeGuid, StringComparison.OrdinalIgnoreCase))
            return FlyoutPowerMode.Balanced;
        if (output.Contains(PowerSaverPowerSchemeGuid, StringComparison.OrdinalIgnoreCase))
            return FlyoutPowerMode.PowerSaver;

        return null;
    }

    private static bool? QueryEnergySaverAlwaysEnabled()
    {
        string? output = RunPowerCfgOutput(
            $"/qh SCHEME_CURRENT {EnergySaverSubgroupGuid} {EnergySaverBatteryThresholdGuid}");
        if (string.IsNullOrWhiteSpace(output)) return null;

        int? acThreshold = ParsePowerCfgIndex(output, "Current AC Power Setting Index");
        int? dcThreshold = ParsePowerCfgIndex(output, "Current DC Power Setting Index");

        if (dcThreshold.HasValue)
            return dcThreshold.Value >= EnergySaverAlwaysThreshold;
        if (acThreshold.HasValue)
            return acThreshold.Value >= EnergySaverAlwaysThreshold;
        return null;
    }

    private static int? ParsePowerCfgIndex(string output, string label)
    {
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains(label, StringComparison.OrdinalIgnoreCase)) continue;
            int colon = line.IndexOf(':');
            if (colon < 0 || colon == line.Length - 1) return null;

            string token = line[(colon + 1)..].Trim();
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(
                    token[2..],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out int hexValue)
                    ? hexValue
                    : null;
            }

            return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : null;
        }

        return null;
    }

    private void SetPowerMode(FlyoutPowerMode mode)
    {
        if (_isPowerModeChanging) return;

        _isPowerModeChanging = true;
        Rebuild();
        _ = SetPowerModeAsync(mode);
    }

    private void SetEnergySaver(bool enabled)
    {
        if (_isEnergySaverChanging) return;

        _isEnergySaverChanging = true;
        Rebuild();
        _ = SetEnergySaverAsync(enabled);
    }

    private async Task SetPowerModeAsync(FlyoutPowerMode mode)
    {
        bool success = await Task.Run(() => SetActivePowerMode(mode));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isPowerModeChanging = false;
            if (!success) TADNLog.Log($"BatteryFlyoutWindow.SetPowerModeAsync({mode}): powercfg failed");
            _batteryMonitor.ForceRefresh();
            Rebuild();
        });
    }

    private static bool SetActivePowerMode(FlyoutPowerMode mode)
    {
        if (mode == FlyoutPowerMode.Ultimate)
            return SetUltimatePowerModeActive();

        return RunPowerCfg("/setactive " + PowerSchemeGuid(mode));
    }

    private static bool SetUltimatePowerModeActive()
    {
        string? schemeGuid = FindPowerSchemeGuid(UltimatePowerSchemeGuid, UltimatePowerSchemeName);
        if (!string.IsNullOrWhiteSpace(schemeGuid) && RunPowerCfg("/setactive " + schemeGuid))
            return true;

        schemeGuid = CreateUltimatePowerScheme();
        return !string.IsNullOrWhiteSpace(schemeGuid) && RunPowerCfg("/setactive " + schemeGuid);
    }

    private static string? CreateUltimatePowerScheme()
    {
        string? output = RunPowerCfgOutput("/duplicatescheme " + UltimatePowerSchemeGuid);
        string? createdGuid = ExtractPowerSchemeGuid(output);
        if (!string.IsNullOrWhiteSpace(createdGuid)) return createdGuid;

        return FindPowerSchemeGuid(UltimatePowerSchemeGuid, UltimatePowerSchemeName);
    }

    private static string? FindPowerSchemeGuid(string preferredGuid, string schemeName)
    {
        string? output = RunPowerCfgOutput("/list");
        if (string.IsNullOrWhiteSpace(output)) return null;

        string? namedGuid = null;
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string? guid = ExtractPowerSchemeGuid(line);
            if (string.IsNullOrWhiteSpace(guid)) continue;
            if (guid.Equals(preferredGuid, StringComparison.OrdinalIgnoreCase)) return guid;
            if (line.Contains($"({schemeName})", StringComparison.OrdinalIgnoreCase)) namedGuid ??= guid;
        }

        return namedGuid;
    }

    private static bool PowerSchemeMatches(string output, string guid, string schemeName) =>
        output.Contains(guid, StringComparison.OrdinalIgnoreCase)
        || output.Contains($"({schemeName})", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractPowerSchemeGuid(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        foreach (string token in output.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Guid.TryParse(token, out Guid guid))
                return guid.ToString("D");
        }

        return null;
    }

    private async Task SetEnergySaverAsync(bool enabled)
    {
        bool success = await Task.Run(() => SetEnergySaverThreshold(enabled));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _isEnergySaverChanging = false;
            if (!success) TADNLog.Log($"BatteryFlyoutWindow.SetEnergySaverAsync({enabled}): powercfg failed");
            _batteryMonitor.ForceRefresh();
            Rebuild();
        });
    }

    private static string PowerSchemeGuid(FlyoutPowerMode mode) => mode switch
    {
        FlyoutPowerMode.Ultimate => UltimatePowerSchemeGuid,
        FlyoutPowerMode.Balanced => BalancedPowerSchemeGuid,
        FlyoutPowerMode.PowerSaver => PowerSaverPowerSchemeGuid,
        _ => BalancedPowerSchemeGuid,
    };

    private static bool SetEnergySaverThreshold(bool enabled)
    {
        int value = enabled ? EnergySaverAlwaysThreshold : EnergySaverNeverThreshold;
        bool acSuccess = RunPowerCfg(
            $"/setacvalueindex SCHEME_CURRENT {EnergySaverSubgroupGuid} {EnergySaverBatteryThresholdGuid} {value}");
        bool dcSuccess = RunPowerCfg(
            $"/setdcvalueindex SCHEME_CURRENT {EnergySaverSubgroupGuid} {EnergySaverBatteryThresholdGuid} {value}");
        bool activeSuccess = RunPowerCfg("/setactive SCHEME_CURRENT");

        return acSuccess && dcSuccess && activeSuccess;
    }

    private static string? RunPowerCfgOutput(string arguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            if (process == null) return null;

            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(PowerCfgTimeoutMs))
            {
                TryKill(process);
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryFlyoutWindow.RunPowerCfgOutput({arguments}): {ex.Message}");
            return null;
        }
    }

    private static bool RunPowerCfg(string arguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null) return false;

            if (!process.WaitForExit(PowerCfgTimeoutMs))
            {
                TryKill(process);
                return false;
            }

            if (process.ExitCode != 0)
                TADNLog.Log($"BatteryFlyoutWindow.RunPowerCfg({arguments}): exit code {process.ExitCode}");
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryFlyoutWindow.RunPowerCfg({arguments}): {ex.Message}");
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { }
    }

    private static void OpenModernPowerSettings()
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:powersleep",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { TADNLog.Log($"BatteryFlyoutWindow.OpenModernPowerSettings: {ex.Message}"); }
    }

    private static void OpenEnergySaverSettings()
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:batterysaver",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { TADNLog.Log($"BatteryFlyoutWindow.OpenEnergySaverSettings: {ex.Message}"); }
    }

    private static string BuildStatus(BatterySnapshot snapshot)
    {
        if (!snapshot.BatteryPresent) return "No battery detected";
        if (snapshot.IsFullyCharged) return "Plugged in, full";
        if (snapshot.IsCharging)
        {
            return snapshot.ChargeRateWatts.HasValue
                ? $"Charging at {snapshot.ChargeRateWatts.Value:F1} W"
                : "Charging";
        }

        if (snapshot.IsOnExternalPower) return "Plugged in";
        return "On battery";
    }

    private static string FormatPower(float? watts) =>
        watts.HasValue ? $"{watts.Value:F1} W" : "N/A";

    private static string FormatCapacity(float? milliwattHours) =>
        milliwattHours.HasValue ? $"{milliwattHours.Value / 1000f:F1} Wh" : "N/A";

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

    private void OnClosed(object? sender, EventArgs e)
    {
        _batteryMonitor.StateChanged -= OnBatteryStateChanged;
        _settings.Changed -= OnSettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Closed -= OnClosed;
    }
}
