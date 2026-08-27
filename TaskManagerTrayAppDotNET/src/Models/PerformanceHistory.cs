using System.Diagnostics;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Stores a fixed-duration normalized utilization history without per-sample allocation.</summary>
internal sealed class PerformanceHistory
{
    public const int DefaultCapacity = 128;
    public const int DefaultWindowSeconds = 60;

    private readonly PerformanceHistorySample[] _samples;
    private readonly long _windowDurationTicks;
    private int _oldestIndex;
    private long _currentTimestamp;
    private bool _hasCurrentTimestamp;

    public PerformanceHistory(int capacity = DefaultCapacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new PerformanceHistorySample[capacity];
        _windowDurationTicks = checked(Stopwatch.Frequency * DefaultWindowSeconds);
    }

    public int Capacity => _samples.Length;
    public int Count { get; private set; }
    public long CurrentTimestamp => _currentTimestamp;
    public long WindowDurationTicks => _windowDurationTicks;

    /// <summary>Adds one finite percentage, clamped to the graph's normalized range.</summary>
    public void Add(long timestamp, double value)
    {
        double normalizedValue = double.IsFinite(value)
            ? Math.Clamp(value, 0, 100)
            : 0;
        AdvanceTo(timestamp);
        if (Count > 0)
        {
            int newestIndex = PhysicalIndex(Count - 1);
            long newestTimestamp = _samples[newestIndex].Timestamp;
            if (timestamp == newestTimestamp)
            {
                _samples[newestIndex] = new PerformanceHistorySample(timestamp, normalizedValue);
                return;
            }
        }

        if (Count == _samples.Length)
        {
            _oldestIndex = (_oldestIndex + 1) % _samples.Length;
            Count--;
        }

        int insertionIndex = PhysicalIndex(Count);
        _samples[insertionIndex] = new PerformanceHistorySample(timestamp, normalizedValue);
        Count++;
    }

    /// <summary>Moves the history window forward even when the current sample is unavailable.</summary>
    public void AdvanceTo(long timestamp)
    {
        if (_hasCurrentTimestamp && timestamp < _currentTimestamp)
            Clear();

        _currentTimestamp = timestamp;
        _hasCurrentTimestamp = true;
        long cutoffTimestamp = timestamp >= _windowDurationTicks
            ? timestamp - _windowDurationTicks
            : long.MinValue;
        while (Count > 0 && _samples[_oldestIndex].Timestamp < cutoffTimestamp)
        {
            _oldestIndex = (_oldestIndex + 1) % _samples.Length;
            Count--;
        }
    }

    /// <summary>Removes every sample so a future trace starts at the current wall-clock interval.</summary>
    public void Clear()
    {
        Array.Clear(_samples);
        _oldestIndex = 0;
        _currentTimestamp = 0;
        _hasCurrentTimestamp = false;
        Count = 0;
    }

    /// <summary>Gets a sample by chronological index, oldest first.</summary>
    public double GetChronological(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));

        return _samples[PhysicalIndex(index)].Value;
    }

    /// <summary>Gets the monotonic sample timestamp by chronological index.</summary>
    public long GetTimestampChronological(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _samples[PhysicalIndex(index)].Timestamp;
    }

    private int PhysicalIndex(int chronologicalIndex) =>
        (_oldestIndex + chronologicalIndex) % _samples.Length;

    private readonly record struct PerformanceHistorySample(long Timestamp, double Value);
}
