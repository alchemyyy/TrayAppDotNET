using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace VolumeTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    // ===========================================================================
    // Generic UI glyphs
    // ===========================================================================

    public new const string SEGOE_FLUENT_ICONS = CommonGlyphCatalog.SEGOE_FLUENT_ICONS;
    public new const string SEGOE_MDL2_ASSETS = CommonGlyphCatalog.SEGOE_MDL2_ASSETS;

    public new const string SETTINGS = CommonGlyphCatalog.SETTINGS;
    public new const string EXIT = CommonGlyphCatalog.EXIT;
    public new const string WARNING = CommonGlyphCatalog.WARNING;

    // Window-chrome / spinner / combobox chevrons.
    public new const string CHEVRON_UP_BIG = CommonGlyphCatalog.CHEVRON_UP_BIG;
    public new const string CHEVRON_DOWN_BIG = CommonGlyphCatalog.CHEVRON_DOWN_BIG;

    public new const string UNDOCK = CommonGlyphCatalog.UNDOCK;
    public new const string REDOCK = CommonGlyphCatalog.REDOCK;



    public const string COMMUNICATIONS_ACTIVITY = "\uE77E"; // Fluent, IncomingCall

    // ===========================================================================
    // Volume tier glyphs (speaker icons; tier selection lives in GetVolumeTier)
    // ===========================================================================

    public const string PLAYBACK_VOLUME_MUTE = "\uE74F"; // Fluent, Mute
    public const string PLAYBACK_VOLUME_SILENT = "\uE992"; // Fluent, Volume0
    public const string PLAYBACK_VOLUME_LOW = "\uE993"; // Fluent, Volume1
    public const string PLAYBACK_VOLUME_MID = "\uE994"; // Fluent, Volume2

    public const string PLAYBACK_VOLUME_HIGH = "\uE995"; // Fluent, Volume3

    // Semantic alias for the titlebar sound-settings entrypoint glyph. Same codepoint as
    // PLAYBACK_VOLUME_HIGH (Volume3) - declared separately so the call site reads as intent
    // ("open Sound settings"), not as a volume tier glyph.
    public const string SOUND_SETTINGS = "\uE995"; // Fluent, Volume3

    public const string MICROPHONE = "\uE720"; // Fluent, Microphone
    public const string MICROPHONE_OFF = "\uF781"; // Fluent, MicOff2
    public const string MICROPHONE_SLEEP = "\uEC55"; // Fluent, MicSleep
    public const string MICROPHONE_LISTENING = "\uF12E"; // Fluent, MicrophoneListening
    public const string EAR_LISTEN = "\uF270"; // Fluent, Ear

    // ===========================================================================
    // Device-row control button glyphs (exclusive mode, sound settings, equalizer APO)
    // ===========================================================================

    // Exclusive mode. The "allow applications to take exclusive control" checkbox state and
    // the "is an app currently holding exclusive control" state are projected onto the same
    // button - UNLOCK reads "free", LOCK reads "held".
    public const string LOCK = "\uE72E"; // Fluent, Lock
    public const string UNLOCK = "\uE785"; // Fluent, Unlock

    // Equalizer APO availability. EQUALIZER is the main button glyph; SIGNAL_NOT_CONNECTED is
    // overlaid via the mini-glyph slot when the APO binary can't be found on the system.
    public const string EQUALIZER = "\uE9E9"; // Fluent, Equalizer
    public const string SIGNAL_NOT_CONNECTED = "\uE871"; // Fluent, SignalNotConnected

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

    public const string PLAYBACK_DEVICE_DEFAULT = "\uF137"; // Fluent, StatusCircleInner
    public const string PLAYBACK_DEVICE_ENABLED = "\uF138"; // Fluent, StatusCircleRing
    public const string PLAYBACK_DEVICE_DISABLED = "\uF13D"; // Fluent, StatusCircleErrorX
    public const string PLAYBACK_DEVICE_DEFAULT_COMMS = "\uE95B"; // Fluent, Headset

    // Per-app-icon overlays. APP_MUTE_OVERLAY is the small X stamped on a muted app's icon -
    // matches what the flyout actually renders (BlockedSite, not Mute).
    // APP_FALLBACK is shown when AppIconResolver couldn't extract
    // a real icon for an app session.
    public const string APP_MUTE_OVERLAY = "\uE653"; // BlockedSite / mute X overlay
    public const string APP_FALLBACK = "\uE978"; // Fluent, PresenceChicklet

    public const string BT_BATTERY_0 = "\uF5F2"; // Fluent, VerticalBattery0
    public const string BT_BATTERY_1 = "\uF5F3"; // Fluent, VerticalBattery1
    public const string BT_BATTERY_2 = "\uF5F4"; // Fluent, VerticalBattery2
    public const string BT_BATTERY_3 = "\uF5F5"; // Fluent, VerticalBattery3
    public const string BT_BATTERY_4 = "\uF5F6"; // Fluent, VerticalBattery4
    public const string BT_BATTERY_5 = "\uF5F7"; // Fluent, VerticalBattery5
    public const string BT_BATTERY_6 = "\uF5F8"; // Fluent, VerticalBattery6
    public const string BT_BATTERY_7 = "\uF5F9"; // Fluent, VerticalBattery7
    public const string BT_BATTERY_8 = "\uF5FA"; // Fluent, VerticalBattery8
    public const string BT_BATTERY_9 = "\uF5FB"; // Fluent, VerticalBattery9
    public const string BT_BATTERY_10 = "\uF5FC"; // Fluent, VerticalBattery10

    // ===========================================================================
    // Window-chrome caption glyphs
    // ===========================================================================

    public new const string CHROME_MINIMIZE = CommonGlyphCatalog.CHROME_MINIMIZE;
    public new const string CHROME_MAXIMIZE = CommonGlyphCatalog.CHROME_MAXIMIZE;
    public new const string CHROME_RESTORE = CommonGlyphCatalog.CHROME_RESTORE;

    // ===========================================================================
    // Decorative shapes (slider-thumb default options)
    // ===========================================================================

    public const string CIRCLE = CommonGlyphCatalog.SLIDER_THUMB_CIRCLE;
    public const string DIAMOND = CommonGlyphCatalog.SLIDER_THUMB_DIAMOND;
    public const string STAR = CommonGlyphCatalog.SLIDER_THUMB_STAR;
    public const string SQUARE = CommonGlyphCatalog.SLIDER_THUMB_SQUARE;
    public const string HEART = CommonGlyphCatalog.SLIDER_THUMB_HEART;
}
