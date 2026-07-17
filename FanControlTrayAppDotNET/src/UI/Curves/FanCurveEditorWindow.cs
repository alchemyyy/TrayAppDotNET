using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FanControlTrayAppDotNET.UI.Settings;

namespace FanControlTrayAppDotNET.UI.Curves;

public sealed partial class FanCurveEditorWindow : Window
{
    private const int SmoothnessMin = 0;
    private const int SmoothnessMax = 100;

    private readonly Fan _fan;
    private readonly Curve _curve;
    private readonly AppSettings _settings;
    private readonly SettingsPalette _palette;
    private readonly FanCurveEditor _editor;
    private readonly Border _dataSourceSelectionBox;
    private readonly TextBlock _dataSourceSelectionText;
    private readonly SettingsSearchableListBox _dataSourceList;
    private readonly SettingsToggle _rpmModeToggle;
    private readonly SettingsNumberBox _maxRPMBox;
    private readonly SettingsNumberBox _minRPMBox;
    private readonly SettingsNumberBox _maxDutyBox;
    private readonly SettingsNumberBox _minDutyBox;
    private readonly SettingsNumberBox _smoothnessBox;
    private readonly SettingsToggle _preventDecreasingToggle;
    private readonly Border _maxRPMRow;
    private readonly SettingsButton _rescaleCurveButton;
    private readonly UIResourceScope _windowResources = new(nameof(FanCurveEditorWindow));
    private FanCurveEditorAxamlProperties? _layout;
    private bool _suppressEvents;
    private bool _rescaleCurvePending;
    private bool _hasPreservedNonMonotonicNodes;
    private double _rescaleCurveOldMax;
    private double _rescaleCurveNewMax;

    public FanCurveEditorWindow()
    {
        _fan = null!;
        _curve = null!;
        _settings = null!;
        _palette = null!;
        _editor = null!;
        _dataSourceSelectionBox = null!;
        _dataSourceSelectionText = null!;
        _dataSourceList = null!;
        _rpmModeToggle = null!;
        _maxRPMBox = null!;
        _minRPMBox = null!;
        _maxDutyBox = null!;
        _minDutyBox = null!;
        _smoothnessBox = null!;
        _preventDecreasingToggle = null!;
        _maxRPMRow = null!;
        _rescaleCurveButton = null!;

        InitializeComponent();
        InitializeComponentState();
    }

    public FanCurveEditorWindow(Fan fan, Curve curve, AppSettings settings)
    {
        _fan = fan;
        _curve = curve;
        _settings = settings;

        try
        {
            InitializeComponent();
            InitializeComponentState();

            _palette = FanSettingsWindow.CreatePalette(
                AppServices.Theme,
                settings,
                AppTheme.ResolveEffectiveIsLightTheme(settings));

            _curve.EnsureEditorDefaults(DefaultMaxRPM(fan));
            ClampCurveLimits();
            EnsureCurveDataSource();
            EnsureCurveNodesOnDataSourceAxis();
            Curve.Register(_curve);

            Title = $"Fan Curve: {_curve.CurveName}";

            _editor = new FanCurveEditor
            {
                Width = Layout.GraphWidth,
                Height = Layout.GraphHeight,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                EditorLayout = Layout,
                Palette = FanCurveEditorPalette.FromSettingsPalette(
                    _palette,
                    AppServices.Theme ?? AppTheme.Default,
                    AppTheme.ResolveEffectiveIsLightTheme(settings))
            };
            _windowResources.Own(_editor);
            _editor.CurveChanged += OnEditorCurveChanged;
            _windowResources.Add(() => _editor.CurveChanged -= OnEditorCurveChanged);
            _editor.GraphEditStarting += OnEditorGraphEditStarting;
            _windowResources.Add(() => _editor.GraphEditStarting -= OnEditorGraphEditStarting);
            _hasPreservedNonMonotonicNodes = _curve.PreventDecreasing;

            _dataSourceSelectionText = DataSourceSelectionText();
            _dataSourceSelectionBox = DataSourceSelectionBox(_dataSourceSelectionText);
            _dataSourceList = _windowResources.Own(DataSourceList());
            _rpmModeToggle = TrayAppDotNETSettingsUI.Toggle(_palette, _curve.RPMMode, OnRPMModeChanged);
            _maxRPMBox = _windowResources.Own(Number(
            _curve.MaxRPM,
            1,
            Math.Max(10000, _curve.MaxRPM),
            "RPM",
            Layout.MaxRPMNumberBoxMinWidth));
            _minRPMBox = _windowResources.Own(Number(
            _curve.MinRPM,
            0,
            Math.Max(10000, _curve.MaxRPM),
            "RPM",
            Layout.MinRPMNumberBoxMinWidth));
            _maxDutyBox = _windowResources.Own(
                Number(_curve.MaxDutyCycle, 1, 100, "%", Layout.MaxDutyNumberBoxMinWidth));
            _minDutyBox = _windowResources.Own(
                Number(_curve.MinDutyCycle, 0, 100, "%", Layout.MinDutyNumberBoxMinWidth));
            _smoothnessBox = _windowResources.Own(Number(
            _curve.SmoothingFactor,
            SmoothnessMin,
            SmoothnessMax,
            string.Empty,
            Layout.SmoothnessNumberBoxMinWidth));
            _preventDecreasingToggle =
                TrayAppDotNETSettingsUI.Toggle(_palette, _curve.PreventDecreasing, OnPreventDecreasingChanged);
            _rescaleCurveButton = RescaleCurveButton();
            _rescaleCurveButton.Click += (_, _) => ApplyPendingNodeRescale();

            PopulateDataSources();
            WireControls();
            _maxRPMRow = BuildMaxRPMRow();

            Content = BuildContent();
            LoadControlState();
            RefreshEditorBinding();
            // Dispose the editor first so its DataSource publisher root is detached before child controls
            _windowResources.Add(_editor.Dispose);
        }
        catch
        {
            _windowResources.Dispose();
            throw;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _windowResources.Dispose();
        base.OnClosed(e);
    }

    private void InitializeComponentState() => _layout = AxamlFanCurveEditor;

    private FanCurveEditorAxamlProperties Layout =>
        _layout ?? throw new InvalidOperationException("Fan curve editor layout resources have not been loaded.");

    private Border BuildContent()
    {
        Grid shell = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
            Margin = Layout.ZeroThickness
        };
        shell.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        shell.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        TextBlock title = TrayAppDotNETSettingsUI.Text(
            Title ?? "Fan Curve",
            _palette,
            Layout.TitleFontSize,
            FontWeight.SemiBold);
        title.Margin = Layout.TitleMargin;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        shell.Children.Add(title);

        Grid main = new() { Margin = Layout.MainMargin };
        main.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.LeftColumnWidth)));
        main.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        main.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        main.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Border dataSource = DataSourceBlock();
        Grid.SetColumn(dataSource, 0);
        Grid.SetRow(dataSource, 0);
        main.Children.Add(dataSource);

        Grid graphHost = new()
        {
            Width = Layout.GraphWidth,
            Height = Layout.GraphHeight,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        graphHost.Children.Add(_editor);
        Grid.SetColumn(graphHost, 1);
        Grid.SetRow(graphHost, 0);
        main.Children.Add(graphHost);

        Grid controlGrid = BuildControlGrid();
        Grid.SetColumn(controlGrid, 0);
        Grid.SetColumnSpan(controlGrid, 2);
        Grid.SetRow(controlGrid, 1);
        main.Children.Add(controlGrid);

        Grid.SetRow(main, 1);
        shell.Children.Add(main);
        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = Layout.RootBorderThickness,
            CornerRadius = _settings.EnableRoundedCorners ? Layout.RootCornerRadius : Layout.ZeroCornerRadius,
            Child = shell
        };
    }

    private Border BuildMaxRPMRow()
    {
        _maxRPMBox.Margin = Layout.ControlGridNumberBoxMargin;
        _maxRPMBox.HorizontalAlignment = HorizontalAlignment.Right;

        Grid row = ControlGridCardContent("Max RPM");
        Grid.SetColumn(_maxRPMBox, 1);
        row.Children.Add(_maxRPMBox);
        Border card = CompactCard(row);
        ConfigureControlGridCard(card, isWide: false);
        return card;
    }

    /// <summary>
    /// Builds the RPM-mode card with the axis-rescale action below the toggle row.
    /// </summary>
    private Border BuildRPMModeRow()
    {
        Grid toggleRow = ControlGridCardContent("RPM mode");
        _rpmModeToggle.Margin = Layout.ControlGridControlMargin;
        _rpmModeToggle.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_rpmModeToggle, 1);
        toggleRow.Children.Add(_rpmModeToggle);

        StackPanel content = new()
        {
            Orientation = Orientation.Vertical
        };
        content.Children.Add(toggleRow);
        _rescaleCurveButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _rescaleCurveButton.Margin = Layout.RescaleCurveButtonMargin;
        content.Children.Add(_rescaleCurveButton);

        Border card = CompactCard(content);
        ConfigureControlGridCard(card, isWide: false);
        return card;
    }

    /// <summary>
    /// Builds the data-source selected value, search box, and list block.
    /// </summary>
    private Border DataSourceBlock()
    {
        Grid content = new()
        {
            VerticalAlignment = VerticalAlignment.Stretch
        };
        content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        content.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        content.Children.Add(DataSourceSelectedRow());
        _dataSourceList.Margin = Layout.DataSourceListMargin;
        _dataSourceList.Width = double.NaN;
        _dataSourceList.HorizontalAlignment = HorizontalAlignment.Stretch;
        _dataSourceList.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetRow(_dataSourceList, 1);
        content.Children.Add(_dataSourceList);
        Border card = CompactCard(content);
        card.Height = Layout.GraphHeight;
        return card;
    }

    /// <summary>
    /// Builds the data-source label and selected value row.
    /// </summary>
    private Grid DataSourceSelectedRow()
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.RowLabelWidth)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        TextBlock title = TrayAppDotNETSettingsUI.TitleText("Data source", _palette);
        title.Margin = Layout.DataSourceTitleMargin;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(title);
        _dataSourceSelectionBox.Margin = Layout.DataSourceSelectedMargin;
        Grid.SetColumn(_dataSourceSelectionBox, 1);
        row.Children.Add(_dataSourceSelectionBox);
        return row;
    }

    /// <summary>
    /// Creates the selected data-source text.
    /// </summary>
    private TextBlock DataSourceSelectionText()
    {
        TextBlock text = TrayAppDotNETSettingsUI.Text(string.Empty, _palette, Layout.DataSourceSelectedFontSize);
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.VerticalAlignment = VerticalAlignment.Center;
        return text;
    }

    /// <summary>
    /// Creates the selected data-source display box.
    /// </summary>
    private Border DataSourceSelectionBox(TextBlock text)
    {
        Border box = new()
        {
            Height = Layout.DataSourceSelectedHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground),
            CornerRadius = _settings.EnableRoundedCorners
                ? Layout.DataSourceSelectedCornerRadius
                : Layout.ZeroCornerRadius,
            Padding = Layout.DataSourceSelectedPadding,
            Child = text
        };
        box.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
            _dataSourceList.FocusSearch();
            e.Handled = true;
        };
        return box;
    }

    /// <summary>
    /// Creates the searchable data-source list.
    /// </summary>
    private SettingsSearchableListBox DataSourceList()
    {
        SettingsSearchableListBox list = new(_palette)
        {
            Width = double.NaN,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ListHeight = Layout.DataSourceListHeight,
            SearchBoxHeight = Layout.DataSourceSearchBoxHeight,
            SearchBoxPadding = Layout.DataSourceSearchBoxPadding,
            ClearButtonWidth = Layout.DataSourceClearButtonWidth,
            ClearButtonHeight = Layout.DataSourceClearButtonHeight,
            ClearButtonFontSize = Layout.DataSourceClearButtonFontSize,
            ClearButtonMargin = Layout.DataSourceClearButtonMargin,
            SearchRowMargin = Layout.DataSourceSearchRowMargin,
            ListBorderThickness = Layout.DataSourceListBorderThickness,
            ListCornerRadius = _settings.EnableRoundedCorners
                ? Layout.DataSourceListCornerRadius
                : Layout.ZeroCornerRadius,
            ListContentMargin = Layout.DataSourceListContentMargin,
            ItemPadding = Layout.DataSourceListItemPadding,
            ItemMargin = Layout.DataSourceListItemMargin,
            ItemCornerRadius = _settings.EnableRoundedCorners
                ? Layout.DataSourceListItemCornerRadius
                : Layout.ZeroCornerRadius,
            ItemFontSize = Layout.DataSourceListItemFontSize,
            PlaceholderText = "Search data sources"
        };
        return list;
    }

    /// <summary>
    /// Builds the single-row settings grid below the data-source card.
    /// </summary>
    private Grid BuildControlGrid()
    {
        Grid grid = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = Layout.ControlGridMargin,
            VerticalAlignment = VerticalAlignment.Top
        };
        for (int i = 0; i < 4; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        StackPanel rpmModeColumn = BuildControlGridColumn(BuildRPMModeRow());
        StackPanel rpmLimitColumn = BuildControlGridColumn(
            NumberGridBlock("Min RPM", _minRPMBox),
            _maxRPMRow);
        StackPanel dutyLimitColumn = BuildControlGridColumn(
            NumberGridBlock("Min duty", _minDutyBox),
            NumberGridBlock("Max duty", _maxDutyBox));
        StackPanel shapeColumn = BuildControlGridColumn(
            NumberGridBlock("Smoothness", _smoothnessBox),
            ToggleGridBlock("Monotonic", _preventDecreasingToggle));

        Grid.SetColumn(rpmModeColumn, 0);
        Grid.SetColumn(rpmLimitColumn, 1);
        Grid.SetColumn(dutyLimitColumn, 2);
        Grid.SetColumn(shapeColumn, 3);
        grid.Children.Add(rpmModeColumn);
        grid.Children.Add(rpmLimitColumn);
        grid.Children.Add(dutyLimitColumn);
        grid.Children.Add(shapeColumn);
        return grid;
    }

    /// <summary>
    /// Builds one explicit settings-card column for the control grid.
    /// </summary>
    private static StackPanel BuildControlGridColumn(params Border[] cards)
    {
        StackPanel column = new()
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Top
        };
        for (int i = 0; i < cards.Length; i++)
            column.Children.Add(cards[i]);

        return column;
    }

    /// <summary>
    /// Builds a settings-grid card for a number box.
    /// </summary>
    private Border NumberGridBlock(string label, SettingsNumberBox box)
    {
        Grid content = ControlGridCardContent(label);
        box.Margin = Layout.ControlGridNumberBoxMargin;
        box.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(box, 1);
        content.Children.Add(box);
        Border card = CompactCard(content);
        ConfigureControlGridCard(card, isWide: false);
        return card;
    }

    /// <summary>
    /// Builds a settings-grid card for a toggle option.
    /// </summary>
    private Border ToggleGridBlock(string label, SettingsToggle toggle)
    {
        Grid content = ControlGridCardContent(label);
        toggle.Margin = Layout.ControlGridControlMargin;
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(toggle, 1);
        content.Children.Add(toggle);
        Border card = CompactCard(content);
        ConfigureControlGridCard(card, isWide: false);
        return card;
    }

    /// <summary>
    /// Creates a two-column card body for the control grid.
    /// </summary>
    private Grid ControlGridCardContent(string label)
    {
        Grid content = new()
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock text = TrayAppDotNETSettingsUI.TitleText(label, _palette);
        text.Margin = Layout.ControlGridTitleMargin;
        text.HorizontalAlignment = HorizontalAlignment.Left;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(text);
        return content;
    }

    /// <summary>
    /// Applies control-grid sizing to a control card.
    /// </summary>
    private void ConfigureControlGridCard(Border card, bool isWide)
    {
        card.Width = isWide ? Layout.ControlGridWideCardWidth : Layout.ControlGridCardWidth;
        card.MinHeight = Layout.ControlGridCardMinHeight;
        card.Margin = Layout.ControlGridCardMargin;
    }

    private Border ControlBlock(string label, Control control)
    {
        StackPanel row = LabeledRow(label);
        row.Children.Add(control);
        return CompactCard(row);
    }

    private Border ToggleRow(string label, SettingsToggle toggle)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        toggle.Margin = Layout.ToggleMargin;
        row.Children.Add(toggle);
        TextBlock text = TrayAppDotNETSettingsUI.TitleText(label, _palette);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return CompactCard(row);
    }

    private StackPanel LabeledRow(string label)
    {
        TextBlock text = TrayAppDotNETSettingsUI.TitleText(label, _palette);
        text.Width = Layout.RowLabelWidth;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { text }
        };
    }

    private Border CompactCard(Control content) =>
        new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CardBackground),
            CornerRadius = _settings.EnableRoundedCorners ? Layout.CardCornerRadius : Layout.ZeroCornerRadius,
            Padding = Layout.CardPadding,
            Margin = Layout.CardMargin,
            Child = content
        };

    private SettingsNumberBox Number(int value, int min, int max, string suffix, double minimumWidth)
    {
        SettingsNumberBox box = TrayAppDotNETSettingsUI.NumberBox(_palette, value, min, max, minimumWidth, suffix);
        box.HandleMouseWheelWhenMouseOver = true;
        return box;
    }

    /// <summary>
    /// Creates the graph node rescale action button.
    /// </summary>
    private SettingsButton RescaleCurveButton()
    {
        SettingsButton button = TrayAppDotNETSettingsUI.Button("Rescale Curve", _palette);
        button.MinHeight = Layout.RescaleCurveButtonHeight;
        button.Height = Layout.RescaleCurveButtonHeight;
        button.Padding = Layout.RescaleCurveButtonPadding;
        button.Label.FontSize = Layout.RescaleCurveButtonFontSize;
        button.IsEnabled = false;
        return button;
    }

    private void WireControls()
    {
        _dataSourceList.SelectionChanged += OnDataSourceSelectionChanged;
        _maxRPMBox.ValueChanged += (_, e) =>
        {
            if (_suppressEvents || !e.NewValue.HasValue) return;
            int old = _curve.MaxRPM;
            _curve.MaxRPM = Math.Max(1, (int)Math.Round(e.NewValue.Value));
            if (_curve.MinRPM > _curve.MaxRPM)
                _curve.MinRPM = _curve.MaxRPM;
            ClampCurveLimits();
            if (_curve.RPMMode) MarkPendingNodeRescale(old, _curve.MaxRPM);
            LoadControlState();
            NotifyCurveShapeChanged();
        };
        _minRPMBox.ValueChanged += (_, e) =>
        {
            if (_suppressEvents || !e.NewValue.HasValue) return;
            int oldMax = _curve.MaxRPM;
            _curve.MinRPM = Math.Max(0, (int)Math.Round(e.NewValue.Value));
            if (_curve.MinRPM > _curve.MaxRPM)
                _curve.MaxRPM = _curve.MinRPM;
            ClampCurveLimits();
            if (_curve.RPMMode && oldMax != _curve.MaxRPM)
                MarkPendingNodeRescale(oldMax, _curve.MaxRPM);
            LoadControlState();
            NotifyCurveShapeChanged();
        };
        _maxDutyBox.ValueChanged += (_, e) =>
        {
            if (_suppressEvents || !e.NewValue.HasValue) return;
            _curve.MaxDutyCycle = Math.Clamp((int)Math.Round(e.NewValue.Value), 1, 100);
            if (_curve.MinDutyCycle > _curve.MaxDutyCycle)
                _curve.MinDutyCycle = _curve.MaxDutyCycle;
            ClampCurveLimits();
            LoadControlState();
            NotifyCurveShapeChanged();
        };
        _minDutyBox.ValueChanged += (_, e) =>
        {
            if (_suppressEvents || !e.NewValue.HasValue) return;
            _curve.MinDutyCycle = Math.Clamp((int)Math.Round(e.NewValue.Value), 0, 100);
            if (_curve.MinDutyCycle > _curve.MaxDutyCycle)
                _curve.MaxDutyCycle = _curve.MinDutyCycle;
            ClampCurveLimits();
            LoadControlState();
            NotifyCurveShapeChanged();
        };
        _smoothnessBox.ValueChanged += (_, e) =>
        {
            if (_suppressEvents || !e.NewValue.HasValue) return;
            _curve.SmoothingFactor =
                Math.Clamp((int)Math.Round(e.NewValue.Value), SmoothnessMin, SmoothnessMax);
            NotifyCurveShapeChanged();
        };
    }

    /// <summary>
    /// Applies data-source list selection to the curve.
    /// </summary>
    private void OnDataSourceSelectionChanged(object? sender, EventArgs e)
    {
        UpdateDataSourceSelectionText();
        if (_suppressEvents) return;

        string key = SelectedDataSourceKey();
        _curve.SelectedDataSourceKey = key;
        EnsureCurveNodesOnDataSourceAxis();
        RefreshEditorBinding();
        Save();
    }

    private void LoadControlState()
    {
        _suppressEvents = true;
        try
        {
            _rpmModeToggle.IsChecked = _curve.RPMMode;
            _maxRPMRow.IsVisible = _curve.RPMMode;
            _maxRPMBox.Maximum = Math.Max(10000, _curve.MaxRPM);
            _maxRPMBox.Value = _curve.MaxRPM;
            _minRPMBox.Maximum = Math.Max(10000, Math.Max(_curve.MinRPM, _curve.MaxRPM));
            _minRPMBox.Value = _curve.MinRPM;
            _maxDutyBox.Value = _curve.MaxDutyCycle;
            _minDutyBox.Maximum = 100;
            _minDutyBox.Value = _curve.MinDutyCycle;
            _smoothnessBox.Value = _curve.SmoothingFactor;
            _preventDecreasingToggle.IsChecked = _curve.PreventDecreasing;
            SelectDataSourceList(_curve.SelectedDataSourceKey);
            UpdateRescaleCurveButtonState();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void PopulateDataSources()
    {
        _dataSourceList.Items.Clear();
        DeviceNicknameResolver deviceNicknameResolver = DeviceNicknameResolver.Create(_settings);
        ProbeNicknameResolver probeNicknameResolver = ProbeNicknameResolver.Create(_settings);
        foreach (DataSource source in DataSource.DataSources.Values
                     .OrderBy(source => DataSourceDeviceName(source, deviceNicknameResolver),
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(source => DataSourceProbeName(source, probeNicknameResolver),
                         StringComparer.OrdinalIgnoreCase))
        {
            string label = DataSourceListLabel(source, deviceNicknameResolver, probeNicknameResolver);
            _dataSourceList.Items.Add(new SettingsSearchableListBoxItem(
                source.DataSourceKey,
                label,
                DataSourceSearchText(source, label)));
        }

        bool hasSources = _dataSourceList.Items.Count > 0;
        _dataSourceList.IsEnabled = hasSources;
        _dataSourceSelectionBox.IsEnabled = hasSources;
        UpdateDataSourceSelectionText();
    }

    private void EnsureCurveDataSource()
    {
        if (!string.IsNullOrWhiteSpace(_curve.SelectedDataSourceKey)
            && DataSource.Find(_curve.SelectedDataSourceKey) != null)
            return;

        DataSource? source = DataSource.DataSources.Values
            .OrderByDescending(static s => s.DataSourceType == DataSourceTypeEnum.Temperature)
            .ThenBy(static s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        _curve.SelectedDataSourceKey = source?.DataSourceKey ?? string.Empty;
    }

    private void EnsureCurveNodesOnDataSourceAxis()
    {
        DataSource? source = CurrentDataSource();
        if (source == null)
        {
            if (_curve.CurveNodes.Count == 0)
            {
                double fallbackYMax = _curve.RPMMode ? _curve.MaxRPM : _curve.MaxDutyCycle;
                _curve.CurveNodes.Add(new CurveNode(0.0, Math.Max(_curve.ActiveYMinLine, fallbackYMax * 0.35)));
                _curve.CurveNodes.Add(new CurveNode(100.0, fallbackYMax * 0.75));
                _curve.BumpVersion();
            }

            return;
        }

        if (_curve is { ClampXMin: 0, ClampXMax: 100 })
        {
            _curve.ClampXMin = (int)Math.Round(source.DisplayMinimum);
            _curve.ClampXMax = (int)Math.Round(source.DisplayMaximum);
        }

        if (_curve.CurveNodes.Count != 0) return;

        double min = source.DisplayMinimum;
        double max = source.DisplayMaximum;
        double yMax = _curve.RPMMode ? _curve.MaxRPM : _curve.MaxDutyCycle;
        _curve.CurveNodes.Add(new CurveNode(min, Math.Max(_curve.ActiveYMinLine, yMax * 0.35)));
        _curve.CurveNodes.Add(new CurveNode(max, yMax * 0.75));
        _curve.BumpVersion();
    }

    /// <summary>
    /// Selects a data source in the searchable list by key.
    /// </summary>
    private void SelectDataSourceList(string? key)
    {
        string normalized = key ?? string.Empty;
        foreach (SettingsSearchableListBoxItem item in _dataSourceList.Items)
        {
            if (!string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase)) continue;
            _dataSourceList.SelectedItem = item;
            return;
        }

        _dataSourceList.SelectedItem = _dataSourceList.Items.Count > 0 ? _dataSourceList.Items[0] : null;
    }

    /// <summary>
    /// Builds searchable text for a data source.
    /// </summary>
    private static string DataSourceSearchText(DataSource source, string label) =>
        string.Join(
            ' ',
            source.ControllerName,
            source.ControllerHardwareType,
            source.DataSourceType.ToString(),
            source.DataSourceKey,
            label,
            source.DisplayUnit);

    /// <summary>
    /// Builds the display label for a data-source list item.
    /// </summary>
    private static string DataSourceListLabel(
        DataSource source,
        DeviceNicknameResolver deviceNicknameResolver,
        ProbeNicknameResolver probeNicknameResolver) =>
        DataSourceSelectionLabel(source, deviceNicknameResolver, probeNicknameResolver)
        + ": "
        + DataSourceValueText(source);

    /// <summary>
    /// Builds the selected data-source label without the live value.
    /// </summary>
    private static string DataSourceSelectionLabel(
        DataSource source,
        DeviceNicknameResolver deviceNicknameResolver,
        ProbeNicknameResolver probeNicknameResolver) =>
        $"{DataSourceDeviceName(source, deviceNicknameResolver)}: "
        + DataSourceProbeName(source, probeNicknameResolver);

    /// <summary>
    /// Resolves the display device name for a data source.
    /// </summary>
    private static string DataSourceDeviceName(DataSource source, DeviceNicknameResolver deviceNicknameResolver)
    {
        string deviceName = deviceNicknameResolver.Resolve(source);
        if (!string.IsNullOrWhiteSpace(deviceName)) return deviceName;
        if (!string.IsNullOrWhiteSpace(source.ControllerName)) return source.ControllerName;
        return "Data source";
    }

    /// <summary>
    /// Resolves the display probe name for a data source.
    /// </summary>
    private static string DataSourceProbeName(DataSource source, ProbeNicknameResolver probeNicknameResolver)
    {
        string probeName = probeNicknameResolver.Resolve(source.DisplayName);
        return string.IsNullOrWhiteSpace(probeName) ? source.DisplayName : probeName;
    }

    /// <summary>
    /// Formats the current value for a data-source list item.
    /// </summary>
    private static string DataSourceValueText(DataSource source)
    {
        if (ProbeValueFormatter.IsProbeDataSource(source))
            return ProbeValueFormatter.FormatValue(source, probe: null);

        string formatted = FormatDataValue(source.DisplayValue);
        string unit = source.DisplayUnit;
        return string.IsNullOrWhiteSpace(unit) ? formatted : $"{formatted} {unit}";
    }

    /// <summary>
    /// Formats generic curve data values without noisy precision.
    /// </summary>
    private static string FormatDataValue(double value)
    {
        double abs = Math.Abs(value);
        if (abs >= 100 || Math.Abs(value - Math.Round(value)) < 0.001)
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Updates the selected data-source display text.
    /// </summary>
    private void UpdateDataSourceSelectionText()
    {
        SettingsSearchableListBoxItem? item = _dataSourceList.SelectedItem;
        _dataSourceSelectionText.Text = item == null ? "No data source" : SelectedDataSourceDisplayText(item);
        _dataSourceSelectionText.Opacity = item == null ? 0.65 : 1.0;
    }

    /// <summary>
    /// Resolves selected data-source text without displaying the live value.
    /// </summary>
    private string SelectedDataSourceDisplayText(SettingsSearchableListBoxItem item)
    {
        DataSource? source = DataSource.Find(item.Tag?.ToString());
        if (source == null) return item.Text;

        DeviceNicknameResolver deviceNicknameResolver = DeviceNicknameResolver.Create(_settings);
        ProbeNicknameResolver probeNicknameResolver = ProbeNicknameResolver.Create(_settings);
        return DataSourceSelectionLabel(source, deviceNicknameResolver, probeNicknameResolver);
    }

    private string SelectedDataSourceKey() =>
        _dataSourceList.SelectedItem?.Tag?.ToString() ?? string.Empty;

    private DataSource? CurrentDataSource() => DataSource.Find(_curve.SelectedDataSourceKey);

    private void RefreshEditorBinding() =>
        _editor.SetCurve(_curve, CurrentDataSource());

    private void OnRPMModeChanged(object? sender, bool enabled)
    {
        if (_suppressEvents) return;

        double oldMax = _curve.ActiveYMaximum;
        if (_curve.RPMMode == enabled) return;
        _curve.RPMMode = enabled;
        NotifyAssignedCardsCurveUnitChanged();
        ClampCurveLimits();
        MarkPendingNodeRescale(oldMax, _curve.ActiveYMaximum);
        LoadControlState();
        NotifyCurveShapeChanged();
    }

    /// <summary>
    /// Marks graph nodes as eligible for proportional Y-axis rescaling.
    /// </summary>
    private void MarkPendingNodeRescale(double oldMax, double newMax)
    {
        double old = _rescaleCurvePending ? _rescaleCurveOldMax : Math.Max(1.0, oldMax);
        double next = Math.Max(1.0, newMax);
        if (Math.Abs(next - old) < 0.001)
        {
            ClearPendingNodeRescale();
            return;
        }

        _rescaleCurveOldMax = old;
        _rescaleCurveNewMax = next;
        _rescaleCurvePending = true;
        UpdateRescaleCurveButtonState();
    }

    /// <summary>
    /// Applies pending proportional Y-axis rescaling to every graph node.
    /// </summary>
    private void ApplyPendingNodeRescale()
    {
        if (!_rescaleCurvePending) return;

        double ratio = _rescaleCurveNewMax / Math.Max(1.0, _rescaleCurveOldMax);
        foreach (CurveNode node in _curve.CurveNodes)
            node.Y = Math.Clamp(node.Y * ratio, 0.0, _rescaleCurveNewMax);

        _curve.BumpVersion();
        ClearPendingNodeRescale();
        NotifyCurveShapeChanged();
    }

    /// <summary>
    /// Clears pending graph node rescale state.
    /// </summary>
    private void ClearPendingNodeRescale()
    {
        _rescaleCurvePending = false;
        _rescaleCurveOldMax = 0.0;
        _rescaleCurveNewMax = 0.0;
        UpdateRescaleCurveButtonState();
    }

    /// <summary>
    /// Updates the always-visible rescale button's enabled visual state.
    /// </summary>
    private void UpdateRescaleCurveButtonState() => _rescaleCurveButton.IsEnabled = _rescaleCurvePending;

    private void OnPreventDecreasingChanged(object? sender, bool enabled)
    {
        if (_suppressEvents) return;

        if (_curve.PreventDecreasing == enabled) return;

        _curve.PreventDecreasing = enabled;
        _hasPreservedNonMonotonicNodes = enabled;
        NotifyCurveShapeChanged();
    }

    /// <summary>
    /// Discards hidden non-monotonic node values only for direct graph edits.
    /// </summary>
    private void OnEditorGraphEditStarting()
    {
        if (!_curve.PreventDecreasing || !_hasPreservedNonMonotonicNodes) return;

        _curve.BurnInEffectiveNodes();
        _hasPreservedNonMonotonicNodes = false;
        _editor.Redraw();
    }

    private void OnEditorCurveChanged()
    {
        _curve.BumpVersion();
        Save();
    }

    private void NotifyCurveShapeChanged()
    {
        _curve.BumpVersion();
        _editor.Redraw();
        Save();
    }

    private void ClampCurveLimits()
    {
        _curve.MaxRPM = Math.Max(1, _curve.MaxRPM);
        _curve.MinRPM = Math.Clamp(_curve.MinRPM, 0, _curve.MaxRPM);
        _curve.MaxDutyCycle = Math.Clamp(_curve.MaxDutyCycle, 1, 100);
        _curve.MinDutyCycle = Math.Clamp(_curve.MinDutyCycle, 0, _curve.MaxDutyCycle);
    }

    private void Save()
    {
        Curve.Register(_curve);
        _settings.SyncFanControlRegistriesForSave();
        _settings.Save();
        _settings.RaiseChanged();
    }

    /// <summary>
    /// Notifies assigned cards that curve-unit conversion inputs changed.
    /// </summary>
    private void NotifyAssignedCardsCurveUnitChanged()
    {
        IEnumerable<Fan>? liveFans = AppServices.LHMService?.Fans;
        IEnumerable<Fan> fans = liveFans ?? _settings.Fans;
        FanCurveModeSync.ApplyToCurveAssignments(_curve, fans, FanGroup.FanGroups.Values);
    }

    private static int DefaultMaxRPM(Fan fan)
    {
        if (fan.MaxRPM > 0) return fan.MaxRPM;
        if (fan.CurrentRPM > 0) return Math.Max(100, fan.CurrentRPM);
        return 3000;
    }
}
