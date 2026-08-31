namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Owns callbacks and disposable resources for one explicit UI lifetime.
/// </summary>
public sealed class UIResourceScope : IDisposable
{
    private static readonly CancellationToken CanceledToken = new(true);

    private readonly Lock _gate = new();
    private readonly Action<Exception>? _logError;
    private readonly string _ownerName;
    private List<CleanupRegistration>? _cleanupActions = [];
    private CancellationTokenSource? _cancellationSource = new();
    private CleanupRegistration? _parentRegistration;
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
        _ = AddRegistration(cleanup);
    }

    /// <summary>Registers and returns a disposable resource owned by this scope.</summary>
    public T Own<T>(T resource)
        where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        Add(resource.Dispose);
        return resource;
    }

    /// <summary>
    /// Creates a child scope that retires with this scope and unregisters when independently retired.
    /// </summary>
    public UIResourceScope CreateChild(string ownerName)
    {
        UIResourceScope child = new(ownerName, _logError);
        CleanupRegistration registration = AddRegistration(child.Dispose);
        child.AttachParentRegistration(registration);
        return child;
    }

    /// <summary>Cancels work and runs all cleanup actions once in reverse registration order.</summary>
    public void Dispose()
    {
        DetachRegistration(Interlocked.Exchange(ref _parentRegistration, value: null));
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

        List<CleanupRegistration> cleanupActions = [];
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
            RunRegistration(cleanupActions[index]);

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

    private CleanupRegistration AddRegistration(Action cleanup)
    {
        CleanupRegistration registration = new(this, cleanup);
        bool runImmediately;
        lock (_gate)
        {
            runImmediately = _cleanupActions == null;
            if (!runImmediately)
                _cleanupActions!.Add(registration);
        }

        if (runImmediately)
            RunRegistration(registration);
        return registration;
    }

    private void AttachParentRegistration(CleanupRegistration registration)
    {
        _parentRegistration = registration;
        if (!IsDisposed) return;

        DetachRegistration(Interlocked.Exchange(ref _parentRegistration, value: null));
    }

    private void RemoveRegistration(CleanupRegistration registration)
    {
        lock (_gate)
            _cleanupActions?.Remove(registration);
    }

    // Interlocked exchange can return null when parent and child disposal race
    private static void DetachRegistration(CleanupRegistration? registration) => registration?.Detach();

    private void RunRegistration(CleanupRegistration registration)
    {
        Action? cleanup = registration.ClaimCleanup();
        if (cleanup != null)
            RunCleanup(cleanup);
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

    private sealed class CleanupRegistration(UIResourceScope owner, Action cleanup)
    {
        private UIResourceScope? _owner = owner;
        private Action? _cleanup = cleanup;

        public Action? ClaimCleanup()
        {
            Interlocked.Exchange(ref _owner, value: null);
            return Interlocked.Exchange(ref _cleanup, value: null);
        }

        public void Detach()
        {
            Interlocked.Exchange(ref _cleanup, value: null);
            UIResourceScope? registrationOwner = Interlocked.Exchange(ref _owner, value: null);
            registrationOwner?.RemoveRegistration(this);
        }
    }
}
