#if DEBUG
using Avalonia.Controls;
using Avalonia.Input;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Coalesces hover captures so only the newest input target reaches the snapshot builder.</summary>
internal sealed class ControlHoverInspectorCaptureQueue(
    Action<Action> schedule,
    Action<TopLevel, IInputElement> capture)
{
    private readonly Action<Action> _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));

    private readonly Action<TopLevel, IInputElement> _capture =
        capture ?? throw new ArgumentNullException(nameof(capture));

    private PendingCapture? _pendingCapture;
    private bool _isScheduled;

    internal bool HasPendingCapture => _pendingCapture.HasValue;

    /// <summary>Replaces any pending target and schedules at most one dispatcher callback.</summary>
    public void Enqueue(TopLevel topLevel, IInputElement hitElement)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        ArgumentNullException.ThrowIfNull(hitElement);

        _pendingCapture = new PendingCapture(topLevel, hitElement);
        if (_isScheduled) return;

        _isScheduled = true;
        _schedule(Drain);
    }

    /// <summary>Discards a pending target without retaining its visual tree.</summary>
    public void CancelPending() => _pendingCapture = null;

    private void Drain()
    {
        _isScheduled = false;
        PendingCapture? pendingCapture = _pendingCapture;
        _pendingCapture = null;
        if (!pendingCapture.HasValue) return;

        PendingCapture capture = pendingCapture.Value;
        _capture(capture.TopLevel, capture.HitElement);
    }

    private readonly record struct PendingCapture(TopLevel TopLevel, IInputElement HitElement);
}
#endif
