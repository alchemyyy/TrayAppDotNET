using System.Diagnostics;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>One raw Actual Frequency counter pair from the Processor Information _Total instance.</summary>
/// <param name="WeightedBusyTime">64-bit numerator: busy time weighted by the measured frequency in megahertz.</param>
/// <param name="BusyTime">32-bit wrapping denominator: busy time in the counter's native units.</param>
internal readonly record struct ProcessorFrequencyReading(
    long WeightedBusyTime,
    uint BusyTime);

/// <summary>Turns raw _Total Actual Frequency counter pairs into busy-time-qualified frequencies.</summary>
/// <remarks>
/// Windows updates the weighted numerator and the busy-time base at different moments, so a window that holds
/// only microseconds of busy time can report several times the real ratio. Measured per logical processor on a
/// Threadripper 9960X at a 100 ms interval, such windows reached a 50 GHz reading, while windows backed by at
/// least 20 ms of busy time never exceeded 134.2% of base speed. The _Total base is the mean of the logical
/// processor bases, so the same floor applies to it, and deltas accumulate across collections until it is met.
/// </remarks>
internal sealed class ProcessorFrequencyAccumulator
{
    // Busy time a window must accumulate before its ratio is trusted, chosen from the measurement above
    public const double MinimumBusySeconds = 0.020;

    private long _previousWeightedBusyTime;
    private uint _previousBusyTime;
    private uint _previousElapsedTime;
    private long _previousTimestamp;
    private long _pendingWeightedBusyTime;
    private ulong _pendingBusyTime;
    private double _secondsPerCounterUnit;
    private bool _hasBaseline;

    /// <summary>Gets the most recent qualified frequency in megahertz, or zero before the first one.</summary>
    public double Megahertz { get; private set; }

    /// <summary>Discards all baselines so the next update only records fresh counter values.</summary>
    public void Reset()
    {
        _hasBaseline = false;
        _pendingWeightedBusyTime = 0;
        _pendingBusyTime = 0;
        _secondsPerCounterUnit = 0;
        Megahertz = 0;
    }

    /// <summary>Folds one collection into the pending window.</summary>
    /// <param name="reading">The _Total Actual Frequency counter pair from one collection.</param>
    /// <param name="elapsedTime">32-bit wrapping elapsed-time base in the same units as the busy time.</param>
    /// <param name="timestamp">Stopwatch timestamp taken when the collection completed.</param>
    /// <returns>True when the pending window qualified and produced a new frequency in this collection.</returns>
    public bool Update(
        ProcessorFrequencyReading reading,
        uint elapsedTime,
        long timestamp)
    {
        if (!_hasBaseline)
        {
            StoreBaseline(reading, elapsedTime, timestamp);
            return false;
        }

        UpdateCounterUnitDuration(elapsedTime, timestamp);
        long weightedDelta = reading.WeightedBusyTime - _previousWeightedBusyTime;
        // NOTE: the busy base is a 32-bit counter that wraps, so the delta is taken modulo 2^32
        uint busyDelta = unchecked(reading.BusyTime - _previousBusyTime);
        _previousWeightedBusyTime = reading.WeightedBusyTime;
        _previousBusyTime = reading.BusyTime;
        if (weightedDelta < 0 || weightedDelta > long.MaxValue - _pendingWeightedBusyTime)
        {
            // A numerator that moves backwards means the counter restarted, so the window restarts too
            _pendingWeightedBusyTime = 0;
            _pendingBusyTime = 0;
            return false;
        }

        _pendingWeightedBusyTime += weightedDelta;
        _pendingBusyTime += busyDelta;
        if (_pendingBusyTime == 0
            || _secondsPerCounterUnit <= 0
            || _pendingBusyTime * _secondsPerCounterUnit < MinimumBusySeconds)
            return false;

        Megahertz = _pendingWeightedBusyTime / (double)_pendingBusyTime;
        _pendingWeightedBusyTime = 0;
        _pendingBusyTime = 0;
        return true;
    }

    private void StoreBaseline(
        ProcessorFrequencyReading reading,
        uint elapsedTime,
        long timestamp)
    {
        _previousWeightedBusyTime = reading.WeightedBusyTime;
        _previousBusyTime = reading.BusyTime;
        _previousElapsedTime = elapsedTime;
        _previousTimestamp = timestamp;
        _hasBaseline = true;
    }

    private void UpdateCounterUnitDuration(uint elapsedTime, long timestamp)
    {
        // NOTE: the elapsed base is a 32-bit counter that wraps, so the delta is taken modulo 2^32
        uint elapsedUnits = unchecked(elapsedTime - _previousElapsedTime);
        long elapsedTicks = timestamp - _previousTimestamp;
        _previousElapsedTime = elapsedTime;
        _previousTimestamp = timestamp;
        if (elapsedUnits == 0 || elapsedTicks <= 0) return;

        _secondsPerCounterUnit = elapsedTicks / (double)Stopwatch.Frequency / elapsedUnits;
    }
}
