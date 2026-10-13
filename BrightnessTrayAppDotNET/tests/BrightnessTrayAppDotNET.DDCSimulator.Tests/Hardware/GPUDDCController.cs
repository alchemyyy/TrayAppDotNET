using System.Text;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;

/// <summary>One GPU output connector: its link and the monitor cabled to it, if any.</summary>
internal sealed class GPUConnector(int index, LinkProfile link, int connectorUID)
{
    public int Index { get; } = index;
    public LinkProfile Link { get; } = link;
    public int ConnectorUID { get; } = connectorUID;

    /// <summary>Monitor whose cable is plugged into this connector. Written only by the host display stack.</summary>
    public SimulatedMonitor? Monitor { get; set; }
}

internal enum DDCOperationKind
{
    GetVCP,
    SetVCP,
    Capabilities,
    HandleOperation
}

/// <summary>Result of one Monitor Configuration API call, as the Win32 error the API would report.</summary>
internal readonly record struct DDCOperationResult(int ErrorCode, ushort Current, ushort Maximum, string Capabilities)
{
    public bool Succeeded => ErrorCode == 0;

    public static DDCOperationResult Success => new(ErrorCode: 0, Current: 0, Maximum: 0, Capabilities: string.Empty);

    public static DDCOperationResult Failure(int errorCode) =>
        new(errorCode, Current: 0, Maximum: 0, Capabilities: string.Empty);
}

/// <summary>One queued API call. <see cref="Completed"/> runs on a pool thread at the call's virtual completion time.</summary>
internal sealed class DDCOperation
{
    public required DDCOperationKind Kind { get; init; }
    public required GPUConnector? Connector { get; init; }
    public byte Code { get; init; }
    public ushort Value { get; init; }
    public required string Label { get; init; }
    public required Action<DDCOperationResult> Completed { get; init; }
}

/// <summary>Win32 errors the Monitor Configuration API surfaces for DDC/CI failures (winerror.h).</summary>
internal static class Win32GraphicsErrors
{
    public const int InvalidHandle = 6;
    public const int I2CErrorTransmittingData = unchecked((int)0xC0262582);
    public const int I2CErrorReceivingData = unchecked((int)0xC0262583);
    public const int DDCCIVCPNotSupported = unchecked((int)0xC0262584);
    public const int DDCCIInvalidData = unchecked((int)0xC0262585);
    public const int MCAInvalidCapabilitiesString = unchecked((int)0xC0262587);
    public const int DDCCIInvalidMessageCommand = unchecked((int)0xC0262589);
    public const int DDCCIInvalidMessageLength = unchecked((int)0xC026258A);
    public const int DDCCIInvalidMessageChecksum = unchecked((int)0xC026258B);
    public const int DDCCICurrentValueGreaterThanMaximum = unchecked((int)0xC02625D8);

    public static string Name(int errorCode) =>
        errorCode switch
        {
            0 => "OK",
            InvalidHandle => "ERROR_INVALID_HANDLE",
            I2CErrorTransmittingData => "I2C_ERROR_TRANSMITTING_DATA",
            I2CErrorReceivingData => "I2C_ERROR_RECEIVING_DATA",
            DDCCIVCPNotSupported => "DDCCI_VCP_NOT_SUPPORTED",
            DDCCIInvalidData => "DDCCI_INVALID_DATA",
            MCAInvalidCapabilitiesString => "MCA_INVALID_CAPABILITIES_STRING",
            DDCCIInvalidMessageCommand => "DDCCI_INVALID_MESSAGE_COMMAND",
            DDCCIInvalidMessageLength => "DDCCI_INVALID_MESSAGE_LENGTH",
            DDCCIInvalidMessageChecksum => "DDCCI_INVALID_MESSAGE_CHECKSUM",
            DDCCICurrentValueGreaterThanMaximum => "DDCCI_CURRENT_VALUE_GREATER_THAN_MAXIMUM",
            _ => $"0x{unchecked((uint)errorCode):X8}"
        };
}

/// <summary>
/// The single DDC/CI path every monitor shares. win32k's CMonitorAPI holds one global OPM mutex across each call,
/// including the 50 ms request/reply wait, so a slow, garbling or absent panel delays every other panel's traffic.
/// Calls queue FIFO and each runs as a timed sequence of I2C phases against the monitor on its connector.
/// </summary>
internal sealed class GPUDDCController(SimulationClock clock, SimulationTrace trace, int seed)
{
    private readonly Lock _gate = new();
    private readonly Lock _randomGate = new();
    private readonly Queue<DDCOperation> _queue = new();
    private readonly Random _random = new(seed);
    private bool _busy;
    private long _operations;
    private int _maximumQueueDepth;

    /// <summary>Extra per-byte bit error probability on every link, for marginal-cable fault injection.</summary>
    public double ExtraBitErrorRate { get; set; }

    public long Operations => Interlocked.Read(ref _operations);

    public int MaximumQueueDepth
    {
        get
        {
            lock (_gate) return _maximumQueueDepth;
        }
    }

    public void Submit(DDCOperation operation)
    {
        bool start;
        lock (_gate)
        {
            _queue.Enqueue(operation);
            _maximumQueueDepth = Math.Max(_maximumQueueDepth, _queue.Count);
            start = !_busy;
            if (start) _busy = true;
        }

        if (start) StartNext();
    }

    private void StartNext()
    {
        DDCOperation? next;
        lock (_gate)
        {
            if (!_queue.TryDequeue(out next))
            {
                _busy = false;
                return;
            }
        }

        Execute(next);
    }

    private void Execute(DDCOperation operation)
    {
        if (operation.Kind == DDCOperationKind.HandleOperation)
        {
            Finish(TimeSpan.FromMicroseconds(DDCTiming.HandleOperationMicroseconds), operation, DDCOperationResult.Success);
            return;
        }

        GPUConnector connector = operation.Connector!;
        SimulatedMonitor? monitor = connector.Monitor;
        LinkProfile link = connector.Link;
        if (monitor == null)
        {
            Finish(NAKLatency(link), operation, DDCOperationResult.Failure(Win32GraphicsErrors.I2CErrorTransmittingData));
            return;
        }

        if (!monitor.AcknowledgesAddress(clock.ElapsedTicks))
        {
            if (link.GarblesWhileSinkSilent && monitor.IsPanelOff)
            {
                // The adapter's own I2C bridge accepts the transfer; the sleeping panel never sees it
                if (operation.Kind == DDCOperationKind.SetVCP)
                {
                    TimeSpan accepted = Transfer(link, byteCount: 7) + TimeSpan.FromMilliseconds(DDCTiming.WindowsSetPostDelayMs);
                    Finish(accepted, operation, DDCOperationResult.Success);
                    return;
                }

                Finish(GarbleLatency(link), operation, DDCOperationResult.Failure(Win32GraphicsErrors.DDCCIInvalidMessageCommand));
                return;
            }

            Finish(NAKLatency(link), operation, DDCOperationResult.Failure(Win32GraphicsErrors.I2CErrorTransmittingData));
            return;
        }

        switch (operation.Kind)
        {
            case DDCOperationKind.SetVCP:
                SendSet(operation, connector, monitor);
                break;
            case DDCOperationKind.GetVCP:
                SendGet(operation, connector, monitor);
                break;
            case DDCOperationKind.Capabilities:
                RequestCapabilitiesFragment(operation, connector, monitor, new StringBuilder(), offset: 0);
                break;
        }
    }

    private void SendSet(DDCOperation operation, GPUConnector connector, SimulatedMonitor monitor)
    {
        byte[] request = DDCMessages.SetVCPRequest(operation.Code, operation.Value);
        clock.Schedule(Transfer(connector.Link, request.Length + 1), () =>
        {
            monitor.ReceiveHostMessage(clock.ElapsedTicks, AddNoise(request, connector.Link));

            // A write has no reply; Windows reports success once the post-SET wait elapses
            Finish(TimeSpan.FromMilliseconds(DDCTiming.WindowsSetPostDelayMs), operation, DDCOperationResult.Success);
        });
    }

    private void SendGet(DDCOperation operation, GPUConnector connector, SimulatedMonitor monitor)
    {
        byte[] request = DDCMessages.GetVCPRequest(operation.Code);
        clock.Schedule(Transfer(connector.Link, request.Length + 1), () =>
        {
            monitor.ReceiveHostMessage(clock.ElapsedTicks, AddNoise(request, connector.Link));
            clock.Schedule(
                TimeSpan.FromMilliseconds(DDCTiming.WindowsReplyDelayMs),
                () => ReadGetReply(operation, connector, monitor));
        });
    }

    private void ReadGetReply(DDCOperation operation, GPUConnector connector, SimulatedMonitor monitor)
    {
        LinkProfile link = connector.Link;
        if (!monitor.AcknowledgesAddress(clock.ElapsedTicks))
        {
            if (link.GarblesWhileSinkSilent && monitor.IsPanelOff)
            {
                Finish(GarbleLatency(link), operation, DDCOperationResult.Failure(Win32GraphicsErrors.DDCCIInvalidMessageCommand));
                return;
            }

            Finish(NAKLatency(link), operation, DDCOperationResult.Failure(Win32GraphicsErrors.I2CErrorReceivingData));
            return;
        }

        byte[] reply = AddNoise(monitor.ReadReply(clock.ElapsedTicks), link);
        DDCOperationResult result = ParseGetReply(reply, operation.Code);
        TimeSpan replyTime = Transfer(link, reply.Length + 1);

        // Adapters turn a garbled exchange into a slow one: defers and retries before the junk arrives
        if (!result.Succeeded && link.GarblesWhileSinkSilent) replyTime += GarbleLatency(link);
        Finish(replyTime, operation, result);
    }

    private void RequestCapabilitiesFragment(
        DDCOperation operation,
        GPUConnector connector,
        SimulatedMonitor monitor,
        StringBuilder collected,
        ushort offset)
    {
        byte[] request = DDCMessages.CapabilitiesRequest(offset);
        clock.Schedule(Transfer(connector.Link, request.Length + 1), () =>
        {
            monitor.ReceiveHostMessage(clock.ElapsedTicks, AddNoise(request, connector.Link));
            clock.Schedule(TimeSpan.FromMilliseconds(DDCTiming.SpecCapabilitiesReplyDelayMs), () =>
            {
                if (!monitor.AcknowledgesAddress(clock.ElapsedTicks))
                {
                    Finish(
                        NAKLatency(connector.Link),
                        operation,
                        DDCOperationResult.Failure(Win32GraphicsErrors.I2CErrorReceivingData));
                    return;
                }

                byte[] reply = AddNoise(monitor.ReadReply(clock.ElapsedTicks), connector.Link);
                TimeSpan replyTime = Transfer(connector.Link, reply.Length + 1);
                DisplayFrameStatus status = DDCMessages.DecodeDisplayMessage(reply, out byte[] payload);
                int error = status switch
                {
                    DisplayFrameStatus.InvalidLength => Win32GraphicsErrors.DDCCIInvalidMessageLength,
                    DisplayFrameStatus.InvalidChecksum => Win32GraphicsErrors.DDCCIInvalidMessageChecksum,
                    _ when payload.Length < 3 || payload[0] != DDCMessages.CapabilitiesReplyOpcode =>
                        Win32GraphicsErrors.DDCCIInvalidMessageCommand,
                    _ when ((payload[1] << 8) | payload[2]) != offset => Win32GraphicsErrors.DDCCIInvalidData,
                    _ => 0
                };
                if (error != 0)
                {
                    Finish(replyTime, operation, DDCOperationResult.Failure(error));
                    return;
                }

                if (payload.Length == 3)
                {
                    Finish(
                        replyTime,
                        operation,
                        new DDCOperationResult(ErrorCode: 0, Current: 0, Maximum: 0, collected.ToString()));
                    return;
                }

                collected.Append(Encoding.ASCII.GetString(payload, index: 3, payload.Length - 3));
                ushort nextOffset = (ushort)(offset + payload.Length - 3);
                clock.Schedule(
                    replyTime,
                    () => RequestCapabilitiesFragment(operation, connector, monitor, collected, nextOffset));
            });
        });
    }

    /// <summary>Validates a Get VCP Feature reply the way the kernel's DDC/CI layer reports each failure class.</summary>
    internal static DDCOperationResult ParseGetReply(byte[] reply, byte requestedCode)
    {
        DisplayFrameStatus status = DDCMessages.DecodeDisplayMessage(reply, out byte[] payload);
        switch (status)
        {
            case DisplayFrameStatus.InvalidLength:
                return DDCOperationResult.Failure(Win32GraphicsErrors.DDCCIInvalidMessageLength);
            case DisplayFrameStatus.InvalidChecksum:
                return DDCOperationResult.Failure(Win32GraphicsErrors.DDCCIInvalidMessageChecksum);
        }

        if (payload.Length == 0 || payload[0] != DDCMessages.GetVCPReplyOpcode)
            return DDCOperationResult.Failure(Win32GraphicsErrors.DDCCIInvalidMessageCommand);
        if (payload.Length != DDCMessages.GetVCPReplyPayloadLength)
            return DDCOperationResult.Failure(Win32GraphicsErrors.DDCCIInvalidMessageLength);
        if (payload[1] == DDCMessages.ResultUnsupportedCode)
            return DDCOperationResult.Failure(Win32GraphicsErrors.DDCCIVCPNotSupported);
        if (payload[1] != DDCMessages.ResultNoError || payload[2] != requestedCode)
            return DDCOperationResult.Failure(Win32GraphicsErrors.DDCCIInvalidData);

        ushort maximum = (ushort)((payload[4] << 8) | payload[5]);
        ushort current = (ushort)((payload[6] << 8) | payload[7]);
        if (maximum > 0 && current > maximum)
            return DDCOperationResult.Failure(Win32GraphicsErrors.DDCCICurrentValueGreaterThanMaximum);

        return new DDCOperationResult(ErrorCode: 0, current, maximum, Capabilities: string.Empty);
    }

    private void Finish(TimeSpan delay, DDCOperation operation, DDCOperationResult result)
    {
        clock.Schedule(delay, () =>
        {
            Interlocked.Increment(ref _operations);
            if (!result.Succeeded)
            {
                trace.Add(
                    "bus",
                    $"{operation.Label} -> {Win32GraphicsErrors.Name(result.ErrorCode)}");
            }

            try
            {
                operation.Completed(result);
            }
            catch (Exception exception)
            {
                trace.Add("bus", $"{operation.Label} completion threw: {exception.Message}");
            }

            StartNext();
        });
    }

    private byte[] AddNoise(byte[] wire, LinkProfile link)
    {
        double rate = link.BitErrorRate + ExtraBitErrorRate;
        if (rate <= 0) return wire;

        byte[] noisy = (byte[])wire.Clone();
        lock (_randomGate)
        {
            for (int index = 0; index < noisy.Length; index++)
            {
                if (_random.NextDouble() < rate) noisy[index] ^= (byte)(1 << _random.Next(maxValue: 8));
            }
        }

        return noisy;
    }

    private static TimeSpan Transfer(LinkProfile link, int byteCount) =>
        DDCTiming.I2CTransferTime(byteCount) * link.TransferScale;

    private TimeSpan NAKLatency(LinkProfile link)
    {
        lock (_randomGate)
            return TimeSpan.FromMilliseconds(_random.Next(link.NAKLatencyMinimumMs, link.NAKLatencyMaximumMs + 1));
    }

    private TimeSpan GarbleLatency(LinkProfile link)
    {
        lock (_randomGate)
            return TimeSpan.FromMilliseconds(_random.Next(link.GarbleLatencyMinimumMs, link.GarbleLatencyMaximumMs + 1));
    }
}
