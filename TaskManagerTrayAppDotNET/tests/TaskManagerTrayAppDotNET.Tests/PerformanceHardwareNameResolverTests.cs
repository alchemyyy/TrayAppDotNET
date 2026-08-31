using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceHardwareNameResolverTests
{
    [Fact]
    public void ResolveAppliesCapturedReplacementsInConfiguredOrder()
    {
        PerformanceHardwareNameResolver resolver = PerformanceHardwareNameResolver.Create(
        [
            new PerformanceHardwareNameReplacementRule
            {
                DeviceKind = PerformanceDeviceKind.Network,
                MatchPattern = "^Intel\\(R\\) (?<Adapter>.+)$",
                Replacement = "${Adapter}"
            },
            new PerformanceHardwareNameReplacementRule
            {
                DeviceKind = PerformanceDeviceKind.Network,
                MatchPattern = "^Ethernet Converged Network Adapter (.+)$",
                Replacement = "Adapter $1"
            }
        ]);

        string resolved = resolver.Resolve(
            PerformanceDeviceKind.Network,
            hardwareName: "intel(R) Ethernet Converged Network Adapter X540-T2");

        Assert.Equal(expected: "Adapter X540-T2", resolved);
    }

    [Fact]
    public void ResolveOnlyAppliesRulesForTheTargetDeviceKind()
    {
        PerformanceHardwareNameResolver resolver = PerformanceHardwareNameResolver.Create(
        [
            new PerformanceHardwareNameReplacementRule
            {
                DeviceKind = PerformanceDeviceKind.Network, MatchPattern = "Adapter", Replacement = "NIC"
            }
        ]);

        Assert.Equal(
            expected: "Test NIC",
            resolver.Resolve(PerformanceDeviceKind.Network, hardwareName: "Test Adapter"));
        Assert.Equal(
            expected: "Test Adapter",
            resolver.Resolve(PerformanceDeviceKind.GPU, hardwareName: "Test Adapter"));
    }

    [Fact]
    public void InvalidRegexDoesNotPreventLaterRulesFromApplying()
    {
        PerformanceHardwareNameResolver resolver = PerformanceHardwareNameResolver.Create(
        [
            new PerformanceHardwareNameReplacementRule
            {
                DeviceKind = PerformanceDeviceKind.Disk, MatchPattern = "(", Replacement = "Broken"
            },
            new PerformanceHardwareNameReplacementRule
            {
                DeviceKind = PerformanceDeviceKind.Disk, MatchPattern = "^Samsung (.+)$", Replacement = "$1"
            }
        ]);

        Assert.Equal(
            expected: "SSD 990 PRO",
            resolver.Resolve(PerformanceDeviceKind.Disk, hardwareName: "Samsung SSD 990 PRO"));
    }
}
