using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>
/// Shows the zoom or stretch a table gesture just set, above the sidebar's footer rows (Update and Settings).
/// The newest gesture replaces what it shows, and the card hides once gestures have stopped for
/// <see cref="TimeConstants.GridMetricIndicatorDecayMilliseconds"/>. A collapsed sidebar shows only the percentage.
/// </summary>
internal sealed class TaskManagerGridMetricCard : Border
{
    private readonly TaskManagerWindowResources _resources;
    private readonly TextBlock _label;
    private readonly TextBlock _value;
    private readonly DispatcherTimer _decayTimer;
    private bool _isCompact;
    private double _collapsedSidebarWidth;

    public TaskManagerGridMetricCard(SettingsPalette palette, TaskManagerWindowResources resources)
    {
        _resources = resources;
        IsVisible = false;
        IsHitTestVisible = false;
        Background = TrayAppDotNETSettingsUI.Brush(palette.CardBackground);
        BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border);

        _label = TrayAppDotNETSettingsUI.Text(string.Empty, palette);
        _label.Foreground = TrayAppDotNETSettingsUI.Brush(palette.SecondaryForeground);
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _value = TrayAppDotNETSettingsUI.Text(string.Empty, palette);
        _value.VerticalAlignment = VerticalAlignment.Center;
        Child = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { _label, _value }
        };

        _decayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.GridMetricIndicatorDecayMilliseconds)
        };
        _decayTimer.Tick += OnDecayTimerTick;
        ApplyLayout();
    }

    /// <summary>Shows a metric in place of the one on display and restarts the decay.</summary>
    public void Show(TaskManagerGridMetric metric)
    {
        _label.Text = LocalizationManager.Instance[metric.Kind switch
        {
            TaskManagerGridMetricKind.Zoom => nameof(AppStrings.Window_GridMetric_Zoom),
            TaskManagerGridMetricKind.Stretch => nameof(AppStrings.Window_GridMetric_Stretch),
            TaskManagerGridMetricKind.ZoomBaseline => nameof(AppStrings.Window_GridMetric_ZoomBaseline),
            TaskManagerGridMetricKind.StretchBaseline => nameof(AppStrings.Window_GridMetric_StretchBaseline),
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric.Kind, message: "Unknown grid metric.")
        }];
        _value.Text = metric.FormatPercent(CultureInfo.CurrentCulture);
        ApplyLayout();
        IsVisible = true;
        _decayTimer.Stop();
        _decayTimer.Start();
    }

    /// <summary>Switches between the labeled card and the percentage-only card of the collapsed sidebar.</summary>
    public void SetCompact(bool isCompact, double collapsedSidebarWidth)
    {
        _isCompact = isCompact;
        _collapsedSidebarWidth = collapsedSidebarWidth;
        ApplyLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnDetachedFromVisualTree(eventArgs);
        _decayTimer.Stop();
        IsVisible = false;
    }

    // Reads the live resources on every show and collapse change, so an AXAML reload reaches the next display
    private void ApplyLayout()
    {
        Margin = _resources.AxamlTaskManagerWindow.GridMetricCardMargin;
        Padding = _isCompact
            ? _resources.AxamlTaskManagerWindow.GridMetricCardCompactPadding
            : _resources.AxamlTaskManagerWindow.GridMetricCardPadding;
        BorderThickness = _resources.AxamlTaskManagerWindow.GridMetricCardBorderThickness;
        CornerRadius = _resources.AxamlTaskManagerWindow.GridMetricCardCornerRadius;
        HorizontalAlignment = _isCompact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        Width = _isCompact ? Math.Max(val1: 0, _collapsedSidebarWidth - Margin.Left - Margin.Right) : double.NaN;

        double fontSize = _resources.AxamlTaskManagerWindow.GridMetricCardFontSize;
        _label.FontSize = fontSize;
        _label.Margin = new Thickness(
            left: 0,
            top: 0,
            _resources.AxamlTaskManagerWindow.GridMetricCardLabelSpacing,
            bottom: 0);
        _label.IsVisible = !_isCompact;
        _value.FontSize = fontSize;
        _value.FontWeight = (FontWeight)_resources.AxamlTaskManagerWindow.GridMetricCardValueFontWeight;
        _value.HorizontalAlignment = _isCompact ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        Grid.SetColumn(_value, _isCompact ? 0 : 1);
        Grid.SetColumnSpan(_value, _isCompact ? 2 : 1);
    }

    private void OnDecayTimerTick(object? sender, EventArgs eventArgs)
    {
        _decayTimer.Stop();
        IsVisible = false;
    }
}
