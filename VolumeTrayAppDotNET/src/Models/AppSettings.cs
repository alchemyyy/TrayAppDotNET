using Color = Avalonia.Media.Color;
using Colors = Avalonia.Media.Colors;

namespace VolumeTrayAppDotNET.Models;

/// <summary>
/// Root application settings class.
/// Skeleton scaffold with a few illustrative fields - extend with project-specific settings in your fork.
///
/// Range / default conventions:
///   Every clamped numeric setting exposes a public const triple <c>XxxMin</c> / <c>XxxMax</c> /
///   <c>XxxDefault</c>. The same consts are referenced in three places: the field initializer
///   (default), the property setter (clamp), and the settings control that edits the value.
///   Adding a new clamped numeric should follow this pattern - no magic literals at call sites.
///
/// Change notification:
///   <see cref="RaiseChanged"/> fires <see cref="Changed"/> and schedules a debounced disk write via
///   <see cref="RequestSave"/> (one save per quiet period regardless of how many setters fired during
///   it). Per-property events still exist for <see cref="MeterPeakFpsChanged"/> /
///   <see cref="MeterPeakSampleRateChanged"/> which feed AudioDeviceManager retune logic.
///   Hex setters and Temporary*Color setters call <see cref="RaiseChanged"/> so DynamicResource consumers
///   re-resolve; derived projections need notifications on every input.
///   Explicit "save now" paths call <see cref="Save()"/> directly to bypass the debounce.
///   Implements the common settings-notification base so UI bindings react to setter writes; the legacy
///   Changed event still fires for non-binding consumers.
/// </summary>
public partial class AppSettings : AppSettingsCommon
{
    // When the user promotes a device to default through this app (ctrl+click on the device icon
    // or via the tray menu), also promote it to the communications-role default. Strictly a
    // side-effect of our own write path; we never observe other apps' default-changes to mirror.
    public bool SetDefaultCommsToDefault
    {
        get;
        set => SetField(ref field, value);
    }

    // Device visibility filters. The flyout / tray menu honor these when building lists.
    // Parent off -> children are forced off; child "even if disabled" toggles only matter when the
    // disabled-devices toggle is OFF (otherwise the disabled devices already show by default).
    public bool ShowDisabledPlaybackDevices
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowDefaultPlaybackDeviceEvenIfDisabled
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool ShowDefaultCommsPlaybackDeviceEvenIfDisabled
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool ShowDisconnectedPlaybackDevices
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowRecordingDevices
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool ShowDisabledRecordingDevices
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowDefaultRecordingDeviceEvenIfDisabled
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool ShowDefaultCommsRecordingDeviceEvenIfDisabled
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool ShowDisconnectedRecordingDevices
    {
        get;
        set => SetField(ref field, value);
    }

    // Default expanded / collapsed state for a device's app drawer. Only consulted for devices
    // without a persisted per-device override in devices.xml; once the user toggles a specific
    // device's chevron the per-device entry wins. Default true to preserve the original behavior.
    public bool DefaultAppDrawerExpanded
    {
        get;
        set => SetField(ref field, value);
    } = true;

    // Persisted "last-seen active default" id per role / flow. AudioDeviceManager writes these
    // every time GetDefaultAudioEndpoint returns a real device, and reads them as a fallback
    // when the same lookup later comes back null - that null result, while a previously-default
    // device still exists in the device list, means the user disabled the active default and
    // Windows had no other active device of that role / flow to promote. The fallback restores
    // IsDefault on the disabled wrapper so the visibility filter under the
    // ShowDefault*EvenIfDisabled toggles has a target to act on.
    public string? LastKnownDefaultPlaybackDeviceID
    {
        get;
        set => SetField(ref field, value);
    }

    public string? LastKnownDefaultCommsPlaybackDeviceID
    {
        get;
        set => SetField(ref field, value);
    }

    public string? LastKnownDefaultRecordingDeviceID
    {
        get;
        set => SetField(ref field, value);
    }

    public string? LastKnownDefaultCommsRecordingDeviceID
    {
        get;
        set => SetField(ref field, value);
    }

    // Registry-only ghost endpoints surfaced via DeviceState.NotPresent: every USB DAC port the user
    // has ever plugged into, every previous GPU's HDMI outputs, every paired Bluetooth headset that
    // accumulated in the audio device registry. Off by default so the tray / flyout don't drown in
    // "Unknown Device" rows; opt-in for users who want to inspect or revive a ghost endpoint.
    // Cross-flow: applies to both render and capture NotPresent devices in one switch.
    public bool ShowNotPresentDevices
    {
        get;
        set => SetField(ref field, value);
    }

    // Tray-menu quick links to the classic Sound control-panel tabs.
    public bool ShowTrayMenuRecordingLink
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowTrayMenuSoundsLink
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowTrayMenuCommunicationsLink
    {
        get;
        set => SetField(ref field, value);
    }

    // Per-device link entries in the tray menu. When on, every visible enabled device gets a
    // sub-entry that opens the classic device-properties tab (same as ctrl+click on the device icon).
    public bool ShowTrayMenuDeviceLinks
    {
        get;
        set => SetField(ref field, value);
    }

    // Apply a perceptual (exponential) curve when mapping the slider position to the system volume,
    // so equal slider deltas feel like equal loudness deltas. Off by default; raw linear mapping.
    public bool UseLogarithmicVolumeScale
    {
        get;
        set => SetField(ref field, value);
    }

    // Audible feedback on playback-device slider changes only (not per-app sliders, not capture devices).
    // Plays the same wav per-app feedback uses but routed through the specific render endpoint the user
    // just adjusted (WASAPI shared mode), so the ding comes out of that device instead of the system
    // default. Fires on mouse-up after a click/drag and on each wheel notch over the device row.
    public bool PlayDeviceVolumeChangeSound
    {
        get;
        set => SetField(ref field, value);
    } = true;

    // Gate on top of PlayDeviceVolumeChangeSound: skip the ding when the device is already rendering
    // audio (peak meter > 0 at play-time). Keeps the beep out of music / calls / games where it would
    // just step on the existing audio. Checked right before the ding fires, after the dwell, so the
    // reading reflects "is anything playing right now" rather than the gesture's leading edge.
    public bool SuppressDeviceVolumeChangeSoundWhenAudioPlaying
    {
        get;
        set => SetField(ref field, value);
    } = true;

    // Noise floor for the suppression gate above, expressed as 0..100 percent of full scale on the
    // smoothed peak meter (PeakValueMax). Suppression triggers only when the meter EXCEEDS this
    // value, so 0 reproduces the original "any audio at all" behavior and 100 effectively disables
    // suppression. Clamped to [Min, Max] in the setter so a corrupt settings.xml can't drift the
    // gate outside what the spinner allows.
    public const int DingSuppressionPeakThresholdPercentDefault = 5;
    public const int DingSuppressionPeakThresholdPercentMin = 0;
    public const int DingSuppressionPeakThresholdPercentMax = 100;

    public int DingSuppressionPeakThresholdPercent
    {
        get;
        set
        {
            int clamped = Math.Clamp(
                value,
                DingSuppressionPeakThresholdPercentMin,
                DingSuppressionPeakThresholdPercentMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = DingSuppressionPeakThresholdPercentDefault;

    // Same idea for per-app sliders. The wav plays through this app's audio session at MediaPlayer.Volume
    // scaled to the target app's slider value, so the feedback's loudness matches what the user just dialed
    // the app to. Caveat: it isn't injected into the target app's session - if the user has muted/lowered
    // VolumeTrayApp itself, the feedback gets attenuated again on top of that scalar.
    public bool PlayAppVolumeChangeSound
    {
        get;
        set => SetField(ref field, value);
    } = true;

    // Context menu
    public ContextMenuPosition ContextMenuPosition
    {
        get;
        set => SetField(ref field, value);
    } = ContextMenuPosition.Modern;

    // Tray context-menu font size. Drives all text in the menu; every other element scales relative
    // to font size, so this is effectively the menu zoom level. Clamped on set so file-tamper paths
    // pass through the same range the spinner accepts.
    public const int ContextMenuFontSizeDefault = 15;
    public const int ContextMenuFontSizeMin = 8;
    public const int ContextMenuFontSizeMax = 48;

    public int ContextMenuFontSize
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, ContextMenuFontSizeMin, ContextMenuFontSizeMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = ContextMenuFontSizeDefault;

    // Per-flow device-name style for the tray context menu rows. Defaults to NameAndModel so the
    // initial UX matches the prior behavior (full Windows FriendlyName).
    public TrayMenuDeviceNameStyle TrayMenuPlaybackDeviceNameStyle
    {
        get;
        set => SetField(ref field, value);
    } = TrayMenuDeviceNameStyle.NameAndModel;

    public TrayMenuDeviceNameStyle TrayMenuRecordingDeviceNameStyle
    {
        get;
        set => SetField(ref field, value);
    } = TrayMenuDeviceNameStyle.NameAndModel;

    // Cap on the rendered device-name length in the tray context menu. When the chosen name slice
    // exceeds this character count, the suffix is replaced with a 2-dot ellipsis ("..") to keep
    // the menu width predictable. Clamped to the spinner's [Min, Max] range so a corrupt
    // settings.xml can't push the value outside what the UI accepts.
    public const int TrayMenuDeviceNameMaxLengthDefault = 32;
    public const int TrayMenuDeviceNameMaxLengthMin = 3;
    public const int TrayMenuDeviceNameMaxLengthMax = 200;

    public int TrayMenuDeviceNameMaxLength
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, TrayMenuDeviceNameMaxLengthMin, TrayMenuDeviceNameMaxLengthMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = TrayMenuDeviceNameMaxLengthDefault;

    public TrayIconStyle TrayIconStyle
    {
        get;
        set => SetField(ref field, value);
    } = TrayIconStyle.Dynamic;

    // Peak meter overlay drawn on top of the volume slider track.
    // Two-rate model mirroring EarTrumpet: SampleRate is how often we COM-read the raw peak from
    // IAudioMeterInformation (off the UI thread); Fps is how often the render timer advances the
    // step-counter lerp toward the most recent sample. Running Fps > SampleRate is what gives the
    // meter visible smoothness - dispatcher updates the lerp multiple times per sample interval,
    // and the screen at vsync catches a stepped sequence of intermediate values rather than a
    // snap-to-latest sequence.
    // Defaults: SampleRate=90, Fps=180 -> 2 interpolation steps per sample.
    // Both clamped 1..1000 so a corrupt settings.xml can't push either timer to insane rates.
    // ColorHex is a single solid color (no light/dark variant) - default opaque white.
    // TemporaryMeterPeakColor is the live-preview slot the color picker writes during a drag;
    // never persisted, mirrors NullableThemeColor.Temporary*.
    // Meter is two stacked overlays driven by the first two channels of IAudioMeterInformation:
    // MeterPeakColor paints the bar to min(L, R) (the level guaranteed in both channels), and
    // MeterPeakStereoColor paints on top to max(L, R). With a translucent stereo color the
    // mismatch between channels reads as a halo extending past the solid base bar; for mono
    // streams (or when L==R) the two bars coincide.
    public const string MeterPeakColorDefaultHex = "#FFFFFFFF";
    public const string MeterPeakStereoColorDefaultHex = "#80FFFFFF";

    public const int MeterPeakFpsDefault = 180;
    public const int MeterPeakFpsMin = 1;
    public const int MeterPeakFpsMax = 1000;

    public const int MeterPeakSampleRateDefault = 90;
    public const int MeterPeakSampleRateMin = 1;
    public const int MeterPeakSampleRateMax = 1000;

    // Per-redraw ceiling, in 0-100 volume units, on how far VolumeSlider's rendered peak can move
    // toward the incoming smoothed target. Caps single-frame jumps so a sudden silence-to-loud
    // (or loud-to-silence) transition ramps over a few frames instead of teleporting. 0 freezes
    // the meter; 100 disables the clamp (one-tick catch-up).
    public const int MeterPeakChangeCeilingDefault = 9;
    public const int MeterPeakChangeCeilingMin = 0;
    public const int MeterPeakChangeCeilingMax = 100;

    // Unified peak meter collapses min(L, R) and max(L, R) into a single weighted value so the
    // base bar and stereo overlay coincide and read as one solid bar. The weighting favors the
    // quieter channel by the bias multiplier: combined = (low * M + high) / (M + 1). A multiplier
    // of 0 falls back to plain max(L, R); 1 averages the channels; the default of 3 dampens
    // moment-to-moment stereo flutter without fully collapsing to min(L, R).
    public const int UnifiedMeterLowChannelBiasMultiplierDefault = 3;
    public const int UnifiedMeterLowChannelBiasMultiplierMin = 0;
    public const int UnifiedMeterLowChannelBiasMultiplierMax = 100;

    public bool UnifiedPeakMeter
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public int UnifiedMeterLowChannelBiasMultiplier
    {
        get;
        set
        {
            int clamped = Math.Clamp(
                value,
                UnifiedMeterLowChannelBiasMultiplierMin,
                UnifiedMeterLowChannelBiasMultiplierMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = UnifiedMeterLowChannelBiasMultiplierDefault;

    public int MeterPeakFps
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, MeterPeakFpsMin, MeterPeakFpsMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            MeterPeakFpsChanged?.Invoke();
            RaiseChanged();
        }
    } = MeterPeakFpsDefault;

    public int MeterPeakSampleRate
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, MeterPeakSampleRateMin, MeterPeakSampleRateMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            MeterPeakSampleRateChanged?.Invoke();
            RaiseChanged();
        }
    } = MeterPeakSampleRateDefault;

    public int MeterPeakChangeCeiling
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, MeterPeakChangeCeilingMin, MeterPeakChangeCeilingMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = MeterPeakChangeCeilingDefault;

    // App icon retry. AppIconResolver.Acquire() can return null for transient reasons - the most
    // common one is a cold shell-icon cache when a session is enumerated before Explorer has had
    // a chance to extract the app's icon at our target raster size. AudioSession retries up to
    // IconRetryAttempts times after the initial resolution; the wait between attempts grows
    // linearly: wait_n = n * IconRetryIntervalMs. With the default (250ms, 4 attempts) the worst-
    // case schedule is 0ms, +250ms, +500ms, +750ms - total ~1.5s before giving up.
    public const int IconRetryIntervalMsDefault = 250;
    public const int IconRetryIntervalMsMin = 50;
    public const int IconRetryIntervalMsMax = 5000;
    public const int IconRetryAttempts = 4;

    public int IconRetryIntervalMs
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, IconRetryIntervalMsMin, IconRetryIntervalMsMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = IconRetryIntervalMsDefault;

    // Bound for the icon-resolver's LRU "limbo" queue. When a cached icon's refcount drops to zero
    // (its last AudioSession is disposed) it sits in this queue and can be revived on the next
    // Acquire for the same app. When the queue overflows, the oldest dead entry is dropped from the
    // cache entirely. Default 10 keeps a small set of recently-departed apps warm so flipping
    // between apps in a session doesn't pay re-extraction. 0 = evict immediately.
    public const int IconLRULimitDefault = 10;
    public const int IconLRULimitMin = 0;
    public const int IconLRULimitMax = 1000;

    // Old XML element name preserved so legacy settings.xml files still load post-rename.
    public int IconLRULimit
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, IconLRULimitMin, IconLRULimitMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = IconLRULimitDefault;

    public string MeterPeakColorHex
    {
        get;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? MeterPeakColorDefaultHex : value;
            if (field == normalized) return;
            field = normalized;
            OnPropertyChanged();
            // Fire Changed so EffectiveMeterPeakColor consumers re-resolve after programmatic writes.
            RaiseChanged();
        }
    } = MeterPeakColorDefaultHex;

    public Color? TemporaryMeterPeakColor
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            RaiseChanged();
        }
    }

    public Color EffectiveMeterPeakColor =>
        TemporaryMeterPeakColor ?? ColorMath.TryParseHexOrNull(MeterPeakColorHex) ?? Colors.White;

    public string MeterPeakStereoColorHex
    {
        get;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? MeterPeakStereoColorDefaultHex : value;
            if (field == normalized) return;
            field = normalized;
            OnPropertyChanged();
            // See MeterPeakColorHex setter: derived EffectiveMeterPeakStereoColor needs notification.
            RaiseChanged();
        }
    } = MeterPeakStereoColorDefaultHex;

    public Color? TemporaryMeterPeakStereoColor
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            RaiseChanged();
        }
    }

    // Parsed once so a future tweak to MeterPeakStereoColorDefaultHex doesn't leave a stale fallback.
    private static readonly Color MeterPeakStereoColorFallback =
        ColorMath.TryParseHexOrNull(MeterPeakStereoColorDefaultHex) ?? Colors.Transparent;

    public Color EffectiveMeterPeakStereoColor =>
        TemporaryMeterPeakStereoColor
        ?? ColorMath.TryParseHexOrNull(MeterPeakStereoColorHex)
        ?? MeterPeakStereoColorFallback;

    /// <summary>Raised when MeterPeakFps changes so AudioDeviceManager can retune its render interval.</summary>
    public event Action? MeterPeakFpsChanged;

    /// <summary>Raised when MeterPeakSampleRate changes so AudioDeviceManager can retune its sample interval.</summary>
    public event Action? MeterPeakSampleRateChanged;

    // Slider thumb. The built-in catalog (SliderThumbGlyphOption.CreateDefaults) is hardcoded and rebuilt
    // from scratch on every load, so the list itself is never persisted. Only the user's current selection
    // is persisted via the SliderThumb element below - and when that selection names a built-in, the built-in
    // wins; otherwise the loaded option is appended to the catalog so it stays in the dropdown.

    public string SliderThumbGlyph
    {
        get;
        set => SetField(ref field, value);
    } = "Capsule";

    public List<SliderThumbGlyphOption> SliderThumbOptions
    {
        get;
        set => SetField(ref field, value);
    } = [];

    // Getter assumes InitializeSliderThumbCatalog (run from the ctor and after loading) has already
    // populated SliderThumbOptions. A partly-initialized AppSettings would otherwise persist
    // <SliderThumb> as empty.
    public SliderThumbGlyphOption? SerializedSliderThumb
    {
        get => SliderThumbOptions.FirstOrDefault(o => o.Name == SliderThumbGlyph);
        set => _loadedSliderThumb = value;
    }

    private SliderThumbGlyphOption? _loadedSliderThumb;

    // Tray icon interaction. Click actions are surfaced through TrayIconPage; the host wires what each
    // action does. The skeleton's TrayClickAction enum is a placeholder set - extend it with app-specific
    // actions, then update App.xaml.cs's tray click handlers to dispatch on the chosen action.
    public bool TrayScrollEnabled
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public const int WheelVolumeStepPercentDefault = 2;
    public const int WheelVolumeStepPercentMin = 1;
    public const int WheelVolumeStepPercentMax = 100;

    public int WheelVolumeStepPercent
    {
        get;
        set => SetField(
            ref field,
            Math.Clamp(value, WheelVolumeStepPercentMin, WheelVolumeStepPercentMax));
    } = WheelVolumeStepPercentDefault;

    public TrayClickAction TrayDoubleClickAction
    {
        get;
        set => SetField(ref field, value);
    } = TrayClickAction.Nothing;

    public TrayClickAction TrayCtrlLeftClickAction
    {
        get;
        set => SetField(ref field, value);
    } = TrayClickAction.Nothing;

    public TrayClickAction TrayAltLeftClickAction
    {
        get;
        set => SetField(ref field, value);
    } = TrayClickAction.Nothing;

    public TrayClickAction TrayCtrlRightClickAction
    {
        get;
        set => SetField(ref field, value);
    } = TrayClickAction.Nothing;

    public TrayClickAction TrayAltRightClickAction
    {
        get;
        set => SetField(ref field, value);
    } = TrayClickAction.Nothing;

    public TrayClickAction TrayCtrlDoubleLeftClickAction
    {
        get;
        set => SetField(ref field, value);
    } = TrayClickAction.Nothing;

    public TrayClickAction TrayAltDoubleLeftClickAction
    {
        get;
        set => SetField(ref field, value);
    } = TrayClickAction.Nothing;

    // Flyout undock/redock.
    // AllowFlyoutUndock is the master switch: when false, the undock button is hidden and any persisted
    // undocked state is force-redocked the next time the flyout opens, so disabling the feature never
    // strands a free-floating window with no way to redock it.
    // RestoreFlyoutUndockedOnStartup gates the single startup read of FlyoutUndocked; runtime undock /
    // redock writes still persist normally so flipping this back on resumes restoration.
    // FlyoutUndocked + FlyoutHasSavedPosition + FlyoutLeft / FlyoutTop are written on drag-release only,
    // never per-frame, so a drag doesn't saturate disk I/O.
    public bool AllowFlyoutUndock
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool ClampUndockedFlyoutToScreen
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool RestoreFlyoutUndockedOnStartup
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool FlyoutUndocked
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool FlyoutHasSavedPosition
    {
        get;
        set => SetField(ref field, value);
    }

    public double FlyoutLeft
    {
        get;
        set => SetField(ref field, value);
    }

    public double FlyoutTop
    {
        get;
        set => SetField(ref field, value);
    }

    // Flyout chrome layout. Flips the title-bar row (Settings cluster + Undock button) between the
    // top of the flyout (default) and the bottom. Visual only - clamping in PositionNearTray is
    // layout-agnostic and re-resolves the SettingsButton offset via TransformToAncestor.
    public bool FlyoutHeaderAtBottom
    {
        get;
        set => SetField(ref field, value);
    }

    // Where the flyout's Sound-settings titlebar button routes. LegacySoundPanel opens mmsys.cpl
    // (the classic Sound panel) and matches the historical Windows experience; WindowsSettingsApp
    // routes to ms-settings:sound for users who prefer the modern Settings UI.
    public SoundSettingsTarget SoundSettingsTarget
    {
        get;
        set => SetField(ref field, value);
    } = SoundSettingsTarget.LegacySoundPanel;

    // Flyout device list. FlyoutDeviceLayout governs how each device's row stacks against its apps;
    // FlyoutDeviceSort orders the device list itself. ShowRecordingDevicesInFlyout is the flyout-side
    // gate for capture endpoints - it sits under the existing ShowRecordingDevices master so turning
    // recording off globally also hides them from the flyout. IntermixRecordingWithPlaybackInFlyout
    // controls whether render and capture devices interleave inside their state buckets or whether
    // capture devices group together at the top of the list.
    public FlyoutDeviceLayoutStyle FlyoutDeviceLayout
    {
        get;
        set => SetField(ref field, value);
    } = FlyoutDeviceLayoutStyle.AppsAboveDevice;

    public FlyoutDeviceTitlePosition FlyoutDeviceTitlePosition
    {
        get;
        set => SetField(ref field, value);
    } = FlyoutDeviceTitlePosition.BelowSlider;

    public FlyoutDeviceSortOrder FlyoutDeviceSort
    {
        get;
        set => SetField(ref field, value);
    } = FlyoutDeviceSortOrder.StateGrouped;

    public bool ShowRecordingDevicesInFlyout
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool IntermixRecordingWithPlaybackInFlyout
    {
        get;
        set => SetField(ref field, value);
    }

    // Titlebar communications-activity button visibility. Drives both the button's Visibility and
    // whether the registry watcher even runs - Hidden keeps the watcher asleep entirely.
    public CommunicationsButtonVisibility FlyoutCommunicationsButtonVisibility
    {
        get;
        set => SetField(ref field, value);
    } = CommunicationsButtonVisibility.WhenDuckingOn;

    // Per-device-row control-button visibility. One pair per button - playback rows read the *ForPlayback
    // flag, recording rows read the *ForRecording flag. The Listen button is capture-only by nature, so
    // only the recording flag exists; toggling it off hides the listen glyph on recording rows.
    // Default-device and Battery buttons ship on; Lock, EqualizerAPO, and Listen ship off as they are
    // power-user features the typical user never reaches for.
    public bool ShowLockButtonForPlayback
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowEqualizerAPOButtonForPlayback
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowDefaultDeviceButtonForPlayback
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool ShowBatteryButtonForPlayback
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool ShowLockButtonForRecording
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowEqualizerAPOButtonForRecording
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowListenButtonForRecording
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowDefaultDeviceButtonForRecording
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool ShowBatteryButtonForRecording
    {
        get;
        set => SetField(ref field, value);
    } = true;

    // Compact PKEY_AudioEngine_DeviceFormat readout under the device name. On by default - shows the
    // current sample-rate / bit-depth / channel layout in a compact strip under the device name.
    // Toggling on / off just shows / collapses the Canvas; no row metrics shift since the Canvas is
    // already zero-measure.
    public bool ShowDeviceFormatText
    {
        get;
        set => SetField(ref field, value);
    } = true;

    // Suffix the format readout strip with the live Bluetooth A2DP codec on BT-flagged devices.
    // Independent from ShowDeviceFormatText: with format off and codec on, the codec name renders
    // alone on the strip for BT devices (non-BT devices stay collapsed). Same diagnostic-info
    // tier as the format readout itself, so the default is off.
    public bool ShowDeviceCodecText
    {
        get;
        set => SetField(ref field, value);
    }

    public CaptureActivityIndicator CaptureActivityIndicator
    {
        get;
        set => SetField(ref field, value);
    } = CaptureActivityIndicator.ActiveGlyph;

    // Drawer style for the per-app session list under a recording device. Defaults to Icons because
    // recording sessions don't have a per-app volume mixer to control; the user can flip back to
    // Sliders for visual consistency with the playback drawers.
    public AppDrawerDisplayType RecordingAppDrawerDisplayType
    {
        get;
        set => SetField(ref field, value);
    } = AppDrawerDisplayType.Icons;

    // Icon-grid sub-options. Center mode picks how a partial trailing row reads: Off keeps it
    // left-anchored; Centered shifts it to the cross-axis center; CenteredSoftMax left-anchors it
    // at the position a centered soft-max-icon row would occupy (so icons don't visibly shift as
    // the row grows from 1 up to soft-max), then crosses over to fully centered once exceeded.
    // Scale is an integer percent applied to the icon visuals (Image + fallback / mute glyphs) so
    // the user can bump them larger without changing the slot grid. Defaults to 115 so icons read
    // a touch larger than the slider-drawer baseline.
    // Soft-max + icons-per-row defaults / clamps are exposed as consts so the WPF panel DP, the
    // Window-side sanitiser, and the property initializer all reference one source of truth.
    public const int AppDrawerIconsCenterSoftMaxDefault = 7;
    public const int AppDrawerIconsCenterSoftMaxMin = 1;
    public const int AppDrawerIconsCenterSoftMaxMax = 16;

    public AppDrawerIconsCenterMode AppDrawerIconsCenterMode
    {
        get;
        set => SetField(ref field, value);
    } = AppDrawerIconsCenterMode.Off;

    public int AppDrawerIconsCenterSoftMax
    {
        get;
        set
        {
            int clamped = Math.Clamp(
                value, AppDrawerIconsCenterSoftMaxMin, AppDrawerIconsCenterSoftMaxMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = AppDrawerIconsCenterSoftMaxDefault;

    // Integer percent applied to grid-drawer icon visuals. 100 = baseline; default reads a touch
    // larger than the slider-drawer reference. Range spans 50..200 to keep icons readable at both ends.
    public const int AppDrawerIconScalePercentDefault = 110;
    public const int AppDrawerIconScalePercentMin = 50;
    public const int AppDrawerIconScalePercentMax = 200;

    public int AppDrawerIconScalePercent
    {
        get;
        set
        {
            int clamped = Math.Clamp(
                value, AppDrawerIconScalePercentMin, AppDrawerIconScalePercentMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = AppDrawerIconScalePercentDefault;

    // Maximum icons per row in the grid drawer. The slot grid auto-shrinks the cell width when this
    // exceeds 8 so the grid stays inside the drawer's inner band; below 8 the slot stays at 40 and
    // the grid is just visually narrower.
    // In vertical stack-direction modes (LeftRight / RightLeft) this same value caps icons per
    // column instead -- one knob covers both axes.
    public const int AppDrawerIconsPerRowDefault = 9;
    public const int AppDrawerIconsPerRowMin = 1;
    public const int AppDrawerIconsPerRowMax = 16;

    public int AppDrawerIconsPerRow
    {
        get;
        set
        {
            int clamped = Math.Clamp(
                value, AppDrawerIconsPerRowMin, AppDrawerIconsPerRowMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = AppDrawerIconsPerRowDefault;

    // Stack direction for the icon grid. Auto resolves at render time against FlyoutDeviceLayout so
    // the first app always sits closest to its device row regardless of which side the apps are on.
    public AppDrawerStackDirection AppDrawerStackDirection
    {
        get;
        set => SetField(ref field, value);
    } = AppDrawerStackDirection.Auto;

    // Per-device-type, per-drawer-mode caps on how many app rows render before the drawer enters
    // overflow scroll. Sliders caps slider rows (each app = one row); Icons caps icon-grid rows.
    // Four distinct values so a user can tune each axis without one knob bleeding into another --
    // even though playback is currently hard-wired to Sliders, the Icons cap is still stored for
    // future symmetry. Defaults: 24 slider rows / 10 icon rows match the heaviest usable density
    // before a typical flyout exceeds the screen.
    public const int AppDrawerSlidersMaxAppsDefault = 24;
    public const int AppDrawerSlidersMaxAppsMin = 1;
    public const int AppDrawerSlidersMaxAppsMax = 200;
    public const int AppDrawerIconsMaxRowsDefault = 10;
    public const int AppDrawerIconsMaxRowsMin = 1;
    public const int AppDrawerIconsMaxRowsMax = 200;

    public int PlaybackAppDrawerSlidersMaxApps
    {
        get;
        set
        {
            int clamped = Math.Clamp(
                value, AppDrawerSlidersMaxAppsMin, AppDrawerSlidersMaxAppsMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = AppDrawerSlidersMaxAppsDefault;

    public int PlaybackAppDrawerIconsMaxRows
    {
        get;
        set
        {
            int clamped = Math.Clamp(
                value, AppDrawerIconsMaxRowsMin, AppDrawerIconsMaxRowsMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = AppDrawerIconsMaxRowsDefault;

    public int RecordingAppDrawerSlidersMaxApps
    {
        get;
        set
        {
            int clamped = Math.Clamp(
                value, AppDrawerSlidersMaxAppsMin, AppDrawerSlidersMaxAppsMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = AppDrawerSlidersMaxAppsDefault;

    public int RecordingAppDrawerIconsMaxRows
    {
        get;
        set
        {
            int clamped = Math.Clamp(
                value, AppDrawerIconsMaxRowsMin, AppDrawerIconsMaxRowsMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = AppDrawerIconsMaxRowsDefault;

    // Single per-process throttler keyed by the AppSettings instance. Coalesces a burst of setters into
    // one disk write after the quiet period; latest-pending-wins so the last-fired RequestSave runs.
    private const int SaveDebounceMs = 400;

    private static readonly AsyncThrottler<AppSettings> SaveThrottle = new(
        SaveDebounceMs,
        drainPollIntervalMs: TimeConstants.DrainPollIntervalMs);

    /// <summary>
    /// Schedules a debounced save to <see cref="GetDefaultPath"/>. Multiple calls within the quiet
    /// period collapse to one disk write. Use <see cref="Save()"/> when an immediate flush is required.
    /// </summary>
    protected override void RequestSave()
    {
        AppSettings self = this;
        _ = SaveThrottle.RunAsync(self, _ =>
        {
            self.Save();
            return Task.CompletedTask;
        });
    }

    public AppSettings()
        : base(TimeConstants.UpdateCheckIntervalDefaultMs)
    {
        WireColorCallbacks();
        InitializeSliderThumbCatalog();
    }

    /// <summary>
    /// Seeds <see cref="SliderThumbOptions"/> from the built-in catalog, and, if a user-selected option was
    /// loaded from XML, either points <see cref="SliderThumbGlyph"/> at the matching built-in (by Name)
    /// or appends the loaded option to the catalog so it remains visible in the dropdown.
    /// </summary>
    public void InitializeSliderThumbCatalog()
    {
        List<SliderThumbGlyphOption> catalog = SliderThumbGlyphOption.CreateDefaults();

        if (_loadedSliderThumb is { } saved && !string.IsNullOrEmpty(saved.Name))
        {
            if (catalog.All(o => o.Name != saved.Name)) catalog.Add(saved);
            SliderThumbGlyph = saved.Name;
        }

        SliderThumbOptions = catalog;
    }

    /// <summary>
    /// Bridges every NullableThemeColor override on this instance to the global Changed event,
    /// so any color edit (committed hex or live-preview Temporary*) flows out through the same
    /// notification path as every other setting change.
    /// Idempotent: Unsubscribe runs first, so re-wiring after loading replaces the ctor-wired
    /// instances can't double-fire.
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
    }

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "settings.xml");
    }

    // The folder that holds settings.xml and other per-app data. Used by the uninstaller's
    // "delete settings" branch.
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
            // Ensure a partially-written .tmp from a mid-write failure doesn't linger across launches.
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
                return LoadXml(stream);
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppSettings.LoadOrDefault: {ex.Message}");
        }

        AppSettings defaults = new();
        defaults.Save(path);
        return defaults;
    }
}
