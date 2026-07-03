using System.Text.RegularExpressions;

namespace FanControlTrayAppDotNET.UI;

/// <summary>
/// Resolves user-configured hardware device nicknames.
/// </summary>
internal sealed class DeviceNicknameResolver
{
    private const string HardwareTypeRulePattern = "^\\{HardwareType\\.(?<HardwareType>[A-Za-z0-9_]+)\\}$";
    private const string HardwareTypeGroupName = "HardwareType";
    private const string CPUHardwareTypeTarget = "CPU";
    private const string GPUHardwareTypeTarget = "GPU";
    private const string LHMCPUHardwareType = "Cpu";
    private const string LHMGPUAMDHardwareType = "GpuAmd";
    private const string LHMGPUIntelHardwareType = "GpuIntel";
    private const string LHMGPUVidiaHardwareType = "GpuNvidia";

    private static readonly Regex HardwareTypeRuleRegex =
        new(HardwareTypeRulePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly Dictionary<DeviceKey, string> _nicknamesByDevice;
    private readonly IReadOnlyList<CompiledDeviceNicknameRule> _rules;

    private DeviceNicknameResolver(
        Dictionary<DeviceKey, string> nicknamesByDevice,
        IReadOnlyList<CompiledDeviceNicknameRule> rules)
    {
        _nicknamesByDevice = nicknamesByDevice;
        _rules = rules;
    }

    public static DeviceNicknameResolver Empty { get; } =
        new(new Dictionary<DeviceKey, string>(), []);

    /// <summary>
    /// Builds a resolver from the current settings and live data sources.
    /// </summary>
    public static DeviceNicknameResolver Create(AppSettings settings)
    {
        List<CompiledDeviceNicknameRule> rules = CompileRules(settings.DeviceNicknameRules);
        List<DeviceMetadata> devices = BuildDeviceMetadata();

        Dictionary<DeviceKey, string> baseNamesByDevice = [];
        Dictionary<string, int> totalsByBaseName = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeviceMetadata device in devices)
        {
            string baseName = ResolveBaseName(rules, device);
            baseNamesByDevice[DeviceKey.From(device)] = baseName;
            totalsByBaseName[baseName] = totalsByBaseName.GetValueOrDefault(baseName) + 1;
        }

        Dictionary<string, int> seenByBaseName = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<DeviceKey, string> nicknamesByDevice = [];
        foreach (DeviceMetadata device in devices)
        {
            DeviceKey key = DeviceKey.From(device);
            string baseName = baseNamesByDevice[key];
            int count = seenByBaseName.GetValueOrDefault(baseName) + 1;
            seenByBaseName[baseName] = count;
            nicknamesByDevice[key] = totalsByBaseName[baseName] <= 1 || count == 1
                ? baseName
                : $"{baseName} {count}";
        }

        return new DeviceNicknameResolver(nicknamesByDevice, rules);
    }

    /// <summary>
    /// Resolves the display name for a hardware device source.
    /// </summary>
    public string Resolve(DataSource? source)
    {
        if (source is not { } dataSource) return string.Empty;
        DeviceMetadata device = DeviceMetadata.From(dataSource);
        if (string.IsNullOrWhiteSpace(device.DeviceName)) return string.Empty;

        DeviceKey key = DeviceKey.From(device);
        if (_nicknamesByDevice.TryGetValue(key, out string? nickname)) return nickname;
        return ResolveBaseName(_rules, device);
    }

    /// <summary>
    /// Resolves a device name without hardware metadata.
    /// </summary>
    public string Resolve(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return string.Empty;
        return ResolveBaseName(_rules, new DeviceMetadata(deviceName, string.Empty));
    }

    /// <summary>
    /// Builds distinct hardware device metadata from registered data sources.
    /// </summary>
    private static List<DeviceMetadata> BuildDeviceMetadata()
    {
        List<DeviceMetadata> devices = [];
        foreach (DataSource source in DataSource.DataSources.Values)
        {
            DeviceMetadata device = DeviceMetadata.From(source);
            if (string.IsNullOrWhiteSpace(device.DeviceName)) continue;
            if (ContainsDevice(devices, device)) continue;
            devices.Add(device);
        }

        return
        [
            .. devices
                .OrderBy(static device => device.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static device => device.HardwareType, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// Checks whether a device list already contains equivalent metadata.
    /// </summary>
    private static bool ContainsDevice(List<DeviceMetadata> devices, DeviceMetadata candidate)
    {
        foreach (DeviceMetadata device in devices)
        {
            if (!string.Equals(device.DeviceName, candidate.DeviceName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(device.HardwareType, candidate.HardwareType, StringComparison.OrdinalIgnoreCase)) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Applies the first matching nickname rule.
    /// </summary>
    private static string ResolveBaseName(IReadOnlyList<CompiledDeviceNicknameRule> rules, DeviceMetadata device)
    {
        foreach (CompiledDeviceNicknameRule rule in rules)
        {
            if (!rule.TryResolve(device, out string replacement)) continue;
            if (!string.IsNullOrWhiteSpace(replacement)) return replacement.Trim();
        }

        return device.DeviceName;
    }

    /// <summary>
    /// Compiles user rules and skips invalid regex entries.
    /// </summary>
    private static List<CompiledDeviceNicknameRule> CompileRules(IEnumerable<DeviceNicknameRule> rules)
    {
        List<CompiledDeviceNicknameRule> compiled = [];
        foreach (DeviceNicknameRule rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.TargetRegex)) continue;

            Match hardwareTypeMatch = HardwareTypeRuleRegex.Match(rule.TargetRegex);
            if (hardwareTypeMatch.Success)
            {
                compiled.Add(new CompiledDeviceNicknameRule
                {
                    Kind = DeviceNicknameRuleKind.HardwareType,
                    HardwareTypeTarget = hardwareTypeMatch.Groups[HardwareTypeGroupName].Value,
                    ReplacementString = rule.ReplacementString ?? string.Empty,
                });
                continue;
            }

            try
            {
                Regex regex = new(rule.TargetRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                compiled.Add(new CompiledDeviceNicknameRule
                {
                    Kind = DeviceNicknameRuleKind.DeviceNameRegex,
                    DeviceNameRegex = regex,
                    ReplacementString = rule.ReplacementString ?? string.Empty,
                });
            }
            catch (ArgumentException ex)
            {
                TADNLog.Log($"Device nickname regex ignored: {ex.Message}");
            }
        }

        return compiled;
    }

    /// <summary>
    /// Checks whether a hardware-type rule target matches a source hardware type.
    /// </summary>
    private static bool HardwareTypeMatches(string hardwareType, string target)
    {
        if (string.IsNullOrWhiteSpace(hardwareType)) return false;

        string normalizedTarget = target.ToUpperInvariant();
        return normalizedTarget switch
        {
            CPUHardwareTypeTarget => string.Equals(hardwareType, LHMCPUHardwareType, StringComparison.OrdinalIgnoreCase),
            GPUHardwareTypeTarget => IsGPUHardwareType(hardwareType),
            _ => string.Equals(hardwareType, target, StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Checks whether a hardware type is one of the LHM GPU types.
    /// </summary>
    private static bool IsGPUHardwareType(string hardwareType) =>
        string.Equals(hardwareType, LHMGPUAMDHardwareType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(hardwareType, LHMGPUIntelHardwareType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(hardwareType, LHMGPUVidiaHardwareType, StringComparison.OrdinalIgnoreCase);

    private readonly record struct DeviceMetadata(string DeviceName, string HardwareType)
    {
        /// <summary>
        /// Builds device metadata from a data source.
        /// </summary>
        public static DeviceMetadata From(DataSource source) =>
            new(source.ControllerName, source.ControllerHardwareType);
    }

    private readonly record struct DeviceKey(string DeviceName, string HardwareType)
    {
        /// <summary>
        /// Builds a case-insensitive device key.
        /// </summary>
        public static DeviceKey From(DeviceMetadata metadata) =>
            new(metadata.DeviceName.ToUpperInvariant(), metadata.HardwareType.ToUpperInvariant());
    }

    private enum DeviceNicknameRuleKind
    {
        DeviceNameRegex,
        HardwareType,
    }

    private sealed class CompiledDeviceNicknameRule
    {
        public DeviceNicknameRuleKind Kind { get; init; }

        public Regex? DeviceNameRegex { get; init; }

        public string HardwareTypeTarget { get; init; } = string.Empty;

        public string ReplacementString { get; init; } = string.Empty;

        /// <summary>
        /// Tries to resolve a nickname replacement for a device.
        /// </summary>
        public bool TryResolve(DeviceMetadata device, out string replacement)
        {
            replacement = string.Empty;
            switch (Kind)
            {
                case DeviceNicknameRuleKind.DeviceNameRegex:
                {
                    if (DeviceNameRegex is not { } regex) return false;
                    if (!regex.IsMatch(device.DeviceName)) return false;
                    replacement = regex.Replace(device.DeviceName, ReplacementString);
                    return true;
                }
                case DeviceNicknameRuleKind.HardwareType:
                {
                    if (!HardwareTypeMatches(device.HardwareType, HardwareTypeTarget)) return false;
                    replacement = ReplacementString;
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}
