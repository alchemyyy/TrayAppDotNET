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
    /// Release the supplied COM wrapper when non-null. Source-generated wrappers go through
    /// <see cref="COMActivation.TryReleaseGeneratedComObject"/>, which only releases unique instances (see its
    /// remarks); built-in RCWs fall back to Marshal.FinalReleaseComObject.
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

    /// <summary>
    /// Shutdown wait for a background task the caller has already asked to stop (cancelled). Waits up to
    /// <paramref name="timeoutMs"/>, logs a timeout and every failure except cancellation under
    /// <paramref name="owner"/>, and never throws. Returns true when the task finished in time (or was null).
    /// </summary>
    public static bool WaitForStop(Task? task, int timeoutMs, string owner)
    {
        if (task == null) return true;

        try
        {
            if (task.Wait(timeoutMs)) return true;

            TADNLog.Log($"{owner} did not stop within {timeoutMs} ms");
            return false;
        }
        catch (AggregateException exception)
        {
            foreach (Exception inner in exception.Flatten().InnerExceptions)
            {
                if (inner is OperationCanceledException) continue;

                TADNLog.Log($"{owner} stopped with {inner}");
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
