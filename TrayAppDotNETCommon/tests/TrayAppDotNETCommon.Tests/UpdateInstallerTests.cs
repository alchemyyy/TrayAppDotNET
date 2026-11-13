using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.Services.Ipc;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

/// <summary>
/// The current and the transitional (older parent) <c>--update-apply</c> invocations, and how the
/// <c>--update-restart</c> restarter opens the worker.
/// </summary>
public sealed class UpdateInstallerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    // Created by the test or the code under test, so a test can also start from a missing folder
    private string TestRoot => _temp.Combine("root");

    [Fact]
    public void AnOlderParentsCommandLineIsTheTransitionalInvocation()
    {
        string[] olderParent =
        [
            "--update-apply", "--parent-pid", "12", "--parent-start", "34", "--pipe", "TrayAppDotNET.Update.12.x",
            "--target", @"C:\Program Files\TrayAppDotNET\TestTrayAppDotNET.exe", "--log", @"C:\temp\update.log"
        ];
        string[] current = [.. olderParent, "--archive", @"C:\temp\TestTrayAppDotNET_101.zip", "--sha256", new('a', 64), "--build", "101"];
        string[] restarter = ["--update-restart", "--worker-pid", "1", "--worker-start", "2", "--target", "t", "--archive", "a", "--log", "l"];

        Assert.True(UpdateInstaller.IsLegacyApplyInvocation(olderParent));
        Assert.False(UpdateInstaller.IsLegacyApplyInvocation(current));
        Assert.False(UpdateInstaller.IsLegacyApplyInvocation(restarter));
    }

    [Fact]
    public void TheTransitionalInvocationStagesTheFolderTheOlderParentExtracted()
    {
        string extracted = Path.Combine(TestRoot, "download");
        Directory.CreateDirectory(Path.Combine(extracted, ".notices"));
        File.WriteAllText(Path.Combine(extracted, "TestTrayAppDotNET.exe"), "new app");
        File.WriteAllText(Path.Combine(extracted, ".notices", "skia.txt"), "notice");
        string staging = Path.Combine(TestRoot, "staging");
        Directory.CreateDirectory(staging);

        string payload = UpdateInstaller.PrepareStagedPayload(legacyInvocation: true, extracted, archivePath: null, string.Empty, staging);

        Assert.Equal("new app", File.ReadAllText(Path.Combine(payload, "TestTrayAppDotNET.exe")));
        Assert.Equal("notice", File.ReadAllText(Path.Combine(payload, ".notices", "skia.txt")));
        Assert.StartsWith(staging, payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheCurrentInvocationExtractsOnlyAPackageWithThePublishedHash()
    {
        string archive = Path.Combine(TestRoot, "TestTrayAppDotNET_101.zip");
        Directory.CreateDirectory(TestRoot);
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(zip.CreateEntry("TestTrayAppDotNET.exe").Open());
            writer.Write("new app");
        }

        string goodHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive)));
        string goodStaging = Path.Combine(TestRoot, "good");
        string badStaging = Path.Combine(TestRoot, "bad");
        Directory.CreateDirectory(goodStaging);
        Directory.CreateDirectory(badStaging);

        string payload = UpdateInstaller.PrepareStagedPayload(legacyInvocation: false, TestRoot, archive, goodHash, goodStaging);

        Assert.Equal("new app", File.ReadAllText(Path.Combine(payload, "TestTrayAppDotNET.exe")));
        Assert.Throws<InvalidDataException>(() =>
            UpdateInstaller.PrepareStagedPayload(legacyInvocation: false, TestRoot, archive, new string('0', 64), badStaging));
        Assert.False(Directory.Exists(Path.Combine(badStaging, "payload")));
    }

    private static readonly SecurityIdentifier User = new("S-1-5-21-1000-2000-3000-1001");
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null);
    private static readonly SecurityIdentifier OtherUser = new("S-1-5-21-1000-2000-3000-1002");

    [Fact]
    public void TheUserAsPipeOwnerIsAcceptedForBothHandshakes()
    {
        Assert.True(UpdateInstaller.IsAcceptedServerOwner(User, User, Administrators, legacyInvocation: false));
        Assert.True(UpdateInstaller.IsAcceptedServerOwner(User, User, Administrators, legacyInvocation: true));
    }

    [Fact]
    public void TheTokenDefaultOwnerIsAcceptedOnlyFromAnOlderParent()
    {
        // An elevated older parent's CurrentUserOnly pipe is owned by Administrators, the token's default owner
        Assert.True(UpdateInstaller.IsAcceptedServerOwner(Administrators, User, Administrators, legacyInvocation: true));
        Assert.False(UpdateInstaller.IsAcceptedServerOwner(Administrators, User, Administrators, legacyInvocation: false));
    }

    [Fact]
    public void AnyOtherPipeOwnerIsRefused()
    {
        Assert.False(UpdateInstaller.IsAcceptedServerOwner(OtherUser, User, Administrators, legacyInvocation: true));
        Assert.False(UpdateInstaller.IsAcceptedServerOwner(Administrators, User, tokenDefaultOwner: null, legacyInvocation: true));
        Assert.False(UpdateInstaller.IsAcceptedServerOwner(owner: null, User, Administrators, legacyInvocation: true));
    }

    [Fact]
    public async Task BothPipeKindsPassTheirOwnerRuleUnderThisProcessToken()
    {
        // The test host runs with this session's token (elevated here, as an app is with UAC turned off). The current
        // parent's pipe carries an explicit user owner; an older parent's CurrentUserOnly pipe the default owner.
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User!;

        string currentName = TrayAppDotNETPipeSecurity.CreatePipeName("TrayAppDotNET.Tests.Update");
        using NamedPipeServerStream current = TrayAppDotNETPipeSecurity.CreateServer(currentName, PipeDirection.InOut);
        using NamedPipeClientStream currentClient = TrayAppDotNETPipeSecurity.CreateClient(currentName, PipeDirection.InOut);
        await Task.WhenAll(current.WaitForConnectionAsync(), currentClient.ConnectAsync());
        SecurityIdentifier? currentOwner = currentClient.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;

        string legacyName = TrayAppDotNETPipeSecurity.CreatePipeName("TrayAppDotNET.Tests.Update");
        using NamedPipeServerStream legacy = new(legacyName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using NamedPipeClientStream legacyClient = TrayAppDotNETPipeSecurity.CreateClient(legacyName, PipeDirection.InOut);
        await Task.WhenAll(legacy.WaitForConnectionAsync(), legacyClient.ConnectAsync());
        SecurityIdentifier? legacyOwner = legacyClient.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;

        Assert.Equal(user, currentOwner);
        Assert.True(UpdateInstaller.IsAcceptedServerOwner(currentOwner, user, identity.Owner, legacyInvocation: false));
        Assert.True(UpdateInstaller.IsAcceptedServerOwner(legacyOwner, user, identity.Owner, legacyInvocation: true));
        Assert.True(TrayAppDotNETPipeSecurity.IsServer(currentClient, Environment.ProcessId));
        Assert.True(TrayAppDotNETPipeSecurity.IsServer(legacyClient, Environment.ProcessId));
    }

    [Fact]
    public void TheRestarterOpensTheWorkerOnlyAtTheStartTimeTheParentPassed()
    {
        using Process worker = TestProcesses.StartWaitingCommandProcessor();
        try
        {
            // All an unelevated restarter gets on an elevated worker, and the start time must still be checked
            TestProcesses.GrantOnlyWaitAndQuery(worker);
            // The parent passes the Process.StartTime of the worker it started, as UTC ticks
            long startTimeTicks = worker.StartTime.ToUniversalTime().Ticks;

            Assert.Throws<InvalidOperationException>(() => UpdateInstaller.OpenWorker(worker.Id, startTimeTicks + 1));
            using ProcessExitHandle opened = UpdateInstaller.OpenWorker(worker.Id, startTimeTicks);
            Assert.Equal(worker.Id, opened.ProcessID);
        }
        finally
        {
            TestProcesses.TerminateIfRunning(worker);
        }
    }

    public void Dispose() => _temp.Dispose();
}
