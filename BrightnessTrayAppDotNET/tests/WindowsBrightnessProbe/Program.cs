using BrightnessTrayAppDotNET.DDCCI;

DisplayService display = new();
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
        + $"displayInstance='{monitor.DisplayInstancePath}' windowsInstance='{monitor.WindowsBrightnessInstanceName}' "
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
