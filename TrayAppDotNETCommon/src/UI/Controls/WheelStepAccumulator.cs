namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>
/// Turns vertical wheel deltas into whole steps. A notched wheel reports ±1 per notch; precision touchpads report
/// fractions, which accumulate until they add up to a step. A purely horizontal event (Delta.Y == 0) yields no step,
/// so callers can leave it unhandled for a horizontal scroller. It is a mutable struct: keep it in a non-readonly
/// field, since a readonly field runs <see cref="Add"/> on a copy and loses the remainder.
/// </summary>
public struct WheelStepAccumulator
{
    private double _pending;

    /// <summary>Adds a vertical wheel delta and returns the whole steps it completes, positive for wheel up.</summary>
    public int Add(double deltaY)
    {
        if (deltaY == 0 || !double.IsFinite(deltaY)) return 0;

        // A reversed direction starts over instead of first paying back the opposite remainder
        if (_pending != 0 && Math.Sign(_pending) != Math.Sign(deltaY)) _pending = 0;
        _pending += deltaY;
        int steps = (int)Math.Truncate(_pending);
        _pending -= steps;
        return steps;
    }

    /// <summary>Drops a partial step, such as when the pointer leaves the control.</summary>
    public void Reset() => _pending = 0;
}
