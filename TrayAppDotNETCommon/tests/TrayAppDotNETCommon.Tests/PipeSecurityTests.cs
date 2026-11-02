using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Services.Ipc;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class PipeSecurityTests
{
    [Fact]
    public void TheServerAdmitsOnlyTheCurrentUserAndIsOwnedByIt()
    {
        using NamedPipeServerStream server = TrayAppDotNETPipeSecurity.CreateServer(
            TrayAppDotNETPipeSecurity.CreatePipeName("TrayAppDotNET.Tests.PipeSecurity"),
            PipeDirection.InOut);

        PipeSecurity security = server.GetAccessControl();
        Assert.Equal(TrayAppDotNETPipeSecurity.CurrentUser, security.GetOwner(typeof(SecurityIdentifier)));
        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier));
        Assert.NotEmpty(rules);
        Assert.All(rules.Cast<PipeAccessRule>(), rule =>
        {
            Assert.Equal(TrayAppDotNETPipeSecurity.CurrentUser, rule.IdentityReference);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        });
    }

    [Fact]
    public void ANameSomeoneAlreadyHoldsCannotBeServedAgain()
    {
        string pipeName = TrayAppDotNETPipeSecurity.CreatePipeName("TrayAppDotNET.Tests.PipeSecurity");
        using NamedPipeServerStream first = TrayAppDotNETPipeSecurity.CreateServer(pipeName, PipeDirection.InOut);

        Assert.ThrowsAny<Exception>(() => TrayAppDotNETPipeSecurity.CreateServer(pipeName, PipeDirection.InOut));
    }

    [Fact]
    public async Task BothEndsIdentifyTheProcessAtTheOtherEnd()
    {
        string pipeName = TrayAppDotNETPipeSecurity.CreatePipeName("TrayAppDotNET.Tests.PipeSecurity");
        await using NamedPipeServerStream server =
            TrayAppDotNETPipeSecurity.CreateServer(pipeName, PipeDirection.InOut);
        await using NamedPipeClientStream client =
            TrayAppDotNETPipeSecurity.CreateClient(pipeName, PipeDirection.InOut);

        Task accept = server.WaitForConnectionAsync();
        await client.ConnectAsync(5_000);
        await accept.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(TrayAppDotNETPipeSecurity.IsServerOwnedByCurrentUser(client));
        Assert.True(TrayAppDotNETPipeSecurity.IsClient(server, Environment.ProcessId));
        Assert.True(TrayAppDotNETPipeSecurity.IsServer(client, Environment.ProcessId));

        // A helper that is not the expected process is rejected
        Assert.False(TrayAppDotNETPipeSecurity.IsClient(server, Environment.ProcessId + 4));
        Assert.False(TrayAppDotNETPipeSecurity.IsServer(client, Environment.ProcessId + 4));
    }

    [Fact]
    public void HelperParentArgumentsNeedAProcessIdAndAStartTime()
    {
        Assert.True(TrayAppDotNETHelperHost.TryReadParent(
            ["--mode", TrayAppDotNETHelperHost.ParentProcessIdArgument, "1234", TrayAppDotNETHelperHost.ParentStartArgument, "133000000000000000"],
            out int processId,
            out long startFileTime));
        Assert.Equal(expected: 1234, processId);
        Assert.Equal(expected: 133000000000000000, startFileTime);

        Assert.False(TrayAppDotNETHelperHost.TryReadParent(
            [TrayAppDotNETHelperHost.ParentProcessIdArgument, "1234"],
            out _,
            out _));
        Assert.False(TrayAppDotNETHelperHost.TryReadParent(
            [TrayAppDotNETHelperHost.ParentProcessIdArgument, "-5", TrayAppDotNETHelperHost.ParentStartArgument, "1"],
            out _,
            out _));
    }

    [Fact]
    public void BothUacDeclineCodesMeanCancelled()
    {
        Assert.True(TrayAppDotNETElevatedLaunch.IsUserCancelled(NativeErrors.ERROR_CANCELLED));
        Assert.True(TrayAppDotNETElevatedLaunch.IsUserCancelled(NativeErrors.HRESULT_ERROR_CANCELLED));
        Assert.False(TrayAppDotNETElevatedLaunch.IsUserCancelled(NativeErrors.ERROR_ACCESS_DENIED));
    }
}
