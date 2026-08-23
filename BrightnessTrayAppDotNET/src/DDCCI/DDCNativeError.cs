using System.Globalization;

namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>Formats and classifies native Monitor Configuration API failures.</summary>
internal static class DDCNativeError
{
    private const int InvalidMessageChecksum = unchecked((int)0xC026258B);
    private const string InvalidMessageChecksumHex = "0xC026258B";

    /// <summary>Formats a native error as both signed decimal and HRESULT-style hexadecimal.</summary>
    public static string Format(string operation, int errorCode) =>
        $"{operation} failed (Win32: {errorCode.ToString(CultureInfo.InvariantCulture)}, "
        + $"0x{unchecked((uint)errorCode):X8})";

    /// <summary>Returns true for ERROR_GRAPHICS_DDCCI_INVALID_MESSAGE_CHECKSUM.</summary>
    public static bool IsInvalidMessageChecksum(string? error)
    {
        if (string.IsNullOrEmpty(error)) return false;

        return error.Contains(InvalidMessageChecksumHex, StringComparison.OrdinalIgnoreCase)
               || error.Contains(
                   InvalidMessageChecksum.ToString(CultureInfo.InvariantCulture),
                   StringComparison.Ordinal);
    }
}
