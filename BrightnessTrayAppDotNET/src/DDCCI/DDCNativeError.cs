using System.Globalization;

namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>Formats and classifies native Monitor Configuration API failures.</summary>
internal static class DDCNativeError
{
    private static readonly NativeErrorText InvalidMessageChecksum = NativeErrorText.For(0xC026258B);

    // A reply arrived but could not be used, so something on the bus answered: unlike a NAK (0xC0262582) or a missing
    // device (0xC0262581). A wedged LG reply pipeline fails every read with these until a SET resynchronizes it. The
    // hdmi-relink recorder saw checksum failures; DISPLAY2 behind a DP-to-HDMI converter answered with invalid
    // message commands and invalid data (btadn log 2026-10-29 07:57-08:02). An active adapter in front of a dark
    // panel answers with them as well
    private static readonly NativeErrorText[] GarbledReplyErrors =
    [
        NativeErrorText.For(0xC0262583), // ERROR_GRAPHICS_I2C_ERROR_RECEIVING_DATA
        NativeErrorText.For(0xC0262585), // ERROR_GRAPHICS_DDCCI_INVALID_DATA
        NativeErrorText.For(0xC0262586), // ERROR_GRAPHICS_DDCCI_MONITOR_RETURNED_INVALID_TIMING_STATUS_BYTE
        NativeErrorText.For(0xC0262589), // ERROR_GRAPHICS_DDCCI_INVALID_MESSAGE_COMMAND
        NativeErrorText.For(0xC026258A), // ERROR_GRAPHICS_DDCCI_INVALID_MESSAGE_LENGTH
        InvalidMessageChecksum
    ];

    /// <summary>Formats a native error as both signed decimal and HRESULT-style hexadecimal.</summary>
    public static string Format(string operation, int errorCode) =>
        $"{operation} failed (Win32: {errorCode.ToString(CultureInfo.InvariantCulture)}, "
        + $"0x{unchecked((uint)errorCode):X8})";

    /// <summary>Returns true for ERROR_GRAPHICS_DDCCI_INVALID_MESSAGE_CHECKSUM.</summary>
    public static bool IsInvalidMessageChecksum(string? error) =>
        !string.IsNullOrEmpty(error) && InvalidMessageChecksum.IsMentionedIn(error);

    /// <summary>
    /// Returns true for a failure whose reply arrived garbled: a wrong checksum, length, command or field, or a broken
    /// receive.
    /// </summary>
    public static bool IsGarbledReply(string? error)
    {
        if (string.IsNullOrEmpty(error)) return false;

        foreach (NativeErrorText garbled in GarbledReplyErrors)
        {
            if (garbled.IsMentionedIn(error)) return true;
        }

        return false;
    }

    /// <summary>
    /// One native error as it appears in a formatted failure: <see cref="Format"/> writes signed decimal and
    /// hexadecimal, and older logs carry the decimal alone.
    /// </summary>
    private readonly record struct NativeErrorText(string Hexadecimal, string Decimal)
    {
        public static NativeErrorText For(uint errorCode) =>
            new($"0x{errorCode:X8}", unchecked((int)errorCode).ToString(CultureInfo.InvariantCulture));

        public bool IsMentionedIn(string error) =>
            error.Contains(Hexadecimal, StringComparison.OrdinalIgnoreCase)
            || error.Contains(Decimal, StringComparison.Ordinal);
    }
}
