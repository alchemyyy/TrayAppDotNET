using Xunit;

namespace TrayAppDotNETCommon.Tests;

/// <summary>Where a process's log goes: never the real profile once a settings folder override is set.</summary>
public sealed class TADNLogLocationTests
{
    [Fact]
    public void AProcessThatLogsBeforeInitializeWritesIntoTheSettingsFolderOverride()
    {
        string settingsDirectory = Path.Combine(Path.GetTempPath(), "TADN settings override");

        Assert.Equal(settingsDirectory, TADNLog.DefaultLogDirectory(settingsDirectory, processName: "testhost"));
    }

    [Fact]
    public void WithoutAnOverrideTheLogFolderIsNamedAfterTheProcessUnderTheSharedRoot()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            TrayAppDotNETConstants.SharedRootFolderName,
            "testhost");

        Assert.Equal(expected, TADNLog.DefaultLogDirectory(settingsDirectoryOverride: null, processName: "testhost"));
    }

    [Fact]
    public void ThisTestProcessLogsIntoItsRedirectedSettingsFolder()
    {
        string settingsDirectory = Assert.IsType<string>(TrayAppDotNETProgram.SettingsDirectoryOverride);
        string marker = "TADNLogLocationTests " + Guid.NewGuid().ToString("N");

        TADNLog.Log(marker);
        TADNLog.Flush();

        string activeLogPath = Assert.IsType<string>(TADNLog.ActiveLogPath);
        Assert.Equal(
            Path.GetFullPath(settingsDirectory),
            Path.GetDirectoryName(Path.GetFullPath(activeLogPath)),
            ignoreCase: true);
        Assert.Contains(marker, File.ReadAllText(activeLogPath));
    }
}
