using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Displays direct-OS CPU, memory, GPU, network, and disk performance snapshots.</summary>
internal sealed class PerformancePage : TaskManagerPageLayout, IDisposable
{
    private const string SelectedGraphViewGlyph = "\uE73E";
    private const int MaximumDetailStatistics = 16;
    private const double BytesPerGibibyte = 1_073_741_824;

    private readonly AppSettings _settings;
    private readonly SettingsPalette _palette;
    private readonly TaskManagerWindowResources _resources;
    private readonly PerformanceSnapshotService _snapshotService;
    private readonly PerformanceDeviceColumn _deviceColumn;
    private readonly Dictionary<string, PerformanceDevicePresentation> _devices =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, PerformanceDeviceCard> _deviceCards =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, PerformanceHistory> _histories =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, NetworkTransferRateHistories> _networkTransferRateHistories =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, DiskTransferRateHistories> _diskTransferRateHistories =
        new(StringComparer.Ordinal);
    private readonly List<string> _staleDeviceIDs = [];
    private readonly TextBlock _detailTitle;
    private readonly TextBlock _detailHardwareName;
    private readonly TextBlock _detailGraphLabel;
    private readonly TextBlock _graphWindowLabel;
    private readonly Grid _genericGraphHeader;
    private readonly Grid _genericGraphFooter;
    private readonly Grid _graphSurface;
    private readonly PerformanceHistoryGraph _detailGraph;
    private readonly MemoryCompositionView _memoryCompositionView;
    private readonly MemoryModuleDetailsPanel _memoryModuleDetailsPanel;
    private readonly DiskPerformanceDetailsView _diskPerformanceDetailsView;
    private readonly GPUPerformanceDetailsView _gpuPerformanceDetailsView;
    private readonly Grid _cpuLogicalProcessorGrid;
    private readonly CPUPerformanceDetailedView _cpuDetailedView;
    private readonly List<PerformanceHistory> _cpuLogicalProcessorHistories = [];
    private readonly List<PerformanceHistoryGraph> _cpuLogicalProcessorGraphs = [];
    private readonly WrapPanel _primaryStatistics;
    private readonly StackPanel _metadataStatistics;
    private readonly StackPanel[] _statisticContainers = new StackPanel[MaximumDetailStatistics];
    private readonly TextBlock[] _statisticLabels = new TextBlock[MaximumDetailStatistics];
    private readonly TextBlock[] _statisticValues = new TextBlock[MaximumDetailStatistics];
    private PerformanceHardwareNameResolver _hardwareNameResolver;
    private MemoryPerformanceSnapshot _latestMemorySnapshot = MemoryPerformanceSnapshot.Empty;
    private PerformanceHistory _cpuHighestCoreHistory;
    private PerformanceMetricHistory _memoryUsedHistory;
    private TaskManagerContextMenuWindow? _cpuGraphContextMenuWindow;
    private string? _selectedDeviceID;
    private int _historyLengthMinutes;
    private int _sampleIntervalMilliseconds;
    private long _lastProcessedTimestamp;
    private int _configuredStatisticCount = -1;
    private int _configuredPrimaryStatisticCount = -1;
    private PerformanceDeviceKind? _configuredStatisticDeviceKind;
    private bool _hasProcessedSnapshot;
    private bool _disposed;

    public PerformancePage(
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        PerformanceSnapshotService snapshotService)
        : base("Performance", palette, resources)
    {
        _settings = settings;
        _palette = palette;
        _resources = resources;
        _snapshotService = snapshotService;
        _historyLengthMinutes = PerformanceSamplingSettings.NormalizeHistoryLengthMinutes(
            settings.PerformanceHistoryLengthMinutes);
        _sampleIntervalMilliseconds = PerformanceSamplingSettings.NormalizeSampleIntervalMilliseconds(
            settings.PerformanceSampleIntervalMilliseconds);
        _cpuHighestCoreHistory = CreateHistory();
        _memoryUsedHistory = CreateMetricHistory();
        _hardwareNameResolver = PerformanceHardwareNameResolver.Create(
            settings.PerformanceHardwareNameReplacementRules);

        MainContent.Margin = resources.AxamlTaskManagerPerformance.BodyMargin;
        MainContent.ColumnDefinitions.Add(new ColumnDefinition(
            new GridLength(resources.AxamlTaskManagerPerformance.DeviceColumnWidth)));
        MainContent.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        _deviceColumn = new PerformanceDeviceColumn(OnDeviceSelected, OnDeviceOrderChanged);
        ScrollViewer deviceScroll = new()
        {
            Content = _deviceColumn,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Border deviceColumnFrame = new()
        {
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = resources.AxamlTaskManagerPerformance.DeviceColumnBorderThickness,
            Padding = resources.AxamlTaskManagerPerformance.DeviceColumnPadding,
            Child = deviceScroll
        };
        MainContent.Children.Add(deviceColumnFrame);

        Grid details = new()
        {
            Margin = resources.AxamlTaskManagerPerformance.DetailMargin,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        Grid.SetColumn(details, 1);
        MainContent.Children.Add(details);

        _detailTitle = TrayAppDotNETSettingsUI.Text(
            "Performance",
            palette,
            resources.AxamlTaskManagerPerformance.DetailTitleFontSize,
            FontWeight.Normal);
        _detailTitle.VerticalAlignment = VerticalAlignment.Center;
        _detailHardwareName = TrayAppDotNETSettingsUI.Text(
            string.Empty,
            palette,
            resources.AxamlTaskManagerPerformance.DetailDeviceNameFontSize,
            FontWeight.Normal);
        _detailHardwareName.HorizontalAlignment = HorizontalAlignment.Right;
        _detailHardwareName.VerticalAlignment = VerticalAlignment.Center;
        _detailHardwareName.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid detailHeader = new()
        {
            Margin = resources.AxamlTaskManagerPerformance.DetailHeaderMargin,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            }
        };
        detailHeader.Children.Add(_detailTitle);
        Grid.SetColumn(_detailHardwareName, 1);
        detailHeader.Children.Add(_detailHardwareName);
        details.Children.Add(detailHeader);

        _detailGraphLabel = TrayAppDotNETSettingsUI.Text(
            string.Concat(
                "% Utilization over ",
                PerformanceDevicePresentationFactory.FormatHistoryWindow(_historyLengthMinutes)),
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            FontWeight.Normal);
        TextBlock graphMaximumLabel = TrayAppDotNETSettingsUI.Text(
            "100%",
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            FontWeight.Normal);
        graphMaximumLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _genericGraphHeader = new Grid
        {
            Margin = resources.AxamlTaskManagerPerformance.DetailGraphLabelMargin,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        _genericGraphHeader.Children.Add(_detailGraphLabel);
        Grid.SetColumn(graphMaximumLabel, 1);
        _genericGraphHeader.Children.Add(graphMaximumLabel);
        PerformanceHistory initialHistory = CreateHistory();
        _detailGraph = new PerformanceHistoryGraph(
            initialHistory,
            PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.CPU),
            palette,
            resources,
            FormatDetailGraphHoverMetric)
        {
            MinHeight = resources.AxamlTaskManagerPerformance.DetailGraphMinimumHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _cpuLogicalProcessorGrid = new Grid
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsVisible = false
        };
        _cpuDetailedView = new CPUPerformanceDetailedView(palette, resources);
        _gpuPerformanceDetailsView = new GPUPerformanceDetailsView(
            palette,
            resources,
            _historyLengthMinutes,
            _sampleIntervalMilliseconds);
        _graphSurface = new Grid
        {
            MinHeight = resources.AxamlTaskManagerPerformance.DetailGraphMinimumHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                _detailGraph,
                _cpuLogicalProcessorGrid,
                _cpuDetailedView,
                _gpuPerformanceDetailsView
            }
        };
        _graphSurface.PointerPressed += OnGraphSurfacePointerPressed;
        Grid graphArea = new()
        {
            Margin = resources.AxamlTaskManagerPerformance.DetailGraphMargin,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        graphArea.Children.Add(_genericGraphHeader);
        Grid.SetRow(_graphSurface, 1);
        graphArea.Children.Add(_graphSurface);
        _graphWindowLabel = TrayAppDotNETSettingsUI.Text(
            PerformanceDevicePresentationFactory.FormatHistoryWindow(_historyLengthMinutes),
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            FontWeight.Normal);
        TextBlock graphMinimumLabel = TrayAppDotNETSettingsUI.Text(
            "0",
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            FontWeight.Normal);
        graphMinimumLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _genericGraphFooter = new Grid
        {
            Margin = resources.AxamlTaskManagerPerformance.DetailGraphScaleMargin,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        _genericGraphFooter.Children.Add(_graphWindowLabel);
        Grid.SetColumn(graphMinimumLabel, 1);
        _genericGraphFooter.Children.Add(graphMinimumLabel);
        Grid.SetRow(_genericGraphFooter, 2);
        graphArea.Children.Add(_genericGraphFooter);
        Grid.SetRow(graphArea, 1);
        details.Children.Add(graphArea);

        _memoryCompositionView = new MemoryCompositionView(palette, resources);
        Grid.SetRow(_memoryCompositionView, 2);
        details.Children.Add(_memoryCompositionView);

        _diskPerformanceDetailsView = new DiskPerformanceDetailsView(
            palette,
            resources,
            _historyLengthMinutes,
            _sampleIntervalMilliseconds);
        Grid.SetRow(_diskPerformanceDetailsView, 2);
        details.Children.Add(_diskPerformanceDetailsView);

        _primaryStatistics = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
        _metadataStatistics = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
        for (int statisticIndex = 0; statisticIndex < MaximumDetailStatistics; statisticIndex++)
        {
            TextBlock label = TrayAppDotNETSettingsUI.Text(
                string.Empty,
                palette,
                resources.AxamlTaskManagerPerformance.DetailStatisticLabelFontSize,
                FontWeight.Normal);
            TextBlock value = TrayAppDotNETSettingsUI.Text(
                string.Empty,
                palette,
                statisticIndex < 2
                    ? resources.AxamlTaskManagerPerformance.DetailPrimaryStatisticValueFontSize
                    : resources.AxamlTaskManagerPerformance.DetailStatisticValueFontSize,
                FontWeight.Normal);
            StackPanel statistic = new()
            {
                IsVisible = false,
                Children = { label, value }
            };
            _statisticContainers[statisticIndex] = statistic;
            _statisticLabels[statisticIndex] = label;
            _statisticValues[statisticIndex] = value;
        }
        Grid statistics = new()
        {
            ColumnSpacing = resources.AxamlTaskManagerPerformance.DetailStatisticsColumnSpacing,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(
                    resources.AxamlTaskManagerPerformance.DetailStatisticsPrimaryRatio,
                    GridUnitType.Star)),
                new ColumnDefinition(new GridLength(
                    resources.AxamlTaskManagerPerformance.DetailStatisticsMetadataRatio,
                    GridUnitType.Star))
            }
        };
        statistics.Children.Add(_primaryStatistics);
        Grid.SetColumn(_metadataStatistics, 1);
        statistics.Children.Add(_metadataStatistics);
        Grid.SetRow(statistics, 3);
        details.Children.Add(statistics);

        _memoryModuleDetailsPanel = new MemoryModuleDetailsPanel(palette, resources);
        Grid.SetRow(_memoryModuleDetailsPanel, 4);
        details.Children.Add(_memoryModuleDetailsPanel);

        _settings.PropertyChanged += OnSettingsPropertyChanged;
        _snapshotService.SnapshotUpdated += OnSnapshotUpdated;
        try
        {
            RebuildHistoriesFromSnapshotArchive();
        }
        catch
        {
            _snapshotService.SnapshotUpdated -= OnSnapshotUpdated;
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
            throw;
        }
    }

    private void OnSnapshotUpdated(object? sender, PerformanceSnapshot snapshot)
    {
        if (_disposed) return;
        SynchronizeSnapshotHistory(snapshot);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_disposed) return;

        if (eventArgs.PropertyName == nameof(AppSettings.PerformanceHardwareNameReplacementRules))
        {
            _hardwareNameResolver = PerformanceHardwareNameResolver.Create(
                _settings.PerformanceHardwareNameReplacementRules);
            ApplySnapshotPresentation(_snapshotService.GetLatestSnapshot());
            return;
        }

        if (eventArgs.PropertyName == nameof(AppSettings.ShowMemoryModuleSerialNumbers))
        {
            UpdateSelectionAndDetails();
            return;
        }

        if (eventArgs.PropertyName == nameof(AppSettings.ShowCPUHighestCoreTrace))
        {
            UpdateCPUHighestCoreTraceVisibility();
            return;
        }

        if (eventArgs.PropertyName == nameof(AppSettings.CPUPerformanceGraphView))
        {
            UpdateSelectionAndDetails();
            RefreshVisibleCPUGraphs();
            return;
        }

        if (eventArgs.PropertyName is not nameof(AppSettings.PerformanceHistoryLengthMinutes)
            and not nameof(AppSettings.PerformanceSampleIntervalMilliseconds)) return;

        int historyLengthMinutes = PerformanceSamplingSettings.NormalizeHistoryLengthMinutes(
            _settings.PerformanceHistoryLengthMinutes);
        int sampleIntervalMilliseconds = PerformanceSamplingSettings.NormalizeSampleIntervalMilliseconds(
            _settings.PerformanceSampleIntervalMilliseconds);
        if (historyLengthMinutes == _historyLengthMinutes
            && sampleIntervalMilliseconds == _sampleIntervalMilliseconds)
        {
            return;
        }

        _historyLengthMinutes = historyLengthMinutes;
        _sampleIntervalMilliseconds = sampleIntervalMilliseconds;
        _graphWindowLabel.Text = PerformanceDevicePresentationFactory.FormatHistoryWindow(
            historyLengthMinutes);
        RebuildHistoriesFromSnapshotArchive();
    }

    /// <summary>Ingests every retained sample newer than the latest page-local sample.</summary>
    private void SynchronizeSnapshotHistory(PerformanceSnapshot wakeSnapshot)
    {
        IReadOnlyList<PerformanceSnapshot> pendingSnapshots = _hasProcessedSnapshot
            ? _snapshotService.GetSnapshotHistoryAfter(_lastProcessedTimestamp)
            : _snapshotService.GetSnapshotHistory();
        if (pendingSnapshots.Count == 0)
        {
            if (_hasProcessedSnapshot
                && wakeSnapshot.CapturedTimestamp <= _lastProcessedTimestamp)
            {
                return;
            }

            pendingSnapshots = new PerformanceSnapshot[] { wakeSnapshot };
        }

        if (RequiresFullHistoryRebuild(pendingSnapshots))
        {
            RebuildHistoriesFromSnapshotArchive();
            return;
        }

        PerformanceSnapshot? latestSnapshot = null;
        for (int snapshotIndex = 0; snapshotIndex < pendingSnapshots.Count; snapshotIndex++)
        {
            PerformanceSnapshot snapshot = pendingSnapshots[snapshotIndex];
            if (_hasProcessedSnapshot && snapshot.CapturedTimestamp <= _lastProcessedTimestamp)
                continue;

            AppendSnapshotHistories(snapshot);
            _lastProcessedTimestamp = snapshot.CapturedTimestamp;
            _hasProcessedSnapshot = true;
            latestSnapshot = snapshot;
        }

        if (latestSnapshot != null)
            ApplySnapshotPresentation(latestSnapshot);
    }

    /// <summary>Rebuilds bounded page-local histories from the application-lifetime archive.</summary>
    private void RebuildHistoriesFromSnapshotArchive()
    {
        IReadOnlyList<PerformanceSnapshot> archivedSnapshots = _snapshotService.GetSnapshotHistory();
        PerformanceSnapshot latestSnapshot = _snapshotService.GetLatestSnapshot();
        if (archivedSnapshots.Count > 0
            && archivedSnapshots[^1].CapturedTimestamp > latestSnapshot.CapturedTimestamp)
        {
            latestSnapshot = archivedSnapshots[^1];
        }

        int logicalProcessorCount = GetLogicalProcessorCount(latestSnapshot.CPU);
        CPUCCDTopology CCDTopology = latestSnapshot.CPU.CCDTopology;
        if (logicalProcessorCount == 0)
        {
            for (int snapshotIndex = archivedSnapshots.Count - 1;
                 snapshotIndex >= 0;
                 snapshotIndex--)
            {
                logicalProcessorCount = GetLogicalProcessorCount(
                    archivedSnapshots[snapshotIndex].CPU);
                if (logicalProcessorCount > 0) break;
            }
        }
        if (!CCDTopology.IsAvailable)
        {
            for (int snapshotIndex = archivedSnapshots.Count - 1;
                 snapshotIndex >= 0;
                 snapshotIndex--)
            {
                CPUCCDTopology archivedTopology = archivedSnapshots[snapshotIndex].CPU.CCDTopology;
                if (!archivedTopology.IsAvailable) continue;

                CCDTopology = archivedTopology;
                break;
            }
        }

        _histories.Clear();
        _cpuHighestCoreHistory = CreateHistory();
        _memoryUsedHistory = CreateMetricHistory();
        _networkTransferRateHistories.Clear();
        _diskTransferRateHistories.Clear();
        RebuildCPULogicalProcessorGraphs(logicalProcessorCount);
        _cpuDetailedView.Rebuild(
            GetOrCreateHistory(CPUPerformanceSnapshot.StableDeviceID),
            _cpuHighestCoreHistory,
            CCDTopology,
            _historyLengthMinutes,
            _sampleIntervalMilliseconds);
        _hasProcessedSnapshot = false;
        _lastProcessedTimestamp = 0;

        for (int snapshotIndex = 0; snapshotIndex < archivedSnapshots.Count; snapshotIndex++)
        {
            PerformanceSnapshot snapshot = archivedSnapshots[snapshotIndex];
            AppendSnapshotHistories(snapshot);
            _lastProcessedTimestamp = snapshot.CapturedTimestamp;
            _hasProcessedSnapshot = true;
        }

        if (!_hasProcessedSnapshot
            || latestSnapshot.CapturedTimestamp > _lastProcessedTimestamp)
        {
            AppendSnapshotHistories(latestSnapshot);
            _lastProcessedTimestamp = latestSnapshot.CapturedTimestamp;
            _hasProcessedSnapshot = true;
        }

        ApplySnapshotPresentation(latestSnapshot);
    }

    /// <summary>Appends one raw snapshot without reconciling live cards or removing stale histories.</summary>
    private void AppendSnapshotHistories(PerformanceSnapshot snapshot)
    {
        _diskPerformanceDetailsView.Append(snapshot);
        _gpuPerformanceDetailsView.Append(snapshot);
        long capturedTimestamp = snapshot.CapturedTimestamp;
        AppendCPUOverallHistories(
            GetOrCreateHistory(snapshot.CPU.DeviceID),
            _cpuHighestCoreHistory,
            snapshot.CPU,
            capturedTimestamp);
        _cpuDetailedView.Append(snapshot.CPU, capturedTimestamp);
        AppendDeviceHistory(
            snapshot.Memory.DeviceID,
            capturedTimestamp,
            snapshot.Memory.HasMemoryData,
            snapshot.Memory.UtilizationPercent);
        _memoryUsedHistory.AdvanceTo(capturedTimestamp);
        if (snapshot.Memory.HasMemoryData)
            _memoryUsedHistory.Add(capturedTimestamp, snapshot.Memory.UsedPhysicalBytes);

        ReadOnlySpan<GPUPerformanceSnapshot> GPUs = snapshot.GPUs.Span;
        for (int GPUIndex = 0; GPUIndex < GPUs.Length; GPUIndex++)
        {
            GPUPerformanceSnapshot GPU = GPUs[GPUIndex];
            AppendDeviceHistory(
                GPU.DeviceID,
                capturedTimestamp,
                GPU.HasUtilizationSample,
                GPU.UtilizationPercent);
        }

        ReadOnlySpan<NetworkPerformanceSnapshot> networks = snapshot.Networks.Span;
        for (int networkIndex = 0; networkIndex < networks.Length; networkIndex++)
        {
            NetworkPerformanceSnapshot network = networks[networkIndex];
            NetworkTransferRateHistories transferRateHistories =
                GetOrCreateNetworkTransferRateHistories(network.DeviceID);
            transferRateHistories.Send.AdvanceTo(capturedTimestamp);
            transferRateHistories.Receive.AdvanceTo(capturedTimestamp);
            if (network.HasThroughputSample)
            {
                transferRateHistories.Send.Add(capturedTimestamp, network.SendBytesPerSecond);
                transferRateHistories.Receive.Add(capturedTimestamp, network.ReceiveBytesPerSecond);
            }

            bool hasUtilization = PerformanceDevicePresentationFactory.TryGetNetworkUtilization(
                network,
                out double utilizationPercent);
            AppendDeviceHistory(
                network.DeviceID,
                capturedTimestamp,
                hasUtilization,
                utilizationPercent);
        }

        ReadOnlySpan<DiskPerformanceSnapshot> disks = snapshot.Disks.Span;
        for (int diskIndex = 0; diskIndex < disks.Length; diskIndex++)
        {
            DiskPerformanceSnapshot disk = disks[diskIndex];
            DiskTransferRateHistories transferRateHistories =
                GetOrCreateDiskTransferRateHistories(disk.DeviceID);
            AppendDiskTransferRateHistories(
                transferRateHistories.Read,
                transferRateHistories.Write,
                disk,
                capturedTimestamp);
            AppendDeviceHistory(
                disk.DeviceID,
                capturedTimestamp,
                disk.HasPerformanceSample,
                disk.ActiveTimePercent);
        }

        AppendCPULogicalProcessorHistories(snapshot.CPU, capturedTimestamp);
    }

    private void AppendDeviceHistory(
        string deviceID,
        long capturedTimestamp,
        bool hasUtilizationSample,
        double utilizationPercent)
    {
        PerformanceHistory history = GetOrCreateHistory(deviceID);
        history.AdvanceTo(capturedTimestamp);
        if (hasUtilizationSample)
            history.Add(capturedTimestamp, utilizationPercent);
    }

    /// <summary>Keeps aggregate and highest-logical-processor CPU histories on one timeline.</summary>
    internal static void AppendCPUOverallHistories(
        PerformanceHistory utilizationHistory,
        PerformanceHistory highestCoreHistory,
        CPUPerformanceSnapshot snapshot,
        long capturedTimestamp)
    {
        ArgumentNullException.ThrowIfNull(utilizationHistory);
        ArgumentNullException.ThrowIfNull(highestCoreHistory);
        ArgumentNullException.ThrowIfNull(snapshot);

        utilizationHistory.AdvanceTo(capturedTimestamp);
        highestCoreHistory.AdvanceTo(capturedTimestamp);
        if (!snapshot.HasUtilizationSample) return;

        utilizationHistory.Add(capturedTimestamp, snapshot.UtilizationPercent);
        highestCoreHistory.Add(capturedTimestamp, snapshot.HighestLogicalProcessorPercent);
    }

    /// <summary>Keeps disk read and write rates on the active-time sample timeline.</summary>
    internal static void AppendDiskTransferRateHistories(
        PerformanceMetricHistory readHistory,
        PerformanceMetricHistory writeHistory,
        DiskPerformanceSnapshot snapshot,
        long capturedTimestamp)
    {
        ArgumentNullException.ThrowIfNull(readHistory);
        ArgumentNullException.ThrowIfNull(writeHistory);
        ArgumentNullException.ThrowIfNull(snapshot);

        readHistory.AdvanceTo(capturedTimestamp);
        writeHistory.AdvanceTo(capturedTimestamp);
        if (!snapshot.HasPerformanceSample) return;

        readHistory.Add(capturedTimestamp, snapshot.ReadBytesPerSecond);
        writeHistory.Add(capturedTimestamp, snapshot.WriteBytesPerSecond);
    }

    private void ApplySnapshotPresentation(PerformanceSnapshot snapshot)
    {
        _latestMemorySnapshot = snapshot.Memory;
        List<PerformanceDevicePresentation> liveDevices =
            PerformanceDevicePresentationFactory.Create(
                snapshot,
                _historyLengthMinutes,
                _hardwareNameResolver);
        _devices.Clear();
        List<PerformanceDeviceOrderItem> orderItems = new(liveDevices.Count);
        for (int deviceIndex = 0; deviceIndex < liveDevices.Count; deviceIndex++)
        {
            PerformanceDevicePresentation device = liveDevices[deviceIndex];
            if (!_devices.TryAdd(device.DeviceID, device)) continue;

            orderItems.Add(device.OrderItem);
        }

        List<PerformanceDeviceOrderItem> orderedItems = PerformanceDeviceOrdering.Resolve(
            orderItems,
            _settings.PerformanceDevicePriority,
            _settings.PerformanceDeviceOrder);
        List<PerformanceDeviceColumnRow> rows = new(orderedItems.Count);
        for (int orderedIndex = 0; orderedIndex < orderedItems.Count; orderedIndex++)
        {
            PerformanceDeviceOrderItem orderItem = orderedItems[orderedIndex];
            PerformanceDevicePresentation device = _devices[orderItem.ID];
            PerformanceHistory history = _histories[orderItem.ID];
            if (!_deviceCards.TryGetValue(orderItem.ID, out PerformanceDeviceCard? card))
            {
                string deviceID = device.DeviceID;
                card = new PerformanceDeviceCard(
                    device,
                    history,
                    _palette,
                    _resources,
                    sampleTimestamp => FormatDeviceColumnGraphHoverMetric(
                        deviceID,
                        sampleTimestamp));
                _deviceCards.Add(orderItem.ID, card);
            }
            else
            {
                card.Update(device, history);
            }
            card.SetSecondaryHistory(
                device.Kind == PerformanceDeviceKind.CPU
                && _settings.ShowCPUHighestCoreTrace
                    ? _cpuHighestCoreHistory
                    : null);
            rows.Add(new PerformanceDeviceColumnRow(orderItem.ID, card));
        }

        _deviceColumn.ReconcileRows(rows);
        RemoveStaleDeviceState();
        if (_selectedDeviceID == null || !_devices.ContainsKey(_selectedDeviceID))
            _selectedDeviceID = orderedItems.Count == 0 ? null : orderedItems[0].ID;

        UpdateSelectionAndDetails();
        RefreshVisibleCPUGraphs();
    }

    /// <summary>Repaints the active expanded CPU view after its shared histories change.</summary>
    private void RefreshVisibleCPUGraphs()
    {
        if (_cpuLogicalProcessorGrid.IsVisible)
        {
            for (int graphIndex = 0;
                 graphIndex < _cpuLogicalProcessorGraphs.Count;
                 graphIndex++)
            {
                _cpuLogicalProcessorGraphs[graphIndex].Refresh();
            }
        }
        _cpuDetailedView.Refresh();
    }

    private PerformanceHistory GetOrCreateHistory(string deviceID)
    {
        if (_histories.TryGetValue(deviceID, out PerformanceHistory? history)) return history;

        history = CreateHistory();
        _histories.Add(deviceID, history);
        return history;
    }

    private NetworkTransferRateHistories GetOrCreateNetworkTransferRateHistories(
        string deviceID)
    {
        if (_networkTransferRateHistories.TryGetValue(
                deviceID,
                out NetworkTransferRateHistories? histories))
        {
            return histories;
        }

        histories = new NetworkTransferRateHistories(
            CreateMetricHistory(),
            CreateMetricHistory());
        _networkTransferRateHistories.Add(deviceID, histories);
        return histories;
    }

    private DiskTransferRateHistories GetOrCreateDiskTransferRateHistories(string deviceID)
    {
        if (_diskTransferRateHistories.TryGetValue(
                deviceID,
                out DiskTransferRateHistories? histories))
        {
            return histories;
        }

        histories = new DiskTransferRateHistories(
            CreateMetricHistory(),
            CreateMetricHistory());
        _diskTransferRateHistories.Add(deviceID, histories);
        return histories;
    }

    private PerformanceHistory CreateHistory() =>
        new(_historyLengthMinutes, _sampleIntervalMilliseconds);

    private PerformanceMetricHistory CreateMetricHistory() =>
        new(_historyLengthMinutes, _sampleIntervalMilliseconds);

    private string? FormatDetailGraphHoverMetric(long sampleTimestamp)
    {
        if (_selectedDeviceID == null
            || !_devices.TryGetValue(
                _selectedDeviceID,
                out PerformanceDevicePresentation? selectedDevice))
        {
            return null;
        }

        switch (selectedDevice.Kind)
        {
            case PerformanceDeviceKind.CPU:
                if (!_histories.TryGetValue(
                        selectedDevice.DeviceID,
                        out PerformanceHistory? overallHistory)
                    || !overallHistory.TryGetExact(
                        sampleTimestamp,
                        out double overallUtilizationPercent)
                    || !_cpuHighestCoreHistory.TryGetExact(
                        sampleTimestamp,
                        out double highestCPUUtilizationPercent))
                {
                    return null;
                }

                return FormatCPUOverallHoverMetric(
                    highestCPUUtilizationPercent,
                    overallUtilizationPercent);

            case PerformanceDeviceKind.Memory:
                if (!_memoryUsedHistory.TryGetExact(sampleTimestamp, out double usedBytes))
                    return null;
                return PerformanceDevicePresentationFactory.FormatBytes(usedBytes);

            case PerformanceDeviceKind.Network:
                if (!_networkTransferRateHistories.TryGetValue(
                        selectedDevice.DeviceID,
                        out NetworkTransferRateHistories? histories)
                    || !histories.Send.TryGetExact(sampleTimestamp, out double sendBytesPerSecond)
                    || !histories.Receive.TryGetExact(
                        sampleTimestamp,
                        out double receiveBytesPerSecond))
                {
                    return null;
                }

                return FormatNetworkTransferHoverMetric(
                    sendBytesPerSecond,
                    receiveBytesPerSecond);

            case PerformanceDeviceKind.Disk:
                if (!_diskTransferRateHistories.TryGetValue(
                        selectedDevice.DeviceID,
                        out DiskTransferRateHistories? diskHistories)
                    || !diskHistories.Read.TryGetExact(
                        sampleTimestamp,
                        out double readBytesPerSecond)
                    || !diskHistories.Write.TryGetExact(
                        sampleTimestamp,
                        out double writeBytesPerSecond))
                {
                    return null;
                }

                return FormatDiskTransferHoverMetric(
                    readBytesPerSecond,
                    writeBytesPerSecond);

            default:
                return null;
        }
    }

    private string? FormatDeviceColumnGraphHoverMetric(
        string deviceID,
        long sampleTimestamp)
    {
        if (!_devices.TryGetValue(
                deviceID,
                out PerformanceDevicePresentation? device))
        {
            return null;
        }

        switch (device.Kind)
        {
            case PerformanceDeviceKind.Memory:
                if (!_memoryUsedHistory.TryGetExact(sampleTimestamp, out double usedBytes))
                    return null;
                return FormatMemoryDeviceColumnHoverMetric(usedBytes);

            case PerformanceDeviceKind.Network:
                if (!_networkTransferRateHistories.TryGetValue(
                        deviceID,
                        out NetworkTransferRateHistories? histories)
                    || !histories.Send.TryGetExact(sampleTimestamp, out double sendBytesPerSecond)
                    || !histories.Receive.TryGetExact(
                        sampleTimestamp,
                        out double receiveBytesPerSecond))
                {
                    return null;
                }

                return FormatNetworkDeviceColumnHoverMetric(
                    sendBytesPerSecond,
                    receiveBytesPerSecond);

            case PerformanceDeviceKind.Disk:
                if (!_diskTransferRateHistories.TryGetValue(
                        deviceID,
                        out DiskTransferRateHistories? diskHistories)
                    || !diskHistories.Read.TryGetExact(
                        sampleTimestamp,
                        out double readBytesPerSecond)
                    || !diskHistories.Write.TryGetExact(
                        sampleTimestamp,
                        out double writeBytesPerSecond))
                {
                    return null;
                }

                return FormatDiskTransferHoverMetric(
                    readBytesPerSecond,
                    writeBytesPerSecond);

            default:
                return null;
        }
    }

    internal static string FormatNetworkTransferHoverMetric(
        double sendBytesPerSecond,
        double receiveBytesPerSecond) =>
        string.Concat(
            "Send: ",
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(sendBytesPerSecond),
            "\nReceive: ",
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(receiveBytesPerSecond));

    internal static string FormatCPUOverallHoverMetric(
        double highestCPUUtilizationPercent,
        double overallUtilizationPercent) =>
        string.Concat(
            "Highest LP: ",
            PerformanceDevicePresentationFactory.FormatPercent(
                true,
                highestCPUUtilizationPercent),
            "\nOverall util: ",
            PerformanceDevicePresentationFactory.FormatPercent(
                true,
                overallUtilizationPercent));

    internal static string FormatNetworkDeviceColumnHoverMetric(
        double sendBytesPerSecond,
        double receiveBytesPerSecond) =>
        string.Concat(
            "S: ",
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(sendBytesPerSecond),
            "\nR: ",
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(receiveBytesPerSecond));

    internal static string FormatDiskTransferHoverMetric(
        double readBytesPerSecond,
        double writeBytesPerSecond) =>
        string.Concat(
            "R: ",
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(readBytesPerSecond),
            "\nW: ",
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(writeBytesPerSecond));

    internal static string FormatMemoryDeviceColumnHoverMetric(double usedBytes)
    {
        if (!double.IsFinite(usedBytes) || usedBytes < 0) return "Unavailable";

        double usedGigabytes = usedBytes / BytesPerGibibyte;
        return string.Concat(
            usedGigabytes.ToString(
                usedGigabytes >= 100 ? "N0" : "N1",
                CultureInfo.CurrentCulture),
            " G");
    }

    private bool RequiresFullHistoryRebuild(IReadOnlyList<PerformanceSnapshot> snapshots)
    {
        for (int snapshotIndex = 0; snapshotIndex < snapshots.Count; snapshotIndex++)
        {
            PerformanceSnapshot snapshot = snapshots[snapshotIndex];
            if (ContainsUnknownDevice(snapshot)) return true;
        }

        CPUPerformanceSnapshot latestCPU = snapshots[^1].CPU;
        int logicalProcessorCount = GetLogicalProcessorCount(latestCPU);
        return logicalProcessorCount > 0
               && logicalProcessorCount != _cpuLogicalProcessorHistories.Count;
    }

    private bool ContainsUnknownDevice(PerformanceSnapshot snapshot)
    {
        if (!_histories.ContainsKey(snapshot.CPU.DeviceID)
            || !_histories.ContainsKey(snapshot.Memory.DeviceID))
        {
            return true;
        }

        ReadOnlySpan<GPUPerformanceSnapshot> GPUs = snapshot.GPUs.Span;
        for (int GPUIndex = 0; GPUIndex < GPUs.Length; GPUIndex++)
        {
            if (!_histories.ContainsKey(GPUs[GPUIndex].DeviceID)) return true;
        }

        ReadOnlySpan<NetworkPerformanceSnapshot> networks = snapshot.Networks.Span;
        for (int networkIndex = 0; networkIndex < networks.Length; networkIndex++)
        {
            if (!_histories.ContainsKey(networks[networkIndex].DeviceID)) return true;
        }

        ReadOnlySpan<DiskPerformanceSnapshot> disks = snapshot.Disks.Span;
        for (int diskIndex = 0; diskIndex < disks.Length; diskIndex++)
        {
            if (!_histories.ContainsKey(disks[diskIndex].DeviceID)) return true;
        }

        return false;
    }

    private static int GetLogicalProcessorCount(CPUPerformanceSnapshot snapshot) =>
        Math.Max(snapshot.LogicalProcessorCount, snapshot.LogicalProcessorUtilizationPercents.Length);

    /// <summary>Appends every logical-processor trace on the aggregate snapshot timeline.</summary>
    private void AppendCPULogicalProcessorHistories(
        CPUPerformanceSnapshot snapshot,
        long capturedTimestamp)
    {
        ReadOnlySpan<double> processorUtilization = snapshot.LogicalProcessorUtilizationPercents.Span;
        for (int processorIndex = 0;
             processorIndex < _cpuLogicalProcessorHistories.Count;
             processorIndex++)
        {
            PerformanceHistory history = _cpuLogicalProcessorHistories[processorIndex];
            history.AdvanceTo(capturedTimestamp);
            if (snapshot.HasUtilizationSample && processorIndex < processorUtilization.Length)
                history.Add(capturedTimestamp, processorUtilization[processorIndex]);
        }
    }

    /// <summary>Rebuilds the logical-processor graph grid when the CPU topology changes.</summary>
    private void RebuildCPULogicalProcessorGraphs(int processorCount)
    {
        _cpuLogicalProcessorGrid.Children.Clear();
        _cpuLogicalProcessorGrid.ColumnDefinitions.Clear();
        _cpuLogicalProcessorGrid.RowDefinitions.Clear();
        _cpuLogicalProcessorHistories.Clear();
        _cpuLogicalProcessorGraphs.Clear();
        if (processorCount <= 0) return;

        int columnCount = CalculateLogicalProcessorColumnCount(processorCount);
        int rowCount = (processorCount + columnCount - 1) / columnCount;
        double graphSpacing = Math.Max(
            0,
            _resources.AxamlTaskManagerPerformance.LogicalProcessorGraphSpacing);
        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            _cpuLogicalProcessorGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            if (columnIndex < columnCount - 1)
            {
                _cpuLogicalProcessorGrid.ColumnDefinitions.Add(new ColumnDefinition(
                    new GridLength(graphSpacing)));
            }
        }
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            _cpuLogicalProcessorGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            if (rowIndex < rowCount - 1)
            {
                _cpuLogicalProcessorGrid.RowDefinitions.Add(new RowDefinition(
                    new GridLength(graphSpacing)));
            }
        }

        Color accent = PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.CPU);
        for (int processorIndex = 0; processorIndex < processorCount; processorIndex++)
        {
            PerformanceHistory history = CreateHistory();
            PerformanceHistoryGraph graph = new(history, accent, _palette, _resources)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            int graphColumn = processorIndex % columnCount;
            int graphRow = processorIndex / columnCount;
            double trailingHitTestWidth = graphColumn < columnCount - 1 ? graphSpacing : 0;
            double trailingHitTestHeight = graphRow < rowCount - 1 ? graphSpacing : 0;
            PerformanceHistoryGraphHitTarget hitTarget = new(
                graph,
                trailingHitTestWidth,
                trailingHitTestHeight);
            _cpuLogicalProcessorHistories.Add(history);
            _cpuLogicalProcessorGraphs.Add(graph);
            Grid.SetColumn(hitTarget, graphColumn * 2);
            Grid.SetRow(hitTarget, graphRow * 2);
            if (trailingHitTestWidth > 0) Grid.SetColumnSpan(hitTarget, 2);
            if (trailingHitTestHeight > 0) Grid.SetRowSpan(hitTarget, 2);
            _cpuLogicalProcessorGrid.Children.Add(hitTarget);
        }
    }

    /// <summary>Chooses the nearest aspect-aware column count, allowing a partial final row.</summary>
    private int CalculateLogicalProcessorColumnCount(int processorCount)
    {
        double targetColumnCount = Math.Sqrt(
            processorCount * _resources.AxamlTaskManagerPerformance.LogicalProcessorGridAspectRatio);
        int bestColumnCount = 1;
        double bestDistance = double.MaxValue;
        for (int candidateColumnCount = 1;
             candidateColumnCount <= processorCount;
             candidateColumnCount++)
        {
            double distance = Math.Abs(candidateColumnCount - targetColumnCount);
            if (distance > bestDistance
                || (distance == bestDistance && candidateColumnCount <= bestColumnCount))
            {
                continue;
            }

            bestColumnCount = candidateColumnCount;
            bestDistance = distance;
        }

        return bestColumnCount;
    }

    private void RemoveStaleDeviceState()
    {
        _staleDeviceIDs.Clear();
        foreach (string deviceID in _histories.Keys)
        {
            if (!_devices.ContainsKey(deviceID)) _staleDeviceIDs.Add(deviceID);
        }
        for (int staleIndex = 0; staleIndex < _staleDeviceIDs.Count; staleIndex++)
        {
            string deviceID = _staleDeviceIDs[staleIndex];
            _deviceCards.Remove(deviceID);
            _histories.Remove(deviceID);
            _networkTransferRateHistories.Remove(deviceID);
            _diskTransferRateHistories.Remove(deviceID);
        }
    }

    private void OnDeviceSelected(string deviceID)
    {
        if (_disposed || !_devices.ContainsKey(deviceID)) return;
        _selectedDeviceID = deviceID;
        UpdateSelectionAndDetails();
    }

    private void OnGraphSurfacePointerPressed(
        object? sender,
        PointerPressedEventArgs eventArgs)
    {
        if (_disposed
            || !eventArgs.GetCurrentPoint(_graphSurface).Properties.IsRightButtonPressed
            || !IsCPUSelected()
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        PixelPoint screenPosition = _graphSurface.PointToScreen(
            eventArgs.GetPosition(_graphSurface));
        eventArgs.Handled = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && IsCPUSelected())
                ShowCPUGraphContextMenu(owner, screenPosition);
        });
    }

    private bool IsCPUSelected() =>
        _selectedDeviceID != null
        && _devices.TryGetValue(
            _selectedDeviceID,
            out PerformanceDevicePresentation? selectedDevice)
        && selectedDevice.Kind == PerformanceDeviceKind.CPU;

    private void ShowCPUGraphContextMenu(Window owner, PixelPoint screenPosition)
    {
        CloseCPUGraphContextMenu();
        TrayMenuEntryBuilder entries = new();
        entries.Add(new TrayMenuEntry(
            "Logical processors",
            () => SetCPUGraphView(CPUPerformanceGraphView.LogicalProcessors))
        {
            TrailingGlyph = _settings.CPUPerformanceGraphView
                            == CPUPerformanceGraphView.LogicalProcessors
                ? SelectedGraphViewGlyph
                : null
        });
        entries.Add(new TrayMenuEntry(
            "Overall utilization",
            () => SetCPUGraphView(CPUPerformanceGraphView.OverallUtilization))
        {
            TrailingGlyph = _settings.CPUPerformanceGraphView
                            == CPUPerformanceGraphView.OverallUtilization
                ? SelectedGraphViewGlyph
                : null
        });
        entries.Add(new TrayMenuEntry(
            "Detailed view",
            () => SetCPUGraphView(CPUPerformanceGraphView.DetailedView))
        {
            TrailingGlyph = _settings.CPUPerformanceGraphView
                            == CPUPerformanceGraphView.DetailedView
                ? SelectedGraphViewGlyph
                : null
        });

        TaskManagerContextMenuWindow menuWindow = new(
            entries.ToList(),
            _palette,
            _settings.EnableRoundedCorners,
            _settings);
        _cpuGraphContextMenuWindow = menuWindow;
        menuWindow.Closed += OnCPUGraphContextMenuClosed;
        menuWindow.ShowAt(owner, screenPosition);
    }

    private void SetCPUGraphView(CPUPerformanceGraphView graphView)
    {
        if (_disposed) return;

        _settings.UpdateCPUPerformanceGraphView(graphView);
    }

    private void CloseCPUGraphContextMenu()
    {
        TaskManagerContextMenuWindow? menuWindow = _cpuGraphContextMenuWindow;
        if (menuWindow == null) return;

        _cpuGraphContextMenuWindow = null;
        menuWindow.Closed -= OnCPUGraphContextMenuClosed;
        menuWindow.Close();
    }

    private void OnCPUGraphContextMenuClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is TaskManagerContextMenuWindow menuWindow)
            menuWindow.Closed -= OnCPUGraphContextMenuClosed;
        if (ReferenceEquals(sender, _cpuGraphContextMenuWindow))
            _cpuGraphContextMenuWindow = null;
    }

    private void OnDeviceOrderChanged(IReadOnlyList<string> visibleDeviceIDs)
    {
        if (_disposed) return;

        List<string> mergedOrder = PerformanceDeviceOrdering.MergeVisibleOrder(
            visibleDeviceIDs,
            _settings.PerformanceDeviceOrder);
        _settings.UpdatePerformanceDeviceOrder(mergedOrder);
    }

    private void UpdateSelectionAndDetails()
    {
        foreach (KeyValuePair<string, PerformanceDeviceCard> pair in _deviceCards)
            pair.Value.SetSelected(string.Equals(pair.Key, _selectedDeviceID, StringComparison.Ordinal));

        if (_selectedDeviceID == null
            || !_devices.TryGetValue(
                _selectedDeviceID,
                out PerformanceDevicePresentation? selectedDevice))
        {
            _detailTitle.Text = "No devices";
            _detailHardwareName.Text = string.Empty;
            _detailGraphLabel.Text = string.Empty;
            _detailGraph.SetSecondaryHistory(null);
            _detailGraph.IsVisible = false;
            _cpuLogicalProcessorGrid.IsVisible = false;
            _cpuDetailedView.IsVisible = false;
            _gpuPerformanceDetailsView.Hide();
            _genericGraphHeader.IsVisible = true;
            _genericGraphFooter.IsVisible = true;
            _memoryCompositionView.IsVisible = false;
            _memoryModuleDetailsPanel.IsVisible = false;
            _diskPerformanceDetailsView.Hide();
            SetStatistics(null, ReadOnlySpan<PerformanceStatistic>.Empty);
            return;
        }

        _detailTitle.Text = selectedDevice.Title;
        _detailHardwareName.Text = selectedDevice.HardwareName;
        _detailGraphLabel.Text = selectedDevice.GraphLabel;
        _detailGraph.SetAccent(selectedDevice.Accent);
        _detailGraph.SetHistory(_histories[selectedDevice.DeviceID]);
        UpdateDetailGraphSecondaryHistory();
        bool showLogicalProcessors = selectedDevice.Kind == PerformanceDeviceKind.CPU
                                     && _settings.CPUPerformanceGraphView
                                     == CPUPerformanceGraphView.LogicalProcessors
                                     && _cpuLogicalProcessorHistories.Count > 0;
        bool showDetailedCPU = selectedDevice.Kind == PerformanceDeviceKind.CPU
                               && _settings.CPUPerformanceGraphView
                               == CPUPerformanceGraphView.DetailedView;
        bool showGPUDetails = selectedDevice.Kind == PerformanceDeviceKind.GPU;
        _detailGraph.IsVisible = !showLogicalProcessors && !showDetailedCPU && !showGPUDetails;
        _cpuLogicalProcessorGrid.IsVisible = showLogicalProcessors;
        _cpuDetailedView.IsVisible = showDetailedCPU;
        _genericGraphHeader.IsVisible = !showGPUDetails;
        _genericGraphFooter.IsVisible = !showGPUDetails;
        if (showGPUDetails)
        {
            if (!_gpuPerformanceDetailsView.IsShowing(
                    selectedDevice.DeviceID,
                    _historyLengthMinutes,
                    _sampleIntervalMilliseconds))
            {
                _gpuPerformanceDetailsView.Show(
                    selectedDevice.DeviceID,
                    _historyLengthMinutes,
                    _sampleIntervalMilliseconds,
                    _snapshotService.GetSnapshotHistory());
            }
        }
        else
        {
            _gpuPerformanceDetailsView.Hide();
        }
        bool showMemoryDetails = selectedDevice.Kind == PerformanceDeviceKind.Memory;
        _memoryCompositionView.IsVisible = showMemoryDetails;
        if (showMemoryDetails)
        {
            _memoryCompositionView.Update(_latestMemorySnapshot);
            _memoryModuleDetailsPanel.Update(
                _latestMemorySnapshot.Hardware.Modules,
                _settings.ShowMemoryModuleSerialNumbers);
        }
        else
        {
            _memoryModuleDetailsPanel.IsVisible = false;
        }
        bool showDiskDetails = selectedDevice.Kind == PerformanceDeviceKind.Disk;
        if (showDiskDetails)
        {
            if (!_diskPerformanceDetailsView.IsShowing(
                    selectedDevice.DeviceID,
                    _historyLengthMinutes,
                    _sampleIntervalMilliseconds))
            {
                _diskPerformanceDetailsView.Show(
                    selectedDevice.DeviceID,
                    _historyLengthMinutes,
                    _sampleIntervalMilliseconds,
                    _snapshotService.GetSnapshotHistory());
            }
        }
        else
        {
            _diskPerformanceDetailsView.Hide();
        }
        SetStatistics(selectedDevice.Kind, selectedDevice.Statistics.Span);
    }

    /// <summary>Shows the highest-core overlay only on the aggregate CPU graph.</summary>
    private void UpdateDetailGraphSecondaryHistory()
    {
        bool showHighestCoreTrace = _settings.ShowCPUHighestCoreTrace
                                    && _selectedDeviceID != null
                                    && _devices.TryGetValue(
                                        _selectedDeviceID,
                                        out PerformanceDevicePresentation? selectedDevice)
                                    && selectedDevice.Kind == PerformanceDeviceKind.CPU;
        _detailGraph.SetSecondaryHistory(
            showHighestCoreTrace ? _cpuHighestCoreHistory : null);
    }

    /// <summary>Applies the CPU overlay toggle to every reachable aggregate graph.</summary>
    private void UpdateCPUHighestCoreTraceVisibility()
    {
        UpdateDetailGraphSecondaryHistory();
        if (!_deviceCards.TryGetValue(
                CPUPerformanceSnapshot.StableDeviceID,
                out PerformanceDeviceCard? CPUCard))
        {
            return;
        }

        CPUCard.SetSecondaryHistory(
            _settings.ShowCPUHighestCoreTrace ? _cpuHighestCoreHistory : null);
    }

    private void SetStatistics(
        PerformanceDeviceKind? deviceKind,
        ReadOnlySpan<PerformanceStatistic> statistics)
    {
        int visibleCount = Math.Min(statistics.Length, MaximumDetailStatistics);
        int primaryStatisticCount = GetPrimaryStatisticCount(deviceKind, visibleCount);
        ConfigureStatisticsLayout(deviceKind, visibleCount, primaryStatisticCount);
        for (int statisticIndex = 0; statisticIndex < MaximumDetailStatistics; statisticIndex++)
        {
            TextBlock label = _statisticLabels[statisticIndex];
            TextBlock value = _statisticValues[statisticIndex];
            bool isVisible = statisticIndex < visibleCount;
            if (!isVisible) continue;

            label.Text = statistics[statisticIndex].Label;
            value.Text = statistics[statisticIndex].Value;
        }
    }

    /// <summary>Splits prominent values from compact metadata without rebuilding it every sample.</summary>
    private void ConfigureStatisticsLayout(
        PerformanceDeviceKind? deviceKind,
        int visibleCount,
        int primaryStatisticCount)
    {
        if (_configuredStatisticCount == visibleCount
            && _configuredPrimaryStatisticCount == primaryStatisticCount
            && _configuredStatisticDeviceKind == deviceKind)
        {
            return;
        }

        _configuredStatisticCount = visibleCount;
        _configuredPrimaryStatisticCount = primaryStatisticCount;
        _configuredStatisticDeviceKind = deviceKind;
        _primaryStatistics.Children.Clear();
        _metadataStatistics.Children.Clear();
        for (int statisticIndex = 0; statisticIndex < MaximumDetailStatistics; statisticIndex++)
        {
            StackPanel statistic = _statisticContainers[statisticIndex];
            TextBlock label = _statisticLabels[statisticIndex];
            TextBlock value = _statisticValues[statisticIndex];
            bool isVisible = statisticIndex < visibleCount;
            statistic.IsVisible = isVisible;
            if (!isVisible) continue;

            bool isPrimary = statisticIndex < primaryStatisticCount;
            statistic.Orientation = isPrimary ? Orientation.Vertical : Orientation.Horizontal;
            statistic.Width = isPrimary
                ? deviceKind == PerformanceDeviceKind.Memory
                    ? _resources.AxamlTaskManagerPerformance.MemoryPrimaryStatisticWidth
                    : _resources.AxamlTaskManagerPerformance.DetailPrimaryStatisticWidth
                : double.NaN;
            statistic.Margin = isPrimary
                ? _resources.AxamlTaskManagerPerformance.DetailPrimaryStatisticMargin
                : _resources.AxamlTaskManagerPerformance.DetailMetadataStatisticMargin;
            label.Width = isPrimary
                ? double.NaN
                : _resources.AxamlTaskManagerPerformance.DetailMetadataStatisticLabelWidth;
            value.FontSize = isPrimary
                ? _resources.AxamlTaskManagerPerformance.DetailPrimaryStatisticValueFontSize
                : _resources.AxamlTaskManagerPerformance.DetailStatisticValueFontSize;
            if (isPrimary)
                _primaryStatistics.Children.Add(statistic);
            else
                _metadataStatistics.Children.Add(statistic);
        }
    }

    /// <summary>Returns the number of leading statistics emphasized for one device category.</summary>
    private static int GetPrimaryStatisticCount(
        PerformanceDeviceKind? deviceKind,
        int visibleCount)
    {
        int requestedCount = deviceKind switch
        {
            PerformanceDeviceKind.CPU => 8,
            PerformanceDeviceKind.Memory => 6,
            PerformanceDeviceKind.GPU => 5,
            PerformanceDeviceKind.Network => 2,
            PerformanceDeviceKind.Disk => 4,
            _ => 0
        };
        return Math.Min(requestedCount, visibleCount);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        CloseCPUGraphContextMenu();
        _graphSurface.PointerPressed -= OnGraphSurfacePointerPressed;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _snapshotService.SnapshotUpdated -= OnSnapshotUpdated;
        _deviceColumn.Dispose();
        _devices.Clear();
        _deviceCards.Clear();
        _histories.Clear();
        _networkTransferRateHistories.Clear();
        _diskTransferRateHistories.Clear();
        _cpuHighestCoreHistory.Clear();
        _memoryUsedHistory.Clear();
        _cpuLogicalProcessorGrid.Children.Clear();
        _cpuLogicalProcessorGraphs.Clear();
        _cpuLogicalProcessorHistories.Clear();
        _cpuDetailedView.Clear();
    }

    private sealed record NetworkTransferRateHistories(
        PerformanceMetricHistory Send,
        PerformanceMetricHistory Receive);

    private sealed record DiskTransferRateHistories(
        PerformanceMetricHistory Read,
        PerformanceMetricHistory Write);

    private sealed class PerformanceDeviceCard : Border
    {
        private readonly SettingsPalette _palette;
        private readonly Border _surface;
        private readonly TextBlock _title;
        private readonly TextBlock _subtitle;
        private readonly TextBlock _summary;
        private readonly PerformanceHistoryGraph _graph;
        private Color _accent;
        private IBrush _accentBrush;
        private bool _isPointerOver;
        private bool _isSelected;

        public PerformanceDeviceCard(
            PerformanceDevicePresentation device,
            PerformanceHistory history,
            SettingsPalette palette,
            TaskManagerWindowResources resources,
            Func<long, string?> hoverMetricProvider)
        {
            _palette = palette;
            _accent = device.Accent;
            _accentBrush = new SolidColorBrush(device.Accent);
            double trailingHitTestHeight = Math.Max(
                0,
                resources.AxamlTaskManagerPerformance.DeviceColumnSpacing);
            Height = resources.AxamlTaskManagerPerformance.DeviceCardHeight + trailingHitTestHeight;
            Background = Brushes.Transparent;
            Cursor = TrayAppDotNETCursors.Hand;

            _graph = new PerformanceHistoryGraph(
                history,
                device.Accent,
                palette,
                resources,
                hoverMetricProvider)
            {
                Width = resources.AxamlTaskManagerPerformance.DeviceGraphWidth,
                Height = resources.AxamlTaskManagerPerformance.DeviceGraphHeight,
                Margin = resources.AxamlTaskManagerPerformance.DeviceGraphMargin,
                VerticalAlignment = VerticalAlignment.Center
            };
            _title = TrayAppDotNETSettingsUI.Text(
                device.Title,
                palette,
                resources.AxamlTaskManagerPerformance.DeviceTitleFontSize,
                FontWeight.Normal);
            _title.TextTrimming = TextTrimming.CharacterEllipsis;
            _subtitle = TrayAppDotNETSettingsUI.Text(
                device.Subtitle,
                palette,
                resources.AxamlTaskManagerPerformance.DeviceSubtitleFontSize,
                FontWeight.Normal);
            _subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            _subtitle.IsVisible = ShouldShowSubtitle(device);
            _summary = TrayAppDotNETSettingsUI.Text(
                device.Summary,
                palette,
                resources.AxamlTaskManagerPerformance.DeviceSummaryFontSize,
                FontWeight.Normal);
            _summary.TextTrimming = TextTrimming.CharacterEllipsis;
            _summary.IsVisible = !string.IsNullOrWhiteSpace(device.Summary);

            StackPanel labels = new()
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _title, _subtitle, _summary }
            };
            Grid content = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                }
            };
            content.Children.Add(_graph);
            Grid.SetColumn(labels, 1);
            content.Children.Add(labels);
            _surface = new Border
            {
                Margin = new Thickness(0, 0, 0, trailingHitTestHeight),
                Padding = resources.AxamlTaskManagerPerformance.DeviceCardPadding,
                CornerRadius = resources.AxamlTaskManagerPerformance.DeviceCardCornerRadius,
                BorderThickness = resources.AxamlTaskManagerPerformance.DeviceCardBorderThickness,
                ClipToBounds = true,
                Child = content
            };
            Child = _surface;

            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
            TrayAppDotNETToolTip.SetTip(this, "Select or drag to reorder. Use Ctrl+Up/Down from the keyboard.");
            UpdateSurface();
        }

        public void Update(
            PerformanceDevicePresentation device,
            PerformanceHistory history)
        {
            if (_accent != device.Accent)
            {
                _accent = device.Accent;
                _accentBrush = new SolidColorBrush(device.Accent);
            }
            _title.Text = device.Title;
            _subtitle.Text = device.Subtitle;
            _subtitle.IsVisible = ShouldShowSubtitle(device);
            _summary.Text = device.Summary;
            _summary.IsVisible = !string.IsNullOrWhiteSpace(device.Summary);
            _graph.SetAccent(device.Accent);
            _graph.SetHistory(history);
            UpdateSurface();
        }

        /// <summary>Forwards an optional aggregate overlay to the card graph.</summary>
        public void SetSecondaryHistory(PerformanceHistory? history) =>
            _graph.SetSecondaryHistory(history);

        public void SetSelected(bool isSelected)
        {
            if (_isSelected == isSelected) return;
            _isSelected = isSelected;
            UpdateSurface();
        }

        private static bool ShouldShowSubtitle(PerformanceDevicePresentation device) =>
            device.Kind is not PerformanceDeviceKind.CPU and not PerformanceDeviceKind.Memory
            && !string.IsNullOrWhiteSpace(device.Subtitle);

        private void OnPointerEntered(object? sender, PointerEventArgs eventArgs)
        {
            _isPointerOver = true;
            UpdateSurface();
        }

        private void OnPointerExited(object? sender, PointerEventArgs eventArgs)
        {
            _isPointerOver = false;
            UpdateSurface();
        }

        private void UpdateSurface()
        {
            _surface.Background = TrayAppDotNETSettingsUI.Brush(
                _isSelected
                    ? _palette.SearchListItemSelected
                    : _isPointerOver
                        ? _palette.HoverDeep
                        : _palette.Background);
            _surface.BorderBrush = _isSelected
                ? _accentBrush
                : TrayAppDotNETSettingsUI.Brush(_palette.Border);
        }
    }

    /// <summary>Owns the visual graph plus the invisible spacing after it.</summary>
    private sealed class PerformanceHistoryGraphHitTarget : Border
    {
        private readonly PerformanceHistoryGraph _graph;

        public PerformanceHistoryGraphHitTarget(
            PerformanceHistoryGraph graph,
            double trailingHitTestWidth,
            double trailingHitTestHeight)
        {
            _graph = graph;
            _graph.IsHitTestVisible = false;
            Background = Brushes.Transparent;
            Padding = new Thickness(0, 0, trailingHitTestWidth, trailingHitTestHeight);
            Child = graph;
        }

        protected override void OnPointerEntered(PointerEventArgs eventArgs)
        {
            base.OnPointerEntered(eventArgs);
            _graph.TrackPointer(eventArgs.GetPosition(_graph));
        }

        protected override void OnPointerMoved(PointerEventArgs eventArgs)
        {
            base.OnPointerMoved(eventArgs);
            _graph.TrackPointer(eventArgs.GetPosition(_graph));
        }

        protected override void OnPointerExited(PointerEventArgs eventArgs)
        {
            base.OnPointerExited(eventArgs);
            _graph.ClearPointer();
        }
    }
}
