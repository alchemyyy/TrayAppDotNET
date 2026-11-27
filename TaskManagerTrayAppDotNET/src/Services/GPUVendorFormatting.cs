using System.Globalization;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Turns the raw device-manager identity of a GPU into the forms its vendor publishes.</summary>
internal static class GPUVendorFormatting
{
    public const ushort NVIDIAVendorID = 0x10DE;
    public const ushort AMDVendorID = 0x1002;
    public const ushort IntelVendorID = 0x8086;

    private const int NVIDIAVersionDigits = 5;
    private const int NVIDIABuildFieldDigits = 4;
    private const int NVIDIAMinorDigits = 2;
    private const string NVIDIABIOSPrefix = "Version";
    private const string PCIHardwareIDPrefix = @"PCI\";

    /// <summary>
    /// Returns the driver version the vendor publishes, or an empty string when it cannot be derived. NVIDIA's comes
    /// from the last five digits of the Windows version's third and zero-padded fourth fields (32.0.16.1692 is 616.92).
    /// AMD's Adrenalin version is only in the registry, so <paramref name="amdSoftwareVersion"/> carries it. Intel
    /// publishes the full Windows version, also quoted by its last two fields (32.0.101.6913 is 101.6913).
    /// </summary>
    public static string FormatPublicDriverVersion(
        ushort vendorID,
        string windowsDriverVersion,
        string amdSoftwareVersion)
    {
        switch (vendorID)
        {
            case NVIDIAVendorID:
                return TryFormatNVIDIADriverVersion(windowsDriverVersion, out string nvidiaVersion)
                    ? nvidiaVersion
                    : string.Empty;
            case AMDVendorID:
                return amdSoftwareVersion.Trim();
            case IntelVendorID:
                return TryFormatIntelDriverVersion(windowsDriverVersion, out string intelVersion)
                    ? intelVersion
                    : string.Empty;
            default:
                return string.Empty;
        }
    }

    /// <summary>Shows the vendor's version first and keeps the Windows version beside it when they differ.</summary>
    public static string FormatDriverVersionDisplay(string publicDriverVersion, string windowsDriverVersion)
    {
        if (string.IsNullOrWhiteSpace(publicDriverVersion)) return windowsDriverVersion;
        if (string.IsNullOrWhiteSpace(windowsDriverVersion)
            || string.Equals(publicDriverVersion, windowsDriverVersion, StringComparison.OrdinalIgnoreCase))
            return publicDriverVersion;

        return string.Concat(publicDriverVersion, " (", windowsDriverVersion, ")");
    }

    private static bool TryFormatIntelDriverVersion(string windowsDriverVersion, out string version)
    {
        version = string.Empty;
        string[] fields = windowsDriverVersion.Trim().Split('.');
        if (fields.Length != 4 || !IsDigits(fields[2]) || !IsDigits(fields[3])) return false;

        version = string.Concat(fields[2], ".", fields[3]);
        return true;
    }

    private static bool TryFormatNVIDIADriverVersion(string windowsDriverVersion, out string version)
    {
        version = string.Empty;
        string[] fields = windowsDriverVersion.Trim().Split('.');
        if (fields.Length != 4
            || !IsDigits(fields[2])
            || !IsDigits(fields[3])
            || fields[3].Length > NVIDIABuildFieldDigits)
            return false;

        string digits = string.Concat(fields[2], fields[3].PadLeft(NVIDIABuildFieldDigits, '0'));
        if (digits.Length < NVIDIAVersionDigits) return false;

        string last = digits[^NVIDIAVersionDigits..];
        version = string.Concat(last[..^NVIDIAMinorDigits], ".", last[^NVIDIAMinorDigits..]);
        return true;
    }

    /// <summary>
    /// Formats the driver's <c>HardwareInformation.BiosString</c>. NVIDIA writes it as <c>Version98.3.3b.c0.ab</c>,
    /// which becomes the <c>98.03.3B.C0.AB</c> its tools print; other vendors' strings are shown as written.
    /// </summary>
    public static string NormalizeVBIOSVersion(ushort vendorID, string rawBIOSString)
    {
        string trimmed = rawBIOSString.Trim().TrimEnd('\0').Trim();
        if (vendorID != NVIDIAVendorID) return trimmed;

        string withoutPrefix = trimmed.StartsWith(NVIDIABIOSPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[NVIDIABIOSPrefix.Length..].Trim()
            : trimmed;
        string[] fields = withoutPrefix.Split('.');
        if (fields.Length < 2) return trimmed;

        for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            string field = fields[fieldIndex];
            if (field.Length is 0 or > 2 || !IsHexDigits(field)) return trimmed;

            fields[fieldIndex] = field.PadLeft(totalWidth: 2, '0').ToUpperInvariant();
        }

        return string.Join('.', fields);
    }

    /// <summary>Parses <c>PCI\VEN_10DE&amp;DEV_2C02&amp;SUBSYS_20961771&amp;REV_A1</c> into its numeric fields.</summary>
    public static bool TryParseHardwareID(string hardwareID, out GPUHardwareID parsed)
    {
        parsed = default;
        if (!hardwareID.StartsWith(PCIHardwareIDPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        ushort vendorID = 0;
        ushort deviceID = 0;
        ushort subsystemVendorID = 0;
        ushort subsystemID = 0;
        byte revision = 0;
        bool hasVendor = false;
        bool hasDevice = false;
        string[] fields = hardwareID[PCIHardwareIDPrefix.Length..].Split('&');
        for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            string field = fields[fieldIndex];
            int separator = field.IndexOf('_');
            if (separator <= 0) continue;

            ReadOnlySpan<char> name = field.AsSpan(start: 0, separator);
            ReadOnlySpan<char> value = field.AsSpan(separator + 1);
            if (name.Equals("VEN", StringComparison.OrdinalIgnoreCase))
                hasVendor = TryParseHex(value, out vendorID);
            else if (name.Equals("DEV", StringComparison.OrdinalIgnoreCase))
                hasDevice = TryParseHex(value, out deviceID);
            else if (name.Equals("SUBSYS", StringComparison.OrdinalIgnoreCase) && value.Length == 8)
            {
                // The subsystem ID comes first and the subsystem vendor last
                if (!TryParseHex(value[..4], out subsystemID)
                    || !TryParseHex(value[4..], out subsystemVendorID))
                {
                    subsystemID = 0;
                    subsystemVendorID = 0;
                }
            }
            else if (name.Equals("REV", StringComparison.OrdinalIgnoreCase)
                     && value.Length <= 2
                     && byte.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte parsedRevision))
                revision = parsedRevision;
        }

        if (!hasVendor || !hasDevice) return false;

        parsed = new GPUHardwareID(
            HasValue: true,
            vendorID,
            deviceID,
            subsystemVendorID,
            subsystemID,
            revision);
        return true;
    }

    /// <summary>Formats the vendor and device as <c>10DE:2C02</c>, followed by the subsystem and revision.</summary>
    public static string FormatHardwareID(GPUHardwareID hardwareID)
    {
        if (!hardwareID.HasValue) return string.Empty;

        string device = string.Create(
            CultureInfo.InvariantCulture,
            $"{hardwareID.VendorID:X4}:{hardwareID.DeviceID:X4}");
        if (hardwareID.SubsystemVendorID == 0 && hardwareID.SubsystemID == 0)
            return string.Create(CultureInfo.InvariantCulture, $"{device} rev {hardwareID.Revision:X2}");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{device} ({hardwareID.SubsystemVendorID:X4}:{hardwareID.SubsystemID:X4}, rev {hardwareID.Revision:X2})");
    }

    /// <summary>
    /// Formats a PCIe link as <c>Gen 5 x16</c>, adding the maximum when the link runs below it. Windows reports the
    /// link speed as the PCIe generation number.
    /// </summary>
    public static string FormatPCIeLink(GPUPCIeLink link)
    {
        if (!link.HasValue) return string.Empty;

        string current = FormatLinkPart(link.CurrentGeneration, link.CurrentWidth);
        bool hasMaximum = link.MaximumGeneration > 0 || link.MaximumWidth > 0;
        bool belowMaximum = hasMaximum
                            && (link.CurrentGeneration < link.MaximumGeneration
                                || link.CurrentWidth < link.MaximumWidth);
        if (!belowMaximum) return current;

        return string.Concat(
            current,
            " (max ",
            FormatLinkPart(link.MaximumGeneration, link.MaximumWidth),
            ")");
    }

    private static string FormatLinkPart(uint generation, uint width) =>
        (generation, width) switch
        {
            (> 0, > 0) => string.Create(CultureInfo.InvariantCulture, $"Gen {generation} x{width}"),
            (> 0, _) => string.Create(CultureInfo.InvariantCulture, $"Gen {generation}"),
            (_, > 0) => string.Create(CultureInfo.InvariantCulture, $"x{width}"),
            _ => string.Empty
        };

    private static bool TryParseHex(ReadOnlySpan<char> value, out ushort parsed) =>
        ushort.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed);

    private static bool IsDigits(string value)
    {
        if (value.Length == 0) return false;

        foreach (char character in value)
        {
            if (!char.IsAsciiDigit(character)) return false;
        }

        return true;
    }

    private static bool IsHexDigits(string value)
    {
        foreach (char character in value)
        {
            if (!char.IsAsciiHexDigit(character)) return false;
        }

        return true;
    }
}
