using static TaskManagerTrayAppDotNET.UI.Performance.PerformanceText;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.Visuals;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>Displays direct-OS CPU, memory, GPU, network, and disk performance snapshots.</summary>
internal sealed class PerformancePage : TaskManagerPageLayout, IDisposable
{
    private const int MaximumDetailStatistics = 20;
    private const double BytesPerGibibyte = 1_073_741_824;

    private readonly AppSettings _settings;
    private readonly SettingsPalette _palette;
    private readonly TaskManagerWindowResources _resources;
    private readonly PerformanceSnapshotService _snapshotService;
    private readonly PerformanceDeviceColumn _deviceColumn;
    private readonly SettingsVerticalScrollViewport _deviceScrollViewport;

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
    private readonly Grid _detailHeader;
    private readonly Grid _graphArea;
    private readonly PerformanceStatisticsPanel _statisticsPanel;
    private readonly StackPanel _memoryErrorPanel;
    private readonly Border _memoryErrorLabelFrame;
    private readonly TextBlock _memoryErrorLabel;
    private readonly InkGlyphButton _memoryErrorInfoButton;
    private readonly InkGlyphButton _memoryErrorResetButton;
    private readonly IBrush _memoryErrorTint;
    private readonly MemoryErrorLogMonitor _memoryErrorLogMonitor = new();
    private MemoryErrorLogWindow? _memoryErrorLogWindow;
    private readonly TextBlock _detailGraphLabel;
    private readonly TextBlock _graphWindowLabel;
    private readonly Grid _genericGraphHeader;
    private readonly Grid _genericGraphFooter;
    private readonly Grid _graphSurface;
    private readonly PerformanceHistoryGraph _detailGraph;
    private readonly PageFileHistoryView _pageFileView;
    private readonly RowDefinition _detailGraphRow = new(GridLength.Star);
    private readonly RowDefinition _pageFileGraphRow = new(new GridLength(0));
    private double _pageFileMinimumHeight;
    private readonly MemoryCompositionView _memoryCompositionView;
    private readonly MemoryModuleDetailsPanel _memoryModuleDetailsPanel;
    private readonly DiskPerformanceDetailsView _diskPerformanceDetailsView;
    private readonly GPUPerformanceDetailsView _gpuPerformanceDetailsView;
    private readonly Grid _cpuLogicalProcessorGrid;
    private readonly CPUPerformanceDetailedView _cpuDetailedView;
    private readonly List<PerformanceHistory> _cpuLogicalProcessorHistories = [];
    private readonly List<PerformanceHistoryGraph> _cpuLogicalProcessorGraphs = [];
    private readonly Grid _metadataStatistics;
    private readonly StackPanel[] _statisticContainers = new StackPanel[MaximumDetailStatistics];
    private readonly TextBlock[] _statisticLabels = new TextBlock[MaximumDetailStatistics];
    private readonly TextBlock[] _statisticValues = new TextBlock[MaximumDetailStatistics];
#if DEBUG
    private readonly List<SettingsButton> _cpuArchitectureSimulationButtons = [];
    private CPUArchitectureSimulation? _cpuArchitectureSimulation;
#endif
    private PerformanceHardwareNameResolver _hardwareNameResolver;
    private MemoryPerformanceSnapshot _latestMemorySnapshot = MemoryPerformanceSnapshot.Empty;
    private PerformanceHistory _cpuHighestCoreHistory;
    private PerformanceMetricHistory _memoryUsedHistory;
    private TaskManagerContextMenuWindow? _graphContextMenuWindow;

    // Device instance IDs whose power limit is being read, and the last failure per device
    private readonly HashSet<string> _gpuPowerLimitReads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _gpuPowerLimitErrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private string? _selectedDeviceID;
    private int _historyLengthMinutes;
    private int _sampleIntervalMilliseconds;
    private long _lastProcessedTimestamp;
    private int _configuredStatisticCount = -1;
    private int _configuredPrimaryStatisticCount = -1;
    private PerformanceDeviceKind? _configuredStatisticDeviceKind;
    private bool _configuredStatisticCompact;

    // Compact mode shrinks the title and the statistics when full size would leave the graphs less than their
    // minimum height
    private bool _isCompact;
    private bool _compactEvaluationPending;
    private bool _hasProcessedSnapshot;
    private bool _isPageActive;
    private bool _disposed;

    public PerformancePage(
        AppSettings settings,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        bool isLightTheme,
        PerformanceSnapshotService snapshotService,
        string? initialSelectedDeviceID)
        : base(title: L(nameof(AppStrings.Page_Performance)), palette, resources, isLightTheme)
    {
        _settings = settings;
        _palette = palette;
        _resources = resources;
        _snapshotService = snapshotService;
        _selectedDeviceID = initialSelectedDeviceID;
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
        _deviceScrollViewport = new SettingsVerticalScrollViewport(
            _deviceColumn,
            resources.AxamlTaskManagerPerformance.DeviceColumnPadding,
            resources.GridBackgroundColor(isLightTheme),
            TaskManagerScrollBarStyles.CreateProcessGrid(resources, isLightTheme),
            TaskManagerContextMenuWindow.CreateOptions(
                palette,
                settings.EnableRoundedCorners,
                settings),
            node: ControlMap.Main.PerformancePage.DeviceList.ID);
        Border deviceColumnFrame = new()
        {
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = resources.AxamlTaskManagerPerformance.DeviceColumnBorderThickness,
            Child = _deviceScrollViewport
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
        SetColumn(details, value: 1);
        MainContent.Children.Add(details);

        _detailTitle = TrayAppDotNETSettingsUI.Text(
            text: L(nameof(AppStrings.Page_Performance)),
            palette,
            resources.AxamlTaskManagerPerformance.DetailTitleFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        _detailTitle.VerticalAlignment = VerticalAlignment.Center;
        _detailHardwareName = TrayAppDotNETSettingsUI.Text(
            string.Empty,
            palette,
            resources.AxamlTaskManagerPerformance.DetailDeviceNameFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        _detailHardwareName.HorizontalAlignment = HorizontalAlignment.Right;
        _detailHardwareName.VerticalAlignment = VerticalAlignment.Center;
        _detailHardwareName.TextTrimming = TextTrimming.CharacterEllipsis;
        _detailHeader = new Grid
        {
            Margin = resources.AxamlTaskManagerPerformance.DetailHeaderMargin,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
        };
        _detailHeader.Children.Add(_detailTitle);
        SetColumn(_detailHardwareName, value: 1);
        _detailHeader.Children.Add(_detailHardwareName);

        // Memory shows its error count where other devices show their hardware name
        _memoryErrorTint = TrayAppDotNETSettingsUI.Brush(resources.AxamlTaskManagerDetails.SearchInvalidTintColor);
        _memoryErrorLabel = TrayAppDotNETSettingsUI.Text(
            string.Empty,
            palette,
            resources.AxamlTaskManagerPerformance.DetailDeviceNameFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        _memoryErrorLabel.VerticalAlignment = VerticalAlignment.Center;
        // Unpadded while the count is zero, so the label sits exactly where other devices show their hardware name
        _memoryErrorLabelFrame = new Border
        {
            CornerRadius = resources.AxamlTaskManagerPerformance.MemoryErrorLabelCornerRadius,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = resources.AxamlTaskManagerPerformance.MemoryErrorLabelMargin,
            Child = _memoryErrorLabel
        };
        _memoryErrorInfoButton = CreateMemoryErrorButton(TaskManagerGlyphCatalog.INFO, palette, resources);
        _memoryErrorInfoButton.MapTo(ControlMap.Main.PerformancePage.Details.MemoryErrors.ShowLog);
        _memoryErrorInfoButton.Click += OnMemoryErrorInfoClick;
        TrayAppDotNETToolTip.SetTip(_memoryErrorInfoButton, L(nameof(AppStrings.Perf_MemoryErrors_ShowLogTip)));
        _memoryErrorResetButton = CreateMemoryErrorButton(TaskManagerGlyphCatalog.RESET, palette, resources);
        _memoryErrorResetButton.MapTo(ControlMap.Main.PerformancePage.Details.MemoryErrors.Reset);
        _memoryErrorResetButton.Click += OnMemoryErrorResetClick;
        TrayAppDotNETToolTip.SetTip(_memoryErrorResetButton, L(nameof(AppStrings.Perf_MemoryErrors_ResetTip)));

        // The buttons center their glyphs' outlines, but text draws its ink off the center of its line box. The
        // label keeps the hardware name's layout position, so the buttons move to the label's ink center
        double labelInkOffset = InkCenterOffset(
            text: "M",
            _memoryErrorLabel.FontFamily,
            _memoryErrorLabel.FontSize,
            _memoryErrorLabel.FontWeight);
        _memoryErrorInfoButton.RenderTransform = new TranslateTransform(x: 0, labelInkOffset);
        _memoryErrorResetButton.RenderTransform = new TranslateTransform(x: 0, labelInkOffset);
        // The buttons touch, so the pointer never falls in a gap between their hit targets
        _memoryErrorPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Children = { _memoryErrorInfoButton, _memoryErrorResetButton, _memoryErrorLabelFrame }
        }.MapTo(ControlMap.Main.PerformancePage.Details.MemoryErrors.ID);
        SetColumn(_memoryErrorPanel, value: 1);
        _detailHeader.Children.Add(_memoryErrorPanel);
        details.Children.Add(_detailHeader);

        _detailGraphLabel = TrayAppDotNETSettingsUI.Text(
            F(
                nameof(AppStrings.Perf_Graph_UtilizationOverFormat),
                PerformanceDevicePresentationFactory.FormatHistoryWindow(_historyLengthMinutes)),
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        TextBlock graphMaximumLabel = TrayAppDotNETSettingsUI.Text(
            text: "100%",
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        graphMaximumLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _genericGraphHeader = new Grid
        {
            Margin = resources.AxamlTaskManagerPerformance.DetailGraphLabelMargin,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        _genericGraphHeader.Children.Add(_detailGraphLabel);
        SetColumn(graphMaximumLabel, value: 1);
        _genericGraphHeader.Children.Add(graphMaximumLabel);
        PerformanceHistory initialHistory = CreateHistory();
        _detailGraph = new PerformanceHistoryGraph(
            initialHistory,
            PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.CPU),
            palette,
            resources,
            isLightTheme,
            FormatDetailGraphHoverMetric)
        {
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
        _cpuDetailedView = new CPUPerformanceDetailedView(palette, resources, isLightTheme);
        _gpuPerformanceDetailsView = new GPUPerformanceDetailsView(
            palette,
            resources,
            isLightTheme,
            _historyLengthMinutes,
            _sampleIntervalMilliseconds);
        // No minimum height: a short window shrinks the graphs instead of letting them spill over the title and the
        // statistics. Compact mode gives them height back first (see EvaluateCompactMode)
        _graphSurface = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children = { _detailGraph, _cpuLogicalProcessorGrid, _cpuDetailedView, _gpuPerformanceDetailsView }
        };
        _graphSurface.PointerPressed += OnGraphSurfacePointerPressed;
        _graphArea = new Grid
        {
            Margin = resources.AxamlTaskManagerPerformance.DetailGraphMargin,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                _detailGraphRow,
                new RowDefinition(GridLength.Auto),
                _pageFileGraphRow
            }
        };
        _graphArea.Children.Add(_genericGraphHeader);
        SetRow(_graphSurface, value: 1);
        _graphArea.Children.Add(_graphSurface);
        _graphWindowLabel = TrayAppDotNETSettingsUI.Text(
            PerformanceDevicePresentationFactory.FormatHistoryWindow(_historyLengthMinutes),
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        TextBlock graphMinimumLabel = TrayAppDotNETSettingsUI.Text(
            text: "0",
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        graphMinimumLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _genericGraphFooter = new Grid
        {
            Margin = resources.AxamlTaskManagerPerformance.DetailGraphScaleMargin,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        _genericGraphFooter.Children.Add(_graphWindowLabel);
        SetColumn(graphMinimumLabel, value: 1);
        _genericGraphFooter.Children.Add(graphMinimumLabel);
        SetRow(_genericGraphFooter, value: 2);
        _graphArea.Children.Add(_genericGraphFooter);
        _pageFileView = new PageFileHistoryView(
            palette,
            resources,
            isLightTheme,
            _historyLengthMinutes,
            _sampleIntervalMilliseconds);
        SetRow(_pageFileView, value: 3);
        _graphArea.Children.Add(_pageFileView);
        _pageFileView.MinimumHeightChanged += OnPageFileMinimumHeightChanged;
        SetRow(_graphArea, value: 1);
        details.Children.Add(_graphArea);
        _graphArea.SizeChanged += OnCompactLayoutInputSizeChanged;

        _memoryCompositionView = new MemoryCompositionView(palette, resources);
        SetRow(_memoryCompositionView, value: 2);
        details.Children.Add(_memoryCompositionView);

        _diskPerformanceDetailsView = new DiskPerformanceDetailsView(
            palette,
            resources,
            isLightTheme,
            _historyLengthMinutes,
            _sampleIntervalMilliseconds);
        SetRow(_diskPerformanceDetailsView, value: 2);
        details.Children.Add(_diskPerformanceDetailsView);

        // Metadata labels share one auto-sized column, so the longest label sets where every value starts
        _metadataStatistics = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            ColumnSpacing = resources.AxamlTaskManagerPerformance.DetailMetadataStatisticColumnSpacing,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
        };
        for (int statisticIndex = 0; statisticIndex < MaximumDetailStatistics; statisticIndex++)
        {
            TextBlock label = TrayAppDotNETSettingsUI.Text(
                string.Empty,
                palette,
                resources.AxamlTaskManagerPerformance.DetailStatisticLabelFontSize,
                (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
            TextBlock value = TrayAppDotNETSettingsUI.Text(
                string.Empty,
                palette,
                statisticIndex < 2
                    ? resources.AxamlTaskManagerPerformance.DetailPrimaryStatisticValueFontSize
                    : resources.AxamlTaskManagerPerformance.DetailStatisticValueFontSize,
                (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
            StackPanel statistic = new() { IsVisible = false, Children = { label, value } };
            _statisticContainers[statisticIndex] = statistic;
            _statisticLabels[statisticIndex] = label;
            _statisticValues[statisticIndex] = value;
        }

        _statisticsPanel = new PerformanceStatisticsPanel(
            _metadataStatistics,
            resources.AxamlTaskManagerPerformance.DetailStatisticsColumnSpacing,
            resources.AxamlTaskManagerPerformance.DetailStatisticsRowSpacing,
            resources.AxamlTaskManagerPerformance.DetailPrimaryStatisticWidthStep);
        _statisticsPanel.SizeChanged += OnCompactLayoutInputSizeChanged;
        SetRow(_statisticsPanel, value: 3);
        details.Children.Add(_statisticsPanel);

        _memoryModuleDetailsPanel = new MemoryModuleDetailsPanel(palette, resources);
        SetRow(_memoryModuleDetailsPanel, value: 4);
        details.Children.Add(_memoryModuleDetailsPanel);

        UpdateGraphUnderfillVisibility();
        UpdateGraphHoverSnapping();

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

        _memoryErrorLogMonitor.Changed += UpdateMemoryErrorDisplay;
        UpdateMemoryErrorDisplay();
        _memoryErrorLogMonitor.Start();
#if DEBUG
        TaskManagerContextMenuResources.ResourcesReloaded += OnContextMenuAXAMLResourcesReloaded;
        HeaderActions.MapTo(ControlMap.Main.PerformancePage.ArchitectureSimulations.ID);
        AddCPUArchitectureSimulationButton(
            text: "Live CPU",
            simulation: null,
            tip: "Detailed view from this machine's Windows topology");
        foreach (CPUArchitectureSimulation simulation in CPUArchitectureSimulation.All)
        {
            AddCPUArchitectureSimulationButton(
                simulation.ButtonText,
                simulation,
                string.Concat(str0: "Detailed view as Windows would report ", simulation.ProcessorName));
        }

        UpdateCPUArchitectureSimulationButtons();
#endif
    }

    /// <summary>Gets the device selection to restore if the page is reconstructed.</summary>
    internal string? SelectedDeviceID => _selectedDeviceID;

#if DEBUG
    /// <summary>Refreshes the device-list scrollbar menu after its standalone AXAML reloads.</summary>
    private void OnContextMenuAXAMLResourcesReloaded()
    {
        if (_disposed) return;

        _deviceScrollViewport.SetContextMenuOptions(TaskManagerContextMenuWindow.CreateOptions(
            _palette,
            _settings.EnableRoundedCorners,
            _settings));
    }

    /// <summary>Adds a header button that previews the CPU Detailed view for a simulated processor.</summary>
    private void AddCPUArchitectureSimulationButton(
        string text,
        CPUArchitectureSimulation? simulation,
        string tip)
    {
        SettingsButton button = TrayAppDotNETSettingsUI.Button(text, _palette)
            .MapTo(ControlMap.Main.PerformancePage.ArchitectureSimulations.Simulation);
        button.Tag = simulation;
        button.BorderThickness = _resources.AxamlTaskManagerPerformance.SimulationButtonBorderThickness;
        button.Click += OnCPUArchitectureSimulationClick;
        TrayAppDotNETToolTip.SetTip(button, tip);
        HeaderActions.Children.Add(button);
        _cpuArchitectureSimulationButtons.Add(button);
    }

    /// <summary>Shows the CPU Detailed view built from the clicked simulation, or from live topology.</summary>
    private void OnCPUArchitectureSimulationClick(object? sender, EventArgs eventArgs)
    {
        if (_disposed || sender is not SettingsButton button) return;

        _cpuArchitectureSimulation = button.Tag as CPUArchitectureSimulation;
        UpdateCPUArchitectureSimulationButtons();
        _selectedDeviceID = CPUPerformanceSnapshot.StableDeviceID;
        _settings.UpdateCPUPerformanceGraphView(CPUPerformanceGraphView.DetailedView);
        RebuildHistoriesFromSnapshotArchive();
    }

    /// <summary>Outlines the header button whose topology the Detailed view currently shows.</summary>
    private void UpdateCPUArchitectureSimulationButtons()
    {
        IBrush activeBrush = new SolidColorBrush(
            PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.CPU));
        for (int buttonIndex = 0; buttonIndex < _cpuArchitectureSimulationButtons.Count; buttonIndex++)
        {
            SettingsButton button = _cpuArchitectureSimulationButtons[buttonIndex];
            button.BorderBrush = ReferenceEquals(button.Tag, _cpuArchitectureSimulation)
                ? activeBrush
                : Brushes.Transparent;
        }
    }
#endif

    internal override void SetPageActive(bool isActive)
    {
        if (_disposed || _isPageActive == isActive) return;

        _isPageActive = isActive;
        // The CPU process, thread, and handle counts cost a process walk per sample, so it only runs while shown
        _snapshotService.SetProcessTotalsRequested(isActive);
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

        if (eventArgs.PropertyName == nameof(AppSettings.GPUPowerLimits))
        {
            ApplySnapshotPresentation(_snapshotService.GetLatestSnapshot());
            UpdateGPUPowerDisplay();
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

        if (eventArgs.PropertyName == nameof(AppSettings.ShowPerformanceGraphUnderfill))
        {
            UpdateGraphUnderfillVisibility();
            return;
        }

        if (eventArgs.PropertyName == nameof(AppSettings.MovePerformanceGraphHoverFreely))
        {
            UpdateGraphHoverSnapping();
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
            return;

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
                return;

            pendingSnapshots = [wakeSnapshot];
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
            latestSnapshot = archivedSnapshots[^1];

        int logicalProcessorCount = GetLogicalProcessorCount(latestSnapshot.CPU);
        CPUCCDTopology CCDTopology = latestSnapshot.CPU.CCDTopology;
        CPUCoreClassTopology coreClassTopology = latestSnapshot.CPU.CoreClassTopology;
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

        for (int snapshotIndex = archivedSnapshots.Count - 1;
             snapshotIndex >= 0 && (!CCDTopology.IsAvailable || !coreClassTopology.IsAvailable);
             snapshotIndex--)
        {
            CPUPerformanceSnapshot archivedCPU = archivedSnapshots[snapshotIndex].CPU;
            if (!CCDTopology.IsAvailable) CCDTopology = archivedCPU.CCDTopology;
            if (!coreClassTopology.IsAvailable) coreClassTopology = archivedCPU.CoreClassTopology;
        }

        List<CPUDetailedGraphGroup> detailedGraphGroups = CPUPerformanceDetailedView.CreateGraphGroups(
            CCDTopology,
            coreClassTopology);
#if DEBUG
        if (_cpuArchitectureSimulation != null)
            detailedGraphGroups = _cpuArchitectureSimulation.CreateGraphGroups(logicalProcessorCount);
#endif

        _histories.Clear();
        _cpuHighestCoreHistory = CreateHistory();
        _memoryUsedHistory = CreateMetricHistory();
        _pageFileView.Reset(_historyLengthMinutes, _sampleIntervalMilliseconds);
        _networkTransferRateHistories.Clear();
        _diskTransferRateHistories.Clear();
        RebuildCPULogicalProcessorGraphs(logicalProcessorCount);
        _cpuDetailedView.Rebuild(
            GetOrCreateHistory(CPUPerformanceSnapshot.StableDeviceID),
            _cpuHighestCoreHistory,
            detailedGraphGroups,
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
        _pageFileView.Append(snapshot);
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
                _hardwareNameResolver,
                _settings.GPUPowerLimits);
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
                    IsLightTheme,
                    sampleTimestamp => FormatDeviceColumnGraphHoverMetric(
                        deviceID,
                        sampleTimestamp));
                _deviceCards.Add(orderItem.ID, card);
            }
            else
                card.Update(device, history);

            card.SetSecondaryHistory(
                device.Kind == PerformanceDeviceKind.CPU
                && _settings.ShowCPUHighestCoreTrace
                    ? _cpuHighestCoreHistory
                    : null);
            card.SetGraphUnderfillVisible(_settings.ShowPerformanceGraphUnderfill);
            card.SetGraphHoverSnapsToSamples(!_settings.MovePerformanceGraphHoverFreely);
            rows.Add(new PerformanceDeviceColumnRow(orderItem.ID, card));
        }

        _deviceColumn.ReconcileRows(rows);
        RemoveStaleDeviceState();
        if (_selectedDeviceID == null || !_devices.ContainsKey(_selectedDeviceID))
            _selectedDeviceID = orderedItems.Count == 0 ? null : orderedItems[0].ID;

        _pageFileView.UpdatePresentation(snapshot.Memory.PageFiles.Span);
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
                _cpuLogicalProcessorGraphs[graphIndex].Refresh();
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
            return histories;

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
            return histories;

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
            return null;

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
                    return null;

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
                    return null;

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
                    return null;

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
            return null;

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
                    return null;

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
                    return null;

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
        F(
            nameof(AppStrings.Perf_Hover_SendReceiveFormat),
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(sendBytesPerSecond),
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(receiveBytesPerSecond));

    internal static string FormatCPUOverallHoverMetric(
        double highestCPUUtilizationPercent,
        double overallUtilizationPercent) =>
        F(
            nameof(AppStrings.Perf_Hover_CPUOverallFormat),
            PerformanceDevicePresentationFactory.FormatPercent(isAvailable: true, highestCPUUtilizationPercent),
            PerformanceDevicePresentationFactory.FormatPercent(isAvailable: true, overallUtilizationPercent));

    internal static string FormatNetworkDeviceColumnHoverMetric(
        double sendBytesPerSecond,
        double receiveBytesPerSecond) =>
        F(
            nameof(AppStrings.Perf_Hover_ShortSendReceiveFormat),
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(sendBytesPerSecond),
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(receiveBytesPerSecond));

    internal static string FormatDiskTransferHoverMetric(
        double readBytesPerSecond,
        double writeBytesPerSecond) =>
        F(
            nameof(AppStrings.Perf_Hover_ShortReadWriteFormat),
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(readBytesPerSecond),
            PerformanceDevicePresentationFactory.FormatBytesPerSecond(writeBytesPerSecond));

    internal static string FormatMemoryDeviceColumnHoverMetric(double usedBytes)
    {
        if (!double.IsFinite(usedBytes) || usedBytes < 0) return Unavailable;

        double usedGigabytes = usedBytes / BytesPerGibibyte;
        return string.Concat(
            usedGigabytes.ToString(
                usedGigabytes >= 100 ? "N0" : "N1",
                CultureInfo.CurrentCulture),
            str1: " G");
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
            return true;

        ReadOnlySpan<GPUPerformanceSnapshot> GPUs = snapshot.GPUs.Span;
        for (int GPUIndex = 0; GPUIndex < GPUs.Length; GPUIndex++)
        {
            if (!_histories.ContainsKey(GPUs[GPUIndex].DeviceID))
                return true;
        }

        ReadOnlySpan<NetworkPerformanceSnapshot> networks = snapshot.Networks.Span;
        for (int networkIndex = 0; networkIndex < networks.Length; networkIndex++)
        {
            if (!_histories.ContainsKey(networks[networkIndex].DeviceID))
                return true;
        }

        ReadOnlySpan<DiskPerformanceSnapshot> disks = snapshot.Disks.Span;
        for (int diskIndex = 0; diskIndex < disks.Length; diskIndex++)
        {
            if (!_histories.ContainsKey(disks[diskIndex].DeviceID))
                return true;
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
            val1: 0,
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
            PerformanceHistoryGraph graph = new(history, accent, _palette, _resources, IsLightTheme)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
            };
            graph.SetUnderfillVisible(_settings.ShowPerformanceGraphUnderfill);
            graph.SetHoverSnapsToSamples(!_settings.MovePerformanceGraphHoverFreely);
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
            SetColumn(hitTarget, graphColumn * 2);
            SetRow(hitTarget, graphRow * 2);
            if (trailingHitTestWidth > 0) SetColumnSpan(hitTarget, value: 2);
            if (trailingHitTestHeight > 0) SetRowSpan(hitTarget, value: 2);
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
                continue;

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
            if (!_devices.ContainsKey(deviceID))
                _staleDeviceIDs.Add(deviceID);
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
        PerformanceDeviceKind? selectedKind = GetSelectedDeviceKind();
        if (_disposed
            || !eventArgs.GetCurrentPoint(_graphSurface).Properties.IsRightButtonPressed
            || selectedKind is not (PerformanceDeviceKind.CPU or PerformanceDeviceKind.GPU)
            || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        PixelPoint screenPosition = _graphSurface.PointToScreen(
            eventArgs.GetPosition(_graphSurface));
        string selectedDeviceID = _selectedDeviceID!;
        eventArgs.Handled = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !string.Equals(_selectedDeviceID, selectedDeviceID, StringComparison.Ordinal)) return;

            if (selectedKind == PerformanceDeviceKind.CPU)
                ShowCPUGraphContextMenu(owner, screenPosition);
            else
                ShowGPUGraphContextMenu(owner, screenPosition, selectedDeviceID);
        });
    }

    private PerformanceDeviceKind? GetSelectedDeviceKind() =>
        _selectedDeviceID != null
        && _devices.TryGetValue(
            _selectedDeviceID,
            out PerformanceDevicePresentation? selectedDevice)
            ? selectedDevice.Kind
            : null;

    private void ShowCPUGraphContextMenu(Window owner, PixelPoint screenPosition)
    {
        CloseGraphContextMenu();
        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry(
            Text: L(nameof(AppStrings.Perf_Label_LogicalProcessors)),
            () => SetCPUGraphView(CPUPerformanceGraphView.LogicalProcessors))
        {
            TrailingGlyphMetadata = _settings.CPUPerformanceGraphView
                                    == CPUPerformanceGraphView.LogicalProcessors
                ? TaskManagerGlyphCatalog.SELECTED
                : null,
            Node = ControlMap.CPUGraphMenu.LogicalProcessors
        });
        entries.Add(new ContextMenuEntry(
            Text: L(nameof(AppStrings.Perf_CPUView_OverallUtilization)),
            () => SetCPUGraphView(CPUPerformanceGraphView.OverallUtilization))
        {
            TrailingGlyphMetadata = _settings.CPUPerformanceGraphView
                                    == CPUPerformanceGraphView.OverallUtilization
                ? TaskManagerGlyphCatalog.SELECTED
                : null,
            Node = ControlMap.CPUGraphMenu.OverallUtilization
        });
        entries.Add(new ContextMenuEntry(
            Text: L(nameof(AppStrings.Perf_CPUView_DetailedView)),
            () => SetCPUGraphView(CPUPerformanceGraphView.DetailedView))
        {
            TrailingGlyphMetadata = _settings.CPUPerformanceGraphView
                                    == CPUPerformanceGraphView.DetailedView
                ? TaskManagerGlyphCatalog.SELECTED
                : null,
            Node = ControlMap.CPUGraphMenu.DetailedView
        });

        ShowGraphContextMenu(owner, screenPosition, entries, ControlMap.CPUGraphMenu.ID);
    }

    /// <summary>
    /// Offers watts for a GPU's power. The first choice reads the default power limit once through the vendor library;
    /// the entry stays greyed out, with the reason as its tooltip, when that cannot work.
    /// </summary>
    private void ShowGPUGraphContextMenu(Window owner, PixelPoint screenPosition, string deviceID)
    {
        CloseGraphContextMenu();
        GPUPerformanceSnapshot? GPU = FindLatestGPU(deviceID);
        GPUAdapterIdentity identity = GPU?.Details?.Identity ?? GPUAdapterIdentity.Empty;
        ushort vendorID = identity.HardwareID.VendorID;
        string libraryFileName = GPUPowerLimitReader.GetLibraryFileName(vendorID);
        GPUPowerLimitRecord? record = GPUPowerLimitCollection.Find(
            _settings.GPUPowerLimits,
            identity.DeviceInstanceID);
        bool hasCurrentLimit = record != null && GPUPowerLimitCollection.IsCurrent(record, identity.VBIOSVersion);
        bool showsWatts = hasCurrentLimit && record!.ShowWatts;
        bool isReading = _gpuPowerLimitReads.Contains(identity.DeviceInstanceID);
        string? disabledReason = GPU?.Details?.HasPowerData != true || identity.DeviceInstanceID.Length == 0
            ? L(nameof(AppStrings.Perf_GPUMenu_NoPowerDataTip))
            : hasCurrentLimit
                ? null
                : libraryFileName.Length == 0
                    ? L(nameof(AppStrings.Perf_GPUMenu_UnsupportedVendorTip))
                    : !GPUPowerLimitReader.TryFindLibrary(vendorID, out _)
                        ? F(nameof(AppStrings.Perf_GPUMenu_NoLibraryTip), libraryFileName)
                        : null;
        string? tip = disabledReason
                      ?? (_gpuPowerLimitErrors.TryGetValue(identity.DeviceInstanceID, out string? error)
                          ? F(nameof(AppStrings.Perf_GPUMenu_ReadFailedTip), error)
                          : hasCurrentLimit
                              ? null
                              : F(nameof(AppStrings.Perf_GPUMenu_ShowPowerInWattsTip), libraryFileName));

        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry(
            Text: isReading
                ? L(nameof(AppStrings.Perf_GPUMenu_ReadingPowerLimit))
                : L(nameof(AppStrings.Perf_GPUMenu_ShowPowerInWatts)),
            () => OnShowGPUPowerInWattsClicked(identity, hasCurrentLimit, showsWatts))
        {
            TrailingGlyphMetadata = showsWatts ? TaskManagerGlyphCatalog.SELECTED : null,
            IsEnabled = disabledReason == null && !isReading,
            ToolTip = tip,
            Node = ControlMap.GPUGraphMenu.ShowPowerInWatts
        });
        ShowGraphContextMenu(owner, screenPosition, entries, ControlMap.GPUGraphMenu.ID);
    }

    private void OnShowGPUPowerInWattsClicked(GPUAdapterIdentity identity, bool hasCurrentLimit, bool showsWatts)
    {
        if (_disposed) return;

        if (hasCurrentLimit)
        {
            _settings.UpdateGPUShowPowerInWatts(identity.DeviceInstanceID, !showsWatts);
            return;
        }

        ReadGPUPowerLimitAsync(identity).ForgetLogged(nameof(ReadGPUPowerLimitAsync));
    }

    /// <summary>Reads the GPU's default power limit in a child process, then saves it and switches to watts.</summary>
    private async Task ReadGPUPowerLimitAsync(GPUAdapterIdentity identity)
    {
        string deviceInstanceID = identity.DeviceInstanceID;
        if (!_gpuPowerLimitReads.Add(deviceInstanceID)) return;

        _gpuPowerLimitErrors.Remove(deviceInstanceID);
        UpdateGPUPowerDisplay();
        GPUPowerLimitResult result;
        try
        {
            result = await GPUPowerLimitReader.ReadAsync(
                new GPUPowerLimitTarget(identity.HardwareID.VendorID, deviceInstanceID, identity.Address),
                _disposeCancellation.Token);
        }
        finally
        {
            _gpuPowerLimitReads.Remove(deviceInstanceID);
        }

        if (_disposed) return;

        if (result.Succeeded)
            _settings.UpdateGPUPowerLimit(deviceInstanceID, identity.VBIOSVersion, result.DefaultLimitMilliwatts);
        else
        {
            _gpuPowerLimitErrors[deviceInstanceID] = result.Error;
            TADNLog.Log($"PerformancePage.ReadGPUPowerLimitAsync: {deviceInstanceID}: {result.Error}");
        }

        UpdateGPUPowerDisplay();
    }

    /// <summary>Points the GPU view's power graph at watts or a percentage for the GPU it shows.</summary>
    private void UpdateGPUPowerDisplay()
    {
        string? deviceID = _gpuPerformanceDetailsView.DeviceID;
        GPUPerformanceSnapshot? GPU = deviceID != null ? FindLatestGPU(deviceID) : null;
        if (GPU == null)
        {
            _gpuPerformanceDetailsView.SetPowerDisplay(double.NaN, status: null);
            return;
        }

        string deviceInstanceID = GPU.Details?.Identity.DeviceInstanceID ?? string.Empty;
        _gpuPerformanceDetailsView.SetPowerDisplay(
            PerformanceDevicePresentationFactory.GetGPUPowerLimitWatts(GPU, _settings.GPUPowerLimits),
            _gpuPowerLimitReads.Contains(deviceInstanceID)
                ? L(nameof(AppStrings.Perf_GPUMenu_ReadingPowerLimit))
                : null);
    }

    private GPUPerformanceSnapshot? FindLatestGPU(string deviceID)
    {
        ReadOnlySpan<GPUPerformanceSnapshot> GPUs = _snapshotService.GetLatestSnapshot().GPUs.Span;
        for (int GPUIndex = 0; GPUIndex < GPUs.Length; GPUIndex++)
        {
            if (string.Equals(GPUs[GPUIndex].DeviceID, deviceID, StringComparison.Ordinal))
                return GPUs[GPUIndex];
        }

        return null;
    }

    private void ShowGraphContextMenu(
        Window owner,
        PixelPoint screenPosition,
        ContextMenuEntryBuilder entries,
        ControlMapNodeID surface)
    {
        TaskManagerContextMenuWindow menuWindow = new TaskManagerContextMenuWindow(
            entries.ToList(),
            _palette,
            _settings.EnableRoundedCorners,
            _settings).MapTo(surface);
        _graphContextMenuWindow = menuWindow;
        menuWindow.Closed += OnGraphContextMenuClosed;
        menuWindow.ShowAt(owner, screenPosition);
    }

    private void SetCPUGraphView(CPUPerformanceGraphView graphView)
    {
        if (_disposed) return;

        _settings.UpdateCPUPerformanceGraphView(graphView);
    }

    private void CloseGraphContextMenu()
    {
        TaskManagerContextMenuWindow? menuWindow = _graphContextMenuWindow;
        if (menuWindow == null) return;

        _graphContextMenuWindow = null;
        menuWindow.Closed -= OnGraphContextMenuClosed;
        menuWindow.Close();
    }

    private void OnGraphContextMenuClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is TaskManagerContextMenuWindow menuWindow)
            menuWindow.Closed -= OnGraphContextMenuClosed;
        if (ReferenceEquals(sender, _graphContextMenuWindow))
            _graphContextMenuWindow = null;
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
            _detailTitle.Text = L(nameof(AppStrings.Perf_NoDevices));
            _detailHardwareName.Text = string.Empty;
            _detailHardwareName.IsVisible = true;
            _memoryErrorPanel.IsVisible = false;
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
            SetPageFileGraphVisible(false);
            SetStatistics(deviceKind: null, ReadOnlySpan<PerformanceStatistic>.Empty);
            return;
        }

        _detailTitle.Text = selectedDevice.Title;
        bool isMemory = selectedDevice.Kind == PerformanceDeviceKind.Memory;
        _detailHardwareName.Text = selectedDevice.HardwareName;
        _detailHardwareName.IsVisible = !isMemory;
        _memoryErrorPanel.IsVisible = isMemory;
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
#if DEBUG
        if (showDetailedCPU && _cpuArchitectureSimulation != null)
        {
            _detailHardwareName.Text = string.Concat(
                str0: "Simulated ",
                _cpuArchitectureSimulation.ProcessorName);
        }
#endif
        _detailGraph.IsVisible = !showLogicalProcessors && !showDetailedCPU && !showGPUDetails;
        _cpuLogicalProcessorGrid.IsVisible = showLogicalProcessors;
        _cpuDetailedView.IsVisible = showDetailedCPU;
        _genericGraphHeader.IsVisible = !showGPUDetails;
        // The memory graph's time and zero labels only take room from the page file graph below it
        _genericGraphFooter.IsVisible = !showGPUDetails && !isMemory;
        SetPageFileGraphVisible(isMemory && _pageFileView.HasPageFiles);
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

            UpdateGPUPowerDisplay();
        }
        else
            _gpuPerformanceDetailsView.Hide();

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
            _memoryModuleDetailsPanel.IsVisible = false;

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
            _diskPerformanceDetailsView.Hide();

        SetStatistics(selectedDevice.Kind, selectedDevice.Statistics.Span);
    }

    /// <summary>
    /// Splits the graph area two thirds to the memory graph and one third to the page file graph, or gives it all
    /// back to the device graph. The page file row never shrinks below what its heading, graph and legend need, so
    /// a short window takes the height from the memory graph instead of overlapping them.
    /// </summary>
    private void SetPageFileGraphVisible(bool isVisible)
    {
        _pageFileView.IsVisible = isVisible;
        _detailGraphRow.Height = new GridLength(isVisible ? 2 : 1, GridUnitType.Star);
        _pageFileGraphRow.Height = isVisible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _pageFileGraphRow.MinHeight = isVisible ? _pageFileMinimumHeight : 0;
    }

    private void OnCompactLayoutInputSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        if (_disposed || _compactEvaluationPending) return;

        // Evaluate after the layout pass so the header, graph area and statistics all hold their new bounds
        _compactEvaluationPending = true;
        Dispatcher.UIThread.Post(EvaluateCompactMode, DispatcherPriority.Background);
    }

    /// <summary>
    /// Uses compact sizes while full-size title and statistics would leave the graph area shorter than its minimum
    /// height. Both sizes are measured at the current width, since the statistics wrap and stack differently.
    /// </summary>
    private void EvaluateCompactMode()
    {
        _compactEvaluationPending = false;
        double width = _statisticsPanel.Bounds.Width;
        if (_disposed || width <= 0) return;

        bool wasCompact = _isCompact;
        double currentChromeHeight = MeasureChromeHeight(wasCompact, width);
        double fullSizeChromeHeight = wasCompact ? MeasureChromeHeight(isCompact: false, width) : currentChromeHeight;
        double fullSizeGraphAreaHeight = _graphArea.Bounds.Height + currentChromeHeight - fullSizeChromeHeight;
        ApplyCompactMode(ShouldUseCompactMode(
            wasCompact,
            fullSizeGraphAreaHeight,
            _resources.AxamlTaskManagerPerformance.DetailGraphMinimumHeight,
            _resources.AxamlTaskManagerPerformance.DetailCompactModeHysteresis));
    }

    /// <summary>
    /// Decides compact mode from the graph area height full-size text would leave. Leaving compact mode takes some
    /// height to spare, so a size right at the threshold does not flip back and forth.
    /// </summary>
    internal static bool ShouldUseCompactMode(
        bool isCompact,
        double fullSizeGraphAreaHeight,
        double minimumGraphAreaHeight,
        double hysteresis) =>
        fullSizeGraphAreaHeight < (isCompact ? minimumGraphAreaHeight + hysteresis : minimumGraphAreaHeight);

    /// <summary>Measures the header and statistics at one size; the caller applies the size it settles on.</summary>
    private double MeasureChromeHeight(bool isCompact, double width)
    {
        ApplyCompactMode(isCompact);
        Size available = new(width, double.PositiveInfinity);
        _detailHeader.Measure(available);
        _statisticsPanel.Measure(available);
        return _detailHeader.DesiredSize.Height + _statisticsPanel.DesiredSize.Height;
    }

    private void ApplyCompactMode(bool isCompact)
    {
        _isCompact = isCompact;
        var metrics = _resources.AxamlTaskManagerPerformance;
        _detailTitle.FontSize = isCompact ? metrics.DetailTitleCompactFontSize : metrics.DetailTitleFontSize;
        if (_configuredStatisticCount >= 0)
        {
            ConfigureStatisticsLayout(
                _configuredStatisticDeviceKind,
                _configuredStatisticCount,
                _configuredPrimaryStatisticCount);
        }
    }

    private void OnPageFileMinimumHeightChanged(double minimumHeight)
    {
        if (_disposed) return;

        _pageFileMinimumHeight = minimumHeight;
        if (_pageFileView.IsVisible) _pageFileGraphRow.MinHeight = minimumHeight;
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
            return;

        CPUCard.SetSecondaryHistory(
            _settings.ShowCPUHighestCoreTrace ? _cpuHighestCoreHistory : null);
    }

    /// <summary>Applies the underfill toggle to every Performance graph surface.</summary>
    private void UpdateGraphUnderfillVisibility()
    {
        bool isVisible = _settings.ShowPerformanceGraphUnderfill;
        _detailGraph.SetUnderfillVisible(isVisible);
        for (int graphIndex = 0; graphIndex < _cpuLogicalProcessorGraphs.Count; graphIndex++)
            _cpuLogicalProcessorGraphs[graphIndex].SetUnderfillVisible(isVisible);

        _cpuDetailedView.SetGraphUnderfillVisible(isVisible);
        _diskPerformanceDetailsView.SetGraphUnderfillVisible(isVisible);
        _gpuPerformanceDetailsView.SetGraphUnderfillVisible(isVisible);
        _pageFileView.SetGraphUnderfillVisible(isVisible);
        foreach (PerformanceDeviceCard card in _deviceCards.Values)
            card.SetGraphUnderfillVisible(isVisible);
    }

    /// <summary>Applies the free hover toggle to every Performance graph whose hover line can lock to samples.</summary>
    private void UpdateGraphHoverSnapping()
    {
        bool snapsToSamples = !_settings.MovePerformanceGraphHoverFreely;
        _detailGraph.SetHoverSnapsToSamples(snapsToSamples);
        for (int graphIndex = 0; graphIndex < _cpuLogicalProcessorGraphs.Count; graphIndex++)
            _cpuLogicalProcessorGraphs[graphIndex].SetHoverSnapsToSamples(snapsToSamples);

        _cpuDetailedView.SetGraphHoverSnapsToSamples(snapsToSamples);
        _gpuPerformanceDetailsView.SetGraphHoverSnapsToSamples(snapsToSamples);
        _pageFileView.SetGraphHoverSnapsToSamples(snapsToSamples);
        foreach (PerformanceDeviceCard card in _deviceCards.Values)
            card.SetGraphHoverSnapsToSamples(snapsToSamples);
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
            && _configuredStatisticDeviceKind == deviceKind
            && _configuredStatisticCompact == _isCompact)
            return;

        _configuredStatisticCount = visibleCount;
        _configuredPrimaryStatisticCount = primaryStatisticCount;
        _configuredStatisticDeviceKind = deviceKind;
        _configuredStatisticCompact = _isCompact;
        var metrics = _resources.AxamlTaskManagerPerformance;
        _statisticsPanel.ClearPrimary();
        _metadataStatistics.Children.Clear();
        _metadataStatistics.RowDefinitions.Clear();
        for (int statisticIndex = 0; statisticIndex < MaximumDetailStatistics; statisticIndex++)
        {
            StackPanel statistic = _statisticContainers[statisticIndex];
            TextBlock label = _statisticLabels[statisticIndex];
            TextBlock value = _statisticValues[statisticIndex];
            bool isVisible = statisticIndex < visibleCount;
            statistic.IsVisible = isVisible;
            if (!isVisible) continue;

            bool isPrimary = statisticIndex < primaryStatisticCount;
            statistic.Children.Clear();
            if (isPrimary)
            {
                // The statistics panel widens every one to the widest, so this is only the narrowest column
                statistic.Width = double.NaN;
                statistic.MinWidth = metrics.DetailPrimaryStatisticWidth;
                statistic.Margin = _isCompact
                    ? metrics.DetailPrimaryStatisticCompactMargin
                    : metrics.DetailPrimaryStatisticMargin;
                label.Margin = default;
                value.Margin = default;
                value.TextTrimming = TextTrimming.None;
                value.FontSize = _isCompact
                    ? metrics.DetailPrimaryStatisticValueCompactFontSize
                    : metrics.DetailPrimaryStatisticValueFontSize;
                statistic.Children.Add(label);
                statistic.Children.Add(value);
                _statisticsPanel.AddPrimary(statistic);
                continue;
            }

            // A metadata pair sits in the shared label and value columns rather than in its own panel
            int row = _metadataStatistics.RowDefinitions.Count;
            _metadataStatistics.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            label.Margin = _resources.AxamlTaskManagerPerformance.DetailMetadataStatisticMargin;
            value.Margin = _resources.AxamlTaskManagerPerformance.DetailMetadataStatisticMargin;
            label.VerticalAlignment = VerticalAlignment.Center;
            value.VerticalAlignment = VerticalAlignment.Center;
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            value.FontSize = _isCompact
                ? metrics.DetailStatisticValueCompactFontSize
                : metrics.DetailStatisticValueFontSize;
            Grid.SetRow(label, row);
            Grid.SetColumn(label, value: 0);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, value: 1);
            _metadataStatistics.Children.Add(label);
            _metadataStatistics.Children.Add(value);
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
            PerformanceDeviceKind.GPU => 8,
            PerformanceDeviceKind.Network => 2,
            PerformanceDeviceKind.Disk => 4,
            _ => 0
        };
        return Math.Min(requestedCount, visibleCount);
    }

    private static InkGlyphButton CreateMemoryErrorButton(
        Glyph glyph,
        SettingsPalette palette,
        TaskManagerWindowResources resources) =>
        new(
            glyph,
            palette,
            resources.AxamlTaskManagerPerformance.MemoryErrorButtonWidth,
            resources.AxamlTaskManagerPerformance.MemoryErrorButtonHeight,
            resources.AxamlTaskManagerPerformance.MemoryErrorGlyphInkSize,
            resources.AxamlTaskManagerPerformance.MemoryErrorGlyphHoverStrokeThickness,
            resources.AxamlTaskManagerDetails.SearchActionDisabledOpacity)
        {
            VerticalAlignment = VerticalAlignment.Center
        };

    /// <summary>Returns how far below the center of its line box a font draws the ink of <paramref name="text"/>.</summary>
    private static double InkCenterOffset(string text, FontFamily fontFamily, double fontSize, FontWeight fontWeight)
    {
        FormattedText formatted = new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(fontFamily, FontStyle.Normal, fontWeight),
            fontSize,
            Brushes.Black);
        Rect ink = formatted.BuildGeometry(default)?.Bounds ?? default;
        return ink.Height > 0 ? ink.Center.Y - formatted.Height / 2 : 0;
    }

    /// <summary>Shows the unacknowledged memory error count, red when it is not zero.</summary>
    private void UpdateMemoryErrorDisplay()
    {
        if (_disposed) return;

        IReadOnlyList<MemoryErrorEntry> entries = _memoryErrorLogMonitor.Entries;
        MemoryErrorAcknowledgement acknowledgement = _settings.MemoryErrorAcknowledgement;
        int count = acknowledgement.CountUnacknowledged(entries);
        string? error = _memoryErrorLogMonitor.Error;
        _memoryErrorLabel.Text = !_memoryErrorLogMonitor.HasRead || (error != null && entries.Count == 0)
            ? F(nameof(AppStrings.Perf_MemoryErrors_LabelFormat), Unavailable)
            : F(nameof(AppStrings.Perf_MemoryErrors_LabelFormat), count.ToString(CultureInfo.CurrentCulture));
        // The tint grows the label outward only while it shows, so a zero count keeps the hardware name's position
        _memoryErrorLabelFrame.Background = count > 0 ? _memoryErrorTint : Brushes.Transparent;
        _memoryErrorLabelFrame.Padding = count > 0
            ? _resources.AxamlTaskManagerPerformance.MemoryErrorLabelPadding
            : default;
        _memoryErrorResetButton.IsEnabled = count > 0;
        TrayAppDotNETToolTip.SetTip(
            _memoryErrorLabelFrame,
            _memoryErrorLogMonitor.HasRead
                ? MemoryErrorLogWindow.BuildSummary(entries, acknowledgement, error)
                : null);
    }

    private void OnMemoryErrorInfoClick(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;

        if (_memoryErrorLogWindow != null)
        {
            _memoryErrorLogWindow.Activate();
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        MemoryErrorLogWindow window = new(
            _memoryErrorLogMonitor,
            () => _settings.MemoryErrorAcknowledgement,
            _palette);
        window.Closed += OnMemoryErrorLogWindowClosed;
        _memoryErrorLogWindow = window;
        _ = window.ShowDialog(owner);
    }

    private void OnMemoryErrorLogWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is Window window) window.Closed -= OnMemoryErrorLogWindowClosed;
        if (ReferenceEquals(sender, _memoryErrorLogWindow)) _memoryErrorLogWindow = null;
    }

    /// <summary>
    /// Stops counting the errors recorded so far, after confirmation. Windows cannot delete single events, so the
    /// System log keeps them and the log window still lists them.
    /// </summary>
    private async void OnMemoryErrorResetClick(object? sender, EventArgs eventArgs)
    {
        if (_disposed || TopLevel.GetTopLevel(this) is not Window owner) return;

        IReadOnlyList<MemoryErrorEntry> entries = _memoryErrorLogMonitor.Entries;
        int count = _settings.MemoryErrorAcknowledgement.CountUnacknowledged(entries);
        if (count == 0) return;

        TrayAppDotNETConfirmationResult result;
        try
        {
            using TrayAppDotNETConfirmationWindow confirmation = new(
                title: L(nameof(AppStrings.Perf_MemoryErrors_ResetTitle)),
                description: F(nameof(AppStrings.Perf_MemoryErrors_ResetMessageFormat), count),
                confirmText: L(nameof(AppStrings.Common_Reset)),
                _palette,
                _settings.EnableRoundedCorners,
                cancelText: L(nameof(CommonStrings.Common_Cancel)))
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            confirmation.MapTo(ControlMap.MemoryErrorResetConfirmation.ID);
            result = await confirmation.ShowDialog<TrayAppDotNETConfirmationResult>(owner);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Memory error reset confirmation failed: {exception}");
            return;
        }

        if (_disposed || result != TrayAppDotNETConfirmationResult.Confirmed) return;

        // Acknowledge only what the prompt counted; an error logged while it was open stays counted
        _settings.AcknowledgeMemoryErrors(MemoryErrorAcknowledgement.Through(entries));
        UpdateMemoryErrorDisplay();
    }

    public void Dispose()
    {
        if (_disposed) return;

        SetPageActive(false);
        _disposed = true;
        CloseGraphContextMenu();
        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
        _graphSurface.PointerPressed -= OnGraphSurfacePointerPressed;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _snapshotService.SnapshotUpdated -= OnSnapshotUpdated;
        _pageFileView.MinimumHeightChanged -= OnPageFileMinimumHeightChanged;
        _graphArea.SizeChanged -= OnCompactLayoutInputSizeChanged;
        _statisticsPanel.SizeChanged -= OnCompactLayoutInputSizeChanged;
        _memoryErrorLogMonitor.Changed -= UpdateMemoryErrorDisplay;
        _memoryErrorLogMonitor.Dispose();
        _memoryErrorLogWindow?.Close();
        _memoryErrorInfoButton.Click -= OnMemoryErrorInfoClick;
        _memoryErrorInfoButton.Dispose();
        _memoryErrorResetButton.Click -= OnMemoryErrorResetClick;
        _memoryErrorResetButton.Dispose();
#if DEBUG
        TaskManagerContextMenuResources.ResourcesReloaded -= OnContextMenuAXAMLResourcesReloaded;
        for (int buttonIndex = 0; buttonIndex < _cpuArchitectureSimulationButtons.Count; buttonIndex++)
            _cpuArchitectureSimulationButtons[buttonIndex].Click -= OnCPUArchitectureSimulationClick;
        _cpuArchitectureSimulationButtons.Clear();
#endif
        _deviceScrollViewport.Dispose();
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
            bool isLightTheme,
            Func<long, string?> hoverMetricProvider)
        {
            _palette = palette;
            _accent = device.Accent;
            _accentBrush = new SolidColorBrush(device.Accent);
            double trailingHitTestHeight = Math.Max(
                val1: 0,
                resources.AxamlTaskManagerPerformance.DeviceColumnSpacing);
            Height = resources.AxamlTaskManagerPerformance.DeviceCardHeight + trailingHitTestHeight;
            Background = Brushes.Transparent;
            Cursor = TrayAppDotNETCursors.Hand;

            _graph = new PerformanceHistoryGraph(
                history,
                device.Accent,
                palette,
                resources,
                isLightTheme,
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
                (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
            _title.TextTrimming = TextTrimming.CharacterEllipsis;
            _subtitle = TrayAppDotNETSettingsUI.Text(
                device.Subtitle,
                palette,
                resources.AxamlTaskManagerPerformance.DeviceSubtitleFontSize,
                (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
            _subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
            _subtitle.IsVisible = ShouldShowSubtitle(device);
            _summary = TrayAppDotNETSettingsUI.Text(
                device.Summary,
                palette,
                resources.AxamlTaskManagerPerformance.DeviceSummaryFontSize,
                (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
            _summary.TextTrimming = TextTrimming.CharacterEllipsis;
            _summary.IsVisible = !string.IsNullOrWhiteSpace(device.Summary);

            StackPanel labels = new()
            {
                VerticalAlignment = VerticalAlignment.Center, Children = { _title, _subtitle, _summary }
            };
            Grid content = new()
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
            };
            content.Children.Add(_graph);
            SetColumn(labels, value: 1);
            content.Children.Add(labels);
            _surface = new Border
            {
                Margin = new Thickness(left: 0, top: 0, right: 0, trailingHitTestHeight),
                Padding = resources.AxamlTaskManagerPerformance.DeviceCardPadding,
                CornerRadius = resources.AxamlTaskManagerPerformance.DeviceCardCornerRadius,
                BorderThickness = resources.AxamlTaskManagerPerformance.DeviceCardBorderThickness,
                ClipToBounds = true,
                Child = content
            };
            Child = _surface;

            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
            TrayAppDotNETToolTip.SetTip(this, tip: L(nameof(AppStrings.Perf_DeviceColumn_ReorderTip)));
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

        /// <summary>Shows or hides the graph's translucent area fill.</summary>
        public void SetGraphUnderfillVisible(bool isVisible) =>
            _graph.SetUnderfillVisible(isVisible);

        /// <summary>Locks the graph's hover line to samples, or lets it follow the pointer.</summary>
        public void SetGraphHoverSnapsToSamples(bool snapsToSamples) =>
            _graph.SetHoverSnapsToSamples(snapsToSamples);

        public void SetSelected(bool isSelected)
        {
            if (_isSelected == isSelected) return;
            _isSelected = isSelected;
            UpdateSurface();
        }

        private static bool ShouldShowSubtitle(PerformanceDevicePresentation device) =>
            device.Kind is not PerformanceDeviceKind.CPU
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
            _graph.SetHoverTrailingExtent(new Size(trailingHitTestWidth, trailingHitTestHeight));
            Background = Brushes.Transparent;
            Padding = new Thickness(left: 0, top: 0, trailingHitTestWidth, trailingHitTestHeight);
            Child = graph;
        }

        protected override void OnPointerEntered(PointerEventArgs eventArgs)
        {
            base.OnPointerEntered(eventArgs);
            _graph.TrackPointer(eventArgs.GetLatestPosition(_graph));
        }

        protected override void OnPointerMoved(PointerEventArgs eventArgs)
        {
            base.OnPointerMoved(eventArgs);
            _graph.TrackPointer(eventArgs.GetLatestPosition(_graph));
        }

        protected override void OnPointerExited(PointerEventArgs eventArgs)
        {
            base.OnPointerExited(eventArgs);
            _graph.ClearPointer();
        }
    }
}
