using BrightnessTrayAppDotNET.DDCCI;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;

/// <summary>Panel power as the monitor itself experiences it.</summary>
internal enum PanelPowerState
{
    On,
    Standby,
    SoftOff,
    HardOff
}

/// <summary>
/// One physical monitor: its EDID identity, power state machine, VCP registers and DDC/CI reply engine.
/// The bus calls into it at the virtual time of each I2C phase, so what a request sees depends on where the panel is
/// in its boot, wake or power-off sequence at that instant.
/// </summary>
internal sealed class SimulatedMonitor
{
    private const ushort PowerModeOn = 1;
    private const ushort PowerModeStandby = 2;
    private const ushort PowerModeOff = 4;
    private const ushort PowerModeHardOff = 5;
    private const ushort PowerModeMaximum = 5;

    private static readonly HashSet<byte> SupportedCodes =
        [VCPConstants.Brightness, VCPConstants.Contrast, VCPConstants.InputSource, VCPConstants.PowerMode, 0xDF];

    private readonly Lock _gate = new();
    private readonly SimulationClock _clock;
    private readonly SimulationTrace _trace;
    private readonly Random _random;
    private readonly Dictionary<byte, ushort> _otherRegisters = new() { [VCPConstants.Contrast] = 70, [0xDF] = 0x0201 };

    private PanelPowerState _power = PanelPowerState.On;
    private bool _hasVideoSignal = true;
    private long _mcuReadyAtTicks;
    private long _flakyUntilTicks;
    private double _flakyProbability;
    private long _hardOffGarbleUntilTicks;
    private long _healthySinceTicks;
    private long _pendingTransitionVersion;

    private ushort _brightness;
    private ushort _pendingBrightness;
    private long _pendingBrightnessAtTicks = long.MaxValue;
    private ushort _committedBrightness;
    private long _brightnessChangedAtTicks;
    private ushort _powerMode = PowerModeOn;
    private ushort _inputSource = 0x0F;

    private long _busyUntilTicks;
    private byte[]? _preparedReply;
    private long _preparedReplyReadyAtTicks;
    private byte[]? _previousReply;
    private bool _wedged;
    private long _wedgedSinceTicks = -1;
    private long _wakeWindowStartTicks = long.MinValue;
    private string _capabilities = string.Empty;

    public SimulatedMonitor(
        string physicalID,
        MonitorProfile profile,
        string serialDescriptor,
        uint numericSerial,
        SimulationClock clock,
        SimulationTrace trace,
        int seed,
        ushort initialBrightness)
    {
        PhysicalID = physicalID;
        Profile = profile;
        SerialDescriptor = serialDescriptor;
        NumericSerial = numericSerial;
        _clock = clock;
        _trace = trace;
        _random = new Random(seed);
        EDID = EDIDBuilder.Build(profile.ManufacturerID, profile.ProductCode, numericSerial, serialDescriptor, profile.ModelName);
        _brightness = Math.Min(initialBrightness, profile.BrightnessMaximum);
        _committedBrightness = _brightness;
        _capabilities = BuildCapabilities();
    }

    public string PhysicalID { get; }
    public MonitorProfile Profile { get; }
    public string SerialDescriptor { get; }
    public uint NumericSerial { get; }
    public byte[] EDID { get; }

    /// <summary>Serial the app's EDIDParser derives: the 0xFF descriptor, else the numeric field, else empty.</summary>
    public string EDIDSerial => EDIDParser.ExtractSerial(EDID);

    /// <summary>Fired after any power transition, outside the monitor lock, so the host can update hot-plug detect.</summary>
    public event Action<SimulatedMonitor>? PowerStateChanged;

    public PanelPowerState PowerState
    {
        get
        {
            lock (_gate) return _power;
        }
    }

    public long SetsReceived { get; private set; }
    public long GetsReceived { get; private set; }

    public string Describe() =>
        $"{PhysicalID} '{Profile.ModelName}' serial='{EDIDSerial}' power={PowerState} brightness={VisibleBrightnessPercent()}";

    /// <summary>Brightness the panel is showing right now, as a 0-100 percentage of its maximum.</summary>
    public int VisibleBrightnessPercent()
    {
        lock (_gate)
        {
            ApplyPendingLocked(_clock.ElapsedTicks);
            return (int)Math.Round(_brightness * 100.0 / Math.Max((ushort)1, Profile.BrightnessMaximum));
        }
    }

    /// <summary>
    /// True when a DDC read would succeed reliably: powered on, MCU booted, outside the post-boot garble window and
    /// with an intact reply pipeline.
    /// </summary>
    public bool IsDDCHealthy(long nowTicks)
    {
        lock (_gate) return IsDDCHealthyLocked(nowTicks);
    }

    /// <summary>Virtual time since which <see cref="IsDDCHealthy"/> has held continuously, or -1.</summary>
    public long HealthySinceTicks(long nowTicks)
    {
        lock (_gate) return IsDDCHealthyLocked(nowTicks) ? Math.Max(_healthySinceTicks, _flakyUntilTicks) : -1;
    }

    public bool IsWedged
    {
        get
        {
            lock (_gate) return _wedged;
        }
    }

    /// <summary>Virtual time the reply pipeline last wedged, or -1 while it is intact.</summary>
    public long WedgedSinceTicks
    {
        get
        {
            lock (_gate) return _wedged ? _wedgedSinceTicks : -1;
        }
    }

    // Scenario controls

    public void PressPowerButton()
    {
        long now = _clock.ElapsedTicks;
        lock (_gate)
        {
            if (_power is PanelPowerState.On or PanelPowerState.Standby)
                EnterOffLocked(now, PanelPowerState.SoftOff, PowerModeOff);
            else
                EnterOnLocked(now, ColdBootDelayMs(), powerCycled: true, coldBoot: true);
        }

        _trace.Add("monitor", $"{PhysicalID} power button -> {PowerState}");
        RaisePowerStateChanged();
    }

    /// <summary>The GPU starts or stops driving video to this panel (display off, sleep, mode change blanking).</summary>
    public void SetVideoSignal(bool present)
    {
        long now = _clock.ElapsedTicks;
        bool changed = false;
        lock (_gate)
        {
            if (_hasVideoSignal == present) return;

            _hasVideoSignal = present;
            if (present)
            {
                // Signal return wakes a sleeping panel, including one put to sleep by a DDC command
                if (_power == PanelPowerState.Standby)
                {
                    EnterOnLocked(now, StandbyWakeDelayMs(), powerCycled: false, coldBoot: false);
                    changed = true;
                }
            }
            else if (_power == PanelPowerState.On)
            {
                long version = ++_pendingTransitionVersion;
                int delayMs = _random.Next(Profile.SignalLossStandbyMinimumMs, Profile.SignalLossStandbyMaximumMs + 1);
                _clock.Schedule(TimeSpan.FromMilliseconds(delayMs), () => EnterSignalLossStandby(version));
            }
        }

        if (changed) RaisePowerStateChanged();
    }

    /// <summary>Puts the panel in soft-off before the simulation starts, as if its power button was off at boot.</summary>
    public void StartPoweredOff()
    {
        lock (_gate)
        {
            _power = PanelPowerState.SoftOff;
            _powerMode = PowerModeOff;
        }
    }

    /// <summary>System suspend cuts video at once; the panel is in standby by the time Windows is asleep.</summary>
    public void EnterSleepStandby()
    {
        bool changed = false;
        lock (_gate)
        {
            _hasVideoSignal = false;
            if (_power == PanelPowerState.On)
            {
                EnterStandbyLocked(_clock.ElapsedTicks, PowerModeOff);
                changed = true;
            }
        }

        if (changed) RaisePowerStateChanged();
    }

    public void ForceWedge()
    {
        lock (_gate)
        {
            _wedged = true;
            _wedgedSinceTicks = _clock.ElapsedTicks;
        }

        _trace.Add("monitor", $"{PhysicalID} reply pipeline wedged (forced)");
    }

    public void BeginFlakyWindow(TimeSpan duration, double probability)
    {
        lock (_gate)
        {
            _flakyUntilTicks = Math.Max(_flakyUntilTicks, _clock.ElapsedTicks + duration.Ticks);
            _flakyProbability = probability;
        }
    }

    // Bus interface. Callers hold the GPU's DDC mutex and pass the virtual time of the phase

    public bool AcknowledgesAddress(long nowTicks)
    {
        lock (_gate)
        {
            if (!Profile.SupportsDDC) return false;

            return _power switch
            {
                PanelPowerState.On => nowTicks >= _mcuReadyAtTicks
                                      && !(Profile.NAKsWhileBusy && nowTicks < _busyUntilTicks),
                PanelPowerState.Standby => Profile.RespondsInStandby && nowTicks >= _mcuReadyAtTicks,
                PanelPowerState.SoftOff => Profile.RespondsWhenSoftOff,
                PanelPowerState.HardOff => nowTicks < _hardOffGarbleUntilTicks,
                _ => false
            };
        }
    }

    /// <summary>True when the panel itself is powered down rather than merely busy or booting.</summary>
    public bool IsPanelOff
    {
        get
        {
            lock (_gate) return _power is PanelPowerState.SoftOff or PanelPowerState.HardOff;
        }
    }

    public void ReceiveHostMessage(long nowTicks, byte[] wire)
    {
        bool powerChanged = false;
        lock (_gate)
        {
            ApplyPendingLocked(nowTicks);
            if (!DDCMessages.TryDecodeHostMessage(wire, out byte[] payload) || payload.Length == 0)
            {
                // A corrupted request is ignored, so the next read finds nothing prepared
                _preparedReply = null;
                return;
            }

            switch (payload[0])
            {
                case DDCMessages.GetVCPRequestOpcode when payload.Length == 2:
                    GetsReceived++;
                    MaybeWedgeOnEarlyReadLocked(nowTicks);
                    PrepareGetReplyLocked(nowTicks, payload[1]);
                    break;

                case DDCMessages.SetVCPOpcode when payload.Length == 4:
                    SetsReceived++;
                    _preparedReply = null;
                    if (Profile.SetClearsWedge && _wedged)
                    {
                        _wedged = false;
                        _healthySinceTicks = nowTicks;
                        _trace.Add("monitor", $"{PhysicalID} reply pipeline resynchronized by SET");
                    }

                    _busyUntilTicks = nowTicks + Milliseconds(_random.Next(Profile.SetBusyMinimumMs, Profile.SetBusyMaximumMs + 1));
                    if (_random.NextDouble() < Profile.DropSetProbability)
                    {
                        _trace.Add("monitor", $"{PhysicalID} dropped SET 0x{payload[1]:X2}");
                        break;
                    }

                    powerChanged = WriteRegisterLocked(nowTicks, payload[1], (ushort)((payload[2] << 8) | payload[3]));
                    break;

                case DDCMessages.CapabilitiesRequestOpcode when payload.Length == 3:
                    PrepareCapabilitiesReplyLocked(nowTicks, (ushort)((payload[1] << 8) | payload[2]));
                    break;

                default:
                    _preparedReply = null;
                    break;
            }
        }

        if (powerChanged) RaisePowerStateChanged();
    }

    public byte[] ReadReply(long nowTicks)
    {
        lock (_gate)
        {
            ApplyPendingLocked(nowTicks);

            // Right after a DDC hard-off the MCU keeps answering for a moment, with nothing coherent to say
            if (_power == PanelPowerState.HardOff) return DDCMessages.NullMessage();

            byte[]? prepared = _preparedReply;
            bool ready = prepared != null && nowTicks >= _preparedReplyReadyAtTicks;
            _preparedReply = null;

            if (Profile.ReadsBroken) return Garble(prepared);
            if (nowTicks < _flakyUntilTicks && _random.NextDouble() < _flakyProbability) return Garble(prepared);
            if (!ready) return DDCMessages.NullMessage();

            byte[] reply = prepared!;
            if (_previousReply != null && _random.NextDouble() < Profile.StaleReplyProbability)
                reply = _previousReply;
            _previousReply = prepared;

            return _wedged ? ShiftedReply(reply) : reply;
        }
    }

    private bool IsDDCHealthyLocked(long nowTicks) =>
        Profile.SupportsDDC
        && !Profile.ReadsBroken
        && _power == PanelPowerState.On
        && nowTicks >= _mcuReadyAtTicks
        && !_wedged;

    private void PrepareGetReplyLocked(long nowTicks, byte code)
    {
        ushort maximum;
        ushort current;
        bool supported = true;
        switch (code)
        {
            case VCPConstants.Brightness:
                maximum = Profile.BrightnessMaximum;
                current = _brightness;
                break;
            case VCPConstants.PowerMode:
                maximum = PowerModeMaximum;
                current = _powerMode;
                break;
            case VCPConstants.InputSource:
                maximum = 0xFF;
                current = _inputSource;
                break;
            default:
                supported = SupportedCodes.Contains(code);
                maximum = 100;
                current = _otherRegisters.GetValueOrDefault(code, defaultValue: (ushort)50);
                break;
        }

        _preparedReply = DDCMessages.GetVCPReply(
            code,
            supported ? DDCMessages.ResultNoError : DDCMessages.ResultUnsupportedCode,
            supported ? maximum : (ushort)0,
            supported ? current : (ushort)0);
        _preparedReplyReadyAtTicks = nowTicks
                                     + Milliseconds(_random.Next(
                                         Profile.ReplyPreparationMinimumMs,
                                         Profile.ReplyPreparationMaximumMs + 1));
    }

    private void PrepareCapabilitiesReplyLocked(long nowTicks, ushort offset)
    {
        int start = Math.Min(offset, _capabilities.Length);
        int length = Math.Min(DDCTiming.CapabilitiesFragmentLength, _capabilities.Length - start);
        byte[] fragment = System.Text.Encoding.ASCII.GetBytes(_capabilities.Substring(start, length));
        _preparedReply = DDCMessages.CapabilitiesReply(offset, fragment);
        _preparedReplyReadyAtTicks = nowTicks + Milliseconds(Profile.ReplyPreparationMaximumMs);
    }

    /// <summary>Applies a SET; returns true when it changed the panel's power state.</summary>
    private bool WriteRegisterLocked(long nowTicks, byte code, ushort value)
    {
        if (_power is PanelPowerState.SoftOff or PanelPowerState.HardOff && !Profile.RespondsWhenSoftOff) return false;

        bool woke = false;
        if (_power == PanelPowerState.Standby && Profile.WakesOnDDCWrite && code != VCPConstants.PowerMode)
        {
            EnterOnLocked(nowTicks, StandbyWakeDelayMs(), powerCycled: false, coldBoot: false);
            _trace.Add("monitor", $"{PhysicalID} woke on DDC write 0x{code:X2}");
            woke = true;
        }

        switch (code)
        {
            case VCPConstants.Brightness:
                ushort clamped = Math.Min(value, Profile.BrightnessMaximum);
                int applyMs = _random.Next(Profile.BrightnessApplyMinimumMs, Profile.BrightnessApplyMaximumMs + 1);
                if (applyMs <= 0)
                {
                    // The newest SET wins over an older one still taking effect
                    _pendingBrightnessAtTicks = long.MaxValue;
                    SetBrightnessLocked(nowTicks, clamped);
                }
                else
                {
                    _pendingBrightness = clamped;
                    _pendingBrightnessAtTicks = nowTicks + Milliseconds(applyMs);
                }

                return woke;

            case VCPConstants.PowerMode:
                return ApplyPowerCommandLocked(nowTicks, value) || woke;

            case VCPConstants.InputSource:
                _inputSource = value;
                return woke;

            default:
                _otherRegisters[code] = value;
                return woke;
        }
    }

    private bool ApplyPowerCommandLocked(long nowTicks, ushort value)
    {
        switch (value)
        {
            case PowerModeOn:
                if (_power != PanelPowerState.Standby || !Profile.HonorsPowerOnCommand) return false;

                EnterOnLocked(nowTicks, StandbyWakeDelayMs(), powerCycled: false, coldBoot: false);
                return true;

            case 2:
            case 3:
                if (_power != PanelPowerState.On) return false;

                EnterStandbyLocked(nowTicks, value);
                return true;

            case PowerModeOff:
                if (_power is PanelPowerState.SoftOff or PanelPowerState.HardOff) return false;

                // DPM off keeps the MCU on the bus like standby on most panels; the LG answers only after a button press
                if (Profile.RespondsInStandby && Profile.HonorsPowerOnCommand)
                    EnterStandbyLocked(nowTicks, PowerModeOff);
                else
                    EnterOffLocked(nowTicks, PanelPowerState.SoftOff, PowerModeOff);
                return true;

            case PowerModeHardOff:
                if (_power == PanelPowerState.HardOff) return false;

                EnterOffLocked(nowTicks, PanelPowerState.HardOff, PowerModeHardOff);
                _hardOffGarbleUntilTicks = nowTicks
                                           + Milliseconds(_random.Next(
                                               Profile.HardOffGarbleMinimumMs,
                                               Profile.HardOffGarbleMaximumMs + 1));
                return true;

            default:
                return false;
        }
    }

    private void EnterSignalLossStandby(long version)
    {
        lock (_gate)
        {
            if (version != _pendingTransitionVersion) return;
            if (_hasVideoSignal || _power != PanelPowerState.On) return;

            EnterStandbyLocked(_clock.ElapsedTicks, PowerModeOff);
        }

        _trace.Add("monitor", $"{PhysicalID} entered standby on signal loss");
        RaisePowerStateChanged();
    }

    private void EnterStandbyLocked(long nowTicks, ushort powerMode)
    {
        ApplyPendingLocked(nowTicks);
        CommitBrightnessLocked(nowTicks);
        _pendingTransitionVersion++;
        _power = PanelPowerState.Standby;
        _powerMode = powerMode;
        _preparedReply = null;
    }

    private void EnterOffLocked(long nowTicks, PanelPowerState state, ushort powerMode)
    {
        ApplyPendingLocked(nowTicks);
        CommitBrightnessLocked(nowTicks);
        _pendingTransitionVersion++;
        _power = state;
        _powerMode = powerMode;
        _preparedReply = null;
        _previousReply = null;
    }

    private void EnterOnLocked(long nowTicks, int readyDelayMs, bool powerCycled, bool coldBoot)
    {
        _pendingTransitionVersion++;
        _power = PanelPowerState.On;
        _powerMode = PowerModeOn;
        _mcuReadyAtTicks = nowTicks + Milliseconds(readyDelayMs);
        _healthySinceTicks = _mcuReadyAtTicks;
        _wakeWindowStartTicks = _mcuReadyAtTicks;
        _preparedReply = null;
        _previousReply = null;
        _busyUntilTicks = 0;
        if (_wedged)
        {
            _wedged = false;
            _trace.Add("monitor", $"{PhysicalID} reply pipeline cleared by power transition");
        }

        if (coldBoot)
        {
            int flakyMs = _random.Next(Profile.BootFlakyWindowMinimumMs, Profile.BootFlakyWindowMaximumMs + 1);
            _flakyUntilTicks = _mcuReadyAtTicks + Milliseconds(flakyMs);
            _flakyProbability = Profile.BootFlakyProbabilityMinimum
                                + _random.NextDouble()
                                * (Profile.BootFlakyProbabilityMaximum - Profile.BootFlakyProbabilityMinimum);
        }
        else
        {
            _flakyUntilTicks = _mcuReadyAtTicks + Milliseconds(Profile.WakeFlakyWindowMs);
            _flakyProbability = Profile.WakeFlakyProbability;
        }

        if (powerCycled)
        {
            // A panel whose MCU stayed on the bus while soft-off kept saving settings, including SETs it took while off
            ApplyPendingLocked(nowTicks);
            if (Profile.RespondsWhenSoftOff) CommitBrightnessLocked(nowTicks);

            ushort restored = Profile.BrightnessAfterPowerCycle switch
            {
                BrightnessAfterPowerCycle.Factory => Profile.FactoryBrightness,
                BrightnessAfterPowerCycle.LastCommitted => _committedBrightness,
                _ => _brightness
            };
            _pendingBrightnessAtTicks = long.MaxValue;
            if (restored != _brightness) SetBrightnessLocked(nowTicks, restored);
        }

        if (!_hasVideoSignal)
        {
            long version = _pendingTransitionVersion;
            int delayMs = _random.Next(Profile.SignalLossStandbyMinimumMs, Profile.SignalLossStandbyMaximumMs + 1);
            _clock.Schedule(TimeSpan.FromMilliseconds(readyDelayMs + delayMs), () => EnterSignalLossStandby(version));
        }
    }

    private void MaybeWedgeOnEarlyReadLocked(long nowTicks)
    {
        if (_wedged || Profile.EarlyReadWedgeProbability <= 0) return;

        // Panels already on when the simulation starts have no wake window
        if (_wakeWindowStartTicks == long.MinValue || nowTicks < _wakeWindowStartTicks) return;
        if (nowTicks - _wakeWindowStartTicks > Milliseconds(Profile.EarlyReadWedgeWindowMs)) return;
        if (_random.NextDouble() >= Profile.EarlyReadWedgeProbability) return;

        _wedged = true;
        _wedgedSinceTicks = nowTicks;
        _trace.Add("monitor", $"{PhysicalID} reply pipeline wedged by an early read after wake");
    }

    private void ApplyPendingLocked(long nowTicks)
    {
        if (nowTicks < _pendingBrightnessAtTicks) return;

        // The SET took effect when its apply delay ran out, not when something next looked, and commits from then
        long appliedAtTicks = _pendingBrightnessAtTicks;
        _pendingBrightnessAtTicks = long.MaxValue;
        SetBrightnessLocked(appliedAtTicks, _pendingBrightness);
    }

    private void SetBrightnessLocked(long nowTicks, ushort value)
    {
        if (value == _brightness) return;

        CommitBrightnessLocked(nowTicks);
        _brightness = value;
        _brightnessChangedAtTicks = nowTicks;
    }

    private void CommitBrightnessLocked(long nowTicks)
    {
        if (nowTicks - _brightnessChangedAtTicks >= Milliseconds(Profile.BrightnessCommitDelayMs))
            _committedBrightness = _brightness;
    }

    private byte[] Garble(byte[]? prepared)
    {
        // Measured garbage arrives as 0x89 (no usable command) or 0x85 (well-framed reply with the wrong fields)
        if (prepared == null || _random.NextDouble() < 0.5) return DDCMessages.NullMessage();

        byte wrongCode = (byte)(prepared.Length > 4 ? prepared[4] ^ 0x5A : 0x5A);
        return DDCMessages.GetVCPReply(wrongCode, DDCMessages.ResultNoError, maximum: 100, current: 0);
    }

    private static byte[] ShiftedReply(byte[] reply)
    {
        // The desynchronized LG pipeline frames each reply correctly but pairs it with the previous reply's checksum,
        // which the host reports as ERROR_GRAPHICS_DDCCI_INVALID_MESSAGE_CHECKSUM (0xC026258B) on every read
        byte[] wedged = (byte[])reply.Clone();
        wedged[^1] ^= 0x5A;
        return wedged;
    }

    private int ColdBootDelayMs() => _random.Next(Profile.ColdBootMinimumMs, Profile.ColdBootMaximumMs + 1);

    private int StandbyWakeDelayMs() => _random.Next(Profile.StandbyWakeMinimumMs, Profile.StandbyWakeMaximumMs + 1);

    private string BuildCapabilities() =>
        $"(prot(monitor)type(LCD)model({Profile.ModelName.Replace(' ', '_')})cmds(01 02 03 0C E3 F3)"
        + "vcp(02 04 05 08 10 12 14(05 08 0B) 16 18 1A 52 60(0F 10 11 12) AC AE B2 B6 C6 C8 C9 D6(01 04 05) DC DF)"
        + "mccs_ver(2.1))";

    private void RaisePowerStateChanged()
    {
        try { PowerStateChanged?.Invoke(this); }
        catch (Exception exception) { _trace.Add("monitor", $"{PhysicalID} power listener threw: {exception.Message}"); }
    }

    private static long Milliseconds(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).Ticks;
}
