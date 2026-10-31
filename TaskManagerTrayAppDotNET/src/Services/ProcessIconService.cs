using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// Resolves only requested process icons off the UI thread and keeps one bounded bitmap per shell identity.
/// </summary>
internal sealed class ProcessIconService : IDisposable
{
    private const int IconPixelSize = 32;
    private const int MaximumExtractedIconDimension = 256;
    private const int MaximumCachedIconCount = 256;
    private const int MaximumPendingRequestCount = 64;
    private const int MaximumResultsPerDispatcherPass = 16;

    private readonly Lock _workerGate = new();
    private readonly Queue<ProcessIconSource> _pendingRequests = new(MaximumPendingRequestCount);
    private readonly Queue<IconLoadResult> _completedResults = new(MaximumPendingRequestCount);

    private readonly Dictionary<ProcessIconSource, IconCacheEntry> _entries = new(
        MaximumCachedIconCount,
        ProcessIconSourceComparer.Instance);

    private readonly Action _applyCompletedResults;
    private long _accessSequence;
    private int _pendingEntryCount;
    private int _completedEntryCount;
    private int _disposed;
    private bool _workerScheduled;
    private bool _completionCallbackScheduled;

    public ProcessIconService() => _applyCompletedResults = ApplyCompletedResults;

    public event Action? IconsChanged;

    /// <summary>Returns a cached icon or schedules one non-blocking resolution for the identity.</summary>
    public IImage? GetOrQueue(ProcessIconSource source)
    {
        if (Volatile.Read(ref _disposed) != 0 || !source.IsAvailable) return null;

        if (_entries.TryGetValue(source, out IconCacheEntry? existingEntry))
        {
            existingEntry.LastAccessSequence = NextAccessSequence();
            return existingEntry.State == IconCacheState.Ready ? existingEntry.Image : null;
        }

        if (_pendingEntryCount >= MaximumPendingRequestCount) return null;

        IconCacheEntry entry = new() { State = IconCacheState.Pending, LastAccessSequence = NextAccessSequence() };
        _entries.Add(source, entry);
        _pendingEntryCount++;
        QueueRequest(source);
        return null;
    }

    private long NextAccessSequence()
    {
        long next = unchecked(_accessSequence + 1);
        if (next != 0)
        {
            _accessSequence = next;
            return next;
        }

        long sequence = 1;
        foreach (IconCacheEntry entry in _entries.Values)
        {
            entry.LastAccessSequence = sequence;
            sequence++;
        }

        _accessSequence = sequence;
        return sequence;
    }

    private void QueueRequest(ProcessIconSource source)
    {
        bool scheduleWorker = false;
        lock (_workerGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            _pendingRequests.Enqueue(source);
            if (!_workerScheduled)
            {
                _workerScheduled = true;
                scheduleWorker = true;
            }
        }

        if (!scheduleWorker) return;
        ThreadPool.UnsafeQueueUserWorkItem(
            static service => service.ProcessRequests(),
            this,
            preferLocal: false);
    }

    private void ProcessRequests()
    {
        using ComApartmentScope apartment = ComApartmentScope.Enter(Ole32.COINIT_MULTITHREADED);
        if (!apartment.IsUsable)
            TADNLog.Log($"ProcessIconService.CoInitializeEx failed: 0x{apartment.HResult:X8}");

        while (TryTakeRequest(out ProcessIconSource source))
        {
            IconPixels? pixels = null;
            try
            {
                pixels = ExtractIcon(source);
            }
            catch (Exception exception)
            {
                TADNLog.Log($"ProcessIconService extraction failed for '{source}': {exception}");
            }

            QueueCompletedResult(new IconLoadResult(source, pixels));
        }
    }

    private bool TryTakeRequest(out ProcessIconSource source)
    {
        lock (_workerGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || _pendingRequests.Count == 0)
            {
                _workerScheduled = false;
                source = default;
                return false;
            }

            source = _pendingRequests.Dequeue();
            return true;
        }
    }

    private void QueueCompletedResult(IconLoadResult result)
    {
        bool scheduleCallback = false;
        lock (_workerGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            _completedResults.Enqueue(result);
            if (!_completionCallbackScheduled)
            {
                _completionCallbackScheduled = true;
                scheduleCallback = true;
            }
        }

        if (scheduleCallback)
            Dispatcher.UIThread.Post(_applyCompletedResults, DispatcherPriority.Background);
    }

    private void ApplyCompletedResults()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            ClearCompletedResults();
            return;
        }

        bool iconsChanged = false;
        int appliedCount = 0;
        while (appliedCount < MaximumResultsPerDispatcherPass
               && TryTakeCompletedResult(out IconLoadResult result))
        {
            if (ApplyResult(result)) iconsChanged = true;
            appliedCount++;
        }

        if (iconsChanged)
        {
            try
            {
                IconsChanged?.Invoke();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"ProcessIconService.IconsChanged: {exception}");
            }
        }

        bool scheduleNextPass;
        lock (_workerGate)
        {
            scheduleNextPass = _completedResults.Count > 0 && Volatile.Read(ref _disposed) == 0;
            if (!scheduleNextPass) _completionCallbackScheduled = false;
        }

        if (scheduleNextPass)
            Dispatcher.UIThread.Post(_applyCompletedResults, DispatcherPriority.Background);
    }

    private bool TryTakeCompletedResult(out IconLoadResult result)
    {
        lock (_workerGate)
        {
            if (_completedResults.Count == 0)
            {
                result = default;
                return false;
            }

            result = _completedResults.Dequeue();
            return true;
        }
    }

    private bool ApplyResult(IconLoadResult result)
    {
        if (!_entries.TryGetValue(result.Source, out IconCacheEntry? entry)
            || entry.State != IconCacheState.Pending)
            return false;

        _pendingEntryCount--;
        entry.State = IconCacheState.Unavailable;
        bool iconBecameReady = false;
        if (result.Pixels.HasValue)
        {
            try
            {
                entry.Image = CreateBitmap(result.Pixels.Value);
                entry.State = IconCacheState.Ready;
                iconBecameReady = true;
            }
            catch (Exception exception)
            {
                TADNLog.Log($"ProcessIconService bitmap creation failed for '{result.Source}': {exception}");
            }
        }

        _completedEntryCount++;
        TrimCompletedCache();
        return iconBecameReady;
    }

    private void TrimCompletedCache()
    {
        while (_completedEntryCount > MaximumCachedIconCount)
        {
            ProcessIconSource oldestSource = default;
            IconCacheEntry? oldestEntry = null;
            foreach ((ProcessIconSource source, IconCacheEntry candidate) in _entries)
            {
                if (candidate.State == IconCacheState.Pending) continue;
                if (oldestEntry != null && candidate.LastAccessSequence >= oldestEntry.LastAccessSequence) continue;

                oldestSource = source;
                oldestEntry = candidate;
            }

            if (oldestEntry == null) return;

            _entries.Remove(oldestSource);
            _completedEntryCount--;
            DisposeImage(oldestEntry.Image);
        }
    }

    private static IconPixels? ExtractIcon(ProcessIconSource source)
    {
        const IconExtraction.SIIGBF shellFlags = IconExtraction.SIIGBF.SIIGBF_ICONONLY
                                                 | IconExtraction.SIIGBF.SIIGBF_RESIZETOFIT;
        if (!string.IsNullOrWhiteSpace(source.ApplicationUserModelID))
        {
            IconPixels? packagedIcon = IconExtraction.TryExtractShellImage(
                source.ApplicationUserModelID,
                isApplicationID: true,
                IconPixelSize,
                shellFlags,
                MaximumExtractedIconDimension);
            if (packagedIcon.HasValue) return packagedIcon;
        }

        if (string.IsNullOrWhiteSpace(source.ExecutablePath)) return null;

        return IconExtraction.TryExtractShellImage(
                   source.ExecutablePath,
                   isApplicationID: false,
                   IconPixelSize,
                   shellFlags,
                   MaximumExtractedIconDimension)
               ?? IconExtraction.TryExtractDefaultIcon(
                   source.ExecutablePath,
                   iconIndex: 0,
                   IconPixelSize,
                   MaximumExtractedIconDimension);
    }

    private static WriteableBitmap CreateBitmap(IconPixels pixels) => IconPixelsBitmap.ToBitmap(pixels);

    private void ClearCompletedResults()
    {
        lock (_workerGate)
        {
            _completedResults.Clear();
            _completionCallbackScheduled = false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

        IconsChanged = null;
        lock (_workerGate)
        {
            _pendingRequests.Clear();
            _completedResults.Clear();
            _completionCallbackScheduled = false;
        }

        foreach (IconCacheEntry entry in _entries.Values)
            DisposeImage(entry.Image);
        _entries.Clear();
        _pendingEntryCount = 0;
        _completedEntryCount = 0;
    }

    private static void DisposeImage(IImage? image)
    {
        if (image is not IDisposable disposable) return;

        try
        {
            disposable.Dispose();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"ProcessIconService bitmap disposal failed: {exception.Message}");
        }
    }

    private readonly record struct IconLoadResult(ProcessIconSource Source, IconPixels? Pixels);

    private enum IconCacheState : byte
    {
        Pending,
        Ready,
        Unavailable
    }

    private sealed class IconCacheEntry
    {
        public IImage? Image;
        public long LastAccessSequence;
        public IconCacheState State;
    }
}

/// <summary>Compares Windows process icon identities without path or AUMID casing differences.</summary>
internal sealed class ProcessIconSourceComparer : IEqualityComparer<ProcessIconSource>
{
    public static readonly ProcessIconSourceComparer Instance = new();

    private ProcessIconSourceComparer()
    {
    }

    public bool Equals(ProcessIconSource left, ProcessIconSource right) =>
        string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            left.ApplicationUserModelID,
            right.ApplicationUserModelID,
            StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(ProcessIconSource source)
    {
        HashCode hashCode = new();
        hashCode.Add(source.ExecutablePath, StringComparer.OrdinalIgnoreCase);
        hashCode.Add(source.ApplicationUserModelID, StringComparer.OrdinalIgnoreCase);
        return hashCode.ToHashCode();
    }
}
