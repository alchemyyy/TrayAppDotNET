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
    {
        if (icoBytes.Length < 22
            || BitConverter.ToUInt16(icoBytes, 0) != 0
            || BitConverter.ToUInt16(icoBytes, 2) != 1)
            throw new InvalidOperationException("Invalid ICO data.");

        int count = BitConverter.ToUInt16(icoBytes, 4);
        int bestOffset = 0;
        int bestLength = 0;
        int bestScore = int.MaxValue;

        for (int i = 0; i < count; i++)
        {
            int entry = 6 + (i * 16);
            if (entry + 16 > icoBytes.Length) break;

            int width = icoBytes[entry] == 0 ? 256 : icoBytes[entry];
            int height = icoBytes[entry + 1] == 0 ? 256 : icoBytes[entry + 1];
            int bytesInRes = BitConverter.ToInt32(icoBytes, entry + 8);
            int imageOffset = BitConverter.ToInt32(icoBytes, entry + 12);
            if (bytesInRes <= 0 || imageOffset < 0 || imageOffset + bytesInRes > icoBytes.Length)
                continue;

            int score = Math.Abs(width - desiredSize) + Math.Abs(height - desiredSize);
            if (score >= bestScore) continue;

            bestScore = score;
            bestOffset = imageOffset;
            bestLength = bytesInRes;
        }

        if (bestLength <= 0) throw new InvalidOperationException("ICO file did not contain a usable icon image.");

        byte[] imageBytes = icoBytes.AsSpan(bestOffset, bestLength).ToArray();
        return FromIconImage(imageBytes, desiredSize);
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
