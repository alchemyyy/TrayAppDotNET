using System.Globalization;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class UserSnapshotBuilderTests
{
    [Fact]
    public void BuildsDeterministicSessionGroupsWithAggregatedMetricsAndChildren()
    {
        ProcessSnapshotBuffer processSnapshot = CreateSnapshot(
            new ProcessSample(ProcessID: 20, CreationTimeTicks: 200, SessionID: 7, UserName: "DOMAIN\\Alice",
                Name: "zeta.exe", CPUPercent: 1.25, WorkingSetBytes: 2_000, DiskBytesPerSecond: 300,
                NetworkBytesPerSecond: 400),
            new ProcessSample(ProcessID: 10, CreationTimeTicks: 100, SessionID: 7, UserName: "DOMAIN\\Alice",
                Name: "alpha.exe", CPUPercent: 2.75, WorkingSetBytes: 3_000, DiskBytesPerSecond: 500,
                NetworkBytesPerSecond: 600),
            new ProcessSample(ProcessID: 30, CreationTimeTicks: 300, SessionID: 9, UserName: "DOMAIN\\Bob",
                Name: "beta.exe", CPUPercent: 4, WorkingSetBytes: 4_000, DiskBytesPerSecond: -1,
                NetworkBytesPerSecond: 800),
            new ProcessSample(ProcessID: 40, CreationTimeTicks: 400, SessionID: 0, UserName: "NT AUTHORITY\\SYSTEM",
                Name: "system.exe", CPUPercent: 20, WorkingSetBytes: 9_000, DiskBytesPerSecond: 900,
                NetworkBytesPerSecond: 900));
        UserSessionInfo alice = new(
            SessionID: 7,
            UserName: "Alice",
            DomainName: "DOMAIN",
            StationName: "Console",
            UserSessionState.Active);
        UserSessionInfo bob = new(
            SessionID: 9,
            UserName: "Bob",
            DomainName: "DOMAIN",
            StationName: "RDP-Tcp#1",
            UserSessionState.Disconnected);

        bool built = UserSnapshotBuilder.TryBuild(
            processSnapshot,
            [bob, alice],
            out UserSnapshot userSnapshot);

        Assert.True(built);
        Assert.Equal(expected: 2, userSnapshot.Groups.Count);
        UserGroupSnapshot aliceGroup = userSnapshot.Groups[0];
        UserGroupSnapshot bobGroup = userSnapshot.Groups[1];
        Assert.Equal(expected: "DOMAIN\\Alice", aliceGroup.Session.AccountName);
        Assert.Equal(new UserSessionKey(7), aliceGroup.Key);
        Assert.Equal(expected: 2, aliceGroup.ProcessCount);
        Assert.Equal(expected: 4, aliceGroup.CPUPercent);
        Assert.Equal(expected: 5_000, aliceGroup.WorkingSetBytes);
        Assert.True(aliceGroup.HasDiskUsage);
        Assert.Equal(expected: 800, aliceGroup.DiskBytesPerSecond);
        Assert.True(aliceGroup.HasNetworkUsage);
        Assert.Equal(expected: 1_000, aliceGroup.NetworkBytesPerSecond);
        Assert.Equal(expected: "alpha.exe", aliceGroup.Processes[0].Name);
        Assert.Equal(expected: "zeta.exe", aliceGroup.Processes[1].Name);
        Assert.True(aliceGroup.CanDisconnect);
        Assert.Equal(expected: "DOMAIN\\Bob", bobGroup.Session.AccountName);
        Assert.Single(bobGroup.Processes);
        Assert.False(bobGroup.HasDiskUsage);
        Assert.Equal(expected: 0, bobGroup.DiskBytesPerSecond);
        Assert.False(bobGroup.CanDisconnect);
    }

    [Fact]
    public void InfersGroupsFromProcessesWhenWTSHasNoUserSessions()
    {
        ProcessSnapshotBuffer processSnapshot = CreateSnapshot(
            new ProcessSample(ProcessID: 10, CreationTimeTicks: 100, SessionID: 3, UserName: "DOMAIN\\Alice",
                Name: "alpha.exe", CPUPercent: 1, WorkingSetBytes: 100, DiskBytesPerSecond: 0,
                NetworkBytesPerSecond: 0),
            new ProcessSample(ProcessID: 11, CreationTimeTicks: 110, SessionID: 4, UserName: "Bob", Name: "beta.exe",
                CPUPercent: 2, WorkingSetBytes: 200, DiskBytesPerSecond: 0, NetworkBytesPerSecond: 0));

        Assert.True(UserSnapshotBuilder.TryBuild(
            processSnapshot,
            [],
            out UserSnapshot userSnapshot));

        Assert.Equal(expected: 2, userSnapshot.Groups.Count);
        Assert.Equal(expected: "DOMAIN\\Alice", userSnapshot.Groups[0].Session.AccountName);
        Assert.Equal(UserSessionState.Unknown, userSnapshot.Groups[0].Session.State);
        Assert.Equal(expected: "Bob", userSnapshot.Groups[1].Session.AccountName);
    }

    [Theory]
    [InlineData((int)UserSessionState.Active, true)]
    [InlineData((int)UserSessionState.Connected, true)]
    [InlineData((int)UserSessionState.Idle, true)]
    [InlineData((int)UserSessionState.Disconnected, false)]
    [InlineData((int)UserSessionState.Unknown, false)]
    public void DisconnectEligibilityTracksSessionState(int stateValue, bool expected)
    {
        UserSessionState state = (UserSessionState)stateValue;
        UserSessionInfo session = new(SessionID: 7, UserName: "Alice", DomainName: "DOMAIN", StationName: "Console",
            state);

        Assert.Equal(expected, UserSessionActions.CanDisconnect(session));
        Assert.Equal(expected, session.CanDisconnect);
    }

    [Fact]
    public void DisconnectEligibilityRejectsMissingUsersAndInvalidSessionIDs()
    {
        UserSessionInfo missingUser =
            new(SessionID: 7, string.Empty, string.Empty, string.Empty, UserSessionState.Active);
        UserSessionInfo invalidSession = new(SessionID: -1, UserName: "Alice", DomainName: "DOMAIN", string.Empty,
            UserSessionState.Active);

        Assert.False(missingUser.CanDisconnect);
        Assert.False(invalidSession.CanDisconnect);
    }

    [Fact]
    public void FormatsUserMetricsAndSessionState()
    {
        Assert.Equal(
            expected: "12.5%",
            TaskManagerUsageFormatter.FormatCPUPercent(percent: 12.5, CultureInfo.InvariantCulture));
        Assert.Equal(
            expected: "2.0 MB",
            TaskManagerUsageFormatter.FormatMemory(2 * 1_048_576, CultureInfo.InvariantCulture));
        Assert.Equal(
            expected: "1.5 MB/s",
            TaskManagerUsageFormatter.FormatDiskRate(
                isAvailable: true,
                1.5 * 1_048_576,
                CultureInfo.InvariantCulture));
        Assert.Equal(
            expected: "8 Mbps",
            TaskManagerUsageFormatter.FormatNetworkRate(
                isAvailable: true,
                bytesPerSecond: 1_000_000,
                CultureInfo.InvariantCulture));
        Assert.Equal(
            expected: "Unavailable",
            TaskManagerUsageFormatter.FormatDiskRate(isAvailable: false, bytesPerSecond: 0,
                CultureInfo.InvariantCulture));
        Assert.Equal(expected: "Disconnected",
            TaskManagerUsageFormatter.FormatSessionState(UserSessionState.Disconnected));
    }

    [Fact]
    public void RejectsSnapshotsWithoutEveryRequiredMetric()
    {
        ProcessDataSchema schema = ProcessDataSchema.Create(
        [
            Setting(ProcessTableColumnKind.Name),
            Setting(ProcessTableColumnKind.UserName),
            Setting(ProcessTableColumnKind.SessionID)
        ]);
        ProcessSnapshotBuffer processSnapshot = new();
        processSnapshot.BeginWrite(schema, requiredCapacity: 0);
        processSnapshot.CompleteWrite(0);

        Assert.False(UserSnapshotBuilder.TryBuild(
            processSnapshot,
            [],
            out UserSnapshot userSnapshot));
        Assert.Empty(userSnapshot.Groups);
    }

    private static ProcessSnapshotBuffer CreateSnapshot(params ProcessSample[] samples)
    {
        ProcessDataSchema schema = ProcessDataSchema.Create(
        [
            Setting(ProcessTableColumnKind.Name),
            Setting(ProcessTableColumnKind.UserName),
            Setting(ProcessTableColumnKind.SessionID),
            Setting(ProcessTableColumnKind.CPU),
            Setting(ProcessTableColumnKind.WorkingSet),
            Setting(ProcessTableColumnKind.Disk),
            Setting(ProcessTableColumnKind.Network)
        ]);
        ProcessSnapshotBuffer snapshot = new();
        snapshot.BeginWrite(schema, samples.Length);
        int sessionIDSlot = schema.GetStaticNumericSlot(ProcessTableColumnKind.SessionID);
        for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
        {
            ProcessSample sample = samples[sampleIndex];
            string executablePath = Path.Combine(path1: @"C:\Apps", sample.Name);
            ProcessImageIdentity image = new(
                executablePath,
                sample.Name,
                executablePath,
                string.Empty,
                new ProcessIconSource(executablePath, ApplicationUserModelID: null));
            long[] staticValues = new long[schema.StaticNumericCount];
            staticValues[sessionIDSlot] = sample.SessionID;
            long[] dynamicValues = new long[schema.DynamicNumericCount];
            dynamicValues[schema.GetDynamicNumericSlot(ProcessTableColumnKind.CPU)] =
                BitConverter.DoubleToInt64Bits(sample.CPUPercent);
            dynamicValues[schema.GetDynamicNumericSlot(ProcessTableColumnKind.WorkingSet)] =
                sample.WorkingSetBytes;
            dynamicValues[schema.GetDynamicNumericSlot(ProcessTableColumnKind.Disk)] =
                BitConverter.DoubleToInt64Bits(sample.DiskBytesPerSecond);
            dynamicValues[schema.GetDynamicNumericSlot(ProcessTableColumnKind.Network)] =
                BitConverter.DoubleToInt64Bits(sample.NetworkBytesPerSecond);
            ProcessStaticData row = new()
            {
                InstanceKey = new ProcessInstanceKey(sample.ProcessID, sample.CreationTimeTicks),
                Image = image,
                UserName = sample.UserName,
                NumericValues = staticValues,
                TextValues = new string?[schema.StaticTextCount]
            };
            snapshot.SetRow(sampleIndex, row, dynamicValues, new string?[schema.DynamicTextCount]);
        }

        snapshot.CompleteWrite(samples.Length);
        return snapshot;
    }

    private static ProcessColumnSetting Setting(ProcessTableColumnKind column) =>
        new() { Column = column, Visible = true, Width = ProcessTableColumnCatalog.Get(column).DefaultWidth };

    private sealed record ProcessSample(
        int ProcessID,
        long CreationTimeTicks,
        int SessionID,
        string UserName,
        string Name,
        double CPUPercent,
        long WorkingSetBytes,
        double DiskBytesPerSecond,
        double NetworkBytesPerSecond);
}
