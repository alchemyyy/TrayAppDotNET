using BrightnessTrayAppDotNET.DDCCI;

namespace BrightnessTrayAppDotNET.Tests;

internal static class Program
{
    private static int Main()
    {
        int failures = 0;

        Console.WriteLine($"Loaded monitors: {DDCMonitorDatabase.LoadedMonitorCount}");
        if (DDCMonitorDatabase.LoadedMonitorCount < 300)
        {
            Console.WriteLine("FAIL: expected >= 300 monitors loaded from embedded DB");
            failures++;
        }

        // Dell P2719HC has the canonical inverted-0xE1 quirk - if our resolver returns the right
        // (code, value) for it, the rest of the database is almost certainly fine.
        DDCMonitor del = new() { EDIDManufacturerID = "DEL", EDIDProductCode = "4187" };
        bool delMatched = DDCMonitorDatabase.ApplyProfile(del);
        Console.WriteLine($"DEL4187 matched={delMatched}, model='{del.ProfileModelName}', "
                          + $"powerOff={string.Join(",", del.PowerOffCommands)}");
        if (!delMatched || !del.HasKnownProfile)
        {
            Console.WriteLine("FAIL: DEL4187 didn't match");
            failures++;
        }
        if (!del.ProfileModelName.Contains("P2719HC", StringComparison.Ordinal))
        {
            Console.WriteLine($"FAIL: DEL4187 expected model 'P2719HC...', got '{del.ProfileModelName}'");
            failures++;
        }
        if (del.BrightnessCode != 0x10)
        {
            Console.WriteLine($"FAIL: DEL4187 brightness expected 0x10, got 0x{del.BrightnessCode:X2}");
            failures++;
        }

        // Hard-off should still come from 0xD6 (the primary entry) at value 0x05
        // because both 0xD6 and 0xE1 are advertised; 0xD6 takes priority.
        (byte hardCode, byte hardValue) = del.ResolvePowerOff(PowerOffLevel.Hard);
        Console.WriteLine($"DEL4187 hard-off: 0x{hardCode:X2}=0x{hardValue:X2}");
        if (hardCode != 0xD6 || hardValue != 0x05)
        {
            Console.WriteLine($"FAIL: DEL4187 hard-off expected 0xD6=0x05, got 0x{hardCode:X2}=0x{hardValue:X2}");
            failures++;
        }

        // The inverted 0xE1 should be present as the SECOND power command.
        if (del.PowerOffCommands.Count < 2 || !del.PowerOffCommands[1].IsInverted)
        {
            Console.WriteLine("FAIL: DEL4187 expected an inverted 0xE1 alternate power command");
            failures++;
        }
        else
        {
            MonitorPowerCommand alt = del.PowerOffCommands[1];
            Console.WriteLine($"DEL4187 alt power: 0x{alt.Code:X2} on=0x{alt.ValueOn:X2} "
                              + $"off=0x{alt.ValueHardOff:X2} inverted={alt.IsInverted}");
            if (alt.Code != 0xE1 || alt.ValueOn != 0 || alt.ValueHardOff != 1 || !alt.IsInverted)
            {
                Console.WriteLine("FAIL: DEL4187 alt 0xE1 values mismatch");
                failures++;
            }
        }

        // Unknown EDID: ApplyProfile returns false and the monitor keeps its VESA-standard defaults.
        DDCMonitor unknown = new() { EDIDManufacturerID = "ZZZ", EDIDProductCode = "FFFF" };
        bool unknownMatched = DDCMonitorDatabase.ApplyProfile(unknown);
        Console.WriteLine($"ZZZFFFF matched={unknownMatched}, hasKnownProfile={unknown.HasKnownProfile}");
        if (unknownMatched || unknown.HasKnownProfile)
        {
            Console.WriteLine("FAIL: unknown EDID was treated as matched");
            failures++;
        }
        (byte uCode, byte uValue) = unknown.ResolvePowerOff(PowerOffLevel.Hard);
        if (uCode != 0xD6 || uValue != 0x05)
        {
            Console.WriteLine($"FAIL: default hard-off expected 0xD6=0x05, got 0x{uCode:X2}=0x{uValue:X2}");
            failures++;
        }

        // Sleep / Soft levels also resolve correctly on the default.
        (_, byte sleepValue) = unknown.ResolvePowerOff(PowerOffLevel.Sleep);
        (_, byte softValue) = unknown.ResolvePowerOff(PowerOffLevel.Soft);
        Console.WriteLine($"Default sleep=0x{sleepValue:X2} soft=0x{softValue:X2} hard=0x{uValue:X2}");
        if (sleepValue != 0x02 || softValue != 0x04)
        {
            Console.WriteLine("FAIL: default profile sleep/soft values mismatch");
            failures++;
        }

        // EDIDIdentifier formatting check.
        if (del.EDIDIdentifier != "DEL4187")
        {
            Console.WriteLine($"FAIL: EDIDIdentifier expected 'DEL4187', got '{del.EDIDIdentifier}'");
            failures++;
        }

        // Empty EDID -> ApplyProfile no-op, monitor stays on defaults.
        DDCMonitor empty = new();
        bool emptyMatched = DDCMonitorDatabase.ApplyProfile(empty);
        if (emptyMatched || empty.HasKnownProfile)
        {
            Console.WriteLine("FAIL: empty EDID was treated as matched");
            failures++;
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"FAILURES: {failures}");
        return failures == 0 ? 0 : 1;
    }
}
