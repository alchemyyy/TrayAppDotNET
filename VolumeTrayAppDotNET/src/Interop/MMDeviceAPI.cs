using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace VolumeTrayAppDotNET.Interop;

// IMMDeviceEnumerator / IMMDevice / IMMDeviceCollection / IMMEndpoint / IMMNotificationClient.
// Verified against Windows SDK mmdeviceapi.h:
//   IMMDeviceEnumerator         a95664d2-9614-4f35-a746-de8db63617e6
//   IMMDevice                   d666063f-1587-4e43-81f1-b948e807363f
//   IMMDeviceCollection         0bd7a1be-7a1a-44db-8397-cc5392387b5e
//   IMMEndpoint                 1be09788-6894-4089-8586-9a2a6c265ac5
//   IMMNotificationClient       7991eec9-7e89-4d85-8390-6c703cec60c0
//   CLSID_MMDeviceEnumerator    bcde0395-e52f-467c-8e3d-c4579291692e

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("d666063f-1587-4e43-81f1-b948e807363f")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMMDevice
{
    // The activated interface arrives as IUnknown* via void**; the runtime hands us a managed proxy
    // and the caller QIs by casting to the desired RCW type.
    [PreserveSig]
    int Activate(
        in Guid iid,
        ClsCtx dwClsCtx,
        IntPtr pActivationParams,
        out IntPtr ppInterface);

    void OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);

    [PreserveSig]
    int GetId(out string ppstrId);

    [PreserveSig]
    int GetState(out uint pdwState);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0bd7a1be-7a1a-44db-8397-cc5392387b5e")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMMDeviceCollection
{
    void GetCount(out uint pcDevices);
    void Item(uint nDevice, [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppDevice);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("1be09788-6894-4089-8586-9a2a6c265ac5")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMMEndpoint
{
    void GetDataFlow(out EDataFlow pDataFlow);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("a95664d2-9614-4f35-a746-de8db63617e6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(
        EDataFlow dataFlow,
        DeviceState dwStateMask,
        out IMMDeviceCollection ppDevices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(
        EDataFlow dataFlow,
        ERole role,
        out IMMDevice ppEndpoint);

    void GetDevice(
        string pwstrId,
        out IMMDevice ppDevice);

    void RegisterEndpointNotificationCallback(
        IMMNotificationClient pClient);

    void UnregisterEndpointNotificationCallback(
        IMMNotificationClient pClient);
}

// Implemented in managed code. NO [ComImport] - see CCW rule in AudioInterop.cs header.
[Guid("7991eec9-7e89-4d85-8390-6c703cec60c0")]
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState);

    [PreserveSig]
    int OnDeviceAdded(string pwstrDeviceId);

    [PreserveSig]
    int OnDeviceRemoved(string pwstrDeviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? pwstrDefaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PROPERTYKEY key);
}

internal static class MMDeviceExtensions
{
    public static int Activate(
        this IMMDevice device,
        Guid iid,
        ClsCtx clsCtx,
        IntPtr activationParams,
        out object? instance)
    {
        int hr = device.Activate(in iid, clsCtx, activationParams, out IntPtr ptr);
        if (hr < 0 || ptr == IntPtr.Zero)
        {
            instance = null;
            return hr;
        }

        try
        {
            instance = ComActivation.GetObjectForComInstance<object>(ptr);
            return hr;
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    public static int Activate<T>(
        this IMMDevice device,
        Guid iid,
        ClsCtx clsCtx,
        IntPtr activationParams,
        out T? instance)
        where T : class
    {
        int hr = device.Activate(in iid, clsCtx, activationParams, out IntPtr ptr);
        if (hr < 0 || ptr == IntPtr.Zero)
        {
            instance = null;
            return hr;
        }

        unsafe
        {
            void* unmanaged = (void*)ptr;
            try
            {
                instance = UniqueComInterfaceMarshaller<T>.ConvertToManaged(unmanaged);
                return hr;
            }
            finally
            {
                UniqueComInterfaceMarshaller<T>.Free(unmanaged);
            }
        }
    }
}

internal static partial class MMDeviceEnumeratorFactory
{
    private static readonly Guid ClsidMMDeviceEnumerator = new("bcde0395-e52f-467c-8e3d-c4579291692e");

    public static IMMDeviceEnumerator Create()
    {
        Guid clsid = ClsidMMDeviceEnumerator;
        Guid iid = typeof(IMMDeviceEnumerator).GUID;
        int hr = CoCreateInstance(
            in clsid,
            IntPtr.Zero,
            (uint)ClsCtx.ALL,
            in iid,
            out IMMDeviceEnumerator enumerator);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return enumerator;
    }

    [LibraryImport("ole32.dll", EntryPoint = "CoCreateInstance")]
    private static partial int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IMMDeviceEnumerator>))]
        out IMMDeviceEnumerator ppv);
}
