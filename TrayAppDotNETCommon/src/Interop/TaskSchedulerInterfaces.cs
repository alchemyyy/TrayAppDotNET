using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace TrayAppDotNETCommon.Interop;

// Task Scheduler 2.0 interfaces, verified against Windows SDK taskschd.h. They are dual interfaces, so IDispatch's four
// members come first. Only the members the app calls are typed; the rest are vtable padding in declaration order.
// Every COM object these hand out is marshalled with UniqueComInterfaceMarshaller, so Safe.Release frees it
// deterministically (see COMActivation.TryReleaseGeneratedComObject).
//   CLSID_TaskScheduler        0f87369f-a4e5-4cfc-bd3e-73e6154572dd
//   ITaskService               2faba4c7-4da9-4013-9697-20cc3fd40f85
//   ITaskFolder                8cfac062-a080-4c15-9a88-aa7c2af80dfc
//   IRegisteredTask            9c86f320-dee3-4dd1-b972-a303f26b061e
//   IRegisteredTaskCollection  86627eb4-42a7-41e4-a4d9-ac33a72f2d52

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("2faba4c7-4da9-4013-9697-20cc3fd40f85")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface ITaskService
{
    void Unused_GetTypeInfoCount();
    void Unused_GetTypeInfo();
    void Unused_GetIDsOfNames();
    void Unused_Invoke();

    [PreserveSig]
    int GetFolder(
        [MarshalAs(UnmanagedType.BStr)] string path,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<ITaskFolder>))] out ITaskFolder? folder);

    void Unused_GetRunningTasks();
    void Unused_NewTask();

    [PreserveSig]
    int Connect(TaskVariant serverName, TaskVariant user, TaskVariant domain, TaskVariant password);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("8cfac062-a080-4c15-9a88-aa7c2af80dfc")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface ITaskFolder
{
    void Unused_GetTypeInfoCount();
    void Unused_GetTypeInfo();
    void Unused_GetIDsOfNames();
    void Unused_Invoke();
    void Unused_GetName();
    void Unused_GetPath();

    [PreserveSig]
    int GetFolder(
        [MarshalAs(UnmanagedType.BStr)] string path,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<ITaskFolder>))] out ITaskFolder? folder);

    void Unused_GetFolders();

    [PreserveSig]
    int CreateFolder(
        [MarshalAs(UnmanagedType.BStr)] string subFolderName,
        TaskVariant securityDescriptor,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<ITaskFolder>))] out ITaskFolder? folder);

    [PreserveSig]
    int DeleteFolder([MarshalAs(UnmanagedType.BStr)] string subFolderName, int flags);

    [PreserveSig]
    int GetTask(
        [MarshalAs(UnmanagedType.BStr)] string path,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IRegisteredTask>))] out IRegisteredTask? task);

    [PreserveSig]
    int GetTasks(
        int flags,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IRegisteredTaskCollection>))] out IRegisteredTaskCollection? tasks);

    [PreserveSig]
    int DeleteTask([MarshalAs(UnmanagedType.BStr)] string name, int flags);

    [PreserveSig]
    int RegisterTask(
        [MarshalAs(UnmanagedType.BStr)] string path,
        [MarshalAs(UnmanagedType.BStr)] string xmlText,
        int flags,
        TaskVariant userId,
        TaskVariant password,
        int logonType,
        TaskVariant securityDescriptor,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IRegisteredTask>))] out IRegisteredTask? task);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("9c86f320-dee3-4dd1-b972-a303f26b061e")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IRegisteredTask
{
    void Unused_GetTypeInfoCount();
    void Unused_GetTypeInfo();
    void Unused_GetIDsOfNames();
    void Unused_Invoke();

    /// <summary>get_Name.</summary>
    [PreserveSig]
    int GetName([MarshalAs(UnmanagedType.BStr)] out string? name);

    void Unused_GetPath();
    void Unused_GetState();
    void Unused_GetEnabled();
    void Unused_PutEnabled();

    /// <summary>Runs the task now; <paramref name="parameters"/> becomes $(Arg0) in the task's action.</summary>
    [PreserveSig]
    int Run(TaskVariant parameters, out IntPtr runningTask);

    void Unused_RunEx();
    void Unused_GetInstances();
    void Unused_GetLastRunTime();
    void Unused_GetLastTaskResult();
    void Unused_GetNumberOfMissedRuns();
    void Unused_GetNextRunTime();
    void Unused_GetDefinition();

    /// <summary>get_Xml: the registered definition.</summary>
    [PreserveSig]
    int GetXml([MarshalAs(UnmanagedType.BStr)] out string? xml);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("86627eb4-42a7-41e4-a4d9-ac33a72f2d52")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IRegisteredTaskCollection
{
    void Unused_GetTypeInfoCount();
    void Unused_GetTypeInfo();
    void Unused_GetIDsOfNames();
    void Unused_Invoke();

    /// <summary>get_Count.</summary>
    [PreserveSig]
    int GetCount(out int count);

    /// <summary>get_Item; the index is one-based.</summary>
    [PreserveSig]
    int GetItem(
        TaskVariant index,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IRegisteredTask>))] out IRegisteredTask? task);
}

/// <summary>
/// An Automation VARIANT with the native x64 size (24 bytes), for the by-value VARIANT arguments of the Task Scheduler
/// interfaces. The default value is VT_EMPTY, which the optional arguments take as "not given". A string variant owns
/// its BSTR until <see cref="Free"/>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct TaskVariant
{
    private const ushort VtI4 = 3;
    private const ushort VtBStr = 8;

    [FieldOffset(0)] private ushort _type;
    [FieldOffset(8)] private int _int32;
    [FieldOffset(8)] private IntPtr _bstr;

    public static TaskVariant FromInt32(int value) => new() { _type = VtI4, _int32 = value };

    public static TaskVariant FromString(string value) => new() { _type = VtBStr, _bstr = Marshal.StringToBSTR(value) };

    /// <summary>Frees the BSTR of a string variant and resets it to VT_EMPTY.</summary>
    public void Free()
    {
        if (_type == VtBStr && _bstr != IntPtr.Zero) Marshal.FreeBSTR(_bstr);
        this = default;
    }
}
