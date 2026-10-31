namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Holds COM proxies that thread-pool workers (meter sampling, throttled volume writes, icon lookups) call while the UI
/// thread may release them. Every worker call runs under the gate against the current value, and
/// <see cref="Retire"/> swaps the value out under the same gate, so once it returns no worker is inside a call on the
/// retired proxies and the caller can release them.
/// </summary>
internal sealed class ComProxyGate<T>
    where T : class
{
    private readonly Lock _gate = new();
    private T? _value;

    /// <summary>Unlocked snapshot for presence checks on the owning thread; workers call through <see cref="TryInvoke"/>.</summary>
    public T? Current => Volatile.Read(ref _value);

    public void Publish(T value)
    {
        lock (_gate) _value = value;
    }

    /// <summary>Takes the proxies out of service; no call is in flight on them once this returns.</summary>
    public T? Retire()
    {
        lock (_gate)
        {
            T? retired = _value;
            _value = null;
            return retired;
        }
    }

    /// <summary>Runs <paramref name="call"/> on the current proxies under the gate; false once they were retired.</summary>
    public bool TryInvoke<TState, TResult>(TState state, Func<T, TState, TResult> call, out TResult result)
    {
        lock (_gate)
        {
            if (_value == null)
            {
                result = default!;
                return false;
            }

            result = call(_value, state);
            return true;
        }
    }
}
