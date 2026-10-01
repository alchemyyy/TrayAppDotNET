using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI.Performance;

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
            text: "Disk transfer rate",
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        _maximumLabel = TrayAppDotNETSettingsUI.Text(
            text: "1 MB/s",
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        _maximumLabel.HorizontalAlignment = HorizontalAlignment.Right;
        Grid header = CreateScaleRow(heading, _maximumLabel);
        header.Margin = resources.AxamlTaskManagerPerformance.SpecialGraphHeaderMargin;
        Children.Add(header);

        Color accent = PerformanceDevicePresentationFactory.GetAccent(PerformanceDeviceKind.Disk);
        _graph = new PerformanceMetricHistoryGraph(
            _readHistory,
            _writeHistory,
            primaryLabel: "R",
            secondaryLabel: "W",
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
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        TextBlock minimumLabel = TrayAppDotNETSettingsUI.Text(
            text: "0",
            palette,
            resources.AxamlTaskManagerPerformance.DetailGraphLabelFontSize,
            (FontWeight)resources.AxamlTaskManagerPerformance.TextFontWeight);
        minimumLabel.HorizontalAlignment = HorizontalAlignment.Right;
        Children.Add(CreateScaleRow(_historyWindowLabel, minimumLabel));
    }

    /// <summary>Shows or hides the translucent areas beneath the disk transfer traces.</summary>
    public void SetGraphUnderfillVisible(bool isVisible) =>
        _graph.SetUnderfillVisible(isVisible);

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
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { left }
        };
        Grid.SetColumn(right, value: 1);
        row.Children.Add(right);
        return row;
    }
}
