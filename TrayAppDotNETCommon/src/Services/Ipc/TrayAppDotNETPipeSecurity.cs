using System.Globalization;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.Services.Ipc;

/// <summary>
/// The one pipe security posture for every app-to-helper and app-to-worker pipe. Line protocols stay with their apps.
/// </summary>
/// <remarks>
/// The server grants access to the current user's SID only and is owned by it. That is the token's user, not its
/// default owner: an elevated and a standard process of one account share it, while their default owners differ
/// (Administrators vs. the user), which is why <see cref="PipeOptions.CurrentUserOnly"/> breaks across an elevation
/// boundary. The first instance must create the name, so a name another account pre-created makes creation fail
/// instead of handing our data to it. Clients identify themselves at Identification level only, so the server can
/// never act as them, and both ends check the other's process ID when they know it.
/// </remarks>
public static class TrayAppDotNETPipeSecurity
{
    private static readonly Lazy<SecurityIdentifier> CurrentUserSid = new(static () =>
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new InvalidOperationException("The current user SID is unavailable.");
    });

    /// <summary>The SID of the account this process runs as (the same elevated or not).</summary>
    public static SecurityIdentifier CurrentUser => CurrentUserSid.Value;

    /// <summary>Builds a name unique per process and call: <c>&lt;prefix&gt;.&lt;pid&gt;.&lt;guid&gt;</c>.</summary>
    public static string CreatePipeName(string prefix) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}.{Environment.ProcessId}.{Guid.NewGuid():N}");

    /// <summary>Creates an overlapped server end that only the current user can open and that must be the first instance.</summary>
    public static NamedPipeServerStream CreateServer(string pipeName, PipeDirection direction)
    {
        PipeSecurity security = new();
        security.AddAccessRule(new PipeAccessRule(CurrentUser, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.SetOwner(CurrentUser);
        return NamedPipeServerStreamAcl.Create(
            pipeName,
            direction,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    /// <summary>Creates an overlapped client end that lets the server identify, but never impersonate, this process.</summary>
    public static NamedPipeClientStream CreateClient(string pipeName, PipeDirection direction) =>
        new(serverName: ".", pipeName, direction, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);

    /// <summary>True when a connected server end is owned by the current user, which no other account can arrange.</summary>
    public static bool IsServerOwnedByCurrentUser(NamedPipeClientStream client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner
               && owner.Equals(CurrentUser);
    }

    /// <summary>The process ID at the other end of a connected server pipe.</summary>
    public static bool TryGetClientProcessId(NamedPipeServerStream server, out int processId)
    {
        ArgumentNullException.ThrowIfNull(server);
        bool known = Kernel32.GetNamedPipeClientProcessId(server.SafePipeHandle, out uint clientProcessId)
                     && clientProcessId is > 0 and <= int.MaxValue;
        processId = known ? (int)clientProcessId : 0;
        return known;
    }

    /// <summary>The process ID that owns the server end of a connected client pipe.</summary>
    public static bool TryGetServerProcessId(NamedPipeClientStream client, out int processId)
    {
        ArgumentNullException.ThrowIfNull(client);
        bool known = Kernel32.GetNamedPipeServerProcessId(client.SafePipeHandle, out uint serverProcessId)
                     && serverProcessId is > 0 and <= int.MaxValue;
        processId = known ? (int)serverProcessId : 0;
        return known;
    }

    /// <summary>True when the connected client is exactly <paramref name="expectedProcessId"/>.</summary>
    public static bool IsClient(NamedPipeServerStream server, int expectedProcessId) =>
        TryGetClientProcessId(server, out int processId) && processId == expectedProcessId;

    /// <summary>True when the connected server is exactly <paramref name="expectedProcessId"/>.</summary>
    public static bool IsServer(NamedPipeClientStream client, int expectedProcessId) =>
        TryGetServerProcessId(client, out int processId) && processId == expectedProcessId;
}
