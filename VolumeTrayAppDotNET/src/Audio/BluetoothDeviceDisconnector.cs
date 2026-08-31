using System.Runtime.InteropServices;
using VolumeTrayAppDotNET.Interop;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>Disconnects a paired Classic Bluetooth device without removing its pairing.</summary>
internal static class BluetoothDeviceDisconnector
{
    private const int BluetoothAddressHexLength = 12;

    internal static bool TryParseAddress(string? deviceInstanceId, out ulong address)
    {
        address = 0;
        if (string.IsNullOrEmpty(deviceInstanceId)) return false;

        const string marker = "DEV_";
        int markerIndex = deviceInstanceId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return false;

        int addressStart = markerIndex + marker.Length;
        int addressEnd = addressStart + BluetoothAddressHexLength;
        if (addressEnd > deviceInstanceId.Length) return false;
        if (addressEnd < deviceInstanceId.Length && HexValue(deviceInstanceId[addressEnd]) >= 0) return false;

        ulong parsed = 0;
        for (int i = addressStart; i < addressEnd; i++)
        {
            int digit = HexValue(deviceInstanceId[i]);
            if (digit < 0) return false;
            parsed = (parsed << 4) | (uint)digit;
        }

        if (parsed == 0) return false;
        address = parsed;
        return true;
    }

    public static bool TryDisconnect(ulong address)
    {
        if (address == 0) return false;

        BluetoothApis.BLUETOOTH_FIND_RADIO_PARAMS findParameters = new()
        {
            dwSize = (uint)Marshal.SizeOf<BluetoothApis.BLUETOOTH_FIND_RADIO_PARAMS>()
        };

        IntPtr findHandle = BluetoothApis.BluetoothFindFirstRadio(ref findParameters, out IntPtr radioHandle);
        if (findHandle == IntPtr.Zero)
        {
            TADNLog.Log($"BluetoothDeviceDisconnector: no local radio found; error={Marshal.GetLastWin32Error()}");
            return false;
        }

        int lastError = 0;
        try
        {
            while (radioHandle != IntPtr.Zero)
            {
                bool disconnected;
                try
                {
                    disconnected = BluetoothApis.DeviceIoControl(
                        radioHandle,
                        BluetoothApis.IOCTL_BTH_DISCONNECT_DEVICE,
                        ref address,
                        sizeof(ulong),
                        IntPtr.Zero,
                        nOutBufferSize: 0,
                        out _,
                        IntPtr.Zero);
                    if (!disconnected) lastError = Marshal.GetLastWin32Error();
                }
                finally
                {
                    Kernel32.CloseHandle(radioHandle);
                }

                if (disconnected)
                {
                    TADNLog.LogDebug($"BluetoothDeviceDisconnector: disconnected {address:X12}");
                    return true;
                }

                if (!BluetoothApis.BluetoothFindNextRadio(findHandle, out radioHandle)) break;
            }
        }
        catch (Exception exception)
        {
            TADNLog.Log($"BluetoothDeviceDisconnector: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            BluetoothApis.BluetoothFindRadioClose(findHandle);
        }

        TADNLog.Log($"BluetoothDeviceDisconnector: disconnect failed for {address:X12}; error={lastError}");
        return false;
    }

    private static int HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'A' and <= 'F' => value - 'A' + 10,
        >= 'a' and <= 'f' => value - 'a' + 10,
        _ => -1
    };
}
