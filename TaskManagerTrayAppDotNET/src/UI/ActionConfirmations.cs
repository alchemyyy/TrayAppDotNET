namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Text for one shell confirmation overlay.</summary>
internal readonly record struct ActionConfirmation(
    string Title,
    string Message,
    string ConfirmText,
    string CancelText);

/// <summary>Builds the confirmation text for risky actions, worded after Windows Task Manager.</summary>
internal static class ActionConfirmations
{
    private const string CancelText = "Cancel";

    private const string EndTaskMessage =
        "If an open program is associated with this process, it will close and you will lose any unsaved data. " +
        "If you end a system process, it might result in system instability. Are you sure you want to continue?";

    private const string EndTasksMessage =
        "Open programs associated with these processes will close and you may lose unsaved data. Ending system " +
        "processes might result in system instability. Are you sure you want to continue?";

    private const string EndProcessTreeMessage =
        "If open programs or processes are associated with this process tree, they will close and you will lose " +
        "any unsaved data. If you end a system process, it might result in system instability. Are you sure you " +
        "want to continue?";

    private const string EndProcessTreesMessage =
        "Open programs and processes associated with these process trees will close and you may lose unsaved " +
        "data. Ending system processes might result in system instability. Are you sure you want to continue?";

    private const string ChangePriorityMessage =
        "Changing the priority of certain processes could cause system instability.";

    private const string DisconnectMessage =
        "Programs the user has open keep running, and they can sign in again to reconnect to the session.";

    private const string DisconnectOwnSessionMessage =
        "This is your own session. Windows returns to the sign-in screen, and your open programs keep running " +
        "until you sign in again.";

    public static ActionConfirmation EndTask(IReadOnlyList<ProcessEndTaskItem> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        return processes.Count > 1
            ? new ActionConfirmation(
                $"Do you want to end {processes.Count} selected processes?",
                EndTasksMessage,
                ConfirmText: "End processes",
                CancelText)
            : new ActionConfirmation(
                $"Do you want to end {FormatProcessName(processes[0])}?",
                EndTaskMessage,
                ConfirmText: "End process",
                CancelText);
    }

    public static ActionConfirmation EndProcessTree(IReadOnlyList<ProcessEndTaskItem> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        return processes.Count > 1
            ? new ActionConfirmation(
                $"Do you want to end the process trees of {processes.Count} selected processes?",
                EndProcessTreesMessage,
                ConfirmText: "End process trees",
                CancelText)
            : new ActionConfirmation(
                $"Do you want to end the process tree of {FormatProcessName(processes[0])}?",
                EndProcessTreeMessage,
                ConfirmText: "End process tree",
                CancelText);
    }

    public static ActionConfirmation SetPriority(IReadOnlyList<ProcessEndTaskItem> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        return processes.Count > 1
            ? new ActionConfirmation(
                $"Do you want to change the priority of {processes.Count} selected processes?",
                ChangePriorityMessage,
                ConfirmText: "Change priorities",
                CancelText)
            : new ActionConfirmation(
                $"Do you want to change the priority of {FormatProcessName(processes[0])}?",
                ChangePriorityMessage,
                ConfirmText: "Change priority",
                CancelText);
    }

    public static ActionConfirmation Disconnect(UserSessionInfo session, int currentSessionID)
    {
        ArgumentNullException.ThrowIfNull(session);
        string userName = string.IsNullOrWhiteSpace(session.AccountName)
            ? $"session {session.SessionID}"
            : session.AccountName;
        bool isOwnSession = session.SessionID == currentSessionID;
        return new ActionConfirmation(
            isOwnSession
                ? $"Do you want to disconnect your own session ({userName})?"
                : $"Do you want to disconnect {userName}?",
            isOwnSession ? DisconnectOwnSessionMessage : DisconnectMessage,
            ConfirmText: "Disconnect user",
            CancelText);
    }

    public static ActionConfirmation AdministratorApproval { get; } = new(
        Title: "Administrator approval required",
        "This action needs administrator rights. Continue to the Windows approval prompt? Declining "
        + "leaves nothing changed, and you will be asked again the next time an action needs elevation.",
        ConfirmText: "Continue",
        CancelText);

    /// <summary>Runs the action only after the user confirms; returns whether it ran.</summary>
    public static async Task<bool> RunIfConfirmedAsync(
        ActionConfirmation confirmation,
        Func<ActionConfirmation, Task<bool>> confirm,
        Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(action);

        if (!await confirm(confirmation)) return false;

        await action();
        return true;
    }

    private static string FormatProcessName(ProcessEndTaskItem process) =>
        string.IsNullOrWhiteSpace(process.ProcessName)
            ? $"PID {process.Target.ProcessID}"
            : process.ProcessName;
}
