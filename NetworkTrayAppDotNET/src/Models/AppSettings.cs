using System.Xml.Serialization;
using TrayAppDotNETCommon.Serialization;

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
[XmlRoot("AppSettings")]
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

    public override void OnTrayXmlDeserialized()
    {
        WireColorCallbacks();
        base.OnTrayXmlDeserialized();
    }

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

    public void Save(string path) =>
        TrayXmlSerializer.TryWriteFile(
            path,
            this,
            ex => TADNLog.Log($"AppSettings.Save: {ex.Message}"));

    public static AppSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static AppSettings LoadOrDefault(string path)
    {
        try
        {
            if (TrayXmlSerializer.TryReadFile(
                    path,
                    out AppSettings? loaded,
                    ex => TADNLog.Log($"AppSettings.LoadOrDefault: {ex.Message}")))
            {
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
