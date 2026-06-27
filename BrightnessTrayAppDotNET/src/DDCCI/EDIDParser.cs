using System.Text;

namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// Minimal EDID 1.3/1.4 block parser.
/// Pulls the fields we care about for monitor identity
/// (serial-number descriptor, numeric serial, product/manufacturer codes, monitor-name descriptor)
/// without dragging in a full EDID structure.
/// Higher-version EDID extensions are left untouched - brightness-tray-app only needs the base 128-byte block.
/// </summary>
internal static class EDIDParser
{
    private const int BlockLength = 128;
    private const int DescriptorBase = 54;
    private const int DescriptorCount = 4;
    private const int DescriptorLength = 18;

    // Tags inside a "monitor descriptor" block (first four bytes are 00 00 00 TAG).
    private const byte TagSerialString = 0xFF;
    private const byte TagMonitorName = 0xFC;

    /// <summary>
    /// Extracts the most-specific stable identifier available for a monitor.
    /// Prefers the ASCII serial-number descriptor (0xFF)
    /// - that's what monitor vendors populate with the per-unit serial.
    /// Falls back to the 4-byte numeric serial at bytes 12-15 if the string descriptor is absent or empty.
    /// Returns <see cref="string.Empty"/> when nothing usable is found
    /// (broken EDID, or a monitor that only carries the manufacturer/model block - which isn't unit-unique).
    /// </summary>
    public static string ExtractSerial(byte[]? edid)
    {
        if (!HasValidHeader(edid)) return string.Empty;

        string descriptor = ReadStringDescriptor(edid!, TagSerialString);
        if (!string.IsNullOrEmpty(descriptor)) return descriptor;

        uint numeric = (uint)(edid![12] | (edid[13] << 8) | (edid[14] << 16) | (edid[15] << 24));
        return numeric == 0 ? string.Empty : numeric.ToString();
    }

    /// <summary>
    /// Extracts the monitor-name descriptor (tag 0xFC), which vendors populate with the human-readable model string
    /// (e.g. "LG ULTRAGEAR+").
    /// Empty when absent - typical on older or cost-reduced displays.
    /// </summary>
    public static string ExtractMonitorName(byte[]? edid) =>
        !HasValidHeader(edid) ? string.Empty : ReadStringDescriptor(edid!, TagMonitorName);

    /// <summary>
    /// Decodes the 3-letter manufacturer ID at bytes 8-9
    /// - each letter is packed into 5 bits, little-endian, with 'A' = 1.
    /// E.g. 0x1E 0xA3 -> "GSM" (Goldstar/LG).
    /// Always 3 uppercase ASCII letters for valid EDIDs.
    /// </summary>
    public static string ExtractManufacturerID(byte[]? edid)
    {
        if (!HasValidHeader(edid)) return string.Empty;

        int packed = (edid![8] << 8) | edid[9];
        char c1 = (char)('A' + (((packed >> 10) & 0x1F) - 1));
        char c2 = (char)('A' + (((packed >> 5) & 0x1F) - 1));
        char c3 = (char)('A' + ((packed & 0x1F) - 1));

        return !char.IsLetter(c1) || !char.IsLetter(c2) || !char.IsLetter(c3) ? string.Empty : new string([c1, c2, c3]);
    }

    /// <summary>
    /// 16-bit vendor-assigned product code at bytes 10-11, little-endian.
    /// Used together with the manufacturer ID to uniquely key a monitor <i>model</i>
    /// (not a specific unit - the serial does that).
    /// </summary>
    public static ushort ExtractProductCode(byte[]? edid) =>
        !HasValidHeader(edid) ? (ushort)0 : (ushort)(edid![10] | (edid[11] << 8));

    private static bool HasValidHeader(byte[]? edid)
    {
        return edid is { Length: >= BlockLength } && edid[0] == 0x00
                                                  && edid[1] == 0xFF && edid[2] == 0xFF && edid[3] == 0xFF
                                                  && edid[4] == 0xFF && edid[5] == 0xFF && edid[6] == 0xFF
                                                  && edid[7] == 0x00;
    }

    /// <summary>
    /// Walks the four 18-byte descriptor blocks at offsets 54, 72, 90, 108
    /// and returns the trimmed payload of the first one whose tag byte matches.
    /// Payload runs from offset 5 for up to 13 bytes, terminated by 0x0A.
    /// </summary>
    private static string ReadStringDescriptor(byte[] edid, byte tag)
    {
        for (int i = 0; i < DescriptorCount; i++)
        {
            int offset = DescriptorBase + i * DescriptorLength;
            // Detailed-timing blocks have a non-zero pixel-clock at offset 0;
            // only monitor descriptors use the 00 00 00 prefix.
            if (edid[offset] != 0x00 || edid[offset + 1] != 0x00 || edid[offset + 2] != 0x00) continue;

            if (edid[offset + 3] != tag) continue;

            StringBuilder sb = new(13);
            for (int j = 0; j < 13; j++)
            {
                byte b = edid[offset + 5 + j];
                if (b == 0x0A) break; // 0x0A terminates the descriptor string
                if (b is >= 0x20 and <= 0x7E) sb.Append((char)b);
            }

            return sb.ToString().Trim();
        }

        return string.Empty;
    }
}
