namespace TaskManagerTrayAppDotNET.Models;

internal enum ProcessExecutionState : byte
{
    Running,
    Suspended
}

internal enum ProcessDisplayCode : byte
{
    Empty,
    Unavailable,
    Running,
    Suspended,
    Windows,
    Platform32Bit,
    Platform64Bit,
    Yes,
    No,
    Enabled,
    Disabled,
    NotAllowed,
    VeryLow,
    Low,
    Normal,
    High,
    Critical,
    DPIUnaware,
    DPISystem,
    DPIPerMonitor,
    X86,
    X64,
    ARM64,
    UWP,
    AppContainer,
    NoIsolation
}

/// <summary>Distinguishes PID reuse without retaining a Process object or kernel handle.</summary>
internal readonly record struct ProcessInstanceKey(int ProcessID, long CreationTimeTicks);

/// <summary>Stable shell identities used to share one cached icon across matching processes.</summary>
internal readonly record struct ProcessIconSource(
    string? ExecutablePath,
    string? ApplicationUserModelID)
{
    public bool IsAvailable =>
        !string.IsNullOrEmpty(ExecutablePath) || !string.IsNullOrEmpty(ApplicationUserModelID);
}

/// <summary>Shared immutable executable metadata referenced by every matching process.</summary>
internal sealed class ProcessImageIdentity(
    string key,
    string name,
    string imagePath,
    string description,
    ProcessIconSource iconSource)
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    public string ImagePath { get; } = imagePath;
    public string Description { get; } = description;
    public ProcessIconSource IconSource { get; } = iconSource;
    public int ReferenceCount { get; set; } = 1;
}

/// <summary>Immutable visible-column data shared by every published snapshot of a process.</summary>
internal sealed class ProcessStaticData
{
    public required ProcessInstanceKey InstanceKey { get; init; }
    public required ProcessImageIdentity Image { get; init; }
    public required string UserName { get; init; }
    public required long[] NumericValues { get; init; }
    public required string?[] TextValues { get; init; }

    public int ProcessID => InstanceKey.ProcessID;
}

internal static class ProcessDisplayCodeText
{
    public static string Get(ProcessDisplayCode code) => code switch
    {
        ProcessDisplayCode.Empty => string.Empty,
        ProcessDisplayCode.Unavailable => "Unavailable",
        ProcessDisplayCode.Running => "Running",
        ProcessDisplayCode.Suspended => "Suspended",
        ProcessDisplayCode.Windows => "Windows",
        ProcessDisplayCode.Platform32Bit => "32-bit",
        ProcessDisplayCode.Platform64Bit => "64-bit",
        ProcessDisplayCode.Yes => "Yes",
        ProcessDisplayCode.No => "No",
        ProcessDisplayCode.Enabled => "Enabled",
        ProcessDisplayCode.Disabled => "Disabled",
        ProcessDisplayCode.NotAllowed => "Not allowed",
        ProcessDisplayCode.VeryLow => "Very low",
        ProcessDisplayCode.Low => "Low",
        ProcessDisplayCode.Normal => "Normal",
        ProcessDisplayCode.High => "High",
        ProcessDisplayCode.Critical => "Critical",
        ProcessDisplayCode.DPIUnaware => "Unaware",
        ProcessDisplayCode.DPISystem => "System",
        ProcessDisplayCode.DPIPerMonitor => "Per-Monitor",
        ProcessDisplayCode.X86 => "x86",
        ProcessDisplayCode.X64 => "x64",
        ProcessDisplayCode.ARM64 => "ARM64",
        ProcessDisplayCode.UWP => "UWP",
        ProcessDisplayCode.AppContainer => "AppContainer",
        ProcessDisplayCode.NoIsolation => "None",
        _ => string.Empty
    };
}
