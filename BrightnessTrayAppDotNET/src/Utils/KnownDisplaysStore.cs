using System.Text.Json;
using TrayAppDotNETCommon.Serialization;
using Timer = System.Threading.Timer;

namespace BrightnessTrayAppDotNET.Utils;

/// <summary>
/// Persistent registry of every unique display the app has ever enumerated, keyed by EDIDKey.
/// Extracted from <see cref="AppSettings.KnownDisplays"/> so monitor enumeration no longer drags
/// the entire settings XML through a write on every refresh;
/// the registry grows without bound (disconnected monitors are never removed except by "Clear displays")
/// and was the largest contributor to settings-file churn.
///
/// The app creates exactly one instance and shares it between <see cref="Services.MonitorService"/>, every
/// <see cref="Services.ProfileManager"/> and the settings pages (<c>AppServices.KnownDisplays</c>); a second instance
/// would write its stale snapshot over the live one. Writes are atomic and happen only after a change.
/// A corrupt <c>displays.json</c> is moved to a <c>.corrupt-*</c> backup; a locked one is never overwritten.
///
/// Entries themselves are still <see cref="KnownDisplayEntry"/> instances so the type is shared with the legacy
/// XML element; only the persistence path differs.
/// </summary>
public sealed class KnownDisplaysStore : IDisposable
{
    private static readonly JsonWriterOptions s_jsonWriterOptions = new() { Indented = true };

    // Trailing-edge debounce for Stamp* calls. A 60Hz slider drag would otherwise rewrite
    // displays.json on every integer transition; this coalesces a burst into one save once
    // the drag settles. Single timer for the whole store - fine because saves serialize the
    // entire list anyway, so per-key debouncing wouldn't reduce I/O.
    private readonly string _path;
    private readonly Timer _stampDebounceTimer;
    private int _disposed;

    private readonly Lock _gate = new();
    private List<KnownDisplayEntry> _entries = [];
    private bool _loaded;
    private bool _dirty;
    private bool _savingDisabled;

    // displays.json holds the entries: it loaded, or the legacy migration wrote it
    private bool _entriesOnDisk;

    public KnownDisplaysStore(string path)
    {
        _path = path;
        _stampDebounceTimer = new Timer(
            _ => FlushPendingSave(),
            state: null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public KnownDisplaysStore() : this(GetDefaultPath()) { }

    /// <summary>
    /// Snapshot of the current entries, copied under the store's lock, so it is safe to enumerate while the
    /// store keeps changing. The entry objects are the live ones.
    /// </summary>
    public IReadOnlyList<KnownDisplayEntry> Entries
    {
        get
        {
            lock (_gate) return _entries.ToList();
        }
    }

    /// <summary>
    /// Path of the JSON file. Sits next to settings.xml under %LocalAppData%\TrayAppDotNET\&lt;app&gt;\.
    /// </summary>
    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, path2: "displays.json");
    }

    /// <summary>
    /// Loads <c>displays.json</c> once; later calls only repeat the first answer, so every owner of the shared
    /// instance may call it. First-run migration: when the JSON file is missing and <paramref name="legacy"/>
    /// contains entries (still living inside settings.xml from before this extraction), copies them across and
    /// writes the JSON file. Returns true only when the entries are on disk (the file loaded, or the migration
    /// write succeeded), the one case where the caller may drop the legacy list: a locked or unmovable corrupt
    /// file, or a failed migration write, leaves that list as the only durable copy.
    /// </summary>
    public bool Load(IEnumerable<KnownDisplayEntry>? legacy = null)
    {
        lock (_gate)
        {
            if (_loaded) return _entriesOnDisk;
            _loaded = true;

            DataFileLoadResult result = DataFileLoader.TryLoad(_path, ReadEntries, Log, out List<KnownDisplayEntry>? loaded);
            switch (result)
            {
                case DataFileLoadResult.Loaded:
                    _entries = loaded!;
                    _entriesOnDisk = true;
                    return true;
                case DataFileLoadResult.Locked:
                    _savingDisabled = true;
                    Log($"{_path}: saving is disabled for this session so the existing file is not overwritten.");
                    return false;
                case DataFileLoadResult.Corrupt when DataFileLoader.MoveCorruptAside(_path, Log) == null:
                    _savingDisabled = true;
                    return false;
            }

            // Missing (or corrupt and moved aside): migrate from AppSettings.KnownDisplays so users upgrading
            // from an older build don't lose their accumulated history
            // (and, crucially, their sticky WasEverDDCCapable flags -
            // DDCRecoveryService keys candidate selection off those).
            _entries = legacy?
                .Where(e => !string.IsNullOrEmpty(e.EDIDKey))
                .Select(Clone)
                .ToList() ?? [];

            if (_entries.Count == 0) return false;

            _dirty = true;
            _entriesOnDisk = TrySaveLocked();
            return _entriesOnDisk;
        }
    }

    /// <summary>
    /// Adds a new entry by EDIDKey if absent;
    /// otherwise refreshes <c>OriginalName</c> and <c>EDIDSerial</c> on the existing entry
    /// from non-empty values in <paramref name="entry"/>.
    /// Returns true when the in-memory list actually changed
    /// (caller may use this to decide whether to <see cref="Save"/>;
    /// <see cref="RegisterMany"/> already auto-saves).
    /// </summary>
    public bool Register(KnownDisplayEntry? entry)
    {
        if (entry == null) return false;

        if (string.IsNullOrEmpty(entry.EDIDKey)) return false;

        lock (_gate) return RegisterLocked(entry);
    }

    /// <summary>
    /// Bulk-register variant. Saves once at the end if anything changed.
    /// </summary>
    public void RegisterMany(IEnumerable<KnownDisplayEntry>? entries)
    {
        if (entries == null) return;

        lock (_gate)
        {
            foreach (KnownDisplayEntry e in entries)
            {
                if (string.IsNullOrEmpty(e.EDIDKey)) continue;

                RegisterLocked(e);
            }

            SaveLocked();
        }
    }

    /// <summary>
    /// Stamps <c>WasEverDDCCapable = true</c> for the entry matching <paramref name="edidKey"/>.
    /// No-op if the key is unknown or the flag is already set. Saves on transition.
    /// Returns true when a flag actually flipped.
    /// </summary>
    public bool MarkDDCCapable(string edidKey)
    {
        if (string.IsNullOrEmpty(edidKey)) return false;

        lock (_gate)
        {
            KnownDisplayEntry? entry = _entries.FirstOrDefault(e => e.EDIDKey == edidKey);
            if (entry == null) return false;

            if (entry.WasEverDDCCapable) return false;

            entry.WasEverDDCCapable = true;
            _dirty = true;
            SaveLocked();
            return true;
        }
    }

    /// <summary>
    /// Forgets every known display, including its sticky DDC flags and last bus brightness ("Clear displays").
    /// Displays that are connected are registered again on the next monitor refresh.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (_entries.Count == 0) return;

            _entries = [];
            _dirty = true;
            SaveLocked();
        }
    }

    /// <summary>
    /// Persists the in-memory list to JSON if it changed since the last write.
    /// </summary>
    public void Save()
    {
        lock (_gate) SaveLocked();
    }

    /// <summary>
    /// Returns the entry for <paramref name="edidKey"/>, or null.
    /// Returned object is the live instance; callers must not mutate fields they don't own.
    /// </summary>
    public KnownDisplayEntry? Find(string edidKey)
    {
        if (string.IsNullOrEmpty(edidKey)) return null;

        lock (_gate) return _entries.FirstOrDefault(e => e.EDIDKey == edidKey);
    }

    private bool RegisterLocked(KnownDisplayEntry incoming)
    {
        KnownDisplayEntry? existing = _entries.FirstOrDefault(e => e.EDIDKey == incoming.EDIDKey);
        if (existing == null)
        {
            _entries.Add(Clone(incoming));
            _dirty = true;
            return true;
        }

        bool changed = false;
        if (!string.IsNullOrWhiteSpace(incoming.OriginalName)
            && existing.OriginalName != incoming.OriginalName)
        {
            existing.OriginalName = incoming.OriginalName;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(incoming.EDIDSerial)
            && existing.EDIDSerial != incoming.EDIDSerial)
        {
            existing.EDIDSerial = incoming.EDIDSerial;
            changed = true;
        }

        if (incoming.WasEverDDCCapable && !existing.WasEverDDCCapable)
        {
            existing.WasEverDDCCapable = true;
            changed = true;
        }

        // LastBusBrightness is owned by the Stamp* path. Register-time merging never overwrites
        // a previously-observed value with null - that would discard intent every time MonitorService
        // re-registers a known display on Refresh.
        _dirty |= changed;
        return changed;
    }

    private void SaveLocked() => TrySaveLocked();

    /// <summary>Writes pending changes; false when saving is disabled or the write failed.</summary>
    private bool TrySaveLocked()
    {
        if (!_dirty) return true;
        if (_savingDisabled) return false;

        try
        {
            AtomicFile.Write(_path, stream =>
            {
                using Utf8JsonWriter writer = new(stream, s_jsonWriterOptions);
                WriteEntries(writer, _entries);
                writer.Flush();
            });
            _dirty = false;
            return true;
        }
        catch (Exception ex)
        {
            Log($"save failed ({_path}): {ex.Message}");
            return false;
        }
    }

    private static void Log(string message) => TADNLog.Log($"KnownDisplaysStore: {message}");

    private static List<KnownDisplayEntry> ReadEntries(Stream stream)
    {
        using JsonDocument document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("displays.json must hold a JSON array.");

        List<KnownDisplayEntry> entries = [];
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            KnownDisplayEntry? entry = ReadEntry(item);
            if (entry != null && !string.IsNullOrEmpty(entry.EDIDKey)) entries.Add(entry);
        }

        return entries;
    }

    private static KnownDisplayEntry? ReadEntry(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;

        return new KnownDisplayEntry
        {
            EDIDKey = ReadString(item, nameof(KnownDisplayEntry.EDIDKey)),
            OriginalName = ReadString(item, nameof(KnownDisplayEntry.OriginalName)),
            EDIDSerial = ReadString(item, nameof(KnownDisplayEntry.EDIDSerial)),
            WasEverDDCCapable = ReadBool(item, nameof(KnownDisplayEntry.WasEverDDCCapable)),
            LastBusBrightness = ReadNullableInt(item, nameof(KnownDisplayEntry.LastBusBrightness))
        };
    }

    private static string ReadString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool ReadBool(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static int? ReadNullableInt(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.Null) return null;
        return value.TryGetInt32(out int result) ? result : null;
    }

    private static void WriteEntries(Utf8JsonWriter writer, IEnumerable<KnownDisplayEntry> entries)
    {
        writer.WriteStartArray();
        foreach (KnownDisplayEntry entry in entries)
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(KnownDisplayEntry.EDIDKey), entry.EDIDKey);
            writer.WriteString(nameof(KnownDisplayEntry.OriginalName), entry.OriginalName);
            writer.WriteString(nameof(KnownDisplayEntry.EDIDSerial), entry.EDIDSerial);
            writer.WriteBoolean(nameof(KnownDisplayEntry.WasEverDDCCapable), entry.WasEverDDCCapable);
            if (entry.LastBusBrightness.HasValue)
                writer.WriteNumber(nameof(KnownDisplayEntry.LastBusBrightness), entry.LastBusBrightness.Value);
            else
                writer.WriteNull(nameof(KnownDisplayEntry.LastBusBrightness));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static KnownDisplayEntry Clone(KnownDisplayEntry src) => new()
    {
        EDIDKey = src.EDIDKey,
        OriginalName = src.OriginalName,
        EDIDSerial = src.EDIDSerial,
        WasEverDDCCapable = src.WasEverDDCCapable,
        LastBusBrightness = src.LastBusBrightness
    };

    /// <summary>
    /// Records the last value confirmed by matching read-back from <paramref name="edidKey"/>'s brightness
    /// VCP and schedules a debounced save. Captures acknowledged bus state regardless of which writer drove it
    /// - slider drag, master propagation, profile load, or curve.
    /// No-op when the key is unknown - we only stamp displays we've already Register()-ed.
    /// </summary>
    public void StampLastBusBrightness(string edidKey, double value)
    {
        if (string.IsNullOrEmpty(edidKey)) return;
        if (Volatile.Read(ref _disposed) != 0) return;

        int clamped = (int)Math.Round(Math.Clamp(value, min: 0, max: 100));
        lock (_gate)
        {
            KnownDisplayEntry? entry = _entries.FirstOrDefault(e => e.EDIDKey == edidKey);
            if (entry == null || entry.LastBusBrightness == clamped) return;

            entry.LastBusBrightness = clamped;
            _dirty = true;
        }

        ScheduleDebouncedSave();
    }

    private void ScheduleDebouncedSave()
    {
        // Change resets the timer's due time - successive stamps within KnownDisplayStampDebounceMs collapse
        // into one trailing-edge fire.
        if (Volatile.Read(ref _disposed) != 0) return;
        try { _stampDebounceTimer.Change(TimeConstants.KnownDisplayStampDebounceMs, Timeout.Infinite); }
        catch (ObjectDisposedException)
        {
            /* racing dispose; safe to drop */
        }
    }

    private void FlushPendingSave()
    {
        lock (_gate) SaveLocked();
    }

    /// <summary>Writes a pending stamp (only if something changed) and stops the debounce timer. Call at exit.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;
        try { _stampDebounceTimer.Change(Timeout.Infinite, Timeout.Infinite); }
        catch (ObjectDisposedException)
        {
            /* fine */
        }

        FlushPendingSave();
        _stampDebounceTimer.Dispose();
    }
}
