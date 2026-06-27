namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// Named constants for commonly used Virtual Control Panel codes
/// from the MCCS (Monitor Control Command Set) specification.
/// See NodeFormatter for the full set of recognized codes and their human-readable names.
/// </summary>
public static class VCPConstants
{
    public const byte Brightness = 0x10;
    public const byte Contrast = 0x12;
    public const byte ColorPreset = 0x14;
    public const byte RedGain = 0x16;
    public const byte GreenGain = 0x18;
    public const byte BlueGain = 0x1A;
    public const byte InputSource = 0x60;
    public const byte Volume = 0x62;
    public const byte PowerMode = 0xD6;
    public const byte RedBlackLevel = 0x6C;
    public const byte GreenBlackLevel = 0x6E;
    public const byte BlueBlackLevel = 0x70;
}
