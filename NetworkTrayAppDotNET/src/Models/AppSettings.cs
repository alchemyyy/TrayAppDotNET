using System.Xml.Serialization;

namespace NetworkTrayAppDotNET.Models;

/// <summary>
/// Action taken when the tray icon is clicked.
/// </summary>
public enum TrayClickAction
{
    Nothing,
    OpenSettings,
    OpenAdapterSettings
}

/// <summary>
/// Root application settings class.
/// </summary>
[XmlRoot("AppSettings")]
public class AppSettings : AppSettingsCommon<NetworkHotkeyBinding, NetworkHotkeyAction>
{
    // Theme
    public TrayAppDotNETTrayIconStyle TrayIconStyle
    {
        get;
        set => SetField(ref field, value);
    } = TrayAppDotNETTrayIconStyle.Dynamic;

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
    // from the context menu. The Windows 10 network flyout is the intended primary experience;
    // App falls back to the available-networks URI if its shell COM contract is unavailable.
    public const FlyoutStyle DefaultFlyoutStyle = FlyoutStyle.Windows10;
    public FlyoutStyle FlyoutStyle { get; set; } = DefaultFlyoutStyle;
    public AdapterSettingsStyle AdapterSettingsStyle { get; set; } = AdapterSettingsStyle.Explorer;

    // Network app: per-state tray icon color overrides. Each falls back to the per-theme default
    // (white/black for connected, amber for no-internet, gray for disconnected) when unset.
    public NullableThemeColor NetworkConnectedColor { get; set; } = new();
    public NullableThemeColor NetworkNoInternetColor { get; set; } = new();
    public NullableThemeColor NetworkDisconnectedColor { get; set; } = new();

    // One-time migration marker: true once the retired Ctrl+Alt+Win+S Open settings default has been
    // removed (or on fresh installs, which never had it). Keeps a binding the user re-adds later.
    public bool LegacyOpenSettingsHotkeyRetired { get; set; }

    public AppSettings()
        : base(TrayAppDotNETRenderingBackend.Software)
    {
    }

    protected override IEnumerable<NullableThemeColor> EnumerateColorOverrides()
    {
        foreach (NullableThemeColor color in base.EnumerateColorOverrides()) yield return color;
        yield return NetworkConnectedColor;
        yield return NetworkNoInternetColor;
        yield return NetworkDisconnectedColor;
    }

    protected override void InitializeNewSettings() => LegacyOpenSettingsHotkeyRetired = true;

    /// <summary>
    /// Retires the Open settings binding on Ctrl+Alt+Win+S that older builds seeded (it collided with Fan
    /// Control's identical default), once, then runs the shared hotkey cleanup.
    /// </summary>
    protected override bool NormalizeLoadedSettings()
    {
        bool changed = false;
        if (!LegacyOpenSettingsHotkeyRetired)
        {
            HotkeyDefaults.RetireLegacyBinding(
                Hotkeys,
                NetworkHotkeyAction.OpenSettings,
                HotkeyModifiers.Control | HotkeyModifiers.Win | HotkeyModifiers.Alt,
                virtualKey: 0x53);
            LegacyOpenSettingsHotkeyRetired = true;
            changed = true;
        }

        return base.NormalizeLoadedSettings() | changed;
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
}
