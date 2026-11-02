using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerReplacementTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32\Taskmgr.exe")]
    [InlineData(@"C:\Windows\System32\taskmgr.exe")]
    [InlineData("TASKMGR.EXE")]
    [InlineData("taskmgr.exe")]
    public void IsRedirectedLaunchDetectsTaskManagerImageAsFirstArgument(string firstArgument)
    {
        Assert.True(TaskManagerReplacement.IsRedirectedLaunch([firstArgument]));
        // Trailing switches Windows appends (for example /0 /4 /7 /d) do not change the decision
        Assert.True(TaskManagerReplacement.IsRedirectedLaunch([firstArgument, "/0", "/4", "/7", "/d"]));
    }

    [Fact]
    public void IsRedirectedLaunchIgnoresNonRedirectArguments()
    {
        Assert.False(TaskManagerReplacement.IsRedirectedLaunch([]));
        Assert.False(TaskManagerReplacement.IsRedirectedLaunch(["--autostart"]));
        Assert.False(TaskManagerReplacement.IsRedirectedLaunch(["--watcher-pid", "1234"]));
        Assert.False(TaskManagerReplacement.IsRedirectedLaunch([@"C:\Windows\System32\notepad.exe"]));
        Assert.False(TaskManagerReplacement.IsRedirectedLaunch([""]));
    }

    private const string SystemExecutable = @"C:\Program Files\TrayAppDotNET\TaskManagerTrayAppDotNET.exe";

    [Theory]
    [InlineData(@"C:\Program Files\TrayAppDotNET\TaskManagerTrayAppDotNET.exe")]
    [InlineData(@"c:\program files\trayappdotnet\taskmanagertrayappdotnet.exe")]
    [InlineData("\"C:\\Program Files\\TrayAppDotNET\\TaskManagerTrayAppDotNET.exe\"")]
    [InlineData(@"C:\Program Files\TrayAppDotNET\.\TaskManagerTrayAppDotNET.exe")]
    public void OwnershipMatchesTheExactExecutablePath(string registeredExecutable) =>
        Assert.True(TaskManagerReplacement.IsOwnedBy(registeredExecutable, [SystemExecutable]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"D:\portable\TaskManagerTrayAppDotNET.exe")]
    [InlineData(@"C:\Users\someone\AppData\Local\TrayAppDotNET\apps\TaskManagerTrayAppDotNET.exe")]
    [InlineData("TaskManagerTrayAppDotNET.exe")]
    [InlineData(@"C:\Windows\System32\Taskmgr.exe")]
    [InlineData(@"C:\Tools\SystemInformer\SystemInformer.exe")]
    public void OwnershipRejectsTheSameFileNameElsewhereAndOtherTargets(string? registeredExecutable) =>
        Assert.False(TaskManagerReplacement.IsOwnedBy(registeredExecutable, [SystemExecutable]));

    [Fact]
    public void OwnershipIgnoresMissingOrRelativeCandidates()
    {
        Assert.False(TaskManagerReplacement.IsOwnedBy(SystemExecutable, [null, "", "TaskManagerTrayAppDotNET.exe"]));
        Assert.True(TaskManagerReplacement.IsOwnedBy(SystemExecutable, [null, SystemExecutable]));
    }

    [Fact]
    public void TryHandleElevatedConfigureIgnoresLaunchesWithoutTheConfigureArgument()
    {
        bool handled = TaskManagerReplacement.TryHandleElevatedConfigure(["--autostart"], out int exitCode);

        Assert.False(handled);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void TryHandleElevatedConfigureRejectsMissingMode()
    {
        bool handled = TaskManagerReplacement.TryHandleElevatedConfigure(["--set-taskmgr-replacement"], out int exitCode);

        Assert.True(handled);
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void TryHandleElevatedConfigureRejectsInvalidMode()
    {
        bool handled = TaskManagerReplacement.TryHandleElevatedConfigure(
            ["--set-taskmgr-replacement", "bogus"],
            out int exitCode);

        Assert.True(handled);
        Assert.NotEqual(0, exitCode);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\TrayAppDotNET\\TaskManagerTrayAppDotNET.exe\"", @"C:\Program Files\TrayAppDotNET\TaskManagerTrayAppDotNET.exe")]
    [InlineData("\"C:\\App\\tmtadn.exe\" \"C:\\Windows\\System32\\Taskmgr.exe\"", @"C:\App\tmtadn.exe")]
    [InlineData(@"C:\App\tmtadn.exe", @"C:\App\tmtadn.exe")]
    [InlineData(@"C:\App\tmtadn.exe /flag", @"C:\App\tmtadn.exe")]
    public void ExtractExecutableUnquotesAndDropsArguments(string debuggerValue, string expected) =>
        Assert.Equal(expected, IfeoRegistry.ExtractExecutable(debuggerValue));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"")]
    [InlineData("\"\"")]
    public void ExtractExecutableReturnsNullForEmptyOrMalformedValues(string? debuggerValue) =>
        Assert.Null(IfeoRegistry.ExtractExecutable(debuggerValue));

    [Fact]
    public void ToggleNeedsASystemInstallUnlessTurningOurRedirectOff()
    {
        TaskManagerReplacementState off = new(IsEnabled: false, PointsElsewhere: false, RegisteredExecutable: null);
        TaskManagerReplacementState elsewhere = new(IsEnabled: false, PointsElsewhere: true, @"C:\Tools\pe.exe");
        TaskManagerReplacementState ours = new(IsEnabled: true, PointsElsewhere: false, SystemExecutable);

        Assert.False(TaskManagerReplacement.CanToggle(systemInstallExists: false, off));
        Assert.False(TaskManagerReplacement.CanToggle(systemInstallExists: false, elsewhere));
        Assert.True(TaskManagerReplacement.CanToggle(systemInstallExists: false, ours));
        Assert.True(TaskManagerReplacement.CanToggle(systemInstallExists: true, off));
    }
}
