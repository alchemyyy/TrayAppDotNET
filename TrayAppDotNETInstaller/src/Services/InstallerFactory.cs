using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace TrayAppDotNETInstaller.Services;

/// <summary>Parsed "--make-installer" command line.</summary>
internal sealed record FactoryArguments(string OutputPath, IReadOnlyList<string> PayloadPaths, string? IconName);

/// <summary>
/// Console-only stamping mode. The factory executable is published once with no payload; this mode copies
/// that image, replaces its shell icon, and appends the payload archive, producing one
/// Installer_&lt;App&gt;_&lt;version&gt;.exe per release without a second compilation. It never creates a window.
/// </summary>
public static class InstallerFactory
{
    private const string Kernel32 = "kernel32.dll";

    public const string FactoryArgument = "--make-installer";
    public const string VerifyArgument = "--verify-installer";
    private const string OutputArgument = "--output";
    private const string ImageArgument = "--image";
    private const string PayloadArgument = "--payload";
    private const string PayloadListArgument = "--payload-list";
    private const string IconArgument = "--icon";
    private const string TemporarySuffix = ".stamping.tmp";
    private const int SuccessExitCode = 0;
    private const int FailureExitCode = 1;
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const int StandardOutputHandle = -11;

    private const string UsageText =
        "Usage: TrayAppDotNETInstaller.exe --make-installer --output <path> --payload <zip> [--payload <zip> ...] "
        + "[--payload-list <file>] [--icon <name>]"
        + "\n       TrayAppDotNETInstaller.exe --verify-installer --image <path>";

    /// <summary>True when the command line asks for a console mode instead of the installer UI.</summary>
    public static bool IsFactoryInvocation(string[] args)
    {
        FrameworkCompatibility.ThrowIfNull(args, nameof(args));

        foreach (string argument in args)
        {
            if (string.Equals(argument, FactoryArgument, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(argument, VerifyArgument, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool IsVerifyInvocation(string[] args)
    {
        foreach (string argument in args)
        {
            if (string.Equals(argument, VerifyArgument, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>Stamps or verifies one installer. Returns 0 on success; failures print to standard error.</summary>
    public static int Run(string[] args)
    {
        FrameworkCompatibility.ThrowIfNull(args, nameof(args));

        EnsureConsoleAttached();
        if (IsVerifyInvocation(args)) return RunVerify(args);

        if (!TryParseArguments(args, out FactoryArguments? arguments, out string? parseError))
        {
            WriteError(parseError);
            WriteError(UsageText);
            return FailureExitCode;
        }

        string? temporaryPath = null;
        string? stagingDirectory = null;
        try
        {
            // 1. The running image is the factory: a stamped copy is a byte-for-byte copy of it
            string? factoryImagePath = FrameworkCompatibility.ProcessPath;
            // The FrameworkCompatibility guard restores the nullable flow the unannotated framework method loses
            if (FrameworkCompatibility.IsNullOrEmpty(factoryImagePath))
            {
                WriteError("The factory image path is unknown; the running process reported no main module.");
                return FailureExitCode;
            }

            Console.WriteLine($"Factory image: {factoryImagePath}");

            // 2. Validate the payloads and refuse to stamp an already stamped installer
            List<string> payloadPaths = [];
            foreach (string payloadPath in arguments.PayloadPaths)
            {
                string fullPath = Path.GetFullPath(payloadPath);
                if (!File.Exists(fullPath))
                {
                    WriteError($"Payload not found: {fullPath}");
                    return FailureExitCode;
                }

                payloadPaths.Add(fullPath);
            }

            using (PayloadArchive? alreadyStamped = PayloadArchive.TryOpen(factoryImagePath, WriteVerbose))
            {
                if (alreadyStamped != null)
                {
                    WriteError($"{factoryImagePath} already carries a payload archive; stamp from the factory build instead.");
                    return FailureExitCode;
                }
            }

            Console.WriteLine($"Payloads: {payloadPaths.Count}");

            // 3. One payload names its own icon; a bundle and an explicit override are handled by the resolver
            if (!TryResolveIconName(arguments, out string? iconName, out string? iconError))
            {
                WriteError(iconError);
                return FailureExitCode;
            }

            Console.WriteLine($"Icon: {iconName}");

            // 4. Copy the factory beside the output so the move at the end stays on one volume
            string outputPath = Path.GetFullPath(arguments.OutputPath);
            string outputDirectory = Path.GetDirectoryName(outputPath)
                                     ?? throw new ArgumentException($"The output path has no directory: {outputPath}");
            Directory.CreateDirectory(outputDirectory);
            temporaryPath = outputPath + TemporarySuffix;
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

            File.Copy(factoryImagePath, temporaryPath, overwrite: true);
            Console.WriteLine($"Copied the factory to {temporaryPath}");

            // 5. Icon first: the resource update APIs rewrite the image and would discard appended bytes
            StampIcon(temporaryPath, iconName);

            // 6. Repack each release zip as one solid LZMA stream. A zip compresses every entry on its own
            // with a 32 KB window, which leaves most of the redundancy in a release package on the table.
            stagingDirectory = InstallerTempPaths.NewStagingDirectory();
            Directory.CreateDirectory(stagingDirectory);
            List<string> packedPaths = PackPayloads(payloadPaths, stagingDirectory);

            // 7. Append the payload archive
            PayloadArchiveWriter.Append(temporaryPath, packedPaths, WriteVerbose);
            Console.WriteLine($"Appended {packedPaths.Count} payload(s)");

            // 8. Read the result back, decompressing every payload, before it takes the output name
            if (!VerifyStampedImage(temporaryPath, packedPaths)) return FailureExitCode;

            // 9. Publish the result
            // MoveFile stands in for the File.Move overload with an overwrite flag, which the .NET Framework lacks
            FrameworkCompatibility.MoveFile(temporaryPath, outputPath, overwrite: true);
            temporaryPath = null;
            Console.WriteLine($"Wrote {outputPath} ({new FileInfo(outputPath).Length} bytes)");
            return SuccessExitCode;
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or InvalidOperationException
                                              or InvalidDataException
                                              or ArgumentException
                                              or NotSupportedException)
        {
            InstallerLog.Write("InstallerFactory.Run", exception);
            WriteError(exception.Message);
            return FailureExitCode;
        }
        finally
        {
            DeleteTemporary(temporaryPath);
            DeleteStaging(stagingDirectory);
        }
    }

    /// <summary>
    /// Verifies the payload archive appended to an image: container hashes, then a full decompression pass of
    /// every solid payload against its recorded SHA-256. Nothing is written to disk.
    /// </summary>
    private static int RunVerify(string[] args)
    {
        if (!TryTakeNamedValue(args, ImageArgument, out string? imagePath, out string? parseError))
        {
            WriteError(parseError);
            WriteError(UsageText);
            return FailureExitCode;
        }

        string fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
        {
            WriteError($"Image not found: {fullPath}");
            return FailureExitCode;
        }

        try
        {
            return VerifyPayloadArchive(fullPath, expectedFileNames: null) ? SuccessExitCode : FailureExitCode;
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or InvalidDataException
                                              or EndOfStreamException
                                              or NotSupportedException)
        {
            InstallerLog.Write("InstallerFactory.RunVerify", exception);
            WriteError(exception.Message);
            return FailureExitCode;
        }
    }

    /// <summary>
    /// Repacks every release zip into a solid payload beside the staging directory and returns what to
    /// append. A package too large to hold resident while packing is appended as its original zip, which the
    /// installer still reads; only the size benefit is lost.
    /// </summary>
    private static List<string> PackPayloads(List<string> payloadPaths, string stagingDirectory)
    {
        List<string> packedPaths = [];
        foreach (string payloadPath in payloadPaths)
        {
            string fileName = Path.GetFileName(payloadPath);
            SolidPayloadArchive.MeasureZip(payloadPath, out int entryCount, out long uncompressedLength);
            if (uncompressedLength > SolidPayloadArchive.MaximumUncompressedLength)
            {
                Console.WriteLine(
                    $"Warning: {fileName} expands to {uncompressedLength} bytes, past the "
                    + $"{SolidPayloadArchive.MaximumUncompressedLength} byte packing limit; appending the zip unchanged");
                packedPaths.Add(payloadPath);
                continue;
            }

            string packedPath = Path.Combine(
                stagingDirectory,
                Path.GetFileNameWithoutExtension(fileName) + SolidPayloadArchive.FileExtension);
            Console.WriteLine($"Packing {fileName}: {entryCount} entries, {uncompressedLength} uncompressed bytes");
            SolidPayloadStatistics statistics = SolidPayloadArchive.Create(
                payloadPath, packedPath, ShouldPackEntry, WriteVerbose);
            long zipLength = new FileInfo(payloadPath).Length;
            Console.WriteLine(
                $"Packed {fileName}: {zipLength} zip bytes became {statistics.PackedLength} "
                + $"({100.0 * statistics.PackedLength / Math.Max(zipLength, 1):F1}% of the zip)");
            packedPaths.Add(packedPath);
        }

        return packedPaths;
    }

    /// <summary>The batch installer a release zip carries is redundant inside an installer, so it is dropped.</summary>
    private static bool ShouldPackEntry(string entryName) => !InstallEngine.IsRootInstallScript(entryName);

    /// <summary>
    /// Parses the factory command line. "--payload-list" points at a UTF-8 file with one path per line and
    /// combines with any "--payload" arguments, in command-line order.
    /// </summary>
    internal static bool TryParseArguments(
        string[] args,
        [NotNullWhen(true)] out FactoryArguments? arguments,
        [NotNullWhen(false)] out string? error)
    {
        FrameworkCompatibility.ThrowIfNull(args, nameof(args));

        arguments = null;
        error = null;
        string? outputPath = null;
        string? iconName = null;
        List<string> payloadPaths = [];
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument.ToLowerInvariant())
            {
                case FactoryArgument:
                    continue;
                case OutputArgument:
                    if (!TryTakeValue(args, ref index, OutputArgument, out outputPath, out error)) return false;
                    continue;
                case PayloadArgument:
                    if (!TryTakeValue(args, ref index, PayloadArgument, out string? payloadPath, out error)) return false;

                    payloadPaths.Add(payloadPath);
                    continue;
                case PayloadListArgument:
                    if (!TryTakeValue(args, ref index, PayloadListArgument, out string? listPath, out error)) return false;
                    if (!TryReadPayloadList(listPath, payloadPaths, out error)) return false;

                    continue;
                case IconArgument:
                    if (!TryTakeValue(args, ref index, IconArgument, out iconName, out error)) return false;

                    continue;
                default:
                    error = $"Unrecognized argument: {argument}";
                    return false;
            }
        }

        if (FrameworkCompatibility.IsNullOrWhiteSpace(outputPath))
        {
            error = $"{OutputArgument} <path> is required.";
            return false;
        }

        if (payloadPaths.Count == 0)
        {
            error = $"At least one {PayloadArgument} <zip> or a non-empty {PayloadListArgument} <file> is required.";
            return false;
        }

        arguments = new FactoryArguments(outputPath, payloadPaths, iconName);
        return true;
    }

    /// <summary>
    /// Derives the icon the stamped copy carries: the explicit override, the single payload's application,
    /// or the suite icon for a bundle. A payload whose name does not parse is an error.
    /// </summary>
    internal static bool TryResolveIconName(
        FactoryArguments arguments,
        [NotNullWhen(true)] out string? iconName,
        [NotNullWhen(false)] out string? error)
    {
        FrameworkCompatibility.ThrowIfNull(arguments, nameof(arguments));

        iconName = null;
        error = null;
        List<string> applicationNames = [];
        foreach (string payloadPath in arguments.PayloadPaths)
        {
            string fileName = Path.GetFileName(payloadPath);
            if (!EmbeddedPayloadCatalog.TryParsePayloadFileName(fileName, out EmbeddedPayload? payload))
            {
                error = $"{fileName} is not a recognized package name; expected <App>_<Version>.zip.";
                return false;
            }

            applicationNames.Add(payload.ApplicationName);
        }

        if (!FrameworkCompatibility.IsNullOrWhiteSpace(arguments.IconName))
        {
            iconName = arguments.IconName;
            return true;
        }

        iconName = applicationNames.Count == 1 ? applicationNames[0] : InstallerIcons.SuiteIconName;
        return true;
    }

    /// <summary>Replaces the shell icon of the copy. A failure is a warning: the copy keeps the suite icon.</summary>
    private static void StampIcon(string temporaryPath, string iconName)
    {
        using Stream? iconStream = InstallerIcons.OpenOrSuite(iconName);
        if (iconStream == null)
        {
            Console.WriteLine($"Warning: the factory carries no icon for {iconName}; the shell icon is unchanged");
            return;
        }

        using MemoryStream iconBuffer = new();
        iconStream.CopyTo(iconBuffer);
        if (IconStamper.TryStamp(temporaryPath, iconBuffer.ToArray(), WriteVerbose))
        {
            Console.WriteLine($"Stamped the {iconName} icon");
            return;
        }

        Console.WriteLine($"Warning: the {iconName} icon could not be stamped; the shell icon is unchanged");
    }

    /// <summary>Reopens the stamped copy and checks that the archive holds exactly the payloads that went in.</summary>
    private static bool VerifyStampedImage(string temporaryPath, List<string> packedPaths)
    {
        List<string> expectedFileNames = [];
        foreach (string packedPath in packedPaths) expectedFileNames.Add(Path.GetFileName(packedPath));

        return VerifyPayloadArchive(temporaryPath, expectedFileNames);
    }

    /// <summary>
    /// Opens the payload archive appended to an image and proves every payload is intact. The container hash
    /// is checked by <see cref="PayloadArchive.OpenEntry"/>; a solid payload is then decompressed in full and
    /// matched against the SHA-256 its header records. A hand written codec is exactly the thing that should
    /// never be trusted on a structural check alone, so no installer is published without one decode pass.
    /// </summary>
    private static bool VerifyPayloadArchive(string imagePath, IReadOnlyList<string>? expectedFileNames)
    {
        using PayloadArchive? archive = PayloadArchive.TryOpen(imagePath, WriteVerbose);
        if (archive == null)
        {
            WriteError($"{imagePath} does not read back as a payload archive.");
            return false;
        }

        if (expectedFileNames != null && archive.Entries.Count != expectedFileNames.Count)
        {
            WriteError($"{imagePath} holds {archive.Entries.Count} entries but {expectedFileNames.Count} payload(s) were appended.");
            return false;
        }

        for (int index = 0; index < archive.Entries.Count; index++)
        {
            PayloadArchiveEntry entry = archive.Entries[index];
            if (expectedFileNames != null && !string.Equals(expectedFileNames[index], entry.FileName, StringComparison.Ordinal))
            {
                WriteError($"{imagePath} holds {entry.FileName} where {expectedFileNames[index]} was expected.");
                return false;
            }

            using Stream payload = archive.OpenEntry(entry);
            long uncompressedLength = VerifyPayload(payload, entry.FileName);
            Console.WriteLine($"payload {entry.FileName} {entry.DataLength} {uncompressedLength}");
        }

        Console.WriteLine($"verified {archive.Entries.Count} payload(s)");
        return true;
    }

    /// <summary>
    /// Returns the uncompressed size of one payload. A solid payload is decompressed to a discarding sink,
    /// which is what actually exercises the decoder; a plain zip is measured from its directory.
    /// </summary>
    private static long VerifyPayload(Stream payload, string fileName)
    {
        byte[] leadingBytes = new byte[SolidPayloadArchive.MagicLength];
        int read = payload.Read(leadingBytes, offset: 0, leadingBytes.Length);
        payload.Position = 0;
        if (!SolidPayloadArchive.StartsWithMagic(leadingBytes, read))
        {
            // A payload that stayed a zip is measured from its directory; DEFLATE is not ours to doubt
            long zipUncompressedLength = 0;
            using ZipArchive zip = new(payload, ZipArchiveMode.Read, leaveOpen: true);
            foreach (ZipArchiveEntry zipEntry in zip.Entries) zipUncompressedLength += zipEntry.Length;

            return zipUncompressedLength;
        }

        SolidPayloadHeader header = SolidPayloadArchive.Read(payload);
        SolidPayloadArchive.Extract(payload, header, destinationDirectory: null, onEntry: null, CancellationToken.None);
        return header.RawLength;
    }

    private static void DeleteStaging(string? stagingDirectory)
    {
        if (stagingDirectory == null) return;

        try
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            InstallerLog.Write($"InstallerFactory: could not delete {stagingDirectory}: {exception.Message}");
        }
    }

    /// <summary>Reads one payload path per line from a UTF-8 file, ignoring blank lines.</summary>
    private static bool TryReadPayloadList(
        string listPath,
        List<string> payloadPaths,
        [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (!File.Exists(listPath))
        {
            error = $"{PayloadListArgument} file not found: {listPath}";
            return false;
        }

        try
        {
            foreach (string line in File.ReadAllLines(listPath))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                payloadPaths.Add(trimmed);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"{PayloadListArgument} file {listPath} could not be read: {exception.Message}";
            return false;
        }
    }

    /// <summary>Finds "&lt;name&gt; &lt;value&gt;" anywhere on a command line. Used by the single argument modes.</summary>
    private static bool TryTakeNamedValue(
        string[] args,
        string name,
        [NotNullWhen(true)] out string? value,
        [NotNullWhen(false)] out string? error)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) continue;

            return TryTakeValue(args, ref index, name, out value, out error);
        }

        value = null;
        error = $"{name} <path> is required.";
        return false;
    }

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        string name,
        [NotNullWhen(true)] out string? value,
        [NotNullWhen(false)] out string? error)
    {
        value = null;
        error = null;
        if (index + 1 >= args.Length || args[index + 1].Length == 0)
        {
            error = $"{name} needs a value.";
            return false;
        }

        index++;
        value = args[index];
        return true;
    }

    private static void DeleteTemporary(string? temporaryPath)
    {
        if (temporaryPath == null) return;

        try
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            InstallerLog.Write($"InstallerFactory: could not delete {temporaryPath}: {exception.Message}");
        }
    }

    private static void WriteVerbose(string message) => Console.WriteLine(message);

    private static void WriteError(string message)
    {
        InstallerLog.Write($"InstallerFactory: {message}");
        Console.Error.WriteLine($"error: {message}");
    }

    /// <summary>
    /// The factory is a WinExe, so a console session hands it no standard handles. Attaching to the parent
    /// console makes the step lines visible when the tool is run by hand; a redirected parent already has
    /// usable handles and is left alone.
    /// </summary>
    private static void EnsureConsoleAttached()
    {
        IntPtr standardOutput = GetStdHandle(StandardOutputHandle);
        if (standardOutput != IntPtr.Zero && standardOutput != new IntPtr(-1)) return;
        if (!AttachConsole(AttachParentProcess))
        {
            InstallerLog.Write("InstallerFactory: no parent console to attach to; step output is discarded");
            return;
        }

        StreamWriter output = new(Console.OpenStandardOutput()) { AutoFlush = true };
        StreamWriter errorOutput = new(Console.OpenStandardError()) { AutoFlush = true };
        Console.SetOut(output);
        Console.SetError(errorOutput);
    }

    // DllImport stands in for LibraryImport; the .NET Framework has no source-generated interop
    [DllImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport(Kernel32, SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);
}
