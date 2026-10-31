namespace TrayAppDotNETCommon.UI.Controls.Curves;

/// <summary>
/// Wrap-aware interval on a normalized day ([0, 1], 0 = midnight). Start greater than End means the interval crosses
/// midnight; Start equal to End is empty.
/// </summary>
public readonly record struct NormalizedTimeRange(double Start, double End)
{
    public bool IsEmpty => Start == End;

    public bool Contains(double time)
    {
        if (IsEmpty) return false;

        return Start <= End
            ? time >= Start && time <= End
            : time >= Start || time <= End;
    }
}
