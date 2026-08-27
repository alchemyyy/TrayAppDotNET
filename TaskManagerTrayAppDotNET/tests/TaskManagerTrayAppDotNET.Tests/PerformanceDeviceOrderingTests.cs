using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceDeviceOrderingTests
{
    [Fact]
    public void DefaultPriorityMatchesRequestedDeviceKindOrder()
    {
        Assert.Equal(
            [
                PerformanceDeviceKind.CPU,
                PerformanceDeviceKind.Memory,
                PerformanceDeviceKind.GPU,
                PerformanceDeviceKind.Network,
                PerformanceDeviceKind.Disk
            ],
            PerformanceDeviceOrdering.DefaultPriority);
    }

    [Fact]
    public void NormalizePriorityPreservesValidFirstOccurrencesAndAppendsMissingKinds()
    {
        List<PerformanceDeviceKind> normalized = PerformanceDeviceOrdering.NormalizePriority(
        [
            PerformanceDeviceKind.Disk,
            (PerformanceDeviceKind)int.MaxValue,
            PerformanceDeviceKind.CPU,
            PerformanceDeviceKind.Disk
        ]);

        Assert.Equal(
            [
                PerformanceDeviceKind.Disk,
                PerformanceDeviceKind.CPU,
                PerformanceDeviceKind.Memory,
                PerformanceDeviceKind.GPU,
                PerformanceDeviceKind.Network
            ],
            normalized);
    }

    [Fact]
    public void ResolveUsesPriorityAndStableFallbackSortKeysWithoutAnExplicitOrder()
    {
        PerformanceDeviceOrderItem[] items =
        [
            Item("disk:1", PerformanceDeviceKind.Disk, 1),
            Item("gpu:1", PerformanceDeviceKind.GPU, 1),
            Item("network:0", PerformanceDeviceKind.Network, 0),
            Item("memory", PerformanceDeviceKind.Memory, 0),
            Item("gpu:0", PerformanceDeviceKind.GPU, 0),
            Item("cpu", PerformanceDeviceKind.CPU, 0),
            Item("disk:0", PerformanceDeviceKind.Disk, 0)
        ];

        List<PerformanceDeviceOrderItem> resolved = PerformanceDeviceOrdering.Resolve(
            items,
            PerformanceDeviceOrdering.CreateDefaultPriority(),
            []);

        Assert.Equal(
            ["cpu", "memory", "gpu:0", "gpu:1", "network:0", "disk:0", "disk:1"],
            resolved.Select(static (PerformanceDeviceOrderItem item) => item.ID));
    }

    [Fact]
    public void ResolveHonorsACustomPriorityForUnconfiguredDevices()
    {
        PerformanceDeviceOrderItem[] items =
        [
            Item("cpu", PerformanceDeviceKind.CPU, 0),
            Item("memory", PerformanceDeviceKind.Memory, 0),
            Item("gpu:0", PerformanceDeviceKind.GPU, 0),
            Item("network:0", PerformanceDeviceKind.Network, 0),
            Item("disk:0", PerformanceDeviceKind.Disk, 0)
        ];
        PerformanceDeviceKind[] priority =
        [
            PerformanceDeviceKind.Disk,
            PerformanceDeviceKind.Network,
            PerformanceDeviceKind.GPU,
            PerformanceDeviceKind.Memory,
            PerformanceDeviceKind.CPU
        ];

        List<PerformanceDeviceOrderItem> resolved = PerformanceDeviceOrdering.Resolve(items, priority, []);

        Assert.Equal(
            ["disk:0", "network:0", "gpu:0", "memory", "cpu"],
            resolved.Select(static (PerformanceDeviceOrderItem item) => item.ID));
    }

    [Fact]
    public void ResolveMergesNewDevicesBesideTheirKindWithoutReorderingExplicitRows()
    {
        PerformanceDeviceOrderItem[] items =
        [
            Item("cpu", PerformanceDeviceKind.CPU, 0),
            Item("memory", PerformanceDeviceKind.Memory, 0),
            Item("gpu:0", PerformanceDeviceKind.GPU, 0),
            Item("gpu:1", PerformanceDeviceKind.GPU, 1),
            Item("network:0", PerformanceDeviceKind.Network, 0),
            Item("network:1", PerformanceDeviceKind.Network, 1),
            Item("disk:0", PerformanceDeviceKind.Disk, 0),
            Item("disk:1", PerformanceDeviceKind.Disk, 1)
        ];
        string[] explicitDeviceIDs =
            ["disk:1", "cpu", "memory", "gpu:0", "network:0", "disk:0"];

        List<PerformanceDeviceOrderItem> resolved = PerformanceDeviceOrdering.Resolve(
            items,
            PerformanceDeviceOrdering.CreateDefaultPriority(),
            explicitDeviceIDs);

        Assert.Equal(
            ["disk:1", "cpu", "memory", "gpu:0", "gpu:1", "network:0", "network:1", "disk:0"],
            resolved.Select(static (PerformanceDeviceOrderItem item) => item.ID));
        Assert.Equal(
            explicitDeviceIDs,
            resolved
                .Where(static (PerformanceDeviceOrderItem item) => item.ID != "gpu:1" && item.ID != "network:1")
                .Select(static (PerformanceDeviceOrderItem item) => item.ID));
    }

    [Fact]
    public void ResolveUsesPriorityAnchorWhenADeviceKindHasNoExplicitRow()
    {
        PerformanceDeviceOrderItem[] items =
        [
            Item("disk:0", PerformanceDeviceKind.Disk, 0),
            Item("cpu", PerformanceDeviceKind.CPU, 0),
            Item("memory", PerformanceDeviceKind.Memory, 0),
            Item("gpu:0", PerformanceDeviceKind.GPU, 0),
            Item("network:0", PerformanceDeviceKind.Network, 0)
        ];
        string[] explicitDeviceIDs = ["disk:0", "cpu", "memory", "network:0"];

        List<PerformanceDeviceOrderItem> resolved = PerformanceDeviceOrdering.Resolve(
            items,
            PerformanceDeviceOrdering.CreateDefaultPriority(),
            explicitDeviceIDs);

        Assert.Equal(
            ["disk:0", "cpu", "memory", "gpu:0", "network:0"],
            resolved.Select(static (PerformanceDeviceOrderItem item) => item.ID));
    }

    [Fact]
    public void MoveAtPreservesStaleIDsAtTheirPriorLiveAnchor()
    {
        PerformanceDeviceOrderItem[] resolvedItems =
        [
            Item("cpu", PerformanceDeviceKind.CPU, 0),
            Item("memory", PerformanceDeviceKind.Memory, 0),
            Item("gpu:0", PerformanceDeviceKind.GPU, 0),
            Item("network:0", PerformanceDeviceKind.Network, 0)
        ];
        string[] explicitDeviceIDs = ["cpu", "gpu:stale", "memory", "gpu:0", "network:0"];

        List<string> moved = PerformanceDeviceOrdering.MoveAt(
            resolvedItems,
            explicitDeviceIDs,
            sourceIndex: 2,
            targetIndex: 0);

        Assert.Equal(["gpu:0", "cpu", "gpu:stale", "memory", "network:0"], moved);
    }

    [Fact]
    public void MoveFindsAStableIDAndMaterializesTheVisibleOrder()
    {
        PerformanceDeviceOrderItem[] resolvedItems =
        [
            Item("cpu", PerformanceDeviceKind.CPU, 0),
            Item("memory", PerformanceDeviceKind.Memory, 0),
            Item("disk:0", PerformanceDeviceKind.Disk, 0)
        ];

        List<string> moved = PerformanceDeviceOrdering.Move(
            resolvedItems,
            [],
            "disk:0",
            targetIndex: 0);

        Assert.Equal(["disk:0", "cpu", "memory"], moved);
    }

    [Fact]
    public void MergeVisibleOrderRetainsDisconnectedIDsAtTheirPriorAnchor()
    {
        string[] visibleDeviceIDs = ["gpu:0", "cpu", "memory", "disk:0"];
        string[] explicitDeviceIDs = ["cpu", "gpu:stale", "memory", "gpu:0", "disk:0"];

        List<string> merged = PerformanceDeviceOrdering.MergeVisibleOrder(
            visibleDeviceIDs,
            explicitDeviceIDs);

        Assert.Equal(["gpu:0", "cpu", "gpu:stale", "memory", "disk:0"], merged);
    }

    private static PerformanceDeviceOrderItem Item(
        string deviceID,
        PerformanceDeviceKind kind,
        int sortKey) =>
        new(deviceID, kind, sortKey);
}
