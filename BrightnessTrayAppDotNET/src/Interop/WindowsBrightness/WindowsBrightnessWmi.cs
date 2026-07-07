using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Utils;

namespace BrightnessTrayAppDotNET.Interop.WindowsBrightness;

internal sealed record WindowsBrightnessTarget(
    string InstanceName,
    string DisplayInstancePath,
    string MethodPath,
    int CurrentBrightness);

/// <summary>
/// Native-AOT friendly WMI client for Windows' built-in display brightness provider.
/// Avoids System.Management so release publishing does not pull the reflection-heavy WMI assembly graph.
/// </summary>
internal static class WindowsBrightnessWmi
{
    private const string WmiNamespace = @"ROOT\WMI";
    private const int WbemInfinite = -1;
    private const int WbemFlagReturnImmediately = 0x10;
    private const int WbemFlagForwardOnly = 0x20;
    private const int WbemQueryFlags = WbemFlagReturnImmediately | WbemFlagForwardOnly;

    public static bool TryGetActiveTargets(out IReadOnlyList<WindowsBrightnessTarget> targets, out string? error)
    {
        targets = [];
        error = null;

        try
        {
            using ComApartmentScope _ = ComApartmentScope.Enter();
            IWbemServices? services = null;
            try
            {
                if (!TryConnect(out services, out error)) return false;

                Dictionary<string, string> methodPathByInstance = QueryBrightnessMethodPaths(services);
                List<WindowsBrightnessTarget> found = [];

                Query(
                    services,
                    "SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE",
                    obj =>
                    {
                        if (!TryGetString(obj, "InstanceName", out string instanceName)
                            || string.IsNullOrWhiteSpace(instanceName))
                            return;

                        if (!TryGetUInt(obj, "CurrentBrightness", out uint currentRaw))
                            return;

                        if (!methodPathByInstance.TryGetValue(instanceName, out string? methodPath)
                            || string.IsNullOrWhiteSpace(methodPath))
                            return;

                        found.Add(new WindowsBrightnessTarget(
                            instanceName,
                            NormalizeDisplayInstancePath(instanceName),
                            methodPath,
                            (int)Math.Clamp(currentRaw, 0, 100)));
                    });

                targets = found;
                return true;
            }
            finally
            {
                Safe.Release(services);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryGetBrightness(string instanceName, out int brightness, out string? error)
    {
        brightness = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(instanceName))
        {
            error = "Windows brightness instance name is empty.";
            return false;
        }

        if (!TryGetActiveTargets(out IReadOnlyList<WindowsBrightnessTarget> targets, out error))
            return false;

        WindowsBrightnessTarget? target = targets.FirstOrDefault(t =>
            string.Equals(t.InstanceName, instanceName, StringComparison.Ordinal));
        if (target == null)
        {
            error = $"Windows brightness instance '{instanceName}' is not active.";
            return false;
        }

        brightness = target.CurrentBrightness;
        return true;
    }

    public static bool TrySetBrightness(string methodPath, int brightness, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(methodPath))
        {
            error = "Windows brightness method path is empty.";
            return false;
        }

        int clamped = Math.Clamp(brightness, 0, 100);

        try
        {
            using ComApartmentScope _ = ComApartmentScope.Enter();
            IWbemServices? services = null;
            IWbemClassObject? inParams = null;
            IWbemClassObject? outParams = null;
            try
            {
                if (!TryConnect(out services, out error)) return false;
                if (!TryBuildSetBrightnessParameters(services, clamped, out inParams, out error))
                    return false;

                int hr = services.ExecMethod(
                    methodPath,
                    "WmiSetBrightness",
                    0,
                    IntPtr.Zero,
                    inParams,
                    out IntPtr outParamsPtr,
                    IntPtr.Zero);
                if (hr < 0)
                {
                    error = $"IWbemServices.ExecMethod(WmiSetBrightness) failed ({FormatHr(hr)}).";
                    return false;
                }

                if (outParamsPtr == IntPtr.Zero) return true;

                outParams = COMActivation.GetObjectForComInstance<IWbemClassObject>(
                    outParamsPtr,
                    releaseInputReference: true);
                if (!TryGetUInt(outParams, "ReturnValue", out uint returnValue))
                    return true;

                if (returnValue == 0) return true;

                error = string.Format(
                    CultureInfo.InvariantCulture,
                    "WmiSetBrightness returned error {0}.",
                    returnValue);
                return false;
            }
            finally
            {
                Safe.Release(outParams);
                Safe.Release(inParams);
                Safe.Release(services);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string NormalizeDisplayInstancePath(string instanceName)
    {
        string trimmed = instanceName.Trim();
        if (trimmed.Length == 0) return string.Empty;

        int slash = trimmed.LastIndexOf('\\');
        if (slash < 0 || slash == trimmed.Length - 1) return trimmed;

        string prefix = trimmed[..(slash + 1)];
        string last = trimmed[(slash + 1)..];
        int underscore = last.LastIndexOf('_');
        if (underscore > 0 && underscore < last.Length - 1)
        {
            bool suffixIsNumeric = true;
            for (int i = underscore + 1; i < last.Length; i++)
            {
                if (char.IsDigit(last[i])) continue;
                suffixIsNumeric = false;
                break;
            }

            if (suffixIsNumeric) last = last[..underscore];
        }

        return prefix + last;
    }

    private static bool TryConnect([NotNullWhen(true)] out IWbemServices? services, out string? error)
    {
        services = null;
        error = null;

        IWbemLocator? locator = null;
        try
        {
            locator = COMActivation.CreateInstance<IWbemLocator>(
                WmiNative.ClsidWbemLocator,
                typeof(IWbemLocator).GUID);

            int hr = locator.ConnectServer(
                WmiNamespace,
                null,
                null,
                null,
                0,
                null,
                IntPtr.Zero,
                out IntPtr servicesPtr);
            if (hr < 0 || servicesPtr == IntPtr.Zero)
            {
                error = $"IWbemLocator.ConnectServer('{WmiNamespace}') failed ({FormatHr(hr)}).";
                return false;
            }

            hr = WmiNative.CoSetProxyBlanket(
                servicesPtr,
                WmiNative.RpcCAutnWinnt,
                WmiNative.RpcCAuthzNone,
                IntPtr.Zero,
                WmiNative.RpcCAuthnLevelCall,
                WmiNative.RpcCImpLevelImpersonate,
                IntPtr.Zero,
                WmiNative.EoacNone);
            if (hr < 0)
            {
                Marshal.Release(servicesPtr);
                error = $"CoSetProxyBlanket(IWbemServices) failed ({FormatHr(hr)}).";
                return false;
            }

            services = COMActivation.GetObjectForComInstance<IWbemServices>(
                servicesPtr,
                releaseInputReference: true);
            return true;
        }
        finally
        {
            Safe.Release(locator);
        }
    }

    private static Dictionary<string, string> QueryBrightnessMethodPaths(IWbemServices services)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        Query(
            services,
            "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE",
            obj =>
            {
                if (!TryGetString(obj, "InstanceName", out string instanceName)
                    || string.IsNullOrWhiteSpace(instanceName))
                    return;

                if (!TryGetString(obj, "__PATH", out string path)
                    || string.IsNullOrWhiteSpace(path))
                    return;

                result[instanceName] = path;
            });

        return result;
    }

    private static void Query(IWbemServices services, string wql, Action<IWbemClassObject> handleObject)
    {
        int hr = services.ExecQuery("WQL", wql, WbemQueryFlags, IntPtr.Zero, out IntPtr enumPtr);
        if (hr < 0 || enumPtr == IntPtr.Zero)
            throw new InvalidOperationException($"IWbemServices.ExecQuery failed ({FormatHr(hr)}): {wql}");

        IEnumWbemClassObject? enumerator = null;
        try
        {
            enumerator = COMActivation.GetObjectForComInstance<IEnumWbemClassObject>(
                enumPtr,
                releaseInputReference: true);

            while (true)
            {
                hr = enumerator.Next(WbemInfinite, 1, out IntPtr objPtr, out uint returned);
                if (hr < 0)
                    throw new InvalidOperationException($"IEnumWbemClassObject.Next failed ({FormatHr(hr)}).");

                if (returned == 0 || objPtr == IntPtr.Zero) break;

                IWbemClassObject? obj = null;
                try
                {
                    obj = COMActivation.GetObjectForComInstance<IWbemClassObject>(
                        objPtr,
                        releaseInputReference: true);
                    handleObject(obj);
                }
                finally
                {
                    Safe.Release(obj);
                }
            }
        }
        finally
        {
            Safe.Release(enumerator);
        }
    }

    private static bool TryBuildSetBrightnessParameters(
        IWbemServices services,
        int brightness,
        out IWbemClassObject? inParams,
        out string? error)
    {
        inParams = null;
        error = null;

        int hr = services.GetObject(
            "WmiMonitorBrightnessMethods",
            0,
            IntPtr.Zero,
            out IntPtr classObjectPtr,
            IntPtr.Zero);
        if (hr < 0 || classObjectPtr == IntPtr.Zero)
        {
            error = $"IWbemServices.GetObject(WmiMonitorBrightnessMethods) failed ({FormatHr(hr)}).";
            return false;
        }

        IWbemClassObject? classObject = null;
        IWbemClassObject? inSignature = null;
        IWbemClassObject? parameters = null;
        try
        {
            classObject = COMActivation.GetObjectForComInstance<IWbemClassObject>(
                classObjectPtr,
                releaseInputReference: true);

            hr = classObject.GetMethod("WmiSetBrightness", 0, out IntPtr inSignaturePtr, out IntPtr outSignaturePtr);
            if (outSignaturePtr != IntPtr.Zero) Marshal.Release(outSignaturePtr);
            if (hr < 0 || inSignaturePtr == IntPtr.Zero)
            {
                if (inSignaturePtr != IntPtr.Zero) Marshal.Release(inSignaturePtr);
                error = $"IWbemClassObject.GetMethod(WmiSetBrightness) failed ({FormatHr(hr)}).";
                return false;
            }

            inSignature = COMActivation.GetObjectForComInstance<IWbemClassObject>(
                inSignaturePtr,
                releaseInputReference: true);

            hr = inSignature.SpawnInstance(0, out IntPtr inParamsPtr);
            if (hr < 0 || inParamsPtr == IntPtr.Zero)
            {
                error = $"IWbemClassObject.SpawnInstance(WmiSetBrightness input) failed ({FormatHr(hr)}).";
                return false;
            }

            parameters = COMActivation.GetObjectForComInstance<IWbemClassObject>(
                inParamsPtr,
                releaseInputReference: true);

            if (!TryPutUInt32(parameters, "Timeout", 0, out error)) return false;
            if (!TryPutUInt8(parameters, "Brightness", (byte)brightness, out error)) return false;

            inParams = parameters;
            parameters = null;
            return true;
        }
        finally
        {
            Safe.Release(parameters);
            Safe.Release(inSignature);
            Safe.Release(classObject);
        }
    }

    private static unsafe bool TryGetString(IWbemClassObject obj, string name, out string value)
    {
        value = string.Empty;
        WmiVariant variant = default;
        int hr = obj.Get(name, 0, (IntPtr)(&variant), IntPtr.Zero, IntPtr.Zero);
        try
        {
            if (hr < 0) return false;
            if ((variant.Vt & WmiVariant.VtArray) != 0) return false;
            if ((variant.Vt & WmiVariant.VtTypeMask) != WmiVariant.VtBStr || variant.BstrVal == IntPtr.Zero)
                return false;

            value = Marshal.PtrToStringBSTR(variant.BstrVal) ?? string.Empty;
            return true;
        }
        finally
        {
            _ = WmiNative.VariantClear((IntPtr)(&variant));
        }
    }

    private static unsafe bool TryGetUInt(IWbemClassObject obj, string name, out uint value)
    {
        value = 0;
        WmiVariant variant = default;
        int hr = obj.Get(name, 0, (IntPtr)(&variant), IntPtr.Zero, IntPtr.Zero);
        try
        {
            if (hr < 0) return false;
            ushort vt = (ushort)(variant.Vt & WmiVariant.VtTypeMask);
            switch (vt)
            {
                case WmiVariant.VtUi1:
                    value = variant.BVal;
                    return true;
                case WmiVariant.VtUi2:
                    value = variant.UiVal;
                    return true;
                case WmiVariant.VtUi4:
                case WmiVariant.VtUint:
                    value = variant.UlVal;
                    return true;
                case WmiVariant.VtI1:
                    if (variant.CVal < 0) return false;
                    value = (uint)variant.CVal;
                    return true;
                case WmiVariant.VtI2:
                    if (variant.IVal < 0) return false;
                    value = (uint)variant.IVal;
                    return true;
                case WmiVariant.VtI4:
                case WmiVariant.VtInt:
                    if (variant.LVal < 0) return false;
                    value = (uint)variant.LVal;
                    return true;
                default:
                    return false;
            }
        }
        finally
        {
            _ = WmiNative.VariantClear((IntPtr)(&variant));
        }
    }

    private static unsafe bool TryPutUInt32(IWbemClassObject obj, string name, uint value, out string? error)
    {
        error = null;
        WmiVariant variant = default;
        if (value > int.MaxValue)
        {
            error = $"IWbemClassObject.Put('{name}') value is outside Int32 range.";
            return false;
        }

        variant.Vt = WmiVariant.VtI4;
        variant.LVal = (int)value;
        int hr = obj.Put(name, 0, (IntPtr)(&variant), 0);
        if (hr >= 0) return true;

        error = $"IWbemClassObject.Put('{name}') failed ({FormatHr(hr)}).";
        return false;
    }

    private static unsafe bool TryPutUInt8(IWbemClassObject obj, string name, byte value, out string? error)
    {
        error = null;
        WmiVariant variant = default;
        variant.Vt = WmiVariant.VtUi1;
        variant.BVal = value;
        int hr = obj.Put(name, 0, (IntPtr)(&variant), 0);
        if (hr >= 0) return true;

        error = $"IWbemClassObject.Put('{name}') failed ({FormatHr(hr)}).";
        return false;
    }

    private static string FormatHr(int hr) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{unchecked((uint)hr):X8}");

    private sealed class ComApartmentScope : IDisposable
    {
        private readonly bool _uninitialize;
        private static int _securityInitialized;

        private ComApartmentScope(bool uninitialize) => _uninitialize = uninitialize;

        public static ComApartmentScope Enter()
        {
            int hr = WmiNative.CoInitializeEx(IntPtr.Zero, WmiNative.CoinitMultithreaded);
            bool uninitialize = hr is WmiNative.SOk or WmiNative.SFalse;
            if (hr < 0 && hr != WmiNative.RpcEChangedMode) Marshal.ThrowExceptionForHR(hr);

            if (Interlocked.Exchange(ref _securityInitialized, 1) == 0)
            {
                int securityHr = WmiNative.CoInitializeSecurity(
                    IntPtr.Zero,
                    -1,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    WmiNative.RpcCAuthnLevelDefault,
                    WmiNative.RpcCImpLevelImpersonate,
                    IntPtr.Zero,
                    WmiNative.EoacNone,
                    IntPtr.Zero);
                if (securityHr < 0 && securityHr != WmiNative.RpcETooLate)
                    Marshal.ThrowExceptionForHR(securityHr);
            }

            return new ComApartmentScope(uninitialize);
        }

        public void Dispose()
        {
            if (_uninitialize) WmiNative.CoUninitialize();
        }
    }
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct WmiVariant
{
    public const ushort VtTypeMask = 0x0FFF;
    public const ushort VtArray = 0x2000;
    public const ushort VtI2 = 2;
    public const ushort VtI4 = 3;
    public const ushort VtBStr = 8;
    public const ushort VtI1 = 16;
    public const ushort VtUi1 = 17;
    public const ushort VtUi2 = 18;
    public const ushort VtUi4 = 19;
    public const ushort VtInt = 22;
    public const ushort VtUint = 23;

    [FieldOffset(0)] public ushort Vt;
    [FieldOffset(8)] public sbyte CVal;
    [FieldOffset(8)] public byte BVal;
    [FieldOffset(8)] public short IVal;
    [FieldOffset(8)] public ushort UiVal;
    [FieldOffset(8)] public int LVal;
    [FieldOffset(8)] public uint UlVal;
    [FieldOffset(8)] public IntPtr BstrVal;
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("dc12a687-737f-11cf-884d-00aa004b2e24")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWbemLocator
{
    [PreserveSig]
    int ConnectServer(
        [MarshalAs(UnmanagedType.BStr)] string strNetworkResource,
        [MarshalAs(UnmanagedType.BStr)] string? strUser,
        [MarshalAs(UnmanagedType.BStr)] string? strPassword,
        [MarshalAs(UnmanagedType.BStr)] string? strLocale,
        int lSecurityFlags,
        [MarshalAs(UnmanagedType.BStr)] string? strAuthority,
        IntPtr pCtx,
        out IntPtr ppNamespace);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("9556dc99-828c-11cf-a37e-00aa003240c7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWbemServices
{
    void Unused_OpenNamespace();
    void Unused_CancelAsyncCall();
    void Unused_QueryObjectSink();

    [PreserveSig]
    int GetObject(
        [MarshalAs(UnmanagedType.BStr)] string strObjectPath,
        int lFlags,
        IntPtr pCtx,
        out IntPtr ppObject,
        IntPtr ppCallResult);

    void Unused_GetObjectAsync();
    void Unused_PutClass();
    void Unused_PutClassAsync();
    void Unused_DeleteClass();
    void Unused_DeleteClassAsync();
    void Unused_CreateClassEnum();
    void Unused_CreateClassEnumAsync();
    void Unused_PutInstance();
    void Unused_PutInstanceAsync();
    void Unused_DeleteInstance();
    void Unused_DeleteInstanceAsync();
    void Unused_CreateInstanceEnum();
    void Unused_CreateInstanceEnumAsync();

    [PreserveSig]
    int ExecQuery(
        [MarshalAs(UnmanagedType.BStr)] string strQueryLanguage,
        [MarshalAs(UnmanagedType.BStr)] string strQuery,
        int lFlags,
        IntPtr pCtx,
        out IntPtr ppEnum);

    void Unused_ExecQueryAsync();
    void Unused_ExecNotificationQuery();
    void Unused_ExecNotificationQueryAsync();

    [PreserveSig]
    int ExecMethod(
        [MarshalAs(UnmanagedType.BStr)] string strObjectPath,
        [MarshalAs(UnmanagedType.BStr)] string strMethodName,
        int lFlags,
        IntPtr pCtx,
        IWbemClassObject? pInParams,
        out IntPtr ppOutParams,
        IntPtr ppCallResult);

    void Unused_ExecMethodAsync();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("dc12a681-737f-11cf-884d-00aa004b2e24")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IWbemClassObject
{
    void Unused_GetQualifierSet();

    [PreserveSig]
    int Get(
        [MarshalAs(UnmanagedType.BStr)] string wszName,
        int lFlags,
        IntPtr pVal,
        IntPtr pType,
        IntPtr plFlavor);

    [PreserveSig]
    int Put(
        [MarshalAs(UnmanagedType.BStr)] string wszName,
        int lFlags,
        IntPtr pVal,
        int type);

    void Unused_Delete();
    void Unused_GetNames();
    void Unused_BeginEnumeration();
    void Unused_Next();
    void Unused_EndEnumeration();
    void Unused_GetPropertyQualifierSet();
    void Unused_Clone();
    void Unused_GetObjectText();
    void Unused_SpawnDerivedClass();

    [PreserveSig]
    int SpawnInstance(int lFlags, out IntPtr ppNewInstance);

    void Unused_CompareTo();
    void Unused_GetPropertyOrigin();
    void Unused_InheritsFrom();

    [PreserveSig]
    int GetMethod(
        [MarshalAs(UnmanagedType.BStr)] string wszName,
        int lFlags,
        out IntPtr ppInSignature,
        out IntPtr ppOutSignature);

    void Unused_PutMethod();
    void Unused_DeleteMethod();
    void Unused_BeginMethodEnumeration();
    void Unused_NextMethod();
    void Unused_EndMethodEnumeration();
    void Unused_GetMethodQualifierSet();
    void Unused_GetMethodOrigin();
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("027947e1-d731-11ce-a357-000000000001")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IEnumWbemClassObject
{
    void Unused_Reset();

    [PreserveSig]
    int Next(int lTimeout, uint uCount, out IntPtr apObjects, out uint puReturned);

    void Unused_NextAsync();
    void Unused_Clone();
    void Unused_Skip();
}

internal static partial class WmiNative
{
    public static readonly Guid ClsidWbemLocator = new("4590f811-1d3a-11d0-891f-00aa004b2e24");

    public const int SOk = 0;
    public const int SFalse = 1;
    public const int RpcEChangedMode = unchecked((int)0x80010106);
    public const int RpcETooLate = unchecked((int)0x80010119);

    public const uint CoinitMultithreaded = 0x0;
    public const uint RpcCAutnWinnt = 10;
    public const uint RpcCAuthzNone = 0;
    public const uint RpcCAuthnLevelDefault = 0;
    public const uint RpcCAuthnLevelCall = 3;
    public const uint RpcCImpLevelImpersonate = 3;
    public const uint EoacNone = 0;

    public const int CimUint8 = 17;
    public const int CimUint32 = 19;

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeSecurity(
        IntPtr pSecDesc,
        int cAuthSvc,
        IntPtr asAuthSvc,
        IntPtr pReserved1,
        uint dwAuthnLevel,
        uint dwImpLevel,
        IntPtr pAuthList,
        uint dwCapabilities,
        IntPtr pReserved3);

    [LibraryImport("ole32.dll")]
    public static partial int CoSetProxyBlanket(
        IntPtr pProxy,
        uint dwAuthnSvc,
        uint dwAuthzSvc,
        IntPtr pServerPrincName,
        uint dwAuthnLevel,
        uint dwImpLevel,
        IntPtr pAuthInfo,
        uint dwCapabilities);

    [LibraryImport("oleaut32.dll")]
    public static partial int VariantClear(IntPtr pvarg);
}
