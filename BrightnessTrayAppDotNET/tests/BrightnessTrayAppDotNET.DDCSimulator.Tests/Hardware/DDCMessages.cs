namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;

/// <summary>Outcome of decoding a display-to-host frame.</summary>
internal enum DisplayFrameStatus
{
    Valid,
    InvalidLength,
    InvalidChecksum
}

/// <summary>
/// VESA DDC/CI 1.1 message framing.
/// Host to display: [0x6E dest][0x51 source][0x80 | length][payload][checksum over everything from 0x6E].
/// Display to host: [0x6E source][0x80 | length][payload][checksum over virtual host address 0x50 and the rest].
/// The leading 0x6E of a host message is the I2C write address, so it appears in the checksum but not in
/// <see cref="EncodeHostMessage"/> output.
/// </summary>
internal static class DDCMessages
{
    public const byte DisplayAddress = 0x6E;
    public const byte HostSourceAddress = 0x51;
    public const byte HostChecksumAddress = 0x50;
    public const byte LengthFlag = 0x80;

    public const byte GetVCPRequestOpcode = 0x01;
    public const byte GetVCPReplyOpcode = 0x02;
    public const byte SetVCPOpcode = 0x03;
    public const byte CapabilitiesRequestOpcode = 0xF3;
    public const byte CapabilitiesReplyOpcode = 0xE3;

    public const byte ResultNoError = 0x00;
    public const byte ResultUnsupportedCode = 0x01;
    public const byte TypeSetParameter = 0x00;

    // Source, length, 8 payload bytes and checksum
    public const int GetVCPReplyFrameLength = 11;

    // Payload of a Get VCP Feature reply: opcode, result, code, type, max high, max low, current high, current low
    public const int GetVCPReplyPayloadLength = 8;

    public static byte[] GetVCPRequest(byte code) => EncodeHostMessage([GetVCPRequestOpcode, code]);

    public static byte[] SetVCPRequest(byte code, ushort value) =>
        EncodeHostMessage([SetVCPOpcode, code, (byte)(value >> 8), (byte)value]);

    public static byte[] CapabilitiesRequest(ushort offset) =>
        EncodeHostMessage([CapabilitiesRequestOpcode, (byte)(offset >> 8), (byte)offset]);

    public static byte[] GetVCPReply(byte code, byte resultCode, ushort maximum, ushort current) =>
        EncodeDisplayMessage(
        [
            GetVCPReplyOpcode,
            resultCode,
            code,
            TypeSetParameter,
            (byte)(maximum >> 8),
            (byte)maximum,
            (byte)(current >> 8),
            (byte)current
        ]);

    public static byte[] CapabilitiesReply(ushort offset, ReadOnlySpan<byte> fragment)
    {
        byte[] payload = new byte[3 + fragment.Length];
        payload[0] = CapabilitiesReplyOpcode;
        payload[1] = (byte)(offset >> 8);
        payload[2] = (byte)offset;
        fragment.CopyTo(payload.AsSpan(3));
        return EncodeDisplayMessage(payload);
    }

    /// <summary>The zero-length reply a display sends when it has nothing ready.</summary>
    public static byte[] NullMessage() => EncodeDisplayMessage([]);

    public static byte[] EncodeHostMessage(ReadOnlySpan<byte> payload)
    {
        byte[] wire = new byte[payload.Length + 3];
        wire[0] = HostSourceAddress;
        wire[1] = (byte)(LengthFlag | payload.Length);
        payload.CopyTo(wire.AsSpan(2));

        byte checksum = DisplayAddress;
        for (int index = 0; index < wire.Length - 1; index++) checksum ^= wire[index];
        wire[^1] = checksum;
        return wire;
    }

    public static byte[] EncodeDisplayMessage(ReadOnlySpan<byte> payload)
    {
        byte[] wire = new byte[payload.Length + 3];
        wire[0] = DisplayAddress;
        wire[1] = (byte)(LengthFlag | payload.Length);
        payload.CopyTo(wire.AsSpan(2));

        byte checksum = HostChecksumAddress;
        for (int index = 0; index < wire.Length - 1; index++) checksum ^= wire[index];
        wire[^1] = checksum;
        return wire;
    }

    /// <summary>Display side: validates a host frame (without its leading write address) and returns its payload.</summary>
    public static bool TryDecodeHostMessage(ReadOnlySpan<byte> wire, out byte[] payload)
    {
        payload = [];
        if (wire.Length < 3 || wire[0] != HostSourceAddress || (wire[1] & LengthFlag) == 0) return false;

        int length = wire[1] & ~LengthFlag;
        if (wire.Length != length + 3) return false;

        byte checksum = DisplayAddress;
        for (int index = 0; index < wire.Length - 1; index++) checksum ^= wire[index];
        if (checksum != wire[^1]) return false;

        payload = wire.Slice(2, length).ToArray();
        return true;
    }

    /// <summary>Host side: validates a display frame and returns its payload.</summary>
    public static DisplayFrameStatus DecodeDisplayMessage(ReadOnlySpan<byte> wire, out byte[] payload)
    {
        payload = [];
        if (wire.Length < 3 || (wire[1] & LengthFlag) == 0) return DisplayFrameStatus.InvalidLength;

        int length = wire[1] & ~LengthFlag;
        if (wire.Length < length + 3) return DisplayFrameStatus.InvalidLength;

        byte checksum = HostChecksumAddress;
        for (int index = 0; index < length + 2; index++) checksum ^= wire[index];
        if (checksum != wire[length + 2]) return DisplayFrameStatus.InvalidChecksum;

        payload = wire.Slice(2, length).ToArray();
        return DisplayFrameStatus.Valid;
    }
}
