using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Interop;

/// <summary>PDH query lifetime natives shared by the performance samplers.</summary>
internal static class Pdh
{
    public const uint Success = 0;

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhAddEnglishCounterW(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    public static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    public static extern uint PdhCloseQuery(IntPtr query);
}

/// <summary>
/// One PDH query whose setup runs lazily on the sampling thread and is retried with backoff when it fails, instead of
/// leaving a panel blank until restart. The first failure in a streak is logged once with its PDH status.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const long InitialRetryDelayMilliseconds = 5_000;
    private const long MaximumRetryDelayMilliseconds = 5 * 60 * 1_000;

    private readonly string _owner;
    private readonly Func<PdhQuery, uint> _addCounters;
    private IntPtr _handle;
    private int _consecutiveFailures;
    private long _nextAttemptTimestamp;
    private bool _disposed;

    /// <param name="owner">Names the sampler in the log.</param>
    /// <param name="addCounters">
    /// Adds the sampler's counters through <see cref="AddCounter"/> and stores their handles; returns
    /// <see cref="Pdh.Success"/> when the query is usable, otherwise the PDH status that made it unusable.
    /// </param>
    public PdhQuery(string owner, Func<PdhQuery, uint> addCounters)
    {
        _owner = owner;
        _addCounters = addCounters;
    }

    public IntPtr Handle => _handle;

    public bool IsOpen => _handle != IntPtr.Zero;

    /// <summary>Adds one English counter path to the query being set up.</summary>
    public uint AddCounter(string path, out IntPtr counter) =>
        Pdh.PdhAddEnglishCounterW(_handle, path, IntPtr.Zero, out counter);

    /// <summary>Sets the query up when needed (subject to backoff), then collects one sample.</summary>
    public bool TryCollect()
    {
        if (!EnsureOpen()) return false;
        return Pdh.PdhCollectQueryData(_handle) == Pdh.Success;
    }

    /// <summary>Returns the wait before the next setup attempt after the given number of failed attempts.</summary>
    internal static TimeSpan RetryDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0) return TimeSpan.Zero;
        int doublings = Math.Min(consecutiveFailures - 1, 16);
        return TimeSpan.FromMilliseconds(Math.Min(InitialRetryDelayMilliseconds << doublings, MaximumRetryDelayMilliseconds));
    }

    private bool EnsureOpen()
    {
        if (_disposed) return false;
        if (_handle != IntPtr.Zero) return true;
        if (_consecutiveFailures > 0 && Stopwatch.GetTimestamp() < _nextAttemptTimestamp) return false;

        uint status = Pdh.PdhOpenQueryW(dataSource: null, IntPtr.Zero, out IntPtr handle);
        if (status == Pdh.Success)
        {
            _handle = handle;
            status = _addCounters(this);
            if (status == Pdh.Success)
            {
                if (_consecutiveFailures > 0)
                    TADNLog.Log($"{_owner}: PDH counters are available after {_consecutiveFailures} failed setup attempts.");
                _consecutiveFailures = 0;
                return true;
            }

            _ = Pdh.PdhCloseQuery(_handle);
            _handle = IntPtr.Zero;
        }

        _consecutiveFailures++;
        if (_consecutiveFailures == 1)
            TADNLog.Log($"{_owner}: PDH setup failed (status 0x{status:X8}); retrying with backoff.");
        _nextAttemptTimestamp = Stopwatch.GetTimestamp()
                                + (long)(RetryDelay(_consecutiveFailures).TotalSeconds * Stopwatch.Frequency);
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle == IntPtr.Zero) return;
        _ = Pdh.PdhCloseQuery(_handle);
        _handle = IntPtr.Zero;
    }
}
