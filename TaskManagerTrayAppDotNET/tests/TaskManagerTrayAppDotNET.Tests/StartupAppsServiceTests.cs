using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class StartupAppsServiceTests
{
    [Theory]
    [InlineData(
        "\"C:\\Program Files\\Example\\Example.exe\" --background",
        @"C:\Program Files\Example\Example.exe")]
    [InlineData(
        @"C:\Program Files\Example\Example.exe --background",
        @"C:\Program Files\Example\Example.exe")]
    [InlineData("cmd.exe /c example.cmd", "cmd.exe")]
    [InlineData("rundll32.exe,Example.dll", "rundll32.exe")]
    [InlineData(
        @"%SystemRoot%\System32\SecurityHealthSystray.exe",
        @"%SystemRoot%\System32\SecurityHealthSystray.exe")]
    [InlineData("python script.py", "python")]
    public void CommandParserExtractsExecutableCandidate(
        string command,
        string expectedCandidate)
    {
        string? candidate = StartupAppsService.ExtractExecutableCandidate(command);

        Assert.Equal(expectedCandidate, candidate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"unterminated")]
    [InlineData("\"\"")]
    public void CommandParserRejectsMissingExecutable(string? command)
    {
        string? candidate = StartupAppsService.ExtractExecutableCandidate(command);

        Assert.Null(candidate);
    }

    [Fact]
    public void MissingStartupApprovedValueDefaultsToEnabled()
    {
        StartupAppStatus status = StartupApprovedStatusCodec.Decode(null);

        Assert.Equal(StartupAppStatus.Enabled, status);
    }

    [Theory]
    [InlineData(0x02, (int)StartupAppStatus.Enabled)]
    [InlineData(0x03, (int)StartupAppStatus.Disabled)]
    [InlineData(0x04, (int)StartupAppStatus.Enabled)]
    [InlineData(0x05, (int)StartupAppStatus.Disabled)]
    [InlineData(0x06, (int)StartupAppStatus.Enabled)]
    [InlineData(0x07, (int)StartupAppStatus.Disabled)]
    public void StartupApprovedDecoderRecognizesStatePairs(
        byte stateByte,
        int expectedStatusValue)
    {
        byte[] blob = new byte[12];
        blob[0] = stateByte;

        StartupAppStatus status = StartupApprovedStatusCodec.Decode(blob);

        Assert.Equal((StartupAppStatus)expectedStatusValue, status);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x08)]
    public void StartupApprovedDecoderRejectsUnknownState(byte stateByte)
    {
        byte[] blob = new byte[12];
        blob[0] = stateByte;

        StartupAppStatus status = StartupApprovedStatusCodec.Decode(blob);

        Assert.Equal(StartupAppStatus.Unknown, status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    public void StartupApprovedDecoderRejectsTruncatedBlob(int length)
    {
        byte[] blob = new byte[length];
        if (length > 0) blob[0] = 0x02;

        StartupAppStatus status = StartupApprovedStatusCodec.Decode(blob);

        Assert.Equal(StartupAppStatus.Unknown, status);
    }

    [Fact]
    public void EnabledStartupApprovedBlobClearsTimestamp()
    {
        DateTimeOffset timestamp = new(year: 2026, month: 8, day: 30, hour: 12, minute: 34, second: 56, TimeSpan.Zero);

        byte[] blob = StartupApprovedStatusCodec.Encode(
            StartupAppStatus.Enabled,
            timestamp);

        Assert.Equal(expected: 12, blob.Length);
        Assert.Equal(expected: 0x02, blob[0]);
        Assert.All(blob.Skip(1), static value => Assert.Equal(expected: 0, value));
    }

    [Fact]
    public void DisabledStartupApprovedBlobStoresChangeTime()
    {
        DateTimeOffset timestamp = new(year: 2026, month: 8, day: 30, hour: 12, minute: 34, second: 56, TimeSpan.Zero);

        byte[] blob = StartupApprovedStatusCodec.Encode(
            StartupAppStatus.Disabled,
            timestamp);

        Assert.Equal(expected: 12, blob.Length);
        Assert.Equal(expected: 0x03, blob[0]);
        Assert.Equal(expected: 0, blob[1]);
        Assert.Equal(expected: 0, blob[2]);
        Assert.Equal(expected: 0, blob[3]);
        Assert.Equal(timestamp.UtcDateTime.ToFileTimeUtc(), BitConverter.ToInt64(blob, startIndex: 4));
    }

    [Fact]
    public void NormalizationDeduplicatesReflectedRegistryEntryAndMergesMetadata()
    {
        StartupAppEntry registry32Entry = CreateRegistryEntry(
            name: "Example",
            publisher: "Example Publisher",
            StartupAppScope.CurrentUser,
            StartupAppRegistryView.Registry32,
            command: @"C:\Example\Example.exe");
        StartupAppEntry registry64Entry = CreateRegistryEntry(
            name: "Example",
            string.Empty,
            StartupAppScope.CurrentUser,
            StartupAppRegistryView.Registry64,
            command: @"C:\Example\Example.exe");
        List<StartupAppEntry> candidates = [registry32Entry, registry64Entry];

        List<StartupAppEntry> normalized = StartupAppsService.NormalizeEntries(candidates);

        StartupAppEntry entry = Assert.Single(normalized);
        Assert.Equal(StartupAppRegistryView.Registry64, entry.Identity.RegistryView);
        Assert.Equal(expected: "Example Publisher", entry.Publisher);
    }

    [Fact]
    public void Registry32EntryUsesNativeRun32ApprovalKeyOn64BitWindows()
    {
        StartupAppApprovalTarget target = StartupAppsService.CreateRegistryApprovalTarget(
            StartupAppScope.AllUsers,
            StartupAppRegistryView.Registry32,
            valueName: "Example",
            is64BitOperatingSystem: true);

        Assert.Equal(StartupAppRegistryView.Registry64, target.RegistryView);
        Assert.EndsWith(expectedEndString: @"\StartupApproved\Run32", target.RegistrySubKey);
        Assert.Equal(expected: "Example", target.ValueName);
    }

    [Fact]
    public void Registry64EntryUsesNativeRunApprovalKey()
    {
        StartupAppApprovalTarget target = StartupAppsService.CreateRegistryApprovalTarget(
            StartupAppScope.AllUsers,
            StartupAppRegistryView.Registry64,
            valueName: "Example",
            is64BitOperatingSystem: true);

        Assert.Equal(StartupAppRegistryView.Registry64, target.RegistryView);
        Assert.EndsWith(expectedEndString: @"\StartupApproved\Run", target.RegistrySubKey);
    }

    [Fact]
    public void NormalizationRetainsSameNameFromDifferentScopes()
    {
        List<StartupAppEntry> candidates =
        [
            CreateRegistryEntry(
                name: "Example",
                publisher: "Publisher",
                StartupAppScope.CurrentUser,
                StartupAppRegistryView.Registry64,
                command: @"C:\Example\Example.exe"),
            CreateRegistryEntry(
                name: "Example",
                publisher: "Publisher",
                StartupAppScope.AllUsers,
                StartupAppRegistryView.Registry64,
                command: @"C:\Example\Example.exe")
        ];

        List<StartupAppEntry> normalized = StartupAppsService.NormalizeEntries(candidates);

        Assert.Equal(expected: 2, normalized.Count);
    }

    [Fact]
    public void NormalizationSortsByNameThenPublisher()
    {
        List<StartupAppEntry> candidates =
        [
            CreateRegistryEntry(
                name: "Zulu",
                publisher: "Publisher",
                StartupAppScope.CurrentUser,
                StartupAppRegistryView.Registry64,
                command: @"C:\Zulu.exe"),
            CreateRegistryEntry(
                name: "alpha",
                publisher: "Zulu Publisher",
                StartupAppScope.CurrentUser,
                StartupAppRegistryView.Registry64,
                command: @"C:\Alpha2.exe"),
            CreateRegistryEntry(
                name: "Alpha",
                publisher: "Alpha Publisher",
                StartupAppScope.CurrentUser,
                StartupAppRegistryView.Registry64,
                command: @"C:\Alpha1.exe")
        ];

        List<StartupAppEntry> normalized = StartupAppsService.NormalizeEntries(candidates);

        Assert.Equal(expected: "Alpha Publisher", normalized[0].Publisher);
        Assert.Equal(expected: "Zulu Publisher", normalized[1].Publisher);
        Assert.Equal(expected: "Zulu", normalized[2].Name);
    }

    [Theory]
    [InlineData((int)StartupAppStatus.Enabled, false, true)]
    [InlineData((int)StartupAppStatus.Disabled, true, false)]
    [InlineData((int)StartupAppStatus.Unknown, false, false)]
    public void ActionEligibilityFollowsStartupStatus(
        int statusValue,
        bool expectedCanEnable,
        bool expectedCanDisable)
    {
        StartupAppActionEligibility eligibility = StartupAppActionEligibility.Create(
            (StartupAppStatus)statusValue,
            supportsStatusChange: true,
            hasResolvedTarget: true);

        Assert.Equal(expectedCanEnable, eligibility.CanEnable);
        Assert.Equal(expectedCanDisable, eligibility.CanDisable);
        Assert.True(eligibility.CanShowProperties);
    }

    [Fact]
    public void ActionEligibilityRejectsStatusChangesWithoutApprovalTarget()
    {
        StartupAppActionEligibility eligibility = StartupAppActionEligibility.Create(
            StartupAppStatus.Enabled,
            supportsStatusChange: false,
            hasResolvedTarget: false);

        Assert.False(eligibility.CanEnable);
        Assert.False(eligibility.CanDisable);
        Assert.False(eligibility.CanShowProperties);
    }

    [Fact]
    public void DisposedServiceRejectsEnumerationBeforeTouchingWindowsState()
    {
        StartupAppsService service = new();
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(service.ReadEntries);
    }

    private static StartupAppEntry CreateRegistryEntry(
        string name,
        string publisher,
        StartupAppScope scope,
        StartupAppRegistryView registryView,
        string command)
    {
        const string SourceSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        string approvalSubKey = registryView == StartupAppRegistryView.Registry32
            ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"
            : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        StartupAppIdentity identity = new(
            StartupAppSourceKind.RegistryRun,
            scope,
            registryView,
            SourceSubKey,
            name);
        StartupAppApprovalTarget approvalTarget = new(
            scope,
            registryView,
            approvalSubKey,
            name);
        return new StartupAppEntry(
            identity,
            name,
            publisher,
            StartupAppStatus.Enabled,
            StartupAppImpact.NotMeasured,
            command,
            command,
            command,
            approvalTarget);
    }
}
