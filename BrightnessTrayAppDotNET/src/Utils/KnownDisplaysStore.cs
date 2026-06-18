using System.Text.Json;
using Timer = System.Threading.Timer;

namespace BrightnessTrayAppDotNET.Utils;

/// <summary>
/// Persistent registry of every unique display the app has ever enumerated, keyed by EDIDKey.
/// Extracted from <see cref="AppSettings.KnownDisplays"/> so monitor enumeration no longer drags
/// the entire settings XML through a write on every refresh;
/// the registry grows without bound (disconnected monitors are never removed)
/// and was the largest contributor to settings-file churn.
///
/// Entries themselves are still <see cref="KnownDisplayEntry"/> instances so the type is shared with the legacy
/// XML element; only the persistence path differs.
/// </summary>
public sealed class KnownDisplaysStore : IDisposable
{
    private static readonly JsonWriterOptions s_jsonWriterOptions = new() { Indented = true, };

    // Trailing-edge debounce for Stamp* calls. A 60Hz slider drag would otherwise rewrite
    // displays.json on every integer transition; this coalesces a burst into one save once
    // the drag settles. Single timer for the whole store - fine because saves serialize the
    // entire list anyway, so per-key debouncing wouldn't reduce I/O.
    private const int StampDebounceMs = 500;

    private readonly string _path;
    private readonly Timer _stampDebounceTimer;
    private int _disposed;

    private readonly Lock _gate = new();
    private List<KnownDisplayEntry> _entries = [];

    public KnownDisplaysStore(string path)
    {
        _path = path;
        _stampDebounceTimer = new Timer(
            _ => FlushPendingSave(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public KnownDisplaysStore() : this(GetDefaultPath()) { }

    /// <summary>
    /// Snapshot of the current entries.
    /// Safe to enumerate without holding the store's internal lock -
    /// mutations replace the list reference rather than mutating in place when the file is reloaded.
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
        return Path.Combine(appFolder, "displays.json");
    }

    /// <summary>
    /// Loads <c>displays.json</c> if present.
    /// First-run migration: when the JSON file is absent and <paramref name="legacy"/> contains entries
    /// (still living inside settings.xml from before this extraction),
    /// copies them across and writes the JSON file so subsequent loads find it on disk.
    /// </summary>
    public void Load(IEnumerable<KnownDisplayEntry>? legacy = null)
    {
        lock (_gate)
        {
            if (TryReadFromDisk(out List<KnownDisplayEntry> loaded))
            {
                _entries = loaded;
                return;
            }

            // First run after extraction: migrate from AppSettings.KnownDisplays so users upgrading
            // from an older build don't lose their accumulated history
            // (and, crucially, their sticky WasEverDDCCapable flags -
            // DDCRecoveryService keys candidate selection off those).
            List<KnownDisplayEntry> seed = legacy?
                .Where(e => !string.IsNullOrEmpty(e.EDIDKey))
                .Select(Clone)
                .ToList() ?? [];

            _entries = seed;

            if (seed.Count > 0) SaveLocked();
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

        bool changed = false;
        lock (_gate)
        {
            foreach (KnownDisplayEntry e in entries)
            {
                if (string.IsNullOrEmpty(e.EDIDKey)) continue;

                if (RegisterLocked(e)) changed = true;
            }

            if (changed) SaveLocked();
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
            SaveLocked();
            return true;
        }
    }

    /// <summary>
    /// Persists the current in-memory list to JSON.
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
            _entries.Add(new KnownDisplayEntry
            {
                EDIDKey = incoming.EDIDKey,
                OriginalName = incoming.OriginalName,
                EDIDSerial = incoming.EDIDSerial,
                WasEverDDCCapable = incoming.WasEverDDCCapable,
                LastBusBrightness = incoming.LastBusBrightness,
            });
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
        return changed;
    }

    private bool TryReadFromDisk(out List<KnownDisplayEntry> loaded)
    {
        loaded = [];
        try
        {
            if (!File.Exists(_path)) return false;

            using FileStream stream = File.OpenRead(_path);
            if (stream.Length == 0) return false;

            using JsonDocument document = JsonDocument.Parse(stream);
            loaded = [.. ReadEntries(document.RootElement).Where(e => !string.IsNullOrEmpty(e.EDIDKey))];
            return true;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"KnownDisplaysStore: load failed ({_path}): {ex.Message}");
            return false;
        }
    }

    private void SaveLocked()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using FileStream stream = new(_path, FileMode.Create, FileAccess.Write, FileShare.None);
            using Utf8JsonWriter writer = new(stream, s_jsonWriterOptions);
            WriteEntries(writer, _entries);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"KnownDisplaysStore: save failed ({_path}): {ex.Message}");
        }
    }

    private static List<KnownDisplayEntry> ReadEntries(JsonElement root)
    {
        List<KnownDisplayEntry> entries = [];
        if (root.ValueKind != JsonValueKind.Array) return entries;

        foreach (JsonElement item in root.EnumerateArray())
        {
            KnownDisplayEntry? entry = ReadEntry(item);
            if (entry != null) entries.Add(entry);
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
            LastBusBrightness = ReadNullableInt(item, nameof(KnownDisplayEntry.LastBusBrightness)),
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
        LastBusBrightness = src.LastBusBrightness,
    };

    /// <summary>
    /// Records the last value successfully written to <paramref name="edidKey"/>'s DDC brightness
    /// VCP and schedules a debounced save. Captures the *bus* value (what the user sees on screen),
    /// regardless of which writer drove it - slider drag, master propagation, profile load, curve.
    /// No-op when the key is unknown - we only stamp displays we've already Register()-ed.
    /// </summary>
    public void StampLastBusBrightness(string edidKey, double value)
    {
        if (string.IsNullOrEmpty(edidKey)) return;
        if (Volatile.Read(ref _disposed) != 0) return;

        int clamped = (int)Math.Round(Math.Clamp(value, 0, 100));
        bool changed;
        lock (_gate)
        {
            KnownDisplayEntry? entry = _entries.FirstOrDefault(e => e.EDIDKey == edidKey);
            if (entry == null) return;
            changed = entry.LastBusBrightness != clamped;
            if (changed) entry.LastBusBrightness = clamped;
        }

        if (changed) ScheduleDebouncedSave();
    }

    /// <summary>
    /// Force any pending debounced save to flush now. Call on app shutdown so a stamp that
    /// arrived in the last <see cref="StampDebounceMs"/> isn't lost when the process exits.
    /// </summary>
    public void Flush()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _stampDebounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        FlushPendingSave();
    }

    private void ScheduleDebouncedSave()
    {
        // Change resets the timer's due time - successive stamps within StampDebounceMs collapse
        // into one trailing-edge fire.
        if (Volatile.Read(ref _disposed) != 0) return;
        try { _stampDebounceTimer.Change(StampDebounceMs, Timeout.Infinite); }
        catch (ObjectDisposedException)
        {
            /* racing dispose; safe to drop */
        }
    }

    private void FlushPendingSave()
    {
        lock (_gate) SaveLocked();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _stampDebounceTimer.Change(Timeout.Infinite, Timeout.Infinite); }
        catch (ObjectDisposedException)
        {
            /* fine */
        }

        FlushPendingSave();
        _stampDebounceTimer.Dispose();
    }
}
