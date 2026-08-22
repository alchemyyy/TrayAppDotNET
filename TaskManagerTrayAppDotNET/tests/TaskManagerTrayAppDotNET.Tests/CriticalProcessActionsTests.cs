using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class CriticalProcessActionsTests
{
    [Fact]
    public void TryTerminateRefusesTheTaskManagerProcess()
    {
        bool result = CriticalProcessActions.TryTerminate(Environment.ProcessId, out string errorMessage);

        Assert.False(result);
        Assert.NotEmpty(errorMessage);
    }
}
