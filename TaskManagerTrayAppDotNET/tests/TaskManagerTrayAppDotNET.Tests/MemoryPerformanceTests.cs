using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class MemoryPerformanceTests
{
    private const ulong Gibibyte = 1_073_741_824;
    private const ulong Mebibyte = 1_048_576;

    [Fact]
    public void CompositionNormalizationProducesAnExactPhysicalMemoryPartition()
    {
        MemoryCompositionSample sample = new(
            HasCompositionData: true,
            CacheBytes: 50,
            FreeBytes: 200,
            ModifiedBytes: 50,
            StandbyBytes: 300,
            HasCompressionData: true,
            CompressedBytes: 40,
            EstimatedDataBytes: 100,
            SavedBytes: 60);

        NormalizedMemoryComposition composition = MemoryCompositionSampler.Normalize(
            totalPhysicalBytes: 1_000,
            fallbackAvailableBytes: 600,
            sample);

        Assert.True(composition.HasCompositionData);
        Assert.Equal(450UL, composition.InUseBytes);
        Assert.Equal(500UL, composition.AvailableBytes);
        Assert.Equal(50UL, composition.ModifiedBytes);
        Assert.Equal(300UL, composition.StandbyBytes);
        Assert.Equal(200UL, composition.FreeBytes);
        Assert.Equal(400UL, composition.CachedBytes);
        Assert.Equal(40UL, composition.CompressedBytes);
        Assert.Equal(100UL, composition.EstimatedDataBytes);
        Assert.Equal(60UL, composition.SavedBytes);
    }

    [Fact]
    public void PhysicalMemoryWmiReaderOmitsSerialNumbersInDefaultMode()
    {
        PhysicalMemoryMetadataReader reader = new();

        PhysicalMemoryHardwareMetadata publicMetadata = reader.Get(
            includeSerialNumbers: false,
            out string? publicError);
        PhysicalMemoryHardwareMetadata privateMetadata = reader.Get(
            includeSerialNumbers: true,
            out string? privateError);

        Assert.Null(publicError);
        Assert.Null(privateError);
        Assert.NotEmpty(publicMetadata.Modules.ToArray());
        Assert.Equal(publicMetadata.Modules.Length, privateMetadata.Modules.Length);
        Assert.All(
            publicMetadata.Modules.ToArray(),
            static module => Assert.Empty(module.SerialNumber));
    }

    [Fact]
    public void MemoryPresentationIncludesOfficialAndHardwareMetrics()
    {
        PhysicalMemoryModuleSnapshot module = new(
            "BANK 0",
            32 * Gibibyte,
            "TEST-PART",
            string.Empty);
        MemoryPerformanceSnapshot memory = MemoryPerformanceSnapshot.Empty with
        {
            HasMemoryData = true,
            UtilizationPercent = 37.5,
            TotalPhysicalBytes = 128 * Gibibyte,
            AvailablePhysicalBytes = 80 * Gibibyte,
            UsedPhysicalBytes = 48 * Gibibyte,
            InstalledPhysicalBytes = 128 * Gibibyte,
            CommittedBytes = 68 * Gibibyte,
            CommitLimitBytes = 191 * Gibibyte,
            CachedBytes = 64 * Gibibyte,
            PagedPoolBytes = 6 * Gibibyte,
            NonPagedPoolBytes = 3 * Gibibyte,
            HardwareReservedBytes = 648 * Mebibyte,
            Composition = new MemoryCompositionSnapshot(
                true,
                Mebibyte,
                64 * Gibibyte,
                16 * Gibibyte,
                true,
                3 * Gibibyte,
                17 * Gibibyte,
                14 * Gibibyte),
            Hardware = new PhysicalMemoryHardwareSnapshot(
                6_000,
                4,
                4,
                "DIMM",
                new PhysicalMemoryModuleSnapshot[] { module })
        };
        PerformanceSnapshot snapshot = PerformanceSnapshot.Empty with { Memory = memory };

        PerformanceDevicePresentation presentation = PerformanceDevicePresentationFactory.Create(snapshot)
            .Single(static device => device.Kind == PerformanceDeviceKind.Memory);
        Dictionary<string, string> values = presentation.Statistics.ToArray().ToDictionary(
            static statistic => statistic.Label,
            static statistic => statistic.Value);

        Assert.Equal("48.0 GB (3.0 GB)", values["In use (Compressed)"]);
        Assert.Equal("68.0/191 GB", values["Committed"]);
        Assert.Equal("6000 MT/s", values["Speed"]);
        Assert.Equal("4 of 4", values["Slots used"]);
        Assert.Equal("DIMM", values["Form factor"]);
        Assert.Equal("648 MB", values["Hardware reserved"]);
        Assert.DoesNotContain("Commit limit", values.Keys);
        Assert.DoesNotContain("Installed", values.Keys);
    }

    [Fact]
    public void MemorySerialNumberSettingDefaultsOffAndRoundTrips()
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            string.Concat(Guid.NewGuid().ToString("N"), ".xml"));
        try
        {
            AppSettings defaults = new();
            Assert.False(defaults.ShowMemoryModuleSerialNumbers);

            defaults.Autosave = false;
            defaults.ShowMemoryModuleSerialNumbers = true;
            defaults.Save(settingsPath);
            AppSettings loaded = AppSettings.LoadOrDefault(settingsPath);

            Assert.True(loaded.ShowMemoryModuleSerialNumbers);
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }
}
