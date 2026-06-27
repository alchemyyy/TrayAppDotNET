namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// A VCP feature advertised by a monitor's capability string
/// together with its current and maximum values as reported by the display.
/// </summary>
public class VCPCapability
{
    public string Name { get; set; } = string.Empty;

    public byte OptCode { get; set; }

    public uint Value { get; set; }

    public uint MaxValue { get; set; }
}
