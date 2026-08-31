using TaskManagerTrayAppDotNET.UI;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Projects process snapshots into interactive user-session groups.</summary>
internal static class UserSnapshotBuilder
{
    public static ulong RequiredColumnMask =>
        ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Name)
        | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.UserName)
        | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.SessionID)
        | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.CPU)
        | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.WorkingSet)
        | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Disk)
        | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Network);

    public static bool TryBuild(
        ProcessSnapshotBuffer processSnapshot,
        IReadOnlyList<UserSessionInfo> sessions,
        out UserSnapshot userSnapshot)
    {
        ArgumentNullException.ThrowIfNull(processSnapshot);
        ArgumentNullException.ThrowIfNull(sessions);
        ProcessDataSchema? schema = processSnapshot.Schema;
        if (schema == null || (schema.VisibleMask & RequiredColumnMask) != RequiredColumnMask)
        {
            userSnapshot = UserSnapshot.Empty;
            return false;
        }

        Dictionary<int, MutableUserGroup> groupsBySessionID = [];
        for (int sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
        {
            UserSessionInfo session = sessions[sessionIndex];
            if (session.SessionID < 0 || string.IsNullOrWhiteSpace(session.UserName)) continue;
            if (!groupsBySessionID.ContainsKey(session.SessionID))
                groupsBySessionID.Add(session.SessionID, new MutableUserGroup(session));
        }

        bool hasWTSUsers = groupsBySessionID.Count > 0;
        int sessionIDSlot = schema.GetStaticNumericSlot(ProcessTableColumnKind.SessionID);
        for (int rowIndex = 0; rowIndex < processSnapshot.Count; rowIndex++)
        {
            ProcessStaticData? row = processSnapshot.StaticRows[rowIndex];
            if (row == null || sessionIDSlot < 0 || sessionIDSlot >= row.NumericValues.Length) continue;

            int sessionID = ClampSessionID(row.NumericValues[sessionIDSlot]);
            if (!groupsBySessionID.TryGetValue(sessionID, out MutableUserGroup? group))
            {
                if (hasWTSUsers || string.IsNullOrWhiteSpace(row.UserName)) continue;

                UserSessionInfo inferredSession = CreateInferredSession(sessionID, row.UserName);
                group = new MutableUserGroup(inferredSession);
                groupsBySessionID.Add(sessionID, group);
            }

            group.Add(CreateProcessSnapshot(processSnapshot, rowIndex, row, sessionID));
        }

        UserGroupSnapshot[] groups = new UserGroupSnapshot[groupsBySessionID.Count];
        int groupIndex = 0;
        foreach (MutableUserGroup group in groupsBySessionID.Values)
        {
            groups[groupIndex] = group.Build();
            groupIndex++;
        }

        Array.Sort(groups, CompareGroups);
        userSnapshot = new UserSnapshot(groups);
        return true;
    }

    private static UserProcessSnapshot CreateProcessSnapshot(
        ProcessSnapshotBuffer snapshot,
        int rowIndex,
        ProcessStaticData row,
        int sessionID)
    {
        double cpuPercent = ReadNonnegativeDouble(snapshot, rowIndex, ProcessTableColumnKind.CPU, out _);
        long workingSetBytes = Math.Max(
            val1: 0,
            snapshot.GetDynamicNumeric(rowIndex, ProcessTableColumnKind.WorkingSet));
        double diskBytesPerSecond = ReadNonnegativeDouble(
            snapshot,
            rowIndex,
            ProcessTableColumnKind.Disk,
            out bool hasDiskUsage);
        double networkBytesPerSecond = ReadNonnegativeDouble(
            snapshot,
            rowIndex,
            ProcessTableColumnKind.Network,
            out bool hasNetworkUsage);
        return new UserProcessSnapshot(
            row.InstanceKey,
            row.Image.Key,
            row.Image.Name,
            row.Image.IconSource,
            sessionID,
            cpuPercent,
            workingSetBytes,
            hasDiskUsage,
            diskBytesPerSecond,
            hasNetworkUsage,
            networkBytesPerSecond);
    }

    private static double ReadNonnegativeDouble(
        ProcessSnapshotBuffer snapshot,
        int rowIndex,
        ProcessTableColumnKind column,
        out bool isAvailable)
    {
        double value = BitConverter.Int64BitsToDouble(snapshot.GetDynamicNumeric(rowIndex, column));
        isAvailable = double.IsFinite(value) && value >= 0;
        return isAvailable ? value : 0;
    }

    private static int ClampSessionID(long sessionID) => sessionID switch
    {
        < 0 => -1,
        > int.MaxValue => int.MaxValue,
        _ => (int)sessionID
    };

    private static UserSessionInfo CreateInferredSession(int sessionID, string accountName)
    {
        int separatorIndex = accountName.IndexOf('\\');
        if (separatorIndex <= 0 || separatorIndex >= accountName.Length - 1)
        {
            return new UserSessionInfo(
                sessionID,
                accountName,
                string.Empty,
                string.Empty,
                UserSessionState.Unknown);
        }

        return new UserSessionInfo(
            sessionID,
            accountName[(separatorIndex + 1)..],
            accountName[..separatorIndex],
            string.Empty,
            UserSessionState.Unknown);
    }

    private static int CompareGroups(UserGroupSnapshot left, UserGroupSnapshot right)
    {
        int userComparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Session.UserName,
            right.Session.UserName);
        if (userComparison != 0) return userComparison;

        int domainComparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Session.DomainName,
            right.Session.DomainName);
        return domainComparison != 0
            ? domainComparison
            : left.Session.SessionID.CompareTo(right.Session.SessionID);
    }

    private static int CompareProcesses(UserProcessSnapshot left, UserProcessSnapshot right)
    {
        int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        if (nameComparison != 0) return nameComparison;

        int processIDComparison = left.Key.ProcessID.CompareTo(right.Key.ProcessID);
        return processIDComparison != 0
            ? processIDComparison
            : left.Key.CreationTimeTicks.CompareTo(right.Key.CreationTimeTicks);
    }

    private sealed class MutableUserGroup(UserSessionInfo session)
    {
        private readonly List<UserProcessSnapshot> _processes = [];
        private double _cpuPercent;
        private long _workingSetBytes;
        private double _diskBytesPerSecond;
        private double _networkBytesPerSecond;
        private bool _hasDiskUsage;
        private bool _hasNetworkUsage;

        public void Add(UserProcessSnapshot process)
        {
            _processes.Add(process);
            _cpuPercent += process.CPUPercent;
            _workingSetBytes = SaturatingAdd(_workingSetBytes, process.WorkingSetBytes);
            if (process.HasDiskUsage)
            {
                _hasDiskUsage = true;
                _diskBytesPerSecond += process.DiskBytesPerSecond;
            }

            if (process.HasNetworkUsage)
            {
                _hasNetworkUsage = true;
                _networkBytesPerSecond += process.NetworkBytesPerSecond;
            }
        }

        public UserGroupSnapshot Build()
        {
            UserProcessSnapshot[] processes = _processes.ToArray();
            Array.Sort(processes, CompareProcesses);
            return new UserGroupSnapshot(
                session,
                processes,
                _cpuPercent,
                _workingSetBytes,
                _hasDiskUsage,
                _diskBytesPerSecond,
                _hasNetworkUsage,
                _networkBytesPerSecond);
        }

        private static long SaturatingAdd(long left, long right) =>
            right > long.MaxValue - left ? long.MaxValue : left + right;
    }
}
