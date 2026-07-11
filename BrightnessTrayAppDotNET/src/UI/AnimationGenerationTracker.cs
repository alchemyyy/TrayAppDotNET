namespace BrightnessTrayAppDotNET.UI;

/// <summary>
/// Rejects callbacks queued by retired animation runs without consuming a newer run's queued frame.
/// </summary>
internal sealed class AnimationGenerationTracker
{
    private long _currentGeneration;
    private long _queuedGeneration;

    /// <summary>Starts a new run and invalidates every callback from prior runs.</summary>
    public long Start()
    {
        _currentGeneration++;
        _queuedGeneration = 0;
        return _currentGeneration;
    }

    /// <summary>Invalidates the current run and every callback already queued for it.</summary>
    public void Invalidate()
    {
        _currentGeneration++;
        _queuedGeneration = 0;
    }

    /// <summary>Marks one frame as queued for the current run.</summary>
    public bool TryQueue(long generation)
    {
        if (generation != _currentGeneration || _queuedGeneration == generation) return false;
        _queuedGeneration = generation;
        return true;
    }

    /// <summary>
    /// Consumes a current callback. A stale callback cannot clear a newer run's queued-frame marker.
    /// </summary>
    public bool TryConsume(long generation)
    {
        if (generation != _currentGeneration || _queuedGeneration != generation) return false;
        _queuedGeneration = 0;
        return true;
    }
}
