using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class DeviceNicknameResolverTests
{
    /// <summary>
    /// Verifies first-run nickname rules are hardware-type rules.
    /// </summary>
    [Fact]
    public void EnsureDefaultDeviceNicknameRulesSeedsHardwareTypeRules()
    {
        AppSettings settings = new();

        bool seeded = settings.EnsureDefaultDeviceNicknameRules();

        Assert.True(seeded);
        Assert.True(settings.DeviceNicknamesInitialized);
        Assert.Equal(expected: 2, settings.DeviceNicknameRules.Count);
        Assert.Equal(expected: "{HardwareType.CPU}", settings.DeviceNicknameRules[0].TargetRegex);
        Assert.Equal(expected: "CPU", settings.DeviceNicknameRules[0].ReplacementString);
        Assert.Equal(expected: "{HardwareType.GPU}", settings.DeviceNicknameRules[1].TargetRegex);
        Assert.Equal(expected: "GPU", settings.DeviceNicknameRules[1].ReplacementString);

        settings.DeviceNicknameRules[0].ReplacementString = "Processor";

        bool reseeded = settings.EnsureDefaultDeviceNicknameRules();

        Assert.False(reseeded);
        Assert.Equal(expected: "Processor", settings.DeviceNicknameRules[0].ReplacementString);
    }

    /// <summary>
    /// Verifies explicit default loading preserves custom nickname rules.
    /// </summary>
    [Fact]
    public void LoadDefaultDeviceNicknameRulesPreservesCustomRules()
    {
        AppSettings settings = new()
        {
            DeviceNicknamesInitialized = true,
            DeviceNicknameRules =
            [
                new DeviceNicknameRule { TargetRegex = "^Custom$", ReplacementString = "Custom" },
                new DeviceNicknameRule { TargetRegex = "{HardwareType.CPU}", ReplacementString = "Processor" }
            ]
        };

        bool loaded = settings.LoadDefaultDeviceNicknameRules();

        Assert.True(loaded);
        Assert.Equal(expected: 3, settings.DeviceNicknameRules.Count);
        Assert.Equal(expected: "{HardwareType.CPU}", settings.DeviceNicknameRules[0].TargetRegex);
        Assert.Equal(expected: "CPU", settings.DeviceNicknameRules[0].ReplacementString);
        Assert.Equal(expected: "{HardwareType.GPU}", settings.DeviceNicknameRules[1].TargetRegex);
        Assert.Equal(expected: "GPU", settings.DeviceNicknameRules[1].ReplacementString);
        Assert.Equal(expected: "^Custom$", settings.DeviceNicknameRules[2].TargetRegex);
        Assert.Equal(expected: "Custom", settings.DeviceNicknameRules[2].ReplacementString);
    }

    /// <summary>
    /// Verifies hardware-type rules ignore device names and suffix duplicates.
    /// </summary>
    [Fact]
    public void CreateAppliesHardwareTypeDefaultNicknames()
    {
        Dictionary<string, DataSource> savedRegistry = SaveDataSourceRegistry();
        try
        {
            DataSource.DataSources.Clear();
            AddSource(key: "cpu0.temp", controllerName: "Unhelpful Model Name", controllerHardwareType: "Cpu");
            AddSource(key: "gpu0.temp", controllerName: "AMD Radeon RX 7900 XTX", controllerHardwareType: "GpuAmd");
            AddSource(key: "gpu1.temp", controllerName: "NVIDIA GeForce RTX 5090", controllerHardwareType: "GpuNvidia");
            AddSource(key: "storage0.temp", controllerName: "NVIDIA Storage Device", controllerHardwareType: "Storage");
            AddSource(key: "legacy0.temp", controllerName: "AMD Ryzen 9 9950X", string.Empty);

            AppSettings settings = new();
            settings.EnsureDefaultDeviceNicknameRules();
            DeviceNicknameResolver resolver = DeviceNicknameResolver.Create(settings);

            Assert.Equal(expected: "CPU", resolver.Resolve(DataSource.Find("cpu0.temp")));
            Assert.Equal(expected: "GPU", resolver.Resolve(DataSource.Find("gpu0.temp")));
            Assert.Equal(expected: "GPU 2", resolver.Resolve(DataSource.Find("gpu1.temp")));
            Assert.Equal(expected: "NVIDIA Storage Device", resolver.Resolve(DataSource.Find("storage0.temp")));
            Assert.Equal(expected: "AMD Ryzen 9 9950X", resolver.Resolve(DataSource.Find("legacy0.temp")));
            Assert.Equal(expected: "Unhelpful Model Name", resolver.Resolve("Unhelpful Model Name"));
        }
        finally
        {
            RestoreDataSourceRegistry(savedRegistry);
        }
    }

    /// <summary>
    /// Verifies exact LHM hardware-type targets are supported.
    /// </summary>
    [Fact]
    public void CreateSupportsExactLHMHardwareTypeTargets()
    {
        Dictionary<string, DataSource> savedRegistry = SaveDataSourceRegistry();
        try
        {
            DataSource.DataSources.Clear();
            AddSource(key: "gpu0.temp", controllerName: "NVIDIA GeForce RTX 5090", controllerHardwareType: "GpuNvidia");
            AddSource(key: "gpu1.temp", controllerName: "AMD Radeon RX 7900 XTX", controllerHardwareType: "GpuAmd");

            AppSettings settings = new()
            {
                DeviceNicknameRules =
                [
                    new DeviceNicknameRule
                    {
                        TargetRegex = "{HardwareType.GpuNvidia}", ReplacementString = "NVIDIA GPU"
                    }
                ]
            };

            DeviceNicknameResolver resolver = DeviceNicknameResolver.Create(settings);

            Assert.Equal(expected: "NVIDIA GPU", resolver.Resolve(DataSource.Find("gpu0.temp")));
            Assert.Equal(expected: "AMD Radeon RX 7900 XTX", resolver.Resolve(DataSource.Find("gpu1.temp")));
        }
        finally
        {
            RestoreDataSourceRegistry(savedRegistry);
        }
    }

    /// <summary>
    /// Verifies invalid regex rules are ignored without blocking later rules.
    /// </summary>
    [Fact]
    public void CreateSkipsInvalidRegexRules()
    {
        Dictionary<string, DataSource> savedRegistry = SaveDataSourceRegistry();
        try
        {
            DataSource.DataSources.Clear();
            AddSource(key: "pump0.power", controllerName: "Corsair Commander", controllerHardwareType: "Cooler");

            AppSettings settings = new()
            {
                DeviceNicknameRules =
                [
                    new DeviceNicknameRule { TargetRegex = "[", ReplacementString = "Broken" },
                    new DeviceNicknameRule { TargetRegex = "Corsair.*", ReplacementString = "Pump" }
                ]
            };

            DeviceNicknameResolver resolver = DeviceNicknameResolver.Create(settings);

            Assert.Equal(expected: "Pump", resolver.Resolve(DataSource.Find("pump0.power")));
        }
        finally
        {
            RestoreDataSourceRegistry(savedRegistry);
        }
    }

    /// <summary>
    /// Verifies generated legacy defaults migrate to hardware-type defaults.
    /// </summary>
    [Fact]
    public void EnsureDefaultDeviceNicknameRulesMigratesGeneratedDefaults()
    {
        AppSettings settings = new()
        {
            DeviceNicknamesInitialized = true,
            DeviceNicknameRules =
            [
                new DeviceNicknameRule
                {
                    TargetRegex = ".*(CPU|Processor|Ryzen|Threadripper|Intel.*Core|Core.*Processor).*",
                    ReplacementString = "CPU"
                },
                new DeviceNicknameRule
                {
                    TargetRegex = ".*(GPU|Graphics|NVIDIA|GeForce|Radeon|Arc).*", ReplacementString = "GPU"
                },
                new DeviceNicknameRule { TargetRegex = "^AMD\\ Ryzen\\ 9\\ 9950X$", ReplacementString = "CPU 2" },
                new DeviceNicknameRule { TargetRegex = "^Corsair Commander$", ReplacementString = "Pump" }
            ]
        };

        bool migrated = settings.EnsureDefaultDeviceNicknameRules();

        Assert.True(migrated);
        Assert.Equal(expected: 3, settings.DeviceNicknameRules.Count);
        Assert.Equal(expected: "{HardwareType.CPU}", settings.DeviceNicknameRules[0].TargetRegex);
        Assert.Equal(expected: "CPU", settings.DeviceNicknameRules[0].ReplacementString);
        Assert.Equal(expected: "{HardwareType.GPU}", settings.DeviceNicknameRules[1].TargetRegex);
        Assert.Equal(expected: "GPU", settings.DeviceNicknameRules[1].ReplacementString);
        Assert.Equal(expected: "^Corsair Commander$", settings.DeviceNicknameRules[2].TargetRegex);
        Assert.Equal(expected: "Pump", settings.DeviceNicknameRules[2].ReplacementString);
    }

    /// <summary>
    /// Registers a test data source.
    /// </summary>
    private static void AddSource(string key, string controllerName, string controllerHardwareType)
    {
        DataSource source = new()
        {
            DataSourceKey = key,
            ControllerName = controllerName,
            ControllerHardwareType = controllerHardwareType,
            DataSourceType = DataSourceTypeEnum.Temperature
        };
        DataSource.Register(source);
    }

    /// <summary>
    /// Saves the global data-source registry for isolated tests.
    /// </summary>
    private static Dictionary<string, DataSource> SaveDataSourceRegistry() =>
        new(DataSource.DataSources, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Restores the global data-source registry after isolated tests.
    /// </summary>
    private static void RestoreDataSourceRegistry(Dictionary<string, DataSource> savedRegistry)
    {
        DataSource.DataSources.Clear();
        foreach (KeyValuePair<string, DataSource> pair in savedRegistry)
            DataSource.DataSources[pair.Key] = pair.Value;
    }
}
