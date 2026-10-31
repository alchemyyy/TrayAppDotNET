using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Interop;

/// <summary>QueryDosDeviceW with one growth policy for every caller (disk, volume and image-path mapping).</summary>
internal static class DosDevices
{
    private const int InitialBufferLength = 1_024;

    // The full DOS-device name list is a few tens of KiB on a typical machine; the cap only bounds a broken result
    private const int MaximumBufferLength = 1_048_576;
    private const int ErrorInsufficientBuffer = 122;

    /// <summary>Every DOS device name (QueryDosDevice with a null name); empty when the call fails.</summary>
    internal static string[] ListNames() => Query(deviceName: null);

    /// <summary>The native targets of one DOS device name, current mapping first; empty when it has none.</summary>
    internal static string[] QueryTargets(string deviceName) => Query(deviceName);

    /// <summary>Splits a double-null-terminated multi-string into its items.</summary>
    internal static string[] ParseMultiString(ReadOnlySpan<char> value)
    {
        List<string> items = [];
        int position = 0;
        while (position < value.Length)
        {
            ReadOnlySpan<char> remaining = value[position..];
            int terminatorOffset = remaining.IndexOf('\0');
            int itemLength = terminatorOffset >= 0 ? terminatorOffset : remaining.Length;
            if (itemLength == 0) break;

            items.Add(remaining[..itemLength].ToString());
            position += itemLength + 1;
        }

        return [.. items];
    }

    private static string[] Query(string? deviceName)
    {
        int bufferLength = InitialBufferLength;
        while (true)
        {
            char[] buffer = new char[bufferLength];
            uint characterCount = QueryDosDeviceW(deviceName, buffer, (uint)buffer.Length);
            if (characterCount > 0)
                return ParseMultiString(buffer.AsSpan(start: 0, Math.Min(buffer.Length, checked((int)characterCount))));

            if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer || bufferLength == MaximumBufferLength)
                return [];

            bufferLength = Math.Min(checked(bufferLength * 2), MaximumBufferLength);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(
        string? deviceName,
        [Out] char[] targetPath,
        uint maximumCharacterCount);
}
