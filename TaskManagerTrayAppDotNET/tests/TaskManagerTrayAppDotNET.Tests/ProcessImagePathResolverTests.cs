using System.Diagnostics;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessImagePathResolverTests
{
    private static readonly DriveDevicePath[] Drives =
    [
        new(DriveName: "C:", DevicePath: @"\Device\HarddiskVolume2"),
        new(DriveName: "E:", DevicePath: @"\Device\HarddiskVolume22"),
        new(DriveName: "Y:", DevicePath: @"\Device\LanmanRedirector\;Y:00000000000520c6\server\share")
    ];

    [Fact]
    public void ConvertsTheMatchingDevicePrefixToItsDriveLetter()
    {
        Assert.True(ProcessImagePathResolver.TryConvertToDrivePath(
            @"\Device\HarddiskVolume2\Windows\System32\svchost.exe",
            Drives,
            out string systemPath));
        Assert.Equal(@"C:\Windows\System32\svchost.exe", systemPath);

        // HarddiskVolume2 is a string prefix of HarddiskVolume22 but not its device
        Assert.True(ProcessImagePathResolver.TryConvertToDrivePath(
            @"\Device\HarddiskVolume22\Games\game.exe",
            Drives,
            out string gamePath));
        Assert.Equal(@"E:\Games\game.exe", gamePath);

        Assert.True(ProcessImagePathResolver.TryConvertToDrivePath(
            @"\Device\LanmanRedirector\;Y:00000000000520c6\server\share\tools\tool.exe",
            Drives,
            out string networkPath));
        Assert.Equal(@"Y:\tools\tool.exe", networkPath);
    }

    [Fact]
    public void LeavesAPathOnNoDriveUnconverted()
    {
        Assert.False(ProcessImagePathResolver.TryConvertToDrivePath(
            @"\Device\Mup\server\share\tool.exe",
            Drives,
            out string path));
        Assert.Equal(string.Empty, path);
        Assert.False(ProcessImagePathResolver.TryConvertToDrivePath(
            @"\Device\HarddiskVolume2",
            Drives,
            out _));
    }

    [Fact]
    public void ResolvesTheCurrentProcessImagePath()
    {
        ProcessImagePathResolver resolver = new();

        string path = resolver.Resolve(Environment.ProcessId);

        Assert.Equal(Environment.ProcessPath, path, ignoreCase: true);
    }

    [Fact]
    public void ResolvesAProcessThatCannotBeOpened()
    {
        // The query needs no process handle, so it reaches smss.exe, which a non-elevated caller cannot open
        Process sessionManager = Assert.Single(Process.GetProcessesByName("smss"));
        ProcessImagePathResolver resolver = new();

        string path = resolver.Resolve(sessionManager.Id);

        Assert.Equal(Path.Combine(Environment.SystemDirectory, "smss.exe"), path, ignoreCase: true);
    }

    [Fact]
    public void PseudoProcessesHaveNoImagePath()
    {
        ProcessImagePathResolver resolver = new();

        Assert.Equal(string.Empty, resolver.Resolve(processID: 0));
        Assert.Equal(string.Empty, resolver.Resolve(processID: 4));
        foreach (Process registry in Process.GetProcessesByName("Registry"))
            Assert.Equal(string.Empty, resolver.Resolve(registry.Id));
    }
}
