using System.Xml.Serialization;

namespace FanControlTrayAppDotNET.Models;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public enum TrayIconStyle
{
    Dynamic,
    Static,
}

/// <summary>
/// Action taken when the tray icon is clicked or scrolled.
/// Skeleton ships with a no-op placeholder; extend with project-specific actions in your fork.
/// </summary>
public enum TrayClickAction
{
    Nothing,
    OpenSettings,
}

/// <summary>
/// Where the tray right-click menu appears.
/// Classic opens at the cursor position (the OS default for tray menus).
/// Modern docks the menu in the bottom-right corner of the primary work area with an 8px inset,
/// matching the Windows 11 system-flyout pattern.
/// </summary>
public enum ContextMenuPosition
{
    Classic,
    Modern,
}

/// <summary>
/// Root application settings class.
/// Skeleton scaffold with a few illustrative fields - extend with project-specific settings in your fork.
/// </summary>
[XmlRoot("AppSettings")]
public partial class AppSettings : ITrayAppDotNETUpdateSettings, ITrayAppDotNETKeepWarmSettings,
    ITrayAppDotNETStartupMemorySettings
{
    // General
    public bool RunOnStartup { get; set; } = true;
    public bool Autosave { get; set; } = true;

    // Fan-control general toggles. DefaultToRPMMode flips new Fan instances to RPMMode at
    // discovery time; existing fans keep whatever the user last picked.
    public bool DefaultToRPMMode { get; set; }

    // Fan properties. Applied to new Fans at discovery time as their initial values; existing
    // fans keep their per-fan overrides. Jumpstart and DeltaMax are always in duty cycle %
    // regardless of any per-fan RPMMode (per spec).
    public int DefaultJumpstartDutyCycle { get; set; } = 50;

    public int DefaultDeltaMaxDutyCycle { get; set; } = 100;

    // Reference into Curve.Curves by name. "None" is the sentinel for no curve assignment.
    public string DefaultAssignedCurve { get; set; } = "None";

    // Context menu
    public ContextMenuPosition ContextMenuPosition { get; set; } = ContextMenuPosition.Modern;
    public int ContextMenuFontSize { get; set; } = 15;

    // Theme
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    public NullableThemeColor TextColor { get; set; } = new();
    public NullableThemeColor BackgroundColor { get; set; } = new();
    public NullableThemeColor FlyoutBackgroundColor { get; set; } = new();
    public NullableThemeColor FlyoutTitleBarBackgroundColor { get; set; } = new();
    public NullableThemeColor FanCardBackgroundColor { get; set; } = new();
    public NullableThemeColor GroupCardBackgroundColor { get; set; } = new();
    public NullableThemeColor CardBorderColor { get; set; } = new();
    public TrayIconStyle TrayIconStyle { get; set; } = TrayIconStyle.Dynamic;
    public NullableThemeColor TrayIconColor { get; set; } = new();
    public bool EnableRoundedCorners { get; set; } = true;
    public bool SquareFlyoutTitleBarCorners { get; set; }
    public bool EnableCardBorders { get; set; }
    public bool EnableHoveredCardBorders { get; set; }
    public bool HideGroupedFanCardBorders { get; set; } = true;
    public bool UseGroupBackgroundForGroupedFanCards { get; set; }
    public int FlyoutCardSpacing { get; set; } = 1;
    public int FlyoutCardHorizontalInset { get; set; } = 1;
    public int FlyoutTitleBarCardSpacing { get; set; } = 2;

    // Tray icon interaction. Click actions are surfaced through TrayIconPage; the host wires what each
    // action does. The skeleton's TrayClickAction enum is a placeholder set extend it with app-specific
    // actions, then update App.xaml.cs's tray click handlers to dispatch on the chosen action.
    public bool TrayScrollEnabled { get; set; } = true;
    public TrayClickAction TrayDoubleClickAction { get; set; } = TrayClickAction.OpenSettings;
    public TrayClickAction TrayCtrlLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;

    // Flyout. The skeleton ships no flyout, but both reference apps it was lifted from expose an
    // undockable secondary window with these two knobs. Kept here so the FlyoutPage scaffold has
    // real properties to bind to; rip them out (along with FlyoutPage) if your fork never grows a
    // flyout.
    public bool AllowFlyoutUndock { get; set; } = true;

    public bool RestoreFlyoutUndockedOnStartup { get; set; } = true;

    // Persisted undock state. Written only on drag-release / explicit redock so a drag doesn't
    // saturate disk IO. FlyoutHasSavedPosition gates whether FlyoutLeft / FlyoutTop are restored.
    public bool FlyoutUndocked { get; set; }
    public bool FlyoutHasSavedPosition { get; set; }
    public double FlyoutLeft { get; set; }
    public double FlyoutTop { get; set; }
    public bool ShowNonFunctioningFans { get; set; } = true;

    // Tray icon tooltip composition. The flyout tooltip is always the application name; these
    // toggles add CPU / GPU temperature lines fed by LHMService DataSources. Both on by default.
    public bool ShowCPUTempInTooltip { get; set; } = true;
    public bool ShowGPUTempInTooltip { get; set; } = true;

    // Slider thumb appearance. Catalog is rebuilt from CreateDefaults() on every load so the
    // built-ins stay current with code; SerializedSliderThumb captures the user's currently-selected
    // option by Name and writes it back on save. Custom (non-builtin) options round-trip too:
    // InitializeSliderThumbCatalog appends the loaded option to the catalog if its Name doesn't
    // match a built-in, keeping the dropdown stable for the user.
    [XmlIgnore] public string SliderThumbGlyph { get; set; } = "Capsule";

    [XmlIgnore] public List<SliderThumbGlyphOption> SliderThumbOptions { get; set; } = [];

    [XmlElement("SliderThumb")]
    public SliderThumbGlyphOption? SerializedSliderThumb
    {
        get => SliderThumbOptions.FirstOrDefault(o => o.Name == SliderThumbGlyph);
        set => _loadedSliderThumb = value;
    }

    private SliderThumbGlyphOption? _loadedSliderThumb;

    // Auto-update. CheckForUpdatesEnabled gates the background poll loop entirely; flipping it off
    // cancels any in-flight wait without disposing UpdateCheckService. ShowUpdateNotificationsEnabled
    // surfaces a tray balloon when a newer version lands. ShowUpdateButtonInFlyout is a hook for a
    // flyout-floating Update! affordance (host wires it; the skeleton just persists the toggle).
    // UpdateCheckIntervalMs is the polling cadence in ms (clamped to [Min, Max] by UpdateCheckService).
    public bool CheckForUpdatesEnabled { get; set; } = true;
    public bool ShowUpdateNotificationsEnabled { get; set; }
    public bool ShowUpdateButtonInFlyout { get; set; } = true;
    public int UpdateCheckIntervalMs { get; set; } = TimeConstants.UpdateCheckIntervalDefaultMs;
    public bool KeepFlyoutWarm { get; set; } = true;
    public bool KeepTrayContextMenuWarm { get; set; } = true;
    public bool PurgeMemoryOnStartup { get; set; } = true;

    // Empty by default; defaults are seeded by EnsureDefaultHotkeys() after construction or load.
    // The previous in-place initializer collided with XmlSerializer's "append to existing list" behavior:
    // the deserializer adds <Binding> elements to the list returned by the getter, so any default
    // listed here would duplicate every time the saved settings.xml was reloaded.
    [XmlArray("Hotkeys")]
    [XmlArrayItem("Binding")]
    public List<HotkeyBinding> Hotkeys { get; set; } = [];

    [XmlArray("Fans")]
    [XmlArrayItem("Fan")]
    public List<Fan> Fans { get; set; } = [];

    [XmlArray("DataSources")]
    [XmlArrayItem("DataSource")]
    public List<DataSource> DataSources { get; set; } = [];

    [XmlArray("Curves")]
    [XmlArrayItem("Curve")]
    public List<Curve> Curves { get; set; } = [];

    [XmlArray("Deadbands")]
    [XmlArrayItem("DeadbandsList")]
    public List<DeadbandsList> Deadbands { get; set; } = [];

    [XmlArray("FanGroups")]
    [XmlArrayItem("Group")]
    public List<FanGroup> FanGroups { get; set; } = [];

    [XmlArray("FanProfiles")]
    [XmlArrayItem("Profile")]
    public List<FanProfile> FanProfiles { get; set; } = [];

    public int SelectedFanProfileIndex { get; set; }

    // Raised when any setting is changed through the settings window.
    public event Action? Changed;

    public void RaiseChanged() => Changed?.Invoke();

    public AppSettings()
    {
        WireColorCallbacks();
        InitializeSliderThumbCatalog();
    }

    // Seeds SliderThumbOptions from the built-in catalog. If a user-selected option was loaded
    // from XML, either points SliderThumbGlyph at the matching built-in (by Name) or appends the
    // loaded option to the catalog so it remains visible in the dropdown.
    public void InitializeSliderThumbCatalog()
    {
        List<SliderThumbGlyphOption> catalog = SliderThumbGlyphOption.CreateDefaults();
        if (_loadedSliderThumb is { } saved && !string.IsNullOrEmpty(saved.Name))
        {
            if (catalog.All(o => o.Name != saved.Name)) catalog.Add(saved);
            SliderThumbGlyph = saved.Name;
        }

        SliderThumbOptions = catalog;
    }

    /// <summary>
    /// Bridges every NullableThemeColor override on this instance to the global Changed event,
    /// so any color edit (committed hex or live-preview Temporary*) flows out through the same
    /// notification path as every other setting change.
    /// Idempotent: Unsubscribe runs first, so re-wiring after XmlSerializer replaces the ctor-wired
    /// instances post-deserialization can't double-fire.
    /// Specific listeners that want per-color granularity should attach via NullableThemeColor.Subscribe directly.
    /// </summary>
    public void WireColorCallbacks()
    {
        Action onChanged = RaiseChanged;
        foreach (NullableThemeColor color in EnumerateColorOverrides())
        {
            color.Unsubscribe(onChanged);
            color.Subscribe(onChanged);
        }
    }

    private IEnumerable<NullableThemeColor> EnumerateColorOverrides()
    {
        yield return TextColor;
        yield return BackgroundColor;
        yield return FlyoutBackgroundColor;
        yield return FlyoutTitleBarBackgroundColor;
        yield return FanCardBackgroundColor;
        yield return GroupCardBackgroundColor;
        yield return CardBorderColor;
        yield return TrayIconColor;
    }

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "settings.xml");
    }

    // The folder that holds settings.xml and other per-app data.
    // Used by the uninstaller's "delete settings" branch.
    public static string GetDefaultDirectory() =>
        Program.AppLocalAppDataDirectory;

    public void Save() => Save(GetDefaultPath());

    public void Save(string path)
    {
        try
        {
            EnsureFanProfileCount(3);
            SyncFanControlRegistriesForSave();

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            WriteAtomic(path, stream =>
            {
                SaveXml(stream);
            });
        }
        catch
        {
            // best-effort
        }
    }

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

    public static AppSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static AppSettings LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                AppSettings loaded = LoadXml(stream);
                loaded.WireColorCallbacks();
                loaded.InitializeSliderThumbCatalog();
                loaded.InitializeFanControlRegistries();

                // One-time cleanup of duplicate hotkey rows that may have accumulated from a prior build
                // that re-seeded the default hotkey on every launch.
                // Top up any defaults missing from the persisted list (e.g. when a new build ships a new
                // default action). Skips entries the user has tombstoned via the UI (RemovedByUser=true)
                // so an explicit removal isn't undone on the next launch.
                bool changed = loaded.DedupeHotkeysByIdentity();
                changed |= loaded.EnsureDefaultHotkeys();
                changed |= loaded.EnsureFanProfileCount(3);
                if (loaded.SelectedFanProfileIndex < 0 || loaded.SelectedFanProfileIndex >= loaded.FanProfiles.Count)
                {
                    loaded.SelectedFanProfileIndex = 0;
                    changed = true;
                }

                if (changed) loaded.Save(path);
                return loaded;
            }
        }
        catch
        {
            // fall through to default
        }

        AppSettings defaults = new();
        defaults.InitializeFanControlRegistries();
        defaults.EnsureDefaultHotkeys();
        defaults.EnsureFanProfileCount(3);
        defaults.Save(path);
        return defaults;
    }

    public bool EnsureFanProfileCount(int count)
    {
        bool added = false;
        while (FanProfiles.Count < count)
        {
            FanProfiles.Add(new FanProfile { Name = $"Profile {FanProfiles.Count + 1}" });
            added = true;
        }

        for (int i = 0; i < FanProfiles.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(FanProfiles[i].Name)) continue;
            FanProfiles[i].Name = $"Profile {i + 1}";
            added = true;
        }

        return added;
    }

    public void InitializeFanControlRegistries()
    {
        DedupeFansByDataSourceKey();
        SyncFanGroupsFromFans();

        FanGroup.FanGroups.Clear();
        foreach (FanGroup group in FanGroups)
            FanGroup.Register(group);

        DataSource.DataSources.Clear();
        foreach (DataSource source in DataSources)
            DataSource.Register(source);

        Curve.Curves.Clear();
        foreach (Curve curve in Curves)
            Curve.Register(curve);

        DeadbandsList.DeadbandsLists.Clear();
        foreach (DeadbandsList list in Deadbands)
            DeadbandsList.Register(list);
    }

    public void SyncFanControlRegistriesForSave()
    {
        DataSources =
        [
            .. DataSource.DataSources.Values
                .OrderBy(s => s.DataSourceKey, StringComparer.OrdinalIgnoreCase)
        ];
        Curves =
        [
            .. Curve.Curves.Values
                .OrderBy(c => c.CurveName, StringComparer.OrdinalIgnoreCase)
        ];
        Deadbands =
        [
            .. DeadbandsList.DeadbandsLists.Values
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
        ];

        DedupeFansByDataSourceKey();
        SyncFanGroupsFromFans();
        FanGroup.FanGroups.Clear();
        foreach (FanGroup group in FanGroups)
            FanGroup.Register(group);
    }

    public Fan? FindPersistedFan(string? dataSourceKey)
    {
        if (string.IsNullOrEmpty(dataSourceKey)) return null;
        foreach (Fan fan in Fans)
        {
            if (string.Equals(fan.DataSourceKey, dataSourceKey, StringComparison.OrdinalIgnoreCase))
                return fan;
        }

        return null;
    }

    public void UpsertPersistedFan(Fan fan)
    {
        if (string.IsNullOrEmpty(fan.DataSourceKey)) return;

        Fan snapshot = fan.CloneForPersistence();
        for (int i = 0; i < Fans.Count; i++)
        {
            if (!string.Equals(Fans[i].DataSourceKey, fan.DataSourceKey, StringComparison.OrdinalIgnoreCase))
                continue;

            Fans[i] = snapshot;
            SyncFanGroupsFromFans();
            return;
        }

        Fans.Add(snapshot);
        SyncFanGroupsFromFans();
    }

    public bool DedupeFansByDataSourceKey()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < Fans.Count; readIndex++)
        {
            Fan fan = Fans[readIndex];
            if (string.IsNullOrEmpty(fan.DataSourceKey)) continue;
            if (!seen.Add(fan.DataSourceKey)) continue;

            if (writeIndex != readIndex) Fans[writeIndex] = fan;
            writeIndex++;
        }

        if (writeIndex == Fans.Count) return false;

        Fans.RemoveRange(writeIndex, Fans.Count - writeIndex);
        return true;
    }

    public void SyncFanGroupsFromFans()
    {
        Dictionary<string, FanGroup> groups = FanGroups
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .GroupBy(g => g.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (Fan fan in Fans)
        {
            if (string.IsNullOrWhiteSpace(fan.Group)) continue;
            if (groups.ContainsKey(fan.Group)) continue;
            groups[fan.Group] = new FanGroup { Name = fan.Group, DisplayOrder = groups.Count, };
        }

        FanGroups =
        [
            .. groups.Values.OrderBy(g => g.DisplayOrder).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// The set of built-in hotkey bindings seeded for fresh installs and topped up on every launch.
    /// Identity is (Action, Parameter, BindingID): defaults always live on BindingID 0 (the primary row),
    /// so a user-added secondary binding (BindingID >= 1) for the same action does not block re-seeding
    /// the primary row.
    /// Skeleton ships with one illustrative binding; replace with your project's own defaults.
    /// </summary>
    private static IReadOnlyList<HotkeyBinding> CreateDefaultHotkeys() =>
    [
        new()
        {
            Action = HotkeyAction.OpenSettings,
            Parameter = string.Empty,
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Win | HotkeyModifiers.Alt,
            VirtualKey = 0x53, // VK_S
            Enabled = true,
            BindingID = 0,
        },
    ];

    /// <summary>
    /// True if the binding occupies the same identity slot as one of the built-in defaults
    /// (same Action, Parameter, and BindingID). Used by the settings UI to decide whether removing
    /// a binding should hard-delete it or keep it as a tombstone (RemovedByUser=true) so the default
    /// doesn't reappear on the next launch.
    /// </summary>
    public static bool IsDefaultHotkeyIdentity(HotkeyAction action, string parameter, int bindingID)
    {
        foreach (HotkeyBinding d in CreateDefaultHotkeys())
            if (d.Matches(action, parameter, bindingID))
                return true;
        return false;
    }

    /// <summary>
    /// Removes redundant hotkey rows that share the same identity tuple (Action, Parameter, BindingID),
    /// keeping the first occurrence.
    /// Returns true when at least one row was dropped (caller should persist).
    /// </summary>
    public bool DedupeHotkeysByIdentity()
    {
        HashSet<(HotkeyAction, string, int)> seen = [];
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < Hotkeys.Count; readIndex++)
        {
            HotkeyBinding b = Hotkeys[readIndex];
            (HotkeyAction, string, int) key = (b.Action, b.Parameter, b.BindingID);
            if (!seen.Add(key)) continue;

            if (writeIndex != readIndex) Hotkeys[writeIndex] = b;
            writeIndex++;
        }

        if (writeIndex == Hotkeys.Count) return false;

        Hotkeys.RemoveRange(writeIndex, Hotkeys.Count - writeIndex);
        return true;
    }

    /// <summary>
    /// Adds any built-in default hotkey bindings that aren't already represented in Hotkeys.
    /// "Represented" means: an existing entry with the same (Action, Parameter, BindingID) - including
    /// tombstoned entries with RemovedByUser=true - so a user who has explicitly removed a default
    /// is not re-seeded.
    /// Returns true when at least one default was newly added (caller should persist).
    /// </summary>
    public bool EnsureDefaultHotkeys()
    {
        bool added = false;
        foreach (HotkeyBinding d in CreateDefaultHotkeys())
        {
            bool present = false;
            foreach (HotkeyBinding existing in Hotkeys)
            {
                if (!existing.Matches(d.Action, d.Parameter, d.BindingID)) continue;

                present = true;
                break;
            }

            if (present) continue;

            Hotkeys.Add(new HotkeyBinding
            {
                Action = d.Action,
                Parameter = d.Parameter,
                Modifiers = d.Modifiers,
                VirtualKey = d.VirtualKey,
                Enabled = d.Enabled,
                BindingID = d.BindingID,
            });
            added = true;
        }

        return added;
    }
}
