using System.Runtime.InteropServices;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.UI.Tray;

public sealed class NativeIcon : IDisposable
{
    private const uint IconResourceVersion = 0x00030000;
    private bool _disposed;

    private NativeIcon(IntPtr handle)
    {
        Handle = handle == IntPtr.Zero
            ? throw new InvalidOperationException("Icon handle creation failed.")
            : handle;
    }

    public IntPtr Handle { get; private set; }

    public static NativeIcon FromIconImage(byte[] imageBytes, int desiredSize)
    {
        IntPtr handle = User32.CreateIconFromResourceEx(
            imageBytes,
            (uint)imageBytes.Length,
            fIcon: true,
            IconResourceVersion,
            desiredSize,
            desiredSize,
            flags: 0);

        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CreateIconFromResourceEx failed (0x{error:X8}).");
        }

        return new NativeIcon(handle);
    }

    public static NativeIcon FromIco(byte[] icoBytes, int desiredSize)
        => FromIconImage(ExtractICOImage(icoBytes, desiredSize), desiredSize);

    /// <summary>Extracts the ICO image whose dimensions most closely match the requested size.</summary>
    public static byte[] ExtractICOImage(byte[] ICOBytes, int desiredSize)
    {
        ArgumentNullException.ThrowIfNull(ICOBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(desiredSize, other: 1);
        if (ICOBytes.Length < 22
            || BitConverter.ToUInt16(ICOBytes, startIndex: 0) != 0
            || BitConverter.ToUInt16(ICOBytes, startIndex: 2) != 1)
            throw new InvalidOperationException("Invalid ICO data.");

        int imageCount = BitConverter.ToUInt16(ICOBytes, startIndex: 4);
        int bestOffset = 0;
        int bestLength = 0;
        int bestScore = int.MaxValue;

        for (int imageIndex = 0; imageIndex < imageCount; imageIndex++)
        {
            int entryOffset = 6 + imageIndex * 16;
            if (entryOffset + 16 > ICOBytes.Length) break;

            int width = ICOBytes[entryOffset] == 0 ? 256 : ICOBytes[entryOffset];
            int height = ICOBytes[entryOffset + 1] == 0 ? 256 : ICOBytes[entryOffset + 1];
            int bytesInResource = BitConverter.ToInt32(ICOBytes, entryOffset + 8);
            int imageOffset = BitConverter.ToInt32(ICOBytes, entryOffset + 12);
            long imageEnd = (long)imageOffset + bytesInResource;
            if (bytesInResource <= 0 || imageOffset < 0 || imageEnd > ICOBytes.Length)
                continue;

            int score = Math.Abs(width - desiredSize) + Math.Abs(height - desiredSize);
            if (score >= bestScore) continue;

            bestScore = score;
            bestOffset = imageOffset;
            bestLength = bytesInResource;
        }

        if (bestLength <= 0) throw new InvalidOperationException("ICO file did not contain a usable icon image.");

        return ICOBytes.AsSpan(bestOffset, bestLength).ToArray();
    }

    public NativeIcon Clone()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(NativeIcon));
        IntPtr clone = User32.CopyIcon(Handle);
        if (clone == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"CopyIcon failed (0x{error:X8}).");
        }

        return new NativeIcon(clone);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Handle != IntPtr.Zero)
        {
            _ = User32.DestroyIcon(Handle);
            Handle = IntPtr.Zero;
        }
    }
}
