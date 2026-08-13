using BrightnessTrayAppDotNET.Interop.NightLight;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class NightLightRegistryTests
{
    [Fact]
    public void InitializedEnabledStateReportsEnabled()
    {
        byte[] blob = BuildStateBlob(isInitialized: true, hasEnabledMarker: true);

        NightLightStateStatus status = NightLightRegistry.InspectStateBlob(blob);

        Assert.True(status.IsInitialized);
        Assert.True(status.IsEnabled);
    }

    [Fact]
    public void InitializedOffStateReportsDisabled()
    {
        byte[] blob = BuildStateBlob(isInitialized: true, hasEnabledMarker: false);

        NightLightStateStatus status = NightLightRegistry.InspectStateBlob(blob);

        Assert.True(status.IsInitialized);
        Assert.False(status.IsEnabled);
    }

    [Fact]
    public void EnabledMarkerWithoutInitializationRemainsInert()
    {
        byte[] blob = BuildStateBlob(isInitialized: false, hasEnabledMarker: true);

        NightLightStateStatus status = NightLightRegistry.InspectStateBlob(blob);

        Assert.False(status.IsInitialized);
        Assert.False(status.IsEnabled);
    }

    [Fact]
    public void MissingStateRemainsInert()
    {
        NightLightStateStatus status = NightLightRegistry.InspectStateBlob(null);

        Assert.False(status.IsInitialized);
        Assert.False(status.IsEnabled);
    }

    private static byte[] BuildStateBlob(bool isInitialized, bool hasEnabledMarker)
    {
        List<byte> inner = [0x43, 0x42, 0x01, 0x00];
        if (hasEnabledMarker)
            inner.AddRange([0x10, 0x00]);
        if (isInitialized)
            inner.AddRange([0xD0, 0x0A, 0x02]);
        inner.Add(0x00);

        List<byte> blob =
        [
            0x43, 0x42, 0x01, 0x00,
            0x0A, 0x02, 0x01, 0x00, 0x2A, 0x06,
            0x01,
            0x2A, 0x2B, 0x0E,
            (byte)inner.Count
        ];
        blob.AddRange(inner);
        return [.. blob];
    }
}
