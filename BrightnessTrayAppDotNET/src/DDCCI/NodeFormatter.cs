using BrightnessTrayAppDotNET.DDCCI.Parser.Nodes;

namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// Translates hex VCP codes and their sub-values into human-readable names using the MCCS specification tables.
/// The top-level lookup is dispatched by the parent node's qualified key
/// (e.g. <c>vcp_14</c> for color-preset sub-values), so nested enums resolve automatically.
/// </summary>
public class NodeFormatter : INodeFormatter
{
    private static readonly Dictionary<string, Func<string, string?>> LookupTables = new()
    {
        { "vcp", FormatVCPControlName },
        { "vcp_14", FormatVCPColorPreset },
        { "vcp_d6", FormatVCPPowerControl },
        { "vcp_60", FormatVCPInputSource },
    };

    public string? FormatNode(INode node)
    {
        string? parentKey = node.Parent?.ToString()?.ToLowerInvariant();
        if (parentKey == null || !LookupTables.TryGetValue(parentKey, out Func<string, string?>? lookup)) return null;

        return lookup(node.Value?.ToLowerInvariant() ?? string.Empty);
    }

    private static string? FormatVCPControlName(string code) => code switch
    {
        "00" => "Degauss",
        "01" => "Degauss",
        "02" => "Secondary Degauss",
        "04" => "Reset Factory Defaults",
        "05" => "SAM: Reset Brightness and Contrast",
        "06" => "Reset Factory Geometry",
        "08" => "Reset Factory Default Color",
        "0a" => "Reset Factory Default Position",
        "0c" => "Reset Factory Default Size",
        "0e" => "SAM: Image Lock Coarse",
        "10" => "Brightness",
        "12" => "Contrast",
        "14" => "Select Color Preset",
        "16" => "Red Video Gain",
        "18" => "Green Video Gain",
        "1a" => "Blue Video Gain",
        "1c" => "Focus",
        "1e" => "SAM: Auto Size Center",
        "20" => "Horizontal Position",
        "22" => "Horizontal Size",
        "24" => "Horizontal Pincushion",
        "26" => "Horizontal Pincushion Balance",
        "28" => "Horizontal Misconvergence",
        "2a" => "Horizontal Linearity",
        "2c" => "Horizontal Linearity Balance",
        "30" => "Vertical Position",
        "32" => "Vertical Size",
        "34" => "Vertical Pincushion",
        "36" => "Vertical Pincushion Balance",
        "38" => "Vertical Misconvergence",
        "3a" => "Vertical Linearity",
        "3c" => "Vertical Linearity Balance",
        "3e" => "SAM: Image Lock Fine",
        "40" => "Parallelogram Distortion",
        "42" => "Trapezoidal Distortion",
        "44" => "Tilt (Rotation)",
        "46" => "Top Corner Distortion Control",
        "48" => "Top Corner Distortion Balance",
        "4a" => "Bottom Corner Distortion Control",
        "4c" => "Bottom Corner Distortion Balance",
        "50" => "Hue",
        "52" => "Saturation",
        "54" => "Color Curve Adjust",
        "56" => "Horizontal Moire",
        "58" => "Vertical Moire",
        "5a" => "Auto Size Center Enable/Disable",
        "5c" => "Landing Adjust",
        "5e" => "Input Level Select",
        "60" => "Input Source Select",
        "62" => "Audio Speaker Volume Adjust",
        "64" => "Audio Microphone Volume Adjust",
        "66" => "On Screen Display Enable/Disable",
        "68" => "Language Select",
        "6c" => "Red Video Black Level",
        "6e" => "Green Video Black Level",
        "70" => "Blue Video Black Level",
        "a2" => "Auto Size Center",
        "a4" => "Polarity Horizontal Synchronization",
        "a6" => "Polarity Vertical Synchronization",
        "a8" => "Synchronization Type",
        "aa" => "Screen Orientation",
        "ac" => "Horizontal Frequency",
        "ae" => "Vertical Frequency",
        "b0" => "Settings",
        "ca" => "On Screen Display",
        "cc" => "SAM: On Screen Display Language",
        "d4" => "Stereo Mode",
        "d6" => "SAM: DPMS control (1 - on/4 - stby)",
        "dc" => "SAM: MagicBright (1 - text/2 - internet/3 - entertain/4 - custom)",
        "df" => "VCP Version",
        "e0" => "SAM: Color preset (0 - normal/1 - warm/2 - cool)",
        "e1" => "SAM: Power control (0 - off/1 - on)",
        "ed" => "SAM: Red Video Black Level",
        "ee" => "SAM: Green Video Black Level",
        "ef" => "SAM: Blue Video Black Level",
        "f5" => "SAM: VCP Enable",
        _ => null,
    };

    private static string? FormatVCPColorPreset(string code) => code switch
    {
        "01" => "sRGB",
        "02" => "Display Native",
        "03" => "4000 K",
        "04" => "5000 K",
        "05" => "6500 K",
        "06" => "7500 K",
        "07" => "8200 K",
        "08" => "9300 K",
        "09" => "10000 K",
        "0a" => "11500 K",
        "0b" => "User 1",
        "0c" => "User 2",
        "0d" => "User 3",
        _ => null,
    };

    private static string? FormatVCPPowerControl(string code) => code switch
    {
        "01" => "DPM: On,  DPMS: Off",
        "04" => "DPM: Off, DPMS: Off",
        "05" => "Write only value to turn off display",
        _ => null,
    };

    private static string? FormatVCPInputSource(string code) => code switch
    {
        "01" => "VGA-1",
        "02" => "VGA-2",
        "03" => "DVI-1",
        "04" => "DVI-2",
        "05" => "Composite video 1",
        "06" => "Composite video 2",
        "07" => "S-Video-1",
        "08" => "S-Video-2",
        "09" => "Tuner-1",
        "0a" => "Tuner-2",
        "0b" => "Tuner-3",
        "0c" => "Component video (YPrPb/YCrCb) 1",
        "0d" => "Component video (YPrPb/YCrCb) 2",
        "0e" => "Component video (YPrPb/YCrCb) 3",
        "0f" => "DisplayPort-1",
        "10" => "DisplayPort-2",
        "11" => "HDMI-1",
        "12" => "HDMI-2",
        _ => null,
    };
}
