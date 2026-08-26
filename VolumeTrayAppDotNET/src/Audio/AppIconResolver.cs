using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IAudioSessionControl = VolumeTrayAppDotNET.Interop.IAudioSessionControl;

namespace VolumeTrayAppDotNET.Audio;

// Resolves an audio session's process to a refcounted, cached Avalonia icon. The extraction chain
// uses the established resolution chain: system-sounds resource, app-supplied icon path, UWP AUMID shell
// image, then desktop PE/shell image.
internal static class AppIconResolver
{
    private const int IconSize = 48;
    private const string SystemSoundsDll = "audiosrv.dll";
    private const int SystemSoundsIconOrdinal = 203;
    private const string CortanaBadAumid = "MicrosoftWindows.Client.CBS_cw5n1h2txyewy!CortanaUI";
    private const string CortanaGoodAumid = "MicrosoftWindows.Client.CBS_cw5n1h2txyewy!PackageMetadata";
    private const string InvalidOrdinalMarker = ",-";

    private const int AlphaThreshold = 16;
    private const int MinOpaqueRun = 2;
    private const double MinPadRatio = 0.10;

    private const string KeySys = "sys";
    private const string KeyPe = "pe";
    private const string KeyShellUwp = "shell|uwp";
    private const string KeyShellDesktop = "shell|desktop";
    private const int MaxIdentityAliasesPerEntry = 8;

    private static readonly Lock s_cacheLock = new();
    private static readonly Dictionary<string, CacheEntry> s_byIdentity = new(StringComparer.Ordinal);
    private static readonly Dictionary<long, CacheEntry> s_byContent = [];
    private static readonly LinkedList<CacheEntry> s_lru = [];
    private static bool s_shutdown;

    public sealed class IconHandle : IDisposable
    {
        internal readonly CacheEntry Entry;
        private int _disposed;

        public IImage Icon => Entry.Bitmap;

        internal IconHandle(CacheEntry entry) => Entry = entry;

        /// <summary>Acquires an independent lease for a visual that may outlive the publishing session.</summary>
        internal IconHandle? TryClone()
        {
            lock (s_cacheLock)
            {
                if (Volatile.Read(ref _disposed) != 0 || s_shutdown || Entry.IsRetired) return null;
                Revive(Entry);
                return new IconHandle(Entry);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            ReleaseEntry(Entry);
        }
    }

    internal sealed class CacheEntry
    {
        public IImage Bitmap = null!;
        public long ContentHash;
        public LinkedList<string> IdentityKeys = [];
        public int RefCount;
        public LinkedListNode<CacheEntry>? LRUNode;
        public bool IsRetired;
        public int BitmapDisposed;
    }

    public static IconHandle? Acquire(IAudioSessionControl control, uint processId, bool isSystemSounds)
    {
        lock (s_cacheLock)
        {
            if (s_shutdown) return null;
        }

        try
        {
            if (isSystemSounds)
            {
                IconHandle? hit = TryAcquireIdentity(KeySys);
                if (hit != null) return hit;

                string sysAudioPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    SystemSoundsDll);
                IconPixels? sysRaw = ExtractFromPEResource(sysAudioPath, SystemSoundsIconOrdinal);
                return sysRaw.HasValue ? MemoizeAndCrop(sysRaw.Value, KeySys) : null;
            }

            string sessionIconPath = string.Empty;
            try { control.GetIconPath(out sessionIconPath); }
            catch { }

            if (IsPackagedProcess(processId))
            {
                string aumid = GetApplicationUserModelID(processId);
                if (!string.IsNullOrEmpty(aumid))
                {
                    string canonical = string.Equals(aumid, CortanaBadAumid, StringComparison.OrdinalIgnoreCase)
                        ? CortanaGoodAumid
                        : aumid;
                    string uwpKey = KeyShellUwp + "|" + canonical;
                    IconHandle? hit = TryAcquireIdentity(uwpKey);
                    if (hit != null) return hit;

                    IconPixels? uwpRaw = ExtractFromShell(canonical, isUWP: true);
                    if (uwpRaw.HasValue) return MemoizeAndCrop(uwpRaw.Value, uwpKey);
                }
            }

            string desktopPath = !string.IsNullOrWhiteSpace(sessionIconPath)
                ? Environment.ExpandEnvironmentVariables(sessionIconPath.TrimStart('@'))
                : ProcessHelper.GetProcessImagePath(processId) ?? string.Empty;

            return string.IsNullOrEmpty(desktopPath) ? null : ResolveDesktop(desktopPath);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppIconResolver.Acquire failed: pid={processId} {ex}");
            return null;
        }
    }

    private static IconHandle? ResolveDesktop(string path)
    {
        StringBuilder iconPath = new(path);
        int iconIndex = IconExtraction.PathParseIconLocationW(iconPath);

        if (iconIndex != 0)
        {
            int ordinal = Math.Abs(iconIndex);
            string normalized = NormalizePath(iconPath.ToString());
            string peKey = KeyPe + "|" + normalized + "|" + ordinal;
            IconHandle? hit = TryAcquireIdentity(peKey);
            if (hit != null) return hit;

            IconPixels? peIcon = ExtractFromPEResource(iconPath.ToString(), ordinal);
            if (peIcon.HasValue) return MemoizeAndCrop(peIcon.Value, peKey);
        }

        string shellPath = path;
        if (shellPath.Contains(InvalidOrdinalMarker, StringComparison.Ordinal))
            shellPath = shellPath[..shellPath.LastIndexOf(InvalidOrdinalMarker, StringComparison.Ordinal)];

        string shellKey = KeyShellDesktop + "|" + NormalizePath(shellPath);
        IconHandle? shellHit = TryAcquireIdentity(shellKey);
        if (shellHit != null) return shellHit;

        IconPixels? shellIcon = ExtractFromShell(shellPath, isUWP: false);
        return shellIcon.HasValue ? MemoizeAndCrop(shellIcon.Value, shellKey) : null;
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).ToLowerInvariant(); }
        catch { return path.ToLowerInvariant(); }
    }

    private static IconHandle? TryAcquireIdentity(string identityKey)
    {
        lock (s_cacheLock)
        {
            if (!s_byIdentity.TryGetValue(identityKey, out CacheEntry? entry)) return null;
            AddIdentityAlias(entry, identityKey);
            Revive(entry);
            return new IconHandle(entry);
        }
    }

    private readonly record struct IconPixels(int Width, int Height, byte[] Bgra);

    private static IconHandle? MemoizeAndCrop(IconPixels raw, string identityKey)
    {
        long contentHash = HashPixels(raw);

        lock (s_cacheLock)
        {
            if (s_shutdown) return null;

            if (s_byIdentity.TryGetValue(identityKey, out CacheEntry? existing))
            {
                AddIdentityAlias(existing, identityKey);
                Revive(existing);
                return new IconHandle(existing);
            }

            if (s_byContent.TryGetValue(contentHash, out CacheEntry? byContent))
            {
                AddIdentityAlias(byContent, identityKey);
                Revive(byContent);
                return new IconHandle(byContent);
            }

            IconPixels cropped = CropTransparentBorder(raw);
            CacheEntry entry = new() { Bitmap = ToAvaloniaBitmap(cropped), ContentHash = contentHash, RefCount = 1 };
            AddIdentityAlias(entry, identityKey);
            s_byContent[contentHash] = entry;
            return new IconHandle(entry);
        }
    }

    /// <summary>Keeps a bounded recent identity set for content-deduplicated entries.</summary>
    private static void AddIdentityAlias(CacheEntry entry, string identityKey)
    {
        if (s_byIdentity.TryGetValue(identityKey, out CacheEntry? previousEntry)
            && !ReferenceEquals(previousEntry, entry))
        {
            previousEntry.IdentityKeys.Remove(identityKey);
        }

        entry.IdentityKeys.Remove(identityKey);
        entry.IdentityKeys.AddLast(identityKey);
        s_byIdentity[identityKey] = entry;

        while (entry.IdentityKeys.Count > MaxIdentityAliasesPerEntry)
        {
            LinkedListNode<string>? oldest = entry.IdentityKeys.First;
            if (oldest == null) break;

            entry.IdentityKeys.RemoveFirst();
            if (s_byIdentity.TryGetValue(oldest.Value, out CacheEntry? mapped)
                && ReferenceEquals(mapped, entry))
            {
                s_byIdentity.Remove(oldest.Value);
            }
        }
    }

    private static void Revive(CacheEntry entry)
    {
        if (entry.LRUNode != null)
        {
            s_lru.Remove(entry.LRUNode);
            entry.LRUNode = null;
        }

        entry.RefCount++;
    }

    internal static void ReleaseEntry(CacheEntry entry)
    {
        List<CacheEntry> disposeEntries = [];
        lock (s_cacheLock)
        {
            entry.RefCount--;
            switch (entry.RefCount)
            {
                case > 0:
                    return;
                case < 0:
                {
                    entry.RefCount = 0;
                    string firstKey = entry.IdentityKeys.First?.Value ?? "?";
                    TADNLog.Log($"AppIconResolver.ReleaseEntry refcount underflow on {firstKey}");
                    return;
                }
            }

            if (s_shutdown || entry.IsRetired)
            {
                entry.IsRetired = true;
                disposeEntries.Add(entry);
            }
            else
            {
                entry.LRUNode ??= s_lru.AddFirst(entry);
                int limit = AppServices.Settings?.IconLRULimit ?? AppSettings.IconLRULimitDefault;
                TrimCacheLocked(limit, disposeEntries);
            }
        }

        DisposeEntries(disposeEntries);
    }

    /// <summary>Immediately applies a reduced dead-entry cache limit.</summary>
    internal static void TrimToLimit(int limit)
    {
        List<CacheEntry> disposeEntries = [];
        lock (s_cacheLock)
        {
            if (s_shutdown) return;
            TrimCacheLocked(limit, disposeEntries);
        }

        DisposeEntries(disposeEntries);
    }

    /// <summary>Stops new acquisitions and retires every entry at its final handle release.</summary>
    internal static void Shutdown()
    {
        List<CacheEntry> disposeEntries = [];
        lock (s_cacheLock)
        {
            if (s_shutdown) return;
            s_shutdown = true;

            foreach (CacheEntry entry in s_byContent.Values)
            {
                entry.IsRetired = true;
                entry.LRUNode = null;
                entry.IdentityKeys.Clear();
                if (entry.RefCount == 0) disposeEntries.Add(entry);
            }

            s_byIdentity.Clear();
            s_byContent.Clear();
            s_lru.Clear();
        }

        DisposeEntries(disposeEntries);
    }

    private static void TrimCacheLocked(int requestedLimit, List<CacheEntry> disposeEntries)
    {
        int limit = Math.Clamp(
            requestedLimit,
            AppSettings.IconLRULimitMin,
            AppSettings.IconLRULimitMax);
        while (s_lru.Count > limit)
        {
            LinkedListNode<CacheEntry>? tail = s_lru.Last;
            if (tail == null) break;

            CacheEntry victim = tail.Value;
            s_lru.RemoveLast();
            victim.LRUNode = null;
            victim.IsRetired = true;
            if (s_byContent.TryGetValue(victim.ContentHash, out CacheEntry? contentEntry)
                && ReferenceEquals(contentEntry, victim))
            {
                s_byContent.Remove(victim.ContentHash);
            }

            LinkedListNode<string>? identityNode = victim.IdentityKeys.First;
            while (identityNode != null)
            {
                LinkedListNode<string>? next = identityNode.Next;
                if (s_byIdentity.TryGetValue(identityNode.Value, out CacheEntry? identityEntry)
                    && ReferenceEquals(identityEntry, victim))
                {
                    s_byIdentity.Remove(identityNode.Value);
                }

                identityNode = next;
            }

            victim.IdentityKeys.Clear();
            disposeEntries.Add(victim);
        }
    }

    private static void DisposeEntries(List<CacheEntry> entries)
    {
        for (int index = 0; index < entries.Count; index++)
        {
            CacheEntry entry = entries[index];
            if (Interlocked.Exchange(ref entry.BitmapDisposed, 1) != 0) continue;

            try
            {
                if (entry.Bitmap is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"AppIconResolver bitmap disposal failed: {exception.Message}");
            }
        }
    }

    private static IconPixels CropTransparentBorder(IconPixels source)
    {
        try
        {
            int width = source.Width;
            int height = source.Height;
            if (width <= 0 || height <= 0) return source;

            byte[] pixels = source.Bgra;
            int stride = width * 4;
            int minX = width, minY = height, maxX = -1, maxY = -1;

            for (int y = 0; y < height; y++)
            {
                int rowBase = y * stride;
                int run = 0;
                int rowMinX = -1, rowMaxX = -1;
                for (int x = 0; x < width; x++)
                {
                    byte alpha = pixels[rowBase + x * 4 + 3];
                    if (alpha > AlphaThreshold)
                    {
                        run++;
                        if (run < MinOpaqueRun) continue;
                        int firstInRun = x - run + 1;
                        if (rowMinX < 0 || firstInRun < rowMinX) rowMinX = firstInRun;
                        rowMaxX = x;
                    }
                    else
                        run = 0;
                }

                if (rowMaxX < 0) continue;
                if (rowMinX < minX) minX = rowMinX;
                if (rowMaxX > maxX) maxX = rowMaxX;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            if (maxX < 0) return source;

            int leftPad = minX;
            int topPad = minY;
            int rightPad = width - 1 - maxX;
            int bottomPad = height - 1 - maxY;
            int minPad = Math.Min(Math.Min(leftPad, rightPad), Math.Min(topPad, bottomPad));
            int padGate = (int)Math.Ceiling(width * MinPadRatio);
            if (minPad < padGate) return source;

            int cropW = maxX - minX + 1;
            int cropH = maxY - minY + 1;
            int side = Math.Max(cropW, cropH);
            int cx = minX + cropW / 2;
            int cy = minY + cropH / 2;
            int half = side / 2;
            int sx = Math.Max(0, Math.Min(width - side, cx - half));
            int sy = Math.Max(0, Math.Min(height - side, cy - half));
            int sw = Math.Min(side, width - sx);
            int sh = Math.Min(side, height - sy);

            byte[] cropped = new byte[sw * sh * 4];
            for (int y = 0; y < sh; y++)
            {
                Buffer.BlockCopy(
                    pixels,
                    (sy + y) * stride + sx * 4,
                    cropped,
                    y * sw * 4,
                    sw * 4);
            }

            return new IconPixels(sw, sh, cropped);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppIconResolver.CropTransparentBorder failed: {ex}");
            return source;
        }
    }

    private static long HashPixels(IconPixels source)
    {
        try
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int i = 0; i < source.Bgra.Length; i++)
            {
                hash ^= source.Bgra[i];
                hash *= prime;
            }

            hash ^= (ulong)source.Width;
            hash *= prime;
            hash ^= (ulong)source.Height;
            hash *= prime;
            return (long)hash;
        }
        catch
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(source.Bgra);
        }
    }

    private static WriteableBitmap ToAvaloniaBitmap(IconPixels pixels)
    {
        WriteableBitmap bitmap = new(
            new PixelSize(pixels.Width, pixels.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using ILockedFramebuffer frame = bitmap.Lock();
        int sourceStride = pixels.Width * 4;
        for (int y = 0; y < pixels.Height; y++)
        {
            Marshal.Copy(
                pixels.Bgra,
                y * sourceStride,
                IntPtr.Add(frame.Address, y * frame.RowBytes),
                Math.Min(sourceStride, frame.RowBytes));
        }

        return bitmap;
    }

    private static IconPixels? ExtractFromPEResource(string path, int iconOrdinal)
    {
        IntPtr hModule = IconExtraction.LoadLibraryExW(
            path,
            IntPtr.Zero,
            IconExtraction.LOAD_LIBRARY_AS_DATAFILE | IconExtraction.LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if (hModule == IntPtr.Zero) return null;

        IntPtr hIcon = IntPtr.Zero;
        try
        {
            IntPtr groupResInfo =
                IconExtraction.FindResource(hModule, new IntPtr(iconOrdinal), IconExtraction.RT_GROUP_ICON);
            if (groupResInfo == IntPtr.Zero) return null;

            IntPtr groupResHandle = IconExtraction.LoadResource(hModule, groupResInfo);
            if (groupResHandle == IntPtr.Zero) return null;

            IntPtr groupResData = IconExtraction.LockResource(groupResHandle);
            if (groupResData == IntPtr.Zero) return null;

            int iconId = IconExtraction.LookupIconIdFromDirectoryEx(
                groupResData, true, IconSize, IconSize, IconExtraction.LoadImageFlags.LR_DEFAULTCOLOR);
            if (iconId == 0) return null;

            IntPtr iconResInfo = IconExtraction.FindResource(hModule, new IntPtr(iconId), IconExtraction.RT_ICON);
            if (iconResInfo == IntPtr.Zero) return null;

            IntPtr iconResHandle = IconExtraction.LoadResource(hModule, iconResInfo);
            if (iconResHandle == IntPtr.Zero) return null;

            IntPtr iconResData = IconExtraction.LockResource(iconResHandle);
            int iconResSize = IconExtraction.SizeofResource(hModule, iconResInfo);
            if (iconResData == IntPtr.Zero || iconResSize == 0) return null;

            hIcon = IconExtraction.CreateIconFromResourceEx(
                iconResData, iconResSize, true,
                IconExtraction.IconCursorVersion.Default,
                IconSize, IconSize,
                IconExtraction.LoadImageFlags.LR_DEFAULTCOLOR);
            return hIcon == IntPtr.Zero ? null : ReadIconPixels(hIcon);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppIconResolver.ExtractFromPEResource {path},{iconOrdinal} {ex}");
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero) User32.DestroyIcon(hIcon);
            IconExtraction.FreeLibrary(hModule);
        }
    }

    private static IconPixels? ExtractFromShell(string path, bool isUWP)
    {
        string canonical = isUWP && string.Equals(path, CortanaBadAumid, StringComparison.OrdinalIgnoreCase)
            ? CortanaGoodAumid
            : path;

        if (!isUWP)
        {
            IconPixels? desktopIcon = ExtractWithShellIconApi(canonical);
            if (desktopIcon.HasValue) return desktopIcon;
        }

        IntPtr factoryPtr = IntPtr.Zero;
        try
        {
            int hr = isUWP
                ? IconExtraction.SHCreateItemInKnownFolder(
                    in IconExtraction.AppsFolderID,
                    IconExtraction.KF_FLAG_DONT_VERIFY,
                    canonical,
                    in IconExtraction.ShellItemImageFactoryIid,
                    out factoryPtr)
                : IconExtraction.SHCreateItemFromParsingName(
                    canonical,
                    IntPtr.Zero,
                    in IconExtraction.ShellItemImageFactoryIid,
                    out factoryPtr);

            if (hr < 0 || factoryPtr == IntPtr.Zero)
            {
                if (isUWP)
                {
                    hr = IconExtraction.SHCreateItemFromParsingName(
                        canonical,
                        IntPtr.Zero,
                        in IconExtraction.ShellItemImageFactoryIid,
                        out factoryPtr);
                }

                if (hr < 0 || factoryPtr == IntPtr.Zero) return null;
            }

            IntPtr hBitmap = IntPtr.Zero;
            try
            {
                using IconExtraction.ShellImageFactory factory = new(factoryPtr);
                factoryPtr = IntPtr.Zero;
                IconExtraction.SIZE size = new() { cx = IconSize, cy = IconSize };
                hr = factory.GetImage(size, IconExtraction.SIIGBF.SIIGBF_RESIZETOFIT, out hBitmap);
                if (hr < 0) return null;
                return hBitmap == IntPtr.Zero ? null : ReadHBitmapPixels(hBitmap);
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) IconExtraction.DeleteObject(hBitmap);
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppIconResolver.ExtractFromShell {canonical} {ex}");
            return null;
        }
        finally
        {
            if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
        }
    }

    private static IconPixels? ExtractWithShellIconApi(string path)
    {
        string iconFile = path;
        int iconIndex = 0;

        try
        {
            StringBuilder parsed = new(path);
            iconIndex = IconExtraction.PathParseIconLocationW(parsed);
            if (parsed.Length > 0) iconFile = parsed.ToString();
        }
        catch
        {
            iconFile = path;
        }

        if (!File.Exists(iconFile)) return null;

        IntPtr hLarge = IntPtr.Zero;
        IntPtr hSmall = IntPtr.Zero;
        try
        {
            uint iconSize = ((uint)IconSize << 16) | IconSize;
            int hr = IconExtraction.SHDefExtractIconW(iconFile, iconIndex, 0, out hLarge, out hSmall, iconSize);
            if (hr < 0) return null;

            IntPtr selected = hLarge != IntPtr.Zero ? hLarge : hSmall;
            return selected == IntPtr.Zero ? null : ReadIconPixels(selected);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppIconResolver.ExtractWithShellIconApi {iconFile},{iconIndex} {ex}");
            return null;
        }
        finally
        {
            if (hLarge != IntPtr.Zero) User32.DestroyIcon(hLarge);
            if (hSmall != IntPtr.Zero) User32.DestroyIcon(hSmall);
        }
    }

    private static IconPixels? ReadIconPixels(IntPtr hIcon)
    {
        if (!IconExtraction.GetIconInfo(hIcon, out IconExtraction.ICONINFO info)) return null;

        try
        {
            if (info.hbmColor != IntPtr.Zero)
                return ReadHBitmapPixels(info.hbmColor);

            return info.hbmMask != IntPtr.Zero ? ReadHBitmapPixels(info.hbmMask) : null;
        }
        finally
        {
            if (info.hbmColor != IntPtr.Zero) IconExtraction.DeleteObject(info.hbmColor);
            if (info.hbmMask != IntPtr.Zero) IconExtraction.DeleteObject(info.hbmMask);
        }
    }

    private static IconPixels? ReadHBitmapPixels(IntPtr hBitmap)
    {
        if (IconExtraction.GetObject(
                hBitmap,
                Marshal.SizeOf<IconExtraction.BITMAP>(),
                out IconExtraction.BITMAP info) == 0
            || info.bmWidth <= 0
            || info.bmHeight == 0)
            return null;

        int width = info.bmWidth;
        int height = Math.Abs(info.bmHeight);
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        GCHandle pixelsHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);

        IntPtr hdc = IntPtr.Zero;
        try
        {
            IconExtraction.BITMAPINFO bmi = new()
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

            hdc = IconExtraction.GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return null;

            int copied = IconExtraction.GetDIBits(
                hdc,
                hBitmap,
                0,
                (uint)height,
                pixelsHandle.AddrOfPinnedObject(),
                ref bmi,
                IconExtraction.DIB_RGB_COLORS);
            if (copied == 0) return null;
        }
        finally
        {
            if (hdc != IntPtr.Zero) IconExtraction.ReleaseDC(IntPtr.Zero, hdc);
            pixelsHandle.Free();
        }

        return new IconPixels(width, height, pixels);
    }

    private static bool IsPackagedProcess(uint processId)
    {
        IntPtr handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero) return false;

        try
        {
            int bufferSize = 0;
            int hr = IconExtraction.GetPackageId(handle, ref bufferSize, IntPtr.Zero);
            return hr == NativeErrors.ERROR_INSUFFICIENT_BUFFER;
        }
        finally { Kernel32.CloseHandle(handle); }
    }

    private static string GetApplicationUserModelID(uint processId)
    {
        IntPtr handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero) return string.Empty;

        try
        {
            int length = IconExtraction.MAX_AUMID_LEN;
            StringBuilder buffer = new(length);
            int hr = IconExtraction.GetApplicationUserModelId(handle, ref length, buffer);
            return hr == NativeErrors.S_OK ? buffer.ToString() : string.Empty;
        }
        finally { Kernel32.CloseHandle(handle); }
    }
}
