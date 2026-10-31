using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Interop;

/// <summary>DXGI hardware-adapter enumeration shared by the GPU sampler and the GPU details reader.</summary>
internal static unsafe class DXGI
{
    private const int ErrorNotFound = unchecked((int)0x887A0002);
    private const uint AdapterFlagSoftware = 0x2;
    private const uint MaximumAdapterCount = 256;

    // IDXGIFactory1 vtable: EnumAdapters1 is slot 12; IDXGIAdapter1 vtable: GetDesc1 is slot 10
    private const int EnumAdapters1Slot = 12;
    private const int GetDesc1Slot = 10;

    private static readonly Guid FactoryInterfaceID = new("770aae78-f26f-4dba-a829-253c83d1b387");

    /// <summary>One hardware (non-software) adapter in DXGI enumeration order.</summary>
    internal readonly record struct HardwareAdapter(uint Index, DXGI_ADAPTER_DESC1 Description);

    /// <summary>Lists the hardware adapters; empty when DXGI is unavailable.</summary>
    internal static List<HardwareAdapter> EnumerateHardwareAdapters()
    {
        List<HardwareAdapter> adapters = [];
        if (!TryCreateFactory(out IntPtr factory)) return adapters;

        try
        {
            for (uint adapterIndex = 0; adapterIndex < MaximumAdapterCount; adapterIndex++)
            {
                int result = EnumAdapters(factory, adapterIndex, out IntPtr adapter);
                if (result == ErrorNotFound) break;
                if (result < 0 || adapter == IntPtr.Zero) continue;

                try
                {
                    if (TryGetHardwareDescription(adapter, out DXGI_ADAPTER_DESC1 description))
                        adapters.Add(new HardwareAdapter(adapterIndex, description));
                }
                finally
                {
                    _ = Marshal.Release(adapter);
                }
            }
        }
        finally
        {
            _ = Marshal.Release(factory);
        }

        return adapters;
    }

    /// <summary>
    /// Finds the hardware adapter with <paramref name="adapterLUID"/>. The caller owns the returned reference and
    /// releases it with <see cref="Marshal.Release"/>; zero when no adapter matches.
    /// </summary>
    internal static IntPtr FindHardwareAdapter(ulong adapterLUID)
    {
        if (!TryCreateFactory(out IntPtr factory)) return IntPtr.Zero;

        try
        {
            for (uint adapterIndex = 0; adapterIndex < MaximumAdapterCount; adapterIndex++)
            {
                int result = EnumAdapters(factory, adapterIndex, out IntPtr adapter);
                if (result == ErrorNotFound) break;
                if (result < 0 || adapter == IntPtr.Zero) continue;

                if (TryGetHardwareDescription(adapter, out DXGI_ADAPTER_DESC1 description)
                    && description.AdapterLUID.ToUInt64() == adapterLUID)
                    return adapter;

                _ = Marshal.Release(adapter);
            }

            return IntPtr.Zero;
        }
        finally
        {
            _ = Marshal.Release(factory);
        }
    }

    private static bool TryCreateFactory(out IntPtr factory)
    {
        Guid interfaceID = FactoryInterfaceID;
        int result;
        try
        {
            result = CreateDXGIFactory1(ref interfaceID, out factory);
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                              or EntryPointNotFoundException
                                              or BadImageFormatException)
        {
            factory = IntPtr.Zero;
            return false;
        }

        return result >= 0 && factory != IntPtr.Zero;
    }

    private static int EnumAdapters(IntPtr factory, uint adapterIndex, out IntPtr adapter)
    {
        IntPtr* vTable = *(IntPtr**)factory;
        delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int> enumAdapters =
            (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)vTable[EnumAdapters1Slot];
        IntPtr result = IntPtr.Zero;
        int status = enumAdapters(factory, adapterIndex, &result);
        adapter = result;
        return status;
    }

    private static bool TryGetHardwareDescription(IntPtr adapter, out DXGI_ADAPTER_DESC1 description)
    {
        IntPtr* vTable = *(IntPtr**)adapter;
        delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int> getDescription =
            (delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int>)vTable[GetDesc1Slot];
        DXGI_ADAPTER_DESC1 result = default;
        bool succeeded = getDescription(adapter, &result) >= 0 && (result.Flags & AdapterFlagSoftware) == 0;
        description = result;
        return succeeded;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid interfaceID, out IntPtr factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DXGI_ADAPTER_DESC1
    {
        private const int DescriptionLength = 128;

        public fixed char Description[DescriptionLength];
        public uint VendorID;
        public uint DeviceID;
        public uint SubsystemID;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public D3DKMT.NATIVE_LUID AdapterLUID;
        public uint Flags;

        internal string ReadDescription()
        {
            fixed (char* description = Description)
            {
                ReadOnlySpan<char> text = new(description, DescriptionLength);
                int terminator = text.IndexOf('\0');
                return (terminator >= 0 ? text[..terminator] : text).Trim().ToString();
            }
        }
    }
}
