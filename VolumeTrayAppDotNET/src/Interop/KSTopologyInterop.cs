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
        0x2B0711DE, 0xDAB7, 0x4610, 0xA1, 0x6F, 0xD3, 0x38, 0x37, 0x49, 0xB2, 0x20);

    // IKsFormatSupport: canonical interface mmsys.cpl uses to populate its Default Format dropdown.
    // IsFormatSupported(PKSDATAFORMAT pKSFormat, ULONG cbFormat, BOOL *pbSupported):
    //   - pKSFormat: pointer to KSDATAFORMAT_WAVEFORMATEX (104 bytes for a 40-byte WAVEFORMATEXTENSIBLE)
    //   - cbFormat: size of the format buffer in bytes (104)
    //   - pbSupported: out BOOL (32-bit int) - TRUE/FALSE for whether the format is supported
    public static readonly Guid IID_IKsFormatSupport = new(
        0x3CB4A69D, 0xBB6F, 0x4D2B, 0x95, 0xB7, 0x45, 0x2D, 0x2C, 0x15, 0x5D, 0xB5);

    // Public topology and kernel-streaming interfaces used to submit the Bluetooth audio
    // driver's filter-level KSPROPERTY_ONESHOT_RECONNECT request.
    public static readonly Guid IID_IDeviceTopology = new(
        0x2A07407E, 0x6497, 0x4A18, 0x97, 0x87, 0x32, 0xF7, 0x9B, 0xD0, 0xD9, 0x8F);

    public static readonly Guid IID_IKsControl = new(
        0x28F54685, 0x06FD, 0x11D2, 0xB2, 0x7A, 0x00, 0xA0, 0xC9, 0x22, 0x31, 0x96);

    public static readonly Guid KSPROPSETID_BtAudio = new(
        0x7FA06C40, 0xB8F6, 0x4C7E, 0x85, 0x56, 0xE8, 0xC3, 0x3A, 0x12, 0xE5, 0x4D);

    public const uint KSPROPERTY_ONESHOT_RECONNECT = 0;
    public const uint KSPROPERTY_TYPE_GET = 0x00000001;

    // KSDATAFORMAT_TYPE_AUDIO: MajorFormat for audio formats.
    public static readonly Guid KSDATAFORMAT_TYPE_AUDIO = new(
        0x73647561, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);

    // KSDATAFORMAT_SPECIFIER_WAVEFORMATEX: Specifier value for a KSDATAFORMAT whose payload is
    // a WAVEFORMATEX (variable size) immediately following the 64-byte KSDATAFORMAT header.
    public static readonly Guid KSDATAFORMAT_SPECIFIER_WAVEFORMATEX = new(
        0x05589F81, 0xC356, 0x11CE, 0xBF, 0x01, 0x00, 0xAA, 0x00, 0x55, 0x59, 0x5A);
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
        FilterMethod3Fn filterByDataFormat = ReadVtableSlot<FilterMethod3Fn>(filterPtr, 3);
        return filterByDataFormat(filterPtr, ksData, ksDataByteCount, IntPtr.Zero, out outEnumerator);
    }

    public static int CallEnumeratorGetCount(IntPtr enumeratorPtr, out uint count)
    {
        EnumGetCountFn getEnumeratorCount = ReadVtableSlot<EnumGetCountFn>(enumeratorPtr, 3);
        return getEnumeratorCount(enumeratorPtr, out count);
    }

    public static int CallEnumeratorGetItem(IntPtr enumeratorPtr, uint index, out IntPtr outItem)
    {
        EnumGetItemFn getEnumeratorItem = ReadVtableSlot<EnumGetItemFn>(enumeratorPtr, 4);
        return getEnumeratorItem(enumeratorPtr, index, out outItem);
    }

    public static int CallPartActivate(
        IntPtr partPtr,
        ClsCtx clsContext,
        Guid iid,
        out IntPtr outInterface)
    {
        PartActivateFn activatePartInterface = ReadVtableSlot<PartActivateFn>(partPtr, 13);
        return activatePartInterface(partPtr, clsContext, ref iid, out outInterface);
    }

    public static int CallIsFormatSupported(
        IntPtr formatSupportPtr,
        IntPtr ksFormat,
        uint cbFormat,
        out bool supported)
    {
        KSIsFormatSupportedFn isFormatSupported = ReadVtableSlot<KSIsFormatSupportedFn>(formatSupportPtr, 3);
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
        TopologyGetConnectorFn getTopologyConnector = ReadVtableSlot<TopologyGetConnectorFn>(topologyPtr, 4);
        return getTopologyConnector(topologyPtr, connectorIndex, out connector);
    }

    public static int CallConnectorGetDeviceIDConnectedTo(IntPtr connectorPtr, out IntPtr deviceID)
    {
        // IConnector is its own IUnknown-derived interface. Its eight methods occupy slots 3-10;
        // GetDeviceIdConnectedTo is the final method at slot 10.
        ConnectorGetDeviceIDConnectedToFn getConnectedDeviceID =
            ReadVtableSlot<ConnectorGetDeviceIDConnectedToFn>(connectorPtr, 10);
        return getConnectedDeviceID(connectorPtr, out deviceID);
    }

    public static int CallKSProperty(
        IntPtr ksControlPtr,
        ref KSPropertyRequest property,
        out uint bytesReturned)
    {
        KSPropertyFn submitKSProperty = ReadVtableSlot<KSPropertyFn>(ksControlPtr, 3);
        return submitKSProperty(
            ksControlPtr,
            ref property,
            (uint)Marshal.SizeOf<KSPropertyRequest>(),
            IntPtr.Zero,
            0,
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
