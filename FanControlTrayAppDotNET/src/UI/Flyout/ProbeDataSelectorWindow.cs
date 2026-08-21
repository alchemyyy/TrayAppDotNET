using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FanControlTrayAppDotNET.Services;
using FanControlTrayAppDotNET.UI;
using FanControlTrayAppDotNET.UI.Settings;
using TrayAppDotNETCommon.UI;
using GlyphCatalogHotReload = TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;

namespace FanControlTrayAppDotNET.UI.Flyout;

/// <summary>
/// Window for selecting and transforming probe data shown by a probe card.
/// </summary>
public sealed partial class ProbeDataSelectorWindow : Window
{
    private const string NicknameTargetControlAnchor = "NicknameTargetRegex";
    private const string NicknameReplacementControlAnchor = "NicknameReplacement";
    private static readonly bool EnableReorderCardHoverCue = false;

    private static readonly ProbeSelectorTab[] Tabs =
    [
        ProbeSelectorTab.Home,
        ProbeSelectorTab.Temperatures,
        ProbeSelectorTab.Power,
        ProbeSelectorTab.Load,
        ProbeSelectorTab.Clocks,
        ProbeSelectorTab.Voltages
    ];

    private readonly ProbeCard _probeCard;
    private readonly AppSettings _settings;
    private readonly Action<ProbeCard> _changed;
    private readonly SettingsPalette _palette;
    private readonly ControlNameScope _controlNames;
    private readonly LHMService? _subscribedLHMService;
    private readonly UIResourceScope _windowResources = new(nameof(ProbeDataSelectorWindow));
    private readonly HashSet<string> _expandedTransformKeys = new(StringComparer.OrdinalIgnoreCase);
    private ProbeSelectorVisualGeneration? _activeVisualGeneration;
    private TextBox? _focusedTransformTextBox;
    private ProbeSelectorAxamlProperties? _layout;
    private StackPanel? _selectedProbeDragPanel;
    private ProbeCardProbe? _draggedSelectedProbe;
    private Border? _draggedSelectedProbeRow;
    private Point _selectedProbeDragStart;
    private double _draggedSelectedProbePointerOffsetY;
    private double _draggedSelectedProbeHeight;
    private int _draggedSelectedProbeTargetIndex = -1;
    private IPointer? _capturedSelectedProbePointer;
    private StackPanel? _nicknameRuleListPanel;
    private List<DeviceNicknameRule>? _draggedNicknameRuleList;
    private DeviceNicknameRule? _draggedNicknameRule;
    private Border? _draggedNicknameRuleRow;
    private Point _nicknameRuleDragStart;
    private double _draggedNicknameRulePointerOffsetY;
    private double _draggedNicknameRuleHeight;
    private int _draggedNicknameRuleTargetIndex = -1;
    private IPointer? _capturedNicknameRulePointer;
    private bool _isResettingGestures;
    private bool _isPublishingContentGeneration;

    /// <summary>
    /// Initializes the XAML designer constructor.
    /// </summary>
    public ProbeDataSelectorWindow()
    {
        _controlNames = ControlNameScope.For(this);
        _probeCard = null!;
        _settings = null!;
        _changed = static _ => { };
        _palette = null!;
        _subscribedLHMService = null;

        try
        {
            InitializeComponent();
            InitializeComponentState();
            AddHandler(PointerPressedEvent, OnSelectorPointerPressed, RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _windowResources.Add(() => RemoveHandler(PointerPressedEvent, OnSelectorPointerPressed));
        }
        catch
        {
            DisposeSelectorResources();
            throw;
        }
    }

    /// <summary>
    /// Initializes a selector window for a probe card.
    /// </summary>
    public ProbeDataSelectorWindow(ProbeCard probeCard, AppSettings settings, Action<ProbeCard> changed)
    {
        _controlNames = ControlNameScope.For(this);
        _probeCard = probeCard;
        _settings = settings;
        _changed = changed;
        _palette = FanSettingsWindow.CreatePalette(
            AppServices.Theme,
            settings,
            AppTheme.ResolveEffectiveIsLightTheme(settings));

        _subscribedLHMService = AppServices.LHMService;
        try
        {
            InitializeComponent();
            InitializeComponentState();
            AddHandler(PointerPressedEvent, OnSelectorPointerPressed, RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _windowResources.Add(() => RemoveHandler(PointerPressedEvent, OnSelectorPointerPressed));
            Title = $"Probe Data: {_probeCard.DisplayName}";
            if (_subscribedLHMService != null)
            {
                _subscribedLHMService.PollTickCompleted += OnPollTickCompleted;
                _windowResources.Add(() =>
                    _subscribedLHMService.PollTickCompleted -= OnPollTickCompleted);
            }

            RebuildContent(ProbeSelectorTab.Home);
            GlyphCatalogHotReload.ResourcesReloaded += OnGlyphCatalogResourcesReloaded;
            _windowResources.Add(() =>
                GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded);
        }
        catch
        {
            DisposeSelectorResources();
            throw;
        }
    }

    private void InitializeComponentState()
    {
        _layout = AxamlProbeSelector;
    }

    private ControlNameScope ControlNames => _controlNames;

    /// <summary>
    /// Rebuilds code-created selector glyphs after a catalog source reload.
    /// </summary>
    private void OnGlyphCatalogResourcesReloaded()
    {
        if (_windowResources.IsDisposed || _activeVisualGeneration == null) return;

        RebuildContent();
    }

    private ProbeSelectorAxamlProperties Layout =>
        _layout ?? throw new InvalidOperationException("Probe selector layout resources have not been loaded.");

    private ProbeSelectorTab SelectedTab =>
        _activeVisualGeneration?.SelectedTab ?? ProbeSelectorTab.Home;

    private double TruncateToggleTrackHeight =>
        Layout.TruncateToggleTrackWidth * Layout.TruncateToggleTrackHeightRatio;

    private double TruncateToggleThumbSize =>
        Layout.TruncateToggleTrackWidth * Layout.TruncateToggleThumbSizeRatio;

    private CornerRadius TruncateToggleTrackCornerRadius =>
        new(TruncateToggleTrackHeight / 2.0);

    private CornerRadius TruncateToggleThumbCornerRadius =>
        new(TruncateToggleThumbSize / 2.0);

    private Thickness TruncateToggleThumbUncheckedMargin =>
        new(TruncateToggleThumbInset, 0, 0, 0);

    private Thickness TruncateToggleThumbCheckedMargin =>
        new(0, 0, TruncateToggleThumbInset, 0);

    private double TruncateToggleThumbInset =>
        Math.Max(0, (TruncateToggleTrackHeight - TruncateToggleThumbSize) / 2.0);

    private bool HasCapturedGesturePointer =>
        _capturedSelectedProbePointer != null || _capturedNicknameRulePointer != null;

    /// <summary>
    /// Rebuilds the selector chrome and active tab body.
    /// </summary>
    private void RebuildContent() => RebuildContent(SelectedTab);

    private void RebuildContent(ProbeSelectorTab selectedTab)
    {
        UIResourceScope resources = new($"{nameof(ProbeDataSelectorWindow)}.Content");
        ProbeSelectorVisualGeneration replacement = new(
            selectedTab,
            DeviceNicknameResolver.Create(_settings),
            ProbeNicknameResolver.Create(_settings),
            resources);
        resources.Add(replacement.Retire);

        try
        {
            Grid shell = ControlNames.Assign(
                new Grid
                {
                    Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
                    Margin = Layout.ContentMargin
                },
                nameof(ProbeDataSelectorWindow));
            shell.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.TabRowHeight)));
            shell.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            shell.Children.Add(BuildTabRow(replacement));

            Control body = BuildBodyHost(replacement);
            Grid.SetRow(body, 1);
            shell.Children.Add(body);

            Border root = ControlNames.Assign(
                new Border
                {
                    Focusable = true,
                    Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
                    BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
                    BorderThickness = Layout.RootBorderThickness,
                    CornerRadius = _settings.EnableRoundedCorners
                        ? Layout.RootCornerRadius
                        : Layout.ZeroCornerRadius,
                    Child = shell
                },
                nameof(ProbeDataSelectorWindow));
            ControlNames.AssignLogicalSubtree(root, this);
            replacement.FocusSink = root;
            replacement.AttachContentGeneration(new UIContentGeneration(
                $"{nameof(ProbeDataSelectorWindow)}.Content",
                root,
                resources));
        }
        catch
        {
            resources.Dispose();
            throw;
        }

        CommitContentGeneration(replacement);
    }

    private void CommitContentGeneration(ProbeSelectorVisualGeneration replacement)
    {
        ProbeSelectorVisualGeneration? previous = _activeVisualGeneration;
        object? previousWindowContent = Content;
        _isPublishingContentGeneration = true;
        try
        {
            _activeVisualGeneration = replacement;
            try
            {
                Content = replacement.ContentGeneration.Root;
            }
            catch (Exception exception)
            {
                _activeVisualGeneration = previous;
                try
                {
                    Content = previousWindowContent;
                }
                catch (Exception rollbackException)
                {
                    TADNLog.Log(
                        $"ProbeDataSelectorWindow content rollback failed after {exception.GetType().Name}: " +
                        $"{rollbackException.GetType().Name}: {rollbackException.Message}");
                }

                // A failed root assignment can still cause Avalonia to drop the previous capture
                try
                {
                    ResetHomeGestureState();
                }
                catch (Exception resetException)
                {
                    TADNLog.Log(
                        $"ProbeDataSelectorWindow rollback gesture reset failed: {resetException.Message}");
                }
                finally
                {
                    if (!replacement.ContentGeneration.IsDisposed)
                        replacement.ContentGeneration.Dispose();
                }
                throw;
            }

            // Retire gestures and old resources only after the replacement root is live
            try
            {
                ResetHomeGestureState();
            }
            catch (Exception resetException)
            {
                TADNLog.Log(
                    $"ProbeDataSelectorWindow post-commit gesture reset failed: {resetException.Message}");
            }
            finally
            {
                previous?.ContentGeneration.Dispose();
            }
        }
        finally
        {
            _isPublishingContentGeneration = false;
        }
    }

    /// <summary>
    /// Builds the tab body host, leaving Home available for section-local scrolling.
    /// </summary>
    private Control BuildBodyHost(ProbeSelectorVisualGeneration generation)
    {
        Control content = BuildTabBody(generation);
        if (generation.SelectedTab == ProbeSelectorTab.Home)
        {
            return new Border
            {
                Margin = Layout.BodyMargin,
                Child = content
            };
        }

        SettingsScrollHost scrollHost = generation.Resources.Own(
            new SettingsScrollHost(content, _palette, Layout.ZeroThickness)
        {
            Margin = Layout.BodyMargin
        });
        return scrollHost;
    }

    /// <summary>
    /// Builds the single-row browser-style tab strip.
    /// </summary>
    private Grid BuildTabRow(ProbeSelectorVisualGeneration generation)
    {
        Grid row = ControlNames.Assign(new Grid(), "Tabs");
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int i = 1; i < Tabs.Length; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        Border homeTab = BuildTab(Tabs[0], generation);
        Grid.SetColumn(homeTab, 0);
        row.Children.Add(homeTab);

        for (int i = 1; i < Tabs.Length; i++)
        {
            Border tab = BuildTab(Tabs[i], generation);
            Grid.SetColumn(tab, i + 1);
            row.Children.Add(tab);
        }

        return row;
    }

    /// <summary>
    /// Builds one tab button.
    /// </summary>
    private Border BuildTab(ProbeSelectorTab tab, ProbeSelectorVisualGeneration generation)
    {
        bool selected = generation.SelectedTab == tab;
        TextBlock label = TrayAppDotNETSettingsUI.Text(TabLabel(tab), _palette, Layout.TabFontSize,
            selected ? FontWeight.SemiBold : FontWeight.Normal);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;

        Border border = ControlNames.Assign(
            new Border
            {
                Background = TrayAppDotNETSettingsUI.Brush(
                    selected ? _palette.CardBackground : _palette.ControlBackground),
                BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
                BorderThickness = Layout.RootBorderThickness,
                CornerRadius = _settings.EnableRoundedCorners
                    ? Layout.TabCornerRadius
                    : Layout.ZeroCornerRadius,
                Margin = Layout.TabMargin,
                Padding = Layout.TabPadding,
                Width = Layout.TabWidth,
                MinHeight = Layout.TabMinHeight,
                Child = label,
                Cursor = TrayAppDotNETCursors.Hand
            },
            $"{tab}Tab");
        border.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed) return;
            if (SelectedTab == tab)
            {
                e.Handled = true;
                return;
            }

            RebuildContent(tab);
            e.Handled = true;
        };
        return border;
    }

    /// <summary>
    /// Builds the active tab body.
    /// </summary>
    private Control BuildTabBody(ProbeSelectorVisualGeneration generation)
    {
        return generation.SelectedTab switch
        {
            ProbeSelectorTab.Home => BuildHomeBody(generation),
            ProbeSelectorTab.Temperatures => BuildTypeBody(DataSourceTypeEnum.Temperature, generation),
            ProbeSelectorTab.Power => BuildTypeBody(DataSourceTypeEnum.Power, generation),
            ProbeSelectorTab.Load => BuildTypeBody(DataSourceTypeEnum.Load, generation),
            ProbeSelectorTab.Clocks => BuildTypeBody(DataSourceTypeEnum.Clock, generation),
            ProbeSelectorTab.Voltages => BuildTypeBody(DataSourceTypeEnum.Voltage, generation),
            _ => BuildHomeBody(generation)
        };
    }

    /// <summary>
    /// Builds the selected-probes home tab.
    /// </summary>
    private Grid BuildHomeBody(ProbeSelectorVisualGeneration generation)
    {
        Grid home = ControlNames.Assign(
            new Grid
            {
                UseLayoutRounding = true
            },
            "Home");
        home.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        home.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.HomeSectionSeparatorThickness)));
        home.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(NicknameColumnWidth())));

        Border selectedProbeHost = new()
        {
            Padding = Layout.HomeNicknameColumnPadding,
            Child = BuildSelectedProbesSection(generation)
        };
        Grid.SetColumn(selectedProbeHost, 0);
        home.Children.Add(selectedProbeHost);

        Border columnSeparator = BuildHomeColumnSeparator();
        Grid.SetColumn(columnSeparator, 1);
        home.Children.Add(columnSeparator);

        Grid nicknames = new()
        {
            UseLayoutRounding = true
        };
        nicknames.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.HomeDeviceNicknamesRowHeight)));
        nicknames.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.HomeSectionSeparatorThickness)));
        nicknames.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Border deviceNicknameHost = new()
        {
            Padding = Layout.HomeNicknameColumnPadding,
            Child = BuildDeviceNicknamesSection(generation)
        };
        Grid.SetRow(deviceNicknameHost, 0);
        nicknames.Children.Add(deviceNicknameHost);

        Border rowSeparator = BuildHomeNicknameRowSeparator();
        Grid.SetRow(rowSeparator, 1);
        nicknames.Children.Add(rowSeparator);

        Border probeNicknameHost = new()
        {
            Padding = Layout.HomeNicknameColumnPadding,
            Child = BuildProbeNicknamesSection(generation)
        };
        Grid.SetRow(probeNicknameHost, 2);
        nicknames.Children.Add(probeNicknameHost);

        Grid.SetColumn(nicknames, 2);
        home.Children.Add(nicknames);
        return home;
    }

    /// <summary>
    /// Builds the full-height separator between home-tab columns.
    /// </summary>
    private Border BuildHomeColumnSeparator() =>
        new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(Layout.HomeSectionSeparatorColor),
            Margin = Layout.HomeColumnSeparatorMargin,
            Width = Layout.HomeSectionSeparatorThickness,
            MinWidth = Layout.HomeSectionSeparatorThickness,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            UseLayoutRounding = true
        };

    /// <summary>
    /// Builds the full-width separator between nickname sections.
    /// </summary>
    private Border BuildHomeNicknameRowSeparator() =>
        new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(Layout.HomeSectionSeparatorColor),
            Margin = Layout.HomeNicknameRowSeparatorMargin,
            Height = Layout.HomeSectionSeparatorThickness,
            MinHeight = Layout.HomeSectionSeparatorThickness,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            UseLayoutRounding = true
        };

    /// <summary>
    /// Builds the selected-probes column for the home tab.
    /// </summary>
    private Grid BuildSelectedProbesSection(ProbeSelectorVisualGeneration generation)
    {
        Grid section = ControlNames.Assign(
            new Grid
            {
                Margin = Layout.NicknameSectionMargin
            },
            "SelectedProbes");
        section.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        section.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        section.Children.Add(BuildSelectedProbesHeader());

        StackPanel selectedProbeList = ControlNames.Assign(
            new StackPanel
            {
                Margin = Layout.SelectedProbeGridMargin
            },
            "SelectedProbes");
        List<ProbeCardProbe> selectedProbes =
        [
            .. _probeCard.Probes
                .Where(static probe => probe.IsSelected && !string.IsNullOrWhiteSpace(probe.DataSourceKey))
        ];
        generation.SelectedProbeListPanel = selectedProbeList;
        foreach (ProbeCardProbe probe in selectedProbes)
        {
            DataSource? source = DataSource.Find(probe.DataSourceKey);
            Border card = source is null
                ? BuildMissingProbeCard(probe)
                : BuildProbeChoiceCard(source, generation);
            WireSelectedProbeDrag(card, probe, selectedProbeList);
            selectedProbeList.Children.Add(card);
        }

        Control content = selectedProbes.Count == 0 ? EmptyText("No probes selected") : selectedProbeList;
        SettingsScrollHost scrollHost = BuildVerticalScrollHost(
            content,
            Layout.HomeSectionScrollHostMargin,
            generation);
        Grid.SetRow(scrollHost, 1);
        section.Children.Add(scrollHost);
        return section;
    }

    /// <summary>
    /// Builds the selected-probes column header.
    /// </summary>
    private Grid BuildSelectedProbesHeader()
    {
        Grid header = new();
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock title = TrayAppDotNETSettingsUI.Text("Selected Probes", _palette,
            Layout.SectionTitleFontSize, FontWeight.SemiBold);
        title.Margin = Layout.SectionHeaderMargin;
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);

        SettingsButton clearDeadSensors = TrayAppDotNETSettingsUI.Button("Clear Dead Sensors", _palette);
        clearDeadSensors.Width = Layout.HomeClearDeadSensorsButtonWidth;
        clearDeadSensors.Height = Layout.HomeActionButtonHeight;
        clearDeadSensors.MinHeight = Layout.HomeActionButtonHeight;
        clearDeadSensors.Padding = Layout.HomeActionButtonPadding;
        clearDeadSensors.Margin = Layout.HomeActionButtonTrailingMargin;
        clearDeadSensors.Click += (_, _) => ClearDeadSensors();
        Grid.SetColumn(clearDeadSensors, 1);
        header.Children.Add(clearDeadSensors);
        return header;
    }

    /// <summary>
    /// Wires drag and keyboard reordering for a selected-probe row.
    /// </summary>
    private void WireSelectedProbeDrag(
        Border row,
        ProbeCardProbe probe,
        StackPanel selectedProbeList)
    {
        row.Tag = probe;
        row.Focusable = true;
        row.Cursor = TrayAppDotNETCursors.Hand;

        bool pointerOver = false;
        bool pointerPressed = false;
        UpdateSelectedProbeDragVisual(row, probe, pointerOver, pointerPressed);

        row.PointerEntered += (_, e) =>
        {
            pointerOver = IsCardBackgroundPointerSource(row, e.Source as Visual);
            UpdateSelectedProbeDragVisual(row, probe, pointerOver, pointerPressed);
        };
        row.PointerExited += (_, _) =>
        {
            pointerOver = false;
            pointerPressed = false;
            UpdateSelectedProbeDragVisual(row, probe, pointerOver, pointerPressed);
        };
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            if (HasCapturedGesturePointer)
            {
                e.Handled = true;
                return;
            }

            _selectedProbeDragPanel = selectedProbeList;
            _draggedSelectedProbe = probe;
            _draggedSelectedProbeRow = row;
            _selectedProbeDragStart = e.GetPosition(selectedProbeList);
            _draggedSelectedProbePointerOffsetY = e.GetPosition(row).Y;
            _draggedSelectedProbeHeight = Math.Max(1, row.Bounds.Height);
            _draggedSelectedProbeTargetIndex = SelectedProbeIndex(probe);
            pointerPressed = true;
            UpdateSelectedProbeDragVisual(row, probe, pointerOver, pointerPressed);
            CapturePointerOrRollback(
                e.Pointer,
                row,
                ref _capturedSelectedProbePointer,
                "selected probe drag",
                () =>
                {
                    pointerPressed = false;
                    ResetSelectedProbeGesture(e.Pointer);
                    UpdateSelectedProbeDragVisual(row, probe, pointerOver, pointerPressed);
                });
            e.Handled = true;
        };
        row.PointerMoved += (_, e) =>
        {
            if (HasCapturedGesturePointer
                && !ReferenceEquals(_capturedSelectedProbePointer, e.Pointer))
            {
                return;
            }

            bool nextPointerOver = IsCardBackgroundPointerSource(row, e.Source as Visual);
            if (pointerOver != nextPointerOver && !pointerPressed)
            {
                pointerOver = nextPointerOver;
                UpdateSelectedProbeDragVisual(row, probe, pointerOver, pointerPressed);
            }

            if (!ReferenceEquals(_capturedSelectedProbePointer, e.Pointer)) return;
            if (!ReferenceEquals(row, _draggedSelectedProbeRow)) return;
            if (_draggedSelectedProbe is null || _selectedProbeDragPanel is null) return;

            Point current = e.GetPosition(_selectedProbeDragPanel);
            if (Math.Abs(current.Y - _selectedProbeDragStart.Y) < Layout.ReorderDragThreshold) return;

            double draggedMidpoint = current.Y - _draggedSelectedProbePointerOffsetY
                + _draggedSelectedProbeHeight / 2.0;
            _draggedSelectedProbeTargetIndex = SelectedProbeInsertionIndexFromMidpoint(draggedMidpoint);
            ApplySelectedProbeDragPreview();
            row.RenderTransform = new TranslateTransform(0, current.Y - _selectedProbeDragStart.Y);
            e.Handled = true;
        };
        row.PointerReleased += (_, e) =>
        {
            if (!ReferenceEquals(_capturedSelectedProbePointer, e.Pointer)) return;
            pointerPressed = false;
            EndSelectedProbeDrag(e.Pointer);
        };
        row.PointerCaptureLost += (_, e) =>
        {
            if (_isResettingGestures || _isPublishingContentGeneration) return;
            if (!ReferenceEquals(_capturedSelectedProbePointer, e.Pointer)) return;
            pointerPressed = false;
            EndSelectedProbeDrag(e.Pointer);
        };
        row.KeyDown += (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
            if (e.Key is not (Key.Up or Key.Down)) return;

            int direction = e.Key == Key.Up ? -1 : 1;
            MoveSelectedProbeByKeyboard(probe, direction);
            e.Handled = true;
        };
    }

    /// <summary>
    /// Updates selected-probe row visuals during reorder interactions.
    /// </summary>
    private void UpdateSelectedProbeDragVisual(
        Border row,
        ProbeCardProbe probe,
        bool pointerOver,
        bool pointerPressed)
    {
        bool dragging = ReferenceEquals(probe, _draggedSelectedProbe);
        bool showHover = EnableReorderCardHoverCue && pointerOver;
        Color background = pointerPressed
            ? _palette.Pressed
            : showHover
                ? _palette.Hover
                : _palette.CardBackground;
        row.Background = TrayAppDotNETSettingsUI.Brush(background);
        row.BorderBrush = TrayAppDotNETSettingsUI.Brush(dragging ? _palette.Accent : _palette.Border);
        row.BorderThickness = Layout.RootBorderThickness;
        row.Opacity = dragging ? Layout.ReorderDraggingOpacity : Layout.FullOpacity;
        row.SetValue(ZIndexProperty, dragging ? Layout.ReorderDraggingZIndex : Layout.ReorderNormalZIndex);
    }

    /// <summary>
    /// Resolves a selected probe's active-list index.
    /// </summary>
    private int SelectedProbeIndex(ProbeCardProbe probe) =>
        ActiveProbeSettingsInOrder().IndexOf(probe);

    /// <summary>
    /// Resolves the insertion index from the dragged row midpoint.
    /// </summary>
    private int SelectedProbeInsertionIndexFromMidpoint(double draggedMidpointY)
    {
        if (_selectedProbeDragPanel is null) return -1;

        int insertion = 0;
        for (int i = 0; i < _selectedProbeDragPanel.Children.Count; i++)
        {
            Control child = _selectedProbeDragPanel.Children[i];
            if (ReferenceEquals(child, _draggedSelectedProbeRow)) continue;

            Point? topLeft = child.TranslatePoint(new Point(0, 0), _selectedProbeDragPanel);
            if (topLeft is null) continue;
            if (draggedMidpointY > topLeft.Value.Y + child.Bounds.Height / 2.0) insertion++;
            else break;
        }

        int max = ActiveProbeSettingsInOrder().Count - (_draggedSelectedProbe is not null ? 1 : 0);
        return Math.Clamp(insertion, 0, Math.Max(0, max));
    }

    /// <summary>
    /// Applies visual offsets to rows displaced by a drag reorder.
    /// </summary>
    private void ApplySelectedProbeDragPreview()
    {
        if (_selectedProbeDragPanel is null || _draggedSelectedProbe is null || _draggedSelectedProbeRow is null)
            return;

        ResetSelectedProbeDragPreview();

        List<ProbeCardProbe> activeProbes = ActiveProbeSettingsInOrder();
        int sourceIndex = activeProbes.IndexOf(_draggedSelectedProbe);
        if (sourceIndex < 0) return;

        int targetIndex = Math.Clamp(_draggedSelectedProbeTargetIndex, 0,
            Math.Max(0, activeProbes.Count - 1));
        double offset = Math.Max(1, _draggedSelectedProbeHeight
            + Math.Max(0, _draggedSelectedProbeRow.Margin.Bottom));
        if (targetIndex < sourceIndex)
        {
            for (int i = targetIndex; i < sourceIndex; i++)
                SetSelectedProbePreviewOffset(i, offset);
        }
        else if (targetIndex > sourceIndex)
        {
            for (int i = sourceIndex + 1; i <= targetIndex && i < _selectedProbeDragPanel.Children.Count; i++)
                SetSelectedProbePreviewOffset(i, -offset);
        }
    }

    /// <summary>
    /// Sets a reorder preview offset for a selected-probe row.
    /// </summary>
    private void SetSelectedProbePreviewOffset(int index, double offset)
    {
        if (_selectedProbeDragPanel is null) return;
        if (index < 0 || index >= _selectedProbeDragPanel.Children.Count) return;
        if (ReferenceEquals(_selectedProbeDragPanel.Children[index], _draggedSelectedProbeRow)) return;

        _selectedProbeDragPanel.Children[index].RenderTransform = new TranslateTransform(0, offset);
    }

    /// <summary>
    /// Clears selected-probe reorder preview offsets.
    /// </summary>
    private void ResetSelectedProbeDragPreview()
    {
        if (_selectedProbeDragPanel is null) return;
        foreach (Control child in _selectedProbeDragPanel.Children)
        {
            if (ReferenceEquals(child, _draggedSelectedProbeRow)) continue;
            child.RenderTransform = null;
        }
    }

    /// <summary>
    /// Ends a selected-probe drag reorder.
    /// </summary>
    private void EndSelectedProbeDrag(IPointer? pointer)
    {
        ProbeCardProbe? dragged = _draggedSelectedProbe;
        int targetIndex = _draggedSelectedProbeTargetIndex;
        bool hadDrag = dragged is not null;
        ResetSelectedProbeGesture(pointer);

        if (dragged is not null && targetIndex >= 0)
            ApplySelectedProbeOrder(dragged, targetIndex);

        if (hadDrag) RebuildContent();
    }

    /// <summary>
    /// Moves a selected probe with keyboard shortcuts.
    /// </summary>
    private void MoveSelectedProbeByKeyboard(ProbeCardProbe probe, int direction)
    {
        int currentIndex = SelectedProbeIndex(probe);
        int nextIndex = currentIndex + direction;
        int count = ActiveProbeSettingsInOrder().Count;
        if (currentIndex < 0 || nextIndex < 0 || nextIndex >= count) return;

        ApplySelectedProbeOrder(probe, nextIndex);
        RebuildContent();
    }

    /// <summary>
    /// Applies active selected-probe order while preserving inactive probe settings.
    /// </summary>
    private void ApplySelectedProbeOrder(ProbeCardProbe dragged, int targetIndex)
    {
        List<ProbeCardProbe> activeProbes = ActiveProbeSettingsInOrder();
        int currentIndex = activeProbes.IndexOf(dragged);
        if (currentIndex < 0) return;

        int clampedTargetIndex = Math.Clamp(targetIndex, 0, activeProbes.Count - 1);
        if (currentIndex == clampedTargetIndex) return;

        activeProbes.RemoveAt(currentIndex);
        activeProbes.Insert(clampedTargetIndex, dragged);

        List<ProbeCardProbe> inactiveProbes =
        [
            .. _probeCard.Probes.Where(static probe => !probe.IsSelected)
        ];
        _probeCard.Probes.Clear();
        _probeCard.Probes.AddRange(activeProbes);
        _probeCard.Probes.AddRange(inactiveProbes);
        _changed(_probeCard);
    }

    /// <summary>
    /// Returns selected active probe settings in persisted order.
    /// </summary>
    private List<ProbeCardProbe> ActiveProbeSettingsInOrder() =>
        [
            .. _probeCard.Probes
                .Where(static probe => probe.IsSelected && !string.IsNullOrWhiteSpace(probe.DataSourceKey))
        ];

    /// <summary>
    /// Builds the global device nickname editor section.
    /// </summary>
    private Grid BuildDeviceNicknamesSection(ProbeSelectorVisualGeneration generation)
    {
        return BuildNicknameSection(
            "Device Nicknames",
            _settings.DeviceNicknameRules,
            LoadDefaultDeviceNicknames,
            AddDeviceNicknameRule,
            DeleteDeviceNicknameRule,
            generation);
    }

    /// <summary>
    /// Builds the global probe nickname editor section.
    /// </summary>
    private Grid BuildProbeNicknamesSection(ProbeSelectorVisualGeneration generation)
    {
        return BuildNicknameSection(
            "Probe Nicknames",
            _settings.ProbeNicknameRules,
            LoadDefaultProbeNicknames,
            AddProbeNicknameRule,
            DeleteProbeNicknameRule,
            generation);
    }

    /// <summary>
    /// Builds a global nickname editor section.
    /// </summary>
    private Grid BuildNicknameSection(
        string titleText,
        List<DeviceNicknameRule> rulesList,
        Action loadDefaultRules,
        Action addRule,
        Action<DeviceNicknameRule> deleteRule,
        ProbeSelectorVisualGeneration generation)
    {
        Grid section = ControlNames.Assign(
            new Grid
            {
                Margin = Layout.NicknameSectionMargin
            },
            titleText);
        section.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        section.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Grid header = new();
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock title = TrayAppDotNETSettingsUI.Text(titleText, _palette,
            Layout.SectionTitleFontSize, FontWeight.SemiBold);
        title.Margin = Layout.SectionHeaderMargin;
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);

        SettingsButton loadDefaultButton = ControlNames.Assign(
            TrayAppDotNETSettingsUI.Button("Load Default Nicknames", _palette),
            titleText);
        loadDefaultButton.Width = Layout.HomeLoadDefaultNicknamesButtonWidth;
        loadDefaultButton.Height = Layout.HomeActionButtonHeight;
        loadDefaultButton.MinHeight = Layout.HomeActionButtonHeight;
        loadDefaultButton.Padding = Layout.HomeActionButtonPadding;
        loadDefaultButton.Margin = Layout.HomeActionButtonMargin;
        loadDefaultButton.Click += (_, _) => loadDefaultRules();
        Grid.SetColumn(loadDefaultButton, 1);
        header.Children.Add(loadDefaultButton);

        SettingsButton addButton = ControlNames.Assign(
            TrayAppDotNETSettingsUI.Button("Add", _palette),
            titleText);
        addButton.Width = Layout.NicknameAddButtonWidth;
        addButton.Height = Layout.NicknameAddButtonHeight;
        addButton.MinHeight = Layout.NicknameAddButtonHeight;
        addButton.Padding = Layout.NicknameAddButtonPadding;
        addButton.Margin = Layout.HomeActionButtonTrailingMargin;
        addButton.Click += (_, _) => addRule();
        Grid.SetColumn(addButton, 2);
        header.Children.Add(addButton);
        section.Children.Add(header);

        StackPanel rules = ControlNames.Assign(new StackPanel(), titleText);
        foreach (DeviceNicknameRule rule in rulesList)
            rules.Children.Add(BuildNicknameRuleCard(rule, rulesList, rules, deleteRule));

        Border rulesHost = new()
        {
            Margin = Layout.NicknameListMargin,
            Padding = Layout.NicknameListPadding,
            Child = rules
        };
        SettingsScrollHost scrollHost = BuildVerticalScrollHost(
            rulesHost,
            Layout.HomeNicknameScrollHostMargin,
            generation);
        Grid.SetRow(scrollHost, 1);
        section.Children.Add(scrollHost);
        return section;
    }

    /// <summary>
    /// Builds a custom vertical-only scrollbar host for a bounded section.
    /// </summary>
    private SettingsScrollHost BuildVerticalScrollHost(
        Control content,
        Thickness margin,
        ProbeSelectorVisualGeneration generation)
    {
        SettingsScrollHost scrollHost = generation.Resources.Own(
            new SettingsScrollHost(content, _palette, Layout.ZeroThickness)
        {
            Margin = margin
        });
        return ControlNames.Assign(scrollHost, "VerticalScroll");
    }

    /// <summary>
    /// Calculates the fixed home nickname column width from card width and padding.
    /// </summary>
    private double NicknameColumnWidth() =>
        Layout.NicknameCardWidth + Layout.HomeNicknameColumnPadding.Left + Layout.HomeNicknameColumnPadding.Right;

    /// <summary>
    /// Builds one global nickname replacement rule card.
    /// </summary>
    private Border BuildNicknameRuleCard(
        DeviceNicknameRule rule,
        List<DeviceNicknameRule> rulesList,
        StackPanel rulesPanel,
        Action<DeviceNicknameRule> deleteRule)
    {
        Grid row = ControlNames.Assign(
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                }
            },
            "NicknameRule");

        TextBox target = NicknameTextBox(rule.TargetRegex, "Regex or {HardwareType.GPU}",
            Layout.NicknameTargetTextBoxWidth);
        ControlNames.Assign(target, NicknameTargetControlAnchor);
        target.Tag = new NicknameRuleEditorTag(rule, NicknameRuleField.Target);
        target.LostFocus += NicknameRuleLostFocus;
        target.KeyDown += NicknameRuleKeyDown;
        row.Children.Add(target);

        TextBlock arrow = TrayAppDotNETSettingsUI.Text(
            GlyphCatalog.ARROW_RIGHT.Text,
            _palette,
            Layout.NicknameArrowFontSize);
        GlyphApplicator.ApplyTo(arrow, GlyphCatalog.ARROW_RIGHT);
        arrow.Margin = Layout.NicknameArrowMargin;
        arrow.VerticalAlignment = VerticalAlignment.Center;
        arrow.Cursor = TrayAppDotNETCursors.Hand;
        Grid.SetColumn(arrow, 1);
        row.Children.Add(arrow);

        TextBox replacement = NicknameTextBox(rule.ReplacementString, "Replacement",
            Layout.NicknameReplacementTextBoxWidth);
        ControlNames.Assign(replacement, NicknameReplacementControlAnchor);
        replacement.Tag = new NicknameRuleEditorTag(rule, NicknameRuleField.Replacement);
        replacement.LostFocus += NicknameRuleLostFocus;
        replacement.KeyDown += NicknameRuleKeyDown;
        Grid.SetColumn(replacement, 2);
        row.Children.Add(replacement);

        SettingsButton delete = BuildNicknameDeleteButton();
        delete.Click += (_, _) => deleteRule(rule);
        Grid.SetColumn(delete, 3);
        row.Children.Add(delete);

        Border card = ControlNames.Assign(WrapNicknameCard(row), "NicknameRule");
        WireNicknameRuleDrag(card, arrow, rule, rulesList, rulesPanel);
        return card;
    }

    /// <summary>
    /// Wires drag and keyboard reordering for a nickname rule row.
    /// </summary>
    private void WireNicknameRuleDrag(
        Border row,
        TextBlock dragHandle,
        DeviceNicknameRule rule,
        List<DeviceNicknameRule> rulesList,
        StackPanel rulesPanel)
    {
        row.Tag = rule;
        row.Focusable = true;

        bool pointerOver = false;
        bool pointerPressed = false;
        UpdateNicknameRuleDragVisual(row, rule, pointerOver, pointerPressed);

        row.PointerEntered += (_, e) =>
        {
            pointerOver = IsCardBackgroundPointerSource(row, e.Source as Visual);
            UpdateNicknameRuleDragVisual(row, rule, pointerOver, pointerPressed);
        };
        row.PointerExited += (_, _) =>
        {
            pointerOver = false;
            pointerPressed = false;
            UpdateNicknameRuleDragVisual(row, rule, pointerOver, pointerPressed);
        };
        dragHandle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(dragHandle).Properties.IsLeftButtonPressed) return;
            if (HasCapturedGesturePointer)
            {
                e.Handled = true;
                return;
            }

            _nicknameRuleListPanel = rulesPanel;
            _draggedNicknameRuleList = rulesList;
            _draggedNicknameRule = rule;
            _draggedNicknameRuleRow = row;
            _nicknameRuleDragStart = e.GetPosition(rulesPanel);
            _draggedNicknameRulePointerOffsetY = e.GetPosition(row).Y;
            _draggedNicknameRuleHeight = Math.Max(1, row.Bounds.Height);
            _draggedNicknameRuleTargetIndex = rulesList.IndexOf(rule);
            pointerPressed = true;
            row.Focus();
            UpdateNicknameRuleDragVisual(row, rule, pointerOver, pointerPressed);
            CapturePointerOrRollback(
                e.Pointer,
                row,
                ref _capturedNicknameRulePointer,
                "nickname rule drag",
                () =>
                {
                    pointerPressed = false;
                    ResetNicknameRuleGesture(e.Pointer);
                    UpdateNicknameRuleDragVisual(row, rule, pointerOver, pointerPressed);
                });
            e.Handled = true;
        };
        row.PointerMoved += (_, e) =>
        {
            if (HasCapturedGesturePointer
                && !ReferenceEquals(_capturedNicknameRulePointer, e.Pointer))
            {
                return;
            }

            bool nextPointerOver = IsCardBackgroundPointerSource(row, e.Source as Visual);
            if (pointerOver != nextPointerOver && !pointerPressed)
            {
                pointerOver = nextPointerOver;
                UpdateNicknameRuleDragVisual(row, rule, pointerOver, pointerPressed);
            }

            if (!ReferenceEquals(_capturedNicknameRulePointer, e.Pointer)) return;
            if (!ReferenceEquals(row, _draggedNicknameRuleRow)) return;
            if (_draggedNicknameRule is null || _nicknameRuleListPanel is null) return;

            Point current = e.GetPosition(_nicknameRuleListPanel);
            if (Math.Abs(current.Y - _nicknameRuleDragStart.Y) < Layout.ReorderDragThreshold) return;

            double draggedMidpoint = current.Y - _draggedNicknameRulePointerOffsetY
                + _draggedNicknameRuleHeight / 2.0;
            _draggedNicknameRuleTargetIndex = NicknameRuleInsertionIndexFromMidpoint(draggedMidpoint);
            ApplyNicknameRuleDragPreview();
            row.RenderTransform = new TranslateTransform(0, current.Y - _nicknameRuleDragStart.Y);
            e.Handled = true;
        };
        row.PointerReleased += (_, e) =>
        {
            if (!ReferenceEquals(_capturedNicknameRulePointer, e.Pointer)) return;
            if (!ReferenceEquals(row, _draggedNicknameRuleRow)) return;

            pointerPressed = false;
            EndNicknameRuleDrag(e.Pointer);
            e.Handled = true;
        };
        row.PointerCaptureLost += (_, e) =>
        {
            if (_isResettingGestures || _isPublishingContentGeneration) return;
            if (!ReferenceEquals(_capturedNicknameRulePointer, e.Pointer)) return;
            if (!ReferenceEquals(row, _draggedNicknameRuleRow)) return;

            pointerPressed = false;
            EndNicknameRuleDrag(e.Pointer);
        };
        row.KeyDown += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, row)) return;
            if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
            if (e.Key is not (Key.Up or Key.Down)) return;

            int direction = e.Key == Key.Up ? -1 : 1;
            MoveNicknameRuleByKeyboard(rule, rulesList, direction);
            e.Handled = true;
        };
    }

    /// <summary>
    /// Determines whether a pointer event is over card chrome instead of child controls.
    /// </summary>
    private static bool IsCardBackgroundPointerSource(Border row, Visual? source)
    {
        if (source is null) return false;
        if (ReferenceEquals(source, row)) return true;
        if (source is not Grid) return false;

        return ReferenceEquals(source.GetVisualParent(), row);
    }

    /// <summary>
    /// Updates nickname rule row visuals during reorder interactions.
    /// </summary>
    private void UpdateNicknameRuleDragVisual(
        Border row,
        DeviceNicknameRule rule,
        bool pointerOver,
        bool pointerPressed)
    {
        bool dragging = ReferenceEquals(rule, _draggedNicknameRule);
        bool showHover = EnableReorderCardHoverCue && pointerOver;
        Color background = pointerPressed
            ? _palette.Pressed
            : showHover
                ? _palette.Hover
                : _palette.CardBackground;
        row.Background = TrayAppDotNETSettingsUI.Brush(background);
        row.BorderBrush = TrayAppDotNETSettingsUI.Brush(dragging ? _palette.Accent : _palette.Border);
        row.BorderThickness = Layout.RootBorderThickness;
        row.Opacity = dragging ? Layout.ReorderDraggingOpacity : Layout.FullOpacity;
        row.SetValue(ZIndexProperty, dragging ? Layout.ReorderDraggingZIndex : Layout.ReorderNormalZIndex);
    }

    /// <summary>
    /// Resolves the insertion index from the dragged nickname rule midpoint.
    /// </summary>
    private int NicknameRuleInsertionIndexFromMidpoint(double draggedMidpointY)
    {
        if (_nicknameRuleListPanel is null || _draggedNicknameRuleList is null) return -1;

        int insertion = 0;
        for (int i = 0; i < _nicknameRuleListPanel.Children.Count; i++)
        {
            Control child = _nicknameRuleListPanel.Children[i];
            if (ReferenceEquals(child, _draggedNicknameRuleRow)) continue;

            Point? topLeft = child.TranslatePoint(new Point(0, 0), _nicknameRuleListPanel);
            if (topLeft is null) continue;
            if (draggedMidpointY > topLeft.Value.Y + child.Bounds.Height / 2.0) insertion++;
            else break;
        }

        int max = _draggedNicknameRuleList.Count - (_draggedNicknameRule is not null ? 1 : 0);
        return Math.Clamp(insertion, 0, Math.Max(0, max));
    }

    /// <summary>
    /// Applies visual offsets to nickname rows displaced by a drag reorder.
    /// </summary>
    private void ApplyNicknameRuleDragPreview()
    {
        if (_nicknameRuleListPanel is null ||
            _draggedNicknameRuleList is null ||
            _draggedNicknameRule is null ||
            _draggedNicknameRuleRow is null)
            return;

        ResetNicknameRuleDragPreview();

        int sourceIndex = _draggedNicknameRuleList.IndexOf(_draggedNicknameRule);
        if (sourceIndex < 0) return;

        int targetIndex = Math.Clamp(_draggedNicknameRuleTargetIndex, 0,
            Math.Max(0, _draggedNicknameRuleList.Count - 1));
        double offset = Math.Max(1, _draggedNicknameRuleHeight
            + Math.Max(0, _draggedNicknameRuleRow.Margin.Bottom));
        if (targetIndex < sourceIndex)
        {
            for (int i = targetIndex; i < sourceIndex; i++)
                SetNicknameRulePreviewOffset(i, offset);
        }
        else if (targetIndex > sourceIndex)
        {
            for (int i = sourceIndex + 1; i <= targetIndex && i < _nicknameRuleListPanel.Children.Count; i++)
                SetNicknameRulePreviewOffset(i, -offset);
        }
    }

    /// <summary>
    /// Sets a reorder preview offset for a nickname rule row.
    /// </summary>
    private void SetNicknameRulePreviewOffset(int index, double offset)
    {
        if (_nicknameRuleListPanel is null) return;
        if (index < 0 || index >= _nicknameRuleListPanel.Children.Count) return;
        if (ReferenceEquals(_nicknameRuleListPanel.Children[index], _draggedNicknameRuleRow)) return;

        _nicknameRuleListPanel.Children[index].RenderTransform = new TranslateTransform(0, offset);
    }

    /// <summary>
    /// Clears nickname rule reorder preview offsets.
    /// </summary>
    private void ResetNicknameRuleDragPreview()
    {
        if (_nicknameRuleListPanel is null) return;
        foreach (Control child in _nicknameRuleListPanel.Children)
        {
            if (ReferenceEquals(child, _draggedNicknameRuleRow)) continue;
            child.RenderTransform = null;
        }
    }

    /// <summary>
    /// Ends a nickname rule drag reorder.
    /// </summary>
    private void EndNicknameRuleDrag(IPointer? pointer)
    {
        DeviceNicknameRule? dragged = _draggedNicknameRule;
        List<DeviceNicknameRule>? rulesList = _draggedNicknameRuleList;
        int targetIndex = _draggedNicknameRuleTargetIndex;
        bool hadDrag = dragged is not null;
        ResetNicknameRuleGesture(pointer);

        if (dragged is not null && rulesList is not null && targetIndex >= 0)
            ApplyNicknameRuleOrder(rulesList, dragged, targetIndex);

        if (hadDrag) RebuildContent();
    }

    /// <summary>Clears Home-tab panels and releases captures during visual generation replacement.</summary>
    private void ResetHomeGestureState()
    {
        if (_isResettingGestures) return;

        _isResettingGestures = true;
        _focusedTransformTextBox = null;
        try
        {
            ResetSelectedProbeGesture(null);
        }
        finally
        {
            try
            {
                ResetNicknameRuleGesture(null);
            }
            finally
            {
                _isResettingGestures = false;
            }
        }
    }

    private void ResetSelectedProbeGesture(IPointer? fallbackPointer)
    {
        IPointer? capturedPointer = _capturedSelectedProbePointer ?? fallbackPointer;
        _capturedSelectedProbePointer = null;
        StackPanel? dragPanel = _selectedProbeDragPanel;
        _selectedProbeDragPanel = null;
        Border? draggedRow = _draggedSelectedProbeRow;
        _draggedSelectedProbeRow = null;
        _draggedSelectedProbe = null;
        _selectedProbeDragStart = default;
        _draggedSelectedProbeTargetIndex = -1;
        _draggedSelectedProbePointerOffsetY = 0;
        _draggedSelectedProbeHeight = 0;
        try
        {
            draggedRow?.RenderTransform = null;
            if (dragPanel != null)
            {
                foreach (Control child in dragPanel.Children)
                    child.RenderTransform = null;
            }
        }
        catch (Exception exception)
        {
            TADNLog.Log($"ProbeDataSelectorWindow selected probe visual reset failed: {exception.Message}");
        }
        finally
        {
            ReleasePointerCapture(capturedPointer, "selected probe drag");
        }
    }

    private void ResetNicknameRuleGesture(IPointer? fallbackPointer)
    {
        IPointer? capturedPointer = _capturedNicknameRulePointer ?? fallbackPointer;
        _capturedNicknameRulePointer = null;
        StackPanel? rulesPanel = _nicknameRuleListPanel;
        _nicknameRuleListPanel = null;
        _draggedNicknameRuleList = null;
        Border? draggedRow = _draggedNicknameRuleRow;
        _draggedNicknameRuleRow = null;
        _draggedNicknameRule = null;
        _nicknameRuleDragStart = default;
        _draggedNicknameRuleTargetIndex = -1;
        _draggedNicknameRulePointerOffsetY = 0;
        _draggedNicknameRuleHeight = 0;
        try
        {
            draggedRow?.RenderTransform = null;
            if (rulesPanel != null)
            {
                foreach (Control child in rulesPanel.Children)
                    child.RenderTransform = null;
            }
        }
        catch (Exception exception)
        {
            TADNLog.Log($"ProbeDataSelectorWindow nickname rule visual reset failed: {exception.Message}");
        }
        finally
        {
            ReleasePointerCapture(capturedPointer, "nickname rule drag");
        }
    }

    private void CapturePointerOrRollback(
        IPointer pointer,
        Control target,
        ref IPointer? capturedPointer,
        string gestureName,
        Action rollbackGesture)
    {
        if (HasCapturedGesturePointer)
            throw new InvalidOperationException("The probe selector already owns a pointer capture.");

        capturedPointer = pointer;
        try
        {
            pointer.Capture(target);
        }
        catch
        {
            if (ReferenceEquals(capturedPointer, pointer))
                capturedPointer = null;
            bool wasResetting = _isResettingGestures;
            _isResettingGestures = true;
            try
            {
                rollbackGesture();
            }
            catch (Exception exception)
            {
                TADNLog.Log(
                    $"ProbeDataSelectorWindow {gestureName} rollback reset failed: {exception.Message}");
                ReleasePointerCapture(pointer, $"{gestureName} capture rollback");
            }
            finally
            {
                _isResettingGestures = wasResetting;
            }

            throw;
        }
    }

    private static void ReleasePointerCapture(IPointer? pointer, string gestureName)
    {
        if (pointer == null) return;

        try
        {
            pointer.Capture(null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"ProbeDataSelectorWindow {gestureName} pointer release failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Moves a nickname rule with keyboard shortcuts.
    /// </summary>
    private void MoveNicknameRuleByKeyboard(
        DeviceNicknameRule rule,
        List<DeviceNicknameRule> rulesList,
        int direction)
    {
        int currentIndex = rulesList.IndexOf(rule);
        int nextIndex = currentIndex + direction;
        if (currentIndex < 0 || nextIndex < 0 || nextIndex >= rulesList.Count) return;

        rulesList.RemoveAt(currentIndex);
        rulesList.Insert(nextIndex, rule);
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Applies nickname rule order to preserve replacement precedence.
    /// </summary>
    private void ApplyNicknameRuleOrder(
        List<DeviceNicknameRule> rulesList,
        DeviceNicknameRule dragged,
        int targetIndex)
    {
        int currentIndex = rulesList.IndexOf(dragged);
        if (currentIndex < 0) return;

        int clampedTargetIndex = Math.Clamp(targetIndex, 0, rulesList.Count - 1);
        if (currentIndex == clampedTargetIndex) return;

        rulesList.RemoveAt(currentIndex);
        rulesList.Insert(clampedTargetIndex, dragged);
        _changed(_probeCard);
    }

    /// <summary>
    /// Builds a typed probe grid tab.
    /// </summary>
    private Control BuildTypeBody(
        DataSourceTypeEnum type,
        ProbeSelectorVisualGeneration generation)
    {
        WrapPanel grid = ControlNames.Assign(new WrapPanel(), $"{type}Probes");
        List<DataSource> sources =
        [
            .. DataSource.DataSources.Values
                .Where(source => source.DataSourceType == type && ProbeValueFormatter.IsProbeDataSource(source))
                .OrderBy(
                    source => generation.DeviceNicknameResolver.Resolve(source),
                    NaturalStringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    source => generation.ProbeNicknameResolver.Resolve(source.DisplayName),
                    NaturalStringComparer.OrdinalIgnoreCase)
        ];
        if (sources.Count == 0)
            return EmptyText("No probes found");

        foreach (DataSource source in sources)
            grid.Children.Add(BuildProbeChoiceCard(source, generation));

        return grid;
    }

    /// <summary>
    /// Builds a card for a selectable live data source.
    /// </summary>
    private Border BuildProbeChoiceCard(
        DataSource source,
        ProbeSelectorVisualGeneration generation)
    {
        ProbeCardProbe? probeSettings = _probeCard.FindProbe(source.DataSourceKey);
        bool isSelected = probeSettings?.IsSelected == true;
        bool isExpanded = probeSettings is not null && _expandedTransformKeys.Contains(source.DataSourceKey);

        Grid card = ControlNames.Assign(new Grid(), "ProbeChoice");
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock deviceName = TrayAppDotNETSettingsUI.Text(generation.DeviceNicknameResolver.Resolve(source),
            _palette, Layout.CardTitleFontSize, FontWeight.SemiBold);
        deviceName.TextTrimming = TextTrimming.CharacterEllipsis;
        deviceName.Margin = Layout.TextColumnMargin;
        deviceName.VerticalAlignment = VerticalAlignment.Center;
        card.Children.Add(deviceName);

        TextBlock probeValue = TrayAppDotNETSettingsUI.Text(
            ProbeValueLine(source, probeSettings, generation.ProbeNicknameResolver),
            _palette, Layout.CardValueFontSize);
        probeValue.TextTrimming = TextTrimming.CharacterEllipsis;
        probeValue.Margin = Layout.ValueRowMargin;
        probeValue.VerticalAlignment = VerticalAlignment.Center;
        RegisterValueText(generation, source.DataSourceKey, probeValue);
        Grid valueRow = BuildProbeValueRow(source.DataSourceType, probeValue);
        Grid.SetRow(valueRow, 1);
        card.Children.Add(valueRow);

        Border enableToggle = BuildProbeEnableToggle(source, isSelected);
        Border truncateToggle = BuildProbeTruncateToggle(source, probeSettings);

        SettingsButton gear = ControlNames.Assign(
            BuildGearButton(isExpanded || ProbeTransformIsActive(probeSettings)),
            "ProbeTransform");
        gear.Margin = Layout.ActionButtonMargin;
        gear.Click += (_, _) => ToggleTransform(source);

        AddProbeControls(card, enableToggle, truncateToggle, gear, probeSettings, isExpanded);
        return ControlNames.Assign(WrapCard(card), "ProbeChoice");
    }

    /// <summary>
    /// Builds a home-tab card for a selected source that is not currently live.
    /// </summary>
    private Border BuildMissingProbeCard(ProbeCardProbe probe)
    {
        Grid card = ControlNames.Assign(new Grid(), "MissingProbe");
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        card.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock title = TrayAppDotNETSettingsUI.Text(probe.DataSourceKey, _palette, Layout.CardTitleFontSize,
            FontWeight.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Margin = Layout.TextColumnMargin;
        title.VerticalAlignment = VerticalAlignment.Center;
        card.Children.Add(title);

        TextBlock value = TrayAppDotNETSettingsUI.Text("--", _palette, Layout.CardValueFontSize);
        value.Margin = Layout.ValueRowMargin;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid valueRow = BuildProbeValueRow(DataSourceTypeEnum.Unknown, value);
        Grid.SetRow(valueRow, 1);
        card.Children.Add(valueRow);

        Border enableToggle = BuildMissingProbeEnableToggle(probe);
        Border truncateToggle = BuildMissingProbeTruncateToggle(probe);

        AddProbeControls(card, enableToggle, truncateToggle, null, null, false);
        return ControlNames.Assign(WrapCard(card), "MissingProbe");
    }

    /// <summary>
    /// Builds a probe value row.
    /// </summary>
    private Grid BuildProbeValueRow(DataSourceTypeEnum type, TextBlock value)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        Glyph probeGlyph = ProbeValueFormatter.GlyphFor(type);
        TextBlock glyph = TrayAppDotNETSettingsUI.Text(
            probeGlyph.Text,
            _palette,
            Layout.ValueGlyphFontSize);
        GlyphApplicator.ApplyTo(glyph, probeGlyph);
        glyph.Width = Layout.ValueGlyphWidth;
        glyph.Margin = Layout.ValueGlyphMargin;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(glyph);

        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        return row;
    }

    /// <summary>
    /// Adds independent transform and toggle control columns.
    /// </summary>
    private void AddProbeControls(
        Grid card,
        Control enableToggle,
        Control truncateToggle,
        SettingsButton? gear,
        ProbeCardProbe? selectedProbe,
        bool isExpanded)
    {
        Control? transformColumn = BuildProbeTransformColumn(gear, selectedProbe, isExpanded);
        if (transformColumn is not null)
        {
            Grid.SetColumn(transformColumn, 1);
            Grid.SetRowSpan(transformColumn, 2);
            card.Children.Add(transformColumn);
        }

        Grid toggleColumn = BuildProbeToggleColumn(enableToggle, truncateToggle);
        Grid.SetColumn(toggleColumn, 2);
        Grid.SetRowSpan(toggleColumn, 2);
        card.Children.Add(toggleColumn);
    }

    /// <summary>
    /// Builds the transform controls column.
    /// </summary>
    private Grid? BuildProbeTransformColumn(
        SettingsButton? gear,
        ProbeCardProbe? selectedProbe,
        bool isExpanded)
    {
        if (gear is null) return null;

        Grid row = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = Layout.ProbeControlRowMargin
        };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        if (!isExpanded || selectedProbe is null)
        {
            row.Children.Add(gear);
            return row;
        }

        Grid transformControls = BuildTransformControls(selectedProbe);
        row.Children.Add(transformControls);

        Grid.SetColumn(gear, 1);
        row.Children.Add(gear);
        return row;
    }

    /// <summary>
    /// Builds the mini-toggle control column.
    /// </summary>
    private Grid BuildProbeToggleColumn(
        Control enableToggle,
        Control truncateToggle)
    {
        Grid controls = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = Layout.ProbeToggleColumnMargin
        };
        controls.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        controls.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid enableRow = BuildProbeToggleRow(enableToggle);
        controls.Children.Add(enableRow);

        Grid truncateRow = BuildProbeToggleRow(truncateToggle);
        Grid.SetRow(truncateRow, 1);
        controls.Children.Add(truncateRow);
        return controls;
    }

    /// <summary>
    /// Builds one mini-toggle row.
    /// </summary>
    private static Grid BuildProbeToggleRow(Control toggle)
    {
        Grid row = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(toggle);
        return row;
    }

    /// <summary>
    /// Builds the per-probe enable toggle for a live selector card.
    /// </summary>
    private SettingsMiniToggle BuildProbeEnableToggle(DataSource source, bool isSelected)
    {
        return BuildLabeledMiniToggle(
            "Enable",
            isSelected,
            true,
            enabled => ToggleProbe(source, enabled));
    }

    /// <summary>
    /// Builds the per-probe enable toggle for a missing selector card.
    /// </summary>
    private SettingsMiniToggle BuildMissingProbeEnableToggle(ProbeCardProbe probe)
    {
        return BuildLabeledMiniToggle(
            "Enable",
            true,
            true,
            enabled =>
            {
                if (enabled) return;
                _probeCard.Probes.Remove(probe);
                _expandedTransformKeys.Remove(probe.DataSourceKey);
                _changed(_probeCard);
                RebuildContent();
            });
    }

    /// <summary>
    /// Builds the per-probe truncate toggle for a live selector card.
    /// </summary>
    private SettingsMiniToggle BuildProbeTruncateToggle(DataSource source, ProbeCardProbe? probe)
    {
        return BuildLabeledMiniToggle(
            "Truncate",
            probe?.TruncateValue == true,
            true,
            truncateValue => SetProbeTruncateValue(source, truncateValue));
    }

    /// <summary>
    /// Builds the per-probe truncate toggle for a missing selector card.
    /// </summary>
    private SettingsMiniToggle BuildMissingProbeTruncateToggle(ProbeCardProbe probe)
    {
        return BuildLabeledMiniToggle(
            "Truncate",
            probe.TruncateValue,
            true,
            truncateValue =>
            {
                if (probe.TruncateValue == truncateValue) return;
                probe.TruncateValue = truncateValue;
                _changed(_probeCard);
            });
    }

    /// <summary>
    /// Builds a labeled mini toggle.
    /// </summary>
    private SettingsMiniToggle BuildLabeledMiniToggle(string labelText, bool isChecked, bool isEnabled, Action<bool> changed)
    {
        SettingsMiniToggle toggle = ControlNames.Assign(
            new SettingsMiniToggle(_palette, BuildTruncateToggleLayout(), labelText)
            {
                IsChecked = isChecked,
                IsEnabled = isEnabled
            },
            $"Probe{labelText}");
        toggle.CheckedChanged += (_, enabled) => changed(enabled);
        return toggle;
    }

    private SettingsMiniToggleLayout BuildTruncateToggleLayout() =>
        new()
        {
            Width = Layout.TruncateToggleWidth,
            TrackWidth = Layout.TruncateToggleTrackWidth,
            TrackHeight = TruncateToggleTrackHeight,
            ThumbSize = TruncateToggleThumbSize,
            ThumbHoverSize = TruncateToggleThumbSize,
            ThumbCheckedSize = TruncateToggleThumbSize,
            LabelFontSize = Layout.TruncateToggleFontSize,
            TrackCornerRadius = TruncateToggleTrackCornerRadius,
            ThumbCornerRadius = TruncateToggleThumbCornerRadius,
            BorderThickness = Layout.RootBorderThickness,
            ThumbUncheckedMargin = TruncateToggleThumbUncheckedMargin,
            ThumbCheckedMargin = TruncateToggleThumbCheckedMargin,
            LabelMargin = Layout.TruncateToggleLabelMargin,
            Margin = Layout.TruncateToggleMargin,
            EnabledOpacity = Layout.FullOpacity,
            DisabledOpacity = Layout.ControlDisabledOpacity
        };

    /// <summary>
    /// Builds the inline transform editor for a selected probe.
    /// </summary>
    private Grid BuildTransformControls(ProbeCardProbe probe)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock label = TrayAppDotNETSettingsUI.Text("X=", _palette, Layout.TransformLabelFontSize);
        label.Margin = Layout.TransformLabelMargin;
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);

        TextBox textBox = TransformTextBox(probe.TransformString, Layout.TransformInlineBoxWidth);
        ControlNames.Assign(textBox, "ProbeTransform");
        textBox.Tag = probe;
        textBox.GotFocus += TransformTextBoxGotFocus;
        textBox.KeyDown += TransformTextBoxKeyDown;
        textBox.LostFocus += TransformTextBoxLostFocus;
        Grid.SetColumn(textBox, 1);
        row.Children.Add(textBox);
        return row;
    }

    /// <summary>
    /// Builds the settings gear button for a selectable probe card.
    /// </summary>
    private SettingsButton BuildGearButton(bool transformIsActive)
    {
        SettingsButton button = new(GlyphCatalog.SETTINGS.Text, _palette, transparentBase: true)
        {
            Width = Layout.ActionButtonWidth,
            Height = Layout.ActionButtonHeight,
            MinHeight = Layout.ActionButtonHeight,
            Padding = Layout.ZeroThickness,
            Label = { FontFamily = TrayAppDotNETSettingsUI.IconFont, FontSize = Layout.ActionButtonGlyphFontSize }
        };
        GlyphApplicator.ApplyTo(button.Label, GlyphCatalog.SETTINGS);
        ApplyGearButtonTransformVisual(button, transformIsActive);
        return button;
    }

    /// <summary>
    /// Applies explicit active-transform visuals to icon-only gear buttons.
    /// </summary>
    private void ApplyGearButtonTransformVisual(SettingsButton button, bool transformIsActive)
    {
        button.IsEnabled = true;
        button.Opacity = Layout.FullOpacity;
        button.Label.Opacity = transformIsActive
            ? Layout.FullOpacity
            : Layout.TransformActionButtonInactiveGlyphOpacity;
        button.Label.Foreground = TrayAppDotNETSettingsUI.Brush(
            transformIsActive ? _palette.Foreground : _palette.SecondaryForeground);
    }

    /// <summary>
    /// Checks whether a probe has an active transform expression.
    /// </summary>
    private static bool ProbeTransformIsActive(ProbeCardProbe? probe) =>
        !string.IsNullOrWhiteSpace(probe?.TransformString);

    /// <summary>
    /// Builds a transform text box.
    /// </summary>
    private TextBox TransformTextBox(string text, double width)
    {
        TextBox textBox = new()
        {
            Width = width,
            Height = Layout.TransformBoxHeight,
            MinHeight = Layout.TransformBoxMinHeight,
            Text = text,
            PlaceholderText = "X",
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = Layout.CardValueFontSize,
            Background = TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            Foreground = TrayAppDotNETSettingsUI.Brush(_palette.Foreground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = Layout.ZeroThickness,
            Padding = Layout.TransformBoxPadding,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        TrayAppDotNETSettingsUI.ApplyTextBoxResources(
            textBox,
            _palette,
            TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            TrayAppDotNETSettingsUI.Brush(_palette.Hover),
            TrayAppDotNETSettingsUI.Brush(_palette.TextBoxFocused));
        return textBox;
    }

    /// <summary>
    /// Builds a device nickname rule text box.
    /// </summary>
    private TextBox NicknameTextBox(string text, string placeholder, double width)
    {
        TextBox textBox = new()
        {
            Width = width,
            Height = Layout.NicknameTextBoxHeight,
            MinHeight = Layout.NicknameTextBoxHeight,
            MaxHeight = Layout.NicknameTextBoxHeight,
            Text = text,
            PlaceholderText = placeholder,
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = Layout.CardValueFontSize,
            Background = TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            Foreground = TrayAppDotNETSettingsUI.Brush(_palette.Foreground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = Layout.ZeroThickness,
            Padding = Layout.NicknameTextBoxPadding,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        TrayAppDotNETSettingsUI.ApplyTextBoxResources(
            textBox,
            _palette,
            TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            TrayAppDotNETSettingsUI.Brush(_palette.Hover),
            TrayAppDotNETSettingsUI.Brush(_palette.TextBoxFocused));
        return textBox;
    }

    /// <summary>
    /// Builds the delete button for a device nickname rule.
    /// </summary>
    private SettingsButton BuildNicknameDeleteButton()
    {
        SettingsButton button = new(GlyphCatalog.CLOSE.Text, _palette, transparentBase: true)
        {
            Width = Layout.NicknameDeleteButtonWidth,
            Height = Layout.NicknameDeleteButtonHeight,
            MinHeight = Layout.NicknameDeleteButtonHeight,
            Padding = Layout.ZeroThickness,
            Margin = Layout.NicknameDeleteButtonMargin,
            Label = { FontFamily = TrayAppDotNETSettingsUI.IconFont, FontSize = Layout.NicknameDeleteButtonFontSize }
        };
        GlyphApplicator.ApplyTo(button.Label, GlyphCatalog.CLOSE);
        return button;
    }

    /// <summary>
    /// Wraps a selector card in the common card chrome.
    /// </summary>
    private Border WrapCard(Control content)
    {
        content.VerticalAlignment = VerticalAlignment.Top;
        return new Border
        {
            Width = Layout.GridCardWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CardBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = Layout.RootBorderThickness,
            CornerRadius = _settings.EnableRoundedCorners ? Layout.CardCornerRadius : Layout.ZeroCornerRadius,
            Padding = Layout.NicknameCardPadding,
            Margin = Layout.CardMargin,
            Child = content
        };
    }

    /// <summary>
    /// Wraps a nickname rule row in the nickname card chrome.
    /// </summary>
    private Border WrapNicknameCard(Control content) =>
        new()
        {
            Width = Layout.NicknameCardWidth,
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CardBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = Layout.RootBorderThickness,
            CornerRadius = _settings.EnableRoundedCorners ? Layout.CardCornerRadius : Layout.ZeroCornerRadius,
            Padding = Layout.CardPadding,
            Margin = Layout.CardMargin,
            Child = content
        };

    /// <summary>
    /// Builds empty-state text for a tab body.
    /// </summary>
    private TextBlock EmptyText(string text)
    {
        TextBlock block = TrayAppDotNETSettingsUI.Text(text, _palette, Layout.EmptyStateFontSize);
        block.Foreground = TrayAppDotNETSettingsUI.Brush(_palette.SecondaryForeground);
        block.HorizontalAlignment = HorizontalAlignment.Center;
        block.VerticalAlignment = VerticalAlignment.Center;
        return block;
    }

    /// <summary>
    /// Toggles whether the probe card displays a source.
    /// </summary>
    private void ToggleProbe(DataSource source, bool enabled)
    {
        ProbeCardProbe? probe = _probeCard.FindProbe(source.DataSourceKey);
        if (enabled)
        {
            if (probe is null)
            {
                _probeCard.Probes.Add(new ProbeCardProbe { DataSourceKey = source.DataSourceKey });
                _changed(_probeCard);
            }
            else if (!probe.IsSelected)
            {
                probe.IsSelected = true;
                _changed(_probeCard);
            }

            RebuildContent();
            return;
        }

        if (probe is not null)
        {
            probe.IsSelected = false;
            _expandedTransformKeys.Remove(source.DataSourceKey);
            RemoveProbeSettingsIfDefault(probe);
            _changed(_probeCard);
        }

        RebuildContent();
    }

    /// <summary>
    /// Persists per-probe value truncation from a selector card.
    /// </summary>
    private void SetProbeTruncateValue(DataSource source, bool truncateValue)
    {
        ProbeCardProbe? probe = _probeCard.FindProbe(source.DataSourceKey);
        if (probe is null)
        {
            if (!truncateValue) return;

            _probeCard.Probes.Add(new ProbeCardProbe
            {
                DataSourceKey = source.DataSourceKey,
                IsSelected = false,
                TruncateValue = true
            });
            _changed(_probeCard);
            RebuildContent();
            return;
        }

        if (probe.TruncateValue == truncateValue) return;

        probe.TruncateValue = truncateValue;
        if (!truncateValue)
            RemoveProbeSettingsIfDefault(probe);
        _changed(_probeCard);
        RefreshVisibleValues();
    }

    /// <summary>
    /// Removes inactive probe settings when no transform or truncate state is stored.
    /// </summary>
    private void RemoveProbeSettingsIfDefault(ProbeCardProbe probe)
    {
        if (probe.IsSelected) return;
        if (probe.TruncateValue) return;
        if (!string.IsNullOrWhiteSpace(probe.TransformString)) return;

        _probeCard.Probes.Remove(probe);
        _expandedTransformKeys.Remove(probe.DataSourceKey);
    }

    /// <summary>
    /// Adds a new global device nickname rule.
    /// </summary>
    private void AddDeviceNicknameRule()
    {
        _settings.DeviceNicknameRules.Add(new DeviceNicknameRule());
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Adds a new global probe nickname rule.
    /// </summary>
    private void AddProbeNicknameRule()
    {
        _settings.ProbeNicknameRules.Add(new DeviceNicknameRule());
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Restores the default hardware-type device nickname rules.
    /// </summary>
    private void LoadDefaultDeviceNicknames()
    {
        if (!_settings.LoadDefaultDeviceNicknameRules()) return;
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Restores the default probe nickname rules.
    /// </summary>
    private void LoadDefaultProbeNicknames()
    {
        if (!_settings.LoadDefaultProbeNicknameRules()) return;
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Deletes a global device nickname rule.
    /// </summary>
    private void DeleteDeviceNicknameRule(DeviceNicknameRule rule)
    {
        if (!_settings.DeviceNicknameRules.Remove(rule)) return;
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Deletes a global probe nickname rule.
    /// </summary>
    private void DeleteProbeNicknameRule(DeviceNicknameRule rule)
    {
        if (!_settings.ProbeNicknameRules.Remove(rule)) return;
        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Removes persisted probe sensors that were not found by live LHM discovery.
    /// </summary>
    private void ClearDeadSensors()
    {
        if (!DataSource.DataSources.Values.Any(static source => source.IsLiveHardwareSensor)) return;

        HashSet<string> deadKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (DataSource source in DataSource.DataSources.Values)
        {
            if (source.IsLiveHardwareSensor) continue;
            if (!ProbeValueFormatter.IsProbeDataSource(source)) continue;
            if (string.IsNullOrWhiteSpace(source.DataSourceKey)) continue;
            deadKeys.Add(source.DataSourceKey);
        }

        if (deadKeys.Count == 0) return;

        foreach (string deadKey in deadKeys)
            DataSource.Unregister(deadKey);

        foreach (ProbeCard probeCard in _settings.ProbeCards)
            RemoveDeadProbeSelections(probeCard, deadKeys);
        RemoveDeadProbeSelections(_probeCard, deadKeys);

        foreach (string deadKey in deadKeys)
        {
            _expandedTransformKeys.Remove(deadKey);
        }

        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Removes selected probes whose data source was cleared.
    /// </summary>
    private static void RemoveDeadProbeSelections(ProbeCard probeCard, HashSet<string> deadKeys)
    {
        for (int i = probeCard.Probes.Count - 1; i >= 0; i--)
        {
            if (!deadKeys.Contains(probeCard.Probes[i].DataSourceKey)) continue;
            probeCard.Probes.RemoveAt(i);
        }
    }

    /// <summary>
    /// Commits a nickname rule field when Enter is pressed.
    /// </summary>
    private void NicknameRuleKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (e.Key != Key.Enter) return;
        CommitNicknameRuleTextBox(textBox);
        e.Handled = true;
    }

    /// <summary>
    /// Commits a nickname rule edit when focus leaves its editor.
    /// </summary>
    private void NicknameRuleLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox) CommitNicknameRuleTextBox(textBox);
    }

    /// <summary>
    /// Persists one nickname rule text-box edit.
    /// </summary>
    private void CommitNicknameRuleTextBox(TextBox textBox)
    {
        if (textBox.Tag is not NicknameRuleEditorTag editorTag) return;

        string next = textBox.Text ?? string.Empty;
        bool changed = editorTag.Field switch
        {
            NicknameRuleField.Target => CommitNicknameTarget(editorTag.Rule, next.Trim()),
            NicknameRuleField.Replacement => CommitNicknameReplacement(editorTag.Rule, next),
            _ => false
        };
        if (!changed) return;

        _changed(_probeCard);
        RebuildContent();
    }

    /// <summary>
    /// Applies a target regex edit to a nickname rule.
    /// </summary>
    private static bool CommitNicknameTarget(DeviceNicknameRule rule, string next)
    {
        if (string.Equals(rule.TargetRegex, next, StringComparison.Ordinal)) return false;
        rule.TargetRegex = next;
        return true;
    }

    /// <summary>
    /// Applies a replacement-string edit to a nickname rule.
    /// </summary>
    private static bool CommitNicknameReplacement(DeviceNicknameRule rule, string next)
    {
        if (string.Equals(rule.ReplacementString, next, StringComparison.Ordinal)) return false;
        rule.ReplacementString = next;
        return true;
    }

    /// <summary>
    /// Toggles the transform editor for a source without selecting the probe.
    /// </summary>
    private void ToggleTransform(DataSource source)
    {
        ProbeCardProbe? probe = _probeCard.FindProbe(source.DataSourceKey);
        if (probe is null)
        {
            probe = new ProbeCardProbe
            {
                DataSourceKey = source.DataSourceKey,
                IsSelected = false
            };
            _probeCard.Probes.Add(probe);
            _changed(_probeCard);
        }

        if (!_expandedTransformKeys.Add(source.DataSourceKey))
        {
            _expandedTransformKeys.Remove(source.DataSourceKey);
            RemoveProbeSettingsIfDefault(probe);
            _changed(_probeCard);
        }

        RebuildContent();
    }

    /// <summary>
    /// Tracks the active transform text box for outside-click commit behavior.
    /// </summary>
    private void TransformTextBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && IsActiveGenerationVisual(textBox))
            _focusedTransformTextBox = textBox;
    }

    /// <summary>
    /// Commits the transform expression on Enter.
    /// </summary>
    private void TransformTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (!IsActiveGenerationVisual(textBox)) return;
        if (e.Key != Key.Enter) return;
        CommitTransformTextBox(textBox);
        e.Handled = true;
    }

    /// <summary>
    /// Commits the transform expression when focus leaves the editor.
    /// </summary>
    private void TransformTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (!IsActiveGenerationVisual(textBox)) return;

        CommitTransformTextBox(textBox);
        if (ReferenceEquals(_focusedTransformTextBox, textBox))
            _focusedTransformTextBox = null;
    }

    /// <summary>
    /// Drops transform text focus when the selector is clicked outside the active editor.
    /// </summary>
    private void OnSelectorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_focusedTransformTextBox is null) return;
        if (IsSelfOrDescendant(_focusedTransformTextBox, e.Source as Visual)) return;

        DropTransformTextBoxFocus(_focusedTransformTextBox);
    }

    /// <summary>
    /// Commits transform text and moves focus to selector chrome.
    /// </summary>
    private void DropTransformTextBoxFocus(TextBox textBox)
    {
        CommitTransformTextBox(textBox);
        textBox.ClearSelection();
        _activeVisualGeneration?.FocusSink.Focus();
    }

    /// <summary>
    /// Determines whether a visual is the target control or one of its descendants.
    /// </summary>
    private static bool IsSelfOrDescendant(Visual owner, Visual? visual)
    {
        if (visual is null) return false;
        if (ReferenceEquals(visual, owner)) return true;
        return visual.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, owner));
    }

    private bool IsActiveGenerationVisual(Visual visual)
    {
        ProbeSelectorVisualGeneration? generation = _activeVisualGeneration;
        if (generation == null || generation.ContentGeneration.IsDisposed) return false;
        return IsSelfOrDescendant(generation.ContentGeneration.Root, visual);
    }

    /// <summary>
    /// Persists the transform expression from a text box.
    /// </summary>
    private void CommitTransformTextBox(TextBox textBox)
    {
        if (textBox.Tag is not ProbeCardProbe probe) return;
        string next = (textBox.Text ?? string.Empty).Trim();
        if (string.Equals(next, probe.TransformString, StringComparison.Ordinal)) return;
        bool previousTransformIsActive = ProbeTransformIsActive(probe);
        probe.TransformString = next;
        bool nextTransformIsActive = ProbeTransformIsActive(probe);
        if (!nextTransformIsActive)
            RemoveProbeSettingsIfDefault(probe);
        _changed(_probeCard);
        if (previousTransformIsActive != nextTransformIsActive)
        {
            RebuildContent();
            return;
        }

        RefreshVisibleValues();
    }

    /// <summary>
    /// Registers a value text block for live refresh.
    /// </summary>
    private static void RegisterValueText(
        ProbeSelectorVisualGeneration generation,
        string dataSourceKey,
        TextBlock textBlock)
    {
        if (!generation.ValueTextByKey.TryGetValue(dataSourceKey, out List<TextBlock>? list))
        {
            list = [];
            generation.ValueTextByKey[dataSourceKey] = list;
        }

        list.Add(textBlock);
    }

    /// <summary>
    /// Refreshes the value text in visible selector cards.
    /// </summary>
    private void RefreshVisibleValues()
    {
        ProbeSelectorVisualGeneration? generation = _activeVisualGeneration;
        if (generation == null) return;

        foreach ((string dataSourceKey, List<TextBlock> textBlocks) in generation.ValueTextByKey)
        {
            DataSource? source = DataSource.Find(dataSourceKey);
            if (source is null) continue;
            ProbeCardProbe? probe = _probeCard.FindProbe(dataSourceKey);
            string value = ProbeValueLine(source, probe, generation.ProbeNicknameResolver);
            foreach (TextBlock textBlock in textBlocks)
                textBlock.Text = value;
        }
    }

    /// <summary>
    /// Formats a probe name and current value for selector cards.
    /// </summary>
    private static string ProbeValueLine(
        DataSource source,
        ProbeCardProbe? probe,
        ProbeNicknameResolver probeNicknameResolver) =>
        $"{probeNicknameResolver.Resolve(source.DisplayName)}: "
        + ProbeValueFormatter.FormatValue(source, probe, probe?.TruncateValue == true);

    /// <summary>
    /// Refreshes probe values after each LHM poll.
    /// </summary>
    private void OnPollTickCompleted()
    {
        if (_windowResources.IsDisposed || !IsVisible) return;
        RefreshVisibleValues();
    }

    /// <summary>Detaches external publishers before retiring candidate-owned controls.</summary>
    protected override void OnClosed(EventArgs e)
    {
        DisposeSelectorResources();
        base.OnClosed(e);
    }

    private void DisposeSelectorResources()
    {
        _windowResources.Dispose();

        try
        {
            ResetHomeGestureState();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"ProbeDataSelectorWindow gesture cleanup failed: {exception.Message}");
        }

        ProbeSelectorVisualGeneration? generation = _activeVisualGeneration;
        _activeVisualGeneration = null;
        try
        {
            Content = null;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"ProbeDataSelectorWindow content detach failed: {exception.Message}");
        }
        finally
        {
            if (generation is { ContentGeneration.IsDisposed: false })
                generation.ContentGeneration.Dispose();
        }

        _focusedTransformTextBox = null;
    }

    /// <summary>
    /// Resolves the tab label.
    /// </summary>
    private static string TabLabel(ProbeSelectorTab tab) => tab switch
    {
        ProbeSelectorTab.Home => "Home",
        ProbeSelectorTab.Temperatures => "Temperatures",
        ProbeSelectorTab.Power => "Power",
        ProbeSelectorTab.Load => "Load",
        ProbeSelectorTab.Clocks => "Clocks",
        ProbeSelectorTab.Voltages => "Voltages",
        _ => string.Empty
    };

    private enum ProbeSelectorTab
    {
        Home,
        Temperatures,
        Power,
        Load,
        Clocks,
        Voltages
    }

    private enum NicknameRuleField
    {
        Target,
        Replacement
    }

    private sealed record NicknameRuleEditorTag(DeviceNicknameRule Rule, NicknameRuleField Field);

    /// <summary>Owns the maps, resolvers, panels, and root for one selector rebuild.</summary>
    private sealed class ProbeSelectorVisualGeneration(
        ProbeSelectorTab selectedTab,
        DeviceNicknameResolver deviceNicknameResolver,
        ProbeNicknameResolver probeNicknameResolver,
        UIResourceScope resources)
    {
        private UIContentGeneration? _contentGeneration;
        private bool _retired;

        public ProbeSelectorTab SelectedTab { get; } = selectedTab;
        public DeviceNicknameResolver DeviceNicknameResolver { get; } = deviceNicknameResolver;
        public ProbeNicknameResolver ProbeNicknameResolver { get; } = probeNicknameResolver;
        public UIResourceScope Resources { get; } = resources;
        public Dictionary<string, List<TextBlock>> ValueTextByKey { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Control FocusSink { get; set; } = null!;
        public StackPanel? SelectedProbeListPanel { get; set; }

        public UIContentGeneration ContentGeneration =>
            _contentGeneration
            ?? throw new InvalidOperationException("The probe selector generation has not been completed.");

        public void AttachContentGeneration(UIContentGeneration contentGeneration)
        {
            ArgumentNullException.ThrowIfNull(contentGeneration);
            if (_contentGeneration != null)
                throw new InvalidOperationException("The probe selector generation is already complete.");
            _contentGeneration = contentGeneration;
        }

        public void Retire()
        {
            if (_retired) return;
            _retired = true;
            ValueTextByKey.Clear();
            SelectedProbeListPanel?.Children.Clear();
            SelectedProbeListPanel = null;
            FocusSink?.DataContext = null;
        }
    }

}
