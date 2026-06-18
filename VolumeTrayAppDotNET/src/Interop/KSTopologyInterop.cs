using System.Runtime.InteropServices;

namespace VolumeTrayAppDotNET.Audio.Interop;

// Per the mmsys.cpl decompile, the Default Format dropdown is populated by activating
// IID_AudioEnginePartFilter on the IMMDevice, walking the returned IPart enumeration, and
// activating IID_IKsFormatSupport on each part. IKsFormatSupport::IsFormatSupported then
// probes each candidate WAVEFORMATEX wrapped in a 104-byte KSDATAFORMAT_WAVEFORMATEX envelope.
// IDeviceTopology / IConnector / direct KSPROPERTY pin probing returned E_NOINTERFACE / empty
// on every driver tested, so we never go through that path.
internal static class KSConstants
{
    // Microsoft-private IID handed to IMMDevice::Activate to reach the audio engine's internal
    // topology where IKsFormatSupport lives (the public IDeviceTopology never exposes it).
    // Chain (per Hex-Rays decompile of mmsys.cpl):
    //   ifilter = IMMDevice::Activate(IID_AudioEnginePartFilter, CLSCTX_INPROC_SERVER, NULL)
    //   enum    = ifilter->vtable[3](&ksDataFormat=64B, 64, NULL)
    //   count   = enum->vtable[3]()
    //   part    = enum->vtable[4](i)
    //   fs      = part->Activate(CLSCTX_INPROC_SERVER, IID_IKsFormatSupport)
    //   supported = fs->IsFormatSupported(KSDATAFORMAT_WAVEFORMATEX, 104)  // per candidate
    public static readonly Guid IID_AudioEnginePartFilter = new(
        0x2B0711DE, 0xDAB7, 0x4610, 0xA1, 0x6F, 0xD3, 0x38, 0x37, 0x49, 0xB2, 0x20);

    // IKsFormatSupport: canonical interface mmsys.cpl uses to populate its Default Format dropdown.
    // IsFormatSupported(PKSDATAFORMAT pKsFormat, ULONG cbFormat, BOOL *pbSupported):
    //   - pKsFormat: pointer to KSDATAFORMAT_WAVEFORMATEX (104 bytes for a 40-byte WAVEFORMATEXTENSIBLE)
    //   - cbFormat: size of the format buffer in bytes (104)
    //   - pbSupported: out BOOL (32-bit int) - TRUE/FALSE for whether the format is supported
    public static readonly Guid IID_IKsFormatSupport = new(
        0x3CB4A69D, 0xBB6F, 0x4D2B, 0x95, 0xB7, 0x45, 0x2D, 0x2C, 0x15, 0x5D, 0xB5);

    // KSDATAFORMAT_TYPE_AUDIO: MajorFormat for audio formats.
    public static readonly Guid KSDATAFORMAT_TYPE_AUDIO = new(
        0x73647561, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    // KSDATAFORMAT_SPECIFIER_WAVEFORMATEX: Specifier value for a KSDATAFORMAT whose payload is
    // a WAVEFORMATEX (variable size) immediately following the 64-byte KSDATAFORMAT header.
    public static readonly Guid KSDATAFORMAT_SPECIFIER_WAVEFORMATEX = new(
        0x05589F81, 0xC356, 0x11CE, 0xBF, 0x01, 0x00, 0xAA, 0x00, 0x55, 0x59, 0x5A);
}

internal static class KsTopologyNative
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FilterMethod3Fn(IntPtr thisPtr, IntPtr ksData, uint cbKsData, IntPtr unused,
        out IntPtr outEnumerator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumGetCountFn(IntPtr thisPtr, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumGetItemFn(IntPtr thisPtr, uint index, out IntPtr outItem);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PartActivateFn(IntPtr thisPtr, ClsCtx clsContext, ref Guid iid, out IntPtr outInterface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int KsIsFormatSupportedFn(IntPtr thisPtr, IntPtr pKsFormat, uint cbFormat, out int supported);

    public static int CallFilterByDataFormat(
        IntPtr filterPtr,
        IntPtr ksData,
        uint cbKsData,
        out IntPtr outEnumerator)
    {
        FilterMethod3Fn fn = ReadVtableSlot<FilterMethod3Fn>(filterPtr, 3);
        return fn(filterPtr, ksData, cbKsData, IntPtr.Zero, out outEnumerator);
    }

    public static int CallEnumeratorGetCount(IntPtr enumeratorPtr, out uint count)
    {
        EnumGetCountFn fn = ReadVtableSlot<EnumGetCountFn>(enumeratorPtr, 3);
        return fn(enumeratorPtr, out count);
    }

    public static int CallEnumeratorGetItem(IntPtr enumeratorPtr, uint index, out IntPtr outItem)
    {
        EnumGetItemFn fn = ReadVtableSlot<EnumGetItemFn>(enumeratorPtr, 4);
        return fn(enumeratorPtr, index, out outItem);
    }

    public static int CallPartActivate(
        IntPtr partPtr,
        ClsCtx clsContext,
        Guid iid,
        out IntPtr outInterface)
    {
        PartActivateFn fn = ReadVtableSlot<PartActivateFn>(partPtr, 13);
        return fn(partPtr, clsContext, ref iid, out outInterface);
    }

    public static int CallIsFormatSupported(
        IntPtr formatSupportPtr,
        IntPtr pKsFormat,
        uint cbFormat,
        out bool supported)
    {
        KsIsFormatSupportedFn fn = ReadVtableSlot<KsIsFormatSupportedFn>(formatSupportPtr, 3);
        int hr = fn(formatSupportPtr, pKsFormat, cbFormat, out int rawSupported);
        supported = rawSupported != 0;
        return hr;
    }

    private static TDelegate ReadVtableSlot<TDelegate>(IntPtr objPtr, int slotIndex)
        where TDelegate : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(objPtr);
        IntPtr slot = Marshal.ReadIntPtr(vtable, slotIndex * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(slot);
    }
}
