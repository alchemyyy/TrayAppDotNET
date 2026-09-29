using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessElevationProbeTests
{
    [Fact]
    public void OwnProcessDoesNotRequireElevation()
    {
        // A process can always open itself for PROCESS_SET_INFORMATION, so no shield
        ProcessTerminationTarget self = new(Environment.ProcessId, CreationTimeFileTime: 0);

        Assert.False(ProcessNativeActions.RequiresElevationForModify(self));
    }

    [Fact]
    public void ProtectedSystemProcessRequiresElevation()
    {
        // PID 4 is the Windows System process; PROCESS_SET_INFORMATION on it is denied, so it earns the shield
        ProcessTerminationTarget system = new(ProcessID: 4, CreationTimeFileTime: 0);

        Assert.True(ProcessNativeActions.RequiresElevationForModify(system));
    }

    [Fact]
    public void MissingProcessDoesNotRequireElevation()
    {
        // A PID that will not resolve fails for a non-access reason, so it must not show the shield
        ProcessTerminationTarget missing = new(ProcessID: 0x7FFFFFF0, CreationTimeFileTime: 0);

        Assert.False(ProcessNativeActions.RequiresElevationForModify(missing));
    }
}
