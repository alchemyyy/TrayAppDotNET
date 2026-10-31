using System.Diagnostics;
using System.IO.Pipes;
using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;
using Xunit;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests;

/// <summary>Checks the simulator's own building blocks against the DDC/CI and EDID formats and its virtual clock.</summary>
public sealed class SimulatorSelfTests
{
    /// <summary>
    /// A thread that is computing keeps virtual time still even when the CPU heuristic would call it blocked: the
    /// scheduler-state check sees it running or ready. This also proves the walk finds this process's threads.
    /// </summary>
    [Fact]
    public void ComputingThreadCountsAsInFlight()
    {
        using ManualResetEventSlim stop = new();
        using ManualResetEventSlim started = new();
        Thread spinner = new(() =>
        {
            started.Set();
            while (!stop.IsSet) Thread.SpinWait(iterations: 100);
        }) { IsBackground = true, Name = "RunnableProbeSpinner" };
        spinner.Start();
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SimulationClock.AnyOtherThreadInFlight());
        }
        finally
        {
            stop.Set();
            spinner.Join();
        }
    }

    /// <summary>
    /// UI-thread work that sleeps in real time, as a retry delay does, holds virtual time: the far-off timer fires only
    /// after the work is done.
    /// </summary>
    [Fact]
    public async Task RealTimeSleepOnTheUIThreadHoldsVirtualTime()
    {
        SimulationClock clock = new(DateTimeOffset.UnixEpoch);
        using SimulationDispatcher dispatcher = new(clock, static _ => { });
        clock.Dispatcher = dispatcher;
        TaskCompletionSource<TimeSpan> slept = new(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Post(() =>
        {
            Thread.Sleep(50);
            slept.SetResult(clock.Elapsed);
        });
        TimeSpan firedAt = TimeSpan.MinValue;
        clock.ScheduleAt(TimeSpan.FromSeconds(10).Ticks, () => firedAt = clock.Elapsed);

        clock.RunUntil(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.Zero, await slept.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(10), firedAt);
    }

    /// <summary>
    /// UI-thread work blocked in synchronous I/O holds virtual time until the I/O completes. On a CI runner's slow disk
    /// the app's displays.json flush at startup used to let every scenario jump to its first script step before the
    /// app had finished starting; a pipe read whose data arrives in real time is the same kernel wait, made slow on
    /// purpose.
    /// </summary>
    [Fact]
    public async Task SynchronousIOOnTheUIThreadHoldsVirtualTime()
    {
        SimulationClock clock = new(DateTimeOffset.UnixEpoch);
        using SimulationDispatcher dispatcher = new(clock, static _ => { });
        clock.Dispatcher = dispatcher;
        using AnonymousPipeServerStream writer = new(PipeDirection.Out);
        using AnonymousPipeClientStream reader = new(PipeDirection.In, writer.ClientSafePipeHandle);
        using Timer reply = new(_ => writer.WriteByte(1), state: null, dueTime: 50, period: Timeout.Infinite);
        TaskCompletionSource<TimeSpan> read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.Post(() =>
        {
            _ = reader.ReadByte();
            read.SetResult(clock.Elapsed);
        });
        TimeSpan firedAt = TimeSpan.MinValue;
        clock.ScheduleAt(TimeSpan.FromSeconds(10).Ticks, () => firedAt = clock.Elapsed);

        clock.RunUntil(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.Zero, await read.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(10), firedAt);
    }

    /// <summary>
    /// The other side of the rule: UI-thread work blocked on a semaphore that a thread parked on virtual time releases
    /// waits on the simulation, so time must move for either to finish.
    /// </summary>
    [Fact]
    public async Task UIThreadWaitingOnAParkedThreadLetsVirtualTimeAdvance()
    {
        SimulationClock clock = new(DateTimeOffset.UnixEpoch);
        using SimulationDispatcher dispatcher = new(clock, static _ => { });
        clock.Dispatcher = dispatcher;
        using SemaphoreSlim released = new(initialCount: 0);
        Task holder = Task.Run(() =>
        {
            clock.Sleep(TimeSpan.FromSeconds(1));
            released.Release();
        });
        TimeSpan acquiredAt = TimeSpan.MinValue;
        dispatcher.Post(() =>
        {
            released.Wait();
            acquiredAt = clock.Elapsed;
        });

        clock.RunUntil(TimeSpan.FromSeconds(2));

        await holder.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(1), acquiredAt);
    }

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
