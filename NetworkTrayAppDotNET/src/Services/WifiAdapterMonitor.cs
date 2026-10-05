using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NetworkTrayAppDotNET.Interop;

namespace NetworkTrayAppDotNET.Services;

/// <summary>What the Wi-Fi adapters are doing, as far as the tray icon cares while no connection reaches the internet.</summary>
internal enum WifiAdapterActivity
{
    // No adapter, every radio off, or an adapter associated with a network that has no internet route
    None,
    Connecting,
    Disconnected
}

/// <summary>
/// Reads Wi-Fi adapter states from the WLAN AutoConfig service through the Native Wifi API, the source of the
/// Windows connecting and not-connected states. <see cref="StateChanged"/> fires when a connection attempt starts,
/// fails or drops, or an adapter arrives or leaves, so those states show without waiting for a poll. Radio switches
/// only arrive as MSM notifications, which need location consent, so the poll picks those up.
/// </summary>
internal sealed unsafe class WifiAdapterMonitor : IDisposable
{
    private readonly Lock _gate = new();
    private GCHandle _callbackContext;
    private IntPtr _clientHandle;
    private bool _loggedOpenFailure;
    private bool _disposed;

    public WifiAdapterMonitor() => _callbackContext = GCHandle.Alloc(this);

    /// <summary>Raised on a WLAN AutoConfig callback thread, so handlers must not block.</summary>
    public event Action? StateChanged;

    /// <summary>Summarizes every Wi-Fi adapter, opening the WLAN client on first use. Never throws.</summary>
    public WifiAdapterActivity ReadActivity()
    {
        lock (_gate)
        {
            if (_disposed || !EnsureOpen()) return WifiAdapterActivity.None;

            WLANAPI.WLAN_INTERFACE_INFO_LIST* list = null;
            uint result = WLANAPI.WlanEnumInterfaces(_clientHandle, IntPtr.Zero, &list);
            if (result != WLANAPI.ERROR_SUCCESS || list == null)
            {
                // A restarted WLAN AutoConfig service invalidates the handle, so reopen on the next read
                TADNLog.Log($"WifiAdapterMonitor: WlanEnumInterfaces failed with {result}");
                CloseClient();
                return WifiAdapterActivity.None;
            }

            try
            {
                return Summarize(list);
            }
            finally
            {
                WLANAPI.WlanFreeMemory(list);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            // WlanCloseHandle waits for a running notification callback, so nothing uses the context afterwards
            CloseClient();
        }

        StateChanged = null;
        _callbackContext.Free();
    }

    /// <summary>A connection attempt on any adapter wins; an idle radio only counts while no adapter is associated.</summary>
    private WifiAdapterActivity Summarize(WLANAPI.WLAN_INTERFACE_INFO_LIST* list)
    {
        WLANAPI.WLAN_INTERFACE_INFO* adapters = &list->InterfaceInfo;
        bool isAssociated = false;
        bool hasIdleRadio = false;
        for (uint index = 0; index < list->dwNumberOfItems; index++)
        {
            WLANAPI.WLAN_INTERFACE_INFO* adapter = adapters + index;
            switch (adapter->isState)
            {
                case WLANAPI.WLAN_INTERFACE_STATE.Associating:
                case WLANAPI.WLAN_INTERFACE_STATE.Discovering:
                case WLANAPI.WLAN_INTERFACE_STATE.Authenticating:
                    return WifiAdapterActivity.Connecting;
                case WLANAPI.WLAN_INTERFACE_STATE.Connected:
                case WLANAPI.WLAN_INTERFACE_STATE.AdHocNetworkFormed:
                    isAssociated = true;
                    break;
                case WLANAPI.WLAN_INTERFACE_STATE.Disconnected:
                case WLANAPI.WLAN_INTERFACE_STATE.Disconnecting:
                    // A switched-off radio also reports disconnected, and Windows shows no network for it
                    hasIdleRadio = hasIdleRadio || IsRadioOn(&adapter->InterfaceGuid);
                    break;
            }
        }

        return hasIdleRadio && !isAssociated ? WifiAdapterActivity.Disconnected : WifiAdapterActivity.None;
    }

    /// <summary>An adapter's radio is on when any of its PHYs has both the software and the hardware switch on.</summary>
    private bool IsRadioOn(Guid* interfaceGuid)
    {
        uint dataSize = 0;
        void* data = null;
        uint result = WLANAPI.WlanQueryInterface(
            _clientHandle,
            interfaceGuid,
            WLANAPI.WLAN_INTF_OPCODE_RADIO_STATE,
            IntPtr.Zero,
            &dataSize,
            &data,
            IntPtr.Zero);
        if (result != WLANAPI.ERROR_SUCCESS || data == null) return false;

        try
        {
            if (dataSize < sizeof(uint)) return false;

            // Stay inside both the reported PHY count and the returned buffer
            WLANAPI.WLAN_RADIO_STATE* radio = (WLANAPI.WLAN_RADIO_STATE*)data;
            uint bufferPhyCount = (dataSize - sizeof(uint)) / (uint)sizeof(WLANAPI.WLAN_PHY_RADIO_STATE);
            uint phyCount = Math.Min(radio->dwNumberOfPhys, Math.Min(bufferPhyCount, WLANAPI.WLAN_MAX_PHY_INDEX));
            WLANAPI.WLAN_PHY_RADIO_STATE* phys = &radio->PhyRadioState;
            for (uint index = 0; index < phyCount; index++)
            {
                if (phys[index] is
                    {
                        dot11SoftwareRadioState: WLANAPI.DOT11_RADIO_STATE.On,
                        dot11HardwareRadioState: WLANAPI.DOT11_RADIO_STATE.On
                    })
                    return true;
            }

            return false;
        }
        finally
        {
            WLANAPI.WlanFreeMemory(data);
        }
    }

    private bool EnsureOpen()
    {
        if (_clientHandle != IntPtr.Zero) return true;

        uint negotiatedVersion = 0;
        IntPtr clientHandle = IntPtr.Zero;
        uint result = WLANAPI.WlanOpenHandle(
            WLANAPI.WLAN_API_VERSION_2_0,
            IntPtr.Zero,
            &negotiatedVersion,
            &clientHandle);
        if (result != WLANAPI.ERROR_SUCCESS)
        {
            // Machines without Wi-Fi usually leave WLAN AutoConfig stopped, so only the first failure is logged
            if (!_loggedOpenFailure) TADNLog.Log($"WifiAdapterMonitor: WlanOpenHandle failed with {result}");
            _loggedOpenFailure = true;
            return false;
        }

        _clientHandle = clientHandle;
        _loggedOpenFailure = false;
        result = WLANAPI.WlanRegisterNotification(
            clientHandle,
            WLANAPI.WLAN_NOTIFICATION_SOURCE_ACM,
            bIgnoreDuplicate: 1,
            &OnNotification,
            GCHandle.ToIntPtr(_callbackContext),
            IntPtr.Zero,
            pdwPrevNotifSource: null);
        // The poll still picks up every change without notifications, only later
        if (result != WLANAPI.ERROR_SUCCESS)
            TADNLog.Log($"WifiAdapterMonitor: WlanRegisterNotification failed with {result}");
        return true;
    }

    private void CloseClient()
    {
        if (_clientHandle == IntPtr.Zero) return;

        uint result = WLANAPI.WlanCloseHandle(_clientHandle, IntPtr.Zero);
        if (result != WLANAPI.ERROR_SUCCESS) TADNLog.Log($"WifiAdapterMonitor: WlanCloseHandle failed with {result}");
        _clientHandle = IntPtr.Zero;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnNotification(WLANAPI.WLAN_NOTIFICATION_DATA* notification, IntPtr context)
    {
        try
        {
            if (notification == null
                || !ChangesAdapterActivity(notification->NotificationSource, notification->NotificationCode))
                return;

            if (GCHandle.FromIntPtr(context).Target is WifiAdapterMonitor monitor) monitor.StateChanged?.Invoke();
        }
        catch (Exception exception)
        {
            // An exception unwinding into the WLAN AutoConfig callback thread would end the process
            TADNLog.Log($"WifiAdapterMonitor.OnNotification: {exception.Message}");
        }
    }

    /// <summary>
    /// Completed connections are left to NetworkStatusChanged, which fires once Windows identifies the network.
    /// Refreshing at association time would show no network while DHCP is still running.
    /// </summary>
    private static bool ChangesAdapterActivity(uint source, uint code) =>
        source == WLANAPI.WLAN_NOTIFICATION_SOURCE_ACM
        && code is WLANAPI.WLAN_NOTIFICATION_ACM_CONNECTION_START
            or WLANAPI.WLAN_NOTIFICATION_ACM_CONNECTION_ATTEMPT_FAIL
            or WLANAPI.WLAN_NOTIFICATION_ACM_INTERFACE_ARRIVAL
            or WLANAPI.WLAN_NOTIFICATION_ACM_INTERFACE_REMOVAL
            or WLANAPI.WLAN_NOTIFICATION_ACM_DISCONNECTED;
}
