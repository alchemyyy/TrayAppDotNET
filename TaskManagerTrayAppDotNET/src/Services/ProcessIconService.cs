using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    private const uint MultithreadedApartment = 0;
    private const int RPCChangedMode = unchecked((int)0x80010106);

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

        IconCacheEntry entry = new()
        {
            State = IconCacheState.Pending,
            LastAccessSequence = NextAccessSequence()
        };
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
            static (ProcessIconService service) => service.ProcessRequests(),
            this,
            preferLocal: false);
    }

    private void ProcessRequests()
    {
        int initializationResult = CoInitializeEx(IntPtr.Zero, MultithreadedApartment);
        bool uninitializeCOM = initializationResult >= 0;
        if (initializationResult < 0 && initializationResult != RPCChangedMode)
            TADNLog.Log($"ProcessIconService.CoInitializeEx failed: 0x{initializationResult:X8}");

        try
        {
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
        finally
        {
            if (uninitializeCOM) CoUninitialize();
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
        {
            return false;
        }

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
            foreach (KeyValuePair<ProcessIconSource, IconCacheEntry> pair in _entries)
            {
                IconCacheEntry candidate = pair.Value;
                if (candidate.State == IconCacheState.Pending) continue;
                if (oldestEntry != null && candidate.LastAccessSequence >= oldestEntry.LastAccessSequence) continue;

                oldestSource = pair.Key;
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
        if (!string.IsNullOrWhiteSpace(source.ApplicationUserModelID))
        {
            IconPixels? packagedIcon = ExtractFromShell(source.ApplicationUserModelID, isApplicationID: true);
            if (packagedIcon.HasValue) return packagedIcon;
        }

        if (string.IsNullOrWhiteSpace(source.ExecutablePath)) return null;

        IconPixels? shellIcon = ExtractFromShell(source.ExecutablePath, isApplicationID: false);
        return shellIcon ?? ExtractFromExecutable(source.ExecutablePath);
    }

    private static IconPixels? ExtractFromShell(string identity, bool isApplicationID)
    {
        IntPtr factoryPointer = IntPtr.Zero;
        try
        {
            int result = isApplicationID
                ? IconExtraction.SHCreateItemInKnownFolder(
                    in IconExtraction.AppsFolderID,
                    IconExtraction.KF_FLAG_DONT_VERIFY,
                    identity,
                    in IconExtraction.ShellItemImageFactoryIid,
                    out factoryPointer)
                : IconExtraction.SHCreateItemFromParsingName(
                    identity,
                    IntPtr.Zero,
                    in IconExtraction.ShellItemImageFactoryIid,
                    out factoryPointer);
            if (result < 0 || factoryPointer == IntPtr.Zero) return null;

            IntPtr bitmapHandle = IntPtr.Zero;
            try
            {
                using IconExtraction.ShellImageFactory factory = new(factoryPointer);
                factoryPointer = IntPtr.Zero;
                IconExtraction.SIZE size = new() { cx = IconPixelSize, cy = IconPixelSize };
                IconExtraction.SIIGBF flags = IconExtraction.SIIGBF.SIIGBF_ICONONLY
                                               | IconExtraction.SIIGBF.SIIGBF_RESIZETOFIT;
                result = factory.GetImage(size, flags, out bitmapHandle);
                return result < 0 || bitmapHandle == IntPtr.Zero
                    ? null
                    : ReadBitmapPixels(bitmapHandle);
            }
            finally
            {
                if (bitmapHandle != IntPtr.Zero) IconExtraction.DeleteObject(bitmapHandle);
            }
        }
        finally
        {
            if (factoryPointer != IntPtr.Zero) Marshal.Release(factoryPointer);
        }
    }

    private static IconPixels? ExtractFromExecutable(string executablePath)
    {
        IntPtr largeIconHandle = IntPtr.Zero;
        IntPtr smallIconHandle = IntPtr.Zero;
        try
        {
            uint iconSizes = ((uint)IconPixelSize << 16) | IconPixelSize;
            int result = IconExtraction.SHDefExtractIconW(
                executablePath,
                0,
                0,
                out largeIconHandle,
                out smallIconHandle,
                iconSizes);
            if (result < 0) return null;

            IntPtr selectedIconHandle = largeIconHandle != IntPtr.Zero ? largeIconHandle : smallIconHandle;
            return selectedIconHandle == IntPtr.Zero ? null : ReadIconPixels(selectedIconHandle);
        }
        finally
        {
            if (largeIconHandle != IntPtr.Zero) User32.DestroyIcon(largeIconHandle);
            if (smallIconHandle != IntPtr.Zero) User32.DestroyIcon(smallIconHandle);
        }
    }

    private static IconPixels? ReadIconPixels(IntPtr iconHandle)
    {
        if (!IconExtraction.GetIconInfo(iconHandle, out IconExtraction.ICONINFO iconInfo)) return null;

        try
        {
            return iconInfo.hbmColor == IntPtr.Zero ? null : ReadBitmapPixels(iconInfo.hbmColor);
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) IconExtraction.DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) IconExtraction.DeleteObject(iconInfo.hbmMask);
        }
    }

    private static IconPixels? ReadBitmapPixels(IntPtr bitmapHandle)
    {
        if (IconExtraction.GetObject(
                bitmapHandle,
                Marshal.SizeOf<IconExtraction.BITMAP>(),
                out IconExtraction.BITMAP bitmapInfo) == 0
            || bitmapInfo.bmWidth <= 0
            || bitmapInfo.bmHeight == 0)
        {
            return null;
        }

        int width = bitmapInfo.bmWidth;
        int height = Math.Abs(bitmapInfo.bmHeight);
        if (width > MaximumExtractedIconDimension || height > MaximumExtractedIconDimension) return null;

        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        GCHandle pixelsHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        IntPtr deviceContext = IntPtr.Zero;
        try
        {
            IconExtraction.BITMAPINFO requestedFormat = new()
            {
                bmiHeader = new IconExtraction.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<IconExtraction.BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = IconExtraction.BI_RGB,
                    biSizeImage = (uint)pixels.Length
                }
            };

            deviceContext = IconExtraction.GetDC(IntPtr.Zero);
            if (deviceContext == IntPtr.Zero) return null;

            int copiedLineCount = IconExtraction.GetDIBits(
                deviceContext,
                bitmapHandle,
                0,
                (uint)height,
                pixelsHandle.AddrOfPinnedObject(),
                ref requestedFormat,
                IconExtraction.DIB_RGB_COLORS);
            return copiedLineCount == 0 ? null : new IconPixels(width, height, pixels);
        }
        finally
        {
            if (deviceContext != IntPtr.Zero) _ = IconExtraction.ReleaseDC(IntPtr.Zero, deviceContext);
            pixelsHandle.Free();
        }
    }

    private static WriteableBitmap CreateBitmap(IconPixels pixels)
    {
        WriteableBitmap bitmap = new(
            new PixelSize(pixels.Width, pixels.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        try
        {
            using ILockedFramebuffer framebuffer = bitmap.Lock();
            int sourceStride = pixels.Width * 4;
            for (int rowIndex = 0; rowIndex < pixels.Height; rowIndex++)
            {
                Marshal.Copy(
                    pixels.BGRA,
                    rowIndex * sourceStride,
                    IntPtr.Add(framebuffer.Address, rowIndex * framebuffer.RowBytes),
                    Math.Min(sourceStride, framebuffer.RowBytes));
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

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
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

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

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint initializationMode);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    private readonly record struct IconPixels(int Width, int Height, byte[] BGRA);

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
