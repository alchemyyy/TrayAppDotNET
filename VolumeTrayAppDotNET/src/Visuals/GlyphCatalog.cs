namespace VolumeTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : TrayAppDotNETCommon.Theming.GlyphCatalog
{
    // ===========================================================================
    // Generic UI glyphs
    // ===========================================================================

    public new const string SETTINGS = "\uE713"; // Setting (gear)
    public new const string EXIT = "\uE8BB"; // ChromeClose
    public new const string WARNING = "\uE7BA"; // Warning (used by hotkey-conflict status badge)

    // Window-chrome / spinner / combobox chevrons.
    public new const string CHEVRON_UP = "\uE96D"; // ChevronUp (spinner up)
    public new const string CHEVRON_DOWN = "\uE96E"; // ChevronDown (combo dropdown arrow, spinner down)

    public const string COMMUNICATIONS_ACTIVITY = "\uE77E"; // Incoming Call

    // Flyout dock / undock toggle. The semantic alias reads as the action a click performs,
    // since the button glyph flips with the IsUndocked state.
    public new const string FLYOUT_UNDOCK_ACTION = "\uE75B"; // Dock (shown docked - click undocks)
    public new const string FLYOUT_REDOCK_ACTION = "\uE75A"; // Undock (shown undocked - click redocks)

    // ===========================================================================
    // Volume tier glyphs (speaker icons; tier selection lives in GetVolumeTier)
    // ===========================================================================

    public const string PLAYBACK_VOLUME_MUTE = "\uE74F"; // Mute
    public const string PLAYBACK_VOLUME_SILENT = "\uE992"; // Volume0 (silent, no waves)
    public const string PLAYBACK_VOLUME_LOW = "\uE993"; // Volume1
    public const string PLAYBACK_VOLUME_MID = "\uE994"; // Volume2

    public const string PLAYBACK_VOLUME_HIGH = "\uE995"; // Volume3

    // Semantic alias for the titlebar sound-settings entrypoint glyph. Same codepoint as
    // PLAYBACK_VOLUME_HIGH (Volume3) - declared separately so the call site reads as intent
    // ("open Sound settings"), not as a volume tier glyph.
    public const string SOUND_SETTINGS = "\uE995"; // Volume3 (reused for Sound settings entry)

    public const string MICROPHONE = "\uE720"; // Mic
    public const string MICROPHONE_OFF = "\uF781"; // Mic Off 2
    public const string MICROPHONE_SLEEP = "\uEC55"; // Mic Sleep
    public const string MICROPHONE_LISTENING = "\uF12E"; // Mic Listening
    public const string EAR_LISTEN = "\uF270"; // Ear (glyph for capture-device 'Listen to this device' toggle)

    // ===========================================================================
    // Device-row control button glyphs (exclusive mode, sound settings, equalizer APO)
    // ===========================================================================

    // Exclusive mode. The "allow applications to take exclusive control" checkbox state and
    // the "is an app currently holding exclusive control" state are projected onto the same
    // button - UNLOCK reads "free", LOCK reads "held".
    public const string LOCK = "\uE72E"; // Lock
    public const string UNLOCK = "\uE785"; // Unlock

    // Equalizer APO availability. EQUALIZER is the main button glyph; SIGNAL_NOT_CONNECTED is
    // overlaid via the mini-glyph slot when the APO binary can't be found on the system.
    public const string EQUALIZER = "\uE9E9"; // Equalizer
    public const string SIGNAL_NOT_CONNECTED = "\uE871"; // Signal Not Connected

    // Single source of truth for volume-tier glyph selection. Shared by the tray-icon renderer
    // and the device-row VolumeGlyphConverter so the bands stay in lockstep. Bands chosen so a
    // slight nudge off zero already swaps to "low" - matches Win11 system tray behavior.
    public static string GetVolumeTier(float scalar, bool muted)
    {
        if (muted) return PLAYBACK_VOLUME_MUTE;
        return scalar switch
        {
            <= 0.001f => PLAYBACK_VOLUME_SILENT,
            < 0.34f => PLAYBACK_VOLUME_LOW,
            < 0.67f => PLAYBACK_VOLUME_MID,
            _ => PLAYBACK_VOLUME_HIGH
        };
    }

    // ===========================================================================
    // Device-icon button states (flyout footer + tray menu device entries)
    // ===========================================================================

    public const string PLAYBACK_DEVICE_DEFAULT = "\uF137"; // Status Circle Inner (filled)
    public const string PLAYBACK_DEVICE_ENABLED = "\uF138"; // Status Circle Ring
    public const string PLAYBACK_DEVICE_DISABLED = "\uF13D"; // Status Circle Error X
    public const string PLAYBACK_DEVICE_DEFAULT_COMMS = "\uE95B"; // Headset

    // Per-app-icon overlays. APP_MUTE_OVERLAY is the small X stamped on a muted app's icon -
    // matches what the flyout actually renders (BlockedSite, not Mute).
    // APP_FALLBACK is shown when AppIconResolver couldn't extract
    // a real icon for an app session.
    public const string APP_MUTE_OVERLAY = "\uE653"; // BlockedSite / mute X overlay
    public const string APP_FALLBACK = "\uE978"; // Presence Chicklet

    public const string BATTERY_0 = "\uF5F2"; // Vertical Battery 0
    public const string BATTERY_1 = "\uF5F3"; // Vertical Battery 1
    public const string BATTERY_2 = "\uF5F4"; // Vertical Battery 2
    public const string BATTERY_3 = "\uF5F5"; // Vertical Battery 3
    public const string BATTERY_4 = "\uF5F6"; // Vertical Battery 4
    public const string BATTERY_5 = "\uF5F7"; // Vertical Battery 5
    public const string BATTERY_6 = "\uF5F8"; // Vertical Battery 6
    public const string BATTERY_7 = "\uF5F9"; // Vertical Battery 7
    public const string BATTERY_8 = "\uF5FA"; // Vertical Battery 8
    public const string BATTERY_9 = "\uF5FB"; // Vertical Battery 9
    public const string BATTERY_10 = "\uF5FC"; // Vertical Battery 10

    // ===========================================================================
    // Window-chrome caption glyphs
    // ===========================================================================

    public new const string CHROME_MINIMIZE = "\uE921"; // ChromeMinimize
    public new const string CHROME_MAXIMIZE = "\uE922"; // ChromeMaximize
    public new const string CHROME_RESTORE = "\uE923"; // ChromeRestore

    // ===========================================================================
    // Decorative shapes (slider-thumb default options)
    // ===========================================================================

    public const string CIRCLE = "\uE91F"; // CircleFill
    public const string DIAMOND = "\uEA3B"; // DiamondSolid
    public const string STAR = "\uE734"; // FavoriteStarFill
    public const string SQUARE = "\uE73B"; // CheckboxFill
    public const string HEART = "\uEB51"; // HeartFill
}
