using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace GlyphOpticalCenter;

internal static class Program
{
    [STAThread]
    public static void Main(string[] arguments) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments);

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<GlyphOpticalCenterApp>()
            .UsePlatformDetect();
}

internal sealed class GlyphOpticalCenterApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new GlyphOpticalCenterWindow();

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class GlyphOpticalCenterWindow : Window
{
    private const double DefaultRenderFontSize = 512.0;
    private const double DefaultTargetFontSize = 11.0;
    private const double MarkerDiameter = 14.0;
    private const double CenterGuideThickness = 1.0;

    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
    private static readonly IBrush PreviewBackground = new SolidColorBrush(Color.Parse("#181818"));
    private static readonly IBrush PreviewBorder = new SolidColorBrush(Color.Parse("#505050"));
    private static readonly IBrush CenterGuideBrush = new SolidColorBrush(Color.Parse("#60A0A0A0"));
    private static readonly IBrush MarkerFill = new SolidColorBrush(Color.Parse("#E53935"));
    private static readonly IBrush MarkerBorder = Brushes.White;

    private readonly TextBox _glyphInput = new() { Text = "U+E653", Width = 120 };
    private readonly TextBox _fontFamilyInput = new()
    {
        Text = "Segoe Fluent Icons, Segoe MDL2 Assets",
        Width = 300
    };
    private readonly TextBox _renderFontSizeInput = new()
    {
        Text = DefaultRenderFontSize.ToString(InvariantCulture),
        Width = 90
    };
    private readonly TextBox _targetFontSizeInput = new()
    {
        Text = DefaultTargetFontSize.ToString(InvariantCulture),
        Width = 80
    };
    private readonly TextBlock _glyphPreview = new()
    {
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false
    };
    private readonly Canvas _inputOverlay = new()
    {
        Background = Brushes.Transparent,
        Focusable = true
    };
    private readonly Border _verticalGuide = new()
    {
        Background = CenterGuideBrush,
        Width = CenterGuideThickness,
        IsHitTestVisible = false
    };
    private readonly Border _horizontalGuide = new()
    {
        Background = CenterGuideBrush,
        Height = CenterGuideThickness,
        IsHitTestVisible = false
    };
    private readonly Border _marker = new()
    {
        Width = MarkerDiameter,
        Height = MarkerDiameter,
        CornerRadius = new CornerRadius(MarkerDiameter / 2.0),
        Background = MarkerFill,
        BorderBrush = MarkerBorder,
        BorderThickness = new Thickness(2),
        IsVisible = false,
        IsHitTestVisible = false
    };
    private readonly TextBlock _result = new()
    {
        Text = "Click the glyph's optical center.",
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        TextWrapping = TextWrapping.Wrap
    };
    private readonly Button _copyButton = new()
    {
        Content = "Copy XML",
        IsEnabled = false
    };

    private Point? _selectedCenter;
    private double _renderFontSize = DefaultRenderFontSize;
    private double _targetFontSize = DefaultTargetFontSize;
    private string _xmlTranslation = string.Empty;

    public GlyphOpticalCenterWindow()
    {
        Title = "Glyph Optical Center";
        Width = 1100;
        Height = 920;
        MinWidth = 760;
        MinHeight = 680;
        Content = BuildContent();

        _inputOverlay.Children.Add(_verticalGuide);
        _inputOverlay.Children.Add(_horizontalGuide);
        _inputOverlay.Children.Add(_marker);
        _inputOverlay.SizeChanged += (_, _) => UpdateOverlayGeometry();
        _inputOverlay.PointerPressed += OverlayPointerPressed;
        _inputOverlay.PointerMoved += OverlayPointerMoved;
        _inputOverlay.KeyDown += OverlayKeyDown;
        _copyButton.Click += CopyButtonClick;

        RenderGlyph();
    }

    private Control BuildContent()
    {
        Button renderButton = new() { Content = "Render" };
        renderButton.Click += (_, _) => RenderGlyph();

        _glyphInput.KeyDown += InputKeyDown;
        _fontFamilyInput.KeyDown += InputKeyDown;
        _renderFontSizeInput.KeyDown += InputKeyDown;
        _targetFontSizeInput.KeyDown += InputKeyDown;

        WrapPanel inputs = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            ItemHeight = double.NaN
        };
        AddInput(inputs, "Glyph", _glyphInput);
        AddInput(inputs, "Font family", _fontFamilyInput);
        AddInput(inputs, "Render size", _renderFontSizeInput);
        AddInput(inputs, "Target size", _targetFontSizeInput);
        inputs.Children.Add(renderButton);

        StackPanel header = new()
        {
            Spacing = 8,
            Children =
            {
                inputs,
                new TextBlock
                {
                    Text = "Glyph accepts a literal character, E653, U+E653, 0xE653, \\uE653, or &#xE653;. " +
                           "Click or drag the red marker; arrow keys move it by one render unit, Shift+arrow by 0.1."
                }
            }
        };

        Grid preview = new();
        preview.Children.Add(_glyphPreview);
        preview.Children.Add(_inputOverlay);

        Border previewFrame = new()
        {
            Background = PreviewBackground,
            BorderBrush = PreviewBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = preview
        };

        Grid resultRow = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        resultRow.Children.Add(_result);
        Grid.SetColumn(_copyButton, 1);
        resultRow.Children.Add(_copyButton);

        Grid root = new()
        {
            Margin = new Thickness(16),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 12
        };
        root.Children.Add(header);
        Grid.SetRow(previewFrame, 1);
        root.Children.Add(previewFrame);
        Grid.SetRow(resultRow, 2);
        root.Children.Add(resultRow);
        return root;
    }

    private static void AddInput(Panel panel, string label, Control input)
    {
        StackPanel group = new()
        {
            Margin = new Thickness(0, 0, 12, 0),
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = label },
                input
            }
        };
        panel.Children.Add(group);
    }

    private void InputKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        if (eventArguments.Key != Key.Enter) return;

        RenderGlyph();
        eventArguments.Handled = true;
    }

    private void RenderGlyph()
    {
        if (!TryParseGlyph(_glyphInput.Text, out string glyph))
        {
            _result.Text = "Enter one glyph or Unicode code point.";
            return;
        }

        if (!double.TryParse(_renderFontSizeInput.Text, NumberStyles.Float, InvariantCulture,
                out _renderFontSize) || _renderFontSize <= 0)
        {
            _result.Text = "Render size must be greater than zero.";
            return;
        }

        if (!double.TryParse(_targetFontSizeInput.Text, NumberStyles.Float, InvariantCulture,
                out _targetFontSize) || _targetFontSize <= 0)
        {
            _result.Text = "Target size must be greater than zero.";
            return;
        }

        string fontFamily = string.IsNullOrWhiteSpace(_fontFamilyInput.Text)
            ? "Segoe Fluent Icons, Segoe MDL2 Assets"
            : _fontFamilyInput.Text.Trim();

        _glyphPreview.Text = glyph;
        _glyphPreview.FontFamily = new FontFamily(fontFamily);
        _glyphPreview.FontSize = _renderFontSize;
        _glyphPreview.FontWeight = FontWeight.Normal;
        TextOptions.SetTextRenderingMode(_glyphPreview, TextRenderingMode.Antialias);
        TextOptions.SetTextHintingMode(_glyphPreview, TextHintingMode.Light);
        TextOptions.SetBaselinePixelAlignment(_glyphPreview, BaselinePixelAlignment.Unaligned);

        _selectedCenter = null;
        _marker.IsVisible = false;
        _copyButton.IsEnabled = false;
        _xmlTranslation = string.Empty;
        _result.Text = "Click the glyph's optical center.";
    }

    private void OverlayPointerPressed(object? sender, PointerPressedEventArgs eventArguments)
    {
        PointerPoint pointerPoint = eventArguments.GetCurrentPoint(_inputOverlay);
        if (!pointerPoint.Properties.IsLeftButtonPressed) return;

        _inputOverlay.Focus();
        SetSelectedCenter(pointerPoint.Position);
        eventArguments.Pointer.Capture(_inputOverlay);
        eventArguments.Handled = true;
    }

    private void OverlayPointerMoved(object? sender, PointerEventArgs eventArguments)
    {
        PointerPoint pointerPoint = eventArguments.GetCurrentPoint(_inputOverlay);
        if (!pointerPoint.Properties.IsLeftButtonPressed) return;

        SetSelectedCenter(pointerPoint.Position);
        eventArguments.Handled = true;
    }

    private void OverlayKeyDown(object? sender, KeyEventArgs eventArguments)
    {
        if (!_selectedCenter.HasValue) return;

        double increment = eventArguments.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1 : 1.0;
        Vector movement = eventArguments.Key switch
        {
            Key.Left => new Vector(-increment, 0),
            Key.Right => new Vector(increment, 0),
            Key.Up => new Vector(0, -increment),
            Key.Down => new Vector(0, increment),
            _ => default
        };
        if (movement == default) return;

        Point selectedCenter = _selectedCenter.Value + movement;
        SetSelectedCenter(selectedCenter);
        eventArguments.Handled = true;
    }

    private void SetSelectedCenter(Point position)
    {
        double selectedX = Math.Clamp(position.X, 0, _inputOverlay.Bounds.Width);
        double selectedY = Math.Clamp(position.Y, 0, _inputOverlay.Bounds.Height);
        _selectedCenter = new Point(selectedX, selectedY);
        UpdateOverlayGeometry();
        UpdateResult();
    }

    private void UpdateOverlayGeometry()
    {
        double width = _inputOverlay.Bounds.Width;
        double height = _inputOverlay.Bounds.Height;
        if (width <= 0 || height <= 0) return;

        _verticalGuide.Height = height;
        Canvas.SetLeft(_verticalGuide, (width - CenterGuideThickness) / 2.0);
        Canvas.SetTop(_verticalGuide, 0);

        _horizontalGuide.Width = width;
        Canvas.SetLeft(_horizontalGuide, 0);
        Canvas.SetTop(_horizontalGuide, (height - CenterGuideThickness) / 2.0);

        if (!_selectedCenter.HasValue) return;

        Point selectedCenter = _selectedCenter.Value;
        Canvas.SetLeft(_marker, selectedCenter.X - MarkerDiameter / 2.0);
        Canvas.SetTop(_marker, selectedCenter.Y - MarkerDiameter / 2.0);
        _marker.IsVisible = true;
        UpdateResult();
    }

    private void UpdateResult()
    {
        if (!_selectedCenter.HasValue || _inputOverlay.Bounds.Width <= 0 || _inputOverlay.Bounds.Height <= 0)
            return;

        Point layoutCenter = new(_inputOverlay.Bounds.Width / 2.0, _inputOverlay.Bounds.Height / 2.0);
        Point selectedCenter = _selectedCenter.Value;
        double renderTranslateX = layoutCenter.X - selectedCenter.X;
        double renderTranslateY = layoutCenter.Y - selectedCenter.Y;
        double targetScale = _targetFontSize / _renderFontSize;
        double targetTranslateX = renderTranslateX * targetScale;
        double targetTranslateY = renderTranslateY * targetScale;

        string translateX = FormatNumber(targetTranslateX);
        string translateY = FormatNumber(targetTranslateY);
        _xmlTranslation = $"TranslateX=\"{translateX}\" TranslateY=\"{translateY}\"";
        _result.Text =
            $"Clicked center: {FormatPoint(selectedCenter)}    Layout center: {FormatPoint(layoutCenter)}\n" +
            $"Render-size correction: ({FormatNumber(renderTranslateX)}, {FormatNumber(renderTranslateY)})\n" +
            $"Target correction at font size {FormatNumber(_targetFontSize)}:  {_xmlTranslation}";
        _copyButton.IsEnabled = true;
    }

    private async void CopyButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArguments)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null || string.IsNullOrEmpty(_xmlTranslation)) return;

        await topLevel.Clipboard.SetTextAsync(_xmlTranslation);
        await topLevel.Clipboard.FlushAsync();
    }

    private static bool TryParseGlyph(string? input, out string glyph)
    {
        glyph = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string candidate = input.Trim();
        string codePointText = candidate;
        if (candidate.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            codePointText = candidate[2..];
        else if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            codePointText = candidate[2..];
        else if (candidate.StartsWith("\\u", StringComparison.OrdinalIgnoreCase))
            codePointText = candidate[2..];
        else if (candidate.StartsWith("&#x", StringComparison.OrdinalIgnoreCase) && candidate.EndsWith(';'))
            codePointText = candidate[3..^1];
        else if (!IsHexCodePoint(candidate))
        {
            glyph = candidate;
            return true;
        }

        if (!int.TryParse(codePointText, NumberStyles.AllowHexSpecifier, InvariantCulture, out int codePoint) ||
            !Rune.IsValid(codePoint))
            return false;

        glyph = new Rune(codePoint).ToString();
        return true;
    }

    private static bool IsHexCodePoint(string candidate)
    {
        if (candidate.Length is < 4 or > 6) return false;

        foreach (char character in candidate)
        {
            if (!Uri.IsHexDigit(character)) return false;
        }

        return true;
    }

    private static string FormatPoint(Point point) =>
        $"({FormatNumber(point.X)}, {FormatNumber(point.Y)})";

    private static string FormatNumber(double value)
    {
        double normalizedValue = Math.Abs(value) < 0.00005 ? 0.0 : value;
        return normalizedValue.ToString("0.####", InvariantCulture);
    }
}
