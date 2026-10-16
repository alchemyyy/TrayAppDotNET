using System.Runtime.InteropServices;
using VolumeTrayAppDotNET.Interop;
using IMMDevice = VolumeTrayAppDotNET.Interop.IMMDevice;
using IMMDeviceEnumerator = VolumeTrayAppDotNET.Interop.IMMDeviceEnumerator;
using IPropertyStore = VolumeTrayAppDotNET.Interop.IPropertyStore;
using MMDeviceEnumeratorFactory = VolumeTrayAppDotNET.Interop.MMDeviceEnumeratorFactory;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Sends the Windows Bluetooth audio drivers' one-shot connect and disconnect requests to the KS
/// filters behind Core Audio endpoints, as the Windows Settings Connect and Disconnect buttons do.
/// Each profile has its own driver: A2DP behind the stereo endpoint, HFP behind the hands-free
/// playback and recording pair. A request is one-shot and asynchronous: acceptance means the driver
/// started an attempt, while the eventual IMMNotificationClient state change reports the outcome.
/// </summary>
internal static class BluetoothAudioConnector
{
    /// <summary>
    /// Sends one request to each distinct filter behind the endpoints, never stopping at the first
    /// driver that accepts. True when any driver accepted.
    /// </summary>
    public static bool RequestConnection(IReadOnlyList<string> endpointIDs, bool connect)
    {
        uint propertyID = connect
            ? KSConstants.KSPROPERTY_ONESHOT_RECONNECT
            : KSConstants.KSPROPERTY_ONESHOT_DISCONNECT;
        string requestName = connect ? "reconnect" : "disconnect";
        HashSet<string> requestedFilterIDs = new(StringComparer.OrdinalIgnoreCase);
        bool isAnyAccepted = false;
        IMMDeviceEnumerator? enumerator = null;

        try
        {
            enumerator = MMDeviceEnumeratorFactory.Create();
            for (int endpointIndex = 0; endpointIndex < endpointIDs.Count; endpointIndex++)
            {
                string endpointID = endpointIDs[endpointIndex];
                if (string.IsNullOrWhiteSpace(endpointID)) continue;

                // NOTE: The hands-free playback and recording endpoints share one filter, which gets one request
                string? filterID = TryResolveFilterID(enumerator, endpointID);
                if (filterID == null || !requestedFilterIDs.Add(filterID)) continue;

                if (!TrySendRequest(enumerator, filterID, propertyID, requestName)) continue;

                isAnyAccepted = true;
                TADNLog.LogDebug(
                    $"BluetoothAudioConnector: {requestName} requested for '{endpointID}' through '{filterID}'");
            }
        }
        catch (Exception exception)
        {
            TADNLog.Log($"BluetoothAudioConnector: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            Safe.Release(enumerator);
        }

        return isAnyAccepted;
    }

    /// <summary>
    /// Device ID of the KS filter on the far side of the endpoint's connector 0, the filter that
    /// implements KSPROPSETID_BtAudio. Null when the endpoint has no reachable filter.
    /// </summary>
    private static string? TryResolveFilterID(IMMDeviceEnumerator enumerator, string endpointID)
    {
        IMMDevice? endpoint = null;
        IntPtr topologyPtr = IntPtr.Zero;
        IntPtr connectorPtr = IntPtr.Zero;
        IntPtr filterIDPtr = IntPtr.Zero;

        try
        {
            enumerator.GetDevice(endpointID, out endpoint);

            int topologyHr = endpoint.Activate(
                in KSConstants.IID_IDeviceTopology,
                ClsCtx.ALL,
                IntPtr.Zero,
                out topologyPtr);
            if (topologyHr < 0 || topologyPtr == IntPtr.Zero)
            {
                TADNLog.Log(
                    $"BluetoothAudioConnector: Activate(IDeviceTopology) failed for '{endpointID}', hr=0x{topologyHr:X8}");
                return null;
            }

            int connectorHr =
                KSTopologyNative.CallTopologyGetConnector(topologyPtr, connectorIndex: 0, out connectorPtr);
            if (connectorHr < 0 || connectorPtr == IntPtr.Zero)
            {
                TADNLog.Log(
                    $"BluetoothAudioConnector: IDeviceTopology.GetConnector(0) failed for '{endpointID}', hr=0x{connectorHr:X8}");
                return null;
            }

            // The activated topology above represents the endpoint itself. IKsControl is exposed
            // by the filter on the other side of its connector, not by the endpoint. Asking the
            // endpoint topology for its own device ID just returns endpointID and therefore makes
            // IMMDevice.Activate(IKsControl) fail with E_NOINTERFACE.
            int filterIDHr = KSTopologyNative.CallConnectorGetDeviceIDConnectedTo(connectorPtr, out filterIDPtr);
            if (filterIDHr < 0 || filterIDPtr == IntPtr.Zero)
            {
                TADNLog.Log(
                    $"BluetoothAudioConnector: IConnector.GetDeviceIdConnectedTo failed for '{endpointID}', hr=0x{filterIDHr:X8}");
                return null;
            }

            string? filterID = Marshal.PtrToStringUni(filterIDPtr);
            if (string.IsNullOrWhiteSpace(filterID)) return null;

            TADNLog.LogDebug($"BluetoothAudioConnector: endpoint '{endpointID}' resolved to filter '{filterID}'");
            return filterID;
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"BluetoothAudioConnector: filter lookup failed for '{endpointID}': {exception.GetType().Name}: {exception.Message}");
            return null;
        }
        finally
        {
            if (filterIDPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(filterIDPtr);
            if (connectorPtr != IntPtr.Zero) Marshal.Release(connectorPtr);
            if (topologyPtr != IntPtr.Zero) Marshal.Release(topologyPtr);
            Safe.Release(endpoint);
        }
    }

    /// <summary>
    /// Follows the Settings route: a filter that names a controller interface, as the A2DP filters do,
    /// gets the request there with no IKsControl fallback; every other filter gets it through IKsControl.
    /// </summary>
    private static bool TrySendRequest(
        IMMDeviceEnumerator enumerator,
        string filterID,
        uint propertyID,
        string requestName)
    {
        IMMDevice? filter = null;

        try
        {
            enumerator.GetDevice(filterID, out filter);
            string? controllerInterfacePath = ReadControllerInterfacePath(filter);
            return controllerInterfacePath == null
                ? TrySendThroughKSControl(filter, filterID, propertyID, requestName)
                : TrySendThroughControllerInterface(controllerInterfacePath, propertyID, requestName);
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"BluetoothAudioConnector: one-shot {requestName} failed for '{filterID}': {exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            Safe.Release(filter);
        }
    }

    /// <summary>Non-empty controller interface path the filter publishes, else null.</summary>
    private static string? ReadControllerInterfacePath(IMMDevice filter)
    {
        IPropertyStore? store = null;

        try
        {
            filter.OpenPropertyStore(Stgm.Read, out store);
            PROPERTYKEY key = PropertyKeys.PKEY_KsAudio_Controller_DeviceInterface_Path;
            store.GetValue(ref key, out PROPVARIANT value);
            try
            {
                string? path = value.GetString();
                return string.IsNullOrEmpty(path) ? null : path;
            }
            finally
            {
                Ole32.PropVariantClear(ref value);
            }
        }
        catch (Exception exception)
        {
            // Settings treats an unreadable store like a missing property and uses IKsControl
            TADNLog.LogDebug(
                $"BluetoothAudioConnector: controller interface lookup failed: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
        finally
        {
            Safe.Release(store);
        }
    }

    private static bool TrySendThroughKSControl(
        IMMDevice filter,
        string filterID,
        uint propertyID,
        string requestName)
    {
        IntPtr ksControlPtr = IntPtr.Zero;

        try
        {
            int controlHr = filter.Activate(
                in KSConstants.IID_IKsControl,
                ClsCtx.ALL,
                IntPtr.Zero,
                out ksControlPtr);
            if (controlHr < 0 || ksControlPtr == IntPtr.Zero)
            {
                TADNLog.Log(
                    $"BluetoothAudioConnector: Activate(IKsControl) failed for '{filterID}', hr=0x{controlHr:X8}");
                return false;
            }

            KSTopologyNative.KSPropertyRequest request = CreateRequest(propertyID);
            int propertyHr = KSTopologyNative.CallKSProperty(ksControlPtr, ref request, out _);
            if (propertyHr >= 0) return true;

            TADNLog.Log(
                $"BluetoothAudioConnector: one-shot {requestName} failed for '{filterID}', hr=0x{propertyHr:X8}");
            return false;
        }
        finally
        {
            if (ksControlPtr != IntPtr.Zero) Marshal.Release(ksControlPtr);
        }
    }

    /// <summary>
    /// Opens the controller interface by its published path, "\??\" prefix included as Settings passes
    /// it, and submits the property as IOCTL_KS_PROPERTY.
    /// </summary>
    private static bool TrySendThroughControllerInterface(
        string controllerInterfacePath,
        uint propertyID,
        string requestName)
    {
        // NOTE: Settings opens this handle overlapped yet passes no OVERLAPPED, which DeviceIoControl leaves undefined; a synchronous handle avoids that
        IntPtr handle = KSTopologyNative.CreateFileW(
            controllerInterfacePath,
            KSConstants.GENERIC_READ | KSConstants.GENERIC_WRITE,
            KSConstants.FILE_SHARE_READ | KSConstants.FILE_SHARE_WRITE,
            IntPtr.Zero,
            KSConstants.OPEN_EXISTING,
            dwFlagsAndAttributes: 0,
            IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == Kernel32.INVALID_HANDLE_VALUE)
        {
            TADNLog.Log(
                $"BluetoothAudioConnector: CreateFile failed for controller '{controllerInterfacePath}', error={Marshal.GetLastWin32Error()}");
            return false;
        }

        try
        {
            KSTopologyNative.KSPropertyRequest request = CreateRequest(propertyID);
            bool isAccepted = KSTopologyNative.DeviceIoControl(
                handle,
                KSConstants.IOCTL_KS_PROPERTY,
                ref request,
                (uint)Marshal.SizeOf<KSTopologyNative.KSPropertyRequest>(),
                IntPtr.Zero,
                nOutBufferSize: 0,
                out _,
                IntPtr.Zero);
            if (isAccepted) return true;

            TADNLog.Log(
                $"BluetoothAudioConnector: one-shot {requestName} failed for controller '{controllerInterfacePath}', error={Marshal.GetLastWin32Error()}");
            return false;
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    private static KSTopologyNative.KSPropertyRequest CreateRequest(uint propertyID) => new()
    {
        Set = KSConstants.KSPROPSETID_BtAudio,
        ID = propertyID,
        Flags = KSConstants.KSPROPERTY_TYPE_GET
    };
}
