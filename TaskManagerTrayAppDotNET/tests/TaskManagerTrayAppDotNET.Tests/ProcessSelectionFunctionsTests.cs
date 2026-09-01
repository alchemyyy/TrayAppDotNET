using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessSelectionFunctionsTests
{
    private static readonly ProcessInstanceKey Process1 = CreateProcess(processID: 1);
    private static readonly ProcessInstanceKey Process2 = CreateProcess(processID: 2);
    private static readonly ProcessInstanceKey Process3 = CreateProcess(processID: 3);
    private static readonly ProcessInstanceKey Process4 = CreateProcess(processID: 4);
    private static readonly ProcessInstanceKey Process5 = CreateProcess(processID: 5);
    private static readonly ProcessInstanceKey[] VisibleProcesses =
        [Process1, Process2, Process3, Process4, Process5];

    [Fact]
    public void PlainClickReplacesTheSelectionAndAnchor()
    {
        HashSet<ProcessInstanceKey> selectedProcesses = [Process1, Process2];

        ProcessSelectionResult result = Apply(
            selectedProcesses,
            clickedIndex: 3,
            activeProcess: Process2,
            anchorProcess: Process1);

        AssertSelection(selectedProcesses, Process4);
        Assert.Equal(Process4, result.ActiveProcess);
        Assert.Equal(Process4, result.AnchorProcess);
        Assert.True(result.Changed);
    }

    [Fact]
    public void ControlClickAddsAndRemovesIndividualProcesses()
    {
        HashSet<ProcessInstanceKey> selectedProcesses = [Process1];

        ProcessSelectionResult added = Apply(
            selectedProcesses,
            clickedIndex: 2,
            activeProcess: Process1,
            anchorProcess: Process1,
            isControlPressed: true);

        AssertSelection(selectedProcesses, Process1, Process3);
        Assert.Equal(Process3, added.ActiveProcess);
        Assert.Equal(Process3, added.AnchorProcess);

        ProcessSelectionResult removed = Apply(
            selectedProcesses,
            clickedIndex: 2,
            activeProcess: added.ActiveProcess,
            anchorProcess: added.AnchorProcess,
            isControlPressed: true);

        AssertSelection(selectedProcesses, Process1);
        Assert.Equal(Process1, removed.ActiveProcess);
        Assert.Equal(Process3, removed.AnchorProcess);
    }

    [Fact]
    public void ShiftClickReplacesSelectionWithInclusiveRangeInEitherDirection()
    {
        HashSet<ProcessInstanceKey> selectedProcesses = [Process1, Process4];

        ProcessSelectionResult result = Apply(
            selectedProcesses,
            clickedIndex: 1,
            activeProcess: Process1,
            anchorProcess: Process4,
            isShiftPressed: true);

        AssertSelection(selectedProcesses, Process2, Process3, Process4);
        Assert.Equal(Process2, result.ActiveProcess);
        Assert.Equal(Process4, result.AnchorProcess);
    }

    [Fact]
    public void ControlShiftClickAddsAnInclusiveRange()
    {
        HashSet<ProcessInstanceKey> selectedProcesses = [Process1, Process3];

        ProcessSelectionResult result = Apply(
            selectedProcesses,
            clickedIndex: 4,
            activeProcess: Process3,
            anchorProcess: Process3,
            isControlPressed: true,
            isShiftPressed: true);

        AssertSelection(selectedProcesses, Process1, Process3, Process4, Process5);
        Assert.Equal(Process5, result.ActiveProcess);
        Assert.Equal(Process3, result.AnchorProcess);
    }

    [Fact]
    public void ShiftRangeUsesStableAnchorIdentityAfterRowsReorder()
    {
        ProcessInstanceKey[] reorderedProcesses =
            [Process5, Process2, Process3, Process4, Process1];
        HashSet<ProcessInstanceKey> selectedProcesses = [Process2, Process4];

        ProcessSelectionResult result = ProcessSelectionFunctions.ApplyPointerSelection(
            selectedProcesses,
            reorderedProcesses,
            clickedIndex: 0,
            activeProcess: Process4,
            anchorProcess: Process2,
            isControlPressed: false,
            isShiftPressed: true);

        AssertSelection(selectedProcesses, Process5, Process2);
        Assert.Equal(Process5, result.ActiveProcess);
        Assert.Equal(Process2, result.AnchorProcess);
    }

    [Fact]
    public void ShiftRangeFallsBackToVisibleActiveProcessWhenAnchorIsHidden()
    {
        ProcessInstanceKey[] visibleProcesses = [Process2, Process3, Process4, Process5];
        HashSet<ProcessInstanceKey> selectedProcesses = [Process1, Process3];

        ProcessSelectionResult result = ProcessSelectionFunctions.ApplyPointerSelection(
            selectedProcesses,
            visibleProcesses,
            clickedIndex: 3,
            activeProcess: Process3,
            anchorProcess: Process1,
            isControlPressed: false,
            isShiftPressed: true);

        AssertSelection(selectedProcesses, Process3, Process4, Process5);
        Assert.Equal(Process5, result.ActiveProcess);
        Assert.Equal(Process3, result.AnchorProcess);
    }

    [Fact]
    public void ShiftClickWithNoVisibleReferenceSelectsOnlyTheClickedProcess()
    {
        ProcessInstanceKey[] visibleProcesses = [Process3, Process4, Process5];
        HashSet<ProcessInstanceKey> selectedProcesses = [Process1, Process2];

        ProcessSelectionResult result = ProcessSelectionFunctions.ApplyPointerSelection(
            selectedProcesses,
            visibleProcesses,
            clickedIndex: 1,
            activeProcess: Process2,
            anchorProcess: Process1,
            isControlPressed: false,
            isShiftPressed: true);

        AssertSelection(selectedProcesses, Process4);
        Assert.Equal(Process4, result.ActiveProcess);
        Assert.Equal(Process4, result.AnchorProcess);
    }

    [Fact]
    public void PlainBlankClickClearsSelectionAndStaleAnchor()
    {
        HashSet<ProcessInstanceKey> selectedProcesses = [];

        ProcessSelectionResult result = Apply(
            selectedProcesses,
            clickedIndex: -1,
            activeProcess: null,
            anchorProcess: Process2);

        Assert.Empty(selectedProcesses);
        Assert.Null(result.ActiveProcess);
        Assert.Null(result.AnchorProcess);
        Assert.True(result.Changed);
    }

    [Fact]
    public void ModifiedBlankClickPreservesSelection()
    {
        HashSet<ProcessInstanceKey> selectedProcesses = [Process2];

        ProcessSelectionResult result = Apply(
            selectedProcesses,
            clickedIndex: -1,
            activeProcess: Process2,
            anchorProcess: Process2,
            isControlPressed: true);

        AssertSelection(selectedProcesses, Process2);
        Assert.Equal(Process2, result.ActiveProcess);
        Assert.Equal(Process2, result.AnchorProcess);
        Assert.False(result.Changed);
    }

    private static ProcessSelectionResult Apply(
        HashSet<ProcessInstanceKey> selectedProcesses,
        int clickedIndex,
        ProcessInstanceKey? activeProcess,
        ProcessInstanceKey? anchorProcess,
        bool isControlPressed = false,
        bool isShiftPressed = false) =>
        ProcessSelectionFunctions.ApplyPointerSelection(
            selectedProcesses,
            VisibleProcesses,
            clickedIndex,
            activeProcess,
            anchorProcess,
            isControlPressed,
            isShiftPressed);

    private static void AssertSelection(
        HashSet<ProcessInstanceKey> actual,
        params ProcessInstanceKey[] expected) =>
        Assert.True(
            actual.SetEquals(expected),
            $"Expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}].");

    private static ProcessInstanceKey CreateProcess(int processID) =>
        new(processID, CreationTimeTicks: processID * 100L);
}
