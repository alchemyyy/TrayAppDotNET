using NetworkTrayAppDotNET.Services;
using Xunit;

namespace NetworkTrayAppDotNET.Tests;

public sealed class AdapterSettingsShellMonitorTests
{
    [Fact]
    public void TargetCloseWithNoOtherWindowEndsTheProcess() =>
        Assert.True(AdapterSettingsShellMonitor.ShouldEndProcess(
            destroyedTarget: true,
            targetClosedEarlier: false,
            otherVisibleWindowRemains: false));

    [Fact]
    public void AnotherVisibleWindowKeepsTheProcessUntilItClosesToo()
    {
        // The target closes while a Properties sheet of the same Explorer process is still open
        Assert.False(AdapterSettingsShellMonitor.ShouldEndProcess(
            destroyedTarget: true,
            targetClosedEarlier: false,
            otherVisibleWindowRemains: true));

        // That sheet's destroy event comes later and its class can no longer be read; it still ends the process
        Assert.True(AdapterSettingsShellMonitor.ShouldEndProcess(
            destroyedTarget: false,
            targetClosedEarlier: true,
            otherVisibleWindowRemains: false));
    }

    [Fact]
    public void OtherWindowsClosingBeforeTheTargetNeverEndTheProcess() =>
        Assert.False(AdapterSettingsShellMonitor.ShouldEndProcess(
            destroyedTarget: false,
            targetClosedEarlier: false,
            otherVisibleWindowRemains: false));

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe /factory,{5bd95610-9434-43c2-886c-57852cc8a120} -Embedding", true)]
    [InlineData(@"C:\Windows\Explorer.EXE /factory,{75dff2b7-6936-4c06-a8bb-676a7b00b24b} -Embedding", true)]
    [InlineData(@"C:\Windows\Explorer.EXE", false)]
    [InlineData(null, false)]
    public void OnlyShellFactoryExplorersAreWatched(string? commandLine, bool expected) =>
        Assert.Equal(expected, AdapterSettingsShellMonitor.IsFactoryCommandLine(commandLine));
}
