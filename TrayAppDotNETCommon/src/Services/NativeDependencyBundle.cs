using System.Diagnostics;
using System.Resources;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TrayAppDotNETCommon.Services;

internal sealed record NativeDependencyDescriptor(
    string FileName,
    string ResourceName,
    long ExpectedLength,
    string ExpectedSHA256);

internal enum NativeDependencyBundleSource
{
    AdjacentDirectory,
    SharedCache
}

internal sealed record NativeDependencyBundleLocation(
    string DirectoryPath,
    NativeDependencyBundleSource Source);

/// <summary>Validates, extracts, and preloads native dependencies embedded in release builds.</summary>
internal static class NativeDependencyBundle
{
    private const int CopyBufferSize = 128 * 1024;
    private const int LockRetryDelayMilliseconds = 50;
    private const int LockTimeoutSeconds = 30;
    private const string CacheProductDirectoryName = "TrayAppDotNET";
    private const string CacheCategoryDirectoryName = "native";
    private const string CacheArchitectureDirectoryName = "win-x64";

#if TRAYAPPDOTNET_EMBEDDED_NATIVE_DEPENDENCIES
    private static readonly Lock InitializationLock = new();
    private static readonly NativeDependencyDescriptor[] EmbeddedDependencies =
    [
        new(
            FileName: "libHarfBuzzSharp.dll",
            ResourceName: "TrayAppDotNETCommon.NativeDependencies.libHarfBuzzSharp.dll",
            EmbeddedNativeDependencyMetadata.HarfBuzzSharpLength,
            EmbeddedNativeDependencyMetadata.HarfBuzzSharpSHA256),
        new(
            FileName: "libSkiaSharp.dll",
            ResourceName: "TrayAppDotNETCommon.NativeDependencies.libSkiaSharp.dll",
            EmbeddedNativeDependencyMetadata.SkiaSharpLength,
            EmbeddedNativeDependencyMetadata.SkiaSharpSHA256)
    ];
    private static IntPtr[] _loadedLibraryHandles = [];
    private static bool _initialized;
#endif

    /// <summary>Loads the validated x64 native graphics dependencies before Avalonia starts.</summary>
    internal static void EnsureLoaded()
    {
#if TRAYAPPDOTNET_EMBEDDED_NATIVE_DEPENDENCIES
        lock (InitializationLock)
        {
            if (_initialized) return;

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                NativeDependencyBundleLocation location = PrepareBundle(
                    EmbeddedDependencies,
                    AppContext.BaseDirectory,
                    DefaultCacheRoot(),
                    OpenEmbeddedResource);
                List<IntPtr> loadedLibraryHandles = new(EmbeddedDependencies.Length);
                try
                {
                    foreach (NativeDependencyDescriptor dependency in EmbeddedDependencies)
                    {
                        string libraryPath = Path.Combine(location.DirectoryPath, dependency.FileName);
                        loadedLibraryHandles.Add(NativeLibrary.Load(libraryPath));
                    }
                }
                catch
                {
                    for (int handleIndex = loadedLibraryHandles.Count - 1; handleIndex >= 0; handleIndex--)
                        NativeLibrary.Free(loadedLibraryHandles[handleIndex]);
                    throw;
                }

                _loadedLibraryHandles = loadedLibraryHandles.ToArray();
                _initialized = true;
                TADNLog.Log(
                    $"NativeDependencyBundle: loaded {_loadedLibraryHandles.Length} libraries from "
                    + $"{location.Source} in {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
            }
            catch (Exception exception)
            {
                TADNLog.Log($"NativeDependencyBundle.EnsureLoaded failed: {exception}");
                TADNLog.Flush();
                throw new InvalidOperationException(
                    "The embedded native graphics dependencies could not be prepared or loaded.",
                    exception);
            }
        }
#endif
    }

    /// <summary>Returns a validated adjacent bundle or atomically materializes the shared cache.</summary>
    internal static NativeDependencyBundleLocation PrepareBundle(
        IReadOnlyList<NativeDependencyDescriptor> dependencies,
        string adjacentDirectory,
        string cacheRootDirectory,
        Func<string, Stream?> openResource)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentException.ThrowIfNullOrWhiteSpace(adjacentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRootDirectory);
        ArgumentNullException.ThrowIfNull(openResource);
        ValidateDescriptors(dependencies);

        string normalizedAdjacentDirectory = Path.GetFullPath(adjacentDirectory);
        if (IsBundleValid(normalizedAdjacentDirectory, dependencies))
        {
            return new NativeDependencyBundleLocation(
                normalizedAdjacentDirectory,
                NativeDependencyBundleSource.AdjacentDirectory);
        }

        string normalizedCacheRoot = Path.GetFullPath(cacheRootDirectory);
        Directory.CreateDirectory(normalizedCacheRoot);
        string bundleID = CreateBundleID(dependencies);
        string bundleDirectory = Path.Combine(normalizedCacheRoot, bundleID);
        if (IsBundleValid(bundleDirectory, dependencies))
            return new NativeDependencyBundleLocation(bundleDirectory, NativeDependencyBundleSource.SharedCache);

        string lockPath = Path.Combine(normalizedCacheRoot, bundleID + ".lock");
        using FileStream cacheLock = AcquireCacheLock(lockPath);
        if (!IsBundleValid(bundleDirectory, dependencies))
            ExtractBundle(dependencies, bundleDirectory, normalizedCacheRoot, openResource);

        if (!IsBundleValid(bundleDirectory, dependencies))
            throw new InvalidDataException($"Extracted native dependency bundle is invalid: {bundleDirectory}");

        return new NativeDependencyBundleLocation(bundleDirectory, NativeDependencyBundleSource.SharedCache);
    }

    /// <summary>Checks every expected file length and SHA-256 digest.</summary>
    internal static bool IsBundleValid(
        string directoryPath,
        IReadOnlyList<NativeDependencyDescriptor> dependencies)
    {
        if (!Directory.Exists(directoryPath)) return false;

        try
        {
            foreach (NativeDependencyDescriptor dependency in dependencies)
            {
                string filePath = Path.Combine(directoryPath, dependency.FileName);
                FileInfo file = new(filePath);
                if (!file.Exists || file.Length != dependency.ExpectedLength) return false;

                using FileStream input = new(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    CopyBufferSize,
                    FileOptions.SequentialScan);
                byte[] actualHash = SHA256.HashData(input);
                byte[] expectedHash = Convert.FromHexString(dependency.ExpectedSHA256);
                if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash)) return false;
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Derives a stable content address from the complete dependency manifest.</summary>
    internal static string CreateBundleID(IReadOnlyList<NativeDependencyDescriptor> dependencies)
    {
        StringBuilder manifest = new();
        foreach (NativeDependencyDescriptor dependency in dependencies)
        {
            manifest.Append(dependency.FileName)
                .Append('\0')
                .Append(dependency.ExpectedLength)
                .Append('\0')
                .Append(dependency.ExpectedSHA256.ToUpperInvariant())
                .Append('\n');
        }

        byte[] manifestBytes = Encoding.UTF8.GetBytes(manifest.ToString());
        return Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
    }

    private static string DefaultCacheRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CacheProductDirectoryName,
            CacheCategoryDirectoryName,
            CacheArchitectureDirectoryName);

    private static Stream? OpenEmbeddedResource(string resourceName) =>
        typeof(NativeDependencyBundle).Assembly.GetManifestResourceStream(resourceName);

    private static void ValidateDescriptors(IReadOnlyList<NativeDependencyDescriptor> dependencies)
    {
        if (dependencies.Count == 0)
            throw new ArgumentException("At least one native dependency is required.", nameof(dependencies));

        HashSet<string> fileNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (NativeDependencyDescriptor dependency in dependencies)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dependency.FileName);
            ArgumentException.ThrowIfNullOrWhiteSpace(dependency.ResourceName);
            if (!string.Equals(dependency.FileName, Path.GetFileName(dependency.FileName), StringComparison.Ordinal))
                throw new ArgumentException($"Native dependency file name is not a leaf name: {dependency.FileName}");
            if (!fileNames.Add(dependency.FileName))
                throw new ArgumentException($"Duplicate native dependency file name: {dependency.FileName}");
            if (dependency.ExpectedLength < 0)
                throw new ArgumentOutOfRangeException(nameof(dependencies), "Native dependency length cannot be negative.");

            byte[] expectedHash;
            try
            {
                expectedHash = Convert.FromHexString(dependency.ExpectedSHA256);
            }
            catch (FormatException exception)
            {
                throw new ArgumentException(
                    $"Native dependency SHA-256 is not hexadecimal: {dependency.FileName}",
                    nameof(dependencies),
                    exception);
            }

            if (expectedHash.Length != SHA256.HashSizeInBytes)
            {
                throw new ArgumentException(
                    $"Native dependency SHA-256 has the wrong length: {dependency.FileName}",
                    nameof(dependencies));
            }
        }
    }

    private static FileStream AcquireCacheLock(string lockPath)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        IOException? lastException = null;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(LockTimeoutSeconds))
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
            }
            catch (IOException exception)
            {
                lastException = exception;
                Thread.Sleep(LockRetryDelayMilliseconds);
            }
        }

        throw new IOException($"Timed out waiting for native dependency cache lock: {lockPath}", lastException);
    }

    private static void ExtractBundle(
        IReadOnlyList<NativeDependencyDescriptor> dependencies,
        string bundleDirectory,
        string cacheRootDirectory,
        Func<string, Stream?> openResource)
    {
        string stagingDirectory = Path.Combine(
            cacheRootDirectory,
            $".staging-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            foreach (NativeDependencyDescriptor dependency in dependencies)
            {
                using Stream resource = openResource(dependency.ResourceName)
                    ?? throw new MissingManifestResourceException(
                        $"Embedded native dependency resource was not found: {dependency.ResourceName}");
                string destinationPath = Path.Combine(stagingDirectory, dependency.FileName);
                WriteValidatedResource(resource, destinationPath, dependency);
            }

            if (Directory.Exists(bundleDirectory)) Directory.Delete(bundleDirectory, recursive: true);
            Directory.Move(stagingDirectory, bundleDirectory);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static void WriteValidatedResource(
        Stream resource,
        string destinationPath,
        NativeDependencyDescriptor dependency)
    {
        byte[] copyBuffer = GC.AllocateUninitializedArray<byte>(CopyBufferSize);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long bytesWritten = 0;
        using (FileStream output = new(
                   destinationPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   CopyBufferSize,
                   FileOptions.SequentialScan))
        {
            int bytesRead;
            while ((bytesRead = resource.Read(copyBuffer, offset: 0, copyBuffer.Length)) != 0)
            {
                bytesWritten += bytesRead;
                if (bytesWritten > dependency.ExpectedLength)
                    throw new InvalidDataException($"Embedded native dependency is too large: {dependency.FileName}");

                hash.AppendData(copyBuffer, offset: 0, bytesRead);
                output.Write(copyBuffer, offset: 0, bytesRead);
            }

            output.Flush(flushToDisk: true);
        }

        byte[] actualHash = hash.GetHashAndReset();
        byte[] expectedHash = Convert.FromHexString(dependency.ExpectedSHA256);
        if (bytesWritten != dependency.ExpectedLength
            || !CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            throw new InvalidDataException(
                $"Embedded native dependency failed length or SHA-256 validation: {dependency.FileName}");
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A later cache cleanup can remove an abandoned staging directory
        }
    }
}
