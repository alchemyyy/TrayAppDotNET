using System.Globalization;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Text for one shell confirmation overlay.</summary>
internal readonly record struct ActionConfirmation(
    string Title,
    string Message,
    string ConfirmText,
    string CancelText);

/// <summary>Builds the confirmation text for risky actions, worded after Windows Task Manager.</summary>
/// <remarks>Ending a process is called "End task" everywhere (owner decision); the tree action is "End task tree".</remarks>
internal static class ActionConfirmations
{
    public static ActionConfirmation EndTask(IReadOnlyList<ProcessEndTaskItem> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        return processes.Count > 1
            ? new ActionConfirmation(
                Format(nameof(AppStrings.Confirm_EndTask_MultipleTitleFormat), processes.Count),
                L(nameof(AppStrings.Confirm_EndTask_MultipleMessage)),
                L(nameof(AppStrings.Confirm_EndTask_MultipleButton)),
                CancelText)
            : new ActionConfirmation(
                Format(nameof(AppStrings.Confirm_EndTask_SingleTitleFormat), FormatProcessName(processes[0])),
                L(nameof(AppStrings.Confirm_EndTask_SingleMessage)),
                L(nameof(AppStrings.Confirm_EndTask_SingleButton)),
                CancelText);
    }

    public static ActionConfirmation EndProcessTree(IReadOnlyList<ProcessEndTaskItem> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        return processes.Count > 1
            ? new ActionConfirmation(
                Format(nameof(AppStrings.Confirm_EndTaskTree_MultipleTitleFormat), processes.Count),
                L(nameof(AppStrings.Confirm_EndTaskTree_MultipleMessage)),
                L(nameof(AppStrings.Confirm_EndTaskTree_MultipleButton)),
                CancelText)
            : new ActionConfirmation(
                Format(nameof(AppStrings.Confirm_EndTaskTree_SingleTitleFormat), FormatProcessName(processes[0])),
                L(nameof(AppStrings.Confirm_EndTaskTree_SingleMessage)),
                L(nameof(AppStrings.Confirm_EndTaskTree_SingleButton)),
                CancelText);
    }

    public static ActionConfirmation SetPriority(IReadOnlyList<ProcessEndTaskItem> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        return processes.Count > 1
            ? new ActionConfirmation(
                Format(nameof(AppStrings.Confirm_SetPriority_MultipleTitleFormat), processes.Count),
                L(nameof(AppStrings.Confirm_SetPriority_Message)),
                L(nameof(AppStrings.Confirm_SetPriority_MultipleButton)),
                CancelText)
            : new ActionConfirmation(
                Format(nameof(AppStrings.Confirm_SetPriority_SingleTitleFormat), FormatProcessName(processes[0])),
                L(nameof(AppStrings.Confirm_SetPriority_Message)),
                L(nameof(AppStrings.Confirm_SetPriority_SingleButton)),
                CancelText);
    }

    public static ActionConfirmation Disconnect(UserSessionInfo session, int currentSessionID)
    {
        ArgumentNullException.ThrowIfNull(session);
        string userName = string.IsNullOrWhiteSpace(session.AccountName)
            ? Format(nameof(AppStrings.Confirm_Disconnect_SessionFormat), session.SessionID)
            : session.AccountName;
        bool isOwnSession = session.SessionID == currentSessionID;
        return new ActionConfirmation(
            isOwnSession
                ? Format(nameof(AppStrings.Confirm_Disconnect_OwnTitleFormat), userName)
                : Format(nameof(AppStrings.Confirm_Disconnect_TitleFormat), userName),
            isOwnSession
                ? L(nameof(AppStrings.Confirm_Disconnect_OwnMessage))
                : L(nameof(AppStrings.Confirm_Disconnect_Message)),
            L(nameof(AppStrings.Confirm_Disconnect_Button)),
            CancelText);
    }

    public static ActionConfirmation AdministratorApproval => new(
        L(nameof(AppStrings.Confirm_AdministratorApproval_Title)),
        L(nameof(AppStrings.Confirm_AdministratorApproval_Message)),
        L(nameof(AppStrings.Confirm_AdministratorApproval_Button)),
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

    private static string CancelText => L(nameof(CommonStrings.Common_Cancel));

    private static string FormatProcessName(ProcessEndTaskItem process) =>
        string.IsNullOrWhiteSpace(process.ProcessName)
            ? Format(nameof(AppStrings.Confirm_ProcessIDFormat), process.Target.ProcessID)
            : process.ProcessName;

    private static string Format(string key, object argument) =>
        string.Format(CultureInfo.CurrentCulture, L(key), argument);

    private static string L(string key) => LocalizationManager.Instance[key];
}
