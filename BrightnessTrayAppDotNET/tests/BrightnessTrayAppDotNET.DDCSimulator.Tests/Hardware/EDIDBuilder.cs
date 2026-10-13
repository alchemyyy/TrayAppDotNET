using System.Text;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;

/// <summary>
/// Builds valid EDID 1.4 base blocks so the app's own <c>EDIDParser</c> decodes simulated monitors exactly as it
/// decodes real ones.
/// </summary>
internal static class EDIDBuilder
{
    private const int BlockLength = 128;
    private const int DescriptorBase = 54;
    private const int DescriptorLength = 18;
    private const byte TagSerialString = 0xFF;
    private const byte TagMonitorName = 0xFC;
    private const byte TagRangeLimits = 0xFD;

    /// <summary>
    /// Creates a base block. An empty <paramref name="serialDescriptor"/> omits the 0xFF descriptor and
    /// <paramref name="numericSerial"/> zero leaves bytes 12-15 empty, which together make a serial-less EDID.
    /// </summary>
    public static byte[] Build(
        string manufacturerID,
        ushort productCode,
        uint numericSerial,
        string serialDescriptor,
        string monitorName)
    {
        byte[] edid = new byte[BlockLength];
        edid[1] = edid[2] = edid[3] = edid[4] = edid[5] = edid[6] = 0xFF;

        // Manufacturer: three 5-bit letters, 'A' = 1, stored big-endian
        int packed = ((manufacturerID[0] - 'A' + 1) << 10)
                     | ((manufacturerID[1] - 'A' + 1) << 5)
                     | (manufacturerID[2] - 'A' + 1);
        edid[8] = (byte)(packed >> 8);
        edid[9] = (byte)packed;
        edid[10] = (byte)productCode;
        edid[11] = (byte)(productCode >> 8);
        edid[12] = (byte)numericSerial;
        edid[13] = (byte)(numericSerial >> 8);
        edid[14] = (byte)(numericSerial >> 16);
        edid[15] = (byte)(numericSerial >> 24);
        edid[16] = 20;
        edid[17] = 2023 - 1990;
        edid[18] = 1;
        edid[19] = 4;

        // Digital input, 10 bits per color, DisplayPort interface; 70 x 39 cm; gamma 2.2; RGB, preferred timing
        edid[20] = 0xB5;
        edid[21] = 70;
        edid[22] = 39;
        edid[23] = 120;
        edid[24] = 0x3A;
        for (int index = 38; index < 54; index++) edid[index] = 0x01;

        // Detailed timing descriptor: 3840x2160 at 60 Hz (533.25 MHz pixel clock)
        WritePreferredTiming(edid.AsSpan(DescriptorBase, DescriptorLength));

        int slot = 1;
        if (!string.IsNullOrEmpty(serialDescriptor))
            WriteStringDescriptor(edid, slot++, TagSerialString, serialDescriptor);
        WriteStringDescriptor(edid, slot++, TagMonitorName, monitorName);
        if (slot < 4) WriteRangeLimits(edid.AsSpan(DescriptorBase + slot * DescriptorLength, DescriptorLength));

        byte sum = 0;
        for (int index = 0; index < BlockLength - 1; index++) sum += edid[index];
        edid[BlockLength - 1] = (byte)(256 - sum);
        return edid;
    }

    private static void WritePreferredTiming(Span<byte> descriptor)
    {
        // Pixel clock in 10 kHz units, little-endian
        const int PixelClock = 53325;
        descriptor[0] = PixelClock & 0xFF;
        descriptor[1] = PixelClock >> 8;
        descriptor[2] = 0x00; // 3840 active, low byte
        descriptor[3] = 0xA0; // 160 blanking
        descriptor[4] = 0xF0; // high nibbles
        descriptor[5] = 0x70; // 2160 active, low byte
        descriptor[6] = 0x3E; // 62 blanking
        descriptor[7] = 0x80;
        descriptor[8] = 0x30;
        descriptor[9] = 0x20;
        descriptor[10] = 0x35;
        descriptor[11] = 0x00;
        descriptor[12] = 0xBC;
        descriptor[13] = 0x86;
        descriptor[14] = 0x21;
        descriptor[17] = 0x1A;
    }

    private static void WriteStringDescriptor(byte[] edid, int slot, byte tag, string text)
    {
        int offset = DescriptorBase + slot * DescriptorLength;
        edid[offset + 3] = tag;
        byte[] ascii = Encoding.ASCII.GetBytes(text.Length > 13 ? text[..13] : text);
        for (int index = 0; index < 13; index++)
        {
            edid[offset + 5 + index] = index < ascii.Length
                ? ascii[index]
                : index == ascii.Length
                    ? (byte)0x0A
                    : (byte)0x20;
        }
    }

    private static void WriteRangeLimits(Span<byte> descriptor)
    {
        // Vertical 48-144 Hz, horizontal 30-255 kHz, pixel clock up to 600 MHz in 10 MHz units
        descriptor[3] = TagRangeLimits;
        descriptor[5] = 48;
        descriptor[6] = 144;
        descriptor[7] = 30;
        descriptor[8] = 255;
        descriptor[9] = 60;
        descriptor[10] = 0x0A;
        for (int index = 11; index < DescriptorLength; index++) descriptor[index] = 0x20;
    }
}
