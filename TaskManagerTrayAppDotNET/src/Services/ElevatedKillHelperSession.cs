namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Exposes one ready helper session while keeping its native transport replaceable in tests.</summary>
internal sealed class ElevatedKillHelperSession : IDisposable
{
    private readonly ElevatedKillHelperClient? _client;
    private readonly Func<bool>? _isReady;
    private readonly Func<ProcessTerminationTarget?, long, bool>? _tryArm;
    private readonly Action? _dispose;
    private int _disposed;

    public ElevatedKillHelperSession(ElevatedKillHelperClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    internal ElevatedKillHelperSession(
        Func<bool> isReady,
        Func<ProcessTerminationTarget?, long, bool> tryArm,
        Action dispose)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(tryArm);
        ArgumentNullException.ThrowIfNull(dispose);
        _isReady = isReady;
        _tryArm = tryArm;
        _dispose = dispose;
    }

    public bool IsReady =>
        Volatile.Read(ref _disposed) == 0 &&
        (_client?.IsReady ?? _isReady?.Invoke() ?? false);

    public int HardeningFlags => Volatile.Read(ref _disposed) == 0
        ? _client?.HardeningFlags ?? 0
        : 0;

    /// <summary>Pre-opens the selected target in the elevated helper.</summary>
    public bool TryArm(ProcessTerminationTarget? target, long generation)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        if (_client != null) return _client.TryArm(target, generation);
        return _tryArm?.Invoke(target, generation) ?? false;
    }

    /// <summary>Publishes one fire request before any managed fallback work occurs.</summary>
    public bool TryRequestTermination(
        ProcessTerminationTarget target,
        long generation,
        out long requestSequence)
    {
        requestSequence = 0;
        return Volatile.Read(ref _disposed) == 0 &&
               _client != null &&
               _client.TryRequestTermination(target, generation, out requestSequence);
    }

    /// <summary>Waits for a matching helper response without depending on the thread pool.</summary>
    public bool TryWaitForResponse(
        long requestSequence,
        int timeoutMilliseconds,
        out int result,
        out int errorCode)
    {
        result = KillHelperProtocol.ResultNone;
        errorCode = 0;
        return Volatile.Read(ref _disposed) == 0 &&
               _client != null &&
               _client.TryWaitForResponse(
                   requestSequence,
                   timeoutMilliseconds,
                   out result,
                   out errorCode);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;
        _client?.Dispose();
        _dispose?.Invoke();
    }
}
