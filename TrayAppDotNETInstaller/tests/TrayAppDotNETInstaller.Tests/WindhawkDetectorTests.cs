using System.Security;
using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class WindhawkDetectorTests
{
    [Fact]
    public void Aggregate_NoHits_ReportsNotInstalled()
    {
        WindhawkProbe[] probes =
        [
            new("first", static () => null),
            new("second", static () => null)
        ];

        WindhawkDetection detection = WindhawkDetector.Aggregate(probes);

        Assert.False(detection.IsInstalled);
        Assert.Null(detection.Evidence);
    }

    [Fact]
    public void Aggregate_OneHit_ReportsInstalledWithNamedEvidence()
    {
        int laterProbeCalls = 0;
        WindhawkProbe[] probes =
        [
            new("first", static () => null),
            new("program files", static () => @"C:\Program Files\Windhawk\windhawk.exe"),
            new("never", () =>
            {
                laterProbeCalls++;
                return "unreached";
            })
        ];

        WindhawkDetection detection = WindhawkDetector.Aggregate(probes);

        Assert.True(detection.IsInstalled);
        Assert.Equal(@"program files: C:\Program Files\Windhawk\windhawk.exe", detection.Evidence);
        Assert.Equal(0, laterProbeCalls);
    }

    [Fact]
    public void Aggregate_SkipsProbesThatThrowAccessErrors()
    {
        WindhawkProbe[] probes =
        [
            new("registry", static () => throw new SecurityException("denied")),
            new("disk", static () => throw new IOException("busy")),
            new("process", static () => "windhawk PID 42")
        ];

        WindhawkDetection detection = WindhawkDetector.Aggregate(probes);

        Assert.True(detection.IsInstalled);
        Assert.Equal("process: windhawk PID 42", detection.Evidence);
    }

    [Fact]
    public void Aggregate_EmptyProbeList_ReportsNotInstalled()
    {
        WindhawkDetection detection = WindhawkDetector.Aggregate([]);

        Assert.False(detection.IsInstalled);
    }

    [Fact]
    public void DefaultProbes_RunWithoutThrowing()
    {
        // Result depends on the machine; the real probes only need to complete
        WindhawkDetection detection = WindhawkDetector.Detect();

        Assert.Equal(detection.IsInstalled, detection.Evidence != null);
    }
}
