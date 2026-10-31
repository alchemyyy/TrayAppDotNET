using System.IO.Pipes;
using System.Security.Principal;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerActivationServerTests
{
    private static readonly SecurityIdentifier UserA = new("S-1-5-21-1000000000-2000000000-3000000000-1001");
    private static readonly SecurityIdentifier UserB = new("S-1-5-21-1000000000-2000000000-3000000000-1002");

    [Fact]
    public void PipeNameIsScopedToTheUserAndTheSession()
    {
        string name = TaskManagerActivationServer.ResolvePipeName(UserA, sessionId: 3);

        Assert.Contains(UserA.Value, name, StringComparison.Ordinal);
        Assert.EndsWith("session3", name, StringComparison.Ordinal);
        Assert.NotEqual(name, TaskManagerActivationServer.ResolvePipeName(UserB, sessionId: 3));
        Assert.NotEqual(name, TaskManagerActivationServer.ResolvePipeName(UserA, sessionId: 4));
    }

    [Fact]
    public void ClientOnlyTrustsAServerOwnedByItsOwnUser()
    {
        Assert.True(TaskManagerActivationServer.IsTrustedServerOwner(UserA, UserA));
        Assert.False(TaskManagerActivationServer.IsTrustedServerOwner(UserB, UserA));
        Assert.False(TaskManagerActivationServer.IsTrustedServerOwner(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null),
            UserA));
        Assert.False(TaskManagerActivationServer.IsTrustedServerOwner(serverOwner: null, UserA));
    }

    [Fact]
    public void ClientConnectsAtIdentificationLevelAndTheServerIsOverlapped()
    {
        Assert.Equal(TokenImpersonationLevel.Identification, TaskManagerActivationServer.ClientImpersonationLevel);
        Assert.True(TaskManagerActivationServer.ServerPipeOptions.HasFlag(PipeOptions.Asynchronous));
    }

    [Fact]
    public void ListenerBackoffGrowsAndIsCapped()
    {
        TimeSpan first = TaskManagerActivationServer.ListenerBackoff(consecutiveFailures: 1);
        TimeSpan second = TaskManagerActivationServer.ListenerBackoff(consecutiveFailures: 2);
        TimeSpan many = TaskManagerActivationServer.ListenerBackoff(consecutiveFailures: 1_000);

        Assert.True(second > first);
        Assert.Equal(TimeSpan.FromSeconds(30), many);
    }

    [Fact]
    public void AClientThatGoesAwayMidExchangeDoesNotTakeDownTheNextListener()
    {
        // A unique name, so the test never reaches a running Task Manager
        string pipeName = "TaskManagerActivationServerTests-" + Guid.NewGuid().ToString("N");
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User!;
        using ManualResetEventSlim clientGone = new(initialState: false);
        using ManualResetEventSlim clientFailureLogged = new(initialState: false);
        int activations = 0;
        using TaskManagerActivationServer server = new(
            () =>
            {
                // The first client closes its end before the server writes the acknowledgement
                if (Interlocked.Increment(ref activations) == 1) clientGone.Wait(TimeSpan.FromSeconds(10));
            },
            message =>
            {
                if (message.Contains("a client failed", StringComparison.Ordinal)) clientFailureLogged.Set();
            },
            pipeName);
        server.Start();

        using (NamedPipeClientStream client = new(
                   serverName: ".",
                   pipeName,
                   PipeDirection.InOut,
                   PipeOptions.None,
                   TaskManagerActivationServer.ClientImpersonationLevel))
        {
            client.Connect(timeout: 5_000);
            client.Write("SHOW\n"u8);
            client.Flush();
        }

        clientGone.Set();
        Assert.True(clientFailureLogged.Wait(TimeSpan.FromSeconds(10)));

        // The pre-created listener answers at once; before the fix it was disposed and recreated only after a
        // 250 ms backoff, longer than this connect timeout
        Assert.True(TaskManagerActivationServer.TryActivate(pipeName, user, timeoutMilliseconds: 100, log: null));
        Assert.Equal(expected: 2, Volatile.Read(ref activations));
    }
}
