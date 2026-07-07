using System.Runtime.InteropServices;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.Utils;

/// <summary>
/// Null-safe one-liners that replace the inline "try { x.Dispose(); } catch { }" /
/// "try { Marshal.FinalReleaseComObject(x); } catch { }" idiom sprinkled across the codebase.
/// Both helpers swallow every exception silently:
/// disposal and COM release sit on shutdown / RCW-teardown paths
/// where a failure is never something a caller can usefully act on.
/// </summary>
public static class Safe
{
    /// <summary>
    /// Dispose the supplied disposable when non-null. Swallows any exception the Dispose call raises.
    /// </summary>
    public static void Dispose(IDisposable? obj)
    {
        if (obj == null) return;
        try { obj.Dispose(); }
        catch
        {
            // Best-effort - dispose is on shutdown / teardown paths.
        }
    }

    /// <summary>
    /// Release the supplied COM wrapper when non-null.
    /// Source-generated ComWrappers objects use the registered strategy release path;
    /// built-in RCWs fall back to Marshal.FinalReleaseComObject.
    /// </summary>
    public static void Release(object? rcw)
    {
        if (rcw == null) return;
        try
        {
            if (COMActivation.TryReleaseGeneratedComObject(rcw)) return;
            if (!Marshal.IsComObject(rcw)) return;
            Marshal.FinalReleaseComObject(rcw);
        }
        catch
        {
            // Best-effort - already-released RCW or apartment teardown can throw here.
        }
    }
}
