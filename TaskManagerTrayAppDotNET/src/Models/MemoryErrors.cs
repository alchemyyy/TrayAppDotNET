namespace TaskManagerTrayAppDotNET.Models;

/// <summary>What a recorded memory error event reports.</summary>
internal enum MemoryErrorKind : byte
{
    Corrected,
    Uncorrected,
    DiagnosticFailure
}

/// <summary>One memory error event from the System log.</summary>
internal sealed record MemoryErrorEntry(
    long RecordID,
    long TimeCreatedFileTime,
    int EventID,
    MemoryErrorKind Kind,
    string Message)
{
    public DateTime TimeCreatedUtc => DateTime.FromFileTimeUtc(TimeCreatedFileTime);
}

/// <summary>
/// The newest memory error the user acknowledged with Reset. Windows cannot delete single events, so Reset records
/// this instead of clearing the System log; events at or before it are no longer counted.
/// </summary>
internal readonly record struct MemoryErrorAcknowledgement(long TimeCreatedFileTime, long RecordID)
{
    public static MemoryErrorAcknowledgement None { get; } = new(TimeCreatedFileTime: 0, RecordID: 0);

    /// <summary>Gets whether <paramref name="entry"/> was already acknowledged.</summary>
    /// <remarks>
    /// The time decides first because record IDs restart when someone clears the log; the record ID only orders
    /// events logged in the same 100 ns tick.
    /// </remarks>
    public bool Covers(MemoryErrorEntry entry) =>
        entry.TimeCreatedFileTime < TimeCreatedFileTime
        || (entry.TimeCreatedFileTime == TimeCreatedFileTime && entry.RecordID <= RecordID);

    /// <summary>Counts the entries that are not yet acknowledged.</summary>
    public int CountUnacknowledged(IReadOnlyList<MemoryErrorEntry> entries)
    {
        int count = 0;
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            if (!Covers(entries[entryIndex])) count++;
        }

        return count;
    }

    /// <summary>Acknowledges everything up to the newest of <paramref name="entries"/>.</summary>
    public static MemoryErrorAcknowledgement Through(IReadOnlyList<MemoryErrorEntry> entries)
    {
        MemoryErrorAcknowledgement newest = None;
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            MemoryErrorEntry entry = entries[entryIndex];
            if (!newest.Covers(entry)) newest = new MemoryErrorAcknowledgement(entry.TimeCreatedFileTime, entry.RecordID);
        }

        return newest;
    }
}
