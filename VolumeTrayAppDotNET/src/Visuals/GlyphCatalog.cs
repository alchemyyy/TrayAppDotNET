using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;
using TrayAppDotNETCommon.Visuals;

namespace VolumeTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons glyphs shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
#if DEBUG
    private static readonly GlyphCatalogHotReloadStore<GlyphCatalogResources> Resources =
        GlyphCatalogHotReloadStore<GlyphCatalogResources>.Create(
            "Volume",
            static () => new GlyphCatalogResources());
#else
    private static readonly Lazy<GlyphCatalogResources> Resources = new(static () => []);
#endif

    public new const string SEGOE_FLUENT_ICONS = CommonGlyphCatalog.SEGOE_FLUENT_ICONS;
    public new const string SEGOE_MDL2_ASSETS = CommonGlyphCatalog.SEGOE_MDL2_ASSETS;

    public new static Glyph SETTINGS => Glyph("Settings");
    public new static Glyph EXIT => Glyph("Exit");
    public new static Glyph WARNING => Glyph("Warning");

    public new static Glyph CHEVRON_UP_BIG => Glyph("ChevronUpBig");
    public new static Glyph CHEVRON_DOWN_BIG => Glyph("ChevronDownBig");

    public new static Glyph UNDOCK => Glyph("Undock");
    public new static Glyph REDOCK => Glyph("Redock");

    public static Glyph COMMUNICATIONS_ACTIVITY => Glyph("CommunicationsActivity");

    public static Glyph PLAYBACK_VOLUME_MUTE => Glyph("PlaybackVolumeMute");
    public static Glyph PLAYBACK_VOLUME_SILENT => Glyph("PlaybackVolumeSilent");
    public static Glyph PLAYBACK_VOLUME_LOW => Glyph("PlaybackVolumeLow");
    public static Glyph PLAYBACK_VOLUME_MID => Glyph("PlaybackVolumeMid");
    public static Glyph PLAYBACK_VOLUME_HIGH => Glyph("PlaybackVolumeHigh");
    public static Glyph SOUND_SETTINGS => Glyph("SoundSettings");
    public static Glyph VIEW => Glyph("View");
    public static Glyph HIDE => Glyph("Hide");

    public static Glyph MICROPHONE => Glyph("Microphone");
    public static Glyph MICROPHONE_OFF => Glyph("MicrophoneOff");
    public static Glyph MICROPHONE_SLEEP => Glyph("MicrophoneSleep");
    public static Glyph MICROPHONE_LISTENING => Glyph("MicrophoneListening");
    public static Glyph EAR_LISTEN => Glyph("EarListen");

    public static Glyph LOCK => Glyph("Lock");
    public static Glyph UNLOCK => Glyph("Unlock");
    public static Glyph EQUALIZER => Glyph("Equalizer");
    public static Glyph SIGNAL_NOT_CONNECTED => Glyph("SignalNotConnected");

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

    public static Glyph PLAYBACK_DEVICE_DEFAULT => Glyph("PlaybackDeviceDefault");
    public static Glyph PLAYBACK_DEVICE_ENABLED => Glyph("PlaybackDeviceEnabled");
    public static Glyph PLAYBACK_DEVICE_DISABLED => Glyph("PlaybackDeviceDisabled");
    public static Glyph PLAYBACK_DEVICE_DEFAULT_COMMS => Glyph("PlaybackDeviceDefaultComms");

    public static Glyph APP_MUTE_OVERLAY => Glyph("AppMuteOverlay");
    public static Glyph APP_FALLBACK => Glyph("AppFallback");

    public static Glyph BLUETOOTH => Glyph("Bluetooth");
    public static Glyph BT_BATTERY_0 => Glyph("BTBattery0");
    public static Glyph BT_BATTERY_1 => Glyph("BTBattery1");
    public static Glyph BT_BATTERY_2 => Glyph("BTBattery2");
    public static Glyph BT_BATTERY_3 => Glyph("BTBattery3");
    public static Glyph BT_BATTERY_4 => Glyph("BTBattery4");
    public static Glyph BT_BATTERY_5 => Glyph("BTBattery5");
    public static Glyph BT_BATTERY_6 => Glyph("BTBattery6");
    public static Glyph BT_BATTERY_7 => Glyph("BTBattery7");
    public static Glyph BT_BATTERY_8 => Glyph("BTBattery8");
    public static Glyph BT_BATTERY_9 => Glyph("BTBattery9");
    public static Glyph BT_BATTERY_10 => Glyph("BTBattery10");

    public new static Glyph CHROME_MINIMIZE => Glyph("ChromeMinimize");
    public new static Glyph CHROME_MAXIMIZE => Glyph("ChromeMaximize");
    public new static Glyph CHROME_RESTORE => Glyph("ChromeRestore");

    public static Glyph CIRCLE => Glyph("Circle");
    public static Glyph DIAMOND => Glyph("Diamond");
    public static Glyph STAR => Glyph("Star");
    public static Glyph SQUARE => Glyph("Square");
    public static Glyph HEART => Glyph("Heart");

    private static Glyph Glyph(string name)
    {
#if DEBUG
        return Resources.Current.Glyph(name);
#else
        return Resources.Value.Glyph(name);
#endif
    }
}
