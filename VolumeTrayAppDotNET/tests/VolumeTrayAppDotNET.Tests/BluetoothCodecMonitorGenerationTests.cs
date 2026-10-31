using VolumeTrayAppDotNET.Audio;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class BluetoothCodecMonitorGenerationTests
{
    [Fact]
    public void OnlyEventsOfTheLiveSessionUpdateTheCodec()
    {
        Assert.True(BluetoothCodecMonitor.IsCurrentGeneration(eventGeneration: 3, currentGeneration: 3));

        // Stop retired session 3 (generation 4) or a new Start opened session 5: a late event of 3 is dropped
        Assert.False(BluetoothCodecMonitor.IsCurrentGeneration(eventGeneration: 3, currentGeneration: 4));
        Assert.False(BluetoothCodecMonitor.IsCurrentGeneration(eventGeneration: 3, currentGeneration: 5));
    }
}
