using TaskManagerTrayAppDotNET.Models;

namespace TaskManagerTrayAppDotNET.UI;

internal readonly record struct ProcessSelectionResult(
    ProcessInstanceKey? ActiveProcess,
    ProcessInstanceKey? AnchorProcess,
    bool Changed);

/// <summary>Applies standard extended-selection pointer semantics to stable process identities.</summary>
internal static class ProcessSelectionFunctions
{
    public static ProcessSelectionResult ApplyPointerSelection(
        HashSet<ProcessInstanceKey> selectedProcesses,
        IReadOnlyList<ProcessInstanceKey> visibleProcesses,
        int clickedIndex,
        ProcessInstanceKey? activeProcess,
        ProcessInstanceKey? anchorProcess,
        bool isControlPressed,
        bool isShiftPressed)
    {
        ArgumentNullException.ThrowIfNull(selectedProcesses);
        ArgumentNullException.ThrowIfNull(visibleProcesses);

        if ((uint)clickedIndex >= (uint)visibleProcesses.Count)
        {
            if (isControlPressed || isShiftPressed)
                return new ProcessSelectionResult(activeProcess, anchorProcess, Changed: false);

            bool changed = selectedProcesses.Count > 0
                           || activeProcess.HasValue
                           || anchorProcess.HasValue;
            selectedProcesses.Clear();
            return new ProcessSelectionResult(
                ActiveProcess: null,
                AnchorProcess: null,
                changed);
        }

        ProcessInstanceKey clickedProcess = visibleProcesses[clickedIndex];
        HashSet<ProcessInstanceKey> nextSelection = new(selectedProcesses);
        ProcessInstanceKey? nextActiveProcess;
        ProcessInstanceKey? nextAnchorProcess;

        if (isShiftPressed && selectedProcesses.Count > 0)
        {
            int anchorIndex = FindVisibleIndex(visibleProcesses, anchorProcess);
            if (anchorIndex < 0)
                anchorIndex = FindVisibleIndex(visibleProcesses, activeProcess);

            if (anchorIndex >= 0)
            {
                if (!isControlPressed) nextSelection.Clear();
                int firstIndex = Math.Min(anchorIndex, clickedIndex);
                int lastIndex = Math.Max(anchorIndex, clickedIndex);
                for (int processIndex = firstIndex; processIndex <= lastIndex; processIndex++)
                    nextSelection.Add(visibleProcesses[processIndex]);

                nextActiveProcess = clickedProcess;
                nextAnchorProcess = visibleProcesses[anchorIndex];
                return CommitSelection(
                    selectedProcesses,
                    nextSelection,
                    activeProcess,
                    anchorProcess,
                    nextActiveProcess,
                    nextAnchorProcess);
            }

            if (!isControlPressed) nextSelection.Clear();
            nextSelection.Add(clickedProcess);
            nextActiveProcess = clickedProcess;
            nextAnchorProcess = clickedProcess;
            return CommitSelection(
                selectedProcesses,
                nextSelection,
                activeProcess,
                anchorProcess,
                nextActiveProcess,
                nextAnchorProcess);
        }

        if (isControlPressed)
        {
            if (!nextSelection.Add(clickedProcess)) nextSelection.Remove(clickedProcess);
            nextActiveProcess = nextSelection.Contains(clickedProcess)
                ? clickedProcess
                : FindFirstSelectedProcess(visibleProcesses, nextSelection);
            nextAnchorProcess = clickedProcess;
            return CommitSelection(
                selectedProcesses,
                nextSelection,
                activeProcess,
                anchorProcess,
                nextActiveProcess,
                nextAnchorProcess);
        }

        nextSelection.Clear();
        nextSelection.Add(clickedProcess);
        return CommitSelection(
            selectedProcesses,
            nextSelection,
            activeProcess,
            anchorProcess,
            clickedProcess,
            clickedProcess);
    }

    /// <summary>
    /// Decides whether a Name-cell click on an expandable row toggles its tree. Ctrl and Shift extend the
    /// selection instead, so grouped rows stay multi-selectable; section headers cannot be selected and
    /// always toggle.
    /// </summary>
    public static bool ShouldToggleTreeOnNameClick(
        bool isSectionRow,
        bool isControlPressed,
        bool isShiftPressed) =>
        isSectionRow || (!isControlPressed && !isShiftPressed);

    /// <summary>Deselects processes that a tree collapse hid, then repairs the active and anchor processes.</summary>
    /// <param name="selectedProcesses">Current selection, updated in place when anything is hidden.</param>
    /// <param name="previouslyVisibleSelection">Selected processes that were visible before the collapse.</param>
    /// <param name="visibleProcesses">Selectable processes in display order after the collapse.</param>
    /// <param name="activeProcess">Current active process.</param>
    /// <param name="anchorProcess">Current range anchor.</param>
    public static ProcessSelectionResult DeselectHiddenProcesses(
        HashSet<ProcessInstanceKey> selectedProcesses,
        IReadOnlyList<ProcessInstanceKey> previouslyVisibleSelection,
        IReadOnlyList<ProcessInstanceKey> visibleProcesses,
        ProcessInstanceKey? activeProcess,
        ProcessInstanceKey? anchorProcess)
    {
        ArgumentNullException.ThrowIfNull(selectedProcesses);
        ArgumentNullException.ThrowIfNull(previouslyVisibleSelection);
        ArgumentNullException.ThrowIfNull(visibleProcesses);

        HashSet<ProcessInstanceKey> stillVisibleProcesses = [.. visibleProcesses];
        HashSet<ProcessInstanceKey> nextSelection = new(selectedProcesses);
        for (int processIndex = 0; processIndex < previouslyVisibleSelection.Count; processIndex++)
        {
            ProcessInstanceKey process = previouslyVisibleSelection[processIndex];
            if (!stillVisibleProcesses.Contains(process)) nextSelection.Remove(process);
        }

        if (nextSelection.Count == selectedProcesses.Count)
            return new ProcessSelectionResult(activeProcess, anchorProcess, Changed: false);

        ProcessInstanceKey? nextActiveProcess = activeProcess.HasValue && nextSelection.Contains(activeProcess.Value)
            ? activeProcess
            : FindFirstSelectedProcess(visibleProcesses, nextSelection);
        ProcessInstanceKey? nextAnchorProcess = anchorProcess.HasValue
                                                && stillVisibleProcesses.Contains(anchorProcess.Value)
            ? anchorProcess
            : nextActiveProcess;
        return CommitSelection(
            selectedProcesses,
            nextSelection,
            activeProcess,
            anchorProcess,
            nextActiveProcess,
            nextAnchorProcess);
    }

    private static ProcessSelectionResult CommitSelection(
        HashSet<ProcessInstanceKey> selectedProcesses,
        HashSet<ProcessInstanceKey> nextSelection,
        ProcessInstanceKey? activeProcess,
        ProcessInstanceKey? anchorProcess,
        ProcessInstanceKey? nextActiveProcess,
        ProcessInstanceKey? nextAnchorProcess)
    {
        bool changed = activeProcess != nextActiveProcess
                       || anchorProcess != nextAnchorProcess
                       || !selectedProcesses.SetEquals(nextSelection);
        if (!changed)
            return new ProcessSelectionResult(activeProcess, anchorProcess, Changed: false);

        selectedProcesses.Clear();
        foreach (ProcessInstanceKey process in nextSelection)
            selectedProcesses.Add(process);
        return new ProcessSelectionResult(nextActiveProcess, nextAnchorProcess, Changed: true);
    }

    private static int FindVisibleIndex(
        IReadOnlyList<ProcessInstanceKey> visibleProcesses,
        ProcessInstanceKey? process)
    {
        if (!process.HasValue) return -1;
        for (int processIndex = 0; processIndex < visibleProcesses.Count; processIndex++)
        {
            if (visibleProcesses[processIndex] == process.Value) return processIndex;
        }

        return -1;
    }

    private static ProcessInstanceKey? FindFirstSelectedProcess(
        IReadOnlyList<ProcessInstanceKey> visibleProcesses,
        HashSet<ProcessInstanceKey> selectedProcesses)
    {
        for (int processIndex = 0; processIndex < visibleProcesses.Count; processIndex++)
        {
            ProcessInstanceKey process = visibleProcesses[processIndex];
            if (selectedProcesses.Contains(process)) return process;
        }

        foreach (ProcessInstanceKey process in selectedProcesses)
            return process;
        return null;
    }
}
