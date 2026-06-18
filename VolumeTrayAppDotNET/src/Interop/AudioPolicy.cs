using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace VolumeTrayAppDotNET.Interop;

// Audio session interfaces (per-app sessions on a device endpoint).
// Verified against Windows SDK audiopolicy.h and audioclient.h:
//   IAudioSessionEvents         24918acc-64b3-37c1-8ca9-74a66e9957a8
//   IAudioSessionControl        f4b1a599-7266-4319-a8ca-e70acb11e8cd
//   IAudioSessionControl2       bfb7ff88-7239-4fc9-8fa2-07c950be9c6d
//   IAudioSessionEnumerator     e2f5bb11-0570-40ca-acdd-3aa01277dee8
//   IAudioSessionNotification   641dd20b-4d41-49cc-aba3-174b9477bb08
//   IAudioSessionManager2       77aa99a0-1bd6-484f-8bc7-2c654c9a9b6f
//   ISimpleAudioVolume          87ce5498-68d6-44e5-9215-6da47ef883d8

// Implemented in managed code. NO [ComImport] - see CCW rule in AudioInterop.cs header.
[Guid("24918acc-64b3-37c1-8ca9-74a66e9957a8")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioSessionEvents
{
    [PreserveSig]
    int OnDisplayNameChanged(string NewDisplayName, ref Guid EventContext);

    [PreserveSig]
    int OnIconPathChanged(string NewIconPath, ref Guid EventContext);

    [PreserveSig]
    int OnSimpleVolumeChanged(float NewVolume, [MarshalAs(UnmanagedType.Bool)] bool NewMute, ref Guid EventContext);

    [PreserveSig]
    int OnChannelVolumeChanged(uint ChannelCount, IntPtr NewChannelVolumeArray, uint ChangedChannel,
        ref Guid EventContext);

    [PreserveSig]
    int OnGroupingParamChanged(ref Guid NewGroupingParam, ref Guid EventContext);

    [PreserveSig]
    int OnStateChanged(AudioSessionState NewState);

    [PreserveSig]
    int OnSessionDisconnected(AudioSessionDisconnectReason DisconnectReason);
}

[Guid("f4b1a599-7266-4319-a8ca-e70acb11e8cd")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioSessionControl
{
    void GetState(out AudioSessionState pRetVal);
    void GetDisplayName(out string pRetVal);
    void SetDisplayName(string Value, ref Guid EventContext);
    void GetIconPath(out string pRetVal);
    void SetIconPath(string Value, ref Guid EventContext);
    void GetGroupingParam(out Guid pRetVal);
    void SetGroupingParam(ref Guid Override, ref Guid EventContext);
    void RegisterAudioSessionNotification(IAudioSessionEvents NewNotifications);
    void UnregisterAudioSessionNotification(IAudioSessionEvents NewNotifications);
}

// IAudioSessionControl2 inherits IAudioSessionControl; declare the parent's vtable in order, then add 2's methods.
// Cast: query a fresh IAudioSessionControl2 from a session by calling Marshal.QueryInterface or direct cast.
[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioSessionControl2
{
    // Inherited from IAudioSessionControl (kept in vtable order)
    void GetState(out AudioSessionState pRetVal);
    void GetDisplayName(out string pRetVal);
    void SetDisplayName(string Value, ref Guid EventContext);
    void GetIconPath(out string pRetVal);
    void SetIconPath(string Value, ref Guid EventContext);
    void GetGroupingParam(out Guid pRetVal);
    void SetGroupingParam(ref Guid Override, ref Guid EventContext);
    void RegisterAudioSessionNotification(IAudioSessionEvents NewNotifications);
    void UnregisterAudioSessionNotification(IAudioSessionEvents NewNotifications);

    // Added by IAudioSessionControl2
    void GetSessionIdentifier(out string pRetVal);
    void GetSessionInstanceIdentifier(out string pRetVal);
    void GetProcessId(out uint pRetVal);

    [PreserveSig]
    int IsSystemSoundsSession();

    void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[Guid("e2f5bb11-0570-40ca-acdd-3aa01277dee8")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioSessionEnumerator
{
    void GetCount(out int SessionCount);
    void GetSession(int SessionCount, out IAudioSessionControl Session);
}

// Implemented in managed code. NO [ComImport] - see CCW rule in AudioInterop.cs header.
[Guid("641dd20b-4d41-49cc-aba3-174b9477bb08")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioSessionNotification
{
    [PreserveSig]
    int OnSessionCreated(IAudioSessionControl NewSession);
}

// IAudioSessionManager2 inherits IAudioSessionManager (which has GetAudioSessionControl + GetSimpleAudioVolume).
// Declare both in vtable order so QI to the .NET RCW lays out correctly.
[Guid("77aa99a0-1bd6-484f-8bc7-2c654c9a9b6f")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioSessionManager2
{
    // Inherited from IAudioSessionManager
    void GetAudioSessionControl(
        ref Guid AudioSessionGuid,
        uint StreamFlags,
        out IAudioSessionControl SessionControl);

    void GetSimpleAudioVolume(
        ref Guid AudioSessionGuid,
        uint StreamFlags,
        out ISimpleAudioVolume AudioVolume);

    // Added by IAudioSessionManager2
    void GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
    void RegisterSessionNotification(IAudioSessionNotification SessionNotification);
    void UnregisterSessionNotification(IAudioSessionNotification SessionNotification);

    // Duck-notification slots present in the vtable; we don't use them but they have to be declared
    // so the methods above remain at the right offsets.
    void RegisterDuckNotification(string sessionID, IntPtr duckNotification);
    void UnregisterDuckNotification(IntPtr duckNotification);
}

[Guid("87ce5498-68d6-44e5-9215-6da47ef883d8")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface ISimpleAudioVolume
{
    void SetMasterVolume(float fLevel, ref Guid EventContext);
    void GetMasterVolume(out float pfLevel);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid EventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}
