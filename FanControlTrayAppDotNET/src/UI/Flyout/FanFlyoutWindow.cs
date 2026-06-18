using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
using FanControlTrayAppDotNET.Services;

namespace FanControlTrayAppDotNET.UI;

public sealed partial class FanFlyoutWindow : FlyoutWindowCommon, INotifyPropertyChanged
{
    private static readonly FontFamily FlyoutFont = new("Segoe UI");
    const string FanFontFamilyName = $"avares://{Program.ApplicationName}/Visuals/FanFont.ttf#Untitled1";

    private const string NewGroupBaseName = "New_Fan_Group";

    private readonly LHMService? _lhmService;
    private readonly AppSettings _settings;
    private readonly Action<FanSettingsPage?> _openSettings;
    private readonly ObservableCollection<FanFlyoutCell> _cells = [];
    private readonly List<string> _groupNames = [];
    private readonly Dictionary<string, FanGroup> _groupSettingsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Fan, FanPropertiesWindow> _fanPropertiesWindows = [];
    private readonly List<Fan> _fanPropertiesOrder = [];
    private readonly HashSet<Fan> _subscribedFans = [];
    private readonly FlyoutWindowDragHelper _dragHelper = new();

    private TrayAppDotNETShellTrayIcon? _lastTrayIcon;
    private StackPanel? _cellStack;
    private Canvas? _dragOverlay;
    private ScrollViewer? _scrollViewer;
    private Border? _rootCard;
    private Border? _undockButton;
    private TextBlock? _undockButtonGlyph;
    private TextBlock? _nonFunctioningFansButtonGlyph;
    private readonly Dictionary<Fan, List<FanRowVisualRefs>> _fanRowRefs = [];
    private Border? _confirmOverlay;
    private TextBlock? _confirmTitle;
    private TextBlock? _confirmMessage;
    private FlyoutLayout? _layout;
    private SettingsButton? _confirmOK;
    private SettingsButton? _confirmCancel;
    private Border? _dragGhost;
    private Border? _dropMarker;
    private readonly List<FanDragSlot> _dragSlots = [];
    private Control? _dragSourceControl;
    private Control? _dragSourceTopLevelControl;
    private Fan? _draggedFan;
    private FanFlyoutCell? _draggedGroupCell;
    private FanFlyoutCell? _dragSourceCell;
    private FanDragPlacement _dragPlacement = FanDragPlacement.None;
    private Point _dragStart;
    private double _dragPointerOffsetY;
    private double _dragSourceSlotHeight;
    private double _dragGhostHeight;
    private int _dragSourceTopLevelIndex = -1;
    private bool _isUndocked;
    private bool _showNonFunctioningFans;
    private bool _suppressFanRebuild;
    private bool _isDraggingWindow;
    private bool _undockButtonPointerCaptured;
    private bool _undockButtonDragOccurred;
    private bool _isCompletingDrag;
    private bool _pendingFanRebuild;
    private int _activeSliderDrags;
    private int? _lastRequestedProfile;
    private TaskCompletionSource<bool>? _confirmTcs;

    public FanFlyoutWindow()
        : this(null, new AppSettings(), static _ => { })
    {
    }

    internal FanFlyoutWindow(LHMService? lhmService, AppSettings settings, Action<FanSettingsPage?> openSettings)
    {
        _lhmService = lhmService;
        _settings = settings;
        _openSettings = openSettings;
        _showNonFunctioningFans = settings.ShowNonFunctioningFans;

        InitializeComponent();
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _isUndocked = settings is
        {
            AllowFlyoutUndock: true, RestoreFlyoutUndockedOnStartup: true, FlyoutUndocked: true,
            FlyoutHasSavedPosition: true
        };

        LoadGroupCatalog();
        _settings.EnsureFanProfileCount(3);
        _settings.Changed += OnSettingsChanged;
        if (_lhmService != null)
        {
            ((INotifyCollectionChanged)_lhmService.Fans).CollectionChanged += OnFansChanged;
            WireFanPropertySubscriptions();
        }

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };

        RebuildCells();
        InitializeComponentState();
    }

    private void InitializeComponentState()
    {
        _layout = FlyoutLayout.From(this);

        if (_settings != null)
            RebuildVisual();
    }

    private FlyoutLayout Layout =>
        _layout ?? throw new InvalidOperationException("Fan flyout layout resources have not been loaded.");

    public new event PropertyChangedEventHandler? PropertyChanged;

    public bool IsUndocked => _isUndocked;

    public string NonFunctioningFansGlyph => _showNonFunctioningFans ? GlyphCatalog.VIEW : GlyphCatalog.HIDE;

    protected override bool HasOpenChildWindow =>
        _fanPropertiesWindows.Values.Any(window => window.IsVisible);

    protected override bool ShouldAutoHideWhenDeactivated => !_isUndocked;

    protected override void HideFlyout() => Hide();

    public void Redock()
    {
        if (!_isUndocked) return;
        _isUndocked = false;
        _settings.FlyoutUndocked = false;
        _settings.Save();
        UpdateUndockButtonVisual();
        QueuePositionNearTray();
        OnPropertyChanged(nameof(IsUndocked));
    }

    public void ShowAt(TrayAppDotNETShellTrayIcon trayIcon, bool activate = true)
    {
        _lastTrayIcon = trayIcon;
        ApplyWorkAreaMaxHeight();
        RebuildCells();
        RebuildVisual();
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
            ShowPinnedFanPropertiesWindows();
            Opacity = 1;
            if (activate) Activate();
        }, DispatcherPriority.Loaded);
    }

    public new void Hide()
    {
        CancelDrag();
        CancelConfirmOverlay();
        HideFanPropertiesWindowsForFlyoutHide();
        base.Hide();
        NotifyWarmDismissed();
    }

    private void RebuildVisual()
    {
        if (_layout == null) return;

        bool isLight = AppTheme.ResolveEffectiveIsLightTheme(_settings);
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;
        SettingsPalette sp = FanSettingsWindow.CreatePalette(theme, _settings, isLight);
        FlyoutControlPalette p = CreateFlyoutPalette(theme, sp, isLight);
        _fanRowRefs.Clear();

        DockPanel body = new() { LastChildFill = true };
        Control header = BuildHeader(p);
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        body.Children.Add(BuildCellList(p, theme, isLight));

        Grid rootGrid = new();
        rootGrid.Children.Add(body);
        _confirmOverlay = BuildConfirmOverlay(sp, isLight);
        _confirmOverlay.IsVisible = false;
        rootGrid.Children.Add(_confirmOverlay);
        _dragOverlay = new Canvas { IsHitTestVisible = false };
        rootGrid.Children.Add(_dragOverlay);

        _rootCard = new Border
        {
            Background = TrayAppDotNETFlyoutUI.Brush(theme.ResolveFlyoutBackground(_settings, isLight)),
            BorderBrush = TrayAppDotNETFlyoutUI.Brush(theme.Border.For(isLight)),
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
                Background = TrayAppDotNETFlyoutUI.Brush(theme.ResolveFlyoutBackground(_settings, isLight)),
                CornerRadius = Rounded(Layout.RootInnerCornerRadius),
                ClipToBounds = true,
                Margin = Layout.RootInnerMargin,
                Child = rootGrid,
            },
        };
        _rootCard.PointerPressed += OnRootPointerPressed;
        _rootCard.PointerMoved += OnRootPointerMoved;
        _rootCard.PointerReleased += OnRootPointerReleased;
        _rootCard.PointerCaptureLost += OnRootPointerCaptureLost;
        Content = _rootCard;
    }

    private Border BuildHeader(FlyoutControlPalette p)
    {
        Grid grid = new()
        {
            Margin = Layout.HeaderMargin,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(Layout.HeaderWideColumnWidth)),
                new ColumnDefinition(new GridLength(Layout.HeaderWideColumnWidth)),
                new ColumnDefinition(new GridLength(Layout.HeaderWideColumnWidth)),
                new ColumnDefinition(new GridLength(Layout.HeaderWideColumnWidth)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(Layout.HeaderNarrowColumnWidth)),
                new ColumnDefinition(new GridLength(Layout.HeaderNarrowColumnWidth)),
                new ColumnDefinition(new GridLength(Layout.HeaderNarrowColumnWidth)),
                new ColumnDefinition(new GridLength(Layout.HeaderWideColumnWidth)),
            },
        };

        AddHeaderButton(grid, 0, GlyphCatalog.SETTINGS, p, () => _openSettings(null), L("Tray_Settings", "Settings"));
        AddHeaderButton(grid, 1, GlyphCatalog.CURVE_WINDOW, p, () => _openSettings(FanSettingsPage.FanProperties),
            "Fan manager", fontSize: Layout.HeaderManagerButtonFontSize);
        _nonFunctioningFansButtonGlyph = AddHeaderButton(grid, 2, NonFunctioningFansGlyph, p, ToggleNonFunctioningFans,
            "Show/hide non-functioning fans");
        AddGroupButton(grid, p);
        AddProfileButton(grid, 5, 1, p);
        AddProfileButton(grid, 6, 2, p);
        AddProfileButton(grid, 7, 3, p);

        _undockButton = BuildUndockButton(p);
        _undockButton.IsVisible = _settings.AllowFlyoutUndock;
        Grid.SetColumn(_undockButton, 8);
        grid.Children.Add(_undockButton);
        return new Border
        {
            Height = Layout.HeaderHeight,
            Background =
                TrayAppDotNETFlyoutUI.Brush(
                    (AppServices.Theme ?? AppTheme.Default).ResolveFlyoutTitleBarBackground(_settings,
                        AppTheme.ResolveEffectiveIsLightTheme(_settings))),
            Child = grid,
        };
    }

    private Grid BuildCellList(FlyoutControlPalette p, AppTheme theme, bool isLight)
    {
        Grid holder = new()
        {
            ClipToBounds = true,
            Margin = Layout.CellListMargin(Math.Clamp(_settings.FlyoutTitleBarCardSpacing, 0,
                Layout.MaxSettingSpacing)),
        };

        _cellStack = new StackPanel { Spacing = 0 };
        foreach (FanFlyoutCell cell in _cells)
            _cellStack.Children.Add(BuildCell(cell, p, theme, isLight));

        _scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = _cellStack,
        };
        holder.Children.Add(_scrollViewer);

        TextBlock empty = TrayAppDotNETFlyoutUI.Text("No fans detected", p, Layout.EmptyTextFontSize,
            color: p.SecondaryForeground);
        empty.Opacity = Layout.EmptyTextOpacity;
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.IsVisible = _cells.Count == 0;
        holder.Children.Add(empty);
        return holder;
    }

    private Border BuildCell(FanFlyoutCell cell, FlyoutControlPalette p, AppTheme theme, bool isLight,
        bool interactive = true)
    {
        StackPanel content = new() { Spacing = 0 };
        if (cell.HasGroupHeader)
        {
            content.Children.Add(BuildGroupHeader(cell, p));
            if (cell.AreGroupFansVisible)
            {
                foreach (Fan fan in cell.Fans)
                    content.Children.Add(BuildFanRow(fan, cell, p, grouped: true, interactive));
            }
        }
        else if (cell.Fans.Count == 1)
        {
            content.Children.Add(BuildFanRow(cell.Fans[0], cell, p, grouped: false, interactive));
        }

        Color background = cell.HasGroupHeader
            ? theme.ResolveGroupCardBackground(_settings, isLight)
            : theme.ResolveFanCardBackground(_settings, isLight);
        Color border = theme.ResolveFlyoutCardBorder(_settings, isLight);
        Thickness borderThickness = _settings.EnableCardBorders ? Layout.CardBorderThickness : Layout.ZeroThickness;
        Border card = TrayAppDotNETFlyoutUI.Card(
            content,
            background,
            border,
            Rounded(Layout.CardCornerRadius),
            Layout.CardPadding,
            Layout.CardMargin(
                Math.Clamp(_settings.FlyoutCardHorizontalInset, 0, Layout.MaxSettingSpacing),
                Math.Clamp(_settings.FlyoutCardHorizontalInset, 0, Layout.MaxSettingSpacing),
                Math.Clamp(_settings.FlyoutCardSpacing, 0, Layout.MaxSettingSpacing)),
            borderThickness);
        card.Tag = cell;
        if (interactive && cell.HasGroupHeader)
            WireGroupDrag(card, cell);
        return card;
    }

    private Grid BuildFanRow(Fan fan, FanFlyoutCell cell, FlyoutControlPalette p, bool grouped, bool interactive = true)
    {
        Grid row = new()
        {
            Tag = fan,
            Background = Brushes.Transparent,
            Margin =
                grouped
                    ? Layout.FanRowGroupedMargin(Math.Clamp(_settings.FlyoutCardSpacing, 0, Layout.MaxSettingSpacing))
                    : Layout.ZeroThickness,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), },
        };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        Border fanButton = TrayAppDotNETFlyoutUI.IconButton(
            GlyphCatalog.FAN,
            p,
            _ => ToggleFanPropertiesWindow(fan),
            Layout.FanButtonWidth,
            Layout.FanButtonHeight,
            Layout.FanButtonFontSize,
            margin: grouped ? Layout.FanButtonGroupedMargin : Layout.FanButtonUngroupedMargin,
            tooltip: "Fan properties",
            fontFamily: FanFontFamilyName);
        Grid.SetRow(fanButton, 0);
        Grid.SetColumn(fanButton, 0);
        row.Children.Add(fanButton);

        Grid nameGrid = new();
        TextBlock name = TrayAppDotNETFlyoutUI.Text(fan.DisplayName, p, Layout.FanNameFontSize);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Tag = fan;
        name.PointerPressed += FanNameTextPointerPressed;
        TextBox edit = InlineTextBox(fan.DisplayName, p);
        edit.Name = "FanNameEdit";
        edit.IsVisible = false;
        edit.KeyDown += FanNameEditKeyDown;
        edit.LostFocus += FanNameEditLostFocus;
        nameGrid.Children.Add(name);
        nameGrid.Children.Add(edit);

        string subtitleText = grouped
            ? $"{fan.CurrentDutyCycle.ToString("0", CultureInfo.InvariantCulture)}%"
            : fan.AssignedCurveDisplayLabel;
        TextBlock subtitle = TrayAppDotNETFlyoutUI.Text(subtitleText, p, Layout.FanSubtitleFontSize);
        subtitle.Opacity = 0.8;
        subtitle.Margin = Layout.SubtitleMargin;
        subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        StackPanel nameStack = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = grouped ? Layout.FanNameStackGroupedMargin : Layout.FanNameStackUngroupedMargin,
        };
        nameStack.Children.Add(nameGrid);
        nameStack.Children.Add(subtitle);
        Grid.SetRow(nameStack, 0);
        Grid.SetColumn(nameStack, 1);
        row.Children.Add(nameStack);

        StackPanel telemetry = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = Layout.TelemetryMargin,
        };
        TextBlock rpm = TrayAppDotNETFlyoutUI.Text($"{fan.CurrentRPM} RPM", p, Layout.RpmFontSize);
        rpm.Opacity = 0.7;
        rpm.HorizontalAlignment = HorizontalAlignment.Right;
        TextBlock controller = TrayAppDotNETFlyoutUI.Text(fan.ControllerDisplayLabel, p, Layout.ControllerFontSize);
        controller.Opacity = 0.45;
        controller.HorizontalAlignment = HorizontalAlignment.Right;
        controller.TextTrimming = TextTrimming.CharacterEllipsis;
        controller.Margin = Layout.ControllerMargin;
        telemetry.Children.Add(rpm);
        telemetry.Children.Add(controller);
        Grid.SetRow(telemetry, 0);
        Grid.SetColumn(telemetry, 2);
        row.Children.Add(telemetry);

        Border mode = TrayAppDotNETFlyoutUI.IconButton(
            fan.CurrentControlMode == FanControlMode.Manual
                ? GlyphCatalog.FLYOUT_FAN_CONTROL_MODE_MANUAL
                : GlyphCatalog.FLYOUT_FAN_CONTROL_MODE_CURVE,
            p,
            _ => ToggleFanMode(fan),
            Layout.ModeButtonWidth,
            Layout.ModeButtonHeight,
            Layout.ModeButtonFontSize,
            margin: grouped ? Layout.ModeButtonGroupedMargin : Layout.ModeButtonUngroupedMargin,
            tooltip: fan.CurrentControlMode == FanControlMode.Manual ? "Manual" : "Curve");
        Grid.SetRow(mode, 1);
        Grid.SetColumn(mode, 0);
        row.Children.Add(mode);
        TextBlock? modeGlyph = mode.Child as TextBlock;

        Grid sliderRow = new()
        {
            Height = Layout.SliderRowHeight,
            Margin = Layout.SliderRowMargin,
            VerticalAlignment = VerticalAlignment.Center,
        };
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(fan.FanDisplayedValueSlotWidth)));
        TextBlock? valueText = null;
        FlyoutSlider slider = CreateSlider(p, fan.FanDisplayedValue, fan.FanSliderMaximum, fan.RPMMode ? 50 : 2);
        bool sliderDragging = false;
        slider.DragStarted += (_, _) =>
        {
            sliderDragging = true;
            _activeSliderDrags++;
        };
        slider.DragCompleted += (_, _) =>
        {
            sliderDragging = false;
            if (_activeSliderDrags > 0) _activeSliderDrags--;
            AppServices.LHMService?.PersistLiveState();
            FlushPendingFanRebuild();
        };
        slider.ValueChanged += (_, value) =>
        {
            int next = Math.Clamp((int)Math.Round(value), 0, fan.FanSliderMaximum);
            fan.CurrentControlMode = FanControlMode.Manual;
            fan.FanDisplayedValue = next;
            valueText?.Text = fan.FanDisplayedValueText;
            if (!sliderDragging) AppServices.LHMService?.PersistLiveState();
        };
        sliderRow.Children.Add(slider);

        Grid valueGrid = new()
        {
            Width = fan.FanDisplayedValueSlotWidth,
            Height = Layout.SliderRowHeight,
            Margin = Layout.ValueGridMargin,
            VerticalAlignment = VerticalAlignment.Center,
        };
        valueText = TrayAppDotNETFlyoutUI.Text(fan.FanDisplayedValueText, p, Layout.ValueFontSize);
        valueText.HorizontalAlignment = HorizontalAlignment.Right;
        valueText.VerticalAlignment = VerticalAlignment.Center;
        Viewbox valueDisplay = new()
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = fan,
            Child = valueText,
        };
        valueDisplay.PointerPressed += FanValueTextPointerPressed;
        TextBox valueEdit = InlineTextBox(fan.FanDisplayedValueText, p);
        valueEdit.Name = "FanDisplayedValueEdit";
        valueEdit.IsVisible = false;
        valueEdit.KeyDown += FanValueEditKeyDown;
        valueEdit.LostFocus += FanValueEditLostFocus;
        valueGrid.Children.Add(valueDisplay);
        valueGrid.Children.Add(valueEdit);
        Grid.SetColumn(valueGrid, 1);
        sliderRow.Children.Add(valueGrid);
        Grid.SetRow(sliderRow, 1);
        Grid.SetColumn(sliderRow, 1);
        Grid.SetColumnSpan(sliderRow, 2);
        row.Children.Add(sliderRow);

        if (interactive)
        {
            RegisterFanRowRefs(fan, new FanRowVisualRefs(grouped, rpm, subtitle, valueText, slider, modeGlyph, mode));
            WireFanDrag(row, fan);
        }

        return row;
    }

    private Grid BuildGroupHeader(FanFlyoutCell cell, FlyoutControlPalette p)
    {
        Grid row = new()
        {
            Tag = cell,
            Background = Brushes.Transparent,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), },
        };

        Border groupIcon = TrayAppDotNETFlyoutUI.IconButton(GlyphCatalog.GROUP, p, _ => { }, Layout.GroupIconWidth,
            Layout.GroupIconHeight, Layout.GroupIconFontSize, tooltip: "Group");
        Grid.SetColumn(groupIcon, 0);
        row.Children.Add(groupIcon);

        Border expand = TrayAppDotNETFlyoutUI.IconButton(cell.GroupExpansionGlyph, p, _ =>
            {
                if (cell.GroupSettings == null) return;
                cell.GroupSettings.IsCollapsed = !cell.GroupSettings.IsCollapsed;
                SaveGroupChanges();
                RebuildVisual();
            }, Layout.GroupHeaderButtonWidth, Layout.GroupHeaderButtonHeight, Layout.GroupExpandFontSize,
            margin: Layout.GroupHeaderButtonMargin, tooltip: cell.GroupExpansionTooltip);
        Grid.SetColumn(expand, 2);
        row.Children.Add(expand);

        Grid nameGrid = new();
        TextBlock name = TrayAppDotNETFlyoutUI.Text(cell.GroupName ?? string.Empty, p, Layout.GroupNameFontSize,
            FontWeight.SemiBold);
        name.Opacity = 0.85;
        name.Tag = cell;
        name.PointerPressed += GroupNameTextPointerPressed;
        TextBox edit = InlineTextBox(cell.GroupName ?? string.Empty, p);
        edit.Name = "GroupNameEdit";
        edit.IsVisible = false;
        edit.KeyDown += GroupNameEditKeyDown;
        edit.LostFocus += GroupNameEditLostFocus;
        nameGrid.Children.Add(name);
        nameGrid.Children.Add(edit);
        TextBlock activeCurve = TrayAppDotNETFlyoutUI.Text(cell.ActiveCurveText, p, Layout.FanSubtitleFontSize);
        activeCurve.Opacity = 0.8;
        activeCurve.Margin = Layout.SubtitleMargin;
        StackPanel title = new() { VerticalAlignment = VerticalAlignment.Center, Margin = Layout.GroupTitleMargin, };
        title.Children.Add(nameGrid);
        title.Children.Add(activeCurve);
        Grid.SetColumn(title, 1);
        row.Children.Add(title);

        Border delete = TrayAppDotNETFlyoutUI.IconButton(GlyphCatalog.DELETE, p, e => _ = DeleteGroupAsync(cell),
            Layout.GroupHeaderButtonWidth, Layout.GroupHeaderButtonHeight, Layout.GroupDeleteFontSize,
            margin: Layout.GroupHeaderButtonMargin, tooltip: "Delete group");
        Grid.SetColumn(delete, 3);
        row.Children.Add(delete);

        Border mode = TrayAppDotNETFlyoutUI.IconButton(
            cell.GroupCurrentControlMode == FanControlMode.Manual
                ? GlyphCatalog.FLYOUT_FAN_CONTROL_MODE_MANUAL
                : GlyphCatalog.FLYOUT_FAN_CONTROL_MODE_CURVE,
            p,
            _ => ToggleGroupMode(cell),
            Layout.ModeButtonWidth,
            Layout.ModeButtonHeight,
            Layout.ModeButtonFontSize,
            margin: Layout.GroupModeMargin);
        Grid.SetRow(mode, 1);
        Grid.SetColumn(mode, 0);
        row.Children.Add(mode);

        Grid sliderRow = new()
        {
            Height = Layout.SliderRowHeight,
            Margin = Layout.GroupSliderRowMargin,
            VerticalAlignment = VerticalAlignment.Center,
        };
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        sliderRow.ColumnDefinitions.Add(
            new ColumnDefinition(new GridLength(FanFlyoutCell.GroupFanDisplayedValueSlotWidth)));
        TextBlock? valueText = null;
        FlyoutSlider slider = CreateSlider(p, cell.GroupFanDisplayedValue, FanFlyoutCell.GroupFanSliderMaximum, 2);
        bool sliderDragging = false;
        slider.DragStarted += (_, _) =>
        {
            sliderDragging = true;
            _activeSliderDrags++;
        };
        slider.DragCompleted += (_, _) =>
        {
            sliderDragging = false;
            if (_activeSliderDrags > 0) _activeSliderDrags--;
            SaveGroupChanges();
            FlushPendingFanRebuild();
        };
        slider.ValueChanged += (_, value) =>
        {
            cell.GroupFanDisplayedValue = Math.Clamp((int)Math.Round(value), 0, FanFlyoutCell.GroupFanSliderMaximum);
            if (cell.GroupCurrentControlMode == FanControlMode.Manual)
                ApplyGroupManualValueToFans(cell);
            valueText?.Text = cell.GroupFanDisplayedValueText;
            if (sliderDragging) AppServices.LHMService?.PersistLiveState(save: false);
            else SaveGroupChanges();
        };
        sliderRow.Children.Add(slider);
        Grid valueGrid = new()
        {
            Width = FanFlyoutCell.GroupFanDisplayedValueSlotWidth,
            Height = Layout.SliderRowHeight,
            Margin = Layout.GroupValueGridMargin,
            VerticalAlignment = VerticalAlignment.Center,
        };
        valueText = TrayAppDotNETFlyoutUI.Text(cell.GroupFanDisplayedValueText, p, Layout.ValueFontSize);
        valueText.HorizontalAlignment = HorizontalAlignment.Right;
        valueText.VerticalAlignment = VerticalAlignment.Center;
        valueGrid.Children.Add(new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Child = valueText,
        });
        Grid.SetColumn(valueGrid, 1);
        sliderRow.Children.Add(valueGrid);
        Grid.SetRow(sliderRow, 1);
        Grid.SetColumn(sliderRow, 1);
        Grid.SetColumnSpan(sliderRow, 3);
        row.Children.Add(sliderRow);

        return row;
    }

    private FlyoutSlider CreateSlider(FlyoutControlPalette p, int value, int maximum, int wheelStep)
    {
        SliderThumbGlyphOption thumb = ResolveSliderThumbOption();
        return new FlyoutSlider
        {
            Minimum = 0,
            Maximum = Math.Max(1, maximum),
            Value = value,
            WheelStep = wheelStep,
            KeyboardStep = wheelStep,
            LargeKeyboardStep = wheelStep * 5,
            HitTestVerticalPadding = Layout.SliderHitTestVerticalPadding,
            TrackColor = p.SliderTrack,
            ProgressColor = p.SliderProgress,
            ThumbColor = p.SliderThumb,
            IndicatorColor = p.IconForeground,
            Thumb = thumb,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }

    private void RegisterFanRowRefs(Fan fan, FanRowVisualRefs refs)
    {
        if (!_fanRowRefs.TryGetValue(fan, out List<FanRowVisualRefs>? list))
        {
            list = [];
            _fanRowRefs[fan] = list;
        }

        list.Add(refs);
    }

    private void RefreshFanRowVisuals(Fan fan, string? propertyName)
    {
        if (!_fanRowRefs.TryGetValue(fan, out List<FanRowVisualRefs>? refsList)) return;

        foreach (FanRowVisualRefs refs in refsList)
        {
            if (propertyName is null or nameof(Fan.CurrentRPM))
                refs.RPM.Text = $"{fan.CurrentRPM} RPM";

            if (refs.Grouped && propertyName is null or nameof(Fan.CurrentDutyCycle))
                refs.Subtitle.Text = $"{fan.CurrentDutyCycle.ToString("0", CultureInfo.InvariantCulture)}%";

            if (propertyName is null or nameof(Fan.FanDisplayedValue) or nameof(Fan.FanDisplayedValueText)
                or nameof(Fan.FanSliderMaximum))
            {
                refs.Value.Text = fan.FanDisplayedValueText;
                refs.Slider.Maximum = Math.Max(1, fan.FanSliderMaximum);
                refs.Slider.Value = Math.Clamp(fan.FanDisplayedValue, 0, fan.FanSliderMaximum);
            }

            if (propertyName is null or nameof(Fan.CurrentControlMode))
            {
                refs.ModeGlyph?.Text = fan.CurrentControlMode == FanControlMode.Manual
                    ? GlyphCatalog.FLYOUT_FAN_CONTROL_MODE_MANUAL
                    : GlyphCatalog.FLYOUT_FAN_CONTROL_MODE_CURVE;
                TrayAppDotNETToolTip.SetTip(refs.ModeButton,
                    fan.CurrentControlMode == FanControlMode.Manual ? "Manual" : "Curve");
            }
        }
    }

    private TextBlock? AddHeaderButton(
        Grid grid,
        int column,
        string glyph,
        FlyoutControlPalette p,
        Action click,
        string tooltip,
        double? fontSize = null)
    {
        Border button = TrayAppDotNETFlyoutUI.IconButton(glyph, p, _ => click(), Layout.HeaderButtonWidth,
            Layout.HeaderButtonHeight, fontSize ?? Layout.HeaderButtonFontSize, tooltip: tooltip);
        TextBlock? text = button.Child as TextBlock;
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
        return text;
    }

    private void AddGroupButton(Grid grid, FlyoutControlPalette p)
    {
        Grid icon = new()
        {
            Width = Layout.HeaderAddGroupIconSize, Height = Layout.HeaderAddGroupIconSize, IsHitTestVisible = false,
        };
        icon.Children.Add(TrayAppDotNETFlyoutUI.IconText(GlyphCatalog.GROUP, p, Layout.HeaderAddGroupFontSize));
        TextBlock add = TrayAppDotNETFlyoutUI.IconText(GlyphCatalog.ADD, p, Layout.HeaderAddGlyphFontSize);
        add.HorizontalAlignment = HorizontalAlignment.Right;
        add.VerticalAlignment = VerticalAlignment.Top;
        icon.Children.Add(add);

        Border button = TrayAppDotNETFlyoutUI.IconButton(string.Empty, p, _ => AddGroup(), Layout.HeaderButtonWidth,
            Layout.HeaderButtonHeight, 0, tooltip: "Add group");
        button.Child = icon;
        Grid.SetColumn(button, 3);
        grid.Children.Add(button);
    }

    private void AddProfileButton(Grid grid, int column, int profileNumber, FlyoutControlPalette p)
    {
        TextBlock label = TrayAppDotNETFlyoutUI.Text(profileNumber.ToString(CultureInfo.InvariantCulture), p,
            Layout.ProfileLabelFontSize, FontWeight.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        Border underline = new()
        {
            Background = TrayAppDotNETFlyoutUI.Brush(p.Foreground),
            Height = Layout.ProfileUnderlineHeight,
            Width = Layout.ProfileUnderlineWidth,
            CornerRadius = Layout.ProfileUnderlineCornerRadius,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = Layout.ProfileUnderlineMargin,
            IsVisible = _settings.SelectedFanProfileIndex == profileNumber - 1,
        };
        Grid content = new() { IsHitTestVisible = false };
        content.Children.Add(label);
        content.Children.Add(underline);
        Border button = TrayAppDotNETFlyoutUI.IconButton(string.Empty, p, _ => SelectFanProfile(profileNumber - 1),
            Layout.ProfileButtonWidth, Layout.ProfileButtonHeight, 0, tooltip: $"Profile {profileNumber}");
        button.Child = content;
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    private Border BuildUndockButton(FlyoutControlPalette p)
    {
        TextBlock text = TrayAppDotNETFlyoutUI.IconText(UndockButtonGlyph(), p, Layout.UndockFontSize);
        Border button = new()
        {
            Width = Layout.HeaderButtonWidth,
            Height = Layout.HeaderButtonHeight,
            CornerRadius = Rounded(Layout.HeaderButtonCornerRadius),
            Background = Brushes.Transparent,
            Child = text,
            Cursor = _settings.AllowFlyoutUndock
                ? new Cursor(StandardCursorType.Hand)
                : new Cursor(StandardCursorType.Arrow),
            IsEnabled = _settings.AllowFlyoutUndock,
        };

        _undockButtonGlyph = text;
        UpdateUndockButtonVisual();
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);

        bool pointerInside = false;
        button.PointerEntered += (_, _) =>
        {
            pointerInside = true;
            if (!_isDraggingWindow && button.IsEnabled)
                button.Background = TrayAppDotNETFlyoutUI.Brush(p.Hover);
        };
        button.PointerExited += (_, _) =>
        {
            pointerInside = false;
            if (!_isDraggingWindow)
                button.Background = Brushes.Transparent;
        };
        button.PointerPressed += (_, e) =>
        {
            if (!button.IsEnabled) return;
            if (e.GetCurrentPoint(button).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
            pointerInside = true;
            BeginUndockButtonDrag(button, e);
            button.Background = TrayAppDotNETFlyoutUI.Brush(p.Pressed);
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
            bool releasedInside = TrayAppDotNETFlyoutUI.IsPointerInside(button, e);
            FinishUndockButtonDrag(e.Pointer, commitDrag: true, clickWhenNotDragged: releasedInside);
            button.Background = releasedInside ? TrayAppDotNETFlyoutUI.Brush(p.Hover) : Brushes.Transparent;
            e.Handled = true;
        };
        button.PointerCaptureLost += (_, _) =>
        {
            if (!_undockButtonPointerCaptured) return;
            FinishUndockButtonDrag(null, commitDrag: _undockButtonDragOccurred, clickWhenNotDragged: false);
            button.Background = pointerInside ? TrayAppDotNETFlyoutUI.Brush(p.Hover) : Brushes.Transparent;
        };

        return button;
    }

    private Border BuildConfirmOverlay(SettingsPalette p, bool isLight)
    {
        _confirmTitle = TrayAppDotNETSettingsUI.Text(L("SettingsWindow_ConfirmOverlay_DefaultTitle", "Confirm"), p,
            Layout.ConfirmTitleFontSize, FontWeight.SemiBold);
        _confirmTitle.TextWrapping = TextWrapping.Wrap;
        _confirmTitle.Margin = Layout.ConfirmTitleMargin;
        _confirmMessage = TrayAppDotNETSettingsUI.DescriptionText(
            L("SettingsWindow_ConfirmOverlay_DefaultMessage", "Are you sure?"),
            p,
            Layout.ConfirmMessageMargin);
        _confirmOK = TrayAppDotNETSettingsUI.Button(L("Flyout_DeleteGroup_Confirm", "Delete"), p);
        _confirmCancel = TrayAppDotNETSettingsUI.Button(L("SettingsWindow_ConfirmOverlay_Cancel", "Cancel"), p);
        _confirmCancel.Margin = Layout.ConfirmCancelMargin;
        _confirmOK.Click += (_, _) => CompleteConfirm(true);
        _confirmCancel.Click += (_, _) => CompleteConfirm(false);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(_confirmCancel);
        buttons.Children.Add(_confirmOK);

        Border dialog = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(p.Border),
            BorderThickness = Layout.ConfirmBorderThickness,
            CornerRadius = Rounded(Layout.ConfirmCornerRadius),
            Padding = Layout.ConfirmPadding,
            MinWidth = Layout.ConfirmMinWidth,
            MaxWidth = Layout.ConfirmMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Children = { _confirmTitle, _confirmMessage, buttons } },
        };
        return new Border
        {
            Background =
                TrayAppDotNETSettingsUI.Brush(
                    (AppServices.Theme ?? AppTheme.Default).FlyoutOverlayBackdrop.For(isLight)),
            CornerRadius = Rounded(Layout.RootCornerRadius),
            Child = dialog,
        };
    }

    private async Task DeleteGroupAsync(FanFlyoutCell cell)
    {
        if (string.IsNullOrWhiteSpace(cell.GroupName)) return;
        bool ok = await ConfirmAsync(
            L("Flyout_DeleteGroup_Title", "Delete group"),
            string.Format(
                L("Flyout_DeleteGroup_MessageFormat", "Delete {0}? Fans in this group will be moved out of the group."),
                cell.GroupName),
            L("Flyout_DeleteGroup_Confirm", "Delete"),
            L("SettingsWindow_ConfirmOverlay_Cancel", "Cancel"));
        if (!ok) return;

        _groupNames.RemoveAll(g => string.Equals(g, cell.GroupName, StringComparison.OrdinalIgnoreCase));
        _groupSettingsByName.Remove(cell.GroupName);
        FanGroup.Unregister(cell.GroupName);
        if (_lhmService != null)
        {
            foreach (Fan fan in _lhmService.Fans)
                if (string.Equals(fan.Group, cell.GroupName, StringComparison.OrdinalIgnoreCase))
                    fan.Group = null;
        }

        SaveGroupChanges();
        RebuildCells();
        RebuildVisual();
        QueuePositionNearTray();
        _pendingFanRebuild = false;
    }

    private Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        _confirmTcs?.TrySetResult(false);
        _confirmTitle!.Text = title;
        _confirmMessage!.Text = message;
        _confirmOK!.Text = confirmText;
        _confirmCancel!.Text = cancelText;
        _confirmOverlay!.IsVisible = true;
        _confirmTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return _confirmTcs.Task;
    }

    private void CompleteConfirm(bool result)
    {
        _confirmOverlay!.IsVisible = false;
        TaskCompletionSource<bool>? tcs = _confirmTcs;
        _confirmTcs = null;
        tcs?.TrySetResult(result);
    }

    private void CancelConfirmOverlay()
    {
        _confirmOverlay?.IsVisible = false;
        TaskCompletionSource<bool>? tcs = _confirmTcs;
        _confirmTcs = null;
        tcs?.TrySetResult(false);
    }

    private void ToggleNonFunctioningFans()
    {
        if (IsControlDown())
        {
            SortFansByLHMName();
            return;
        }

        _showNonFunctioningFans = !_showNonFunctioningFans;
        _settings.ShowNonFunctioningFans = _showNonFunctioningFans;
        UpdateNonFunctioningFansButtonVisual();
        _settings.Save();
        _settings.RaiseChanged();
        OnPropertyChanged(nameof(NonFunctioningFansGlyph));
        RebuildCells();
        RebuildVisual();
        QueuePositionNearTray();
    }

    private void UpdateNonFunctioningFansButtonVisual() =>
        _nonFunctioningFansButtonGlyph?.Text = NonFunctioningFansGlyph;

    private void SortFansByLHMName()
    {
        if (_lhmService == null) return;
        List<Fan> sorted =
        [
            .. _lhmService.Fans
                .OrderBy(f => string.IsNullOrWhiteSpace(f.FansName) ? f.DataSourceKey : f.FansName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.DataSourceKey, StringComparer.OrdinalIgnoreCase)
        ];
        _suppressFanRebuild = true;
        try
        {
            for (int i = 0; i < sorted.Count; i++)
                sorted[i].FlyoutDisplayOrder = i;
        }
        finally
        {
            _suppressFanRebuild = false;
        }

        _lhmService.PersistLiveState();
        RebuildCells();
        RebuildVisual();
    }

    private void AddGroup()
    {
        string groupName = CreateUniqueGroupName();
        OffsetTopLevelDisplayOrders(1);
        _groupNames.Insert(0, groupName);
        GetOrCreateGroupSettings(groupName).DisplayOrder = 0;
        SaveGroupChanges();
        RebuildCells();
        RebuildVisual();
        QueuePositionNearTray();
    }

    private void ToggleFanMode(Fan fan)
    {
        fan.CurrentControlMode = fan.CurrentControlMode == FanControlMode.Manual
            ? FanControlMode.Curve
            : FanControlMode.Manual;
        AppServices.LHMService?.PersistLiveState();
        RebuildVisual();
    }

    private void ToggleGroupMode(FanFlyoutCell cell)
    {
        if (!cell.HasGroupHeader) return;
        cell.GroupCurrentControlMode = cell.GroupCurrentControlMode == FanControlMode.Manual
            ? FanControlMode.Curve
            : FanControlMode.Manual;
        foreach (Fan fan in cell.Fans)
            fan.CurrentControlMode = cell.GroupCurrentControlMode;
        if (cell.GroupCurrentControlMode == FanControlMode.Manual)
            ApplyGroupManualValueToFans(cell);
        SaveGroupChanges();
        RebuildVisual();
    }

    private static void ApplyGroupManualValueToFans(FanFlyoutCell cell)
    {
        foreach (Fan fan in cell.Fans)
        {
            int max = fan.RPMMode ? fan.FanSliderMaximum : 100;
            int value = Math.Clamp(cell.GroupFanDisplayedValue, 0, max);
            fan.FanDisplayedValue = value;
        }
    }

    private void SelectFanProfile(int targetIndex)
    {
        if (_lhmService == null) return;
        _lastRequestedProfile = targetIndex + 1;
        _settings.EnsureFanProfileCount(3);
        if (targetIndex < 0 || targetIndex >= _settings.FanProfiles.Count) return;
        int currentIndex = Math.Clamp(_settings.SelectedFanProfileIndex, 0, _settings.FanProfiles.Count - 1);
        if (_settings.Autosave) SaveFanProfileSlot(currentIndex);
        FanProfile target = _settings.FanProfiles[targetIndex];
        if (target.Fans.Count > 0) target.ApplyTo(_lhmService.Fans);
        else SaveFanProfileSlot(targetIndex);
        _settings.SelectedFanProfileIndex = targetIndex;
        _lhmService.PersistLiveState(save: false);
        _settings.Save();
        _settings.RaiseChanged();
        RebuildCells();
        RebuildVisual();
        _ = _lastRequestedProfile;
    }

    private void SaveFanProfileSlot(int index)
    {
        if (_lhmService == null) return;
        if (index < 0 || index >= _settings.FanProfiles.Count) return;
        string name = string.IsNullOrWhiteSpace(_settings.FanProfiles[index].Name)
            ? $"Profile {index + 1}"
            : _settings.FanProfiles[index].Name;
        _settings.FanProfiles[index] = FanProfile.FromFans(name, _lhmService.Fans);
    }

    private void RebuildCells()
    {
        if (_lhmService == null)
        {
            _cells.Clear();
            return;
        }

        List<Fan> visibleFans = [.. _lhmService.Fans.Where(IsFanVisibleInFlyout)];
        List<FanFlyoutCell> built = BuildCellsForVisualOrder(OrderFansForFlyout(visibleFans), fan => fan.Group);
        _cells.Clear();
        foreach (FanFlyoutCell cell in built)
            _cells.Add(cell);
    }

    private bool IsFanVisibleInFlyout(Fan fan) =>
        _showNonFunctioningFans || fan.CurrentState == FanState.Normal;

    private static List<Fan> OrderFansForFlyout(IEnumerable<Fan> fans) =>
    [
        .. fans
            .OrderBy(f => f.FlyoutDisplayOrder < 0 ? int.MaxValue : f.FlyoutDisplayOrder)
            .ThenBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
    ];

    private List<FanFlyoutCell> BuildCellsForVisualOrder(IEnumerable<Fan> orderedFans, Func<Fan, string?> groupSelector)
    {
        Dictionary<string, List<Fan>> groups = new(StringComparer.OrdinalIgnoreCase);
        List<(Fan Fan, int Sequence)> ungrouped = [];
        Dictionary<string, int> groupFirstSequence = new(StringComparer.OrdinalIgnoreCase);
        int sequence = 0;

        foreach (Fan fan in orderedFans)
        {
            string? group = NormalizeGroupName(groupSelector(fan));
            if (group == null)
            {
                ungrouped.Add((fan, sequence++));
                continue;
            }

            groupFirstSequence.TryAdd(group, sequence);

            if (!groups.TryGetValue(group, out List<Fan>? list))
            {
                list = [];
                groups[group] = list;
            }

            list.Add(fan);
            sequence++;
        }

        List<(FanFlyoutCell Cell, int Order, int Sequence, string Tie)> slots = [];
        for (int groupIndex = 0; groupIndex < _groupNames.Count; groupIndex++)
        {
            string groupName = _groupNames[groupIndex];
            groups.TryGetValue(groupName, out List<Fan>? fansInGroup);
            FanGroup group = GetOrCreateGroupSettings(groupName);
            int slotSequence = groupFirstSequence.GetValueOrDefault(groupName, groupIndex);
            slots.Add((new FanFlyoutCell(group, fansInGroup ?? []), NormalizeDisplayOrder(group.DisplayOrder),
                slotSequence, groupName));
            if (fansInGroup != null) groups.Remove(groupName);
        }

        foreach (KeyValuePair<string, List<Fan>> kv in groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            FanGroup group = GetOrCreateGroupSettings(kv.Key);
            int slotSequence = groupFirstSequence.TryGetValue(kv.Key, out int firstSequence)
                ? firstSequence
                : sequence++;
            slots.Add((new FanFlyoutCell(group, kv.Value), NormalizeDisplayOrder(group.DisplayOrder), slotSequence,
                kv.Key));
        }

        foreach ((Fan fan, int fanSequence) in ungrouped)
            slots.Add((new FanFlyoutCell(null, [fan]), NormalizeDisplayOrder(fan.FlyoutDisplayOrder), fanSequence,
                fan.DisplayName));

        return
        [
            .. slots
                .OrderBy(s => s.Order)
                .ThenBy(s => s.Sequence)
                .ThenBy(s => s.Tie, StringComparer.OrdinalIgnoreCase)
                .Select(s => s.Cell)
        ];
    }

    private void WireFanDrag(Control row, Fan fan)
    {
        row.PointerPressed += (_, e) =>
        {
            if (TrayAppDotNETFlyoutUI.IsInteractiveDragSource(e.Source as Visual)) return;
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            _draggedFan = fan;
            _draggedGroupCell = null;
            _dragSourceControl = row;
            _dragStart = e.GetPosition(_cellStack);
            _dragPointerOffsetY = e.GetPosition(row).Y;
            e.Pointer.Capture(row);
            e.Handled = true;
        };
        row.PointerMoved += (_, e) => UpdateFanDrag(row, e);
        row.PointerReleased += (_, e) => CompleteDrag(e.Pointer, e.GetPosition(_cellStack));
        row.PointerCaptureLost += (_, _) =>
        {
            if (!_isCompletingDrag) CancelDrag();
        };
    }

    private void WireGroupDrag(Control root, FanFlyoutCell cell)
    {
        root.PointerPressed += (_, e) =>
        {
            if (TrayAppDotNETFlyoutUI.IsInteractiveDragSource(e.Source as Visual)) return;
            if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) return;
            _draggedGroupCell = cell;
            _draggedFan = null;
            _dragSourceControl = root;
            _dragStart = e.GetPosition(_cellStack);
            _dragPointerOffsetY = e.GetPosition(root).Y;
            e.Pointer.Capture(root);
            e.Handled = true;
        };
        root.PointerMoved += (_, e) => UpdateGroupDrag(root, e);
        root.PointerReleased += (_, e) => CompleteDrag(e.Pointer, e.GetPosition(_cellStack));
        root.PointerCaptureLost += (_, _) =>
        {
            if (!_isCompletingDrag) CancelDrag();
        };
    }

    private void UpdateFanDrag(Control source, PointerEventArgs e)
    {
        if (_draggedFan == null || _cellStack == null) return;
        Point current = e.GetPosition(_cellStack);
        if (_dragGhost == null && Math.Abs(current.Y - _dragStart.Y) < Layout.DragThreshold) return;
        EnsureDragGhost(source, current);
        UpdateDragPreview(current);
        e.Handled = true;
    }

    private void UpdateGroupDrag(Control source, PointerEventArgs e)
    {
        if (_draggedGroupCell == null || _cellStack == null) return;
        Point current = e.GetPosition(_cellStack);
        if (_dragGhost == null && Math.Abs(current.Y - _dragStart.Y) < Layout.DragThreshold) return;
        EnsureDragGhost(source, current);
        UpdateDragPreview(current);
        e.Handled = true;
    }

    private void EnsureDragGhost(Control source, Point current)
    {
        if (_dragOverlay == null || _cellStack == null || _dragGhost != null) return;
        SnapshotDragSlots();
        ResolveDragSourceLayout(source);

        Control ghostSource = _dragSourceTopLevelControl ?? source;
        Point? sourceTop = ghostSource.TranslatePoint(new Point(0, 0), _dragOverlay);
        Point? sourceTopInStack = ghostSource.TranslatePoint(new Point(0, 0), _cellStack);
        if (sourceTopInStack != null)
            _dragPointerOffsetY = current.Y - sourceTopInStack.Value.Y;

        Control ghostContent = CloneDragVisual(ghostSource);
        _dragGhostHeight = Math.Max(1, ghostSource.Bounds.Height);
        _dragGhost = new Border
        {
            Width = Math.Max(1, ghostSource.Bounds.Width),
            Height = _dragGhostHeight,
            Opacity = Layout.DragGhostOpacity,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetY = Layout.DragGhostShadowOffsetY,
                Blur = Layout.DragGhostShadowBlur,
                Color = Color.FromArgb(0x99, 0, 0, 0),
            }),
            Child = ghostContent,
            IsHitTestVisible = false,
        };
        _dropMarker = new Border
        {
            Height = Layout.DropMarkerHeight,
            CornerRadius = Rounded(Layout.DropMarkerCornerRadius),
            Background = TrayAppDotNETFlyoutUI.Brush(CreateFlyoutPalette(AppServices.Theme ?? AppTheme.Default,
                Palette(), AppTheme.ResolveEffectiveIsLightTheme(_settings)).SliderProgress),
            IsHitTestVisible = false,
        };
        _dragOverlay.Children.Add(_dropMarker);
        _dragOverlay.Children.Add(_dragGhost);
        SetDragSourceOpacity(Layout.DragSourceOpacity);
        Canvas.SetLeft(_dragGhost, sourceTop?.X ?? 0);
        UpdateDragPreview(current);
    }

    private Control CloneDragVisual(Control source)
    {
        if (_draggedFan != null)
        {
            if (_dragSourceCell is { HasGroupHeader: false })
                return BuildCell(_dragSourceCell,
                    CreateFlyoutPalette(AppServices.Theme ?? AppTheme.Default, Palette(),
                        AppTheme.ResolveEffectiveIsLightTheme(_settings)), AppServices.Theme ?? AppTheme.Default,
                    AppTheme.ResolveEffectiveIsLightTheme(_settings), interactive: false);

            return BuildFanRow(_draggedFan, new FanFlyoutCell(null, [_draggedFan]),
                CreateFlyoutPalette(AppServices.Theme ?? AppTheme.Default, Palette(),
                    AppTheme.ResolveEffectiveIsLightTheme(_settings)), grouped: false, interactive: false);
        }

        return _draggedGroupCell != null
            ? BuildCell(_draggedGroupCell
                , CreateFlyoutPalette(
                    AppServices.Theme ?? AppTheme.Default
                    , Palette()
                    , AppTheme.ResolveEffectiveIsLightTheme(_settings))
                , AppServices.Theme ?? AppTheme.Default
                , AppTheme.ResolveEffectiveIsLightTheme(_settings)
                , interactive: false)
            : new Border { Width = source.Bounds.Width, Height = source.Bounds.Height };
    }

    private void SnapshotDragSlots()
    {
        _dragSlots.Clear();
        if (_cellStack == null) return;

        List<(FanFlyoutCell Cell, Control Visual, double Top, double Height)> snapshot = [];
        int count = Math.Min(_cells.Count, _cellStack.Children.Count);
        for (int i = 0; i < count; i++)
        {
            if (_cellStack.Children[i] is not Control visual) continue;
            Point? top = visual.TranslatePoint(new Point(0, 0), _cellStack);
            if (top == null) continue;
            snapshot.Add((_cells[i], visual, top.Value.Y, Math.Max(1, visual.Bounds.Height)));
        }

        for (int i = 0; i < snapshot.Count; i++)
        {
            double slotHeight = i < snapshot.Count - 1
                ? Math.Max(snapshot[i].Height, snapshot[i + 1].Top - snapshot[i].Top)
                : snapshot[i].Height + Math.Max(0, snapshot[i].Visual.Margin.Bottom);
            _dragSlots.Add(new FanDragSlot(snapshot[i].Cell, snapshot[i].Visual, snapshot[i].Top, snapshot[i].Height,
                slotHeight));
        }
    }

    private void ResolveDragSourceLayout(Control source)
    {
        _dragSourceCell = _draggedGroupCell ?? (_draggedFan == null ? null : FindCellContainingFan(_draggedFan));
        _dragSourceTopLevelIndex = -1;
        _dragSourceTopLevelControl = null;

        if (_dragSourceCell != null)
        {
            _dragSourceTopLevelIndex = IndexOfDragSlot(_dragSourceCell);
            if (_dragSourceTopLevelIndex >= 0)
            {
                bool wholeCardDrag = _draggedGroupCell != null
                                     || (_draggedFan != null && !_dragSourceCell.HasGroupHeader);
                if (wholeCardDrag)
                    _dragSourceTopLevelControl = _dragSlots[_dragSourceTopLevelIndex].Visual;
            }
        }

        _dragSourceSlotHeight = _dragSourceTopLevelIndex >= 0 && _dragSourceTopLevelControl != null
            ? _dragSlots[_dragSourceTopLevelIndex].SlotHeight
            : Math.Max(1, source.Bounds.Height + Math.Max(0, source.Margin.Bottom));
    }

    private FanFlyoutCell? FindCellContainingFan(Fan fan)
    {
        foreach (FanFlyoutCell cell in _cells)
        {
            if (cell.Fans.Any(candidate => ReferenceEquals(candidate, fan)))
                return cell;
        }

        return null;
    }

    private int IndexOfDragSlot(FanFlyoutCell cell)
    {
        for (int i = 0; i < _dragSlots.Count; i++)
        {
            FanFlyoutCell candidate = _dragSlots[i].Cell;
            if (ReferenceEquals(candidate, cell) || candidate.GroupSettings != null
                && cell.GroupSettings != null
                && ReferenceEquals(candidate.GroupSettings, cell.GroupSettings) || !candidate.HasGroupHeader
                && !cell.HasGroupHeader
                && candidate.Fans.Count == 1
                && cell.Fans.Count == 1
                && ReferenceEquals(candidate.Fans[0], cell.Fans[0]))
                return i;
        }

        return -1;
    }

    private void SetDragSourceOpacity(double opacity)
    {
        if (_dragSourceTopLevelControl != null) _dragSourceTopLevelControl.Opacity = opacity;
        else
        {
            _dragSourceControl?.Opacity = opacity;
        }
    }

    private void MoveDragGhost(Point current)
    {
        if (_dragGhost == null || _cellStack == null || _dragOverlay == null) return;
        Point overlayPoint = _cellStack.TranslatePoint(current, _dragOverlay) ?? current;
        Canvas.SetTop(_dragGhost, overlayPoint.Y - _dragPointerOffsetY);
    }

    private void UpdateDragPreview(Point current)
    {
        if (_dragGhost == null || _cellStack == null) return;
        MoveDragGhost(current);

        double ghostTop = current.Y - _dragPointerOffsetY;
        double ghostMidpoint = ghostTop + _dragGhostHeight / 2.0;
        FanDragPlacement placement = CalculateDragPlacement(ghostMidpoint);
        if (!placement.Equals(_dragPlacement))
        {
            _dragPlacement = placement;
            ApplyDragPreview(placement);
        }
    }

    private FanDragPlacement CalculateDragPlacement(double draggedMidpointY)
    {
        if (_dragSlots.Count == 0) return FanDragPlacement.TopLevel(0);

        if (_draggedFan != null)
        {
            foreach (FanDragSlot slot in _dragSlots)
            {
                if (!slot.Cell.HasGroupHeader) continue;
                if (draggedMidpointY >= slot.Top && draggedMidpointY <= slot.Top + slot.Height)
                    return FanDragPlacement.IntoGroup(slot.Cell);
            }
        }

        return FanDragPlacement.TopLevel(CalculateTopLevelInsertionIndex(draggedMidpointY));
    }

    private int CalculateTopLevelInsertionIndex(double draggedMidpointY)
    {
        int insertion = 0;
        for (int i = 0; i < _dragSlots.Count; i++)
        {
            if (i == _dragSourceTopLevelIndex) continue;
            FanDragSlot slot = _dragSlots[i];
            if (draggedMidpointY > slot.Top + slot.Height / 2.0) insertion++;
            else break;
        }

        int max = _dragSlots.Count - (_dragSourceTopLevelIndex >= 0 ? 1 : 0);
        return Math.Clamp(insertion, 0, max);
    }

    private void ApplyDragPreview(FanDragPlacement placement)
    {
        ResetDragPreviewTransforms();

        if (placement is { Kind: FanDragPlacementKind.IntoGroup, GroupCell: not null })
            ApplyIntoGroupPreview(placement.GroupCell);
        else
            ApplyTopLevelPreview(placement.TopLevelIndex);

        PositionDropMarker(placement);
    }

    private void ApplyTopLevelPreview(int targetIndex)
    {
        if (_dragSlots.Count == 0) return;
        double offset = Math.Max(1, _dragSourceSlotHeight);

        if (_dragSourceTopLevelIndex >= 0 && _dragSourceTopLevelControl != null)
        {
            int sourceIndex = _dragSourceTopLevelIndex;
            int target = Math.Clamp(targetIndex, 0, _dragSlots.Count - 1);
            if (target < sourceIndex)
            {
                for (int i = target; i < sourceIndex; i++)
                    SetDragSlotOffset(i, offset);
            }
            else if (target > sourceIndex)
            {
                for (int i = sourceIndex + 1; i <= target && i < _dragSlots.Count; i++)
                    SetDragSlotOffset(i, -offset);
            }

            return;
        }

        int insertion = Math.Clamp(targetIndex, 0, _dragSlots.Count);
        for (int i = insertion; i < _dragSlots.Count; i++)
            SetDragSlotOffset(i, offset);
    }

    private void ApplyIntoGroupPreview(FanFlyoutCell groupCell)
    {
        int groupIndex = IndexOfDragSlot(groupCell);
        if (groupIndex < 0) return;

        double offset = Math.Max(1, _dragSourceSlotHeight);
        if (_dragSourceTopLevelIndex >= 0 && _dragSourceTopLevelControl != null)
        {
            int sourceIndex = _dragSourceTopLevelIndex;
            if (sourceIndex < groupIndex)
            {
                for (int i = sourceIndex + 1; i <= groupIndex; i++)
                    SetDragSlotOffset(i, -offset);
            }
            else if (sourceIndex > groupIndex)
            {
                for (int i = groupIndex + 1; i < sourceIndex; i++)
                    SetDragSlotOffset(i, offset);
            }

            return;
        }

        if (!ReferenceEquals(_dragSourceCell?.GroupSettings, groupCell.GroupSettings))
            for (int i = groupIndex + 1; i < _dragSlots.Count; i++)
                SetDragSlotOffset(i, offset);
    }

    private void SetDragSlotOffset(int index, double offset)
    {
        if (index < 0 || index >= _dragSlots.Count) return;
        _dragSlots[index].Visual.RenderTransform = offset == 0
            ? null
            : new TranslateTransform(0, offset);
    }

    private void ResetDragPreviewTransforms()
    {
        foreach (FanDragSlot slot in _dragSlots)
            slot.Visual.RenderTransform = null;
    }

    private void PositionDropMarker(FanDragPlacement placement)
    {
        if (_dropMarker == null || _cellStack == null || _dragOverlay == null)
            return;

        _dropMarker.Width = Math.Max(1, _cellStack.Bounds.Width);
        double y = placement is { Kind: FanDragPlacementKind.IntoGroup, GroupCell: not null }
            ? GroupDropMarkerY(placement.GroupCell)
            : TopLevelDropMarkerY(placement.TopLevelIndex);

        Point overlayPoint = _cellStack.TranslatePoint(new Point(0, y), _dragOverlay) ?? new Point(0, y);
        Canvas.SetLeft(_dropMarker, overlayPoint.X);
        Canvas.SetTop(_dropMarker, overlayPoint.Y - _dropMarker.Height / 2.0);
    }

    private double GroupDropMarkerY(FanFlyoutCell groupCell)
    {
        int index = IndexOfDragSlot(groupCell);
        if (index < 0 || index >= _dragSlots.Count) return 0;

        FanDragSlot slot = _dragSlots[index];
        return slot.Top + CurrentPreviewOffset(slot.Visual) + slot.Height;
    }

    private double TopLevelDropMarkerY(int targetIndex)
    {
        if (_dragSlots.Count == 0) return 0;

        if (_dragSourceTopLevelIndex >= 0 && _dragSourceTopLevelControl != null)
        {
            int sourceIndex = _dragSourceTopLevelIndex;
            int target = Math.Clamp(targetIndex, 0, _dragSlots.Count - 1);
            if (target <= sourceIndex)
            {
                FanDragSlot slot = _dragSlots[target];
                return slot.Top + CurrentPreviewOffset(slot.Visual);
            }

            FanDragSlot targetSlot = _dragSlots[target];
            return targetSlot.Top + CurrentPreviewOffset(targetSlot.Visual) + targetSlot.Height;
        }

        int insertion = Math.Clamp(targetIndex, 0, _dragSlots.Count);
        if (insertion >= _dragSlots.Count)
        {
            FanDragSlot last = _dragSlots[^1];
            return last.Top + CurrentPreviewOffset(last.Visual) + last.Height;
        }

        FanDragSlot slotAtInsertion = _dragSlots[insertion];
        return slotAtInsertion.Top + CurrentPreviewOffset(slotAtInsertion.Visual);
    }

    private static double CurrentPreviewOffset(Control visual) =>
        visual.RenderTransform is TranslateTransform translate ? translate.Y : 0;

    private void CompleteDrag(IPointer pointer, Point current)
    {
        if (_dragGhost == null)
        {
            ClearDragState();
            pointer.Capture(null);
            return;
        }

        _isCompletingDrag = true;
        try
        {
            if (_dragPlacement.Kind == FanDragPlacementKind.None)
            {
                double ghostTop = current.Y - _dragPointerOffsetY;
                _dragPlacement = CalculateDragPlacement(ghostTop + _dragGhostHeight / 2.0);
            }

            if (_draggedFan != null) ApplyFanDrop(_draggedFan, _dragPlacement);
            else if (_draggedGroupCell != null) ApplyGroupDrop(_draggedGroupCell, _dragPlacement.TopLevelIndex);
            pointer.Capture(null);
        }
        finally
        {
            _isCompletingDrag = false;
            ClearDragState();
        }

        RebuildCells();
        RebuildVisual();
        QueuePositionNearTray();
    }

    private void CancelDrag() => ClearDragState();

    private void ClearDragState()
    {
        _dragSourceControl?.Opacity = 1;
        _dragSourceTopLevelControl?.Opacity = 1;
        ResetDragPreviewTransforms();
        if (_dragOverlay != null)
        {
            if (_dragGhost != null) _dragOverlay.Children.Remove(_dragGhost);
            if (_dropMarker != null) _dragOverlay.Children.Remove(_dropMarker);
        }

        _dragGhost = null;
        _dropMarker = null;
        _dragSlots.Clear();
        _dragSourceControl = null;
        _dragSourceTopLevelControl = null;
        _draggedFan = null;
        _draggedGroupCell = null;
        _dragSourceCell = null;
        _dragPlacement = FanDragPlacement.None;
        _dragSourceTopLevelIndex = -1;
        _dragSourceSlotHeight = 0;
        _dragGhostHeight = 0;
    }

    private void ApplyFanDrop(Fan fan, FanDragPlacement placement)
    {
        FanFlyoutCell? groupCell = placement.Kind == FanDragPlacementKind.IntoGroup ? placement.GroupCell : null;
        if (groupCell?.GroupName != null)
        {
            if (ReferenceEquals(_dragSourceCell?.GroupSettings, groupCell.GroupSettings))
                return;

            fan.Group = groupCell.GroupName;
            fan.FlyoutDisplayOrder = groupCell.Fans.Count;
            if (groupCell.GroupSettings != null)
            {
                fan.CurrentControlMode = groupCell.GroupSettings.CurrentControlMode;
                if (fan.CurrentControlMode == FanControlMode.Manual)
                    fan.FanDisplayedValue =
                        Math.Clamp(groupCell.GroupSettings.FanDisplayedValue, 0, fan.FanSliderMaximum);
            }
        }
        else
        {
            fan.Group = null;
        }

        List<FanFlyoutCell> ordered = [.. _cells];
        int targetIndex = placement.Kind == FanDragPlacementKind.TopLevel ? placement.TopLevelIndex : _cells.Count;
        List<FanFlyoutCell> arranged = MoveFanAsTopLevel(ordered, fan, targetIndex);
        ApplyTopLevelDisplayOrder(arranged);
        ApplyGroupedFanDisplayOrders(arranged);
        SaveGroupChanges();
    }

    private static List<FanFlyoutCell> MoveFanAsTopLevel(List<FanFlyoutCell> cells, Fan fan, int targetIndex)
    {
        List<FanFlyoutCell> result = [];
        foreach (FanFlyoutCell cell in cells)
        {
            if (cell.Fans.Contains(fan))
            {
                if (cell.HasGroupHeader)
                {
                    List<Fan> remaining = [.. cell.Fans.Where(f => !ReferenceEquals(f, fan))];
                    result.Add(new FanFlyoutCell(cell.GroupSettings, remaining));
                }

                continue;
            }

            result.Add(cell);
        }

        if (fan.Group == null)
            result.Insert(Math.Clamp(targetIndex, 0, result.Count), new FanFlyoutCell(null, [fan]));
        return result;
    }

    private void ApplyGroupDrop(FanFlyoutCell cell, int targetIndex)
    {
        List<FanFlyoutCell> ordered = [.. _cells.Where(c => !ReferenceEquals(c.GroupSettings, cell.GroupSettings))];
        ordered.Insert(Math.Clamp(targetIndex, 0, ordered.Count), cell);
        ApplyTopLevelDisplayOrder(ordered);
        SaveGroupChanges();
    }

    private void ApplyTopLevelDisplayOrder(IEnumerable<FanFlyoutCell> orderedCells)
    {
        _suppressFanRebuild = true;
        try
        {
            _groupNames.Clear();
            int index = 0;
            foreach (FanFlyoutCell cell in orderedCells)
            {
                if (cell.HasGroupHeader)
                {
                    if (cell.GroupName == null) continue;
                    FanGroup group = GetOrCreateGroupSettings(cell.GroupName);
                    group.DisplayOrder = index++;
                    if (!_groupNames.Any(g => string.Equals(g, cell.GroupName, StringComparison.OrdinalIgnoreCase)))
                        _groupNames.Add(cell.GroupName);
                    continue;
                }

                if (cell.Fans.Count == 1)
                    cell.Fans[0].FlyoutDisplayOrder = index++;
            }
        }
        finally
        {
            _suppressFanRebuild = false;
        }
    }

    private static void ApplyGroupedFanDisplayOrders(IEnumerable<FanFlyoutCell> orderedCells)
    {
        foreach (FanFlyoutCell cell in orderedCells)
        {
            if (!cell.HasGroupHeader) continue;
            for (int i = 0; i < cell.Fans.Count; i++)
                cell.Fans[i].FlyoutDisplayOrder = i;
        }
    }

    private void FanNameTextPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not TextBlock text || text.Tag is not Fan fan) return;
        if (text.Parent is not Grid grid) return;
        TextBox? box = grid.Children.OfType<TextBox>().FirstOrDefault();
        if (box == null) return;
        box.Tag = fan;
        box.Text = fan.DisplayName;
        text.IsVisible = false;
        box.IsVisible = true;
        box.Focus();
        box.SelectAll();
        e.Handled = true;
    }

    private void GroupNameTextPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not TextBlock text || text.Tag is not FanFlyoutCell cell) return;
        if (text.Parent is not Grid grid) return;
        TextBox? box = grid.Children.OfType<TextBox>().FirstOrDefault();
        if (box == null) return;
        box.Tag = cell;
        box.Text = cell.GroupName;
        text.IsVisible = false;
        box.IsVisible = true;
        box.Focus();
        box.SelectAll();
        e.Handled = true;
    }

    private void FanValueTextPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not Visual visual) return;
        if (sender is not Control { Tag: Fan fan }) return;
        Grid? grid = FindVisualAncestor<Grid>(visual);
        TextBox? box = grid?.Children.OfType<TextBox>().FirstOrDefault();
        if (box == null) return;
        box.Tag = fan;
        box.Text = fan.FanDisplayedValueText;
        if (sender is Control control) control.IsVisible = false;
        box.IsVisible = true;
        box.Focus();
        box.SelectAll();
        e.Handled = true;
    }

    private void FanNameEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key == Key.Escape)
        {
            CancelInlineEdit(box);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitFanNameEdit(box);
            e.Handled = true;
        }
    }

    private void GroupNameEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key == Key.Escape)
        {
            CancelInlineEdit(box);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitGroupNameEdit(box);
            e.Handled = true;
        }
    }

    private void FanValueEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key == Key.Escape)
        {
            CancelInlineEdit(box);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitFanValueEdit(box);
            e.Handled = true;
        }
    }

    private void FanNameEditLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box) CommitFanNameEdit(box);
    }

    private void GroupNameEditLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box) CommitGroupNameEdit(box);
    }

    private void FanValueEditLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box) CommitFanValueEdit(box);
    }

    private void CommitFanNameEdit(TextBox box)
    {
        if (box.Tag is Fan fan)
        {
            string trimmed = (box.Text ?? string.Empty).Trim();
            fan.UserDefinedName =
                string.Equals(trimmed, fan.FansName, StringComparison.Ordinal) ? string.Empty : trimmed;
            AppServices.LHMService?.PersistLiveState();
        }

        CancelInlineEdit(box);
        RebuildCells();
        RebuildVisual();
    }

    private void CommitGroupNameEdit(TextBox box)
    {
        if (box.Tag is FanFlyoutCell cell && !string.IsNullOrWhiteSpace(cell.GroupName))
        {
            string trimmed = (box.Text ?? string.Empty).Trim();
            if (trimmed.Length > 0 && !string.Equals(trimmed, cell.GroupName, StringComparison.Ordinal))
                RenameGroup(cell.GroupName, CreateUniqueGroupName(trimmed, cell.GroupName));
        }

        CancelInlineEdit(box);
    }

    private void CommitFanValueEdit(TextBox box)
    {
        if (box.Tag is Fan fan)
        {
            if (TryParseFanDisplayedValue(box.Text, fan, out int value))
            {
                fan.FanDisplayedValue = value;
                AppServices.LHMService?.PersistLiveState();
            }
        }

        CancelInlineEdit(box);
        RebuildVisual();
    }

    private static bool TryParseFanDisplayedValue(string? text, Fan fan, out int value)
    {
        string normalized = (text ?? string.Empty).Trim().Replace("%", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("RPM", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            value = Math.Clamp(parsed, 0, fan.FanSliderMaximum);
            return true;
        }

        value = fan.FanDisplayedValue;
        return false;
    }

    private static void CancelInlineEdit(TextBox box)
    {
        box.Tag = null;
        box.IsVisible = false;
        if (box.Parent is Grid grid)
            foreach (Control control in grid.Children)
                if (control is TextBlock or Viewbox)
                    control.IsVisible = true;
    }

    private static T? FindVisualAncestor<T>(Visual visual)
        where T : Visual
    {
        for (Visual? current = visual; current != null; current = current.GetVisualParent())
            if (current is T match)
                return match;
        return null;
    }

    private void RenameGroup(string oldName, string newName)
    {
        int index = _groupNames.FindIndex(g => string.Equals(g, oldName, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _groupNames[index] = newName;
        else _groupNames.Insert(0, newName);
        if (_groupSettingsByName.Remove(oldName, out FanGroup? group))
        {
            group.Name = newName;
            _groupSettingsByName[newName] = group;
        }
        else
        {
            GetOrCreateGroupSettings(newName);
        }

        if (_lhmService != null)
            foreach (Fan fan in _lhmService.Fans)
                if (string.Equals(fan.Group, oldName, StringComparison.OrdinalIgnoreCase))
                    fan.Group = newName;
        SaveGroupChanges();
        RebuildCells();
        RebuildVisual();
    }

    private void ToggleFanPropertiesWindow(Fan fan)
    {
        if (_fanPropertiesWindows.TryGetValue(fan, out FanPropertiesWindow? existing))
        {
            if (!existing.IsVisible)
            {
                existing.Show();
                PositionFanPropertiesWindows();
                existing.Activate();
                return;
            }

            if (!existing.RequestClose())
                existing.Activate();
            return;
        }

        FanPropertiesWindow window = new(fan, _settings);
        window.Closed += FanPropertiesWindowClosed;
        _fanPropertiesWindows[fan] = window;
        _fanPropertiesOrder.Add(fan);
        window.Show(this);
        PositionFanPropertiesWindows();
        window.Activate();
    }

    private void FanPropertiesWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not FanPropertiesWindow window) return;
        window.Closed -= FanPropertiesWindowClosed;
        Fan? fanToRemove = _fanPropertiesWindows.FirstOrDefault(kv => ReferenceEquals(kv.Value, window)).Key;
        if (fanToRemove != null)
        {
            _fanPropertiesWindows.Remove(fanToRemove);
            _fanPropertiesOrder.Remove(fanToRemove);
        }

        PositionFanPropertiesWindows();
        NotifyChildWindowClosedFromDeactivation();
    }

    private void PositionFanPropertiesWindows()
    {
        if (!IsVisible) return;
        List<FanPropertiesWindow> visible = [];
        foreach (Fan fan in _fanPropertiesOrder)
            if (_fanPropertiesWindows.TryGetValue(fan, out FanPropertiesWindow? window) && window.IsVisible)
                visible.Add(window);
        if (visible.Count == 0) return;
        double flyoutHeight = Bounds.Height > 0 ? Bounds.Height : 300;
        int rows = Math.Min(Layout.FanPropertiesRowsPerColumn, visible.Count);
        double windowHeight = visible[0].Height;
        double stackHeight = rows * windowHeight + Math.Max(0, rows - 1) * Layout.FanPropertiesGap;
        double stackTop = Math.Min(Position.Y, Position.Y + flyoutHeight - stackHeight);
        for (int i = 0; i < visible.Count; i++)
        {
            FanPropertiesWindow window = visible[i];
            int column = i / Layout.FanPropertiesRowsPerColumn;
            int row = i % Layout.FanPropertiesRowsPerColumn;
            double windowWidth = window.Width;
            window.Position = new PixelPoint(
                Position.X - (int)Math.Round(Layout.FanPropertiesGap) - (column + 1) * (int)Math.Round(windowWidth) -
                column * (int)Math.Round(Layout.FanPropertiesGap),
                (int)Math.Round(stackTop + row * (windowHeight + Layout.FanPropertiesGap)));
        }
    }

    private void ShowPinnedFanPropertiesWindows()
    {
        bool showedAny = false;
        foreach (Fan fan in _fanPropertiesOrder.ToArray())
        {
            if (!_fanPropertiesWindows.TryGetValue(fan, out FanPropertiesWindow? window)) continue;
            if (!window.IsPinned) continue;
            if (!window.IsVisible)
            {
                window.Show();
                showedAny = true;
            }
        }

        if (showedAny) PositionFanPropertiesWindows();
    }

    private void HideFanPropertiesWindowsForFlyoutHide()
    {
        foreach (FanPropertiesWindow window in _fanPropertiesWindows.Values.ToArray())
        {
            if (window.IsPinned) window.Hide();
            else window.ForceClose();
        }
    }

    private void ForceCloseAllFanPropertiesWindows()
    {
        foreach (FanPropertiesWindow window in _fanPropertiesWindows.Values.ToArray())
            window.ForceClose();
        _fanPropertiesWindows.Clear();
        _fanPropertiesOrder.Clear();
    }

    private void OnFansChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        WireFanPropertySubscriptions();
        RebuildCells();
        RebuildVisual();
        QueuePositionNearTray();
    }

    private void WireFanPropertySubscriptions()
    {
        if (_lhmService == null) return;
        HashSet<Fan> liveFans = [.. _lhmService.Fans];
        foreach (Fan fan in _subscribedFans.ToArray())
        {
            if (liveFans.Contains(fan)) continue;
            fan.PropertyChanged -= OnFanPropertyChanged;
            _subscribedFans.Remove(fan);
        }

        foreach (Fan fan in liveFans)
        {
            if (!_subscribedFans.Add(fan)) continue;
            fan.PropertyChanged += OnFanPropertyChanged;
        }
    }

    private void OnFanPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressFanRebuild) return;

        if (sender is Fan fan
            && e.PropertyName is nameof(Fan.CurrentRPM)
                or nameof(Fan.CurrentDutyCycle)
                or nameof(Fan.FanDisplayedValue)
                or nameof(Fan.FanDisplayedValueText)
                or nameof(Fan.FanSliderMaximum)
                or nameof(Fan.CurrentControlMode))
        {
            Dispatcher.UIThread.Post(() => RefreshFanRowVisuals(fan, e.PropertyName));
            return;
        }

        if (e.PropertyName is not nameof(Fan.Group)
            and not nameof(Fan.CurrentState)
            and not nameof(Fan.DisplayName)
            and not nameof(Fan.FlyoutDisplayOrder)
            and not nameof(Fan.RPMMode)
            and not nameof(Fan.AssignedCurveDisplayLabel)) return;
        RequestFanRebuild();
    }

    private void OnSettingsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (IsPointerGestureActive)
        {
            _pendingFanRebuild = true;
            return;
        }

        if (_isUndocked && !_settings.AllowFlyoutUndock)
        {
            Redock();
            return;
        }

        _showNonFunctioningFans = _settings.ShowNonFunctioningFans;
        UpdateNonFunctioningFansButtonVisual();
        RebuildCells();
        RebuildVisual();
    });

    private bool IsPointerGestureActive =>
        _activeSliderDrags > 0
        || _draggedFan != null
        || _draggedGroupCell != null
        || _isDraggingWindow
        || _undockButtonPointerCaptured;

    private void RequestFanRebuild() => Dispatcher.UIThread.Post(() =>
    {
        if (IsPointerGestureActive)
        {
            _pendingFanRebuild = true;
            return;
        }

        RebuildCells();
        RebuildVisual();
        QueuePositionNearTray();
    });

    private void FlushPendingFanRebuild()
    {
        if (!_pendingFanRebuild || IsPointerGestureActive) return;
        _pendingFanRebuild = false;
        RebuildCells();
        RebuildVisual();
        QueuePositionNearTray();
    }

    private void LoadGroupCatalog()
    {
        _groupNames.Clear();
        _groupSettingsByName.Clear();
        foreach (FanGroup group in _settings.FanGroups
                     .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                     .OrderBy(g => g.DisplayOrder)
                     .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            string groupName = group.Name!;
            if (_groupNames.Any(existing =>
                    string.Equals(existing, groupName, StringComparison.OrdinalIgnoreCase))) continue;
            _groupSettingsByName[groupName] = group;
            FanGroup.Register(group);
            _groupNames.Add(groupName);
        }
    }

    private FanGroup GetOrCreateGroupSettings(string groupName)
    {
        if (_groupSettingsByName.TryGetValue(groupName, out FanGroup? group)) return group;
        if (FanGroup.Find(groupName) is { } registeredGroup)
        {
            _groupSettingsByName[groupName] = registeredGroup;
            return registeredGroup;
        }

        group = new FanGroup
        {
            Name = groupName,
            DisplayOrder =
                _groupNames.FindIndex(g => string.Equals(g, groupName, StringComparison.OrdinalIgnoreCase)),
        };
        if (group.DisplayOrder < 0) group.DisplayOrder = _groupSettingsByName.Count;
        _groupSettingsByName[groupName] = group;
        FanGroup.Register(group);
        return group;
    }

    private string CreateUniqueGroupName(string baseName = NewGroupBaseName, string? ignoredName = null)
    {
        string normalizedBase = string.IsNullOrWhiteSpace(baseName) ? NewGroupBaseName : baseName.Trim();
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        foreach (string groupName in _groupNames)
        {
            if (!string.IsNullOrWhiteSpace(ignoredName) &&
                string.Equals(groupName, ignoredName, StringComparison.OrdinalIgnoreCase)) continue;
            used.Add(groupName);
        }

        if (_lhmService != null)
            foreach (Fan fan in _lhmService.Fans)
            {
                if (string.IsNullOrWhiteSpace(fan.Group)) continue;
                if (!string.IsNullOrWhiteSpace(ignoredName) &&
                    string.Equals(fan.Group, ignoredName, StringComparison.OrdinalIgnoreCase)) continue;
                used.Add(fan.Group);
            }

        if (!used.Contains(normalizedBase)) return normalizedBase;
        int suffix = 2;
        while (used.Contains($"{normalizedBase}_{suffix}")) suffix++;
        return $"{normalizedBase}_{suffix}";
    }

    private void SaveGroupChanges()
    {
        _settings.FanGroups =
        [
            .. _groupNames.Select(name =>
            {
                FanGroup group = GetOrCreateGroupSettings(name);
                group.Name = name;
                FanGroup.Register(group);
                return group;
            }).OrderBy(g => g.DisplayOrder).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
        ];
        AppServices.LHMService?.PersistLiveState(save: false);
        _settings.Save();
        _settings.RaiseChanged();
    }

    private void OffsetTopLevelDisplayOrders(int offset)
    {
        if (offset == 0) return;
        _suppressFanRebuild = true;
        try
        {
            foreach (string groupName in _groupNames)
            {
                FanGroup group = GetOrCreateGroupSettings(groupName);
                if (group.DisplayOrder >= 0) group.DisplayOrder += offset;
            }

            if (_lhmService == null) return;
            foreach (Fan fan in _lhmService.Fans)
                if (string.IsNullOrWhiteSpace(fan.Group) && fan.FlyoutDisplayOrder >= 0)
                    fan.FlyoutDisplayOrder += offset;
        }
        finally
        {
            _suppressFanRebuild = false;
        }
    }

    private static int NormalizeDisplayOrder(int displayOrder) =>
        displayOrder < 0 ? int.MaxValue : displayOrder;

    private static string? NormalizeGroupName(string? groupName) =>
        string.IsNullOrWhiteSpace(groupName) ? null : groupName;

    private void ToggleUndock()
    {
        if (_undockButtonDragOccurred)
        {
            _undockButtonDragOccurred = false;
            return;
        }

        if (_isUndocked) Redock();
        else UndockToSavedPosition();
    }

    private void UndockToSavedPosition()
    {
        _isUndocked = true;
        _settings.FlyoutUndocked = true;
        _settings.Save();
        if (_settings.FlyoutHasSavedPosition)
            Position = new PixelPoint((int)Math.Round(_settings.FlyoutLeft), (int)Math.Round(_settings.FlyoutTop));
        UpdateUndockButtonVisual();
        OnPropertyChanged(nameof(IsUndocked));
    }

    private void UpdateUndockButtonVisual()
    {
        _undockButtonGlyph?.Text = UndockButtonGlyph();
        if (_undockButton != null)
            TrayAppDotNETToolTip.SetTip(_undockButton, UndockButtonTooltip());
    }

    private string UndockButtonGlyph() =>
        _isUndocked ? GlyphCatalog.FLYOUT_REDOCK_ACTION : GlyphCatalog.FLYOUT_UNDOCK_ACTION;

    private string UndockButtonTooltip() =>
        _isUndocked ? "Redock" : "Undock";

    private void BeginUndockButtonDrag(Control source, PointerPressedEventArgs e)
    {
        (PixelPoint docked, int snap) = CaptureDockedPosition();
        PixelPoint pointer = source.PointToScreen(e.GetPosition(source));
        _dragHelper.BeginDrag(pointer, Position, docked, snap);
        _undockButtonPointerCaptured = true;
        _undockButtonDragOccurred = false;
        _isDraggingWindow = true;
        e.Pointer.Capture(source);
    }

    private void ContinueUndockButtonDrag(Control source, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
            OnPropertyChanged(nameof(IsUndocked));
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
            if (commitDrag) CommitDragPosition();
            FlushPendingFanRebuild();
            return;
        }

        if (clickWhenNotDragged) ToggleUndock();
        FlushPendingFanRebuild();
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isUndocked || _confirmOverlay?.IsVisible == true) return;
        if (_undockButtonPointerCaptured || _draggedFan != null || _draggedGroupCell != null) return;
        if (TrayAppDotNETFlyoutUI.IsInteractiveDragSource(e.Source as Visual)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (sender is not Control control) return;
        (PixelPoint docked, int snap) = CaptureDockedPosition();
        PixelPoint pointer = control.PointToScreen(e.GetPosition(control));
        _dragHelper.BeginDrag(pointer, Position, docked, snap);
        _isDraggingWindow = true;
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingWindow || _undockButtonPointerCaptured || sender is not Control control) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            EndWindowDrag(e.Pointer);
            return;
        }

        PixelPoint pointer = control.PointToScreen(e.GetPosition(control));
        PixelPoint natural = _dragHelper.ComputeNatural(pointer);
        _dragHelper.ApplyDragPosition(this, natural);
        e.Handled = true;
    }

    private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingWindow || _undockButtonPointerCaptured) return;
        EndWindowDrag(e.Pointer);
        e.Handled = true;
    }

    private void OnRootPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDraggingWindow) CommitDragPosition();
        _isDraggingWindow = false;
        FlushPendingFanRebuild();
    }

    private void EndWindowDrag(IPointer pointer)
    {
        _isDraggingWindow = false;
        pointer.Capture(null);
        CommitDragPosition();
        FlushPendingFanRebuild();
    }

    private void CommitDragPosition()
    {
        if (_dragHelper.IsCurrentlySnapped) Redock();
        else SaveCurrentFlyoutPosition();
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

    private void PositionNearTray()
    {
        Position = ResolvePosition(_lastTrayIcon);
        PositionFanPropertiesWindows();
    }

    private PixelPoint OffscreenPosition() => new(Layout.OffscreenPosition, Layout.OffscreenPosition);

    private PixelRect FallbackWorkArea() => new(
        Layout.FallbackWorkAreaX,
        Layout.FallbackWorkAreaY,
        Layout.FallbackWorkAreaWidth,
        Layout.FallbackWorkAreaHeight);

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

    private PixelPoint ResolvePosition(TrayAppDotNETShellTrayIcon? trayIcon)
    {
        if (_isUndocked && _settings.FlyoutHasSavedPosition)
            return new PixelPoint((int)Math.Round(_settings.FlyoutLeft), (int)Math.Round(_settings.FlyoutTop));
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
        return new PixelPoint(Math.Clamp(target.X, minLeft, maxLeft), Math.Clamp(target.Y, minTop, maxTop));
    }

    private int CurrentPixelWidth() =>
        Math.Max(Layout.PixelMinSize, (int)Math.Ceiling(Math.Max(Bounds.Width, Width) * RenderScaling));

    private int CurrentPixelHeight() =>
        Math.Max(Layout.PixelMinSize, (int)Math.Ceiling(Math.Max(Bounds.Height, MinHeight) * RenderScaling));

    private (PixelPoint DockedPosition, int SnapTolerance) CaptureDockedPosition()
    {
        PixelRect workArea = ResolveWorkArea(_lastTrayIcon);
        int snapTolerance = Math.Max(Layout.PixelMinSize,
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
        MaxHeight = Math.Max(Layout.WorkAreaMinHeight, workArea.Height / RenderScaling - Layout.EdgePadding * 2);
    }

    private FlyoutControlPalette CreateFlyoutPalette(AppTheme theme, SettingsPalette sp, bool isLight) =>
        new(
            theme.ResolveForeground(_settings, isLight),
            theme.SecondaryForeground.For(isLight),
            theme.ResolveFlyoutCardBorder(_settings, isLight),
            theme.ButtonHover.For(isLight),
            theme.ButtonPressed.For(isLight),
            theme.ControlBackground.For(isLight),
            theme.CardBackground.For(isLight),
            theme.IconForeground.For(isLight),
            sp.SliderTrack,
            sp.SliderProgress,
            sp.SliderThumb);

    private SettingsPalette Palette() =>
        FanSettingsWindow.CreatePalette(AppServices.Theme, _settings, AppTheme.ResolveEffectiveIsLightTheme(_settings));

    private TextBox InlineTextBox(string text, FlyoutControlPalette p) =>
        new()
        {
            Text = text,
            FontFamily = FlyoutFont,
            FontSize = Layout.InlineEditorFontSize,
            Background = TrayAppDotNETFlyoutUI.Brush(p.ControlBackground),
            Foreground = TrayAppDotNETFlyoutUI.Brush(p.Foreground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = Layout.InlineEditorBorderThickness,
            Padding = Layout.InlineEditorPadding,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

    private SliderThumbGlyphOption ResolveSliderThumbOption()
    {
        List<SliderThumbGlyphOption> options =
            _settings.SliderThumbOptions is { Count: > 0 } list
                ? list
                : SliderThumbGlyphOption.CreateDefaults();
        return options.FirstOrDefault(o => o.Name == _settings.SliderThumbGlyph) ?? options[0];
    }

    private CornerRadius Rounded(CornerRadius radius) =>
        _settings.EnableRoundedCorners ? radius : Layout.ZeroCornerRadius;

    private sealed record FlyoutLayout(
        int EdgePadding,
        double DragThreshold,
        double SnapTolerancePercent,
        double WorkAreaMinHeight,
        int PixelMinSize,
        int OffscreenPosition,
        int FallbackWorkAreaX,
        int FallbackWorkAreaY,
        int FallbackWorkAreaWidth,
        int FallbackWorkAreaHeight,
        int FanPropertiesRowsPerColumn,
        double FanPropertiesGap,
        double MaxSettingSpacing,
        Thickness ZeroThickness,
        CornerRadius ZeroCornerRadius,
        Thickness RootBorderThickness,
        CornerRadius RootCornerRadius,
        CornerRadius RootInnerCornerRadius,
        Thickness RootInnerMargin,
        double RootShadowOffsetY,
        double RootShadowBlur,
        double HeaderHeight,
        Thickness HeaderMargin,
        double HeaderWideColumnWidth,
        double HeaderNarrowColumnWidth,
        double HeaderButtonWidth,
        double HeaderButtonHeight,
        double HeaderButtonFontSize,
        double HeaderManagerButtonFontSize,
        double HeaderAddGroupIconSize,
        double HeaderAddGroupFontSize,
        double HeaderAddGlyphFontSize,
        double ProfileLabelFontSize,
        double ProfileUnderlineWidth,
        double ProfileUnderlineHeight,
        CornerRadius ProfileUnderlineCornerRadius,
        Thickness ProfileUnderlineMargin,
        double ProfileButtonWidth,
        double ProfileButtonHeight,
        double UndockFontSize,
        CornerRadius HeaderButtonCornerRadius,
        double EmptyTextFontSize,
        double EmptyTextOpacity,
        Thickness CardBorderThickness,
        CornerRadius CardCornerRadius,
        Thickness CardPadding,
        Thickness FanRowGroupedMarginBase,
        double FanButtonWidth,
        double FanButtonHeight,
        double FanButtonFontSize,
        Thickness FanButtonGroupedMargin,
        Thickness FanButtonUngroupedMargin,
        double FanNameFontSize,
        double FanSubtitleFontSize,
        Thickness SubtitleMargin,
        Thickness FanNameStackGroupedMargin,
        Thickness FanNameStackUngroupedMargin,
        Thickness TelemetryMargin,
        double RpmFontSize,
        double ControllerFontSize,
        Thickness ControllerMargin,
        double ModeButtonWidth,
        double ModeButtonHeight,
        double ModeButtonFontSize,
        Thickness ModeButtonGroupedMargin,
        Thickness ModeButtonUngroupedMargin,
        double SliderRowHeight,
        Thickness SliderRowMargin,
        Thickness ValueGridMargin,
        double ValueFontSize,
        double SliderHitTestVerticalPadding,
        double GroupIconWidth,
        double GroupIconHeight,
        double GroupIconFontSize,
        double GroupHeaderButtonWidth,
        double GroupHeaderButtonHeight,
        double GroupExpandFontSize,
        Thickness GroupHeaderButtonMargin,
        double GroupNameFontSize,
        Thickness GroupTitleMargin,
        double GroupDeleteFontSize,
        Thickness GroupModeMargin,
        Thickness GroupSliderRowMargin,
        Thickness GroupValueGridMargin,
        double ConfirmTitleFontSize,
        Thickness ConfirmTitleMargin,
        Thickness ConfirmMessageMargin,
        Thickness ConfirmCancelMargin,
        Thickness ConfirmBorderThickness,
        CornerRadius ConfirmCornerRadius,
        Thickness ConfirmPadding,
        double ConfirmMinWidth,
        double ConfirmMaxWidth,
        double DragGhostOpacity,
        double DragGhostShadowOffsetY,
        double DragGhostShadowBlur,
        double DropMarkerHeight,
        CornerRadius DropMarkerCornerRadius,
        double DragSourceOpacity,
        double InlineEditorFontSize,
        Thickness InlineEditorBorderThickness,
        Thickness InlineEditorPadding)
    {
        public Thickness CellListMargin(double top) =>
            new(ZeroThickness.Left, top, ZeroThickness.Right, ZeroThickness.Bottom);

        public Thickness CardMargin(double left, double right, double bottom) =>
            new(left, ZeroThickness.Top, right, bottom);

        public Thickness FanRowGroupedMargin(double bottom) =>
            new(FanRowGroupedMarginBase.Left, FanRowGroupedMarginBase.Top, FanRowGroupedMarginBase.Right, bottom);

        public static FlyoutLayout From(Control owner)
        {
            HotReloadResourceReader r = new(owner, "Flyout");
            return new FlyoutLayout(
                r.Int("EdgePadding"),
                r.Double("DragThreshold"),
                r.Double("SnapTolerancePercent"),
                r.Double("WorkAreaMinHeight"),
                r.Int("PixelMinSize"),
                r.Int("OffscreenPosition"),
                r.Int("FallbackWorkAreaX"),
                r.Int("FallbackWorkAreaY"),
                r.Int("FallbackWorkAreaWidth"),
                r.Int("FallbackWorkAreaHeight"),
                r.Int("FanPropertiesRowsPerColumn"),
                r.Double("FanPropertiesGap"),
                r.Double("MaxSettingSpacing"),
                r.Thickness("ZeroThickness"),
                r.CornerRadius("ZeroCornerRadius"),
                r.Thickness("RootBorderThickness"),
                r.CornerRadius("RootCornerRadius"),
                r.CornerRadius("RootInnerCornerRadius"),
                r.Thickness("RootInnerMargin"),
                r.Double("RootShadowOffsetY"),
                r.Double("RootShadowBlur"),
                r.Double("HeaderHeight"),
                r.Thickness("HeaderMargin"),
                r.Double("HeaderWideColumnWidth"),
                r.Double("HeaderNarrowColumnWidth"),
                r.Double("HeaderButtonWidth"),
                r.Double("HeaderButtonHeight"),
                r.Double("HeaderButtonFontSize"),
                r.Double("HeaderManagerButtonFontSize"),
                r.Double("HeaderAddGroupIconSize"),
                r.Double("HeaderAddGroupFontSize"),
                r.Double("HeaderAddGlyphFontSize"),
                r.Double("ProfileLabelFontSize"),
                r.Double("ProfileUnderlineWidth"),
                r.Double("ProfileUnderlineHeight"),
                r.CornerRadius("ProfileUnderlineCornerRadius"),
                r.Thickness("ProfileUnderlineMargin"),
                r.Double("ProfileButtonWidth"),
                r.Double("ProfileButtonHeight"),
                r.Double("UndockFontSize"),
                r.CornerRadius("HeaderButtonCornerRadius"),
                r.Double("EmptyTextFontSize"),
                r.Double("EmptyTextOpacity"),
                r.Thickness("CardBorderThickness"),
                r.CornerRadius("CardCornerRadius"),
                r.Thickness("CardPadding"),
                r.Thickness("FanRowGroupedMarginBase"),
                r.Double("FanButtonWidth"),
                r.Double("FanButtonHeight"),
                r.Double("FanButtonFontSize"),
                r.Thickness("FanButtonGroupedMargin"),
                r.Thickness("FanButtonUngroupedMargin"),
                r.Double("FanNameFontSize"),
                r.Double("FanSubtitleFontSize"),
                r.Thickness("SubtitleMargin"),
                r.Thickness("FanNameStackGroupedMargin"),
                r.Thickness("FanNameStackUngroupedMargin"),
                r.Thickness("TelemetryMargin"),
                r.Double("RpmFontSize"),
                r.Double("ControllerFontSize"),
                r.Thickness("ControllerMargin"),
                r.Double("ModeButtonWidth"),
                r.Double("ModeButtonHeight"),
                r.Double("ModeButtonFontSize"),
                r.Thickness("ModeButtonGroupedMargin"),
                r.Thickness("ModeButtonUngroupedMargin"),
                r.Double("SliderRowHeight"),
                r.Thickness("SliderRowMargin"),
                r.Thickness("ValueGridMargin"),
                r.Double("ValueFontSize"),
                r.Double("SliderHitTestVerticalPadding"),
                r.Double("GroupIconWidth"),
                r.Double("GroupIconHeight"),
                r.Double("GroupIconFontSize"),
                r.Double("GroupHeaderButtonWidth"),
                r.Double("GroupHeaderButtonHeight"),
                r.Double("GroupExpandFontSize"),
                r.Thickness("GroupHeaderButtonMargin"),
                r.Double("GroupNameFontSize"),
                r.Thickness("GroupTitleMargin"),
                r.Double("GroupDeleteFontSize"),
                r.Thickness("GroupModeMargin"),
                r.Thickness("GroupSliderRowMargin"),
                r.Thickness("GroupValueGridMargin"),
                r.Double("ConfirmTitleFontSize"),
                r.Thickness("ConfirmTitleMargin"),
                r.Thickness("ConfirmMessageMargin"),
                r.Thickness("ConfirmCancelMargin"),
                r.Thickness("ConfirmBorderThickness"),
                r.CornerRadius("ConfirmCornerRadius"),
                r.Thickness("ConfirmPadding"),
                r.Double("ConfirmMinWidth"),
                r.Double("ConfirmMaxWidth"),
                r.Double("DragGhostOpacity"),
                r.Double("DragGhostShadowOffsetY"),
                r.Double("DragGhostShadowBlur"),
                r.Double("DropMarkerHeight"),
                r.CornerRadius("DropMarkerCornerRadius"),
                r.Double("DragSourceOpacity"),
                r.Double("InlineEditorFontSize"),
                r.Thickness("InlineEditorBorderThickness"),
                r.Thickness("InlineEditorPadding"));
        }
    }

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

    private enum FanDragPlacementKind
    {
        None,
        TopLevel,
        IntoGroup,
    }

    private readonly record struct FanDragPlacement(
        FanDragPlacementKind Kind,
        int TopLevelIndex,
        FanFlyoutCell? GroupCell)
    {
        public static FanDragPlacement None => new(FanDragPlacementKind.None, 0, null);

        public static FanDragPlacement TopLevel(int index) =>
            new(FanDragPlacementKind.TopLevel, index, null);

        public static FanDragPlacement IntoGroup(FanFlyoutCell groupCell) =>
            new(FanDragPlacementKind.IntoGroup, 0, groupCell);
    }

    private sealed record FanDragSlot(
        FanFlyoutCell Cell,
        Control Visual,
        double Top,
        double Height,
        double SlotHeight);

    private sealed record FanRowVisualRefs(
        bool Grouped,
        TextBlock RPM,
        TextBlock Subtitle,
        TextBlock Value,
        FlyoutSlider Slider,
        TextBlock? ModeGlyph,
        Border ModeButton);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ForceCloseAllFanPropertiesWindows();
        _settings.Changed -= OnSettingsChanged;
        if (_lhmService != null)
        {
            ((INotifyCollectionChanged)_lhmService.Fans).CollectionChanged -= OnFansChanged;
            foreach (Fan fan in _subscribedFans)
                fan.PropertyChanged -= OnFanPropertyChanged;
            _subscribedFans.Clear();
        }
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
