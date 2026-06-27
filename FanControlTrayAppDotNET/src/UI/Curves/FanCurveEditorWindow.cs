using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace FanControlTrayAppDotNET.UI.Curves;

public sealed class FanCurveEditorWindow : Window
{
    private const double GraphWidth = 540.0;
    private const double GraphHeight = 270.0;
    private const double LeftColumnWidth = 260.0;
    private const int SmoothnessMin = 0;
    private const int SmoothnessMax = 100;

    private readonly Fan _fan;
    private readonly Curve _curve;
    private readonly AppSettings _settings;
    private readonly SettingsPalette _palette;
    private readonly FanCurveEditor _editor;
    private readonly SettingsComboBox _dataSourceCombo;
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
        _dataSourceCombo = null!;
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
    }

    public FanCurveEditorWindow(Fan fan, Curve curve, AppSettings settings)
    {
        _fan = fan;
        _curve = curve;
        _settings = settings;
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
        Width = 880;
        Height = 392;
        MinWidth = 820;
        MinHeight = 360;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;

        _editor = new FanCurveEditor
        {
            Width = GraphWidth,
            Height = GraphHeight,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Palette = FanCurveEditorPalette.FromSettingsPalette(
                _palette,
                AppServices.Theme ?? AppTheme.Default,
                AppTheme.ResolveEffectiveIsLightTheme(settings)),
        };
        _editor.CurveChanged += OnEditorCurveChanged;

        _dataSourceCombo = TrayAppDotNETSettingsUI.ComboBox(_palette, 210, autoSizeToText: false);
        _rpmModeToggle = TrayAppDotNETSettingsUI.Toggle(_palette, _curve.RPMMode, OnRpmModeChanged);
        _maxRpmBox = Number(_curve.MaxRPM, 1, Math.Max(10000, _curve.MaxRPM), "RPM", width: 100);
        _minRpmBox = Number(_curve.MinRPM, 0, Math.Max(10000, _curve.MaxRPM), "RPM", width: 100);
        _maxDutyBox = Number(_curve.MaxDutyCycle, 1, 100, "%", width: 82);
        _minDutyBox = Number(_curve.MinDutyCycle, 0, 100, "%", width: 82);
        _smoothnessBox = Number(_curve.SmoothingFactor, SmoothnessMin, SmoothnessMax, string.Empty, width: 82);
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
        _editor.CurveChanged -= OnEditorCurveChanged;
        base.OnClosed(e);
    }

    private Border BuildContent()
    {
        Grid shell = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
            Margin = new Thickness(0),
        };
        shell.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        shell.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        TextBlock title = TrayAppDotNETSettingsUI.Text(Title ?? "Fan Curve", _palette, 16, FontWeight.SemiBold);
        title.Margin = new Thickness(16, 14, 16, 6);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        shell.Children.Add(title);

        Grid main = new() { Margin = new Thickness(16, 4, 16, 16) };
        main.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(LeftColumnWidth)));
        main.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        StackPanel left = new()
        {
            Width = LeftColumnWidth,
            VerticalAlignment = VerticalAlignment.Top,
        };
        left.Children.Add(ControlBlock("Data source", _dataSourceCombo));
        left.Children.Add(ToggleRow("RPM mode", _rpmModeToggle));
        left.Children.Add(_maxRpmRow);
        left.Children.Add(ControlBlock("Min RPM", _minRpmBox));
        left.Children.Add(ControlBlock("Max duty", _maxDutyBox));
        left.Children.Add(ControlBlock("Min duty", _minDutyBox));
        left.Children.Add(ControlBlock("Smoothness", _smoothnessBox));
        left.Children.Add(ToggleRow("No decreasing", _preventDecreasingToggle));

        SettingsButton close = TrayAppDotNETSettingsUI.Button("Close", _palette);
        close.Width = 76;
        close.HorizontalAlignment = HorizontalAlignment.Left;
        close.Margin = new Thickness(0, 8, 0, 0);
        close.Click += (_, _) => Close();
        left.Children.Add(close);

        main.Children.Add(left);

        Grid graphHost = new()
        {
            Width = GraphWidth,
            Height = GraphHeight,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        graphHost.Children.Add(_editor);
        Grid.SetColumn(graphHost, 1);
        main.Children.Add(graphHost);

        Grid.SetRow(main, 1);
        shell.Children.Add(main);
        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = _settings.EnableRoundedCorners ? new CornerRadius(8) : new CornerRadius(0),
            Child = shell,
        };
    }

    private Border BuildMaxRpmRow()
    {
        StackPanel syncButtons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        syncButtons.Children.Add(_syncYesButton);
        _syncNoButton.Margin = new Thickness(4, 0, 0, 0);
        syncButtons.Children.Add(_syncNoButton);

        StackPanel row = LabeledRow("Max RPM");
        row.Children.Add(_maxRpmBox);
        row.Children.Add(syncButtons);
        return CompactCard(row);
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
        toggle.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(toggle);
        TextBlock text = TrayAppDotNETSettingsUI.TitleText(label, _palette);
        text.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        return CompactCard(row);
    }

    private StackPanel LabeledRow(string label)
    {
        TextBlock text = TrayAppDotNETSettingsUI.TitleText(label, _palette);
        text.Width = 86;
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
            CornerRadius = _settings.EnableRoundedCorners ? new CornerRadius(6) : new CornerRadius(0),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 0, 6),
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
        button.Width = 38;
        button.MinHeight = 28;
        button.Height = 28;
        button.Padding = new Thickness(0);
        button.Label.FontSize = 12;
        button.IsEnabled = false;
        return button;
    }

    private void WireControls()
    {
        _dataSourceCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            string key = SelectedDataSourceKey();
            _curve.SelectedDataSourceKey = key;
            EnsureCurveNodesOnDataSourceAxis();
            RefreshEditorBinding();
            Save();
        };
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
            SelectDataSourceCombo(_curve.SelectedDataSourceKey);
            SetRpmSyncButtonsEnabled(_rpmSyncPending);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void PopulateDataSources()
    {
        _dataSourceCombo.Items.Clear();
        foreach (DataSource source in DataSource.DataSources.Values
                     .OrderBy(s => s.ControllerName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            string unit = source.DisplayUnit;
            string label = string.IsNullOrWhiteSpace(unit)
                ? source.DisplayName
                : $"{source.DisplayName} ({unit})";
            _dataSourceCombo.Items.Add(new SettingsComboBoxItem(source.DataSourceKey, label, _palette));
        }

        _dataSourceCombo.IsEnabled = _dataSourceCombo.Items.Count > 0;
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

    private void SelectDataSourceCombo(string? key)
    {
        string normalized = key ?? string.Empty;
        foreach (SettingsComboBoxItem item in _dataSourceCombo.Items)
        {
            if (!string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase)) continue;
            _dataSourceCombo.SelectedItem = item;
            return;
        }

        if (_dataSourceCombo.Items.Count > 0)
            _dataSourceCombo.SelectedIndex = 0;
    }

    private string SelectedDataSourceKey() =>
        _dataSourceCombo.SelectedItem?.Tag?.ToString() ?? string.Empty;

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
