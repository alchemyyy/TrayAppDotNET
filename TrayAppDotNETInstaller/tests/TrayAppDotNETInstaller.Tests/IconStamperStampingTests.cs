using System.Runtime.InteropServices;
using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

/// <summary>
/// Stamps a copy of the built factory executable and reads the result back through the Win32 resource
/// APIs. The test reports success without stamping when no build output is available to copy.
/// </summary>
public sealed class IconStamperStampingTests
{
    private const int ResourceTypeIcon = 3;
    private const int ResourceTypeVersion = 16;
    private const int ResourceTypeManifest = 24;
    private const int ResourceTypeGroupIcon = 14;
    private const int IconDirectoryHeaderSize = 6;
    private const int GroupDirectoryEntrySize = 14;
    private const ushort FirstIconResourceID = 1;
    private const string FactoryExecutableName = "TrayAppDotNETInstaller.exe";
    private const string FactoryProjectFileName = "TrayAppDotNETInstaller.csproj";
    private const string VolumeApplicationName = "VolumeTrayAppDotNET";
    private const string TemporaryDirectoryName = "TrayAppDotNETInstaller.IconStamperTests";

    [Fact]
    public void TryStamp_ReplacesTheShellIconAndKeepsTheManifest()
    {
        string? factoryExecutable = FindFactoryExecutable();
        if (factoryExecutable == null)
        {
            // Nothing has been built on this machine, so the check is skipped rather than failed
            return;
        }

        byte[] iconBytes = ReadEmbeddedIcon(VolumeApplicationName);
        bool parsed = IconStamper.TryParseIconDirectory(
            iconBytes,
            out IconDirectoryEntry[]? entries,
            out string? parseFailure);
        Assert.True(parsed, parseFailure);
        Assert.NotNull(entries);

        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            TemporaryDirectoryName,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        string stampedPath = Path.Combine(temporaryDirectory, FactoryExecutableName);
        List<string> logLines = [];
        try
        {
            File.Copy(factoryExecutable, stampedPath, overwrite: true);
            // A copy inherits the source attributes, and a read only image blocks BeginUpdateResourceW
            File.SetAttributes(stampedPath, FileAttributes.Normal);

            bool stamped = IconStamper.TryStamp(stampedPath, iconBytes, logLines.Add);
            Assert.True(stamped, string.Join(Environment.NewLine, logLines));

            List<ushort> groupIconIDs = NativeResourceReader.ReadResourceIDs(stampedPath, ResourceTypeGroupIcon);
            Assert.Single(groupIconIDs);

            byte[]? groupDirectory = NativeResourceReader.ReadResourceBytes(
                stampedPath,
                ResourceTypeGroupIcon,
                groupIconIDs[0]);
            Assert.NotNull(groupDirectory);
            Assert.Equal(
                IconDirectoryHeaderSize + (GroupDirectoryEntrySize * entries.Length),
                groupDirectory.Length);
            // FrameworkCompatibility stands in for BinaryPrimitives, which needs spans the .NET Framework lacks
            Assert.Equal(entries.Length, FrameworkCompatibility.ReadUInt16LittleEndian(groupDirectory, offset: 4));

            List<ushort> iconIDs = NativeResourceReader.ReadResourceIDs(stampedPath, ResourceTypeIcon);
            Assert.Equal(entries.Length, iconIDs.Count);

            byte[]? firstImage = NativeResourceReader.ReadResourceBytes(
                stampedPath,
                ResourceTypeIcon,
                FirstIconResourceID);
            Assert.NotNull(firstImage);
            // Array ranges need a runtime helper the .NET Framework lacks, so the range is copied by hand
            byte[] expectedFirstImage = new byte[(int)entries[0].BytesInResource];
            Array.Copy(
                iconBytes,
                (int)entries[0].ImageOffset,
                expectedFirstImage,
                destinationIndex: 0,
                expectedFirstImage.Length);
            Assert.Equal(expectedFirstImage, firstImage);

            // Losing the manifest would cost per monitor DPI awareness and the asInvoker execution level
            Assert.NotEmpty(NativeResourceReader.ReadResourceIDs(stampedPath, ResourceTypeManifest));
            Assert.NotEmpty(NativeResourceReader.ReadResourceIDs(stampedPath, ResourceTypeVersion));
        }
        finally
        {
            DeleteQuietly(temporaryDirectory);
        }
    }

    [Fact]
    public void TryStamp_RejectsAMissingFileWithoutThrowing()
    {
        List<string> logLines = [];
        string missingPath = Path.Combine(Path.GetTempPath(), TemporaryDirectoryName, $"{Guid.NewGuid():N}.exe");

        bool stamped = IconStamper.TryStamp(missingPath, ReadEmbeddedIcon(VolumeApplicationName), logLines.Add);

        Assert.False(stamped);
        Assert.NotEmpty(logLines);
    }

    [Fact]
    public void TryStamp_RejectsUnusableArgumentsWithoutThrowing()
    {
        List<string> logLines = [];

        Assert.False(IconStamper.TryStamp(string.Empty, [1, 2, 3], logLines.Add));
        Assert.False(IconStamper.TryStamp("installer.exe", [], logLines.Add));
        Assert.Equal(2, logLines.Count);
    }

    /// <summary>Reads one of the factory's embedded application icons into memory.</summary>
    private static byte[] ReadEmbeddedIcon(string applicationName)
    {
        using Stream? stream = InstallerIcons.Open(InstallerIcons.ResourceName(applicationName));
        Assert.NotNull(stream);
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Prefers the Release output, then the Prerelease one, then the Debug build. Returns null when none of
    /// them exist so the stamping check can stand down. The .NET Framework build writes a single executable
    /// straight into bin\&lt;Configuration&gt;, so there is no publish directory to look in.
    /// </summary>
    private static string? FindFactoryExecutable()
    {
        string? projectRoot = FindFactoryProjectRoot();
        if (projectRoot == null) return null;

        List<string> candidates =
        [
            Path.Combine(projectRoot, "bin", "Release", FactoryExecutableName),
            Path.Combine(projectRoot, "bin", "Prerelease", FactoryExecutableName),
            Path.Combine(projectRoot, "bin", "Debug", FactoryExecutableName)
        ];
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>Walks up from the test output directory to the installer project folder.</summary>
    private static string? FindFactoryProjectRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", FactoryProjectFileName)))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private static void DeleteQuietly(string directoryPath)
    {
        try
        {
            if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover copy under the temp directory is harmless and must not fail the run
            InstallerLog.Write($"IconStamperStampingTests: could not remove {directoryPath}: {exception.Message}");
        }
    }
}

/// <summary>
/// Reads Win32 resources straight out of a file image, so stamping is verified without reusing the
/// stamper's own enumeration.
/// </summary>
internal static class NativeResourceReader
{
    private const string Kernel32 = "kernel32.dll";
    private const uint LoadLibraryAsDataFile = 0x00000002;
    private const int ContinueEnumeration = 1;
    private const int StopEnumeration = 0;

    /// <summary>Returns the integer resource IDs of one resource type, in enumeration order.</summary>
    public static List<ushort> ReadResourceIDs(string filePath, int resourceType)
    {
        List<ushort> resourceIDs = [];
        IntPtr module = LoadLibraryExW(filePath, IntPtr.Zero, LoadLibraryAsDataFile);
        if (module == IntPtr.Zero) return resourceIDs;

        // The .NET Framework supports neither UnmanagedCallersOnly nor unmanaged function pointers, so the
        // callback is an ordinary delegate kept alive with GC.KeepAlive past the last native call
        EnumResourceNameCallback nameCallback = OnResourceNameFound;
        GCHandle collector = GCHandle.Alloc(resourceIDs);
        try
        {
            EnumResourceNamesW(
                module,
                new IntPtr(resourceType),
                nameCallback,
                GCHandle.ToIntPtr(collector));
        }
        finally
        {
            GC.KeepAlive(nameCallback);
            collector.Free();
            FreeLibrary(module);
        }

        return resourceIDs;
    }

    /// <summary>Returns the bytes of one resource, or null when the executable does not carry it.</summary>
    public static byte[]? ReadResourceBytes(string filePath, int resourceType, ushort resourceID)
    {
        IntPtr module = LoadLibraryExW(filePath, IntPtr.Zero, LoadLibraryAsDataFile);
        if (module == IntPtr.Zero) return null;

        try
        {
            IntPtr resourceInfo = FindResourceW(module, new IntPtr(resourceID), new IntPtr(resourceType));
            if (resourceInfo == IntPtr.Zero) return null;

            uint resourceSize = SizeofResource(module, resourceInfo);
            IntPtr resourceData = LoadResource(module, resourceInfo);
            if (resourceSize == 0 || resourceData == IntPtr.Zero) return null;

            IntPtr contents = LockResource(resourceData);
            if (contents == IntPtr.Zero) return null;

            byte[] bytes = new byte[resourceSize];
            Marshal.Copy(contents, bytes, 0, (int)resourceSize);
            return bytes;
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private static int OnResourceNameFound(IntPtr module, IntPtr resourceType, IntPtr resourceName, IntPtr state)
    {
        try
        {
            if (GCHandle.FromIntPtr(state).Target is not List<ushort> resourceIDs) return StopEnumeration;

            ulong value = (ulong)resourceName.ToInt64();
            if (value <= ushort.MaxValue) resourceIDs.Add((ushort)value);
            return ContinueEnumeration;
        }
        catch (Exception exception)
        {
            InstallerLog.Write("NativeResourceReader.OnResourceNameFound", exception);
            return StopEnumeration;
        }
    }

    /// <summary>ENUMRESNAMEPROCW; the Winapi convention is stdcall on x86 and the default one elsewhere.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumResourceNameCallback(IntPtr module, IntPtr resourceType, IntPtr resourceName, IntPtr state);

    // DllImport stands in for LibraryImport; the .NET Framework has no source-generated interop, so string
    // marshalling is spelled with CharSet.Unicode instead of StringMarshalling
    [DllImport(Kernel32, EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr reservedFileHandle, uint flags);

    [DllImport(Kernel32, EntryPoint = "FreeLibrary", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport(Kernel32, EntryPoint = "EnumResourceNamesW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumResourceNamesW(
        IntPtr module,
        IntPtr resourceType,
        EnumResourceNameCallback callback,
        IntPtr state);

    [DllImport(Kernel32, EntryPoint = "FindResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr resourceName, IntPtr resourceType);

    [DllImport(Kernel32, EntryPoint = "LoadResource", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resourceInfo);

    [DllImport(Kernel32, EntryPoint = "LockResource", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr resourceData);

    [DllImport(Kernel32, EntryPoint = "SizeofResource", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resourceInfo);
}
