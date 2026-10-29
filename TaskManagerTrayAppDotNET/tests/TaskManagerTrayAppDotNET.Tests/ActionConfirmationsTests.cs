using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ActionConfirmationsTests
{
    [Fact]
    public async Task ConfirmedActionRunsAfterTheUserConfirms()
    {
        ActionConfirmation expected = ActionConfirmations.EndProcessTree([CreateProcess(10, "app.exe")]);
        List<ActionConfirmation> shown = [];
        int actionRuns = 0;

        bool ran = await ActionConfirmations.RunIfConfirmedAsync(
            expected,
            confirmation =>
            {
                shown.Add(confirmation);
                return Task.FromResult(true);
            },
            () =>
            {
                actionRuns++;
                return Task.CompletedTask;
            });

        Assert.True(ran);
        Assert.Equal(1, actionRuns);
        Assert.Equal(expected, Assert.Single(shown));
    }

    [Fact]
    public async Task CanceledConfirmationLeavesTheActionUnrun()
    {
        int actionRuns = 0;

        bool ran = await ActionConfirmations.RunIfConfirmedAsync(
            ActionConfirmations.SetPriority([CreateProcess(10, "app.exe")]),
            static _ => Task.FromResult(false),
            () =>
            {
                actionRuns++;
                return Task.CompletedTask;
            });

        Assert.False(ran);
        Assert.Equal(0, actionRuns);
    }

    [Fact]
    public void EndProcessTreeNamesOneProcessAndCountsSeveral()
    {
        ActionConfirmation single = ActionConfirmations.EndProcessTree([CreateProcess(10, "app.exe")]);
        ActionConfirmation multiple = ActionConfirmations.EndProcessTree(
            [CreateProcess(10, "app.exe"), CreateProcess(11, "other.exe")]);

        Assert.Equal("Do you want to end the process tree of app.exe?", single.Title);
        Assert.Equal("End process tree", single.ConfirmText);
        Assert.Contains("unsaved data", single.Message);
        Assert.Contains("system instability", single.Message);
        Assert.Equal("Do you want to end the process trees of 2 selected processes?", multiple.Title);
        Assert.Equal("End process trees", multiple.ConfirmText);
        Assert.Contains("system instability", multiple.Message);
        Assert.Equal("Cancel", single.CancelText);
        Assert.Equal("Cancel", multiple.CancelText);
    }

    [Fact]
    public void SetPriorityWarnsAboutInstabilityForOneOrSeveralProcesses()
    {
        ActionConfirmation single = ActionConfirmations.SetPriority([CreateProcess(10, string.Empty)]);
        ActionConfirmation multiple = ActionConfirmations.SetPriority(
            [CreateProcess(10, "app.exe"), CreateProcess(11, "other.exe"), CreateProcess(12, "third.exe")]);

        Assert.Equal("Do you want to change the priority of PID 10?", single.Title);
        Assert.Equal("Change priority", single.ConfirmText);
        Assert.Equal(
            "Changing the priority of certain processes could cause system instability.",
            single.Message);
        Assert.Equal("Do you want to change the priority of 3 selected processes?", multiple.Title);
        Assert.Equal("Change priorities", multiple.ConfirmText);
        Assert.Equal(single.Message, multiple.Message);
    }

    [Fact]
    public void EndTaskKeepsItsSingularAndPluralWording()
    {
        ActionConfirmation single = ActionConfirmations.EndTask([CreateProcess(10, "app.exe")]);
        ActionConfirmation multiple = ActionConfirmations.EndTask(
            [CreateProcess(10, "app.exe"), CreateProcess(11, "other.exe")]);

        Assert.Equal("Do you want to end app.exe?", single.Title);
        Assert.Equal("End process", single.ConfirmText);
        Assert.Equal("Do you want to end 2 selected processes?", multiple.Title);
        Assert.Equal("End processes", multiple.ConfirmText);
    }

    [Fact]
    public void DisconnectNamesTheUserAndCallsOutTheOwnSession()
    {
        UserSessionInfo session = new(
            SessionID: 2,
            UserName: "alex",
            DomainName: "PC",
            StationName: "Console",
            UserSessionState.Active);

        ActionConfirmation other = ActionConfirmations.Disconnect(session, currentSessionID: 1);
        ActionConfirmation own = ActionConfirmations.Disconnect(session, currentSessionID: 2);

        Assert.Equal("Do you want to disconnect PC\\alex?", other.Title);
        Assert.DoesNotContain("your own session", other.Message);
        Assert.Equal("Do you want to disconnect your own session (PC\\alex)?", own.Title);
        Assert.Contains("your own session", own.Message);
        Assert.Equal("Disconnect user", own.ConfirmText);
        Assert.Equal("Cancel", own.CancelText);
    }

    private static ProcessEndTaskItem CreateProcess(int processID, string processName) =>
        new(
            new ProcessTerminationTarget(processID, CreationTimeFileTime: processID * 100L),
            processName);
}
