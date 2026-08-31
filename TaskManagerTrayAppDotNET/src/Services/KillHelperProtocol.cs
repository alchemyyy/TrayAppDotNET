using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

internal static class KillHelperProtocol
{
    public const uint MailboxMagic = 0x4B484D54;
    public const uint ProtocolVersion = 1;
    public const int MailboxSize = 4096;

    public const int StateStarting = 1;
    public const int StateReady = 2;
    public const int StateStopping = 3;
    public const int StateFailed = 4;

    public const int ControlShutdown = 0x00000001;

    public const int FlagMailboxLocked = 0x00000001;
    public const int FlagStateLocked = 0x00000002;
    public const int FlagHotCodeLocked = 0x00000004;
    public const int FlagKernelCodeLocked = 0x00000008;
    public const int FlagHighPriority = 0x00000010;
    public const int FlagDebugPrivilege = 0x00000020;
    public const int FlagPowerThrottlingDisabled = 0x00000040;
    public const int FlagStackLocked = 0x00000080;
    public const int FlagLockCapacityReserved = 0x00000100;

    public const int RequiredHardeningFlags =
        FlagMailboxLocked |
        FlagStateLocked |
        FlagHotCodeLocked |
        FlagKernelCodeLocked |
        FlagHighPriority |
        FlagDebugPrivilege |
        FlagPowerThrottlingDisabled |
        FlagStackLocked |
        FlagLockCapacityReserved;

    public const int ResultNone = 0;
    public const int ResultSuccess = 1;
    public const int ResultInvalidTarget = 2;
    public const int ResultOpenFailed = 3;
    public const int ResultIdentityMismatch = 4;
    public const int ResultCriticalProcess = 5;
    public const int ResultTerminateFailed = 6;
}

[StructLayout(LayoutKind.Explicit, Size = KillHelperProtocol.MailboxSize)]
internal struct KillHelperMailbox
{
    [FieldOffset(0)]
    public uint Magic;

    [FieldOffset(4)]
    public uint Version;

    [FieldOffset(8)]
    public int HelperState;

    [FieldOffset(12)]
    public uint HelperProcessID;

    [FieldOffset(16)]
    public int HelperFlags;

    [FieldOffset(20)]
    public int HelperStartupError;

    [FieldOffset(24)]
    public uint ParentProcessID;

    [FieldOffset(28)]
    public int ControlFlags;

    [FieldOffset(64)]
    public long ArmPayloadSequence;

    [FieldOffset(72)]
    public long ArmRequestSequence;

    [FieldOffset(80)]
    public long ArmGeneration;

    [FieldOffset(88)]
    public long ArmCreationTime;

    [FieldOffset(96)]
    public int ArmProcessID;

    [FieldOffset(128)]
    public long FirePayloadSequence;

    [FieldOffset(136)]
    public long FireRequestSequence;

    [FieldOffset(144)]
    public long FireGeneration;

    [FieldOffset(152)]
    public long FireCreationTime;

    [FieldOffset(160)]
    public int FireProcessID;

    [FieldOffset(164)]
    public uint FireExitCode;

    [FieldOffset(192)]
    public long FireResponseSequence;

    [FieldOffset(200)]
    public int FireResult;

    [FieldOffset(204)]
    public int FireError;

    [FieldOffset(208)]
    public int FireResponseProcessID;
}
