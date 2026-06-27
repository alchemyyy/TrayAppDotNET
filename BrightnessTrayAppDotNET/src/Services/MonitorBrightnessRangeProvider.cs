using System.ComponentModel;

namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Tracks the live (min, max) manual-slider brightness range across all currently-enumerated monitors plus the
/// flyout's master monitor, and re-emits it whenever any one of those values changes.
/// Lifts the per-monitor PropertyChanged subscription dance out of <see cref="SettingsWindow"/> so the curve editor's
/// degeneration lines can be driven by an event rather than by code that only runs while the settings window is open.
/// CurveReleased rows are intentionally NOT filtered out of the active set: in absolute curve mode, dragging an
/// individual slider transitions that row to CurveReleased, but the slider value the user just dialed in is still the
/// relevant input to the gap calculation - excluding released monitors would make the emitted range ignore that drag.
/// </summary>
public sealed class MonitorBrightnessRangeProvider : IDisposable
{
    private readonly MonitorService _monitorService;
    private readonly List<MonitorInfo> _subscribed = [];
    private MonitorInfo? _subscribedMaster;
    private bool _disposed;

    /// <summary>
    /// Raised on the UI thread whenever the active-set min/max may have moved.
    /// (min, max) are null when there are no monitors to sample (matches the editor's "no range" state).
    /// </summary>
    public event Action<double?, double?>? LiveBrightnessRangeChanged;

    public MonitorBrightnessRangeProvider(MonitorService monitorService)
    {
        _monitorService = monitorService;
        _monitorService.MonitorsRefreshed += OnMonitorsRefreshed;
        Resubscribe();
    }

    /// <summary>
    /// Computes the current (min, max) and pushes it through <see cref="LiveBrightnessRangeChanged"/>.
    /// Subscribers that need an immediate value at attach time can call this once after subscribing instead of
    /// waiting for the next refresh / property change.
    /// </summary>
    public void EmitCurrent()
    {
        if (_disposed) return;

        (double? min, double? max) = ComputeRange();
        LiveBrightnessRangeChanged?.Invoke(min, max);
    }

    private (double? min, double? max) ComputeRange()
    {
        double? min = null;
        double? max = null;
        foreach (MonitorInfo monitor in _monitorService.Monitors)
        {
            double b = monitor.Brightness;
            min = min is null ? b : Math.Min(min.Value, b);
            max = max is null ? b : Math.Max(max.Value, b);
        }

        return (min, max);
    }

    private void OnMonitorsRefreshed()
    {
        if (_disposed) return;

        Resubscribe();
        EmitCurrent();
    }

    /// <summary>
    /// Detaches PropertyChanged from any monitors we'd previously hooked, then re-attaches to the current live set
    /// (plus the flyout's master monitor when one is registered).
    /// The master is read lazily from <see cref="AppServices.BrightnessFlyout"/> because the flyout is created shortly
    /// after this provider; reading at attach time makes the wire-up idempotent regardless of which side starts up
    /// first.
    /// </summary>
    private void Resubscribe()
    {
        foreach (MonitorInfo monitor in _subscribed)
            monitor.PropertyChanged -= OnMonitorPropertyChanged;
        _subscribed.Clear();

        if (_subscribedMaster != null)
        {
            _subscribedMaster.PropertyChanged -= OnMonitorPropertyChanged;
            _subscribedMaster = null;
        }

        foreach (MonitorInfo monitor in _monitorService.Monitors)
        {
            monitor.PropertyChanged += OnMonitorPropertyChanged;
            _subscribed.Add(monitor);
        }

        if (AppServices.BrightnessFlyout is { } flyout)
        {
            flyout.MasterMonitor.PropertyChanged += OnMonitorPropertyChanged;
            _subscribedMaster = flyout.MasterMonitor;
        }
    }

    private void OnMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only Brightness changes and SliderState transitions influence the active-set min/max.
        // SliderState collapses the previous IsSliderEnabled / IsReleasedFromCurve filters into one property,
        // so a single PropertyChanged on it covers both axes.
        if (e.PropertyName is not (nameof(MonitorInfo.Brightness) or nameof(MonitorInfo.SliderState))) return;

        EmitCurrent();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _monitorService.MonitorsRefreshed -= OnMonitorsRefreshed;

        foreach (MonitorInfo monitor in _subscribed)
            monitor.PropertyChanged -= OnMonitorPropertyChanged;
        _subscribed.Clear();

        if (_subscribedMaster != null)
        {
            _subscribedMaster.PropertyChanged -= OnMonitorPropertyChanged;
            _subscribedMaster = null;
        }
    }
}
