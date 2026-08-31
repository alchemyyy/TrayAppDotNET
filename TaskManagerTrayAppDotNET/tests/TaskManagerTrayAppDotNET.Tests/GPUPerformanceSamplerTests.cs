using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class GPUPerformanceSamplerTests
{
    [Fact]
    public void ParsesAdapterMemoryCounterIdentity()
    {
        const string instanceName = "luid_0x00000001_0x0000C85A_phys_2";

        bool parsed = GPUAdapterCounterInstanceParser.TryParse(
            instanceName,
            out ulong adapterLUID,
            out int physicalAdapterIndex);

        Assert.True(parsed);
        Assert.Equal(expected: 0x000000010000C85AUL, adapterLUID);
        Assert.Equal(expected: 2, physicalAdapterIndex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("luid_bad")]
    [InlineData("luid_0x1_0x2_phys_bad")]
    public void RejectsMalformedAdapterMemoryCounterIdentity(string instanceName) =>
        Assert.False(GPUAdapterCounterInstanceParser.TryParse(instanceName, out _, out _));

    [Fact]
    public void AggregatesProcessInstancesByPhysicalEngine()
    {
        GPUEngineCounterSample[] samples =
        [
            new(EngineIndex: 2, Name: "3D", UtilizationPercent: 65),
            new(EngineIndex: 2, Name: "3D", UtilizationPercent: 50),
            new(EngineIndex: 4, Name: "Copy", UtilizationPercent: 12.5),
            new(EngineIndex: -1, Name: "Invalid", UtilizationPercent: 50),
            new(EngineIndex: 5, Name: "Invalid", double.NaN)
        ];

        GPUPerformanceEngineSnapshot[] engines = GPUPerformanceSampler.AggregateEngineSamples(samples);

        Assert.Equal(expected: 2, engines.Length);
        Assert.Equal(expected: 2, engines[0].EngineIndex);
        Assert.Equal(expected: "3D", engines[0].Name);
        Assert.Equal(expected: 100, engines[0].UtilizationPercent);
        Assert.Equal(expected: 4, engines[1].EngineIndex);
        Assert.Equal(expected: "Copy", engines[1].Name);
        Assert.Equal(expected: 12.5, engines[1].UtilizationPercent);
    }

    [Fact]
    public void PCIFallbackDeviceIDRetainsHardwareMetadata()
    {
        string deviceID = GPUPerformanceSampler.CreatePCIFallbackDeviceID(
            vendorID: 0x10DE,
            deviceID: 0x2684,
            subsystemID: 0x16A310DE,
            revision: 0xA1,
            displayIndex: 1,
            physicalAdapterIndex: 0);

        Assert.Equal(expected: "gpu:pci:10DE:2684:16A310DE:A1:1:0", deviceID);
    }

    [Fact]
    public void CanonicalizesHardwarePNPKeyWithoutVolatileControlSetPath()
    {
        const string hardwarePNPKey =
            @"\REGISTRY\MACHINE\SYSTEM\ControlSet007\Enum\PCI\VEN_10DE&DEV_2702\4&ABC&0&0009\Device Parameters";

        string canonicalKey = D3DKMTAdapterIdentityReader.CanonicalizeHardwarePNPKey(
            hardwarePNPKey);

        Assert.Equal(expected: "pci/ven_10de&dev_2702/4&abc&0&0009", canonicalKey);
    }

    [Fact]
    public void UniquePNPKeyDoesNotDependOnAdapterGUIDOrEnumerationOrder()
    {
        GPUDeviceIdentity[] first =
        [
            new(
                new GPUAdapterKey(LUID: 10, PhysicalAdapterIndex: 0),
                HardwarePNPKey: "pci/adapter-a",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                FallbackDeviceID: "fallback-a")
        ];
        GPUDeviceIdentity[] second =
        [
            new(
                new GPUAdapterKey(LUID: 20, PhysicalAdapterIndex: 0),
                HardwarePNPKey: "pci/adapter-a",
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                FallbackDeviceID: "fallback-b")
        ];

        string firstDeviceID = Assert.Single(GPUPerformanceSampler.ResolveStableDeviceIDs(first));
        string secondDeviceID = Assert.Single(GPUPerformanceSampler.ResolveStableDeviceIDs(second));

        Assert.Equal(expected: "gpu:pnp:pci/adapter-a", firstDeviceID);
        Assert.Equal(firstDeviceID, secondDeviceID);
    }

    [Fact]
    public void SharedPhysicalPNPKeyCollapsesAcrossAdapterLUIDs()
    {
        GPUDeviceIdentity[] identities =
        [
            new(
                new GPUAdapterKey(LUID: 10, PhysicalAdapterIndex: 0),
                HardwarePNPKey: "pci/shared-adapter",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                FallbackDeviceID: "fallback-a"),
            new(
                new GPUAdapterKey(LUID: 20, PhysicalAdapterIndex: 0),
                HardwarePNPKey: "pci/shared-adapter",
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                FallbackDeviceID: "fallback-b")
        ];

        string[] deviceIDs = GPUPerformanceSampler.ResolveStableDeviceIDs(identities);

        Assert.Equal(expected: "gpu:pnp:pci/shared-adapter", deviceIDs[0]);
        Assert.Equal(deviceIDs[0], deviceIDs[1]);
    }

    [Fact]
    public void AdapterGUIDPrecedesEnumerationBasedFallbackWhenPNPKeyIsUnavailable()
    {
        GPUDeviceIdentity[] identities =
        [
            new(
                new GPUAdapterKey(LUID: 10, PhysicalAdapterIndex: 2),
                HardwarePNPKey: null,
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                FallbackDeviceID: "gpu:pci:enumeration-dependent")
        ];

        string deviceID = Assert.Single(GPUPerformanceSampler.ResolveStableDeviceIDs(identities));

        Assert.Equal(
            expected: "gpu:guid:33333333333333333333333333333333",
            deviceID);
    }

    [Fact]
    public void CounterOnlyTuplesDoNotBecomeDisplayDeviceKeys()
    {
        GPUAdapterMetadata[] adapters =
        [
            new(LUID: 10, DisplayIndex: 0, Name: "GPU", VendorID: 1, DeviceID: 2, SubsystemID: 3, Revision: 4,
                DedicatedMemoryCapacityBytes: 5, SharedMemoryCapacityBytes: 6, HasValue: true)
        ];
        GPUAdapterKey[] counterKeys =
        [
            new(LUID: 10, PhysicalAdapterIndex: 1),
            new(LUID: 20, PhysicalAdapterIndex: 0)
        ];

        GPUAdapterKey[] displayKeys = GPUPerformanceSampler.ResolveDisplayDeviceKeys(
            adapters,
            counterKeys);

        Assert.Equal(
            new GPUAdapterKey[] { new(LUID: 10, PhysicalAdapterIndex: 0), new(LUID: 10, PhysicalAdapterIndex: 1) },
            displayKeys);
    }

    [Theory]
    [InlineData(@"ROOT\SudoMaker\0000", true)]
    [InlineData(@"SWD\RemoteDisplayEnum\VirtualDisplay", true)]
    [InlineData(@"PCI\VEN_10DE&DEV_2702\4&ABC&0&0009", false)]
    [InlineData(@"ACPI\QCOM0D50\0", false)]
    [InlineData(null, false)]
    public void ClassifiesSoftwareEnumeratedDisplayPNPKeys(string? pnpKey, bool expectedVirtual) =>
        Assert.Equal(expectedVirtual, GPUPerformanceSampler.IsVirtualDisplayPNPKey(pnpKey));

    [Fact]
    public void NativeD3DKMTIdentitiesAreStableWithinTheAdapterLifetime()
    {
        GPUAdapterMetadata[] adapters = DXGIAdapterEnumerator.Enumerate();
        if (adapters.Length == 0) return;

        GPUDeviceIdentity[] identities = new GPUDeviceIdentity[adapters.Length];
        for (int adapterIndex = 0; adapterIndex < adapters.Length; adapterIndex++)
        {
            GPUAdapterMetadata adapter = adapters[adapterIndex];
            GPUAdapterKey key = new(adapter.LUID, PhysicalAdapterIndex: 0);
            GPUAdapterPersistentIdentity firstIdentity = D3DKMTAdapterIdentityReader.Read(key);
            GPUAdapterPersistentIdentity secondIdentity = D3DKMTAdapterIdentityReader.Read(key);

            Assert.Equal(firstIdentity, secondIdentity);
            Assert.True(
                !string.IsNullOrWhiteSpace(firstIdentity.HardwarePNPKey)
                || firstIdentity.UniqueAdapterGUID != Guid.Empty);
            identities[adapterIndex] = new GPUDeviceIdentity(
                key,
                firstIdentity.HardwarePNPKey,
                firstIdentity.UniqueAdapterGUID,
                GPUPerformanceSampler.CreatePCIFallbackDeviceID(
                    adapter.VendorID,
                    adapter.DeviceID,
                    adapter.SubsystemID,
                    adapter.Revision,
                    adapter.DisplayIndex,
                    physicalAdapterIndex: 0));
        }

        string[] deviceIDs = GPUPerformanceSampler.ResolveStableDeviceIDs(identities);

        Assert.True(
            deviceIDs.Distinct(StringComparer.OrdinalIgnoreCase).Count() <= deviceIDs.Length);
    }

    [Fact]
    public void NativeSamplerReturnsOneCardPerDistinctHardwareIdentity()
    {
        using GPUPerformanceSampler sampler = new();

        _ = sampler.Sample();
        GPUPerformanceSnapshot[] snapshots = sampler.Sample();

        Assert.Equal(
            snapshots.Length,
            snapshots.Select(static snapshot => snapshot.DeviceID)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.DoesNotContain(
            snapshots,
            static snapshot => snapshot.DeviceID.StartsWith(
                                   value: "gpu:pnp:root/",
                                   StringComparison.OrdinalIgnoreCase)
                               || snapshot.DeviceID.StartsWith(
                                   value: "gpu:pnp:swd/",
                                   StringComparison.OrdinalIgnoreCase));
    }
}
