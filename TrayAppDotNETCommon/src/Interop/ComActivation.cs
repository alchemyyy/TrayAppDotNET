using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace TrayAppDotNETCommon.Interop;

/// <summary>
/// AOT-safe activation and wrapping for source-generated COM interfaces. Every wrapper made here is a unique
/// instance, so <see cref="TryReleaseGeneratedComObject"/> releases it deterministically.
/// </summary>
public static class COMActivation
{
    private static readonly StrategyBasedComWrappers ComWrappers = new();
    private static int _registeredForMarshalling;

    public static T CreateInstance<T>(Guid clsid, Guid iid, uint clsContext = Ole32.CLSCTX_ALL)
        where T : class
    {
        EnsureRegisteredForMarshalling();

        int hr = Ole32.CoCreateInstance(in clsid, IntPtr.Zero, clsContext, in iid, out IntPtr instance);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);

        return GetObjectForComInstance<T>(instance, releaseInputReference: true);
    }

    public static T GetObjectForComInstance<T>(IntPtr instance, bool releaseInputReference = false)
        where T : class
    {
        EnsureRegisteredForMarshalling();

        unsafe
        {
            void* unmanaged = (void*)instance;
            T? managed;
            try
            {
                managed = UniqueComInterfaceMarshaller<T>.ConvertToManaged(unmanaged);
            }
            finally
            {
                if (releaseInputReference) UniqueComInterfaceMarshaller<T>.Free(unmanaged);
            }

            return managed ??
                   throw new InvalidOperationException($"Failed to marshal COM interface {typeof(T).FullName}.");
        }
    }

    /// <summary>
    /// Registers a <see cref="StrategyBasedComWrappers"/> as the process-wide marshalling instance, so runtime paths
    /// that still go through the built-in marshaller (Marshal.GetObjectForIUnknown and friends) produce
    /// source-generated wrappers. The generated stubs and the marshallers used here never depend on it.
    /// </summary>
    public static void EnsureRegisteredForMarshalling()
    {
        if (Interlocked.Exchange(ref _registeredForMarshalling, value: 1) != 0) return;

        try
        {
            System.Runtime.InteropServices.ComWrappers.RegisterForMarshalling(ComWrappers);
        }
        catch (InvalidOperationException)
        {
            // Another component already registered a process-wide instance; those runtime paths use it instead
        }
    }

    /// <summary>
    /// Releases a source-generated wrapper now through <see cref="ComObject.FinalRelease"/> and returns true;
    /// returns false for anything that is not a source-generated wrapper.
    /// </summary>
    /// <remarks>
    /// <see cref="ComObject.FinalRelease"/> only acts on unique instances: a cached wrapper (the default marshaller
    /// of a generated out-parameter or return value) is left for the finalizer, and the runtime exposes no way to
    /// tell the two apart. So every generated interface member that hands out a COM object must marshal it with
    /// <c>[MarshalUsing(typeof(UniqueComInterfaceMarshaller&lt;T&gt;))]</c>, and other wrappers must come from
    /// <see cref="CreateInstance{T}"/> or <see cref="GetObjectForComInstance{T}"/>.
    /// </remarks>
    public static bool TryReleaseGeneratedComObject(object? obj)
    {
        if (obj is not ComObject comObject) return false;

        comObject.FinalRelease();
        return true;
    }
}
