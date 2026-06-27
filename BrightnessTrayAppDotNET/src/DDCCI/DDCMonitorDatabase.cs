using System.Reflection;
using System.Text.Json;

namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// Lazily-loaded database of per-monitor VCP-command profiles, keyed by 7-character EDID identifier
/// (3-letter PnP manufacturer + 4-hex product code, e.g. "DEL4187").
///
/// The backing JSON is embedded as a resource at <c>BrightnessTrayAppDotNET.DDCCI.monitor-database.json</c>
/// (file lives at <c>src/DDCCI/monitor-database.json</c>).
/// It is consolidated from ddccontrol/ddccontrol-db (309 monitors), rockowitz/ddcutil (per-model quirks),
/// and several Windows-side implementations - see <c>claudeit/ddc-research/</c> for the source data.
///
/// Parsing is hand-done with <see cref="JsonDocument"/> rather than reflection-based deserialization
/// so the path is fully trim-safe under PublishTrimmed. First call materializes the dictionary; subsequent
/// lookups are O(1).
///
/// Usage: <see cref="ApplyProfile"/> populates the per-monitor profile fields on a DDCMonitor in place.
/// Untouched monitors (no DB match, or empty EDID id) keep their VESA-standard defaults.
/// </summary>
public static class DDCMonitorDatabase
{
    private const string ResourceName = "BrightnessTrayAppDotNET.DDCCI.monitor-database.json";

    private static readonly Lazy<Dictionary<string, ParsedProfile>> _profiles = new(LoadFromEmbeddedResource);

    public static int LoadedMonitorCount => _profiles.Value.Count;

    /// <summary>
    /// Looks up the profile for <paramref name="monitor"/> by EDID identity and copies the profile fields
    /// onto it. Returns true iff a database entry was matched (i.e. the monitor's <see cref="DDCMonitor.HasKnownProfile"/>
    /// will be true after the call). On a miss, the monitor's profile fields are left at their VESA-standard defaults.
    /// </summary>
    public static bool ApplyProfile(DDCMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        string id = monitor.EDIDIdentifier;
        if (string.IsNullOrEmpty(id)) return false;
        if (!_profiles.Value.TryGetValue(id, out ParsedProfile? profile)) return false;

        monitor.BrightnessCode = profile.BrightnessCode;
        monitor.ProfileModelName = profile.ModelName;
        monitor.PowerOffCommands = profile.PowerOffCommands;
        monitor.ProfileQuirks = profile.Quirks;
        return true;
    }

    /// <summary>Internal record of one parsed DB row. Held in the lazy dictionary; never exposed.</summary>
    private sealed class ParsedProfile
    {
        public string ModelName = string.Empty;
        public byte BrightnessCode = 0x10;
        public IReadOnlyList<MonitorPowerCommand> PowerOffCommands = [];
        public IReadOnlyList<string> Quirks = [];
    }

    private static Dictionary<string, ParsedProfile> LoadFromEmbeddedResource()
    {
        Assembly assembly = typeof(DDCMonitorDatabase).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            WPFLog.Log($"DDCMonitorDatabase: embedded resource '{ResourceName}' not found - using empty DB");
            return new Dictionary<string, ParsedProfile>(0, StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(stream);
            return ParseRoot(doc.RootElement);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"DDCMonitorDatabase: failed to parse embedded JSON - {ex.GetType().Name}: {ex.Message}");
            return new Dictionary<string, ParsedProfile>(0, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, ParsedProfile> ParseRoot(JsonElement root)
    {
        Dictionary<string, ParsedProfile> result = new(309, StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty("monitors", out JsonElement monitorsObj)) return result;
        if (monitorsObj.ValueKind != JsonValueKind.Object) return result;

        foreach (JsonProperty entry in monitorsObj.EnumerateObject())
        {
            ParsedProfile? profile = ParseMonitor(entry.Value);
            if (profile != null) result[entry.Name] = profile;
        }

        WPFLog.Log($"DDCMonitorDatabase: loaded {result.Count} monitor profiles from embedded DB");
        return result;
    }

    private static ParsedProfile? ParseMonitor(JsonElement m)
    {
        if (m.ValueKind != JsonValueKind.Object) return null;

        string EDIDDatabaseID = ReadString(m, "edidId");
        if (string.IsNullOrEmpty(EDIDDatabaseID)) return null;

        byte brightnessCode = 0x10;
        if (m.TryGetProperty("brightness", out JsonElement brightnessElem)
            && brightnessElem.ValueKind == JsonValueKind.Object
            && brightnessElem.TryGetProperty("code", out JsonElement bcElem)
            && bcElem.TryGetInt32(out int bcInt))
            brightnessCode = (byte)bcInt;

        List<MonitorPowerCommand> powerCommands = [];
        if (m.TryGetProperty("powerOff", out JsonElement powerArr) && powerArr.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement p in powerArr.EnumerateArray())
            {
                MonitorPowerCommand? cmd = ParsePowerCommand(p);
                if (cmd != null) powerCommands.Add(cmd);
            }
        }

        List<string> quirks = [];
        if (m.TryGetProperty("quirks", out JsonElement quirksArr) && quirksArr.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement q in quirksArr.EnumerateArray())
            {
                if (q.ValueKind == JsonValueKind.String)
                {
                    string? s = q.GetString();
                    if (!string.IsNullOrEmpty(s)) quirks.Add(s);
                }
            }
        }

        return new ParsedProfile
        {
            ModelName = ReadString(m, "modelName"),
            BrightnessCode = brightnessCode,
            PowerOffCommands = powerCommands,
            Quirks = quirks,
        };
    }

    private static MonitorPowerCommand? ParsePowerCommand(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object) return null;
        if (!p.TryGetProperty("code", out JsonElement codeElem) || !codeElem.TryGetInt32(out int codeInt))
            return null;

        byte code = (byte)codeInt;
        bool isInverted = p.TryGetProperty("isInverted", out JsonElement invElem)
                          && invElem.ValueKind == JsonValueKind.True;

        // Hard-off value lives under "valueHardOff" for 0xD6 (which has multiple off levels);
        // 0xE1 has a single off value under "valueOff". Either is the "off" we use for the
        // most aggressive power-off.
        byte valueHardOff = ReadByte(p, "valueHardOff", defaultValue: ReadByte(p, "valueOff", defaultValue: 0));
        byte valueOn = ReadByte(p, "valueOn", defaultValue: 0x01);

        byte? valueStandby = TryReadByte(p, "valueStandby");
        byte? valueSoftOff = TryReadByte(p, "valueSoftOff");

        string label = ReadString(p, "label");

        return new MonitorPowerCommand
        {
            Code = code,
            ValueOn = valueOn,
            ValueStandby = valueStandby,
            ValueSoftOff = valueSoftOff,
            ValueHardOff = valueHardOff,
            IsInverted = isInverted,
            Label = label,
        };
    }

    private static string ReadString(JsonElement obj, string name)
    {
        return obj.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
    }

    private static byte ReadByte(JsonElement obj, string name, byte defaultValue) =>
        obj.TryGetProperty(name, out JsonElement v) && v.TryGetInt32(out int n) ? (byte)n : defaultValue;

    private static byte? TryReadByte(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement v) && v.TryGetInt32(out int n) ? (byte)n : null;
}
