using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// Enumerates conventional Windows startup registrations and changes their Explorer approval state.
/// </summary>
internal sealed partial class StartupAppsService : IDisposable
{
    private const string RunRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";

    private const string StartupApprovedRegistryRoot =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private const string StartupApprovedRunSubKey = StartupApprovedRegistryRoot + @"\Run";
    private const string StartupApprovedRun32SubKey = StartupApprovedRegistryRoot + @"\Run32";
    private const string StartupApprovedFolderSubKey = StartupApprovedRegistryRoot + @"\StartupFolder";
    private const string URLFilePrefix = "URL=";
    private const string DesktopConfigurationFileName = "desktop.ini";
    private const int MaximumURLFileLines = 128;
    private const int MaximumWindowsPathLength = 32_768;
    private const uint COMMultithreaded = 0;
    private const uint COMInProcessServer = 1;
    private const uint StorageModeRead = 0;
    private const uint ShellLinkGetPathRaw = 0x00000004;
    private const int COMChangedMode = unchecked((int)0x80010106);

    private static readonly object MissingRegistryValue = new();
    private static readonly string[] ExecutableCommandExtensions = [".exe", ".com", ".bat", ".cmd"];
    private static readonly Guid ShellLinkClassID = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkInterfaceID = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PersistFileInterfaceID = new("0000010B-0000-0000-C000-000000000046");

    private int _disposed;

    /// <summary>Reads and normalizes startup entries from the registry and Startup folders.</summary>
    public IReadOnlyList<StartupAppEntry> ReadEntries()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!OperatingSystem.IsWindows()) return [];

        List<StartupAppEntry> entries = [];
        RegistryView[] registryViews = Environment.Is64BitOperatingSystem
            ? [RegistryView.Registry64, RegistryView.Registry32]
            : [RegistryView.Registry32];
        foreach (RegistryView registryView in registryViews)
        {
            EnumerateRegistryEntries(
                entries,
                RegistryHive.CurrentUser,
                StartupAppScope.CurrentUser,
                registryView,
                RunRegistrySubKey,
                StartupAppSourceKind.RegistryRun);
            EnumerateRegistryEntries(
                entries,
                RegistryHive.CurrentUser,
                StartupAppScope.CurrentUser,
                registryView,
                RunOnceRegistrySubKey,
                StartupAppSourceKind.RegistryRunOnce);
            EnumerateRegistryEntries(
                entries,
                RegistryHive.LocalMachine,
                StartupAppScope.AllUsers,
                registryView,
                RunRegistrySubKey,
                StartupAppSourceKind.RegistryRun);
            EnumerateRegistryEntries(
                entries,
                RegistryHive.LocalMachine,
                StartupAppScope.AllUsers,
                registryView,
                RunOnceRegistrySubKey,
                StartupAppSourceKind.RegistryRunOnce);
        }

        StartupAppRegistryView nativeRegistryView = GetNativeStartupRegistryView();
        EnumerateStartupFolder(
            entries,
            Environment.SpecialFolder.Startup,
            StartupAppScope.CurrentUser,
            nativeRegistryView);
        EnumerateStartupFolder(
            entries,
            Environment.SpecialFolder.CommonStartup,
            StartupAppScope.AllUsers,
            nativeRegistryView);
        return NormalizeEntries(entries);
    }

    /// <summary>Marks a startup entry enabled without changing its registration command.</summary>
    public StartupAppActionResult Enable(StartupAppEntry entry) =>
        SetStatus(entry, StartupAppStatus.Enabled);

    /// <summary>Marks a startup entry disabled without changing its registration command.</summary>
    public StartupAppActionResult Disable(StartupAppEntry entry) =>
        SetStatus(entry, StartupAppStatus.Disabled);

    private StartupAppActionResult SetStatus(
        StartupAppEntry entry,
        StartupAppStatus desiredStatus)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(entry);
        if (!OperatingSystem.IsWindows())
        {
            return StartupAppActionResult.Failure(
                entry.Status,
                errorMessage: "Startup application status changes are only supported on Windows.");
        }

        if (desiredStatus is not (StartupAppStatus.Enabled or StartupAppStatus.Disabled))
            throw new ArgumentOutOfRangeException(nameof(desiredStatus));
        if (!entry.ApprovalTarget.IsValid)
        {
            return StartupAppActionResult.Failure(
                entry.Status,
                $"{entry.Name} does not have a writable StartupApproved target.");
        }

        if (entry.Status == desiredStatus) return StartupAppActionResult.Success(desiredStatus);

        StartupAppApprovalTarget target = entry.ApprovalTarget;
        RegistryHive registryHive = target.Scope == StartupAppScope.CurrentUser
            ? RegistryHive.CurrentUser
            : RegistryHive.LocalMachine;
        RegistryView registryView = ToRegistryView(target.RegistryView);
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(registryHive, registryView);
            using RegistryKey approvalKey = baseKey.CreateSubKey(
                target.RegistrySubKey,
                writable: true);
            byte[] approvalBlob = StartupApprovedStatusCodec.Encode(
                desiredStatus,
                DateTimeOffset.UtcNow);
            approvalKey.SetValue(
                target.ValueName,
                approvalBlob,
                RegistryValueKind.Binary);
            return StartupAppActionResult.Success(desiredStatus);
        }
        catch (Exception exception) when (IsExpectedWindowsAccessException(exception))
        {
            string action = desiredStatus == StartupAppStatus.Enabled ? "enable" : "disable";
            string errorMessage = $"Could not {action} {entry.Name}: {exception.Message}";
            TADNLog.Log($"StartupAppsService.SetStatus: {errorMessage}");
            return StartupAppActionResult.Failure(entry.Status, errorMessage);
        }
    }

    private static void EnumerateRegistryEntries(
        List<StartupAppEntry> entries,
        RegistryHive registryHive,
        StartupAppScope scope,
        RegistryView registryView,
        string sourceSubKey,
        StartupAppSourceKind sourceKind)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(registryHive, registryView);
            using RegistryKey? sourceKey = baseKey.OpenSubKey(sourceSubKey, writable: false);
            if (sourceKey == null) return;

            StartupAppRegistryView modelRegistryView = ToStartupRegistryView(registryView);
            StartupAppApprovalTarget approvalTargetTemplate = CreateRegistryApprovalTarget(
                scope,
                modelRegistryView,
                string.Empty,
                Environment.Is64BitOperatingSystem);
            string approvalSubKey = approvalTargetTemplate.RegistrySubKey;
            StartupAppRegistryView approvalRegistryView = approvalTargetTemplate.RegistryView;
            RegistryKey? approvalBaseKey = null;
            RegistryKey? approvalKey = null;
            bool approvalStatusUnavailable = false;
            try
            {
                approvalBaseKey = RegistryKey.OpenBaseKey(
                    registryHive,
                    ToRegistryView(approvalRegistryView));
                approvalKey = approvalBaseKey.OpenSubKey(approvalSubKey, writable: false);
            }
            catch (Exception exception) when (IsExpectedWindowsAccessException(exception))
            {
                approvalStatusUnavailable = true;
                TADNLog.LogDebug(
                    $"StartupAppsService could not read {registryHive} {approvalRegistryView}\\{approvalSubKey}: "
                    + exception.Message);
            }

            using (approvalBaseKey)
            using (approvalKey)
            {
                string[] valueNames = sourceKey.GetValueNames();
                foreach (string valueName in valueNames)
                {
                    object? rawCommand = sourceKey.GetValue(
                        valueName,
                        defaultValue: null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (rawCommand is not string command || string.IsNullOrWhiteSpace(command)) continue;

                    string? executableCandidate = ExtractExecutableCandidate(command);
                    string? executablePath = ResolveExistingFilePath(
                        executableCandidate,
                        searchExecutableName: true);
                    string? targetPath = executablePath ?? ExpandCandidate(executableCandidate);
                    string displayName = ResolveDisplayName(valueName, targetPath);
                    StartupAppStatus status = ReadApprovalStatus(
                        approvalKey,
                        valueName,
                        approvalStatusUnavailable);
                    StartupAppIdentity identity = new(
                        sourceKind,
                        scope,
                        modelRegistryView,
                        sourceSubKey,
                        valueName);
                    StartupAppApprovalTarget approvalTarget = approvalTargetTemplate with { ValueName = valueName };
                    StartupAppEntry entry = new(
                        identity,
                        displayName,
                        ResolvePublisher(executablePath),
                        status,
                        StartupAppImpact.NotMeasured,
                        command,
                        targetPath,
                        executablePath,
                        approvalTarget);
                    entries.Add(entry);
                }
            }
        }
        catch (Exception exception) when (IsExpectedWindowsAccessException(exception))
        {
            TADNLog.LogDebug(
                $"StartupAppsService could not read {registryHive} {registryView}\\{sourceSubKey}: "
                + exception.Message);
        }
    }

    private static void EnumerateStartupFolder(
        List<StartupAppEntry> entries,
        Environment.SpecialFolder specialFolder,
        StartupAppScope scope,
        StartupAppRegistryView approvalRegistryView)
    {
        string folderPath;
        try
        {
            folderPath = Environment.GetFolderPath(specialFolder);
        }
        catch (Exception exception) when (IsExpectedFileAccessException(exception))
        {
            TADNLog.LogDebug(
                $"StartupAppsService could not resolve {specialFolder}: {exception.Message}");
            return;
        }

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

        string[] startupFiles;
        try
        {
            startupFiles = Directory.GetFiles(folderPath, searchPattern: "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (IsExpectedFileAccessException(exception))
        {
            TADNLog.LogDebug(
                $"StartupAppsService could not enumerate {folderPath}: {exception.Message}");
            return;
        }

        RegistryHive registryHive = scope == StartupAppScope.CurrentUser
            ? RegistryHive.CurrentUser
            : RegistryHive.LocalMachine;
        RegistryView registryView = ToRegistryView(approvalRegistryView);
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(registryHive, registryView);
            RegistryKey? approvalKey = null;
            bool approvalStatusUnavailable = false;
            try
            {
                approvalKey = baseKey.OpenSubKey(
                    StartupApprovedFolderSubKey,
                    writable: false);
            }
            catch (Exception exception) when (IsExpectedWindowsAccessException(exception))
            {
                approvalStatusUnavailable = true;
                TADNLog.LogDebug(
                    $"StartupAppsService could not read {registryHive}\\{StartupApprovedFolderSubKey}: "
                    + exception.Message);
            }

            using (approvalKey)
            {
                foreach (string startupFile in startupFiles)
                {
                    string valueName = Path.GetFileName(startupFile);
                    if (string.IsNullOrWhiteSpace(valueName)
                        || valueName.Equals(
                            DesktopConfigurationFileName,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? targetCandidate = ResolveStartupFileTarget(startupFile);
                    string? executablePath = ResolveExistingFilePath(
                        targetCandidate,
                        searchExecutableName: false);
                    string? targetPath = executablePath ?? targetCandidate;
                    StartupAppIdentity identity = new(
                        StartupAppSourceKind.StartupFolder,
                        scope,
                        StartupAppRegistryView.None,
                        startupFile,
                        valueName);
                    StartupAppApprovalTarget approvalTarget = new(
                        scope,
                        approvalRegistryView,
                        StartupApprovedFolderSubKey,
                        valueName);
                    StartupAppEntry entry = new(
                        identity,
                        ResolveDisplayName(Path.GetFileNameWithoutExtension(startupFile), targetPath),
                        ResolvePublisher(executablePath),
                        ReadApprovalStatus(
                            approvalKey,
                            valueName,
                            approvalStatusUnavailable),
                        StartupAppImpact.NotMeasured,
                        startupFile,
                        targetPath,
                        executablePath,
                        approvalTarget);
                    entries.Add(entry);
                }
            }
        }
        catch (Exception exception) when (IsExpectedWindowsAccessException(exception))
        {
            TADNLog.LogDebug(
                $"StartupAppsService could not read StartupApproved status for {folderPath}: "
                + exception.Message);
        }
    }

    private static StartupAppStatus ReadApprovalStatus(
        RegistryKey? approvalKey,
        string valueName,
        bool approvalStatusUnavailable)
    {
        if (approvalStatusUnavailable) return StartupAppStatus.Unknown;
        if (approvalKey == null) return StartupAppStatus.Enabled;

        try
        {
            object? value = approvalKey.GetValue(
                valueName,
                MissingRegistryValue,
                RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (ReferenceEquals(value, MissingRegistryValue)) return StartupAppStatus.Enabled;
            return value is byte[] approvalBlob
                ? StartupApprovedStatusCodec.Decode(approvalBlob)
                : StartupAppStatus.Unknown;
        }
        catch (Exception exception) when (IsExpectedWindowsAccessException(exception))
        {
            TADNLog.LogDebug(
                $"StartupAppsService could not read StartupApproved value {valueName}: "
                + exception.Message);
            return StartupAppStatus.Unknown;
        }
    }

    /// <summary>Extracts the executable token from a conventional Run command without executing it.</summary>
    internal static string? ExtractExecutableCandidate(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        string trimmedCommand = command.Trim();
        if (trimmedCommand[0] == '"')
        {
            int closingQuoteIndex = trimmedCommand.IndexOf(value: '"', startIndex: 1);
            if (closingQuoteIndex <= 1) return null;
            return trimmedCommand[1..closingQuoteIndex].Trim();
        }

        int extensionEndIndex = FindExecutableExtensionEnd(trimmedCommand);
        if (extensionEndIndex > 0)
            return trimmedCommand[..extensionEndIndex].Trim().Trim('"');

        int tokenEndIndex = 0;
        while (tokenEndIndex < trimmedCommand.Length
               && !char.IsWhiteSpace(trimmedCommand[tokenEndIndex]))
            tokenEndIndex++;

        string token = trimmedCommand[..tokenEndIndex].Trim().Trim('"');
        return token.Length > 0 ? token : null;
    }

    private static int FindExecutableExtensionEnd(string command)
    {
        for (int characterIndex = 0; characterIndex < command.Length; characterIndex++)
        {
            foreach (string extension in ExecutableCommandExtensions)
            {
                if (characterIndex + extension.Length > command.Length) continue;
                ReadOnlySpan<char> candidate = command.AsSpan(characterIndex, extension.Length);
                if (!candidate.Equals(extension, StringComparison.OrdinalIgnoreCase)) continue;

                int extensionEndIndex = characterIndex + extension.Length;
                if (extensionEndIndex == command.Length) return extensionEndIndex;
                char nextCharacter = command[extensionEndIndex];
                if (char.IsWhiteSpace(nextCharacter)
                    || nextCharacter is ',' or '"')
                    return extensionEndIndex;
            }
        }

        return -1;
    }

    private static string? ExpandCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
            return expanded.Length > 0 ? expanded : null;
        }
        catch (Exception exception) when (IsExpectedFileAccessException(exception))
        {
            return null;
        }
    }

    private static string? ResolveExistingFilePath(
        string? candidate,
        bool searchExecutableName)
    {
        string? expandedCandidate = ExpandCandidate(candidate);
        if (string.IsNullOrWhiteSpace(expandedCandidate)) return null;
        if (Uri.TryCreate(expandedCandidate, UriKind.Absolute, out Uri? candidateURI)
            && !candidateURI.IsFile)
            return null;

        List<string> fileNames = [expandedCandidate];
        if (searchExecutableName && string.IsNullOrEmpty(Path.GetExtension(expandedCandidate)))
            fileNames.Add(expandedCandidate + ".exe");

        foreach (string fileName in fileNames)
        {
            string? directPath = TryResolveExistingPath(fileName);
            if (directPath != null) return directPath;
        }

        if (!searchExecutableName || ContainsDirectorySeparator(expandedCandidate)) return null;

        List<string> searchDirectories = BuildExecutableSearchDirectories();
        foreach (string searchDirectory in searchDirectories)
        {
            foreach (string fileName in fileNames)
            {
                try
                {
                    string combinedPath = Path.Combine(searchDirectory, fileName);
                    string? resolvedPath = TryResolveExistingPath(combinedPath);
                    if (resolvedPath != null) return resolvedPath;
                }
                catch (Exception exception) when (IsExpectedFileAccessException(exception))
                {
                    // Continue through the remaining Windows search locations
                }
            }
        }

        return null;
    }

    private static string? TryResolveExistingPath(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (IsExpectedFileAccessException(exception))
        {
            return null;
        }
    }

    private static List<string> BuildExecutableSearchDirectories()
    {
        List<string> directories = [];
        HashSet<string> seenDirectories = new(StringComparer.OrdinalIgnoreCase);
        AddSearchDirectory(directories, seenDirectories, AppContext.BaseDirectory);
        AddSearchDirectory(directories, seenDirectories, Environment.CurrentDirectory);
        AddSearchDirectory(directories, seenDirectories, Environment.SystemDirectory);
        AddSearchDirectory(
            directories,
            seenDirectories,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            string[] pathDirectories = pathValue.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string pathDirectory in pathDirectories)
                AddSearchDirectory(directories, seenDirectories, pathDirectory.Trim('"'));
        }

        return directories;
    }

    private static void AddSearchDirectory(
        List<string> directories,
        HashSet<string> seenDirectories,
        string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !seenDirectories.Add(directory)) return;
        directories.Add(directory);
    }

    private static bool ContainsDirectorySeparator(string path) =>
        path.Contains(Path.DirectorySeparatorChar)
        || path.Contains(Path.AltDirectorySeparatorChar);

    private static string? ResolveStartupFileTarget(string startupFile)
    {
        string extension = Path.GetExtension(startupFile);
        if (extension.Equals(value: ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return TryResolveShellLink(startupFile, out string shortcutTarget)
                ? shortcutTarget
                : null;
        }

        if (extension.Equals(value: ".url", StringComparison.OrdinalIgnoreCase))
            return ReadInternetShortcutTarget(startupFile);
        return startupFile;
    }

    private static string? ReadInternetShortcutTarget(string shortcutPath)
    {
        try
        {
            using StreamReader reader = new(shortcutPath);
            for (int lineIndex = 0; lineIndex < MaximumURLFileLines; lineIndex++)
            {
                string? line = reader.ReadLine();
                if (line == null) return null;
                if (!line.StartsWith(URLFilePrefix, StringComparison.OrdinalIgnoreCase)) continue;

                string target = line[URLFilePrefix.Length..].Trim();
                return target.Length > 0 ? target : null;
            }
        }
        catch (Exception exception) when (IsExpectedFileAccessException(exception))
        {
            TADNLog.LogDebug(
                $"StartupAppsService could not read internet shortcut {shortcutPath}: "
                + exception.Message);
        }

        return null;
    }

    private static unsafe bool TryResolveShellLink(string shortcutPath, out string targetPath)
    {
        targetPath = string.Empty;
        int initializationResult = CoInitializeEx(IntPtr.Zero, COMMultithreaded);
        bool uninitializeCOM = initializationResult >= 0;
        if (initializationResult < 0 && initializationResult != COMChangedMode) return false;

        IntPtr shellLinkPointer = IntPtr.Zero;
        IntPtr persistFilePointer = IntPtr.Zero;
        try
        {
            int result = CoCreateInstance(
                in ShellLinkClassID,
                IntPtr.Zero,
                COMInProcessServer,
                in ShellLinkInterfaceID,
                out shellLinkPointer);
            if (result < 0 || shellLinkPointer == IntPtr.Zero) return false;

            Guid persistFileInterfaceID = PersistFileInterfaceID;
            IntPtr queriedPersistFilePointer = IntPtr.Zero;
            IntPtr* shellLinkVTable = *(IntPtr**)shellLinkPointer;
            delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int> queryInterface =
                (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)shellLinkVTable[0];
            result = queryInterface(
                shellLinkPointer,
                &persistFileInterfaceID,
                &queriedPersistFilePointer);
            if (result < 0 || queriedPersistFilePointer == IntPtr.Zero) return false;
            persistFilePointer = queriedPersistFilePointer;

            IntPtr* persistFileVTable = *(IntPtr**)persistFilePointer;
            delegate* unmanaged[Stdcall]<IntPtr, char*, uint, int> load =
                (delegate* unmanaged[Stdcall]<IntPtr, char*, uint, int>)persistFileVTable[5];
            fixed (char* shortcutPathPointer = shortcutPath)
                result = load(persistFilePointer, shortcutPathPointer, StorageModeRead);
            if (result < 0) return false;

            char[] targetBuffer = new char[MaximumWindowsPathLength];
            fixed (char* targetBufferPointer = targetBuffer)
            {
                delegate* unmanaged[Stdcall]<IntPtr, char*, int, IntPtr, uint, int> getPath =
                    (delegate* unmanaged[Stdcall]<IntPtr, char*, int, IntPtr, uint, int>)shellLinkVTable[3];
                result = getPath(
                    shellLinkPointer,
                    targetBufferPointer,
                    targetBuffer.Length,
                    IntPtr.Zero,
                    ShellLinkGetPathRaw);
                if (result < 0 || targetBuffer[0] == '\0') return false;
                targetPath = new string(targetBufferPointer).Trim();
            }

            return targetPath.Length > 0;
        }
        catch (Exception exception) when (IsExpectedFileAccessException(exception))
        {
            TADNLog.LogDebug(
                $"StartupAppsService could not resolve shortcut {shortcutPath}: "
                + exception.Message);
            return false;
        }
        finally
        {
            ReleaseCOMPointer(persistFilePointer);
            ReleaseCOMPointer(shellLinkPointer);
            if (uninitializeCOM) CoUninitialize();
        }
    }

    private static unsafe void ReleaseCOMPointer(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return;

        IntPtr* virtualTable = *(IntPtr**)pointer;
        delegate* unmanaged[Stdcall]<IntPtr, uint> release =
            (delegate* unmanaged[Stdcall]<IntPtr, uint>)virtualTable[2];
        _ = release(pointer);
    }

    private static string ResolveDisplayName(string sourceName, string? targetPath)
    {
        if (!string.IsNullOrWhiteSpace(sourceName)) return sourceName.Trim();
        if (string.IsNullOrWhiteSpace(targetPath)) return "Unnamed startup app";

        string fileName = Path.GetFileNameWithoutExtension(targetPath);
        return string.IsNullOrWhiteSpace(fileName) ? targetPath : fileName;
    }

    private static string ResolvePublisher(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return string.Empty;
        try
        {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            return versionInfo.CompanyName?.Trim() ?? string.Empty;
        }
        catch (Exception exception) when (IsExpectedFileAccessException(exception))
        {
            return string.Empty;
        }
    }

    /// <summary>Deduplicates reflected registry entries and applies deterministic display ordering.</summary>
    internal static List<StartupAppEntry> NormalizeEntries(
        IEnumerable<StartupAppEntry> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        Dictionary<StartupAppDeduplicationKey, StartupAppEntry> uniqueEntries = new(
            StartupAppDeduplicationKeyComparer.Instance);
        foreach (StartupAppEntry candidate in candidates)
        {
            StartupAppDeduplicationKey key = StartupAppDeduplicationKey.From(candidate);
            if (uniqueEntries.TryGetValue(key, out StartupAppEntry? existing))
                uniqueEntries[key] = MergeDuplicate(existing, candidate);
            else
                uniqueEntries.Add(key, candidate);
        }

        List<StartupAppEntry> normalized = new(uniqueEntries.Count);
        foreach (StartupAppEntry entry in uniqueEntries.Values)
            normalized.Add(entry);
        normalized.Sort(CompareEntries);
        return normalized;
    }

    private static StartupAppEntry MergeDuplicate(
        StartupAppEntry first,
        StartupAppEntry second)
    {
        StartupAppEntry preferred = IsPreferredDuplicate(second, first) ? second : first;
        StartupAppEntry alternate = ReferenceEquals(preferred, first) ? second : first;
        return preferred with
        {
            Publisher = ChoosePopulated(preferred.Publisher, alternate.Publisher) ?? string.Empty,
            TargetPath = ChoosePopulated(preferred.TargetPath, alternate.TargetPath),
            ExecutablePath = ChoosePopulated(preferred.ExecutablePath, alternate.ExecutablePath),
            Status = preferred.Status == StartupAppStatus.Unknown
                ? alternate.Status
                : preferred.Status
        };
    }

    private static bool IsPreferredDuplicate(
        StartupAppEntry candidate,
        StartupAppEntry current)
    {
        int candidateViewRank = GetRegistryViewRank(candidate.Identity.RegistryView);
        int currentViewRank = GetRegistryViewRank(current.Identity.RegistryView);
        if (candidateViewRank != currentViewRank) return candidateViewRank > currentViewRank;

        int candidateMetadataRank = GetMetadataRank(candidate);
        int currentMetadataRank = GetMetadataRank(current);
        if (candidateMetadataRank != currentMetadataRank)
            return candidateMetadataRank > currentMetadataRank;

        return CompareEntries(candidate, current) < 0;
    }

    private static int GetRegistryViewRank(StartupAppRegistryView registryView) => registryView switch
    {
        StartupAppRegistryView.Registry64 => 2,
        StartupAppRegistryView.Registry32 => 1,
        _ => 0
    };

    private static int GetMetadataRank(StartupAppEntry entry)
    {
        int rank = 0;
        if (!string.IsNullOrWhiteSpace(entry.ExecutablePath)) rank += 4;
        if (!string.IsNullOrWhiteSpace(entry.TargetPath)) rank += 2;
        if (!string.IsNullOrWhiteSpace(entry.Publisher)) rank++;
        return rank;
    }

    private static string? ChoosePopulated(string? preferred, string? alternate) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred : alternate;

    private static int CompareEntries(StartupAppEntry left, StartupAppEntry right)
    {
        int comparison = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        if (comparison != 0) return comparison;
        comparison = StringComparer.OrdinalIgnoreCase.Compare(left.Publisher, right.Publisher);
        if (comparison != 0) return comparison;
        comparison = left.Identity.Scope.CompareTo(right.Identity.Scope);
        if (comparison != 0) return comparison;
        comparison = left.Identity.SourceKind.CompareTo(right.Identity.SourceKind);
        if (comparison != 0) return comparison;
        comparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Identity.SourceLocation,
            right.Identity.SourceLocation);
        if (comparison != 0) return comparison;
        comparison = StringComparer.OrdinalIgnoreCase.Compare(
            left.Identity.EntryName,
            right.Identity.EntryName);
        if (comparison != 0) return comparison;
        return StringComparer.OrdinalIgnoreCase.Compare(left.Command, right.Command);
    }

    private static StartupAppRegistryView ToStartupRegistryView(RegistryView registryView) =>
        registryView switch
        {
            RegistryView.Registry32 => StartupAppRegistryView.Registry32,
            RegistryView.Registry64 => StartupAppRegistryView.Registry64,
            _ => Environment.Is64BitOperatingSystem
                ? StartupAppRegistryView.Registry64
                : StartupAppRegistryView.Registry32
        };

    private static StartupAppRegistryView GetNativeStartupRegistryView() =>
        Environment.Is64BitOperatingSystem
            ? StartupAppRegistryView.Registry64
            : StartupAppRegistryView.Registry32;

    /// <summary>Maps a Run registration view to its native Explorer StartupApproved value.</summary>
    internal static StartupAppApprovalTarget CreateRegistryApprovalTarget(
        StartupAppScope scope,
        StartupAppRegistryView sourceRegistryView,
        string valueName,
        bool is64BitOperatingSystem)
    {
        if (sourceRegistryView is not (
            StartupAppRegistryView.Registry32 or StartupAppRegistryView.Registry64))
            throw new ArgumentOutOfRangeException(nameof(sourceRegistryView));

        StartupAppRegistryView approvalRegistryView = is64BitOperatingSystem
            ? StartupAppRegistryView.Registry64
            : StartupAppRegistryView.Registry32;
        string approvalSubKey = is64BitOperatingSystem
                                && sourceRegistryView == StartupAppRegistryView.Registry32
            ? StartupApprovedRun32SubKey
            : StartupApprovedRunSubKey;
        return new StartupAppApprovalTarget(
            scope,
            approvalRegistryView,
            approvalSubKey,
            valueName);
    }

    private static RegistryView ToRegistryView(StartupAppRegistryView registryView) =>
        registryView switch
        {
            StartupAppRegistryView.Registry32 => RegistryView.Registry32,
            StartupAppRegistryView.Registry64 => RegistryView.Registry64,
            _ => throw new ArgumentOutOfRangeException(nameof(registryView))
        };

    private static bool IsExpectedWindowsAccessException(Exception exception) =>
        exception is UnauthorizedAccessException
            or SecurityException
            or IOException
            or ArgumentException
            or PlatformNotSupportedException;

    private static bool IsExpectedFileAccessException(Exception exception) =>
        exception is UnauthorizedAccessException
            or SecurityException
            or IOException
            or Win32Exception
            or ArgumentException
            or NotSupportedException;

    public void Dispose()
    {
        _ = Interlocked.Exchange(ref _disposed, value: 1);
        GC.SuppressFinalize(this);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(IntPtr reserved, uint concurrencyModel);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid classID,
        IntPtr outer,
        uint context,
        in Guid interfaceID,
        out IntPtr instance);

    private readonly record struct StartupAppDeduplicationKey(
        StartupAppSourceKind SourceKind,
        StartupAppScope Scope,
        string SourceLocation,
        string EntryName,
        string Command)
    {
        public static StartupAppDeduplicationKey From(StartupAppEntry entry) => new(
            entry.Identity.SourceKind,
            entry.Identity.Scope,
            entry.Identity.SourceLocation,
            entry.Identity.EntryName,
            entry.Command);
    }

    private sealed class StartupAppDeduplicationKeyComparer
        : IEqualityComparer<StartupAppDeduplicationKey>
    {
        public static readonly StartupAppDeduplicationKeyComparer Instance = new();

        public bool Equals(
            StartupAppDeduplicationKey left,
            StartupAppDeduplicationKey right) =>
            left.SourceKind == right.SourceKind
            && left.Scope == right.Scope
            && StringComparer.OrdinalIgnoreCase.Equals(left.SourceLocation, right.SourceLocation)
            && StringComparer.OrdinalIgnoreCase.Equals(left.EntryName, right.EntryName)
            && StringComparer.OrdinalIgnoreCase.Equals(left.Command, right.Command);

        public int GetHashCode(StartupAppDeduplicationKey key)
        {
            HashCode hashCode = new();
            hashCode.Add(key.SourceKind);
            hashCode.Add(key.Scope);
            hashCode.Add(key.SourceLocation, StringComparer.OrdinalIgnoreCase);
            hashCode.Add(key.EntryName, StringComparer.OrdinalIgnoreCase);
            hashCode.Add(key.Command, StringComparer.OrdinalIgnoreCase);
            return hashCode.ToHashCode();
        }
    }
}

/// <summary>Encodes and decodes the undocumented state byte used by StartupApproved values.</summary>
internal static class StartupApprovedStatusCodec
{
    private const int BlobLength = 12;
    private const byte EnabledState = 0x02;
    private const byte DisabledState = 0x03;
    private const int TimestampOffset = 4;

    /// <summary>Decodes a missing value as enabled and recognized state-byte pairs by parity.</summary>
    public static StartupAppStatus Decode(byte[]? blob)
    {
        if (blob == null) return StartupAppStatus.Enabled;
        if (blob.Length < BlobLength) return StartupAppStatus.Unknown;

        byte state = blob[0];
        if (state is < 0x02 or > 0x07) return StartupAppStatus.Unknown;
        return (state & 1) == 0
            ? StartupAppStatus.Enabled
            : StartupAppStatus.Disabled;
    }

    /// <summary>Creates the 12-byte value written by Explorer for enabled or disabled entries.</summary>
    public static byte[] Encode(StartupAppStatus status, DateTimeOffset changedAt)
    {
        if (status is not (StartupAppStatus.Enabled or StartupAppStatus.Disabled))
            throw new ArgumentOutOfRangeException(nameof(status));

        byte[] blob = new byte[BlobLength];
        blob[0] = status == StartupAppStatus.Enabled ? EnabledState : DisabledState;
        if (status == StartupAppStatus.Disabled)
        {
            long fileTime = changedAt.UtcDateTime.ToFileTimeUtc();
            bool timestampWritten = BitConverter.TryWriteBytes(
                blob.AsSpan(TimestampOffset),
                fileTime);
            if (!timestampWritten)
                throw new InvalidOperationException("The StartupApproved timestamp buffer is invalid.");
        }

        return blob;
    }
}
