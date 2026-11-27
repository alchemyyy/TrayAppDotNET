using static TaskManagerTrayAppDotNET.UI.Performance.PerformanceText;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>
/// Names the memory composition bar's segments above the bar. Each label is centered over its segment and joined to
/// it by a short tick; labels that would collide are pushed apart, and the tick then leans from the label to the
/// segment so it still points at the right one.
/// </summary>
internal sealed class MemoryCompositionLabels : Control
{
    public const int SegmentCount = 4;

    private readonly FormattedText[] _texts = new FormattedText[SegmentCount];
    private readonly double[] _segmentBytes = new double[SegmentCount];
    private readonly double[] _anchors = new double[SegmentCount];
    private readonly double[] _widths = new double[SegmentCount];
    private readonly double[] _lefts = new double[SegmentCount];
    private readonly int[] _visibleSegments = new int[SegmentCount];
    private readonly IBrush _textBrush;
    private readonly Pen _tickPen;
    private readonly Typeface _typeface;
    private readonly double _fontSize;
    private readonly double _labelSpacing;
    private readonly double _tickGap;
    private readonly double _tickLength;
    private readonly Thickness _barBorderThickness;
    private readonly string[] _labels = new string[SegmentCount];

    public MemoryCompositionLabels(
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        Color tickColor)
    {
        _textBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
        _tickPen = new Pen(
            new SolidColorBrush(tickColor),
            resources.AxamlTaskManagerPerformance.MemoryCompositionTickThickness);
        _typeface = new Typeface(
            TrayAppDotNETSettingsUI.UIFont,
            FontStyle.Normal,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        _fontSize = resources.AxamlTaskManagerPerformance.MemoryCompositionSegmentLabelFontSize;
        _labelSpacing = resources.AxamlTaskManagerPerformance.MemoryCompositionSegmentLabelSpacing;
        _tickGap = resources.AxamlTaskManagerPerformance.MemoryCompositionTickGap;
        _tickLength = resources.AxamlTaskManagerPerformance.MemoryCompositionTickLength;
        _barBorderThickness = resources.AxamlTaskManagerPerformance.MemoryCompositionFrameBorderThickness;
        for (int segmentIndex = 0; segmentIndex < SegmentCount; segmentIndex++)
            _labels[segmentIndex] = string.Empty;
        SetLabels(
            L(nameof(AppStrings.Perf_Memory_SegmentInUse)),
            L(nameof(AppStrings.Perf_Memory_SegmentModified)),
            L(nameof(AppStrings.Perf_Memory_SegmentStandby)),
            L(nameof(AppStrings.Perf_Memory_SegmentFree)));
    }

    /// <summary>Sets the segment names in bar order: in use, modified, standby, free.</summary>
    public void SetLabels(string inUse, string modified, string standby, string free)
    {
        string[] labels = [inUse, modified, standby, free];
        bool changed = false;
        for (int segmentIndex = 0; segmentIndex < SegmentCount; segmentIndex++)
        {
            if (string.Equals(_labels[segmentIndex], labels[segmentIndex], StringComparison.Ordinal)
                && _texts[segmentIndex] != null)
                continue;

            _labels[segmentIndex] = labels[segmentIndex];
            _texts[segmentIndex] = new FormattedText(
                labels[segmentIndex],
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                _typeface,
                _fontSize,
                _textBrush);
            changed = true;
        }

        if (!changed) return;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Sets the segment sizes in bar order; a zero-size segment has no label.</summary>
    public void SetSegments(ulong inUse, ulong modified, ulong standby, ulong free)
    {
        ReadOnlySpan<double> bytes = [inUse, modified, standby, free];
        if (bytes.SequenceEqual(_segmentBytes)) return;

        bytes.CopyTo(_segmentBytes);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double textHeight = 0;
        for (int segmentIndex = 0; segmentIndex < SegmentCount; segmentIndex++)
            textHeight = Math.Max(textHeight, _texts[segmentIndex].Height);

        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        return new Size(width, textHeight + _tickGap + _tickLength);
    }

    public override void Render(DrawingContext context)
    {
        double totalBytes = 0;
        for (int segmentIndex = 0; segmentIndex < SegmentCount; segmentIndex++)
            totalBytes += _segmentBytes[segmentIndex];
        double barLeft = _barBorderThickness.Left;
        double barWidth = Bounds.Width - _barBorderThickness.Left - _barBorderThickness.Right;
        if (totalBytes <= 0 || barWidth <= 0) return;

        int visibleCount = 0;
        double segmentStart = 0;
        for (int segmentIndex = 0; segmentIndex < SegmentCount; segmentIndex++)
        {
            double bytes = _segmentBytes[segmentIndex];
            if (bytes > 0)
            {
                _visibleSegments[visibleCount] = segmentIndex;
                _anchors[visibleCount] = barLeft + (segmentStart + bytes / 2) / totalBytes * barWidth;
                _widths[visibleCount] = _texts[segmentIndex].WidthIncludingTrailingWhitespace;
                visibleCount++;
            }

            segmentStart += bytes;
        }

        ArrangeLabels(
            _anchors.AsSpan(0, visibleCount),
            _widths.AsSpan(0, visibleCount),
            _labelSpacing,
            Bounds.Width,
            _lefts.AsSpan(0, visibleCount));

        double tickBottom = Bounds.Height;
        double tickTop = tickBottom - _tickLength;
        for (int labelIndex = 0; labelIndex < visibleCount; labelIndex++)
        {
            FormattedText text = _texts[_visibleSegments[labelIndex]];
            double textTop = tickTop - _tickGap - text.Height;
            context.DrawText(text, new Point(_lefts[labelIndex], textTop));
            double labelCenter = _lefts[labelIndex] + _widths[labelIndex] / 2;
            context.DrawLine(
                _tickPen,
                new Point(labelCenter, tickTop),
                new Point(_anchors[labelIndex], tickBottom));
        }
    }

    /// <summary>
    /// Centers each label on its anchor, then separates labels that would overlap while keeping them inside
    /// <paramref name="availableWidth"/>. Anchors must be in ascending order.
    /// </summary>
    internal static void ArrangeLabels(
        ReadOnlySpan<double> anchors,
        ReadOnlySpan<double> widths,
        double spacing,
        double availableWidth,
        Span<double> lefts)
    {
        int count = anchors.Length;
        if (count == 0) return;

        for (int index = 0; index < count; index++)
            lefts[index] = Math.Clamp(anchors[index] - widths[index] / 2, 0, Math.Max(0, availableWidth - widths[index]));

        // Push right past each left neighbor, then pull back left from the right edge
        for (int index = 1; index < count; index++)
            lefts[index] = Math.Max(lefts[index], lefts[index - 1] + widths[index - 1] + spacing);

        double limit = availableWidth;
        for (int index = count - 1; index >= 0; index--)
        {
            lefts[index] = Math.Max(0, Math.Min(lefts[index], limit - widths[index]));
            limit = lefts[index] - spacing;
        }
    }
}
