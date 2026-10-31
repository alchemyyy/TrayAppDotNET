using System.Xml.Serialization;

namespace FanControlTrayAppDotNET.Models;

/// <summary>
/// Action taken when the tray icon is clicked or scrolled.
/// </summary>
public enum TrayClickAction
{
    Nothing,
    OpenSettings
}

public enum MultipleSliderValuesDisplayMode
{
    Disabled,
    Enabled,
    OnlyInManual
}

/// <summary>
/// Root application settings class.
/// </summary>
[XmlRoot("AppSettings")]
public class AppSettings : AppSettingsCommon<HotkeyBinding, HotkeyAction>, IFlyoutDockSettings
{
    private const string CPUNickname = "CPU";
    private const string GPUNickname = "GPU";
    private const string HardwareTypeCPUTarget = "{HardwareType.CPU}";
    private const string HardwareTypeGPUTarget = "{HardwareType.GPU}";

    private const string PreviousDefaultCPUTargetRegex =
        ".*(CPU|Processor|Ryzen|Threadripper|Intel.*Core|Core.*Processor).*";

    private const string PreviousDefaultGPUTargetRegex = ".*(GPU|Graphics|NVIDIA|GeForce|Radeon|Arc).*";
    private const string ProbeTargetRegex_Tdie = "\\(Tdie\\)";
    private const string ProbeTargetRegex_TctlTdie = "\\(Tctl/Tdie\\)";
    private const string ProbeTargetRegex_SMU = "\\(SMU\\)";
    private const string ProbeTargetRegex_CPUCore = "CPU Core";

    /// <summary>
    /// The shared implicit-save switch (see <see cref="AppSettingsCommon.Autosave"/>). Stored as
    /// <c>AutosaveSettings</c> because Fan's older files use <c>Autosave</c> for
    /// <see cref="CaptureControlStateOnProfileSwitch"/>.
    /// </summary>
    [XmlElement("AutosaveSettings")]
    public override bool Autosave
    {
        get => base.Autosave;
        set => base.Autosave = value;
    }

    /// <summary>
    /// When switching fan profiles, also capture the outgoing profile's live control state (manual duty, curve
    /// assignment), not only its layout. Stored as <c>Autosave</c> for compatibility with older files.
    /// </summary>
    [XmlElement("Autosave")]
    public bool CaptureControlStateOnProfileSwitch { get; set; } = true;

    // Fan-control general toggles. DefaultToRPMMode flips new Fan instances to RPMMode at
    // discovery time; existing fans keep whatever the user last picked.
    public bool DefaultToRPMMode { get; set; }

    // Fan properties. Applied to new Fans at discovery time as their initial values; existing
    // fans keep their per-fan overrides. Property-unit toggles default to duty cycle.
    public int DefaultJumpstartDutyCycle { get; set; } = 50;

    public int DefaultDeltaMaxDutyCycle { get; set; } = 100;

    // Reference into Curve.Curves by name. "None" is the sentinel for no curve assignment.
    public string DefaultAssignedCurve { get; set; } = "None";

    // Theme
    public NullableThemeColor FlyoutBackgroundColor { get; set; } = new();
    public NullableThemeColor FlyoutTitleBarBackgroundColor { get; set; } = new();
    public NullableThemeColor FanCardBackgroundColor { get; set; } = new();
    public NullableThemeColor GroupCardBackgroundColor { get; set; } = new();
    public NullableThemeColor CardBorderColor { get; set; } = new();
    public TrayAppDotNETTrayIconStyle TrayIconStyle { get; set; } = TrayAppDotNETTrayIconStyle.Dynamic;
    public bool SquareFlyoutTitleBarCorners { get; set; }
    public bool EnableCardBorders { get; set; }
    public bool EnableHoveredCardBorders { get; set; }
    public bool HideGroupedFanCardBorders { get; set; } = true;
    public bool UseGroupBackgroundForGroupedFanCards { get; set; }
    public int FlyoutCardSpacing { get; set; } = 1;
    public int FlyoutCardHorizontalInset { get; set; } = 1;
    public int FlyoutTitleBarCardSpacing { get; set; } = 2;

    // Tray icon click actions, surfaced through the Tray icon settings page. Fan has no tray wheel action; an old
    // TrayScrollEnabled element in settings.xml is skipped on load.
    public TrayClickAction TrayDoubleClickAction { get; set; } = TrayClickAction.OpenSettings;
    public TrayClickAction TrayCtrlLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;

    // Flyout undocking: whether the flyout shows its undock button, keeps an undocked flyout on screen, and
    // reopens undocked at startup (Settings, Flyout page)
    public bool AllowFlyoutUndock { get; set; } = true;

    public bool ClampUndockedFlyoutToScreen { get; set; } = true;

    public bool RestoreFlyoutUndockedOnStartup { get; set; } = true;

    // Runtime dock state: persisted without raising Changed, so a drag never re-applies the app or rebuilds the
    // flyout. FlyoutHasSavedPosition gates whether FlyoutLeft / FlyoutTop are restored.
    public bool FlyoutUndocked
    {
        get;
        set => SetState(ref field, value);
    }

    public bool FlyoutHasSavedPosition
    {
        get;
        set => SetState(ref field, value);
    }

    public double FlyoutLeft
    {
        get;
        set => SetState(ref field, value);
    }

    public double FlyoutTop
    {
        get;
        set => SetState(ref field, value);
    }

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
    [XmlIgnore]
    public string SliderThumbGlyph { get; set; } = "Capsule";

    [XmlIgnore]
    public string CurveSliderThumbGlyph { get; set; } = "Diamond";

    [XmlIgnore]
    public List<SliderThumbGlyphOption> SliderThumbOptions { get; set; } = [];

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

    // Set once the retired Open settings binding has been removed (or on fresh installs, which never had the
    // retired binding) so a binding the user re-adds later is never removed again.
    public bool LegacyOpenSettingsHotkeyRetired { get; set; }

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

    public bool FanProfileLayoutsInitialized { get; set; }

    public AppSettings() => InitializeSliderThumbCatalog();

    /// <summary>Copies the live fan registries and the active profile's layout into the persisted lists.</summary>
    public override void OnTrayXmlSerializing()
    {
        EnsureFanProfileLayouts();
        SyncFanControlRegistriesForSave();
        FanProfiles[SelectedFanProfileIndex].Capture(Fans, FanGroups, includeControlState: false);
        base.OnTrayXmlSerializing();
    }

    public override void OnTrayXmlDeserialized()
    {
        InitializeSliderThumbCatalog();
        InitializeFanControlRegistries();
        base.OnTrayXmlDeserialized();
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

    protected override IEnumerable<NullableThemeColor> EnumerateColorOverrides()
    {
        foreach (NullableThemeColor color in base.EnumerateColorOverrides()) yield return color;
        yield return FlyoutBackgroundColor;
        yield return FlyoutTitleBarBackgroundColor;
        yield return FanCardBackgroundColor;
        yield return GroupCardBackgroundColor;
        yield return CardBorderColor;
    }

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, path2: "settings.xml");
    }

    // The folder that holds settings.xml and other per-app data.
    // Used by the uninstaller's "delete settings" branch.
    public static string GetDefaultDirectory() =>
        Program.AppLocalAppDataDirectory;

    public static AppSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static AppSettings LoadOrDefault(string path) => LoadOrCreate<AppSettings>(path);

    protected override void InitializeNewSettings()
    {
        LegacyOpenSettingsHotkeyRetired = true;
        InitializeFanControlRegistries();
    }

    /// <summary>
    /// Retires the Ctrl+Alt+Win+S Open settings binding older builds seeded (it collided with Network's identical
    /// default) once, runs the shared hotkey cleanup, then repairs profile layouts and seeds nickname rules.
    /// </summary>
    protected override bool NormalizeLoadedSettings()
    {
        bool changed = false;
        if (!LegacyOpenSettingsHotkeyRetired)
        {
            HotkeyDefaults.RetireLegacyBinding(
                Hotkeys,
                HotkeyAction.OpenSettings,
                HotkeyModifiers.Control | HotkeyModifiers.Win | HotkeyModifiers.Alt,
                virtualKey: 0x53);
            LegacyOpenSettingsHotkeyRetired = true;
            changed = true;
        }

        changed |= base.NormalizeLoadedSettings();
        changed |= EnsureFanProfileLayouts();
        changed |= EnsureDefaultDeviceNicknameRules();
        changed |= EnsureDefaultProbeNicknameRules();
        return changed;
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
        new() { TargetRegex = HardwareTypeCPUTarget, ReplacementString = CPUNickname },
        new() { TargetRegex = HardwareTypeGPUTarget, ReplacementString = GPUNickname }
    ];

    /// <summary>
    /// Builds default probe-name replacement rules.
    /// </summary>
    private static List<DeviceNicknameRule> BuildDefaultProbeNicknameRules() =>
    [
        new() { TargetRegex = ProbeTargetRegex_Tdie, ReplacementString = string.Empty },
        new() { TargetRegex = ProbeTargetRegex_TctlTdie, ReplacementString = string.Empty },
        new() { TargetRegex = ProbeTargetRegex_SMU, ReplacementString = string.Empty },
        new() { TargetRegex = ProbeTargetRegex_CPUCore, ReplacementString = "Core" }
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
        {
            if (string.Equals(rules[i].TargetRegex, targetRegex, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Clones a nickname rule before inserting it into user settings.
    /// </summary>
    private static DeviceNicknameRule CloneNicknameRule(DeviceNicknameRule rule) =>
        new() { TargetRegex = rule.TargetRegex, ReplacementString = rule.ReplacementString };

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

    /// <summary>Migrates previously global layout into only the active profile and repairs card visibility.</summary>
    public bool EnsureFanProfileLayouts()
    {
        bool changed = EnsureFanProfileCount(FanProfile.SlotCount);
        int selectedIndex = SelectedFanProfileIndex >= 0 && SelectedFanProfileIndex < FanProfile.SlotCount
            ? SelectedFanProfileIndex
            : 0;
        if (SelectedFanProfileIndex != selectedIndex)
        {
            SelectedFanProfileIndex = selectedIndex;
            changed = true;
        }

        if (!FanProfileLayoutsInitialized)
        {
            FanProfiles[selectedIndex].Capture(Fans, FanGroups, includeControlState: false);
            FanProfileLayoutsInitialized = true;
            changed = true;
        }

        foreach (ProbeCard probeCard in ProbeCards)
            changed |= probeCard.EnsureProfileVisibility(selectedIndex);
        return changed;
    }

    /// <summary>Switches fan layout and control snapshots without borrowing names or groups from another slot.</summary>
    public bool SelectFanProfile(int targetIndex, IEnumerable<Fan> liveFans)
    {
        if (targetIndex is < 0 or >= FanProfile.SlotCount) return false;
        EnsureFanProfileLayouts();
        if (targetIndex == SelectedFanProfileIndex) return false;

        // Include disconnected fans so their names and membership also follow profile selection
        Dictionary<string, Fan> knownFans = new(StringComparer.OrdinalIgnoreCase);
        foreach (Fan fan in Fans)
        {
            if (!string.IsNullOrWhiteSpace(fan.DataSourceKey)) knownFans[fan.DataSourceKey] = fan;
        }
        foreach (Fan fan in liveFans)
        {
            if (!string.IsNullOrWhiteSpace(fan.DataSourceKey)) knownFans[fan.DataSourceKey] = fan;
        }

        FanProfiles[SelectedFanProfileIndex].Capture(knownFans.Values, FanGroups, CaptureControlStateOnProfileSwitch);
        FanProfile target = FanProfiles[targetIndex];
        target.ApplyTo(knownFans.Values);
        SelectedFanProfileIndex = targetIndex;

        Fans = [];
        foreach (Fan fan in knownFans.Values)
            Fans.Add(fan.CloneForPersistence());

        FanGroups = [];
        foreach (FanProfileGroup group in target.Groups)
            FanGroups.Add(group.ToGroup());
        FanGroup.FanGroups.Clear();
        SyncFanGroupsFromFans();
        foreach (FanGroup group in FanGroups)
            FanGroup.Register(group);

        // Seed unseen fan controls only after clearing the previous profile's layout
        target.Capture(knownFans.Values, FanGroups, includeControlState: false);
        return true;
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
}
