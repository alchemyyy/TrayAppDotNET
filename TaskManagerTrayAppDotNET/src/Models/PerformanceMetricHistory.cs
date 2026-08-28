using System.Diagnostics;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>Stores a fixed-duration history of non-negative raw performance values.</summary>
internal sealed class PerformanceMetricHistory
{
    private const int SecondsPerMinute = 60;

    private readonly PerformanceMetricHistorySample[] _samples;
    private readonly long _windowDurationTicks;
    private int _oldestIndex;
    private long _currentTimestamp;
    private bool _hasCurrentTimestamp;

    public PerformanceMetricHistory(int historyLengthMinutes, int sampleIntervalMilliseconds)
    {
        int capacity = PerformanceSamplingSettings.CalculateMaximumHistoryCount(
            historyLengthMinutes,
            sampleIntervalMilliseconds);
        _samples = new PerformanceMetricHistorySample[capacity];
        int normalizedHistoryLength = PerformanceSamplingSettings.NormalizeHistoryLengthMinutes(
            historyLengthMinutes);
        _windowDurationTicks = checked(
            (long)Stopwatch.Frequency * normalizedHistoryLength * SecondsPerMinute);
    }

    public int Count { get; private set; }
    public long CurrentTimestamp => _currentTimestamp;
    public long WindowDurationTicks => _windowDurationTicks;

    /// <summary>Adds one finite raw value without normalizing it to a percentage.</summary>
    public void Add(long timestamp, double value)
    {
        double normalizedValue = double.IsFinite(value) ? Math.Max(0, value) : 0;
        AdvanceTo(timestamp);
        if (Count > 0)
        {
            int newestIndex = PhysicalIndex(Count - 1);
            if (_samples[newestIndex].Timestamp == timestamp)
            {
                _samples[newestIndex] = new PerformanceMetricHistorySample(
                    timestamp,
                    normalizedValue);
                return;
            }
        }

        if (Count == _samples.Length)
        {
            _oldestIndex = (_oldestIndex + 1) % _samples.Length;
            Count--;
        }

        int insertionIndex = PhysicalIndex(Count);
        _samples[insertionIndex] = new PerformanceMetricHistorySample(timestamp, normalizedValue);
        Count++;
    }

    /// <summary>Moves the history window forward when the current metric is unavailable.</summary>
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

    /// <summary>Removes every retained sample.</summary>
    public void Clear()
    {
        Array.Clear(_samples);
        _oldestIndex = 0;
        _currentTimestamp = 0;
        _hasCurrentTimestamp = false;
        Count = 0;
    }

    /// <summary>Gets a raw value by chronological index, oldest first.</summary>
    public double GetChronological(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _samples[PhysicalIndex(index)].Value;
    }

    /// <summary>Gets a sample timestamp by chronological index, oldest first.</summary>
    public long GetTimestampChronological(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _samples[PhysicalIndex(index)].Timestamp;
    }

    /// <summary>Returns the largest retained value, or zero when the history is empty.</summary>
    public double GetMaximumValue()
    {
        double maximumValue = 0;
        for (int sampleIndex = 0; sampleIndex < Count; sampleIndex++)
            maximumValue = Math.Max(maximumValue, GetChronological(sampleIndex));
        return maximumValue;
    }

    /// <summary>Finds the sample nearest a timestamp without allocating a projection.</summary>
    public bool TryGetNearest(long timestamp, out double value)
    {
        value = 0;
        if (Count == 0) return false;

        int bestIndex = 0;
        ulong bestDistance = TimestampDistance(GetTimestampChronological(0), timestamp);
        for (int sampleIndex = 1; sampleIndex < Count; sampleIndex++)
        {
            ulong distance = TimestampDistance(GetTimestampChronological(sampleIndex), timestamp);
            if (distance >= bestDistance) continue;

            bestIndex = sampleIndex;
            bestDistance = distance;
        }

        value = GetChronological(bestIndex);
        return true;
    }

    private int PhysicalIndex(int chronologicalIndex) =>
        (_oldestIndex + chronologicalIndex) % _samples.Length;

    private static ulong TimestampDistance(long left, long right) =>
        left >= right ? (ulong)(left - right) : (ulong)(right - left);

    private readonly record struct PerformanceMetricHistorySample(long Timestamp, double Value);
}
