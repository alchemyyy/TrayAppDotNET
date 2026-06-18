using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace VolumeTrayAppDotNET.Audio.Interop;

// Endpoint volume + meter interfaces. Verified against Windows SDK endpointvolume.h:
//   IAudioEndpointVolumeCallback   657804fa-d6ad-4496-8a60-352752af4f89
//   IAudioEndpointVolume           5cdf2c82-841e-4546-9722-0cf74078229a
//   IAudioMeterInformation         c02216f6-8c67-4b5b-9d00-d008e73e0064

// Pushed into IAudioEndpointVolumeCallback.OnNotify when master volume / mute / per-channel volume change.
// LayoutKind.Sequential matches the C struct exactly:
//   GUID guidEventContext;  // 16
//   BOOL bMuted;            // 4
//   float fMasterVolume;    // 4
//   UINT nChannels;         // 4
//   float afChannelVolumes[1];  // 4 (variable-length tail)
[StructLayout(LayoutKind.Sequential)]
internal struct AUDIO_VOLUME_NOTIFICATION_DATA
{
    public Guid guidEventContext;
    [MarshalAs(UnmanagedType.Bool)] public bool bMuted;
    public float fMasterVolume;

    public uint nChannels;
    // afChannelVolumes[1] omitted; we ignore per-channel changes.
}

// Implemented in managed code. NO [ComImport] - see CCW rule in AudioInterop.cs header.
// pNotify is passed as IntPtr because AUDIO_VOLUME_NOTIFICATION_DATA's trailing channel-volumes
// array is variable-length; the caller marshals the fixed-size header manually.
[Guid("657804fa-d6ad-4496-8a60-352752af4f89")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioEndpointVolumeCallback
{
    [PreserveSig]
    int OnNotify(IntPtr pNotify);
}

[Guid("5cdf2c82-841e-4546-9722-0cf74078229a")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
    void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);

    void GetChannelCount(out uint pnChannelCount);

    void SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
    void SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
    void GetMasterVolumeLevel(out float pfLevelDB);
    void GetMasterVolumeLevelScalar(out float pfLevel);

    void SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
    void SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
    void GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
    void GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);

    void SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);

    void GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
    void VolumeStepUp(ref Guid pguidEventContext);
    void VolumeStepDown(ref Guid pguidEventContext);

    void QueryHardwareSupport(out uint pdwHardwareSupportMask);
    void GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
}

// Real-time peak meter. Available on the endpoint AND on each session.
[Guid("c02216f6-8c67-4b5b-9d00-d008e73e0064")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IAudioMeterInformation
{
    void GetPeakValue(out float pfPeak);

    void GetMeteringChannelCount(out uint pnChannelCount);

    // afPeakValues is [out, size_is(u32ChannelCount)] float* - the COM contract is "caller hands
    // me a pointer to a buffer of u32ChannelCount floats, I fill it." The CLR's default array
    // marshaler doesn't know about size_is and writes past unsafely-sized arrays, so we declare
    // the parameter as IntPtr and have callers allocate / copy explicitly via Marshal.AllocHGlobal
    // and Marshal.Copy. PreserveSig so callers can branch on HRESULT instead of catching.
    [PreserveSig]
    int GetChannelsPeakValues(uint u32ChannelCount, IntPtr afPeakValues);

    void QueryHardwareSupport(out uint pdwHardwareSupportMask);
}
