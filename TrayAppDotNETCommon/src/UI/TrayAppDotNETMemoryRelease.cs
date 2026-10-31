using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.OpenGL.Egl;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Gives back the memory a closed window leaves in an idle tray app. Its visual tree stays on the GC heap until the
/// app allocates enough to trigger a full collection, and the graphics driver keeps caching the Direct3D allocations
/// its frames rendered with until the device renders again. Brightness kept about 110 MB of the two after its
/// settings window closed.
/// </summary>
public static class TrayAppDotNETMemoryRelease
{
    // IDXGIDevice3::Trim follows IUnknown (3), IDXGIObject (4), IDXGIDevice (5), IDXGIDevice1 (2) and IDXGIDevice2 (3)
    private const int DXGIDevice3TrimSlot = 17;
    private static readonly Guid IDXGIDevice3Guid = new("6007896c-3244-4afd-bf18-a6d3beda5023");

    private static WeakReference<EglDisplay>? _renderDisplay;
    private static int _releaseScheduled;

    /// <summary>
    /// Records the EGL display the next frame of <paramref name="visual"/> renders with, so a release can reach the
    /// Direct3D device behind it. Software rendering has none and records nothing.
    /// </summary>
    public static void TrackRenderDevice(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (ElementComposition.GetElementVisual(visual) is not { } hostVisual) return;

        CompositionCustomVisual probe = hostVisual.Compositor.CreateCustomVisual(new RenderDeviceProbe());
        probe.Size = new Vector(1, 1);
        ElementComposition.SetElementChildVisual(visual, probe);
    }

    /// <summary>
    /// Compacts the GC heap and trims the render device once the render thread has disposed the closed window's render
    /// target. Call on the UI thread after the window closed; closes that arrive while a release runs share it.
    /// </summary>
    public static void ReleaseAfterWindowClose()
    {
        if (Interlocked.Exchange(ref _releaseScheduled, value: 1) != 0) return;

        ReleaseAsync().ForgetLogged(nameof(TrayAppDotNETMemoryRelease));
    }

    private static async Task ReleaseAsync()
    {
        try
        {
            // A closed window's render target is disposed with the next composition batch the render thread processes
            if (Compositor.TryGetDefaultCompositor() is { } compositor)
                await compositor.RequestCommitAsync().ConfigureAwait(false);

            await Task.Run(static () =>
            {
                ManagedHeap.CompactAndDecommit();
                TrimRenderDevice();
            }).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _releaseScheduled, value: 0);
        }
    }

    /// <summary>Asks the Direct3D runtime and the driver to drop the allocations they cache for reuse.</summary>
    private static unsafe void TrimRenderDevice()
    {
        if (Volatile.Read(ref _renderDisplay) is not { } displayReference ||
            !displayReference.TryGetTarget(out EglDisplay? display))
            return;

        EglInterface egl = display.EglInterface;
        if (!egl.IsQueryDisplayAttribExtAvailable || !egl.IsQueryDeviceAttribExtAvailable) return;

        // ANGLE's contexts hold the display lock while current, so the render thread leaves the device alone meanwhile
        using (display.Lock())
        {
            if (display.IsLost) return;
            if (!egl.QueryDisplayAttribExt(display.Handle, EglConsts.EGL_DEVICE_EXT, out IntPtr eglDevice) ||
                !egl.QueryDeviceAttribExt(eglDevice, EglConsts.EGL_D3D11_DEVICE_ANGLE, out IntPtr d3dDevice) ||
                d3dDevice == IntPtr.Zero)
                return;

            if (Marshal.QueryInterface(d3dDevice, in IDXGIDevice3Guid, out IntPtr dxgiDevice) != 0) return;

            try
            {
                void** vtable = *(void***)dxgiDevice;
                ((delegate* unmanaged[Stdcall]<IntPtr, void>)vtable[DXGIDevice3TrimSlot])(dxgiDevice);
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
    }

    /// <summary>Records the EGL display of the first frame it is drawn in; draws nothing.</summary>
    private sealed class RenderDeviceProbe : CompositionCustomVisualHandler
    {
        private bool _recorded;

        public override void OnRender(ImmediateDrawingContext drawingContext)
        {
            if (_recorded) return;

            _recorded = true;
            if (drawingContext.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature
                leaseFeature)
                return;

            using ISkiaSharpApiLease lease = leaseFeature.Lease();
            using ISkiaSharpPlatformGraphicsApiLease? graphicsLease = lease.TryLeasePlatformGraphicsApi();
            if (graphicsLease?.Context is EglContext context)
                Volatile.Write(ref _renderDisplay, new WeakReference<EglDisplay>(context.Display));
        }
    }
}
