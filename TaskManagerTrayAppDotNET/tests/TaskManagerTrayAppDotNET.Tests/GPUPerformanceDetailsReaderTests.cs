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
            new(EngineIndex: 0, Name: "Video Decode"),
            new(EngineIndex: 1, Name: "3D"),
            new(EngineIndex: 2, Name: "Video Encode"),
            new(EngineIndex: 3, Name: "Copy"),
            new(EngineIndex: 4, Name: "Compute")
        ];
        GPUPerformanceEngineSnapshot[] liveEngines =
        [
            new(EngineIndex: 0, Name: "Video Decode", UtilizationPercent: 7.5),
            new(EngineIndex: 1, Name: "3D", UtilizationPercent: 30),
            new(EngineIndex: 3, Name: "Copy", UtilizationPercent: 5)
        ];

        GPUPerformanceDetailEngineSnapshot[] engines =
            GPUPerformanceDetailsReader.SelectEngineSlots(
                catalog,
                liveEngines,
                hasUtilizationSample: true);

        Assert.Collection(
            engines,
            engine => AssertEngine(engine, expectedIndex: 1, expectedName: "3D", expectedUtilization: 30),
            engine => AssertEngine(engine, expectedIndex: 3, expectedName: "Copy", expectedUtilization: 5),
            engine => AssertEngine(engine, expectedIndex: 2, expectedName: "Video Encode", expectedUtilization: 0),
            engine => AssertEngine(engine, expectedIndex: 0, expectedName: "Video Decode", expectedUtilization: 7.5));
    }

    [Fact]
    public void FallsBackToLiveEngineIdentitiesAndSanitizesValues()
    {
        GPUPerformanceEngineSnapshot[] liveEngines =
        [
            new(EngineIndex: 8, Name: "Compute", double.NaN),
            new(EngineIndex: 6, Name: "Copy#1", UtilizationPercent: 125),
            new(EngineIndex: 2, Name: "Other", UtilizationPercent: -15)
        ];

        GPUPerformanceDetailEngineSnapshot[] engines =
            GPUPerformanceDetailsReader.SelectEngineSlots(
                [],
                liveEngines,
                hasUtilizationSample: true);

        Assert.Equal(expected: 3, engines.Length);
        AssertEngine(engines[0], expectedIndex: 6, expectedName: "Copy", expectedUtilization: 100);
        AssertEngine(engines[1], expectedIndex: 8, expectedName: "Compute", expectedUtilization: 0);
        AssertEngine(engines[2], expectedIndex: 2, expectedName: "Other", expectedUtilization: 0);
    }

    [Fact]
    public void DoesNotSelectDuplicateEngineCategoriesForDefaultLanes()
    {
        GPUAdapterEngineIdentity[] catalog =
        [
            new(EngineIndex: 0, Name: "3D"),
            new(EngineIndex: 1, Name: "3D"),
            new(EngineIndex: 2, Name: "Copy")
        ];

        GPUPerformanceDetailEngineSnapshot[] engines =
            GPUPerformanceDetailsReader.SelectEngineSlots(catalog, [], hasUtilizationSample: true);

        Assert.Equal(["3D", "Copy"], engines.Select(static engine => engine.Name));
    }

    [Fact]
    public void MetadataFailuresRetryBeforeSuccessfulMetadataRefreshes()
    {
        const long CurrentTick = 10_000;

        long failureRefreshTick = GPUPerformanceDetailsReader.CalculateNextMetadataRefreshTick(
            CurrentTick,
            hasError: true);
        long successRefreshTick = GPUPerformanceDetailsReader.CalculateNextMetadataRefreshTick(
            CurrentTick,
            hasError: false);

        Assert.Equal(CurrentTick + 30_000, failureRefreshTick);
        Assert.Equal(CurrentTick + 300_000, successRefreshTick);
    }

    [Fact]
    public void FailedMetadataRefreshRetainsPreviouslyValidFields()
    {
        GPUAdapterHardwareMetadata previousMetadata = new(
            HasMetadata: true,
            DriverVersion: "32.0.15.9660",
            new DateOnly(year: 2026, month: 5, day: 22),
            DirectXVersion: "12",
            FeatureLevel: "12.2",
            PhysicalLocation: "PCI bus 33, device 0, function 0",
            HasHardwareReservedMemoryData: true,
            HardwareReservedMemoryBytes: 347_078_656,
            ReadOnlyMemory<GPUAdapterEngineIdentity>.Empty);

        GPUAdapterHardwareMetadata selectedMetadata =
            GPUPerformanceDetailsReader.SelectMetadataAfterRefresh(
                previousMetadata,
                GPUAdapterHardwareMetadata.Empty,
                hasError: true);

        Assert.Same(previousMetadata, selectedMetadata);
    }

    [Fact]
    public void SuccessfulMetadataRefreshReplacesPreviousFields()
    {
        GPUAdapterHardwareMetadata previousMetadata = new(
            HasMetadata: true,
            DriverVersion: "1.0.0.0",
            DriverDate: null,
            DirectXVersion: "12",
            FeatureLevel: "12.1",
            string.Empty,
            HasHardwareReservedMemoryData: false,
            HardwareReservedMemoryBytes: 0,
            ReadOnlyMemory<GPUAdapterEngineIdentity>.Empty);
        GPUAdapterHardwareMetadata refreshedMetadata = previousMetadata with
        {
            DriverVersion = "2.0.0.0", FeatureLevel = "12.2"
        };

        GPUAdapterHardwareMetadata selectedMetadata =
            GPUPerformanceDetailsReader.SelectMetadataAfterRefresh(
                previousMetadata,
                refreshedMetadata,
                hasError: false);

        Assert.Same(refreshedMetadata, selectedMetadata);
    }

    [Theory]
    [InlineData("3d", "3D")]
    [InlineData("video_decode", "Video Decode")]
    [InlineData("VideoEncode#4", "Video Encode")]
    [InlineData("optical-flow", "Optical Flow")]
    [InlineData("custom engine", "custom engine")]
    [InlineData(null, "GPU Engine")]
    public void NormalizesKernelAndCounterEngineNames(string? input, string expected) =>
        Assert.Equal(expected, GPUPerformanceDetailsReader.NormalizeEngineName(input));

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
            expected: "PCI bus 33, device 0, function 2",
            GPUAdapterNativeDetailsReader.FormatPhysicalLocation(busNumber: 33, deviceNumber: 0, functionNumber: 2));
    }

    [Fact]
    public void NativeMetadataMatchesEveryDXGIHardwareAdapter()
    {
        GPUAdapterMetadata[] adapters = DXGIAdapterEnumerator.Enumerate();
        for (int adapterIndex = 0; adapterIndex < adapters.Length; adapterIndex++)
        {
            GPUAdapterMetadata adapter = adapters[adapterIndex];
            GPUAdapterKey key = new(adapter.LUID, PhysicalAdapterIndex: 0);

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
                Assert.Equal(expected: "12", metadata.DirectXVersion);
                Assert.False(string.IsNullOrWhiteSpace(metadata.FeatureLevel));
            }

            bool hasTemperature = GPUAdapterNativeDetailsReader.TryReadTemperature(
                key,
                out double temperatureCelsius);
            if (hasTemperature)
                Assert.InRange(temperatureCelsius, low: 0.1, high: 200);
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
