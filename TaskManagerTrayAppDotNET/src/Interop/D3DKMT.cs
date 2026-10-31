using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Interop;

/// <summary>Graphics-kernel (D3DKMT) adapter handle and query calls shared by the GPU readers.</summary>
internal static unsafe class D3DKMT
{
    /// <summary>Opens a kernel adapter handle for a boot-local adapter LUID; false when the OS lacks D3DKMT.</summary>
    internal static bool TryOpenAdapter(ulong adapterLUID, out uint adapterHandle)
    {
        D3DKMT_OPENADAPTERFROMLUID openAdapter = new() { AdapterLUID = NATIVE_LUID.FromUInt64(adapterLUID) };
        int result;
        try
        {
            result = D3DKMTOpenAdapterFromLuid(ref openAdapter);
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                              or EntryPointNotFoundException
                                              or BadImageFormatException)
        {
            adapterHandle = 0;
            return false;
        }

        adapterHandle = result >= 0 ? openAdapter.AdapterHandle : 0;
        return adapterHandle != 0;
    }

    internal static void CloseAdapter(uint adapterHandle)
    {
        if (adapterHandle == 0) return;
        D3DKMT_CLOSEADAPTER closeAdapter = new() { AdapterHandle = adapterHandle };
        _ = D3DKMTCloseAdapter(ref closeAdapter);
    }

    /// <summary>Runs one KMTQUERYADAPTERINFOTYPE query into caller-owned private data.</summary>
    internal static bool TryQueryAdapterInfo(uint adapterHandle, int informationType, void* data, uint dataSize)
    {
        D3DKMT_QUERYADAPTERINFO query = new()
        {
            AdapterHandle = adapterHandle,
            Type = informationType,
            PrivateDriverData = (IntPtr)data,
            PrivateDriverDataSize = dataSize
        };
        return D3DKMTQueryAdapterInfo(ref query) >= 0;
    }

    /// <summary>Runs one query whose private data is a single unmanaged structure, filled in place.</summary>
    internal static bool TryQueryAdapterInfo<T>(uint adapterHandle, int informationType, ref T information)
        where T : unmanaged
    {
        fixed (T* informationPointer = &information)
            return TryQueryAdapterInfo(adapterHandle, informationType, informationPointer, checked((uint)sizeof(T)));
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NATIVE_LUID
    {
        public uint LowPart;
        public int HighPart;

        internal static NATIVE_LUID FromUInt64(ulong value) => new()
        {
            LowPart = (uint)value, HighPart = unchecked((int)(value >> 32))
        };

        internal readonly ulong ToUInt64() => ((ulong)(uint)HighPart << 32) | LowPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_OPENADAPTERFROMLUID
    {
        public NATIVE_LUID AdapterLUID;
        public uint AdapterHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint AdapterHandle;
        public int Type;
        public IntPtr PrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER
    {
        public uint AdapterHandle;
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(ref D3DKMT_OPENADAPTERFROMLUID openAdapter);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO queryAdapterInformation);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER closeAdapter);
}
