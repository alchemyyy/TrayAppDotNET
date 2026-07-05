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
using FanControlTrayAppDotNET.UI.Curves;
using FanControlTrayAppDotNET.UI.Settings;

namespace FanControlTrayAppDotNET.UI.Flyout;

public sealed partial class FanFlyoutWindow : FlyoutWindowCommon, INotifyPropertyChanged
{
    private static readonly FontFamily FlyoutFont = new("Segoe UI");

    private const string NewGroupBaseName = "New_Fan_Group";
    private const string NewProbeCardBaseName = "New_Probe_Card";
    private static readonly bool EnableFanDragDebugOverlay = false;
    private static readonly bool EnableFanDragInstrumentation = false;

    private readonly LHMService? _lhmService;
    private readonly AppSettings _settings;
    private readonly Action<FanSettingsPage?> _openSettings;
    private readonly ObservableCollection<FanFlyoutCell> _cells = [];
    private readonly List<string> _groupNames = [];
    private readonly List<ProbeCard> _probeCards = [];
    private readonly Dictionary<string, FanGroup> _groupSettingsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Fan, FanPropertiesWindow> _fanPropertiesWindows = [];
    private readonly List<FanCurveEditorWindow> _fanCurveEditorWindows = [];
    private readonly Dictionary<ProbeCard, ProbeDataSelectorWindow> _probeSelectorWindows = [];
    private readonly List<Fan> _fanPropertiesOrder = [];
    private readonly HashSet<Fan> _subscribedFans = [];
    private readonly FlyoutWindowDragHelper _dragHelper = new();
    private readonly FanDragInstrumentation _dragInstrumentation = new(log: static message => TADNLog.Log(message));
    private readonly List<Control> _dragDebugVisuals = [];

    private TrayAppDotNETShellTrayIcon? _lastTrayIcon;
    private StackPanel? _cellStack;
    private Canvas? _dragOverlay;
    private ScrollViewer? _scrollViewer;
    private Border? _rootCard;
    private Border? _undockButton;
    private TextBlock? _undockButtonGlyph;
    private TextBlock? _nonFunctioningFansButtonGlyph;
    private readonly Dictionary<Fan, List<FanRowVisualRefs>> _fanRowRefs = [];
    private readonly Dictionary<FanFlyoutCell, GroupHeaderVisualRefs> _groupHeaderRefs = [];
    private readonly Dictionary<ProbeCard, List<ProbeCardRowVisualRefs>> _probeRowRefs = [];
    private Border? _confirmOverlay;
    private TextBlock? _confirmTitle;
    private TextBlock? _confirmMessage;
    private FlyoutLayout? _layout;
    private SettingsButton? _confirmOK;
    private SettingsButton? _confirmCancel;
    private Border? _dragGhost;
    private Control? _groupDropPreview;
    private StackPanel? _groupDropPreviewHost;
    private readonly List<FanDragSlot> _dragSlots = [];
    private readonly List<FanDragFanSlot> _dragFanSlots = [];
    private Control? _dragSourceControl;
    private Control? _dragSourceTopLevelControl;
    private Fan? _draggedFan;
    private FanFlyoutCell? _draggedGroupCell;
    private ProbeCard? _draggedProbeCard;
    private FanFlyoutCell? _dragSourceCell;
    private FanDragPlacement _dragPlacement = FanDragPlacement.None;
    internal FanDragEvaluation? LastDragEvaluation { get; private set; }
    private FanDragGhostStyle _dragGhostStyle = FanDragGhostStyle.None;
    private Point _dragStart;
    private Point? _lastDragInstrumentationPoint;
    private double _dragPointerOffsetY;
    private double _dragPointerOffsetRatio;
    private double _dragPlacementPointerOffsetY;
    private double _dragPlacementSourceHeight;
    private double _dragSourceSlotHeight;
    private double _dragSourceFanSlotHeight;
    private double _dragGhostHeight;
    private double _lastDragPointerY;
    private int _dragSourceTopLevelIndex = -1;
    private bool _isUndocked;
    private bool _showNonFunctioningFans;
    private bool _suppressFanRebuild;
    private bool _isUpdateDownloadInFlight;
    private bool _isUpdateDialogOpen;
    private bool _isDraggingWindow;
    private bool _undockButtonPointerCaptured;
    private bool _undockButtonDragOccurred;
    private bool _isCompletingDrag;
    private bool _dragMovingDown = true;
    private bool _pendingFanRebuild;
    private bool _fanRebuildQueued;
    private bool _isRebuildingVisual;
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
        LoadProbeCards();
        _settings.EnsureFanProfileCount(3);
        _settings.Changed += OnSettingsChanged;
        if (_lhmService != null)
        {
            ((INotifyCollectionChanged)_lhmService.Fans).CollectionChanged += OnFansChanged;
            _lhmService.PollTickCompleted += OnPollTickCompleted;
            WireFanPropertySubscriptions();
        }

        if (AppServices.UpdateCheckService is { } updateService)
            updateService.StateChanged += NotifyUpdateStateChanged;

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Hide();
            e.Handled = true;
        };
        if (EnableFanDragInstrumentation)
            AddHandler(KeyDownEvent, OnDragInstrumentationKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

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
        _fanPropertiesWindows.Values.Any(window => window.IsVisible)
        || _fanCurveEditorWindows.Any(window => window.IsVisible)
        || _probeSelectorWindows.Values.Any(window => window.IsVisible)
        || _isUpdateDialogOpen;

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
        ExecuteFanRebuild(reposition: false);
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

    /// <summary>Rebuilds the flyout and logs failures before they can escape the dispatcher.</summary>
    private void RebuildVisual()
    {
        try { RebuildVisualCore(); }
        catch (Exception ex)
        {
            _fanRebuildQueued = false;
            TADNLog.Log($"FanFlyoutWindow.RebuildVisual: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Builds the visual tree before replacing the existing content.</summary>
    private void RebuildVisualCore()
    {
        if (_layout == null) return;
        if (_isRebuildingVisual)
        {
            _pendingFanRebuild = true;
            return;
        }

        _isRebuildingVisual = true;
        try
        {
            bool isLight = AppTheme.ResolveEffectiveIsLightTheme(_settings);
            AppTheme theme = AppServices.Theme ?? AppTheme.Default;
            SettingsPalette sp = FanSettingsWindow.CreatePalette(theme, _settings, isLight);
            FlyoutControlPalette p = CreateFlyoutPalette(theme, sp, isLight);
            _fanRowRefs.Clear();
            _groupHeaderRefs.Clear();
            _probeRowRefs.Clear();

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
                Focusable = true,
                Background = TrayAppDotNETFlyoutUI.Brush(theme.ResolveFlyoutBackground(_settings, isLight)),
                BorderBrush = TrayAppDotNETFlyoutUI.Brush(theme.Border.For(isLight)),
                BorderThickness = Layout.RootBorderThickness,
                CornerRadius = Rounded(Layout.RootCornerRadius),
                ClipToBounds = false,
                BoxShadow = new BoxShadows(new BoxShadow
                {
                    OffsetY = Layout.RootShadowOffsetY,
                    Blur = Layout.RootShadowBlur,
                    Color = theme.FlyoutShadow.For(isLight),
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
        finally
        {
            _isRebuildingVisual = false;
        }
    }

    private Border BuildHeader(FlyoutControlPalette flyoutControlPalette)
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
                new ColumnDefinition(new GridLength(Layout.HeaderWideColumnWidth)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(Layout.HeaderNarrowColumnWidth)),
                new ColumnDefinition(new GridLength(Layout.HeaderNarrowColumnWidth)),
                new ColumnDefinition(new GridLength(Layout.HeaderNarrowColumnWidth)),
                new ColumnDefinition(new GridLength(Layout.HeaderWideColumnWidth)),
            },
        };

        AddHeaderButton(
            grid,
            0,
            GlyphCatalog.SETTINGS,
            flyoutControlPalette,
            () => _openSettings(null),
            L("Tray_Settings", "Settings"),
            configureButton: SuppressNextAutoHideWhenPressed);
        AddHeaderButton(grid, 1, GlyphCatalog.CURVE_WINDOW, flyoutControlPalette, OpenHeaderCurveEditor,
            "Fan curve editor", fontSize: Layout.HeaderManagerButtonFontSize,
            configureButton: SuppressNextAutoHideWhenPressed);
        _nonFunctioningFansButtonGlyph = AddHeaderButton(grid, 2, NonFunctioningFansGlyph, flyoutControlPalette, ToggleNonFunctioningFans,
            "Show/hide non-functioning fans");
        AddGroupButton(grid, 3, flyoutControlPalette);
        AddProbeButton(grid, 4, flyoutControlPalette);
        AddProfileButton(grid, 6, 1, flyoutControlPalette);
        AddProfileButton(grid, 7, 2, flyoutControlPalette);
        AddProfileButton(grid, 8, 3, flyoutControlPalette);

        _undockButton = BuildUndockButton(flyoutControlPalette);
        _undockButton.IsVisible = _settings.AllowFlyoutUndock;
        Grid.SetColumn(_undockButton, 9);
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

    private void OpenHeaderCurveEditor()
    {
        Fan? fan = ResolveCurveEditorFan();
        if (fan == null)
        {
            _openSettings(FanSettingsPage.FanProperties);
            return;
        }

        OpenCurveEditor(fan);
    }

    private Fan? ResolveCurveEditorFan()
    {
        Fan? fan = _cells
            .SelectMany(static cell => cell.Fans)
            .FirstOrDefault(static candidate => candidate.AssignedCurve != null);
        fan ??= _cells
            .SelectMany(static cell => cell.Fans)
            .FirstOrDefault();
        if (fan != null || _lhmService == null) return fan;

        fan = _lhmService.Fans.FirstOrDefault(static candidate => candidate.AssignedCurve != null);
        return fan ?? _lhmService.Fans.FirstOrDefault();
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
        bool hasUpdateCard = IsUpdateCardVisible;
        if (hasUpdateCard)
            _cellStack.Children.Add(BuildUpdateCard(p, theme, isLight));

        foreach (FlyoutVisualSlot slot in BuildVisualSlots(p, theme, isLight))
            _cellStack.Children.Add(slot.Control);

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
        empty.IsVisible = _cells.Count == 0 && _probeCards.Count == 0 && !hasUpdateCard;
        holder.Children.Add(empty);
        return holder;
    }

    /// <summary>
    /// Builds non-update flyout cards in persisted display order.
    /// </summary>
    private List<FlyoutVisualSlot> BuildVisualSlots(FlyoutControlPalette p, AppTheme theme, bool isLight)
    {
        List<FlyoutVisualSlot> slots = [];
        int sequence = 0;
        foreach (FanFlyoutCell cell in _cells)
        {
            slots.Add(new FlyoutVisualSlot(
                NormalizeDisplayOrder(ResolveCellDisplayOrder(cell)),
                sequence++,
                ResolveCellTie(cell),
                BuildCell(cell, p, theme, isLight)));
        }

        foreach (ProbeCard probeCard in _probeCards)
        {
            slots.Add(new FlyoutVisualSlot(
                NormalizeDisplayOrder(probeCard.DisplayOrder),
                sequence++,
                probeCard.DisplayName,
                BuildProbeCard(probeCard, p, theme, isLight)));
        }

        return
        [
            .. slots
                .OrderBy(static s => s.Order)
                .ThenBy(static s => s.Sequence)
                .ThenBy(static s => s.Tie, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// Resolves the persisted display order for a fan or group cell.
    /// </summary>
    private static int ResolveCellDisplayOrder(FanFlyoutCell cell)
    {
        if (cell.HasGroupHeader) return cell.GroupSettings?.DisplayOrder ?? -1;
        return cell.Fans.Count == 1 ? cell.Fans[0].FlyoutDisplayOrder : -1;
    }

    /// <summary>
    /// Resolves a stable visual tie-breaker for a fan or group cell.
    /// </summary>
    private static string ResolveCellTie(FanFlyoutCell cell)
    {
        if (cell.HasGroupHeader) return cell.GroupName ?? string.Empty;
        return cell.Fans.Count == 1 ? cell.Fans[0].DisplayName : string.Empty;
    }

    private Border BuildUpdateCard(FlyoutControlPalette p, AppTheme theme, bool isLight)
    {
        UpdateInfo? update = AppServices.UpdateCheckService?.AvailableUpdate;
        string releaseName = update?.ReleaseName ?? L("UpdateDialog_DefaultTitle", "Update available");

        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        Border icon = new()
        {
            Width = Layout.FanButtonWidth,
            Height = Layout.FanButtonHeight,
            Margin = Layout.FanButtonUngroupedMargin,
            Child = TrayAppDotNETFlyoutUI.IconText(GlyphCatalog.INFO, p, Layout.FanButtonFontSize),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        StackPanel text = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = Layout.FanNameStackUngroupedMargin,
        };
        TextBlock title = TrayAppDotNETFlyoutUI.Text(
            L("UpdateNotification_Title", "Update available"),
            p,
            Layout.FanNameFontSize,
            FontWeight.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        TextBlock subtitle = TrayAppDotNETFlyoutUI.Text(
            string.Format(CultureInfo.CurrentCulture,
                L("UpdateNotification_BodyFormat", "{0} is available."),
                releaseName),
            p,
            Layout.FanSubtitleFontSize,
            color: p.SecondaryForeground);
        subtitle.Margin = Layout.SubtitleMargin;
        subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(title);
        text.Children.Add(subtitle);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        Border install = TrayAppDotNETFlyoutUI.TextButton(
            L("Settings_About_InstallUpdate_Available", "Install update"),
            p,
            ShowUpdateConfirmation,
            Layout.FanSubtitleFontSize,
            Layout.UpdateInstallButtonPadding);
        install.VerticalAlignment = VerticalAlignment.Center;
        install.Margin = Layout.TelemetryMargin;
        Grid.SetColumn(install, 2);
        row.Children.Add(install);

        Thickness borderThickness = _settings.EnableCardBorders ? Layout.CardBorderThickness : Layout.ZeroThickness;
        return TrayAppDotNETFlyoutUI.Card(
            row,
            theme.ResolveFanCardBackground(_settings, isLight),
            theme.ResolveFlyoutCardBorder(_settings, isLight),
            Rounded(Layout.CardCornerRadius),
            Layout.CardPadding,
            Layout.CardMargin(
                Math.Clamp(_settings.FlyoutCardHorizontalInset, 0, Layout.MaxSettingSpacing),
                Math.Clamp(_settings.FlyoutCardHorizontalInset, 0, Layout.MaxSettingSpacing),
                Math.Clamp(_settings.FlyoutCardSpacing, 0, Layout.MaxSettingSpacing)),
            borderThickness);
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
        else if (cell.Fans.Count == 1) content.Children.Add(BuildFanRow(cell.Fans[0], cell, p, grouped: false, interactive));

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
        if (interactive)
        {
            if (cell.HasGroupHeader)
                WireGroupDrag(card, cell);
            else if (cell.Fans.Count == 1)
                WireFanDrag(card, cell.Fans[0]);
        }

        return card;
    }

    /// <summary>
    /// Builds a flyout card that displays selected probe values.
    /// </summary>
    private Border BuildProbeCard(
        ProbeCard probeCard,
        FlyoutControlPalette p,
        AppTheme theme,
        bool isLight,
        bool interactive = true)
    {
        DeviceNicknameResolver nicknameResolver = DeviceNicknameResolver.Create(_settings);
        ProbeNicknameResolver probeNicknameResolver = ProbeNicknameResolver.Create(_settings);
        StackPanel content = new() { Spacing = 0 };
        content.Children.Add(BuildProbeHeader(probeCard, p));//, ProbeCardDominantDeviceName(probeCard, nicknameResolver)));

        if (!probeCard.IsCollapsed)
        {
            StackPanel rows = new()
            {
                Margin = Layout.ProbeRowsMargin,
            };

            List<ProbeCardProbe> selectedProbes =
            [
                .. probeCard.Probes
                    .Where(static probe => probe.IsSelected && !string.IsNullOrWhiteSpace(probe.DataSourceKey))
            ];
            if (selectedProbes.Count == 0)
            {
                TextBlock empty = TrayAppDotNETFlyoutUI.Text("No probes selected", p, Layout.ProbeEmptyFontSize,
                    color: p.SecondaryForeground);
                empty.Opacity = Layout.ProbeEmptyOpacity;
                rows.Children.Add(empty);
            }
            else
            {
                foreach (ProbeCardProbe probe in selectedProbes)
                    rows.Children.Add(BuildProbeRow(probeCard, probe, p, nicknameResolver, probeNicknameResolver));
            }

            content.Children.Add(rows);
        }

        Thickness borderThickness = _settings.EnableCardBorders ? Layout.CardBorderThickness : Layout.ZeroThickness;
        Border card = TrayAppDotNETFlyoutUI.Card(
            content,
            theme.ResolveFanCardBackground(_settings, isLight),
            theme.ResolveFlyoutCardBorder(_settings, isLight),
            Rounded(Layout.CardCornerRadius),
            Layout.CardPadding,
            Layout.CardMargin(
                Math.Clamp(_settings.FlyoutCardHorizontalInset, 0, Layout.MaxSettingSpacing),
                Math.Clamp(_settings.FlyoutCardHorizontalInset, 0, Layout.MaxSettingSpacing),
                Math.Clamp(_settings.FlyoutCardSpacing, 0, Layout.MaxSettingSpacing)),
            borderThickness);
        card.Tag = probeCard;
        if (interactive)
            WireProbeDrag(card, probeCard);
        return card;
    }

    /// <summary>
    /// Builds the probe-card header with a selector button and editable title.
    /// </summary>
    private Grid BuildProbeHeader(ProbeCard probeCard, FlyoutControlPalette p)//, string deviceName)
    {
        Grid row = new()
        {
            Tag = probeCard,
            Background = Brushes.Transparent,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        Border probeButton = TrayAppDotNETFlyoutUI.IconButton(
            GlyphCatalog.PROBE,
            p,
            _ => ToggleProbeSelectorWindow(probeCard),
            Layout.ProbeButtonWidth,
            Layout.ProbeButtonHeight,
            Layout.ProbeButtonFontSize,
            margin: Layout.ProbeButtonMargin,
            tooltip: "Probe data selector");
        probeButton.VerticalAlignment = VerticalAlignment.Center;
        SuppressNextAutoHideWhenPressed(probeButton);
        row.Children.Add(probeButton);

        Grid nameGrid = new();
        TextBlock name = TrayAppDotNETFlyoutUI.Text(probeCard.DisplayName, p, Layout.ProbeNameFontSize);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Tag = probeCard;
        name.PointerPressed += ProbeNameTextPointerPressed;
        TextBox edit = InlineTextBox(probeCard.DisplayName, p);
        edit.Name = "ProbeNameEdit";
        edit.IsVisible = false;
        edit.KeyDown += ProbeNameEditKeyDown;
        edit.LostFocus += ProbeNameEditLostFocus;
        nameGrid.Children.Add(name);
        nameGrid.Children.Add(edit);
        // TextBlock device = TrayAppDotNETFlyoutUI.Text(deviceName, p, Layout.FanSubtitleFontSize);
        // device.Opacity = 0.8;
        // device.Margin = Layout.SubtitleMargin;
        // device.TextTrimming = TextTrimming.CharacterEllipsis;

        StackPanel nameStack = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = Layout.ProbeNameStackMargin,
        };
        nameStack.Children.Add(nameGrid);
        // nameStack.Children.Add(device);
        Grid.SetColumn(nameStack, 1);
        row.Children.Add(nameStack);

        Border expand = TrayAppDotNETFlyoutUI.IconButton(ProbeExpansionGlyph(probeCard), p, _ =>
            {
                probeCard.IsCollapsed = !probeCard.IsCollapsed;
                SaveProbeCardChanges();
                RebuildVisual();
            }, Layout.GroupHeaderButtonWidth, Layout.GroupHeaderButtonHeight, Layout.GroupExpandFontSize,
            margin: Layout.GroupHeaderButtonMargin, tooltip: ProbeExpansionTooltip(probeCard));
        Grid.SetColumn(expand, 2);
        row.Children.Add(expand);

        Border delete = TrayAppDotNETFlyoutUI.IconButton(GlyphCatalog.DELETE, p, e => _ = DeleteProbeCardAsync(probeCard),
            Layout.GroupHeaderButtonWidth, Layout.GroupHeaderButtonHeight, Layout.GroupDeleteFontSize,
            margin: Layout.GroupHeaderButtonMargin, tooltip: "Delete probe card");
        Grid.SetColumn(delete, 3);
        row.Children.Add(delete);
        return row;
    }

    /// <summary>
    /// Resolves the device name represented by the most selected probes in a probe card.
    /// </summary>
    private static string ProbeCardDominantDeviceName(ProbeCard probeCard, DeviceNicknameResolver nicknameResolver)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> firstIndexes = new(StringComparer.OrdinalIgnoreCase);
        string bestDeviceName = string.Empty;
        int bestCount = 0;
        int bestIndex = int.MaxValue;
        int index = 0;

        foreach (ProbeCardProbe probe in probeCard.Probes)
        {
            if (!probe.IsSelected || string.IsNullOrWhiteSpace(probe.DataSourceKey)) continue;

            DataSource? source = DataSource.Find(probe.DataSourceKey);
            if (source == null) continue;

            string deviceName = nicknameResolver.Resolve(source);
            if (string.IsNullOrWhiteSpace(deviceName)) continue;

            if (!firstIndexes.ContainsKey(deviceName))
                firstIndexes[deviceName] = index;

            int count = counts.GetValueOrDefault(deviceName) + 1;
            counts[deviceName] = count;
            int firstIndex = firstIndexes[deviceName];
            if (count < bestCount || count == bestCount && firstIndex >= bestIndex)
            {
                index++;
                continue;
            }

            bestDeviceName = deviceName;
            bestCount = count;
            bestIndex = firstIndex;
            index++;
        }

        return bestDeviceName;
    }

    /// <summary>
    /// Builds one selected-probe row for a probe card.
    /// </summary>
    private Grid BuildProbeRow(
        ProbeCard probeCard,
        ProbeCardProbe probe,
        FlyoutControlPalette p,
        DeviceNicknameResolver nicknameResolver,
        ProbeNicknameResolver probeNicknameResolver)
    {
        DataSource? source = DataSource.Find(probe.DataSourceKey);
        Grid row = new()
        {
            Margin = Layout.ProbeRowMargin,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(Layout.ProbeRowValueMinWidth)),
            },
        };

        StackPanel labelRow = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextBlock glyph = TrayAppDotNETFlyoutUI.IconText(
            ProbeValueFormatter.GlyphFor(source?.DataSourceType ?? DataSourceTypeEnum.Unknown),
            p,
            Layout.ProbeRowGlyphFontSize);
        glyph.Width = Layout.ProbeRowGlyphWidth;

        //TODO: Move this hack to its own layer its currently repeated everywhere I need it
        if (source?.DataSourceType == DataSourceTypeEnum.Load) glyph.RenderTransform = new ScaleTransform(0.9, 1.0);
        glyph.VerticalAlignment = VerticalAlignment.Center;
        labelRow.Children.Add(glyph);

        TextBlock label = TrayAppDotNETFlyoutUI.Text(
            $"{ProbeLabel(source, probe, nicknameResolver, probeNicknameResolver)}: ",
            p,
            Layout.ProbeRowTextFontSize);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.VerticalAlignment = VerticalAlignment.Center;
        labelRow.Children.Add(label);
        row.Children.Add(labelRow);

        TextBlock value = TrayAppDotNETFlyoutUI.Text(
            ProbeValue(source, probe),
            p,
            Layout.ProbeRowTextFontSize);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        RegisterProbeRowRef(probeCard, new ProbeCardRowVisualRefs(probe, value));
        return row;
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
            fontFamily: Constants.FanFontFamilyName);
        fanButton.VerticalAlignment = VerticalAlignment.Center;
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
        subtitle.Opacity = Layout.SubtitleOpacity;
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
        TextBlock rpm = TrayAppDotNETFlyoutUI.Text($"{fan.CurrentRPM} RPM", p, Layout.RPMFontSize);
        rpm.Opacity = Layout.RPMOpacity;
        rpm.HorizontalAlignment = HorizontalAlignment.Right;
        TextBlock controller = TrayAppDotNETFlyoutUI.Text(fan.ControllerDisplayLabel, p, Layout.ControllerFontSize);
        controller.Opacity = Layout.ControllerOpacity;
        controller.HorizontalAlignment = HorizontalAlignment.Right;
        controller.TextTrimming = TextTrimming.CharacterEllipsis;
        controller.Margin = Layout.ControllerMargin;
        telemetry.Children.Add(rpm);
        telemetry.Children.Add(controller);
        Grid.SetRow(telemetry, 0);
        Grid.SetColumn(telemetry, 2);
        row.Children.Add(telemetry);

        TextBlock? valueText = null;
        FlyoutSlider? slider = null;
        TextBlock? modeGlyph = null;
        Border? mode = null;
        if (!grouped)
        {
            mode = TrayAppDotNETFlyoutUI.IconButton(
                ControlModeGlyph(fan.CurrentControlMode),
                p,
                _ => ToggleFanMode(fan),
                Layout.ModeButtonWidth,
                Layout.ModeButtonHeight,
                Layout.ModeButtonFontSize,
                margin: Layout.ModeButtonUngroupedMargin,
                tooltip: ControlModeTooltip(fan.CurrentControlMode));
            mode.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(mode, 1);
            Grid.SetColumn(mode, 0);
            row.Children.Add(mode);
            modeGlyph = mode.Child as TextBlock;
            ApplyControlModeGlyphVisual(modeGlyph, fan.CurrentControlMode);

            Grid sliderRow = new()
            {
                Height = Layout.SliderRowHeight,
                Margin = Layout.SliderRowMargin,
                VerticalAlignment = VerticalAlignment.Center,
            };
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(fan.FanDisplayedValueSlotWidth)));
            SpeedSliderRange sliderRange = ResolveFanSliderRange(fan);
            double? curveSliderValue = ResolveFanCurveSliderValue(fan, sliderRange);
            slider = CreateSlider(
                p,
                FanSliderValue(fan, curveSliderValue, sliderRange),
                sliderRange,
                fan.RPMMode ? 50 : 2,
                ResolveFanSliderThumb(fan));
            ConfigureFanSliderMultipleValues(fan, slider, sliderRange);
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
                if (fan.CurrentControlMode == FanControlMode.Manual)
                    AppServices.LHMService?.PersistLiveState();
                FlushPendingFanRebuild();
            };
            slider.ValueChanged += (_, value) =>
            {
                if (fan.CurrentControlMode != FanControlMode.Manual)
                {
                    SpeedSliderRange currentSliderRange = ResolveFanSliderRange(fan);
                    ApplySliderRange(slider, currentSliderRange);
                    double? currentCurveSliderValue = ResolveFanCurveSliderValue(fan, currentSliderRange);
                    valueText?.Text = FanSliderValueText(fan, currentCurveSliderValue, currentSliderRange);
                    slider.Thumb = ResolveFanSliderThumb(fan);
                    slider.Value = FanSliderValue(fan, currentCurveSliderValue, currentSliderRange);
                    ConfigureFanSliderMultipleValues(fan, slider, currentSliderRange);
                    if (valueText != null) ApplyFanRelinquishedControlVisual(fan, name, valueText, slider);
                    ApplyControlModeGlyphVisual(modeGlyph, fan.CurrentControlMode);
                    TrayAppDotNETToolTip.SetTip(mode, ControlModeTooltip(fan.CurrentControlMode));
                    return;
                }

                SpeedSliderRange currentManualSliderRange = ResolveFanSliderRange(fan);
                int next = ClampSliderValue(value, currentManualSliderRange);
                fan.FanDisplayedValue = next;
                valueText?.Text = FanSliderValueText(fan, null, currentManualSliderRange);
                slider.Thumb = ResolveFanSliderThumb(fan);
                ConfigureFanSliderMultipleValues(fan, slider, currentManualSliderRange);
                if (valueText != null) ApplyFanRelinquishedControlVisual(fan, name, valueText, slider);
                ApplyControlModeGlyphVisual(modeGlyph, fan.CurrentControlMode);
                TrayAppDotNETToolTip.SetTip(mode, ControlModeTooltip(fan.CurrentControlMode));
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
            valueText = TrayAppDotNETFlyoutUI.Text(FanSliderValueText(fan, curveSliderValue, sliderRange), p,
                Layout.ValueFontSize);
            valueText.HorizontalAlignment = HorizontalAlignment.Right;
            valueText.VerticalAlignment = VerticalAlignment.Center;
            ApplyFanRelinquishedControlVisual(fan, name, valueText, slider);
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
            TextBox valueEdit = InlineTextBox(FanSliderValueText(fan, curveSliderValue, sliderRange), p);
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
        }

        if (interactive)
        {
            RegisterFanRowRefs(fan,
                new FanRowVisualRefs(grouped, name, rpm, subtitle, valueText, slider, modeGlyph, mode));
            if (grouped)
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
            Layout.GroupIconHeight, Layout.GroupIconFontSize, margin: Layout.FanButtonUngroupedMargin,
            tooltip: "Group");
        groupIcon.VerticalAlignment = VerticalAlignment.Center;
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
        name.Opacity = Layout.GroupNameOpacity;
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
        activeCurve.Opacity = Layout.SubtitleOpacity;
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
            ControlModeGlyph(cell.GroupCurrentControlMode),
            p,
            _ => ToggleGroupMode(cell),
            Layout.ModeButtonWidth,
            Layout.ModeButtonHeight,
            Layout.ModeButtonFontSize,
            margin: Layout.GroupModeMargin,
            tooltip: ControlModeTooltip(cell.GroupCurrentControlMode));
        mode.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(mode, 1);
        Grid.SetColumn(mode, 0);
        row.Children.Add(mode);
        TextBlock? modeGlyph = mode.Child as TextBlock;
        ApplyControlModeGlyphVisual(modeGlyph, cell.GroupCurrentControlMode);

        Grid sliderRow = new()
        {
            Height = Layout.SliderRowHeight,
            Margin = Layout.GroupSliderRowMargin,
            VerticalAlignment = VerticalAlignment.Center,
        };
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        sliderRow.ColumnDefinitions.Add(
            new ColumnDefinition(new GridLength(cell.GroupDisplayedValueSlotWidth)));

        SpeedSliderRange sliderRange = ResolveGroupSliderRange(cell);
        double? groupCurveSliderValue = ResolveGroupCurveSliderValue(cell, sliderRange);
        FlyoutSlider slider = CreateSlider(
            p,
            GroupSliderValue(cell, groupCurveSliderValue, sliderRange),
            sliderRange,
            cell.GroupRPMMode ? 50 : 2,
            ResolveGroupSliderThumb(cell));
        ConfigureGroupSliderMultipleValues(cell, slider, sliderRange);
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
            if (cell.GroupCurrentControlMode == FanControlMode.Manual)
                SaveGroupChanges();
            FlushPendingFanRebuild();
        };
        TextBlock? valueText = null;
        slider.ValueChanged += (_, value) =>
        {
            if (cell.GroupCurrentControlMode != FanControlMode.Manual)
            {
                SpeedSliderRange currentSliderRange = ResolveGroupSliderRange(cell);
                ApplySliderRange(slider, currentSliderRange);
                double? currentCurveSliderValue = ResolveGroupCurveSliderValue(cell, currentSliderRange);
                valueText?.Text = GroupSliderValueText(cell, currentCurveSliderValue, currentSliderRange);
                slider.Thumb = ResolveGroupSliderThumb(cell);
                slider.Value = GroupSliderValue(cell, currentCurveSliderValue, currentSliderRange);
                ConfigureGroupSliderMultipleValues(cell, slider, currentSliderRange);
                ApplyControlModeGlyphVisual(modeGlyph, cell.GroupCurrentControlMode);
                TrayAppDotNETToolTip.SetTip(mode, ControlModeTooltip(cell.GroupCurrentControlMode));
                if (valueText != null) ApplyGroupRelinquishedControlVisual(cell, name, valueText, slider);
                return;
            }

            SpeedSliderRange currentManualSliderRange = ResolveGroupSliderRange(cell);
            cell.GroupFanDisplayedValue =
                ClampSliderValue(value, currentManualSliderRange);
            ApplyGroupManualValueToFans(cell);
            valueText?.Text = GroupSliderValueText(cell, null, currentManualSliderRange);
            slider.Thumb = ResolveGroupSliderThumb(cell);
            ConfigureGroupSliderMultipleValues(cell, slider, currentManualSliderRange);
            if (sliderDragging) AppServices.LHMService?.PersistLiveState(save: false);
            else SaveGroupChanges();
        };
        sliderRow.Children.Add(slider);
        Grid valueGrid = new()
        {
            Width = cell.GroupDisplayedValueSlotWidth,
            Height = Layout.SliderRowHeight,
            Margin = Layout.GroupValueGridMargin,
            VerticalAlignment = VerticalAlignment.Center,
        };
        valueText = TrayAppDotNETFlyoutUI.Text(GroupSliderValueText(cell, groupCurveSliderValue, sliderRange), p,
            Layout.ValueFontSize);
        valueText.HorizontalAlignment = HorizontalAlignment.Right;
        valueText.VerticalAlignment = VerticalAlignment.Center;
        ApplyGroupRelinquishedControlVisual(cell, name, valueText, slider);
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

        RegisterGroupHeaderRefs(cell, new GroupHeaderVisualRefs(name, activeCurve, valueText, slider, modeGlyph, mode));

        return row;
    }

    private FlyoutSlider CreateSlider(
        FlyoutControlPalette p,
        int value,
        SpeedSliderRange range,
        int wheelStep,
        SliderThumbGlyphOption thumb)
    {
        return new FlyoutSlider
        {
            Minimum = range.Minimum,
            Maximum = range.Maximum,
            Value = ClampSliderValue(value, range),
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

    private void RegisterGroupHeaderRefs(FanFlyoutCell cell, GroupHeaderVisualRefs refs) =>
        _groupHeaderRefs[cell] = refs;

    /// <summary>
    /// Registers a probe value text block for live refresh.
    /// </summary>
    private void RegisterProbeRowRef(ProbeCard probeCard, ProbeCardRowVisualRefs refs)
    {
        if (!_probeRowRefs.TryGetValue(probeCard, out List<ProbeCardRowVisualRefs>? list))
        {
            list = [];
            _probeRowRefs[probeCard] = list;
        }

        list.Add(refs);
    }

    /// <summary>
    /// Refreshes selected probe values in all visible probe cards.
    /// </summary>
    private void RefreshProbeCardVisuals()
    {
        foreach (List<ProbeCardRowVisualRefs> refsList in _probeRowRefs.Values)
        {
            foreach (ProbeCardRowVisualRefs refs in refsList)
            {
                DataSource? source = DataSource.Find(refs.Probe.DataSourceKey);
                refs.Value.Text = ProbeValue(source, refs.Probe);
            }
        }
    }

    /// <summary>
    /// Resolves the label shown for a probe source.
    /// </summary>
    private static string ProbeLabel(
        DataSource? source,
        ProbeCardProbe probe,
        DeviceNicknameResolver nicknameResolver,
        ProbeNicknameResolver probeNicknameResolver)
    {
        if (source == null) return probe.DataSourceKey;
        return $"{nicknameResolver.Resolve(source)} {probeNicknameResolver.Resolve(source.DisplayName)}".Trim();
    }

    /// <summary>
    /// Resolves the value text shown for a probe source.
    /// </summary>
    private static string ProbeValue(DataSource? source, ProbeCardProbe probe) =>
        source == null ? "--" : ProbeValueFormatter.FormatValue(source, probe, probe.TruncateValue);

    /// <summary>
    /// Resolves the probe-card drawer expansion glyph.
    /// </summary>
    private static string ProbeExpansionGlyph(ProbeCard probeCard) =>
        probeCard.IsCollapsed ? GlyphCatalog.COLLAPSED : GlyphCatalog.EXPANDED;

    /// <summary>
    /// Resolves the probe-card drawer expansion tooltip.
    /// </summary>
    private static string ProbeExpansionTooltip(ProbeCard probeCard) =>
        probeCard.IsCollapsed ? "Expand probe card" : "Collapse probe card";

    /// <summary>
    /// Resolves a stable sort label for selected probes.
    /// </summary>
    private static string ProbeSortLabel(
        DataSource? source,
        ProbeCardProbe probe,
        DeviceNicknameResolver nicknameResolver,
        ProbeNicknameResolver probeNicknameResolver) =>
        source == null
            ? probe.DataSourceKey
            : $"{nicknameResolver.Resolve(source)}.{probeNicknameResolver.Resolve(source.DisplayName)}";

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
                or nameof(Fan.FanSliderMaximum) or nameof(Fan.CurrentControlMode) or nameof(Fan.CurrentRPM)
                or nameof(Fan.CurrentDutyCycle))
                RefreshFanSliderVisual(fan, refs);

            if (propertyName is null or nameof(Fan.CurrentControlMode))
            {
                ApplyControlModeGlyphVisual(refs.ModeGlyph, fan.CurrentControlMode);
                if (refs.ModeButton != null)
                    TrayAppDotNETToolTip.SetTip(refs.ModeButton, ControlModeTooltip(fan.CurrentControlMode));
            }
        }
    }

    private void RefreshFanSliderVisual(Fan fan, FanRowVisualRefs refs)
    {
        if (refs.Value == null || refs.Slider == null) return;

        SpeedSliderRange sliderRange = ResolveFanSliderRange(fan);
        double? curveSliderValue = ResolveFanCurveSliderValue(fan, sliderRange);
        refs.Value.Text = FanSliderValueText(fan, curveSliderValue, sliderRange);
        ApplySliderRange(refs.Slider, sliderRange);
        refs.Slider.Thumb = ResolveFanSliderThumb(fan);
        refs.Slider.Value = FanSliderValue(fan, curveSliderValue, sliderRange);
        ConfigureFanSliderMultipleValues(fan, refs.Slider, sliderRange);
        ApplyFanRelinquishedControlVisual(fan, refs.Name, refs.Value, refs.Slider);
    }

    private void RefreshGroupHeaderVisuals()
    {
        foreach ((FanFlyoutCell cell, GroupHeaderVisualRefs refs) in _groupHeaderRefs)
        {
            SpeedSliderRange sliderRange = ResolveGroupSliderRange(cell);
            double? curveSliderValue = ResolveGroupCurveSliderValue(cell, sliderRange);
            refs.Value.Text = GroupSliderValueText(cell, curveSliderValue, sliderRange);
            ApplySliderRange(refs.Slider, sliderRange);
            refs.Slider.Thumb = ResolveGroupSliderThumb(cell);
            refs.Slider.Value = GroupSliderValue(cell, curveSliderValue, sliderRange);
            ConfigureGroupSliderMultipleValues(cell, refs.Slider, sliderRange);
            refs.ActiveCurve.Text = cell.ActiveCurveText;
            ApplyControlModeGlyphVisual(refs.ModeGlyph, cell.GroupCurrentControlMode);
            TrayAppDotNETToolTip.SetTip(refs.ModeButton, ControlModeTooltip(cell.GroupCurrentControlMode));
            ApplyGroupRelinquishedControlVisual(cell, refs.Name, refs.Value, refs.Slider);
        }
    }

    private static string ControlModeGlyph(FanControlMode mode) =>
        mode == FanControlMode.Manual
            ? GlyphCatalog.FLYOUT_FAN_CONTROL_MODE_MANUAL
            : GlyphCatalog.FLYOUT_FAN_CONTROL_MODE_CURVE;

    private static string ControlModeTooltip(FanControlMode mode) =>
        mode == FanControlMode.Manual ? "Manual" : "Curve";

    private void ApplyControlModeGlyphVisual(TextBlock? glyph, FanControlMode mode)
    {
        if (glyph == null) return;
        glyph.Text = ControlModeGlyph(mode);
        glyph.Opacity = mode == FanControlMode.Manual ? Layout.FullOpacity : Layout.RelinquishedControlOpacity;
    }

    private void ApplyFanRelinquishedControlVisual(
        Fan fan,
        TextBlock name,
        TextBlock value,
        FlyoutSlider slider)
    {
        double opacity = IsFanControlRelinquished(fan) ? Layout.RelinquishedControlOpacity : Layout.FullOpacity;
        name.Opacity = Layout.FullOpacity;
        value.Opacity = opacity;
        slider.Opacity = opacity;
    }

    private void ApplyGroupRelinquishedControlVisual(
        FanFlyoutCell cell,
        TextBlock name,
        TextBlock value,
        FlyoutSlider slider)
    {
        double opacity = IsGroupControlRelinquished(cell) ? Layout.RelinquishedControlOpacity : Layout.FullOpacity;
        name.Opacity = cell.HasGroupHeader ? Layout.GroupNameOpacity : Layout.FullOpacity;
        value.Opacity = opacity;
        slider.Opacity = opacity;
    }

    private static bool IsFanControlRelinquished(Fan fan) =>
        fan.CurrentControlMode != FanControlMode.Manual
        && fan.AssignedCurve?.SelectedDataSource == null;

    private static bool IsGroupControlRelinquished(FanFlyoutCell cell) =>
        cell.GroupCurrentControlMode != FanControlMode.Manual
        && cell.GroupSettings?.AssignedCurve?.SelectedDataSource == null;

    private SliderThumbGlyphOption ResolveFanSliderThumb(Fan fan) =>
        fan.CurrentControlMode == FanControlMode.Manual
            ? ResolveSliderThumbOption()
            : ResolveCurveSliderThumbOption();

    private SliderThumbGlyphOption ResolveGroupSliderThumb(FanFlyoutCell cell) =>
        cell.GroupCurrentControlMode == FanControlMode.Manual
            ? ResolveSliderThumbOption()
            : ResolveCurveSliderThumbOption();

    /// <summary>
    /// Configures the optional dimmed manual or curve value for a fan slider.
    /// </summary>
    private void ConfigureFanSliderMultipleValues(Fan fan, FlyoutSlider slider, SpeedSliderRange sliderRange)
    {
        ClearSliderMultipleValues(slider);
        if (!ShouldShowMultipleSliderValues(fan.CurrentControlMode)) return;

        double? curveSliderValue = ResolveFanCurveSliderValue(fan, sliderRange, requireCurveMode: false);
        if (!curveSliderValue.HasValue) return;

        if (fan.CurrentControlMode == FanControlMode.Manual)
        {
            slider.SecondaryValue = ClampSliderPosition(curveSliderValue.Value, sliderRange);
            slider.SecondaryThumb = ResolveCurveSliderThumbOption();
            slider.SecondaryOpacity = Layout.InactiveSliderValueOpacity;
            return;
        }

        if (IsFanControlRelinquished(fan)) return;

        slider.SecondaryValue = ClampSliderPosition(fan.FanDisplayedValue, sliderRange);
        slider.SecondaryThumb = ResolveSliderThumbOption();
        slider.SecondaryOpacity = Layout.InactiveSliderValueOpacity;
    }

    /// <summary>
    /// Configures the optional dimmed manual or curve value for a group slider.
    /// </summary>
    private void ConfigureGroupSliderMultipleValues(
        FanFlyoutCell cell,
        FlyoutSlider slider,
        SpeedSliderRange sliderRange)
    {
        ClearSliderMultipleValues(slider);
        if (!ShouldShowMultipleSliderValues(cell.GroupCurrentControlMode)) return;

        double? curveSliderValue = ResolveGroupCurveSliderValue(cell, sliderRange, requireCurveMode: false);
        if (!curveSliderValue.HasValue) return;

        if (cell.GroupCurrentControlMode == FanControlMode.Manual)
        {
            slider.SecondaryValue = ClampSliderPosition(curveSliderValue.Value, sliderRange);
            slider.SecondaryThumb = ResolveCurveSliderThumbOption();
            slider.SecondaryOpacity = Layout.InactiveSliderValueOpacity;
            return;
        }

        if (IsGroupControlRelinquished(cell)) return;

        slider.SecondaryValue = ClampSliderPosition(cell.GroupFanDisplayedValue, sliderRange);
        slider.SecondaryThumb = ResolveSliderThumbOption();
        slider.SecondaryOpacity = Layout.InactiveSliderValueOpacity;
    }

    /// <summary>
    /// Resolves whether the current setting allows dual slider values for the active control mode.
    /// </summary>
    private bool ShouldShowMultipleSliderValues(FanControlMode controlMode) =>
        _settings.ShowMultipleSliderValuesMode switch
        {
            MultipleSliderValuesDisplayMode.Enabled => true,
            MultipleSliderValuesDisplayMode.OnlyInManual => controlMode == FanControlMode.Manual,
            _ => false,
        };

    /// <summary>
    /// Restores single-value rendering on a slider.
    /// </summary>
    private static void ClearSliderMultipleValues(FlyoutSlider slider)
    {
        slider.SecondaryValue = null;
        slider.SecondaryThumb = null;
        slider.SecondaryProgressColor = null;
    }

    /// <summary>
    /// Applies the displayed slider range without relying on the old default zero baseline.
    /// </summary>
    private static void ApplySliderRange(FlyoutSlider slider, SpeedSliderRange sliderRange)
    {
        slider.Minimum = sliderRange.Minimum;
        slider.Maximum = sliderRange.Maximum;
    }

    /// <summary>
    /// Resolves the current fan slider range in the fan's displayed unit.
    /// </summary>
    private static SpeedSliderRange ResolveFanSliderRange(Fan fan)
    {
        Curve? curve = fan.AssignedCurve;
        if (fan.CurrentControlMode != FanControlMode.Manual && curve != null && !IsFanControlRelinquished(fan))
            return ConvertCurveRangeToFanSliderRange(fan, curve);

        return CreateSpeedSliderRange(0.0, fan.RPMMode ? fan.FanSliderMaximum : 100.0);
    }

    /// <summary>
    /// Resolves the current group slider range in the group's displayed unit.
    /// </summary>
    private static SpeedSliderRange ResolveGroupSliderRange(FanFlyoutCell cell)
    {
        Curve? curve = cell.GroupSettings?.AssignedCurve;
        if (cell.GroupCurrentControlMode != FanControlMode.Manual && curve != null && !IsGroupControlRelinquished(cell))
            return ConvertCurveRangeToGroupSliderRange(cell, curve);

        return CreateSpeedSliderRange(0.0, cell.GroupSliderMaximum);
    }

    /// <summary>
    /// Converts the assigned curve Y-axis range into the fan slider's active unit.
    /// </summary>
    private static SpeedSliderRange ConvertCurveRangeToFanSliderRange(Fan fan, Curve curve)
    {
        double minimum = curve.ActiveYMinLine;
        double maximum = curve.RPMMode
            ? Math.Max(curve.MinRPM, curve.MaxRPM)
            : Math.Max(curve.MinDutyCycle, curve.MaxDutyCycle);
        if (fan.RPMMode == curve.RPMMode)
            return CreateSpeedSliderRange(minimum, maximum);

        double rpmReference = FanRPMReference(fan, curve);
        return fan.RPMMode
            ? CreateSpeedSliderRange(minimum / 100.0 * rpmReference, maximum / 100.0 * rpmReference)
            : CreateSpeedSliderRange(minimum / rpmReference * 100.0, maximum / rpmReference * 100.0);
    }

    /// <summary>
    /// Converts the assigned curve Y-axis range into the group slider's active unit.
    /// </summary>
    private static SpeedSliderRange ConvertCurveRangeToGroupSliderRange(FanFlyoutCell cell, Curve curve)
    {
        double minimum = curve.ActiveYMinLine;
        double maximum = curve.RPMMode
            ? Math.Max(curve.MinRPM, curve.MaxRPM)
            : Math.Max(curve.MinDutyCycle, curve.MaxDutyCycle);
        return CreateSpeedSliderRange(
            ConvertCurveTargetToGroupSliderValue(cell, curve, minimum),
            ConvertCurveTargetToGroupSliderValue(cell, curve, maximum));
    }

    /// <summary>
    /// Creates a non-negative slider range with a normalized maximum.
    /// </summary>
    private static SpeedSliderRange CreateSpeedSliderRange(double minimum, double maximum)
    {
        double sanitizedMinimum = SanitizeSliderBound(minimum);
        double sanitizedMaximum = Math.Max(sanitizedMinimum, SanitizeSliderBound(maximum));
        return new SpeedSliderRange(sanitizedMinimum, sanitizedMaximum);
    }

    /// <summary>
    /// Drops invalid slider bounds before they reach rendering or input math.
    /// </summary>
    private static double SanitizeSliderBound(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : Math.Max(0.0, value);

    /// <summary>
    /// Clamps a continuous slider position into the active display range.
    /// </summary>
    private static double ClampSliderPosition(double value, SpeedSliderRange sliderRange) =>
        Math.Clamp(value, sliderRange.Minimum, sliderRange.Maximum);

    /// <summary>
    /// Rounds and clamps a slider value into the active display range.
    /// </summary>
    private static int ClampSliderValue(double value, SpeedSliderRange sliderRange) =>
        (int)Math.Round(ClampSliderPosition(value, sliderRange));

    /// <summary>
    /// Resolves the fan slider value in the active display range.
    /// </summary>
    private static int FanSliderValue(Fan fan, double? curveSliderValue, SpeedSliderRange sliderRange)
    {
        if (IsFanControlRelinquished(fan))
        {
            double duty = CurrentDutyCycleValue(fan);
            double value = fan.RPMMode
                ? duty / 100.0 * Math.Max(1, fan.FanSliderMaximum)
                : duty;
            return ClampSliderValue(value, sliderRange);
        }

        return ClampSliderValue(curveSliderValue ?? fan.FanDisplayedValue, sliderRange);
    }

    /// <summary>
    /// Formats the fan slider value in the active display range.
    /// </summary>
    private static string FanSliderValueText(Fan fan, double? curveSliderValue, SpeedSliderRange sliderRange) =>
        IsFanControlRelinquished(fan)
            ? $"{(int)Math.Round(CurrentDutyCycleValue(fan))}%"
            : $"{FanSliderValue(fan, curveSliderValue, sliderRange)}{fan.FanDisplayedValueSuffix}";

    /// <summary>
    /// Resolves the group slider value in the active display range.
    /// </summary>
    private static int GroupSliderValue(
        FanFlyoutCell cell,
        double? curveSliderValue,
        SpeedSliderRange sliderRange)
    {
        double value = IsGroupControlRelinquished(cell)
            ? GroupCurrentSpeedValue(cell)
            : curveSliderValue ?? cell.GroupFanDisplayedValue;
        return ClampSliderValue(value, sliderRange);
    }

    /// <summary>
    /// Formats the group slider value in the active display range.
    /// </summary>
    private static string GroupSliderValueText(
        FanFlyoutCell cell,
        double? curveSliderValue,
        SpeedSliderRange sliderRange) =>
        $"{GroupSliderValue(cell, curveSliderValue, sliderRange)}{cell.GroupFanDisplayedValueSuffix}";

    private static double CurrentDutyCycleValue(Fan fan) =>
        Math.Clamp(fan.CurrentDutyCycle, 0.0, 100.0);

    private static double GroupCurrentDutyCycleValue(FanFlyoutCell cell) =>
        cell.Fans.Count == 0 ? 0.0 : Math.Clamp(cell.Fans.Average(CurrentDutyCycleValue), 0.0, 100.0);

    /// <summary>
    /// Resolves the live group speed in the group's active unit.
    /// </summary>
    private static double GroupCurrentSpeedValue(FanFlyoutCell cell)
    {
        if (!cell.GroupRPMMode) return GroupCurrentDutyCycleValue(cell);
        if (cell.Fans.Count == 0) return 0.0;
        return Math.Clamp(cell.Fans.Average(static fan => fan.CurrentRPM), 0.0, cell.GroupSliderMaximum);
    }

    /// <summary>
    /// Resolves the assigned curve target as a fan slider value.
    /// </summary>
    private static double? ResolveFanCurveSliderValue(Fan fan, SpeedSliderRange sliderRange) =>
        ResolveFanCurveSliderValue(fan, sliderRange, requireCurveMode: true);

    /// <summary>
    /// Resolves the assigned curve target as a fan slider value.
    /// </summary>
    private static double? ResolveFanCurveSliderValue(
        Fan fan,
        SpeedSliderRange sliderRange,
        bool requireCurveMode)
    {
        if (requireCurveMode && fan.CurrentControlMode == FanControlMode.Manual) return null;
        Curve? curve = fan.AssignedCurve;
        DataSource? source = curve?.SelectedDataSource;
        if (curve == null || source == null) return null;

        double target = curve.Evaluate(source.DisplayValue);
        double sliderValue = ConvertCurveTargetToFanSliderValue(fan, curve, target);
        return ClampSliderPosition(sliderValue, sliderRange);
    }

    /// <summary>
    /// Resolves the assigned curve target as a group slider value.
    /// </summary>
    private static double? ResolveGroupCurveSliderValue(FanFlyoutCell cell, SpeedSliderRange sliderRange) =>
        ResolveGroupCurveSliderValue(cell, sliderRange, requireCurveMode: true);

    /// <summary>
    /// Resolves the assigned curve target as a group slider value.
    /// </summary>
    private static double? ResolveGroupCurveSliderValue(
        FanFlyoutCell cell,
        SpeedSliderRange sliderRange,
        bool requireCurveMode)
    {
        if (requireCurveMode && cell.GroupCurrentControlMode == FanControlMode.Manual) return null;
        Curve? curve = cell.GroupSettings?.AssignedCurve;
        DataSource? source = curve?.SelectedDataSource;
        if (curve == null || source == null) return null;

        double target = curve.Evaluate(source.DisplayValue);
        target = ConvertCurveTargetToGroupSliderValue(cell, curve, target);

        return ClampSliderPosition(target, sliderRange);
    }

    /// <summary>
    /// Converts a curve target to the grouped slider's active unit.
    /// </summary>
    private static double ConvertCurveTargetToGroupSliderValue(FanFlyoutCell cell, Curve curve, double target)
    {
        if (cell.GroupRPMMode == curve.RPMMode || cell.Fans.Count == 0) return target;

        return cell.GroupRPMMode
            ? cell.Fans.Average(fan => target / 100.0 * FanRPMReference(fan, curve))
            : cell.Fans.Average(fan => target / FanRPMReference(fan, curve) * 100.0);
    }

    private static double ConvertCurveTargetToFanSliderValue(Fan fan, Curve curve, double target)
    {
        if (fan.RPMMode == curve.RPMMode) return target;

        double rpmReference = FanRPMReference(fan, curve);
        return fan.RPMMode
            ? target / 100.0 * rpmReference
            : target / rpmReference * 100.0;
    }

    private static double FanRPMReference(Fan fan, Curve curve) =>
        Math.Max(1.0,
            fan.MaxRPM > 0
                ? fan.MaxRPM
                : fan.CurrentRPM > 0
                    ? Math.Max(100, fan.CurrentRPM)
                    : curve.MaxRPM > 0
                        ? curve.MaxRPM
                        : fan.FanSliderMaximum);

    private TextBlock? AddHeaderButton(
        Grid grid,
        int column,
        string glyph,
        FlyoutControlPalette p,
        Action click,
        string tooltip,
        double? fontSize = null,
        Action<Border>? configureButton = null)
    {
        Border button = TrayAppDotNETFlyoutUI.IconButton(glyph, p, _ => click(), Layout.HeaderButtonWidth,
            Layout.HeaderButtonHeight, fontSize ?? Layout.HeaderButtonFontSize, tooltip: tooltip);
        configureButton?.Invoke(button);
        TextBlock? text = button.Child as TextBlock;
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
        return text;
    }

    private void AddGroupButton(Grid grid, int column, FlyoutControlPalette p)
    {
        Grid icon = new()
        {
            Width = Layout.HeaderAddGroupIconSize, Height = Layout.HeaderAddGroupIconSize, IsHitTestVisible = false,
        };
        TextBlock groupGlyph = TrayAppDotNETFlyoutUI.IconText(GlyphCatalog.GROUP, p, Layout.HeaderAddGroupFontSize);
        TextBlock plusGlyph = TrayAppDotNETFlyoutUI.IconText(GlyphCatalog.ADD, p, Layout.HeaderAddGlyphFontSize);
        plusGlyph.HorizontalAlignment = HorizontalAlignment.Right;
        plusGlyph.VerticalAlignment = VerticalAlignment.Top;
        plusGlyph.RenderTransform = Layout.HeaderAddGlyphTransform;
        icon.Children.Add(groupGlyph);
        icon.Children.Add(plusGlyph);

        Border button = TrayAppDotNETFlyoutUI.IconButton(string.Empty, p, _ => AddGroup(), Layout.HeaderButtonWidth,
            Layout.HeaderButtonHeight, 0, tooltip: "Add group");
        button.Child = icon;
        Grid.SetColumn(button, column);
        grid.Children.Add(button);
    }

    private void AddProbeButton(Grid grid, int column, FlyoutControlPalette p)
    {
        Grid icon = new()
        {
            Width = Layout.HeaderAddGroupIconSize,
            Height = Layout.HeaderAddGroupIconSize,
            IsHitTestVisible = false,
        };
        TextBlock probeGlyph = TrayAppDotNETFlyoutUI.IconText(GlyphCatalog.PROBE, p, Layout.HeaderAddProbeFontSize);
        TextBlock plusGlyph = TrayAppDotNETFlyoutUI.IconText(GlyphCatalog.ADD, p, Layout.HeaderAddGlyphFontSize);
        plusGlyph.HorizontalAlignment = HorizontalAlignment.Right;
        plusGlyph.VerticalAlignment = VerticalAlignment.Top;
        plusGlyph.RenderTransform = Layout.HeaderAddGlyphTransform;
        icon.Children.Add(probeGlyph);
        icon.Children.Add(plusGlyph);

        Border button = TrayAppDotNETFlyoutUI.IconButton(string.Empty, p, _ => AddProbeCard(), Layout.HeaderButtonWidth,
            Layout.HeaderButtonHeight, 0, tooltip: "Add probe card");
        button.Child = icon;
        Grid.SetColumn(button, column);
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
            {
                if (string.Equals(fan.Group, cell.GroupName, StringComparison.OrdinalIgnoreCase))
                    fan.Group = null;
            }
        }

        SaveGroupChanges();
        ExecuteFanRebuild(reposition: true);
    }

    /// <summary>
    /// Confirms and removes a probe card from the flyout.
    /// </summary>
    private async Task DeleteProbeCardAsync(ProbeCard probeCard)
    {
        bool ok = await ConfirmAsync(
            L("Flyout_DeleteProbeCard_Title", "Delete probe card"),
            string.Format(
                CultureInfo.CurrentCulture,
                L("Flyout_DeleteProbeCard_MessageFormat", "Delete {0}?"),
                probeCard.DisplayName),
            L("Flyout_DeleteGroup_Confirm", "Delete"),
            L("SettingsWindow_ConfirmOverlay_Cancel", "Cancel"));
        if (!ok) return;

        if (_probeSelectorWindows.TryGetValue(probeCard, out ProbeDataSelectorWindow? selector))
        {
            selector.Close();
            _probeSelectorWindows.Remove(probeCard);
        }

        _probeCards.Remove(probeCard);
        _probeRowRefs.Remove(probeCard);
        SaveProbeCardChanges();
        RebuildVisual();
        QueuePositionNearTray();
    }

    private Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        _confirmTcs?.TrySetResult(false);
        _confirmTitle!.Text = title;
        _confirmMessage!.Text = message;
        _confirmOK!.Text = confirmText;
        _confirmCancel!.Text = cancelText;
        _confirmOverlay!.IsVisible = true;
        _confirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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
        ExecuteFanRebuild(reposition: true);
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
        ExecuteFanRebuild(reposition: false);
    }

    private void AddGroup()
    {
        string groupName = CreateUniqueGroupName();
        OffsetTopLevelDisplayOrders(1);
        _groupNames.Insert(0, groupName);
        GetOrCreateGroupSettings(groupName).DisplayOrder = 0;
        SaveGroupChanges();
        ExecuteFanRebuild(reposition: true);
    }

    /// <summary>
    /// Adds a new probe card to the top of the flyout.
    /// </summary>
    private void AddProbeCard()
    {
        string name = CreateUniqueProbeCardName();
        OffsetTopLevelDisplayOrders(1);
        ProbeCard probeCard = new()
        {
            Name = name,
            DisplayOrder = 0,
        };
        _probeCards.Insert(0, probeCard);
        SaveProbeCardChanges();
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
            fan.RPMMode = cell.GroupRPMMode;
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
        ExecuteFanRebuild(reposition: false);
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
        {
            slots.Add((new FanFlyoutCell(null, [fan]), NormalizeDisplayOrder(fan.FlyoutDisplayOrder), fanSequence,
                fan.DisplayName));
        }

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
            if (IsInteractiveCardDragSource(e.Source as Visual, row)) return;
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            _draggedFan = fan;
            _draggedGroupCell = null;
            _draggedProbeCard = null;
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
            if (IsInteractiveCardDragSource(e.Source as Visual, root)) return;
            if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) return;
            _draggedGroupCell = cell;
            _draggedFan = null;
            _draggedProbeCard = null;
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

    /// <summary>
    /// Wires a probe card for top-level drag reordering.
    /// </summary>
    private void WireProbeDrag(Control root, ProbeCard probeCard)
    {
        root.PointerPressed += (_, e) =>
        {
            if (IsInteractiveCardDragSource(e.Source as Visual, root)) return;
            if (!e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) return;
            _draggedProbeCard = probeCard;
            _draggedFan = null;
            _draggedGroupCell = null;
            _dragSourceControl = root;
            _dragStart = e.GetPosition(_cellStack);
            _dragPointerOffsetY = e.GetPosition(root).Y;
            e.Pointer.Capture(root);
            e.Handled = true;
        };
        root.PointerMoved += (_, e) => UpdateProbeDrag(root, e);
        root.PointerReleased += (_, e) => CompleteDrag(e.Pointer, e.GetPosition(_cellStack));
        root.PointerCaptureLost += (_, _) =>
        {
            if (!_isCompletingDrag) CancelDrag();
        };
    }

    private static bool IsInteractiveCardDragSource(Visual? source, Visual dragRoot)
    {
        for (Visual? current = source; current != null && !ReferenceEquals(current, dragRoot);
             current = current.GetVisualParent())
        {
            if (current is FlyoutSlider or TextBox or Button or Slider or Thumb or RepeatButton or ComboBox)
                return true;
            if (current is Control { Cursor: not null })
                return true;
        }

        return false;
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

    /// <summary>
    /// Updates probe-card top-level drag state.
    /// </summary>
    private void UpdateProbeDrag(Control source, PointerEventArgs e)
    {
        if (_draggedProbeCard == null || _cellStack == null) return;
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

        _dragPointerOffsetRatio = Math.Clamp(_dragPointerOffsetY / Math.Max(1, ghostSource.Bounds.Height), 0.0, 1.0);
        _dragPlacementSourceHeight = Math.Max(1, ghostSource.Bounds.Height);
        _dragPlacementPointerOffsetY = Math.Clamp(_dragPointerOffsetY, 0.0, _dragPlacementSourceHeight);
        _lastDragPointerY = current.Y;
        _dragMovingDown = current.Y >= _dragStart.Y;
        FanDragGhostStyle initialStyle = ResolveDragGhostStyle(FanDragPlacement.None);
        Control ghostContent = CreateDragGhostContent(initialStyle, ghostSource);
        double ghostWidth = ResolveDragGhostWidth(initialStyle, FanDragPlacement.None, ghostSource);
        _dragGhostHeight = MeasureDragGhostHeight(ghostContent, ghostWidth, Math.Max(1, ghostSource.Bounds.Height));
        _dragPointerOffsetY = Math.Clamp(_dragPointerOffsetRatio * _dragGhostHeight, 0.0, _dragGhostHeight);
        _dragGhostStyle = initialStyle;
        _dragGhost = new Border
        {
            Width = ghostWidth,
            Height = _dragGhostHeight,
            Opacity = Layout.FullOpacity,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetY = Layout.DragGhostShadowOffsetY,
                Blur = Layout.DragGhostShadowBlur,
                Color = (AppServices.Theme ?? AppTheme.Default).FlyoutShadow.For(
                    AppTheme.ResolveEffectiveIsLightTheme(_settings)),
            }),
            Child = ghostContent,
            IsHitTestVisible = false,
        };
        _dragOverlay.Children.Add(_dragGhost);
        ApplyDragGhostSurface(initialStyle);
        SetDragSourceOpacity(Layout.DragSourceOpacity);
        Canvas.SetLeft(_dragGhost, sourceTop?.X ?? 0);
        _rootCard?.Focus();
        if (EnableFanDragInstrumentation)
            BeginDragInstrumentation();
        UpdateDragPreview(current);
    }

    private Control CreateDragGhostContent(FanDragGhostStyle style, Control source)
    {
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;
        bool isLight = AppTheme.ResolveEffectiveIsLightTheme(_settings);
        FlyoutControlPalette palette = CreateFlyoutPalette(theme, Palette(), isLight);

        if (_draggedFan != null && style == FanDragGhostStyle.TopLevelFan)
        {
            FanFlyoutCell cell = _dragSourceCell is { HasGroupHeader: false }
                ? _dragSourceCell
                : new FanFlyoutCell(null, [_draggedFan]);
            return BuildCell(cell, palette, theme, isLight, interactive: false);
        }

        if (_draggedFan != null && style == FanDragGhostStyle.GroupedFan)
        {
            return BuildFanRow(_draggedFan, new FanFlyoutCell(null, [_draggedFan]),
                palette, grouped: true, interactive: false);
        }

        if (_draggedProbeCard != null && style == FanDragGhostStyle.Probe)
            return BuildProbeCard(_draggedProbeCard, palette, theme, isLight, interactive: false);

        return _draggedGroupCell != null
            ? BuildCell(_draggedGroupCell, palette, theme, isLight, interactive: false)
            : new Border { Width = source.Bounds.Width, Height = source.Bounds.Height };
    }

    private Border? CreateGroupedFanDropPlaceholder()
    {
        if (_draggedFan == null) return null;
        return new Border
        {
            Height = Layout.FanButtonHeight,
            Margin = Layout.FanRowGroupedMargin(Math.Clamp(_settings.FlyoutCardSpacing, 0,
                Layout.MaxSettingSpacing)),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
    }

    private FanDragGhostStyle ResolveDragGhostStyle(FanDragPlacement placement)
    {
        if (_draggedGroupCell != null) return FanDragGhostStyle.Group;
        if (_draggedProbeCard != null) return FanDragGhostStyle.Probe;
        if (_draggedFan == null) return FanDragGhostStyle.None;

        return placement.Kind switch
        {
            FanDragPlacementKind.IntoGroup => FanDragGhostStyle.GroupedFan,
            FanDragPlacementKind.TopLevel => FanDragGhostStyle.TopLevelFan,
            _ => _dragSourceCell is { HasGroupHeader: true }
                ? FanDragGhostStyle.GroupedFan
                : FanDragGhostStyle.TopLevelFan,
        };
    }

    private void UpdateDragGhostForPlacement(FanDragPlacement placement, Point current)
    {
        if (_dragGhost == null || _dragSourceControl == null) return;

        Control source = _dragSourceTopLevelControl ?? _dragSourceControl;
        FanDragGhostStyle style = ResolveDragGhostStyle(placement);
        double width = ResolveDragGhostWidth(style, placement, source);
        if (style != _dragGhostStyle)
        {
            _dragGhost.Child = CreateDragGhostContent(style, source);
            _dragGhostStyle = style;
        }

        ApplyDragGhostSurface(style);
        if (_dragGhost.Child is { } child)
            child.Width = width;

        _dragGhost.Width = width;
        Canvas.SetLeft(_dragGhost, ResolveDragGhostLeft(style, placement, source));
        _dragGhostHeight = MeasureDragGhostHeight(_dragGhost.Child, width, Math.Max(1, source.Bounds.Height));
        _dragGhost.Height = _dragGhostHeight;
        _dragPointerOffsetY = Math.Clamp(_dragPointerOffsetRatio * _dragGhostHeight, 0.0, _dragGhostHeight);
        MoveDragGhost(current);
    }

    private void ApplyDragGhostSurface(FanDragGhostStyle style)
    {
        if (_dragGhost == null) return;

        _dragGhost.Opacity = Layout.FullOpacity;
        if (style == FanDragGhostStyle.GroupedFan)
        {
            AppTheme theme = AppServices.Theme ?? AppTheme.Default;
            bool isLight = AppTheme.ResolveEffectiveIsLightTheme(_settings);
            _dragGhost.Background = TrayAppDotNETFlyoutUI.Brush(theme.ResolveGroupCardBackground(_settings, isLight));
            _dragGhost.CornerRadius = Rounded(Layout.CardCornerRadius);
            return;
        }

        _dragGhost.Background = Brushes.Transparent;
        _dragGhost.CornerRadius = Layout.ZeroCornerRadius;
    }

    private double ResolveDragGhostWidth(FanDragGhostStyle style, FanDragPlacement placement, Control source)
    {
        if (style == FanDragGhostStyle.GroupedFan)
            return ResolveGroupedFanGhostWidth(placement.GroupCell, source);

        if (_dragSourceTopLevelControl != null)
            return Math.Max(1, _dragSourceTopLevelControl.Bounds.Width);

        if (_dragSlots.Count > 0)
            return Math.Max(1, _dragSlots[0].Visual.Bounds.Width);

        return Math.Max(1, source.Bounds.Width);
    }

    private double ResolveGroupedFanGhostWidth(FanFlyoutCell? groupCell, Control source)
    {
        if (groupCell != null)
        {
            FanDragFanSlot? fanSlot = GroupFanSlots(groupCell, excludeDraggedFan: true).FirstOrDefault();
            if (fanSlot != null)
                return Math.Max(1, fanSlot.Visual.Bounds.Width);

            int groupIndex = IndexOfDragSlot(groupCell);
            if (groupIndex >= 0 && _dragSlots[groupIndex].Visual is Border border)
            {
                double leftInset = border.Padding.Left + Layout.FanRowGroupedMarginBase.Left;
                double rightInset = border.Padding.Right + Layout.FanRowGroupedMarginBase.Right;
                return Math.Max(1, border.Bounds.Width - leftInset - rightInset);
            }
        }

        return Math.Max(1, source.Bounds.Width);
    }

    private double ResolveDragGhostLeft(FanDragGhostStyle style, FanDragPlacement placement, Control source)
    {
        if (_dragOverlay == null) return 0;

        if (style == FanDragGhostStyle.GroupedFan
            && placement is { Kind: FanDragPlacementKind.IntoGroup, GroupCell: not null })
        {
            FanDragFanSlot? fanSlot = GroupFanSlots(placement.GroupCell, excludeDraggedFan: true).FirstOrDefault();
            if (fanSlot?.Visual.TranslatePoint(new Point(0, 0), _dragOverlay) is { } rowTop)
                return rowTop.X;

            int groupIndex = IndexOfDragSlot(placement.GroupCell);
            if (groupIndex >= 0 && _dragSlots[groupIndex].Visual is Border border
                && border.TranslatePoint(new Point(0, 0), _dragOverlay) is { } cardTop)
            {
                double leftInset = border.Padding.Left + Layout.FanRowGroupedMarginBase.Left;
                return cardTop.X + leftInset;
            }
        }

        Control leftSource = _dragSourceTopLevelControl ?? source;
        return leftSource.TranslatePoint(new Point(0, 0), _dragOverlay)?.X ?? 0;
    }

    private static double MeasureDragGhostHeight(Control? content, double width, double fallbackHeight)
    {
        if (content == null) return Math.Max(1, fallbackHeight);

        content.Measure(new Size(Math.Max(1, width), double.PositiveInfinity));
        return Math.Max(1, content.DesiredSize.Height > 0 ? content.DesiredSize.Height : fallbackHeight);
    }

    private void SnapshotDragSlots()
    {
        _dragSlots.Clear();
        _dragFanSlots.Clear();
        if (_cellStack == null) return;

        List<(FanFlyoutCell Cell, ProbeCard? ProbeCard, Control Visual, double Top, double Height,
            double GroupInsertionTop, double GroupDropBottom)> snapshot = [];
        foreach (Control visual in _cellStack.Children)
        {
            FanFlyoutCell? cell = visual.Tag switch
            {
                FanFlyoutCell fanCell => fanCell,
                ProbeCard _ when _draggedProbeCard != null => new FanFlyoutCell(null, []),
                _ => null,
            };
            if (cell == null) continue;

            ProbeCard? probeCard = visual.Tag as ProbeCard;
            Point? top = visual.TranslatePoint(new Point(0, 0), _cellStack);
            if (top == null) continue;
            double renderOffsetY = RenderTransformOffsetY(visual);
            double naturalTop = top.Value.Y - renderOffsetY;
            double height = Math.Max(1, visual.Bounds.Height);
            snapshot.Add((cell, probeCard, visual, naturalTop, height,
                ResolveGroupInsertionTop(cell, naturalTop, height, renderOffsetY),
                ResolveGroupDropBottom(cell, visual, naturalTop, height)));
            if (probeCard == null)
                SnapshotFanRows(cell, visual, renderOffsetY);
        }

        for (int i = 0; i < snapshot.Count; i++)
        {
            double slotHeight = i < snapshot.Count - 1
                ? Math.Max(snapshot[i].Height, snapshot[i + 1].Top - snapshot[i].Top)
                : snapshot[i].Height + Math.Max(0, snapshot[i].Visual.Margin.Bottom);
            _dragSlots.Add(new FanDragSlot(snapshot[i].Cell, snapshot[i].Visual, snapshot[i].Top,
                snapshot[i].Height, slotHeight, snapshot[i].GroupInsertionTop, snapshot[i].GroupDropBottom,
                snapshot[i].ProbeCard));
        }
    }

    private double ResolveGroupInsertionTop(
        FanFlyoutCell cell,
        double cardTop,
        double cardHeight,
        double cardRenderOffsetY)
    {
        if (!cell.HasGroupHeader) return cardTop;

        double fallback = cardTop + Math.Min(cardHeight * 0.35, Layout.FanButtonHeight);
        if (!_groupHeaderRefs.TryGetValue(cell, out GroupHeaderVisualRefs? refs) || _cellStack == null)
            return fallback;

        Point? curveBottom = refs.ActiveCurve.TranslatePoint(
            new Point(0, Math.Max(1, refs.ActiveCurve.Bounds.Height)), _cellStack);
        if (curveBottom == null) return fallback;

        double slack = Math.Max(2, Layout.DropMarkerHeight * 2.0);
        double minimum = cardTop + Math.Max(1, Layout.DropMarkerHeight * 3.0);
        double maximum = cardTop + Math.Max(minimum - cardTop, cardHeight * 0.55);
        return Math.Clamp(curveBottom.Value.Y - cardRenderOffsetY + slack, minimum, maximum);
    }

    private double ResolveGroupDropBottom(FanFlyoutCell cell, Control visual, double cardTop, double cardHeight)
    {
        double currentBottom = cardTop + cardHeight;
        if (!cell.HasGroupHeader
            || _groupDropPreview == null
            || visual is not Border { Child: StackPanel content }
            || !ReferenceEquals(_groupDropPreviewHost, content)
            || content.Children.IndexOf(_groupDropPreview) < 0)
            return currentBottom;

        return Math.Max(cardTop, currentBottom - ResolveGroupDropPreviewExtent());
    }

    private void SnapshotFanRows(FanFlyoutCell cell, Control card, double cardRenderOffsetY)
    {
        if (_cellStack == null || card is not Border { Child: StackPanel content }) return;

        int previewIndex = -1;
        double previewExtent = 0;
        if (_groupDropPreview != null
            && ReferenceEquals(_groupDropPreviewHost, content))
        {
            previewIndex = content.Children.IndexOf(_groupDropPreview);
            if (previewIndex >= 0)
                previewExtent = ResolveGroupDropPreviewExtent();
        }

        List<(Fan Fan, Control Visual, double Top, double Height, int FanIndex)> snapshot = [];
        for (int childIndex = 0; childIndex < content.Children.Count; childIndex++)
        {
            if (content.Children[childIndex] is not { } child) continue;
            if (child.Tag is not Fan fan) continue;
            int fanIndex = IndexOfFan(cell, fan);
            if (fanIndex < 0) continue;
            Point? top = child.TranslatePoint(new Point(0, 0), _cellStack);
            if (top == null) continue;
            double naturalTop = top.Value.Y - cardRenderOffsetY - RenderTransformOffsetY(child);
            naturalTop = NormalizePreviewShiftedTop(naturalTop, childIndex, previewIndex, previewExtent);
            snapshot.Add((fan, child, naturalTop, Math.Max(1, child.Bounds.Height), fanIndex));
        }

        for (int i = 0; i < snapshot.Count; i++)
        {
            _dragFanSlots.Add(new FanDragFanSlot(cell, snapshot[i].Fan, snapshot[i].Visual, snapshot[i].Top,
                snapshot[i].Height, snapshot[i].FanIndex));
        }
    }

    private static double RenderTransformOffsetY(Control control) =>
        control.RenderTransform is TranslateTransform translate ? translate.Y : 0.0;

    private double ResolveGroupDropPreviewExtent()
    {
        if (_groupDropPreview == null) return Math.Max(1, _dragSourceFanSlotHeight);

        double previewExtent = _groupDropPreview.Bounds.Height
                               + Math.Max(0, _groupDropPreview.Margin.Top)
                               + Math.Max(0, _groupDropPreview.Margin.Bottom);
        return previewExtent > 0 ? previewExtent : Math.Max(1, _dragSourceFanSlotHeight);
    }

    private static double NormalizePreviewShiftedTop(
        double measuredTop,
        int childIndex,
        int previewChildIndex,
        double previewExtent) =>
        previewChildIndex >= 0 && childIndex > previewChildIndex
            ? measuredTop - Math.Max(0, previewExtent)
            : measuredTop;

    private void ResolveDragSourceLayout(Control source)
    {
        _dragSourceCell = _draggedProbeCard != null
            ? null
            : _draggedGroupCell ?? (_draggedFan == null ? null : FindCellContainingFan(_draggedFan));
        _dragSourceTopLevelIndex = -1;
        _dragSourceTopLevelControl = null;

        if (_draggedProbeCard != null)
        {
            _dragSourceTopLevelIndex = IndexOfProbeDragSlot(_draggedProbeCard);
            if (_dragSourceTopLevelIndex >= 0)
                _dragSourceTopLevelControl = _dragSlots[_dragSourceTopLevelIndex].Visual;
        }
        else if (_dragSourceCell != null)
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
        _dragSourceFanSlotHeight = ResolveGroupedFanPreviewSlotHeight(source);
    }

    private double ResolveGroupedFanPreviewSlotHeight(Control source)
    {
        if (_draggedFan == null) return Math.Max(1, source.Bounds.Height + Math.Max(0, source.Margin.Bottom));

        FanDragFanSlot? sourceSlot = _dragFanSlots.FirstOrDefault(slot => ReferenceEquals(slot.Fan, _draggedFan));
        if (sourceSlot != null)
            return Math.Max(1, sourceSlot.Height + Math.Max(0, sourceSlot.Visual.Margin.Bottom));

        Control? preview = _groupDropPreview ?? CreateGroupedFanDropPlaceholder();
        if (preview == null) return Math.Max(1, source.Bounds.Height + Math.Max(0, source.Margin.Bottom));

        preview.Measure(new Size(Math.Max(1, source.Bounds.Width), double.PositiveInfinity));
        return Math.Max(1, preview.DesiredSize.Height + Math.Max(0, preview.Margin.Bottom));
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

    private static int IndexOfFan(FanFlyoutCell cell, Fan fan) => FanDragEngine.IndexOfFan(cell, fan);

    private static bool IsSameGroup(FanFlyoutCell? left, FanFlyoutCell? right)
        => FanDragEngine.IsSameGroup(left, right);

    private int IndexOfDragSlot(FanFlyoutCell cell) => FanDragEngine.IndexOfDragSlot(CreateDragSnapshot(), cell);

    /// <summary>
    /// Resolves the top-level drag slot for a probe card.
    /// </summary>
    private int IndexOfProbeDragSlot(ProbeCard probeCard)
    {
        for (int i = 0; i < _dragSlots.Count; i++)
        {
            if (ReferenceEquals(_dragSlots[i].ProbeCard, probeCard))
                return i;
        }

        return -1;
    }

    private void SetDragSourceOpacity(double opacity)
    {
        if (_dragSourceTopLevelControl != null) _dragSourceTopLevelControl.Opacity = opacity;
        else
            _dragSourceControl?.Opacity = opacity;
    }

    private void MoveDragGhost(Point current)
    {
        if (_dragGhost == null || _cellStack == null || _dragOverlay == null) return;
        Point overlayPoint = _cellStack.TranslatePoint(current, _dragOverlay) ?? current;
        Canvas.SetTop(_dragGhost, overlayPoint.Y - _dragPointerOffsetY);
    }

    private FanDragBounds StableDragBounds(Point current)
    {
        double sourceHeight = _dragPlacementSourceHeight > 0 ? _dragPlacementSourceHeight : Math.Max(1, _dragGhostHeight);
        double top = current.Y - _dragPlacementPointerOffsetY;
        return new FanDragBounds(top, top + sourceHeight, sourceHeight, top + sourceHeight / 2.0,
            current.Y, _dragMovingDown);
    }

    private void UpdateDragDirection(Point current)
    {
        double delta = current.Y - _lastDragPointerY;
        if (Math.Abs(delta) >= 0.5)
            _dragMovingDown = delta > 0;
        _lastDragPointerY = current.Y;
    }

    private void UpdateDragPreview(Point current)
    {
        if (_dragGhost == null || _cellStack == null) return;
        MoveDragGhost(current);
        UpdateDragDirection(current);

        FanDragEvaluation evaluation = EvaluateDrag(current);
        FanDragPlacement placement = evaluation.Placement;
        if (!placement.Equals(_dragPlacement))
        {
            _dragPlacement = placement;
            UpdateDragGhostForPlacement(placement, current);
            bool layoutChanged = ApplyDragPreview(placement);
            if (layoutChanged)
                RefreshDragGeometryAfterPreview(current);
        }

        if (EnableFanDragDebugOverlay)
            UpdateDragDebugOverlay(current);
        if (EnableFanDragInstrumentation)
            RecordDragInstrumentationMovement(current);
    }

    private void RefreshDragGeometryAfterPreview(Point current)
    {
        if (_cellStack == null) return;

        UpdateLayout();
        SnapshotDragSlots();
        UpdateDragGhostForPlacement(_dragPlacement, current);

        FanDragEvaluation refreshed = EvaluateDrag(current);
        FanDragPlacement refreshedPlacement = refreshed.Placement;
        if (refreshedPlacement.Equals(_dragPlacement)) return;

        _dragPlacement = refreshedPlacement;
        UpdateDragGhostForPlacement(refreshedPlacement, current);
        if (ApplyDragPreview(refreshedPlacement))
        {
            UpdateLayout();
            SnapshotDragSlots();
            UpdateDragGhostForPlacement(_dragPlacement, current);
        }

        if (EnableFanDragDebugOverlay)
            UpdateDragDebugOverlay(current);
    }

    private List<FanDragFanSlot> GroupFanSlots(FanFlyoutCell groupCell, bool excludeDraggedFan)
        => FanDragEngine.GroupFanSlots(CreateDragSnapshot(), groupCell, excludeDraggedFan);

    private FanDragEvaluation EvaluateDrag(Point current)
    {
        FanDragEvaluation evaluation = FanDragEngine.Evaluate(CreateDragSnapshot(), StableDragBounds(current));
        LastDragEvaluation = evaluation;
        return evaluation;
    }

    private FanDragSnapshot CreateDragSnapshot() => new(
        CreateEffectiveDragSlots(),
        CreateEffectiveDragFanSlots(),
        _draggedFan,
        _dragSourceCell,
        _dragSourceTopLevelControl,
        _dragSourceTopLevelIndex,
        _dragSourceSlotHeight,
        _dragSourceFanSlotHeight,
        _dragPlacementSourceHeight,
        _dragPointerOffsetRatio);

    private List<FanDragSlot> CreateEffectiveDragSlots()
    {
        List<FanDragSlot> slots = [];
        foreach (FanDragSlot slot in _dragSlots)
        {
            double offset = RenderTransformOffsetY(slot.Visual);
            slots.Add(offset == 0
                ? slot
                : slot with
                {
                    Top = slot.Top + offset,
                    GroupInsertionTop = slot.GroupInsertionTop + offset,
                    GroupDropBottom = slot.GroupDropBottom + offset,
                });
        }

        return slots;
    }

    private List<FanDragFanSlot> CreateEffectiveDragFanSlots()
    {
        List<FanDragFanSlot> slots = [];
        foreach (FanDragFanSlot slot in _dragFanSlots)
        {
            double offset = ResolveCellRenderOffset(slot.Cell) + RenderTransformOffsetY(slot.Visual);
            slots.Add(offset == 0 ? slot : slot with { Top = slot.Top + offset });
        }

        return slots;
    }

    private double ResolveCellRenderOffset(FanFlyoutCell cell)
    {
        foreach (FanDragSlot slot in _dragSlots)
        {
            if (ReferenceEquals(slot.Cell, cell)
                || IsSameGroup(slot.Cell, cell)
                || !slot.Cell.HasGroupHeader
                && !cell.HasGroupHeader
                && slot.Cell.Fans.Count == 1
                && cell.Fans.Count == 1
                && ReferenceEquals(slot.Cell.Fans[0], cell.Fans[0]))
                return RenderTransformOffsetY(slot.Visual);
        }

        return 0;
    }

    private bool ApplyDragPreview(FanDragPlacement placement)
    {
        bool layoutChanged = ResetDragPreviewTransforms();
        FanDragPreviewPlan preview = FanDragEngine.CalculatePreviewPlan(CreateDragSnapshot(), placement);
        foreach (FanDragSlotOffset offset in preview.TopLevelOffsets)
            SetDragSlotOffset(offset.Index, offset.Offset);

        layoutChanged |= preview.GroupDropPreviewCell != null
            ? InsertGroupDropPreview(preview.GroupDropPreviewCell, preview.GroupDropPreviewFanIndex)
            : RemoveGroupDropPreview();

        return layoutChanged;
    }

    private void SetDragSlotOffset(int index, double offset)
    {
        if (index < 0 || index >= _dragSlots.Count) return;
        _dragSlots[index].Visual.RenderTransform = offset == 0
            ? null
            : new TranslateTransform(0, offset);
    }

    private bool ResetDragPreviewTransforms()
    {
        bool layoutChanged = RemoveGroupDropPreview();
        foreach (FanDragSlot slot in _dragSlots)
            slot.Visual.RenderTransform = null;
        foreach (FanDragFanSlot slot in _dragFanSlots)
            slot.Visual.RenderTransform = null;
        return layoutChanged;
    }

    private bool InsertGroupDropPreview(FanFlyoutCell groupCell, int groupFanIndex)
    {
        if (_draggedFan == null)
            return false;

        int groupIndex = IndexOfDragSlot(groupCell);
        if (groupIndex < 0 || groupIndex >= _dragSlots.Count) return false;
        if (_dragSlots[groupIndex].Visual is not Border { Child: StackPanel content }) return false;

        if (_groupDropPreview == null)
        {
            _groupDropPreview = CreateGroupedFanDropPlaceholder();
            if (_groupDropPreview == null) return false;
            _groupDropPreview.Opacity = Layout.FullOpacity;
            _groupDropPreview.IsHitTestVisible = false;
        }

        bool layoutChanged = false;
        if (_groupDropPreviewHost != content)
            layoutChanged = RemoveGroupDropPreview();

        int insertionIndex = ResolveGroupDropPreviewChildIndex(groupCell, groupFanIndex, content);
        int currentIndex = content.Children.IndexOf(_groupDropPreview);
        if (currentIndex == insertionIndex) return layoutChanged;
        if (currentIndex >= 0)
        {
            content.Children.RemoveAt(currentIndex);
            if (currentIndex < insertionIndex) insertionIndex--;
        }

        content.Children.Insert(insertionIndex, _groupDropPreview);
        _groupDropPreviewHost = content;
        return true;
    }

    private int ResolveGroupDropPreviewChildIndex(
        FanFlyoutCell groupCell,
        int groupFanIndex,
        StackPanel content)
        => FanDragEngine.ResolveGroupDropPreviewChildIndex(
            groupCell,
            groupFanIndex,
            _dragSourceCell,
            _draggedFan,
            content.Children.Count);

    private bool RemoveGroupDropPreview()
    {
        if (_groupDropPreviewHost != null && _groupDropPreview != null)
        {
            _groupDropPreviewHost.Children.Remove(_groupDropPreview);
            _groupDropPreviewHost = null;
            return true;
        }

        _groupDropPreviewHost = null;
        return false;
    }

    private void UpdateDragDebugOverlay(Point current)
    {
        if (!EnableFanDragDebugOverlay) return;
        ClearDragDebugOverlay();
        if (_dragOverlay == null || _cellStack == null || _dragGhost == null) return;

        FanDragSnapshot snapshot = CreateDragSnapshot();
        FanDragBounds bounds = StableDragBounds(current);
        double overlayWidth = Math.Max(1, _dragOverlay.Bounds.Width > 0 ? _dragOverlay.Bounds.Width : Bounds.Width);
        foreach (FanDragDebugMarker marker in FanDragEngine.CalculateDebugMarkers(snapshot, bounds))
        {
            Point? top = _cellStack.TranslatePoint(new Point(0, marker.Y), _dragOverlay);
            if (top == null) continue;
            AddDragDebugLine(0, top.Value.Y, overlayWidth, Brushes.Red, Layout.DragDebugMarkerHeight,
                Layout.DragDebugMarkerOpacity);
        }

        Point? pointer = _cellStack.TranslatePoint(current, _dragOverlay);
        if (pointer == null) return;

        double ghostLeft = Canvas.GetLeft(_dragGhost);
        if (double.IsNaN(ghostLeft)) ghostLeft = 0;
        AddDragDebugLine(ghostLeft, pointer.Value.Y, Math.Max(1, _dragGhost.Width), Brushes.Yellow,
            Layout.DragDebugPointerHeight, Layout.DragDebugPointerOpacity);
    }

    private void AddDragDebugLine(double left, double top, double width, IBrush brush, double height, double opacity)
    {
        if (_dragOverlay == null) return;
        Border line = new()
        {
            Width = width,
            Height = height,
            Background = brush,
            Opacity = opacity,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(line, left);
        Canvas.SetTop(line, top - height / 2.0);
        _dragOverlay.Children.Add(line);
        _dragDebugVisuals.Add(line);
    }

    private void ClearDragDebugOverlay()
    {
        if (_dragOverlay != null)
        {
            foreach (Control visual in _dragDebugVisuals)
                _dragOverlay.Children.Remove(visual);
        }

        _dragDebugVisuals.Clear();
    }

    private void OnDragInstrumentationKeyDown(object? sender, KeyEventArgs e)
    {
        if (!EnableFanDragInstrumentation) return;
        if (!_dragInstrumentation.IsActive || _cellStack == null) return;

        Point current = _lastDragInstrumentationPoint ?? _dragStart;
        if (!_dragInstrumentation.RecordAnnotation(e.Key, current,
                point => CaptureDragInstrumentation(point, "key", interpolated: false)))
            return;

        e.Handled = true;
    }

    private void BeginDragInstrumentation()
    {
        if (!EnableFanDragInstrumentation) return;
        if (_dragInstrumentation.IsActive) return;

        string sourceKind = _draggedFan != null
            ? "fan"
            : _draggedGroupCell != null
                ? "group"
                : _draggedProbeCard != null
                    ? "probe"
                    : "unknown";
        string sourceName = _draggedFan?.DisplayName
                            ?? _draggedGroupCell?.GroupName
                            ?? _draggedProbeCard?.DisplayName
                            ?? DragInstrumentationCellLabel(_dragSourceCell)
                            ?? "<unknown>";
        _dragInstrumentation.Begin(new FanDragInstrumentationStart(
            sourceKind,
            sourceName,
            DragInstrumentationCellLabel(_dragSourceCell),
            _dragSourceTopLevelIndex,
            _dragSourceSlotHeight,
            _dragSourceFanSlotHeight));
    }

    private void RecordDragInstrumentationMovement(Point current)
    {
        if (!EnableFanDragInstrumentation) return;
        _lastDragInstrumentationPoint = current;
        _dragInstrumentation.RecordMovement(current,
            (point, interpolated) => CaptureDragInstrumentation(point, "move", interpolated));
    }

    private FanDragInstrumentationCapture CaptureDragInstrumentation(
        Point current,
        string stage,
        bool interpolated)
    {
        FanDragSnapshot snapshot = CreateDragSnapshot();
        FanDragBounds bounds = StableDragBounds(current);
        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, bounds);
        return new FanDragInstrumentationCapture(
            current,
            stage,
            interpolated,
            evaluation,
            _dragPlacement,
            _dragGhostStyle,
            CaptureDragInstrumentationSlots(_dragSlots),
            CaptureDragInstrumentationFanSlots(_dragFanSlots),
            CaptureDragInstrumentationDebugMarkers(FanDragEngine.CalculateDebugMarkers(snapshot, bounds)),
            CaptureDragInstrumentationGhost(),
            CaptureDragInstrumentationGroupPreview());
    }

    private List<FanDragInstrumentationSlot> CaptureDragInstrumentationSlots(List<FanDragSlot> dragSlots)
    {
        List<FanDragInstrumentationSlot> slots = [];
        for (int i = 0; i < dragSlots.Count; i++)
        {
            FanDragSlot slot = dragSlots[i];
            double renderOffsetY = RenderTransformOffsetY(slot.Visual);
            slots.Add(new FanDragInstrumentationSlot(
                i,
                slot.ProbeCard != null ? "probe" : slot.Cell.HasGroupHeader ? "group" : "fan",
                slot.ProbeCard?.DisplayName ?? DragInstrumentationCellLabel(slot.Cell) ?? "<unknown>",
                slot.Top,
                slot.Top + renderOffsetY,
                slot.Height,
                slot.SlotHeight,
                renderOffsetY,
                slot.GroupInsertionTop,
                slot.GroupDropBottom,
                IsDragInstrumentationSource(slot)));
        }

        return slots;
    }

    private List<FanDragInstrumentationFanSlot> CaptureDragInstrumentationFanSlots(
        IReadOnlyList<FanDragFanSlot> dragFanSlots)
    {
        List<FanDragInstrumentationFanSlot> slots = [];
        foreach (FanDragFanSlot slot in dragFanSlots)
        {
            double renderOffsetY = ResolveCellRenderOffset(slot.Cell) + RenderTransformOffsetY(slot.Visual);
            slots.Add(new FanDragInstrumentationFanSlot(
                slot.Cell.GroupName,
                slot.Fan.DisplayName,
                slot.FanIndex,
                slot.Top,
                slot.Top + renderOffsetY,
                slot.Height,
                renderOffsetY,
                ReferenceEquals(slot.Fan, _draggedFan)));
        }

        return slots;
    }

    private static List<FanDragInstrumentationDebugMarker> CaptureDragInstrumentationDebugMarkers(
        IReadOnlyList<FanDragDebugMarker> markers)
    {
        List<FanDragInstrumentationDebugMarker> captured = [];
        foreach (FanDragDebugMarker marker in markers)
            captured.Add(new FanDragInstrumentationDebugMarker(marker.Y, marker.Placement));
        return captured;
    }

    private bool IsDragInstrumentationSource(FanDragSlot slot)
    {
        if (_draggedProbeCard != null)
            return ReferenceEquals(slot.ProbeCard, _draggedProbeCard);
        if (_dragSourceTopLevelControl != null)
            return ReferenceEquals(slot.Visual, _dragSourceTopLevelControl);
        if (_dragSourceCell == null) return false;
        return ReferenceEquals(slot.Cell, _dragSourceCell) || IsSameGroup(slot.Cell, _dragSourceCell);
    }

    private FanDragInstrumentationGhost? CaptureDragInstrumentationGhost()
    {
        if (_dragGhost == null) return null;

        double left = Canvas.GetLeft(_dragGhost);
        double top = Canvas.GetTop(_dragGhost);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
        return new FanDragInstrumentationGhost(
            left,
            top,
            _dragGhost.Bounds.Width,
            _dragGhost.Bounds.Height,
            _dragGhost.Opacity);
    }

    private FanDragInstrumentationGroupPreview? CaptureDragInstrumentationGroupPreview()
    {
        if (_groupDropPreview == null || _groupDropPreviewHost == null || _cellStack == null) return null;

        FanFlyoutCell? groupCell = null;
        foreach (FanDragSlot slot in _dragSlots)
        {
            if (slot.Visual is Border { Child: StackPanel content }
                && ReferenceEquals(content, _groupDropPreviewHost))
            {
                groupCell = slot.Cell;
                break;
            }
        }

        Point? top = _groupDropPreview.TranslatePoint(new Point(0, 0), _cellStack);
        return new FanDragInstrumentationGroupPreview(
            groupCell?.GroupName,
            _groupDropPreviewHost.Children.IndexOf(_groupDropPreview),
            top?.Y ?? 0,
            _groupDropPreview.Bounds.Height,
            ResolveGroupDropPreviewExtent());
    }

    private static string? DragInstrumentationCellLabel(FanFlyoutCell? cell)
    {
        if (cell == null) return null;
        if (cell.HasGroupHeader) return cell.GroupName;
        return cell.Fans.Count == 1 ? cell.Fans[0].DisplayName : "<empty>";
    }

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
                UpdateDragDirection(current);
                _dragPlacement = EvaluateDrag(current).Placement;
            }

            if (_draggedFan != null) ApplyFanDrop(_draggedFan, _dragPlacement);
            else if (_draggedGroupCell != null) ApplyGroupDrop(_draggedGroupCell, _dragPlacement.TopLevelIndex);
            else if (_draggedProbeCard != null) ApplyProbeDrop(_draggedProbeCard, _dragPlacement.TopLevelIndex);
            _dragInstrumentation.End("complete");
            pointer.Capture(null);
        }
        finally
        {
            _isCompletingDrag = false;
            ClearDragState();
        }

        ExecuteFanRebuild(reposition: true);
    }

    private void CancelDrag()
    {
        _dragInstrumentation.End("cancel");
        ClearDragState();
    }

    private void ClearDragState()
    {
        _dragInstrumentation.End("clear");
        _dragSourceControl?.Opacity = 1;
        _dragSourceTopLevelControl?.Opacity = 1;
        ResetDragPreviewTransforms();
        if (_dragOverlay != null)
        {
            ClearDragDebugOverlay();
            if (_dragGhost != null) _dragOverlay.Children.Remove(_dragGhost);
        }

        RemoveGroupDropPreview();
        _dragGhost = null;
        _groupDropPreview = null;
        _dragSlots.Clear();
        _dragFanSlots.Clear();
        _dragSourceControl = null;
        _dragSourceTopLevelControl = null;
        _draggedFan = null;
        _draggedGroupCell = null;
        _draggedProbeCard = null;
        _dragSourceCell = null;
        _dragPlacement = FanDragPlacement.None;
        LastDragEvaluation = null;
        _dragGhostStyle = FanDragGhostStyle.None;
        _dragSourceTopLevelIndex = -1;
        _dragPointerOffsetRatio = 0;
        _dragPlacementPointerOffsetY = 0;
        _dragPlacementSourceHeight = 0;
        _dragSourceSlotHeight = 0;
        _dragSourceFanSlotHeight = 0;
        _dragGhostHeight = 0;
        _lastDragPointerY = 0;
        _lastDragInstrumentationPoint = null;
        _dragMovingDown = true;
    }

    private void ApplyFanDrop(Fan fan, FanDragPlacement placement)
    {
        FanFlyoutCell? groupCell = placement.Kind == FanDragPlacementKind.IntoGroup ? placement.GroupCell : null;
        if (groupCell?.GroupName != null)
            ApplyFanDropIntoGroup(fan, groupCell, placement.GroupFanIndex);
        else
            ApplyFanDropTopLevel(fan, placement.TopLevelIndex);
    }

    private void ApplyFanDropIntoGroup(Fan fan, FanFlyoutCell groupCell, int groupFanIndex)
    {
        if (groupCell.GroupName == null) return;

        bool sameGroup = IsSameGroup(_dragSourceCell, groupCell);
        fan.Group = groupCell.GroupName;
        if (!sameGroup && groupCell.GroupSettings != null)
        {
            fan.RPMMode = groupCell.GroupRPMMode;
            fan.CurrentControlMode = groupCell.GroupSettings.CurrentControlMode;
            if (fan.CurrentControlMode == FanControlMode.Manual)
            {
                fan.FanDisplayedValue =
                    Math.Clamp(groupCell.GroupSettings.FanDisplayedValue, 0, fan.FanSliderMaximum);
            }
        }

        List<FanFlyoutCell> arranged = MoveFanIntoGroup([.. _cells], fan, groupCell, groupFanIndex);
        ApplyTopLevelDisplayOrder(arranged);
        ApplyGroupedFanDisplayOrders(arranged);
        SaveGroupChanges();
    }

    private void ApplyFanDropTopLevel(Fan fan, int targetIndex)
    {
        fan.Group = null;
        List<FanFlyoutCell> arranged = MoveFanToTopLevel([.. _cells], fan, targetIndex);
        ApplyTopLevelDisplayOrder(arranged);
        ApplyGroupedFanDisplayOrders(arranged);
        SaveGroupChanges();
    }

    private static List<FanFlyoutCell> MoveFanToTopLevel(List<FanFlyoutCell> cells, Fan fan, int targetIndex)
        => FanDragEngine.MoveFanToTopLevel(cells, fan, targetIndex);

    private static List<FanFlyoutCell> MoveFanIntoGroup(
        List<FanFlyoutCell> cells,
        Fan fan,
        FanFlyoutCell targetGroup,
        int targetFanIndex)
        => FanDragEngine.MoveFanIntoGroup(cells, fan, targetGroup, targetFanIndex);

    private void ApplyGroupDrop(FanFlyoutCell cell, int targetIndex)
    {
        List<FanFlyoutCell> ordered = [.. _cells.Where(c => !ReferenceEquals(c.GroupSettings, cell.GroupSettings))];
        ordered.Insert(Math.Clamp(targetIndex, 0, ordered.Count), cell);
        ApplyTopLevelDisplayOrder(ordered);
        SaveGroupChanges();
    }

    /// <summary>
    /// Applies a top-level drop for a probe card.
    /// </summary>
    private void ApplyProbeDrop(ProbeCard probeCard, int targetIndex)
    {
        List<FlyoutTopLevelItem> ordered = [.. BuildTopLevelOrder()
            .Where(item => !ReferenceEquals(item.ProbeCard, probeCard))];
        ordered.Insert(Math.Clamp(targetIndex, 0, ordered.Count), FlyoutTopLevelItem.Probe(probeCard));
        ApplyTopLevelDisplayOrder(ordered);
        SaveGroupChanges();
        SaveProbeCardChanges();
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

    /// <summary>
    /// Applies display order across fan/group/probe top-level cards.
    /// </summary>
    private void ApplyTopLevelDisplayOrder(IEnumerable<FlyoutTopLevelItem> orderedItems)
    {
        _suppressFanRebuild = true;
        try
        {
            _groupNames.Clear();
            _probeCards.Clear();
            int index = 0;
            foreach (FlyoutTopLevelItem item in orderedItems)
            {
                if (item.ProbeCard != null)
                {
                    item.ProbeCard.DisplayOrder = index++;
                    _probeCards.Add(item.ProbeCard);
                    continue;
                }

                FanFlyoutCell? cell = item.Cell;
                if (cell == null) continue;
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

    /// <summary>
    /// Builds current top-level visual order without creating controls.
    /// </summary>
    private List<FlyoutTopLevelItem> BuildTopLevelOrder()
    {
        List<(FlyoutTopLevelItem Item, int Order, int Sequence, string Tie)> slots = [];
        int sequence = 0;
        foreach (FanFlyoutCell cell in _cells)
        {
            slots.Add((FlyoutTopLevelItem.FanCell(cell), NormalizeDisplayOrder(ResolveCellDisplayOrder(cell)),
                sequence++, ResolveCellTie(cell)));
        }

        foreach (ProbeCard probeCard in _probeCards)
        {
            slots.Add((FlyoutTopLevelItem.Probe(probeCard), NormalizeDisplayOrder(probeCard.DisplayOrder),
                sequence++, probeCard.DisplayName));
        }

        return
        [
            .. slots
                .OrderBy(static slot => slot.Order)
                .ThenBy(static slot => slot.Sequence)
                .ThenBy(static slot => slot.Tie, StringComparer.OrdinalIgnoreCase)
                .Select(static slot => slot.Item)
        ];
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
        if (e.ClickCount != 2 || sender is not TextBlock { Tag: Fan fan } text) return;
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
        if (e.ClickCount != 2 || sender is not TextBlock { Tag: FanFlyoutCell cell } text) return;
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

    /// <summary>
    /// Starts inline editing for a probe-card title.
    /// </summary>
    private void ProbeNameTextPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not TextBlock { Tag: ProbeCard probeCard } text) return;
        if (text.Parent is not Grid grid) return;
        TextBox? box = grid.Children.OfType<TextBox>().FirstOrDefault();
        if (box == null) return;
        box.Tag = probeCard;
        box.Text = probeCard.DisplayName;
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
        SpeedSliderRange sliderRange = ResolveFanSliderRange(fan);
        box.Text = FanSliderValueText(fan, ResolveFanCurveSliderValue(fan, sliderRange), sliderRange);
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

    /// <summary>
    /// Commits or cancels inline editing for a probe-card title.
    /// </summary>
    private void ProbeNameEditKeyDown(object? sender, KeyEventArgs e)
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
            CommitProbeNameEdit(box);
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

    /// <summary>
    /// Commits a probe-card title edit when focus leaves the editor.
    /// </summary>
    private void ProbeNameEditLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box) CommitProbeNameEdit(box);
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
        ExecuteFanRebuild(reposition: false);
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

    /// <summary>
    /// Persists a probe-card title edit.
    /// </summary>
    private void CommitProbeNameEdit(TextBox box)
    {
        if (box.Tag is ProbeCard probeCard)
        {
            string trimmed = (box.Text ?? string.Empty).Trim();
            if (trimmed.Length > 0 && !string.Equals(trimmed, probeCard.Name, StringComparison.Ordinal))
                probeCard.Name = CreateUniqueProbeCardName(trimmed, probeCard.Name);
            SaveProbeCardChanges();
        }

        CancelInlineEdit(box);
        RebuildVisual();
    }

    private void CommitFanValueEdit(TextBox box)
    {
        if (box.Tag is Fan fan)
        {
            bool wasRelinquished = IsFanControlRelinquished(fan);
            SpeedSliderRange sliderRange = ResolveFanSliderRange(fan);
            if (TryParseFanDisplayedValue(box.Text, fan, sliderRange, wasRelinquished, out int value))
            {
                fan.CurrentControlMode = FanControlMode.Manual;
                fan.FanDisplayedValue = value;
                AppServices.LHMService?.PersistLiveState();
            }
        }

        CancelInlineEdit(box);
        RebuildVisual();
    }

    /// <summary>
    /// Parses inline fan speed edits against the same range used by the visible slider.
    /// </summary>
    private static bool TryParseFanDisplayedValue(
        string? text,
        Fan fan,
        SpeedSliderRange sliderRange,
        bool sourceIsDutyCycle,
        out int value)
    {
        string raw = (text ?? string.Empty).Trim();
        bool hasPercent = raw.Contains('%');
        bool hasRPM = raw.Contains("RPM", StringComparison.OrdinalIgnoreCase);
        string normalized = raw.Replace("%", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("RPM", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            if (fan.RPMMode && !hasRPM && (hasPercent || sourceIsDutyCycle && parsed <= 100))
            {
                double duty = Math.Clamp(parsed, 0, 100);
                value = ClampSliderValue(duty / 100.0 * Math.Max(1, fan.FanSliderMaximum), sliderRange);
                return true;
            }

            value = ClampSliderValue(parsed, sliderRange);
            return true;
        }

        value = ClampSliderValue(fan.FanDisplayedValue, sliderRange);
        return false;
    }

    private static void CancelInlineEdit(TextBox box)
    {
        box.Tag = null;
        box.IsVisible = false;
        if (box.Parent is Grid grid)
        {
            foreach (Control control in grid.Children)
            {
                if (control is TextBlock or Viewbox)
                    control.IsVisible = true;
            }
        }
    }

    private static T? FindVisualAncestor<T>(Visual visual)
        where T : Visual
    {
        for (Visual? current = visual; current != null; current = current.GetVisualParent())
        {
            if (current is T match)
                return match;
        }

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
            GetOrCreateGroupSettings(newName);

        if (_lhmService != null)
        {
            foreach (Fan fan in _lhmService.Fans)
            {
                if (string.Equals(fan.Group, oldName, StringComparison.OrdinalIgnoreCase))
                    fan.Group = newName;
            }
        }

        SaveGroupChanges();
        ExecuteFanRebuild(reposition: false);
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

    /// <summary>
    /// Opens or activates the selector window for a probe card.
    /// </summary>
    private void ToggleProbeSelectorWindow(ProbeCard probeCard)
    {
        if (_probeSelectorWindows.TryGetValue(probeCard, out ProbeDataSelectorWindow? existing))
        {
            if (!existing.IsVisible)
                existing.Show(this);
            existing.Activate();
            return;
        }

        ProbeDataSelectorWindow window = new(probeCard, _settings, OnProbeCardSelectorChanged)
        {
            Topmost = Topmost,
            ShowInTaskbar = false,
        };
        _probeSelectorWindows[probeCard] = window;
        window.Closed += ProbeSelectorWindowClosed;
        window.Show(this);
        window.Activate();
    }

    /// <summary>
    /// Persists selector changes and refreshes the flyout card.
    /// </summary>
    private void OnProbeCardSelectorChanged(ProbeCard probeCard)
    {
        SaveProbeCardChanges();
        RebuildVisual();
        QueuePositionNearTray();
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
            Palette = FanSettingsWindow.CreatePalette(
                AppServices.Theme,
                _settings,
                AppTheme.ResolveEffectiveIsLightTheme(_settings)),
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
            PromptClosed = NotifyChildWindowClosedFromDeactivation,
        });
    }

    private bool IsUpdateCardVisible =>
        _settings.ShowUpdateButtonInFlyout && AppServices.UpdateCheckService?.AvailableUpdate != null;

    private void NotifyUpdateStateChanged() => RequestFanRebuild();

    private void OpenCurveEditor(Fan fan)
    {
        Curve curve = fan.AssignedCurve ?? CreateCurveForFan(fan);
        AssignCurveForEditor(fan, curve.CurveName);
        fan.CurrentControlMode = FanControlMode.Curve;
        PersistAndNotifyCurveEditorLaunch();

        FanCurveEditorWindow window = new(fan, curve, _settings)
        {
            Topmost = Topmost,
            ShowInTaskbar = false,
        };
        _fanCurveEditorWindows.Add(window);
        window.Closed += FanCurveEditorWindowClosed;
        window.Show(this);
        window.Activate();
    }

    private void FanCurveEditorWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not FanCurveEditorWindow window) return;
        window.Closed -= FanCurveEditorWindowClosed;
        _fanCurveEditorWindows.Remove(window);
        NotifyChildWindowClosedFromDeactivation();
    }

    /// <summary>
    /// Removes a closed probe selector from the child-window catalog.
    /// </summary>
    private void ProbeSelectorWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not ProbeDataSelectorWindow window) return;
        window.Closed -= ProbeSelectorWindowClosed;
        ProbeCard? probeCard = _probeSelectorWindows.FirstOrDefault(kv => ReferenceEquals(kv.Value, window)).Key;
        if (probeCard != null)
            _probeSelectorWindows.Remove(probeCard);
        NotifyChildWindowClosedFromDeactivation();
    }

    private static Curve CreateCurveForFan(Fan fan)
    {
        string name = UniqueCurveName($"{fan.DisplayName} Curve");
        int maxRPM = fan.MaxRPM > 0 ? fan.MaxRPM : fan.CurrentRPM > 0 ? Math.Max(100, fan.CurrentRPM) : 3000;
        Curve curve = new()
        {
            CurveName = name,
            RPMMode = fan.RPMMode,
            MaxRPM = maxRPM,
            MinRPM = 0,
            MaxDutyCycle = 100,
            MinDutyCycle = 0,
            SmoothingFactor = 50,
            PreventDecreasing = true,
            SelectedDataSourceKey = DefaultCurveDataSourceKey(),
        };
        Curve.Register(curve);
        return curve;
    }

    private void AssignCurveForEditor(Fan fan, string curveName)
    {
        Curve? curve = Curve.Find(curveName);
        IEnumerable<Fan>? liveFans = _lhmService?.Fans;
        IEnumerable<Fan> fans = liveFans ?? [fan];
        if (!string.IsNullOrWhiteSpace(fan.Group) && FanGroup.Find(fan.Group) is { } group)
        {
            group.AssignedCurveName = curveName;
            group.CurrentControlMode = FanControlMode.Curve;
            FanCurveModeSync.ApplyToGroup(group, fans, curve);
            FanGroup.Register(group);
            return;
        }

        fan.AssignedCurveName = curveName;
        FanCurveModeSync.ApplyToFan(fan, curve);
    }

    private static string UniqueCurveName(string baseName)
    {
        string normalized = string.IsNullOrWhiteSpace(baseName) ? "Fan Curve" : baseName.Trim();
        string candidate = normalized;
        int suffix = 2;
        while (Curve.Find(candidate) != null)
            candidate = $"{normalized} {suffix++}";
        return candidate;
    }

    private static string DefaultCurveDataSourceKey()
    {
        DataSource? source = DataSource.DataSources.Values
            .OrderByDescending(static s => s.DataSourceType == DataSourceTypeEnum.Temperature)
            .ThenBy(static s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return source?.DataSourceKey ?? string.Empty;
    }

    private void PersistAndNotifyCurveEditorLaunch()
    {
        AppServices.LHMService?.PersistLiveState(save: false);
        _settings.SyncFanControlRegistriesForSave();
        _settings.Save();
        _settings.RaiseChanged();
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
        {
            if (_fanPropertiesWindows.TryGetValue(fan, out FanPropertiesWindow? window) && window.IsVisible)
                visible.Add(window);
        }

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

    /// <summary>
    /// Closes all probe selector child windows.
    /// </summary>
    private void ForceCloseAllProbeSelectorWindows()
    {
        foreach (ProbeDataSelectorWindow window in _probeSelectorWindows.Values.ToArray())
            window.Close();
        _probeSelectorWindows.Clear();
    }

    private void OnFansChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        WireFanPropertySubscriptions();
        RequestFanRebuild();
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
            if (!IsVisible) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsVisible) return;
                try { RefreshFanRowVisuals(fan, e.PropertyName); }
                catch (Exception ex)
                {
                    TADNLog.Log($"FanFlyoutWindow.RefreshFanRowVisuals: {ex.GetType().Name}: {ex.Message}");
                }
            }, DispatcherPriority.Background);
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

    private void OnPollTickCompleted()
    {
        if (!IsVisible || IsPointerGestureActive) return;

        foreach (Fan fan in _fanRowRefs.Keys.ToArray())
            RefreshFanRowVisuals(fan, null);
        RefreshGroupHeaderVisuals();
        RefreshProbeCardVisuals();
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
        RequestFanRebuild();
    });

    private bool IsPointerGestureActive =>
        _activeSliderDrags > 0
        || _draggedFan != null
        || _draggedGroupCell != null
        || _draggedProbeCard != null
        || _isDraggingWindow
        || _undockButtonPointerCaptured;

    /// <summary>Queues one coalesced fan rebuild and defers hidden warm-window churn.</summary>
    private void RequestFanRebuild()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RequestFanRebuild, DispatcherPriority.Background);
            return;
        }

        if (!IsVisible && !IsWarmPriming || IsPointerGestureActive || _isRebuildingVisual)
        {
            _pendingFanRebuild = true;
            return;
        }

        if (_fanRebuildQueued) return;

        _fanRebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _fanRebuildQueued = false;
            if (!IsVisible && !IsWarmPriming || IsPointerGestureActive || _isRebuildingVisual)
            {
                _pendingFanRebuild = true;
                return;
            }

            ExecuteFanRebuild(reposition: true);
        }, DispatcherPriority.Background);
    }

    /// <summary>Runs a fan rebuild and keeps failures contained to the flyout.</summary>
    private void ExecuteFanRebuild(bool reposition)
    {
        try
        {
            RebuildCells();
            RebuildVisual();
            _pendingFanRebuild = false;
            if (reposition) QueuePositionNearTray();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"FanFlyoutWindow.ExecuteFanRebuild: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Runs a deferred rebuild after pointer gestures complete.</summary>
    private void FlushPendingFanRebuild()
    {
        if (!_pendingFanRebuild || IsPointerGestureActive) return;
        if (!IsVisible && !IsWarmPriming) return;

        ExecuteFanRebuild(reposition: true);
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

    /// <summary>
    /// Loads probe cards from persisted settings.
    /// </summary>
    private void LoadProbeCards()
    {
        _probeCards.Clear();
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProbeCard probeCard in _settings.ProbeCards
                     .Where(static c => c != null)
                     .OrderBy(static c => NormalizeDisplayOrder(c.DisplayOrder))
                     .ThenBy(static c => c.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(probeCard.Name))
                probeCard.Name = CreateUniqueProbeCardName();
            if (!names.Add(probeCard.Name))
                probeCard.Name = CreateUniqueProbeCardName(probeCard.Name);
            _probeCards.Add(probeCard);
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
        {
            foreach (Fan fan in _lhmService.Fans)
            {
                if (string.IsNullOrWhiteSpace(fan.Group)) continue;
                if (!string.IsNullOrWhiteSpace(ignoredName) &&
                    string.Equals(fan.Group, ignoredName, StringComparison.OrdinalIgnoreCase)) continue;
                used.Add(fan.Group);
            }
        }

        if (!used.Contains(normalizedBase)) return normalizedBase;
        int suffix = 2;
        while (used.Contains($"{normalizedBase}_{suffix}")) suffix++;
        return $"{normalizedBase}_{suffix}";
    }

    /// <summary>
    /// Creates a probe-card name that is unique in the current flyout.
    /// </summary>
    private string CreateUniqueProbeCardName(string baseName = NewProbeCardBaseName, string? ignoredName = null)
    {
        string normalizedBase = string.IsNullOrWhiteSpace(baseName) ? NewProbeCardBaseName : baseName.Trim();
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProbeCard probeCard in _probeCards)
        {
            if (!string.IsNullOrWhiteSpace(ignoredName) &&
                string.Equals(probeCard.Name, ignoredName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(probeCard.Name)) used.Add(probeCard.Name);
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

    /// <summary>
    /// Persists probe-card changes and notifies listeners.
    /// </summary>
    private void SaveProbeCardChanges()
    {
        _settings.ProbeCards =
        [
            .. _probeCards
                .OrderBy(static c => NormalizeDisplayOrder(c.DisplayOrder))
                .ThenBy(static c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
        ];
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

            foreach (ProbeCard probeCard in _probeCards)
                if (probeCard.DisplayOrder >= 0) probeCard.DisplayOrder += offset;

            if (_lhmService == null) return;
            foreach (Fan fan in _lhmService.Fans)
            {
                if (string.IsNullOrWhiteSpace(fan.Group) && fan.FlyoutDisplayOrder >= 0)
                    fan.FlyoutDisplayOrder += offset;
            }
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
        if (_undockButtonPointerCaptured || _draggedFan != null || _draggedGroupCell != null
            || _draggedProbeCard != null) return;
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

    private SliderThumbGlyphOption ResolveCurveSliderThumbOption()
    {
        List<SliderThumbGlyphOption> options =
            _settings.SliderThumbOptions is { Count: > 0 } list
                ? list
                : SliderThumbGlyphOption.CreateDefaults();

        return options.FirstOrDefault(o => o.IsGlyph && o.Name == _settings.CurveSliderThumbGlyph)
               ?? options.FirstOrDefault(static o => o is { IsGlyph: true, Name: "Diamond" })
               ?? options.First(static o => o.IsGlyph);
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
        Thickness UpdateInstallButtonPadding,
        double HeaderAddGroupIconSize,
        double HeaderAddGroupFontSize,
        double HeaderAddProbeFontSize,
        double HeaderAddGlyphFontSize,
        TranslateTransform HeaderAddGlyphTransform,
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
        double SubtitleOpacity,
        Thickness SubtitleMargin,
        Thickness FanNameStackGroupedMargin,
        Thickness FanNameStackUngroupedMargin,
        Thickness TelemetryMargin,
        double RPMFontSize,
        double RPMOpacity,
        double ControllerFontSize,
        double ControllerOpacity,
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
        double GroupNameOpacity,
        Thickness GroupTitleMargin,
        double GroupDeleteFontSize,
        Thickness GroupModeMargin,
        Thickness GroupSliderRowMargin,
        Thickness GroupValueGridMargin,
        double ProbeButtonWidth,
        double ProbeButtonHeight,
        double ProbeButtonFontSize,
        Thickness ProbeButtonMargin,
        double ProbeNameFontSize,
        Thickness ProbeNameStackMargin,
        Thickness ProbeRowsMargin,
        Thickness ProbeRowMargin,
        double ProbeRowGlyphWidth,
        double ProbeRowGlyphFontSize,
        double ProbeRowTextFontSize,
        double ProbeRowValueMinWidth,
        double ProbeEmptyFontSize,
        double ProbeEmptyOpacity,
        double ConfirmTitleFontSize,
        Thickness ConfirmTitleMargin,
        Thickness ConfirmMessageMargin,
        Thickness ConfirmCancelMargin,
        Thickness ConfirmBorderThickness,
        CornerRadius ConfirmCornerRadius,
        Thickness ConfirmPadding,
        double ConfirmMinWidth,
        double ConfirmMaxWidth,
        double FullOpacity,
        double RelinquishedControlOpacity,
        double InactiveSliderValueOpacity,
        double DragGhostOpacity,
        double DragGhostShadowOffsetY,
        double DragGhostShadowBlur,
        double DropMarkerHeight,
        CornerRadius DropMarkerCornerRadius,
        double DragSourceOpacity,
        double DragDebugMarkerHeight,
        double DragDebugMarkerOpacity,
        double DragDebugPointerHeight,
        double DragDebugPointerOpacity,
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

        public static FlyoutLayout From(FanFlyoutWindow owner)
        {
            FlyoutAxamlProperties axaml = owner.AxamlFlyout;
            return new FlyoutLayout(
                (int)Math.Round(axaml.EdgePadding),
                axaml.DragThreshold,
                axaml.SnapTolerancePercent,
                axaml.WorkAreaMinHeight,
                (int)Math.Round(axaml.PixelMinSize),
                axaml.OffscreenPosition,
                axaml.FallbackWorkAreaX,
                axaml.FallbackWorkAreaY,
                axaml.FallbackWorkAreaWidth,
                axaml.FallbackWorkAreaHeight,
                axaml.FanPropertiesRowsPerColumn,
                axaml.FanPropertiesGap,
                axaml.MaxSettingSpacing,
                axaml.ZeroThickness,
                axaml.ZeroCornerRadius,
                axaml.RootBorderThickness,
                axaml.RootCornerRadius,
                axaml.RootInnerCornerRadius,
                axaml.RootInnerMargin,
                axaml.RootShadowOffsetY,
                axaml.RootShadowBlur,
                axaml.HeaderHeight,
                axaml.HeaderMargin,
                axaml.HeaderWideColumnWidth,
                axaml.HeaderNarrowColumnWidth,
                axaml.HeaderButtonWidth,
                axaml.HeaderButtonHeight,
                axaml.HeaderButtonFontSize,
                axaml.HeaderManagerButtonFontSize,
                axaml.UpdateInstallButtonPadding,
                axaml.HeaderAddGroupIconSize,
                axaml.HeaderAddGroupFontSize,
                axaml.HeaderAddProbeFontSize,
                axaml.HeaderAddGlyphFontSize,
                axaml.HeaderAddGlyphTransform,
                axaml.ProfileLabelFontSize,
                axaml.ProfileUnderlineWidth,
                axaml.ProfileUnderlineHeight,
                axaml.ProfileUnderlineCornerRadius,
                axaml.ProfileUnderlineMargin,
                axaml.ProfileButtonWidth,
                axaml.ProfileButtonHeight,
                axaml.UndockFontSize,
                axaml.HeaderButtonCornerRadius,
                axaml.EmptyTextFontSize,
                axaml.EmptyTextOpacity,
                axaml.CardBorderThickness,
                axaml.CardCornerRadius,
                axaml.CardPadding,
                axaml.FanRowGroupedMarginBase,
                axaml.FanButtonWidth,
                axaml.FanButtonHeight,
                axaml.FanButtonFontSize,
                axaml.FanButtonGroupedMargin,
                axaml.FanButtonUngroupedMargin,
                axaml.FanNameFontSize,
                axaml.FanSubtitleFontSize,
                axaml.SubtitleOpacity,
                axaml.SubtitleMargin,
                axaml.FanNameStackGroupedMargin,
                axaml.FanNameStackUngroupedMargin,
                axaml.TelemetryMargin,
                axaml.RPMFontSize,
                axaml.RPMOpacity,
                axaml.ControllerFontSize,
                axaml.ControllerOpacity,
                axaml.ControllerMargin,
                axaml.ModeButtonWidth,
                axaml.ModeButtonHeight,
                axaml.ModeButtonFontSize,
                axaml.ModeButtonGroupedMargin,
                axaml.ModeButtonUngroupedMargin,
                axaml.SliderRowHeight,
                axaml.SliderRowMargin,
                axaml.ValueGridMargin,
                axaml.ValueFontSize,
                axaml.SliderHitTestVerticalPadding,
                axaml.GroupIconWidth,
                axaml.GroupIconHeight,
                axaml.GroupIconFontSize,
                axaml.GroupHeaderButtonWidth,
                axaml.GroupHeaderButtonHeight,
                axaml.GroupExpandFontSize,
                axaml.GroupHeaderButtonMargin,
                axaml.GroupNameFontSize,
                axaml.GroupNameOpacity,
                axaml.GroupTitleMargin,
                axaml.GroupDeleteFontSize,
                axaml.GroupModeMargin,
                axaml.GroupSliderRowMargin,
                axaml.GroupValueGridMargin,
                axaml.ProbeButtonWidth,
                axaml.ProbeButtonHeight,
                axaml.ProbeButtonFontSize,
                axaml.ProbeButtonMargin,
                axaml.ProbeNameFontSize,
                axaml.ProbeNameStackMargin,
                axaml.ProbeRowsMargin,
                axaml.ProbeRowMargin,
                axaml.ProbeRowGlyphWidth,
                axaml.ProbeRowGlyphFontSize,
                axaml.ProbeRowTextFontSize,
                axaml.ProbeRowValueMinWidth,
                axaml.ProbeEmptyFontSize,
                axaml.ProbeEmptyOpacity,
                axaml.ConfirmTitleFontSize,
                axaml.ConfirmTitleMargin,
                axaml.ConfirmMessageMargin,
                axaml.ConfirmCancelMargin,
                axaml.ConfirmBorderThickness,
                axaml.ConfirmCornerRadius,
                axaml.ConfirmPadding,
                axaml.ConfirmMinWidth,
                axaml.ConfirmMaxWidth,
                axaml.FullOpacity,
                axaml.RelinquishedControlOpacity,
                axaml.InactiveSliderValueOpacity,
                axaml.DragGhostOpacity,
                axaml.DragGhostShadowOffsetY,
                axaml.DragGhostShadowBlur,
                axaml.DropMarkerHeight,
                axaml.DropMarkerCornerRadius,
                axaml.DragSourceOpacity,
                axaml.DragDebugMarkerHeight,
                axaml.DragDebugMarkerOpacity,
                axaml.DragDebugPointerHeight,
                axaml.DragDebugPointerOpacity,
                axaml.InlineEditorFontSize,
                axaml.InlineEditorBorderThickness,
                axaml.InlineEditorPadding);
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

    private sealed record FanRowVisualRefs(
        bool Grouped,
        TextBlock Name,
        TextBlock RPM,
        TextBlock Subtitle,
        TextBlock? Value,
        FlyoutSlider? Slider,
        TextBlock? ModeGlyph,
        Border? ModeButton);

    private sealed record GroupHeaderVisualRefs(
        TextBlock Name,
        TextBlock ActiveCurve,
        TextBlock Value,
        FlyoutSlider Slider,
        TextBlock? ModeGlyph,
        Border ModeButton);

    /// <summary>
    /// Captures the visible speed range used by a flyout slider.
    /// </summary>
    private readonly record struct SpeedSliderRange(double Minimum, double Maximum);

    private sealed record FlyoutVisualSlot(
        int Order,
        int Sequence,
        string Tie,
        Control Control);

    private sealed record FlyoutTopLevelItem(
        FanFlyoutCell? Cell,
        ProbeCard? ProbeCard)
    {
        /// <summary>
        /// Creates a fan/group top-level item.
        /// </summary>
        public static FlyoutTopLevelItem FanCell(FanFlyoutCell cell) => new(cell, null);

        /// <summary>
        /// Creates a probe-card top-level item.
        /// </summary>
        public static FlyoutTopLevelItem Probe(ProbeCard probeCard) => new(null, probeCard);
    }

    private sealed record ProbeCardRowVisualRefs(
        ProbeCardProbe Probe,
        TextBlock Value);

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ForceCloseAllFanPropertiesWindows();
        ForceCloseAllProbeSelectorWindows();
        _settings.Changed -= OnSettingsChanged;
        if (AppServices.UpdateCheckService is { } updateService)
            updateService.StateChanged -= NotifyUpdateStateChanged;
        if (_lhmService != null)
        {
            _lhmService.PollTickCompleted -= OnPollTickCompleted;
            ((INotifyCollectionChanged)_lhmService.Fans).CollectionChanged -= OnFansChanged;
            foreach (Fan fan in _subscribedFans)
                fan.PropertyChanged -= OnFanPropertyChanged;
            _subscribedFans.Clear();
        }
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
