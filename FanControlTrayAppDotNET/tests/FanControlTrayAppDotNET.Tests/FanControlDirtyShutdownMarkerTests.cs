using FanControlTrayAppDotNET.Services;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanControlDirtyShutdownMarkerTests
{
    private static readonly DateTime Boot = new(2026, 10, 27, 7, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Verifies the boot time is wall time minus monotonic uptime, so two reads in one boot agree.
    /// </summary>
    [Fact]
    public void BootTimeIsWallTimeMinusUptime()
    {
        DateTime early = FanControlDirtyShutdownMarker.BootTimeUtc(Boot.AddMinutes(5), tickCount64Ms: 300_000);
        DateTime late = FanControlDirtyShutdownMarker.BootTimeUtc(Boot.AddHours(3), tickCount64Ms: 10_800_000);

        Assert.Equal(Boot, early);
        Assert.Equal(Boot, late);
    }

    /// <summary>
    /// Verifies a marker from this boot (within tolerance) makes firmware defaults untrusted, while a marker from
    /// another boot, a missing marker, or an unreadable one leaves them trusted.
    /// </summary>
    [Fact]
    public void SameBootMarkerMeansUntrustedDefaults()
    {
        string sameBoot = FanControlDirtyShutdownMarker.Format(Boot.AddSeconds(3));
        string atTolerance = FanControlDirtyShutdownMarker.Format(
            Boot.AddMilliseconds(-TimeConstants.BootIdentityToleranceMs));
        string earlierBoot = FanControlDirtyShutdownMarker.Format(Boot.AddHours(-1));

        Assert.False(FanControlDirtyShutdownMarker.AreFirmwareDefaultsTrusted(sameBoot, Boot));
        Assert.False(FanControlDirtyShutdownMarker.AreFirmwareDefaultsTrusted(atTolerance, Boot));
        Assert.True(FanControlDirtyShutdownMarker.AreFirmwareDefaultsTrusted(earlierBoot, Boot));
        Assert.True(FanControlDirtyShutdownMarker.AreFirmwareDefaultsTrusted(markerText: null, Boot));
        Assert.True(FanControlDirtyShutdownMarker.AreFirmwareDefaultsTrusted("not a boot time", Boot));
    }

    /// <summary>
    /// Verifies the stored boot identity round-trips.
    /// </summary>
    [Fact]
    public void FormatRoundTrips()
    {
        Assert.True(FanControlDirtyShutdownMarker.TryParse(FanControlDirtyShutdownMarker.Format(Boot), out DateTime parsed));
        Assert.Equal(Boot, parsed);
    }

    /// <summary>
    /// Verifies the marker file lifecycle: written on first software control, removed after a clean restore when
    /// defaults were trusted, kept when a same-boot marker made them untrusted, and dropped when from another boot.
    /// </summary>
    [Fact]
    public void MarkerFileLifecycle()
    {
        string directory = Path.Combine(Path.GetTempPath(), "FanControlTrayAppDotNET.Tests", "ShutdownMarker",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, FanControlDirtyShutdownMarker.FileName);
        try
        {
            FanControlDirtyShutdownMarker clean = new(path);
            clean.Load(Boot);
            Assert.True(clean.FirmwareDefaultsTrusted);
            clean.MarkSoftwareControl(Boot);
            Assert.True(File.Exists(path));

            FanControlDirtyShutdownMarker afterCrash = new(path);
            afterCrash.Load(Boot.AddSeconds(2));
            Assert.False(afterCrash.FirmwareDefaultsTrusted);
            afterCrash.ClearAfterCleanRestore();
            Assert.True(File.Exists(path));

            FanControlDirtyShutdownMarker afterReboot = new(path);
            afterReboot.Load(Boot.AddHours(2));
            Assert.True(afterReboot.FirmwareDefaultsTrusted);
            Assert.False(File.Exists(path));

            afterReboot.MarkSoftwareControl(Boot.AddHours(2));
            Assert.True(File.Exists(path));
            afterReboot.ClearAfterCleanRestore();
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
