namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Owns callbacks and disposable resources for one explicit UI lifetime.
/// </summary>
public sealed class UIResourceScope : IDisposable
{
    private static readonly CancellationToken CanceledToken = new(canceled: true);

    private readonly Lock _gate = new();
    private readonly Action<Exception>? _logError;
    private readonly string _ownerName;
    private List<Action>? _cleanupActions = [];
    private CancellationTokenSource? _cancellationSource = new();
    private int _disposed;

    public UIResourceScope(string ownerName, Action<Exception>? logError = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerName);
        _ownerName = ownerName;
        _logError = logError;
    }

    /// <summary>Gets whether cleanup has already started.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Gets a token canceled before owned resources are released.</summary>
    public CancellationToken CancellationToken
    {
        get
        {
            lock (_gate)
                return _cancellationSource?.Token ?? CanceledToken;
        }
    }

    /// <summary>
    /// Registers a cleanup action. Registration after disposal runs immediately so late ownership cannot leak.
    /// </summary>
    public void Add(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);

        bool runImmediately;
        lock (_gate)
        {
            runImmediately = _cleanupActions == null;
            if (!runImmediately)
                _cleanupActions!.Add(cleanup);
        }

        if (runImmediately)
            RunCleanup(cleanup);
    }

    /// <summary>Registers and returns a disposable resource owned by this scope.</summary>
    public T Own<T>(T resource)
        where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        Add(resource.Dispose);
        return resource;
    }

    /// <summary>Creates a child scope that is retired with this scope.</summary>
    public UIResourceScope CreateChild(string ownerName)
    {
        UIResourceScope child = new(ownerName, _logError);
        Add(child.Dispose);
        return child;
    }

    /// <summary>Cancels work and runs all cleanup actions once in reverse registration order.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        List<Action> cleanupActions = [];
        CancellationTokenSource? cancellationSource;
        lock (_gate)
        {
            if (_cleanupActions != null)
            {
                cleanupActions.AddRange(_cleanupActions);
                _cleanupActions.Clear();
                _cleanupActions = null;
            }

            cancellationSource = _cancellationSource;
            _cancellationSource = null;
        }

        if (cancellationSource != null)
        {
            try
            {
                cancellationSource.Cancel();
            }
            catch (Exception exception)
            {
                Log(exception);
            }
        }

        for (int index = cleanupActions.Count - 1; index >= 0; index--)
            RunCleanup(cleanupActions[index]);

        if (cancellationSource == null) return;

        try
        {
            cancellationSource.Dispose();
        }
        catch (Exception exception)
        {
            Log(exception);
        }
    }

    private void RunCleanup(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            Log(exception);
        }
    }

    private void Log(Exception exception)
    {
        if (_logError != null)
        {
            try
            {
                _logError(exception);
                return;
            }
            catch (Exception loggerException)
            {
                TADNLog.Log(
                    $"UIResourceScope '{_ownerName}' logger failed: {loggerException.GetType().Name}: " +
                    loggerException.Message);
            }
        }

        TADNLog.Log(
            $"UIResourceScope '{_ownerName}' cleanup failed: {exception.GetType().Name}: {exception.Message}");
    }
}
