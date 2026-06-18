using BrightnessTrayAppDotNET.Utils;

namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Manages brightness profiles - loading, saving, and applying.
/// </summary>
public sealed class ProfileManager
{
    private const string EDIDIDPrefix = "edid:";

    private readonly string _profilesPath;
    private readonly KnownDisplaysStore _knownDisplays;
    private int _selectedIndex;

    // Set by MigrateLegacyMonitorStates when at least one MonitorState was upgraded during load,
    // so the constructor can persist the migrated XML eagerly (otherwise the next user-driven Save
    // would do it, but we'd rather not leave the file in its legacy shape any longer than necessary).
    private bool _migrationDirty;

    /// <summary>
    /// Event raised when the selected profile changes.
    /// </summary>
    public event Action<int>? SelectedProfileChanged;

    /// <summary>
    /// Event raised when the unsaved changes status changes.
    /// </summary>
    public event Action<bool>? UnsavedChangesStatusChanged;

    /// <summary>
    /// Raised when the user-visible shape of the profile list changes - a slot was renamed, the slots
    /// were reordered, or (less commonly) added/removed. Subscribers re-read whatever they cache about
    /// the list. Not raised for selection changes (see <see cref="SelectedProfileChanged"/>) or for
    /// per-monitor edits inside a profile.
    /// </summary>
    public event Action? ProfilesListChanged;

    /// <summary>
    /// Invoked by callers that mutate the profile list shape directly (currently the Profiles page's
    /// rename-commit and Apply-swaps paths). Kept on this manager rather than spread across pages so a
    /// future caller has one fan-out point.
    /// </summary>
    public void RaiseProfilesListChanged() => ProfilesListChanged?.Invoke();

    /// <summary>
    /// Currently selected profile index.
    /// </summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        private set
        {
            if (_selectedIndex != value)
            {
                _selectedIndex = value;
                SelectedProfileChanged?.Invoke(value);
            }
        }
    }

    /// <summary>
    /// The loaded profile collection.
    /// </summary>
    public ProfileCollection Profiles { get; }

    /// <summary>
    /// Whether the current monitor state differs from the saved profile.
    /// </summary>
    public bool HasUnsavedChanges { get; private set; }

    public ProfileManager() : this(GetDefaultPath())
    {
    }

    /// <summary>
    /// <paramref name="knownDisplays"/> is the EDID resolver consulted by the load-time legacy-ID
    /// migration; null falls back to a default-constructed store backed by <c>displays.json</c>
    /// (the same file <see cref="MonitorService"/> populates), so a parameterless construction
    /// still gets the migration without needing the caller to wire up the dependency.
    /// </summary>
    public ProfileManager(string profilesPath, KnownDisplaysStore? knownDisplays = null)
    {
        _profilesPath = profilesPath;
        _knownDisplays = knownDisplays ?? new KnownDisplaysStore();
        // Ensure the store is populated even when the caller didn't pre-load it; idempotent if it was.
        _knownDisplays.Load();
        bool fileExisted = File.Exists(_profilesPath);
        Profiles = LoadOrCreate();
        _selectedIndex = Profiles.LastSelectedIndex;

        // Persist the default collection on first run so the file is visible to users
        // (matches the AppSettings behavior). Also persists the migrated form on subsequent
        // runs when LoadOrCreate flipped any legacy-ID entries to EDIDKey.
        if (!fileExisted || _migrationDirty) Save();
    }

    /// <summary>
    /// Selects a profile by index and applies it to the monitors.
    /// The master slider is derived from individuals and is not written here -
    /// the caller is responsible for recomputing it
    /// (and for mirroring <see cref="BrightnessProfile.MasterSliderMode"/>
    /// into <see cref="AppSettings.MasterSliderMode"/>) once this returns.
    /// PropertyChanged on each affected <see cref="MonitorInfo"/> is suspended for the entire apply,
    /// including the <see cref="SelectedProfileChanged"/> notification:
    /// subscribers see one consolidated event per affected property, fired AFTER the new index is in effect,
    /// so a dirty-check triggered by SelectedProfileChanged compares the now-current monitor state against
    /// the now-current profile and (correctly) sees no divergence.
    /// The optional <paramref name="applyNightLight"/> callback is invoked with the profile's saved strength -
    /// callers that route this through a separate <see cref="MonitorInfo"/> instance (e.g. NightLightMonitor)
    /// must wrap the entire <c>SelectProfile</c> call in their own <see cref="MonitorInfo.SuspendNotifications"/>
    /// scope, since this method only suspends the <paramref name="monitors"/> collection it was handed.
    /// </summary>
    public void SelectProfile(int index, IList<MonitorInfo> monitors, Action<int>? applyNightLight = null)
    {
        if (index < 0 || index >= Profiles.Profiles.Count) return;

        BrightnessProfile profile = Profiles.Profiles[index];
        // Apply target profile, flip SelectedIndex, and only THEN flush PropertyChanged.
        // Reordering matters: any handler that reacts to SelectedProfileChanged (autosave's dirty-check
        // is the canonical example) needs to see SelectedIndex already advanced when it later
        // observes the consolidated MonitorInfo notifications, otherwise it would compare new monitor
        // state against the stale outgoing profile and either flash the save indicator or - with
        // autosave on - write the new state into the previous profile.
        using (DeferMonitorNotifications(monitors))
        {
            ApplyProfile(profile, monitors, includeBrightness: true);
            applyNightLight?.Invoke(profile.NightLight);
            SelectedIndex = index;
            Profiles.LastSelectedIndex = index;
        }

        Save();
    }

    /// <summary>
    /// Applies the currently selected profile to the monitors without saving.
    /// When <paramref name="includeBrightness"/> is false, the per-monitor brightness values are left untouched
    /// (caller is expected to seed them from the live hardware state).
    /// Everything else - enable/disable, power - is always loaded so the UI reflects the saved profile structure.
    /// The master slider is never written; the caller recomputes it after return.
    /// PropertyChanged on each <paramref name="monitors"/> entry is suspended across the apply and flushed
    /// once at scope exit, so subscribers see one consolidated event per affected property instead of one
    /// per intermediate setter call.
    /// </summary>
    public void ApplyCurrentProfile(IList<MonitorInfo> monitors, bool includeBrightness)
    {
        if (SelectedIndex < 0 || SelectedIndex >= Profiles.Profiles.Count) return;

        using (DeferMonitorNotifications(monitors))
            ApplyProfile(Profiles.Profiles[SelectedIndex], monitors, includeBrightness);
    }

    /// <summary>
    /// Opens a single composite scope that suspends <see cref="MonitorInfo.PropertyChanged"/> on every entry
    /// of <paramref name="monitors"/>. Disposing the returned scope flushes all pending notifications,
    /// even if a mid-apply exception unwinds through the using block - each per-monitor suspension's
    /// IDisposable.Dispose is unconditional, so no entity is left in a half-suspended state.
    /// </summary>
    private static CompositeDisposable DeferMonitorNotifications(IList<MonitorInfo> monitors)
    {
        IDisposable[] scopes = new IDisposable[monitors.Count];
        for (int i = 0; i < monitors.Count; i++) scopes[i] = monitors[i].SuspendNotifications();
        return new CompositeDisposable(scopes);
    }

    private sealed class CompositeDisposable(IDisposable[] inner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            // Dispose every inner scope, even if one throws on flush - losing later flushes
            // because of an earlier handler exception would leave subsequent monitors in a
            // permanently-suspended state. Re-throw the first exception once the loop completes.
            Exception? first = null;
            foreach (IDisposable scope in inner)
            {
                try { scope.Dispose(); }
                catch (Exception ex) { first ??= ex; }
            }

            if (first != null) throw first;
        }
    }

    /// <summary>
    /// Saves the current state to the selected profile.
    /// <paramref name="masterSliderMode"/> is the app-wide tracking mode active at save time -
    /// the profile remembers it so reselecting restores the exact master behavior the user had.
    /// <paramref name="nightlight"/> captures the current night-light strength (0-100).
    /// </summary>
    public void SaveCurrentState(IList<MonitorInfo> monitors, MasterSliderMode masterSliderMode, int nightlight)
    {
        if (SelectedIndex < 0 || SelectedIndex >= Profiles.Profiles.Count) return;

        BrightnessProfile profile = Profiles.Profiles[SelectedIndex];
        profile.MasterSliderMode = masterSliderMode;
        profile.NightLight = nightlight;

        // Purely additive: update existing entries in place and append new ones,
        // but never drop entries for monitors absent from the current live list.
        // A display that's temporarily powered off, unplugged, or that transiently failed DDC enumeration
        // must keep its saved settings so they're restored on reconnect.
        foreach (MonitorInfo monitor in monitors)
        {
            MonitorState? existing = FindStateForMonitor(profile.MonitorStates, monitor);
            if (existing != null)
            {
                // EDIDKey is the authoritative key - refresh it from the live monitor every save
                // so a lazy migration (FindStateForMonitor populating an empty EDIDKey via the
                // legacy ID fallback) lands on disk too. Legacy ID is left as-is on the wire for
                // older readers; new code never reads or writes it.
                existing.EDIDKey = monitor.EDIDKey;
                existing.Brightness = (int)Math.Round(monitor.Brightness);
                existing.IsPoweredOn = monitor.IsPoweredOn;
                // Persist only the user-intent bit: "did the user exclude this row from master?"
                // Curve / sleep / failed states are transient runtime concerns and reconstitute from
                // the curve flags + DDC capability on next startup; saving them would freeze a
                // freshly-disabled-by-DDC monitor as "user-disabled" forever.
                existing.IsSliderEnabled = monitor.SliderState != SliderState.Disabled;
            }
            else
            {
                profile.MonitorStates.Add(new MonitorState
                {
                    EDIDKey = monitor.EDIDKey,
                    Brightness = (int)Math.Round(monitor.Brightness),
                    IsPoweredOn = monitor.IsPoweredOn,
                    IsSliderEnabled = monitor.SliderState != SliderState.Disabled,
                });
            }
        }

        Save();
    }

    /// <summary>
    /// Looks up the saved <see cref="MonitorState"/> for a live <see cref="MonitorInfo"/>.
    /// Authoritative match is by <see cref="MonitorState.EDIDKey"/>; legacy entries that haven't
    /// been migrated yet (load-time migration only handles <c>edid:</c>-prefix IDs - see
    /// <see cref="MigrateLegacyMonitorStates"/>) are still resolved through their stored
    /// <see cref="MonitorState.ID"/> and opportunistically upgraded in place: when the legacy-ID
    /// fallback hits and the live monitor exposes a non-empty EDIDKey, the saved entry's EDIDKey
    /// is populated so the next lookup goes through the primary path. The next user-driven
    /// <see cref="Save"/> persists the upgrade. Centralised so save / apply / preview /
    /// divergence-check all use the same matching rule and can't drift relative to each other.
    /// </summary>
    public static MonitorState? FindStateForMonitor(IList<MonitorState> states, MonitorInfo monitor)
    {
        if (!string.IsNullOrEmpty(monitor.EDIDKey))
        {
            MonitorState? byEDID = states.FirstOrDefault(s =>
                !string.IsNullOrEmpty(s.EDIDKey)
                && string.Equals(s.EDIDKey, monitor.EDIDKey, StringComparison.Ordinal));
            if (byEDID != null) return byEDID;
        }

        MonitorState? byLegacyID = states.FirstOrDefault(s =>
            !string.IsNullOrEmpty(LegacyIDOf(s)) && LegacyIDOf(s) == monitor.ID);
        if (byLegacyID != null
            && string.IsNullOrEmpty(byLegacyID.EDIDKey)
            && !string.IsNullOrEmpty(monitor.EDIDKey))
            byLegacyID.EDIDKey = monitor.EDIDKey;
        return byLegacyID;
    }

    // Read-only access to the obsolete MonitorState.ID, isolated to migration-aware code paths.
    // The wider codebase no longer touches the legacy property, but the load-time and lazy
    // migration logic must - wrap the suppression here so the rest of the file stays clean.
#pragma warning disable CS0618
    private static string LegacyIDOf(MonitorState state) => state.ID;
    private static MonitorState CloneMonitorState(MonitorState s) => new()
    {
        ID = s.ID,
        EDIDKey = s.EDIDKey,
        Brightness = s.Brightness,
        IsPoweredOn = s.IsPoweredOn,
        IsSliderEnabled = s.IsSliderEnabled,
    };
#pragma warning restore CS0618

    /// <summary>
    /// Gets the custom glyph for a profile, if any.
    /// </summary>
    public string? GetCustomGlyph(int index)
    {
        if (index >= 0 && index < Profiles.Profiles.Count) return Profiles.Profiles[index].CustomGlyph;

        return null;
    }

    /// <summary>
    /// Gets the user-supplied custom name for a profile, if any.
    /// Returns null when unset/blank so callers can fall back to a default label.
    /// </summary>
    public string? GetName(int index)
    {
        if (index >= 0 && index < Profiles.Profiles.Count) return Profiles.Profiles[index].Name;

        return null;
    }

    /// <summary>
    /// Rearranges stored profile data across slots.
    /// <paramref name="sourceIndexPerSlot"/> has one entry per profile slot;
    /// entry <c>i</c> is the original index of the profile whose stored data
    /// (CustomGlyph, MasterSliderMode, per-monitor states)
    /// should end up at slot <c>i</c> after the reorder.
    /// Slot indices themselves are fixed - only the data is reassigned,
    /// so <see cref="BrightnessProfile.Index"/> stays in sync with position
    /// and the user's "Profile N" buttons continue to refer to slot N.
    /// </summary>
    public void SwapProfileData(IReadOnlyList<int> sourceIndexPerSlot)
    {
        int count = Profiles.Profiles.Count;
        if (sourceIndexPerSlot.Count != count) return;

        // Reject invalid or non-permutation input - bail rather than partially mutate.
        bool[] seen = new bool[count];
        foreach (int src in sourceIndexPerSlot)
        {
            if (src < 0 || src >= count || seen[src]) return;

            seen[src] = true;
        }

        // Snapshot every profile's data first.
        // In-place reassignment without a snapshot would overwrite the source of a later slot's copy.
        (string? Name, string? CustomGlyph, MasterSliderMode Mode, int NightLight, List<MonitorState> States)[]
            snapshot = new (string?, string?, MasterSliderMode, int, List<MonitorState>)[count];
        for (int i = 0; i < count; i++)
        {
            BrightnessProfile p = Profiles.Profiles[i];
            List<MonitorState> copiedStates = [];
            // Clone preserves both EDIDKey and the obsolete ID so an in-flight reorder doesn't
            // strip the legacy fallback off entries that haven't reached the lazy-migration path yet.
            foreach (MonitorState s in p.MonitorStates) copiedStates.Add(CloneMonitorState(s));
            snapshot[i] = (p.Name, p.CustomGlyph, p.MasterSliderMode, p.NightLight, copiedStates);
        }

        for (int i = 0; i < count; i++)
        {
            BrightnessProfile target = Profiles.Profiles[i];
            (string? name, string? glyph, MasterSliderMode mode, int nightlight, List<MonitorState> states) =
                snapshot[sourceIndexPerSlot[i]];
            target.Name = name;
            target.CustomGlyph = glyph;
            target.MasterSliderMode = mode;
            target.NightLight = nightlight;
            target.MonitorStates = states;
        }

        Save();
    }

    /// <summary>
    /// Updates the user-supplied name of the profile at <paramref name="index"/> and persists.
    /// Empty/whitespace input is normalized to <c>null</c>
    /// so the default "Profile" label is shown rather than a literal empty string.
    /// </summary>
    public void RenameProfile(int index, string? name)
    {
        if (index < 0 || index >= Profiles.Profiles.Count) return;

        string? trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (Profiles.Profiles[index].Name == trimmed) return;

        Profiles.Profiles[index].Name = trimmed;
        Save();
    }

    /// <summary>
    /// Ensures the profile collection has at least the specified number of profiles.
    /// </summary>
    public void EnsureProfileCount(int count)
    {
        while (Profiles.Profiles.Count < count)
            Profiles.Profiles.Add(new BrightnessProfile { Index = Profiles.Profiles.Count });
        NormalizeCurves(Profiles);
        Save();
    }

    /// <summary>
    /// Checks if the current state differs from the saved profile and updates HasUnsavedChanges.
    /// </summary>
    public void CheckForUnsavedChanges(IList<MonitorInfo> monitors, MasterSliderMode masterSliderMode, int nightlight)
    {
        bool hasChanges = DetectChanges(monitors, masterSliderMode, nightlight);

        if (hasChanges != HasUnsavedChanges)
        {
            HasUnsavedChanges = hasChanges;
            UnsavedChangesStatusChanged?.Invoke(hasChanges);
        }
    }

    /// <summary>
    /// Peeks at whether the current monitor state differs from the saved profile
    /// without touching <see cref="HasUnsavedChanges"/> or firing the status event.
    /// Use this when you need the answer for a control-flow decision (e.g. autosave)
    /// and don't want to affect the UI's dirty indicator.
    /// </summary>
    public bool HasPendingChanges(IList<MonitorInfo> monitors, MasterSliderMode masterSliderMode, int nightlight)
        => DetectChanges(monitors, masterSliderMode, nightlight);

    /// <summary>
    /// Detects whether the current state differs from the saved profile.
    /// Brightness is compared after rounding to int, since that's how it's persisted (SaveCurrentState rounds) -
    /// otherwise fractional slider values would make the dirty flag stick even immediately after a save.
    /// </summary>
    private bool DetectChanges(IList<MonitorInfo> monitors, MasterSliderMode masterSliderMode, int nightlight)
    {
        if (SelectedIndex < 0 || SelectedIndex >= Profiles.Profiles.Count) return false;

        BrightnessProfile profile = Profiles.Profiles[SelectedIndex];

        if (profile.MasterSliderMode != masterSliderMode) return true;

        if (profile.NightLight != nightlight) return true;

        foreach (MonitorInfo monitor in monitors)
        {
            MonitorState? savedState = FindStateForMonitor(profile.MonitorStates, monitor);
            if (savedState == null)
            {
                // Live monitor with no entry in the profile - either a never-saved profile (empty MonitorStates),
                // or a hot-plug after the last save.
                // Either way, the profile is missing data for this monitor, so the current state diverges from saved.
                // SaveCurrentState appends a new entry, which clears the divergence next time around.
                return true;
            }

            if (savedState.Brightness != (int)Math.Round(monitor.Brightness)) return true;

            if (savedState.IsPoweredOn != monitor.IsPoweredOn) return true;

            // Saved bool is "user excluded from master?" - compare against the live equivalent
            // derived from SliderState, which is the in-memory source of truth.
            if (savedState.IsSliderEnabled != (monitor.SliderState != SliderState.Disabled)) return true;
        }

        return false;
    }

    private static void ApplyProfile(BrightnessProfile profile, IList<MonitorInfo> monitors, bool includeBrightness)
    {
        foreach (MonitorState state in profile.MonitorStates)
        {
            // EDIDKey is authoritative; legacy ID is consulted only for unmigrated entries
            // (LoadOrCreate migrates edid:-prefix IDs eagerly, FindStateForMonitor migrates the
            // rest lazily on first live-monitor match - see MigrateLegacyMonitorStates).
            // Mirrors FindStateForMonitor but iterates the other way.
            MonitorInfo? monitor = null;
            if (!string.IsNullOrEmpty(state.EDIDKey))
            {
                monitor = monitors.FirstOrDefault(m =>
                    !string.IsNullOrEmpty(m.EDIDKey)
                    && string.Equals(m.EDIDKey, state.EDIDKey, StringComparison.Ordinal));
            }

            if (monitor == null)
            {
                string legacyID = LegacyIDOf(state);
                if (!string.IsNullOrEmpty(legacyID))
                {
                    monitor = monitors.FirstOrDefault(m => m.ID == legacyID);
                    if (monitor != null
                        && string.IsNullOrEmpty(state.EDIDKey)
                        && !string.IsNullOrEmpty(monitor.EDIDKey))
                    {
                        // Lazy migration symmetric with FindStateForMonitor: backfill EDIDKey
                        // the moment a live monitor confirms the binding.
                        state.EDIDKey = monitor.EDIDKey;
                    }
                }
            }

            if (monitor != null)
            {
                if (includeBrightness) monitor.Brightness = state.Brightness;

                monitor.IsPoweredOn = state.IsPoweredOn;
                // Route the saved bool through the state machine so a Failed monitor stays Failed
                // (Failed wins over user toggle), and a curve-engaged row that the profile says is
                // "enabled" doesn't get clobbered out of CurveActive into Enabled.
                // OnUserToggleOff transitions any-non-Failed to Disabled; OnUserToggleOn only
                // transitions out of Disabled (other states are left alone here - the curve
                // service's toggle/sleep transitions remain authoritative for them).
                monitor.SliderState = state.IsSliderEnabled
                    ? SliderStateMachine.OnUserToggleOn(monitor.SliderState, curveEngaged: false,
                        inDisabledPeriod: false)
                    : SliderStateMachine.OnUserToggleOff(monitor.SliderState);
            }
        }
    }

    private ProfileCollection LoadOrCreate()
    {
        try
        {
            if (File.Exists(_profilesPath))
            {
                using FileStream stream = new(_profilesPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                ProfileCollection loaded = ProfileXml.Load(stream);
                NormalizeCurves(loaded);
                MigrateLegacyMonitorStates(loaded);
                return loaded;
            }
        }
        catch
        {
            // fall through to create default
        }

        return CreateDefault();
    }

    /// <summary>
    /// One-time, idempotent upgrade of <see cref="MonitorState"/> entries that were written before
    /// <see cref="MonitorState.EDIDKey"/> existed.
    /// <list type="bullet">
    /// <item><c>edid:&lt;serial&gt;</c>-prefix legacy IDs map directly to EDIDKey
    /// (<see cref="MonitorService"/>'s <c>ComputeEDIDKey</c> always emits that exact prefix);
    /// optionally cross-checked against <see cref="KnownDisplaysStore"/> when an entry is present.</item>
    /// <item><c>num:</c> and <c>port:</c> legacy IDs cannot be eagerly resolved - the mapping back to
    /// an EDIDKey requires a live <see cref="MonitorInfo"/>, which only the lazy fallback in
    /// <see cref="FindStateForMonitor"/> and <see cref="ApplyProfile"/> can supply on first match.</item>
    /// </list>
    /// Entries that can't be migrated keep their legacy ID intact (no data loss for offline displays
    /// not in <see cref="KnownDisplaysStore"/>); the runtime fallback chain is reduced but not removed
    /// because the app creates this manager parameterless and so the resolver can only see
    /// EDIDKeys, not the full live-monitor list.
    /// Sets <see cref="_migrationDirty"/> when at least one entry was upgraded so the constructor
    /// persists the migrated XML; running again on already-migrated data is a no-op.
    /// </summary>
    private void MigrateLegacyMonitorStates(ProfileCollection collection)
    {
        // Cache the set of known EDIDKeys so we can flag upgrades that resolve to an EDIDKey the
        // store has actually seen - useful telemetry, but not a hard requirement: the prefix itself
        // is sufficient because the legacy ID was produced by ComputeEDIDKey and so is already
        // exactly the EDIDKey we'd write today.
        HashSet<string> knownEDIDKeys = _knownDisplays.Entries
            .Where(e => !string.IsNullOrEmpty(e.EDIDKey))
            .Select(e => e.EDIDKey)
            .ToHashSet(StringComparer.Ordinal);

        bool anyUpgrade = false;
        foreach (BrightnessProfile profile in collection.Profiles)
        {
            foreach (MonitorState state in profile.MonitorStates)
            {
                // Already migrated - idempotent fast path.
                if (!string.IsNullOrEmpty(state.EDIDKey)) continue;

                string legacyID = LegacyIDOf(state);
                if (string.IsNullOrEmpty(legacyID)) continue;

                if (legacyID.StartsWith(EDIDIDPrefix, StringComparison.Ordinal))
                {
                    // edid:<serial> IS the EDIDKey by construction (ComputeEDIDKey == ComputeMonitorID
                    // with EDIDSerial strategy). Adopting the value directly is the eager migration.
                    state.EDIDKey = legacyID;
                    anyUpgrade = true;
                    if (knownEDIDKeys.Count > 0 && !knownEDIDKeys.Contains(legacyID))
                    {
                        WPFLog.Log(
                            $"ProfileManager: migrated EDIDKey '{legacyID}' not yet in KnownDisplays");
                    }
                }
                // num:N / port:... legacy IDs can't be mapped without a live MonitorInfo.
                // Intentionally left in place so re-plug-in through FindStateForMonitor /
                // ApplyProfile completes the migration on first match.
            }
        }

        _migrationDirty = anyUpgrade;
    }

    private static ProfileCollection CreateDefault()
    {
        ProfileCollection collection = new();
        for (int i = 0; i < 4; i++) collection.Profiles.Add(new BrightnessProfile { Index = i });
        NormalizeCurves(collection);
        return collection;
    }

    /// <summary>
    /// Backfills empty curve series with defaults
    /// and collapses any duplicate edge points to a single canonical point each.
    /// Run on every load and after any profile-creation path so the legacy profile accumulation bug
    /// (see <see cref="EnvironmentalCurve.EnsureNormalized"/>) self-heals from the next save onward.
    /// </summary>
    private static void NormalizeCurves(ProfileCollection collection)
    {
        foreach (BrightnessProfile profile in collection.Profiles) profile.EnvironmentalCurve.EnsureNormalized();
    }

    /// <summary>
    /// Persists the in-memory profile collection to disk.
    /// Public so callers that mutate profile data directly
    /// (e.g. the Environmental page's curve editor,
    /// which writes through the live <see cref="BrightnessProfile.EnvironmentalCurve"/> reference)
    /// can trigger a save without going through one of the higher-level apply paths.
    /// </summary>
    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_profilesPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            WriteAtomic(_profilesPath, stream => ProfileXml.Save(Profiles, stream));
        }
        catch (Exception ex)
        {
            // Best-effort: a locked file (AV scan), full disk, or roaming-profile hiccup
            // loses the latest edit but doesn't crash the app.
            WPFLog.Log($"ProfileManager.Save: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes <paramref name="finalPath"/> atomically by streaming <paramref name="writeContent"/> into
    /// <c><paramref name="finalPath"/>.tmp</c> (exclusive lock, FileShare.None) and then moving the tmp file
    /// over the destination via <see cref="File.Move(string, string, bool)"/> with overwrite.
    /// Defeats the FileMode.Create truncate-then-write window where a crash or concurrent writer
    /// could otherwise leave a zero-byte profiles.xml that the next load silently replaces with defaults.
    /// On failure the tmp file is best-effort deleted and the exception is rethrown
    /// to the caller's outer try so it can be logged through the existing path.
    /// Duplicated rather than extracted to a shared helper because <see cref="AppSettings"/> lives in
    /// a different namespace and the audit calls for the minimal localised change.
    /// </summary>
    private static void WriteAtomic(string finalPath, Action<Stream> writeContent)
    {
        string tmpPath = finalPath + ".tmp";
        try
        {
            using (FileStream stream = new(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                writeContent(stream);
                stream.Flush();
            }

            File.Move(tmpPath, finalPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
            catch
            {
                // ignored - the next save attempt will overwrite the tmp file anyway
            }

            throw;
        }
    }

    /// <summary>
    /// Persists current in-memory profile state on app shutdown.
    /// Curve edits update <see cref="BrightnessProfile.EnvironmentalCurve"/> in place and rely on the
    /// Environmental page's UI-side debounce timer to call <see cref="Save"/>;
    /// SessionEnding can fire while that debounce is still pending, bypassing the
    /// <c>SettingsWindow.OnClosing</c> flush.
    /// This method forces a synchronous final save so the latest in-memory state hits disk regardless of
    /// debounce state.
    /// Idempotent: <see cref="Save"/> just rewrites whatever is currently in memory, so multiple calls
    /// are harmless (and a no-op effectively, when nothing has changed since the last save).
    /// </summary>
    public void SaveOnShutdown() => Save();

    /// <summary>
    /// Gets the default profiles file path.
    /// </summary>
    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "profiles.xml");
    }
}
