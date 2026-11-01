using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ElevatedStartTaskTests
{
    private const string SystemExecutable = @"C:\Program Files\TrayAppDotNET\TaskManagerTrayAppDotNET.exe";
    private const string UserSid = "S-1-5-21-1000-2000-3000-1001";

    [Theory]
    [InlineData("", "--elevated-start-task")]
    [InlineData("", "--elevated-start-task", "$(Arg0)")]
    [InlineData("--autostart", "--elevated-start-task", "--autostart")]
    [InlineData("--hidden", "--elevated-start-task", "--hidden")]
    [InlineData("--autostart --hidden", "--elevated-start-task", "--hidden", "--autostart")]
    public void TaskStartBecomesAPlainStartWithItsStartFlags(string expected, params string[] arguments)
    {
        Assert.True(ElevatedStartTask.TryGetTaskLaunchArguments(arguments, out string[] launchArguments));
        Assert.Equal(expected, string.Join(' ', launchArguments));
    }

    [Theory]
    [InlineData("--elevated-start-task", "--elevated-broker", "--broker-parent", "4", "--broker-parent-start", "1",
        "--broker-pipe", "planted")]
    [InlineData("--elevated-start-task", "--set-taskmgr-replacement", "on")]
    [InlineData("--elevated-start-task", "--set-elevated-start", "on")]
    [InlineData("--elevated-start-task", "--settings-dir", @"C:\Users\Public\planted")]
    [InlineData("--elevated-start-task", "--uninstall-prepare", "--scope", "system")]
    [InlineData("--elevated-start-task", "--autostart", "--autostart")]
    [InlineData("--elevated-start-task", "--AUTOSTART")]
    [InlineData("--elevated-start-task", "x --kill-helper y")]
    [InlineData("--elevated-start-task", "")]
    [InlineData("--elevated-start-task", "$(Arg0)", "--hidden")]
    [InlineData("--autostart", "--elevated-start-task")]
    [InlineData("--ELEVATED-START-TASK")]
    public void TaskStartRefusesAnythingElseItsParameterCouldCarry(params string[] arguments)
    {
        // Program.Main sends every start that carries the marker here first, so none of these reaches another mode
        Assert.True(ElevatedStartTask.IsTaskLaunch(arguments));
        Assert.False(ElevatedStartTask.TryGetTaskLaunchArguments(arguments, out _));
    }

    [Fact]
    public void StartsWithoutTheMarkerAreNotTaskStarts()
    {
        Assert.False(ElevatedStartTask.IsTaskLaunch([]));
        Assert.False(ElevatedStartTask.IsTaskLaunch(["--autostart", "--hidden"]));
        Assert.False(ElevatedStartTask.IsTaskLaunch(["--set-elevated-start", "on"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--autostart", "--autostart")]
    [InlineData("--hidden", "--hidden")]
    [InlineData("--autostart --hidden", "--autostart", "--hidden")]
    [InlineData("--autostart --hidden", "--HIDDEN", "--Autostart")]
    public void PlainStartsReachTheElevatedStartWithTheirFlags(string expected, params string[] arguments)
    {
        Assert.True(ElevatedStartTask.TryGetHandOffParameter(arguments, out string parameter));

        // Task Scheduler puts the parameter in place of $(Arg0) in the task's action
        string[] taskArguments = ElevatedStartTask.TaskActionArguments
            .Replace("$(Arg0)", parameter, StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.True(ElevatedStartTask.TryGetTaskLaunchArguments(taskArguments, out string[] launchArguments));
        Assert.Equal(expected, string.Join(' ', launchArguments));
    }

    [Fact]
    public void RedirectedTaskManagerStartHandsOffAsAPlainStart()
    {
        Assert.True(ElevatedStartTask.TryGetHandOffParameter(
            [@"C:\Windows\System32\Taskmgr.exe", "/0", "/4"],
            out string parameter));
        Assert.Equal(string.Empty, parameter);
    }

    [Theory]
    [InlineData("--settings-dir", @"D:\TaskManagerData")]
    [InlineData("--autostart", "--settings-dir", @"D:\TaskManagerData")]
    [InlineData("--watcher")]
    [InlineData("--monitored", "--watcher-pid", "1234")]
    [InlineData("--installer")]
    [InlineData("--uninstall-headless", "local")]
    [InlineData("--update-restart")]
    public void StartsThatAreNotPlainStayInTheirOwnProcess(params string[] arguments)
    {
        Assert.False(ElevatedStartTask.TryGetHandOffParameter(arguments, out _));
    }

    [Fact]
    public void TheAppsDefinitionStartsOnlyTheSystemInstallElevated()
    {
        string definition = ElevatedStartTask.BuildTaskDefinition(SystemExecutable, UserSid);

        Assert.True(ElevatedStartTask.IsUsableDefinition(definition, SystemExecutable));
        // Task Scheduler returns definitions with an XML declaration
        Assert.True(ElevatedStartTask.IsUsableDefinition(
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + definition,
            SystemExecutable));
        Assert.False(ElevatedStartTask.IsUsableDefinition(definition, @"D:\portable\TaskManagerTrayAppDotNET.exe"));
    }

    [Theory]
    [InlineData("<RunLevel>HighestAvailable</RunLevel>", "<RunLevel>LeastPrivilege</RunLevel>")]
    [InlineData("<LogonType>InteractiveToken</LogonType>", "<LogonType>Password</LogonType>")]
    [InlineData("<Priority>4</Priority>", "<Priority>4</Priority><Enabled>false</Enabled>")]
    [InlineData("--elevated-start-task $(Arg0)", "--elevated-broker $(Arg0)")]
    [InlineData("--elevated-start-task $(Arg0)", @"--elevated-start-task --settings-dir C:\Users\Public $(Arg0)")]
    [InlineData(@"<Command>C:\Program Files\", @"<Command>C:\Users\Public\")]
    [InlineData("</Exec>", @"</Exec><Exec><Command>C:\Users\Public\other.exe</Command></Exec>")]
    public void ChangedDefinitionsAreNotUsable(string original, string replacement)
    {
        string definition = ElevatedStartTask.BuildTaskDefinition(SystemExecutable, UserSid);
        Assert.Contains(original, definition, StringComparison.Ordinal);

        string changed = definition.Replace(original, replacement, StringComparison.Ordinal);

        Assert.False(ElevatedStartTask.IsUsableDefinition(changed, SystemExecutable));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<Task")]
    [InlineData("<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\" />")]
    public void MissingOrMalformedDefinitionsAreNotUsable(string? definition)
    {
        Assert.False(ElevatedStartTask.IsUsableDefinition(definition, SystemExecutable));
    }

    [Fact]
    public void TurningOnNeedsAnAdministratorRunningTheSystemInstall()
    {
        ElevatedStartState off = new(IsRegistered: false, IsUsable: false, TaskPath: string.Empty);
        ElevatedStartState on = new(IsRegistered: true, IsUsable: true, TaskPath: string.Empty);

        Assert.True(ElevatedStartTask.CanToggle(new ElevatedStartAvailability(IsSystemInstall: true, AccountCanElevate: true), off));
        Assert.False(ElevatedStartTask.CanToggle(new ElevatedStartAvailability(IsSystemInstall: false, AccountCanElevate: true), off));
        Assert.False(ElevatedStartTask.CanToggle(new ElevatedStartAvailability(IsSystemInstall: true, AccountCanElevate: false), off));

        // A registered task can be turned off from any copy, but only by an administrator
        Assert.True(ElevatedStartTask.CanToggle(new ElevatedStartAvailability(IsSystemInstall: false, AccountCanElevate: true), on));
        Assert.False(ElevatedStartTask.CanToggle(new ElevatedStartAvailability(IsSystemInstall: true, AccountCanElevate: false), on));
    }
}
