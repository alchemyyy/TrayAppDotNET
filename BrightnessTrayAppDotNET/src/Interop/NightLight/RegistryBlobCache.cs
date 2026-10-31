using Microsoft.Win32;

namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// One HKCU key's binary value, re-read only after the key changed. A thread-agnostic
/// <c>RegNotifyChangeKeyValue</c> is armed before every uncached read, so a change during the read is never missed.
/// When the key can't be watched, every call reads the registry. Callers must not modify the returned array.
/// </summary>
internal sealed class RegistryBlobCache(string keyPath, Func<string, byte[]?> readBlob) : IDisposable
{
    private readonly Lock _gate = new();
    private RegistryKey? _watchedKey;
    private AutoResetEvent? _changed;
    private RegisteredWaitHandle? _changeRegistration;
    private byte[]? _value;
    private int _version;
    private int _valueVersion = -1;
    private bool _armed;
    private bool _watchFailureLogged;
    private bool _disposed;

    public byte[]? Read()
    {
        int version;
        lock (_gate)
        {
            if (!TryArmLocked()) return readBlob(keyPath);

            version = _version;
            if (_valueVersion == version) return _value;
        }

        byte[]? value = readBlob(keyPath);
        lock (_gate)
        {
            // A change that landed during the read moved the version on, so the next call reads again
            _value = value;
            _valueVersion = version;
        }

        return value;
    }

    /// <summary>Drops the cached value; used right after this process (or a helper it waited on) wrote the key.</summary>
    public void Invalidate()
    {
        lock (_gate)
            _version++;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _armed = false;
            _changeRegistration?.Unregister(waitObject: null);
            _changeRegistration = null;
            _watchedKey?.Dispose();
            _watchedKey = null;
            _changed?.Dispose();
            _changed = null;
            _value = null;
        }
    }

    private bool TryArmLocked()
    {
        if (_disposed) return false;
        if (_armed) return true;

        try
        {
            _watchedKey ??= Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
            if (_watchedKey == null) return false;

            _changed ??= new AutoResetEvent(initialState: false);
            int rc = AsyncUtils.RegNotifyChangeKeyValue(
                _watchedKey.Handle,
                bWatchSubtree: false,
                AsyncUtils.RegNotifyChangeLastSetThreadAgnostic,
                _changed.SafeWaitHandle,
                fAsynchronous: true);
            if (rc != 0)
            {
                LogWatchFailureOnce($"RegNotifyChangeKeyValue rc={rc}");
                return false;
            }

            _changeRegistration ??= ThreadPool.RegisterWaitForSingleObject(
                _changed,
                OnKeyChanged,
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: false);
            _armed = true;
            return true;
        }
        catch (Exception ex)
        {
            LogWatchFailureOnce(ex.Message);
            return false;
        }
    }

    private void OnKeyChanged(object? state, bool timedOut)
    {
        lock (_gate)
        {
            _armed = false;
            _version++;
        }
    }

    private void LogWatchFailureOnce(string reason)
    {
        if (_watchFailureLogged) return;

        _watchFailureLogged = true;
        TADNLog.Log($"RegistryBlobCache('{keyPath}'): change watch unavailable, reading uncached: {reason}");
    }
}
