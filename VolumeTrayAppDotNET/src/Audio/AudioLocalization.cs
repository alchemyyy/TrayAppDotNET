using System.Globalization;

namespace VolumeTrayAppDotNET.Audio;

internal static class AudioLocalization
{
    public static string UnknownAppName =>
        L(nameof(AppStrings.Audio_UnknownAppName));

    public static string SystemSoundsName =>
        L(nameof(AppStrings.Audio_SystemSoundsName));

    public static string UnknownDeviceName =>
        L(nameof(AppStrings.Audio_UnknownDeviceName));

    public static string AppTooltip(string appName, uint processId) =>
        string.Format(
            CultureInfo.CurrentCulture,
            L(nameof(AppStrings.Audio_AppTooltipFormat)),
            appName,
            processId);

    public static string BatteryLevel(int percent) =>
        string.Format(
            CultureInfo.CurrentCulture,
            L(nameof(AppStrings.Audio_BatteryLevelFormat)),
            percent);

    public static string DeviceFormat(int channels, int bits, int sampleRate) =>
        string.Format(
            CultureInfo.CurrentCulture,
            L(nameof(AppStrings.Audio_DeviceFormatFormat)),
            channels,
            bits,
            sampleRate);

    public static string BluetoothCodecUnknownInvalidVendor(byte standardCodecId, int vendorId, int vendorCodecId) =>
        string.Format(
            CultureInfo.CurrentCulture,
            L(nameof(AppStrings.Audio_BluetoothCodecUnknownInvalidVendorFormat)),
            standardCodecId,
            vendorId,
            vendorCodecId);

    public static string BluetoothCodecUnknown(int vendorId, int vendorCodecId) =>
        string.Format(
            CultureInfo.CurrentCulture,
            L(nameof(AppStrings.Audio_BluetoothCodecUnknownFormat)),
            vendorId,
            vendorCodecId);

    private static string L(string key) => LocalizationManager.Instance[key];
}
