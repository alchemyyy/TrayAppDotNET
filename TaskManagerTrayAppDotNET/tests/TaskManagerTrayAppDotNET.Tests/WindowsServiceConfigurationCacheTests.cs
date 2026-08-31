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
            "Example description",
            "ExampleGroup",
            WindowsServiceStartType.Automatic);

        cache.Store("ExampleService", configuration);

        Assert.True(cache.TryGet("exampleservice", out WindowsServiceConfiguration cached));
        Assert.Equal(configuration, cached);
    }

    [Fact]
    public void InvalidateRemovesOneConfiguration()
    {
        WindowsServiceConfigurationCache cache = new();
        cache.Store(
            "ExampleService",
            new WindowsServiceConfiguration(
                string.Empty,
                string.Empty,
                WindowsServiceStartType.OnDemand));

        cache.Invalidate("EXAMPLESERVICE");

        Assert.False(cache.TryGet("ExampleService", out _));
    }

    [Fact]
    public void RetainOnlyPrunesServicesNoLongerEnumerated()
    {
        WindowsServiceConfigurationCache cache = new();
        WindowsServiceConfiguration retained = new(
            "Retained",
            string.Empty,
            WindowsServiceStartType.Automatic);
        cache.Store("RetainedService", retained);
        cache.Store(
            "RemovedService",
            new WindowsServiceConfiguration(
                "Removed",
                string.Empty,
                WindowsServiceStartType.Disabled));
        HashSet<string> currentServices = new(StringComparer.OrdinalIgnoreCase)
        {
            "retainedservice"
        };

        cache.RetainOnly(currentServices);

        Assert.True(cache.TryGet("RetainedService", out WindowsServiceConfiguration cached));
        Assert.Equal(retained, cached);
        Assert.False(cache.TryGet("RemovedService", out _));
    }
}
