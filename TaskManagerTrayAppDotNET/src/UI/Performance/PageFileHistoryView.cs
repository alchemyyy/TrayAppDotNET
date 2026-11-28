using static TaskManagerTrayAppDotNET.UI.Performance.PerformanceText;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>
/// Graphs every page file's use under the memory graph, one line each, with a legend row naming them.
/// </summary>
/// <remarks>
/// Each line is the page file's use as a percentage of its current size, so files of different sizes share one
/// scale. The first page file takes the memory accent and later ones are progressively lighter shades of it.
/// </remarks>
internal sealed class PageFileHistoryView : Grid
{
    private const double BytesPerGibibyte = 1_073_741_824;

    private readonly Dictionary<string, PageFileHistories> _histories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _stalePaths = [];
    private readonly List<string> _shownPaths = [];
    private readonly List<TextBlock> _legendTexts = [];
    private readonly SettingsPalette _palette;
    private readonly TaskManagerWindowResources _resources;
    private readonly TextBlock _heading;
    private readonly Grid _header;
    private readonly PerformanceHistoryGraph _graph;
    private readonly WrapPanel _legend;
    private readonly PerformanceHistory _emptyHistory;
    private int _historyLengthMinutes;
    private int _sampleIntervalMilliseconds;
    private long _lastTimestamp;
    private double _minimumHeight = -1;
    private Color _accent = PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.Memory);

    public PageFileHistoryView(
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        bool isLightTheme,
        int historyLengthMinutes,
        int sampleIntervalMilliseconds)
    {
        _palette = palette;
        _resources = resources;
        _historyLengthMinutes = historyLengthMinutes;
        _sampleIntervalMilliseconds = sampleIntervalMilliseconds;
        _emptyHistory = CreatePercentHistory();
        IsVisible = false;
        Margin = resources.AxamlTaskManagerPerformance.PageFileGraphMargin;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        _heading = CreateLabel(string.Empty);
        TextBlock maximumLabel = CreateLabel("100%");
        maximumLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _header = new Grid
        {
            Margin = resources.AxamlTaskManagerPerformance.DetailGraphLabelMargin,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { _heading, maximumLabel }
        };
        SetColumn(maximumLabel, value: 1);
        Children.Add(_header);

        _graph = new PerformanceHistoryGraph(
            _emptyHistory,
            PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.Memory),
            palette,
            resources,
            isLightTheme,
            FormatHoverMetric)
        {
            MinHeight = resources.AxamlTaskManagerPerformance.PageFileGraphMinimumHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        SetRow(_graph, value: 1);
        Children.Add(_graph);

        _legend = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = resources.AxamlTaskManagerPerformance.PageFileLegendMargin
        };
        SetRow(_legend, value: 2);
        Children.Add(_legend);
        UpdateHeading();
    }

    /// <summary>
    /// Raised after a layout pass when the height this view needs for its heading, its graph's minimum height and its
    /// legend changes. The legend wraps, so the height depends on the width it was given.
    /// </summary>
    public event Action<double>? MinimumHeightChanged;

    protected override Size MeasureOverride(Size availableSize)
    {
        Size desiredSize = base.MeasureOverride(availableSize);

        // Desired sizes include the children's margins; the view's own margin sits outside this measure
        double minimumHeight = Margin.Top
                               + Margin.Bottom
                               + _header.DesiredSize.Height
                               + _graph.MinHeight
                               + _legend.DesiredSize.Height;
        if (minimumHeight != _minimumHeight)
        {
            _minimumHeight = minimumHeight;

            // The owner reacts by resizing this view's row, so let the current layout pass finish first
            Dispatcher.UIThread.Post(() => MinimumHeightChanged?.Invoke(minimumHeight));
        }

        return desiredSize;
    }

    /// <summary>Gets whether the latest snapshot listed any page file.</summary>
    public bool HasPageFiles => _shownPaths.Count > 0;

    /// <summary>Shows or hides the translucent areas beneath the page file traces.</summary>
    public void SetGraphUnderfillVisible(bool isVisible) => _graph.SetUnderfillVisible(isVisible);

    /// <summary>Locks the page file graph's hover line to samples, or lets it follow the pointer.</summary>
    public void SetGraphHoverSnapsToSamples(bool snapsToSamples) => _graph.SetHoverSnapsToSamples(snapsToSamples);

    /// <summary>
    /// Draws the first page file in <paramref name="accent"/> and later ones in lighter shades of it, from the next
    /// <see cref="UpdatePresentation"/>.
    /// </summary>
    public void SetAccent(Color accent)
    {
        if (_accent == accent) return;

        _accent = accent;
        _graph.SetAccent(accent);
        _shownPaths.Clear();
    }

    /// <summary>Drops every history so the page can replay its snapshot archive with new sampling settings.</summary>
    public void Reset(int historyLengthMinutes, int sampleIntervalMilliseconds)
    {
        _historyLengthMinutes = historyLengthMinutes;
        _sampleIntervalMilliseconds = sampleIntervalMilliseconds;
        _histories.Clear();
        _shownPaths.Clear();
        _lastTimestamp = 0;
        UpdateHeading();
    }

    /// <summary>Records one snapshot's page file use; page files that disappear age out of the graph.</summary>
    public void Append(PerformanceSnapshot snapshot)
    {
        long timestamp = snapshot.CapturedTimestamp;
        if (timestamp <= _lastTimestamp) return;

        _lastTimestamp = timestamp;
        foreach (PageFileHistories histories in _histories.Values)
        {
            histories.Percent.AdvanceTo(timestamp);
            histories.Bytes.AdvanceTo(timestamp);
        }

        ReadOnlySpan<PageFileSnapshot> pageFiles = snapshot.Memory.PageFiles.Span;
        for (int pageFileIndex = 0; pageFileIndex < pageFiles.Length; pageFileIndex++)
        {
            PageFileSnapshot pageFile = pageFiles[pageFileIndex];
            if (!_histories.TryGetValue(pageFile.Path, out PageFileHistories? histories))
            {
                histories = new PageFileHistories(CreatePercentHistory(), CreateBytesHistory());
                histories.Percent.AdvanceTo(timestamp);
                histories.Bytes.AdvanceTo(timestamp);
                _histories.Add(pageFile.Path, histories);
            }

            histories.Percent.Add(
                timestamp,
                pageFile.SizeBytes > 0 ? pageFile.InUseBytes / (double)pageFile.SizeBytes * 100 : 0);
            histories.Bytes.Add(timestamp, pageFile.InUseBytes);
        }

        _stalePaths.Clear();
        foreach (KeyValuePair<string, PageFileHistories> pair in _histories)
        {
            if (pair.Value.Percent.Count == 0) _stalePaths.Add(pair.Key);
        }

        foreach (string stalePath in _stalePaths)
            _histories.Remove(stalePath);
    }

    /// <summary>Shows the current page files' lines and legend.</summary>
    public void UpdatePresentation(ReadOnlySpan<PageFileSnapshot> pageFiles)
    {
        if (!HasSamePaths(pageFiles)) RebuildSeries(pageFiles);

        for (int pageFileIndex = 0; pageFileIndex < pageFiles.Length; pageFileIndex++)
            _legendTexts[pageFileIndex].Text = FormatLegend(pageFiles[pageFileIndex]);
        _graph.Refresh();
    }

    /// <summary>Formats a legend entry: <c>Pagefile H: 7.9 / 40.2GB</c>.</summary>
    internal static string FormatLegend(PageFileSnapshot pageFile) =>
        F(
            nameof(AppStrings.Perf_PageFile_LegendFormat),
            pageFile.DriveName,
            FormatGibibytes(pageFile.InUseBytes),
            FormatGibibytes(pageFile.SizeBytes));

    /// <summary>Returns the shade of the memory accent for the page file at <paramref name="index"/>.</summary>
    internal static Color GetShade(Color accent, int index, double step, double maximumLightening)
    {
        double amount = Math.Clamp(index * step, 0, maximumLightening);
        return Color.FromArgb(
            accent.A,
            Lighten(accent.R, amount),
            Lighten(accent.G, amount),
            Lighten(accent.B, amount));
    }

    private static byte Lighten(byte channel, double amount) =>
        (byte)Math.Round(channel + (byte.MaxValue - channel) * amount);

    private bool HasSamePaths(ReadOnlySpan<PageFileSnapshot> pageFiles)
    {
        if (pageFiles.Length != _shownPaths.Count) return false;

        for (int pageFileIndex = 0; pageFileIndex < pageFiles.Length; pageFileIndex++)
        {
            if (!string.Equals(pageFiles[pageFileIndex].Path, _shownPaths[pageFileIndex], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private void RebuildSeries(ReadOnlySpan<PageFileSnapshot> pageFiles)
    {
        _shownPaths.Clear();
        _legendTexts.Clear();
        _legend.Children.Clear();
        Color accent = _accent;
        List<PerformanceGraphSeries> additionalSeries = [];
        PerformanceHistory primaryHistory = _emptyHistory;
        for (int pageFileIndex = 0; pageFileIndex < pageFiles.Length; pageFileIndex++)
        {
            PageFileSnapshot pageFile = pageFiles[pageFileIndex];
            Color shade = GetShade(
                accent,
                pageFileIndex,
                _resources.AxamlTaskManagerPerformance.PageFileShadeStep,
                _resources.AxamlTaskManagerPerformance.PageFileMaximumLightening);
            PerformanceHistory history = _histories.TryGetValue(pageFile.Path, out PageFileHistories? histories)
                ? histories.Percent
                : CreatePercentHistory();
            if (pageFileIndex == 0)
                primaryHistory = history;
            else
                additionalSeries.Add(_graph.CreateSeries(history, shade));

            _shownPaths.Add(pageFile.Path);
            _legend.Children.Add(CreateLegendItem(pageFile, shade));
        }

        _graph.SetHistory(primaryHistory);
        _graph.SetAdditionalSeries(additionalSeries);
    }

    private StackPanel CreateLegendItem(PageFileSnapshot pageFile, Color shade)
    {
        TextBlock text = CreateLabel(FormatLegend(pageFile));
        text.VerticalAlignment = VerticalAlignment.Center;
        _legendTexts.Add(text);
        Border swatch = new()
        {
            Width = _resources.AxamlTaskManagerPerformance.PageFileSwatchWidth,
            Height = _resources.AxamlTaskManagerPerformance.PageFileSwatchHeight,
            Margin = _resources.AxamlTaskManagerPerformance.PageFileSwatchMargin,
            Background = new SolidColorBrush(shade),
            VerticalAlignment = VerticalAlignment.Center
        };
        StackPanel item = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = _resources.AxamlTaskManagerPerformance.PageFileLegendItemMargin,
            Background = Brushes.Transparent,
            Children = { swatch, text }
        };
        TrayAppDotNETToolTip.SetTip(item, pageFile.Path);
        return item;
    }

    private string? FormatHoverMetric(long timestamp)
    {
        List<string> parts = new(_shownPaths.Count);
        foreach (string path in _shownPaths)
        {
            if (!_histories.TryGetValue(path, out PageFileHistories? histories)
                || !histories.Bytes.TryGetExact(timestamp, out double bytes))
                continue;

            parts.Add(string.Concat(PageFileReader.GetDriveName(path), ": ", FormatGibibytes(bytes), " GB"));
        }

        return parts.Count == 0 ? null : string.Join("  ", parts);
    }

    private void UpdateHeading() =>
        _heading.Text = F(
            nameof(AppStrings.Perf_Graph_PageFileUseOverFormat),
            PerformanceDevicePresentationFactory.FormatHistoryWindow(_historyLengthMinutes));

    private static string FormatGibibytes(double bytes) =>
        (bytes / BytesPerGibibyte).ToString(format: "0.0", CultureInfo.CurrentCulture);

    private TextBlock CreateLabel(string text) =>
        TrayAppDotNETSettingsUI.Text(
            text,
            _palette,
            _resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)_resources.AxamlTaskManagerPerformance.TextFontWeight);

    private PerformanceHistory CreatePercentHistory() => new(_historyLengthMinutes, _sampleIntervalMilliseconds);

    private PerformanceMetricHistory CreateBytesHistory() => new(_historyLengthMinutes, _sampleIntervalMilliseconds);

    private sealed record PageFileHistories(PerformanceHistory Percent, PerformanceMetricHistory Bytes);
}
