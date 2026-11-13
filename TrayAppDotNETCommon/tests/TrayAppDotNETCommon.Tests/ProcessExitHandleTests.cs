using System.Diagnostics;
using TrayAppDotNETCommon.Services;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class ProcessExitHandleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheExitCodeOfAProcessThatGrantsOnlyWaitAndQueryIsRead(bool waitAsynchronously)
    {
        using Process process = TestProcesses.StartWaitingCommandProcessor();
        try
        {
            // What the unelevated update restarter and uninstaller window get on their elevated helpers
            TestProcesses.GrantOnlyWaitAndQuery(process);
            ProcessExitHandle handle = ProcessExitHandle.Open(process.Id);
            Task<int> exitCode = waitAsynchronously
                ? WaitAndDisposeAsync(handle)
                : Task.Run(() =>
                {
                    using (handle) return handle.WaitForExit();
                });

            await process.StandardInput.WriteLineAsync("exit 20");
            Assert.Equal(20, await exitCode.WaitAsync(Timeout));
        }
        finally
        {
            TestProcesses.TerminateIfRunning(process);
        }
    }

    private static async Task<int> WaitAndDisposeAsync(ProcessExitHandle handle)
    {
        using (handle) return await handle.WaitForExitAsync();
    }
}
