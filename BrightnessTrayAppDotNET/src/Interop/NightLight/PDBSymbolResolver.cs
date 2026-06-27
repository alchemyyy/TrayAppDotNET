using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Resolves named symbols (mangled or namespace-qualified) to RVAs in a loaded native DLL
/// by parsing the DLL's PE Debug Directory,
/// fetching the matching PDB directly from the Microsoft public symbol server,
/// and querying it via dbghelp.dll.
///
/// First checks an on-disk JSON cache keyed on (DLL path, file version, file size, PDB signature, PDB age).
/// The PDB signature is the load-bearing piece of the key: Microsoft can (and does) push a refreshed
/// SettingsHandlers_Display.dll binary that keeps the same FileVersion + FileSize but has a different
/// internal layout - the only reliable invalidation signal is the CodeView (RSDS) PDB GUID embedded
/// in the new binary. Without it, a cache hit on (path,version,size) returns RVAs for the old binary
/// and dispatch ends up inside the wrong function -&gt; __fastfail.
/// On miss, reads the CodeView (RSDS) record from the in-memory PE image
/// to extract (PDB filename, GUID, Age),
/// downloads the PDB into a per-app local cache laid out symstore-style:
///   %LocalAppData%\TrayAppDotNET\BrightnessTrayAppDotNET\symbols\{pdbName}\{GUID}{Age}\{pdbName}
/// then resolves via SymInitialize + SymLoadModuleEx + SymFromName against that local file.
/// Resolved RVAs are persisted back to the JSON cache for subsequent runs.
///
/// Why we bypass dbghelp's built-in symbol-server support:
/// Win11's bundled dbghelp.dll has no companion symsrv.dll in System32,
/// so the "srv*cache*https://..." search-path syntax silently no-ops the actual download.
/// SymLoadModule then returns success in deferred mode,
/// the deferred load fails when SymFromName fires, and we get a misleading ERROR_MOD_NOT_FOUND.
/// Doing the HTTP GET ourselves sidesteps symsrv entirely; dbghelp is only used to read the on-disk PDB.
///
/// Used as a fallback path by <see cref="NightLightCloudStore"/>
/// when the SettingsHandlers_Display version isn't in the hardcoded RVA table.
/// Steady state on a known build is the hardcoded path;
/// this only kicks in after a Windows update changes the binary layout,
/// after which the new RVAs get captured into the JSON cache
/// and remain there until the next layout change.
///
/// Cache files:
///   %LocalAppData%\TrayAppDotNET\BrightnessTrayAppDotNET\nightlight-rva-cache.json   - resolved RVAs
///   %LocalAppData%\TrayAppDotNET\BrightnessTrayAppDotNET\symbols\                    - downloaded PDBs
///
/// Air-gapped machines (or networks that block <c>msdl.microsoft.com</c>) silently fail the download;
/// the caller treats that as "backend not supported" and degrades to the registry/gamma path.
/// </summary>
internal static class PDBSymbolResolver
{
    private const string SymbolServer = "https://msdl.microsoft.com/download/symbols";
    private const string SymbolServerUserAgent = "Microsoft-Symbol-Server/10.0";

    /// <summary>
    /// Per-PDB-file HTTP download timeout.
    /// Mutable so the settings UI can override the default without restarting the app;
    /// reads are unsynchronized because <see cref="HttpClient.Timeout"/> is set once per download
    /// from a single field read.
    /// </summary>
    internal static int DownloadTimeout = TimeConstants.PDBSymbolResolverDownloadTimeout;

    private static readonly Lock _gate = new();

    private static readonly string AppDataDir = Program.AppLocalAppDataDirectory;

    public static readonly string NightlightDir = Path.Combine(AppDataDir, "nightlight");
    private static readonly string CacheFile = Path.Combine(NightlightDir, "nightlight-rva-cache.json");


    /// <summary>
    /// Resolves multiple symbols in a single PDB load.
    /// Returns true on full success;
    /// false (with <paramref name="rvas"/> partially or fully empty)
    /// if any symbol couldn't be resolved or the PDB was unavailable.
    /// </summary>
    /// <param name="dllPath">Path of the loaded DLL (used to compute the cache key and to load symbols).</param>
    /// <param name="loadedModuleBase">
    /// Base address from <c>LoadLibraryW</c>.
    /// Used both to read the PE Debug Directory in memory and as the SymLoadModule base,
    /// so the resolver returns RVAs relative to it
    /// (which equal RVAs relative to the on-disk preferred base,
    /// because LoadLibrary maps sections at their RVA offsets from the chosen base).
    /// </param>
    /// <param name="symbolNames">
    /// Names to resolve.
    /// Either decorated (<c>?Foo@@YA...</c>) or undecorated namespace-qualified (<c>Foo::Bar</c>)
    /// - dbghelp's UNDNAME flag handles both.
    /// </param>
    /// <param name="rvas">Filled on success with name -&gt; RVA. Reused as the output dict.</param>
    public static bool TryResolveSymbols(
        string dllPath,
        IntPtr loadedModuleBase,
        IReadOnlyList<string> symbolNames,
        out Dictionary<string, int> rvas)
    {
        rvas = new Dictionary<string, int>(symbolNames.Count);

        FileVersionInfo info;
        long fileSize;
        try
        {
            info = FileVersionInfo.GetVersionInfo(dllPath);
            fileSize = new FileInfo(dllPath).Length;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"PDBSymbolResolver: failed to stat '{dllPath}': {ex.Message}");
            return false;
        }

        string version = info.FileVersion ?? string.Empty;

        // Pull the CodeView (PDB GUID + Age) up-front so the cache key includes it. Without this,
        // a Microsoft binary refresh that keeps the same FileVersion + FileSize but changes the
        // internal layout still hits the cache and dispatch crashes inside the DLL. A failure to
        // read CodeView is non-fatal - we fall through with an empty signature, the cache lookup
        // misses, and the standard download path will surface the same parse failure with its own
        // explicit log line.
        string PDBSignature = string.Empty;
        uint PDBAge = 0;
        if (TryReadCodeViewInfo(loadedModuleBase, out _, out Guid sigGUID, out uint age))
        {
            PDBSignature = sigGUID.ToString("N");
            PDBAge = age;
        }

        if (TryReadFromCache(dllPath, version, fileSize, PDBSignature, PDBAge, symbolNames, rvas)) return true;

        // dbghelp's session APIs are only safe for one process-wide session at a time,
        // so serialise any concurrent resolution attempts.
        // The lock also covers cache writes and PDB downloads for the same reason.
        lock (_gate)
        {
            // Re-check the cache inside the lock - another thread may have just resolved while we waited.
            if (TryReadFromCache(dllPath, version, fileSize, PDBSignature, PDBAge, symbolNames, rvas)) return true;

            if (!ResolveByDownloadingPDB(dllPath, loadedModuleBase, symbolNames, rvas)) return false;

            try
            {
                WriteToCache(dllPath, version, fileSize, PDBSignature, PDBAge, rvas);
            }
            catch (Exception ex)
            {
                // Cache write is best-effort: a future run will simply re-resolve.
                WPFLog.Log($"PDBSymbolResolver: cache write failed: {ex.Message}");
            }
        }

        return true;
    }

    private static bool TryReadFromCache(
        string dllPath, string version, long fileSize, string PDBSignature, uint PDBAge,
        IReadOnlyList<string> symbolNames, Dictionary<string, int> rvas)
    {
        List<CacheEntry>? entries = TryReadCacheEntries(logFailures: true);
        if (entries == null || entries.Count == 0) return false;

        // PDB signature is the authoritative key. FileVersion + FileSize are kept in the predicate as
        // a belt-and-braces sanity check, but a Microsoft binary refresh with the same FileVersion +
        // FileSize but a different PDB signature must miss here so the resolver re-resolves.
        // Entries written by an older build of this app (no PDBSignature field) deserialize with
        // empty strings, naturally miss, and get rewritten with the proper key on the next resolve.
        CacheEntry? entry = entries.FirstOrDefault(e =>
            string.Equals(e.DLLPath, dllPath, StringComparison.OrdinalIgnoreCase)
            && e.Version == version
            && e.FileSize == fileSize
            && string.Equals(e.PDBSignature, PDBSignature, StringComparison.OrdinalIgnoreCase)
            && e.PDBAge == PDBAge);

        if (entry == null) return false;

        foreach (string name in symbolNames)
        {
            if (!entry.Symbols.TryGetValue(name, out int rva))
            {
                // Cache exists for this DLL+version but is missing a needed symbol
                // - treat as miss so the resolver fills in the gap.
                // The new entry will overwrite this stale one.
                rvas.Clear();
                return false;
            }

            rvas[name] = rva;
        }

        return true;
    }

    private static void WriteToCache(
        string dllPath, string version, long fileSize, string PDBSignature, uint PDBAge,
        Dictionary<string, int> rvas)
    {
        Directory.CreateDirectory(NightlightDir);
        List<CacheEntry> entries = TryReadCacheEntries(logFailures: false) ?? [];

        // Drop any existing entry for the same DLL before appending the fresh one. We match only on
        // DllPath here (not the full key) so stale entries written by older builds of this app -
        // including the (path,version,size)-only entries that triggered the SettingsHandlers_Display
        // cache-poisoning crash - get pruned on the next successful resolve instead of accumulating.
        entries.RemoveAll(e =>
            string.Equals(e.DLLPath, dllPath, StringComparison.OrdinalIgnoreCase));

        entries.Add(new CacheEntry
        {
            DLLPath = dllPath,
            Version = version,
            FileSize = fileSize,
            PDBSignature = PDBSignature,
            PDBAge = PDBAge,
            Symbols = new Dictionary<string, int>(rvas),
        });

        WriteCacheEntries(entries);
    }

    private static List<CacheEntry>? TryReadCacheEntries(bool logFailures)
    {
        if (!File.Exists(CacheFile)) return [];

        try
        {
            using FileStream stream = File.OpenRead(CacheFile);
            if (stream.Length == 0) return [];

            using JsonDocument document = JsonDocument.Parse(stream);
            return ReadCacheEntries(document.RootElement);
        }
        catch (Exception ex)
        {
            if (logFailures)
                WPFLog.Log($"PDBSymbolResolver: cache read failed (will re-resolve): {ex.Message}");
            return null;
        }
    }

    private static List<CacheEntry> ReadCacheEntries(JsonElement root)
    {
        List<CacheEntry> entries = [];
        if (root.ValueKind != JsonValueKind.Object) return entries;
        if (!root.TryGetProperty("Entries", out JsonElement entriesElement)
            || entriesElement.ValueKind != JsonValueKind.Array)
            return entries;

        foreach (JsonElement entryElement in entriesElement.EnumerateArray())
        {
            CacheEntry? entry = ReadCacheEntry(entryElement);
            if (entry != null) entries.Add(entry);
        }

        return entries;
    }

    private static CacheEntry? ReadCacheEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        string DLLPath = ReadStringProperty(element, "DLLPath", fallbackName: "DllPath");
        if (string.IsNullOrWhiteSpace(DLLPath)) return null;

        Dictionary<string, int> symbols = [];
        if (element.TryGetProperty("Symbols", out JsonElement symbolsElement)
            && symbolsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty symbolProperty in symbolsElement.EnumerateObject())
            {
                if (symbolProperty.Value.TryGetInt32(out int RVA))
                    symbols[symbolProperty.Name] = RVA;
            }
        }

        return new CacheEntry
        {
            DLLPath = DLLPath,
            Version = ReadStringProperty(element, "Version"),
            FileSize = ReadInt64Property(element, "FileSize"),
            PDBSignature = ReadStringProperty(element, "PDBSignature", fallbackName: "PdbSignature"),
            PDBAge = ReadUInt32Property(element, "PDBAge", fallbackName: "PdbAge"),
            Symbols = symbols,
        };
    }

    private static string ReadStringProperty(JsonElement element, string propertyName, string? fallbackName = null)
    {
        if (element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? string.Empty;

        if (fallbackName != null
            && element.TryGetProperty(fallbackName, out JsonElement fallbackValue)
            && fallbackValue.ValueKind == JsonValueKind.String)
            return fallbackValue.GetString() ?? string.Empty;

        return string.Empty;
    }

    private static long ReadInt64Property(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)) return 0;
        return value.TryGetInt64(out long result) ? result : 0;
    }

    private static uint ReadUInt32Property(JsonElement element, string propertyName, string? fallbackName = null)
    {
        if (element.TryGetProperty(propertyName, out JsonElement value)
            && value.TryGetUInt32(out uint result))
            return result;

        if (fallbackName != null
            && element.TryGetProperty(fallbackName, out JsonElement fallbackValue)
            && fallbackValue.TryGetUInt32(out uint fallbackResult))
            return fallbackResult;

        return 0;
    }

    private static void WriteCacheEntries(IEnumerable<CacheEntry> entries)
    {
        using FileStream stream = new(CacheFile, FileMode.Create, FileAccess.Write, FileShare.None);
        using Utf8JsonWriter writer = new(stream, CacheJsonWriterOptions);

        writer.WriteStartObject();
        writer.WritePropertyName("Entries");
        writer.WriteStartArray();
        foreach (CacheEntry entry in entries)
        {
            writer.WriteStartObject();
            writer.WriteString("DLLPath", entry.DLLPath);
            writer.WriteString("Version", entry.Version);
            writer.WriteNumber("FileSize", entry.FileSize);
            writer.WriteString("PDBSignature", entry.PDBSignature);
            writer.WriteNumber("PDBAge", entry.PDBAge);
            writer.WritePropertyName("Symbols");
            writer.WriteStartObject();
            foreach (KeyValuePair<string, int> symbol in entry.Symbols.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
                writer.WriteNumber(symbol.Key, symbol.Value);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static bool ResolveByDownloadingPDB(
        string dllPath, IntPtr loadedModuleBase,
        IReadOnlyList<string> symbolNames, Dictionary<string, int> rvas)
    {
        if (!TryReadCodeViewInfo(loadedModuleBase, out string pdbName, out Guid pdbSig, out uint pdbAge))
        {
            WPFLog.Log(
                $"PDBSymbolResolver: '{dllPath}' has no usable CodeView (RSDS) debug record"
                + " - cannot derive PDB identity");
            return false;
        }

        // Symstore key: GUID with no dashes, then age in hex, both uppercase.
        // Microsoft's symbol server is case-sensitive on this segment for some files, so always emit uppercase.
        string symbolKey = ($"{pdbSig:N}{pdbAge:X}").ToUpperInvariant();
        string pdbDir = Path.Combine(NightlightDir, pdbName, symbolKey);
        string pdbPath = Path.Combine(pdbDir, pdbName);

        if (!File.Exists(pdbPath))
        {
            try
            {
                Directory.CreateDirectory(pdbDir);
            }
            catch (Exception ex)
            {
                WPFLog.Log($"PDBSymbolResolver: could not create PDB cache dir '{pdbDir}': {ex.Message}");
                return false;
            }

            string url = $"{SymbolServer}/{pdbName}/{symbolKey}/{pdbName}";
            if (!TryDownloadFile(url, pdbPath)) return false;
            WPFLog.Log($"PDBSymbolResolver: downloaded {pdbName} ({symbolKey}) -> {pdbPath}");
        }

        return ResolveViaDbghelp(loadedModuleBase, pdbDir, pdbPath, symbolNames, rvas);
    }

    /// <summary>
    /// Reads the in-memory mapped image's PE Debug Directory
    /// and extracts the IMAGE_DEBUG_TYPE_CODEVIEW (RSDS) record.
    /// The CodeView record carries the (PDB filename, signature GUID, age) tuple
    /// that uniquely identifies which PDB matches this DLL build.
    /// </summary>
    private static bool TryReadCodeViewInfo(
        IntPtr modBase, out string pdbName, out Guid signature, out uint age)
    {
        pdbName = string.Empty;
        signature = Guid.Empty;
        age = 0;

        try
        {
            // DOS header: must start with 'MZ', e_lfanew at offset 0x3C points to the PE header.
            short dosMagic = Marshal.ReadInt16(modBase, 0);
            if (dosMagic != 0x5A4D) return false;

            int peOffset = Marshal.ReadInt32(modBase, 0x3C);
            int peSig = Marshal.ReadInt32(modBase, peOffset);
            if (peSig != 0x00004550) return false; // 'PE\0\0'

            // COFF file header is 20 bytes immediately after 'PE\0\0', then the optional header.
            int optHeaderOffset = peOffset + 4 + 20;
            short optMagic = Marshal.ReadInt16(modBase, optHeaderOffset);

            // PE32 (0x10B) data dirs start at +96 from the optional header; PE32+ (0x20B) at +112.
            int dataDirOffset = optMagic switch
            {
                0x10B => optHeaderOffset + 96,
                0x20B => optHeaderOffset + 112,
                _ => -1,
            };
            if (dataDirOffset < 0) return false;

            // IMAGE_DIRECTORY_ENTRY_DEBUG = 6, each data dir entry is 8 bytes (RVA + Size).
            int debugDirRva = Marshal.ReadInt32(modBase, dataDirOffset + 6 * 8);
            int debugDirSize = Marshal.ReadInt32(modBase, dataDirOffset + 6 * 8 + 4);
            if (debugDirRva == 0 || debugDirSize < 28) return false;

            // sizeof(IMAGE_DEBUG_DIRECTORY) = 28. There can be multiple entries; we want type 2.
            int entryCount = debugDirSize / 28;
            for (int i = 0; i < entryCount; i++)
            {
                IntPtr entry = nint.Add(modBase, debugDirRva + i * 28);
                int debugType = Marshal.ReadInt32(entry, 12); // IMAGE_DEBUG_DIRECTORY.Type
                if (debugType != 2) continue; // IMAGE_DEBUG_TYPE_CODEVIEW

                int sizeOfData = Marshal.ReadInt32(entry, 16); // IMAGE_DEBUG_DIRECTORY.SizeOfData
                int rvaOfData = Marshal.ReadInt32(entry, 20); // IMAGE_DEBUG_DIRECTORY.AddressOfRawData
                // RSDS record: 4-byte sig + 16-byte GUID + 4-byte age + null-terminated path.
                // 24 bytes is the minimum before the (possibly empty) name.
                if (sizeOfData < 24 || rvaOfData == 0) continue;

                IntPtr cv = nint.Add(modBase, rvaOfData);
                int cvSig = Marshal.ReadInt32(cv, 0);
                if (cvSig != 0x53445352) continue; // 'RSDS'

                byte[] guidBytes = new byte[16];
                Marshal.Copy(nint.Add(cv, 4), guidBytes, 0, 16);
                Guid sig = new(guidBytes);

                uint a = (uint)Marshal.ReadInt32(cv, 20);

                // Name is null-terminated ANSI;
                // bound the scan to the entry size to avoid a runaway read into adjacent debug data.
                int maxNameBytes = sizeOfData - 24;
                IntPtr nameAddr = nint.Add(cv, 24);
                int nameLen = 0;
                while (nameLen < maxNameBytes && Marshal.ReadByte(nameAddr, nameLen) != 0) nameLen++;
                if (nameLen == 0) continue;

                byte[] nameBytes = new byte[nameLen];
                Marshal.Copy(nameAddr, nameBytes, 0, nameLen);
                string fullName = System.Text.Encoding.ASCII.GetString(nameBytes);

                // The recorded path can be a build-machine absolute path;
                // symbol server lookups only use the basename.
                pdbName = Path.GetFileName(fullName);
                signature = sig;
                age = a;
                return !string.IsNullOrEmpty(pdbName);
            }
        }
        catch (Exception ex)
        {
            WPFLog.Log($"PDBSymbolResolver: PE Debug Directory parse failed: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Streams <paramref name="url"/> to <paramref name="targetPath"/> via a *.tmp side file and an atomic move.
    /// Returns false on any HTTP, IO, or timeout failure (logged).
    /// </summary>
    private static bool TryDownloadFile(string url, string targetPath)
    {
        string tempPath = targetPath + ".tmp";
        try
        {
            using HttpClientHandler handler = new();
            handler.AllowAutoRedirect = true;
            using HttpClient http = new(handler);
            http.Timeout = TimeSpan.FromMilliseconds(DownloadTimeout);
            http.DefaultRequestHeaders.UserAgent.ParseAdd(SymbolServerUserAgent);

            using HttpResponseMessage resp = http.Send(
                new HttpRequestMessage(HttpMethod.Get, url),
                HttpCompletionOption.ResponseHeadersRead);

            if (!resp.IsSuccessStatusCode)
            {
                WPFLog.Log($"PDBSymbolResolver: HTTP {(int)resp.StatusCode} for '{url}'");
                return false;
            }

            using Stream src = resp.Content.ReadAsStream();
            using FileStream dst = File.Create(tempPath);
            src.CopyTo(dst);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"PDBSymbolResolver: download '{url}' failed: {ex.Message}");
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                /* best-effort */
            }

            return false;
        }

        try
        {
            // File.Move with overwrite is atomic on the same volume;
            // safe vs. concurrent same-key downloads even though the lock should have already serialised them.
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"PDBSymbolResolver: move '{tempPath}' -> '{targetPath}' failed: {ex.Message}");
            try { File.Delete(tempPath); }
            catch
            {
                /* best-effort */
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Loads the on-disk PDB into a dbghelp session and resolves each requested symbol name to an RVA.
    /// The PDB is identified by its on-disk path;
    /// <paramref name="pdbDir"/> is set as the search path
    /// so dbghelp's filename+signature match logic finds it without consulting a symbol server.
    /// Returns false if SymInitialize, SymLoadModule, or any SymFromName fails.
    /// </summary>
    private static bool ResolveViaDbghelp(
        IntPtr loadedModuleBase, string pdbDir, string pdbPath,
        IReadOnlyList<string> symbolNames, Dictionary<string, int> rvas)
    {
        IntPtr hProc = GetCurrentProcess();
        // UNDNAME lets us pass either decorated or namespace-qualified names.
        // Explicitly clear DEFERRED_LOADS so SymLoadModule reports failures synchronously
        // rather than deferring them to the first SymFromName
        // (which is what historically masked the missing-symsrv bug as ERROR_MOD_NOT_FOUND).
        _ = SymSetOptions(SYMOPT_UNDNAME | SYMOPT_LOAD_LINES | SYMOPT_FAIL_CRITICAL_ERRORS);

        if (!SymInitializeW(hProc, pdbDir, fInvadeProcess: false))
        {
            int lastError = Marshal.GetLastWin32Error();
            WPFLog.Log($"PDBSymbolResolver: SymInitialize failed err=0x{lastError:X8}");
            return false;
        }

        bool ok;
        try
        {
            // Pass the PDB path (not the DLL path) as ImageName:
            // dbghelp will recognise it as a PDB and load it directly, no DLL re-read needed.
            // BaseOfDll is the in-process load address,
            // so returned absolute symbol addresses minus this base give us in-process RVAs
            // that the caller can add back to LoadLibrary's return value.
            ulong modBase = SymLoadModuleExW(
                hProc, IntPtr.Zero, pdbPath, ModuleName: null,
                BaseOfDll: (ulong)loadedModuleBase.ToInt64(), DllSize: 0,
                Data: IntPtr.Zero, Flags: 0);

            if (modBase == 0)
            {
                int lastError = Marshal.GetLastWin32Error();
                WPFLog.Log(
                    $"PDBSymbolResolver: SymLoadModule failed err=0x{lastError:X8} for '{pdbPath}'");
                return false;
            }

            ok = ResolveAllNames(hProc, symbolNames, rvas);
        }
        finally
        {
            SymCleanup(hProc);
        }

        return ok;
    }

    private static bool ResolveAllNames(
        IntPtr hProc, IReadOnlyList<string> names, Dictionary<string, int> rvas)
    {
        // SYMBOL_INFOW (x64): SizeOfStruct=88 fixed prefix, then a variable-length WCHAR Name buffer.
        // We allocate enough room for a generously-sized name.
        const int sizeOfStruct = 88;
        const int maxNameChars = 1024;
        const int bufferSize = sizeOfStruct + maxNameChars * 2;
        IntPtr symbolInfoBuffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            foreach (string name in names)
            {
                // Zero the struct each iteration:
                // SymFromNameW writes into the variable-length tail
                // and we want a clean slate for NameLen/Address etc.
                for (int i = 0; i < bufferSize; i++) Marshal.WriteByte(symbolInfoBuffer, i, 0);
                Marshal.WriteInt32(symbolInfoBuffer, 0, sizeOfStruct);
                Marshal.WriteInt32(symbolInfoBuffer, 80, maxNameChars);

                if (!SymFromNameW(hProc, name, symbolInfoBuffer))
                {
                    int lastError = Marshal.GetLastWin32Error();
                    WPFLog.Log($"PDBSymbolResolver: SymFromName('{name}') failed err=0x{lastError:X8}");
                    return false;
                }

                long address = Marshal.ReadInt64(symbolInfoBuffer, 56); // SYMBOL_INFOW.Address
                long moduleBase = Marshal.ReadInt64(symbolInfoBuffer, 32); // SYMBOL_INFOW.ModBase
                long rva = address - moduleBase;

                if (rva is < 0 or > int.MaxValue)
                {
                    WPFLog.Log($"PDBSymbolResolver: nonsensical RVA 0x{rva:X16} for '{name}'");
                    return false;
                }

                rvas[name] = (int)rva;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(symbolInfoBuffer);
        }
    }

    private static readonly JsonWriterOptions CacheJsonWriterOptions = new() { Indented = true, };

    private sealed class CacheEntry
    {
        public string DLLPath { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;

        public long FileSize { get; set; }

        // PDB GUID in `N` format (32 hex chars, no dashes). Empty string means the entry was written
        // by an older build of this app that did not key on the PDB signature - those entries miss
        // the lookup predicate and are overwritten on the next resolve.
        public string PDBSignature { get; set; } = string.Empty;
        public uint PDBAge { get; set; }
        public Dictionary<string, int> Symbols { get; set; } = [];
    }

    // -- dbghelp P/Invoke ------------------------------------------------

    private const uint SYMOPT_UNDNAME = 0x00000002;
    private const uint SYMOPT_LOAD_LINES = 0x00000010;
    private const uint SYMOPT_FAIL_CRITICAL_ERRORS = 0x00000200;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SymInitializeW(
        IntPtr hProcess, [MarshalAs(UnmanagedType.LPWStr)] string userSearchPath, bool fInvadeProcess);

    [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ulong SymLoadModuleExW(
        IntPtr hProcess, IntPtr hFile,
        [MarshalAs(UnmanagedType.LPWStr)] string ImageName,
        [MarshalAs(UnmanagedType.LPWStr)] string? ModuleName,
        ulong BaseOfDll, uint DllSize, IntPtr Data, uint Flags);

    [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SymFromNameW(
        IntPtr hProcess, [MarshalAs(UnmanagedType.LPWStr)] string Name, IntPtr Symbol);

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool SymCleanup(IntPtr hProcess);

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern uint SymSetOptions(uint SymOptions);
}
