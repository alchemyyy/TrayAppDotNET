namespace TaskManagerTrayAppDotNET.Services;

/// <summary>One D3D12 feature-level probe; <see cref="IsDefinitive"/> is false when the probe could not decide.</summary>
internal readonly record struct DirectXProbeResult(string DirectXVersion, string FeatureLevel, bool IsDefinitive)
{
    /// <summary>The adapter was missing, removed or reset, or the driver failed: probe again later.</summary>
    public static DirectXProbeResult Transient { get; } = new(string.Empty, string.Empty, IsDefinitive: false);

    /// <summary>The adapter supports no D3D12 feature level, or this Windows has no D3D12.</summary>
    public static DirectXProbeResult Unsupported { get; } = new(string.Empty, string.Empty, IsDefinitive: true);
}

/// <summary>
/// Remembers the D3D12 feature-level answer per adapter LUID for the process lifetime, so periodic metadata refreshes
/// never probe the same adapter again. Only a definitive answer is kept forever: a probe that could not decide (the
/// adapter was not found, removed or reset) is repeated after <see cref="RetryDelay"/>, and until then the empty
/// answer is returned without probing. Thread-safe; at most one probe runs at a time.
/// </summary>
internal sealed class DirectXFeatureLevelCache(TimeProvider timeProvider)
{
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<ulong, DirectXProbeResult> _definitive = [];
    private readonly Dictionary<ulong, long> _transientProbeTimestamps = [];

    public DirectXFeatureLevelCache() : this(TimeProvider.System)
    {
    }

    public (string DirectXVersion, string FeatureLevel) GetOrAdd(
        ulong adapterLUID,
        Func<ulong, DirectXProbeResult> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        lock (_gate)
        {
            if (_definitive.TryGetValue(adapterLUID, out DirectXProbeResult cached))
                return (cached.DirectXVersion, cached.FeatureLevel);

            long now = timeProvider.GetTimestamp();
            if (_transientProbeTimestamps.TryGetValue(adapterLUID, out long lastProbe)
                && timeProvider.GetElapsedTime(lastProbe, now) < RetryDelay)
                return (string.Empty, string.Empty);

            DirectXProbeResult result = probe(adapterLUID);
            if (result.IsDefinitive)
            {
                _definitive.Add(adapterLUID, result);
                _transientProbeTimestamps.Remove(adapterLUID);
            }
            else
            {
                _transientProbeTimestamps[adapterLUID] = now;
            }

            return (result.DirectXVersion, result.FeatureLevel);
        }
    }
}
