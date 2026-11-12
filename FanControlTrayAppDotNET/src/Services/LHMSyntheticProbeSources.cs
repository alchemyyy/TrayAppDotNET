using LibreHardwareMonitor.Hardware;

namespace FanControlTrayAppDotNET.Services;

/// <summary>
/// Builds app-side synthetic probe sources from LibreHardwareMonitor readings.
/// </summary>
internal static class LHMSyntheticProbeSources
{
    public const string MaxClockDisplayName = "Cores (Max)";
    public const string MaxEffectiveClockDisplayName = "Cores (Max Effective)";

    public const string PowerDrawTotalControllerName = "System";
    public const string PowerDrawTotalHardwareType = "System";
    public const string PowerDrawTotalDisplayName = "Power Draw (Total)";
    public const string PowerDrawTotalKey = "System.Power.Power_Draw_Total";

    private const string IntelIntegratedGpuIdentifierPrefix = "/gpu-intel-integrated/";
    private const string CoolerDissipatedPowerName = "Dissipated Power";

    private const string ClockFolderName = "Clock";
    private const string MaxClockLeaf = "Cores_(Max)";
    private const string MaxEffectiveClockLeaf = "Cores_(Max_Effective)";
    private const string EffectiveClockToken = "(Effective)";
    private const string AverageToken = "Average";
    private const string CoreToken = "Core";

    /// <summary>
    /// Builds the stable key for a CPU max-clock synthetic source.
    /// </summary>
    public static string BuildMaxClockKey(string hardwareName)
    {
        string controller = hardwareName.Replace(oldChar: ' ', newChar: '_');
        return $"{controller}.{ClockFolderName}.{MaxClockLeaf}";
    }

    /// <summary>
    /// Builds the stable key for a CPU max-effective-clock synthetic source.
    /// </summary>
    public static string BuildMaxEffectiveClockKey(string hardwareName)
    {
        string controller = hardwareName.Replace(oldChar: ' ', newChar: '_');
        return $"{controller}.{ClockFolderName}.{MaxEffectiveClockLeaf}";
    }

    /// <summary>
    /// Returns true for per-core clock sensors used by the max clock synthetic probe.
    /// </summary>
    public static bool IsCoreClockSensor(DataSourceTypeEnum sourceType, string sensorName)
    {
        if (sourceType != DataSourceTypeEnum.Clock) return false;
        if (sensorName.Contains(EffectiveClockToken, StringComparison.OrdinalIgnoreCase)) return false;
        if (sensorName.Contains(AverageToken, StringComparison.OrdinalIgnoreCase)) return false;

        return sensorName.Contains(CoreToken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true for per-core effective clock sensors used by the max synthetic probe.
    /// </summary>
    public static bool IsCoreEffectiveClockSensor(DataSourceTypeEnum sourceType, string sensorName)
    {
        if (sourceType != DataSourceTypeEnum.Clock) return false;
        if (!sensorName.Contains(EffectiveClockToken, StringComparison.OrdinalIgnoreCase)) return false;
        if (sensorName.Contains(AverageToken, StringComparison.OrdinalIgnoreCase)) return false;

        return sensorName.Contains(CoreToken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True for Intel iGPU hardware, whose power is already part of the CPU package.
    /// </summary>
    public static bool IsIntegratedGpu(HardwareType hardwareType, string hardwareIdentifier) =>
        hardwareType == HardwareType.GpuIntel
        && hardwareIdentifier.StartsWith(IntelIntegratedGpuIdentifierPrefix, StringComparison.Ordinal);

    /// <summary>
    /// How one LHM sensor feeds the system power draw total. Each device contributes its best-ranked whole-device
    /// reading (lower rank wins) plus its additive readings. Any PSU total replaces the component sum, since it
    /// measures everything the components measure and more.
    /// </summary>
    public static PowerDrawContribution ClassifyPowerDrawSensor(
        HardwareType hardwareType,
        bool isIntegratedGpu,
        SensorType sensorType,
        string sensorName)
    {
        if (sensorType != SensorType.Power || isIntegratedGpu) return PowerDrawContribution.None;

        return hardwareType switch
        {
            // Intel PSys covers the whole platform including the package; AMD and Intel both name the package
            HardwareType.Cpu => sensorName switch
            {
                "CPU Platform" => PowerDrawContribution.DeviceTotal(rank: 0),
                "CPU Package" or "Package" => PowerDrawContribution.DeviceTotal(rank: 1),
                _ => PowerDrawContribution.None
            },
            // NVML and ADL report whole-board power under this name
            HardwareType.GpuNvidia or HardwareType.GpuAmd => sensorName == "GPU Package"
                ? PowerDrawContribution.DeviceTotal(rank: 0)
                : PowerDrawContribution.None,
            // Intel discrete: total card energy, else GPU-only energy
            HardwareType.GpuIntel => sensorName switch
            {
                "GPU Total" => PowerDrawContribution.DeviceTotal(rank: 0),
                "GPU Package" => PowerDrawContribution.DeviceTotal(rank: 1),
                _ => PowerDrawContribution.None
            },
            // Pump and fan electrical power; coolant heat dissipation is not a draw
            HardwareType.Cooler => sensorName == CoolerDissipatedPowerName
                ? PowerDrawContribution.None
                : PowerDrawContribution.Additive,
            // Corsair "Total watts", MSI "PSU Out", else Corsair's summed rails
            HardwareType.Psu => sensorName switch
            {
                "Total watts" or "PSU Out" => PowerDrawContribution.PsuTotal(rank: 0),
                "Total Output" => PowerDrawContribution.PsuTotal(rank: 1),
                _ => PowerDrawContribution.None
            },
            _ => PowerDrawContribution.None
        };
    }
}

internal enum PowerDrawRole
{
    None,
    DeviceTotal,
    Additive,
    PsuTotal
}

internal readonly record struct PowerDrawContribution(PowerDrawRole Role, int Rank)
{
    public static PowerDrawContribution None => default;
    public static PowerDrawContribution Additive => new(PowerDrawRole.Additive, Rank: 0);
    public static PowerDrawContribution DeviceTotal(int rank) => new(PowerDrawRole.DeviceTotal, rank);
    public static PowerDrawContribution PsuTotal(int rank) => new(PowerDrawRole.PsuTotal, rank);
}

/// <summary>
/// Sums one poll tick's power readings into the system power draw total. Call <see cref="BeginDevice"/> before each
/// device's sensors and <see cref="EndDevice"/> after them; non-finite or negative readings are skipped.
/// </summary>
internal sealed class PowerDrawTotalAccumulator
{
    private double _components;
    private bool _hasComponents;
    private double _psus;
    private bool _hasPsus;
    private PowerDrawRole _deviceRole;
    private int _deviceRank;
    private float _deviceValue;
    private double _deviceAdditive;
    private bool _hasDeviceAdditive;

    /// <summary>Watts: the PSU total when any PSU reported one, else the component sum; null when nothing reported.</summary>
    public float? Total => _hasPsus ? (float)_psus : _hasComponents ? (float)_components : null;

    public void BeginDevice()
    {
        _deviceRole = PowerDrawRole.None;
        _deviceRank = int.MaxValue;
        _deviceValue = 0.0f;
        _deviceAdditive = 0.0;
        _hasDeviceAdditive = false;
    }

    public void Add(PowerDrawContribution contribution, float value)
    {
        if (contribution.Role == PowerDrawRole.None || !float.IsFinite(value) || value < 0.0f) return;

        if (contribution.Role == PowerDrawRole.Additive)
        {
            _deviceAdditive += value;
            _hasDeviceAdditive = true;
            return;
        }

        if (contribution.Rank >= _deviceRank) return;

        _deviceRole = contribution.Role;
        _deviceRank = contribution.Rank;
        _deviceValue = value;
    }

    public void EndDevice()
    {
        switch (_deviceRole)
        {
            case PowerDrawRole.PsuTotal:
                _psus += _deviceValue;
                _hasPsus = true;
                break;
            case PowerDrawRole.DeviceTotal:
                _components += _deviceValue;
                _hasComponents = true;
                break;
        }

        if (!_hasDeviceAdditive) return;

        _components += _deviceAdditive;
        _hasComponents = true;
    }
}
