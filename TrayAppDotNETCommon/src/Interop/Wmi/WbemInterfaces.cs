using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace TrayAppDotNETCommon.Interop.Wmi;

// Native AOT friendly WMI interfaces (no System.Management, which pulls a reflection-heavy assembly graph). Only the
// members the apps call are typed; the rest are vtable padding in declaration order.

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("dc12a687-737f-11cf-884d-00aa004b2e24")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public partial interface IWbemLocator
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
public partial interface IWbemServices
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
public partial interface IWbemClassObject
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
public partial interface IEnumWbemClassObject
{
    void Unused_Reset();

    [PreserveSig]
    int Next(int lTimeout, uint uCount, out IntPtr apObjects, out uint puReturned);

    void Unused_NextAsync();
    void Unused_Clone();
    void Unused_Skip();
}
