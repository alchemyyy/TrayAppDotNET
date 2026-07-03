using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

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
    private readonly SettingsNumberBox _maxRpmBox;
    private readonly SettingsNumberBox _minRpmBox;
    private readonly SettingsNumberBox _maxDutyBox;
    private readonly SettingsNumberBox _minDutyBox;
    private readonly SettingsNumberBox _smoothnessBox;
    private readonly SettingsToggle _preventDecreasingToggle;
    private readonly Border _maxRpmRow;
    private readonly SettingsButton _syncYesButton;
    private readonly SettingsButton _syncNoButton;
    private FanCurveLayout? _layout;
    private bool _suppressEvents;
    private bool _rpmSyncPending;
    private double _rpmSyncOldMax;
    private double _rpmSyncNewMax;

    public FanCurveEditorWindow()
    {
        _fan = null!;
        _curve = null!;
        _settings = null!;
        _palette = default;
        _editor = null!;
        _dataSourceSelectionBox = null!;
        _dataSourceSelectionText = null!;
        _dataSourceList = null!;
        _rpmModeToggle = null!;
        _maxRpmBox = null!;
        _minRpmBox = null!;
        _maxDutyBox = null!;
        _minDutyBox = null!;
        _smoothnessBox = null!;
        _preventDecreasingToggle = null!;
        _maxRpmRow = null!;
        _syncYesButton = null!;
        _syncNoButton = null!;

        InitializeComponent();
        InitializeComponentState();
    }

    public FanCurveEditorWindow(Fan fan, Curve curve, AppSettings settings)
    {
        _fan = fan;
        _curve = curve;
        _settings = settings;

        InitializeComponent();
        InitializeComponentState();

        _palette = FanSettingsWindow.CreatePalette(
            AppServices.Theme,
            settings,
            AppTheme.ResolveEffectiveIsLightTheme(settings));

        _curve.EnsureEditorDefaults(DefaultMaxRpm(fan));
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
            Palette = FanCurveEditorPalette.FromSettingsPalette(
                _palette,
                AppServices.Theme ?? AppTheme.Default,
                AppTheme.ResolveEffectiveIsLightTheme(settings)),
        };
        _editor.CurveChanged += OnEditorCurveChanged;

        _dataSourceSelectionText = DataSourceSelectionText();
        _dataSourceSelectionBox = DataSourceSelectionBox(_dataSourceSelectionText);
        _dataSourceList = DataSourceList();
        _rpmModeToggle = TrayAppDotNETSettingsUI.Toggle(_palette, _curve.RPMMode, OnRpmModeChanged);
        _maxRpmBox = Number(_curve.MaxRPM, 1, Math.Max(10000, _curve.MaxRPM), "RPM", Layout.RPMNumberBoxWidth);
        _minRpmBox = Number(_curve.MinRPM, 0, Math.Max(10000, _curve.MaxRPM), "RPM", Layout.RPMNumberBoxWidth);
        _maxDutyBox = Number(_curve.MaxDutyCycle, 1, 100, "%", Layout.DutyNumberBoxWidth);
        _minDutyBox = Number(_curve.MinDutyCycle, 0, 100, "%", Layout.DutyNumberBoxWidth);
        _smoothnessBox = Number(
            _curve.SmoothingFactor,
            SmoothnessMin,
            SmoothnessMax,
            string.Empty,
            Layout.SmoothnessNumberBoxWidth);
        _preventDecreasingToggle =
            TrayAppDotNETSettingsUI.Toggle(_palette, _curve.PreventDecreasing, OnPreventDecreasingChanged);
        _syncYesButton = SmallButton("Yes");
        _syncNoButton = SmallButton("No");
        _syncYesButton.Click += (_, _) => ApplyRpmNodeSync();
        _syncNoButton.Click += (_, _) => ClearRpmSyncPending();

        PopulateDataSources();
        WireControls();
        _maxRpmRow = BuildMaxRpmRow();

        Content = BuildContent();
        LoadControlState();
        RefreshEditorBinding();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_editor != null)
            _editor.CurveChanged -= OnEditorCurveChanged;

        base.OnClosed(e);
    }

    private void InitializeComponentState() => _layout = FanCurveLayout.From(this);

    private FanCurveLayout Layout =>
        _layout ?? throw new InvalidOperationException("Fan curve editor layout resources have not been loaded.");

    private Border BuildContent()
    {
        Grid shell = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
            Margin = Layout.ZeroThickness,
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
            VerticalAlignment = VerticalAlignment.Top,
        };
        graphHost.Children.Add(_editor);
        Grid.SetColumn(graphHost, 1);
        Grid.SetRow(graphHost, 0);
        main.Children.Add(graphHost);

        WrapPanel controlGrid = BuildControlGrid();
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
            Child = shell,
        };
    }

    private Border BuildMaxRpmRow()
    {
        StackPanel syncButtons = new() { Orientation = Orientation.Horizontal, Margin = Layout.SyncButtonGroupMargin };
        syncButtons.Children.Add(_syncYesButton);
        _syncNoButton.Margin = Layout.SyncNoButtonMargin;
        syncButtons.Children.Add(_syncNoButton);

        StackPanel controls = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _maxRpmBox.Margin = Layout.ControlGridControlMargin;
        controls.Children.Add(_maxRpmBox);
        controls.Children.Add(syncButtons);

        Grid row = ControlGridCardContent("Max RPM");
        Grid.SetColumn(controls, 1);
        row.Children.Add(controls);
        Border card = CompactCard(row);
        ConfigureControlGridCard(card, isWide: true);
        return card;
    }

    /// <summary>
    /// Builds the data-source selected value, search box, and list block.
    /// </summary>
    private Border DataSourceBlock()
    {
        Grid content = new()
        {
            VerticalAlignment = VerticalAlignment.Stretch,
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
            Child = text,
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
            PlaceholderText = "Search data sources",
        };
        return list;
    }

    /// <summary>
    /// Builds the wrapping grid of curve options below the data-source card.
    /// </summary>
    private WrapPanel BuildControlGrid()
    {
        WrapPanel grid = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = Layout.ControlGridMargin,
            VerticalAlignment = VerticalAlignment.Top,
        };
        grid.Children.Add(ToggleGridBlock("RPM mode", _rpmModeToggle));
        grid.Children.Add(_maxRpmRow);
        grid.Children.Add(ControlGridBlock("Min RPM", _minRpmBox));
        grid.Children.Add(ControlGridBlock("Max duty", _maxDutyBox));
        grid.Children.Add(ControlGridBlock("Min duty", _minDutyBox));
        grid.Children.Add(ControlGridBlock("Smoothness", _smoothnessBox));
        grid.Children.Add(ToggleGridBlock("Monotonic", _preventDecreasingToggle));
        return grid;
    }

    /// <summary>
    /// Builds a wrapping-grid card for a labeled control.
    /// </summary>
    private Border ControlGridBlock(string label, Control control)
    {
        Grid content = ControlGridCardContent(label);
        control.Margin = Layout.ControlGridControlMargin;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(control, 1);
        content.Children.Add(control);
        Border card = CompactCard(content);
        ConfigureControlGridCard(card, isWide: false);
        return card;
    }

    /// <summary>
    /// Builds a wrapping-grid card for a toggle option.
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
    /// Creates a two-column card body for the wrapping control grid.
    /// </summary>
    private Grid ControlGridCardContent(string label)
    {
        Grid content = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
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
    /// Applies wrapping-grid sizing to a control card.
    /// </summary>
    private void ConfigureControlGridCard(Border card, bool isWide)
    {
        card.Width = isWide ? Layout.ControlGridWideCardWidth : Layout.ControlGridCardWidth;
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
            Children = { text },
        };
    }

    private Border CompactCard(Control content) =>
        new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CardBackground),
            CornerRadius = _settings.EnableRoundedCorners ? Layout.CardCornerRadius : Layout.ZeroCornerRadius,
            Padding = Layout.CardPadding,
            Margin = Layout.CardMargin,
            Child = content,
        };

    private SettingsNumberBox Number(int value, int min, int max, string suffix, double width)
    {
        SettingsNumberBox box = TrayAppDotNETSettingsUI.NumberBox(_palette, value, min, max, width, suffix);
        box.HandleMouseWheelWhenMouseOver = true;
        return box;
    }

    private SettingsButton SmallButton(string text)
    {
        SettingsButton button = TrayAppDotNETSettingsUI.Button(text, _palette);
        button.Width = Layout.SmallButtonWidth;
        button.MinHeight = Layout.SmallButtonHeight;
        button.Height = Layout.SmallButtonHeight;
        button.Padding = Layout.SmallButtonPadding;
        button.Label.FontSize = Layout.SmallButtonFontSize;
        button.IsEnabled = false;
        return button;
    }

    private sealed record FanCurveLayout(
        double GraphWidth,
        double GraphHeight,
        double LeftColumnWidth,
        double DataSourceSelectedHeight,
        double DataSourceSelectedFontSize,
        double DataSourceListHeight,
        double DataSourceSearchBoxHeight,
        double DataSourceClearButtonWidth,
        double DataSourceClearButtonHeight,
        double DataSourceClearButtonFontSize,
        double DataSourceListItemFontSize,
        double ControlGridCardWidth,
        double ControlGridWideCardWidth,
        double RPMNumberBoxWidth,
        double DutyNumberBoxWidth,
        double SmoothnessNumberBoxWidth,
        double TitleFontSize,
        double RowLabelWidth,
        double SmallButtonWidth,
        double SmallButtonHeight,
        double SmallButtonFontSize,
        Thickness RootBorderThickness,
        CornerRadius RootCornerRadius,
        CornerRadius CardCornerRadius,
        CornerRadius DataSourceSelectedCornerRadius,
        CornerRadius DataSourceListCornerRadius,
        CornerRadius DataSourceListItemCornerRadius,
        CornerRadius ZeroCornerRadius,
        Thickness ZeroThickness,
        Thickness TitleMargin,
        Thickness MainMargin,
        Thickness DataSourceTitleMargin,
        Thickness DataSourceSelectedMargin,
        Thickness DataSourceSelectedPadding,
        Thickness DataSourceListMargin,
        Thickness DataSourceSearchBoxPadding,
        Thickness DataSourceClearButtonMargin,
        Thickness DataSourceSearchRowMargin,
        Thickness DataSourceListBorderThickness,
        Thickness DataSourceListContentMargin,
        Thickness DataSourceListItemPadding,
        Thickness DataSourceListItemMargin,
        Thickness ControlGridMargin,
        Thickness ControlGridCardMargin,
        Thickness ControlGridTitleMargin,
        Thickness ControlGridControlMargin,
        Thickness SyncButtonGroupMargin,
        Thickness SyncNoButtonMargin,
        Thickness ToggleMargin,
        Thickness CardPadding,
        Thickness CardMargin,
        Thickness SmallButtonPadding)
    {
        public static FanCurveLayout From(Control owner)
        {
            HotReloadResourceReader r = new(owner, "FanCurveEditor");
            return new FanCurveLayout(
                r.Double("GraphWidth"),
                r.Double("GraphHeight"),
                r.Double("LeftColumnWidth"),
                r.Double("DataSourceSelectedHeight"),
                r.Double("DataSourceSelectedFontSize"),
                r.Double("DataSourceListHeight"),
                r.Double("DataSourceSearchBoxHeight"),
                r.Double("DataSourceClearButtonWidth"),
                r.Double("DataSourceClearButtonHeight"),
                r.Double("DataSourceClearButtonFontSize"),
                r.Double("DataSourceListItemFontSize"),
                r.Double("ControlGridCardWidth"),
                r.Double("ControlGridWideCardWidth"),
                r.Double("RPMNumberBoxWidth"),
                r.Double("DutyNumberBoxWidth"),
                r.Double("SmoothnessNumberBoxWidth"),
                r.Double("TitleFontSize"),
                r.Double("RowLabelWidth"),
                r.Double("SmallButtonWidth"),
                r.Double("SmallButtonHeight"),
                r.Double("SmallButtonFontSize"),
                r.Thickness("RootBorderThickness"),
                r.CornerRadius("RootCornerRadius"),
                r.CornerRadius("CardCornerRadius"),
                r.CornerRadius("DataSourceSelectedCornerRadius"),
                r.CornerRadius("DataSourceListCornerRadius"),
                r.CornerRadius("DataSourceListItemCornerRadius"),
                r.CornerRadius("ZeroCornerRadius"),
                r.Thickness("ZeroThickness"),
                r.Thickness("TitleMargin"),
                r.Thickness("MainMargin"),
                r.Thickness("DataSourceTitleMargin"),
                r.Thickness("DataSourceSelectedMargin"),
                r.Thickness("DataSourceSelectedPadding"),
                r.Thickness("DataSourceListMargin"),
                r.Thickness("DataSourceSearchBoxPadding"),
                r.Thickness("DataSourceClearButtonMargin"),
                r.Thickness("DataSourceSearchRowMargin"),
                r.Thickness("DataSourceListBorderThickness"),
                r.Thickness("DataSourceListContentMargin"),
                r.Thickness("DataSourceListItemPadding"),
                r.Thickness("DataSourceListItemMargin"),
                r.Thickness("ControlGridMargin"),
                r.Thickness("ControlGridCardMargin"),
                r.Thickness("ControlGridTitleMargin"),
                r.Thickness("ControlGridControlMargin"),
                r.Thickness("SyncButtonGroupMargin"),
                r.Thickness("SyncNoButtonMargin"),
                r.Thickness("ToggleMargin"),
                r.Thickness("CardPadding"),
                r.Thickness("CardMargin"),
                r.Thickness("SmallButtonPadding"));
        }
    }

    private void WireControls()
    {
        _dataSourceList.SelectionChanged += OnDataSourceSelectionChanged;
        _maxRpmBox.ValueChanged += (_, e) =>
        {
            if (_suppressEvents || !e.NewValue.HasValue) return;
            int old = _curve.MaxRPM;
            _curve.MaxRPM = Math.Max(1, (int)Math.Round(e.NewValue.Value));
            ClampCurveLimits();
            if (_curve.RPMMode) MarkRpmSyncPending(old, _curve.MaxRPM);
            LoadControlState();
            NotifyCurveShapeChanged();
        };
        _minRpmBox.ValueChanged += (_, e) =>
        {
            if (_suppressEvents || !e.NewValue.HasValue) return;
            _curve.MinRPM = Math.Clamp((int)Math.Round(e.NewValue.Value), 0, _curve.MaxRPM);
            NotifyCurveShapeChanged();
        };
        _maxDutyBox.ValueChanged += (_, e) =>
        {
            if (_suppressEvents || !e.NewValue.HasValue) return;
            _curve.MaxDutyCycle = Math.Clamp((int)Math.Round(e.NewValue.Value), 1, 100);
            ClampCurveLimits();
            LoadControlState();
            NotifyCurveShapeChanged();
        };
        _minDutyBox.ValueChanged += (_, e) =>
        {
            if (_suppressEvents || !e.NewValue.HasValue) return;
            _curve.MinDutyCycle = Math.Clamp((int)Math.Round(e.NewValue.Value), 0, _curve.MaxDutyCycle);
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
            _maxRpmRow.IsVisible = _curve.RPMMode;
            _maxRpmBox.Maximum = Math.Max(10000, _curve.MaxRPM);
            _maxRpmBox.Value = _curve.MaxRPM;
            _minRpmBox.Maximum = _curve.MaxRPM;
            _minRpmBox.Value = _curve.MinRPM;
            _maxDutyBox.Value = _curve.MaxDutyCycle;
            _minDutyBox.Maximum = _curve.MaxDutyCycle;
            _minDutyBox.Value = _curve.MinDutyCycle;
            _smoothnessBox.Value = _curve.SmoothingFactor;
            _preventDecreasingToggle.IsChecked = _curve.PreventDecreasing;
            SelectDataSourceList(_curve.SelectedDataSourceKey);
            SetRpmSyncButtonsEnabled(_rpmSyncPending);
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

        if (_curve.ClampXMin == 0 && _curve.ClampXMax == 100)
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
        $"{DataSourceDeviceName(source, deviceNicknameResolver)}: "
        + $"{DataSourceProbeName(source, probeNicknameResolver)}: "
        + DataSourceValueText(source);

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
        _dataSourceSelectionText.Text = item?.Text ?? "No data source";
        _dataSourceSelectionText.Opacity = item == null ? 0.65 : 1.0;
    }

    private string SelectedDataSourceKey() =>
        _dataSourceList.SelectedItem?.Tag?.ToString() ?? string.Empty;

    private DataSource? CurrentDataSource() => DataSource.Find(_curve.SelectedDataSourceKey);

    private void RefreshEditorBinding() =>
        _editor.SetCurve(_curve, CurrentDataSource());

    private void OnRpmModeChanged(object? sender, bool enabled)
    {
        if (_suppressEvents) return;

        double oldMax = _curve.ActiveYMaximum;
        if (_curve.RPMMode == enabled) return;
        _curve.RPMMode = enabled;
        if (enabled) MarkRpmSyncPending(oldMax, _curve.MaxRPM);
        ClampCurveLimits();
        LoadControlState();
        NotifyCurveShapeChanged();
    }

    private void MarkRpmSyncPending(double oldMax, double newMax)
    {
        if (!_rpmSyncPending)
            _rpmSyncOldMax = Math.Max(1.0, oldMax);

        _rpmSyncNewMax = Math.Max(1.0, newMax);
        _rpmSyncPending = true;
        SetRpmSyncButtonsEnabled(true);
    }

    private void ApplyRpmNodeSync()
    {
        if (!_rpmSyncPending) return;

        double ratio = _rpmSyncNewMax / Math.Max(1.0, _rpmSyncOldMax);
        foreach (CurveNode node in _curve.CurveNodes)
            node.Y = Math.Clamp(node.Y * ratio, 0.0, _rpmSyncNewMax);

        _curve.BumpVersion();
        ClearRpmSyncPending();
        NotifyCurveShapeChanged();
    }

    private void ClearRpmSyncPending()
    {
        _rpmSyncPending = false;
        _rpmSyncOldMax = 0.0;
        _rpmSyncNewMax = 0.0;
        SetRpmSyncButtonsEnabled(false);
    }

    private void SetRpmSyncButtonsEnabled(bool enabled)
    {
        _syncYesButton.IsEnabled = enabled;
        _syncNoButton.IsEnabled = enabled;
    }

    private void OnPreventDecreasingChanged(object? sender, bool enabled)
    {
        if (_suppressEvents) return;

        if (_curve.PreventDecreasing && !enabled)
            _curve.BurnInEffectiveNodes();
        _curve.PreventDecreasing = enabled;
        NotifyCurveShapeChanged();
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

    private static int DefaultMaxRpm(Fan fan)
    {
        if (fan.MaxRPM > 0) return fan.MaxRPM;
        if (fan.CurrentRPM > 0) return Math.Max(100, fan.CurrentRPM);
        return 3000;
    }
}
