namespace TrayAppDotNETCommon.Interop;

/// <summary>MOD_* flags of RegisterHotKey; the P/Invokes themselves live in <see cref="User32"/>.</summary>
public static class HotkeyModifiers
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;
    public const uint NoRepeat = 0x4000;

    /// <summary>A binding needs at least one of these.</summary>
    public const uint Required = Alt | Control | Shift | Win;
}

public static class HotkeyVirtualKeys
{
    /// <summary>VK_F12 is reserved for the debugger at all times, so RegisterHotKey rejects it.</summary>
    public const uint DebuggerReserved = 0x7B;
}
