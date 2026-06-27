using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace VolumeTrayAppDotNET.Interop;

// IPolicyConfig is an undocumented Windows interface used by mmsys.cpl to set the system default
// audio endpoint and rewrite per-device engine settings. Slot indices are load-bearing - each
// unused-prior method is declared as a stub so the methods we DO call land at the right vtable
// offsets.
//
// Vtable map (Win10 RS1+):
//   0  GetMixFormat               (stub)
//   1  GetDeviceFormat            (stub)
//   2  ResetDeviceFormat          (stub)
//   3  SetDeviceFormat            <-- called for the format-picker context menu
//   4  GetProcessingPeriod        (stub)
//   5  SetProcessingPeriod        (stub)
//   6  GetShareMode               (stub)
//   7  SetShareMode               (stub)
//   8  GetPropertyValue           (stub)
//   9  SetPropertyValue           (stub)
//   10 SetDefaultEndpoint         <-- called for the device-icon set-as-default click
//   11 SetEndpointVisibility      <-- called for the device enable / disable click
//
// IIDs:
//   IPolicyConfig (Win7 / Win10 RS1+)   f8679f50-850a-41cf-9c72-430f290290c8
//   CLSID_PolicyConfigClient            870af99c-171d-4f9e-af0d-e63df40c2bc9

[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPolicyConfig
{
    void Unused0();
    void Unused1();
    void Unused2();

    // Writes the per-endpoint default mix format. mmsys.cpl Advanced > Default Format and the
    // flyout's format-label context menu both end up here. pEndpointFormat is the format the
    // endpoint device renders / captures at; pMixFormat is what the audio engine mixes to
    // before resampling - in practice we pass the same WAVEFORMATEXTENSIBLE blob for both, which
    // matches what mmsys.cpl does. PreserveSig so the caller can log a non-zero HRESULT instead
    // of throwing through the threadpool action.
    [PreserveSig]
    int SetDeviceFormat(string wszDeviceId, IntPtr pEndpointFormat, IntPtr pMixFormat);

    void Unused4();
    void Unused5();
    void Unused6();
    void Unused7();
    void Unused8();
    void Unused9();

    void SetDefaultEndpoint(string wszDeviceId, ERole eRole);

    // Native signature is `HRESULT SetEndpointVisibility(LPCWSTR, INT bVisible)` - INT is the 4-byte
    // Win32 BOOL. The earlier I2/short declaration only put 2 bytes on the stack; calls worked by luck.
    void SetEndpointVisibility(string wszDeviceId, int isVisible);
}

internal static partial class PolicyConfigFactory
{
    private static readonly Guid ClsidPolicyConfigClient = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");

    public static IPolicyConfig Create()
    {
        Guid clsid = ClsidPolicyConfigClient;
        Guid iid = typeof(IPolicyConfig).GUID;
        int hr = CoCreateInstance(
            in clsid,
            IntPtr.Zero,
            (uint)ClsCtx.ALL,
            in iid,
            out IPolicyConfig policyConfig);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return policyConfig;
    }

    [LibraryImport("ole32.dll", EntryPoint = "CoCreateInstance")]
    private static partial int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IPolicyConfig>))]
        out IPolicyConfig ppv);
}
