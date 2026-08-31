using VolumeTrayAppDotNET.Audio;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class CoreAudioSessionMTAThreadTests
{
    [Fact]
    public void ConstructorKeepsExplicitMTAAliveUntilDispose()
    {
        CoreAudioSessionMTAThread apartmentThread = new();

        Assert.True(apartmentThread.IsRunning);
        Assert.Equal(ApartmentState.MTA, apartmentThread.ApartmentState);

        apartmentThread.Dispose();

        Assert.False(apartmentThread.IsRunning);
    }

    [Fact]
    public void DisposeCanBeCalledMoreThanOnce()
    {
        CoreAudioSessionMTAThread apartmentThread = new();

        apartmentThread.Dispose();
        apartmentThread.Dispose();

        Assert.False(apartmentThread.IsRunning);
    }
}
