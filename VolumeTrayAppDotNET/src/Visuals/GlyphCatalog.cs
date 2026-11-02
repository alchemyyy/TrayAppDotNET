using CommonGlyphCatalog = TrayAppDotNETCommon.Visuals.GlyphCatalog;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;

namespace VolumeTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons glyphs shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : CommonGlyphCatalog
{
    public static Glyph COMMUNICATIONS_ACTIVITY => Glyphs.CommunicationsActivity;

    public static Glyph PLAYBACK_VOLUME_MUTE => Glyphs.PlaybackVolumeMute;
    public static Glyph PLAYBACK_VOLUME_SILENT => Glyphs.PlaybackVolumeSilent;
    public static Glyph PLAYBACK_VOLUME_LOW => Glyphs.PlaybackVolumeLow;
    public static Glyph PLAYBACK_VOLUME_MID => Glyphs.PlaybackVolumeMid;
    public static Glyph PLAYBACK_VOLUME_HIGH => Glyphs.PlaybackVolumeHigh;
    public static Glyph SOUND_SETTINGS => Glyphs.SoundSettings;
    public static Glyph VIEW => Glyphs.View;
    public static Glyph HIDE => Glyphs.Hide;

    public static Glyph MICROPHONE => Glyphs.Microphone;
    public static Glyph MICROPHONE_OFF => Glyphs.MicrophoneOff;
    public static Glyph MICROPHONE_SLEEP => Glyphs.MicrophoneSleep;
    public static Glyph MICROPHONE_LISTENING => Glyphs.MicrophoneListening;
    public static Glyph EAR_LISTEN => Glyphs.EarListen;

    public static Glyph LOCK => Glyphs.Lock;
    public static Glyph UNLOCK => Glyphs.Unlock;
    public static Glyph EQUALIZER => Glyphs.Equalizer;
    public static Glyph SIGNAL_NOT_CONNECTED => Glyphs.SignalNotConnected;

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

    public static Glyph PLAYBACK_DEVICE_DEFAULT => Glyphs.PlaybackDeviceDefault;
    public static Glyph PLAYBACK_DEVICE_ENABLED => Glyphs.PlaybackDeviceEnabled;
    public static Glyph PLAYBACK_DEVICE_DISABLED => Glyphs.PlaybackDeviceDisabled;
    public static Glyph PLAYBACK_DEVICE_DEFAULT_COMMS => Glyphs.PlaybackDeviceDefaultComms;

    public static Glyph APP_MUTE_OVERLAY => Glyphs.AppMuteOverlay;
    public static Glyph APP_FALLBACK => Glyphs.AppFallback;

    public static Glyph BLUETOOTH => Glyphs.Bluetooth;
    public static Glyph BLUETOOTH_AUDIO_WAITING => Glyphs.BluetoothAudioWaiting;
    public static Glyph BT_BATTERY_0 => Glyphs.BTBattery0;
    public static Glyph BT_BATTERY_1 => Glyphs.BTBattery1;
    public static Glyph BT_BATTERY_2 => Glyphs.BTBattery2;
    public static Glyph BT_BATTERY_3 => Glyphs.BTBattery3;
    public static Glyph BT_BATTERY_4 => Glyphs.BTBattery4;
    public static Glyph BT_BATTERY_5 => Glyphs.BTBattery5;
    public static Glyph BT_BATTERY_6 => Glyphs.BTBattery6;
    public static Glyph BT_BATTERY_7 => Glyphs.BTBattery7;
    public static Glyph BT_BATTERY_8 => Glyphs.BTBattery8;
    public static Glyph BT_BATTERY_9 => Glyphs.BTBattery9;
    public static Glyph BT_BATTERY_10 => Glyphs.BTBattery10;

    public static Glyph CIRCLE => Glyphs.Circle;

    // Each read goes through the current dictionary, so a Debug catalog reload reaches the next read
    private static GlyphCatalogResources.GlyphCatalogAxamlProperties Glyphs =>
        GlyphCatalogResources.Current.AxamlGlyphCatalog;
}
