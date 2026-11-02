using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Interop.Wmi;

/// <summary>What one IEnumWbemClassObject.Next call produced.</summary>
public enum WmiEnumerationStep
{
    Object,
    End,
    TimedOut,
    Failed
}

/// <summary>
/// One connected WMI namespace. Waits are always bounded: the connect uses WBEM_FLAG_CONNECT_USE_MAX_WAIT (at most two
/// minutes), and <see cref="Query"/> polls IEnumWbemClassObject.Next with a finite step and gives up at an overall
/// deadline, so a stalled WMI service never hangs the calling thread.
/// </summary>
/// <remarks>
/// The call-level, impersonating proxy blanket is applied to the services proxy and to every enumerator proxy, so the
/// apps need no process-wide CoInitializeSecurity (which would otherwise depend on which thread touched COM first).
/// The caller owns the COM apartment: hold a <see cref="ComApartmentScope"/> (multithreaded) for the connection's
/// lifetime.
/// </remarks>
public sealed class WmiConnection : IDisposable
{
    /// <summary>The longest single IEnumWbemClassObject.Next wait.</summary>
    public const int DefaultNextTimeoutMilliseconds = 1_000;

    /// <summary>The longest a whole query may take before it fails with a <see cref="TimeoutException"/>.</summary>
    public static readonly TimeSpan DefaultQueryTimeout = TimeSpan.FromSeconds(10);

    private IWbemServices? _services;

    private WmiConnection(IWbemServices services) => _services = services;

    public IWbemServices Services => _services ?? throw new ObjectDisposedException(nameof(WmiConnection));

    /// <summary>Connects to a local namespace such as <c>ROOT\CIMV2</c>.</summary>
    public static bool TryConnect(
        string wmiNamespace,
        [NotNullWhen(true)] out WmiConnection? connection,
        out string? error)
    {
        connection = null;
        error = null;
        IWbemLocator? locator = null;
        try
        {
            locator = COMActivation.CreateInstance<IWbemLocator>(
                WmiNative.ClsidWbemLocator,
                typeof(IWbemLocator).GUID);
            int hr = locator.ConnectServer(
                wmiNamespace,
                strUser: null,
                strPassword: null,
                strLocale: null,
                lSecurityFlags: WmiNative.WbemFlagConnectUseMaxWait,
                strAuthority: null,
                IntPtr.Zero,
                out IntPtr servicesPointer);
            if (hr < 0 || servicesPointer == IntPtr.Zero)
            {
                error = $"IWbemLocator.ConnectServer('{wmiNamespace}') failed ({WmiNative.FormatHResult(hr)}).";
                return false;
            }

            hr = WmiNative.SetWmiProxyBlanket(servicesPointer);
            if (hr < 0)
            {
                _ = Marshal.Release(servicesPointer);
                error = $"CoSetProxyBlanket(IWbemServices) failed ({WmiNative.FormatHResult(hr)}).";
                return false;
            }

            connection = new WmiConnection(COMActivation.GetObjectForComInstance<IWbemServices>(
                servicesPointer,
                releaseInputReference: true));
            return true;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            error = exception.Message;
            return false;
        }
        finally
        {
            Safe.Release(locator);
        }
    }

    /// <summary>
    /// Runs a WQL query and hands each object to <paramref name="handleObject"/>; the object is released after the
    /// callback returns. Throws <see cref="TimeoutException"/> when the whole enumeration exceeds
    /// <paramref name="queryTimeout"/> and <see cref="InvalidOperationException"/> on a WMI failure.
    /// </summary>
    public void Query(
        string wql,
        Action<IWbemClassObject> handleObject,
        TimeSpan? queryTimeout = null,
        int nextTimeoutMilliseconds = DefaultNextTimeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(handleObject);
        int hr = Services.ExecQuery(
            strQueryLanguage: "WQL",
            wql,
            WmiNative.WbemQueryFlags,
            IntPtr.Zero,
            out IntPtr enumeratorPointer);
        if (hr < 0 || enumeratorPointer == IntPtr.Zero)
            throw new InvalidOperationException($"IWbemServices.ExecQuery failed ({WmiNative.FormatHResult(hr)}): {wql}");

        // The enumerator is a separate proxy and does not inherit the services blanket
        hr = WmiNative.SetWmiProxyBlanket(enumeratorPointer);
        if (hr < 0)
        {
            _ = Marshal.Release(enumeratorPointer);
            throw new InvalidOperationException($"CoSetProxyBlanket(IEnumWbemClassObject) failed ({WmiNative.FormatHResult(hr)}).");
        }

        IEnumWbemClassObject? enumerator = null;
        try
        {
            enumerator = COMActivation.GetObjectForComInstance<IEnumWbemClassObject>(
                enumeratorPointer,
                releaseInputReference: true);
            long deadline = Stopwatch.GetTimestamp()
                            + (long)((queryTimeout ?? DefaultQueryTimeout).TotalSeconds * Stopwatch.Frequency);
            while (true)
            {
                hr = enumerator.Next(nextTimeoutMilliseconds, uCount: 1, out IntPtr objectPointer, out uint returned);
                switch (ClassifyNext(hr, returned, objectPointer))
                {
                    case WmiEnumerationStep.End:
                        return;
                    case WmiEnumerationStep.Failed:
                        throw new InvalidOperationException($"IEnumWbemClassObject.Next failed ({WmiNative.FormatHResult(hr)}).");
                    case WmiEnumerationStep.TimedOut:
                        if (Stopwatch.GetTimestamp() >= deadline)
                            throw new TimeoutException($"The WMI query did not complete in time: {wql}");
                        continue;
                }

                IWbemClassObject? item = null;
                try
                {
                    item = COMActivation.GetObjectForComInstance<IWbemClassObject>(
                        objectPointer,
                        releaseInputReference: true);
                    handleObject(item);
                }
                finally
                {
                    Safe.Release(item);
                }

                if (Stopwatch.GetTimestamp() >= deadline)
                    throw new TimeoutException($"The WMI query did not complete in time: {wql}");
            }
        }
        finally
        {
            Safe.Release(enumerator);
        }
    }

    /// <summary>Classifies one IEnumWbemClassObject.Next result.</summary>
    public static WmiEnumerationStep ClassifyNext(int hr, uint returned, IntPtr objectPointer)
    {
        if (hr < 0) return WmiEnumerationStep.Failed;
        if (returned > 0 && objectPointer != IntPtr.Zero) return WmiEnumerationStep.Object;
        return hr == WmiNative.WbemSTimedOut ? WmiEnumerationStep.TimedOut : WmiEnumerationStep.End;
    }

    /// <summary>Reads a non-array string property.</summary>
    public static bool TryGetString(IWbemClassObject item, string name, out string value)
    {
        // Explicit parameter types keep T non-nullable; without them value = text raises CS8601
        // ReSharper disable once RedundantLambdaParameterType
        if (TryGet(item, name, static (in WmiVariant v, out string s) => v.TryGetString(out s), out string? text))
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Reads a non-negative integer property (any integer width, or a numeric string).</summary>
    public static bool TryGetUInt64(IWbemClassObject item, string name, out ulong value) =>
        TryGet(item, name, static (in v, out u) => v.TryGetUInt64(out u), out value);

    /// <summary>Reads a non-negative integer property that fits 32 bits.</summary>
    public static bool TryGetUInt32(IWbemClassObject item, string name, out uint value) =>
        TryGet(item, name, static (in v, out u) => v.TryGetUInt32(out u), out value);

    /// <summary>Writes a VT_I4 property (WMI's wire type for CIM uint32 parameters).</summary>
    public static unsafe bool TryPutInt32(IWbemClassObject item, string name, int value, out string? error)
    {
        WmiVariant variant = default;
        variant.Vt = WmiVariant.VtI4;
        variant.LVal = value;
        int hr = item.Put(name, lFlags: 0, (IntPtr)(&variant), type: 0);
        error = hr >= 0 ? null : $"IWbemClassObject.Put('{name}') failed ({WmiNative.FormatHResult(hr)}).";
        return hr >= 0;
    }

    /// <summary>Writes a VT_UI1 property.</summary>
    public static unsafe bool TryPutUInt8(IWbemClassObject item, string name, byte value, out string? error)
    {
        WmiVariant variant = default;
        variant.Vt = WmiVariant.VtUi1;
        variant.BVal = value;
        int hr = item.Put(name, lFlags: 0, (IntPtr)(&variant), type: 0);
        error = hr >= 0 ? null : $"IWbemClassObject.Put('{name}') failed ({WmiNative.FormatHResult(hr)}).";
        return hr >= 0;
    }

    public void Dispose()
    {
        IWbemServices? services = _services;
        _services = null;
        Safe.Release(services);
    }

    private delegate bool VariantReader<T>(in WmiVariant variant, out T value);

    private static unsafe bool TryGet<T>(
        IWbemClassObject item,
        string name,
        VariantReader<T> read,
        [MaybeNullWhen(false)] out T value)
    {
        WmiVariant variant = default;
        int hr = item.Get(name, lFlags: 0, (IntPtr)(&variant), IntPtr.Zero, IntPtr.Zero);
        try
        {
            if (hr >= 0 && read(in variant, out value)) return true;
            value = default;
            return false;
        }
        finally
        {
            _ = OleAut32.VariantClear((IntPtr)(&variant));
        }
    }
}
