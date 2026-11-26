using static TaskManagerTrayAppDotNET.UI.Performance.PerformanceText;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>
/// Displays the GPU's engine histories (the first two alone, the video pair together), its power, and its dedicated
/// and shared memory on one graph.
/// </summary>
internal sealed class GPUPerformanceDetailsView : Grid
{
    private const int EngineLaneCount = 4;
    private const int SingleEngineCellCount = 2;
    private const int PairedPrimaryLane = 2;
    private const int PairedSecondaryLane = 3;
    private const int PairedEngineCell = 2;
    private const int PowerCell = 3;
    private const int ClockCell = 4;
    private const int MemoryClockCell = 5;
    private const int HalfWidthCellCount = 6;
    private const double MinimumPowerScalePercent = 100;

    // Video Decode precedes Video Encode so the paired cell draws decode solid and encode dashed
    private static readonly string[] PreferredEngineNames =
    [
        "3D",
        "Copy",
        "Video Decode",
        "Video Encode"
    ];

    private readonly SettingsPalette _palette;
    private readonly TaskManagerWindowResources _resources;
    private readonly PerformanceHistory[] _engineHistories = new PerformanceHistory[EngineLaneCount];
    private readonly string[] _engineNames = new string[EngineLaneCount];
    private readonly bool[] _engineHasSample = new bool[EngineLaneCount];
    private readonly double[] _engineUtilization = new double[EngineLaneCount];
    private readonly PerformanceHistoryGraph[] _singleEngineGraphs = new PerformanceHistoryGraph[SingleEngineCellCount];
    private readonly TextBlock[] _singleEngineTitleLabels = new TextBlock[SingleEngineCellCount];
    private readonly TextBlock[] _singleEngineValueLabels = new TextBlock[SingleEngineCellCount];
    private readonly PerformanceHistoryGraph _pairedEngineGraph;
    private readonly TextBlock _pairedEngineTitleLabel;
    private readonly TextBlock _pairedEngineValueLabel;
    private readonly PerformanceMetricHistoryGraph _powerGraph;
    private readonly TextBlock _powerValueLabel;
    private readonly PerformanceMetricHistoryGraph _clockGraph;
    private readonly TextBlock _clockValueLabel;
    private readonly PerformanceMetricHistoryGraph _memoryClockGraph;
    private readonly TextBlock _memoryClockValueLabel;
    private readonly PerformanceMetricHistoryGraph _memoryGraph;
    private readonly TextBlock _memoryCapacityLabel;

    // Half-width cells in reading order, two per row, then the full-width memory cell. A cell whose metric has no
    // sample in the current history is left out, and the rest close up.
    private readonly Grid[] _halfWidthCells = new Grid[HalfWidthCellCount];
    private readonly bool[] _halfWidthCellVisible = new bool[HalfWidthCellCount];
    private readonly Grid _memoryCell;
    private bool _memoryCellVisible;
    private bool _hasArrangedCells;
    private PerformanceMetricHistory _powerHistory;
    private PerformanceMetricHistory _clockHistory;
    private PerformanceMetricHistory _memoryClockHistory;
    private PerformanceMetricHistory _dedicatedMemoryHistory;
    private PerformanceMetricHistory _sharedMemoryHistory;
    private string? _deviceID;
    private int _historyLengthMinutes;
    private int _sampleIntervalMilliseconds;
    private long _lastTimestamp;
    private double _powerLimitWatts = double.NaN;
    private string? _powerStatus;
    private bool _hasPowerSample;
    private double _latestPowerPercent;

    public GPUPerformanceDetailsView(
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
        _powerHistory = CreateMetricHistory();
        _clockHistory = CreateMetricHistory();
        _memoryClockHistory = CreateMetricHistory();
        _dedicatedMemoryHistory = CreateMetricHistory();
        _sharedMemoryHistory = CreateMetricHistory();
        IsVisible = false;
        ColumnSpacing = resources.AxamlTaskManagerPerformance.GPUDetailGraphColumnSpacing;
        RowSpacing = resources.AxamlTaskManagerPerformance.GPUDetailGraphRowSpacing;
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        Color accent = PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.GPU);
        Color secondaryAccent = resources.AxamlTaskManagerPerformance.GPUSecondaryAccentColor;
        for (int laneIndex = 0; laneIndex < EngineLaneCount; laneIndex++)
        {
            _engineHistories[laneIndex] = CreatePercentageHistory();
            _engineNames[laneIndex] = PreferredEngineNames[laneIndex];
        }

        for (int cellIndex = 0; cellIndex < SingleEngineCellCount; cellIndex++)
        {
            PerformanceHistoryGraph graph = CreateEngineGraph(_engineHistories[cellIndex], accent, isLightTheme);
            _singleEngineGraphs[cellIndex] = graph;
            Grid cell = BuildGraphCell(
                PreferredEngineNames[cellIndex],
                graph,
                initialValue: "0%",
                out _singleEngineTitleLabels[cellIndex],
                out _singleEngineValueLabels[cellIndex]);
            _halfWidthCells[cellIndex] = cell;
        }

        _pairedEngineGraph = CreateEngineGraph(
            _engineHistories[PairedPrimaryLane],
            accent,
            isLightTheme,
            FormatPairedEngineHover);
        _pairedEngineGraph.SetSecondaryHistory(_engineHistories[PairedSecondaryLane]);
        _pairedEngineGraph.SetSecondaryLineColor(secondaryAccent);
        Grid pairedCell = BuildGraphCell(
            CombineEngineTitles(PreferredEngineNames[PairedPrimaryLane], PreferredEngineNames[PairedSecondaryLane]),
            _pairedEngineGraph,
            initialValue: "0% / 0%",
            out _pairedEngineTitleLabel,
            out _pairedEngineValueLabel);
        _halfWidthCells[PairedEngineCell] = pairedCell;

        _powerGraph = CreateMetricGraph(
            _powerHistory,
            L(nameof(AppStrings.Perf_Label_Power)),
            FormatPowerValue,
            accent,
            isLightTheme);
        _powerGraph.SetMaximumValue(MinimumPowerScalePercent);
        Grid powerCell = BuildGraphCell(
            L(nameof(AppStrings.Perf_Label_Power)),
            _powerGraph,
            initialValue: Unavailable,
            out _,
            out _powerValueLabel);
        _halfWidthCells[PowerCell] = powerCell;

        _clockGraph = CreateMetricGraph(
            _clockHistory,
            L(nameof(AppStrings.Perf_Label_GPUClock)),
            PerformanceDevicePresentationFactory.FormatMegahertz,
            accent,
            isLightTheme);
        _halfWidthCells[ClockCell] = BuildGraphCell(
            L(nameof(AppStrings.Perf_Label_GPUClock)),
            _clockGraph,
            initialValue: Unavailable,
            out _,
            out _clockValueLabel);

        _memoryClockGraph = CreateMetricGraph(
            _memoryClockHistory,
            L(nameof(AppStrings.Perf_Label_MemoryClock)),
            PerformanceDevicePresentationFactory.FormatMegahertz,
            accent,
            isLightTheme);
        _halfWidthCells[MemoryClockCell] = BuildGraphCell(
            L(nameof(AppStrings.Perf_Label_MemoryClock)),
            _memoryClockGraph,
            initialValue: Unavailable,
            out _,
            out _memoryClockValueLabel);

        _memoryGraph = new PerformanceMetricHistoryGraph(
            _dedicatedMemoryHistory,
            _sharedMemoryHistory,
            primaryLabel: L(nameof(AppStrings.Perf_Label_DedicatedGPUMemory)),
            secondaryLabel: L(nameof(AppStrings.Perf_Label_SharedGPUMemory)),
            PerformanceDevicePresentationFactory.FormatBytes,
            accent,
            palette,
            resources,
            isLightTheme,
            secondaryAccent)
        {
            Height = resources.AxamlTaskManagerPerformance.GPUDetailMemoryGraphHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _memoryCell = BuildGraphCell(
            title: L(nameof(AppStrings.Perf_Label_DedicatedAndSharedGPUMemory)),
            _memoryGraph,
            initialValue: Unavailable,
            out _,
            out _memoryCapacityLabel);
        SetColumnSpan(_memoryCell, value: 2);
        ArrangeCells();
    }

    /// <summary>Gets the device whose histories the view currently shows.</summary>
    public string? DeviceID => _deviceID;

    /// <summary>Shows or hides the translucent areas beneath every GPU graph.</summary>
    public void SetGraphUnderfillVisible(bool isVisible)
    {
        for (int cellIndex = 0; cellIndex < SingleEngineCellCount; cellIndex++)
            _singleEngineGraphs[cellIndex].SetUnderfillVisible(isVisible);

        _pairedEngineGraph.SetUnderfillVisible(isVisible);
        _powerGraph.SetUnderfillVisible(isVisible);
        _clockGraph.SetUnderfillVisible(isVisible);
        _memoryClockGraph.SetUnderfillVisible(isVisible);
        _memoryGraph.SetUnderfillVisible(isVisible);
    }

    /// <summary>
    /// Shows power in watts against the given default limit, or as a percentage of it when the limit is NaN. A status
    /// such as "Reading power limit…" replaces the value until it is cleared with null.
    /// </summary>
    public void SetPowerDisplay(double limitWatts, string? status)
    {
        _powerLimitWatts = double.IsFinite(limitWatts) && limitWatts > 0 ? limitWatts : double.NaN;
        _powerStatus = status;
        UpdatePowerLabel();
        _powerGraph.Refresh();
    }

    /// <summary>Rebuilds the selected adapter histories when selection or sampling settings change.</summary>
    public void Show(
        string deviceID,
        int historyLengthMinutes,
        int sampleIntervalMilliseconds,
        IReadOnlyList<PerformanceSnapshot> snapshots)
    {
        bool configurationChanged = _historyLengthMinutes != historyLengthMinutes
                                    || _sampleIntervalMilliseconds != sampleIntervalMilliseconds;
        bool deviceChanged = !string.Equals(_deviceID, deviceID, StringComparison.Ordinal);
        IsVisible = true;
        if (!configurationChanged && !deviceChanged) return;

        _deviceID = deviceID;
        _historyLengthMinutes = historyLengthMinutes;
        _sampleIntervalMilliseconds = sampleIntervalMilliseconds;
        for (int laneIndex = 0; laneIndex < EngineLaneCount; laneIndex++)
            _engineHistories[laneIndex] = CreatePercentageHistory();
        for (int cellIndex = 0; cellIndex < SingleEngineCellCount; cellIndex++)
            _singleEngineGraphs[cellIndex].SetHistory(_engineHistories[cellIndex]);
        _pairedEngineGraph.SetHistory(_engineHistories[PairedPrimaryLane]);
        _pairedEngineGraph.SetSecondaryHistory(_engineHistories[PairedSecondaryLane]);

        _powerHistory = CreateMetricHistory();
        _clockHistory = CreateMetricHistory();
        _memoryClockHistory = CreateMetricHistory();
        _dedicatedMemoryHistory = CreateMetricHistory();
        _sharedMemoryHistory = CreateMetricHistory();
        _powerGraph.SetHistories(_powerHistory, secondaryHistory: null);
        _powerGraph.SetMaximumValue(MinimumPowerScalePercent);
        _clockGraph.SetHistories(_clockHistory, secondaryHistory: null);
        _memoryClockGraph.SetHistories(_memoryClockHistory, secondaryHistory: null);
        _memoryGraph.SetHistories(_dedicatedMemoryHistory, _sharedMemoryHistory);
        _hasPowerSample = false;
        _lastTimestamp = 0;
        for (int snapshotIndex = 0; snapshotIndex < snapshots.Count; snapshotIndex++)
            Append(snapshots[snapshotIndex]);
        UpdatePowerLabel();
        ArrangeCells();
    }

    /// <summary>Appends the selected adapter's engine, power and memory samples.</summary>
    public void Append(PerformanceSnapshot snapshot)
    {
        if (_deviceID == null || snapshot.CapturedTimestamp <= _lastTimestamp) return;

        AdvanceHistories(snapshot.CapturedTimestamp);
        GPUPerformanceSnapshot? selectedGPU = FindGPU(snapshot.GPUs.Span, _deviceID);
        if (selectedGPU != null)
        {
            AppendEngines(snapshot.CapturedTimestamp, selectedGPU);
            GPUPerformanceDetailsSnapshot? details = selectedGPU.Details;
            _hasPowerSample = details?.HasPowerData == true;
            if (_hasPowerSample)
            {
                _latestPowerPercent = details!.PowerPercent;
                _powerHistory.Add(snapshot.CapturedTimestamp, _latestPowerPercent);
                _powerGraph.SetMaximumValue(Math.Max(MinimumPowerScalePercent, _powerHistory.GetMaximumValue()));
            }

            UpdatePowerLabel();
            AppendClocks(snapshot.CapturedTimestamp, details);
            if (selectedGPU.HasDedicatedMemoryData)
                _dedicatedMemoryHistory.Add(snapshot.CapturedTimestamp, selectedGPU.DedicatedMemoryBytes);
            if (selectedGPU.HasSharedMemoryData)
                _sharedMemoryHistory.Add(snapshot.CapturedTimestamp, selectedGPU.SharedMemoryBytes);

            UpdateMemoryScale(selectedGPU);
        }

        _lastTimestamp = snapshot.CapturedTimestamp;
        ArrangeCells();
        RefreshGraphs();
    }

    public void Hide() => IsVisible = false;

    /// <summary>Reports whether the current selection already owns the requested history shape.</summary>
    public bool IsShowing(
        string deviceID,
        int historyLengthMinutes,
        int sampleIntervalMilliseconds) =>
        IsVisible
        && string.Equals(_deviceID, deviceID, StringComparison.Ordinal)
        && _historyLengthMinutes == historyLengthMinutes
        && _sampleIntervalMilliseconds == sampleIntervalMilliseconds;

    /// <summary>
    /// Titles a cell holding two engines; names sharing a first word share it once, so Video Decode and Video Encode
    /// read "Video Decode / Encode".
    /// </summary>
    internal static string CombineEngineTitles(string primaryName, string secondaryName)
    {
        int primarySpace = primaryName.IndexOf(' ');
        int secondarySpace = secondaryName.IndexOf(' ');
        bool sharesFirstWord = primarySpace > 0
                               && primarySpace == secondarySpace
                               && string.Compare(
                                   primaryName,
                                   0,
                                   secondaryName,
                                   0,
                                   primarySpace,
                                   StringComparison.OrdinalIgnoreCase) == 0;
        return sharesFirstWord
            ? string.Concat(primaryName, " / ", secondaryName.AsSpan(secondarySpace + 1))
            : string.Concat(primaryName, " / ", secondaryName);
    }

    /// <summary>Returns the busiest physical engine matching one Task Manager category.</summary>
    internal static bool TryGetEngineUtilization(
        ReadOnlySpan<GPUPerformanceEngineSnapshot> engines,
        string engineName,
        out double utilizationPercent)
    {
        utilizationPercent = 0;
        bool found = false;
        for (int engineIndex = 0; engineIndex < engines.Length; engineIndex++)
        {
            GPUPerformanceEngineSnapshot engine = engines[engineIndex];
            if (!string.Equals(engine.Name, engineName, StringComparison.OrdinalIgnoreCase))
                continue;

            utilizationPercent = Math.Max(utilizationPercent, engine.UtilizationPercent);
            found = true;
        }

        return found;
    }

    /// <summary>Selects a live fallback category that does not reuse a native detail node.</summary>
    internal static bool TrySelectFallbackEngine(
        ReadOnlySpan<string> displayedEngineNames,
        ReadOnlySpan<GPUPerformanceDetailEngineSnapshot> detailEngines,
        ReadOnlySpan<GPUPerformanceEngineSnapshot> liveEngines,
        out string engineName,
        out double utilizationPercent)
    {
        utilizationPercent = 0;
        for (int preferredIndex = 0; preferredIndex < PreferredEngineNames.Length; preferredIndex++)
        {
            string preferredName = PreferredEngineNames[preferredIndex];
            if (ContainsEngineName(displayedEngineNames, preferredName)) continue;

            bool found = false;
            for (int liveIndex = 0; liveIndex < liveEngines.Length; liveIndex++)
            {
                GPUPerformanceEngineSnapshot liveEngine = liveEngines[liveIndex];
                if (!string.Equals(
                        liveEngine.Name,
                        preferredName,
                        StringComparison.OrdinalIgnoreCase)
                    || ContainsEngineIndex(detailEngines, liveEngine.EngineIndex)
                    || !double.IsFinite(liveEngine.UtilizationPercent))
                    continue;

                utilizationPercent = Math.Max(
                    utilizationPercent,
                    Math.Clamp(liveEngine.UtilizationPercent, min: 0, max: 100));
                found = true;
            }

            if (!found) continue;
            engineName = preferredName;
            return true;
        }

        engineName = SelectFallbackEngineName(displayedEngineNames);
        return false;
    }

    /// <summary>Selects the first preferred category not already displayed by an earlier lane.</summary>
    internal static string SelectFallbackEngineName(ReadOnlySpan<string> displayedEngineNames)
    {
        for (int preferredIndex = 0; preferredIndex < PreferredEngineNames.Length; preferredIndex++)
        {
            string preferredName = PreferredEngineNames[preferredIndex];
            if (!ContainsEngineName(displayedEngineNames, preferredName)) return preferredName;
        }

        return "GPU Engine";
    }

    private void AppendEngines(long timestamp, GPUPerformanceSnapshot selectedGPU)
    {
        ReadOnlySpan<GPUPerformanceDetailEngineSnapshot> detailEngines =
            selectedGPU.Details != null
                ? selectedGPU.Details.Engines.Span
                : ReadOnlySpan<GPUPerformanceDetailEngineSnapshot>.Empty;
        for (int laneIndex = 0; laneIndex < EngineLaneCount; laneIndex++)
        {
            bool hasEngineSample;
            double utilizationPercent;
            if (laneIndex < detailEngines.Length)
            {
                GPUPerformanceDetailEngineSnapshot engine = detailEngines[laneIndex];
                _engineNames[laneIndex] = engine.Name;
                hasEngineSample = engine.HasUtilizationSample;
                utilizationPercent = engine.UtilizationPercent;
            }
            else
            {
                bool hasFallbackSample = TrySelectFallbackEngine(
                    _engineNames.AsSpan(start: 0, laneIndex),
                    detailEngines,
                    selectedGPU.Engines.Span,
                    out string fallbackEngineName,
                    out utilizationPercent);
                _engineNames[laneIndex] = fallbackEngineName;
                hasEngineSample = selectedGPU.HasUtilizationSample && hasFallbackSample;
            }

            if (hasEngineSample)
                _engineHistories[laneIndex].Add(timestamp, utilizationPercent);
            _engineHasSample[laneIndex] = hasEngineSample;
            _engineUtilization[laneIndex] = utilizationPercent;
        }

        for (int cellIndex = 0; cellIndex < SingleEngineCellCount; cellIndex++)
        {
            _singleEngineTitleLabels[cellIndex].Text = _engineNames[cellIndex];
            _singleEngineValueLabels[cellIndex].Text = FormatLaneValue(cellIndex);
        }

        _pairedEngineTitleLabel.Text = CombineEngineTitles(
            _engineNames[PairedPrimaryLane],
            _engineNames[PairedSecondaryLane]);
        _pairedEngineValueLabel.Text = string.Concat(
            FormatLaneValue(PairedPrimaryLane),
            " / ",
            FormatLaneValue(PairedSecondaryLane));
    }

    private string FormatLaneValue(int laneIndex) =>
        _engineHasSample[laneIndex]
            ? PerformanceDevicePresentationFactory.FormatPercent(isAvailable: true, _engineUtilization[laneIndex])
            : Unavailable;

    private string? FormatPairedEngineHover(long timestamp)
    {
        bool hasPrimary = _engineHistories[PairedPrimaryLane].TryGetExact(timestamp, out double primary);
        bool hasSecondary = _engineHistories[PairedSecondaryLane].TryGetExact(timestamp, out double secondary);
        if (!hasPrimary && !hasSecondary) return null;

        return string.Concat(
            _engineNames[PairedPrimaryLane],
            ": ",
            hasPrimary ? FormatHoverPercent(primary) : Unavailable,
            "\n",
            _engineNames[PairedSecondaryLane],
            ": ",
            hasSecondary ? FormatHoverPercent(secondary) : Unavailable);
    }

    private static string FormatHoverPercent(double value) =>
        string.Concat(value.ToString(format: "N0", CultureInfo.CurrentCulture), "%");

    private string FormatPowerValue(double percent) =>
        PerformanceDevicePresentationFactory.FormatGPUPower(percent, _powerLimitWatts);

    private void UpdatePowerLabel() =>
        _powerValueLabel.Text = _powerStatus
                                ?? (_hasPowerSample ? FormatPowerValue(_latestPowerPercent) : Unavailable);

    /// <summary>
    /// Appends the GPU and memory clocks. The GPU clock is scaled to the driver's maximum (its boost limit) or the
    /// highest clock seen; the memory clock to the highest seen, since drivers report a maximum memory clock below
    /// the one the memory actually runs at.
    /// </summary>
    private void AppendClocks(long timestamp, GPUPerformanceDetailsSnapshot? details)
    {
        bool hasClock = details?.HasClockData == true;
        if (hasClock)
        {
            _clockHistory.Add(timestamp, details!.ClockHertz);
            _clockGraph.SetMaximumValue(Math.Max(details.MaximumClockHertz, _clockHistory.GetMaximumValue()));
        }

        bool hasMemoryClock = details?.HasMemoryClockData == true;
        if (hasMemoryClock)
        {
            _memoryClockHistory.Add(timestamp, details!.MemoryClockHertz);
            _memoryClockGraph.SetMaximumValue(_memoryClockHistory.GetMaximumValue());
        }

        _clockValueLabel.Text = hasClock
            ? PerformanceDevicePresentationFactory.FormatMegahertz(details!.ClockHertz)
            : Unavailable;
        _memoryClockValueLabel.Text = hasMemoryClock
            ? PerformanceDevicePresentationFactory.FormatMegahertz(details!.MemoryClockHertz)
            : Unavailable;
    }

    /// <summary>
    /// Lays the cells whose metric has a sample in the current history out two per row, with the memory cell last
    /// across both columns. A metric the driver never reports gets no graph; one that stops reporting keeps its graph
    /// until its last sample leaves the history window.
    /// </summary>
    private void ArrangeCells()
    {
        bool changed = !_hasArrangedCells;
        for (int cellIndex = 0; cellIndex < HalfWidthCellCount; cellIndex++)
        {
            bool isVisible = HasHalfWidthCellSamples(cellIndex);
            changed |= _halfWidthCellVisible[cellIndex] != isVisible;
            _halfWidthCellVisible[cellIndex] = isVisible;
        }

        bool memoryVisible = _dedicatedMemoryHistory.Count > 0 || _sharedMemoryHistory.Count > 0;
        changed |= _memoryCellVisible != memoryVisible;
        _memoryCellVisible = memoryVisible;
        if (!changed) return;

        _hasArrangedCells = true;
        Children.Clear();
        RowDefinitions.Clear();
        int position = 0;
        for (int cellIndex = 0; cellIndex < HalfWidthCellCount; cellIndex++)
        {
            if (!_halfWidthCellVisible[cellIndex]) continue;

            Grid cell = _halfWidthCells[cellIndex];
            if (position % 2 == 0) RowDefinitions.Add(new RowDefinition(GridLength.Star));
            SetRow(cell, position / 2);
            SetColumn(cell, position % 2);
            Children.Add(cell);
            position++;
        }

        if (!_memoryCellVisible) return;

        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        SetRow(_memoryCell, RowDefinitions.Count - 1);
        Children.Add(_memoryCell);
    }

    private bool HasHalfWidthCellSamples(int cellIndex) =>
        cellIndex switch
        {
            PairedEngineCell => _engineHistories[PairedPrimaryLane].Count > 0
                                || _engineHistories[PairedSecondaryLane].Count > 0,
            PowerCell => _powerHistory.Count > 0,
            ClockCell => _clockHistory.Count > 0,
            MemoryClockCell => _memoryClockHistory.Count > 0,
            _ => _engineHistories[cellIndex].Count > 0
        };

    private void AdvanceHistories(long timestamp)
    {
        for (int laneIndex = 0; laneIndex < EngineLaneCount; laneIndex++)
            _engineHistories[laneIndex].AdvanceTo(timestamp);
        _powerHistory.AdvanceTo(timestamp);
        _clockHistory.AdvanceTo(timestamp);
        _memoryClockHistory.AdvanceTo(timestamp);
        _dedicatedMemoryHistory.AdvanceTo(timestamp);
        _sharedMemoryHistory.AdvanceTo(timestamp);
    }

    private void UpdateMemoryScale(GPUPerformanceSnapshot GPU)
    {
        GPUPerformanceDetailsSnapshot? details = GPU.Details;
        ulong dedicatedCapacityBytes = details?.HasHardwareReservedMemoryData == true
            ? SaturatingAdd(
                GPU.DedicatedMemoryCapacityBytes,
                details.HardwareReservedMemoryBytes)
            : GPU.DedicatedMemoryCapacityBytes;
        double dedicatedCapacity = dedicatedCapacityBytes > 0
            ? dedicatedCapacityBytes
            : Math.Max(val1: 1, _dedicatedMemoryHistory.GetMaximumValue());
        double sharedCapacity = GPU.SharedMemoryCapacityBytes > 0
            ? GPU.SharedMemoryCapacityBytes
            : Math.Max(val1: 1, _sharedMemoryHistory.GetMaximumValue());

        // Each series fills the graph against its own capacity, as the two separate graphs did
        _memoryGraph.SetMaximumValue(dedicatedCapacity);
        _memoryGraph.SetSecondaryMaximumValue(sharedCapacity);
        _memoryCapacityLabel.Text = string.Concat(
            dedicatedCapacityBytes > 0
                ? PerformanceDevicePresentationFactory.FormatBytes(dedicatedCapacityBytes)
                : Unavailable,
            " / ",
            GPU.SharedMemoryCapacityBytes > 0
                ? PerformanceDevicePresentationFactory.FormatBytes(GPU.SharedMemoryCapacityBytes)
                : Unavailable);
    }

    private void RefreshGraphs()
    {
        for (int cellIndex = 0; cellIndex < SingleEngineCellCount; cellIndex++)
            _singleEngineGraphs[cellIndex].Refresh();
        _pairedEngineGraph.Refresh();
        _powerGraph.Refresh();
        _clockGraph.Refresh();
        _memoryClockGraph.Refresh();
        _memoryGraph.Refresh();
    }

    private PerformanceHistoryGraph CreateEngineGraph(
        PerformanceHistory history,
        Color accent,
        bool isLightTheme,
        Func<long, string?>? hoverMetricProvider = null) =>
        new(history, accent, _palette, _resources, isLightTheme, hoverMetricProvider)
        {
            MinHeight = _resources.AxamlTaskManagerPerformance.GPUDetailEngineGraphMinimumHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

    private PerformanceMetricHistoryGraph CreateMetricGraph(
        PerformanceMetricHistory history,
        string label,
        Func<double, string> formatter,
        Color accent,
        bool isLightTheme) =>
        new(history, secondaryHistory: null, label, string.Empty, formatter, accent, _palette, _resources, isLightTheme)
        {
            MinHeight = _resources.AxamlTaskManagerPerformance.GPUDetailEngineGraphMinimumHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

    private Grid BuildGraphCell(
        string title,
        Control graph,
        string initialValue,
        out TextBlock titleLabel,
        out TextBlock valueLabel)
    {
        titleLabel = TrayAppDotNETSettingsUI.Text(
            title,
            _palette,
            _resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)_resources.AxamlTaskManagerPerformance.TextFontWeight);
        titleLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        valueLabel = TrayAppDotNETSettingsUI.Text(
            initialValue,
            _palette,
            _resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)_resources.AxamlTaskManagerPerformance.TextFontWeight);
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        valueLabel.Margin = _resources.AxamlTaskManagerPerformance.GPUDetailGraphValueMargin;
        Grid header = new()
        {
            Margin = _resources.AxamlTaskManagerPerformance.SpecialGraphHeaderMargin,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { titleLabel }
        };
        SetColumn(valueLabel, value: 1);
        header.Children.Add(valueLabel);

        Grid cell = new()
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            Children = { header }
        };
        SetRow(graph, value: 1);
        cell.Children.Add(graph);
        return cell;
    }

    private PerformanceHistory CreatePercentageHistory() => new(
        _historyLengthMinutes,
        _sampleIntervalMilliseconds);

    private PerformanceMetricHistory CreateMetricHistory() => new(
        _historyLengthMinutes,
        _sampleIntervalMilliseconds);

    private static bool ContainsEngineName(
        ReadOnlySpan<string> engineNames,
        string candidateName)
    {
        for (int engineIndex = 0; engineIndex < engineNames.Length; engineIndex++)
        {
            if (string.Equals(
                    engineNames[engineIndex],
                    candidateName,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ContainsEngineIndex(
        ReadOnlySpan<GPUPerformanceDetailEngineSnapshot> engines,
        int candidateIndex)
    {
        for (int engineIndex = 0; engineIndex < engines.Length; engineIndex++)
        {
            if (engines[engineIndex].EngineIndex == candidateIndex)
                return true;
        }

        return false;
    }

    private static GPUPerformanceSnapshot? FindGPU(
        ReadOnlySpan<GPUPerformanceSnapshot> GPUs,
        string deviceID)
    {
        for (int GPUIndex = 0; GPUIndex < GPUs.Length; GPUIndex++)
        {
            if (string.Equals(GPUs[GPUIndex].DeviceID, deviceID, StringComparison.Ordinal))
                return GPUs[GPUIndex];
        }

        return null;
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        left > ulong.MaxValue - right ? ulong.MaxValue : left + right;
}
