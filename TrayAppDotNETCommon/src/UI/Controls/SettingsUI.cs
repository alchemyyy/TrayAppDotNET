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
using TrayAppDotNETCommon.UI.Debugging;
using TrayAppDotNETCommon.UI.Settings;
using TrayAppDotNETCommon.UI.Tray;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class SettingsUILayout
{
    private static readonly Lazy<SettingsUIResources> Resources = new(static () => new SettingsUIResources());

    private static SettingsUIResources AXAMLResources => Resources.Value;

    public static Thickness NavItemMargin => AXAMLResources.AxamlSettingsUI.NavItemMargin;
    public static Thickness NavActionMargin => AXAMLResources.AxamlSettingsUI.NavActionMargin;
    public static double NavIndicatorWidth => AXAMLResources.AxamlSettingsUI.NavIndicatorWidth;
    public static double NavIndicatorHeight => AXAMLResources.AxamlSettingsUI.NavIndicatorHeight;
    public static CornerRadius NavIndicatorCornerRadius =>
        AXAMLResources.AxamlSettingsUI.NavIndicatorCornerRadius;
    public static Thickness NavIndicatorMargin => AXAMLResources.AxamlSettingsUI.NavIndicatorMargin;
    public static CornerRadius NavItemCornerRadius => AXAMLResources.AxamlSettingsUI.NavItemCornerRadius;
    public static Thickness NavItemPadding => AXAMLResources.AxamlSettingsUI.NavItemPadding;
    public static Thickness NavItemInnerMargin => AXAMLResources.AxamlSettingsUI.NavItemInnerMargin;
    public static Color Windows11NavIndicatorColor => AXAMLResources.AxamlSettingsUI.Windows11NavIndicatorColor;
    public static Thickness Windows11NavItemPadding => AXAMLResources.AxamlSettingsUI.Windows11NavItemPadding;
    public static Thickness Windows11NavContentMargin => AXAMLResources.AxamlSettingsUI.Windows11NavContentMargin;
    public static double Windows11NavIconSize => AXAMLResources.AxamlSettingsUI.Windows11NavIconSize;
    public static double Windows11NavIconFontSize => AXAMLResources.AxamlSettingsUI.Windows11NavIconFontSize;
    public static double Windows11NavIconColumnWidth => AXAMLResources.AxamlSettingsUI.Windows11NavIconColumnWidth;
    public static Thickness Windows11NavIconMargin => AXAMLResources.AxamlSettingsUI.Windows11NavIconMargin;
    public static Thickness Windows11NavLabelMargin => AXAMLResources.AxamlSettingsUI.Windows11NavLabelMargin;
    public static CornerRadius ButtonCornerRadius => AXAMLResources.AxamlSettingsUI.ButtonCornerRadius;
    public static double ButtonMinHeight => AXAMLResources.AxamlSettingsUI.ButtonMinHeight;
    public static Thickness ButtonPadding => AXAMLResources.AxamlSettingsUI.ButtonPadding;
    public static double EnabledOpacity => AXAMLResources.AxamlSettingsUI.EnabledOpacity;
    public static double DisabledOpacity => AXAMLResources.AxamlSettingsUI.DisabledOpacity;
    public static double ControlDisabledOpacity => AXAMLResources.AxamlSettingsUI.ControlDisabledOpacity;
    public static double ToggleWidth => AXAMLResources.AxamlSettingsUI.ToggleWidth;
    public static double ToggleHeight => AXAMLResources.AxamlSettingsUI.ToggleHeight;
    public static CornerRadius ToggleTrackCornerRadius => AXAMLResources.AxamlSettingsUI.ToggleTrackCornerRadius;
    public static Thickness ToggleBorderThickness => AXAMLResources.AxamlSettingsUI.ToggleBorderThickness;
    public static double ToggleThumbWidth => AXAMLResources.AxamlSettingsUI.ToggleThumbWidth;
    public static double ToggleThumbHeight => AXAMLResources.AxamlSettingsUI.ToggleThumbHeight;
    public static double ToggleThumbHoverSize => AXAMLResources.AxamlSettingsUI.ToggleThumbHoverSize;
    public static double ToggleThumbCheckedSize => AXAMLResources.AxamlSettingsUI.ToggleThumbCheckedSize;
    public static CornerRadius ToggleThumbCornerRadius => AXAMLResources.AxamlSettingsUI.ToggleThumbCornerRadius;
    public static Thickness ToggleThumbUncheckedMargin => AXAMLResources.AxamlSettingsUI.ToggleThumbUncheckedMargin;
    public static Thickness ToggleThumbCheckedMargin => AXAMLResources.AxamlSettingsUI.ToggleThumbCheckedMargin;
    public static double SwatchWidth => AXAMLResources.AxamlSettingsUI.SwatchWidth;
    public static double SwatchHeight => AXAMLResources.AxamlSettingsUI.SwatchHeight;
    public static CornerRadius SwatchCornerRadius => AXAMLResources.AxamlSettingsUI.SwatchCornerRadius;
    public static Thickness SwatchBorderThickness => AXAMLResources.AxamlSettingsUI.SwatchBorderThickness;
    public static Thickness SwatchMargin => AXAMLResources.AxamlSettingsUI.SwatchMargin;
    public static double SwatchFallbackOpacity => AXAMLResources.AxamlSettingsUI.SwatchFallbackOpacity;
    public static double ScrollWheelStep => AXAMLResources.AxamlSettingsUI.ScrollWheelStep;
    public static double ScrollBarTotalWidth => AXAMLResources.AxamlSettingsUI.ScrollBarTotalWidth;
    public static double ScrollBarCollapsedTrackWidth => AXAMLResources.AxamlSettingsUI.ScrollBarCollapsedTrackWidth;
    public static double ScrollBarThumbMargin => AXAMLResources.AxamlSettingsUI.ScrollBarThumbMargin;
    public static double ScrollBarMinThumbHeight => AXAMLResources.AxamlSettingsUI.ScrollBarMinThumbHeight;
    public static Thickness ComboItemPadding => AXAMLResources.AxamlSettingsUI.ComboItemPadding;
    public static double ComboIndicatorWidth => AXAMLResources.AxamlSettingsUI.ComboIndicatorWidth;
    public static double ComboIndicatorHeight => AXAMLResources.AxamlSettingsUI.ComboIndicatorHeight;
    public static CornerRadius ComboIndicatorCornerRadius =>
        AXAMLResources.AxamlSettingsUI.ComboIndicatorCornerRadius;
    public static double ComboIndicatorColumnWidth => AXAMLResources.AxamlSettingsUI.ComboIndicatorColumnWidth;
    public static double ComboIndicatorGapWidth => AXAMLResources.AxamlSettingsUI.ComboIndicatorGapWidth;
    public static CornerRadius ComboItemCornerRadius => AXAMLResources.AxamlSettingsUI.ComboItemCornerRadius;
    public static Thickness ComboItemInnerPadding => AXAMLResources.AxamlSettingsUI.ComboItemInnerPadding;
    public static double ComboArrowColumnWidth => AXAMLResources.AxamlSettingsUI.ComboArrowColumnWidth;
    public static double ComboDefaultMinWidth => AXAMLResources.AxamlSettingsUI.ComboDefaultMinWidth;
    public static double ComboPopupMaxHeight => AXAMLResources.AxamlSettingsUI.ComboPopupMaxHeight;
    public static double ComboAutoSizeExtraPadding => AXAMLResources.AxamlSettingsUI.ComboAutoSizeExtraPadding;
    public static Thickness ComboContentPadding => AXAMLResources.AxamlSettingsUI.ComboContentPadding;
    public static double ComboHeight => AXAMLResources.AxamlSettingsUI.ComboHeight;
    public static Thickness ComboBorderThickness => AXAMLResources.AxamlSettingsUI.ComboBorderThickness;
    public static CornerRadius ComboCornerRadius => AXAMLResources.AxamlSettingsUI.ComboCornerRadius;
    public static Thickness ComboPopupScrollPadding => AXAMLResources.AxamlSettingsUI.ComboPopupScrollPadding;
    public static Thickness ComboPopupBorderThickness => AXAMLResources.AxamlSettingsUI.ComboPopupBorderThickness;
    public static CornerRadius ComboPopupCornerRadius => AXAMLResources.AxamlSettingsUI.ComboPopupCornerRadius;
    public static Thickness ComboPopupPadding => AXAMLResources.AxamlSettingsUI.ComboPopupPadding;
    public static Thickness ComboPopupMargin => AXAMLResources.AxamlSettingsUI.ComboPopupMargin;
    public static double NumberBoxHeight => AXAMLResources.AxamlSettingsUI.NumberBoxHeight;
    public static double NumberBoxSpinnerColumnWidth => AXAMLResources.AxamlSettingsUI.NumberBoxSpinnerColumnWidth;
    public static Thickness NumberTextBorderThickness => AXAMLResources.AxamlSettingsUI.NumberTextBorderThickness;
    public static double NumberTextFontSize => AXAMLResources.AxamlSettingsUI.NumberTextFontSize;
    public static Thickness NumberTextPadding => AXAMLResources.AxamlSettingsUI.NumberTextPadding;
    public static Thickness NumberSuffixMargin => AXAMLResources.AxamlSettingsUI.NumberSuffixMargin;
    public static CornerRadius NumberValueCornerRadius => AXAMLResources.AxamlSettingsUI.NumberValueCornerRadius;
    public static double NumberSuffixPlaceholderOpacity =>
        AXAMLResources.AxamlSettingsUI.NumberSuffixPlaceholderOpacity;
    public static double NumberSuffixFontSize => AXAMLResources.AxamlSettingsUI.NumberSuffixFontSize;
    public static double NumberValueFontSize => AXAMLResources.AxamlSettingsUI.NumberValueFontSize;
    public static double NumberAutoWidthReserve => AXAMLResources.AxamlSettingsUI.NumberAutoWidthReserve;
    public static CornerRadius SpinnerButtonCornerRadius =>
        AXAMLResources.AxamlSettingsUI.SpinnerButtonCornerRadius;
    public static double SpinnerGlyphFontSize => AXAMLResources.AxamlSettingsUI.SpinnerGlyphFontSize;
    public static double SectionHeaderFontSize => AXAMLResources.AxamlSettingsUI.SectionHeaderFontSize;
    public static Thickness SectionHeaderMargin => AXAMLResources.AxamlSettingsUI.SectionHeaderMargin;
    public static double SubsectionHeaderFontSize => AXAMLResources.AxamlSettingsUI.SubsectionHeaderFontSize;
    public static Thickness SubsectionHeaderMargin => AXAMLResources.AxamlSettingsUI.SubsectionHeaderMargin;
    public static double TitleFontSize => AXAMLResources.AxamlSettingsUI.TitleFontSize;
    public static double DescriptionFontSize => AXAMLResources.AxamlSettingsUI.DescriptionFontSize;
    public static double DescriptionOpacity => AXAMLResources.AxamlSettingsUI.DescriptionOpacity;
    public static Thickness DescriptionMargin => AXAMLResources.AxamlSettingsUI.DescriptionMargin;
    public static Thickness RightControlMargin => AXAMLResources.AxamlSettingsUI.RightControlMargin;
    public static CornerRadius CardCornerRadius => AXAMLResources.AxamlSettingsUI.CardCornerRadius;
    public static Thickness CardPadding => AXAMLResources.AxamlSettingsUI.CardPadding;
    public static Thickness CardMargin => AXAMLResources.AxamlSettingsUI.CardMargin;
    public static double TextBoxHeight => AXAMLResources.AxamlSettingsUI.TextBoxHeight;
    public static double TextBoxFontSize => AXAMLResources.AxamlSettingsUI.TextBoxFontSize;
    public static Thickness TextBoxBorderThickness => AXAMLResources.AxamlSettingsUI.TextBoxBorderThickness;
    public static Thickness TextBoxPadding => AXAMLResources.AxamlSettingsUI.TextBoxPadding;
    public static double CaptionButtonWidth => AXAMLResources.AxamlSettingsUI.CaptionButtonWidth;
    public static double CaptionButtonHeight => AXAMLResources.AxamlSettingsUI.CaptionButtonHeight;
    public static double CaptionButtonGlyphFontSize => AXAMLResources.AxamlSettingsUI.CaptionButtonGlyphFontSize;
}

/// <summary>
/// A mutable palette color backed by one shared brush.
/// Updating the color invalidates only controls that use that brush.
/// </summary>
public readonly struct SettingsPaletteColor
{
    private readonly SettingsPalette _owner;
    private readonly int _index;

    internal SettingsPaletteColor(SettingsPalette owner, int index)
    {
        _owner = owner;
        _index = index;
    }

    public Color Value => _owner.GetColor(_index);
    public byte A => Value.A;
    public byte R => Value.R;
    public byte G => Value.G;
    public byte B => Value.B;
    internal SolidColorBrush Brush => _owner.GetBrush(_index);

    public static implicit operator Color(SettingsPaletteColor color) => color.Value;
}

/// <summary>
/// Settings colors shared by every control in one visual surface.
/// </summary>
public sealed class SettingsPalette
{
    private const int BackgroundIndex = 0;
    private const int ForegroundIndex = 1;
    private const int BorderIndex = 2;
    private const int HoverIndex = 3;
    private const int PressedIndex = 4;
    private const int CardBackgroundIndex = 5;
    private const int ControlBackgroundIndex = 6;
    private const int SecondaryForegroundIndex = 7;
    private const int DisabledForegroundIndex = 8;
    private const int AccentIndex = 9;
    private const int ToggleOnTrackIndex = 10;
    private const int ToggleOnThumbIndex = 11;
    private const int TextBoxFocusedIndex = 12;
    private const int SearchListItemSelectedIndex = 13;
    private const int SearchListItemHoverIndex = 14;
    private const int SliderProgressIndex = 15;
    private const int SliderTrackIndex = 16;
    private const int SliderThumbIndex = 17;
    private const int CloseButtonHoverIndex = 18;
    private const int CloseButtonPressedIndex = 19;
    private const int CloseButtonGlyphActiveIndex = 20;
    private const int HoverDeepIndex = 21;
    private const int PressedDeepIndex = 22;
    private const int ControlBackgroundDeepIndex = 23;
    private const int ColorCount = 24;

    private readonly Color[] _colors;
    private SolidColorBrush?[]? _brushes;

    public SettingsPalette(
        Color background,
        Color foreground,
        Color border,
        Color hover,
        Color pressed,
        Color cardBackground,
        Color controlBackground,
        Color secondaryForeground,
        Color disabledForeground,
        Color accent,
        Color toggleOnTrack,
        Color toggleOnThumb,
        Color textBoxFocused,
        Color searchListItemSelected,
        Color searchListItemHover,
        Color sliderProgress,
        Color sliderTrack,
        Color sliderThumb,
        Color closeButtonHover,
        Color closeButtonPressed,
        Color closeButtonGlyphActive,
        Color? hoverDeep = null,
        Color? pressedDeep = null,
        Color? controlBackgroundDeep = null)
    {
        _colors = new Color[ColorCount];
        _colors[BackgroundIndex] = background;
        _colors[ForegroundIndex] = foreground;
        _colors[BorderIndex] = border;
        _colors[HoverIndex] = hover;
        _colors[PressedIndex] = pressed;
        _colors[CardBackgroundIndex] = cardBackground;
        _colors[ControlBackgroundIndex] = controlBackground;
        _colors[SecondaryForegroundIndex] = secondaryForeground;
        _colors[DisabledForegroundIndex] = disabledForeground;
        _colors[AccentIndex] = accent;
        _colors[ToggleOnTrackIndex] = toggleOnTrack;
        _colors[ToggleOnThumbIndex] = toggleOnThumb;
        _colors[TextBoxFocusedIndex] = textBoxFocused;
        _colors[SearchListItemSelectedIndex] = searchListItemSelected;
        _colors[SearchListItemHoverIndex] = searchListItemHover;
        _colors[SliderProgressIndex] = sliderProgress;
        _colors[SliderTrackIndex] = sliderTrack;
        _colors[SliderThumbIndex] = sliderThumb;
        _colors[CloseButtonHoverIndex] = closeButtonHover;
        _colors[CloseButtonPressedIndex] = closeButtonPressed;
        _colors[CloseButtonGlyphActiveIndex] = closeButtonGlyphActive;
        _colors[HoverDeepIndex] = hoverDeep ?? hover;
        _colors[PressedDeepIndex] = pressedDeep ?? pressed;
        _colors[ControlBackgroundDeepIndex] = controlBackgroundDeep ?? controlBackground;
    }

    public SettingsPaletteColor Background => new(this, BackgroundIndex);
    public SettingsPaletteColor Foreground => new(this, ForegroundIndex);
    public SettingsPaletteColor Border => new(this, BorderIndex);
    public SettingsPaletteColor Hover => new(this, HoverIndex);
    public SettingsPaletteColor HoverDeep => new(this, HoverDeepIndex);
    public SettingsPaletteColor Pressed => new(this, PressedIndex);
    public SettingsPaletteColor PressedDeep => new(this, PressedDeepIndex);
    public SettingsPaletteColor CardBackground => new(this, CardBackgroundIndex);
    public SettingsPaletteColor ControlBackground => new(this, ControlBackgroundIndex);
    public SettingsPaletteColor ControlBackgroundDeep => new(this, ControlBackgroundDeepIndex);
    public SettingsPaletteColor SecondaryForeground => new(this, SecondaryForegroundIndex);
    public SettingsPaletteColor DisabledForeground => new(this, DisabledForegroundIndex);
    public SettingsPaletteColor Accent => new(this, AccentIndex);
    public SettingsPaletteColor ToggleOnTrack => new(this, ToggleOnTrackIndex);
    public SettingsPaletteColor ToggleOnThumb => new(this, ToggleOnThumbIndex);
    public SettingsPaletteColor TextBoxFocused => new(this, TextBoxFocusedIndex);
    public SettingsPaletteColor SearchListItemSelected => new(this, SearchListItemSelectedIndex);
    public SettingsPaletteColor SearchListItemHover => new(this, SearchListItemHoverIndex);
    public SettingsPaletteColor SliderProgress => new(this, SliderProgressIndex);
    public SettingsPaletteColor SliderTrack => new(this, SliderTrackIndex);
    public SettingsPaletteColor SliderThumb => new(this, SliderThumbIndex);
    public SettingsPaletteColor CloseButtonHover => new(this, CloseButtonHoverIndex);
    public SettingsPaletteColor CloseButtonPressed => new(this, CloseButtonPressedIndex);
    public SettingsPaletteColor CloseButtonGlyphActive => new(this, CloseButtonGlyphActiveIndex);
    public SettingsPaletteColor Separator => Border;
    public SettingsPaletteColor ControlBorder => Border;
    public SettingsPaletteColor ButtonHover => Hover;
    public SettingsPaletteColor ButtonPressed => Pressed;
    public SettingsPaletteColor IconForeground => Foreground;
    public SettingsPaletteColor FooterBackground => Background;

    /// <summary>
    /// Updates the shared brushes without replacing the controls that reference them.
    /// </summary>
    public void UpdateFrom(SettingsPalette source)
    {
        ArgumentNullException.ThrowIfNull(source);

        for (int colorIndex = 0; colorIndex < ColorCount; colorIndex++)
        {
            Color color = source._colors[colorIndex];
            if (_colors[colorIndex] == color) continue;

            _colors[colorIndex] = color;
            if (_brushes?[colorIndex] is { } brush)
                brush.Color = color;
        }
    }

    /// <summary>
    /// Creates an independent palette for secondary windows that must not follow live previews.
    /// </summary>
    public SettingsPalette Snapshot() =>
        new(
            Background,
            Foreground,
            Border,
            Hover,
            Pressed,
            CardBackground,
            ControlBackground,
            SecondaryForeground,
            DisabledForeground,
            Accent,
            ToggleOnTrack,
            ToggleOnThumb,
            TextBoxFocused,
            SearchListItemSelected,
            SearchListItemHover,
            SliderProgress,
            SliderTrack,
            SliderThumb,
            CloseButtonHover,
            CloseButtonPressed,
            CloseButtonGlyphActive,
            hoverDeep: HoverDeep,
            pressedDeep: PressedDeep,
            controlBackgroundDeep: ControlBackgroundDeep);

    internal Color GetColor(int index) => _colors[index];

    internal SolidColorBrush GetBrush(int index)
    {
        _brushes ??= new SolidColorBrush?[ColorCount];
        SolidColorBrush? brush = _brushes[index];
        if (brush != null) return brush;

        brush = new SolidColorBrush(_colors[index]);
        _brushes[index] = brush;
        return brush;
    }
}

/// <summary>Allows a custom settings-navigation icon to follow live palette changes.</summary>
public interface ISettingsNavigationIcon
{
    Color IconColor { get; set; }
}

public sealed class SettingsNavItem : Border
{
    private readonly SettingsPalette _palette;
    private readonly Border _outer;
    private readonly Border _indicator;
    private readonly IBrush _selectedIndicatorBrush;
    private readonly ISettingsNavigationIcon? _customNavigationIcon;
    private bool _isPointerOver;
    private bool _isSelected;

    public SettingsNavItem(
        string text,
        SettingsPalette palette,
        CornerRadius? indicatorRadius = null,
        CornerRadius? itemRadius = null,
        bool useWindows11Style = false,
        Glyph? navigationGlyph = null,
        Control? customNavigationIcon = null,
        double navigationIconScale = 1.0,
        ITransform? navigationIconTransform = null)
    {
        _palette = palette;
        Background = Brushes.Transparent;
        Margin = SettingsUILayout.NavItemMargin;
        Cursor = TrayAppDotNETCursors.Hand;
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        _indicator = new Border
        {
            Width = SettingsUILayout.NavIndicatorWidth,
            Height = SettingsUILayout.NavIndicatorHeight,
            CornerRadius = indicatorRadius ?? SettingsUILayout.NavIndicatorCornerRadius,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        DebugUIProvenance.RecordBuilder(_indicator);
        _selectedIndicatorBrush = useWindows11Style
            ? TrayAppDotNETSettingsUI.Brush(SettingsUILayout.Windows11NavIndicatorColor)
            : TrayAppDotNETSettingsUI.Brush(_palette.Foreground);

        TextBlock label = TrayAppDotNETSettingsUI.Text(text, palette);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Left;

        Grid row;
        Thickness itemPadding;
        if (useWindows11Style)
        {
            Control? navigationIcon = customNavigationIcon ?? CreateNavigationGlyph(navigationGlyph, palette);
            _customNavigationIcon = navigationIcon as ISettingsNavigationIcon;
            row = CreateWindows11Content(
                label,
                navigationIcon,
                navigationIconScale,
                navigationIconTransform);
            itemPadding = SettingsUILayout.Windows11NavItemPadding;
        }
        else
        {
            _indicator.Margin = SettingsUILayout.NavIndicatorMargin;
            DebugUIProvenance.RecordBuilder(_indicator);
            row = CreateClassicContent(label);
            itemPadding = SettingsUILayout.NavItemPadding;
        }

        _outer = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = itemRadius ?? SettingsUILayout.NavItemCornerRadius,
            Padding = itemPadding,
            Margin = SettingsUILayout.NavItemInnerMargin,
            Child = row
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

        DebugUIProvenance.RecordBuilder(this);
        DebugUIProvenance.RecordBuilder(label);
        DebugUIProvenance.RecordBuilder(_outer);
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

    /// <summary>Refreshes custom icon colors that cannot bind to the shared palette brush.</summary>
    internal void RefreshPalette()
    {
        if (_customNavigationIcon != null)
            _customNavigationIcon.IconColor = _palette.Foreground;

        UpdateVisual();
    }

    private void UpdateVisual()
    {
        _outer.Background =
            TrayAppDotNETSettingsUI.Brush(_isSelected || _isPointerOver ? _palette.Hover : Colors.Transparent);
        _indicator.Background = _isSelected
            ? _selectedIndicatorBrush
            : Brushes.Transparent;
        DebugUIProvenance.RecordBuilder(_outer);
        DebugUIProvenance.RecordBuilder(_indicator);
    }

    private Grid CreateClassicContent(TextBlock label)
    {
        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.Children.Add(_indicator);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }

    private Grid CreateWindows11Content(
        TextBlock label,
        Control? navigationIcon,
        double navigationIconScale,
        ITransform? navigationIconTransform)
    {
        Grid row = new();
        row.Children.Add(_indicator);

        Grid content = new() { Margin = SettingsUILayout.Windows11NavContentMargin };
        content.ColumnDefinitions.Add(new ColumnDefinition(
            new GridLength(SettingsUILayout.Windows11NavIconColumnWidth)));
        content.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        if (navigationIcon != null)
        {
            double navigationIconSize = SettingsUILayout.Windows11NavIconSize * navigationIconScale;
            navigationIcon.Width = navigationIconSize;
            navigationIcon.Height = navigationIconSize;
            navigationIcon.HorizontalAlignment = HorizontalAlignment.Center;
            navigationIcon.VerticalAlignment = VerticalAlignment.Center;
            navigationIcon.Margin = SettingsUILayout.Windows11NavIconMargin;
            navigationIcon.RenderTransform = navigationIconTransform;
            content.Children.Add(navigationIcon);
        }

        label.Margin = SettingsUILayout.Windows11NavLabelMargin;
        Grid.SetColumn(label, 1);
        content.Children.Add(label);
        row.Children.Add(content);
        return row;
    }

    private static TextBlock? CreateNavigationGlyph(Glyph? glyph, SettingsPalette palette)
    {
        if (glyph == null) return null;

        TextBlock icon = TrayAppDotNETSettingsUI.Text(
            string.Empty,
            palette,
            SettingsUILayout.Windows11NavIconFontSize);
        icon.TextAlignment = TextAlignment.Center;
        GlyphApplicator.ApplyTo(icon, glyph);
        return icon;
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
        Cursor = TrayAppDotNETCursors.Hand;
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        Border indicator = new()
        {
            Width = SettingsUILayout.NavIndicatorWidth,
            Height = SettingsUILayout.NavIndicatorHeight,
            CornerRadius = indicatorRadius ?? SettingsUILayout.NavIndicatorCornerRadius,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = SettingsUILayout.NavIndicatorMargin
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
            CornerRadius = itemRadius ?? SettingsUILayout.NavItemCornerRadius,
            Padding = SettingsUILayout.NavItemPadding,
            Child = row
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
            bool releasedInside = TrayAppDotNETFlyoutUI.IsPointerInside(this, e);
            bool clicked = _isPressed && releasedInside;
            _isPointerOver = releasedInside;
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

        DebugUIProvenance.RecordBuilder(this);
        DebugUIProvenance.RecordBuilder(label);
        DebugUIProvenance.RecordBuilder(indicator);
        DebugUIProvenance.RecordBuilder(_outer);
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
        DebugUIProvenance.RecordBuilder(_outer);
    }
}

public sealed class SettingsButton : Border
{
    private readonly TextBlock _label;
    private readonly bool _transparentBase;
    private readonly SettingsPaletteColor _normalBackground;
    private readonly SettingsPaletteColor _hoverBackground;
    private readonly SettingsPaletteColor _pressedBackground;
    private bool _isPointerOver;
    private bool _isPressed;

    public SettingsButton(string text, SettingsPalette palette, bool transparentBase = false, bool navGutter = false)
        : this(
            text,
            palette,
            palette.ControlBackground,
            palette.Hover,
            palette.Pressed,
            transparentBase,
            navGutter)
    {
    }

    internal SettingsButton(
        string text,
        SettingsPalette palette,
        SettingsPaletteColor normalBackground,
        SettingsPaletteColor hoverBackground,
        SettingsPaletteColor pressedBackground,
        bool transparentBase = false,
        bool navGutter = false)
    {
        _transparentBase = transparentBase;
        _normalBackground = normalBackground;
        _hoverBackground = hoverBackground;
        _pressedBackground = pressedBackground;
        _label = TrayAppDotNETSettingsUI.Text(text, palette);
        _label.HorizontalAlignment = navGutter ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;

        Background = transparentBase
            ? Brushes.Transparent
            : TrayAppDotNETSettingsUI.Brush(_normalBackground);
        CornerRadius = SettingsUILayout.ButtonCornerRadius;
        MinHeight = SettingsUILayout.ButtonMinHeight;
        Padding = SettingsUILayout.ButtonPadding;
        Cursor = TrayAppDotNETCursors.Hand;
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
            bool releasedInside = TrayAppDotNETFlyoutUI.IsPointerInside(this, e);
            bool clicked = _isPressed && releasedInside;
            _isPointerOver = releasedInside;
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

        DebugUIProvenance.RecordBuilder(this);
        DebugUIProvenance.RecordBuilder(_label);
    }

    /// <summary>
    /// Creates a settings button label from a glyph object.
    /// </summary>
    public SettingsButton(Glyph glyph, SettingsPalette palette, bool transparentBase = false, bool navGutter = false)
        : this(glyph.Text, palette, transparentBase, navGutter)
    {
        GlyphApplicator.ApplyTo(_label, glyph);
    }

    internal SettingsButton(
        Glyph glyph,
        SettingsPalette palette,
        SettingsPaletteColor normalBackground,
        SettingsPaletteColor hoverBackground,
        SettingsPaletteColor pressedBackground,
        bool transparentBase = false,
        bool navGutter = false)
        : this(
            glyph.Text,
            palette,
            normalBackground,
            hoverBackground,
            pressedBackground,
            transparentBase,
            navGutter)
    {
        GlyphApplicator.ApplyTo(_label, glyph);
    }

    public event EventHandler? Click;

    public TextBlock Label => _label;

    /// <summary>Gets or sets whether this button closes its containing settings window.</summary>
    public bool IsSettingsWindowCloseButton { get; set; }

    /// <summary>Gets or sets whether this button minimizes its containing settings window.</summary>
    public bool IsSettingsWindowMinimizeButton { get; set; }

    public string Text
    {
        get => _label.Text ?? string.Empty;
        set => _label.Text = value;
    }

    private void UpdateVisual()
    {
        Opacity = IsEnabled ? SettingsUILayout.EnabledOpacity : SettingsUILayout.DisabledOpacity;
        if (_isPressed)
            Background = TrayAppDotNETSettingsUI.Brush(_pressedBackground);
        else if (_isPointerOver)
            Background = TrayAppDotNETSettingsUI.Brush(_hoverBackground);
        else
        {
            Background = _transparentBase
                ? Brushes.Transparent
                : TrayAppDotNETSettingsUI.Brush(_normalBackground);
        }
        DebugUIProvenance.RecordBuilder(this);
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
            CornerRadius = SettingsUILayout.NavIndicatorCornerRadius,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = SettingsUILayout.NavIndicatorMargin
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
        Cursor = TrayAppDotNETCursors.Hand;
        Focusable = true;

        Grid grid = new()
        {
            Width = SettingsUILayout.ToggleWidth, Height = SettingsUILayout.ToggleHeight, IsHitTestVisible = false
        };
        _track = new Border
        {
            Width = SettingsUILayout.ToggleWidth,
            Height = SettingsUILayout.ToggleHeight,
            CornerRadius = SettingsUILayout.ToggleTrackCornerRadius,
            BorderThickness = SettingsUILayout.ToggleBorderThickness,
            IsHitTestVisible = false
        };
        _thumb = new Border
        {
            Width = SettingsUILayout.ToggleThumbWidth,
            Height = SettingsUILayout.ToggleThumbHeight,
            CornerRadius = SettingsUILayout.ToggleThumbCornerRadius,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = SettingsUILayout.ToggleThumbUncheckedMargin,
            IsHitTestVisible = false
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
        DebugUIProvenance.RecordBuilder(this);
        DebugUIProvenance.RecordBuilder(grid);
        DebugUIProvenance.RecordBuilder(_track);
        DebugUIProvenance.RecordBuilder(_thumb);
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
            DebugUIProvenance.RecordBuilder(this);
            DebugUIProvenance.RecordBuilder(_track);
            DebugUIProvenance.RecordBuilder(_thumb);
            return;
        }

        _track.Background = Brushes.Transparent;
        _track.BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.Foreground);
        _thumb.Background = TrayAppDotNETSettingsUI.Brush(_palette.Foreground);
        _thumb.Width = _isPointerOver ? SettingsUILayout.ToggleThumbHoverSize : SettingsUILayout.ToggleThumbWidth;
        _thumb.Height = _isPointerOver ? SettingsUILayout.ToggleThumbHoverSize : SettingsUILayout.ToggleThumbHeight;
        _thumb.HorizontalAlignment = HorizontalAlignment.Left;
        _thumb.Margin = SettingsUILayout.ToggleThumbUncheckedMargin;
        DebugUIProvenance.RecordBuilder(this);
        DebugUIProvenance.RecordBuilder(_track);
        DebugUIProvenance.RecordBuilder(_thumb);
    }
}

public sealed class SettingsSwatch : Border
{
    private readonly SettingsPalette _palette;
    private readonly SolidColorBrush _colorBrush = new(Colors.Transparent);
    private bool _isPointerOver;

    public SettingsSwatch(SettingsPalette palette)
    {
        _palette = palette;
        Width = SettingsUILayout.SwatchWidth;
        Height = SettingsUILayout.SwatchHeight;
        CornerRadius = SettingsUILayout.SwatchCornerRadius;
        Background = _colorBrush;
        BorderThickness = SettingsUILayout.SwatchBorderThickness;
        BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border);
        Margin = SettingsUILayout.SwatchMargin;
        Cursor = TrayAppDotNETCursors.Hand;
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

        DebugUIProvenance.RecordBuilder(this);
    }

    public event EventHandler? Click;

    public void SetColor(Color? color, Color fallback)
    {
        _colorBrush.Color = color ?? fallback;
        Opacity = color.HasValue ? SettingsUILayout.EnabledOpacity : SettingsUILayout.SwatchFallbackOpacity;
        DebugUIProvenance.RecordBuilder(_colorBrush);
        DebugUIProvenance.RecordBuilder(this);
    }

    private void UpdateBorder() =>
        BorderBrush = TrayAppDotNETSettingsUI.Brush(_isPointerOver ? _palette.Accent : _palette.Border);
}

/// <summary>Dimensions and colors for a TADN-painted scrollbar.</summary>
public readonly record struct SettingsScrollBarStyle(
    double TrackThickness,
    double IdleThumbThickness,
    double HoverThumbThickness,
    double ThumbEndMargin,
    double MinimumThumbLength,
    Color TrackColor,
    Color IdleThumbColor,
    Color HoverThumbColor,
    Color DragThumbColor,
    Color ArrowColor,
    bool ShowButtonsOnHover);

public sealed class SettingsScrollHost : Grid, IDisposable
{
    private readonly Border _contentHost;
    private readonly ScrollViewer _scrollViewer;
    private readonly SettingsScrollBar _scrollBar;
    private int _disposed;

    public SettingsScrollHost(Control content, SettingsPalette palette, Thickness padding)
    {
        Background = TrayAppDotNETSettingsUI.Brush(palette.Background);
        ClipToBounds = true;

        _contentHost = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background), Padding = padding, Child = content
        };

        _scrollViewer = new ScrollViewer
        {
            Content = _contentHost,
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        Children.Add(_scrollViewer);

        _scrollBar = new SettingsScrollBar(palette)
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Stretch
        };
        _scrollBar.Attach(_scrollViewer);
        Children.Add(_scrollBar);
    }

    public double VerticalOffset => _scrollViewer.Offset.Y;

    public double ViewportHeight => _scrollViewer.Viewport.Height;

    /// <summary>Enables outer page scrolling or constrains content that owns its own scroll viewport.</summary>
    public void SetContentScrollingEnabled(bool isEnabled)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _scrollViewer.VerticalScrollBarVisibility = isEnabled
            ? ScrollBarVisibility.Hidden
            : ScrollBarVisibility.Disabled;
        _scrollBar.IsVisible = isEnabled;
        if (!isEnabled)
            _scrollViewer.Offset = default;
    }

    /// <summary>Replaces the scrollable content without rebuilding the scroll host.</summary>
    public void SetContent(Control content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Control? previousContent = _contentHost.Child;
        if (ReferenceEquals(previousContent, content)) return;

        if (previousContent != null)
            TextBlockLayoutLifetime.ReleaseForRetirement(previousContent);
        _contentHost.Child = content;
    }

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
            _scrollViewer.Offset.Y - e.Delta.Y * SettingsUILayout.ScrollWheelStep,
            0,
            maxOffset);
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, next);
        e.Handled = true;
    }

    private double MaxOffset => Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        TextBlockLayoutLifetime.ReleaseForRetirement(this);
        _scrollBar.Dispose();
        _contentHost.Child = null;
        _scrollViewer.Content = null;
        Children.Clear();
    }

}

/// <summary>Vertical scroll viewport with a reserved right track and a TADN-painted scrollbar.</summary>
public sealed class SettingsVerticalScrollViewport : Grid, IDisposable
{
    private readonly Border _contentHost;
    private readonly ScrollViewer _scrollViewer;
    private readonly SettingsScrollBar _scrollBar;
    private double _lastVerticalOffset;
    private int _disposed;

    public SettingsVerticalScrollViewport(
        Control content,
        Thickness padding,
        Color background,
        SettingsScrollBarStyle scrollBarStyle,
        TrayMenuWindowOptions contextMenuOptions)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(contextMenuOptions);
        if (scrollBarStyle.TrackThickness <= 0)
            throw new ArgumentOutOfRangeException(nameof(scrollBarStyle), "Track thickness must be positive.");

        IBrush backgroundBrush = TrayAppDotNETSettingsUI.Brush(background);
        Background = backgroundBrush;
        ClipToBounds = true;
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));

        _contentHost = new Border
        {
            Background = backgroundBrush,
            Padding = padding,
            Child = content
        };
        _scrollViewer = new ScrollViewer
        {
            Background = backgroundBrush,
            Content = _contentHost,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        _scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
        Children.Add(_scrollViewer);

        _scrollBar = new SettingsScrollBar(
            Orientation.Vertical,
            scrollBarStyle,
            TrayAppDotNETCursors.Arrow,
            contextMenuOptions);
        _scrollBar.Attach(_scrollViewer);
        Grid.SetColumn(_scrollBar, 1);
        Children.Add(_scrollBar);
    }

    public double VerticalOffset => _scrollViewer.Offset.Y;

    public double ViewportHeight => _scrollViewer.Viewport.Height;

    /// <summary>Raised after wheel, thumb, or programmatic scrolling changes the vertical offset.</summary>
    public event EventHandler? VerticalOffsetChanged;

    /// <summary>Moves the viewport to a clamped vertical offset.</summary>
    public void SetVerticalOffset(double offset)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        double maximumOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        double nextOffset = maximumOffset <= 0 ? 0 : Math.Clamp(offset, 0, maximumOffset);
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, nextOffset);
    }

    /// <summary>Applies new painted-scrollbar visuals without replacing the scroll viewport.</summary>
    public void SetScrollBarStyle(SettingsScrollBarStyle scrollBarStyle)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _scrollBar.SetStyle(scrollBarStyle);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs eventArgs)
    {
        double maximumOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        if (maximumOffset <= 0)
        {
            base.OnPointerWheelChanged(eventArgs);
            return;
        }

        double nextOffset = Math.Clamp(
            _scrollViewer.Offset.Y - eventArgs.Delta.Y * SettingsUILayout.ScrollWheelStep,
            0,
            maximumOffset);
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, nextOffset);
        eventArgs.Handled = true;
    }

    private void OnScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs eventArgs)
    {
        double verticalOffset = _scrollViewer.Offset.Y;
        if (verticalOffset.Equals(_lastVerticalOffset)) return;

        _lastVerticalOffset = verticalOffset;
        VerticalOffsetChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        TextBlockLayoutLifetime.ReleaseForRetirement(this);
        _scrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
        VerticalOffsetChanged = null;
        _scrollBar.Dispose();
        _contentHost.Child = null;
        _scrollViewer.Content = null;
        Children.Clear();
    }
}

/// <summary>Two-axis scroll viewport with reserved tracks and TADN-painted scrollbars.</summary>
public sealed class SettingsScrollViewport : Grid, IDisposable
{
    private readonly Border _contentHost;
    private readonly ScrollViewer _scrollViewer;
    private readonly SettingsScrollBar _verticalScrollBar;
    private readonly SettingsScrollBar _horizontalScrollBar;
    private readonly Border _cornerHost;
    private int _disposed;

    public SettingsScrollViewport(
        Control content,
        Thickness padding,
        Color background,
        SettingsScrollBarStyle scrollBarStyle,
        TrayMenuWindowOptions contextMenuOptions,
        Control? cornerContent = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(contextMenuOptions);
        if (scrollBarStyle.TrackThickness <= 0)
            throw new ArgumentOutOfRangeException(nameof(scrollBarStyle), "Track thickness must be positive.");

        IBrush backgroundBrush = TrayAppDotNETSettingsUI.Brush(background);
        Background = backgroundBrush;
        ClipToBounds = true;
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        _contentHost = new Border
        {
            Background = backgroundBrush,
            Padding = padding,
            Child = content
        };
        _scrollViewer = new ScrollViewer
        {
            Background = backgroundBrush,
            Content = _contentHost,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        Children.Add(_scrollViewer);

        _verticalScrollBar = new SettingsScrollBar(
            Orientation.Vertical,
            scrollBarStyle,
            TrayAppDotNETCursors.Arrow,
            contextMenuOptions);
        _verticalScrollBar.Attach(_scrollViewer);
        Grid.SetColumn(_verticalScrollBar, 1);
        Children.Add(_verticalScrollBar);

        _horizontalScrollBar = new SettingsScrollBar(
            Orientation.Horizontal,
            scrollBarStyle,
            TrayAppDotNETCursors.Arrow,
            contextMenuOptions);
        _horizontalScrollBar.Attach(_scrollViewer);
        Grid.SetRow(_horizontalScrollBar, 1);
        Children.Add(_horizontalScrollBar);

        _cornerHost = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(scrollBarStyle.TrackColor),
            Child = cornerContent
        };
        _cornerHost.PointerEntered += OnCornerPointerEntered;
        _cornerHost.PointerExited += OnCornerPointerExited;
        Grid.SetColumn(_cornerHost, 1);
        Grid.SetRow(_cornerHost, 1);
        Children.Add(_cornerHost);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs eventArgs)
    {
        bool useHorizontal =
            (eventArgs.KeyModifiers & KeyModifiers.Shift) != 0 ||
            Math.Abs(eventArgs.Delta.X) > Math.Abs(eventArgs.Delta.Y);
        double verticalMaximum = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        double horizontalMaximum = Math.Max(0, _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width);
        if (useHorizontal && horizontalMaximum > 0)
        {
            double delta = eventArgs.Delta.X != 0 ? eventArgs.Delta.X : eventArgs.Delta.Y;
            double next = Math.Clamp(
                _scrollViewer.Offset.X - delta * SettingsUILayout.ScrollWheelStep,
                0,
                horizontalMaximum);
            _scrollViewer.Offset = new Vector(next, _scrollViewer.Offset.Y);
            eventArgs.Handled = true;
            return;
        }

        if (verticalMaximum > 0)
        {
            double next = Math.Clamp(
                _scrollViewer.Offset.Y - eventArgs.Delta.Y * SettingsUILayout.ScrollWheelStep,
                0,
                verticalMaximum);
            _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, next);
            eventArgs.Handled = true;
            return;
        }

        base.OnPointerWheelChanged(eventArgs);
    }

    /// <summary>Applies new painted-scrollbar visuals without replacing the scroll viewport.</summary>
    public void SetScrollBarStyle(SettingsScrollBarStyle scrollBarStyle)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _verticalScrollBar.SetStyle(scrollBarStyle);
        _horizontalScrollBar.SetStyle(scrollBarStyle);
        _cornerHost.Background = TrayAppDotNETSettingsUI.Brush(scrollBarStyle.TrackColor);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        TextBlockLayoutLifetime.ReleaseForRetirement(this);
        _verticalScrollBar.Dispose();
        _horizontalScrollBar.Dispose();
        _cornerHost.PointerEntered -= OnCornerPointerEntered;
        _cornerHost.PointerExited -= OnCornerPointerExited;
        _cornerHost.Child = null;
        _contentHost.Child = null;
        _scrollViewer.Content = null;
        Children.Clear();
    }

    private void OnCornerPointerEntered(object? sender, PointerEventArgs eventArgs)
    {
        _verticalScrollBar.SetExternalExpansion(true);
        _horizontalScrollBar.SetExternalExpansion(true);
    }

    private void OnCornerPointerExited(object? sender, PointerEventArgs eventArgs)
    {
        _verticalScrollBar.SetExternalExpansion(false);
        _horizontalScrollBar.SetExternalExpansion(false);
    }
}

internal sealed class SettingsScrollBar : Control, IDisposable
{
    private const string ScrollHereText = "Scroll Here";
    private const string TopText = "Top";
    private const string BottomText = "Bottom";
    private const string PageUpText = "Page Up";
    private const string PageDownText = "Page Down";
    private const string ScrollUpText = "Scroll Up";
    private const string ScrollDownText = "Scroll Down";
    private const string LeftEdgeText = "Left Edge";
    private const string RightEdgeText = "Right Edge";
    private const string PageLeftText = "Page Left";
    private const string PageRightText = "Page Right";
    private const string ScrollLeftText = "Scroll Left";
    private const string ScrollRightText = "Scroll Right";

    private readonly Orientation _orientation;
    private readonly TrayMenuWindowOptions _contextMenuOptions;
    private SettingsScrollBarStyle _style;
    private IBrush _trackBrush;
    private IBrush _idleThumbBrush;
    private IBrush _hoverThumbBrush;
    private IBrush _dragThumbBrush;
    private Pen _arrowPen;
    private ScrollViewer? _viewer;
    private bool _isPointerOver;
    private bool _isDragging;
    private bool _isExternallyExpanded;
    private double _dragOffset;
    private IPointer? _capturedPointer;
    private TrayMenuWindow? _contextMenuWindow;
    private int _disposed;

    public SettingsScrollBar(SettingsPalette palette)
        : this(
            Orientation.Vertical,
            CreateDefaultStyle(palette),
            TrayAppDotNETCursors.Hand,
            CreateDefaultContextMenuOptions(palette))
    {
    }

    public SettingsScrollBar(
        Orientation orientation,
        SettingsScrollBarStyle style,
        Cursor cursor,
        TrayMenuWindowOptions contextMenuOptions)
    {
        ArgumentNullException.ThrowIfNull(contextMenuOptions);
        if (style.TrackThickness <= 0)
            throw new ArgumentOutOfRangeException(nameof(style), "Track thickness must be positive.");
        if (style.IdleThumbThickness <= 0 || style.HoverThumbThickness <= 0)
            throw new ArgumentOutOfRangeException(nameof(style), "Thumb thicknesses must be positive.");

        _orientation = orientation;
        _contextMenuOptions = contextMenuOptions;
        _style = style;
        _trackBrush = TrayAppDotNETSettingsUI.Brush(style.TrackColor);
        _idleThumbBrush = TrayAppDotNETSettingsUI.Brush(style.IdleThumbColor);
        _hoverThumbBrush = TrayAppDotNETSettingsUI.Brush(style.HoverThumbColor);
        _dragThumbBrush = TrayAppDotNETSettingsUI.Brush(style.DragThumbColor);
        _arrowPen = new Pen(TrayAppDotNETSettingsUI.Brush(style.ArrowColor), 1);
        UpdateTrackThickness();
        Cursor = cursor;
        Focusable = false;
        IsHitTestVisible = true;

        PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            UpdateTrackThickness();
            InvalidateVisual();
        };
        PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            UpdateTrackThickness();
            InvalidateVisual();
        };
    }

    public void Attach(ScrollViewer viewer)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (ReferenceEquals(_viewer, viewer)) return;

        DetachViewer();
        _viewer = viewer;
        viewer.ScrollChanged += OnViewerScrollChanged;
        viewer.EffectiveViewportChanged += OnViewerEffectiveViewportChanged;
        viewer.PropertyChanged += OnViewerPropertyChanged;
    }

    public void SetExternalExpansion(bool isExpanded)
    {
        if (_isExternallyExpanded == isExpanded) return;

        _isExternallyExpanded = isExpanded;
        UpdateTrackThickness();
        InvalidateVisual();
    }

    /// <summary>Applies a new style while preserving scroll position and pointer state.</summary>
    internal void SetStyle(SettingsScrollBarStyle style)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (style.TrackThickness <= 0)
            throw new ArgumentOutOfRangeException(nameof(style), "Track thickness must be positive.");
        if (style.IdleThumbThickness <= 0 || style.HoverThumbThickness <= 0)
            throw new ArgumentOutOfRangeException(nameof(style), "Thumb thicknesses must be positive.");

        _style = style;
        _trackBrush = TrayAppDotNETSettingsUI.Brush(style.TrackColor);
        _idleThumbBrush = TrayAppDotNETSettingsUI.Brush(style.IdleThumbColor);
        _hoverThumbBrush = TrayAppDotNETSettingsUI.Brush(style.HoverThumbColor);
        _dragThumbBrush = TrayAppDotNETSettingsUI.Brush(style.DragThumbColor);
        _arrowPen = new Pen(TrayAppDotNETSettingsUI.Brush(style.ArrowColor), 1);
        UpdateTrackThickness();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(_trackBrush, new Rect(0, 0, Bounds.Width, Bounds.Height));
        if (_viewer == null) return;

        double maxOffset = MaxOffset;
        if (maxOffset <= 0 || TrackLength <= 0) return;

        Rect thumb = ThumbRect();
        IBrush thumbBrush = _isDragging
            ? _dragThumbBrush
            : _isPointerOver
                ? _hoverThumbBrush
                : _idleThumbBrush;
        double radius = ThumbThickness / 2;
        context.FillRectangle(thumbBrush, thumb, (float)radius);
        DrawHoverButtons(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_viewer == null || MaxOffset <= 0)
        {
            base.OnPointerPressed(e);
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        Point position = e.GetPosition(this);
        if (point.Properties.IsRightButtonPressed)
        {
            ShowContextMenu(position);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            base.OnPointerPressed(e);
            return;
        }

        double pointerAxis = Axis(position);
        double buttonLength = ButtonLength;
        if (buttonLength > 0 && pointerAxis < buttonLength)
        {
            ScrollLine(-1);
            e.Handled = true;
            return;
        }
        if (buttonLength > 0 && pointerAxis >= TrackLength - buttonLength)
        {
            ScrollLine(1);
            e.Handled = true;
            return;
        }

        Rect thumb = ThumbRect();
        _isDragging = true;
        UpdateTrackThickness();
        _dragOffset = thumb.Contains(position)
            ? pointerAxis - ThumbStart(thumb)
            : ThumbLength(thumb) / 2;
        _capturedPointer = e.Pointer;
        try
        {
            e.Pointer.Capture(this);
        }
        catch
        {
            _capturedPointer = null;
            _isDragging = false;
            throw;
        }
        SetOffsetFromPointer(pointerAxis);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_isDragging)
        {
            SetOffsetFromPointer(Axis(e.GetPosition(this)));
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
            _capturedPointer = null;
            e.Pointer.Capture(null);
            UpdateTrackThickness();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        base.OnPointerReleased(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _capturedPointer = null;
        _isDragging = false;
        UpdateTrackThickness();
        InvalidateVisual();
        base.OnPointerCaptureLost(e);
    }

    private bool IsExpanded => _isPointerOver || _isDragging || _isExternallyExpanded;

    private double TrackThickness => IsExpanded
        ? _style.TrackThickness
        : Math.Max(
            _style.IdleThumbThickness,
            _style.TrackThickness - _style.HoverThumbThickness + _style.IdleThumbThickness);

    private double ThumbThickness => Math.Min(
        _style.TrackThickness,
        IsExpanded ? _style.HoverThumbThickness : _style.IdleThumbThickness);

    private double TrackLength => _orientation == Orientation.Vertical ? Bounds.Height : Bounds.Width;

    private double ButtonLength => _style.ShowButtonsOnHover ? _style.TrackThickness : 0;

    private double MaxOffset
    {
        get
        {
            if (_viewer == null) return 0;
            return _orientation == Orientation.Vertical
                ? Math.Max(0, _viewer.Extent.Height - _viewer.Viewport.Height)
                : Math.Max(0, _viewer.Extent.Width - _viewer.Viewport.Width);
        }
    }

    private Rect ThumbRect()
    {
        if (_viewer == null) return default;

        double trackLength = Math.Max(0, TrackLength);
        double viewport = _orientation == Orientation.Vertical
            ? Math.Max(0, _viewer.Viewport.Height)
            : Math.Max(0, _viewer.Viewport.Width);
        double extent = _orientation == Orientation.Vertical
            ? Math.Max(viewport, _viewer.Extent.Height)
            : Math.Max(viewport, _viewer.Extent.Width);
        double buttonLength = ButtonLength;
        double scrollingTrackLength = Math.Max(0, trackLength - buttonLength * 2);
        double thumbLength = extent <= 0
            ? 0
            : Math.Min(
                scrollingTrackLength,
                Math.Max(_style.MinimumThumbLength, scrollingTrackLength * viewport / extent));
        double available = Math.Max(0, scrollingTrackLength - thumbLength);
        double axisOffset = MaxOffset <= 0
            ? 0
            : Math.Clamp(CurrentOffset / MaxOffset * available, 0, available);
        double crossOffset = Math.Clamp(
            TrackThickness - ThumbThickness - _style.ThumbEndMargin,
            0,
            Math.Max(0, TrackThickness - ThumbThickness));
        return _orientation == Orientation.Vertical
            ? new Rect(crossOffset, buttonLength + axisOffset, ThumbThickness, thumbLength)
            : new Rect(buttonLength + axisOffset, crossOffset, thumbLength, ThumbThickness);
    }

    private void SetOffsetFromPointer(double pointerAxis)
    {
        if (_viewer == null) return;

        Rect thumb = ThumbRect();
        double buttonLength = ButtonLength;
        double available = Math.Max(0, TrackLength - buttonLength * 2 - ThumbLength(thumb));
        if (available <= 0)
        {
            SetCurrentOffset(0);
            return;
        }

        double start = Math.Clamp(pointerAxis - _dragOffset - buttonLength, 0, available);
        SetCurrentOffset(start / available * MaxOffset);
    }

    private double CurrentOffset => _viewer == null
        ? 0
        : _orientation == Orientation.Vertical
            ? _viewer.Offset.Y
            : _viewer.Offset.X;

    private void SetCurrentOffset(double offset)
    {
        if (_viewer == null) return;
        _viewer.Offset = _orientation == Orientation.Vertical
            ? new Vector(_viewer.Offset.X, offset)
            : new Vector(offset, _viewer.Offset.Y);
    }

    private void ScrollLine(int direction) =>
        SetCurrentOffset(Math.Clamp(
            CurrentOffset + direction * SettingsUILayout.ScrollWheelStep,
            0,
            MaxOffset));

    private void ScrollPage(int direction) =>
        SetCurrentOffset(Math.Clamp(
            CurrentOffset + direction * ViewportLength,
            0,
            MaxOffset));

    private void ScrollHere(double pointerAxis)
    {
        Rect thumb = ThumbRect();
        double offset = CalculateScrollHereOffset(
            pointerAxis,
            TrackLength,
            ButtonLength,
            ThumbLength(thumb),
            MaxOffset);
        SetCurrentOffset(offset);
    }

    internal static double CalculateScrollHereOffset(
        double pointerAxis,
        double trackLength,
        double buttonLength,
        double thumbLength,
        double maximumOffset)
    {
        double available = Math.Max(0, trackLength - buttonLength * 2 - thumbLength);
        if (available <= 0 || maximumOffset <= 0) return 0;

        double thumbStart = Math.Clamp(
            pointerAxis - buttonLength - thumbLength / 2,
            0,
            available);
        return thumbStart / available * maximumOffset;
    }

    internal IReadOnlyList<TrayMenuEntry> BuildContextMenuEntries(double pointerAxis)
    {
        string startText;
        string endText;
        string pageBackwardText;
        string pageForwardText;
        string lineBackwardText;
        string lineForwardText;
        switch (_orientation)
        {
            case Orientation.Vertical:
                startText = TopText;
                endText = BottomText;
                pageBackwardText = PageUpText;
                pageForwardText = PageDownText;
                lineBackwardText = ScrollUpText;
                lineForwardText = ScrollDownText;
                break;
            case Orientation.Horizontal:
                startText = LeftEdgeText;
                endText = RightEdgeText;
                pageBackwardText = PageLeftText;
                pageForwardText = PageRightText;
                lineBackwardText = ScrollLeftText;
                lineForwardText = ScrollRightText;
                break;
            default:
                throw new InvalidOperationException($"Unsupported scrollbar orientation: {_orientation}.");
        }

        TrayMenuEntryBuilder entries = new();
        entries.Add(ScrollHereText, () => ScrollHere(pointerAxis));
        entries.AddSeparator();
        entries.Add(startText, () => SetCurrentOffset(0));
        entries.Add(endText, () => SetCurrentOffset(MaxOffset));
        entries.AddSeparator();
        entries.Add(pageBackwardText, () => ScrollPage(-1));
        entries.Add(pageForwardText, () => ScrollPage(1));
        entries.AddSeparator();
        entries.Add(lineBackwardText, () => ScrollLine(-1));
        entries.Add(lineForwardText, () => ScrollLine(1));
        return entries.ToList();
    }

    private void ShowContextMenu(Point position)
    {
        if (_viewer == null || MaxOffset <= 0) return;

        PixelPoint screenPosition = this.PointToScreen(position);
        CloseContextMenu();
        TrayMenuWindow menuWindow = new(BuildContextMenuEntries(Axis(position)), _contextMenuOptions);
        _contextMenuWindow = menuWindow;
        menuWindow.Closed += OnContextMenuClosed;
        if (TopLevel.GetTopLevel(this) is Window owner)
            menuWindow.ShowAt(owner, screenPosition);
        else
            menuWindow.ShowAt(screenPosition);
    }

    private void CloseContextMenu()
    {
        TrayMenuWindow? menuWindow = _contextMenuWindow;
        if (menuWindow == null) return;

        _contextMenuWindow = null;
        menuWindow.Closed -= OnContextMenuClosed;
        menuWindow.Close();
    }

    private void OnContextMenuClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is not TrayMenuWindow menuWindow) return;

        menuWindow.Closed -= OnContextMenuClosed;
        if (ReferenceEquals(menuWindow, _contextMenuWindow)) _contextMenuWindow = null;
    }

    private double Axis(Point point) => _orientation == Orientation.Vertical ? point.Y : point.X;

    private double ThumbStart(Rect thumb) => _orientation == Orientation.Vertical ? thumb.Y : thumb.X;

    private double ThumbLength(Rect thumb) => _orientation == Orientation.Vertical ? thumb.Height : thumb.Width;

    private double ViewportLength => _viewer == null
        ? 0
        : _orientation == Orientation.Vertical
            ? Math.Max(0, _viewer.Viewport.Height)
            : Math.Max(0, _viewer.Viewport.Width);

    private void DrawHoverButtons(DrawingContext context)
    {
        if (!IsExpanded || !_style.ShowButtonsOnHover) return;

        double buttonLength = ButtonLength;
        if (buttonLength <= 0) return;

        double center = TrackThickness / 2;
        const double arrowRadius = 2.5;
        if (_orientation == Orientation.Vertical)
        {
            DrawChevron(context, center, center, arrowRadius, -1, isVertical: true);
            DrawChevron(context, center, TrackLength - center, arrowRadius, 1, isVertical: true);
            return;
        }

        DrawChevron(context, center, center, arrowRadius, -1, isVertical: false);
        DrawChevron(context, TrackLength - center, center, arrowRadius, 1, isVertical: false);
    }

    private void DrawChevron(
        DrawingContext context,
        double centerX,
        double centerY,
        double radius,
        int direction,
        bool isVertical)
    {
        if (isVertical)
        {
            double tipY = centerY + direction * radius;
            double baseY = centerY - direction * radius;
            context.DrawLine(_arrowPen, new Point(centerX - radius, baseY), new Point(centerX, tipY));
            context.DrawLine(_arrowPen, new Point(centerX, tipY), new Point(centerX + radius, baseY));
            return;
        }

        double tipX = centerX + direction * radius;
        double baseX = centerX - direction * radius;
        context.DrawLine(_arrowPen, new Point(baseX, centerY - radius), new Point(tipX, centerY));
        context.DrawLine(_arrowPen, new Point(tipX, centerY), new Point(baseX, centerY + radius));
    }

    private static SettingsScrollBarStyle CreateDefaultStyle(SettingsPalette palette)
    {
        Color sliderColor = palette.SliderProgress;
        return new SettingsScrollBarStyle(
            SettingsUILayout.ScrollBarTotalWidth,
            SettingsUILayout.ScrollBarCollapsedTrackWidth - SettingsUILayout.ScrollBarThumbMargin * 2,
            SettingsUILayout.ScrollBarTotalWidth - SettingsUILayout.ScrollBarThumbMargin * 2,
            SettingsUILayout.ScrollBarThumbMargin,
            SettingsUILayout.ScrollBarMinThumbHeight,
            Colors.Transparent,
            Color.FromArgb(140, sliderColor.R, sliderColor.G, sliderColor.B),
            Color.FromArgb(217, sliderColor.R, sliderColor.G, sliderColor.B),
            sliderColor,
            sliderColor,
            ShowButtonsOnHover: false);
    }

    private static TrayMenuWindowOptions CreateDefaultContextMenuOptions(SettingsPalette palette) =>
        new() { Palette = palette };

    private void UpdateTrackThickness()
    {
        if (_orientation == Orientation.Vertical)
        {
            Width = TrackThickness;
            return;
        }

        Height = TrackThickness;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        CloseContextMenu();
        IPointer? capturedPointer = Interlocked.Exchange(ref _capturedPointer, null);
        _isDragging = false;
        _isExternallyExpanded = false;
        if (capturedPointer != null)
        {
            try { capturedPointer.Capture(null); }
            catch (Exception exception)
            {
                TADNLog.Log($"SettingsScrollBar pointer release failed: {exception.Message}");
            }
        }

        DetachViewer();
        Cursor = null;
    }

    private void DetachViewer()
    {
        ScrollViewer? viewer = _viewer;
        _viewer = null;
        if (viewer == null) return;

        viewer.ScrollChanged -= OnViewerScrollChanged;
        viewer.EffectiveViewportChanged -= OnViewerEffectiveViewportChanged;
        viewer.PropertyChanged -= OnViewerPropertyChanged;
    }

    private void OnViewerScrollChanged(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    private void OnViewerEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e) =>
        InvalidateVisual();

    private void OnViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty
            || e.Property == ScrollViewer.ExtentProperty
            || e.Property == ScrollViewer.ViewportProperty)
            InvalidateVisual();
    }
}

public sealed class SettingsComboBoxItem : Border, IDisposable
{
    private readonly SettingsPalette _palette;
    private readonly Border _inner;
    private readonly Border _selectionBar;
    private readonly Func<Control>? _contentFactory;
    private Control? _itemContent;
    private bool _isPointerOver;
    private bool _isSelected;
    private int _disposed;

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
        Cursor = TrayAppDotNETCursors.Hand;
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        _selectionBar = new Border
        {
            Width = SettingsUILayout.ComboIndicatorWidth,
            Height = SettingsUILayout.ComboIndicatorHeight,
            CornerRadius = SettingsUILayout.ComboIndicatorCornerRadius,
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center
        };

        _itemContent = CreateContent();
        _itemContent.VerticalAlignment = VerticalAlignment.Center;

        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(SettingsUILayout.ComboIndicatorColumnWidth)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(SettingsUILayout.ComboIndicatorGapWidth)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        row.Children.Add(_selectionBar);
        Grid.SetColumn(_itemContent, 2);
        row.Children.Add(_itemContent);

        _inner = new Border
        {
            Background = Brushes.Transparent,
            CornerRadius = SettingsUILayout.ComboItemCornerRadius,
            Padding = SettingsUILayout.ComboItemInnerPadding,
            Child = row
        };
        Child = _inner;

        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        KeyDown += OnKeyDown;
    }

    public event EventHandler? Pressed;

    public string Text { get; }

    internal Control CreateSelectionContent()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Control content = CreateContent();
        content.VerticalAlignment = VerticalAlignment.Center;
        return content;
    }

    internal double MeasureContentWidth()
    {
        Control content = CreateSelectionContent();
        try
        {
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return content.DesiredSize.Width;
        }
        finally
        {
            TextBlockLayoutLifetime.ReleaseForRetirement(content);
            if (content is IDisposable disposable)
                disposable.Dispose();
        }
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        TextBlockLayoutLifetime.ReleaseForRetirement(this);
        PointerEntered -= OnPointerEntered;
        PointerExited -= OnPointerExited;
        PointerPressed -= OnPointerPressed;
        KeyDown -= OnKeyDown;
        Pressed = null;
        _inner.Child = null;
        Child = null;
        Control? itemContent = Interlocked.Exchange(ref _itemContent, null);
        if (itemContent is IDisposable disposable)
            disposable.Dispose();
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerOver = true;
        UpdateVisual();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerOver = false;
        UpdateVisual();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsEnabled) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Pressed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;
        if (e.Key is not (Key.Enter or Key.Space)) return;
        Pressed?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}

public enum SettingsComboBoxAutoSizeMode
{
    LongestItem,
    SelectedItem
}

public sealed class SettingsComboBox : Grid, IDisposable
{
    private readonly SettingsPalette _palette;
    private readonly SettingsComboBoxItemCollection _items;
    private readonly Border _surface;
    private readonly ContentControl _selectionPresenter;
    private readonly Popup _popup;
    private readonly Border _popupBorder;
    private readonly StackPanel _itemsPanel;
    private readonly SettingsScrollHost _popupScrollHost;
    private bool _autoSizeToText;
    private SettingsComboBoxAutoSizeMode _autoSizeMode;
    private bool _isPointerOver;
    private bool _isPressed;
    private bool _isDropDownOpen;
    private SettingsComboBoxItem? _selectedItem;
    private Control? _selectionContent;
    private Thickness _contentPadding = SettingsUILayout.ComboContentPadding;
    private int _disposed;

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
        Cursor = TrayAppDotNETCursors.Hand;
        Focusable = true;
        ClipToBounds = false;

        _selectionPresenter = new ContentControl
        {
            VerticalAlignment = VerticalAlignment.Center, Margin = _contentPadding, IsHitTestVisible = false
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
            CornerRadius = SettingsUILayout.ComboCornerRadius,
            Child = row
        };
        Children.Add(_surface);

        _itemsPanel = new StackPanel();
        _popupScrollHost = new SettingsScrollHost(
            _itemsPanel,
            palette,
            SettingsUILayout.ComboPopupScrollPadding)
        {
            MaxHeight = SettingsUILayout.ComboPopupMaxHeight
        };

        _popupBorder = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = SettingsUILayout.ComboPopupBorderThickness,
            CornerRadius = SettingsUILayout.ComboPopupCornerRadius,
            Padding = SettingsUILayout.ComboPopupPadding,
            Margin = SettingsUILayout.ComboPopupMargin,
            Child = _popupScrollHost
        };

        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 4,
            IsLightDismissEnabled = true,
            Child = _popupBorder
        };
        Children.Add(_popup);

        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        KeyDown += OnKeyDown;
        _popup.PropertyChanged += OnPopupPropertyChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

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
            if (Volatile.Read(ref _disposed) != 0) return;
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
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_selectedItem == value) return;

            Control? replacementContent = value?.CreateSelectionContent();
            Control? previousContent = _selectionContent;
            SettingsComboBoxItem? previousItem = _selectedItem;
            try
            {
                _selectionPresenter.Content = replacementContent;
                _selectionContent = replacementContent;
                previousItem?.IsSelected = false;
                _selectedItem = value;
                _selectedItem?.IsSelected = true;
            }
            catch
            {
                _selectionPresenter.Content = previousContent;
                _selectionContent = previousContent;
                _selectedItem = previousItem;
                previousItem?.IsSelected = true;
                if (replacementContent != null)
                    TextBlockLayoutLifetime.ReleaseForRetirement(replacementContent);
                if (replacementContent is IDisposable failedDisposable)
                    failedDisposable.Dispose();
                throw;
            }

            if (previousContent != null)
                TextBlockLayoutLifetime.ReleaseForRetirement(previousContent);
            if (previousContent is IDisposable disposable)
                disposable.Dispose();
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
        if (Volatile.Read(ref _disposed) != 0)
        {
            item.Dispose();
            return;
        }

        item.Pressed += OnItemPressed;
        RebuildPopupItemsIfOpen();
        UpdateAutoWidth();
    }

    internal void OnItemRemoved(SettingsComboBoxItem item)
    {
        item.Pressed -= OnItemPressed;
        if (ReferenceEquals(_selectedItem, item)) SelectedItem = null;
        RebuildPopupItemsIfOpen();
        item.Dispose();
        UpdateAutoWidth();
    }

    internal void OnItemReplaced(SettingsComboBoxItem oldItem, SettingsComboBoxItem newItem)
    {
        oldItem.Pressed -= OnItemPressed;
        newItem.Pressed += OnItemPressed;
        if (ReferenceEquals(_selectedItem, oldItem)) SelectedItem = null;
        oldItem.Dispose();
        RebuildPopupItemsIfOpen();
        UpdateAutoWidth();
    }

    internal void OnItemsCleared(IReadOnlyList<SettingsComboBoxItem> removedItems)
    {
        _itemsPanel.Children.Clear();
        if (_selectedItem != null) SelectedItem = null;
        foreach (SettingsComboBoxItem item in removedItems)
        {
            item.Pressed -= OnItemPressed;
            item.Dispose();
        }

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

    private void RebuildPopupItemsIfOpen()
    {
        if (_isDropDownOpen)
            RebuildPopupItems();
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

    /// <summary>Closes the popup and releases generated selection and item content.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        TextBlockLayoutLifetime.ReleaseForRetirement(this);
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        PointerEntered -= OnPointerEntered;
        PointerExited -= OnPointerExited;
        PointerPressed -= OnPointerPressed;
        PointerReleased -= OnPointerReleased;
        KeyDown -= OnKeyDown;
        _popup.PropertyChanged -= OnPopupPropertyChanged;
        _isDropDownOpen = false;
        _popup.IsOpen = false;
        _itemsPanel.Children.Clear();
        _popupScrollHost.Dispose();
        _selectionPresenter.Content = null;
        if (_selectionContent is IDisposable selectionDisposable)
            selectionDisposable.Dispose();
        _selectionContent = null;

        List<SettingsComboBoxItem> items = [.. _items];
        _items.ClearWithoutNotification();
        foreach (SettingsComboBoxItem item in items)
        {
            item.Pressed -= OnItemPressed;
            item.Dispose();
        }

        _selectedItem = null;
        SelectionChanged = null;
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerOver = true;
        UpdateSurface();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerOver = false;
        _isPressed = false;
        UpdateSurface();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsEnabled) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _isPressed = true;
        IsDropDownOpen = !IsDropDownOpen;
        Focus();
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPressed) return;

        _isPointerOver = TrayAppDotNETFlyoutUI.IsPointerInside(this, e);
        _isPressed = false;
        UpdateSurface();
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;
        switch (e.Key)
        {
            case Key.Enter:
            case Key.Space:
            case Key.Down:
                IsDropDownOpen = true;
                e.Handled = true;
                return;

            case Key.Escape when IsDropDownOpen:
                IsDropDownOpen = false;
                e.Handled = true;
                return;
        }
    }

    private void OnPopupPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Popup.IsOpenProperty || _popup.IsOpen || !_isDropDownOpen) return;

        _isDropDownOpen = false;
        UpdateSurface();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (Volatile.Read(ref _disposed) == 0)
            IsDropDownOpen = false;
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
        base.SetItem(index, item);
        owner.OnItemReplaced(old, item);
    }

    protected override void RemoveItem(int index)
    {
        SettingsComboBoxItem old = this[index];
        base.RemoveItem(index);
        owner.OnItemRemoved(old);
    }

    protected override void ClearItems()
    {
        List<SettingsComboBoxItem> removedItems = [.. this];
        base.ClearItems();
        owner.OnItemsCleared(removedItems);
    }

    internal void ClearWithoutNotification() => base.ClearItems();
}

public sealed class SettingsNumberValueChangedEventArgs(double? oldValue, double? newValue) : EventArgs
{
    public double? OldValue { get; } = oldValue;
    public double? NewValue { get; } = newValue;
}

public sealed class SettingsNumberBox : Grid, IDisposable
{
    private const int MaximumDecimalPlaces = 6;
    private const double MinimumStep = 0.000001;

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
    private double _minimum;
    private double _maximum;
    private int _decimalPlaces;
    private string _numberFormat = "0";
    private double? _value;
    private double? _valueAtTextFocus;
    private int _disposed;

    public SettingsNumberBox(
        SettingsPalette palette,
        double value,
        double min,
        double max,
        double width = 100,
        string suffix = "",
        int decimalPlaces = 0)
    {
        _palette = palette;
        _minimum = min;
        _maximum = max;
        _decimalPlaces = Math.Clamp(decimalPlaces, 0, MaximumDecimalPlaces);
        _numberFormat = CreateNumberFormat(_decimalPlaces);
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
            SelectionForegroundBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground)
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
                if (IsDecimalSeparator(c) &&
                    DecimalPlaces > 0 &&
                    !ContainsDecimalSeparator(_textBox.Text ?? string.Empty))
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
            CornerRadius = SettingsUILayout.NumberValueCornerRadius,
            Height = SettingsUILayout.NumberBoxHeight,
            Child = valueGrid
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
            double magnitude = WheelStepFromModifiers(e.KeyModifiers);
            ChangeBy(e.Delta.Y > 0 ? magnitude : -magnitude);
            e.Handled = true;
        };
        _upButton.Click += (_, _) => ChangeBy(Step);
        _downButton.Click += (_, _) => ChangeBy(-Step);

        Value = value;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        TrayAppDotNETSettingsUI.ApplyDisabledOpacity(this, 0.4);
        UpdateValueBorder();
    }

    public event EventHandler<SettingsNumberValueChangedEventArgs>? ValueChanged;

    public double Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            ClampCurrentValue();
            UpdateAutoWidth();
        }
    }

    public double Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            ClampCurrentValue();
            UpdateAutoWidth();
        }
    }

    public double Step
    {
        get;
        set => field = NormalizeStep(value);
    } = 1;

    public double WheelStep
    {
        get;
        set => field = NormalizeStep(value);
    } = 1;

    public double LargeStep
    {
        get;
        set => field = NormalizeStep(value);
    } = 10;

    public double ExtraLargeStep
    {
        get;
        set => field = NormalizeStep(value);
    } = 100;

    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set
        {
            int normalized = Math.Clamp(value, 0, MaximumDecimalPlaces);
            if (_decimalPlaces == normalized) return;

            _decimalPlaces = normalized;
            _numberFormat = CreateNumberFormat(normalized);
            SetValue(_value, raiseChanged: false);
            UpdateText();
            UpdateAutoWidth();
        }
    }

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

    private void ChangeBy(double delta)
    {
        string text = _textBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) ||
            !TryParseNumber(text, out double current))
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

            double fallback = Value == InheritValue ? Minimum : Value ?? Minimum;
            Value = Math.Clamp(fallback, Minimum, Maximum);
            return;
        }

        if (TryParseNumber(text, out double parsed))
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
        if (Volatile.Read(ref _disposed) != 0) return;
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
        return ReferenceEquals(visual, this) || visual.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, this));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        DetachOutsidePointerHandler();
        ValueChanged = null;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        DetachOutsidePointerHandler();

    private void SetValue(double? next, bool raiseChanged)
    {
        double? clamped = next.HasValue
            ? AllowInherit && (int)Math.Round(next.Value) == InheritValue
                ? InheritValue
                : NormalizeValue(next.Value)
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
                : FormatValue(_value.Value)
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

    private double ArrowStepFromModifiers(KeyModifiers modifiers)
    {
        bool ctrl = (modifiers & KeyModifiers.Control) != 0;
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        return ctrl switch
        {
            true when shift => ExtraLargeStep,
            true => LargeStep,
            _ => Step
        };
    }

    private double WheelStepFromModifiers(KeyModifiers modifiers)
    {
        bool ctrl = (modifiers & KeyModifiers.Control) != 0;
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        return ctrl switch
        {
            true when shift => ExtraLargeStep,
            true => LargeStep,
            _ => WheelStep
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
            valueText = FormatValue(Value ?? 0);
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
        TextBlock probe = new() { Text = text, FontFamily = TrayAppDotNETSettingsUI.UIFont, FontSize = fontSize };
        try
        {
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width;
        }
        finally
        {
            TextBlockLayoutLifetime.ReleaseForRetirement(probe);
        }
    }

    private double NormalizeValue(double value)
    {
        double rounded = Math.Round(value, DecimalPlaces, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, Minimum, Maximum);
    }

    private string FormatValue(double value) =>
        value.ToString(_numberFormat, CultureInfo.CurrentCulture);

    private static string CreateNumberFormat(int decimalPlaces) =>
        decimalPlaces == 0
            ? "0"
            : $"0.{new string('#', decimalPlaces)}";

    private static double NormalizeStep(double value) =>
        double.IsFinite(value) ? Math.Max(MinimumStep, value) : 1;

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool IsDecimalSeparator(char character)
    {
        string currentSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        return character == '.' ||
               currentSeparator.Length == 1 && character == currentSeparator[0];
    }

    private static bool ContainsDecimalSeparator(string text)
    {
        string currentSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        return text.Contains('.', StringComparison.Ordinal) ||
               text.Contains(currentSeparator, StringComparison.Ordinal);
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
    private readonly TextBlock _glyph;
    private bool _isPointerOver;
    private bool _isPressed;

    public SettingsSpinnerButton(string glyph, SettingsPalette palette)
    {
        _palette = palette;
        Background = Brushes.Transparent;
        CornerRadius = SettingsUILayout.SpinnerButtonCornerRadius;
        Cursor = TrayAppDotNETCursors.Hand;
        Focusable = false;
        _glyph = new TextBlock
        {
            Text = glyph,
            FontFamily = TrayAppDotNETSettingsUI.IconFont,
            FontSize = SettingsUILayout.SpinnerGlyphFontSize,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
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
            if (!IsEnabled) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _isPressed = true;
            UpdateVisual();
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (!_isPressed) return;
            bool releasedInside = TrayAppDotNETFlyoutUI.IsPointerInside(this, e);
            _isPointerOver = releasedInside;
            _isPressed = false;
            UpdateVisual();
            if (releasedInside) Click?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };

        DebugUIProvenance.RecordBuilder(this);
        DebugUIProvenance.RecordBuilder(_glyph);
    }

    /// <summary>
    /// Creates a spinner button from a glyph object.
    /// </summary>
    public SettingsSpinnerButton(Glyph glyph, SettingsPalette palette)
        : this(glyph.Text, palette)
    {
        GlyphApplicator.ApplyTo(_glyph, glyph);
    }

    public event EventHandler? Click;

    private void UpdateVisual()
    {
        Background = TrayAppDotNETSettingsUI.Brush(
            _isPressed ? _palette.Pressed : _isPointerOver ? _palette.Hover : Colors.Transparent);
        DebugUIProvenance.RecordBuilder(this);
    }
}

public static class TrayAppDotNETSettingsUI
{
    public static readonly FontFamily UIFont = TADNFontResolver.ResolveFontFamily(TADNFont.SegoeUI);

    public static readonly FontFamily IconFont =
        TADNFontResolver.ResolveFontFamily(TADNFont.SegoeFluentIconsThenMDL2Assets);

    public static IBrush Brush(Color color) =>
        color == Colors.Transparent ? Brushes.Transparent : new SolidColorBrush(color);

    public static IBrush Brush(SettingsPaletteColor color) => color.Brush;

    public static TextBlock Text(string text, SettingsPalette palette, double fontSize = 14, FontWeight? weight = null)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = UIFont,
            FontSize = fontSize,
            FontWeight = weight ?? FontWeight.Normal,
            Foreground = Brush(palette.Foreground)
        };
    }

    public static TextBlock SectionHeader(string text, SettingsPalette palette)
    {
        TextBlock header = new()
        {
            Text = text,
            FontFamily = UIFont,
            FontSize = SettingsUILayout.SectionHeaderFontSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(palette.Foreground),
            Margin = SettingsUILayout.SectionHeaderMargin
        };
        DebugUIProvenance.RecordBuilder(header);
        return SettingsSearchMetadata.Mark(header, SettingsSearchRole.PageHeader);
    }

    public static TextBlock SubsectionHeader(string text, SettingsPalette palette)
    {
        TextBlock header = new()
        {
            Text = text,
            FontFamily = UIFont,
            FontSize = SettingsUILayout.SubsectionHeaderFontSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(palette.Foreground),
            Margin = SettingsUILayout.SubsectionHeaderMargin
        };
        DebugUIProvenance.RecordBuilder(header);
        return SettingsSearchMetadata.Mark(header, SettingsSearchRole.SubsectionHeader);
    }

    public static TextBlock TitleText(string text, SettingsPalette palette) =>
        new()
        {
            Text = text,
            FontFamily = UIFont,
            FontSize = SettingsUILayout.TitleFontSize,
            Foreground = Brush(palette.Foreground),
            TextWrapping = TextWrapping.Wrap
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
            Margin = margin ?? SettingsUILayout.DescriptionMargin
        };

    public static StackPanel PageStack(string title, SettingsPalette palette)
    {
        StackPanel stack = new() { Background = Brush(palette.Background) };
        stack.Children.Add(SectionHeader(title, palette));
        DebugUIProvenance.RecordBuilder(stack);
        return stack;
    }

    public static Border Card(string title, string description, Control? rightControl, SettingsPalette palette)
    {
        StackPanel text = new()
        {
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
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
            rightControl.Margin = SettingsUILayout.RightControlMargin;
            Grid.SetColumn(rightControl, 1);
            grid.Children.Add(rightControl);
        }

        Border card = new()
        {
            Background = Brush(palette.CardBackground),
            CornerRadius = SettingsUILayout.CardCornerRadius,
            Padding = SettingsUILayout.CardPadding,
            Margin = SettingsUILayout.CardMargin,
            Child = grid
        };
        ApplyDisabledOpacity(card, SettingsUILayout.ControlDisabledOpacity);
        DebugUIProvenance.RecordBuilder(card);
        return SettingsSearchMetadata.MarkCard(card, title);
    }

    public static Border RawCard(Control content, SettingsPalette palette)
    {
        Border card = new()
        {
            Background = Brush(palette.CardBackground),
            CornerRadius = SettingsUILayout.CardCornerRadius,
            Padding = SettingsUILayout.CardPadding,
            Margin = SettingsUILayout.CardMargin,
            Child = content
        };
        ApplyDisabledOpacity(card, SettingsUILayout.ControlDisabledOpacity);
        DebugUIProvenance.RecordBuilder(card);
        return SettingsSearchMetadata.Mark(card, SettingsSearchRole.Card);
    }

    public static SettingsScrollHost ScrollHost(Control content, SettingsPalette palette, Thickness padding) =>
        new(content, palette, padding);

    public static SettingsButton Button(string text, SettingsPalette palette) => new(text, palette);

    /// <summary>
    /// Creates a settings button from a glyph object.
    /// </summary>
    public static SettingsButton Button(Glyph glyph, SettingsPalette palette) => new(glyph, palette);

    public static SettingsButton NavAction(string text, SettingsPalette palette) =>
        new(text, palette, transparentBase: true, navGutter: true)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = SettingsUILayout.NavItemPadding,
            Margin = SettingsUILayout.NavActionMargin
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

    public static TextBox TextBox(SettingsPalette palette, double width, string text = "") =>
        TextBox(
            palette,
            width,
            text,
            palette.ControlBackground,
            palette.Hover,
            palette.TextBoxFocused);

    /// <summary>Creates a search text box using the deep surface-state colors.</summary>
    public static TextBox SearchTextBox(SettingsPalette palette, double width, string text = "") =>
        TextBox(
            palette,
            width,
            text,
            palette.ControlBackgroundDeep,
            palette.HoverDeep,
            palette.PressedDeep);

    /// <summary>Creates a text box with caller-selected surface-state colors.</summary>
    internal static TextBox TextBox(
        SettingsPalette palette,
        double width,
        string text,
        SettingsPaletteColor normalBackground,
        SettingsPaletteColor pointerOverBackground,
        SettingsPaletteColor focusedBackground)
    {
        TextBox textBox = new()
        {
            Width = width,
            Height = SettingsUILayout.TextBoxHeight,
            Text = text,
            FontFamily = UIFont,
            FontSize = SettingsUILayout.TextBoxFontSize,
            Background = Brush(normalBackground),
            Foreground = Brush(palette.Foreground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = SettingsUILayout.TextBoxBorderThickness,
            Padding = SettingsUILayout.TextBoxPadding,
            VerticalContentAlignment = VerticalAlignment.Center,
            CaretBrush = Brush(palette.Foreground),
            SelectionBrush = Brush(AppTheme.ResolveTextSelectionHighlight(palette.Accent)),
            SelectionForegroundBrush = Brush(palette.Foreground)
        };
        ApplyTextBoxResources(
            textBox,
            palette,
            Brush(normalBackground),
            Brush(pointerOverBackground),
            Brush(focusedBackground));

        AttachSurfaceStates(
            textBox,
            normalBackground,
            pointerOverBackground,
            focusedBackground);
        DebugUIProvenance.RecordBuilder(textBox);
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
        DebugUIProvenance.RecordBuilder(textBox);
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
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center
        };
        foreach (Control control in controls)
            panel.Children.Add(control);
        DebugUIProvenance.RecordBuilder(panel);
        return panel;
    }

    public static TextBlock CaptionGlyph(string glyph, SettingsPalette palette) =>
        new()
        {
            Text = glyph,
            FontFamily = IconFont,
            FontSize = SettingsUILayout.CaptionButtonGlyphFontSize,
            Foreground = Brush(palette.Foreground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

    /// <summary>
    /// Creates a caption glyph from a glyph object.
    /// </summary>
    public static TextBlock CaptionGlyph(Glyph glyph, SettingsPalette palette)
    {
        TextBlock textBlock = CaptionGlyph(glyph.Text, palette);
        GlyphApplicator.ApplyTo(textBlock, glyph);
        return textBlock;
    }

    internal static void ApplyDisabledOpacity(Control control, double disabledOpacity)
    {
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsEnabledProperty)
                control.Opacity = control.IsEnabled ? SettingsUILayout.EnabledOpacity : disabledOpacity;
        };
        control.Opacity = control.IsEnabled ? SettingsUILayout.EnabledOpacity : disabledOpacity;
        DebugUIProvenance.RecordBuilder(control);
    }

    private static void AttachSurfaceStates(
        Control control,
        SettingsPaletteColor normal,
        SettingsPaletteColor hover,
        SettingsPaletteColor focusedOrPressed)
    {
        bool pointerOver = false;
        bool focused = false;

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
        return;

        void Update()
        {
            SettingsPaletteColor color = focused ? focusedOrPressed : pointerOver ? hover : normal;
            switch (control)
            {
                case TextBox textBox:
                    textBox.Background = Brush(color);
                    DebugUIProvenance.RecordBuilder(textBox);
                    break;
            }
        }
    }
}
