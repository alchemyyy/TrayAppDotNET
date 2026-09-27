using System.Diagnostics;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>One logical processor's raw % Processor Performance counter pair.</summary>
/// <param name="ProcessorKey">Stable processor identity derived from the "group,number" instance name.</param>
/// <param name="WeightedBusyTime">64-bit numerator: busy time weighted by the performance percentage.</param>
/// <param name="BusyTime">32-bit wrapping denominator: busy time in the counter's native units.</param>
internal readonly record struct ProcessorPerformanceReading(
    int ProcessorKey,
    long WeightedBusyTime,
    uint BusyTime);

/// <summary>Turns raw per-processor performance counters into busy-time-qualified effective speeds.</summary>
/// <remarks>
/// Windows updates the weighted numerator and the busy-time base of % Processor Performance at different
/// moments, so an interval that holds only microseconds of busy time can report several times the real
/// ratio. Measured on a Threadripper 9960X at a 100 ms interval, near-idle logical processors reported up
/// to 1190% (a 50 GHz reading), while estimates backed by at least 20 ms of busy time never exceeded 134.2%.
/// Each processor therefore accumulates deltas until it has enough busy time to be trusted.
/// </remarks>
internal sealed class ProcessorSpeedEstimator
{
    // Busy time a processor must accumulate before its ratio is trusted, chosen from the measurement above
    public const double MinimumBusySeconds = 0.020;

    private readonly Dictionary<int, ProcessorAccumulator> _accumulators = [];
    private uint _previousElapsedTime;
    private long _previousTimestamp;
    private double _secondsPerCounterUnit;
    private double _highestPerformancePercent;
    private bool _hasElapsedBaseline;

    /// <summary>Gets the most recent highest qualified performance percentage, or zero before the first one.</summary>
    public double HighestPerformancePercent => _highestPerformancePercent;

    /// <summary>Discards all baselines so the next update only records fresh counter values.</summary>
    public void Reset()
    {
        _accumulators.Clear();
        _hasElapsedBaseline = false;
        _previousElapsedTime = 0;
        _previousTimestamp = 0;
        _highestPerformancePercent = 0;
    }

    /// <summary>Folds one collection into the per-processor accumulators.</summary>
    /// <param name="readings">Logical processor counters from one collection.</param>
    /// <param name="elapsedTime">32-bit wrapping elapsed-time base in the same units as the busy time.</param>
    /// <param name="timestamp">Stopwatch timestamp taken when the collection completed.</param>
    /// <returns>True when at least one processor produced a qualified estimate in this collection.</returns>
    public bool Update(
        ReadOnlySpan<ProcessorPerformanceReading> readings,
        uint elapsedTime,
        long timestamp)
    {
        UpdateCounterUnitDuration(elapsedTime, timestamp);

        bool hasEstimate = false;
        double highestPerformancePercent = 0;
        for (int readingIndex = 0; readingIndex < readings.Length; readingIndex++)
        {
            ProcessorPerformanceReading reading = readings[readingIndex];
            if (!_accumulators.TryGetValue(reading.ProcessorKey, out ProcessorAccumulator? accumulator))
            {
                _accumulators[reading.ProcessorKey] = new ProcessorAccumulator(reading);
                continue;
            }

            if (!accumulator.TryAdd(reading)) continue;
            if (_secondsPerCounterUnit <= 0
                || accumulator.PendingBusyTime * _secondsPerCounterUnit < MinimumBusySeconds)
                continue;

            double performancePercent = accumulator.TakeEstimate();
            highestPerformancePercent = Math.Max(highestPerformancePercent, performancePercent);
            hasEstimate = true;
        }

        if (hasEstimate) _highestPerformancePercent = highestPerformancePercent;
        return hasEstimate;
    }

    private void UpdateCounterUnitDuration(uint elapsedTime, long timestamp)
    {
        if (!_hasElapsedBaseline)
        {
            _previousElapsedTime = elapsedTime;
            _previousTimestamp = timestamp;
            _hasElapsedBaseline = true;
            return;
        }

        // NOTE: the elapsed base is a 32-bit counter that wraps, so the delta is taken modulo 2^32
        uint elapsedUnits = unchecked(elapsedTime - _previousElapsedTime);
        long elapsedTicks = timestamp - _previousTimestamp;
        _previousElapsedTime = elapsedTime;
        _previousTimestamp = timestamp;
        if (elapsedUnits == 0 || elapsedTicks <= 0) return;

        _secondsPerCounterUnit = elapsedTicks / (double)Stopwatch.Frequency / elapsedUnits;
    }

    private sealed class ProcessorAccumulator(ProcessorPerformanceReading baseline)
    {
        private long _previousWeightedBusyTime = baseline.WeightedBusyTime;
        private uint _previousBusyTime = baseline.BusyTime;
        private long _pendingWeightedBusyTime;

        public ulong PendingBusyTime { get; private set; }

        /// <summary>Adds the delta since the previous reading, restarting after a counter reset or garbage delta.</summary>
        public bool TryAdd(ProcessorPerformanceReading reading)
        {
            long weightedDelta = reading.WeightedBusyTime - _previousWeightedBusyTime;
            // NOTE: the busy base is a 32-bit counter that wraps, so the delta is taken modulo 2^32
            uint busyDelta = unchecked(reading.BusyTime - _previousBusyTime);
            _previousWeightedBusyTime = reading.WeightedBusyTime;
            _previousBusyTime = reading.BusyTime;
            if (weightedDelta < 0 || weightedDelta > long.MaxValue - _pendingWeightedBusyTime)
            {
                _pendingWeightedBusyTime = 0;
                PendingBusyTime = 0;
                return false;
            }

            _pendingWeightedBusyTime += weightedDelta;
            PendingBusyTime += busyDelta;
            return PendingBusyTime > 0;
        }

        /// <summary>Returns the busy-time-weighted performance percentage and starts a new window.</summary>
        public double TakeEstimate()
        {
            double performancePercent = _pendingWeightedBusyTime / (double)PendingBusyTime;
            _pendingWeightedBusyTime = 0;
            PendingBusyTime = 0;
            return performancePercent;
        }
    }
}
