using System.Runtime.InteropServices;

namespace NetworkTrayAppDotNET.Interop;

/// <summary>
/// Native Wifi (wlanapi.dll) declarations for reading Wi-Fi adapter connection and radio states and for receiving
/// WLAN AutoConfig notifications. Layouts and values follow wlanapi.h and l2cmn.h from the Windows SDK.
/// </summary>
internal static unsafe partial class WLANAPI
{
    private const string LibraryName = "wlanapi.dll";
    private const int WLAN_MAX_NAME_LENGTH = 256;

    public const uint ERROR_SUCCESS = 0;
    public const uint WLAN_API_VERSION_2_0 = 0x00000002;
    public const uint WLAN_MAX_PHY_INDEX = 64;

    // WLAN_INTF_OPCODE whose query returns a WLAN_RADIO_STATE
    public const uint WLAN_INTF_OPCODE_RADIO_STATE = 4;

    // L2_NOTIFICATION_SOURCE_WLAN_ACM. MSM notifications are left out: since Windows 11 24H2 they need the
    // location-gated wiFiControl capability, and WlanRegisterNotification fails with ERROR_ACCESS_DENIED without it
    public const uint WLAN_NOTIFICATION_SOURCE_ACM = 0x00000008;

    // WLAN_NOTIFICATION_ACM codes
    public const uint WLAN_NOTIFICATION_ACM_CONNECTION_START = 9;
    public const uint WLAN_NOTIFICATION_ACM_CONNECTION_ATTEMPT_FAIL = 11;
    public const uint WLAN_NOTIFICATION_ACM_INTERFACE_ARRIVAL = 13;
    public const uint WLAN_NOTIFICATION_ACM_INTERFACE_REMOVAL = 14;
    public const uint WLAN_NOTIFICATION_ACM_DISCONNECTED = 21;

    // Both enums list every native member, used or not, so each value keeps its native number
    public enum WLAN_INTERFACE_STATE
    {
        NotReady,
        Connected,
        AdHocNetworkFormed,
        Disconnecting,
        Disconnected,
        Associating,
        Discovering,
        Authenticating
    }

    public enum DOT11_RADIO_STATE
    {
        Unknown,
        On,
        Off
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        public fixed char strInterfaceDescription[WLAN_MAX_NAME_LENGTH];
        public WLAN_INTERFACE_STATE isState;
    }

    /// <summary>Variable-length list whose <see cref="InterfaceInfo"/> is the first of dwNumberOfItems entries.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WLAN_INTERFACE_INFO_LIST
    {
        public uint dwNumberOfItems;
        public uint dwIndex;
        public WLAN_INTERFACE_INFO InterfaceInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WLAN_PHY_RADIO_STATE
    {
        public uint dwPhyIndex;
        public DOT11_RADIO_STATE dot11SoftwareRadioState;
        public DOT11_RADIO_STATE dot11HardwareRadioState;
    }

    /// <summary>Radio states whose <see cref="PhyRadioState"/> is the first of WLAN_MAX_PHY_INDEX entries.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WLAN_RADIO_STATE
    {
        public uint dwNumberOfPhys;
        public WLAN_PHY_RADIO_STATE PhyRadioState;
    }

    /// <summary>L2_NOTIFICATION_DATA, which WLAN_NOTIFICATION_DATA aliases.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WLAN_NOTIFICATION_DATA
    {
        public uint NotificationSource;
        public uint NotificationCode;
        public Guid InterfaceGuid;
        public uint dwDataSize;
        public IntPtr pData;
    }

    [LibraryImport(LibraryName)]
    public static partial uint WlanOpenHandle(
        uint dwClientVersion,
        IntPtr pReserved,
        uint* pdwNegotiatedVersion,
        IntPtr* phClientHandle);

    /// <summary>Unregisters notifications and waits for a running callback, so never call it from one.</summary>
    [LibraryImport(LibraryName)]
    public static partial uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [LibraryImport(LibraryName)]
    public static partial uint WlanEnumInterfaces(
        IntPtr hClientHandle,
        IntPtr pReserved,
        WLAN_INTERFACE_INFO_LIST** ppInterfaceList);

    [LibraryImport(LibraryName)]
    public static partial uint WlanQueryInterface(
        IntPtr hClientHandle,
        Guid* pInterfaceGuid,
        uint OpCode,
        IntPtr pReserved,
        uint* pdwDataSize,
        void** ppData,
        IntPtr pWlanOpcodeValueType);

    [LibraryImport(LibraryName)]
    public static partial uint WlanRegisterNotification(
        IntPtr hClientHandle,
        uint dwNotifSource,
        int bIgnoreDuplicate,
        delegate* unmanaged[Stdcall]<WLAN_NOTIFICATION_DATA*, IntPtr, void> funcCallback,
        IntPtr pCallbackContext,
        IntPtr pReserved,
        uint* pdwPrevNotifSource);

    [LibraryImport(LibraryName)]
    public static partial void WlanFreeMemory(void* pMemory);
}
