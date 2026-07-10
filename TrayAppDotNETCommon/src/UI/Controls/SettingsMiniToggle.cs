using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>
/// Layout values for a compact settings toggle.
/// </summary>
public sealed class SettingsMiniToggleLayout
{
    public double Width { get; set; } = 32;

    public double TrackWidth { get; set; } = 32;

    public double TrackHeight { get; set; } = 16;

    public double ThumbSize { get; set; } = 8;

    public double ThumbHoverSize { get; set; } = 8;

    public double ThumbCheckedSize { get; set; } = 8;

    public double LabelFontSize { get; set; } = 10;

    public CornerRadius TrackCornerRadius { get; set; } = new(8);

    public CornerRadius ThumbCornerRadius { get; set; } = new(4);

    public Thickness BorderThickness { get; set; } = new(1);

    public Thickness ThumbUncheckedMargin { get; set; } = new(4, 0, 0, 0);

    public Thickness ThumbCheckedMargin { get; set; } = new(0, 0, 4, 0);

    public Thickness LabelMargin { get; set; } = new(4, 0, 0, 0);

    public Thickness Margin { get; set; }

    public double EnabledOpacity { get; set; } = 1;

    public double DisabledOpacity { get; set; } = 0.45;
}

/// <summary>
/// Compact toggle used in dense settings grids.
/// </summary>
public sealed class SettingsMiniToggle : Border
{
    private readonly SettingsPalette _palette;
    private readonly SettingsMiniToggleLayout _layout;
    private readonly Border _track;
    private readonly Border _thumb;
    private bool _isChecked;
    private bool _isPointerOver;

    public SettingsMiniToggle(SettingsPalette palette, SettingsMiniToggleLayout layout, string? labelText = null)
    {
        _palette = palette;
        _layout = layout;
        Width = Math.Max(layout.TrackWidth, layout.Width);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = layout.Margin;
        Background = Brushes.Transparent;
        Cursor = TrayAppDotNETCursors.Hand;
        Focusable = true;

        Grid toggle = new()
        {
            Width = layout.TrackWidth,
            Height = layout.TrackHeight,
            IsHitTestVisible = false
        };
        _track = new Border
        {
            Width = layout.TrackWidth,
            Height = layout.TrackHeight,
            CornerRadius = layout.TrackCornerRadius,
            BorderThickness = layout.BorderThickness,
            IsHitTestVisible = false
        };
        _thumb = new Border
        {
            Width = layout.ThumbSize,
            Height = layout.ThumbSize,
            CornerRadius = layout.ThumbCornerRadius,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = layout.ThumbUncheckedMargin,
            IsHitTestVisible = false
        };
        toggle.Children.Add(_track);
        toggle.Children.Add(_thumb);

        Child = string.IsNullOrEmpty(labelText)
            ? toggle
            : BuildLabeledContent(toggle, labelText);

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

            IsChecked = !IsChecked;
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (!IsEnabled) return;
            switch (e.Key)
            {
                case Key.Enter:
                case Key.Space:
                    IsChecked = !IsChecked;
                    e.Handled = true;
                    break;
            }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsEnabledProperty)
                UpdateVisual();
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

    private Grid BuildLabeledContent(Grid toggle, string labelText)
    {
        TextBlock label = TrayAppDotNETSettingsUI.Text(labelText, _palette, _layout.LabelFontSize);
        label.Foreground = TrayAppDotNETSettingsUI.Brush(_palette.SecondaryForeground);
        label.Margin = _layout.LabelMargin;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.IsHitTestVisible = false;

        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.Children.Add(toggle);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        return row;
    }

    private void UpdateVisual()
    {
        Opacity = IsEnabled ? _layout.EnabledOpacity : _layout.DisabledOpacity;
        if (_isChecked)
        {
            _track.Background = TrayAppDotNETSettingsUI.Brush(_palette.ToggleOnTrack);
            _track.BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.ToggleOnTrack);
            _thumb.Background = TrayAppDotNETSettingsUI.Brush(_palette.ToggleOnThumb);
            _thumb.Width = _isPointerOver ? _layout.ThumbHoverSize : _layout.ThumbCheckedSize;
            _thumb.Height = _isPointerOver ? _layout.ThumbHoverSize : _layout.ThumbCheckedSize;
            _thumb.HorizontalAlignment = HorizontalAlignment.Right;
            _thumb.Margin = _layout.ThumbCheckedMargin;
            return;
        }

        _track.Background = Brushes.Transparent;
        _track.BorderBrush = TrayAppDotNETSettingsUI.Brush(_palette.SecondaryForeground);
        _thumb.Background = TrayAppDotNETSettingsUI.Brush(_palette.SecondaryForeground);
        _thumb.Width = _isPointerOver ? _layout.ThumbHoverSize : _layout.ThumbSize;
        _thumb.Height = _isPointerOver ? _layout.ThumbHoverSize : _layout.ThumbSize;
        _thumb.HorizontalAlignment = HorizontalAlignment.Left;
        _thumb.Margin = _layout.ThumbUncheckedMargin;
    }
}
