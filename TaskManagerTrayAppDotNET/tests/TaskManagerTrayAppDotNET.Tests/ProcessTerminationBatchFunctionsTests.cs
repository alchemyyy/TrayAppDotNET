using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTerminationBatchFunctionsTests
{
    [Fact]
    public void ParentTerminationSuppressesAlreadyExitedSelectedChildren()
    {
        ProcessEndTaskItem parent = CreateProcess(processID: 10, processName: "parent.exe");
        ProcessEndTaskItem child1 = CreateProcess(processID: 11, processName: "child1.exe");
        ProcessEndTaskItem child2 = CreateProcess(processID: 12, processName: "child2.exe");
        ProcessEndTaskItem child3 = CreateProcess(processID: 13, processName: "child3.exe");
        ProcessEndTaskItem[] processes = [parent, child1, child2, child3];
        HashSet<ProcessTerminationTarget> goneTargets = [];
        List<ProcessTerminationTarget> terminationAttempts = [];

        ProcessTerminationBatchResult result = ProcessTerminationBatchFunctions.Execute(
            processes,
            (ProcessTerminationTarget target, out string errorMessage) =>
            {
                terminationAttempts.Add(target);
                goneTargets.Add(parent.Target);
                goneTargets.Add(child1.Target);
                goneTargets.Add(child2.Target);
                goneTargets.Add(child3.Target);
                errorMessage = string.Empty;
                return true;
            },
            goneTargets.Contains);

        Assert.Equal(parent.Target, Assert.Single(terminationAttempts));
        Assert.True(result.RefreshNeeded);
        Assert.Empty(result.ErrorMessage);
    }

    [Fact]
    public void FailureIsSuppressedWhenTargetVanishesDuringTermination()
    {
        ProcessEndTaskItem process = CreateProcess(processID: 20, processName: "short-lived.exe");
        bool isGone = false;

        ProcessTerminationBatchResult result = ProcessTerminationBatchFunctions.Execute(
            [process],
            (ProcessTerminationTarget target, out string errorMessage) =>
            {
                Assert.Equal(process.Target, target);
                isGone = true;
                errorMessage = "The process could not be opened.";
                return false;
            },
            _ => isGone);

        Assert.True(result.RefreshNeeded);
        Assert.Empty(result.ErrorMessage);
    }

    [Fact]
    public void RealMultiProcessFailuresIncludeProcessNamesAndIDs()
    {
        ProcessEndTaskItem first = CreateProcess(processID: 30, processName: "first.exe");
        ProcessEndTaskItem second = CreateProcess(processID: 31, processName: string.Empty);

        ProcessTerminationBatchResult result = ProcessTerminationBatchFunctions.Execute(
            [first, second],
            (ProcessTerminationTarget target, out string errorMessage) =>
            {
                errorMessage = target == first.Target
                    ? "Access is denied."
                    : "The target is protected.";
                return false;
            },
            static _ => false);

        Assert.False(result.RefreshNeeded);
        Assert.Equal(
            "first.exe (PID 30): Access is denied.\nPID 31: The target is protected.",
            result.ErrorMessage);
    }

    [Fact]
    public void SingularFailurePreservesTheNativeMessageWithoutAPrefix()
    {
        ProcessEndTaskItem process = CreateProcess(processID: 40, processName: "single.exe");

        ProcessTerminationBatchResult result = ProcessTerminationBatchFunctions.Execute(
            [process],
            (ProcessTerminationTarget target, out string errorMessage) =>
            {
                Assert.Equal(process.Target, target);
                errorMessage = "Access is denied.";
                return false;
            },
            static _ => false);

        Assert.False(result.RefreshNeeded);
        Assert.Equal(expected: "Access is denied.", result.ErrorMessage);
    }

    [Fact]
    public void EmptyNativeFailureStillProducesAnError()
    {
        ProcessEndTaskItem process = CreateProcess(processID: 50, processName: "single.exe");

        ProcessTerminationBatchResult result = ProcessTerminationBatchFunctions.Execute(
            [process],
            static (ProcessTerminationTarget _, out string errorMessage) =>
            {
                errorMessage = string.Empty;
                return false;
            },
            static _ => false);

        Assert.Equal(expected: "The process action failed.", result.ErrorMessage);
    }

    [Fact]
    public void EndTaskRequestCopiesTheSelectionSnapshot()
    {
        ProcessEndTaskItem original = CreateProcess(processID: 60, processName: "original.exe");
        List<ProcessEndTaskItem> processes = [original];
        ProcessEndTaskRequest request = new(processes);

        processes[0] = CreateProcess(processID: 61, processName: "replacement.exe");

        Assert.Equal(expected: 1, request.Count);
        Assert.Equal(original, request.Processes[0]);
    }

    private static ProcessEndTaskItem CreateProcess(int processID, string processName) =>
        new(
            new ProcessTerminationTarget(processID, CreationTimeFileTime: processID * 100L),
            processName);
}
