using System.Globalization;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Hardware;

/// <summary>What a panel does to its brightness register across a power cycle.</summary>
internal enum BrightnessAfterPowerCycle
{
    Retain,
    LastCommitted,
    Factory
}

/// <summary>
/// Static behavior of one monitor model. Defaults describe the user's LG ULTRAGEAR+ (GSM7799) as measured on
/// 2026-09-29 and 2026-09-30 through the hdmi-relink recorder: MCU answers within about 2.3 s of a button power-on,
/// 25 to 35 percent of replies come back garbled (0x85 or 0x89) for 26 to 30 s after power-on, a DDC hard-off is
/// accepted and then answers garbage for 0.6 to 1.5 s before it stops answering.
/// </summary>
internal sealed class MonitorProfile
{
    public string ModelName = "LG ULTRAGEAR+";
    public string ManufacturerID = "GSM";
    public ushort ProductCode = 0x7799;
    public bool SupportsDDC = true;

    // Brightness register
    public ushort BrightnessMaximum = 100;
    public ushort FactoryBrightness = 70;
    public BrightnessAfterPowerCycle BrightnessAfterPowerCycle = BrightnessAfterPowerCycle.LastCommitted;
    public int BrightnessCommitDelayMs = 3_000;
    public int BrightnessApplyMinimumMs;
    public int BrightnessApplyMaximumMs = 60;

    // MCU readiness after power transitions
    public int ColdBootMinimumMs = 1_200;
    public int ColdBootMaximumMs = 2_200;
    public int StandbyWakeMinimumMs = 300;
    public int StandbyWakeMaximumMs = 900;
    public int BootFlakyWindowMinimumMs = 26_000;
    public int BootFlakyWindowMaximumMs = 30_000;
    public double BootFlakyProbabilityMinimum = 0.25;
    public double BootFlakyProbabilityMaximum = 0.35;
    public int WakeFlakyWindowMs = 3_000;
    public double WakeFlakyProbability = 0.15;

    // DDC/CI engine. Reply preparation beyond the host's 50 ms wait reads back as a null message
    public int ReplyPreparationMinimumMs = 4;
    public int ReplyPreparationMaximumMs = 30;
    public int SetBusyMinimumMs = 10;
    public int SetBusyMaximumMs = 45;
    public bool NAKsWhileBusy = true;
    public double DropSetProbability;
    public double StaleReplyProbability;
    public bool ReadsBroken;

    // Power behavior
    public bool RespondsInStandby = true;
    public bool RespondsWhenSoftOff;
    public bool WakesOnDDCWrite;
    public bool HonorsPowerOnCommand = true;
    public int SignalLossStandbyMinimumMs = 2_000;
    public int SignalLossStandbyMaximumMs = 6_000;
    public int HardOffGarbleMinimumMs = 600;
    public int HardOffGarbleMaximumMs = 1_500;

    // Reply pipeline desync: a read too soon after wake shifts every later reply until a SET resynchronizes it
    // (MonitorPostDetectionSettleDelayMs and MonitorChecksumRecoveryPostWriteDelayMs exist for this LG behavior)
    public double EarlyReadWedgeProbability = 0.15;
    public int EarlyReadWedgeWindowMs = 1_500;
    public bool SetClearsWedge = true;

    public string HardwareID => ManufacturerID + ProductCode.ToString("X4", CultureInfo.InvariantCulture);

    public MonitorProfile Clone() => (MonitorProfile)MemberwiseClone();

    public static MonitorProfile LGUltraGear() => new();

    /// <summary>A spec-compliant panel with none of the measured LG quirks; isolates app logic from hardware noise.</summary>
    public static MonitorProfile Clean() =>
        new()
        {
            ColdBootMinimumMs = 400,
            ColdBootMaximumMs = 800,
            BootFlakyWindowMinimumMs = 0,
            BootFlakyWindowMaximumMs = 0,
            BootFlakyProbabilityMinimum = 0,
            BootFlakyProbabilityMaximum = 0,
            WakeFlakyWindowMs = 0,
            WakeFlakyProbability = 0,
            EarlyReadWedgeProbability = 0,
            BrightnessAfterPowerCycle = BrightnessAfterPowerCycle.Retain,
            BrightnessApplyMaximumMs = 0,
            NAKsWhileBusy = false
        };

    /// <summary>Replies sometimes miss the host's 50 ms read window and come back as null messages.</summary>
    public static MonitorProfile SlowReplies() =>
        new() { ReplyPreparationMinimumMs = 25, ReplyPreparationMaximumMs = 85, SetBusyMaximumMs = 120 };

    /// <summary>GET never returns a valid reply while SET still lands, the read-degraded case.</summary>
    public static MonitorProfile WriteOnly() => new() { ReadsBroken = true };

    /// <summary>A panel without DDC/CI, such as a TV or a monitor with DDC disabled in its OSD.</summary>
    public static MonitorProfile NoDDC() => new() { SupportsDDC = false };

    /// <summary>Accepts SETs and silently drops some, and occasionally replays an older reply.</summary>
    public static MonitorProfile Lossy() => new() { DropSetProbability = 0.08, StaleReplyProbability = 0.03 };
}
