using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Displays aggregate, per-CCD, and highest-core CPU utilization histories.</summary>
internal sealed class CPUPerformanceDetailedView : Grid
{
    private readonly SettingsPalette _palette;
    private readonly TaskManagerWindowResources _resources;
    private readonly List<PerformanceHistory> _ccdHistories = [];
    private readonly List<PerformanceHistoryGraph> _graphs = [];
    private CPUCCDTopology _topology = CPUCCDTopology.Empty;
    private int _historyLengthMinutes;
    private int _sampleIntervalMilliseconds;
    private bool _showGraphUnderfill = true;

    public CPUPerformanceDetailedView(
        SettingsPalette palette,
        TaskManagerWindowResources resources)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(resources);

        _palette = palette;
        _resources = resources;
        Background = Brushes.Transparent;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        IsVisible = false;
    }

    /// <summary>Recreates detailed histories and graphs for the active CPU topology.</summary>
    public void Rebuild(
        PerformanceHistory overallHistory,
        PerformanceHistory highestCoreHistory,
        CPUCCDTopology topology,
        int historyLengthMinutes,
        int sampleIntervalMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(overallHistory);
        ArgumentNullException.ThrowIfNull(highestCoreHistory);
        ArgumentNullException.ThrowIfNull(topology);

        _topology = topology.IsAvailable ? topology : CPUCCDTopology.Empty;
        _historyLengthMinutes = historyLengthMinutes;
        _sampleIntervalMilliseconds = sampleIntervalMilliseconds;
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        _ccdHistories.Clear();
        _graphs.Clear();

        int visibleCCDCount = GetVisibleCCDGraphCount(_topology);
        for (int CCDIndex = 0; CCDIndex < visibleCCDCount; CCDIndex++)
            _ccdHistories.Add(CreateHistory());

        int graphCount = 2 + visibleCCDCount;
        int columnCount = CalculateColumnCount(
            graphCount,
            _resources.AxamlTaskManagerPerformance.DetailedCPUGridAspectRatio);
        int rowCount = (graphCount + columnCount - 1) / columnCount;
        double graphSpacing = Math.Max(
            val1: 0,
            _resources.AxamlTaskManagerPerformance.DetailedCPUGraphSpacing);
        BuildGridDefinitions(columnCount, rowCount, graphSpacing);

        Color accent = PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.CPU);
        int graphIndex = 0;
        AddGraph(
            labelText: "Overall usage",
            overallHistory,
            accent,
            hoverMetricProvider: null,
            graphIndex++,
            columnCount);
        for (int CCDIndex = 0; CCDIndex < visibleCCDCount; CCDIndex++)
        {
            AddGraph(
                string.Concat(arg0: "CCD ", CCDIndex),
                _ccdHistories[CCDIndex],
                accent,
                hoverMetricProvider: null,
                graphIndex++,
                columnCount);
        }

        AddGraph(
            labelText: "Highest single LP utilization",
            highestCoreHistory,
            accent,
            hoverMetricProvider: null,
            graphIndex,
            columnCount);
    }

    /// <summary>Appends per-CCD averages while preserving unavailable intervals.</summary>
    public void Append(CPUPerformanceSnapshot snapshot, long capturedTimestamp)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        AppendCCDHistories(
            _ccdHistories,
            _topology,
            snapshot,
            capturedTimestamp);
    }

    /// <summary>Repaints every detailed graph after its shared histories change.</summary>
    public void Refresh()
    {
        if (!IsVisible) return;

        for (int graphIndex = 0; graphIndex < _graphs.Count; graphIndex++)
            _graphs[graphIndex].Refresh();
    }

    /// <summary>Shows or hides the translucent area beneath every detailed CPU graph.</summary>
    public void SetGraphUnderfillVisible(bool isVisible)
    {
        _showGraphUnderfill = isVisible;
        for (int graphIndex = 0; graphIndex < _graphs.Count; graphIndex++)
            _graphs[graphIndex].SetUnderfillVisible(isVisible);
    }

    /// <summary>Releases retained graph and history references.</summary>
    public void Clear()
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        _graphs.Clear();
        _ccdHistories.Clear();
        _topology = CPUCCDTopology.Empty;
    }

    /// <summary>Returns the number of CCD graphs that should be shown.</summary>
    internal static int GetVisibleCCDGraphCount(CPUCCDTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return topology is { IsAvailable: true, CCDs.Length: > 1 }
            ? topology.CCDs.Length
            : 0;
    }

    /// <summary>Averages logical-processor utilization into timestamp-aligned CCD histories.</summary>
    internal static void AppendCCDHistories(
        IReadOnlyList<PerformanceHistory> histories,
        CPUCCDTopology topology,
        CPUPerformanceSnapshot snapshot,
        long capturedTimestamp)
    {
        ArgumentNullException.ThrowIfNull(histories);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(snapshot);

        for (int historyIndex = 0; historyIndex < histories.Count; historyIndex++)
            histories[historyIndex].AdvanceTo(capturedTimestamp);
        if (histories.Count == 0
            || !snapshot.HasUtilizationSample
            || !topology.IsAvailable
            || !snapshot.CCDTopology.IsAvailable
            || topology.CCDs.Length != histories.Count)
            return;

        ReadOnlySpan<double> processorUtilization =
            snapshot.LogicalProcessorUtilizationPercents.Span;
        ReadOnlySpan<CPUCCDTopologyEntry> CCDs = topology.CCDs.Span;
        for (int CCDIndex = 0; CCDIndex < CCDs.Length; CCDIndex++)
        {
            ReadOnlySpan<int> processorIndexes = CCDs[CCDIndex].LogicalProcessorIndexes.Span;
            if (processorIndexes.Length == 0) return;

            for (int processorOffset = 0;
                 processorOffset < processorIndexes.Length;
                 processorOffset++)
            {
                int processorIndex = processorIndexes[processorOffset];
                if ((uint)processorIndex >= (uint)processorUtilization.Length) return;
            }
        }

        for (int CCDIndex = 0; CCDIndex < CCDs.Length; CCDIndex++)
        {
            ReadOnlySpan<int> processorIndexes = CCDs[CCDIndex].LogicalProcessorIndexes.Span;
            double utilizationTotal = 0;
            for (int processorOffset = 0;
                 processorOffset < processorIndexes.Length;
                 processorOffset++)
                utilizationTotal += processorUtilization[processorIndexes[processorOffset]];

            histories[CCDIndex].Add(
                capturedTimestamp,
                utilizationTotal / processorIndexes.Length);
        }
    }

    private PerformanceHistory CreateHistory() =>
        new(_historyLengthMinutes, _sampleIntervalMilliseconds);

    private void BuildGridDefinitions(
        int columnCount,
        int rowCount,
        double graphSpacing)
    {
        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            if (columnIndex < columnCount - 1)
            {
                ColumnDefinitions.Add(new ColumnDefinition(
                    new GridLength(graphSpacing)));
            }
        }

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            RowDefinitions.Add(new RowDefinition(GridLength.Star));
            if (rowIndex < rowCount - 1)
            {
                RowDefinitions.Add(new RowDefinition(
                    new GridLength(graphSpacing)));
            }
        }
    }

    private void AddGraph(
        string labelText,
        PerformanceHistory history,
        Color accent,
        Func<long, string?>? hoverMetricProvider,
        int graphIndex,
        int columnCount)
    {
        TextBlock label = TrayAppDotNETSettingsUI.Text(
            labelText,
            _palette,
            _resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)_resources.AxamlTaskManagerPerformance.TextFontWeight);
        label.Margin = _resources.AxamlTaskManagerPerformance.SpecialGraphHeaderMargin;
        label.TextTrimming = TextTrimming.CharacterEllipsis;

        PerformanceHistoryGraph graph = new(
            history,
            accent,
            _palette,
            _resources,
            hoverMetricProvider)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
        };
        graph.SetUnderfillVisible(_showGraphUnderfill);
        Grid tile = new()
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
        };
        tile.Children.Add(label);
        SetRow(graph, value: 1);
        tile.Children.Add(graph);

        int graphColumn = graphIndex % columnCount;
        int graphRow = graphIndex / columnCount;
        SetColumn(tile, graphColumn * 2);
        SetRow(tile, graphRow * 2);
        Children.Add(tile);
        _graphs.Add(graph);
    }

    /// <summary>Chooses the nearest aspect-aware column count for a partial final row.</summary>
    private static int CalculateColumnCount(int graphCount, double aspectRatio)
    {
        double normalizedAspectRatio = double.IsFinite(aspectRatio) && aspectRatio > 0
            ? aspectRatio
            : 1;
        double targetColumnCount = Math.Sqrt(graphCount * normalizedAspectRatio);
        int bestColumnCount = 1;
        double bestDistance = double.MaxValue;
        for (int candidateColumnCount = 1;
             candidateColumnCount <= graphCount;
             candidateColumnCount++)
        {
            double distance = Math.Abs(candidateColumnCount - targetColumnCount);
            if (distance > bestDistance
                || (distance == bestDistance && candidateColumnCount <= bestColumnCount))
                continue;

            bestColumnCount = candidateColumnCount;
            bestDistance = distance;
        }

        return bestColumnCount;
    }
}
