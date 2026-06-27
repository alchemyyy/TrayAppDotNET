using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

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

public sealed class TrayAppDotNETColorPickerWindow : Window
{
    private const double PickerPlaneWidth = 160;
    private const double ChannelBandHeight = 120;

    private readonly SettingsPalette _palette;
    private readonly TrayAppDotNETColorPickerStrings _strings;
    private readonly bool _hasAlpha;
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
        TrayAppDotNETColorPickerStrings strings)
    {
        _palette = palette;
        _strings = strings;
        _hasAlpha = hasAlpha;

        Color seed = startingColor ?? AppTheme.ColorPickerDefaultColor;
        if (!hasAlpha) seed = Color.FromArgb(0xFF, seed.R, seed.G, seed.B);
        _currentColor = seed;
        _baselineColor = seed;

        Color fallback = defaultColor ?? seed;
        if (!hasAlpha) fallback = Color.FromArgb(0xFF, fallback.R, fallback.G, fallback.B);
        _defaultColor = fallback;

        Title = string.IsNullOrWhiteSpace(title) ? strings.DefaultTitle : title;
        Width = 408;
        MinWidth = 200;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.None;
        Background = TrayAppDotNETSettingsUI.Brush(palette.Background);
        Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        FontFamily = TrayAppDotNETSettingsUI.UIFont;

        _svPicker = new TrayAppDotNETSaturationValuePicker(palette)
        {
            Width = PickerPlaneWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _hueSlider = new TrayAppDotNETColorSlider(TrayAppDotNETColorSliderKind.Hue, palette)
        {
            Width = 32,
            Minimum = 0,
            Maximum = 360,
            SmallChange = 1,
            LargeChange = 30,
        };
        _alphaSlider = new TrayAppDotNETColorSlider(TrayAppDotNETColorSliderKind.Alpha, palette)
        {
            Width = 32,
            Minimum = 0,
            Maximum = 255,
            SmallChange = 1,
            LargeChange = 16,
            IsDirectionReversed = true,
            IsEnabled = hasAlpha,
        };
        _rSlider = CreateChannelSlider();
        _gSlider = CreateChannelSlider();
        _bSlider = CreateChannelSlider();
        _rValueLabel = ChannelValueLabel("0");
        _gValueLabel = ChannelValueLabel("0");
        _bValueLabel = ChannelValueLabel("0");
        _rgbaBox = HexBox();
        _argbBox = HexBox();

        _notifyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.ColorPickerChangeCooldownMs),
        };
        _notifyTimer.Tick += OnNotifyTimerTick;

        Content = BuildContent(Title);
        WireEvents();

        RefreshHueFromColor();
        SyncControlsFromColor();

        Closed += (_, _) =>
        {
            _closed = true;
            _notifyTimer.Stop();
            _pendingNotification = null;
        };
    }

    public event EventHandler<Color>? ColorChanged;

    public Color CurrentColor => _currentColor;

    public bool IsDirty => _currentColor != _baselineColor;

    private Border BuildContent(string title)
    {
        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(new GridLength(32)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Control titleBar = BuildTitleBar(title);
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        Grid body = BuildBody();
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Border),
            BorderThickness = new Thickness(1),
            Child = root,
        };
    }

    private Grid BuildTitleBar(string title)
    {
        Grid titleBar = new() { Background = Brushes.Transparent, Height = 32 };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        titleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e);
        };

        TextBlock titleText = TrayAppDotNETSettingsUI.Text(title, _palette);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(titleText, 0);
        titleBar.Children.Add(titleText);

        TrayAppDotNETCaptionCloseButton close = new(_palette);
        TrayAppDotNETToolTip.SetTip(close, _strings.CloseTooltip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(close);
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);

        return titleBar;
    }

    private Grid BuildBody()
    {
        Grid body = new() { Margin = new Thickness(20, 12, 20, 16), };
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(new GridLength(16)));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid pickerGrid = BuildPickerGrid();
        Grid.SetRow(pickerGrid, 0);
        body.Children.Add(pickerGrid);

        Grid footer = BuildFooterGrid();
        Grid.SetRow(footer, 2);
        body.Children.Add(footer);

        return body;
    }

    private Grid BuildPickerGrid()
    {
        Grid grid = SharedPickerColumns();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(ChannelBandHeight)));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid.SetRow(_rValueLabel, 0);
        Grid.SetColumn(_rValueLabel, 6);
        grid.Children.Add(_rValueLabel);
        Grid.SetRow(_gValueLabel, 0);
        Grid.SetColumn(_gValueLabel, 8);
        grid.Children.Add(_gValueLabel);
        Grid.SetRow(_bValueLabel, 0);
        Grid.SetColumn(_bValueLabel, 10);
        grid.Children.Add(_bValueLabel);

        Grid.SetRow(_svPicker, 0);
        Grid.SetRowSpan(_svPicker, 2);
        Grid.SetColumn(_svPicker, 0);
        grid.Children.Add(_svPicker);

        AddSlider(grid, _hueSlider, 2, spanValueRow: true);
        AddSlider(grid, _alphaSlider, 4, spanValueRow: true);
        AddSlider(grid, _rSlider, 6, spanValueRow: false);
        AddSlider(grid, _gSlider, 8, spanValueRow: false);
        AddSlider(grid, _bSlider, 10, spanValueRow: false);

        AddFooterLabel(grid, _strings.HueLabel, 2);
        AddFooterLabel(grid, _strings.AlphaLabel, 4);
        AddFooterLabel(grid, _strings.RedLabel, 6);
        AddFooterLabel(grid, _strings.GreenLabel, 8);
        AddFooterLabel(grid, _strings.BlueLabel, 10);

        return grid;
    }

    private Grid BuildFooterGrid()
    {
        Grid grid = SharedPickerColumns();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(6)));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Margin = new Thickness(0, -34, 0, 0);

        Grid rgbaRow = HexRow(_strings.RgbaHexLabel, _rgbaBox);
        Grid.SetRow(rgbaRow, 0);
        Grid.SetColumn(rgbaRow, 0);
        grid.Children.Add(rgbaRow);

        Grid argbRow = HexRow(_strings.ArgbHexLabel, _argbBox);
        Grid.SetRow(argbRow, 2);
        Grid.SetColumn(argbRow, 0);
        grid.Children.Add(argbRow);

        Grid buttons = new() { VerticalAlignment = VerticalAlignment.Center, };
        buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        buttons.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(8)));
        buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        SettingsButton defaultButton = TrayAppDotNETSettingsUI.Button(_strings.DefaultButton, _palette);
        SettingsButton resetButton = TrayAppDotNETSettingsUI.Button(_strings.ResetButton, _palette);
        defaultButton.Padding = new Thickness(20, 8);
        resetButton.Padding = new Thickness(20, 8);
        defaultButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        resetButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        defaultButton.Click += (_, _) => ApplyColor(_defaultColor, ColorApplySource.None, force: true);
        resetButton.Click += (_, _) => ApplyColor(_baselineColor, ColorApplySource.None, force: true);

        Grid.SetColumn(defaultButton, 0);
        buttons.Children.Add(defaultButton);
        Grid.SetColumn(resetButton, 2);
        buttons.Children.Add(resetButton);

        Grid.SetRow(buttons, 2);
        Grid.SetColumn(buttons, 2);
        Grid.SetColumnSpan(buttons, 9);
        grid.Children.Add(buttons);

        return grid;
    }

    private static Grid SharedPickerColumns()
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(PickerPlaneWidth)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(16)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(8)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(14)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(14)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(14)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        return grid;
    }

    private static void AddSlider(Grid grid, Control slider, int column, bool spanValueRow)
    {
        Grid.SetRow(slider, spanValueRow ? 0 : 1);
        if (spanValueRow) Grid.SetRowSpan(slider, 2);
        Grid.SetColumn(slider, column);
        slider.VerticalAlignment = VerticalAlignment.Stretch;
        grid.Children.Add(slider);
    }

    private void AddFooterLabel(Grid grid, string text, int column)
    {
        TextBlock label = TrayAppDotNETSettingsUI.Text(text, _palette, 14, FontWeight.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(label, 2);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private TextBlock ChannelValueLabel(string text)
    {
        TextBlock label = TrayAppDotNETSettingsUI.Text(text, _palette, 14, FontWeight.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        label.Width = 22;
        label.Margin = new Thickness(0, 0, 0, 4);
        return label;
    }

    private TextBox HexBox()
    {
        TextBox box = TrayAppDotNETSettingsUI.TextBox(_palette, 94);
        box.FontFamily = new FontFamily("Consolas, Courier New");
        return box;
    }

    private Grid HexRow(string labelText, TextBox box)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(8)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock label = TrayAppDotNETSettingsUI.Text(labelText, _palette, 14, FontWeight.SemiBold);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        Grid.SetColumn(box, 2);
        row.Children.Add(box);
        return row;
    }

    private TrayAppDotNETColorSlider CreateChannelSlider() =>
        new(TrayAppDotNETColorSliderKind.Channel, _palette)
        {
            Width = 22,
            Minimum = 0,
            Maximum = 255,
            SmallChange = 1,
            LargeChange = 16,
        };

    private void WireEvents()
    {
        _svPicker.SelectionChanged += (_, e) =>
        {
            Color rgb = HsvToRgb(_freePickHue, e.Saturation, e.Value);
            ApplyColor(Color.FromArgb(_currentColor.A, rgb.R, rgb.G, rgb.B), ColorApplySource.SaturationValue);
        };

        _hueSlider.ValueChanged += (_, value) =>
        {
            if (_suppressSlider) return;
            _freePickHue = Math.Clamp(value, 0, 360);
            (double _, double sat, double val) = RgbToHsv(_currentColor.R, _currentColor.G, _currentColor.B);
            Color rgb = HsvToRgb(_freePickHue, sat, val);
            ApplyColor(Color.FromArgb(_currentColor.A, rgb.R, rgb.G, rgb.B), ColorApplySource.Hue, force: true);
        };
        _alphaSlider.ValueChanged += (_, value) =>
        {
            if (_suppressSlider) return;
            byte channel = ToByte(value);
            ApplyColor(Color.FromArgb(channel, _currentColor.R, _currentColor.G, _currentColor.B),
                ColorApplySource.Alpha);
        };
        _rSlider.ValueChanged += (_, value) =>
        {
            if (_suppressSlider) return;
            byte channel = ToByte(value);
            ApplyColor(Color.FromArgb(_currentColor.A, channel, _currentColor.G, _currentColor.B),
                ColorApplySource.Red);
        };
        _gSlider.ValueChanged += (_, value) =>
        {
            if (_suppressSlider) return;
            byte channel = ToByte(value);
            ApplyColor(Color.FromArgb(_currentColor.A, _currentColor.R, channel, _currentColor.B),
                ColorApplySource.Green);
        };
        _bSlider.ValueChanged += (_, value) =>
        {
            if (_suppressSlider) return;
            byte channel = ToByte(value);
            ApplyColor(Color.FromArgb(_currentColor.A, _currentColor.R, _currentColor.G, channel),
                ColorApplySource.Blue);
        };

        _rgbaBox.TextChanged += (_, _) =>
        {
            if (_suppressRgba) return;
            if (!TryParseHex(_rgbaBox.Text, argbOrder: false, out Color parsed)) return;
            if (!_hasAlpha) parsed = Color.FromArgb(0xFF, parsed.R, parsed.G, parsed.B);
            ApplyColor(parsed, ColorApplySource.RgbaText);
        };
        _argbBox.TextChanged += (_, _) =>
        {
            if (_suppressArgb) return;
            if (!TryParseHex(_argbBox.Text, argbOrder: true, out Color parsed)) return;
            if (!_hasAlpha) parsed = Color.FromArgb(0xFF, parsed.R, parsed.G, parsed.B);
            ApplyColor(parsed, ColorApplySource.ArgbText);
        };
    }

    private void ApplyColor(Color color, ColorApplySource source, bool force = false)
    {
        if (!_hasAlpha)
            color = Color.FromArgb(0xFF, color.R, color.G, color.B);
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

        _svPicker.SetState(HsvToRgb(_freePickHue, 1.0, 1.0), sat, val, _currentColor);

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

    private static string FormatArgb(Color color) => $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string FormatRgba(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));

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
                    0xFF,
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));
                return true;
            }

            byte b0 = Convert.ToByte(hex[..2], 16);
            byte b1 = Convert.ToByte(hex[2..4], 16);
            byte b2 = Convert.ToByte(hex[4..6], 16);
            byte b3 = Convert.ToByte(hex[6..8], 16);
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
        sat = Math.Clamp(sat, 0, 1);
        val = Math.Clamp(val, 0, 1);
        if (sat <= 0)
        {
            byte gray = (byte)Math.Round(val * 255);
            return Color.FromArgb(0xFF, gray, gray, gray);
        }

        double h = ((hue % 360) + 360) % 360 / 60.0;
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
            _ => (val, p, q),
        };

        return Color.FromArgb(
            0xFF,
            (byte)Math.Round(Math.Clamp(r, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(g, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(b, 0, 1) * 255));
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
            if (max == rd) hue = 60.0 * (((gd - bd) / delta) % 6);
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
        ArgbText,
    }
}

internal sealed class TrayAppDotNETSaturationValuePicker : Control
{
    private readonly SettingsPalette _palette;
    private bool _dragging;
    private Color _hueColor = AppTheme.ColorPickerHueRed;
    private Color _currentColor = AppTheme.ColorPickerHueRed;
    private double _saturation;
    private double _value = 1;

    public TrayAppDotNETSaturationValuePicker(SettingsPalette palette)
    {
        _palette = palette;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public event EventHandler<TrayAppDotNETSaturationValueChangedEventArgs>? SelectionChanged;

    public void SetState(Color hueColor, double saturation, double value, Color currentColor)
    {
        _hueColor = hueColor;
        _saturation = Math.Clamp(saturation, 0, 1);
        _value = Math.Clamp(value, 0, 1);
        _currentColor = currentColor;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        context.FillRectangle(new SolidColorBrush(_hueColor), bounds, 6);
        context.FillRectangle(
            CreateGradient(AppTheme.ColorPickerWhite, AppTheme.ColorPickerTransparentWhite, horizontal: true),
            bounds,
            6);
        context.FillRectangle(
            CreateGradient(AppTheme.ColorPickerTransparentBlack, AppTheme.ColorPickerBlack, horizontal: false),
            bounds,
            6);
        context.DrawRectangle(new Pen(new SolidColorBrush(_palette.Border)), bounds, 6);

        double x = Math.Clamp(_saturation * bounds.Width, 0, bounds.Width);
        double y = Math.Clamp((1 - _value) * bounds.Height, 0, bounds.Height);
        Point center = new(x, y);
        context.DrawEllipse(
            Brushes.Transparent,
            new Pen(new SolidColorBrush(AppTheme.ColorPickerBlack)),
            center,
            8,
            8);
        context.DrawEllipse(
            new SolidColorBrush(_currentColor),
            new Pen(new SolidColorBrush(AppTheme.ColorPickerWhite)),
            center,
            7,
            7);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        _dragging = true;
        e.Pointer.Capture(this);
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

    private void StopDragging(PointerEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void UpdateFromPoint(Point point)
    {
        double width = Math.Max(Bounds.Width, 1);
        double height = Math.Max(Bounds.Height, 1);
        _saturation = Math.Clamp(point.X / width, 0, 1);
        _value = Math.Clamp(1 - point.Y / height, 0, 1);
        InvalidateVisual();
        SelectionChanged?.Invoke(this, new TrayAppDotNETSaturationValueChangedEventArgs(_saturation, _value));
    }

    private static LinearGradientBrush CreateGradient(Color start, Color end, bool horizontal) =>
        new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = horizontal
                ? new RelativePoint(1, 0, RelativeUnit.Relative)
                : new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(start, 0), new GradientStop(end, 1), },
        };
}

internal sealed record TrayAppDotNETSaturationValueChangedEventArgs(double Saturation, double Value);

internal enum TrayAppDotNETColorSliderKind
{
    Channel,
    Hue,
    Alpha,
}

internal sealed class TrayAppDotNETColorSlider : Control
{
    private readonly SettingsPalette _palette;
    private readonly TrayAppDotNETColorSliderKind _kind;
    private bool _dragging;
    private double _value;

    public TrayAppDotNETColorSlider(TrayAppDotNETColorSliderKind kind, SettingsPalette palette)
    {
        _kind = kind;
        _palette = palette;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
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
        e.Pointer.Capture(this);
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
            _ => null,
        };
        if (!next.HasValue) return;
        Value = next.Value;
        e.Handled = true;
    }

    private void StopDragging(PointerEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
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
        Rect track = new(x, 0, 4, bounds.Height);
        context.FillRectangle(new SolidColorBrush(_palette.SliderTrack), track, 2);

        double y = ThumbCenterY(bounds);
        Rect progress = new(x, y, 4, Math.Max(0, bounds.Height - y));
        context.FillRectangle(new SolidColorBrush(_palette.SliderProgress), progress, 2);

        Rect thumb = new(bounds.Center.X - 11, y - 5, 22, 10);
        context.FillRectangle(new SolidColorBrush(_palette.SliderThumb), thumb, 5);
    }

    private void RenderGradient(DrawingContext context, Rect bounds)
    {
        Rect bar = new(0.5, 0.5, bounds.Width - 1, bounds.Height - 1);
        context.FillRectangle(CreateBarGradient(), bar, 4);
        context.DrawRectangle(new Pen(new SolidColorBrush(_palette.Border)), bar, 4);

        double thumbY = ThumbCenterY(bounds);
        Rect thumb = new(1, thumbY - 4, Math.Max(0, bounds.Width - 2), 8);
        Color fill = _kind == TrayAppDotNETColorSliderKind.Hue
            ? TrayAppDotNETColorPickerWindow.HsvToRgb(Value, 1.0, 1.0)
            : CurrentColor;
        context.FillRectangle(new SolidColorBrush(fill), thumb, 2);
        context.DrawRectangle(new Pen(new SolidColorBrush(ThumbBorderColor())), thumb, 2);
    }

    private LinearGradientBrush CreateBarGradient()
    {
        LinearGradientBrush brush = new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        };

        if (_kind == TrayAppDotNETColorSliderKind.Hue)
        {
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueRed, 0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueMagenta, 1.0 / 6.0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueBlue, 2.0 / 6.0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueCyan, 3.0 / 6.0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueLime, 4.0 / 6.0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueYellow, 5.0 / 6.0));
            brush.GradientStops.Add(new GradientStop(AppTheme.ColorPickerHueRed, 1));
            return brush;
        }

        Color top = Color.FromArgb(ToByte(ValueFromY(0)), CurrentColor.R, CurrentColor.G, CurrentColor.B);
        Color bottom = Color.FromArgb(ToByte(ValueFromY(Bounds.Height)), CurrentColor.R, CurrentColor.G,
            CurrentColor.B);
        brush.GradientStops.Add(new GradientStop(top, 0));
        brush.GradientStops.Add(new GradientStop(bottom, 1));
        return brush;
    }

    private double ValueFromY(double y)
    {
        double height = Math.Max(Bounds.Height, 1);
        double normalized = Math.Clamp(y / height, 0, 1);
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
        return Math.Clamp(normalized * bounds.Height, 0, bounds.Height);
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

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));
}

internal sealed class TrayAppDotNETCaptionCloseButton : Border
{
    private readonly SettingsPalette _palette;
    private readonly TextBlock _glyph;
    private bool _isPointerOver;
    private bool _isPressed;

    public TrayAppDotNETCaptionCloseButton(SettingsPalette palette)
    {
        _palette = palette;
        Width = 46;
        Height = 32;
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;

        _glyph = new TextBlock
        {
            Text = GlyphCatalog.CHROME_CLOSE,
            FontFamily = TrayAppDotNETSettingsUI.IconFont,
            FontSize = 10,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _glyph;

        PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            UpdateVisual();
        };
        PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            _isPressed = false;
            UpdateVisual();
        };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _isPressed = true;
            UpdateVisual();
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            bool clicked = _isPressed;
            _isPressed = false;
            UpdateVisual();
            if (clicked)
            {
                Click?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            Click?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
    }

    public event EventHandler? Click;

    private void UpdateVisual()
    {
        if (_isPressed)
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CloseButtonPressed);
        else if (_isPointerOver)
            Background = TrayAppDotNETSettingsUI.Brush(_palette.CloseButtonHover);
        else
            Background = Brushes.Transparent;

        _glyph.Foreground = TrayAppDotNETSettingsUI.Brush(
            _isPointerOver || _isPressed ? _palette.CloseButtonGlyphActive : _palette.Foreground);
    }
}
