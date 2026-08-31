using System.Diagnostics;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Tracks thread CPU counters and reports the busiest thread in each process sample.</summary>
internal sealed class ProcessThreadCPUTracker
{
    private readonly Dictionary<ProcessThreadInstanceKey, ProcessThreadCPUHistory> _history = [];
    private readonly List<ProcessThreadInstanceKey> _staleKeys = [];
    private int _generation;

    /// <summary>Updates thread baselines and returns the highest single-thread utilization.</summary>
    public double Update(ReadOnlySpan<SystemThreadCPUSample> samples, long sampleTimestamp)
    {
        int generation = NextGeneration();
        double maximumCPUPercent = 0;
        for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
        {
            SystemThreadCPUSample sample = samples[sampleIndex];
            if (sample.ThreadID <= 0) continue;

            ProcessThreadInstanceKey key = new(sample.ThreadID, sample.CreationTimeTicks);
            if (_history.TryGetValue(key, out ProcessThreadCPUHistory previous))
            {
                double cpuPercent = CalculateCPUPercent(
                    previous.TotalProcessorTicks,
                    previous.SampleTimestamp,
                    sample.TotalProcessorTicks,
                    sampleTimestamp);
                maximumCPUPercent = Math.Max(maximumCPUPercent, cpuPercent);
            }

            _history[key] = new ProcessThreadCPUHistory(
                sample.TotalProcessorTicks,
                sampleTimestamp,
                generation);
        }

        _staleKeys.Clear();
        foreach (KeyValuePair<ProcessThreadInstanceKey, ProcessThreadCPUHistory> pair in _history)
        {
            if (pair.Value.LastSeenGeneration != generation)
                _staleKeys.Add(pair.Key);
        }

        for (int staleIndex = 0; staleIndex < _staleKeys.Count; staleIndex++)
            _history.Remove(_staleKeys[staleIndex]);

        return maximumCPUPercent;
    }

    internal static double CalculateCPUPercent(
        long previousProcessorTicks,
        long previousTimestamp,
        long totalProcessorTicks,
        long sampleTimestamp)
    {
        if (sampleTimestamp <= previousTimestamp
            || totalProcessorTicks < previousProcessorTicks)
            return 0;

        double elapsedSeconds = (sampleTimestamp - previousTimestamp)
                                / (double)Stopwatch.Frequency;
        long processorTickDelta = totalProcessorTicks - previousProcessorTicks;
        double processorSeconds = processorTickDelta / (double)TimeSpan.TicksPerSecond;
        double cpuPercent = processorSeconds / elapsedSeconds * 100;
        return Math.Clamp(cpuPercent, min: 0, max: 100);
    }

    private int NextGeneration()
    {
        if (_generation < int.MaxValue)
        {
            _generation++;
            return _generation;
        }

        _history.Clear();
        _generation = 1;
        return _generation;
    }

    private readonly record struct ProcessThreadInstanceKey(long ThreadID, long CreationTimeTicks);

    private readonly record struct ProcessThreadCPUHistory(
        long TotalProcessorTicks,
        long SampleTimestamp,
        int LastSeenGeneration);
}

internal readonly record struct SystemThreadCPUSample(
    long ThreadID,
    long CreationTimeTicks,
    long TotalProcessorTicks);
