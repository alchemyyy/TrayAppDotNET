using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FanControlTrayAppDotNET.UI.Curves;

namespace FanControlTrayAppDotNET.UI;

public sealed partial class FanPropertiesWindow : Window
{
    private readonly Fan _fan;
    private readonly AppSettings _settings;
    private PropertiesLayout? _layout;
    private readonly TextBlock _titleText;
    private readonly TextBlock _fanIDText;
    private readonly TextBlock _sensorControllerText;
    private readonly TextBox _nameBox;
    private readonly SettingsComboBox _groupCombo;
    private readonly SettingsComboBox _curveCombo;
    private readonly RadioButton _curveModeRadio;
    private readonly RadioButton _manualModeRadio;
    private readonly RadioButton _detachedModeRadio;
    private readonly SettingsNumberBox _jumpstartBox;
    private readonly SettingsNumberBox _clampHighBox;
    private readonly SettingsNumberBox _clampLowBox;
    private readonly SettingsNumberBox _warnLowBox;
    private readonly SettingsNumberBox _warnHighBox;
    private readonly SettingsNumberBox _deltaMaxBox;
    private readonly SettingsNumberBox _offsetBox;
    private readonly SettingsButton _editCurveButton;
    private readonly SettingsButton _pinButton;
    private bool _forceClose;

    public FanPropertiesWindow()
    {
        _fan = null!;
        _settings = null!;
        _titleText = null!;
        _fanIDText = null!;
        _sensorControllerText = null!;
        _nameBox = null!;
        _groupCombo = null!;
        _curveCombo = null!;
        _curveModeRadio = null!;
        _manualModeRadio = null!;
        _detachedModeRadio = null!;
        _jumpstartBox = null!;
        _clampHighBox = null!;
        _clampLowBox = null!;
        _warnLowBox = null!;
        _warnHighBox = null!;
        _deltaMaxBox = null!;
        _offsetBox = null!;
        _editCurveButton = null!;
        _pinButton = null!;

        InitializeComponent();
        InitializeComponentState();
    }

    public FanPropertiesWindow(Fan fan, AppSettings settings)
    {
        _fan = fan;
        _settings = settings;

        InitializeComponent();
        InitializeComponentState();

        SettingsPalette p = FanSettingsWindow.CreatePalette(
            AppServices.Theme,
            _settings,
            AppTheme.ResolveEffectiveIsLightTheme(_settings));
        bool rounded = _settings.EnableRoundedCorners;

        _titleText = TrayAppDotNETSettingsUI.Text("Fan Properties", p, 13, FontWeight.SemiBold);
        _fanIDText = ValueText(p);
        _sensorControllerText = ValueText(p);
        _nameBox = TrayAppDotNETSettingsUI.TextBox(p, Layout.TextBoxWidth);
        _groupCombo = TrayAppDotNETSettingsUI.ComboBox(p, Layout.TextBoxWidth, autoSizeToText: true);
        _curveCombo = TrayAppDotNETSettingsUI.ComboBox(p, Layout.CurveComboWidth, autoSizeToText: true);
        _curveModeRadio = CompactRadio("Curve", p);
        _manualModeRadio = CompactRadio("Manual", p);
        _detachedModeRadio = CompactRadio("Detached", p);
        _jumpstartBox = Number(p, 0, 100, "%");
        _clampHighBox = Number(p, 0, 100, "%");
        _clampLowBox = Number(p, 0, 100, "%");
        _warnLowBox = Number(p, 0, 100, "%");
        _warnHighBox = Number(p, 0, 100, "%");
        _deltaMaxBox = Number(p, 0, 100, "%/s");
        _offsetBox = Number(p, -100, 100, "%");
        _editCurveButton = TrayAppDotNETSettingsUI.Button("Edit curve", p);

        _pinButton = CaptionButton(GlyphCatalog.PIN, p);
        SettingsButton closeButton = CaptionButton(GlyphCatalog.EXIT, p);
        _pinButton.Click += (_, _) => IsPinned = !IsPinned;
        closeButton.Click += (_, _) => RequestClose();

        Grid titleBar = BuildTitleBar(p, _pinButton, closeButton);
        Grid body = BuildBody(p);
        Grid footer = BuildFooter(p);

        Grid chrome = new();
        chrome.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.TitleBarHeight)));
        chrome.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        chrome.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.FooterHeight)));
        chrome.Children.Add(titleBar);
        Grid.SetRow(body, 1);
        chrome.Children.Add(body);
        Grid.SetRow(footer, 2);
        chrome.Children.Add(footer);

        Content = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(p.Border),
            BorderThickness = Layout.RootBorderThickness,
            CornerRadius = rounded ? Layout.RootCornerRadius : Layout.ZeroCornerRadius,
            Child = chrome,
        };

        LoadFromFan();
        _fan.PropertyChanged += OnFanPropertyChanged;
        _settings.Changed += OnSettingsChanged;
        Closed += OnClosed;
    }

    private void InitializeComponentState() => _layout = PropertiesLayout.From(this);

    private PropertiesLayout Layout =>
        _layout ?? throw new InvalidOperationException("Fan properties layout resources have not been loaded.");

    public bool IsPinned
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            _pinButton.Text = value ? GlyphCatalog.PINNED : GlyphCatalog.PIN;
        }
    }

    public bool HasFocus() => IsActive;

    public bool RequestClose()
    {
        if (IsPinned)
        {
            if (IsVisible) Activate();
            return false;
        }

        Close();
        return true;
    }

    public void ForceClose()
    {
        _forceClose = true;
        try { Close(); }
        catch { }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_forceClose && IsPinned)
        {
            e.Cancel = true;
            if (!IsVisible) Show();
            Activate();
            return;
        }

        base.OnClosing(e);
    }

    private Grid BuildTitleBar(SettingsPalette p, SettingsButton pinButton, SettingsButton closeButton)
    {
        Grid titleBar = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(
                (AppServices.Theme ?? AppTheme.Default).ResolveFlyoutTitleBarBackground(_settings,
                    AppTheme.ResolveEffectiveIsLightTheme(_settings))),
        };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.Source is SettingsButton) return;
            if (e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _titleText.Margin = Layout.TitleMargin;
        _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        titleBar.Children.Add(_titleText);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { pinButton, closeButton },
        };
        Grid.SetColumn(buttons, 1);
        titleBar.Children.Add(buttons);
        return titleBar;
    }

    private Grid BuildBody(SettingsPalette p)
    {
        Grid body = new() { Margin = Layout.BodyMargin };
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.BodyLeftColumnWidth)));
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        StackPanel left = new() { Margin = Layout.LeftMargin };
        left.Children.Add(Row("ID", _fanIDText, p));
        left.Children.Add(Row("Sensor", _sensorControllerText, p, bottomMargin: 6));
        left.Children.Add(Row("Name", _nameBox, p));
        left.Children.Add(Row("Group", _groupCombo, p));
        left.Children.Add(Row("Mode",
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { _curveModeRadio, _manualModeRadio, _detachedModeRadio },
            }, p));
        left.Children.Add(Row("Jumpstart", _jumpstartBox, p));
        left.Children.Add(Row("Clamp High", _clampHighBox, p));
        left.Children.Add(Row("Clamp Low", _clampLowBox, p));
        left.Children.Add(Row("Warn Low", _warnLowBox, p));
        left.Children.Add(Row("Warn High", _warnHighBox, p));
        left.Children.Add(Row("Max Delta", _deltaMaxBox, p));
        left.Children.Add(Row("Offset", _offsetBox, p, bottomMargin: Layout.OffsetRowBottomMargin));

        ScrollViewer scroll = new()
        {
            Content = left,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        body.Children.Add(scroll);

        Grid right = new() { Margin = Layout.RightMargin };
        right.RowDefinitions.Add(new RowDefinition(new GridLength(Layout.RightPreviewHeight)));
        right.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        right.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        right.Children.Add(new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.ControlBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(p.Border),
            BorderThickness = Layout.RootBorderThickness,
            CornerRadius = _settings.EnableRoundedCorners ? Layout.InnerCornerRadius : Layout.ZeroCornerRadius,
        });
        _curveCombo.Margin = Layout.CurveComboMargin;
        Grid.SetRow(_curveCombo, 1);
        right.Children.Add(_curveCombo);
        _editCurveButton.Margin = new Thickness(0, 8, 0, 0);
        _editCurveButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _editCurveButton.Click += (_, _) => OpenCurveEditor();
        Grid.SetRow(_editCurveButton, 2);
        right.Children.Add(_editCurveButton);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        return body;
    }

    private Grid BuildFooter(SettingsPalette p)
    {
        Grid footer = new() { Margin = Layout.FooterMargin };
        SettingsButton reset = TrayAppDotNETSettingsUI.Button("Reset to defaults", p);
        SettingsButton save = TrayAppDotNETSettingsUI.Button("Save", p);
        reset.Margin = Layout.ResetMargin;
        reset.Click += (_, _) => ResetToDefaults();
        save.Click += (_, _) => SaveFromControls();

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { reset, save },
        };
        footer.Children.Add(buttons);
        return footer;
    }

    private void LoadFromFan()
    {
        UpdateTitle();
        _fanIDText.Text = string.IsNullOrWhiteSpace(_fan.DataSourceKey) ? _fan.FansName : _fan.DataSourceKey;
        _sensorControllerText.Text = _fan.ControllerDisplayLabel;
        _nameBox.Text = _fan.UserDefinedName;
        PopulateGroupCombo();
        PopulateCurveCombo();
        SelectComboByTag(_groupCombo, _fan.Group ?? string.Empty);
        SelectComboByTag(_curveCombo, GetEffectiveCurveName(_fan));
        _detachedModeRadio.IsChecked = _fan.ForcedNonFunctioning;
        _curveModeRadio.IsChecked = _fan is { ForcedNonFunctioning: false, CurrentControlMode: FanControlMode.Curve };
        _manualModeRadio.IsChecked = _fan is { ForcedNonFunctioning: false, CurrentControlMode: FanControlMode.Manual };
        _jumpstartBox.Value = _fan.StartupSpeed;
        _clampHighBox.Value = _fan.ClampHigh;
        _clampLowBox.Value = _fan.ClampLow;
        _warnLowBox.Value = _fan.WarnLow;
        _warnHighBox.Value = _fan.WarnHigh;
        _deltaMaxBox.Value = _fan.DeltaMax;
        _offsetBox.Value = _fan.Offset;
    }

    private void PopulateGroupCombo()
    {
        _groupCombo.Items.Clear();
        _groupCombo.Items.Add(new SettingsComboBoxItem(string.Empty, "None", Palette()));

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (FanGroup group in _settings.FanGroups
                     .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                     .OrderBy(g => g.DisplayOrder)
                     .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            string groupName = group.Name!;
            if (!names.Add(groupName)) continue;
            _groupCombo.Items.Add(new SettingsComboBoxItem(groupName, groupName, Palette()));
        }

        foreach (FanGroup group in FanGroup.FanGroups.Values
                     .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                     .OrderBy(g => g.DisplayOrder)
                     .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            string groupName = group.Name!;
            if (!names.Add(groupName)) continue;
            _groupCombo.Items.Add(new SettingsComboBoxItem(groupName, groupName, Palette()));
        }
    }

    private void PopulateCurveCombo()
    {
        _curveCombo.Items.Clear();
        _curveCombo.Items.Add(new SettingsComboBoxItem(string.Empty, "None", Palette()));
        foreach (Curve curve in Curve.Curves.Values
                     .Where(c => !string.IsNullOrWhiteSpace(c.CurveName))
                     .OrderBy(c => c.CurveName, StringComparer.OrdinalIgnoreCase))
            _curveCombo.Items.Add(new SettingsComboBoxItem(curve.CurveName, curve.CurveName, Palette()));
    }

    private void SaveFromControls()
    {
        ApplyControlsToFan();
        PersistAndNotify();
        LoadFromFan();
    }

    private void ResetToDefaults()
    {
        _nameBox.Text = string.Empty;
        SelectComboByTag(_groupCombo, string.Empty);
        string defaultCurve = NormalizeCurveName(_settings.DefaultAssignedCurve);
        SelectComboByTag(_curveCombo, defaultCurve);
        _curveModeRadio.IsChecked = !string.IsNullOrEmpty(defaultCurve);
        _manualModeRadio.IsChecked = string.IsNullOrEmpty(defaultCurve);
        _detachedModeRadio.IsChecked = false;
        _jumpstartBox.Value = _settings.DefaultJumpstartDutyCycle;
        _clampHighBox.Value = 100;
        _clampLowBox.Value = 0;
        _warnLowBox.Value = 0;
        _warnHighBox.Value = 100;
        _deltaMaxBox.Value = _settings.DefaultDeltaMaxDutyCycle;
        _offsetBox.Value = 0;
        SaveFromControls();
    }

    private void ApplyControlsToFan()
    {
        string groupName = SelectedTag(_groupCombo);
        string curveName = SelectedTag(_curveCombo);
        int clampLow = Math.Min(ReadInt(_clampLowBox), ReadInt(_clampHighBox));
        int clampHigh = Math.Max(ReadInt(_clampLowBox), ReadInt(_clampHighBox));
        int warnLow = Math.Min(ReadInt(_warnLowBox), ReadInt(_warnHighBox));
        int warnHigh = Math.Max(ReadInt(_warnLowBox), ReadInt(_warnHighBox));

        _fan.UserDefinedName = (_nameBox.Text ?? string.Empty).Trim();
        _fan.Group = string.IsNullOrWhiteSpace(groupName) ? null : groupName;
        _fan.StartupSpeed = ReadInt(_jumpstartBox);
        _fan.ClampLow = clampLow;
        _fan.ClampHigh = clampHigh;
        _fan.WarnLow = warnLow;
        _fan.WarnHigh = warnHigh;
        _fan.DeltaMax = ReadInt(_deltaMaxBox);
        _fan.Offset = ReadInt(_offsetBox);

        if (_detachedModeRadio.IsChecked == true)
            _fan.ForcedNonFunctioning = true;
        else
        {
            _fan.ForcedNonFunctioning = false;
            _fan.CurrentControlMode = _curveModeRadio.IsChecked == true
                ? FanControlMode.Curve
                : FanControlMode.Manual;
        }

        ApplyCurveSelection(curveName);
    }

    private void ApplyCurveSelection(string curveName)
    {
        if (!string.IsNullOrWhiteSpace(_fan.Group) && FanGroup.Find(_fan.Group) is { } group)
        {
            group.AssignedCurveName = curveName;
            FanGroup.Register(group);
            return;
        }

        _fan.AssignedCurveName = curveName;
    }

    private void OpenCurveEditor()
    {
        string curveName = SelectedTag(_curveCombo);
        Curve? curve = string.IsNullOrWhiteSpace(curveName) ? null : Curve.Find(curveName);
        if (curve == null)
        {
            curve = CreateCurveForFan();
            curveName = curve.CurveName;
            PopulateCurveCombo();
            SelectComboByTag(_curveCombo, curveName);
        }

        ApplyControlsToFan();
        ApplyCurveSelection(curveName);
        _fan.CurrentControlMode = FanControlMode.Curve;
        _curveModeRadio.IsChecked = true;
        PersistAndNotify();

        FanCurveEditorWindow window = new(_fan, curve, _settings)
        {
            Topmost = Topmost,
            ShowInTaskbar = false,
        };
        window.Closed += (_, _) =>
        {
            PopulateCurveCombo();
            SelectComboByTag(_curveCombo, GetEffectiveCurveName(_fan));
        };
        window.Show(this);
    }

    private Curve CreateCurveForFan()
    {
        string name = UniqueCurveName($"{_fan.DisplayName} Curve");
        int maxRpm = _fan.MaxRPM > 0 ? _fan.MaxRPM : _fan.CurrentRPM > 0 ? Math.Max(100, _fan.CurrentRPM) : 3000;
        Curve curve = new()
        {
            CurveName = name,
            RPMMode = _fan.RPMMode,
            MaxRPM = maxRpm,
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

    private void PersistAndNotify()
    {
        AppServices.LHMService?.PersistLiveState(save: false);
        _settings.SyncFanControlRegistriesForSave();
        _settings.Save();
        _settings.RaiseChanged();
    }

    private void OnFanPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == nameof(Fan.DisplayName)
            || e.PropertyName == nameof(Fan.UserDefinedName))
            Dispatcher.UIThread.Post(UpdateTitle);
    }

    private void OnSettingsChanged()
    {
        if (Content is Border root)
            root.CornerRadius = _settings.EnableRoundedCorners ? Layout.RootCornerRadius : Layout.ZeroCornerRadius;
    }

    private void UpdateTitle()
    {
        string title = $"Fan Properties: {_fan.DisplayName}";
        Title = title;
        _titleText.Text = title;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _fan.PropertyChanged -= OnFanPropertyChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    private SettingsPalette Palette() =>
        FanSettingsWindow.CreatePalette(AppServices.Theme, _settings, AppTheme.ResolveEffectiveIsLightTheme(_settings));

    private SettingsNumberBox Number(SettingsPalette p, int min, int max, string suffix) =>
        TrayAppDotNETSettingsUI.NumberBox(p, 0, min, max, Layout.NumberBoxWidth, suffix);

    private TextBlock ValueText(SettingsPalette p) =>
        new()
        {
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = Layout.ValueFontSize,
            Foreground = TrayAppDotNETSettingsUI.Brush(p.Foreground),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

    private RadioButton CompactRadio(string text, SettingsPalette p) =>
        new()
        {
            Content = text,
            GroupName = "FanMode",
            Foreground = TrayAppDotNETSettingsUI.Brush(p.Foreground),
            FontSize = Layout.RadioFontSize,
            Margin = Layout.RadioMargin,
            VerticalAlignment = VerticalAlignment.Center,
        };

    private Grid Row(string label, Control value, SettingsPalette p, double? bottomMargin = null)
    {
        TextBlock labelBlock = new()
        {
            Text = label,
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = Layout.RowLabelFontSize,
            Foreground = TrayAppDotNETSettingsUI.Brush(p.SecondaryForeground),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid grid = new() { Margin = Layout.RowMargin(bottomMargin ?? Layout.RowBottomMargin) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Layout.RowLabelColumnWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Children.Add(labelBlock);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return grid;
    }

    private SettingsButton CaptionButton(string glyph, SettingsPalette p)
    {
        SettingsButton button = new(glyph, p, transparentBase: true)
        {
            Width = Layout.CaptionButtonWidth,
            Height = Layout.CaptionButtonHeight,
            CornerRadius = Layout.ZeroCornerRadius,
            Padding = Layout.CaptionButtonPadding,
            Label = { FontFamily = TrayAppDotNETSettingsUI.IconFont, FontSize = Layout.CaptionButtonFontSize }
        };
        return button;
    }

    private sealed record PropertiesLayout(
        double TitleBarHeight,
        double FooterHeight,
        double BodyLeftColumnWidth,
        double RightPreviewHeight,
        double TextBoxWidth,
        double CurveComboWidth,
        double NumberBoxWidth,
        double RowLabelColumnWidth,
        Thickness RootBorderThickness,
        CornerRadius RootCornerRadius,
        CornerRadius InnerCornerRadius,
        CornerRadius ZeroCornerRadius,
        Thickness TitleMargin,
        Thickness BodyMargin,
        Thickness LeftMargin,
        Thickness RightMargin,
        Thickness CurveComboMargin,
        Thickness FooterMargin,
        Thickness ResetMargin,
        Thickness RadioMargin,
        Thickness ZeroThickness,
        double ValueFontSize,
        double RadioFontSize,
        double RowLabelFontSize,
        double RowBottomMargin,
        double OffsetRowBottomMargin,
        double CaptionButtonWidth,
        double CaptionButtonHeight,
        double CaptionButtonFontSize,
        Thickness CaptionButtonPadding)
    {
        public Thickness RowMargin(double bottom) =>
            new(ZeroThickness.Left, ZeroThickness.Top, ZeroThickness.Right, bottom);

        public static PropertiesLayout From(Control owner)
        {
            HotReloadResourceReader r = new(owner, "FanProperties");
            return new PropertiesLayout(
                r.Double("TitleBarHeight"),
                r.Double("FooterHeight"),
                r.Double("BodyLeftColumnWidth"),
                r.Double("RightPreviewHeight"),
                r.Double("TextBoxWidth"),
                r.Double("CurveComboWidth"),
                r.Double("NumberBoxWidth"),
                r.Double("RowLabelColumnWidth"),
                r.Thickness("RootBorderThickness"),
                r.CornerRadius("RootCornerRadius"),
                r.CornerRadius("InnerCornerRadius"),
                r.CornerRadius("ZeroCornerRadius"),
                r.Thickness("TitleMargin"),
                r.Thickness("BodyMargin"),
                r.Thickness("LeftMargin"),
                r.Thickness("RightMargin"),
                r.Thickness("CurveComboMargin"),
                r.Thickness("FooterMargin"),
                r.Thickness("ResetMargin"),
                r.Thickness("RadioMargin"),
                r.Thickness("ZeroThickness"),
                r.Double("ValueFontSize"),
                r.Double("RadioFontSize"),
                r.Double("RowLabelFontSize"),
                r.Double("RowBottomMargin"),
                r.Double("OffsetRowBottomMargin"),
                r.Double("CaptionButtonWidth"),
                r.Double("CaptionButtonHeight"),
                r.Double("CaptionButtonFontSize"),
                r.Thickness("CaptionButtonPadding"));
        }
    }

    private static string GetEffectiveCurveName(Fan fan)
    {
        if (!string.IsNullOrWhiteSpace(fan.Group) && fan.AssignedGroup is { } group)
            return group.AssignedCurveName;

        return fan.AssignedCurveName;
    }

    private static string NormalizeCurveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        return string.Equals(name, "None", StringComparison.OrdinalIgnoreCase) ? string.Empty : name;
    }

    private static void SelectComboByTag(SettingsComboBox combo, string? tag)
    {
        string normalized = tag ?? string.Empty;
        foreach (SettingsComboBoxItem item in combo.Items.OfType<SettingsComboBoxItem>())
        {
            if (!string.Equals(item.Tag as string ?? string.Empty, normalized,
                    StringComparison.OrdinalIgnoreCase)) continue;
            combo.SelectedItem = item;
            return;
        }

        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string SelectedTag(SettingsComboBox combo) =>
        combo.SelectedItem is { } item ? item.Tag as string ?? string.Empty : string.Empty;

    private static int ReadInt(SettingsNumberBox box) =>
        box.Value.HasValue ? (int)Math.Round(box.Value.Value) : 0;
}
