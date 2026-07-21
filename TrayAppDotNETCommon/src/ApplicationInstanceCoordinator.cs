namespace TrayAppDotNETCommon;

/// <summary>Prevents overlapping monitored application processes during watcher handoff.</summary>
internal sealed class ApplicationInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;

    private ApplicationInstanceCoordinator(Mutex mutex) => _mutex = mutex;

    public static ApplicationInstanceCoordinator? TryAcquire(
        SingleInstanceIdentity identity,
        int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(timeoutMs);

        Mutex mutex = new(initiallyOwned: false, identity.ApplicationInstanceMutexName);
        try
        {
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(timeoutMs);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }

            return new ApplicationInstanceCoordinator(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _mutex.ReleaseMutex();
        }
        finally
        {
            _mutex.Dispose();
        }
    }
}
