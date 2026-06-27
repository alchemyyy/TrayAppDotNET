using System.Runtime.InteropServices;
using System.Text;

namespace TrayAppDotNETCommon.Interop;

// Win32 + COM bindings consumed by AppIconResolver. Process P/Invokes live in Kernel32.cs;
// DestroyIcon lives in User32.cs; S_OK / ERROR_INSUFFICIENT_BUFFER live in NativeErrors.cs.
public static unsafe partial class IconExtraction
{
    public const int KF_FLAG_DONT_VERIFY = 0x00004000;
    public const int LOAD_LIBRARY_AS_DATAFILE = 0x02;
    public const int LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;
    public const int MAX_AUMID_LEN = 512;

    // Standard PE resource type ordinals (winuser.h: MAKEINTRESOURCE).
    public static readonly IntPtr RT_ICON = new(3);
    public static readonly IntPtr RT_GROUP_ICON = new(14);

    // Known-folder GUID for the Apps folder. Used as the parent folder when resolving a UWP AUMID
    // through SHCreateItemInKnownFolder.
    public static readonly Guid AppsFolderID = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");

    public enum LoadImageFlags : uint
    {
        LR_DEFAULTCOLOR = 0x00000000,
    }

    public enum IconCursorVersion
    {
        Default = 0x00030000,
    }

    // SHGetImageFromShellItem flag set; only RESIZETOFIT is needed for the icon use case.
    public enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0,
    }

    public const uint BI_RGB = 0;
    public const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    // -- kernel32 (icon-resource family only; process P/Invokes live in Kernel32.cs)

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibraryExW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpLibFileName,
        IntPtr hFile,
        int dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindResourceW")]
    public static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll")]
    public static extern int SizeofResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll")]
    public static extern int GetPackageId(IntPtr hProcess, ref int bufferLength, IntPtr packageId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetApplicationUserModelId(
        IntPtr hProcess,
        ref int applicationUserModelIdLength,
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder applicationUserModelId);

    // -- user32 (icon-resource family only; DestroyIcon lives in User32.cs)

    [DllImport("user32.dll")]
    public static extern int LookupIconIdFromDirectoryEx(
        IntPtr presbits,
        [MarshalAs(UnmanagedType.Bool)] bool fIcon,
        int cxDesired,
        int cyDesired,
        LoadImageFlags Flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconFromResourceEx(
        IntPtr presbits,
        int dwResSize,
        [MarshalAs(UnmanagedType.Bool)] bool fIcon,
        IconCursorVersion dwVer,
        int cxDesired,
        int cyDesired,
        LoadImageFlags Flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    // -- shlwapi

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int PathParseIconLocationW(
        [MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconFile);

    // -- gdi32

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    public static extern int GetObject(IntPtr hObject, int nCount, out BITMAP bitmap);

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbm,
        uint start,
        uint cLines,
        IntPtr lpvBits,
        ref BITMAPINFO lpbmi,
        uint usage);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    // -- shell32

    public static readonly Guid ShellItemImageFactoryIid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int SHDefExtractIconW(
        [MarshalAs(UnmanagedType.LPWStr)] string pszIconFile,
        int iIndex,
        uint uFlags,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIconSize);

    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemInKnownFolder",
        StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemInKnownFolder(
        in Guid kfid,
        uint dwKFFlags,
        string pszItem,
        in Guid riid,
        out IntPtr ppv);

    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName",
        StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        in Guid riid,
        out IntPtr ppv);

    public readonly struct ShellImageFactory : IDisposable
    {
        private readonly void* _ptr;

        public ShellImageFactory(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) throw new InvalidOperationException("Shell image factory returned a null pointer.");
            _ptr = ptr.ToPointer();
        }

        private void** VTable => *(void***)_ptr;

        public int GetImage(SIZE size, SIIGBF flags, out IntPtr hBitmap)
        {
            IntPtr bitmap = IntPtr.Zero;
            int hr = ((delegate* unmanaged[Stdcall]<void*, SIZE, SIIGBF, IntPtr*, int>)VTable[3])(
                _ptr,
                size,
                flags,
                &bitmap);
            hBitmap = bitmap;
            return hr;
        }

        public void Dispose()
        {
            if (_ptr == null) return;
            _ = ((delegate* unmanaged[Stdcall]<void*, uint>)VTable[2])(_ptr);
        }
    }
}
