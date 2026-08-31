using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class WindowsServiceConfigurationCacheTests
{
    [Fact]
    public void CacheUsesCaseInsensitiveServiceNames()
    {
        WindowsServiceConfigurationCache cache = new();
        WindowsServiceConfiguration configuration = new(
            Description: "Example description",
            Group: "ExampleGroup",
            WindowsServiceStartType.Automatic);

        cache.Store(serviceName: "ExampleService", configuration);

        Assert.True(cache.TryGet(serviceName: "exampleservice", out WindowsServiceConfiguration cached));
        Assert.Equal(configuration, cached);
    }

    [Fact]
    public void InvalidateRemovesOneConfiguration()
    {
        WindowsServiceConfigurationCache cache = new();
        cache.Store(
            serviceName: "ExampleService",
            new WindowsServiceConfiguration(
                string.Empty,
                string.Empty,
                WindowsServiceStartType.OnDemand));

        cache.Invalidate("EXAMPLESERVICE");

        Assert.False(cache.TryGet(serviceName: "ExampleService", out _));
    }

    [Fact]
    public void RetainOnlyPrunesServicesNoLongerEnumerated()
    {
        WindowsServiceConfigurationCache cache = new();
        WindowsServiceConfiguration retained = new(
            Description: "Retained",
            string.Empty,
            WindowsServiceStartType.Automatic);
        cache.Store(serviceName: "RetainedService", retained);
        cache.Store(
            serviceName: "RemovedService",
            new WindowsServiceConfiguration(
                Description: "Removed",
                string.Empty,
                WindowsServiceStartType.Disabled));
        HashSet<string> currentServices = new(StringComparer.OrdinalIgnoreCase) { "retainedservice" };

        cache.RetainOnly(currentServices);

        Assert.True(cache.TryGet(serviceName: "RetainedService", out WindowsServiceConfiguration cached));
        Assert.Equal(retained, cached);
        Assert.False(cache.TryGet(serviceName: "RemovedService", out _));
    }
}
