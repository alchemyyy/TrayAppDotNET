using System.Runtime.InteropServices;
using VolumeTrayAppDotNET.Interop;

using IMMDevice = VolumeTrayAppDotNET.Interop.IMMDevice;
using IMMDeviceEnumerator = VolumeTrayAppDotNET.Interop.IMMDeviceEnumerator;
using MMDeviceEnumeratorFactory = VolumeTrayAppDotNET.Interop.MMDeviceEnumeratorFactory;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Asks the Windows Bluetooth audio driver behind a Core Audio endpoint to reconnect its physical
/// device. The KS request is one-shot and asynchronous: success means the driver accepted an
/// attempt, while the eventual IMMNotificationClient state change reports whether it connected.
/// </summary>
internal static class BluetoothAudioConnector
{
    public static bool TryReconnect(string endpointID)
    {
        if (string.IsNullOrWhiteSpace(endpointID)) return false;

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? endpoint = null;
        IMMDevice? adapter = null;
        IntPtr topologyPtr = IntPtr.Zero;
        IntPtr connectorPtr = IntPtr.Zero;
        IntPtr adapterIDPtr = IntPtr.Zero;
        IntPtr ksControlPtr = IntPtr.Zero;

        try
        {
            enumerator = MMDeviceEnumeratorFactory.Create();
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
                return false;
            }

            int connectorHr = KSTopologyNative.CallTopologyGetConnector(topologyPtr, 0, out connectorPtr);
            if (connectorHr < 0 || connectorPtr == IntPtr.Zero)
            {
                TADNLog.Log(
                    $"BluetoothAudioConnector: IDeviceTopology.GetConnector(0) failed for '{endpointID}', hr=0x{connectorHr:X8}");
                return false;
            }

            // The activated topology above represents the endpoint itself. IKsControl is exposed
            // by the adapter on the other side of its connector, not by the endpoint. Asking the
            // endpoint topology for its own device ID just returns endpointID and therefore makes
            // IMMDevice.Activate(IKsControl) fail with E_NOINTERFACE.
            int adapterIDHr = KSTopologyNative.CallConnectorGetDeviceIDConnectedTo(connectorPtr, out adapterIDPtr);
            if (adapterIDHr < 0 || adapterIDPtr == IntPtr.Zero)
            {
                TADNLog.Log(
                    $"BluetoothAudioConnector: IConnector.GetDeviceIdConnectedTo failed for '{endpointID}', hr=0x{adapterIDHr:X8}");
                return false;
            }

            string? adapterID = Marshal.PtrToStringUni(adapterIDPtr);
            if (string.IsNullOrWhiteSpace(adapterID)) return false;

            enumerator.GetDevice(adapterID, out adapter);
            int controlHr = adapter.Activate(
                in KSConstants.IID_IKsControl,
                ClsCtx.ALL,
                IntPtr.Zero,
                out ksControlPtr);
            if (controlHr < 0 || ksControlPtr == IntPtr.Zero)
            {
                TADNLog.Log(
                    $"BluetoothAudioConnector: Activate(IKsControl) failed for '{adapterID}', hr=0x{controlHr:X8}");
                return false;
            }

            TADNLog.LogDebug(
                $"BluetoothAudioConnector: endpoint '{endpointID}' resolved to adapter '{adapterID}'");

            KSTopologyNative.KSPropertyRequest request = new()
            {
                Set = KSConstants.KSPROPSETID_BtAudio,
                ID = KSConstants.KSPROPERTY_ONESHOT_RECONNECT,
                Flags = KSConstants.KSPROPERTY_TYPE_GET
            };

            int propertyHr = KSTopologyNative.CallKSProperty(ksControlPtr, ref request, out _);
            if (propertyHr < 0)
            {
                TADNLog.Log(
                    $"BluetoothAudioConnector: KSPROPERTY_ONESHOT_RECONNECT failed for '{endpointID}', hr=0x{propertyHr:X8}");
                return false;
            }

            TADNLog.LogDebug($"BluetoothAudioConnector: reconnect requested for '{endpointID}'");
            return true;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"BluetoothAudioConnector: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            if (ksControlPtr != IntPtr.Zero) Marshal.Release(ksControlPtr);
            if (adapterIDPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(adapterIDPtr);
            if (connectorPtr != IntPtr.Zero) Marshal.Release(connectorPtr);
            if (topologyPtr != IntPtr.Zero) Marshal.Release(topologyPtr);
            Safe.Release(adapter);
            Safe.Release(endpoint);
            Safe.Release(enumerator);
        }
    }
}
