using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class DirectXFeatureLevelCacheTests
{
    [Fact]
    public void EachAdapterIsProbedOnceWhenTheAnswerIsDefinitive()
    {
        DirectXFeatureLevelCache cache = new(new ManualTimeProvider());
        List<ulong> probed = [];

        DirectXProbeResult Probe(ulong luid)
        {
            probed.Add(luid);
            return luid == 3
                ? DirectXProbeResult.Unsupported
                : new DirectXProbeResult("12", luid == 1 ? "12.2" : "11.0", IsDefinitive: true);
        }

        Assert.Equal(("12", "12.2"), cache.GetOrAdd(adapterLUID: 1, Probe));
        Assert.Equal(("12", "12.2"), cache.GetOrAdd(adapterLUID: 1, Probe));
        Assert.Equal(("12", "11.0"), cache.GetOrAdd(adapterLUID: 2, Probe));
        Assert.Equal((string.Empty, string.Empty), cache.GetOrAdd(adapterLUID: 3, Probe));
        Assert.Equal((string.Empty, string.Empty), cache.GetOrAdd(adapterLUID: 3, Probe));
        Assert.Equal([1UL, 2UL, 3UL], probed);
    }

    [Fact]
    public void AnUndecidedProbeIsRetriedAfterTheDelay()
    {
        ManualTimeProvider time = new();
        DirectXFeatureLevelCache cache = new(time);
        int probes = 0;
        DirectXProbeResult answer = DirectXProbeResult.Transient;

        DirectXProbeResult Probe(ulong _)
        {
            probes++;
            return answer;
        }

        Assert.Equal((string.Empty, string.Empty), cache.GetOrAdd(adapterLUID: 1, Probe));

        // Within the delay the empty answer is returned without probing again
        time.Advance(DirectXFeatureLevelCache.RetryDelay - TimeSpan.FromSeconds(1));
        Assert.Equal((string.Empty, string.Empty), cache.GetOrAdd(adapterLUID: 1, Probe));
        Assert.Equal(expected: 1, probes);

        // After it, the adapter is probed again and a definitive answer is kept
        answer = new DirectXProbeResult("12", "12.1", IsDefinitive: true);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(("12", "12.1"), cache.GetOrAdd(adapterLUID: 1, Probe));
        time.Advance(DirectXFeatureLevelCache.RetryDelay * 10);
        Assert.Equal(("12", "12.1"), cache.GetOrAdd(adapterLUID: 1, Probe));
        Assert.Equal(expected: 2, probes);
    }

    [Theory]
    [InlineData(unchecked((int)0x887A0005), true)]   // DXGI_ERROR_DEVICE_REMOVED
    [InlineData(unchecked((int)0x887A0007), true)]   // DXGI_ERROR_DEVICE_RESET
    [InlineData(unchecked((int)0x8007000E), true)]   // E_OUTOFMEMORY
    [InlineData(unchecked((int)0x80004005), false)]  // E_FAIL: the feature level is not supported
    [InlineData(unchecked((int)0x887A0004), false)]  // DXGI_ERROR_UNSUPPORTED
    public void OnlyDeviceLossAndResourceFailuresAreTransient(int hresult, bool expectedTransient) =>
        Assert.Equal(expectedTransient, GPUAdapterNativeDetailsReader.IsTransientProbeFailure(hresult));

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp = 1;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
