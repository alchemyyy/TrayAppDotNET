namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Thread-safe decaying maximum used by volume-feedback suppression. New peaks attack
/// immediately; lower samples let the held maximum fall exponentially without a discrete cutoff.
/// </summary>
internal sealed class DingSuppressionPeak
{
    private const int ThresholdPercentMin = 0;
    private const int ThresholdPercentMax = 100;
    private const float PercentToScalar = 0.01f;

    private readonly Lock _gate = new();
    private float _value;
    private long _lastUpdateMilliseconds;
    private bool _isInitialized;

    /// <summary>Observes a current meter sample and returns the updated decaying maximum.</summary>
    internal float Observe(float currentPeak) => Observe(currentPeak, Environment.TickCount64);

    /// <summary>Returns the held maximum after applying falloff through the current time.</summary>
    internal float Read() => Read(Environment.TickCount64);

    /// <summary>Scales the configured full-volume threshold by the volume being set.</summary>
    internal static float ResolveThreshold(int configuredPercent, float scalarVolume)
    {
        int clampedPercent = Math.Clamp(configuredPercent, ThresholdPercentMin, ThresholdPercentMax);
        float clampedVolume = float.IsFinite(scalarVolume) ? Math.Clamp(scalarVolume, min: 0f, max: 1f) : 0f;
        return clampedPercent * PercentToScalar * clampedVolume;
    }

    /// <summary>
    /// Applies the suppression decision. An unavailable peak fails closed because the endpoint is
    /// tearing down and should not begin new feedback playback.
    /// </summary>
    internal static bool ShouldSuppressFeedback(
        float recentPeak,
        int configuredPercent,
        float scalarVolume,
        bool isPeakAvailable)
    {
        if (!isPeakAvailable) return true;

        float clampedPeak = float.IsFinite(recentPeak) ? Math.Clamp(recentPeak, min: 0f, max: 1f) : 0f;
        return clampedPeak > ResolveThreshold(configuredPercent, scalarVolume);
    }

    // Explicit timestamps keep the envelope deterministic in tests and avoid update-rate-dependent
    // falloff when the user changes the configurable peak-meter sample rate.
    internal float Observe(float currentPeak, long timestampMilliseconds)
    {
        float clampedPeak = float.IsFinite(currentPeak) ? Math.Clamp(currentPeak, min: 0f, max: 1f) : 0f;

        lock (_gate)
        {
            Advance(timestampMilliseconds);
            if (clampedPeak >= _value) _value = clampedPeak;

            return _value;
        }
    }

    internal float Read(long timestampMilliseconds)
    {
        lock (_gate)
        {
            Advance(timestampMilliseconds);
            return _value;
        }
    }

    private void Advance(long timestampMilliseconds)
    {
        if (!_isInitialized)
        {
            _lastUpdateMilliseconds = timestampMilliseconds;
            _isInitialized = true;
            return;
        }

        if (timestampMilliseconds <= _lastUpdateMilliseconds) return;

        long elapsedMilliseconds = timestampMilliseconds - _lastUpdateMilliseconds;
        _lastUpdateMilliseconds = timestampMilliseconds;
        float falloff = MathF.Pow(
            x: 0.5f,
            elapsedMilliseconds / (float)TimeConstants.DingSuppressionPeakHalfLifeMs);
        _value *= falloff;
    }
}
