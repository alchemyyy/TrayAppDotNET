namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Thread-safe, length-one queue used to preserve only the newest requested Night Light strength.
/// </summary>
internal sealed class NightLightLatestStrengthQueue
{
    private readonly Lock _gate = new();
    private bool _hasValue;
    private int _value;

    /// <summary>
    /// Stores the latest value. Returns true when an older pending value was replaced.
    /// </summary>
    public bool Store(int value)
    {
        lock (_gate)
        {
            bool replaced = _hasValue;
            _value = value;
            _hasValue = true;
            return replaced;
        }
    }

    /// <summary>
    /// Takes the pending value, if present.
    /// </summary>
    public bool TryTake(out int value)
    {
        lock (_gate)
        {
            if (!_hasValue)
            {
                value = 0;
                return false;
            }

            value = _value;
            _hasValue = false;
            return true;
        }
    }

    /// <summary>
    /// Restores a failed in-flight value only when no newer request is already pending.
    /// </summary>
    public bool RestoreIfEmpty(int value)
    {
        lock (_gate)
        {
            if (_hasValue) return false;

            _value = value;
            _hasValue = true;
            return true;
        }
    }

    /// <summary>
    /// Drops the pending value during shutdown or an active-state transition.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
            _hasValue = false;
    }
}
