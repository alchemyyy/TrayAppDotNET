using System.Diagnostics;
using TrayAppDotNETCommon.Utils;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class ProcessImagePathTests
{
    [Fact]
    public void ResolvesTheCurrentProcessToItsExecutable()
    {
        string? path = ProcessImagePath.TryGet(Environment.ProcessId);

        Assert.NotNull(path);
        Assert.Equal(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(path), ignoreCase: true);
    }

    [Fact]
    public void ReturnsNullForTheIdleProcessAndAnExitedOne()
    {
        Assert.Null(ProcessImagePath.TryGet(0));

        using Process exited = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        int processID = exited.Id;
        exited.WaitForExit();
        exited.Dispose();

        // Once the last handle closes the ID is free; a reuse would resolve to some other image, never cmd.exe
        string? path = ProcessImagePath.TryGet(processID);
        Assert.True(path == null || !path.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase));
    }
}
