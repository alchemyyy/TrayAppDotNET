using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

/// <summary>
/// Native AOT-safe IShellLink / IPersistFile wrapper for Windows .lnk shortcuts.
/// </summary>
public static unsafe partial class ShellLink
{
    private const uint CLSCTX_INPROC_SERVER = 0x1;
    private const uint SLGP_RAWPATH = 0x0004;
    private const int MaxPath = 1024;

    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-c000-000000000046");
    private static readonly Guid ShellLinkIid = new("000214f9-0000-0000-c000-000000000046");
    private static readonly Guid PersistFileIid = new("0000010b-0000-0000-c000-000000000046");

    public static void Create(string lnkPath, string targetExe, string description)
    {
        using ComPtr link = CreateShellLink();

        ThrowIfFailed(link.SetPath(targetExe));

        string? workDir = Path.GetDirectoryName(targetExe);
        if (!string.IsNullOrEmpty(workDir)) ThrowIfFailed(link.SetWorkingDirectory(workDir));

        ThrowIfFailed(link.SetDescription(description));

        using ComPtr persist = link.Query(PersistFileIid);
        ThrowIfFailed(persist.Save(lnkPath, remember: true));
    }

    public static string? TryRead(string lnkPath, Action<string>? log = null)
    {
        try
        {
            using ComPtr link = CreateShellLink();
            using ComPtr persist = link.Query(PersistFileIid);

            ThrowIfFailed(persist.Load(lnkPath));

            string raw = link.GetPath();
            return string.IsNullOrEmpty(raw) ? null : raw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"ShellLink.TryRead({lnkPath}): {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static ComPtr CreateShellLink()
    {
        int hr = CoCreateInstance(
            in ShellLinkClsid,
            IntPtr.Zero,
            CLSCTX_INPROC_SERVER,
            in ShellLinkIid,
            out IntPtr ptr);

        ThrowIfFailed(hr);
        return new ComPtr(ptr);
    }

    private static void ThrowIfFailed(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IntPtr ppv);

    private readonly struct ComPtr : IDisposable
    {
        private readonly void* _ptr;

        public ComPtr(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) throw new InvalidOperationException("COM returned a null interface pointer.");
            _ptr = ptr.ToPointer();
        }

        private void** VTable => *(void***)_ptr;

        public ComPtr Query(Guid iid)
        {
            void* result = null;
            Guid localIid = iid;
            int hr = ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)VTable[0])(_ptr, &localIid, &result);
            ThrowIfFailed(hr);

            return new ComPtr((IntPtr)result);
        }

        public int SetDescription(string description)
        {
            fixed (char* p = description)
                return ((delegate* unmanaged[Stdcall]<void*, char*, int>)VTable[7])(_ptr, p);
        }

        public int SetWorkingDirectory(string directory)
        {
            fixed (char* p = directory)
                return ((delegate* unmanaged[Stdcall]<void*, char*, int>)VTable[9])(_ptr, p);
        }

        public int SetPath(string path)
        {
            fixed (char* p = path)
                return ((delegate* unmanaged[Stdcall]<void*, char*, int>)VTable[20])(_ptr, p);
        }

        public int Load(string path)
        {
            fixed (char* p = path)
                return ((delegate* unmanaged[Stdcall]<void*, char*, uint, int>)VTable[5])(_ptr, p, 0);
        }

        public int Save(string path, bool remember)
        {
            fixed (char* p = path)
                return ((delegate* unmanaged[Stdcall]<void*, char*, int, int>)VTable[6])(_ptr, p, remember ? 1 : 0);
        }

        public string GetPath()
        {
            char* buffer = stackalloc char[MaxPath];
            buffer[0] = '\0';
            int hr = ((delegate* unmanaged[Stdcall]<void*, char*, int, void*, uint, int>)VTable[3])(
                _ptr,
                buffer,
                MaxPath,
                null,
                SLGP_RAWPATH);
            ThrowIfFailed(hr);
            return new string(buffer);
        }

        public void Dispose()
        {
            if (_ptr == null) return;
            _ = ((delegate* unmanaged[Stdcall]<void*, uint>)VTable[2])(_ptr);
        }
    }
}
