using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>One detailed CPU graph that averages a fixed set of logical processors.</summary>
internal sealed record CPUDetailedGraphGroup(
    string Label,
    ReadOnlyMemory<int> LogicalProcessorIndexes);

/// <summary>Displays aggregate, per-CCD, per-core-class, and highest-core CPU utilization histories.</summary>
internal sealed class CPUPerformanceDetailedView : Grid
{
    private readonly SettingsPalette _palette;
    private readonly TaskManagerWindowResources _resources;
    private readonly List<PerformanceHistory> _groupHistories = [];
    private readonly List<PerformanceHistoryGraph> _graphs = [];
    private IReadOnlyList<CPUDetailedGraphGroup> _groups = [];
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

    /// <summary>Recreates detailed histories and graphs for the given logical-processor groups.</summary>
    public void Rebuild(
        PerformanceHistory overallHistory,
        PerformanceHistory highestCoreHistory,
        IReadOnlyList<CPUDetailedGraphGroup> groups,
        int historyLengthMinutes,
        int sampleIntervalMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(overallHistory);
        ArgumentNullException.ThrowIfNull(highestCoreHistory);
        ArgumentNullException.ThrowIfNull(groups);

        _groups = groups;
        _historyLengthMinutes = historyLengthMinutes;
        _sampleIntervalMilliseconds = sampleIntervalMilliseconds;
        Children.Clear();
        ColumnDefinitions.Clear();
        RowDefinitions.Clear();
        _groupHistories.Clear();
        _graphs.Clear();

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            _groupHistories.Add(CreateHistory());

        int graphCount = 2 + groups.Count;
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
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            AddGraph(
                groups[groupIndex].Label,
                _groupHistories[groupIndex],
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

    /// <summary>Appends per-group averages while preserving unavailable intervals.</summary>
    public void Append(CPUPerformanceSnapshot snapshot, long capturedTimestamp)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        AppendGroupHistories(
            _groupHistories,
            _groups,
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
        _groupHistories.Clear();
        _groups = [];
    }

    /// <summary>Returns the CCD and core-class graphs the Windows topology supports, in display order.</summary>
    internal static List<CPUDetailedGraphGroup> CreateGraphGroups(
        CPUCCDTopology CCDTopology,
        CPUCoreClassTopology coreClassTopology)
    {
        ArgumentNullException.ThrowIfNull(CCDTopology);
        ArgumentNullException.ThrowIfNull(coreClassTopology);

        List<CPUDetailedGraphGroup> groups = [];
        if (CCDTopology is { IsAvailable: true, CCDs.Length: > 1 })
        {
            ReadOnlySpan<CPUCCDTopologyEntry> CCDs = CCDTopology.CCDs.Span;
            bool hasUnequalL3Caches = TryGetSmallestUnequalL3CacheBytes(
                CCDs,
                out ulong smallestL3CacheBytes);
            for (int CCDIndex = 0; CCDIndex < CCDs.Length; CCDIndex++)
            {
                CPUCCDTopologyEntry CCD = CCDs[CCDIndex];
                string label = hasUnequalL3Caches
                    ? FormatCCDLabel(
                        CCDIndex,
                        CCD.L3CacheBytes,
                        CCD.L3CacheBytes > smallestL3CacheBytes)
                    : FormatCCDLabel(CCDIndex);
                groups.Add(new CPUDetailedGraphGroup(label, CCD.LogicalProcessorIndexes));
            }
        }

        if (coreClassTopology.IsHeterogeneous)
        {
            ReadOnlySpan<CPUCoreClassEntry> classes = coreClassTopology.Classes.Span;
            int efficiencyClassCount = 1;
            for (int classIndex = 1; classIndex < classes.Length; classIndex++)
            {
                if (classes[classIndex].EfficiencyClass != classes[classIndex - 1].EfficiencyClass)
                    efficiencyClassCount++;
            }

            for (int classIndex = 0; classIndex < classes.Length; classIndex++)
            {
                CPUCoreClassEntry coreClass = classes[classIndex];
                groups.Add(new CPUDetailedGraphGroup(
                    FormatCoreClassLabel(
                        coreClass,
                        isHighestClass: classIndex == 0,
                        showEfficiencyClass: efficiencyClassCount > 2),
                    coreClass.LogicalProcessorIndexes));
            }
        }

        return groups;
    }

    /// <summary>Formats a CCD graph label when every CCD reports the same level-3 cache.</summary>
    internal static string FormatCCDLabel(int CCDIndex) =>
        string.Concat(str0: "CCD ", CCDIndex.ToString(CultureInfo.CurrentCulture));

    /// <summary>Formats a CCD graph label with its level-3 cache, naming a larger-cache CCD X3D.</summary>
    internal static string FormatCCDLabel(int CCDIndex, ulong l3CacheBytes, bool isX3D) =>
        string.Concat(
            FormatCCDLabel(CCDIndex),
            isX3D ? " (X3D, " : " (",
            PerformanceDevicePresentationFactory.FormatBytes(l3CacheBytes),
            str3: " L3)");

    /// <summary>Formats a core-class graph label such as "8 Performance Cores" or "2 Low Power Efficiency Cores".</summary>
    internal static string FormatCoreClassLabel(
        CPUCoreClassEntry coreClass,
        bool isHighestClass,
        bool showEfficiencyClass)
    {
        ArgumentNullException.ThrowIfNull(coreClass);

        // Windows orders classes by performance but does not name them, so the name follows the order
        // NOTE: names are spelled out because LP already means logical processor in this view
        string className = isHighestClass
            ? "Performance Core"
            : coreClass.IsOutsideL3Cache
                ? "Low Power Efficiency Core"
                : "Efficiency Core";
        string label = string.Concat(
            coreClass.CoreCount.ToString(CultureInfo.CurrentCulture),
            str1: " ",
            className,
            coreClass.CoreCount == 1 ? string.Empty : "s");
        return showEfficiencyClass && !isHighestClass
            ? string.Concat(
                label,
                str1: " (class ",
                coreClass.EfficiencyClass.ToString(CultureInfo.InvariantCulture),
                str3: ")")
            : label;
    }

    /// <summary>Averages logical-processor utilization into timestamp-aligned group histories.</summary>
    internal static void AppendGroupHistories(
        IReadOnlyList<PerformanceHistory> histories,
        IReadOnlyList<CPUDetailedGraphGroup> groups,
        CPUPerformanceSnapshot snapshot,
        long capturedTimestamp)
    {
        ArgumentNullException.ThrowIfNull(histories);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(snapshot);

        for (int historyIndex = 0; historyIndex < histories.Count; historyIndex++)
            histories[historyIndex].AdvanceTo(capturedTimestamp);
        if (histories.Count == 0
            || !snapshot.HasUtilizationSample
            || groups.Count != histories.Count)
            return;

        ReadOnlySpan<double> processorUtilization =
            snapshot.LogicalProcessorUtilizationPercents.Span;
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            ReadOnlySpan<int> processorIndexes = groups[groupIndex].LogicalProcessorIndexes.Span;
            if (processorIndexes.Length == 0) return;

            for (int processorOffset = 0;
                 processorOffset < processorIndexes.Length;
                 processorOffset++)
            {
                int processorIndex = processorIndexes[processorOffset];
                if ((uint)processorIndex >= (uint)processorUtilization.Length) return;
            }
        }

        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            ReadOnlySpan<int> processorIndexes = groups[groupIndex].LogicalProcessorIndexes.Span;
            double utilizationTotal = 0;
            for (int processorOffset = 0;
                 processorOffset < processorIndexes.Length;
                 processorOffset++)
                utilizationTotal += processorUtilization[processorIndexes[processorOffset]];

            histories[groupIndex].Add(
                capturedTimestamp,
                utilizationTotal / processorIndexes.Length);
        }
    }

    /// <summary>Finds the smallest CCD level-3 cache when every CCD reports one and they are not all equal.</summary>
    private static bool TryGetSmallestUnequalL3CacheBytes(
        ReadOnlySpan<CPUCCDTopologyEntry> CCDs,
        out ulong smallestL3CacheBytes)
    {
        smallestL3CacheBytes = ulong.MaxValue;
        ulong largestL3CacheBytes = 0;
        for (int CCDIndex = 0; CCDIndex < CCDs.Length; CCDIndex++)
        {
            ulong l3CacheBytes = CCDs[CCDIndex].L3CacheBytes;
            if (l3CacheBytes == 0) return false;

            smallestL3CacheBytes = Math.Min(smallestL3CacheBytes, l3CacheBytes);
            largestL3CacheBytes = Math.Max(largestL3CacheBytes, l3CacheBytes);
        }

        return largestL3CacheBytes > smallestL3CacheBytes;
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
