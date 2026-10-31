using System.Runtime.InteropServices;
using System.Text;

namespace TrayAppDotNETCommon.Interop;

/// <summary>32-bit BGRA pixels (unpremultiplied, top-down rows) read from an icon or bitmap handle.</summary>
public readonly record struct IconPixels(int Width, int Height, byte[] Bgra);

// Win32 + COM bindings for icon extraction, plus the shared pixel readers used by Volume's AppIconResolver and Task
// Manager's ProcessIconService. Each app keeps its own extraction order, size and cache. DestroyIcon, GetDC/ReleaseDC
// and CreateIconFromResourceEx live in User32.cs.
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
        LR_DEFAULTCOLOR = 0x00000000
    }

    public enum IconCursorVersion
    {
        Default = 0x00030000
    }

    // SHGetImageFromShellItem flags used by the shared shell icon pipelines.
    [Flags]
    public enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0,
        SIIGBF_ICONONLY = 0x00000004
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
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;

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

    /// <summary>The largest icon side the readers accept; bigger handles are refused before allocating.</summary>
    public const int DefaultMaximumDimension = 256;

    /// <summary>
    /// Reads an HICON. Uses the colour bitmap, deriving alpha from the AND mask when the colour bitmap carries none
    /// (low-bpp icons), or the monochrome mask pair when the icon has no colour bitmap. Every GDI object is released.
    /// </summary>
    public static IconPixels? TryReadIconPixels(IntPtr iconHandle, int maximumDimension = DefaultMaximumDimension)
    {
        if (iconHandle == IntPtr.Zero || !GetIconInfo(iconHandle, out ICONINFO iconInfo)) return null;

        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero)
            {
                IconPixels? maskPair = iconInfo.hbmMask == IntPtr.Zero
                    ? null
                    : TryReadBitmapPixels(iconInfo.hbmMask, checked(maximumDimension * 2));
                return maskPair is { } mask ? ConvertMonochromeMask(mask) : null;
            }

            IconPixels? color = TryReadBitmapPixels(iconInfo.hbmColor, maximumDimension);
            if (color is not { } colorPixels || iconInfo.hbmMask == IntPtr.Zero || HasAnyAlpha(colorPixels.Bgra))
                return color;

            IconPixels? andMask = TryReadBitmapPixels(iconInfo.hbmMask, checked(maximumDimension * 2));
            if (andMask is { } maskPixels && maskPixels.Width == colorPixels.Width && maskPixels.Height >= colorPixels.Height)
                ApplyMaskAlpha(colorPixels.Bgra, maskPixels.Bgra);
            else
                SetOpaque(colorPixels.Bgra);
            return colorPixels;
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
        }
    }

    /// <summary>Reads an HBITMAP as 32-bit BGRA; null when the handle is invalid, empty or larger than the cap.</summary>
    public static IconPixels? TryReadBitmapPixels(IntPtr bitmapHandle, int maximumDimension = DefaultMaximumDimension)
    {
        if (bitmapHandle == IntPtr.Zero
            || GetObject(bitmapHandle, Marshal.SizeOf<BITMAP>(), out BITMAP bitmapInfo) == 0)
            return null;

        int width = bitmapInfo.bmWidth;
        int height = Math.Abs(bitmapInfo.bmHeight);
        if (!IsAcceptedSize(width, height, maximumDimension)) return null;

        byte[] pixels = new byte[checked(width * 4 * height)];
        IntPtr deviceContext = User32.GetDC(IntPtr.Zero);
        if (deviceContext == IntPtr.Zero) return null;

        try
        {
            BITMAPINFO requestedFormat = new()
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                    biSizeImage = (uint)pixels.Length
                }
            };

            fixed (byte* pixelPointer = pixels)
            {
                int copiedLineCount = GetDIBits(
                    deviceContext,
                    bitmapHandle,
                    start: 0,
                    (uint)height,
                    (IntPtr)pixelPointer,
                    ref requestedFormat,
                    DIB_RGB_COLORS);
                return copiedLineCount == 0 ? null : new IconPixels(width, height, pixels);
            }
        }
        finally
        {
            _ = User32.ReleaseDC(IntPtr.Zero, deviceContext);
        }
    }

    /// <summary>
    /// Asks the shell for an item image: an AUMID through the Apps known folder (falling back to the parsing name), or
    /// a file path through its parsing name.
    /// </summary>
    public static IconPixels? TryExtractShellImage(
        string identity,
        bool isApplicationID,
        int size,
        SIIGBF flags,
        int maximumDimension = DefaultMaximumDimension)
    {
        if (string.IsNullOrWhiteSpace(identity)) return null;

        IntPtr factoryPointer = IntPtr.Zero;
        try
        {
            int result = isApplicationID
                ? SHCreateItemInKnownFolder(
                    in AppsFolderID,
                    KF_FLAG_DONT_VERIFY,
                    identity,
                    in ShellItemImageFactoryIid,
                    out factoryPointer)
                : SHCreateItemFromParsingName(identity, IntPtr.Zero, in ShellItemImageFactoryIid, out factoryPointer);
            if ((result < 0 || factoryPointer == IntPtr.Zero) && isApplicationID)
                result = SHCreateItemFromParsingName(identity, IntPtr.Zero, in ShellItemImageFactoryIid, out factoryPointer);
            if (result < 0 || factoryPointer == IntPtr.Zero) return null;

            IntPtr bitmapHandle = IntPtr.Zero;
            try
            {
                using ShellImageFactory factory = new(factoryPointer);
                factoryPointer = IntPtr.Zero;
                result = factory.GetImage(new SIZE { cx = size, cy = size }, flags, out bitmapHandle);
                return result < 0 || bitmapHandle == IntPtr.Zero
                    ? null
                    : TryReadBitmapPixels(bitmapHandle, maximumDimension);
            }
            finally
            {
                if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
            }
        }
        finally
        {
            if (factoryPointer != IntPtr.Zero) Marshal.Release(factoryPointer);
        }
    }

    /// <summary>Extracts icon <paramref name="iconIndex"/> of a file at the requested size through SHDefExtractIcon.</summary>
    public static IconPixels? TryExtractDefaultIcon(
        string iconFile,
        int iconIndex,
        int size,
        int maximumDimension = DefaultMaximumDimension)
    {
        if (string.IsNullOrWhiteSpace(iconFile) || size is <= 0 or > ushort.MaxValue) return null;

        IntPtr largeIconHandle = IntPtr.Zero;
        IntPtr smallIconHandle = IntPtr.Zero;
        try
        {
            uint iconSizes = ((uint)size << 16) | (uint)size;
            int result = SHDefExtractIconW(iconFile, iconIndex, uFlags: 0, out largeIconHandle, out smallIconHandle, iconSizes);
            if (result < 0) return null;

            IntPtr selectedIconHandle = largeIconHandle != IntPtr.Zero ? largeIconHandle : smallIconHandle;
            return TryReadIconPixels(selectedIconHandle, maximumDimension);
        }
        finally
        {
            if (largeIconHandle != IntPtr.Zero) User32.DestroyIcon(largeIconHandle);
            if (smallIconHandle != IntPtr.Zero) User32.DestroyIcon(smallIconHandle);
        }
    }

    /// <summary>Returns whether a bitmap of this size may be read (positive and within the cap).</summary>
    public static bool IsAcceptedSize(int width, int height, int maximumDimension) =>
        width > 0 && height > 0 && width <= maximumDimension && height <= maximumDimension;

    /// <summary>Returns whether any pixel carries a non-zero alpha byte.</summary>
    public static bool HasAnyAlpha(ReadOnlySpan<byte> bgra)
    {
        for (int index = 3; index < bgra.Length; index += 4)
        {
            if (bgra[index] != 0) return true;
        }

        return false;
    }

    /// <summary>
    /// Derives alpha for a colour bitmap without one from the AND mask (read as 32-bit BGRA, same width, at least as
    /// tall): a black mask pixel is opaque, a white one transparent.
    /// </summary>
    public static void ApplyMaskAlpha(Span<byte> colorBgra, ReadOnlySpan<byte> maskBgra)
    {
        int pixelCount = Math.Min(colorBgra.Length, maskBgra.Length) / 4;
        for (int pixel = 0; pixel < pixelCount; pixel++)
        {
            int offset = pixel * 4;
            bool transparent = maskBgra[offset] != 0 || maskBgra[offset + 1] != 0 || maskBgra[offset + 2] != 0;
            colorBgra[offset + 3] = transparent ? (byte)0 : (byte)255;
        }
    }

    /// <summary>
    /// Converts a monochrome icon (an AND mask stacked over an XOR mask, each <c>height / 2</c> rows) to BGRA: AND set
    /// means transparent, otherwise the XOR bit gives white or black.
    /// </summary>
    public static IconPixels? ConvertMonochromeMask(IconPixels maskPair)
    {
        if (maskPair.Height < 2 || maskPair.Height % 2 != 0) return null;

        int height = maskPair.Height / 2;
        int halfLength = maskPair.Width * 4 * height;
        byte[] pixels = new byte[halfLength];
        for (int offset = 0; offset < halfLength; offset += 4)
        {
            bool transparent = maskPair.Bgra[offset] != 0;
            byte color = maskPair.Bgra[halfLength + offset] != 0 ? (byte)255 : (byte)0;
            pixels[offset] = color;
            pixels[offset + 1] = color;
            pixels[offset + 2] = color;
            pixels[offset + 3] = transparent ? (byte)0 : (byte)255;
        }

        return new IconPixels(maskPair.Width, height, pixels);
    }

    private static void SetOpaque(Span<byte> bgra)
    {
        for (int index = 3; index < bgra.Length; index += 4)
            bgra[index] = 255;
    }
}
