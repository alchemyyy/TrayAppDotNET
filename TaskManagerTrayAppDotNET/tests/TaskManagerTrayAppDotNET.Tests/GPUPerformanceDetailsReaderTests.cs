using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class GPUPerformanceDetailsReaderTests
{
    [Fact]
    public void SelectsOfficialEngineLanesInStableOrder()
    {
        GPUAdapterEngineIdentity[] catalog =
        [
            new(0, "Video Decode"),
            new(1, "3D"),
            new(2, "Video Encode"),
            new(3, "Copy"),
            new(4, "Compute")
        ];
        GPUPerformanceEngineSnapshot[] liveEngines =
        [
            new(0, "Video Decode", 7.5),
            new(1, "3D", 30),
            new(3, "Copy", 5)
        ];

        GPUPerformanceDetailEngineSnapshot[] engines =
            GPUPerformanceDetailsReader.SelectEngineSlots(
                catalog,
                liveEngines,
                true);

        Assert.Collection(
            engines,
            engine => AssertEngine(engine, 1, "3D", 30),
            engine => AssertEngine(engine, 3, "Copy", 5),
            engine => AssertEngine(engine, 2, "Video Encode", 0),
            engine => AssertEngine(engine, 0, "Video Decode", 7.5));
    }

    [Fact]
    public void FallsBackToLiveEngineIdentitiesAndSanitizesValues()
    {
        GPUPerformanceEngineSnapshot[] liveEngines =
        [
            new(8, "Compute", double.NaN),
            new(6, "Copy#1", 125),
            new(2, "Other", -15)
        ];

        GPUPerformanceDetailEngineSnapshot[] engines =
            GPUPerformanceDetailsReader.SelectEngineSlots(
                [],
                liveEngines,
                true);

        Assert.Equal(3, engines.Length);
        AssertEngine(engines[0], 6, "Copy", 100);
        AssertEngine(engines[1], 8, "Compute", 0);
        AssertEngine(engines[2], 2, "Other", 0);
    }

    [Fact]
    public void DoesNotSelectDuplicateEngineCategoriesForDefaultLanes()
    {
        GPUAdapterEngineIdentity[] catalog =
        [
            new(0, "3D"),
            new(1, "3D"),
            new(2, "Copy")
        ];

        GPUPerformanceDetailEngineSnapshot[] engines =
            GPUPerformanceDetailsReader.SelectEngineSlots(catalog, [], true);

        Assert.Equal(["3D", "Copy"], engines.Select(static engine => engine.Name));
    }

    [Fact]
    public void MetadataFailuresRetryBeforeSuccessfulMetadataRefreshes()
    {
        const long CurrentTick = 10_000;

        long failureRefreshTick = GPUPerformanceDetailsReader.CalculateNextMetadataRefreshTick(
            CurrentTick,
            true);
        long successRefreshTick = GPUPerformanceDetailsReader.CalculateNextMetadataRefreshTick(
            CurrentTick,
            false);

        Assert.Equal(CurrentTick + 30_000, failureRefreshTick);
        Assert.Equal(CurrentTick + 300_000, successRefreshTick);
    }

    [Fact]
    public void FailedMetadataRefreshRetainsPreviouslyValidFields()
    {
        GPUAdapterHardwareMetadata previousMetadata = new(
            true,
            "32.0.15.9660",
            new DateOnly(2026, 5, 22),
            "12",
            "12.2",
            "PCI bus 33, device 0, function 0",
            true,
            347_078_656,
            ReadOnlyMemory<GPUAdapterEngineIdentity>.Empty);

        GPUAdapterHardwareMetadata selectedMetadata =
            GPUPerformanceDetailsReader.SelectMetadataAfterRefresh(
                previousMetadata,
                GPUAdapterHardwareMetadata.Empty,
                true);

        Assert.Same(previousMetadata, selectedMetadata);
    }

    [Fact]
    public void SuccessfulMetadataRefreshReplacesPreviousFields()
    {
        GPUAdapterHardwareMetadata previousMetadata = new(
            true,
            "1.0.0.0",
            null,
            "12",
            "12.1",
            string.Empty,
            false,
            0,
            ReadOnlyMemory<GPUAdapterEngineIdentity>.Empty);
        GPUAdapterHardwareMetadata refreshedMetadata = previousMetadata with
        {
            DriverVersion = "2.0.0.0",
            FeatureLevel = "12.2"
        };

        GPUAdapterHardwareMetadata selectedMetadata =
            GPUPerformanceDetailsReader.SelectMetadataAfterRefresh(
                previousMetadata,
                refreshedMetadata,
                false);

        Assert.Same(refreshedMetadata, selectedMetadata);
    }

    [Theory]
    [InlineData("3d", "3D")]
    [InlineData("video_decode", "Video Decode")]
    [InlineData("VideoEncode#4", "Video Encode")]
    [InlineData("optical-flow", "Optical Flow")]
    [InlineData("custom engine", "custom engine")]
    [InlineData(null, "GPU Engine")]
    public void NormalizesKernelAndCounterEngineNames(string? input, string expected)
    {
        Assert.Equal(expected, GPUPerformanceDetailsReader.NormalizeEngineName(input));
    }

    [Theory]
    [InlineData(16_000UL, 15_500UL, 500UL)]
    [InlineData(15_500UL, 16_000UL, 0UL)]
    [InlineData(16_000UL, 16_000UL, 0UL)]
    public void CalculatesOnlyInstalledMemoryHiddenFromVisibleVRAM(
        ulong installedBytes,
        ulong visibleBytes,
        ulong expectedReservedBytes)
    {
        Assert.Equal(
            expectedReservedBytes,
            GPUAdapterNativeDetailsReader.CalculateHardwareReservedMemory(
                installedBytes,
                visibleBytes));
    }

    [Fact]
    public void FormatsPhysicalPCILocation()
    {
        Assert.Equal(
            "PCI bus 33, device 0, function 2",
            GPUAdapterNativeDetailsReader.FormatPhysicalLocation(33, 0, 2));
    }

    [Fact]
    public void NativeMetadataMatchesEveryDXGIHardwareAdapter()
    {
        GPUAdapterMetadata[] adapters = DXGIAdapterEnumerator.Enumerate();
        for (int adapterIndex = 0; adapterIndex < adapters.Length; adapterIndex++)
        {
            GPUAdapterMetadata adapter = adapters[adapterIndex];
            GPUAdapterKey key = new(adapter.LUID, 0);

            GPUAdapterHardwareMetadata metadata = GPUAdapterNativeDetailsReader.ReadMetadata(
                key,
                adapter.DedicatedMemoryCapacityBytes,
                out string? error);
            GPUDeviceMetadata deviceMetadata = GPUDevicePropertyReader.Read(key, out string? deviceError);

            Assert.True(metadata.HasMetadata, error);
            Assert.True(deviceMetadata.HasValue, deviceError);
            Assert.False(string.IsNullOrWhiteSpace(deviceMetadata.DriverVersion));
            Assert.NotNull(deviceMetadata.DriverDate);
            Assert.Equal(deviceMetadata.DriverVersion, metadata.DriverVersion);
            Assert.Equal(deviceMetadata.DriverDate, metadata.DriverDate);
            Assert.NotEmpty(metadata.EngineCatalog.ToArray());
            if (metadata.DirectXVersion.Length > 0)
            {
                Assert.Equal("12", metadata.DirectXVersion);
                Assert.False(string.IsNullOrWhiteSpace(metadata.FeatureLevel));
            }

            bool hasTemperature = GPUAdapterNativeDetailsReader.TryReadTemperature(
                key,
                out double temperatureCelsius);
            if (hasTemperature)
                Assert.InRange(temperatureCelsius, 0.1, 200);
        }
    }

    private static void AssertEngine(
        GPUPerformanceDetailEngineSnapshot engine,
        int expectedIndex,
        string expectedName,
        double expectedUtilization)
    {
        Assert.Equal(expectedIndex, engine.EngineIndex);
        Assert.Equal(expectedName, engine.Name);
        Assert.True(engine.HasUtilizationSample);
        Assert.Equal(expectedUtilization, engine.UtilizationPercent, precision: 8);
    }
}
