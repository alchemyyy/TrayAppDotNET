using Avalonia.Media;
using TrayAppDotNETCommon.Visuals;
using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;

namespace VolumeTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons glyphs shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    private const double MicrophoneGlyphScale = 20.5 / 26.0;
    private const double DisabledPlaybackDeviceGlyphScale = 34.0 / 18.0;
    private const double DeviceStateCircleGlyphOffsetY = -1.0;
    private const double ExpandedDrawerChevronGlyphOffsetY = 1.0;
    private const double CollapsedDrawerChevronGlyphOffsetY = -1.0;

    // ===========================================================================
    // Generic UI glyphs
    // ===========================================================================

    public new const string SEGOE_FLUENT_ICONS = CommonGlyphCatalog.SEGOE_FLUENT_ICONS;
    public new const string SEGOE_MDL2_ASSETS = CommonGlyphCatalog.SEGOE_MDL2_ASSETS;

    public new static readonly Glyph SETTINGS = Glyph.Fluent(CommonGlyphCatalog.SETTINGS);
    public new static readonly Glyph EXIT = Glyph.Fluent(CommonGlyphCatalog.EXIT);
    public new static readonly Glyph WARNING = Glyph.Fluent(CommonGlyphCatalog.WARNING);

    // Window-chrome / spinner / combobox chevrons.
    public new static readonly Glyph CHEVRON_UP_BIG = Glyph.Fluent(CommonGlyphCatalog.CHEVRON_UP_BIG.Text,
        translateY: ExpandedDrawerChevronGlyphOffsetY);
    public new static readonly Glyph CHEVRON_DOWN_BIG = Glyph.Fluent(CommonGlyphCatalog.CHEVRON_DOWN_BIG.Text,
        translateY: CollapsedDrawerChevronGlyphOffsetY);

    public new static readonly Glyph UNDOCK = Glyph.Fluent(CommonGlyphCatalog.UNDOCK);
    public new static readonly Glyph REDOCK = Glyph.Fluent(CommonGlyphCatalog.REDOCK);

    public static readonly Glyph COMMUNICATIONS_ACTIVITY = Glyph.Fluent("\uE77E"); // Fluent, IncomingCall

    // ===========================================================================
    // Volume tier glyphs (speaker icons; tier selection lives in GetVolumeTier)
    // ===========================================================================

    public static readonly Glyph PLAYBACK_VOLUME_MUTE = Glyph.Fluent("\uE74F"); // Fluent, Mute
    public static readonly Glyph PLAYBACK_VOLUME_SILENT = Glyph.Fluent("\uE992"); // Fluent, Volume0
    public static readonly Glyph PLAYBACK_VOLUME_LOW = Glyph.Fluent("\uE993"); // Fluent, Volume1
    public static readonly Glyph PLAYBACK_VOLUME_MID = Glyph.Fluent("\uE994"); // Fluent, Volume2

    public static readonly Glyph PLAYBACK_VOLUME_HIGH = Glyph.Fluent("\uE995"); // Fluent, Volume3

    // Semantic alias for the titlebar sound-settings entrypoint glyph. Same codepoint as
    // PLAYBACK_VOLUME_HIGH (Volume3) - declared separately so the call site reads as intent
    // ("open Sound settings"), not as a volume tier glyph.
    public static readonly Glyph SOUND_SETTINGS = Glyph.Fluent("\uE995"); // Fluent, Volume3

    public static readonly Glyph MICROPHONE = Microphone("\uE720"); // Fluent, Microphone
    public static readonly Glyph MICROPHONE_OFF = Microphone("\uF781"); // Fluent, MicOff2
    public static readonly Glyph MICROPHONE_SLEEP = Microphone("\uEC55"); // Fluent, MicSleep
    public static readonly Glyph MICROPHONE_LISTENING = Microphone("\uF12E"); // Fluent, MicrophoneListening
    public static readonly Glyph EAR_LISTEN = Glyph.Fluent("\uF270"); // Fluent, Ear

    // ===========================================================================
    // Device-row control button glyphs (exclusive mode, sound settings, equalizer APO)
    // ===========================================================================

    // Exclusive mode. The "allow applications to take exclusive control" checkbox state and
    // the "is an app currently holding exclusive control" state are projected onto the same
    // button - UNLOCK reads "free", LOCK reads "held".
    public static readonly Glyph LOCK = Glyph.Fluent("\uE72E"); // Fluent, Lock
    public static readonly Glyph UNLOCK = Glyph.Fluent("\uE785"); // Fluent, Unlock

    // Equalizer APO availability. EQUALIZER is the main button glyph; SIGNAL_NOT_CONNECTED is
    // overlaid via the mini-glyph slot when the APO binary can't be found on the system.
    public static readonly Glyph EQUALIZER = Glyph.Fluent("\uE9E9"); // Fluent, Equalizer
    public static readonly Glyph SIGNAL_NOT_CONNECTED = Glyph.Fluent("\uE871"); // Fluent, SignalNotConnected

    // Single source of truth for volume-tier glyph selection. Shared by the tray-icon renderer
    // and the device-row VolumeGlyphConverter so the bands stay in lockstep. Bands chosen so a
    // slight nudge off zero already swaps to "low" - matches Win11 system tray behavior.
    public static Glyph GetVolumeTier(float scalar, bool muted)
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

    public static readonly Glyph PLAYBACK_DEVICE_DEFAULT = Glyph.Fluent("\uF137",
        translateY: DeviceStateCircleGlyphOffsetY); // Fluent, StatusCircleInner
    public static readonly Glyph PLAYBACK_DEVICE_ENABLED = Glyph.Fluent("\uF138",
        translateY: DeviceStateCircleGlyphOffsetY); // Fluent, StatusCircleRing
    public static readonly Glyph PLAYBACK_DEVICE_DISABLED = Glyph.Fluent("\uF13D",
        scaleX: DisabledPlaybackDeviceGlyphScale,
        scaleY: DisabledPlaybackDeviceGlyphScale,
        translateX: -1.5,
        translateY: -0.5); // Fluent, StatusCircleErrorX
    public static readonly Glyph PLAYBACK_DEVICE_DEFAULT_COMMS = Glyph.Fluent("\uE95B"); // Fluent, Headset

    // Per-app-icon overlays. APP_MUTE_OVERLAY is the small X stamped on a muted app's icon -
    // matches what the flyout actually renders (BlockedSite, not Mute).
    // APP_FALLBACK is shown when AppIconResolver couldn't extract
    // a real icon for an app session.
    public static readonly Glyph APP_MUTE_OVERLAY = Glyph.Fluent("\uE653"); // BlockedSite / mute X overlay
    public static readonly Glyph APP_FALLBACK = Glyph.Fluent("\uE978"); // Fluent, PresenceChicklet

    public static readonly Glyph BT_BATTERY_0 = Glyph.Fluent("\uF5F2"); // Fluent, VerticalBattery0
    public static readonly Glyph BT_BATTERY_1 = Glyph.Fluent("\uF5F3"); // Fluent, VerticalBattery1
    public static readonly Glyph BT_BATTERY_2 = Glyph.Fluent("\uF5F4"); // Fluent, VerticalBattery2
    public static readonly Glyph BT_BATTERY_3 = Glyph.Fluent("\uF5F5"); // Fluent, VerticalBattery3
    public static readonly Glyph BT_BATTERY_4 = Glyph.Fluent("\uF5F6"); // Fluent, VerticalBattery4
    public static readonly Glyph BT_BATTERY_5 = Glyph.Fluent("\uF5F7"); // Fluent, VerticalBattery5
    public static readonly Glyph BT_BATTERY_6 = Glyph.Fluent("\uF5F8"); // Fluent, VerticalBattery6
    public static readonly Glyph BT_BATTERY_7 = Glyph.Fluent("\uF5F9"); // Fluent, VerticalBattery7
    public static readonly Glyph BT_BATTERY_8 = Glyph.Fluent("\uF5FA"); // Fluent, VerticalBattery8
    public static readonly Glyph BT_BATTERY_9 = Glyph.Fluent("\uF5FB"); // Fluent, VerticalBattery9
    public static readonly Glyph BT_BATTERY_10 = Glyph.Fluent("\uF5FC"); // Fluent, VerticalBattery10

    // ===========================================================================
    // Window-chrome caption glyphs
    // ===========================================================================

    public new static readonly Glyph CHROME_MINIMIZE = Glyph.Fluent(CommonGlyphCatalog.CHROME_MINIMIZE);
    public new static readonly Glyph CHROME_MAXIMIZE = Glyph.Fluent(CommonGlyphCatalog.CHROME_MAXIMIZE);
    public new static readonly Glyph CHROME_RESTORE = Glyph.Fluent(CommonGlyphCatalog.CHROME_RESTORE);

    // ===========================================================================
    // Decorative shapes (slider-thumb default options)
    // ===========================================================================

    public static readonly Glyph CIRCLE = Glyph.Fluent(CommonGlyphCatalog.SLIDER_THUMB_CIRCLE);
    public static readonly Glyph DIAMOND = Glyph.Fluent(CommonGlyphCatalog.SLIDER_THUMB_DIAMOND);
    public static readonly Glyph STAR = Glyph.Fluent(CommonGlyphCatalog.SLIDER_THUMB_STAR);
    public static readonly Glyph SQUARE = Glyph.Fluent(CommonGlyphCatalog.SLIDER_THUMB_SQUARE);
    public static readonly Glyph HEART = Glyph.Fluent(CommonGlyphCatalog.SLIDER_THUMB_HEART);

    private static Glyph Microphone(string text) => Glyph.Fluent(text,
        fontWeight: FontWeight.ExtraBold,
        scaleX: MicrophoneGlyphScale,
        scaleY: MicrophoneGlyphScale,
        translateX: -1.0);
}
