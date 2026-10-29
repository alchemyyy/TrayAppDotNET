using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTerminationElevationFunctionsTests
{
    private static readonly ProcessEndTaskItem Protected = CreateProcess(processID: 10, processName: "elevated.exe");
    private static readonly ProcessEndTaskItem Broken = CreateProcess(processID: 11, processName: "broken.exe");
    private static readonly ProcessEndTaskItem Plain = CreateProcess(processID: 12, processName: "plain.exe");

    [Theory]
    [InlineData(nameof(ElevatedHelperState.NotRequested), true)]
    [InlineData(nameof(ElevatedHelperState.Declined), true)]
    [InlineData(nameof(ElevatedHelperState.Failed), true)]
    [InlineData(nameof(ElevatedHelperState.Starting), false)]
    [InlineData(nameof(ElevatedHelperState.Ready), false)]
    [InlineData(nameof(ElevatedHelperState.Disposed), false)]
    public void OnlyARefusalWhileElevationIsOffPrompts(string stateName, bool expected)
    {
        ElevatedHelperState state = Enum.Parse<ElevatedHelperState>(stateName);
        ProcessTerminationBatchResult result = CreateResult(
            new ProcessTerminationFailure(Protected, "Access is denied.", AccessDenied: true));

        Assert.Equal(expected, ProcessTerminationElevationFunctions.ShouldRequestElevation(
            result,
            state,
            appIsElevated: false));
    }

    [Fact]
    public void OtherFailuresAndAnElevatedAppNeverPrompt()
    {
        ProcessTerminationBatchResult otherFailure = CreateResult(
            new ProcessTerminationFailure(Broken, "The process is critical.", AccessDenied: false));
        ProcessTerminationBatchResult refused = CreateResult(
            new ProcessTerminationFailure(Protected, "Access is denied.", AccessDenied: true));

        Assert.False(ProcessTerminationElevationFunctions.ShouldRequestElevation(
            otherFailure,
            ElevatedHelperState.NotRequested,
            appIsElevated: false));
        Assert.False(ProcessTerminationElevationFunctions.ShouldRequestElevation(
            CreateResult(),
            ElevatedHelperState.NotRequested,
            appIsElevated: false));
        Assert.False(ProcessTerminationElevationFunctions.ShouldRequestElevation(
            refused,
            ElevatedHelperState.NotRequested,
            appIsElevated: true));
    }

    [Fact]
    public void BatchMarksOnlyRefusedFailures()
    {
        ProcessTerminationBatchResult result = ProcessTerminationBatchFunctions.Execute(
            [Protected, Broken, Plain],
            (ProcessTerminationTarget target, out string errorMessage) =>
            {
                errorMessage = target == Plain.Target ? string.Empty : "Failed.";
                return target == Plain.Target;
            },
            static _ => false,
            target => target == Protected.Target);

        Assert.Equal(2, result.Failures.Count);
        Assert.True(result.Failures[0].AccessDenied);
        Assert.Equal(Protected, result.Failures[0].Process);
        Assert.False(result.Failures[1].AccessDenied);
        Assert.Equal(Broken, result.Failures[1].Process);
    }

    [Fact]
    public void RefusedDescendantIsItsOwnRetryTargetAndOtherChildErrorsStay()
    {
        ProcessTerminationTarget elevatedChild = new(ProcessID: 99, CreationTimeFileTime: 9_900);
        ProcessTerminationTarget criticalChild = new(ProcessID: 98, CreationTimeFileTime: 9_800);

        ProcessTerminationBatchResult result = ProcessTerminationBatchFunctions.ExecuteTrees(
            [Plain],
            (ProcessTerminationTarget target, out string errorMessage) =>
            {
                errorMessage = target == elevatedChild ? "Access is denied." : string.Empty;
                return target != elevatedChild;
            },
            (ProcessTerminationTarget _, TryTerminateProcessAction terminate, out string errorMessage) =>
                TerminateChildren([criticalChild, elevatedChild], criticalChild, terminate, out errorMessage),
            static _ => false,
            target => target == elevatedChild);

        Assert.Equal(2, result.Failures.Count);
        ProcessTerminationFailure rootFailure = result.Failures[0];
        Assert.Equal(Plain, rootFailure.Process);
        Assert.False(rootFailure.AccessDenied);
        Assert.Contains("PID 98", rootFailure.ErrorMessage);
        Assert.DoesNotContain("PID 99", rootFailure.ErrorMessage);
        ProcessTerminationFailure childFailure = result.Failures[1];
        Assert.Equal(elevatedChild, childFailure.Process.Target);
        Assert.True(childFailure.AccessDenied);
        Assert.True(childFailure.IsDescendant);
        Assert.Equal(
            elevatedChild,
            Assert.Single(ProcessTerminationElevationFunctions.SelectRetryTargets(result)).Target);

        // Declining drops only the refused child; the critical child's error still reports
        ProcessTerminationBatchResult declined = ProcessTerminationElevationFunctions.WithoutAccessDenied(result);
        Assert.Equal(rootFailure, Assert.Single(declined.Failures));
        Assert.Contains("PID 98", declined.ErrorMessage);
    }

    [Fact]
    public async Task ApprovalEndsAnElevatedChildOrphanedByItsEndedRoot()
    {
        ProcessTerminationTarget elevatedChild = new(ProcessID: 99, CreationTimeFileTime: 9_900);
        HashSet<ProcessTerminationTarget> alive = [Plain.Target, elevatedChild];
        List<IReadOnlyList<ProcessEndTaskItem>> batches = [];
        bool elevated = false;

        bool Terminate(ProcessTerminationTarget target, out string errorMessage)
        {
            if (target == elevatedChild && !elevated)
            {
                errorMessage = "Access is denied.";
                return false;
            }

            alive.Remove(target);
            errorMessage = string.Empty;
            return true;
        }

        bool TerminateDescendants(
            ProcessTerminationTarget root,
            TryTerminateProcessAction terminate,
            out string errorMessage)
        {
            ProcessTerminationTarget[] children = root == Plain.Target && alive.Contains(elevatedChild)
                ? [elevatedChild]
                : [];
            return TerminateChildren(children, criticalChild: null, terminate, out errorMessage);
        }

        ProcessTerminationBatchResult result = await ProcessTerminationElevationFunctions.ExecuteAsync(
            [Plain],
            processes =>
            {
                batches.Add([.. processes]);
                return ProcessTerminationBatchFunctions.ExecuteTrees(
                    processes,
                    Terminate,
                    TerminateDescendants,
                    target => !alive.Contains(target),
                    target => !elevated && target == elevatedChild && alive.Contains(target));
            },
            static () => ElevatedHelperState.NotRequested,
            appIsElevated: false,
            () =>
            {
                elevated = true;
                return Task.FromResult(ElevatedTerminationRequestOutcome.Approved);
            });

        Assert.Empty(alive);
        Assert.Equal(2, batches.Count);
        Assert.Equal(elevatedChild, Assert.Single(batches[1]).Target);
        Assert.Empty(result.Failures);
        Assert.Empty(result.ErrorMessage);
    }

    [Fact]
    public void RetriedChildThatStillFailsKeepsItsChildLabel()
    {
        ProcessEndTaskItem child = new(new ProcessTerminationTarget(99, 9_900), ProcessName: string.Empty);
        ProcessTerminationBatchResult first = new(
            RefreshNeeded: true,
            [new ProcessTerminationFailure(child, "Access is denied.", AccessDenied: true, IsDescendant: true)],
            IsMultiple: false);
        ProcessTerminationBatchResult retry = CreateResult(
            new ProcessTerminationFailure(child, "Still protected.", AccessDenied: false));

        ProcessTerminationBatchResult merged = ProcessTerminationElevationFunctions.MergeRetry(first, [child], retry);

        Assert.Equal("Child process PID 99: Still protected.", merged.ErrorMessage);
    }

    [Fact]
    public async Task ApprovalRetriesOnlyTheRefusedProcesses()
    {
        List<IReadOnlyList<ProcessEndTaskItem>> batches = [];
        bool elevated = false;
        int prompts = 0;

        ProcessTerminationBatchResult result = await ProcessTerminationElevationFunctions.ExecuteAsync(
            [Protected, Broken, Plain],
            processes =>
            {
                batches.Add([.. processes]);
                return Execute(processes, elevated);
            },
            static () => ElevatedHelperState.NotRequested,
            appIsElevated: false,
            () =>
            {
                prompts++;
                elevated = true;
                return Task.FromResult(ElevatedTerminationRequestOutcome.Approved);
            });

        Assert.Equal(1, prompts);
        Assert.Equal(2, batches.Count);
        Assert.Equal([Protected], batches[1]);
        ProcessTerminationFailure remaining = Assert.Single(result.Failures);
        Assert.Equal(Broken, remaining.Process);
        Assert.Equal("broken.exe (PID 11): Broken.", result.ErrorMessage);
        Assert.True(result.RefreshNeeded);
    }

    [Fact]
    public async Task RetryFailureReplacesTheFirstAttemptMessage()
    {
        int attempts = 0;

        ProcessTerminationBatchResult result = await ProcessTerminationElevationFunctions.ExecuteAsync(
            [Protected],
            processes =>
            {
                attempts++;
                return CreateResult(new ProcessTerminationFailure(
                    processes[0],
                    attempts == 1 ? "Access is denied." : "Still protected.",
                    AccessDenied: attempts == 1));
            },
            static () => ElevatedHelperState.Declined,
            appIsElevated: false,
            static () => Task.FromResult(ElevatedTerminationRequestOutcome.Approved));

        Assert.Equal(2, attempts);
        Assert.Equal("Still protected.", result.ErrorMessage);
    }

    [Fact]
    public async Task DecliningRetriesNothingAndKeepsOtherFailures()
    {
        int batches = 0;

        ProcessTerminationBatchResult result = await ProcessTerminationElevationFunctions.ExecuteAsync(
            [Protected, Broken, Plain],
            processes =>
            {
                batches++;
                return Execute(processes, elevated: false);
            },
            static () => ElevatedHelperState.NotRequested,
            appIsElevated: false,
            static () => Task.FromResult(ElevatedTerminationRequestOutcome.Declined));

        Assert.Equal(1, batches);
        Assert.Equal("broken.exe (PID 11): Broken.", result.ErrorMessage);
    }

    [Fact]
    public async Task UnavailableHelperReportsTheOriginalFailures()
    {
        int batches = 0;

        ProcessTerminationBatchResult result = await ProcessTerminationElevationFunctions.ExecuteAsync(
            [Protected, Broken],
            processes =>
            {
                batches++;
                return Execute(processes, elevated: false);
            },
            static () => ElevatedHelperState.NotRequested,
            appIsElevated: false,
            static () => Task.FromResult(ElevatedTerminationRequestOutcome.Unavailable));

        Assert.Equal(1, batches);
        Assert.Equal(
            "elevated.exe (PID 10): Access is denied.\nbroken.exe (PID 11): Broken.",
            result.ErrorMessage);
    }

    [Fact]
    public async Task NoRefusalNeverPrompts()
    {
        int prompts = 0;

        ProcessTerminationBatchResult result = await ProcessTerminationElevationFunctions.ExecuteAsync(
            [Broken],
            processes => Execute(processes, elevated: false),
            static () => ElevatedHelperState.NotRequested,
            appIsElevated: false,
            () =>
            {
                prompts++;
                return Task.FromResult(ElevatedTerminationRequestOutcome.Approved);
            });

        Assert.Equal(0, prompts);
        Assert.Equal("Broken.", result.ErrorMessage);
    }

    // Protected ends only once elevated, Broken always fails, Plain always ends
    private static ProcessTerminationBatchResult Execute(
        IReadOnlyList<ProcessEndTaskItem> processes,
        bool elevated) =>
        ProcessTerminationBatchFunctions.Execute(
            processes,
            (ProcessTerminationTarget target, out string errorMessage) =>
            {
                if (target == Plain.Target || (elevated && target == Protected.Target))
                {
                    errorMessage = string.Empty;
                    return true;
                }

                errorMessage = target == Protected.Target ? "Access is denied." : "Broken.";
                return false;
            },
            static _ => false,
            target => !elevated && target == Protected.Target);

    // Mirrors ProcessNativeActions.TryTerminateDescendants: a critical child is reported without a termination
    // attempt, every other child goes through the supplied termination path
    private static bool TerminateChildren(
        ProcessTerminationTarget[] children,
        ProcessTerminationTarget? criticalChild,
        TryTerminateProcessAction terminate,
        out string errorMessage)
    {
        List<string> failures = [];
        foreach (ProcessTerminationTarget child in children)
        {
            if (child == criticalChild)
            {
                failures.Add($"PID {child.ProcessID}: Windows reports that the process is critical.");
                continue;
            }

            if (!terminate(child, out string terminationError))
                failures.Add($"PID {child.ProcessID}: {terminationError}");
        }

        errorMessage = failures.Count == 0
            ? string.Empty
            : "Some child processes could not be terminated:\n" + string.Join(separator: "\n", failures);
        return failures.Count == 0;
    }

    private static ProcessTerminationBatchResult CreateResult(params ProcessTerminationFailure[] failures) =>
        new(RefreshNeeded: false, failures, IsMultiple: false);

    private static ProcessEndTaskItem CreateProcess(int processID, string processName) =>
        new(
            new ProcessTerminationTarget(processID, CreationTimeFileTime: processID * 100L),
            processName);
}
