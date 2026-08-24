using System.Diagnostics;
using BrightnessTrayAppDotNET.DDCCI;

const string EnumerationStressOption = "--enumeration-stress";

if (args.Length > 0 && string.Equals(args[0], EnumerationStressOption, StringComparison.Ordinal))
{
    if (args.Length != 2 || !int.TryParse(args[1], out int iterationCount) || iterationCount <= 0)
    {
        Console.Error.WriteLine($"Usage: WindowsBrightnessProbe {EnumerationStressOption} <positive iteration count>");
        return 64;
    }

    return RunEnumerationStress(iterationCount);
}

using DisplayService display = new();
display.OperationTimeoutMs = 0;

if (!display.TryGetMonitors(out IReadOnlyList<DDCMonitor> monitors, out string? enumError))
{
    Console.WriteLine($"ENUM_FAIL {enumError}");
    return 2;
}

Console.WriteLine($"MONITOR_COUNT {monitors.Count}");
for (int i = 0; i < monitors.Count; i++)
{
    DDCMonitor monitor = monitors[i];
    Console.WriteLine(
        $"MONITOR {i} kind={monitor.BrightnessControlKind} name='{monitor.Name}' "
        + $"deviceID='{monitor.DeviceID}' displayNumber={monitor.DisplayNumber} "
        + $"displayInstance='{monitor.DisplayInstancePath}' windowsInstance='{monitor.WindowsBrightnessInstanceName}' "
        + $"edidSerial='{monitor.EDIDSerial}' friendly='{monitor.FriendlyName}' "
        + $"methodPath='{monitor.WindowsBrightnessMethodPath}' supportsPower={monitor.SupportsVcpPower}");

    bool read = display.TryGetVCPFeature(
        monitor,
        monitor.BrightnessCode,
        out uint current,
        out uint max,
        out string? readError);
    Console.WriteLine($"READ {i} ok={read} current={current} max={max} error='{readError}'");
}

DDCMonitor? windows = monitors.FirstOrDefault(m => m.BrightnessControlKind == MonitorBrightnessControlKind.Windows);
if (windows == null)
{
    Console.WriteLine("WINDOWS_TARGET_MISSING");
    return 3;
}

if (!display.TryGetVCPFeature(windows, windows.BrightnessCode, out uint before, out uint beforeMax, out string? beforeError))
{
    Console.WriteLine($"WINDOWS_READ_FAIL {beforeError}");
    return 4;
}

uint target = before > 1 ? before - 1 : before + 1;
Console.WriteLine($"WINDOWS_SET target={target} from={before}");
if (!display.TrySetVCPFeature(windows, windows.BrightnessCode, target, out string? setError))
{
    Console.WriteLine($"WINDOWS_SET_FAIL {setError}");
    return 5;
}

Thread.Sleep(500);
bool afterRead = display.TryGetVCPFeature(windows, windows.BrightnessCode, out uint after, out uint afterMax, out string? afterError);
Console.WriteLine($"WINDOWS_AFTER ok={afterRead} current={after} max={afterMax} error='{afterError}'");

Console.WriteLine($"WINDOWS_RESTORE target={before}");
if (!display.TrySetVCPFeature(windows, windows.BrightnessCode, before, out string? restoreError))
{
    Console.WriteLine($"WINDOWS_RESTORE_FAIL {restoreError}");
    return 6;
}

Thread.Sleep(500);
bool restoredRead = display.TryGetVCPFeature(windows, windows.BrightnessCode, out uint restored, out uint restoredMax,
    out string? restoredError);
Console.WriteLine($"WINDOWS_RESTORED ok={restoredRead} current={restored} max={restoredMax} error='{restoredError}'");

return afterRead && restoredRead && after == target && restored == before ? 0 : 7;

static int RunEnumerationStress(int iterationCount)
{
    const int WarmupIterations = 10;
    using DisplayService display = new();

    for (int warmupIteration = 0; warmupIteration < WarmupIterations; warmupIteration++)
        _ = display.TryGetMonitors(out IReadOnlyList<DDCMonitor> _, out string? _);

    ForceCollection();
    using Process process = Process.GetCurrentProcess();
    process.Refresh();
    long startingPrivateBytes = process.PrivateMemorySize64;
    int startingHandleCount = process.HandleCount;
    int failures = 0;
    Stopwatch stopwatch = Stopwatch.StartNew();

    for (int iteration = 1; iteration <= iterationCount; iteration++)
    {
        if (!display.TryGetMonitors(out IReadOnlyList<DDCMonitor> _, out string? error))
        {
            failures++;
            Console.Error.WriteLine($"ENUM_FAIL iteration={iteration} error='{error}'");
        }

        if (iteration % 1000 == 0)
        {
            process.Refresh();
            Console.WriteLine(
                $"ENUM_PROGRESS completed={iteration} "
                + $"privateDelta={process.PrivateMemorySize64 - startingPrivateBytes} "
                + $"handles={process.HandleCount}");
        }
    }

    stopwatch.Stop();
    process.Refresh();
    long endingPrivateBytesBeforeCollection = process.PrivateMemorySize64;
    int endingHandleCountBeforeCollection = process.HandleCount;

    ForceCollection();
    process.Refresh();
    long endingPrivateBytesAfterCollection = process.PrivateMemorySize64;
    int endingHandleCountAfterCollection = process.HandleCount;

    Console.WriteLine(
        $"ENUM_STRESS iterations={iterationCount} failures={failures} elapsedMs={stopwatch.ElapsedMilliseconds} "
        + $"privateStart={startingPrivateBytes} privateEndBeforeGC={endingPrivateBytesBeforeCollection} "
        + $"privateEndAfterGC={endingPrivateBytesAfterCollection} "
        + $"privateDeltaBeforeGC={endingPrivateBytesBeforeCollection - startingPrivateBytes} "
        + $"privateDeltaAfterGC={endingPrivateBytesAfterCollection - startingPrivateBytes} "
        + $"handlesStart={startingHandleCount} handlesEndBeforeGC={endingHandleCountBeforeCollection} "
        + $"handlesEndAfterGC={endingHandleCountAfterCollection}");

    return failures == 0 ? 0 : 8;
}

static void ForceCollection()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}
