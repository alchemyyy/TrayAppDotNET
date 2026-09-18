using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TrayAppDotNETInstaller.Services;

/// <summary>One application package carried by this installer, named by its bare zip file name.</summary>
public sealed record EmbeddedPayload(string ApplicationName, int Version, string FileName);

/// <summary>
/// The application packages this installer can install, sorted by application name. A stamped installer
/// reads them from the archive appended to its own image; a development build reads them from a "Payloads"
/// directory beside the executable so "dotnet run" works without stamping.
/// </summary>
public sealed class EmbeddedPayloadCatalog : IDisposable
{
    public const string DevelopmentDirectoryName = "Payloads";
    public const string ZipExtension = ".zip";
    private const string ZipSearchPattern = "*.zip";
    private const char NameVersionSeparator = '_';

    // A stamped installer carries solid payloads; a development Payloads directory holds plain release zips
    private static string[] PayloadExtensions { get; } = [SolidPayloadArchive.FileExtension, ZipExtension];

    // Stands in for System.Threading.Lock, which the .NET Framework does not define
    private static readonly object LoadGate = new();
    private static EmbeddedPayloadCatalog? LoadedCatalog;

    private readonly PayloadArchive? _archive;
    private readonly string? _developmentDirectory;
    private bool _disposed;

    public IReadOnlyList<EmbeddedPayload> Payloads { get; }

    public bool IsBundle => Payloads.Count > 1;

    public EmbeddedPayloadCatalog(IEnumerable<EmbeddedPayload> payloads)
        : this(payloads, archive: null, developmentDirectory: null)
    {
    }

    private EmbeddedPayloadCatalog(
        IEnumerable<EmbeddedPayload> payloads,
        PayloadArchive? archive,
        string? developmentDirectory)
    {
        FrameworkCompatibility.ThrowIfNull(payloads, nameof(payloads));

        List<EmbeddedPayload> sorted = [];
        sorted.AddRange(payloads);
        sorted.Sort(static (left, right) =>
            string.Compare(left.ApplicationName, right.ApplicationName, StringComparison.OrdinalIgnoreCase));
        Payloads = sorted;
        _archive = archive;
        _developmentDirectory = developmentDirectory;
    }

    /// <summary>
    /// Resolves the payloads once per process and hands the same catalog to every later caller: the archive
    /// appended to this executable wins, then a development "Payloads" directory, then an empty catalog.
    /// </summary>
    public static EmbeddedPayloadCatalog Load()
    {
        lock (LoadGate)
        {
            LoadedCatalog ??= CreateForProcess();
            return LoadedCatalog;
        }
    }

    /// <summary>Builds a catalog from bare payload file names. Used by the tests and by callers that already know the set.</summary>
    public static EmbeddedPayloadCatalog FromFileNames(IEnumerable<string> fileNames)
    {
        FrameworkCompatibility.ThrowIfNull(fileNames, nameof(fileNames));

        List<EmbeddedPayload> payloads = [];
        foreach (string fileName in fileNames)
        {
            if (TryParsePayloadFileName(fileName, out EmbeddedPayload? payload))
            {
                payloads.Add(payload);
                continue;
            }

            InstallerLog.Write($"EmbeddedPayloadCatalog: ignoring unrecognized payload name {fileName}");
        }

        return new EmbeddedPayloadCatalog(payloads);
    }

    /// <summary>
    /// Parses "&lt;App&gt;_&lt;Version&gt;.tadn" or the plain "&lt;App&gt;_&lt;Version&gt;.zip" a development
    /// payload directory holds. A trailing "_&lt;token&gt;" after the version is tolerated so non-legacy
    /// release asset names also resolve.
    /// </summary>
    public static bool TryParsePayloadFileName(
        string? fileName,
        [NotNullWhen(true)] out EmbeddedPayload? payload)
    {
        payload = null;
        // The FrameworkCompatibility guard restores the nullable flow the unannotated framework method loses
        if (FrameworkCompatibility.IsNullOrEmpty(fileName)) return false;

        string? extension = null;
        foreach (string candidate in PayloadExtensions)
        {
            if (!fileName.EndsWith(candidate, StringComparison.OrdinalIgnoreCase)) continue;

            extension = candidate;
            break;
        }

        if (extension == null) return false;

        string stem = fileName[..^extension.Length];
        int separatorIndex = stem.IndexOf(NameVersionSeparator);
        if (separatorIndex <= 0 || separatorIndex == stem.Length - 1) return false;

        string applicationName = stem[..separatorIndex];
        string remainder = stem[(separatorIndex + 1)..];
        int tokenSeparatorIndex = remainder.IndexOf(NameVersionSeparator);
        string versionText = tokenSeparatorIndex < 0 ? remainder : remainder[..tokenSeparatorIndex];
        if (!int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out int version)) return false;

        payload = new EmbeddedPayload(applicationName, version, fileName);
        return true;
    }

    public EmbeddedPayload? Find(string applicationName)
    {
        foreach (EmbeddedPayload payload in Payloads)
        {
            if (string.Equals(payload.ApplicationName, applicationName, StringComparison.OrdinalIgnoreCase))
                return payload;
        }

        return null;
    }

    /// <summary>
    /// Opens the payload zip. The stream is seekable and independently disposable. Archive payloads are
    /// hash-checked first, so a corrupt download throws <see cref="InvalidDataException"/> here.
    /// </summary>
    public Stream OpenPayload(EmbeddedPayload payload)
    {
        FrameworkCompatibility.ThrowIfNull(payload, nameof(payload));
        FrameworkCompatibility.ThrowIfDisposed(_disposed, this);

        string fileName = Path.GetFileName(payload.FileName);
        if (_archive != null)
        {
            foreach (PayloadArchiveEntry entry in _archive.Entries)
            {
                if (string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    return _archive.OpenEntry(entry);
            }

            throw new FileNotFoundException($"The appended payload {fileName} was not found.", fileName);
        }

        if (_developmentDirectory != null)
        {
            string filePath = Path.Combine(_developmentDirectory, fileName);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"The development payload {filePath} was not found.", filePath);

            return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }

        throw new InvalidOperationException($"This installer carries no payload for {payload.ApplicationName}.");
    }

    /// <summary>Releases the handle on the appended archive. The process calls this once, on the way out.</summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _archive?.Dispose();
        lock (LoadGate)
        {
            if (ReferenceEquals(LoadedCatalog, this)) LoadedCatalog = null;
        }
    }

    /// <summary>Applies the archive, development directory, empty resolution order and logs which source won.</summary>
    private static EmbeddedPayloadCatalog CreateForProcess()
    {
        string? imagePath = FrameworkCompatibility.ProcessPath;
        PayloadArchive? archive = FrameworkCompatibility.IsNullOrEmpty(imagePath) ? null : PayloadArchive.TryOpen(imagePath);
        if (archive != null)
        {
            List<EmbeddedPayload> archivePayloads = [];
            foreach (PayloadArchiveEntry entry in archive.Entries)
            {
                if (TryParsePayloadFileName(entry.FileName, out EmbeddedPayload? payload))
                {
                    archivePayloads.Add(payload);
                    continue;
                }

                InstallerLog.Write($"EmbeddedPayloadCatalog: ignoring unrecognized appended payload {entry.FileName}");
            }

            InstallerLog.Write($"EmbeddedPayloadCatalog: {archivePayloads.Count} payload(s) from the archive appended to {imagePath}");
            return new EmbeddedPayloadCatalog(archivePayloads, archive, developmentDirectory: null);
        }

        string? developmentDirectory = FindDevelopmentDirectory();
        if (developmentDirectory != null)
        {
            List<EmbeddedPayload> developmentPayloads = [];
            foreach (string filePath in Directory.GetFiles(developmentDirectory, ZipSearchPattern))
            {
                string fileName = Path.GetFileName(filePath);
                if (TryParsePayloadFileName(fileName, out EmbeddedPayload? payload))
                {
                    developmentPayloads.Add(payload);
                    continue;
                }

                InstallerLog.Write($"EmbeddedPayloadCatalog: ignoring unrecognized development payload {fileName}");
            }

            InstallerLog.Write($"EmbeddedPayloadCatalog: {developmentPayloads.Count} payload(s) from {developmentDirectory}");
            return new EmbeddedPayloadCatalog(developmentPayloads, archive: null, developmentDirectory);
        }

        InstallerLog.Write("EmbeddedPayloadCatalog: no appended archive and no development payload directory; the catalog is empty");
        return new EmbeddedPayloadCatalog([], archive: null, developmentDirectory: null);
    }

    /// <summary>
    /// Looks for the development payload directory beside the executable. The base directory is probed as
    /// well so a "dotnet TrayAppDotNETInstaller.dll" launch, where the process is dotnet itself, still works.
    /// </summary>
    private static string? FindDevelopmentDirectory()
    {
        // The .NET Framework Path.GetDirectoryName throws on null where the modern one returns null, so the
        // process path is checked before it is taken apart
        string? imagePath = FrameworkCompatibility.ProcessPath;
        string?[] candidateRoots =
        [
            FrameworkCompatibility.IsNullOrEmpty(imagePath) ? null : Path.GetDirectoryName(imagePath),
            AppContext.BaseDirectory
        ];
        foreach (string? candidateRoot in candidateRoots)
        {
            if (string.IsNullOrEmpty(candidateRoot)) continue;

            string candidate = Path.Combine(candidateRoot, DevelopmentDirectoryName);
            if (Directory.Exists(candidate)) return candidate;
        }

        return null;
    }
}
