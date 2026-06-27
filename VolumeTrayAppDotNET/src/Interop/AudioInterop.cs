using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace VolumeTrayAppDotNET.Interop;

// Windows Core Audio common types, enums, property keys, and IPropertyStore.
// Signatures verified against the Windows SDK headers
// (mmdeviceapi.h, propsys.h, propkey.h, functiondiscoverykeys_devpkey.h).
//
// CCW rule for callback interfaces (IMMNotificationClient, IAudioSessionEvents,
// IAudioSessionNotification, IAudioEndpointVolumeCallback): declare WITHOUT [ComImport]. That
// attribute is for interfaces we consume from native COM; on an interface we implement, the
// runtime can fail to wire the CCW such that QueryInterface from native succeeds for
// registration but callbacks never deliver. PreserveSig on every method so we own the HRESULT.

internal enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2,
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2,
}

internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2,
}

internal enum AudioSessionDisconnectReason
{
    DeviceRemoval = 0,
    ServerShutdown = 1,
    FormatChanged = 2,
    SessionLogoff = 3,
    SessionDisconnected = 4,
    ExclusiveModeOverride = 5,
}

internal enum AudioClientShareMode
{
    Shared = 0,
    Exclusive = 1,
}

[Flags]
internal enum DeviceState : uint
{
    Active = 0x00000001,
    Disabled = 0x00000002,
    NotPresent = 0x00000004,
    Unplugged = 0x00000008,
    All = 0x0000000F,
}

[Flags]
internal enum ClsCtx : uint
{
    INPROC_SERVER = 0x1,
    INPROC_HANDLER = 0x2,
    LOCAL_SERVER = 0x4,
    REMOTE_SERVER = 0x10,
    ALL = INPROC_SERVER | INPROC_HANDLER | LOCAL_SERVER | REMOTE_SERVER,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY(Guid fmtid, uint pid)
{
    public Guid fmtid = fmtid;
    public uint pid = pid;
}

// PROPVARIANT minimal layout. On x64 the natural size is 24 bytes (8-byte header + 16-byte union),
// and on x86 it's 16 bytes (8 + 8). Two IntPtr fields after the header match that on both archs.
// VT_LPWSTR / VT_UI4 / VT_BOOL all live in p1; VT_BLOB uses both - cbSize in the low 32 bits
// of p1 (with 4 bytes of padding above it on x64) and the data pointer in p2.
[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT
{
    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public IntPtr p1;
    public IntPtr p2;

    public const ushort VT_EMPTY = 0;
    public const ushort VT_I4 = 3;
    public const ushort VT_BOOL = 11;
    public const ushort VT_UI4 = 19;
    public const ushort VT_LPWSTR = 31;
    public const ushort VT_BLOB = 65;
    public const ushort VT_CLSID = 72;

    public string? GetString() => vt == VT_LPWSTR ? Marshal.PtrToStringUni(p1) : null;

    public uint GetUInt32() => vt == VT_UI4 ? (uint)p1.ToInt64() : 0u;

    // VT_CLSID: p1 holds a pointer to a 16-byte GUID. Returns null on type mismatch / null
    // pointer so callers don't have to recheck vt. Used to read PKEY_Device_ContainerId,
    // which DEVPROP_TYPE_GUID maps to VT_CLSID through IPropertyStore.
    public Guid? GetGuid()
    {
        if (vt != VT_CLSID || p1 == IntPtr.Zero) return null;
        byte[] buf = new byte[16];
        Marshal.Copy(p1, buf, 0, 16);
        return new Guid(buf);
    }

    // VT_BLOB: low 32 bits of p1 hold cbSize (rest is alignment padding), p2 holds the data
    // pointer. Returns null on type mismatch or empty payload so callers don't have to recheck vt.
    public byte[]? GetBlobBytes()
    {
        if (vt != VT_BLOB) return null;
        int size = (int)p1.ToInt64();
        if (size <= 0 || p2 == IntPtr.Zero) return null;
        byte[] buf = new byte[size];
        Marshal.Copy(p2, buf, 0, size);
        return buf;
    }
}

// Well-known Function-Discovery property keys. Verified against
// functiondiscoverykeys_devpkey.h and mmdeviceapi.h.
internal static class PropertyKeys
{
    // Friendly endpoint name, e.g. "Speakers (Realtek(R) Audio)"
    public static readonly PROPERTYKEY PKEY_Device_FriendlyName = new(
        new Guid(0xA45C254E, 0xDF1C, 0x4EFD, 0x80, 0x20, 0x67, 0xD1, 0x46, 0xA8, 0x50, 0xE0), 14);

    // Adapter / interface name, e.g. "Realtek(R) Audio"
    public static readonly PROPERTYKEY PKEY_DeviceInterface_FriendlyName = new(
        new Guid(0x026E516E, 0xB814, 0x414B, 0x83, 0xCD, 0x85, 0x6D, 0x6F, 0xEF, 0x48, 0x22), 2);

    // Endpoint description, e.g. "Speakers" or "Headphones"
    public static readonly PROPERTYKEY PKEY_Device_DeviceDesc = new(
        new Guid(0xA45C254E, 0xDF1C, 0x4EFD, 0x80, 0x20, 0x67, 0xD1, 0x46, 0xA8, 0x50, 0xE0), 2);

    // DEVPKEY_Device_EnumeratorName - the bus enumerator a PnP device belongs to. The audio
    // endpoint property store inherits this from the underlying device, so for any endpoint
    // backed by Bluetooth Classic (A2DP / HFP) it reads "BTHENUM". USB endpoints read "USB",
    // PCI read "PCI", HDAudio read "HDAUDIO". This is the definitive Bluetooth signal -
    // friendly names like "Headphones (WH-1000XM4)" carry no protocol hint, so we can't rely
    // on substring heuristics alone. fmtid family is the PnP-name family (shared with
    // PKEY_Device_FriendlyName / PKEY_Device_DeviceDesc); pid 24 is documented in devpkey.h.
    public static readonly PROPERTYKEY PKEY_Device_EnumeratorName = new(
        new Guid(0xA45C254E, 0xDF1C, 0x4EFD, 0x80, 0x20, 0x67, 0xD1, 0x46, 0xA8, 0x50, 0xE0), 24);

    // DEVPKEY_Device_ContainerId - the GUID of the physical "container" a PnP device belongs to.
    // Every interface a single physical device exposes (audio render endpoint, audio capture
    // endpoint, HID, the Bluetooth radio node itself) inherits the same container id, so this is
    // the canonical key for matching an audio endpoint to the Bluetooth devnode that backs it.
    // VT_CLSID through IPropertyStore. Documented in devpkey.h; pid 2.
    public static readonly PROPERTYKEY PKEY_Device_ContainerId = new(
        new Guid(0x8C7ED206, 0x3F8A, 0x4827, 0xB3, 0xAB, 0xAE, 0x9E, 0x1F, 0xAE, 0xFC, 0x6C), 2);

    // 'Listen to this device' state on capture endpoints, mirroring the checkbox under
    // Sound > Recording > [Mic Properties] > Listen tab. Stored as VT_BOOL (VARIANT_TRUE / FALSE)
    // in HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\{guid}\Properties.
    // Verified empirically on Windows 11 - the bytes following the 8-byte PROPVARIANT header are
    // FF FF for TRUE, 00 00 for FALSE. VT_EMPTY when never toggled. Not in the public Windows SDK
    // headers - this fmtid is the MMDevAPI listen-feature family used by mmsys.cpl.
    public static readonly PROPERTYKEY PKEY_AudioEndpoint_ListenToThisDevice = new(
        new Guid(0x24DBB0FC, 0x9311, 0x4B3D, 0x9C, 0xF0, 0x18, 0xFF, 0x15, 0x56, 0x39, 0xD4), 1);

    // Listen target playback device on a capture endpoint. Stored as VT_LPWSTR holding the target
    // render endpoint's IMMDevice id (e.g. "{0.0.0.00000000}.{<guid>}"). Absent / VT_EMPTY means
    // 'Default Playback Device' - mmsys.cpl deletes this pid to encode the follow-default mode.
    // Verified empirically against the registry; same fmtid as the listen-enable bool.
    public static readonly PROPERTYKEY PKEY_AudioEndpoint_ListenTargetDeviceID = new(
        new Guid(0x24DBB0FC, 0x9311, 0x4B3D, 0x9C, 0xF0, 0x18, 0xFF, 0x15, 0x56, 0x39, 0xD4), 0);

    // "Allow applications to take exclusive control of this device" - the master checkbox in
    // mmsys.cpl Advanced > Exclusive Mode. Stored as VT_UI4 in
    // HKLM\...\MMDevices\Audio\{Render|Capture}\{guid}\Properties as REG_DWORD: 1 = allowed,
    // 0 = disallowed. Absent / VT_EMPTY when never toggled, in which case the OS default is
    // "allowed". Not in the public Windows SDK headers; same fmtid as PKEY_AudioEndpoint_FormFactor.
    public static readonly PROPERTYKEY PKEY_AudioEndpoint_AllowExclusiveControl = new(
        new Guid(0xB3F8FA53, 0x0004, 0x438E, 0x90, 0x03, 0x51, 0xA4, 0x6E, 0x13, 0x9B, 0xFC), 3);

    // "Give exclusive mode applications priority" - the sub-checkbox under the master allow bit.
    // Same fmtid, pid 4. We yoke it to pid 3 so the flyout button drives both together: enabling
    // exclusive control re-enables priority; disabling it clears priority too, matching what a
    // user toggling the master in mmsys.cpl would expect.
    public static readonly PROPERTYKEY PKEY_AudioEndpoint_ExclusiveModeAppsPriority = new(
        new Guid(0xB3F8FA53, 0x0004, 0x438E, 0x90, 0x03, 0x51, 0xA4, 0x6E, 0x13, 0x9B, 0xFC), 4);

    // "Disable all enhancements" master checkbox on the mmsys.cpl Enhancements tab. Stored as
    // VT_UI4 DWORD: 0 = enhancements enabled (engine default when absent), 1 = disabled. On
    // capture endpoints the audio engine routes the listen-to-this-device monitor through the
    // same sysfx pipeline, so flipping this to 1 silently breaks the listen feature even when
    // PKEY_AudioEndpoint_ListenToThisDevice is true. fmtid 1DA5D803...0E pid 5; audioendpoints.h.
    public static readonly PROPERTYKEY PKEY_AudioEndpoint_Disable_SysFx = new(
        new Guid(0x1DA5D803, 0xD492, 0x4EDD, 0x8C, 0x23, 0xE0, 0xC0, 0xFF, 0xEE, 0x7F, 0x0E), 5);

    // Endpoint default mix format. VT_BLOB holding a WAVEFORMATEX (or WAVEFORMATEXTENSIBLE when
    // wFormatTag == 0xFFFE). Same value the Sound Control Panel's Advanced tab edits, and what the
    // audio engine resamples / mixes to before handing buffers to the driver.
    public static readonly PROPERTYKEY PKEY_AudioEngine_DeviceFormat = new(
        new Guid(0xF19F064D, 0x082C, 0x4E27, 0xBC, 0x73, 0x68, 0x82, 0xA1, 0xBB, 0x8E, 0x4C), 0);

    // KSDATAFORMAT_SUBTYPE_PCM. The SubFormat GUID inside a WAVEFORMATEXTENSIBLE that says "this
    // is integer PCM" (vs IEEE float, AC-3, etc). Synthesized into format blobs we hand to
    // IPolicyConfig::SetDeviceFormat when the existing format wasn't already EXTENSIBLE so we
    // have nothing to copy from.
    public static readonly Guid KSDATAFORMAT_SUBTYPE_PCM = new(
        0x00000001, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
}

// Activatable on an IMMDevice. Vtable layout matches audioclient.h: every slot we don't call
// is left as a stubbed Unused_* so the slots we do call (Initialize / GetBufferSize /
// GetCurrentPadding / Start / Stop / GetService) land at the right indices. PreserveSig so
// callers branch on the HRESULT directly.
[Guid("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioClient
{
    // Shared-mode initialize. hnsBufferDuration is the requested engine buffer in 100-ns ticks;
    // hnsPeriodicity must be 0 in shared mode. audioSessionGuid passed as IntPtr.Zero opts into the
    // default cross-process session. AUTOCONVERTPCM | SRC_DEFAULT_QUALITY in streamFlags lets us
    // submit any PCM format and have the engine resample / remix to the device's mix format.
    [PreserveSig]
    int Initialize(
        AudioClientShareMode shareMode,
        uint streamFlags,
        long hnsBufferDuration,
        long hnsPeriodicity,
        IntPtr pFormat,
        IntPtr audioSessionGuid);

    [PreserveSig]
    int GetBufferSize(out uint numBufferFrames);

    void Unused_GetStreamLatency();

    [PreserveSig]
    int GetCurrentPadding(out uint numPaddingFrames);

    void Unused_IsFormatSupported();

    void Unused_GetMixFormat();
    void Unused_GetDevicePeriod();

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();

    void Unused_Reset();
    void Unused_SetEventHandle();

    [PreserveSig]
    int GetService(in Guid riid, out IntPtr ppv);
}

// Render-side service obtained via IAudioClient.GetService. GetBuffer hands back a writable
// pointer into the engine's shared ring buffer; the caller copies PCM in and ReleaseBuffer
// commits the write. Frame count must be <= (BufferSize - CurrentPadding).
[Guid("f294acfc-3146-4483-a7bf-addca7c260e2")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioRenderClient
{
    [PreserveSig]
    int GetBuffer(uint numFramesRequested, out IntPtr ppData);

    [PreserveSig]
    int ReleaseBuffer(uint numFramesWritten, uint dwFlags);
}

internal static class AudioClientExtensions
{
    public static int GetService<T>(this IAudioClient client, Guid iid, out T? service)
        where T : class
    {
        int hr = client.GetService(in iid, out IntPtr ptr);
        if (hr < 0 || ptr == IntPtr.Zero)
        {
            service = null;
            return hr;
        }

        unsafe
        {
            void* unmanaged = (void*)ptr;
            try
            {
                service = UniqueComInterfaceMarshaller<T>.ConvertToManaged(unmanaged);
                return hr;
            }
            finally
            {
                UniqueComInterfaceMarshaller<T>.Free(unmanaged);
            }
        }
    }
}

// IAudioClient.Initialize streamFlags. NoPersist keeps our short feedback wav from leaking a
// per-app entry into the OS volume mixer history; AutoConvertPcm + SrcDefaultQuality let us
// submit the wav in its native PCM format and have the engine resample / remix transparently.
internal static class AudioClientStreamFlags
{
    public const uint NoPersist = 0x00080000;
    public const uint AutoConvertPcm = 0x80000000;
    public const uint SrcDefaultQuality = 0x08000000;
}

// STGM access flags for IMMDevice.OpenPropertyStore. Hoisted out of AudioDevice.cs where the
// raw 0 / 1 literals appeared nine times with inline "/* STGM_READ */" comments. uint to match
// the IMMDevice.OpenPropertyStore signature.
internal static class Stgm
{
    public const uint Read = 0u;
    public const uint Write = 1u;
}

// Event-context GUID used for our own IAudioEndpointVolume / IAudioSession writes so the
// matching change callbacks can suppress our own echoes. Single declaration shared by
// AudioDevice and AudioSession.
internal static class AudioEventContext
{
    public static readonly Guid Value = new(Constants.AppGUID);
}

// IPropertyStore: read + write side of the endpoint property store. SetValue / Commit are used
// for listen-state, exclusive-mode, and friendly-name writes; GetValue for everything else.
// The store is always addressed by known PROPERTYKEY, so GetCount / GetAt are vtable padding.
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPropertyStore
{
    void Unused_GetCount();
    void Unused_GetAt();
    void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    void SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
    void Commit();
}

// Frees PROPVARIANT-allocated resources (e.g. the LPWSTR returned by GetValue).
internal static class Ole32
{
    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PROPVARIANT pvar);
}
