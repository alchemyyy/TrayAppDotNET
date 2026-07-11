namespace FanControlTrayAppDotNET.UI.Flyout;

internal enum FanVisualRebuildResult
{
    Committed,
    Deferred,
    Unavailable,
    Failed
}

/// <summary>Resolves pending rebuild state without losing requests raised during an active rebuild.</summary>
internal static class FanVisualRebuildLogic
{
    public static bool ResolvePendingAfterAttempt(
        bool pendingAtStart,
        bool requestedDuringAttempt,
        FanVisualRebuildResult result) =>
        result == FanVisualRebuildResult.Committed
            ? requestedDuringAttempt
            : pendingAtStart || requestedDuringAttempt;
}
