using TrayAppDotNETCommon.Serialization;

namespace BrightnessTrayAppDotNET.Services;

/// <summary>
/// Manages brightness profiles - loading, saving, and applying. <c>profiles.xml</c> goes through a
/// <see cref="TrayXmlFileStore{T}"/>: a corrupt file is kept as a <c>.corrupt-*</c> backup, a locked one is never
/// overwritten, and writes are atomic.
/// </summary>
public sealed class ProfileManager : IDisposable
{
    private readonly TrayXmlFileStore<ProfileCollection> _store;
    private int _selectedIndex;
    private int _disposed;

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

    /// <summary>Loads <paramref name="profilesPath"/>, creating the default four profiles when it is missing.</summary>
    public ProfileManager(string profilesPath)
    {
        _store = new TrayXmlFileStore<ProfileCollection>(
            profilesPath,
            CreateDefault,
            static message => TADNLog.Log($"ProfileManager: {message}"),
            static collection =>
            {
                NormalizeCurves(collection);
                return false;
            });
        Profiles = _store.Load();
        _selectedIndex = Profiles.LastSelectedIndex;
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
            if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;
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
                    IsSliderEnabled = monitor.SliderState != SliderState.Disabled
                });
            }
        }

        Save();
    }

    /// <summary>
    /// Looks up the saved <see cref="MonitorState"/> for a live <see cref="MonitorInfo"/> by
    /// <see cref="MonitorState.EDIDKey"/>. Centralised so save / apply / preview / divergence-check all use the
    /// same matching rule and can't drift relative to each other.
    /// </summary>
    public static MonitorState? FindStateForMonitor(IList<MonitorState> states, MonitorInfo monitor) =>
        string.IsNullOrEmpty(monitor.EDIDKey)
            ? null
            : states.FirstOrDefault(s =>
                !string.IsNullOrEmpty(s.EDIDKey)
                && string.Equals(s.EDIDKey, monitor.EDIDKey, StringComparison.Ordinal));

    private static MonitorState CloneMonitorState(MonitorState s) => new()
    {
        EDIDKey = s.EDIDKey,
        Brightness = s.Brightness,
        IsPoweredOn = s.IsPoweredOn,
        IsSliderEnabled = s.IsSliderEnabled
    };

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
            MonitorInfo? monitor = string.IsNullOrEmpty(state.EDIDKey)
                ? null
                : monitors.FirstOrDefault(m =>
                    !string.IsNullOrEmpty(m.EDIDKey)
                    && string.Equals(m.EDIDKey, state.EDIDKey, StringComparison.Ordinal));

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
        if (Volatile.Read(ref _disposed) != 0) return;
        _store.SaveNow();
    }

    /// <summary>
    /// Writes the current profiles and waits for the write. Curve edits update
    /// <see cref="BrightnessProfile.EnvironmentalCurve"/> in place and rely on the Environmental page's debounce to
    /// call <see cref="Save"/>, which may still be pending at exit.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

        _store.SaveNow();
        _store.Flush();

        SelectedProfileChanged = null;
        UnsavedChangesStatusChanged = null;
        ProfilesListChanged = null;
    }

    /// <summary>
    /// Gets the default profiles file path.
    /// </summary>
    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, path2: "profiles.xml");
    }
}
