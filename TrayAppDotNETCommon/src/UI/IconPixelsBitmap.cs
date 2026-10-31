using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.UI;

/// <summary>Turns extracted <see cref="IconPixels"/> into an Avalonia bitmap.</summary>
public static class IconPixelsBitmap
{
    /// <summary>Copies the pixels into a new 96 DPI unpremultiplied BGRA bitmap; disposes it if the copy fails.</summary>
    public static WriteableBitmap ToBitmap(IconPixels pixels)
    {
        WriteableBitmap bitmap = new(
            new PixelSize(pixels.Width, pixels.Height),
            new Vector(x: 96, y: 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        try
        {
            using ILockedFramebuffer framebuffer = bitmap.Lock();
            int sourceStride = pixels.Width * 4;
            for (int rowIndex = 0; rowIndex < pixels.Height; rowIndex++)
            {
                Marshal.Copy(
                    pixels.Bgra,
                    rowIndex * sourceStride,
                    IntPtr.Add(framebuffer.Address, rowIndex * framebuffer.RowBytes),
                    Math.Min(sourceStride, framebuffer.RowBytes));
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
