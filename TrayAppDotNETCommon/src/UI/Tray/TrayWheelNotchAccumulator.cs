namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>
/// Turns raw wheel deltas into whole notches. A standard wheel reports 120 per detent; a high-resolution wheel
/// reports fractions of it, which accumulate until they make a notch. Turning the other way drops the remainder, as
/// the Windows wheel guidance recommends.
/// </summary>
internal sealed class TrayWheelNotchAccumulator
{
    public const int WheelDeltaPerNotch = 120;

    private int _remainder;

    /// <summary>Adds a raw wheel delta and returns the whole notches it completes, signed like the delta.</summary>
    public int Add(int wheelDelta)
    {
        if (wheelDelta == 0) return 0;
        if (Math.Sign(wheelDelta) != Math.Sign(_remainder)) _remainder = 0;

        int total = _remainder + wheelDelta;
        int notches = total / WheelDeltaPerNotch;
        _remainder = total - notches * WheelDeltaPerNotch;
        return notches;
    }

    public void Reset() => _remainder = 0;
}
