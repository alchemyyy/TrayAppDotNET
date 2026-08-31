using System.Runtime.InteropServices;

namespace VolumeTrayAppDotNET.Interop;

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
        a: 0x2B0711DE, b: 0xDAB7, c: 0x4610, d: 0xA1, e: 0x6F, f: 0xD3, g: 0x38, h: 0x37, i: 0x49, j: 0xB2, k: 0x20);

    // IKsFormatSupport: canonical interface mmsys.cpl uses to populate its Default Format dropdown.
    // IsFormatSupported(PKSDATAFORMAT pKSFormat, ULONG cbFormat, BOOL *pbSupported):
    //   - pKSFormat: pointer to KSDATAFORMAT_WAVEFORMATEX (104 bytes for a 40-byte WAVEFORMATEXTENSIBLE)
    //   - cbFormat: size of the format buffer in bytes (104)
    //   - pbSupported: out BOOL (32-bit int) - TRUE/FALSE for whether the format is supported
    public static readonly Guid IID_IKsFormatSupport = new(
        a: 0x3CB4A69D, b: 0xBB6F, c: 0x4D2B, d: 0x95, e: 0xB7, f: 0x45, g: 0x2D, h: 0x2C, i: 0x15, j: 0x5D, k: 0xB5);

    // Public topology and kernel-streaming interfaces used to submit the Bluetooth audio
    // driver's filter-level KSPROPERTY_ONESHOT_RECONNECT request.
    public static readonly Guid IID_IDeviceTopology = new(
        a: 0x2A07407E, b: 0x6497, c: 0x4A18, d: 0x97, e: 0x87, f: 0x32, g: 0xF7, h: 0x9B, i: 0xD0, j: 0xD9, k: 0x8F);

    public static readonly Guid IID_IKsControl = new(
        a: 0x28F54685, b: 0x06FD, c: 0x11D2, d: 0xB2, e: 0x7A, f: 0x00, g: 0xA0, h: 0xC9, i: 0x22, j: 0x31, k: 0x96);

    public static readonly Guid KSPROPSETID_BtAudio = new(
        a: 0x7FA06C40, b: 0xB8F6, c: 0x4C7E, d: 0x85, e: 0x56, f: 0xE8, g: 0xC3, h: 0x3A, i: 0x12, j: 0xE5, k: 0x4D);

    public const uint KSPROPERTY_ONESHOT_RECONNECT = 0;
    public const uint KSPROPERTY_TYPE_GET = 0x00000001;

    // KSDATAFORMAT_TYPE_AUDIO: MajorFormat for audio formats.
    public static readonly Guid KSDATAFORMAT_TYPE_AUDIO = new(
        a: 0x73647561, b: 0x0000, c: 0x0010, d: 0x80, e: 0x00, f: 0x00, g: 0xAA, h: 0x00, i: 0x38, j: 0x9B, k: 0x71);

    // KSDATAFORMAT_SPECIFIER_WAVEFORMATEX: Specifier value for a KSDATAFORMAT whose payload is
    // a WAVEFORMATEX (variable size) immediately following the 64-byte KSDATAFORMAT header.
    public static readonly Guid KSDATAFORMAT_SPECIFIER_WAVEFORMATEX = new(
        a: 0x05589F81, b: 0xC356, c: 0x11CE, d: 0xBF, e: 0x01, f: 0x00, g: 0xAA, h: 0x00, i: 0x55, j: 0x59, k: 0x5A);
}

internal static class KSTopologyNative
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FilterMethod3Fn(IntPtr thisPtr, IntPtr ksData, uint ksDataByteCount, IntPtr unused,
        out IntPtr outEnumerator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumGetCountFn(IntPtr thisPtr, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumGetItemFn(IntPtr thisPtr, uint index, out IntPtr outItem);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PartActivateFn(IntPtr thisPtr, ClsCtx clsContext, ref Guid iid, out IntPtr outInterface);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int KSIsFormatSupportedFn(IntPtr thisPtr, IntPtr ksFormat, uint cbFormat, out int supported);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TopologyGetConnectorFn(IntPtr thisPtr, uint connectorIndex, out IntPtr connector);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ConnectorGetDeviceIDConnectedToFn(IntPtr thisPtr, out IntPtr deviceID);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int KSPropertyFn(
        IntPtr thisPtr,
        ref KSPropertyRequest property,
        uint propertyLength,
        IntPtr propertyData,
        uint dataLength,
        out uint bytesReturned);

    [StructLayout(LayoutKind.Sequential)]
    internal struct KSPropertyRequest
    {
        public Guid Set;
        public uint ID;
        public uint Flags;
    }

    public static int CallFilterByDataFormat(
        IntPtr filterPtr,
        IntPtr ksData,
        uint ksDataByteCount,
        out IntPtr outEnumerator)
    {
        FilterMethod3Fn filterByDataFormat = ReadVtableSlot<FilterMethod3Fn>(filterPtr, slotIndex: 3);
        return filterByDataFormat(filterPtr, ksData, ksDataByteCount, IntPtr.Zero, out outEnumerator);
    }

    public static int CallEnumeratorGetCount(IntPtr enumeratorPtr, out uint count)
    {
        EnumGetCountFn getEnumeratorCount = ReadVtableSlot<EnumGetCountFn>(enumeratorPtr, slotIndex: 3);
        return getEnumeratorCount(enumeratorPtr, out count);
    }

    public static int CallEnumeratorGetItem(IntPtr enumeratorPtr, uint index, out IntPtr outItem)
    {
        EnumGetItemFn getEnumeratorItem = ReadVtableSlot<EnumGetItemFn>(enumeratorPtr, slotIndex: 4);
        return getEnumeratorItem(enumeratorPtr, index, out outItem);
    }

    public static int CallPartActivate(
        IntPtr partPtr,
        ClsCtx clsContext,
        Guid iid,
        out IntPtr outInterface)
    {
        PartActivateFn activatePartInterface = ReadVtableSlot<PartActivateFn>(partPtr, slotIndex: 13);
        return activatePartInterface(partPtr, clsContext, ref iid, out outInterface);
    }

    public static int CallIsFormatSupported(
        IntPtr formatSupportPtr,
        IntPtr ksFormat,
        uint cbFormat,
        out bool supported)
    {
        KSIsFormatSupportedFn isFormatSupported = ReadVtableSlot<KSIsFormatSupportedFn>(formatSupportPtr, slotIndex: 3);
        int hr = isFormatSupported(formatSupportPtr, ksFormat, cbFormat, out int rawSupported);
        supported = rawSupported != 0;
        return hr;
    }

    public static int CallTopologyGetConnector(
        IntPtr topologyPtr,
        uint connectorIndex,
        out IntPtr connector)
    {
        // IUnknown (0-2), GetConnectorCount (3), GetConnector (4).
        TopologyGetConnectorFn getTopologyConnector = ReadVtableSlot<TopologyGetConnectorFn>(topologyPtr, slotIndex: 4);
        return getTopologyConnector(topologyPtr, connectorIndex, out connector);
    }

    public static int CallConnectorGetDeviceIDConnectedTo(IntPtr connectorPtr, out IntPtr deviceID)
    {
        // IConnector is its own IUnknown-derived interface. Its eight methods occupy slots 3-10;
        // GetDeviceIdConnectedTo is the final method at slot 10.
        ConnectorGetDeviceIDConnectedToFn getConnectedDeviceID =
            ReadVtableSlot<ConnectorGetDeviceIDConnectedToFn>(connectorPtr, slotIndex: 10);
        return getConnectedDeviceID(connectorPtr, out deviceID);
    }

    public static int CallKSProperty(
        IntPtr ksControlPtr,
        ref KSPropertyRequest property,
        out uint bytesReturned)
    {
        KSPropertyFn submitKSProperty = ReadVtableSlot<KSPropertyFn>(ksControlPtr, slotIndex: 3);
        return submitKSProperty(
            ksControlPtr,
            ref property,
            (uint)Marshal.SizeOf<KSPropertyRequest>(),
            IntPtr.Zero,
            dataLength: 0,
            out bytesReturned);
    }

    private static TDelegate ReadVtableSlot<TDelegate>(IntPtr objPtr, int slotIndex)
        where TDelegate : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(objPtr);
        IntPtr slot = Marshal.ReadIntPtr(vtable, slotIndex * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<TDelegate>(slot);
    }
}
