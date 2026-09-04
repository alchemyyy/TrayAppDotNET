using System.Resources;
using System.Security.Cryptography;
using TrayAppDotNETCommon.Services;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class NativeDependencyBundleTests
{
    [Fact]
    public void ValidAdjacentBundleAvoidsCacheExtraction()
    {
        using NativeDependencyTestDirectories directories = new();
        byte[] firstContent = "first-native-library"u8.ToArray();
        byte[] secondContent = "second-native-library"u8.ToArray();
        NativeDependencyDescriptor[] dependencies = CreateDependencies(firstContent, secondContent);
        WriteBundle(directories.AdjacentDirectory, dependencies, firstContent, secondContent);

        NativeDependencyBundleLocation location = NativeDependencyBundle.PrepareBundle(
            dependencies,
            directories.AdjacentDirectory,
            directories.CacheDirectory,
            _ => throw new InvalidOperationException("Embedded resources should not be opened."));

        Assert.Equal(NativeDependencyBundleSource.AdjacentDirectory, location.Source);
        Assert.Equal(Path.GetFullPath(directories.AdjacentDirectory), location.DirectoryPath);
        Assert.False(Directory.Exists(directories.CacheDirectory));
    }

    [Fact]
    public void MissingBundleIsExtractedAndReusedFromContentAddressedCache()
    {
        using NativeDependencyTestDirectories directories = new();
        byte[] firstContent = "first-native-library"u8.ToArray();
        byte[] secondContent = "second-native-library"u8.ToArray();
        NativeDependencyDescriptor[] dependencies = CreateDependencies(firstContent, secondContent);
        Dictionary<string, byte[]> resources = CreateResources(dependencies, firstContent, secondContent);
        int resourceOpenCount = 0;

        NativeDependencyBundleLocation firstLocation = NativeDependencyBundle.PrepareBundle(
            dependencies,
            directories.AdjacentDirectory,
            directories.CacheDirectory,
            resourceName =>
            {
                Interlocked.Increment(ref resourceOpenCount);
                return new MemoryStream(resources[resourceName], writable: false);
            });
        NativeDependencyBundleLocation secondLocation = NativeDependencyBundle.PrepareBundle(
            dependencies,
            directories.AdjacentDirectory,
            directories.CacheDirectory,
            _ => throw new InvalidOperationException("A valid cache should not reopen resources."));

        Assert.Equal(NativeDependencyBundleSource.SharedCache, firstLocation.Source);
        Assert.Equal(firstLocation, secondLocation);
        Assert.Equal(dependencies.Length, resourceOpenCount);
        Assert.True(NativeDependencyBundle.IsBundleValid(firstLocation.DirectoryPath, dependencies));
    }

    [Fact]
    public void CorruptCacheIsReplacedAsOneValidatedBundle()
    {
        using NativeDependencyTestDirectories directories = new();
        byte[] firstContent = "first-native-library"u8.ToArray();
        byte[] secondContent = "second-native-library"u8.ToArray();
        NativeDependencyDescriptor[] dependencies = CreateDependencies(firstContent, secondContent);
        Dictionary<string, byte[]> resources = CreateResources(dependencies, firstContent, secondContent);
        NativeDependencyBundleLocation initialLocation = NativeDependencyBundle.PrepareBundle(
            dependencies,
            directories.AdjacentDirectory,
            directories.CacheDirectory,
            resourceName => new MemoryStream(resources[resourceName], writable: false));
        File.WriteAllBytes(
            Path.Combine(initialLocation.DirectoryPath, dependencies[0].FileName),
            new byte[firstContent.Length]);

        NativeDependencyBundleLocation repairedLocation = NativeDependencyBundle.PrepareBundle(
            dependencies,
            directories.AdjacentDirectory,
            directories.CacheDirectory,
            resourceName => new MemoryStream(resources[resourceName], writable: false));

        Assert.Equal(initialLocation, repairedLocation);
        Assert.True(NativeDependencyBundle.IsBundleValid(repairedLocation.DirectoryPath, dependencies));
        Assert.Equal(
            firstContent,
            File.ReadAllBytes(Path.Combine(repairedLocation.DirectoryPath, dependencies[0].FileName)));
    }

    [Fact]
    public void ConcurrentPreparationExtractsEachResourceOnce()
    {
        using NativeDependencyTestDirectories directories = new();
        byte[] firstContent = new byte[256 * 1024];
        byte[] secondContent = new byte[128 * 1024];
        Random.Shared.NextBytes(firstContent);
        Random.Shared.NextBytes(secondContent);
        NativeDependencyDescriptor[] dependencies = CreateDependencies(firstContent, secondContent);
        Dictionary<string, byte[]> resources = CreateResources(dependencies, firstContent, secondContent);
        const int callerCount = 8;
        NativeDependencyBundleLocation?[] locations = new NativeDependencyBundleLocation[callerCount];
        int resourceOpenCount = 0;

        Parallel.For(0, callerCount, callerIndex =>
        {
            locations[callerIndex] = NativeDependencyBundle.PrepareBundle(
                dependencies,
                directories.AdjacentDirectory,
                directories.CacheDirectory,
                resourceName =>
                {
                    Interlocked.Increment(ref resourceOpenCount);
                    return new MemoryStream(resources[resourceName], writable: false);
                });
        });

        NativeDependencyBundleLocation expectedLocation = Assert.IsType<NativeDependencyBundleLocation>(locations[0]);
        Assert.All(locations, location => Assert.Equal(expectedLocation, location));
        Assert.Equal(dependencies.Length, resourceOpenCount);
    }

    [Fact]
    public void MissingResourceDoesNotPublishPartialCacheDirectory()
    {
        using NativeDependencyTestDirectories directories = new();
        byte[] firstContent = "first-native-library"u8.ToArray();
        byte[] secondContent = "second-native-library"u8.ToArray();
        NativeDependencyDescriptor[] dependencies = CreateDependencies(firstContent, secondContent);

        Assert.Throws<MissingManifestResourceException>(() => NativeDependencyBundle.PrepareBundle(
            dependencies,
            directories.AdjacentDirectory,
            directories.CacheDirectory,
            _ => null));

        string bundleDirectory = Path.Combine(
            directories.CacheDirectory,
            NativeDependencyBundle.CreateBundleID(dependencies));
        Assert.False(Directory.Exists(bundleDirectory));
        Assert.Empty(Directory.EnumerateDirectories(directories.CacheDirectory, ".staging-*"));
    }

    private static NativeDependencyDescriptor[] CreateDependencies(
        byte[] firstContent,
        byte[] secondContent) =>
    [
        CreateDependency("first.dll", "Test.Resources.first.dll", firstContent),
        CreateDependency("second.dll", "Test.Resources.second.dll", secondContent)
    ];

    private static NativeDependencyDescriptor CreateDependency(
        string fileName,
        string resourceName,
        byte[] content) =>
        new(fileName, resourceName, content.LongLength, Convert.ToHexString(SHA256.HashData(content)));

    private static Dictionary<string, byte[]> CreateResources(
        NativeDependencyDescriptor[] dependencies,
        byte[] firstContent,
        byte[] secondContent) =>
        new()
        {
            [dependencies[0].ResourceName] = firstContent,
            [dependencies[1].ResourceName] = secondContent
        };

    private static void WriteBundle(
        string directoryPath,
        NativeDependencyDescriptor[] dependencies,
        byte[] firstContent,
        byte[] secondContent)
    {
        Directory.CreateDirectory(directoryPath);
        File.WriteAllBytes(Path.Combine(directoryPath, dependencies[0].FileName), firstContent);
        File.WriteAllBytes(Path.Combine(directoryPath, dependencies[1].FileName), secondContent);
    }

    private sealed class NativeDependencyTestDirectories : IDisposable
    {
        public NativeDependencyTestDirectories()
        {
            RootDirectory = Path.Combine(
                Path.GetTempPath(),
                "TrayAppDotNET-native-dependency-tests",
                Guid.NewGuid().ToString("N"));
            AdjacentDirectory = Path.Combine(RootDirectory, "adjacent");
            CacheDirectory = Path.Combine(RootDirectory, "cache");
            Directory.CreateDirectory(AdjacentDirectory);
        }

        private string RootDirectory { get; }

        public string AdjacentDirectory { get; }

        public string CacheDirectory { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
