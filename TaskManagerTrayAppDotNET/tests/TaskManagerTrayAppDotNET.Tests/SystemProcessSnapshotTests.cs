using System.Diagnostics;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class SystemProcessSnapshotTests
{
    [Fact]
    public void CaptureReturnsCoherentDataForCurrentProcess()
    {
        using SystemProcessSnapshot snapshot = new();
        Dictionary<int, SystemProcessData> processes = [];

        bool captured = snapshot.TryCapture(processes);

        Assert.True(captured);
        Assert.True(processes.TryGetValue(Environment.ProcessId, out SystemProcessData current));
        Assert.True(current.CreationTimeTicks > 0);
        Assert.True(current.TotalProcessorTicks >= 0);
        Assert.True(current.WorkingSetBytes > 0);
        Assert.True(current.PrivateWorkingSetBytes >= 0);
        Assert.True(current.PeakWorkingSetBytes >= current.WorkingSetBytes);
        Assert.True(current.HandleCount > 0);
        Assert.True(current.SessionID >= 0);
        Assert.True(current.ThreadCount > 0);
        Assert.True(current.HasDiskCounters);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ReadImageName(current)));

        using Process process = Process.GetCurrentProcess();
        Assert.InRange(current.ThreadCount, 1, process.Threads.Count + 8);
    }

    [Fact]
    public void FullCaptureExposesJobObjectIDsWhenTheOperatingSystemPermitsIt()
    {
        using SystemProcessSnapshot snapshot = new();
        Dictionary<int, SystemProcessData> processes = [];

        bool captured = snapshot.TryCapture(processes, true);

        Assert.True(captured);
        Assert.True(processes.TryGetValue(Environment.ProcessId, out SystemProcessData current));
        if (snapshot.HasJobObjectIDs)
            Assert.True(current.JobObjectID >= 0);
        else
            Assert.Equal(-1, current.JobObjectID);
        Assert.NotEqual(ProcessExecutionState.Suspended, snapshot.ReadExecutionState(current));
    }

    [Fact]
    public void NominalProcessorCapacityIsAvailable()
    {
        Assert.True(NativeProcessInfo.ReadNominalProcessorCycleCapacity() > 0);
    }
}
