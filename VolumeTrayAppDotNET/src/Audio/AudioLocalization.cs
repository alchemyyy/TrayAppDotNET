using System.Globalization;

namespace VolumeTrayAppDotNET.Audio;

internal static class AudioLocalization
{
    public static string UnknownAppName =>
        L("Audio_UnknownAppName", "Unknown");

    public static string SystemSoundsName =>
        L("Audio_SystemSoundsName", "System Sounds");

    public static string UnknownDeviceName =>
        L("Audio_UnknownDeviceName", "Unknown Device");

    public static string AppTooltip(string appName, uint processId) =>
        string.Format(
            CultureInfo.CurrentCulture,
            L("Audio_AppTooltipFormat", "{0}\nPID: {1}"),
            appName,
            processId);

    public static string BatteryLevel(int percent) =>
        string.Format(
            CultureInfo.CurrentCulture,
            L("Audio_BatteryLevelFormat", "{0}%"),
            percent);

    public static string DeviceFormat(int channels, int bits, int sampleRate) =>
        string.Format(
            CultureInfo.CurrentCulture,
            L("Audio_DeviceFormatFormat", "{0} channel, {1} bit, {2} Hz"),
            channels,
            bits,
            sampleRate);

    public static string BluetoothCodecUnknownInvalidVendor(byte standardCodecId, int vendorId, int vendorCodecId) =>
        string.Format(
            CultureInfo.CurrentCulture,
            L("Audio_BluetoothCodecUnknownInvalidVendorFormat", "Unknown Codec (Invalid Vendor): 0x{0:X2} {1}:{2}"),
            standardCodecId,
            vendorId,
            vendorCodecId);

    public static string BluetoothCodecUnknown(int vendorId, int vendorCodecId) =>
        string.Format(
            CultureInfo.CurrentCulture,
            L("Audio_BluetoothCodecUnknownFormat", "Unknown Codec: 0x{0:X4}:0x{1:X4}"),
            vendorId,
            vendorCodecId);

    private static string L(string key, string fallback) =>
        LocalizationManager.Instance.GetString(key, fallback);
}
