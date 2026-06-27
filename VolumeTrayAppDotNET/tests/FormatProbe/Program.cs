using System.Runtime.InteropServices;

namespace FormatProbe;

// Self-contained probe-comparison harness for the audio-format enumeration problem.
// Finds the "Realtek Digital Output" endpoint, runs each candidate algorithm against it,
// and prints what each one returns vs what mmsys.cpl shows.
//
// Expected formats (per mmsys.cpl on the test machine):
//   2 channel, 16 bit, 44100/48000/88200/96000/192000 Hz
//   2 channel, 24 bit, 44100/48000/88200/96000/192000 Hz
internal static class Program
{
    // Curated rate candidates that mmsys.cpl-style dropdowns probe.
    private static readonly int[] Rates16 =
        { 8000, 11025, 16000, 22050, 32000, 44100, 48000, 88200, 96000, 176400, 192000 };
    private static readonly int[] Rates24 =
        { 44100, 48000, 88200, 96000, 176400, 192000 };

    // Ground truth per the user, keyed by device-name substring.
    private static readonly (string NameSubstring, HashSet<(int Channels, int Bits, int Rate)> Formats)[] DeviceExpectations =
    {
        ("Realtek Digital Output", new HashSet<(int, int, int)>
        {
            (2, 16, 44100), (2, 16, 48000), (2, 16, 88200), (2, 16, 96000), (2, 16, 192000),
            (2, 24, 44100), (2, 24, 48000), (2, 24, 88200), (2, 24, 96000), (2, 24, 192000),
        }),
        ("AT2020USB-X", new HashSet<(int, int, int)>
        {
            (1, 16, 44100), (1, 16, 48000), (1, 16, 88200), (1, 16, 96000),
            (1, 24, 44100), (1, 24, 48000), (1, 24, 88200), (1, 24, 96000),
        }),
    };

    private static HashSet<(int Channels, int Bits, int Rate)> Expected = new();

    internal static int Main()
    {
        int hr = Ole32.CoInitializeEx(IntPtr.Zero, Ole32.COINIT_MULTITHREADED);
        if (hr < 0 && hr != unchecked((int)0x80010106))
        {
            Console.WriteLine($"CoInitializeEx failed: 0x{hr:X8}");
            return 1;
        }

        try
        {
            return RunAll();
        }
        finally
        {
            Ole32.CoUninitialize();
        }
    }

    private static int RunAll()
    {
        int totalFailures = 0;
        foreach (var (nameSubstring, formats) in DeviceExpectations)
        {
            Console.WriteLine();
            Console.WriteLine($"############ {nameSubstring} ############");
            Expected = formats;
            int rc = RunForDevice(nameSubstring);
            if (rc != 0) totalFailures++;
        }
        return totalFailures;
    }

    private static int RunForDevice(string nameSubstring)
    {
        IMMDevice? device = FindDevice(nameSubstring);
        if (device == null)
        {
            Console.WriteLine($"Could not find '{nameSubstring}' endpoint - enumerating both flows:");
            DumpAllDevices();
            return 2;
        }

        string name = ReadFriendlyName(device);
        Console.WriteLine($"Target endpoint: {name}");
        Console.WriteLine();

        Console.WriteLine("Expected formats (mmsys.cpl):");
        PrintFormats(Expected.OrderBy(t => t.Channels).ThenBy(t => t.Bits).ThenBy(t => t.Rate));

        int approaches = 0;

        approaches += RunApproach("H. mmsys.cpl algorithm: 24-in-32 container",
            () => ApproachMmsys2b07FilteredEnumerator(device, container24: 32));

        approaches += RunApproach("H2. mmsys.cpl algorithm: 24-in-24 container (packed)",
            () => ApproachMmsys2b07FilteredEnumerator(device, container24: 24));

        approaches += RunApproach("H3. mmsys.cpl algorithm: 24-in-32 OR 24-in-24 (union)",
            () => ApproachMmsys2b07Union(device));

        Console.WriteLine();
        Console.WriteLine($"Approaches matching expected exactly: {approaches}");
        return approaches > 0 ? 0 : 3;
    }

    private static void DumpControlInterfaces(IMMDevice device)
    {
        Guid iidTopology = typeof(IDeviceTopology).GUID;
        int hr = device.Activate(iidTopology, Ole32.CLSCTX_ALL, IntPtr.Zero, out object? topoObj);
        if (hr < 0 || topoObj == null) { Console.WriteLine("  Activate(IDeviceTopology) failed"); return; }
        IDeviceTopology endpointTopology = (IDeviceTopology)topoObj;
        try
        {
            Console.WriteLine("-- endpoint-side topology --");
            DumpTopologyControlInterfaces(endpointTopology, "endpoint");

            hr = endpointTopology.GetConnector(0, out IntPtr endpointConnPtr);
            if (hr < 0 || endpointConnPtr == IntPtr.Zero) return;
            try
            {
                IConnector endpointConn = (IConnector)Marshal.GetObjectForIUnknown(endpointConnPtr);
                hr = endpointConn.GetConnectedTo(out IntPtr deviceSideConnPtr);
                if (hr < 0 || deviceSideConnPtr == IntPtr.Zero) return;
                try
                {
                    Guid iidPart = typeof(IPart).GUID;
                    hr = Marshal.QueryInterface(deviceSideConnPtr, in iidPart, out IntPtr devicePartPtr);
                    if (hr < 0 || devicePartPtr == IntPtr.Zero) return;
                    try
                    {
                        IPart deviceSidePart = (IPart)Marshal.GetObjectForIUnknown(devicePartPtr);
                        hr = deviceSidePart.GetTopologyObject(out IntPtr deviceTopologyPtr);
                        if (hr < 0 || deviceTopologyPtr == IntPtr.Zero) return;
                        try
                        {
                            IDeviceTopology deviceTopology = (IDeviceTopology)Marshal.GetObjectForIUnknown(deviceTopologyPtr);
                            Console.WriteLine("-- adapter-side topology --");
                            Console.WriteLine("[device-side connector]:");
                            DumpPartControlInterfaces(deviceSidePart);
                            DumpTopologyControlInterfaces(deviceTopology, "adapter");
                        }
                        finally { Marshal.Release(deviceTopologyPtr); }
                    }
                    finally { Marshal.Release(devicePartPtr); }
                }
                finally { Marshal.Release(deviceSideConnPtr); }
            }
            finally { Marshal.Release(endpointConnPtr); }
        }
        finally { Marshal.FinalReleaseComObject(endpointTopology); }
    }

    private static void DumpTopologyControlInterfaces(IDeviceTopology topology, string label)
    {
        if (topology.GetConnectorCount(out uint connCount) >= 0)
        {
            for (uint i = 0; i < connCount; i++)
            {
                Console.WriteLine($"[{label} connector {i}]:");
                if (topology.GetConnector(i, out IntPtr cp) >= 0 && cp != IntPtr.Zero)
                {
                    try { DumpPartControlInterfacesFromPtr(cp); }
                    finally { Marshal.Release(cp); }
                }
            }
        }
        if (topology.GetSubunitCount(out uint subCount) >= 0)
        {
            for (uint i = 0; i < subCount; i++)
            {
                Console.WriteLine($"[{label} subunit {i}]:");
                if (topology.GetSubunit(i, out IntPtr sp) >= 0 && sp != IntPtr.Zero)
                {
                    try { DumpPartControlInterfacesFromPtr(sp); }
                    finally { Marshal.Release(sp); }
                }
            }
        }
    }

    private static void DumpPartControlInterfacesFromPtr(IntPtr rawPartPtr)
    {
        Guid iidPart = typeof(IPart).GUID;
        int hr = Marshal.QueryInterface(rawPartPtr, in iidPart, out IntPtr ipartPtr);
        if (hr < 0 || ipartPtr == IntPtr.Zero) { Console.WriteLine("    (no IPart)"); return; }
        try
        {
            IPart part = (IPart)Marshal.GetObjectForIUnknown(ipartPtr);
            DumpPartControlInterfaces(part);
        }
        finally { Marshal.Release(ipartPtr); }
    }

    // Probes the private mmsys.cpl IIDs. We just want to know which ones IMMDevice::Activate
    // accepts and what known interface IIDs the returned object supports via QI.
    private static void ProbePrivateIIDs(IMMDevice device)
    {
        // From the mmsys.cpl decompile: the IID used in CEndpointFormatChanger::Initialize
        // and CPageFormat::PopulateHWAudioEngineFormatList.
        Guid iid2b07 = new Guid("2b0711de-dab7-4610-a16f-d3383749b220");
        TryActivate(device, iid2b07, "IID_2b0711de (mmsys private)");

        // IHardwareAudioEngineBase - mmsys.cpl QIs the activated object for this to detect
        // whether the endpoint has hardware audio engine support (offloading).
        Guid iidHwEngine = new Guid("67c5fc9c-29e1-4154-8307-84ed8edb5a21");

        // 67c5fc9c is documented as IHardwareAudioEngineBase. Try Activate directly.
        TryActivate(device, iidHwEngine, "IHardwareAudioEngineBase");
    }

    // Per mmsys.cpl's PopulateHWAudioEngineFormatList:
    //   v14 = IMMDevice::Activate(IID_2b0711de, CLSCTX_ALL, NULL)
    //   v13 = v14->vtable[8](&v13)   // returns sub-interface
    //   pv  = v13->vtable[6](&pv)   // returns format-list buffer
    //   pv layout: count at offset 4, entries at offset 72+ each 104 bytes
    private static void ProbePrivateActivateMethods(IMMDevice device)
    {
        Guid iid2b07 = new Guid("2b0711de-dab7-4610-a16f-d3383749b220");
        int hr = device.Activate(iid2b07, Ole32.CLSCTX_ALL, IntPtr.Zero, out object? obj);
        Console.WriteLine($"  Activate(IID_2b0711de) hr=0x{hr:X8}");
        if (hr < 0 || obj == null) return;

        IntPtr v14 = Marshal.GetIUnknownForObject(obj);
        try
        {
            // Call v14->vtable[8] (offset 64): expected signature (this, void** out)
            IntPtr vtable = Marshal.ReadIntPtr(v14);
            IntPtr slot8 = Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size);
            Console.WriteLine($"  v14 vtable[8] = 0x{slot8.ToInt64():X}");

            var fn8 = Marshal.GetDelegateForFunctionPointer<Vtable8Fn>(slot8);
            IntPtr v13 = IntPtr.Zero;
            int hr8 = fn8(v14, out v13);
            Console.WriteLine($"  v14->vtable[8](&out) hr=0x{hr8:X8} v13=0x{v13.ToInt64():X}");
            if (hr8 < 0 || v13 == IntPtr.Zero) return;

            try
            {
                // Call v13->vtable[6] (offset 48): expected signature (this, void** out_buf)
                IntPtr vt13 = Marshal.ReadIntPtr(v13);
                IntPtr slot6 = Marshal.ReadIntPtr(vt13, 6 * IntPtr.Size);
                Console.WriteLine($"  v13 vtable[6] = 0x{slot6.ToInt64():X}");

                var fn6 = Marshal.GetDelegateForFunctionPointer<Vtable6Fn>(slot6);
                IntPtr pv = IntPtr.Zero;
                int hr6 = fn6(v13, out pv);
                Console.WriteLine($"  v13->vtable[6](&out) hr=0x{hr6:X8} pv=0x{pv.ToInt64():X}");
                if (hr6 < 0 || pv == IntPtr.Zero) return;

                try
                {
                    // Parse: count at offset 4, entries at offset 72, each 104 bytes
                    uint count = (uint)Marshal.ReadInt32(pv, 4);
                    Console.WriteLine($"  count={count}");
                    for (uint i = 0; i < count; i++)
                    {
                        int entryOffset = 72 + (int)i * 104;
                        ushort formatTag = (ushort)Marshal.ReadInt16(pv, entryOffset);
                        ushort channels = (ushort)Marshal.ReadInt16(pv, entryOffset + 2);
                        uint sampleRate = (uint)Marshal.ReadInt32(pv, entryOffset + 4);
                        ushort blockAlign = (ushort)Marshal.ReadInt16(pv, entryOffset + 12);
                        ushort containerBits = (ushort)Marshal.ReadInt16(pv, entryOffset + 14);
                        ushort cbSize = (ushort)Marshal.ReadInt16(pv, entryOffset + 16);
                        ushort validBits = cbSize >= 22
                            ? (ushort)Marshal.ReadInt16(pv, entryOffset + 18)
                            : containerBits;
                        Console.WriteLine($"  entry[{i}] tag=0x{formatTag:X4} ch={channels} rate={sampleRate} blockAlign={blockAlign} container={containerBits} cbSize={cbSize} valid={validBits}");
                    }
                }
                finally { Marshal.FreeCoTaskMem(pv); }
            }
            finally { Marshal.Release(v13); }
        }
        finally
        {
            Marshal.Release(v14);
            Marshal.FinalReleaseComObject(obj);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Vtable8Fn(IntPtr thisPtr, out IntPtr outPtr);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Vtable6Fn(IntPtr thisPtr, out IntPtr outPtr);

    private static void TryActivate(IMMDevice device, Guid iid, string label)
    {
        int hr = device.Activate(iid, Ole32.CLSCTX_ALL, IntPtr.Zero, out object? obj);
        Console.WriteLine($"  Activate({label}) hr=0x{hr:X8}");
        if (hr < 0 || obj == null) return;

        IntPtr unk = Marshal.GetIUnknownForObject(obj);
        try
        {
            // QI for a battery of known IIDs to see what the object actually exposes.
            Guid[] iids =
            {
                new Guid("2a07407e-6497-4a18-9787-32f79bd0d98f"), // IDeviceTopology
                new Guid("9c2c4058-23f5-41de-877a-df3af236a09e"), // IConnector
                new Guid("ae2de0e4-5bca-4f2d-aa46-5d13f8fdb3a9"), // IPart
                new Guid("3cb4a69d-bb6f-4d2b-95b7-452d2c155db5"), // IKsFormatSupport
                new Guid("28f54685-06fd-11d2-b27a-00a0c9223196"), // IKsControl
                new Guid("67c5fc9c-29e1-4154-8307-84ed8edb5a21"), // IHardwareAudioEngineBase
                new Guid("82149a85-dba6-4487-86bb-ea8f7fefcc71"), // ISubunit
                new Guid("8fa906e4-c31c-4e31-932e-19a66385e9aa"), // IAudioInputSelector? (variant)
                new Guid("4f03dc02-5e6e-4653-8f72-a030c123d598"), // IAudioInputSelector
            };
            string[] names =
            {
                "IDeviceTopology", "IConnector", "IPart", "IKsFormatSupport",
                "IKsControl", "IHardwareAudioEngineBase", "ISubunit",
                "Unknown-8fa906e4", "IAudioInputSelector",
            };
            for (int i = 0; i < iids.Length; i++)
            {
                Guid local = iids[i];
                int qiHr = Marshal.QueryInterface(unk, in local, out IntPtr ptr);
                if (qiHr >= 0 && ptr != IntPtr.Zero)
                {
                    Console.WriteLine($"    QI({names[i]}) -> SUCCESS");
                    Marshal.Release(ptr);
                }
            }
        }
        finally
        {
            Marshal.Release(unk);
            Marshal.FinalReleaseComObject(obj);
        }
    }

    private static void DumpPartControlInterfaces(IPart part)
    {
        part.GetName(out string? partName);
        part.GetLocalId(out uint localId);
        part.GetPartType(out uint partType);
        part.GetSubType(out Guid subType);
        Console.WriteLine($"    name='{partName}' localId={localId} type={partType} subType={subType}");

        if (part.GetControlInterfaceCount(out uint count) < 0) { Console.WriteLine("    (count failed)"); return; }
        if (count == 0) { Console.WriteLine("    (no control interfaces)"); return; }
        for (uint i = 0; i < count; i++)
        {
            if (part.GetControlInterface(i, out IntPtr ciPtr) < 0 || ciPtr == IntPtr.Zero) continue;
            try
            {
                IControlInterface ci = (IControlInterface)Marshal.GetObjectForIUnknown(ciPtr);
                ci.GetName(out string? name);
                ci.GetIID(out Guid iid);
                Console.WriteLine($"    [{i}] {iid} - {name}");
            }
            finally { Marshal.Release(ciPtr); }
        }
    }

    // ----- Approach harness ------------------------------------------------

    private static int RunApproach(
        string title,
        Func<HashSet<(int, int, int)>?> approach)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {title} ===");
        try
        {
            HashSet<(int, int, int)>? result = approach();
            if (result == null)
            {
                Console.WriteLine("  (approach returned null - inner step failed)");
                return 0;
            }
            if (result.Count == 0)
            {
                Console.WriteLine("  (empty result)");
                return 0;
            }

            PrintFormats(result.OrderBy(t => t.Item1).ThenBy(t => t.Item2).ThenBy(t => t.Item3));

            HashSet<(int, int, int)> missing = new(Expected);
            missing.ExceptWith(result);
            HashSet<(int, int, int)> extra = new(result);
            extra.ExceptWith(Expected);

            if (missing.Count == 0 && extra.Count == 0)
            {
                Console.WriteLine("  EXACT MATCH");
                return 1;
            }
            if (missing.Count > 0)
            {
                Console.WriteLine($"  Missing {missing.Count}:");
                foreach (var t in missing.OrderBy(x => x.Item1).ThenBy(x => x.Item2).ThenBy(x => x.Item3))
                    Console.WriteLine($"    -{t.Item1}ch/{t.Item2}-bit/{t.Item3}Hz");
            }
            if (extra.Count > 0)
            {
                Console.WriteLine($"  Extra {extra.Count}:");
                foreach (var t in extra.OrderBy(x => x.Item1).ThenBy(x => x.Item2).ThenBy(x => x.Item3))
                    Console.WriteLine($"    +{t.Item1}ch/{t.Item2}-bit/{t.Item3}Hz");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  exception: {ex.GetType().Name} {ex.Message}");
            return 0;
        }
    }

    private static void PrintFormats(IEnumerable<(int, int, int)> formats)
    {
        foreach (var t in formats)
            Console.WriteLine($"  {t.Item1} channel, {t.Item2} bit, {t.Item3} Hz");
    }

    // ----- Approaches ------------------------------------------------------

    private static HashSet<(int, int, int)>? ApproachEndpointSideFormatSupport(IMMDevice device)
    {
        Guid iidTopology = typeof(IDeviceTopology).GUID;
        int hr = device.Activate(iidTopology, Ole32.CLSCTX_ALL, IntPtr.Zero, out object? topoObj);
        if (hr < 0 || topoObj == null) { Console.WriteLine($"  Activate(IDeviceTopology) hr=0x{hr:X8}"); return null; }
        IDeviceTopology topology = (IDeviceTopology)topoObj;
        try
        {
            hr = topology.GetConnector(0, out IntPtr connectorPtr);
            Console.WriteLine($"  GetConnector(0) hr=0x{hr:X8}");
            if (hr < 0 || connectorPtr == IntPtr.Zero) return null;
            try
            {
                IntPtr fsPtr = ActivateFromPart(connectorPtr, typeof(IKsFormatSupport).GUID, "endpoint connector");
                if (fsPtr == IntPtr.Zero) return null;
                try
                {
                    IKsFormatSupport fs = (IKsFormatSupport)Marshal.GetObjectForIUnknown(fsPtr);
                    return ProbeWithFormatSupport(fs);
                }
                finally { Marshal.Release(fsPtr); }
            }
            finally { Marshal.Release(connectorPtr); }
        }
        finally { Marshal.FinalReleaseComObject(topology); }
    }

    private static HashSet<(int, int, int)>? ApproachDeviceSideFormatSupport(IMMDevice device)
    {
        Guid iidTopology = typeof(IDeviceTopology).GUID;
        int hr = device.Activate(iidTopology, Ole32.CLSCTX_ALL, IntPtr.Zero, out object? topoObj);
        if (hr < 0 || topoObj == null) { Console.WriteLine($"  Activate(IDeviceTopology) hr=0x{hr:X8}"); return null; }
        IDeviceTopology topology = (IDeviceTopology)topoObj;
        try
        {
            hr = topology.GetConnector(0, out IntPtr endpointConnectorPtr);
            if (hr < 0 || endpointConnectorPtr == IntPtr.Zero) return null;
            try
            {
                IConnector endpointConnector = (IConnector)Marshal.GetObjectForIUnknown(endpointConnectorPtr);
                hr = endpointConnector.GetConnectedTo(out IntPtr deviceSideConnectorPtr);
                Console.WriteLine($"  GetConnectedTo hr=0x{hr:X8}");
                if (hr < 0 || deviceSideConnectorPtr == IntPtr.Zero) return null;
                try
                {
                    IntPtr fsPtr = ActivateFromPart(deviceSideConnectorPtr, typeof(IKsFormatSupport).GUID, "device-side connector");
                    if (fsPtr == IntPtr.Zero) return null;
                    try
                    {
                        IKsFormatSupport fs = (IKsFormatSupport)Marshal.GetObjectForIUnknown(fsPtr);
                        return ProbeWithFormatSupport(fs);
                    }
                    finally { Marshal.Release(fsPtr); }
                }
                finally { Marshal.Release(deviceSideConnectorPtr); }
            }
            finally { Marshal.Release(endpointConnectorPtr); }
        }
        finally { Marshal.FinalReleaseComObject(topology); }
    }

    private static HashSet<(int, int, int)>? ApproachDeviceTopologyWalk(IMMDevice device)
    {
        Guid iidTopology = typeof(IDeviceTopology).GUID;
        int hr = device.Activate(iidTopology, Ole32.CLSCTX_ALL, IntPtr.Zero, out object? topoObj);
        if (hr < 0 || topoObj == null) return null;
        IDeviceTopology endpointTopology = (IDeviceTopology)topoObj;
        try
        {
            hr = endpointTopology.GetConnector(0, out IntPtr endpointConnectorPtr);
            if (hr < 0 || endpointConnectorPtr == IntPtr.Zero) return null;
            try
            {
                IConnector endpointConnector = (IConnector)Marshal.GetObjectForIUnknown(endpointConnectorPtr);
                hr = endpointConnector.GetConnectedTo(out IntPtr deviceSideConnectorPtr);
                if (hr < 0 || deviceSideConnectorPtr == IntPtr.Zero) return null;
                try
                {
                    Guid iidPart = typeof(IPart).GUID;
                    hr = Marshal.QueryInterface(deviceSideConnectorPtr, in iidPart, out IntPtr devicePartPtr);
                    if (hr < 0 || devicePartPtr == IntPtr.Zero) return null;
                    try
                    {
                        IPart deviceSidePart = (IPart)Marshal.GetObjectForIUnknown(devicePartPtr);
                        hr = deviceSidePart.GetTopologyObject(out IntPtr deviceTopologyPtr);
                        Console.WriteLine($"  GetTopologyObject hr=0x{hr:X8}");
                        if (hr < 0 || deviceTopologyPtr == IntPtr.Zero) return null;
                        try
                        {
                            IDeviceTopology deviceTopology = (IDeviceTopology)Marshal.GetObjectForIUnknown(deviceTopologyPtr);

                            IntPtr fsPtr = FindFormatSupportInTopology(deviceTopology, deviceSidePart);
                            if (fsPtr == IntPtr.Zero) return null;
                            try
                            {
                                IKsFormatSupport fs = (IKsFormatSupport)Marshal.GetObjectForIUnknown(fsPtr);
                                return ProbeWithFormatSupport(fs);
                            }
                            finally { Marshal.Release(fsPtr); }
                        }
                        finally { Marshal.Release(deviceTopologyPtr); }
                    }
                    finally { Marshal.Release(devicePartPtr); }
                }
                finally { Marshal.Release(deviceSideConnectorPtr); }
            }
            finally { Marshal.Release(endpointConnectorPtr); }
        }
        finally { Marshal.FinalReleaseComObject(endpointTopology); }
    }

    private static IntPtr FindFormatSupportInTopology(IDeviceTopology topology, IPart deviceSidePart)
    {
        Guid iidFormatSupport = typeof(IKsFormatSupport).GUID;

        int hr = deviceSidePart.Activate(Ole32.CLSCTX_INPROC_SERVER, ref iidFormatSupport, out IntPtr ptr);
        Console.WriteLine($"  part[device-side connector] Activate(IKsFormatSupport) hr=0x{hr:X8}");
        if (hr >= 0 && ptr != IntPtr.Zero) return ptr;

        if (topology.GetConnectorCount(out uint connCount) >= 0)
        {
            for (uint i = 0; i < connCount; i++)
            {
                if (topology.GetConnector(i, out IntPtr cp) < 0 || cp == IntPtr.Zero) continue;
                try
                {
                    IntPtr fs = ActivateFromPart(cp, iidFormatSupport, $"connector {i}");
                    if (fs != IntPtr.Zero) return fs;
                }
                finally { Marshal.Release(cp); }
            }
        }

        if (topology.GetSubunitCount(out uint subCount) >= 0)
        {
            for (uint i = 0; i < subCount; i++)
            {
                if (topology.GetSubunit(i, out IntPtr sp) < 0 || sp == IntPtr.Zero) continue;
                try
                {
                    IntPtr fs = ActivateFromPart(sp, iidFormatSupport, $"subunit {i}");
                    if (fs != IntPtr.Zero) return fs;
                }
                finally { Marshal.Release(sp); }
            }
        }
        return IntPtr.Zero;
    }

    private static IntPtr ActivateFromPart(IntPtr rawPartPtr, Guid iid, string label)
    {
        Guid iidPart = typeof(IPart).GUID;
        int hr = Marshal.QueryInterface(rawPartPtr, in iidPart, out IntPtr ipartPtr);
        if (hr < 0 || ipartPtr == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            IPart part = (IPart)Marshal.GetObjectForIUnknown(ipartPtr);
            hr = part.Activate(Ole32.CLSCTX_INPROC_SERVER, ref iid, out IntPtr fsPtr);
            Console.WriteLine($"  part[{label}] Activate hr=0x{hr:X8}");
            if (hr < 0 || fsPtr == IntPtr.Zero) return IntPtr.Zero;
            return fsPtr;
        }
        finally { Marshal.Release(ipartPtr); }
    }

    private static HashSet<(int, int, int)> ProbeWithFormatSupport(IKsFormatSupport formatSupport, ushort container24 = 32)
    {
        HashSet<(int, int, int)> result = new();
        foreach (int channels in new[] { 1, 2 })
        {
            foreach (int rate in Rates16)
            {
                if (ProbeFormatSupport(formatSupport, (ushort)channels, 16, 16, (uint)rate))
                    result.Add((channels, 16, rate));
            }
            foreach (int rate in Rates24)
            {
                if (ProbeFormatSupport(formatSupport, (ushort)channels, 24, container24, (uint)rate))
                    result.Add((channels, 24, rate));
            }
        }
        return result;
    }

    private static bool ProbeFormatSupport(
        IKsFormatSupport fs, ushort channels, ushort validBits, ushort containerBits, uint rate)
    {
        const int KsHeader = 64;
        const int Wfx = 40;
        const int Total = KsHeader + Wfx; // 104

        IntPtr p = Marshal.AllocHGlobal(Total);
        try
        {
            for (int i = 0; i < Total; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteInt32(p, 0, Total);
            byte[] major = Guids.KSDATAFORMAT_TYPE_AUDIO.ToByteArray();
            byte[] sub = Guids.KSDATAFORMAT_SUBTYPE_PCM.ToByteArray();
            byte[] spec = Guids.KSDATAFORMAT_SPECIFIER_WAVEFORMATEX.ToByteArray();
            Marshal.Copy(major, 0, IntPtr.Add(p, 16), 16);
            Marshal.Copy(sub, 0, IntPtr.Add(p, 32), 16);
            Marshal.Copy(spec, 0, IntPtr.Add(p, 48), 16);
            byte[] wfx = BuildWfxExtensible(channels, validBits, containerBits, rate);
            Marshal.Copy(wfx, 0, IntPtr.Add(p, KsHeader), Wfx);

            int hr = fs.IsFormatSupported(p, Total, out bool supported);
            return hr >= 0 && supported;
        }
        finally { Marshal.FreeHGlobal(p); }
    }

    // IAudioClient.IsFormatSupported approach (NAudio / cscore style).
    // mmsys.cpl's CEndpointFormatChanger path:
    //   v14 = IMMDevice::Activate(IID_2b0711de, CLSCTX_INPROC_SERVER) - treated as IDeviceTopology
    //   v14->GetConnector(0, &iPart) - vtable[4]
    //   iPart->Activate(CLSCTX_INPROC_SERVER, IID_IKsFormatSupport, &fs) - vtable[13]
    //   then per-format IKsFormatSupport::IsFormatSupported probing
    private static HashSet<(int, int, int)>? ApproachMmsys2b07TopologyChain(IMMDevice device)
    {
        Guid iid2b07 = new Guid("2b0711de-dab7-4610-a16f-d3383749b220");
        int hr = device.Activate(iid2b07, Ole32.CLSCTX_INPROC_SERVER, IntPtr.Zero, out object? obj);
        Console.WriteLine($"  Activate(IID_2b0711de, CLSCTX_INPROC_SERVER) hr=0x{hr:X8}");
        if (hr < 0 || obj == null) return null;

        IntPtr v14 = Marshal.GetIUnknownForObject(obj);
        try
        {
            // Call vtable[4] (offset 32) = IDeviceTopology::GetConnector(UINT, IConnector**)
            IntPtr vtable = Marshal.ReadIntPtr(v14);
            IntPtr slot4 = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
            var getConnector = Marshal.GetDelegateForFunctionPointer<GetConnectorFn>(slot4);
            IntPtr connectorPtr = IntPtr.Zero;
            int gcHr = getConnector(v14, 0, out connectorPtr);
            Console.WriteLine($"  v14->vtable[4](GetConnector 0) hr=0x{gcHr:X8} ptr=0x{connectorPtr.ToInt64():X}");
            if (gcHr < 0 || connectorPtr == IntPtr.Zero) return null;

            try
            {
                // QI for IPart
                Guid iidPart = typeof(IPart).GUID;
                hr = Marshal.QueryInterface(connectorPtr, in iidPart, out IntPtr ipartPtr);
                Console.WriteLine($"  QI(IPart) hr=0x{hr:X8}");
                if (hr < 0 || ipartPtr == IntPtr.Zero) return null;
                try
                {
                    IPart part = (IPart)Marshal.GetObjectForIUnknown(ipartPtr);
                    Guid iidFs = typeof(IKsFormatSupport).GUID;
                    hr = part.Activate(Ole32.CLSCTX_INPROC_SERVER, ref iidFs, out IntPtr fsPtr);
                    Console.WriteLine($"  part->Activate(IKsFormatSupport) hr=0x{hr:X8}");
                    if (hr < 0 || fsPtr == IntPtr.Zero) return null;
                    try
                    {
                        IKsFormatSupport fs = (IKsFormatSupport)Marshal.GetObjectForIUnknown(fsPtr);
                        return ProbeWithFormatSupport(fs);
                    }
                    finally { Marshal.Release(fsPtr); }
                }
                finally { Marshal.Release(ipartPtr); }
            }
            finally { Marshal.Release(connectorPtr); }
        }
        finally
        {
            Marshal.Release(v14);
            Marshal.FinalReleaseComObject(obj);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetConnectorFn(IntPtr thisPtr, uint index, out IntPtr connector);

    // mmsys.cpl line 25981-25994:
    //   v16 = IMMDevice::Activate(IID_2b0711de, CLSCTX_INPROC_SERVER, NULL)
    //   v15 = v16->vtable[3](&ksDataFormat, 64, NULL, &out)  // get format-filtered part enumerator
    //   v12 = v15->vtable[3](&count)                          // GetCount
    //   v11 = v15->vtable[4](index, &part)                    // GetItem -> IPart
    //   then enumerate v11's IControlInterface entries to find IKsFormatSupport
    private static HashSet<(int, int, int)>? ApproachMmsys2b07Union(IMMDevice device)
    {
        HashSet<(int, int, int)>? a = ApproachMmsys2b07FilteredEnumerator(device, container24: 32);
        HashSet<(int, int, int)>? b = ApproachMmsys2b07FilteredEnumerator(device, container24: 24);
        if (a == null && b == null) return null;
        HashSet<(int, int, int)> u = a ?? new();
        if (b != null) u.UnionWith(b);
        return u;
    }

    private static HashSet<(int, int, int)>? ApproachMmsys2b07FilteredEnumerator(IMMDevice device, ushort container24 = 32)
    {
        Guid iid2b07 = new Guid("2b0711de-dab7-4610-a16f-d3383749b220");
        int hr = device.Activate(iid2b07, Ole32.CLSCTX_INPROC_SERVER, IntPtr.Zero, out object? obj);
        Console.WriteLine($"  Activate(IID_2b0711de, CLSCTX_INPROC_SERVER) hr=0x{hr:X8}");
        if (hr < 0 || obj == null) return null;

        IntPtr v16 = Marshal.GetIUnknownForObject(obj);
        try
        {
            // Build the 64-byte KSDATAFORMAT (header only, no WFX payload).
            // bytes:  0..3   FormatSize = 64
            //         4..7   Flags = 0
            //         8..11  SampleSize = 0
            //        12..15  Reserved = 0
            //        16..31  MajorFormat = KSDATAFORMAT_TYPE_AUDIO
            //        32..47  SubFormat = KSDATAFORMAT_SUBTYPE_PCM
            //        48..63  Specifier = KSDATAFORMAT_SPECIFIER_WAVEFORMATEX
            IntPtr ksData = Marshal.AllocHGlobal(64);
            try
            {
                for (int i = 0; i < 64; i++) Marshal.WriteByte(ksData, i, 0);
                Marshal.WriteInt32(ksData, 0, 64);
                Marshal.Copy(Guids.KSDATAFORMAT_TYPE_AUDIO.ToByteArray(), 0, IntPtr.Add(ksData, 16), 16);
                Marshal.Copy(Guids.KSDATAFORMAT_SUBTYPE_PCM.ToByteArray(), 0, IntPtr.Add(ksData, 32), 16);
                Marshal.Copy(Guids.KSDATAFORMAT_SPECIFIER_WAVEFORMATEX.ToByteArray(), 0, IntPtr.Add(ksData, 48), 16);

                // v16->vtable[3](ksData, 64, NULL, &out_enumerator)
                IntPtr vtable16 = Marshal.ReadIntPtr(v16);
                IntPtr slot3 = Marshal.ReadIntPtr(vtable16, 3 * IntPtr.Size);
                var fn3 = Marshal.GetDelegateForFunctionPointer<Filter3Fn>(slot3);

                IntPtr enumPtr = IntPtr.Zero;
                int fhr = fn3(v16, ksData, 64, IntPtr.Zero, out enumPtr);
                Console.WriteLine($"  v16->vtable[3](&ksData, 64, NULL, &out) hr=0x{fhr:X8} enum=0x{enumPtr.ToInt64():X}");
                if (fhr < 0 || enumPtr == IntPtr.Zero) return null;

                try
                {
                    IntPtr vtable15 = Marshal.ReadIntPtr(enumPtr);
                    IntPtr slot3_15 = Marshal.ReadIntPtr(vtable15, 3 * IntPtr.Size);
                    IntPtr slot4_15 = Marshal.ReadIntPtr(vtable15, 4 * IntPtr.Size);
                    var getCount = Marshal.GetDelegateForFunctionPointer<GetCountFn>(slot3_15);
                    var getItem = Marshal.GetDelegateForFunctionPointer<GetItemFn>(slot4_15);

                    int ghr = getCount(enumPtr, out uint count);
                    Console.WriteLine($"  enum->GetCount hr=0x{ghr:X8} count={count}");
                    if (ghr < 0 || count == 0) return null;

                    Guid iidPart = typeof(IPart).GUID;
                    Guid iidFs = typeof(IKsFormatSupport).GUID;

                    for (uint i = 0; i < count; i++)
                    {
                        int ihr = getItem(enumPtr, i, out IntPtr itemPtr);
                        Console.WriteLine($"  enum->GetItem({i}) hr=0x{ihr:X8} ptr=0x{itemPtr.ToInt64():X}");
                        if (ihr < 0 || itemPtr == IntPtr.Zero) continue;

                        try
                        {
                            int qhr = Marshal.QueryInterface(itemPtr, in iidPart, out IntPtr partPtr);
                            if (qhr < 0 || partPtr == IntPtr.Zero) { Console.WriteLine($"    QI(IPart) hr=0x{qhr:X8}"); continue; }
                            try
                            {
                                IPart part = (IPart)Marshal.GetObjectForIUnknown(partPtr);
                                part.GetName(out string? pname);
                                part.GetLocalId(out uint plid);
                                Console.WriteLine($"    part name='{pname}' localId={plid}");
                                int ahr = part.Activate(Ole32.CLSCTX_INPROC_SERVER, ref iidFs, out IntPtr fsPtr);
                                Console.WriteLine($"    part->Activate(IKsFormatSupport) hr=0x{ahr:X8}");
                                if (ahr >= 0 && fsPtr != IntPtr.Zero)
                                {
                                    try
                                    {
                                        IKsFormatSupport fs = (IKsFormatSupport)Marshal.GetObjectForIUnknown(fsPtr);
                                        return ProbeWithFormatSupport(fs, container24);
                                    }
                                    finally { Marshal.Release(fsPtr); }
                                }
                            }
                            finally { Marshal.Release(partPtr); }
                        }
                        finally { Marshal.Release(itemPtr); }
                    }
                    return null;
                }
                finally { Marshal.Release(enumPtr); }
            }
            finally { Marshal.FreeHGlobal(ksData); }
        }
        finally
        {
            Marshal.Release(v16);
            Marshal.FinalReleaseComObject(obj);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Filter3Fn(IntPtr thisPtr, IntPtr ksData, uint cbKsData, IntPtr unused, out IntPtr outEnumerator);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCountFn(IntPtr thisPtr, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetItemFn(IntPtr thisPtr, uint index, out IntPtr outItem);

    private static HashSet<(int, int, int)>? ApproachAudioClient(
        IMMDevice device, bool exclusive, bool acceptSFalse)
    {
        Guid iidAudioClient = typeof(IAudioClient).GUID;
        int hr = device.Activate(iidAudioClient, Ole32.CLSCTX_ALL, IntPtr.Zero, out object? clientObj);
        if (hr < 0 || clientObj == null) return null;
        IAudioClient client = (IAudioClient)clientObj;
        try
        {
            HashSet<(int, int, int)> result = new();
            uint shareMode = exclusive ? 1u : 0u;
            foreach (int channels in new[] { 1, 2 })
            {
                foreach (int rate in Rates16)
                {
                    if (ProbeAudioClient(client, shareMode, acceptSFalse, (ushort)channels, 16, 16, (uint)rate))
                        result.Add((channels, 16, rate));
                }
                foreach (int rate in Rates24)
                {
                    if (ProbeAudioClient(client, shareMode, acceptSFalse, (ushort)channels, 24, 32, (uint)rate))
                        result.Add((channels, 24, rate));
                }
            }
            return result;
        }
        finally { Marshal.FinalReleaseComObject(client); }
    }

    private static bool ProbeAudioClient(
        IAudioClient client, uint shareMode, bool acceptSFalse,
        ushort channels, ushort validBits, ushort containerBits, uint rate)
    {
        byte[] wfx = BuildWfxExtensible(channels, validBits, containerBits, rate);
        IntPtr p = Marshal.AllocHGlobal(wfx.Length);
        IntPtr closest = IntPtr.Zero;
        try
        {
            Marshal.Copy(wfx, 0, p, wfx.Length);
            int hr = client.IsFormatSupported(shareMode, p, out closest);
            return hr == 0 || (acceptSFalse && hr == 1);
        }
        finally
        {
            if (closest != IntPtr.Zero) Marshal.FreeCoTaskMem(closest);
            Marshal.FreeHGlobal(p);
        }
    }

    // ----- WAVEFORMATEXTENSIBLE construction -------------------------------

    private static byte[] BuildWfxExtensible(ushort channels, ushort validBits, ushort containerBits, uint rate)
    {
        const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;
        const ushort CB = 22;
        ushort blockAlign = (ushort)(channels * (containerBits / 8));
        uint bytesPerSec = rate * blockAlign;
        uint mask = channels switch
        {
            1 => 0x4u, 2 => 0x3u, 4 => 0x33u, 6 => 0x3Fu, 8 => 0xFFu,
            _ => channels >= 32 ? 0u : (1u << channels) - 1,
        };

        byte[] b = new byte[40];
        BitConverter.GetBytes(WAVE_FORMAT_EXTENSIBLE).CopyTo(b, 0);
        BitConverter.GetBytes(channels).CopyTo(b, 2);
        BitConverter.GetBytes(rate).CopyTo(b, 4);
        BitConverter.GetBytes(bytesPerSec).CopyTo(b, 8);
        BitConverter.GetBytes(blockAlign).CopyTo(b, 12);
        BitConverter.GetBytes(containerBits).CopyTo(b, 14);
        BitConverter.GetBytes(CB).CopyTo(b, 16);
        BitConverter.GetBytes(validBits).CopyTo(b, 18);
        BitConverter.GetBytes(mask).CopyTo(b, 20);
        Guids.KSDATAFORMAT_SUBTYPE_PCM.ToByteArray().CopyTo(b, 24);
        return b;
    }

    // ----- Endpoint enumeration --------------------------------------------

    private static IMMDevice? FindDevice(string nameSubstring)
    {
        // 2 = eAll (both render and capture). Mic shows up under capture.
        MMDeviceEnumerator enumeratorObj = new MMDeviceEnumerator();
        IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)enumeratorObj;
        try
        {
            int hr = enumerator.EnumAudioEndpoints(2, 0x00000001, out IMMDeviceCollection coll);
            if (hr < 0) return null;
            try
            {
                coll.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    coll.Item(i, out IMMDevice device);
                    string name = ReadFriendlyName(device);
                    if (name.Contains(nameSubstring, StringComparison.OrdinalIgnoreCase))
                        return device;
                    Marshal.FinalReleaseComObject(device);
                }
            }
            finally { Marshal.FinalReleaseComObject(coll); }
        }
        finally { Marshal.FinalReleaseComObject(enumeratorObj); }
        return null;
    }

    private static void DumpAllDevices()
    {
        MMDeviceEnumerator enumeratorObj = new MMDeviceEnumerator();
        IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)enumeratorObj;
        try
        {
            enumerator.EnumAudioEndpoints(2, 0x00000001, out IMMDeviceCollection coll);
            coll.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                coll.Item(i, out IMMDevice d);
                Console.WriteLine($"  [{i}] {ReadFriendlyName(d)}");
                Marshal.FinalReleaseComObject(d);
            }
            Marshal.FinalReleaseComObject(coll);
        }
        finally { Marshal.FinalReleaseComObject(enumeratorObj); }
    }

    private static string ReadFriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(0 /*STGM_READ*/, out IPropertyStore store);
        try
        {
            PROPERTYKEY key = PropertyKeys.PKEY_Device_FriendlyName;
            store.GetValue(ref key, out PROPVARIANT pv);
            try
            {
                if (pv.vt == 31 /*VT_LPWSTR*/ && pv.ptr != IntPtr.Zero)
                    return Marshal.PtrToStringUni(pv.ptr) ?? "<null>";
                return "<no-name>";
            }
            finally { Ole32.PropVariantClear(ref pv); }
        }
        finally { Marshal.FinalReleaseComObject(store); }
    }
}

// ===== Interop ============================================================

internal static class Ole32
{
    public const uint CLSCTX_INPROC_SERVER = 0x1;
    public const uint CLSCTX_ALL = 0x17;
    public const uint COINIT_MULTITHREADED = 0x0;

    [DllImport("ole32.dll")] public static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
    [DllImport("ole32.dll")] public static extern void CoUninitialize();
    [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PROPVARIANT pvar);
}

[ComImport, Guid("bcde0395-e52f-467c-8e3d-c4579291692e")]
internal class MMDeviceEnumerator { }

[ComImport, Guid("a95664d2-9614-4f35-a746-de8db63617e6"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint dwStateMask,
        [MarshalAs(UnmanagedType.Interface)] out IMMDeviceCollection devices);
    void Unused_GetDefaultAudioEndpoint();
    void Unused_GetDevice();
    void Unused_RegisterEndpointNotificationCallback();
    void Unused_UnregisterEndpointNotificationCallback();
}

[ComImport, Guid("0bd7a1be-7a1a-44db-8397-cc5392387b5e"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);
}

[ComImport, Guid("d666063f-1587-4e43-81f1-b948e807363f"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(
        [MarshalAs(UnmanagedType.LPStruct)] Guid iid, uint dwClsCtx, IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
    [PreserveSig] int OpenPropertyStore(uint stgmAccess, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out uint state);
}

[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void Unused_GetCount();
    void Unused_GetAt();
    [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    void Unused_SetValue();
    void Unused_Commit();
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPVARIANT
{
    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public IntPtr ptr;
    public IntPtr ptr2;
}

internal static class PropertyKeys
{
    public static PROPERTYKEY PKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 14,
    };
}

[ComImport, Guid("2a07407e-6497-4a18-9787-32f79bd0d98f"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    [PreserveSig] int GetConnectorCount(out uint count);
    [PreserveSig] int GetConnector(uint index, out IntPtr connector);
    [PreserveSig] int GetSubunitCount(out uint count);
    [PreserveSig] int GetSubunit(uint index, out IntPtr subunit);
    void Unused_GetPartById();
    void Unused_GetDeviceId();
    void Unused_GetSignalPath();
}

[ComImport, Guid("9c2c4058-23f5-41de-877a-df3af236a09e"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    void Unused_GetType();
    void Unused_GetDataFlow();
    void Unused_ConnectTo();
    void Unused_Disconnect();
    void Unused_IsConnected();
    [PreserveSig] int GetConnectedTo(out IntPtr connectedTo);
    void Unused_GetConnectorIdConnectedTo();
    void Unused_GetDeviceIdConnectedTo();
}

[ComImport, Guid("ae2de0e4-5bca-4f2d-aa46-5d13f8fdb3a9"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPart
{
    [PreserveSig] int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int GetLocalId(out uint id);
    [PreserveSig] int GetGlobalId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetPartType(out uint partType);
    [PreserveSig] int GetSubType(out Guid subType);
    [PreserveSig] int GetControlInterfaceCount(out uint count);
    [PreserveSig] int GetControlInterface(uint index, out IntPtr controlInterface);
    void Unused_EnumPartsIncoming();
    void Unused_EnumPartsOutgoing();
    [PreserveSig] int GetTopologyObject(out IntPtr topology);
    [PreserveSig] int Activate(uint dwClsContext, ref Guid refiid, out IntPtr interfacePointer);
    void Unused_RegisterControlChangeCallback();
    void Unused_UnregisterControlChangeCallback();
}

// IControlInterface: returned by IPart::GetControlInterface(i). Names the interface (LPWSTR)
// and exposes its IID. This is the canonical way to discover which Activate(IID) calls will
// succeed for a given part.
[ComImport, Guid("45d37c3f-5140-444a-ae24-400789f3cbf3"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IControlInterface
{
    [PreserveSig] int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int GetIID(out Guid iid);
}

[ComImport, Guid("3cb4a69d-bb6f-4d2b-95b7-452d2c155db5"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IKsFormatSupport
{
    [PreserveSig] int IsFormatSupported(IntPtr pKsFormat, uint cbFormat,
        [MarshalAs(UnmanagedType.Bool)] out bool supported);
    void Unused_GetDevicePreferredFormat();
}

[ComImport, Guid("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    void Unused_Initialize();
    void Unused_GetBufferSize();
    void Unused_GetStreamLatency();
    void Unused_GetCurrentPadding();
    [PreserveSig] int IsFormatSupported(uint shareMode, IntPtr pFormat, out IntPtr ppClosest);
    void Unused_GetMixFormat();
    void Unused_GetDevicePeriod();
    void Unused_Start();
    void Unused_Stop();
    void Unused_Reset();
    void Unused_SetEventHandle();
    void Unused_GetService();
}

internal static class Guids
{
    public static readonly Guid KSDATAFORMAT_TYPE_AUDIO = new(
        0x73647561, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
    public static readonly Guid KSDATAFORMAT_SUBTYPE_PCM = new(
        0x00000001, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
    public static readonly Guid KSDATAFORMAT_SPECIFIER_WAVEFORMATEX = new(
        0x05589F81, 0xC356, 0x11CE, 0xBF, 0x01, 0x00, 0xAA, 0x00, 0x55, 0x59, 0x5A);
}
