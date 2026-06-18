using System.Xml.Linq;
using static TrayAppDotNETCommon.Models.TrayAppDotNETSettingsXml;

namespace NetworkTrayAppDotNET.Models;

public enum TrayIconStyle
{
    Dynamic,
    Static,
}

/// <summary>
/// Action taken when the tray icon is clicked.
/// </summary>
public enum TrayClickAction
{
    Nothing,
    OpenSettings,
    OpenAdapterSettings,
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
/// </summary>
public class AppSettings : AppSettingsCommon
{
    // Context menu
    public ContextMenuPosition ContextMenuPosition { get; set; } = ContextMenuPosition.Modern;
    public int ContextMenuFontSize { get; set; } = 15;

    // Theme
    public TrayIconStyle TrayIconStyle { get; set; } = TrayIconStyle.Dynamic;

    // Tray icon interaction. Click actions are surfaced through TrayIconPage; the host wires what each
    // action does.
    public TrayClickAction TrayDoubleClickAction { get; set; } = TrayClickAction.OpenAdapterSettings;
    public TrayClickAction TrayCtrlLeftClickAction { get; set; } = TrayClickAction.OpenAdapterSettings;
    public TrayClickAction TrayAltLeftClickAction { get; set; } = TrayClickAction.OpenSettings;
    public TrayClickAction TrayCtrlRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;

    // Network app: which UI to surface on tray left-click and which adapter window to open
    // from the context menu. AvailableNetworks is the safest default - works under any Win10/11
    // build without depending on the undocumented shell-experience COM contracts.
    public FlyoutStyle FlyoutStyle { get; set; } = FlyoutStyle.AvailableNetworks;
    public AdapterSettingsStyle AdapterSettingsStyle { get; set; } = AdapterSettingsStyle.Explorer;

    // Network app: per-state tray icon color overrides. Each falls back to the per-theme default
    // (white/black for connected, amber for no-internet, gray for disconnected) when unset.
    public NullableThemeColor NetworkConnectedColor { get; set; } = new();
    public NullableThemeColor NetworkNoInternetColor { get; set; } = new();
    public NullableThemeColor NetworkDisconnectedColor { get; set; } = new();

    public AppSettings()
        : base(updateCheckIntervalDefaultMs: 0) =>
        WireColorCallbacks();

    private static readonly AsyncThrottler<AppSettings> SaveThrottle = new(
        TimeConstants.SettingsSaveDebounceMs,
        drainPollIntervalMs: TimeConstants.DrainPollIntervalMs);

    protected override void RequestSave()
    {
        AppSettings self = this;
        _ = SaveThrottle.RunAsync(self, _ =>
        {
            self.Save();
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Bridges every NullableThemeColor override on this instance to the global Changed event,
    /// so any color edit (committed hex or live-preview Temporary*) flows out through the same
    /// notification path as every other setting change.
    /// Idempotent: Unsubscribe runs first, so re-wiring after loading replaces the ctor-wired
    /// instances without double-firing.
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
        yield return TrayIconColor;
        yield return NetworkConnectedColor;
        yield return NetworkNoInternetColor;
        yield return NetworkDisconnectedColor;
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
        string tmp = path + ".tmp";
        try
        {
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            if (directory.Length > 0) Directory.CreateDirectory(directory);

            using (FileStream stream = new(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                SaveXml(stream);

            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppSettings.Save: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
                /* best-effort */
            }
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
                AppSettings loaded = FromXml(XDocument.Load(stream));

                // One-time cleanup of duplicate hotkey rows that may have accumulated from a prior build
                // that re-seeded the default hotkey on every launch.
                // Top up any defaults missing from the persisted list (e.g. when a new build ships a new
                // default action). Skips entries the user has tombstoned via the UI (RemovedByUser=true)
                // so an explicit removal isn't undone on the next launch.
                bool changed = loaded.DedupeHotkeysByIdentity();
                changed |= loaded.EnsureDefaultHotkeys();
                if (changed) loaded.Save(path);
                return loaded;
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppSettings.LoadOrDefault: {ex.Message}");
        }

        AppSettings defaults = new();
        defaults.EnsureDefaultHotkeys();
        defaults.Save(path);
        return defaults;
    }

    private void SaveXml(Stream stream) => SaveDocument(stream, ToXml());

    private XDocument ToXml()
    {
        XElement root = new("AppSettings",
            Bool(nameof(RunOnStartup), RunOnStartup),
            Bool(nameof(Autosave), Autosave),
            Enum(nameof(ContextMenuPosition), ContextMenuPosition),
            Int(nameof(ContextMenuFontSize), ContextMenuFontSize),
            Enum(nameof(ThemeMode), ThemeMode),
            NullableThemeColorElement(nameof(TextColor), TextColor),
            NullableThemeColorElement(nameof(BackgroundColor), BackgroundColor),
            Enum(nameof(TrayIconStyle), TrayIconStyle),
            NullableThemeColorElement(nameof(TrayIconColor), TrayIconColor),
            Bool(nameof(EnableRoundedCorners), EnableRoundedCorners),
            Enum(nameof(AnimationMode), AnimationMode),
            Int(nameof(ToolTipShowDelayMs), ToolTipShowDelayMs),
            Enum(nameof(TrayDoubleClickAction), TrayDoubleClickAction),
            Enum(nameof(TrayCtrlLeftClickAction), TrayCtrlLeftClickAction),
            Enum(nameof(TrayAltLeftClickAction), TrayAltLeftClickAction),
            Enum(nameof(TrayCtrlRightClickAction), TrayCtrlRightClickAction),
            Enum(nameof(TrayAltRightClickAction), TrayAltRightClickAction),
            Enum(nameof(TrayCtrlDoubleLeftClickAction), TrayCtrlDoubleLeftClickAction),
            Enum(nameof(TrayAltDoubleLeftClickAction), TrayAltDoubleLeftClickAction),
            Enum(nameof(FlyoutStyle), FlyoutStyle),
            Enum(nameof(AdapterSettingsStyle), AdapterSettingsStyle),
            Bool(nameof(KeepTrayContextMenuWarm), KeepTrayContextMenuWarm),
            Bool(nameof(PurgeMemoryOnStartup), PurgeMemoryOnStartup),
            NullableThemeColorElement(nameof(NetworkConnectedColor), NetworkConnectedColor),
            NullableThemeColorElement(nameof(NetworkNoInternetColor), NetworkNoInternetColor),
            NullableThemeColorElement(nameof(NetworkDisconnectedColor), NetworkDisconnectedColor),
            HotkeysElement(Hotkeys));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static AppSettings FromXml(XDocument document)
    {
        XElement root = LoadRoot(
            document,
            "AppSettings",
            "Missing AppSettings root.");

        AppSettings loaded = new() { SuppressChangeNotification = true };
        try
        {
            loaded.RunOnStartup = ReadBool(root, nameof(RunOnStartup), loaded.RunOnStartup);
            loaded.Autosave = ReadBool(root, nameof(Autosave), loaded.Autosave);
            loaded.ContextMenuPosition = ReadEnum(root, nameof(ContextMenuPosition), loaded.ContextMenuPosition);
            loaded.ContextMenuFontSize = ReadInt(root, nameof(ContextMenuFontSize), loaded.ContextMenuFontSize);
            loaded.ThemeMode = ReadEnum(root, nameof(ThemeMode), loaded.ThemeMode);
            loaded.TextColor = ReadNullableThemeColor(root, nameof(TextColor), loaded.TextColor);
            loaded.BackgroundColor = ReadNullableThemeColor(root, nameof(BackgroundColor), loaded.BackgroundColor);
            loaded.TrayIconStyle = ReadEnum(root, nameof(TrayIconStyle), loaded.TrayIconStyle);
            loaded.TrayIconColor = ReadNullableThemeColor(root, nameof(TrayIconColor), loaded.TrayIconColor);
            loaded.EnableRoundedCorners = ReadBool(root, nameof(EnableRoundedCorners), loaded.EnableRoundedCorners);
            loaded.AnimationMode = ReadEnum(root, nameof(AnimationMode), loaded.AnimationMode);
            loaded.ToolTipShowDelayMs = ReadInt(root, nameof(ToolTipShowDelayMs), loaded.ToolTipShowDelayMs);
            loaded.TrayDoubleClickAction = ReadEnum(root, nameof(TrayDoubleClickAction), loaded.TrayDoubleClickAction);
            loaded.TrayCtrlLeftClickAction =
                ReadEnum(root, nameof(TrayCtrlLeftClickAction), loaded.TrayCtrlLeftClickAction);
            loaded.TrayAltLeftClickAction =
                ReadEnum(root, nameof(TrayAltLeftClickAction), loaded.TrayAltLeftClickAction);
            loaded.TrayCtrlRightClickAction =
                ReadEnum(root, nameof(TrayCtrlRightClickAction), loaded.TrayCtrlRightClickAction);
            loaded.TrayAltRightClickAction =
                ReadEnum(root, nameof(TrayAltRightClickAction), loaded.TrayAltRightClickAction);
            loaded.TrayCtrlDoubleLeftClickAction = ReadEnum(root, nameof(TrayCtrlDoubleLeftClickAction),
                loaded.TrayCtrlDoubleLeftClickAction);
            loaded.TrayAltDoubleLeftClickAction = ReadEnum(root, nameof(TrayAltDoubleLeftClickAction),
                loaded.TrayAltDoubleLeftClickAction);
            loaded.FlyoutStyle = ReadEnum(root, nameof(FlyoutStyle), loaded.FlyoutStyle);
            loaded.AdapterSettingsStyle = ReadEnum(root, nameof(AdapterSettingsStyle), loaded.AdapterSettingsStyle);
            loaded.KeepTrayContextMenuWarm =
                ReadBool(root, nameof(KeepTrayContextMenuWarm), loaded.KeepTrayContextMenuWarm);
            loaded.PurgeMemoryOnStartup = ReadBool(root, nameof(PurgeMemoryOnStartup), loaded.PurgeMemoryOnStartup);
            loaded.NetworkConnectedColor =
                ReadNullableThemeColor(root, nameof(NetworkConnectedColor), loaded.NetworkConnectedColor);
            loaded.NetworkNoInternetColor =
                ReadNullableThemeColor(root, nameof(NetworkNoInternetColor), loaded.NetworkNoInternetColor);
            loaded.NetworkDisconnectedColor = ReadNullableThemeColor(root, nameof(NetworkDisconnectedColor),
                loaded.NetworkDisconnectedColor);
            loaded.Hotkeys = ReadHotkeys(root.Element("Hotkeys"));
        }
        finally
        {
            loaded.SuppressChangeNotification = false;
        }

        loaded.WireColorCallbacks();
        return loaded;
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
