namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;

/// <summary>
/// Timing baselines for one DDC/CI exchange, from the VESA DDC/CI 1.1 standard, the I2C standard-mode bus it rides on,
/// and the Windows kernel implementation behind dxva2.
/// </summary>
internal static class DDCTiming
{
    // I2C standard mode (DDC2Bi) clocks at 100 kHz; a byte plus its ACK bit is 9 clocks
    public const double I2CByteMicroseconds = 90.0;

    // START, repeated START and STOP setup plus bus free time at 100 kHz (tSU;STA 4.7 us, tSU;STO 4.0 us, tBUF 4.7 us)
    public const double I2CFramingMicroseconds = 15.0;

    // DDC/CI 1.1: the host waits at least 40 ms after Get VCP Feature before reading the reply
    public const int SpecGetReplyDelayMs = 40;

    // DDC/CI 1.1: the host waits at least 50 ms after Set VCP Feature before sending the next message
    public const int SpecSetPostDelayMs = 50;

    // DDC/CI 1.1: the host waits at least 50 ms between a capabilities request fragment and reading its reply
    public const int SpecCapabilitiesReplyDelayMs = 50;

    // DDC/CI 1.1: capabilities replies carry at most 32 data bytes per fragment
    public const int CapabilitiesFragmentLength = 32;

    // win32k CMonitorAPI waits -500000 x 100 ns = 50 ms inside every request/reply exchange (disassembly, build 26200)
    public const int WindowsReplyDelayMs = 50;

    // MSDN: SetMonitorBrightness and SetVCPFeature take about 50 ms to return, which covers the spec's post-SET wait
    public const int WindowsSetPostDelayMs = SpecSetPostDelayMs;

    // win32k serializes DDC/CI calls behind a global OPM mutex; opening or closing physical monitor handles also takes it
    public const double HandleOperationMicroseconds = 300.0;

    /// <summary>Wire time to move <paramref name="byteCount"/> bytes, including the address byte, across a plain I2C bus.</summary>
    public static TimeSpan I2CTransferTime(int byteCount) =>
        TimeSpan.FromMicroseconds(I2CFramingMicroseconds + byteCount * I2CByteMicroseconds);
}
