using System.Xml.Serialization;
using TrayAppDotNETCommon.Serialization;
using TrayAppDotNETCommon.UI;

namespace FanControlTrayAppDotNET.Models;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public enum TrayIconStyle
{
    Dynamic,
    Static
}

/// <summary>
/// Action taken when the tray icon is clicked or scrolled.
/// Skeleton ships with a no-op placeholder; extend with project-specific actions in your fork.
/// </summary>
public enum TrayClickAction
{
    Nothing,
    OpenSettings
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
    Modern
}

public enum MultipleSliderValuesDisplayMode
{
    Disabled,
    Enabled,
    OnlyInManual
}

/// <summary>
/// Root application settings class.
/// Skeleton scaffold with a few illustrative fields - extend with project-specific settings in your fork.
/// </summary>
[XmlRoot("AppSettings")]
public class AppSettings : ITrayAppDotNETUpdateSettings, ITrayAppDotNETRenderingSettings, ITrayAppDotNETWarmWindowSettings,
    ITrayAppDotNETTrayMenuSettings, ISettingsSidebarWidthSettings, IFlyoutDockSettings,
    ITrayXmlSerializationCallbacks
{
    private const string CPUNickname = "CPU";
    private const string GPUNickname = "GPU";
    private const string HardwareTypeCPUTarget = "{HardwareType.CPU}";
    private const string HardwareTypeGPUTarget = "{HardwareType.GPU}";
    private const string PreviousDefaultCPUTargetRegex = ".*(CPU|Processor|Ryzen|Threadripper|Intel.*Core|Core.*Processor).*";
    private const string PreviousDefaultGPUTargetRegex = ".*(GPU|Graphics|NVIDIA|GeForce|Radeon|Arc).*";
    private const string ProbeTargetRegex_Tdie = "\\(Tdie\\)";
    private const string ProbeTargetRegex_TctlTdie = "\\(Tctl/Tdie\\)";
    private const string ProbeTargetRegex_SMU = "\\(SMU\\)";
    private const string ProbeTargetRegex_CPUCore = "CPU Core";

    // General
    public bool RunOnStartup { get; set; } = true;
    public bool Autosave { get; set; } = true;

    // Fan-control general toggles. DefaultToRPMMode flips new Fan instances to RPMMode at
    // discovery time; existing fans keep whatever the user last picked.
    public bool DefaultToRPMMode { get; set; }

    // Fan properties. Applied to new Fans at discovery time as their initial values; existing
    // fans keep their per-fan overrides. Property-unit toggles default to duty cycle.
    public int DefaultJumpstartDutyCycle { get; set; } = 50;

    public int DefaultDeltaMaxDutyCycle { get; set; } = 100;

    // Reference into Curve.Curves by name. "None" is the sentinel for no curve assignment.
    public string DefaultAssignedCurve { get; set; } = "None";

    // Context menu
    public ContextMenuPosition ContextMenuPosition { get; set; } = ContextMenuPosition.Modern;
    public int ContextMenuFontSize { get; set; } = 15;
    public bool UseSystemSubmenuShowDelay { get; set; }

    public int SubmenuShowDelayMs
    {
        get;
        set => field = Math.Clamp(
            value,
            TimeConstants.TrayMenuSubmenuShowDelayMinMs,
            TimeConstants.TrayMenuSubmenuShowDelayMaxMs);
    } = TimeConstants.TrayMenuSubmenuShowDelayDefaultMs;

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
    public bool UseWindows11SettingsNavigation { get; set; }
    public double SettingsSidebarWidth { get; set; }
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

    public bool ClampUndockedFlyoutToScreen { get; set; } = true;

    public bool RestoreFlyoutUndockedOnStartup { get; set; } = true;

    // Persisted undock state. Written only on drag-release / explicit redock so a drag doesn't
    // saturate disk IO. FlyoutHasSavedPosition gates whether FlyoutLeft / FlyoutTop are restored.
    public bool FlyoutUndocked { get; set; }
    public bool FlyoutHasSavedPosition { get; set; }
    public double FlyoutLeft { get; set; }
    public double FlyoutTop { get; set; }
    public bool ShowNonFunctioningFans { get; set; } = true;

    [XmlIgnore]
    public MultipleSliderValuesDisplayMode ShowMultipleSliderValuesMode { get; set; } =
        MultipleSliderValuesDisplayMode.OnlyInManual;

    [XmlElement("ShowMultipleSliderValues")]
    public string SerializedShowMultipleSliderValuesMode
    {
        get => ShowMultipleSliderValuesMode.ToString();
        set => ShowMultipleSliderValuesMode = ParseMultipleSliderValuesDisplayMode(value);
    }

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

    [XmlIgnore] public string CurveSliderThumbGlyph { get; set; } = "Diamond";

    [XmlIgnore] public List<SliderThumbGlyphOption> SliderThumbOptions { get; set; } = [];

    [XmlElement("SliderThumb")]
    public SliderThumbGlyphOption? SerializedSliderThumb
    {
        get => SliderThumbOptions.FirstOrDefault(o => o.Name == SliderThumbGlyph);
        set => _loadedSliderThumb = value;
    }

    [XmlElement("CurveSliderThumb")]
    public SliderThumbGlyphOption? SerializedCurveSliderThumb
    {
        get => SliderThumbOptions.FirstOrDefault(o => o.Name == CurveSliderThumbGlyph);
        set => _loadedCurveSliderThumb = value;
    }

    private SliderThumbGlyphOption? _loadedSliderThumb;

    private SliderThumbGlyphOption? _loadedCurveSliderThumb;

    // Auto-update. CheckForUpdatesEnabled gates the background poll loop entirely; flipping it off
    // cancels any in-flight wait without disposing UpdateCheckService. ShowUpdateNotificationsEnabled
    // surfaces a tray balloon when a newer version lands. ShowUpdateButtonInFlyout is a hook for a
    // flyout-floating Update! affordance (host wires it; the skeleton just persists the toggle).
    // UpdateCheckIntervalMs is the polling cadence in ms (clamped to [Min, Max] by UpdateCheckService).
    public bool CheckForUpdatesEnabled { get; set; } = true;
    public bool ShowUpdateNotificationsEnabled { get; set; }
    public bool ShowUpdateButtonInFlyout { get; set; } = true;
    public int UpdateCheckIntervalMs { get; set; } = TimeConstants.UpdateCheckIntervalDefaultMs;
    public int SkippedUpdateVersion { get; set; }
    public bool KeepFlyoutWarm { get; set; } = true;
    public bool KeepTrayContextMenuWarm { get; set; } = true;
    public TrayAppDotNETRenderingBackend RenderingBackend { get; set; } = TrayAppDotNETRenderingBackend.GPUPreferred;

    // Empty by default; defaults are seeded by EnsureDefaultHotkeys() after construction or load.
    // Keep defaults out of the initializer so load paths do not duplicate seeded bindings.
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

    [XmlArray("ProbeCards")]
    [XmlArrayItem("ProbeCard")]
    public List<ProbeCard> ProbeCards { get; set; } = [];

    public bool DeviceNicknamesInitialized { get; set; }

    [XmlArray("DeviceNicknameRules")]
    [XmlArrayItem("Rule")]
    public List<DeviceNicknameRule> DeviceNicknameRules { get; set; } = [];

    public bool ProbeNicknamesInitialized { get; set; }

    [XmlArray("ProbeNicknameRules")]
    [XmlArrayItem("Rule")]
    public List<DeviceNicknameRule> ProbeNicknameRules { get; set; } = [];

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

    public void OnTrayXmlSerializing()
    {
    }

    public void OnTrayXmlDeserializing()
    {
    }

    public void OnTrayXmlDeserialized()
    {
        WireColorCallbacks();
        InitializeSliderThumbCatalog();
        InitializeFanControlRegistries();
    }

    // Seeds SliderThumbOptions from the built-in catalog. If a user-selected option was loaded
    // from XML, either points SliderThumbGlyph at the matching built-in (by Name) or appends the
    // loaded option to the catalog so it remains visible in the dropdown.
    public void InitializeSliderThumbCatalog()
    {
        List<SliderThumbGlyphOption> catalog = SliderThumbGlyphOption.CreateDefaults();
        ApplyLoadedSliderThumb(catalog, _loadedSliderThumb, name => SliderThumbGlyph = name);
        ApplyLoadedSliderThumb(catalog, _loadedCurveSliderThumb, name => CurveSliderThumbGlyph = name);

        SliderThumbOptions = catalog;
    }

    /// <summary>
    /// Parses the current enum value and the prior bool-shaped setting value.
    /// </summary>
    private static MultipleSliderValuesDisplayMode ParseMultipleSliderValuesDisplayMode(string? value)
    {
        if (bool.TryParse(value, out bool enabled))
            return enabled ? MultipleSliderValuesDisplayMode.Enabled : MultipleSliderValuesDisplayMode.Disabled;

        return Enum.TryParse(value, ignoreCase: true, out MultipleSliderValuesDisplayMode mode)
            ? mode
            : MultipleSliderValuesDisplayMode.OnlyInManual;
    }

    private static void ApplyLoadedSliderThumb(
        List<SliderThumbGlyphOption> catalog,
        SliderThumbGlyphOption? saved,
        Action<string> select)
    {
        if (saved is not { } option || string.IsNullOrEmpty(option.Name)) return;
        if (catalog.All(o => o.Name != option.Name)) catalog.Add(option);
        select(option.Name);
    }

    /// <summary>
    /// Bridges every NullableThemeColor override on this instance to the global Changed event,
    /// so any color edit (committed hex or live-preview Temporary*) flows out through the same
    /// notification path as every other setting change.
    /// Idempotent: Unsubscribe runs first, so re-wiring after the generated reader replaces the ctor-wired
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
            TrayXmlSerializer.WriteFile(path, this);
        }
        catch
        {
            // best-effort
        }
    }

    public static AppSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static AppSettings LoadOrDefault(string path)
    {
        try
        {
            if (TrayXmlSerializer.TryReadFile(path, out AppSettings? loaded))
            {
                // One-time cleanup of duplicate hotkey rows that may have accumulated from a prior build
                // that re-seeded the default hotkey on every launch.
                // Top up any defaults missing from the persisted list (e.g. when a new build ships a new
                // default action). Skips entries the user has tombstoned via the UI (RemovedByUser=true)
                // so an explicit removal isn't undone on the next launch.
                bool changed = loaded.DedupeHotkeysByIdentity();
                changed |= loaded.EnsureDefaultHotkeys();
                changed |= loaded.EnsureFanProfileCount(3);
                changed |= loaded.EnsureDefaultDeviceNicknameRules();
                changed |= loaded.EnsureDefaultProbeNicknameRules();
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
        defaults.EnsureDefaultDeviceNicknameRules();
        defaults.EnsureDefaultProbeNicknameRules();
        defaults.Save(path);
        return defaults;
    }

    /// <summary>
    /// Seeds first-run hardware-type device nickname rules.
    /// </summary>
    public bool EnsureDefaultDeviceNicknameRules()
    {
        List<DeviceNicknameRule> defaultRules = BuildDefaultDeviceNicknameRules();

        if (!DeviceNicknamesInitialized)
        {
            DeviceNicknameRules = defaultRules;
            DeviceNicknamesInitialized = true;
            return true;
        }

        return ReplacePreviousDefaultDeviceNicknameRules(defaultRules);
    }

    /// <summary>
    /// Merges hardware-type default nickname rules without deleting custom rules.
    /// </summary>
    public bool LoadDefaultDeviceNicknameRules()
    {
        List<DeviceNicknameRule> defaultRules = BuildDefaultDeviceNicknameRules();
        bool changed = MergeDefaultNicknameRules(DeviceNicknameRules, defaultRules);
        if (!DeviceNicknamesInitialized)
        {
            DeviceNicknamesInitialized = true;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Seeds first-run probe nickname rules.
    /// </summary>
    public bool EnsureDefaultProbeNicknameRules()
    {
        if (ProbeNicknamesInitialized) return false;

        ProbeNicknameRules = BuildDefaultProbeNicknameRules();
        ProbeNicknamesInitialized = true;
        return true;
    }

    /// <summary>
    /// Merges default probe nickname rules without deleting custom rules.
    /// </summary>
    public bool LoadDefaultProbeNicknameRules()
    {
        List<DeviceNicknameRule> defaultRules = BuildDefaultProbeNicknameRules();
        bool changed = MergeDefaultNicknameRules(ProbeNicknameRules, defaultRules);
        if (!ProbeNicknamesInitialized)
        {
            ProbeNicknamesInitialized = true;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Builds default hardware-type nickname rules.
    /// </summary>
    private static List<DeviceNicknameRule> BuildDefaultDeviceNicknameRules() =>
        [
            new DeviceNicknameRule
            {
                TargetRegex = HardwareTypeCPUTarget,
                ReplacementString = CPUNickname
            },
            new DeviceNicknameRule
            {
                TargetRegex = HardwareTypeGPUTarget,
                ReplacementString = GPUNickname
            }
        ];

    /// <summary>
    /// Builds default probe-name replacement rules.
    /// </summary>
    private static List<DeviceNicknameRule> BuildDefaultProbeNicknameRules() =>
        [
            new()
            {
                TargetRegex = ProbeTargetRegex_Tdie,
                ReplacementString = string.Empty
            },
            new()
            {
                TargetRegex = ProbeTargetRegex_TctlTdie,
                ReplacementString = string.Empty
            },
            new()
            {
                TargetRegex = ProbeTargetRegex_SMU,
                ReplacementString = string.Empty
            },
            new()
            {
                TargetRegex = ProbeTargetRegex_CPUCore,
                ReplacementString = "Core"
            }
        ];

    /// <summary>
    /// Replaces generated legacy default nickname rules with hardware-type defaults.
    /// </summary>
    private bool ReplacePreviousDefaultDeviceNicknameRules(List<DeviceNicknameRule> defaultRules)
    {
        List<DeviceNicknameRule> preservedRules =
        [
            .. DeviceNicknameRules.Where(static rule => !IsGeneratedLegacyDefaultDeviceNicknameRule(rule))
        ];
        if (preservedRules.Count == DeviceNicknameRules.Count) return false;

        DeviceNicknameRules = [];
        if (!HasHardwareTypeDefaultDeviceNicknameRules(preservedRules))
            DeviceNicknameRules.AddRange(defaultRules);
        DeviceNicknameRules.AddRange(preservedRules);
        return true;
    }

    /// <summary>
    /// Checks whether hardware-type defaults are already present.
    /// </summary>
    private static bool HasHardwareTypeDefaultDeviceNicknameRules(IEnumerable<DeviceNicknameRule> rules)
    {
        bool hasCPU = false;
        bool hasGPU = false;
        foreach (DeviceNicknameRule rule in rules)
        {
            hasCPU |= IsDeviceNicknameRule(rule, HardwareTypeCPUTarget, CPUNickname);
            hasGPU |= IsDeviceNicknameRule(rule, HardwareTypeGPUTarget, GPUNickname);
        }

        return hasCPU && hasGPU;
    }

    /// <summary>
    /// Checks whether a rule is a generated default from a previous seed pass.
    /// </summary>
    private static bool IsGeneratedLegacyDefaultDeviceNicknameRule(DeviceNicknameRule rule) =>
        IsDeviceNicknameRule(rule, PreviousDefaultCPUTargetRegex, CPUNickname)
        || IsDeviceNicknameRule(rule, PreviousDefaultGPUTargetRegex, GPUNickname)
        || IsGeneratedExactNameDefaultDeviceNicknameRule(rule);

    /// <summary>
    /// Checks whether a nickname rule matches a specific target and replacement.
    /// </summary>
    private static bool IsDeviceNicknameRule(DeviceNicknameRule rule, string targetRegex, string replacementString) =>
        string.Equals(rule.TargetRegex, targetRegex, StringComparison.Ordinal)
        && string.Equals(rule.ReplacementString, replacementString, StringComparison.Ordinal);

    /// <summary>
    /// Merges defaults at the top of a nickname rule list.
    /// </summary>
    private static bool MergeDefaultNicknameRules(
        List<DeviceNicknameRule> currentRules,
        List<DeviceNicknameRule> defaultRules)
    {
        bool changed = false;
        int insertIndex = 0;
        foreach (DeviceNicknameRule defaultRule in defaultRules)
        {
            int existingIndex = FindNicknameRuleIndexByTarget(currentRules, defaultRule.TargetRegex);
            if (existingIndex < 0)
            {
                currentRules.Insert(insertIndex, CloneNicknameRule(defaultRule));
                changed = true;
                insertIndex++;
                continue;
            }

            DeviceNicknameRule existingRule = currentRules[existingIndex];
            if (!string.Equals(existingRule.ReplacementString, defaultRule.ReplacementString, StringComparison.Ordinal))
            {
                existingRule.ReplacementString = defaultRule.ReplacementString;
                changed = true;
            }

            if (existingIndex != insertIndex)
            {
                currentRules.RemoveAt(existingIndex);
                currentRules.Insert(insertIndex, existingRule);
                changed = true;
            }

            insertIndex++;
        }

        return changed;
    }

    /// <summary>
    /// Finds a nickname rule by its target regex.
    /// </summary>
    private static int FindNicknameRuleIndexByTarget(List<DeviceNicknameRule> rules, string targetRegex)
    {
        for (int i = 0; i < rules.Count; i++)
            if (string.Equals(rules[i].TargetRegex, targetRegex, StringComparison.Ordinal)) return i;

        return -1;
    }

    /// <summary>
    /// Clones a nickname rule before inserting it into user settings.
    /// </summary>
    private static DeviceNicknameRule CloneNicknameRule(DeviceNicknameRule rule) =>
        new()
        {
            TargetRegex = rule.TargetRegex,
            ReplacementString = rule.ReplacementString
        };

    /// <summary>
    /// Checks whether a prior generated exact-name rule should be migrated.
    /// </summary>
    private static bool IsGeneratedExactNameDefaultDeviceNicknameRule(DeviceNicknameRule rule)
    {
        if (rule.TargetRegex.Length < 3) return false;
        if (!rule.TargetRegex.StartsWith('^')) return false;
        if (!rule.TargetRegex.EndsWith('$')) return false;
        return IsGeneratedNicknameReplacement(rule.ReplacementString, CPUNickname)
            || IsGeneratedNicknameReplacement(rule.ReplacementString, GPUNickname);
    }

    /// <summary>
    /// Checks whether a replacement is a generated base nickname or numbered suffix.
    /// </summary>
    private static bool IsGeneratedNicknameReplacement(string replacementString, string baseNickname)
    {
        if (string.Equals(replacementString, baseNickname, StringComparison.Ordinal)) return true;

        string prefix = $"{baseNickname} ";
        if (!replacementString.StartsWith(prefix, StringComparison.Ordinal)) return false;

        string suffix = replacementString[prefix.Length..];
        return int.TryParse(suffix, out int number) && number > 1;
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
            groups[fan.Group] = new FanGroup { Name = fan.Group, DisplayOrder = groups.Count };
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
            BindingID = 0
        }
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
        {
            if (d.Matches(action, parameter, bindingID))
                return true;
        }

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
                BindingID = d.BindingID
            });
            added = true;
        }

        return added;
    }
}
