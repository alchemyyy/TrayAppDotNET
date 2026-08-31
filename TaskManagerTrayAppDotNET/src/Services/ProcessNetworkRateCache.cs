using TaskManagerTrayAppDotNET.Models;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Retains the latest valid network rate across process snapshot schemas.</summary>
internal sealed class ProcessNetworkRateCache
{
    private readonly Dictionary<int, CacheEntry> _entries = new(1_024);
    private readonly List<int> _staleProcessIDs = new(256);

    /// <summary>Marks a matching process instance as present in the current process walk.</summary>
    public void MarkSeen(ProcessInstanceKey instanceKey, int generation)
    {
        if (!_entries.TryGetValue(instanceKey.ProcessID, out CacheEntry entry)) return;
        if (entry.CreationTimeTicks != instanceKey.CreationTimeTicks)
        {
            _entries.Remove(instanceKey.ProcessID);
            return;
        }

        _entries[instanceKey.ProcessID] = entry with { LastSeenGeneration = generation };
    }

    /// <summary>Reads the latest rate only when the process instance still matches.</summary>
    public bool TryGet(ProcessInstanceKey instanceKey, out double bytesPerSecond)
    {
        if (_entries.TryGetValue(instanceKey.ProcessID, out CacheEntry entry)
            && entry.CreationTimeTicks == instanceKey.CreationTimeTicks)
        {
            bytesPerSecond = entry.BytesPerSecond;
            return true;
        }

        _entries.Remove(instanceKey.ProcessID);
        bytesPerSecond = 0;
        return false;
    }

    /// <summary>Stores the latest valid rate for one process instance.</summary>
    public void Set(ProcessInstanceKey instanceKey, double bytesPerSecond, int generation)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond < 0) return;

        _entries[instanceKey.ProcessID] = new CacheEntry(
            instanceKey.CreationTimeTicks,
            bytesPerSecond,
            generation);
    }

    /// <summary>Removes cached rates for processes absent from the latest process walk.</summary>
    public void RemoveStale(int generation)
    {
        _staleProcessIDs.Clear();
        foreach (KeyValuePair<int, CacheEntry> pair in _entries)
        {
            if (pair.Value.LastSeenGeneration != generation)
                _staleProcessIDs.Add(pair.Key);
        }

        for (int staleIndex = 0; staleIndex < _staleProcessIDs.Count; staleIndex++)
            _entries.Remove(_staleProcessIDs[staleIndex]);
    }

    /// <summary>Clears all retained process rates.</summary>
    public void Clear() => _entries.Clear();

    private readonly record struct CacheEntry(
        long CreationTimeTicks,
        double BytesPerSecond,
        int LastSeenGeneration);
}
