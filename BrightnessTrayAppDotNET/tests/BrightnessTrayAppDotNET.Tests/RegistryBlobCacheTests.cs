using System.Diagnostics;
using BrightnessTrayAppDotNET.Interop.NightLight;
using Microsoft.Win32;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class RegistryBlobCacheTests : IDisposable
{
    private const string ValueName = "Data";

    private readonly string _keyPath = $@"Software\TrayAppDotNET.Tests\RegistryBlobCache\{Guid.NewGuid():N}";
    private int _reads;

    public RegistryBlobCacheTests()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_keyPath);
        key.SetValue(ValueName, new byte[] { 1 }, RegistryValueKind.Binary);
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);

    [Fact]
    public void UnchangedKeyIsReadOnce()
    {
        using RegistryBlobCache cache = new(_keyPath, ReadCounted);

        byte[]? first = cache.Read();
        byte[]? second = cache.Read();

        Assert.Equal([1], first);
        Assert.Same(first, second);
        Assert.Equal(expected: 1, Volatile.Read(ref _reads));
    }

    [Fact]
    public async Task ExternalChangeIsSeenThroughTheNotification()
    {
        using RegistryBlobCache cache = new(_keyPath, ReadCounted);
        Assert.Equal([1], cache.Read());

        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true)!)
            key.SetValue(ValueName, new byte[] { 2 }, RegistryValueKind.Binary);

        Stopwatch elapsed = Stopwatch.StartNew();
        while (cache.Read() is not [2])
        {
            Assert.True(elapsed.ElapsedMilliseconds < 5_000, "The change notification never invalidated the cache.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public void InvalidateForcesTheNextRead()
    {
        using RegistryBlobCache cache = new(_keyPath, ReadCounted);
        _ = cache.Read();

        cache.Invalidate();
        _ = cache.Read();

        Assert.Equal(expected: 2, Volatile.Read(ref _reads));
    }

    [Fact]
    public void DisposedCacheReadsUncached()
    {
        RegistryBlobCache cache = new(_keyPath, ReadCounted);
        cache.Dispose();

        _ = cache.Read();
        _ = cache.Read();

        Assert.Equal(expected: 2, Volatile.Read(ref _reads));
    }

    private byte[]? ReadCounted(string path)
    {
        Interlocked.Increment(ref _reads);
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path, writable: false);
        return key?.GetValue(ValueName) as byte[];
    }
}
