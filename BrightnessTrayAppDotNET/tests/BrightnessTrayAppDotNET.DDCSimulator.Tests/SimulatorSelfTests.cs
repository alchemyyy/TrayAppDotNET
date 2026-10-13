using System.Diagnostics;
using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;
using Xunit;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests;

/// <summary>Checks the simulator's own building blocks against the DDC/CI and EDID formats and its virtual clock.</summary>
public sealed class SimulatorSelfTests
{
    [Fact]
    public void GetVCPRequestMatchesTheDDCCIWireFormat()
    {
        // 6E 51 82 01 10 AC is the canonical brightness query; 0x6E is the I2C address and is not part of the payload
        Assert.Equal([0x51, 0x82, 0x01, 0x10, 0xAC], DDCMessages.GetVCPRequest(VCPConstants.Brightness));
    }

    [Fact]
    public void GetVCPReplyRoundTripsThroughTheHostParser()
    {
        byte[] reply = DDCMessages.GetVCPReply(VCPConstants.Brightness, DDCMessages.ResultNoError, maximum: 100, current: 37);

        DDCOperationResult result = GPUDDCController.ParseGetReply(reply, VCPConstants.Brightness);

        Assert.True(result.Succeeded);
        Assert.Equal(expected: 37, result.Current);
        Assert.Equal(expected: 100, result.Maximum);
    }

    [Fact]
    public void ReplyFailuresMapToTheMeasuredWin32Codes()
    {
        byte[] valid = DDCMessages.GetVCPReply(VCPConstants.Brightness, DDCMessages.ResultNoError, maximum: 100, current: 37);
        byte[] corrupted = (byte[])valid.Clone();
        corrupted[^2] ^= 0x04;
        byte[] otherCode = DDCMessages.GetVCPReply(0x12, DDCMessages.ResultNoError, maximum: 100, current: 37);
        byte[] unsupported = DDCMessages.GetVCPReply(VCPConstants.Brightness, DDCMessages.ResultUnsupportedCode, maximum: 0, current: 0);

        Assert.Equal(Win32GraphicsErrors.DDCCIInvalidMessageChecksum, GPUDDCController.ParseGetReply(corrupted, VCPConstants.Brightness).ErrorCode);
        Assert.Equal(Win32GraphicsErrors.DDCCIInvalidMessageCommand, GPUDDCController.ParseGetReply(DDCMessages.NullMessage(), VCPConstants.Brightness).ErrorCode);
        Assert.Equal(Win32GraphicsErrors.DDCCIInvalidData, GPUDDCController.ParseGetReply(otherCode, VCPConstants.Brightness).ErrorCode);
        Assert.Equal(Win32GraphicsErrors.DDCCIVCPNotSupported, GPUDDCController.ParseGetReply(unsupported, VCPConstants.Brightness).ErrorCode);
    }

    [Fact]
    public void HostFramesAreRejectedByTheDisplayWhenCorrupted()
    {
        byte[] request = DDCMessages.SetVCPRequest(VCPConstants.Brightness, value: 42);
        Assert.True(DDCMessages.TryDecodeHostMessage(request, out byte[] payload));
        Assert.Equal([DDCMessages.SetVCPOpcode, VCPConstants.Brightness, 0x00, 42], payload);

        request[2] ^= 0x01;
        Assert.False(DDCMessages.TryDecodeHostMessage(request, out _));
    }

    [Fact]
    public void BuiltEDIDDecodesThroughTheAppParser()
    {
        byte[] edid = EDIDBuilder.Build("GSM", productCode: 0x7799, numericSerial: 123456, "302NTLE27568", "LG ULTRAGEAR+");

        Assert.Equal(expected: 0, edid.Aggregate(seed: 0, (sum, value) => (sum + value) & 0xFF));
        Assert.Equal("302NTLE27568", EDIDParser.ExtractSerial(edid));
        Assert.Equal("LG ULTRAGEAR+", EDIDParser.ExtractMonitorName(edid));
        Assert.Equal("GSM", EDIDParser.ExtractManufacturerID(edid));
        Assert.Equal(expected: 0x7799, EDIDParser.ExtractProductCode(edid));
    }

    [Fact]
    public void SerialLessEDIDFallsBackToTheNumericFieldThenToNothing()
    {
        Assert.Equal("4242", EDIDParser.ExtractSerial(EDIDBuilder.Build("GSM", 0x7799, numericSerial: 4242, string.Empty, "LG ULTRAGEAR+")));
        Assert.Equal(string.Empty, EDIDParser.ExtractSerial(EDIDBuilder.Build("GSM", 0x7799, numericSerial: 0, string.Empty, "LG ULTRAGEAR+")));
    }

    [Fact]
    public void VirtualDelaysCompleteWithoutWaitingInRealTime()
    {
        SimulationClock clock = new(DateTimeOffset.UnixEpoch);
        Stopwatch real = Stopwatch.StartNew();
        List<TimeSpan> completions = [];
        Lock completionsGate = new();

        for (int index = 1; index <= 5; index++)
        {
            TimeSpan delay = TimeSpan.FromMinutes(index);
            _ = Task.Delay(delay, clock).ContinueWith(_ =>
            {
                lock (completionsGate) completions.Add(clock.Elapsed);
            }, TaskScheduler.Default);
        }

        clock.RunFor(TimeSpan.FromMinutes(10));

        Assert.Equal(
            [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5)],
            completions.Order());
        Assert.True(real.Elapsed < TimeSpan.FromSeconds(5), $"ten virtual minutes took {real.Elapsed}");
    }

    [Fact]
    public async Task ParkedThreadsLetVirtualTimeAdvance()
    {
        SimulationClock clock = new(DateTimeOffset.UnixEpoch);
        TimeSpan observed = TimeSpan.Zero;
        Task worker = Task.Run(() =>
        {
            clock.Sleep(TimeSpan.FromSeconds(30));
            observed = clock.Elapsed;
        });

        clock.RunFor(TimeSpan.FromSeconds(60));

        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(30), observed);
    }
}
