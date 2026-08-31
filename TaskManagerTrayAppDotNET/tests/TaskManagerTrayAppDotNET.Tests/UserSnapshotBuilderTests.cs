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
            new ProcessSample(20, 200, 7, "DOMAIN\\Alice", "zeta.exe", 1.25, 2_000, 300, 400),
            new ProcessSample(10, 100, 7, "DOMAIN\\Alice", "alpha.exe", 2.75, 3_000, 500, 600),
            new ProcessSample(30, 300, 9, "DOMAIN\\Bob", "beta.exe", 4, 4_000, -1, 800),
            new ProcessSample(40, 400, 0, "NT AUTHORITY\\SYSTEM", "system.exe", 20, 9_000, 900, 900));
        UserSessionInfo alice = new(
            7,
            "Alice",
            "DOMAIN",
            "Console",
            UserSessionState.Active);
        UserSessionInfo bob = new(
            9,
            "Bob",
            "DOMAIN",
            "RDP-Tcp#1",
            UserSessionState.Disconnected);

        bool built = UserSnapshotBuilder.TryBuild(
            processSnapshot,
            [bob, alice],
            out UserSnapshot userSnapshot);

        Assert.True(built);
        Assert.Equal(2, userSnapshot.Groups.Count);
        UserGroupSnapshot aliceGroup = userSnapshot.Groups[0];
        UserGroupSnapshot bobGroup = userSnapshot.Groups[1];
        Assert.Equal("DOMAIN\\Alice", aliceGroup.Session.AccountName);
        Assert.Equal(new UserSessionKey(7), aliceGroup.Key);
        Assert.Equal(2, aliceGroup.ProcessCount);
        Assert.Equal(4, aliceGroup.CPUPercent);
        Assert.Equal(5_000, aliceGroup.WorkingSetBytes);
        Assert.True(aliceGroup.HasDiskUsage);
        Assert.Equal(800, aliceGroup.DiskBytesPerSecond);
        Assert.True(aliceGroup.HasNetworkUsage);
        Assert.Equal(1_000, aliceGroup.NetworkBytesPerSecond);
        Assert.Equal("alpha.exe", aliceGroup.Processes[0].Name);
        Assert.Equal("zeta.exe", aliceGroup.Processes[1].Name);
        Assert.True(aliceGroup.CanDisconnect);
        Assert.Equal("DOMAIN\\Bob", bobGroup.Session.AccountName);
        Assert.Single(bobGroup.Processes);
        Assert.False(bobGroup.HasDiskUsage);
        Assert.Equal(0, bobGroup.DiskBytesPerSecond);
        Assert.False(bobGroup.CanDisconnect);
    }

    [Fact]
    public void InfersGroupsFromProcessesWhenWTSHasNoUserSessions()
    {
        ProcessSnapshotBuffer processSnapshot = CreateSnapshot(
            new ProcessSample(10, 100, 3, "DOMAIN\\Alice", "alpha.exe", 1, 100, 0, 0),
            new ProcessSample(11, 110, 4, "Bob", "beta.exe", 2, 200, 0, 0));

        Assert.True(UserSnapshotBuilder.TryBuild(
            processSnapshot,
            Array.Empty<UserSessionInfo>(),
            out UserSnapshot userSnapshot));

        Assert.Equal(2, userSnapshot.Groups.Count);
        Assert.Equal("DOMAIN\\Alice", userSnapshot.Groups[0].Session.AccountName);
        Assert.Equal(UserSessionState.Unknown, userSnapshot.Groups[0].Session.State);
        Assert.Equal("Bob", userSnapshot.Groups[1].Session.AccountName);
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
        UserSessionInfo session = new(7, "Alice", "DOMAIN", "Console", state);

        Assert.Equal(expected, UserSessionActions.CanDisconnect(session));
        Assert.Equal(expected, session.CanDisconnect);
    }

    [Fact]
    public void DisconnectEligibilityRejectsMissingUsersAndInvalidSessionIDs()
    {
        UserSessionInfo missingUser = new(7, string.Empty, string.Empty, string.Empty, UserSessionState.Active);
        UserSessionInfo invalidSession = new(-1, "Alice", "DOMAIN", string.Empty, UserSessionState.Active);

        Assert.False(missingUser.CanDisconnect);
        Assert.False(invalidSession.CanDisconnect);
    }

    [Fact]
    public void FormatsUserMetricsAndSessionState()
    {
        Assert.Equal(
            "12.5%",
            TaskManagerUsageFormatter.FormatCPUPercent(12.5, CultureInfo.InvariantCulture));
        Assert.Equal(
            "2.0 MB",
            TaskManagerUsageFormatter.FormatMemory(2 * 1_048_576, CultureInfo.InvariantCulture));
        Assert.Equal(
            "1.5 MB/s",
            TaskManagerUsageFormatter.FormatDiskRate(
                true,
                1.5 * 1_048_576,
                CultureInfo.InvariantCulture));
        Assert.Equal(
            "8 Mbps",
            TaskManagerUsageFormatter.FormatNetworkRate(
                true,
                1_000_000,
                CultureInfo.InvariantCulture));
        Assert.Equal(
            "Unavailable",
            TaskManagerUsageFormatter.FormatDiskRate(false, 0, CultureInfo.InvariantCulture));
        Assert.Equal("Disconnected", TaskManagerUsageFormatter.FormatSessionState(UserSessionState.Disconnected));
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
        processSnapshot.BeginWrite(schema, 0);
        processSnapshot.CompleteWrite(0);

        Assert.False(UserSnapshotBuilder.TryBuild(
            processSnapshot,
            Array.Empty<UserSessionInfo>(),
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
            string executablePath = Path.Combine(@"C:\Apps", sample.Name);
            ProcessImageIdentity image = new(
                executablePath,
                sample.Name,
                executablePath,
                string.Empty,
                new ProcessIconSource(executablePath, null));
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
        new()
        {
            Column = column,
            Visible = true,
            Width = ProcessTableColumnCatalog.Get(column).DefaultWidth
        };

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
