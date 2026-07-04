using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class SettingsUILayout
{
    private static readonly ControlAXAMLResources R = new(
        "avares://TrayAppDotNETCommon/UI/Controls/SettingsUI.axaml",
        "SettingsUI");

    public static Thickness NavItemMargin => R.Thickness("NavItemMargin");
    public static Thickness NavActionMargin => R.Thickness("NavActionMargin");
    public static double NavIndicatorWidth => R.Double("NavIndicatorWidth");
    public static double NavIndicatorHeight => R.Double("NavIndicatorHeight");
    public static CornerRadius NavIndicatorRadius => R.CornerRadius("NavIndicatorRadius");
    public static Thickness NavIndicatorMargin => R.Thickness("NavIndicatorMargin");
    public static CornerRadius NavItemRadius => R.CornerRadius("NavItemRadius");
    public static Thickness NavItemPadding => R.Thickness("NavItemPadding");
    public static Thickness NavItemInnerMargin => R.Thickness("NavItemInnerMargin");
    public static CornerRadius ButtonRadius => R.CornerRadius("ButtonRadius");
    public static double ButtonMinHeight => R.Double("ButtonMinHeight");
    public static Thickness ButtonPadding => R.Thickness("ButtonPadding");
    public static double EnabledOpacity => R.Double("EnabledOpacity");
    public static double DisabledOpacity => R.Double("DisabledOpacity");
    public static double ControlDisabledOpacity => R.Double("ControlDisabledOpacity");
    public static double ToggleWidth => R.Double("ToggleWidth");
    public static double ToggleHeight => R.Double("ToggleHeight");
    public static CornerRadius ToggleTrackRadius => R.CornerRadius("ToggleTrackRadius");
    public static Thickness ToggleBorderThickness => R.Thickness("ToggleBorderThickness");
    public static double ToggleThumbWidth => R.Double("ToggleThumbWidth");
    public static double ToggleThumbHeight => R.Double("ToggleThumbHeight");
    public static double ToggleThumbHoverSize => R.Double("ToggleThumbHoverSize");
    public static double ToggleThumbCheckedSize => R.Double("ToggleThumbCheckedSize");
    public static CornerRadius ToggleThumbRadius => R.CornerRadius("ToggleThumbRadius");
    public static Thickness ToggleThumbUncheckedMargin => R.Thickness("ToggleThumbUncheckedMargin");
    public static Thickness ToggleThumbCheckedMargin => R.Thickness("ToggleThumbCheckedMargin");
    public static double SwatchWidth => R.Double("SwatchWidth");
    public static double SwatchHeight => R.Double("SwatchHeight");
    public static CornerRadius SwatchRadius => R.CornerRadius("SwatchRadius");
    public static Thickness SwatchBorderThickness => R.Thickness("SwatchBorderThickness");
    public static Thickness SwatchMargin => R.Thickness("SwatchMargin");
    public static double SwatchFallbackOpacity => R.Double("SwatchFallbackOpacity");
    public static double ScrollWheelStep => R.Double("ScrollWheelStep");
    public static double ScrollBarTotalWidth => R.Double("ScrollBarTotalWidth");
    public static double ScrollBarCollapsedTrackWidth => R.Double("ScrollBarCollapsedTrackWidth");
    public static double ScrollBarThumbMargin => R.Double("ScrollBarThumbMargin");
    public static double ScrollBarMinThumbHeight => R.Double("ScrollBarMinThumbHeight");
    public static Thickness ComboItemPadding => R.Thickness("ComboItemPadding");
    public static double ComboIndicatorWidth => R.Double("ComboIndicatorWidth");
    public static double ComboIndicatorHeight => R.Double("ComboIndicatorHeight");
    public static CornerRadius ComboIndicatorRadius => R.CornerRadius("ComboIndicatorRadius");
    public static double ComboIndicatorColumnWidth => R.Double("ComboIndicatorColumnWidth");
    public static double ComboIndicatorGapWidth => R.Double("ComboIndicatorGapWidth");
    public static CornerRadius ComboItemRadius => R.CornerRadius("ComboItemRadius");
    public static Thickness ComboItemInnerPadding => R.Thickness("ComboItemInnerPadding");
    public static double ComboArrowColumnWidth => R.Double("ComboArrowColumnWidth");
    public static double ComboDefaultMinWidth => R.Double("ComboDefaultMinWidth");
    public static double ComboPopupMaxHeight => R.Double("ComboPopupMaxHeight");
    public static double ComboAutoSizeExtraPadding => R.Double("ComboAutoSizeExtraPadding");
    public static Thickness ComboContentPadding => R.Thickness("ComboContentPadding");
    public static double ComboHeight => R.Double("ComboHeight");
    public static Thickness ComboBorderThickness => R.Thickness("ComboBorderThickness");
    public static CornerRadius ComboRadius => R.CornerRadius("ComboRadius");
    public static Thickness ComboPopupScrollPadding => R.Thickness("ComboPopupScrollPadding");
    public static Thickness ComboPopupBorderThickness => R.Thickness("ComboPopupBorderThickness");
    public static CornerRadius ComboPopupRadius => R.CornerRadius("ComboPopupRadius");
    public static Thickness ComboPopupPadding => R.Thickness("ComboPopupPadding");
    public static Thickness ComboPopupMargin => R.Thickness("ComboPopupMargin");
    public static double NumberBoxHeight => R.Double("NumberBoxHeight");
    public static double NumberBoxSpinnerColumnWidth => R.Double("NumberBoxSpinnerColumnWidth");
    public static Thickness NumberTextBorderThickness => R.Thickness("NumberTextBorderThickness");
    public static double NumberTextFontSize => R.Double("NumberTextFontSize");
    public static Thickness NumberTextPadding => R.Thickness("NumberTextPadding");
    public static Thickness NumberSuffixMargin => R.Thickness("NumberSuffixMargin");
    public static CornerRadius NumberValueRadius => R.CornerRadius("NumberValueRadius");
    public static double NumberSuffixPlaceholderOpacity => R.Double("NumberSuffixPlaceholderOpacity");
    public static double NumberSuffixFontSize => R.Double("NumberSuffixFontSize");
    public static double NumberValueFontSize => R.Double("NumberValueFontSize");
    public static double NumberAutoWidthReserve => R.Double("NumberAutoWidthReserve");
    public static CornerRadius SpinnerButtonRadius => R.CornerRadius("SpinnerButtonRadius");
    public static double SpinnerGlyphFontSize => R.Double("SpinnerGlyphFontSize");
    public static double SectionHeaderFontSize => R.Double("SectionHeaderFontSize");
    public static Thickness SectionHeaderMargin => R.Thickness("SectionHeaderMargin");
    public static double SubsectionHeaderFontSize => R.Double("SubsectionHeaderFontSize");
    public static Thickness SubsectionHeaderMargin => R.Thickness("SubsectionHeaderMargin");
    public static double TitleFontSize => R.Double("TitleFontSize");
    public static double DescriptionFontSize => R.Double("DescriptionFontSize");
    public static double DescriptionOpacity => R.Double("DescriptionOpacity");
    public static Thickness DescriptionMargin => R.Thickness("DescriptionMargin");
    public static Thickness CardRightControlMargin => R.Thickness("CardRightControlMargin");
    public static CornerRadius CardRadius => R.CornerRadius("CardRadius");
    public static Thickness CardPadding => R.Thickness("CardPadding");
    public static Thickness CardMargin => R.Thickness("CardMargin");
    public static double TextBoxHeight => R.Double("TextBoxHeight");
    public static double TextBoxFontSize => R.Double("TextBoxFontSize");
    public static Thickness TextBoxBorderThickness => R.Thickness("TextBoxBorderThickness");
    public static Thickness TextBoxPadding => R.Thickness("TextBoxPadding");
    public static double CaptionGlyphFontSize => R.Double("CaptionGlyphFontSize");
}

public readonly record struct SettingsPalette(
    Color Background,
    Color Foreground,
    Color Border,
    Color Hover,
    Color Pressed,
    Color CardBackground,
    Color ControlBackground,
    Color SecondaryForeground,
    Color DisabledForeground,
    Color Accent,
    Color ToggleOnTrack,
    Color ToggleOnThumb,
    Color TextBoxFocused,
    Color SearchListItemSelected,
    Color SearchListItemHover,
    Color SliderProgress,
    Color SliderTrack,
    Color SliderThumb,
    Color CloseButtonHover,
    Color CloseButtonPressed,
    Color CloseButtonGlyphActive)
{
    public Color Separator => Border;
    public Color ControlBorder => Border;
    public Color ButtonHover => Hover;
    public Color ButtonPressed => Pressed;
    public Color IconForeground => Foreground;
    public Color FooterBackground => Background;
}

public sealed class SettingsNavItem : Border
{
    private readonly SettingsPalette _palette;
    private readonly Border _outer;
    private readonly Border _indicator;
    private bool _isPointerOver;
    private bool _isSelected;

    public SettingsNavItem(
        string text,
        SettingsPalette palette,
        CornerRadius? indicatorRadius = null,
        CornerRadius? itemRadius = null)
    {
        _palette = palette;
        Background = Brushes.Transparent;
        Margin = SettingsUILayout.NavItemMargin;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        _indicator = new Border
        {
            Width = SettingsUILayout.NavIndicatorWidth,
            Height = SettingsUILayout.NavIndicatorHeight,
            CornerRadius = indicatorRadius ?? SettingsUILayout.NavIndicatorRadius,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = SettingsUILayout.NavIndicatorMargin,
        };

        TextBlock label = TrayAppDotNETSettingsUI.Text(text, palette);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Left;

        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.Children.Add(_indicator);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);

        _outer = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = itemRadius ?? SettingsUILayout.NavItemRadius,
            Padding = SettingsUILayout.NavItemPadding,
            Margin = SettingsUILayout.NavItemInnerMargin,
            Child = row,
        };
        Child = _outer;

        PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            UpdateVisual();
        };
        PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            UpdateVisual();
        };
        PointerPressed += (_, e) =>
        {
            if (!IsEnabled) return;
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Click?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
        KeyDown += (_, e) =>
        {
            if (!IsEnabled) return;
            if (e.Key is Key.Enter or Key.Space)
            {
                Click?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
    }

    public event EventHandler? Click;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            UpdateVisual();
        }
    }

    private void UpdateVisual()
    {
        _outer.Background =
            TrayAppDotNETSettingsUI.Brush(_isSelected || _isPointerOver ? _palette.Hover : Colors.Transparent);
        _indicator.Background = TrayAppDotNETSettingsUI.Brush(_isSelected ? _palette.Foreground : Colors.Transparent);
    }
}

public sealed class SettingsNavAction : Border
{
    private readonly SettingsPalette _palette;
    private readonly Border _outer;
    private bool _isPointerOver;
    private bool _isPressed;

    public SettingsNavAction(
        string text,
        SettingsPalette palette,
        CornerRadius? indicatorRadius = null,
        CornerRadius? itemRadius = null)
    {
        _palette = palette;
        Background = Brushes.Transparent;
        Margin = SettingsUILayout.NavActionMargin;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        Border indicator = new()
        {
            Width = SettingsUILayout.NavIndicatorWidth,
            Height = SettingsUILayout.NavIndicatorHeight,
            CornerRadius = indicatorRadius ?? SettingsUILayout.NavIndicatorRadius,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = SettingsUILayout.NavIndicatorMargin,
        };

        TextBlock label = TrayAppDotNETSettingsUI.Text(text, palette);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Left;

        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.Children.Add(indicator);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);

        _outer = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = itemRadius ?? SettingsUILayout.NavItemRadius,
            Padding = SettingsUILayout.NavItemPadding,
            Child = row,
        };
        Child = _outer;

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
            if (!IsEnabled) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _isPressed = true;
            UpdateVisual();
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (!IsEnabled) return;
            bool clicked = _isPressed;
            _isPressed = false;
            UpdateVisual();
            if (!clicked) return;
            Click?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (!IsEnabled) return;
            if (e.Key is not (Key.Enter or Key.Space)) return;
            Click?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
    }

    public event EventHandler? Click;

    private void UpdateVisual()
    {
        Color background = _isPressed
            ? _palette.Pressed
            : _isPointerOver
                ? _palette.Hover
                : Colors.Transparent;
        _outer.Background = TrayAppDotNETSettingsUI.Brush(background);
    }
}

public sealed class SettingsButton : Border
{
    private readonly SettingsPalette _palette;
    private readonly TextBlock _label;
    private readonly bool _transparentBase;
    private bool _isPointerOver;
    private bool _isPressed;

    public SettingsButton(string text, SettingsPalette palette, bool transparentBase = false, bool navGutter = false)
    {
        _palette = palette;
        _transparentBase = transparentBase;
        _label = TrayAppDotNETSettingsUI.Text(text, palette);
        _label.HorizontalAlignment = navGutter ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;

        Background = transparentBase ? Brushes.Transparent : TrayAppDotNETSettingsUI.Brush(palette.ControlBackground);
        CornerRadius = SettingsUILayout.ButtonRadius;
        MinHeight = SettingsUILayout.ButtonMinHeight;
        Padding = SettingsUILayout.ButtonPadding;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        Child = navGutter ? CreateNavContent(_label) : _label;

        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsEnabledProperty)
                UpdateVisual();
        };
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
            if (!IsEnabled) return;
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _isPressed = true;
                UpdateVisual();
                e.Handled = true;
            }
        };
        PointerReleased += (_, e) =>
        {
            if (!IsEnabled) return;
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
            if (!IsEnabled) return;
            if (e.Key is Key.Enter or Key.Space)
            {
                Click?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
    }

    public event EventHandler? Click;

    public TextBlock Label => _label;

    public string Text
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
    }

    private void UpdateVisual()
    {
        Opacity = IsEnabled ? SettingsUILayout.EnabledOpacity : SettingsUILayout.DisabledOpacity;
        if (_isPressed)
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Pressed);
        else if (_isPointerOver)
            Background = TrayAppDotNETSettingsUI.Brush(_palette.Hover);
        else
            Background = _transparentBase
                ? Brushes.Transparent
                : TrayAppDotNETSettingsUI.Brush(_palette.ControlBackground);
    }

    private static Grid CreateNavContent(TextBlock label)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.Children.Add(new Border
        {
            Width = SettingsUILayout.NavIndicatorWidth,
            Height = SettingsUILayout.NavIndicatorHeight,
            CornerRadius = SettingsUILayout.NavIndicatorRadius,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = SettingsUILayout.NavIndicatorMargin,
        });

        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }
}

public sealed class SettingsToggle : Border
{
    private readonly SettingsPalette _palette;
    private readonly Border _track;
    private readonly Border _thumb;
    private bool _isChecked;
    private bool _isPointerOver;

    public SettingsToggle(SettingsPalette palette)
    {
        _palette = palette;
        Width = SettingsUILayout.ToggleWidth;
        Height = SettingsUILayout.ToggleHeight;
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;

        Grid grid = new()
        {
            Width = SettingsUILayout.ToggleWidth, Height = SettingsUILayout.ToggleHeight, IsHitTestVisible = false
        };
        _track = new Border
        {
            Width = SettingsUILayout.ToggleWidth,
            Height = SettingsUILayout.ToggleHeight,
            CornerRadius = SettingsUILayout.ToggleTrackRadius,
            BorderThickness = SettingsUILayout.ToggleBorderThickness,
            IsHitTestVisible = false,
        };
        _thumb = new Border
        {
            Width = SettingsUILayout.ToggleThumbWidth,
            Height = SettingsUILayout.ToggleThumbHeight,
            CornerRadius = SettingsUILayout.ToggleThumbRadius,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = SettingsUILayout.ToggleThumbUncheckedMargin,
            IsHitTestVisible = false,
        };
        grid.Children.Add(_track);
        grid.Children.Add(_thumb);
        Child = grid;

        PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            UpdateVisual();
        };
        PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            UpdateVisual();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsEnabledProperty)
                UpdateVisual();
        };
        PointerPressed += (_, e) =>
        {
            if (!IsEnabled) return;
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                IsChecked = !IsChecked;
                e.Handled = true;
            }
        };
        KeyDown += (_, e) =>
        {
            if (!IsEnabled) return;
            if (e.Key is Key.Enter or Key.Space)
            {
                IsChecked = !IsChecked;
                e.Handled = true;
            }
        };

        UpdateVisual();
    }

    public event EventHandler<bool>? CheckedChanged;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            UpdateVisual();
            CheckedChanged?.Invoke(this, value);
        }
    }

    private void UpdateVisual()
    {
        Opacity = IsEnabled ? SettingsUILayout.EnabledOpacity : SettingsUILayout.ControlDisabledOpacity;

        if (_isChecked)
        {
            _track.Background = TrayAppDotNETSettingsUI.Brush(_palette.ToggleOnTrack);
            _track.BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.ToggleOnTrack);
            _thumb.Background = TrayAppDotNETSettingsUI.Brush(_palette.ToggleOnThumb);
            _thumb.Width = _isPointerOver
                ? SettingsUILayout.ToggleThumbHoverSize
                : SettingsUILayout.ToggleThumbCheckedSize;
            _thumb.Height = _isPointerOver
                ? SettingsUILayout.ToggleThumbHoverSize
                : SettingsUILayout.ToggleThumbCheckedSize;
            _thumb.HorizontalAlignment = HorizontalAlignment.Right;
            _thumb.Margin = SettingsUILayout.ToggleThumbCheckedMargin;
            return;
        }

        _track.Background = Brushes.Transparent;
        _track.BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Foreground);
        _thumb.Background = TrayAppDotNETSettingsUI.Brush(_palette.Foreground);
        _thumb.Width = _isPointerOver ? SettingsUILayout.ToggleThumbHoverSize : SettingsUILayout.ToggleThumbWidth;
        _thumb.Height = _isPointerOver ? SettingsUILayout.ToggleThumbHoverSize : SettingsUILayout.ToggleThumbHeight;
        _thumb.HorizontalAlignment = HorizontalAlignment.Left;
        _thumb.Margin = SettingsUILayout.ToggleThumbUncheckedMargin;
    }
}

public sealed class SettingsSwatch : Border
{
    private readonly SettingsPalette _palette;
    private bool _isPointerOver;

    public SettingsSwatch(SettingsPalette palette)
    {
        _palette = palette;
        Width = SettingsUILayout.SwatchWidth;
        Height = SettingsUILayout.SwatchHeight;
        CornerRadius = SettingsUILayout.SwatchRadius;
        BorderThickness = SettingsUILayout.SwatchBorderThickness;
        BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border);
        Margin = SettingsUILayout.SwatchMargin;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;

        PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            UpdateBorder();
        };
        PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            UpdateBorder();
        };
        PointerPressed += (_, e) =>
        {
            if (!IsEnabled) return;
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Click?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
        KeyDown += (_, e) =>
        {
            if (!IsEnabled) return;
            if (e.Key is Key.Enter or Key.Space)
            {
                Click?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        };
    }

    public event EventHandler? Click;

    public void SetColor(Color? color, Color fallback)
    {
        Background = TrayAppDotNETSettingsUI.Brush(color ?? fallback);
        Opacity = color.HasValue ? SettingsUILayout.EnabledOpacity : SettingsUILayout.SwatchFallbackOpacity;
    }

    private void UpdateBorder() =>
        BorderBrush = TrayAppDotNETSettingsUI.Brush(_isPointerOver ? _palette.Accent : _palette.Border);
}

public sealed class SettingsScrollHost : Grid
{
    private readonly ScrollViewer _scrollViewer;

    public SettingsScrollHost(Control content, SettingsPalette palette, Thickness padding)
    {
        Background = TrayAppDotNETSettingsUI.Brush(palette.Background);
        ClipToBounds = true;

        Border paddedContent = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background), Padding = padding, Child = content,
        };

        _scrollViewer = new ScrollViewer
        {
            Content = paddedContent,
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        Children.Add(_scrollViewer);

        SettingsScrollBar scrollBar = new(palette)
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch,
        };
        scrollBar.Attach(_scrollViewer);
        Children.Add(scrollBar);
    }

    public double VerticalOffset => _scrollViewer.Offset.Y;

    public double ViewportHeight => _scrollViewer.Viewport.Height;

    public void SetVerticalOffset(double offset)
    {
        double maxOffset = MaxOffset;
        double next = maxOffset <= 0 ? 0 : Math.Clamp(offset, 0, maxOffset);
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, next);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        double maxOffset = MaxOffset;
        if (maxOffset <= 0)
        {
            base.OnPointerWheelChanged(e);
            return;
        }

        double next = Math.Clamp(
            _scrollViewer.Offset.Y - (e.Delta.Y * SettingsUILayout.ScrollWheelStep),
            0,
            maxOffset);
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, next);
        e.Handled = true;
    }

    private double MaxOffset => Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
}

internal sealed class SettingsScrollBar : Control
{
    private readonly SettingsPalette _palette;
    private ScrollViewer? _viewer;
    private bool _isPointerOver;
    private bool _isDragging;
    private double _dragOffset;

    public SettingsScrollBar(SettingsPalette palette)
    {
        _palette = palette;
        Width = SettingsUILayout.ScrollBarTotalWidth;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = false;
        IsHitTestVisible = true;

        PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            InvalidateVisual();
        };
        PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            InvalidateVisual();
        };
    }

    public void Attach(ScrollViewer viewer)
    {
        _viewer = viewer;
        viewer.ScrollChanged += (_, _) => InvalidateVisual();
        viewer.EffectiveViewportChanged += (_, _) => InvalidateVisual();
        viewer.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.OffsetProperty ||
                e.Property == ScrollViewer.ExtentProperty ||
                e.Property == ScrollViewer.ViewportProperty)
                InvalidateVisual();
        };
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(0, 0, Bounds.Width, Bounds.Height));
        if (_viewer == null) return;

        double maxOffset = MaxOffset;
        if (maxOffset <= 0 || Bounds.Height <= 0) return;

        Rect thumb = ThumbRect();
        double opacity = _isDragging ? 1.0 : _isPointerOver ? 0.85 : 0.55;
        Color color = Color.FromArgb((byte)Math.Round(255 * opacity), _palette.SliderProgress.R,
            _palette.SliderProgress.G, _palette.SliderProgress.B);
        double radius = IsExpanded ? 7 : 3;
        context.FillRectangle(new SolidColorBrush(color), thumb, (float)radius);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_viewer == null || MaxOffset <= 0)
        {
            base.OnPointerPressed(e);
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            base.OnPointerPressed(e);
            return;
        }

        Point position = e.GetPosition(this);
        Rect thumb = ThumbRect();
        _isDragging = true;
        _dragOffset = thumb.Contains(position) ? position.Y - thumb.Y : thumb.Height / 2;
        e.Pointer.Capture(this);
        SetOffsetFromPointer(position.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_isDragging)
        {
            SetOffsetFromPointer(e.GetPosition(this).Y);
            e.Handled = true;
            return;
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
        InvalidateVisual();
        base.OnPointerCaptureLost(e);
    }

    private bool IsExpanded => _isPointerOver || _isDragging;

    private double TrackWidth => IsExpanded
        ? SettingsUILayout.ScrollBarTotalWidth
        : SettingsUILayout.ScrollBarCollapsedTrackWidth;

    private double MaxOffset => _viewer == null ? 0 : Math.Max(0, _viewer.Extent.Height - _viewer.Viewport.Height);

    private Rect ThumbRect()
    {
        if (_viewer == null) return default;

        double height = Math.Max(0, Bounds.Height);
        double viewport = Math.Max(0, _viewer.Viewport.Height);
        double extent = Math.Max(viewport, _viewer.Extent.Height);
        double thumbHeight = extent <= 0
            ? 0
            : Math.Min(height, Math.Max(SettingsUILayout.ScrollBarMinThumbHeight, height * viewport / extent));
        double available = Math.Max(0, height - thumbHeight);
        double top = MaxOffset <= 0 ? 0 : Math.Clamp(_viewer.Offset.Y / MaxOffset * available, 0, available);
        double width = Math.Max(2, TrackWidth - (SettingsUILayout.ScrollBarThumbMargin * 2));
        double left = SettingsUILayout.ScrollBarTotalWidth - TrackWidth + SettingsUILayout.ScrollBarThumbMargin;
        return new Rect(left, top, width, thumbHeight);
    }

    private void SetOffsetFromPointer(double pointerY)
    {
        if (_viewer == null) return;

        Rect thumb = ThumbRect();
        double available = Math.Max(0, Bounds.Height - thumb.Height);
        if (available <= 0)
        {
            _viewer.Offset = new Vector(_viewer.Offset.X, 0);
            return;
        }

        double top = Math.Clamp(pointerY - _dragOffset, 0, available);
        double next = top / available * MaxOffset;
        _viewer.Offset = new Vector(_viewer.Offset.X, next);
    }
}

public sealed class SettingsComboBoxItem : Border
{
    private readonly SettingsPalette _palette;
    private readonly Border _inner;
    private readonly Border _selectionBar;
    private readonly Func<Control>? _contentFactory;
    private bool _isPointerOver;
    private bool _isSelected;

    public SettingsComboBoxItem(object tag, string text, SettingsPalette palette)
        : this(tag, text, palette, contentFactory: null)
    {
    }

    public SettingsComboBoxItem(object tag, string text, SettingsPalette palette, Func<Control>? contentFactory)
    {
        Tag = tag;
        Text = text;
        _palette = palette;
        _contentFactory = contentFactory;

        Background = Brushes.Transparent;
        Padding = SettingsUILayout.ComboItemPadding;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        _selectionBar = new Border
        {
            Width = SettingsUILayout.ComboIndicatorWidth,
            Height = SettingsUILayout.ComboIndicatorHeight,
            CornerRadius = SettingsUILayout.ComboIndicatorRadius,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Control content = CreateContent();
        content.VerticalAlignment = VerticalAlignment.Center;

        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(SettingsUILayout.ComboIndicatorColumnWidth)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(SettingsUILayout.ComboIndicatorGapWidth)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        row.Children.Add(_selectionBar);
        Grid.SetColumn(content, 2);
        row.Children.Add(content);

        _inner = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = SettingsUILayout.ComboItemRadius,
            Padding = SettingsUILayout.ComboItemInnerPadding,
            Child = row,
        };
        Child = _inner;

        PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            UpdateVisual();
        };
        PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            UpdateVisual();
        };
        PointerPressed += (_, e) =>
        {
            if (!IsEnabled) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Pressed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (!IsEnabled) return;
            if (e.Key is not (Key.Enter or Key.Space)) return;
            Pressed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
    }

    public event EventHandler? Pressed;

    public string Text { get; }

    internal Control CreateSelectionContent()
    {
        Control content = CreateContent();
        content.VerticalAlignment = VerticalAlignment.Center;
        return content;
    }

    internal double MeasureContentWidth()
    {
        Control content = CreateSelectionContent();
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return content.DesiredSize.Width;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            UpdateVisual();
        }
    }

    private void UpdateVisual()
    {
        _inner.Background =
            TrayAppDotNETSettingsUI.Brush(_isPointerOver || _isSelected ? _palette.Hover : Colors.Transparent);
        _selectionBar.Background = TrayAppDotNETSettingsUI.Brush(_isSelected ? _palette.Accent : Colors.Transparent);
    }

    private Control CreateContent()
    {
        if (_contentFactory != null) return _contentFactory();

        TextBlock label = TrayAppDotNETSettingsUI.Text(Text, _palette);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.TextWrapping = TextWrapping.NoWrap;
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }
}

public enum SettingsComboBoxAutoSizeMode
{
    LongestItem,
    SelectedItem,
}

public sealed class SettingsComboBox : Grid
{
    private readonly SettingsPalette _palette;
    private readonly SettingsComboBoxItemCollection _items;
    private readonly Border _surface;
    private readonly ContentControl _selectionPresenter;
    private readonly Popup _popup;
    private readonly Border _popupBorder;
    private readonly StackPanel _itemsPanel;
    private bool _autoSizeToText;
    private SettingsComboBoxAutoSizeMode _autoSizeMode;
    private bool _isPointerOver;
    private bool _isPressed;
    private bool _isDropDownOpen;
    private SettingsComboBoxItem? _selectedItem;
    private Thickness _contentPadding = SettingsUILayout.ComboContentPadding;

    public SettingsComboBox(
        SettingsPalette palette,
        double width = 153,
        bool autoSizeToText = false,
        SettingsComboBoxAutoSizeMode autoSizeMode = SettingsComboBoxAutoSizeMode.LongestItem)
    {
        _palette = palette;
        _autoSizeToText = autoSizeToText;
        _autoSizeMode = autoSizeMode;
        _items = new SettingsComboBoxItemCollection(this);

        MinWidth = SettingsUILayout.ComboDefaultMinWidth;
        Width = autoSizeToText ? double.NaN : width;
        Height = SettingsUILayout.ComboHeight;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        ClipToBounds = false;

        _selectionPresenter = new ContentControl
        {
            VerticalAlignment = VerticalAlignment.Center, Margin = _contentPadding, IsHitTestVisible = false,
        };

        TextBlock arrow = TrayAppDotNETSettingsUI.CaptionGlyph(GlyphCatalog.CHEVRON_DOWN, palette);
        arrow.IsHitTestVisible = false;

        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(SettingsUILayout.ComboArrowColumnWidth)));
        row.Children.Add(_selectionPresenter);
        SetColumn(arrow, 1);
        row.Children.Add(arrow);

        _surface = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = SettingsUILayout.ComboBorderThickness,
            CornerRadius = SettingsUILayout.ComboRadius,
            Child = row,
        };
        Children.Add(_surface);

        _itemsPanel = new StackPanel();
        SettingsScrollHost scrollHost = new(_itemsPanel, palette, SettingsUILayout.ComboPopupScrollPadding)
        {
            MaxHeight = SettingsUILayout.ComboPopupMaxHeight,
        };

        _popupBorder = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = SettingsUILayout.ComboPopupBorderThickness,
            CornerRadius = SettingsUILayout.ComboPopupRadius,
            Padding = SettingsUILayout.ComboPopupPadding,
            Margin = SettingsUILayout.ComboPopupMargin,
            Child = scrollHost,
        };

        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 4,
            IsLightDismissEnabled = true,
            Child = _popupBorder,
        };
        Children.Add(_popup);

        PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            UpdateSurface();
        };
        PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            _isPressed = false;
            UpdateSurface();
        };
        PointerPressed += (_, e) =>
        {
            if (!IsEnabled) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _isPressed = true;
            IsDropDownOpen = !IsDropDownOpen;
            Focus();
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (_isPressed)
            {
                _isPressed = false;
                UpdateSurface();
                e.Handled = true;
            }
        };
        KeyDown += (_, e) =>
        {
            if (!IsEnabled) return;
            if (e.Key is Key.Enter or Key.Space or Key.Down)
            {
                IsDropDownOpen = true;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && IsDropDownOpen)
            {
                IsDropDownOpen = false;
                e.Handled = true;
            }
        };
        _popup.PropertyChanged += (_, e) =>
        {
            if (e.Property == Popup.IsOpenProperty && !_popup.IsOpen && _isDropDownOpen)
            {
                _isDropDownOpen = false;
                UpdateSurface();
            }
        };

        TrayAppDotNETSettingsUI.ApplyDisabledOpacity(this, 0.4);
        UpdateSurface();
    }

    public event EventHandler? SelectionChanged;

    public SettingsComboBoxItemCollection Items => _items;

    public Thickness Padding
    {
        get => _contentPadding;
        set
        {
            _contentPadding = value;
            _selectionPresenter.Margin = value;
            UpdateAutoWidth();
        }
    }

    public bool AutoSizeToText
    {
        get => _autoSizeToText;
        set
        {
            if (_autoSizeToText == value) return;
            _autoSizeToText = value;
            Width = value ? double.NaN : Math.Max(SettingsUILayout.ComboDefaultMinWidth, Bounds.Width);
            UpdateAutoWidth();
        }
    }

    public SettingsComboBoxAutoSizeMode AutoSizeMode
    {
        get => _autoSizeMode;
        set
        {
            if (_autoSizeMode == value) return;
            _autoSizeMode = value;
            UpdateAutoWidth();
        }
    }

    public bool IsDropDownOpen
    {
        get => _isDropDownOpen;
        set
        {
            if (_isDropDownOpen == value) return;
            _isDropDownOpen = value;
            if (value) RebuildPopupItems();
            _popup.IsOpen = value;
            UpdateSurface();
        }
    }

    public SettingsComboBoxItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem == value) return;
            if (_selectedItem != null) _selectedItem.IsSelected = false;
            _selectedItem = value;
            if (_selectedItem != null) _selectedItem.IsSelected = true;
            _selectionPresenter.Content = _selectedItem?.CreateSelectionContent();
            UpdateAutoWidth();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int SelectedIndex
    {
        get => _selectedItem == null ? -1 : _items.IndexOf(_selectedItem);
        set
        {
            if (value < 0)
            {
                SelectedItem = null;
                return;
            }

            if (value >= _items.Count) throw new ArgumentOutOfRangeException(nameof(value));
            SelectedItem = _items[value];
        }
    }

    internal void OnItemAdded(SettingsComboBoxItem item)
    {
        item.Pressed += OnItemPressed;
        UpdateAutoWidth();
    }

    internal void OnItemRemoved(SettingsComboBoxItem item)
    {
        item.Pressed -= OnItemPressed;
        if (ReferenceEquals(_selectedItem, item)) SelectedItem = null;
        UpdateAutoWidth();
    }

    internal void OnItemsReset()
    {
        _itemsPanel.Children.Clear();
        SelectedItem = null;
        UpdateAutoWidth();
    }

    private void OnItemPressed(object? sender, EventArgs e)
    {
        if (sender is not SettingsComboBoxItem item) return;
        SelectedItem = item;
        IsDropDownOpen = false;
    }

    private void RebuildPopupItems()
    {
        _itemsPanel.Children.Clear();
        foreach (SettingsComboBoxItem item in _items)
        {
            item.IsSelected = ReferenceEquals(item, _selectedItem);
            _itemsPanel.Children.Add(item);
        }

        _popupBorder.MinWidth = Math.Max(SettingsUILayout.ComboDefaultMinWidth, Bounds.Width);
    }

    private void UpdateSurface()
    {
        Color color = _isDropDownOpen || _isPressed
            ? _palette.Pressed
            : _isPointerOver
                ? _palette.Hover
                : _palette.ControlBackground;
        _surface.Background = TrayAppDotNETSettingsUI.Brush(color);
    }

    private void UpdateAutoWidth()
    {
        if (!_autoSizeToText) return;

        double contentWidth = _autoSizeMode == SettingsComboBoxAutoSizeMode.SelectedItem
            ? _selectedItem?.MeasureContentWidth() ?? 0
            : MeasureLongestItemWidth();

        double desired = Math.Ceiling(Math.Max(
            SettingsUILayout.ComboDefaultMinWidth,
            contentWidth
            + _contentPadding.Left
            + _contentPadding.Right
            + SettingsUILayout.ComboArrowColumnWidth
            + SettingsUILayout.ComboAutoSizeExtraPadding));
        Width = desired;
    }

    private double MeasureLongestItemWidth()
    {
        double widest = 0;
        foreach (SettingsComboBoxItem item in _items)
            widest = Math.Max(widest, item.MeasureContentWidth());
        return widest;
    }
}

public sealed class SettingsComboBoxItemCollection(SettingsComboBox owner) : Collection<SettingsComboBoxItem>
{
    protected override void InsertItem(int index, SettingsComboBoxItem item)
    {
        base.InsertItem(index, item);
        owner.OnItemAdded(item);
    }

    protected override void SetItem(int index, SettingsComboBoxItem item)
    {
        SettingsComboBoxItem old = this[index];
        owner.OnItemRemoved(old);
        base.SetItem(index, item);
        owner.OnItemAdded(item);
    }

    protected override void RemoveItem(int index)
    {
        SettingsComboBoxItem old = this[index];
        base.RemoveItem(index);
        owner.OnItemRemoved(old);
    }

    protected override void ClearItems()
    {
        foreach (SettingsComboBoxItem item in this)
            owner.OnItemRemoved(item);
        base.ClearItems();
        owner.OnItemsReset();
    }
}

public sealed class SettingsNumberValueChangedEventArgs(double? oldValue, double? newValue) : EventArgs
{
    public double? OldValue { get; } = oldValue;
    public double? NewValue { get; } = newValue;
}

public sealed class SettingsNumberBox : Grid
{
    private readonly SettingsPalette _palette;
    private readonly Border _valueBorder;
    private readonly TextBox _textBox;
    private readonly TextBlock _suffixText;
    private readonly SettingsSpinnerButton _upButton;
    private readonly SettingsSpinnerButton _downButton;
    private readonly double _baseWidth;
    private TopLevel? _outsidePointerHost;
    private bool _isPointerOverValue;
    private bool _isTextFocused;
    private bool _cancelTextEditOnLostFocus;
    private int _minimum;
    private int _maximum;
    private double? _value;
    private double? _valueAtTextFocus;

    public SettingsNumberBox(SettingsPalette palette, int value, int min, int max, double width = 100,
        string suffix = "")
    {
        _palette = palette;
        _minimum = min;
        _maximum = max;
        _baseWidth = Math.Max(1, width);
        MinWidth = _baseWidth;
        Height = SettingsUILayout.NumberBoxHeight;
        Focusable = true;
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(SettingsUILayout.NumberBoxSpinnerColumnWidth)));

        _textBox = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = SettingsUILayout.NumberTextBorderThickness,
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = SettingsUILayout.NumberTextFontSize,
            MinWidth = 0,
            Padding = SettingsUILayout.NumberTextPadding,
            TextAlignment = TextAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            SelectionBrush =
                TrayAppDotNETSettingsUI.Brush(AppTheme.ResolveTextSelectionHighlight(palette.Accent)),
            SelectionForegroundBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
        };
        TrayAppDotNETSettingsUI.ApplyTextBoxResources(
            _textBox,
            palette,
            Brushes.Transparent,
            Brushes.Transparent,
            Brushes.Transparent);
        _textBox.TextInput += (_, e) =>
        {
            foreach (char c in e.Text ?? string.Empty)
            {
                if (char.IsDigit(c)) continue;
                if (c == '-' &&
                    Minimum < 0 &&
                    _textBox.SelectionStart == 0 &&
                    !(_textBox.Text ?? string.Empty).Contains('-', StringComparison.Ordinal))
                    continue;

                e.Handled = true;
                return;
            }
        };

        _suffixText = TrayAppDotNETSettingsUI.Text(suffix, palette, SettingsUILayout.NumberSuffixFontSize);
        _suffixText.Margin = SettingsUILayout.NumberSuffixMargin;
        _suffixText.VerticalAlignment = VerticalAlignment.Center;
        _suffixText.IsHitTestVisible = false;

        Grid valueGrid = new();
        valueGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        valueGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        valueGrid.Children.Add(_textBox);
        SetColumn(_suffixText, 1);
        valueGrid.Children.Add(_suffixText);

        _valueBorder = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            CornerRadius = SettingsUILayout.NumberValueRadius,
            Height = SettingsUILayout.NumberBoxHeight,
            Child = valueGrid,
        };
        Children.Add(_valueBorder);

        Grid spinnerGrid = new();
        spinnerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        spinnerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        _upButton = new SettingsSpinnerButton(GlyphCatalog.CHEVRON_UP, palette);
        _downButton = new SettingsSpinnerButton(GlyphCatalog.CHEVRON_DOWN, palette);
        spinnerGrid.Children.Add(_upButton);
        SetRow(_downButton, 1);
        spinnerGrid.Children.Add(_downButton);
        SetColumn(spinnerGrid, 1);
        Children.Add(spinnerGrid);

        _valueBorder.PointerEntered += (_, _) =>
        {
            _isPointerOverValue = true;
            UpdateValueBorder();
        };
        _valueBorder.PointerExited += (_, _) =>
        {
            _isPointerOverValue = false;
            UpdateValueBorder();
        };
        _textBox.GotFocus += (_, _) =>
        {
            _isTextFocused = true;
            _valueAtTextFocus = Value;
            _cancelTextEditOnLostFocus = false;
            AttachOutsidePointerHandler();
            UpdateSuffixOpacity();
            UpdateValueBorder();
        };
        _textBox.LostFocus += (_, _) =>
        {
            DetachOutsidePointerHandler();
            _isTextFocused = false;
            if (_cancelTextEditOnLostFocus)
            {
                _cancelTextEditOnLostFocus = false;
                RestoreTextFocusValue();
            }
            else
                CommitTextOrRestore();

            UpdateSuffixOpacity();
            UpdateValueBorder();
        };
        _textBox.TextChanged += (_, _) =>
        {
            UpdateSuffixOpacity();
            UpdateAutoWidth();
        };
        _textBox.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Up:
                    ChangeBy(ArrowStepFromModifiers(e.KeyModifiers));
                    e.Handled = true;
                    break;
                case Key.Down:
                    ChangeBy(-ArrowStepFromModifiers(e.KeyModifiers));
                    e.Handled = true;
                    break;
                case Key.Enter:
                    CommitFocusedTextEdit();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    CancelTextEdit();
                    e.Handled = true;
                    break;
            }
        };
        PointerWheelChanged += (_, e) =>
        {
            if (!_isTextFocused && !HandleMouseWheelWhenMouseOver) return;
            int magnitude = WheelStepFromModifiers(e.KeyModifiers);
            ChangeBy(e.Delta.Y > 0 ? magnitude : -magnitude);
            e.Handled = true;
        };
        _upButton.Click += (_, _) => ChangeBy(Step);
        _downButton.Click += (_, _) => ChangeBy(-Step);

        Value = value;
        TrayAppDotNETSettingsUI.ApplyDisabledOpacity(this, 0.4);
        UpdateValueBorder();
    }

    public event EventHandler<SettingsNumberValueChangedEventArgs>? ValueChanged;

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            ClampCurrentValue();
            UpdateAutoWidth();
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            ClampCurrentValue();
            UpdateAutoWidth();
        }
    }

    public int Step
    {
        get;
        set => field = Math.Max(1, value);
    } = 1;

    public int WheelStep
    {
        get;
        set => field = Math.Max(1, value);
    } = 1;

    public int LargeStep
    {
        get;
        set => field = Math.Max(1, value);
    } = 10;

    public int ExtraLargeStep
    {
        get;
        set => field = Math.Max(1, value);
    } = 100;

    public bool AllowInherit
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            UpdateText();
            UpdateAutoWidth();
        }
    }

    public int InheritValue
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            UpdateText();
            UpdateAutoWidth();
        }
    } = -1;

    public string PlaceholderText
    {
        get;
        set
        {
            field = value;
            _textBox.PlaceholderText = value;
            UpdateSuffixOpacity();
            UpdateAutoWidth();
        }
    } = string.Empty;

    public bool HandleMouseWheelWhenMouseOver
    {
        get;
        set;
    }

    public string Suffix
    {
        get => _suffixText.Text ?? string.Empty;
        set
        {
            _suffixText.Text = value;
            UpdateAutoWidth();
        }
    }

    public double? Value
    {
        get => _value;
        set => SetValue(value, raiseChanged: true);
    }

    private void ChangeBy(int delta)
    {
        int current;
        string text = _textBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) ||
            !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out current))
            current = delta > 0 ? Minimum : Maximum;

        Value = Math.Clamp(current + delta, Minimum, Maximum);
        if (_isTextFocused)
            _valueAtTextFocus = Value;
    }

    /// <summary>
    /// Commits the text edit and makes the committed value the new cancel baseline.
    /// </summary>
    private void CommitFocusedTextEdit()
    {
        CommitTextOrRestore();
        _valueAtTextFocus = Value;
    }

    private void CommitTextOrRestore()
    {
        string text = _textBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (AllowInherit)
            {
                Value = InheritValue;
                return;
            }

            int fallback = Value == InheritValue ? Minimum : (int)Math.Round(Value ?? Minimum);
            Value = Math.Clamp(fallback, Minimum, Maximum);
            return;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            Value = Math.Clamp(parsed, Minimum, Maximum);
            return;
        }

        if (AllowInherit)
        {
            Value = InheritValue;
            return;
        }

        UpdateText();
    }

    private void CancelTextEdit()
    {
        _cancelTextEditOnLostFocus = true;
        RestoreTextFocusValue();
        _textBox.ClearSelection();
        Focus();
    }

    private void RestoreTextFocusValue()
    {
        SetValue(_valueAtTextFocus, raiseChanged: true);
        UpdateText();
    }

    private void AttachOutsidePointerHandler()
    {
        TopLevel? host = TopLevel.GetTopLevel(this);
        if (host == null || ReferenceEquals(host, _outsidePointerHost)) return;

        DetachOutsidePointerHandler();
        _outsidePointerHost = host;
        host.AddHandler(PointerPressedEvent, OnTopLevelPointerPressed, RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void DetachOutsidePointerHandler()
    {
        if (_outsidePointerHost == null) return;

        _outsidePointerHost.RemoveHandler(PointerPressedEvent, OnTopLevelPointerPressed);
        _outsidePointerHost = null;
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isTextFocused) return;
        if (IsSelfOrDescendant(e.Source as Visual)) return;

        CancelTextEdit();
    }

    private bool IsSelfOrDescendant(Visual? visual)
    {
        if (visual == null) return false;
        if (ReferenceEquals(visual, this)) return true;
        return visual.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, this));
    }

    private void SetValue(double? next, bool raiseChanged)
    {
        double? clamped = next.HasValue
            ? AllowInherit && (int)Math.Round(next.Value) == InheritValue
                ? InheritValue
                : Math.Clamp(next.Value, Minimum, Maximum)
            : null;
        if (_value == clamped)
        {
            UpdateText();
            return;
        }

        double? old = _value;
        _value = clamped;
        UpdateText();
        UpdateAutoWidth();
        if (raiseChanged)
            ValueChanged?.Invoke(this, new SettingsNumberValueChangedEventArgs(old, clamped));
    }

    private void UpdateText()
    {
        string text = _value.HasValue
            ? AllowInherit && (int)Math.Round(_value.Value) == InheritValue
                ? string.Empty
                : ((int)Math.Round(_value.Value)).ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        if (_textBox.Text == text) return;

        _textBox.Text = text;
        _textBox.CaretIndex = text.Length;
        UpdateSuffixOpacity();
    }

    private void ClampCurrentValue()
    {
        if (!Value.HasValue) return;
        if (AllowInherit && (int)Math.Round(Value.Value) == InheritValue) return;
        Value = Math.Clamp(Value.Value, Minimum, Maximum);
    }

    private int ArrowStepFromModifiers(KeyModifiers modifiers)
    {
        bool ctrl = (modifiers & KeyModifiers.Control) != 0;
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        return ctrl switch
        {
            true when shift => ExtraLargeStep,
            true => LargeStep,
            _ => Step,
        };
    }

    private int WheelStepFromModifiers(KeyModifiers modifiers)
    {
        bool ctrl = (modifiers & KeyModifiers.Control) != 0;
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        return ctrl switch
        {
            true when shift => ExtraLargeStep,
            true => LargeStep,
            _ => WheelStep,
        };
    }

    private void UpdateSuffixOpacity()
    {
        bool placeholderShowing = !string.IsNullOrEmpty(PlaceholderText)
                                  && string.IsNullOrEmpty(_textBox.Text)
                                  && !_isTextFocused;
        _suffixText.Opacity = placeholderShowing
            ? SettingsUILayout.NumberSuffixPlaceholderOpacity
            : SettingsUILayout.EnabledOpacity;
    }

    private void UpdateAutoWidth()
    {
        string valueText = _textBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(valueText))
            valueText = PlaceholderText;
        bool isInheritedValue = Value.HasValue && (int)Math.Round(Value.Value) == InheritValue;
        if (string.IsNullOrEmpty(valueText) && !(AllowInherit && isInheritedValue))
            valueText = ((int)Math.Round(Value ?? 0)).ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(valueText))
            valueText = "0";

        double suffixWidth = string.IsNullOrEmpty(Suffix)
            ? 0
            : MeasureTextWidth(Suffix, SettingsUILayout.NumberSuffixFontSize)
              + _suffixText.Margin.Left
              + _suffixText.Margin.Right;
        MinWidth = Math.Max(_baseWidth, Math.Ceiling(
            MeasureTextWidth(valueText, SettingsUILayout.NumberValueFontSize)
            + _textBox.Padding.Left
            + _textBox.Padding.Right
            + suffixWidth
            + SettingsUILayout.NumberBoxSpinnerColumnWidth
            + SettingsUILayout.NumberAutoWidthReserve));
    }

    private static double MeasureTextWidth(string text, double fontSize)
    {
        TextBlock probe = new() { Text = text, FontFamily = TrayAppDotNETSettingsUI.UIFont, FontSize = fontSize, };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    private void UpdateValueBorder()
    {
        Color color = _isTextFocused
            ? _palette.TextBoxFocused
            : _isPointerOverValue
                ? _palette.Hover
                : _palette.ControlBackground;
        _valueBorder.Background = TrayAppDotNETSettingsUI.Brush(color);
    }
}

internal sealed class SettingsSpinnerButton : Border
{
    private readonly SettingsPalette _palette;
    private bool _isPointerOver;
    private bool _isPressed;

    public SettingsSpinnerButton(string glyph, SettingsPalette palette)
    {
        _palette = palette;
        Background = Brushes.Transparent;
        CornerRadius = SettingsUILayout.SpinnerButtonRadius;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = false;
        Child = new TextBlock
        {
            Text = glyph,
            FontFamily = TrayAppDotNETSettingsUI.IconFont,
            FontSize = SettingsUILayout.SpinnerGlyphFontSize,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

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
            if (!IsEnabled) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _isPressed = true;
            UpdateVisual();
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (!_isPressed) return;
            _isPressed = false;
            UpdateVisual();
            Click?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
    }

    public event EventHandler? Click;

    private void UpdateVisual()
    {
        Background = TrayAppDotNETSettingsUI.Brush(
            _isPressed ? _palette.Pressed : _isPointerOver ? _palette.Hover : Colors.Transparent);
    }
}

public static class TrayAppDotNETSettingsUI
{
    public static readonly FontFamily UIFont = new("Segoe UI Variable, Segoe UI");

    public static readonly FontFamily IconFont = new(
        $"{GlyphCatalog.SEGOE_FLUENT_ICONS}, {GlyphCatalog.SEGOE_MDL2_ASSETS}");

    public static IBrush Brush(Color color) =>
        color == Colors.Transparent ? Brushes.Transparent : new SolidColorBrush(color);

    public static TextBlock Text(string text, SettingsPalette palette, double fontSize = 14, FontWeight? weight = null)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = UIFont,
            FontSize = fontSize,
            FontWeight = weight ?? FontWeight.Normal,
            Foreground = Brush(palette.Foreground),
        };
    }

    public static TextBlock SectionHeader(string text, SettingsPalette palette) =>
        new()
        {
            Text = text,
            FontFamily = UIFont,
            FontSize = SettingsUILayout.SectionHeaderFontSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(palette.Foreground),
            Margin = SettingsUILayout.SectionHeaderMargin,
        };

    public static TextBlock SubsectionHeader(string text, SettingsPalette palette) =>
        new()
        {
            Text = text,
            FontFamily = UIFont,
            FontSize = SettingsUILayout.SubsectionHeaderFontSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(palette.Foreground),
            Margin = SettingsUILayout.SubsectionHeaderMargin,
        };

    public static TextBlock TitleText(string text, SettingsPalette palette) =>
        new()
        {
            Text = text,
            FontFamily = UIFont,
            FontSize = SettingsUILayout.TitleFontSize,
            Foreground = Brush(palette.Foreground),
            TextWrapping = TextWrapping.Wrap,
        };

    public static TextBlock DescriptionText(string text, SettingsPalette palette, Thickness? margin = null) =>
        new()
        {
            Text = text,
            FontFamily = UIFont,
            FontSize = SettingsUILayout.DescriptionFontSize,
            Foreground = Brush(palette.SecondaryForeground),
            Opacity = SettingsUILayout.DescriptionOpacity,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin ?? SettingsUILayout.DescriptionMargin,
        };

    public static StackPanel PageStack(string title, SettingsPalette palette)
    {
        StackPanel stack = new() { Background = Brush(palette.Background), };
        stack.Children.Add(SectionHeader(title, palette));
        return stack;
    }

    public static Border Card(string title, string description, Control? rightControl, SettingsPalette palette)
    {
        StackPanel text = new()
        {
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Children.Add(TitleText(title, palette));
        if (!string.IsNullOrEmpty(description))
            text.Children.Add(DescriptionText(description, palette));

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(text);

        if (rightControl != null)
        {
            rightControl.VerticalAlignment = VerticalAlignment.Center;
            rightControl.Margin = SettingsUILayout.CardRightControlMargin;
            Grid.SetColumn(rightControl, 1);
            grid.Children.Add(rightControl);
        }

        Border card = new()
        {
            Background = Brush(palette.CardBackground),
            CornerRadius = SettingsUILayout.CardRadius,
            Padding = SettingsUILayout.CardPadding,
            Margin = SettingsUILayout.CardMargin,
            Child = grid,
        };
        ApplyDisabledOpacity(card, SettingsUILayout.ControlDisabledOpacity);
        return card;
    }

    public static Border RawCard(Control content, SettingsPalette palette)
    {
        Border card = new()
        {
            Background = Brush(palette.CardBackground),
            CornerRadius = SettingsUILayout.CardRadius,
            Padding = SettingsUILayout.CardPadding,
            Margin = SettingsUILayout.CardMargin,
            Child = content,
        };
        ApplyDisabledOpacity(card, SettingsUILayout.ControlDisabledOpacity);
        return card;
    }

    public static SettingsScrollHost ScrollHost(Control content, SettingsPalette palette, Thickness padding) =>
        new(content, palette, padding);

    public static SettingsButton Button(string text, SettingsPalette palette) => new(text, palette);

    public static SettingsButton NavAction(string text, SettingsPalette palette) =>
        new(text, palette, transparentBase: true, navGutter: true)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = SettingsUILayout.NavItemPadding,
            Margin = SettingsUILayout.NavActionMargin,
        };

    public static SettingsToggle Toggle(SettingsPalette palette, bool isChecked, EventHandler<bool> changed)
    {
        SettingsToggle toggle = new(palette) { IsChecked = isChecked };
        toggle.CheckedChanged += changed;
        return toggle;
    }

    public static SettingsComboBox ComboBox(
        SettingsPalette palette,
        double width = 153,
        bool autoSizeToText = false,
        SettingsComboBoxAutoSizeMode autoSizeMode = SettingsComboBoxAutoSizeMode.LongestItem) =>
        new(palette, width, autoSizeToText, autoSizeMode);

    public static SettingsComboBoxItem ComboItem(string tag, string text, SettingsPalette palette) =>
        new(tag, text, palette);

    public static void SelectComboByTag(SettingsComboBox combo, string tag)
    {
        foreach (SettingsComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    public static string? SelectedTag(SettingsComboBox combo) =>
        combo.SelectedItem?.Tag?.ToString();

    public static TextBox TextBox(SettingsPalette palette, double width, string text = "")
    {
        TextBox textBox = new()
        {
            Width = width,
            Height = SettingsUILayout.TextBoxHeight,
            Text = text,
            FontFamily = UIFont,
            FontSize = SettingsUILayout.TextBoxFontSize,
            Background = Brush(palette.ControlBackground),
            Foreground = Brush(palette.Foreground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = SettingsUILayout.TextBoxBorderThickness,
            Padding = SettingsUILayout.TextBoxPadding,
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = Brush(palette.Foreground),
            SelectionBrush = Brush(AppTheme.ResolveTextSelectionHighlight(palette.Accent)),
            SelectionForegroundBrush = Brush(palette.Foreground),
        };
        ApplyTextBoxResources(
            textBox,
            palette,
            Brush(palette.ControlBackground),
            Brush(palette.Hover),
            Brush(palette.TextBoxFocused));

        AttachSurfaceStates(
            textBox,
            palette.ControlBackground,
            palette.Hover,
            palette.TextBoxFocused);
        return textBox;
    }

    public static void ApplyTextBoxResources(
        TextBox textBox,
        SettingsPalette palette,
        IBrush normalBackground,
        IBrush pointerOverBackground,
        IBrush focusedBackground)
    {
        IBrush transparent = Brushes.Transparent;
        IBrush foreground = Brush(palette.Foreground);
        IBrush disabled = Brush(palette.DisabledForeground);

        textBox.CaretBrush = foreground;
        textBox.SelectionBrush = Brush(AppTheme.ResolveTextSelectionHighlight(palette.Accent));
        textBox.SelectionForegroundBrush = foreground;
        textBox.Resources["TextControlBackground"] = normalBackground;
        textBox.Resources["TextControlBackgroundPointerOver"] = pointerOverBackground;
        textBox.Resources["TextControlBackgroundFocused"] = focusedBackground;
        textBox.Resources["TextControlBackgroundPressed"] = focusedBackground;
        textBox.Resources["TextControlBackgroundDisabled"] = normalBackground;
        textBox.Resources["TextControlBorderBrush"] = transparent;
        textBox.Resources["TextControlBorderBrushPointerOver"] = transparent;
        textBox.Resources["TextControlBorderBrushFocused"] = transparent;
        textBox.Resources["TextControlBorderBrushPressed"] = transparent;
        textBox.Resources["TextControlBorderBrushDisabled"] = transparent;
        textBox.Resources["TextControlForeground"] = foreground;
        textBox.Resources["TextControlForegroundPointerOver"] = foreground;
        textBox.Resources["TextControlForegroundFocused"] = foreground;
        textBox.Resources["TextControlForegroundDisabled"] = disabled;
        textBox.Resources["TextControlPlaceholderForeground"] = disabled;
        textBox.Resources["TextControlPlaceholderForegroundPointerOver"] = disabled;
        textBox.Resources["TextControlPlaceholderForegroundFocused"] = disabled;
        textBox.Resources["TextControlPlaceholderForegroundDisabled"] = disabled;
        textBox.Resources["TextControlSelectionHighlightColor"] = AppTheme.ResolveTextSelectionHighlight(palette.Accent);
        textBox.Resources["TextControlSelectionHighlightForeground"] = foreground;
    }

    public static SettingsNumberBox NumberBox(
        SettingsPalette palette,
        int value,
        int min,
        int max,
        double width = 100,
        string suffix = "") =>
        new(palette, value, min, max, width, suffix);

    public static StackPanel Horizontal(params Control[] controls)
    {
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (Control control in controls)
            panel.Children.Add(control);
        return panel;
    }

    public static TextBlock CaptionGlyph(string glyph, SettingsPalette palette) =>
        new()
        {
            Text = glyph,
            FontFamily = IconFont,
            FontSize = SettingsUILayout.CaptionGlyphFontSize,
            Foreground = Brush(palette.Foreground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

    internal static void ApplyDisabledOpacity(Control control, double disabledOpacity)
    {
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsEnabledProperty)
                control.Opacity = control.IsEnabled ? SettingsUILayout.EnabledOpacity : disabledOpacity;
        };
        control.Opacity = control.IsEnabled ? SettingsUILayout.EnabledOpacity : disabledOpacity;
    }

    private static void AttachSurfaceStates(Control control, Color normal, Color hover, Color focusedOrPressed)
    {
        bool pointerOver = false;
        bool focused = false;

        void Update()
        {
            Color color = focused ? focusedOrPressed : pointerOver ? hover : normal;
            switch (control)
            {
                case TextBox textBox:
                    textBox.Background = Brush(color);
                    break;
            }
        }

        control.PointerEntered += (_, _) =>
        {
            pointerOver = true;
            Update();
        };
        control.PointerExited += (_, _) =>
        {
            pointerOver = false;
            Update();
        };
        control.GotFocus += (_, _) =>
        {
            focused = true;
            Update();
        };
        control.LostFocus += (_, _) =>
        {
            focused = false;
            Update();
        };
        ApplyDisabledOpacity(control, SettingsUILayout.DisabledOpacity);
    }
}
