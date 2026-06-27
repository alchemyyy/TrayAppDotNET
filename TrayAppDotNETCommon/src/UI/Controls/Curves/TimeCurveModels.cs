namespace TrayAppDotNETCommon.UI.Controls.Curves;

/// <summary>
/// Mutable point on a normalized 24-hour curve.
/// Time is normalized to [0, 1], where 0 is midnight and 1 is the next midnight.
/// Value is intentionally unitless so applications can map it to brightness, volume, fan speed, or offsets.
/// </summary>
public interface ITimeCurvePoint
{
    double Time { get; set; }
    double Value { get; set; }
}

public sealed class TimeCurvePoint : ITimeCurvePoint
{
    public TimeCurvePoint()
    {
    }

    public TimeCurvePoint(double time, double value)
    {
        Time = time;
        Value = value;
    }

    public double Time { get; set; }
    public double Value { get; set; }
}

/// <summary>
/// Display axis mapping for a normalized curve value.
/// Storage remains [0, 100]; DisplayMinimum/DisplayMaximum allow an editor to show offset modes such as -100…+100.
/// </summary>
public readonly record struct TimeCurveValueAxis(
    double StorageMinimum,
    double StorageMaximum,
    double DisplayMinimum,
    double DisplayMaximum)
{
    public static readonly TimeCurveValueAxis Percent = new(0.0, 100.0, 0.0, 100.0);
    public static readonly TimeCurveValueAxis CenteredOffset = new(0.0, 100.0, -100.0, 100.0);

    public double StorageRange => Math.Max(0.001, StorageMaximum - StorageMinimum);
    public double DisplayRange => Math.Max(0.001, DisplayMaximum - DisplayMinimum);

    public double ToDisplay(double storageValue) =>
        DisplayMinimum + ((storageValue - StorageMinimum) / StorageRange * DisplayRange);

    public double ToStorage(double displayValue) =>
        StorageMinimum + ((displayValue - DisplayMinimum) / DisplayRange * StorageRange);
}

/// <summary>
/// Wrap-aware interval on a normalized day.
/// Start greater than End means the interval crosses midnight.
/// </summary>
public readonly record struct NormalizedTimeRange(double Start, double End)
{
    public bool IsEmpty => Start == End;

    public bool Contains(double time)
    {
        if (IsEmpty) return false;

        double t = Math.Clamp(time, 0.0, 1.0);
        double start = Math.Clamp(Start, 0.0, 1.0);
        double end = Math.Clamp(End, 0.0, 1.0);

        return start <= end
            ? t >= start && t <= end
            : t >= start || t <= end;
    }

    public IEnumerable<(double Start, double End)> Segments()
    {
        double start = Math.Clamp(Start, 0.0, 1.0);
        double end = Math.Clamp(End, 0.0, 1.0);
        if (start == end) yield break;

        if (start <= end)
        {
            yield return (start, end);
            yield break;
        }

        yield return (start, 1.0);
        yield return (0.0, end);
    }
}
