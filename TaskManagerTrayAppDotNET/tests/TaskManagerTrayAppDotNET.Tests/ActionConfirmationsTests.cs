using TaskManagerTrayAppDotNET.Models;
using TrayAppDotNETCommon.Localization;
using AppStrings = TaskManagerTrayAppDotNET.Localization.Strings;
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

        Assert.Equal(Format(nameof(AppStrings.Confirm_EndTaskTree_SingleTitleFormat), "app.exe"), single.Title);
        Assert.Equal(L(nameof(AppStrings.Confirm_EndTaskTree_SingleButton)), single.ConfirmText);
        Assert.Equal(L(nameof(AppStrings.Confirm_EndTaskTree_SingleMessage)), single.Message);
        Assert.Equal(Format(nameof(AppStrings.Confirm_EndTaskTree_MultipleTitleFormat), 2), multiple.Title);
        Assert.Equal(L(nameof(AppStrings.Confirm_EndTaskTree_MultipleButton)), multiple.ConfirmText);
        Assert.Equal(L(nameof(AppStrings.Confirm_EndTaskTree_MultipleMessage)), multiple.Message);
        Assert.Equal(L(nameof(CommonStrings.Common_Cancel)), single.CancelText);
        Assert.Equal(single.CancelText, multiple.CancelText);
    }

    [Fact]
    public void SetPriorityFallsBackToThePIDAndSharesTheWarning()
    {
        ActionConfirmation single = ActionConfirmations.SetPriority([CreateProcess(10, string.Empty)]);
        ActionConfirmation multiple = ActionConfirmations.SetPriority(
            [CreateProcess(10, "app.exe"), CreateProcess(11, "other.exe"), CreateProcess(12, "third.exe")]);

        Assert.Equal(
            Format(nameof(AppStrings.Confirm_SetPriority_SingleTitleFormat), Format(nameof(AppStrings.Confirm_ProcessIDFormat), 10)),
            single.Title);
        Assert.Equal(L(nameof(AppStrings.Confirm_SetPriority_SingleButton)), single.ConfirmText);
        Assert.Equal(Format(nameof(AppStrings.Confirm_SetPriority_MultipleTitleFormat), 3), multiple.Title);
        Assert.Equal(L(nameof(AppStrings.Confirm_SetPriority_MultipleButton)), multiple.ConfirmText);
        Assert.Equal(single.Message, multiple.Message);
    }

    [Fact]
    public void EndTaskUsesTheEndTaskWordingForOneOrSeveralProcesses()
    {
        ActionConfirmation single = ActionConfirmations.EndTask([CreateProcess(10, "app.exe")]);
        ActionConfirmation multiple = ActionConfirmations.EndTask(
            [CreateProcess(10, "app.exe"), CreateProcess(11, "other.exe")]);

        Assert.Equal(Format(nameof(AppStrings.Confirm_EndTask_SingleTitleFormat), "app.exe"), single.Title);
        Assert.Equal(L(nameof(AppStrings.Confirm_EndTask_SingleButton)), single.ConfirmText);
        Assert.Equal(Format(nameof(AppStrings.Confirm_EndTask_MultipleTitleFormat), 2), multiple.Title);
        Assert.Equal(L(nameof(AppStrings.Confirm_EndTask_MultipleButton)), multiple.ConfirmText);
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

        Assert.Equal(Format(nameof(AppStrings.Confirm_Disconnect_TitleFormat), "PC\\alex"), other.Title);
        Assert.Equal(L(nameof(AppStrings.Confirm_Disconnect_Message)), other.Message);
        Assert.Equal(Format(nameof(AppStrings.Confirm_Disconnect_OwnTitleFormat), "PC\\alex"), own.Title);
        Assert.Equal(L(nameof(AppStrings.Confirm_Disconnect_OwnMessage)), own.Message);
        Assert.Equal(L(nameof(AppStrings.Confirm_Disconnect_Button)), own.ConfirmText);
    }

    private static string L(string key) => LocalizationManager.Instance[key];

    private static string Format(string key, object argument) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, L(key), argument);

    private static ProcessEndTaskItem CreateProcess(int processID, string processName) =>
        new(
            new ProcessTerminationTarget(processID, CreationTimeFileTime: processID * 100L),
            processName);
}
