using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class ColorPickerLayout
{
    private static ColorPickerWindowResources AXAMLResources => ColorPickerWindowResources.Current;

    public static double WindowWidth => AXAMLResources.AxamlColorPicker.WindowWidth;
    public static double WindowMinWidth => AXAMLResources.AxamlColorPicker.WindowMinWidth;
    public static double PickerPlaneWidth => AXAMLResources.AxamlColorPicker.PickerPlaneWidth;
    public static double PrimarySliderWidth => AXAMLResources.AxamlColorPicker.PrimarySliderWidth;
    public static double ChannelSliderWidth => AXAMLResources.AxamlColorPicker.ChannelSliderWidth;
    public static double ChannelBandHeight => AXAMLResources.AxamlColorPicker.ChannelBandHeight;
    public static double TitleBarHeight => AXAMLResources.AxamlColorPicker.TitleBarHeight;
    public static Thickness RootBorderThickness => AXAMLResources.AxamlColorPicker.RootBorderThickness;
    public static CornerRadius RootCornerRadius => AXAMLResources.AxamlColorPicker.RootCornerRadius;
    public static CornerRadius ZeroCornerRadius => AXAMLResources.AxamlColorPicker.ZeroCornerRadius;
    public static Thickness TitleMargin => AXAMLResources.AxamlColorPicker.TitleMargin;
    public static Thickness BodyMargin => AXAMLResources.AxamlColorPicker.BodyMargin;
    public static double BodyGapHeight => AXAMLResources.AxamlColorPicker.BodyGapHeight;
    public static double FooterGapHeight => AXAMLResources.AxamlColorPicker.FooterGapHeight;
    public static Thickness FooterMargin => AXAMLResources.AxamlColorPicker.FooterMargin;
    public static double ActionButtonGapWidth => AXAMLResources.AxamlColorPicker.ActionButtonGapWidth;
    public static Thickness ActionButtonPadding => AXAMLResources.AxamlColorPicker.ActionButtonPadding;
    public static double PrimaryColumnGapWidth => AXAMLResources.AxamlColorPicker.PrimaryColumnGapWidth;
    public static double SecondaryColumnGapWidth => AXAMLResources.AxamlColorPicker.SecondaryColumnGapWidth;
    public static double ChannelColumnGapWidth => AXAMLResources.AxamlColorPicker.ChannelColumnGapWidth;
    public static double LabelFontSize => AXAMLResources.AxamlColorPicker.LabelFontSize;
    public static Thickness FooterLabelMargin => AXAMLResources.AxamlColorPicker.FooterLabelMargin;
    public static double ChannelValueWidth => AXAMLResources.AxamlColorPicker.ChannelValueWidth;
    public static Thickness ChannelValueMargin => AXAMLResources.AxamlColorPicker.ChannelValueMargin;
    public static double HexBoxWidth => AXAMLResources.AxamlColorPicker.HexBoxWidth;
    public static double HexRowGapWidth => AXAMLResources.AxamlColorPicker.HexRowGapWidth;
}

public sealed record TrayAppDotNETColorPickerStrings(
    string DefaultTitle,
    string CloseTooltip,
    string HueLabel,
    string AlphaLabel,
    string RedLabel,
    string GreenLabel,
    string BlueLabel,
    string RgbaHexLabel,
    string ArgbHexLabel,
    string DefaultButton,
    string ResetButton);

public sealed class TrayAppDotNETColorPickerWindow : Window, IDisposable
{
    private readonly SettingsPalette _palette;
    private readonly TrayAppDotNETColorPickerStrings _strings;
    private readonly bool _hasAlpha;
    private readonly bool _enableRoundedCorners;
    private readonly Color _baselineColor;
    private readonly Color _defaultColor;
    private readonly TrayAppDotNETSaturationValuePicker _svPicker;
    private readonly TrayAppDotNETColorSlider _hueSlider;
    private readonly TrayAppDotNETColorSlider _alphaSlider;
    private readonly TrayAppDotNETColorSlider _rSlider;
    private readonly TrayAppDotNETColorSlider _gSlider;
    private readonly TrayAppDotNETColorSlider _bSlider;
    private readonly TextBlock _rValueLabel;
    private readonly TextBlock _gValueLabel;
    private readonly TextBlock _bValueLabel;
    private readonly TextBox _rgbaBox;
    private readonly TextBox _argbBox;
    private readonly DispatcherTimer _notifyTimer;
    private readonly UIResourceScope _windowResources;
    private UIContentGeneration? _contentGeneration;
    private int _disposeState;
    private bool _closed;
    private bool _suppressArgb;
    private bool _suppressRgba;
    private bool _suppressSlider;
    private Color? _pendingNotification;
    private Color _currentColor;
    private double _freePickHue;

    public TrayAppDotNETColorPickerWindow(
        string title,
        bool hasAlpha,
        Color? startingColor,
        Color? defaultColor,
        SettingsPalette palette,
        TrayAppDotNETColorPickerStrings strings,
        bool enableRoundedCorners = true)
    {
        _palette = palette;
        _strings = strings;
        _hasAlpha = hasAlpha;
        _enableRoundedCorners = enableRoundedCorners;

        Color seed = startingColor ?? AppTheme.ColorPickerDefaultColor;
        if (!hasAlpha) seed = Color.FromArgb(a: 0xFF, seed.R, seed.G, seed.B);
        _currentColor = seed;
        _baselineColor = seed;

        Color fallback = defaultColor ?? seed;
        if (!hasAlpha) fallback = Color.FromArgb(a: 0xFF, fallback.R, fallback.G, fallback.B);
        _defaultColor = fallback;

        Title = string.IsNullOrWhiteSpace(title) ? strings.DefaultTitle : title;
        Width = ColorPickerLayout.WindowWidth;
        MinWidth = ColorPickerLayout.WindowMinWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _svPicker = new TrayAppDotNETSaturationValuePicker(palette)
        {
            Width = ColorPickerLayout.PickerPlaneWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _hueSlider = new TrayAppDotNETColorSlider(TrayAppDotNETColorSliderKind.Hue, palette)
        {
            Width = ColorPickerLayout.PrimarySliderWidth,
            Minimum = 0,
            Maximum = 360,
            SmallChange = 1,
            LargeChange = 30
        };
        _alphaSlider = new TrayAppDotNETColorSlider(TrayAppDotNETColorSliderKind.Alpha, palette)
        {
            Width = ColorPickerLayout.PrimarySliderWidth,
            Minimum = 0,
            Maximum = 255,
            SmallChange = 1,
            LargeChange = 16,
            IsDirectionReversed = true,
            IsEnabled = hasAlpha
        };
        _rSlider = CreateChannelSlider();
        _gSlider = CreateChannelSlider();
        _bSlider = CreateChannelSlider();
        _rValueLabel = ChannelValueLabel("0");
        _gValueLabel = ChannelValueLabel("0");
        _bValueLabel = ChannelValueLabel("0");
        _rgbaBox = HexBox();
        _argbBox = HexBox();
        _windowResources = new UIResourceScope(nameof(TrayAppDotNETColorPickerWindow));

        _notifyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.ColorPickerChangeCooldownMs)
        };
        _notifyTimer.Tick += OnNotifyTimerTick;
        _windowResources.Add(() =>
        {
            _notifyTimer.Stop();
            _notifyTimer.Tick -= OnNotifyTimerTick;
        });

        Closed += OnWindowClosed;
        _windowResources.Add(() => Closed -= OnWindowClosed);

        UIResourceScope contentResources = new(nameof(TrayAppDotNETColorPickerWindow) + ".Content");
        try
        {
            contentResources.Own(_svPicker);
            contentResources.Own(_hueSlider);
            contentResources.Own(_alphaSlider);
            contentResources.Own(_rSlider);
            contentResources.Own(_gSlider);
            contentResources.Own(_bSlider);

            Border root = BuildContent(Title, contentResources);
            WireEvents(contentResources);
            UIContentGeneration contentGeneration = new(
                nameof(TrayAppDotNETColorPickerWindow),
                root,
                contentResources);
            _contentGeneration = contentGeneration;
            ControlNameScope.For(this).AssignLogicalSubtree(root, this);
            Content = root;
            _windowResources.Add(() => RetireContent(contentGeneration));

            RefreshHueFromColor();
            SyncControlsFromColor();
        }
        catch
        {
            contentResources.Dispose();
            DisposeCore();
            throw;
        }
    }

    public event EventHandler<Color>? ColorChanged;

    public Color CurrentColor => _currentColor;

    public bool IsDirty => _currentColor != _baselineColor;

    private Border BuildContent(string title, UIResourceScope resources)
    {
        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(new GridLength(ColorPickerLayout.TitleBarHeight)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Control titleBar = BuildTitleBar(title, resources);
        Grid.SetRow(titleBar, value: 0);
        root.Children.Add(titleBar);

        Grid body = BuildBody(resources);
        Grid.SetRow(body, value: 1);
        root.Children.Add(body);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = ColorPickerLayout.RootBorderThickness,
            CornerRadius = _enableRoundedCorners
                ? ColorPickerLayout.RootCornerRadius
                : ColorPickerLayout.ZeroCornerRadius,
            ClipToBounds = _enableRoundedCorners,
            Child = root
        };
    }

    private Grid BuildTitleBar(string title, UIResourceScope resources)
    {
        Grid titleBar = new() { Background = Brushes.Transparent, Height = ColorPickerLayout.TitleBarHeight };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        titleBar.PointerPressed += OnTitleBarPointerPressed;
        resources.Add(() => titleBar.PointerPressed -= OnTitleBarPointerPressed);

        TextBlock titleText = TrayAppDotNETSettingsUI.Text(title, _palette);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = ColorPickerLayout.TitleMargin;
        Grid.SetColumn(titleText, value: 0);
        titleBar.Children.Add(titleText);

        TrayAppDotNETCaptionCloseButton close = new(_palette);
        TrayAppDotNETToolTip.SetTip(close, _strings.CloseTooltip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(close);
        close.Click += OnCloseClick;
        resources.Add(() => close.Click -= OnCloseClick);
        Grid.SetColumn(close, value: 1);
        titleBar.Children.Add(close);

        return titleBar;
    }

    private Grid BuildBody(UIResourceScope resources)
    {
        Grid body = new() { Margin = ColorPickerLayout.BodyMargin };
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(new GridLength(ColorPickerLayout.BodyGapHeight)));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid pickerGrid = BuildPickerGrid();
        Grid.SetRow(pickerGrid, value: 0);
        body.Children.Add(pickerGrid);

        Grid footer = BuildFooterGrid(resources);
        Grid.SetRow(footer, value: 2);
        body.Children.Add(footer);

        return body;
    }

    private Grid BuildPickerGrid()
    {
        Grid grid = SharedPickerColumns();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(ColorPickerLayout.ChannelBandHeight)));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid.SetRow(_rValueLabel, value: 0);
        Grid.SetColumn(_rValueLabel, value: 6);
        grid.Children.Add(_rValueLabel);
        Grid.SetRow(_gValueLabel, value: 0);
        Grid.SetColumn(_gValueLabel, value: 8);
        grid.Children.Add(_gValueLabel);
        Grid.SetRow(_bValueLabel, value: 0);
        Grid.SetColumn(_bValueLabel, value: 10);
        grid.Children.Add(_bValueLabel);

        Grid.SetRow(_svPicker, value: 0);
        Grid.SetRowSpan(_svPicker, value: 2);
        Grid.SetColumn(_svPicker, value: 0);
        grid.Children.Add(_svPicker);

        AddSlider(grid, _hueSlider, column: 2, spanValueRow: true);
        AddSlider(grid, _alphaSlider, column: 4, spanValueRow: true);
        AddSlider(grid, _rSlider, column: 6, spanValueRow: false);
        AddSlider(grid, _gSlider, column: 8, spanValueRow: false);
        AddSlider(grid, _bSlider, column: 10, spanValueRow: false);

        AddFooterLabel(grid, _strings.HueLabel, column: 2);
        AddFooterLabel(grid, _strings.AlphaLabel, column: 4);
        AddFooterLabel(grid, _strings.RedLabel, column: 6);
        AddFooterLabel(grid, _strings.GreenLabel, column: 8);
        AddFooterLabel(grid, _strings.BlueLabel, column: 10);

        return grid;
    }

    private Grid BuildFooterGrid(UIResourceScope resources)
    {
        Grid grid = SharedPickerColumns();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(ColorPickerLayout.FooterGapHeight)));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Margin = ColorPickerLayout.FooterMargin;

        Grid rgbaRow = HexRow(_strings.RgbaHexLabel, _rgbaBox);
        Grid.SetRow(rgbaRow, value: 0);
        Grid.SetColumn(rgbaRow, value: 0);
        grid.Children.Add(rgbaRow);

        Grid argbRow = HexRow(_strings.ArgbHexLabel, _argbBox);
        Grid.SetRow(argbRow, value: 2);
        Grid.SetColumn(argbRow, value: 0);
        grid.Children.Add(argbRow);

        Grid buttons = new() { VerticalAlignment = VerticalAlignment.Center };
        buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        buttons.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(ColorPickerLayout.ActionButtonGapWidth)));
        buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        SettingsButton defaultButton = TrayAppDotNETSettingsUI.Button(_strings.DefaultButton, _palette);
        SettingsButton resetButton = TrayAppDotNETSettingsUI.Button(_strings.ResetButton, _palette);
        defaultButton.Padding = ColorPickerLayout.ActionButtonPadding;
        resetButton.Padding = ColorPickerLayout.ActionButtonPadding;
        defaultButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        resetButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        defaultButton.Click += OnDefaultClick;
        resources.Add(() => defaultButton.Click -= OnDefaultClick);
        resetButton.Click += OnResetClick;
        resources.Add(() => resetButton.Click -= OnResetClick);

        Grid.SetColumn(defaultButton, value: 0);
        buttons.Children.Add(defaultButton);
        Grid.SetColumn(resetButton, value: 2);
        buttons.Children.Add(resetButton);

        Grid.SetRow(buttons, value: 2);
        Grid.SetColumn(buttons, value: 2);
        Grid.SetColumnSpan(buttons, value: 9);
        grid.Children.Add(buttons);

        return grid;
    }

    private static Grid SharedPickerColumns()
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(ColorPickerLayout.PickerPlaneWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(ColorPickerLayout.PrimaryColumnGapWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(ColorPickerLayout.SecondaryColumnGapWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(ColorPickerLayout.ChannelColumnGapWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(ColorPickerLayout.ChannelColumnGapWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(ColorPickerLayout.ChannelColumnGapWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        return grid;
    }

    private static void AddSlider(Grid grid, Control slider, int column, bool spanValueRow)
    {
        Grid.SetRow(slider, spanValueRow ? 0 : 1);
        if (spanValueRow) Grid.SetRowSpan(slider, value: 2);
        Grid.SetColumn(slider, column);
        slider.VerticalAlignment = VerticalAlignment.Stretch;
        grid.Children.Add(slider);
    }

    private void AddFooterLabel(Grid grid, string text, int column)
    {
        TextBlock label =
            TrayAppDotNETSettingsUI.Text(text, _palette, ColorPickerLayout.LabelFontSize, FontWeight.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = ColorPickerLayout.FooterLabelMargin;
        Grid.SetRow(label, value: 2);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private TextBlock ChannelValueLabel(string text)
    {
        TextBlock label =
            TrayAppDotNETSettingsUI.Text(text, _palette, ColorPickerLayout.LabelFontSize, FontWeight.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        label.Width = ColorPickerLayout.ChannelValueWidth;
        label.Margin = ColorPickerLayout.ChannelValueMargin;
        return label;
    }

    private TextBox HexBox()
    {
        TextBox box = TrayAppDotNETSettingsUI.TextBox(_palette, ColorPickerLayout.HexBoxWidth);
        box.FontFamily = new FontFamily("Consolas, Courier New");
        return box;
    }

    private Grid HexRow(string labelText, TextBox box)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(ColorPickerLayout.HexRowGapWidth)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock label = TrayAppDotNETSettingsUI.Text(labelText, _palette, ColorPickerLayout.LabelFontSize,
            FontWeight.SemiBold);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, value: 0);
        row.Children.Add(label);

        Grid.SetColumn(box, value: 2);
        row.Children.Add(box);
        return row;
    }

    private TrayAppDotNETColorSlider CreateChannelSlider() =>
        new(TrayAppDotNETColorSliderKind.Channel, _palette)
        {
            Width = ColorPickerLayout.ChannelSliderWidth,
            Minimum = 0,
            Maximum = 255,
            SmallChange = 1,
            LargeChange = 16
        };

    private void WireEvents(UIResourceScope resources)
    {
        _svPicker.SelectionChanged += OnSaturationValueChanged;
        resources.Add(() => _svPicker.SelectionChanged -= OnSaturationValueChanged);
        _hueSlider.ValueChanged += OnHueChanged;
        resources.Add(() => _hueSlider.ValueChanged -= OnHueChanged);
        _alphaSlider.ValueChanged += OnAlphaChanged;
        resources.Add(() => _alphaSlider.ValueChanged -= OnAlphaChanged);
        _rSlider.ValueChanged += OnRedChanged;
        resources.Add(() => _rSlider.ValueChanged -= OnRedChanged);
        _gSlider.ValueChanged += OnGreenChanged;
        resources.Add(() => _gSlider.ValueChanged -= OnGreenChanged);
        _bSlider.ValueChanged += OnBlueChanged;
        resources.Add(() => _bSlider.ValueChanged -= OnBlueChanged);
        _rgbaBox.TextChanged += OnRgbaTextChanged;
        resources.Add(() => _rgbaBox.TextChanged -= OnRgbaTextChanged);
        _argbBox.TextChanged += OnArgbTextChanged;
        resources.Add(() => _argbBox.TextChanged -= OnArgbTextChanged);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_closed || sender is not Control titleBar) return;
        if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed) return;
        BeginMoveDrag(e);
    }

    private void OnCloseClick(object? sender, EventArgs e)
    {
        if (_closed) return;
        Close();
    }

    private void OnDefaultClick(object? sender, EventArgs e) =>
        ApplyColor(_defaultColor, ColorApplySource.None, force: true);

    private void OnResetClick(object? sender, EventArgs e) =>
        ApplyColor(_baselineColor, ColorApplySource.None, force: true);

    private void OnSaturationValueChanged(
        object? sender,
        TrayAppDotNETSaturationValueChangedEventArgs e)
    {
        Color rgb = HsvToRgb(_freePickHue, e.Saturation, e.Value);
        ApplyColor(Color.FromArgb(_currentColor.A, rgb.R, rgb.G, rgb.B), ColorApplySource.SaturationValue);
    }

    private void OnHueChanged(object? sender, double value)
    {
        if (_closed || _suppressSlider) return;
        _freePickHue = Math.Clamp(value, min: 0, max: 360);
        (double _, double saturation, double brightness) =
            RgbToHsv(_currentColor.R, _currentColor.G, _currentColor.B);
        Color rgb = HsvToRgb(_freePickHue, saturation, brightness);
        ApplyColor(Color.FromArgb(_currentColor.A, rgb.R, rgb.G, rgb.B), ColorApplySource.Hue, force: true);
    }

    private void OnAlphaChanged(object? sender, double value)
    {
        if (_closed || _suppressSlider) return;
        byte channel = ToByte(value);
        ApplyColor(Color.FromArgb(channel, _currentColor.R, _currentColor.G, _currentColor.B),
            ColorApplySource.Alpha);
    }

    private void OnRedChanged(object? sender, double value)
    {
        if (_closed || _suppressSlider) return;
        byte channel = ToByte(value);
        ApplyColor(Color.FromArgb(_currentColor.A, channel, _currentColor.G, _currentColor.B),
            ColorApplySource.Red);
    }

    private void OnGreenChanged(object? sender, double value)
    {
        if (_closed || _suppressSlider) return;
        byte channel = ToByte(value);
        ApplyColor(Color.FromArgb(_currentColor.A, _currentColor.R, channel, _currentColor.B),
            ColorApplySource.Green);
    }

    private void OnBlueChanged(object? sender, double value)
    {
        if (_closed || _suppressSlider) return;
        byte channel = ToByte(value);
        ApplyColor(Color.FromArgb(_currentColor.A, _currentColor.R, _currentColor.G, channel),
            ColorApplySource.Blue);
    }

    private void OnRgbaTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_closed || _suppressRgba) return;
        if (!TryParseHex(_rgbaBox.Text, argbOrder: false, out Color parsed)) return;
        if (!_hasAlpha) parsed = Color.FromArgb(a: 0xFF, parsed.R, parsed.G, parsed.B);
        ApplyColor(parsed, ColorApplySource.RgbaText);
    }

    private void OnArgbTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_closed || _suppressArgb) return;
        if (!TryParseHex(_argbBox.Text, argbOrder: true, out Color parsed)) return;
        if (!_hasAlpha) parsed = Color.FromArgb(a: 0xFF, parsed.R, parsed.G, parsed.B);
        ApplyColor(parsed, ColorApplySource.ArgbText);
    }

    private void ApplyColor(Color color, ColorApplySource source, bool force = false)
    {
        if (_closed) return;
        if (!_hasAlpha)
            color = Color.FromArgb(a: 0xFF, color.R, color.G, color.B);
        if (!force && color == _currentColor) return;

        _currentColor = color;
        SyncControlsFromColor(source);
        EnqueueColorChangedNotification();
    }

    private void SyncControlsFromColor(ColorApplySource source = ColorApplySource.None)
    {
        (double hue, double sat, double val) = RgbToHsv(_currentColor.R, _currentColor.G, _currentColor.B);
        if (source != ColorApplySource.Hue && source != ColorApplySource.SaturationValue && sat > 0)
            _freePickHue = hue;

        _svPicker.SetState(HsvToRgb(_freePickHue, sat: 1.0, val: 1.0), sat, val, _currentColor);

        _suppressSlider = true;
        try
        {
            _hueSlider.SetValueSilently(_freePickHue);
            _alphaSlider.CurrentColor = _currentColor;
            _alphaSlider.SetValueSilently(_currentColor.A);
            _rSlider.SetValueSilently(_currentColor.R);
            _gSlider.SetValueSilently(_currentColor.G);
            _bSlider.SetValueSilently(_currentColor.B);
        }
        finally
        {
            _suppressSlider = false;
        }

        _rValueLabel.Text = _currentColor.R.ToString(CultureInfo.InvariantCulture);
        _gValueLabel.Text = _currentColor.G.ToString(CultureInfo.InvariantCulture);
        _bValueLabel.Text = _currentColor.B.ToString(CultureInfo.InvariantCulture);

        if (source != ColorApplySource.RgbaText) WriteRgbaBox();
        if (source != ColorApplySource.ArgbText) WriteArgbBox();
    }

    private void RefreshHueFromColor()
    {
        (double hue, double sat, double _) = RgbToHsv(_currentColor.R, _currentColor.G, _currentColor.B);
        if (sat > 0) _freePickHue = hue;
    }

    private void WriteRgbaBox()
    {
        _suppressRgba = true;
        try { _rgbaBox.Text = FormatRgba(_currentColor); }
        finally { _suppressRgba = false; }
    }

    private void WriteArgbBox()
    {
        _suppressArgb = true;
        try { _argbBox.Text = FormatArgb(_currentColor); }
        finally { _suppressArgb = false; }
    }

    private void EnqueueColorChangedNotification()
    {
        if (_closed) return;
        _pendingNotification = _currentColor;
        if (!_notifyTimer.IsEnabled)
            _notifyTimer.Start();
    }

    private void OnNotifyTimerTick(object? sender, EventArgs e)
    {
        if (_closed)
        {
            _notifyTimer.Stop();
            _pendingNotification = null;
            return;
        }

        if (_pendingNotification is not { } snapshot)
        {
            _notifyTimer.Stop();
            return;
        }

        _pendingNotification = null;
        ColorChanged?.Invoke(this, snapshot);
    }

    private void OnWindowClosed(object? sender, EventArgs e) => DisposeCore();

    /// <summary>Closes the picker when necessary and deterministically releases its UI resources.</summary>
    public void Dispose()
    {
        if (!_closed && IsVisible)
        {
            try
            {
                Close();
            }
            finally
            {
                DisposeCore();
            }

            return;
        }

        DisposeCore();
    }

    private void DisposeCore()
    {
        if (Interlocked.Exchange(ref _disposeState, value: 1) != 0) return;

        _closed = true;
        _pendingNotification = null;
        ColorChanged = null;
        _windowResources.Dispose();
        UIContentGeneration? contentGeneration = Interlocked.Exchange(ref _contentGeneration, value: null);
        if (contentGeneration == null) return;

        try
        {
            if (!contentGeneration.IsDisposed && ReferenceEquals(Content, contentGeneration.Root))
                Content = null;
        }
        finally
        {
            contentGeneration.Dispose();
        }
    }

    private void RetireContent(UIContentGeneration contentGeneration)
    {
        if (ReferenceEquals(_contentGeneration, contentGeneration))
            _contentGeneration = null;
        try
        {
            if (!contentGeneration.IsDisposed && ReferenceEquals(Content, contentGeneration.Root))
                Content = null;
        }
        finally
        {
            contentGeneration.Dispose();
        }
    }

    private static string FormatArgb(Color color) => $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string FormatRgba(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, min: 0, max: 255));

    private static bool TryParseHex(string? input, bool argbOrder, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string hex = input.Trim().TrimStart('#');
        if (hex.Length != 6 && hex.Length != 8) return false;

        try
        {
            if (hex.Length == 6)
            {
                color = Color.FromArgb(
                    a: 0xFF,
                    Convert.ToByte(hex[..2], fromBase: 16),
                    Convert.ToByte(hex[2..4], fromBase: 16),
                    Convert.ToByte(hex[4..6], fromBase: 16));
                return true;
            }

            byte b0 = Convert.ToByte(hex[..2], fromBase: 16);
            byte b1 = Convert.ToByte(hex[2..4], fromBase: 16);
            byte b2 = Convert.ToByte(hex[4..6], fromBase: 16);
            byte b3 = Convert.ToByte(hex[6..8], fromBase: 16);
            color = argbOrder
                ? Color.FromArgb(b0, b1, b2, b3)
                : Color.FromArgb(b3, b0, b1, b2);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static Color HsvToRgb(double hue, double sat, double val)
    {
        sat = Math.Clamp(sat, min: 0, max: 1);
        val = Math.Clamp(val, min: 0, max: 1);
        if (sat <= 0)
        {
            byte gray = (byte)Math.Round(val * 255);
            return Color.FromArgb(a: 0xFF, gray, gray, gray);
        }

        double h = (hue % 360 + 360) % 360 / 60.0;
        int sector = (int)Math.Floor(h);
        double f = h - sector;
        double p = val * (1 - sat);
        double q = val * (1 - sat * f);
        double t = val * (1 - sat * (1 - f));

        (double r, double g, double b) = sector switch
        {
            0 => (val, t, p),
            1 => (q, val, p),
            2 => (p, val, t),
            3 => (p, q, val),
            4 => (t, p, val),
            _ => (val, p, q)
        };

        return Color.FromArgb(
            a: 0xFF,
            (byte)Math.Round(Math.Clamp(r, min: 0, max: 1) * 255),
            (byte)Math.Round(Math.Clamp(g, min: 0, max: 1) * 255),
            (byte)Math.Round(Math.Clamp(b, min: 0, max: 1) * 255));
    }

    internal static (double Hue, double Sat, double Val) RgbToHsv(byte r, byte g, byte b)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double val = max;
        double sat = max == 0 ? 0 : delta / max;
        double hue = 0;

        if (delta > 0)
        {
            if (max == rd) hue = 60.0 * ((gd - bd) / delta % 6);
            else if (max == gd) hue = 60.0 * ((bd - rd) / delta + 2);
            else hue = 60.0 * ((rd - gd) / delta + 4);
        }

        if (hue < 0) hue += 360;
        return (hue, sat, val);
    }

    private enum ColorApplySource
    {
        None,
        SaturationValue,
        Hue,
        Alpha,
        Red,
        Green,
        Blue,
        RgbaText,
        ArgbText
    }
}

internal sealed class TrayAppDotNETSaturationValuePicker : Control, IDisposable
{
    private readonly SettingsPalette _palette;
    private IPointer? _capturedPointer;
    private int _disposeState;
    private bool _dragging;
    private Color _hueColor = AppTheme.ColorPickerHueRed;
    private Color _currentColor = AppTheme.ColorPickerHueRed;
    private double _saturation;
    private double _value = 1;

    public TrayAppDotNETSaturationValuePicker(SettingsPalette palette)
    {
        _palette = palette;
        Focusable = true;
        Cursor = TrayAppDotNETCursors.Cross;
    }

    public event EventHandler<TrayAppDotNETSaturationValueChangedEventArgs>? SelectionChanged;

    public void SetState(Color hueColor, double saturation, double value, Color currentColor)
    {
        _hueColor = hueColor;
        _saturation = Math.Clamp(saturation, min: 0, max: 1);
        _value = Math.Clamp(value, min: 0, max: 1);
        _currentColor = currentColor;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        context.FillRectangle(new SolidColorBrush(_hueColor), bounds, cornerRadius: 6);
        context.FillRectangle(
            CreateGradient(AppTheme.ColorPickerWhite, AppTheme.ColorPickerTransparentWhite, horizontal: true),
            bounds,
            cornerRadius: 6);
        context.FillRectangle(
            CreateGradient(AppTheme.ColorPickerTransparentBlack, AppTheme.ColorPickerBlack, horizontal: false),
            bounds,
            cornerRadius: 6);
        context.DrawRectangle(new Pen(new SolidColorBrush(_palette.Border)), bounds, cornerRadius: 6);

        double x = Math.Clamp(_saturation * bounds.Width, min: 0, bounds.Width);
        double y = Math.Clamp((1 - _value) * bounds.Height, min: 0, bounds.Height);
        Point center = new(x, y);
        context.DrawEllipse(
            Brushes.Transparent,
            new Pen(new SolidColorBrush(AppTheme.ColorPickerBlack)),
            center,
            radiusX: 8,
            radiusY: 8);
        context.DrawEllipse(
            new SolidColorBrush(_currentColor),
            new Pen(new SolidColorBrush(AppTheme.ColorPickerWhite)),
            center,
            radiusX: 7,
            radiusY: 7);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        _dragging = true;
        _capturedPointer = e.Pointer;
        try
        {
            e.Pointer.Capture(this);
        }
        catch
        {
            _capturedPointer = null;
            _dragging = false;
            throw;
        }

        UpdateFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            UpdateFromPoint(e.GetPosition(this));
        else
            StopDragging(e);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        StopDragging(e);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _capturedPointer = null;
        _dragging = false;
        base.OnPointerCaptureLost(e);
    }

    private void StopDragging(PointerEventArgs e)
    {
        _dragging = false;
        _capturedPointer = null;
        e.Pointer.Capture(null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, value: 1) != 0) return;

        _dragging = false;
        IPointer? capturedPointer = Interlocked.Exchange(ref _capturedPointer, value: null);
        try { capturedPointer?.Capture(null); }
        catch (Exception exception)
        {
            TADNLog.Log($"Color picker pointer release failed: {exception.Message}");
        }

        SelectionChanged = null;
    }

    private void UpdateFromPoint(Point point)
    {
        double width = Math.Max(Bounds.Width, val2: 1);
        double height = Math.Max(Bounds.Height, val2: 1);
        _saturation = Math.Clamp(point.X / width, min: 0, max: 1);
        _value = Math.Clamp(1 - point.Y / height, min: 0, max: 1);
        InvalidateVisual();
        SelectionChanged?.Invoke(this, new TrayAppDotNETSaturationValueChangedEventArgs(_saturation, _value));
    }

    private static LinearGradientBrush CreateGradient(Color start, Color end, bool horizontal) =>
        new()
        {
            StartPoint = new RelativePoint(x: 0, y: 0, RelativeUnit.Relative),
            EndPoint = horizontal
                ? new RelativePoint(x: 1, y: 0, RelativeUnit.Relative)
                : new RelativePoint(x: 0, y: 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(start, offset: 0), new GradientStop(end, offset: 1) }
        };
}

internal sealed record TrayAppDotNETSaturationValueChangedEventArgs(double Saturation, double Value);

internal enum TrayAppDotNETColorSliderKind
{
    Channel,
    Hue,
    Alpha
}

internal sealed class TrayAppDotNETColorSlider : Control, IDisposable
{
    private readonly SettingsPalette _palette;
    private readonly TrayAppDotNETColorSliderKind _kind;
    private IPointer? _capturedPointer;
    private int _disposeState;
    private bool _dragging;
    private double _value;

    public TrayAppDotNETColorSlider(TrayAppDotNETColorSliderKind kind, SettingsPalette palette)
    {
        _kind = kind;
        _palette = palette;
        Focusable = true;
        Cursor = TrayAppDotNETCursors.Hand;
    }

    public event EventHandler<double>? ValueChanged;

    public double Minimum { get; init; }

    public double Maximum { get; init; } = 255;

    public double SmallChange { get; init; } = 1;

    public double LargeChange { get; init; } = 16;

    public bool IsDirectionReversed { get; init; }

    public Color CurrentColor { get; set; } = AppTheme.ColorPickerDefaultColor;

    public double Value
    {
        get => _value;
        set => SetValue(value, raise: true);
    }

    public void SetValueSilently(double value) => SetValue(value, raise: false);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (_kind == TrayAppDotNETColorSliderKind.Channel)
            RenderChannel(context, bounds);
        else
            RenderGradient(context, bounds);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        _dragging = true;
        _capturedPointer = e.Pointer;
        try
        {
            e.Pointer.Capture(this);
        }
        catch
        {
            _capturedPointer = null;
            _dragging = false;
            throw;
        }

        Value = ValueFromY(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Value = ValueFromY(e.GetPosition(this).Y);
        else
            StopDragging(e);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        StopDragging(e);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _capturedPointer = null;
        _dragging = false;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsEnabled) return;
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? LargeChange : SmallChange;
        Value += Math.Sign(e.Delta.Y) * step;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEnabled) return;

        double? next = e.Key switch
        {
            Key.Up or Key.Right => Value + SmallChange,
            Key.Down or Key.Left => Value - SmallChange,
            Key.PageUp => Value + LargeChange,
            Key.PageDown => Value - LargeChange,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null
        };
        if (!next.HasValue) return;
        Value = next.Value;
        e.Handled = true;
    }

    private void StopDragging(PointerEventArgs e)
    {
        _dragging = false;
        _capturedPointer = null;
        e.Pointer.Capture(null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, value: 1) != 0) return;

        _dragging = false;
        IPointer? capturedPointer = Interlocked.Exchange(ref _capturedPointer, value: null);
        try { capturedPointer?.Capture(null); }
        catch (Exception exception)
        {
            TADNLog.Log($"Color slider pointer release failed: {exception.Message}");
        }

        ValueChanged = null;
    }

    private void SetValue(double value, bool raise)
    {
        double clamped = Math.Clamp(value, Minimum, Maximum);
        if (Math.Abs(_value - clamped) < 0.0001) return;
        _value = clamped;
        InvalidateVisual();
        if (raise) ValueChanged?.Invoke(this, _value);
    }

    private void RenderChannel(DrawingContext context, Rect bounds)
    {
        double x = bounds.Center.X - 2;
        Rect track = new(x, y: 0, width: 4, bounds.Height);
        context.FillRectangle(new SolidColorBrush(_palette.SliderTrack), track, cornerRadius: 2);

        double y = ThumbCenterY(bounds);
        Rect progress = new(x, y, width: 4, Math.Max(val1: 0, bounds.Height - y));
        context.FillRectangle(new SolidColorBrush(_palette.SliderProgress), progress, cornerRadius: 2);

        Rect thumb = new(bounds.Center.X - 11, y - 5, width: 22, height: 10);
        context.FillRectangle(new SolidColorBrush(_palette.SliderThumb), thumb, cornerRadius: 5);
    }

    private void RenderGradient(DrawingContext context, Rect bounds)
    {
        Rect bar = new(x: 0.5, y: 0.5, bounds.Width - 1, bounds.Height - 1);
        context.FillRectangle(CreateBarGradient(), bar, cornerRadius: 4);
        context.DrawRectangle(new Pen(new SolidColorBrush(_palette.Border)), bar, cornerRadius: 4);

        double thumbY = ThumbCenterY(bounds);
        Rect thumb = new(x: 1, thumbY - 4, Math.Max(val1: 0, bounds.Width - 2), height: 8);
        Color fill = _kind == TrayAppDotNETColorSliderKind.Hue
            ? TrayAppDotNETColorPickerWindow.HsvToRgb(Value, sat: 1.0, val: 1.0)
            : CurrentColor;
        context.FillRectangle(new SolidColorBrush(fill), thumb, cornerRadius: 2);
        context.DrawRectangle(new Pen(new SolidColorBrush(ThumbBorderColor())), thumb, cornerRadius: 2);
    }

    private LinearGradientBrush CreateBarGradient()
    {
        LinearGradientBrush brush = new()
        {
            StartPoint = new RelativePoint(x: 0, y: 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(x: 0, y: 1, RelativeUnit.Relative)
        };

        if (_kind == TrayAppDotNETColorSliderKind.Hue)
        {
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueRed, offset: 0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueMagenta, 1.0 / 6.0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueBlue, 2.0 / 6.0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueCyan, 3.0 / 6.0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueLime, 4.0 / 6.0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueYellow, 5.0 / 6.0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueRed, offset: 1));
            return brush;
        }

        Color top = Color.FromArgb(ToByte(ValueFromY(0)), CurrentColor.R, CurrentColor.G, CurrentColor.B);
        Color bottom = Color.FromArgb(ToByte(ValueFromY(Bounds.Height)), CurrentColor.R, CurrentColor.G,
            CurrentColor.B);
        brush.GradientStops.Add(new GradientStop(top, offset: 0));
        brush.GradientStops.Add(new GradientStop(bottom, offset: 1));
        return brush;
    }

    private double ValueFromY(double y)
    {
        double height = Math.Max(Bounds.Height, val2: 1);
        double normalized = Math.Clamp(y / height, min: 0, max: 1);
        double range = Maximum - Minimum;
        return IsDirectionReversed
            ? Minimum + normalized * range
            : Maximum - normalized * range;
    }

    private double ThumbCenterY(Rect bounds)
    {
        double range = Maximum - Minimum;
        if (range <= 0) return bounds.Height;

        double normalized = IsDirectionReversed
            ? (Value - Minimum) / range
            : (Maximum - Value) / range;
        return Math.Clamp(normalized * bounds.Height, min: 0, bounds.Height);
    }

    private Color ThumbBorderColor()
    {
        if (_kind != TrayAppDotNETColorSliderKind.Alpha) return _palette.Foreground;
        double a = CurrentColor.A / 255.0;
        double rgbWeight = a * (2 - a);
        double bgWeight = (1 - a) * (1 - a);
        double r = CurrentColor.R * rgbWeight + _palette.Background.R * bgWeight;
        double g = CurrentColor.G * rgbWeight + _palette.Background.G * bgWeight;
        double b = CurrentColor.B * rgbWeight + _palette.Background.B * bgWeight;
        double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        return luminance < 128 ? AppTheme.ColorPickerWhite : AppTheme.ColorPickerBlack;
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, min: 0, max: 255));
}
