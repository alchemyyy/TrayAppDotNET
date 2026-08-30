using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Displays the selected disk's read and write throughput history.</summary>
internal sealed class DiskPerformanceDetailsView : StackPanel
{
    private const double BytesPerMebibyte = 1_048_576;

    private readonly TextBlock _maximumLabel;
    private readonly TextBlock _historyWindowLabel;
    private PerformanceMetricHistory _readHistory;
    private PerformanceMetricHistory _writeHistory;
    private readonly PerformanceMetricHistoryGraph _graph;
    private string? _deviceID;
    private int _historyLengthMinutes;
    private int _sampleIntervalMilliseconds;
    private long _lastTimestamp;

    public DiskPerformanceDetailsView(
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        int historyLengthMinutes,
        int sampleIntervalMilliseconds)
    {
        _historyLengthMinutes = historyLengthMinutes;
        _sampleIntervalMilliseconds = sampleIntervalMilliseconds;
        _readHistory = CreateHistory();
        _writeHistory = CreateHistory();
        IsVisible = false;
        Margin = resources.AxamlTaskManagerPerformance.DiskTransferMargin;

        TextBlock heading = TrayAppDotNETSettingsUI.Text(
            "Disk transfer rate",
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            FontWeight.Normal);
        _maximumLabel = TrayAppDotNETSettingsUI.Text(
            "1 MB/s",
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            FontWeight.Normal);
        _maximumLabel.HorizontalAlignment = HorizontalAlignment.Right;
        Grid header = CreateScaleRow(heading, _maximumLabel);
        header.Margin = resources.AxamlTaskManagerPerformance.SpecialGraphHeaderMargin;
        Children.Add(header);

        Color accent = PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.Disk);
        _graph = new PerformanceMetricHistoryGraph(
            _readHistory,
            _writeHistory,
            "R",
            "W",
            PerformanceDevicePresentationFactory.FormatBytesPerSecond,
            accent,
            palette,
            resources)
        {
            Height = resources.AxamlTaskManagerPerformance.DiskTransferGraphHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Children.Add(_graph);

        _historyWindowLabel = TrayAppDotNETSettingsUI.Text(
            PerformanceDevicePresentationFactory.FormatHistoryWindow(historyLengthMinutes),
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            FontWeight.Normal);
        TextBlock minimumLabel = TrayAppDotNETSettingsUI.Text(
            "0",
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            FontWeight.Normal);
        minimumLabel.HorizontalAlignment = HorizontalAlignment.Right;
        Children.Add(CreateScaleRow(_historyWindowLabel, minimumLabel));
    }

    /// <summary>Rebuilds the selected disk history when selection or sampling settings change.</summary>
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
        _readHistory = CreateHistory();
        _writeHistory = CreateHistory();
        _graph.SetHistories(_readHistory, _writeHistory);
        _historyWindowLabel.Text = PerformanceDevicePresentationFactory.FormatHistoryWindow(
            historyLengthMinutes);
        _lastTimestamp = 0;
        for (int snapshotIndex = 0; snapshotIndex < snapshots.Count; snapshotIndex++)
            Append(snapshots[snapshotIndex]);
        UpdateScale();
    }

    /// <summary>Appends a snapshot when it belongs to the selected disk.</summary>
    public void Append(PerformanceSnapshot snapshot)
    {
        if (_deviceID == null || snapshot.CapturedTimestamp <= _lastTimestamp) return;

        _readHistory.AdvanceTo(snapshot.CapturedTimestamp);
        _writeHistory.AdvanceTo(snapshot.CapturedTimestamp);
        ReadOnlySpan<DiskPerformanceSnapshot> disks = snapshot.Disks.Span;
        for (int diskIndex = 0; diskIndex < disks.Length; diskIndex++)
        {
            DiskPerformanceSnapshot disk = disks[diskIndex];
            if (!string.Equals(disk.DeviceID, _deviceID, StringComparison.Ordinal)) continue;
            if (disk.HasPerformanceSample)
            {
                _readHistory.Add(snapshot.CapturedTimestamp, disk.ReadBytesPerSecond);
                _writeHistory.Add(snapshot.CapturedTimestamp, disk.WriteBytesPerSecond);
            }
            break;
        }

        _lastTimestamp = snapshot.CapturedTimestamp;
        UpdateScale();
        _graph.Refresh();
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

    /// <summary>Chooses a stable binary scale with headroom for the largest visible transfer.</summary>
    internal static double CalculateTransferScale(double maximumTransferBytesPerSecond)
    {
        double requiredScale = double.IsFinite(maximumTransferBytesPerSecond)
            ? Math.Max(BytesPerMebibyte, maximumTransferBytesPerSecond * 1.1)
            : BytesPerMebibyte;
        double unit = BytesPerMebibyte;
        while (unit < requiredScale && unit <= double.MaxValue / 2)
            unit *= 2;
        return unit;
    }

    private void UpdateScale()
    {
        double maximumTransfer = Math.Max(
            _readHistory.GetMaximumValue(),
            _writeHistory.GetMaximumValue());
        double scale = CalculateTransferScale(maximumTransfer);
        _graph.SetMaximumValue(scale);
        _maximumLabel.Text = PerformanceDevicePresentationFactory.FormatBytesPerSecond(scale);
    }

    private PerformanceMetricHistory CreateHistory() => new(
        _historyLengthMinutes,
        _sampleIntervalMilliseconds);

    private static Grid CreateScaleRow(Control left, Control right)
    {
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children = { left }
        };
        Grid.SetColumn(right, 1);
        row.Children.Add(right);
        return row;
    }
}

/// <summary>Displays four GPU engine histories plus dedicated and shared memory histories.</summary>
internal sealed class GPUPerformanceDetailsView : Grid
{
    private const int EngineGraphCount = 4;
    private static readonly string[] PreferredEngineNames =
    [
        "3D",
        "Copy",
        "Video Encode",
        "Video Decode"
    ];

    private readonly SettingsPalette _palette;
    private readonly TaskManagerWindowResources _resources;
    private readonly PerformanceHistory[] _engineHistories = new PerformanceHistory[EngineGraphCount];
    private readonly PerformanceHistoryGraph[] _engineGraphs = new PerformanceHistoryGraph[EngineGraphCount];
    private readonly string[] _engineNames = new string[EngineGraphCount];
    private readonly TextBlock[] _engineTitleLabels = new TextBlock[EngineGraphCount];
    private readonly TextBlock[] _engineValueLabels = new TextBlock[EngineGraphCount];
    private readonly TextBlock _dedicatedCapacityLabel;
    private readonly TextBlock _sharedCapacityLabel;
    private PerformanceMetricHistory _dedicatedMemoryHistory;
    private PerformanceMetricHistory _sharedMemoryHistory;
    private readonly PerformanceMetricHistoryGraph _dedicatedMemoryGraph;
    private readonly PerformanceMetricHistoryGraph _sharedMemoryGraph;
    private string? _deviceID;
    private int _historyLengthMinutes;
    private int _sampleIntervalMilliseconds;
    private long _lastTimestamp;

    public GPUPerformanceDetailsView(
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        int historyLengthMinutes,
        int sampleIntervalMilliseconds)
    {
        _palette = palette;
        _resources = resources;
        _historyLengthMinutes = historyLengthMinutes;
        _sampleIntervalMilliseconds = sampleIntervalMilliseconds;
        _dedicatedMemoryHistory = CreateMetricHistory();
        _sharedMemoryHistory = CreateMetricHistory();
        IsVisible = false;
        ColumnSpacing = resources.AxamlTaskManagerPerformance.GPUDetailGraphColumnSpacing;
        RowSpacing = resources.AxamlTaskManagerPerformance.GPUDetailGraphRowSpacing;
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Color accent = PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.GPU);
        for (int engineIndex = 0; engineIndex < EngineGraphCount; engineIndex++)
        {
            _engineHistories[engineIndex] = CreatePercentageHistory();
            PerformanceHistoryGraph graph = new(
                _engineHistories[engineIndex],
                accent,
                palette,
                resources)
            {
                MinHeight = resources.AxamlTaskManagerPerformance.GPUDetailEngineGraphMinimumHeight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _engineGraphs[engineIndex] = graph;
            _engineNames[engineIndex] = PreferredEngineNames[engineIndex];
            TextBlock titleLabel;
            TextBlock valueLabel;
            Grid engineCell = BuildGraphCell(
                PreferredEngineNames[engineIndex],
                graph,
                "0%",
                out titleLabel,
                out valueLabel);
            _engineTitleLabels[engineIndex] = titleLabel;
            _engineValueLabels[engineIndex] = valueLabel;
            Grid.SetColumn(engineCell, engineIndex % 2);
            Grid.SetRow(engineCell, engineIndex / 2);
            Children.Add(engineCell);
        }

        _dedicatedMemoryGraph = new PerformanceMetricHistoryGraph(
            _dedicatedMemoryHistory,
            null,
            "Dedicated GPU memory",
            string.Empty,
            PerformanceDevicePresentationFactory.FormatBytes,
            accent,
            palette,
            resources)
        {
            Height = resources.AxamlTaskManagerPerformance.GPUDetailMemoryGraphHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid dedicatedCell = BuildGraphCell(
            "Dedicated GPU memory",
            _dedicatedMemoryGraph,
            "Unavailable",
            out _,
            out _dedicatedCapacityLabel);
        Grid.SetRow(dedicatedCell, 2);
        Grid.SetColumnSpan(dedicatedCell, 2);
        Children.Add(dedicatedCell);

        _sharedMemoryGraph = new PerformanceMetricHistoryGraph(
            _sharedMemoryHistory,
            null,
            "Shared GPU memory",
            string.Empty,
            PerformanceDevicePresentationFactory.FormatBytes,
            accent,
            palette,
            resources)
        {
            Height = resources.AxamlTaskManagerPerformance.GPUDetailMemoryGraphHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid sharedCell = BuildGraphCell(
            "Shared GPU memory",
            _sharedMemoryGraph,
            "Unavailable",
            out _,
            out _sharedCapacityLabel);
        Grid.SetRow(sharedCell, 3);
        Grid.SetColumnSpan(sharedCell, 2);
        Children.Add(sharedCell);
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
        for (int engineIndex = 0; engineIndex < EngineGraphCount; engineIndex++)
        {
            _engineHistories[engineIndex] = CreatePercentageHistory();
            _engineGraphs[engineIndex].SetHistory(_engineHistories[engineIndex]);
        }
        _dedicatedMemoryHistory = CreateMetricHistory();
        _sharedMemoryHistory = CreateMetricHistory();
        _dedicatedMemoryGraph.SetHistories(_dedicatedMemoryHistory, null);
        _sharedMemoryGraph.SetHistories(_sharedMemoryHistory, null);
        _lastTimestamp = 0;
        for (int snapshotIndex = 0; snapshotIndex < snapshots.Count; snapshotIndex++)
            Append(snapshots[snapshotIndex]);
    }

    /// <summary>Appends the selected adapter's engine and memory samples.</summary>
    public void Append(PerformanceSnapshot snapshot)
    {
        if (_deviceID == null || snapshot.CapturedTimestamp <= _lastTimestamp) return;

        AdvanceHistories(snapshot.CapturedTimestamp);
        GPUPerformanceSnapshot? selectedGPU = FindGPU(snapshot.GPUs.Span, _deviceID);
        if (selectedGPU != null)
        {
            ReadOnlySpan<GPUPerformanceDetailEngineSnapshot> detailEngines =
                selectedGPU.Details != null
                    ? selectedGPU.Details.Engines.Span
                    : ReadOnlySpan<GPUPerformanceDetailEngineSnapshot>.Empty;
            for (int engineIndex = 0; engineIndex < EngineGraphCount; engineIndex++)
            {
                bool hasEngineSample;
                double utilizationPercent;
                if (engineIndex < detailEngines.Length)
                {
                    GPUPerformanceDetailEngineSnapshot engine = detailEngines[engineIndex];
                    _engineNames[engineIndex] = engine.Name;
                    _engineTitleLabels[engineIndex].Text = engine.Name;
                    hasEngineSample = engine.HasUtilizationSample;
                    utilizationPercent = engine.UtilizationPercent;
                }
                else
                {
                    bool hasFallbackSample = TrySelectFallbackEngine(
                        _engineNames.AsSpan(0, engineIndex),
                        detailEngines,
                        selectedGPU.Engines.Span,
                        out string fallbackEngineName,
                        out utilizationPercent);
                    _engineNames[engineIndex] = fallbackEngineName;
                    _engineTitleLabels[engineIndex].Text = fallbackEngineName;
                    hasEngineSample = selectedGPU.HasUtilizationSample
                                      && hasFallbackSample;
                }
                if (hasEngineSample)
                    _engineHistories[engineIndex].Add(
                        snapshot.CapturedTimestamp,
                        utilizationPercent);
                _engineValueLabels[engineIndex].Text = hasEngineSample
                    ? PerformanceDevicePresentationFactory.FormatPercent(
                        true,
                        utilizationPercent)
                    : "Unavailable";
            }

            if (selectedGPU.HasDedicatedMemoryData)
            {
                _dedicatedMemoryHistory.Add(
                    snapshot.CapturedTimestamp,
                    selectedGPU.DedicatedMemoryBytes);
            }
            if (selectedGPU.HasSharedMemoryData)
            {
                _sharedMemoryHistory.Add(
                    snapshot.CapturedTimestamp,
                    selectedGPU.SharedMemoryBytes);
            }
            UpdateMemoryScale(selectedGPU);
        }

        _lastTimestamp = snapshot.CapturedTimestamp;
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
                {
                    continue;
                }

                utilizationPercent = Math.Max(
                    utilizationPercent,
                    Math.Clamp(liveEngine.UtilizationPercent, 0, 100));
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
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsEngineIndex(
        ReadOnlySpan<GPUPerformanceDetailEngineSnapshot> engines,
        int candidateIndex)
    {
        for (int engineIndex = 0; engineIndex < engines.Length; engineIndex++)
        {
            if (engines[engineIndex].EngineIndex == candidateIndex) return true;
        }

        return false;
    }

    private void AdvanceHistories(long timestamp)
    {
        for (int engineIndex = 0; engineIndex < EngineGraphCount; engineIndex++)
            _engineHistories[engineIndex].AdvanceTo(timestamp);
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
            : Math.Max(1, _dedicatedMemoryHistory.GetMaximumValue());
        double sharedCapacity = GPU.SharedMemoryCapacityBytes > 0
            ? GPU.SharedMemoryCapacityBytes
            : Math.Max(1, _sharedMemoryHistory.GetMaximumValue());
        _dedicatedMemoryGraph.SetMaximumValue(dedicatedCapacity);
        _sharedMemoryGraph.SetMaximumValue(sharedCapacity);
        _dedicatedCapacityLabel.Text = dedicatedCapacityBytes > 0
            ? PerformanceDevicePresentationFactory.FormatBytes(dedicatedCapacityBytes)
            : "Unavailable";
        _sharedCapacityLabel.Text = GPU.SharedMemoryCapacityBytes > 0
            ? PerformanceDevicePresentationFactory.FormatBytes(GPU.SharedMemoryCapacityBytes)
            : "Unavailable";
    }

    private void RefreshGraphs()
    {
        for (int engineIndex = 0; engineIndex < EngineGraphCount; engineIndex++)
            _engineGraphs[engineIndex].Refresh();
        _dedicatedMemoryGraph.Refresh();
        _sharedMemoryGraph.Refresh();
    }

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
            FontWeight.Normal);
        valueLabel = TrayAppDotNETSettingsUI.Text(
            initialValue,
            _palette,
            _resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            FontWeight.Normal);
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        Grid header = new()
        {
            Margin = _resources.AxamlTaskManagerPerformance.SpecialGraphHeaderMargin,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children = { titleLabel }
        };
        Grid.SetColumn(valueLabel, 1);
        header.Children.Add(valueLabel);

        Grid cell = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            Children = { header }
        };
        Grid.SetRow(graph, 1);
        cell.Children.Add(graph);
        return cell;
    }

    private PerformanceHistory CreatePercentageHistory() => new(
        _historyLengthMinutes,
        _sampleIntervalMilliseconds);

    private PerformanceMetricHistory CreateMetricHistory() => new(
        _historyLengthMinutes,
        _sampleIntervalMilliseconds);

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
