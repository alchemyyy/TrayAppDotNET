using System.Globalization;
using System.Text;

namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>Defines the versioned line protocol shared by the managed client and native Night Light helper.</summary>
internal static class NightLightHelperProtocol
{
    public const int Version = 1;
    public const int MaximumLineLength = 512;

    public const string ServerArg = "--night-light-helper-server";
    public const string ParentProcessIDArg = "--parent-pid";
    public const string PipeNameArg = "--pipe-name";

    public const string InitializationCommand = "INIT";
    public const string SetStrengthCommand = "SET";
    public const string SetEnabledCommand = "ACTIVE";
    public const string PingCommand = "PING";
    public const string DrainCommand = "DRAIN";
    public const string ExitCommand = "EXIT";

    public const string ReadyResponse = "READY";
    public const string UnsupportedResponse = "UNSUPPORTED";
    public const string ImageMismatchResponse = "IMAGE_MISMATCH";
    public const string SuccessResponse = "OK";
    public const string PongResponse = "PONG";
    public const string DrainedResponse = "DRAINED";
    public const string FailureResponse = "FAIL";

    internal static readonly Encoding PipeEncoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>Serializes the exact ten-field INIT version 1 command consumed by the native helper.</summary>
    public static string SerializeInitialization(NightLightNativeBootstrapDescriptor descriptor)
    {
        descriptor.Validate();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{InitializationCommand}\t{Version}\t{descriptor.PDBGuid:N}\t{descriptor.PDBAge}" +
            $"\t{descriptor.ImageSize:X}\t{descriptor.InitializeRVA:X}\t{descriptor.SInstanceRVA:X}" +
            $"\t{descriptor.SetTargetColorTemperatureRVA:X}" +
            $"\t{descriptor.SetPreviewColorTemperatureChangesRVA:X}" +
            $"\t{descriptor.SetBlueLightActiveRVA:X}");
    }

    /// <summary>Parses and validates an INIT version 1 command without accepting alternate wire formats.</summary>
    public static bool TryParseInitialization(
        string line,
        out NightLightNativeBootstrapDescriptor descriptor)
    {
        descriptor = default;
        if (string.IsNullOrEmpty(line) || line.Length > MaximumLineLength || !IsASCII(line)) return false;

        string[] fields = line.Split('\t');
        if (fields is not
            [InitializationCommand, "1", _, _, _, _, _, _, _, _]) return false;
        if (fields[2].Length != 32 || !Guid.TryParseExact(fields[2], format: "N", out Guid PDBGuid)) return false;
        if (!uint.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint PDBAge)) return false;
        if (!TryParseHex(fields[4], out uint imageSize)
            || !TryParseHex(fields[5], out uint initializeRVA)
            || !TryParseHex(fields[6], out uint sInstanceRVA)
            || !TryParseHex(fields[7], out uint setTargetColorTemperatureRVA)
            || !TryParseHex(fields[8], out uint setPreviewColorTemperatureChangesRVA)
            || !TryParseHex(fields[9], out uint setBlueLightActiveRVA)) return false;

        try
        {
            descriptor = new NightLightNativeBootstrapDescriptor(
                PDBGuid,
                PDBAge,
                imageSize,
                initializeRVA,
                sInstanceRVA,
                setTargetColorTemperatureRVA,
                setPreviewColorTemperatureChangesRVA,
                setBlueLightActiveRVA);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Builds a validated SET command.</summary>
    public static string SerializeSetStrength(int percent)
    {
        if (percent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SetStrengthCommand}\t{percent}");
    }

    /// <summary>Builds a validated ACTIVE command with an optional pre-enable strength.</summary>
    public static string SerializeSetEnabled(bool enabled, int? enableStrength)
    {
        if (enableStrength is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(enableStrength));
        if (!enabled && enableStrength.HasValue)
            throw new ArgumentException("A disabled command cannot include a strength.", nameof(enableStrength));

        string command = SetEnabledCommand + (enabled ? "\t1" : "\t0");
        return enableStrength.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"{command}\t{enableStrength.Value}")
            : command;
    }

    private static bool TryParseHex(string field, out uint value)
    {
        value = 0;
        return field.Length > 0
               && uint.TryParse(
                   field,
                   NumberStyles.AllowHexSpecifier,
                   CultureInfo.InvariantCulture,
                   out value);
    }

    private static bool IsASCII(string value)
    {
        foreach (char character in value)
        {
            if (character > 0x7F) return false;
        }

        return true;
    }
}
