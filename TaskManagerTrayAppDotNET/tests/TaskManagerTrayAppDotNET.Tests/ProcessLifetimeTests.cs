using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessLifetimeTests
{
    [Fact]
    public void FormatsClockWithDaysOnlyWhenNeeded()
    {
        TimeSpan multiDayLifetime = new(1, 8, 10, 22);
        TimeSpan sameDayLifetime = new(0, 16, 12, 1);

        Assert.Equal("1d 8:10:22", ProcessLifetime.Format(multiDayLifetime.Ticks));
        Assert.Equal("16:12:01", ProcessLifetime.Format(sameDayLifetime.Ticks));
        Assert.Equal("0:00:00", ProcessLifetime.Format(0));
    }

    [Fact]
    public void TruncatesSubsecondLifetime()
    {
        TimeSpan lifetime = TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(999);

        Assert.Equal("0:00:01", ProcessLifetime.Format(lifetime.Ticks));
    }

    [Fact]
    public void CalculatesElapsedFileTimeTicksAndRejectsInvalidTimes()
    {
        long creationTimeTicks = DateTime.UtcNow.ToFileTimeUtc();
        long sampleTimeTicks = creationTimeTicks + TimeSpan.FromHours(2).Ticks;

        Assert.Equal(
            TimeSpan.FromHours(2).Ticks,
            ProcessLifetime.CalculateTicks(creationTimeTicks, sampleTimeTicks));
        Assert.Equal(ProcessLifetime.UnavailableTicks, ProcessLifetime.CalculateTicks(0, sampleTimeTicks));
        Assert.Equal(
            ProcessLifetime.UnavailableTicks,
            ProcessLifetime.CalculateTicks(sampleTimeTicks, creationTimeTicks));
    }
}
