using System.Buffers.Binary;
using TrayAppDotNETCommon.Interop;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class RawInputPacketTests
{
    // x64: RAWINPUTHEADER is 24 bytes, RAWHID's size and count follow, then the report bytes
    private const int HeaderSize = 24;
    private const int HidDataOffset = HeaderSize + 8;

    [Fact]
    public void AShortHidPacketDecodesWithinItsOwnLength()
    {
        // A 12-byte report gives a 44-byte packet, smaller than the 48-byte RAWINPUT union
        byte[] packet = HidPacket(reportSize: 12, reportCount: 1);
        for (int index = 0; index < 12; index++) packet[HidDataOffset + index] = (byte)(index + 1);

        InputHelper.RawInputPacket decoded = InputHelper.RawInputPacket.Decode(packet);

        Assert.Equal(expected: 44, decoded.Length);
        Assert.Equal(expected: 1, decoded.HidReportCount);
        Assert.Equal(expected: 12, decoded.HidReportSize);
        Assert.Equal(packet.AsSpan(HidDataOffset, length: 12).ToArray(), decoded.HidReport(packet, index: 0).ToArray());
        Assert.True(decoded.HidReport(packet, index: 1).IsEmpty);
    }

    [Fact]
    public void ReportCountIsClampedToTheBytesThePacketHolds()
    {
        byte[] packet = HidPacket(reportSize: 12, reportCount: 1);
        // The header claims three reports, but only one fits
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 4), value: 3);

        InputHelper.RawInputPacket decoded = InputHelper.RawInputPacket.Decode(packet);

        Assert.Equal(expected: 1, decoded.HidReportCount);
    }

    [Fact]
    public void ATruncatedMousePacketDoesNotReadPastItsEnd()
    {
        byte[] packet = new byte[HeaderSize + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, User32.RIM_TYPEMOUSE);

        InputHelper.RawInputPacket decoded = InputHelper.RawInputPacket.Decode(packet);

        Assert.True(decoded.IsMouse);
        Assert.Equal(expected: 0, decoded.WheelDelta);
        Assert.Equal(expected: 0, decoded.MouseButtonFlags);
    }

    private static byte[] HidPacket(int reportSize, int reportCount)
    {
        byte[] packet = new byte[HidDataOffset + reportSize * reportCount];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, User32.RIM_TYPEHID);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(start: 4), (uint)packet.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize), (uint)reportSize);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(HeaderSize + 4), (uint)reportCount);
        return packet;
    }
}
