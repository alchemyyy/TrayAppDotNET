namespace VolumeTrayAppDotNET.Models;

// Top-level enums consumed by AppSettings. Moved out of AppSettings.cs to keep that file focused on
// settings state. Same namespace, same names - consumers needed no change.

public enum TrayIconStyle
{
    Dynamic,
    Static,
}

/// <summary>
/// Action taken when the tray icon is clicked or scrolled.
/// Skeleton ships with a no-op placeholder; extend with project-specific actions in your fork.
/// </summary>
public enum TrayClickAction
{
    Nothing,
    OpenSettings,
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
/// Which Windows surface the flyout's Sound-settings titlebar button opens.
/// LegacySoundPanel: classic mmsys.cpl Sound control panel (the floating window with Playback /
/// Recording / Sounds / Communications tabs).
/// WindowsSettingsApp: the modern Settings app's System > Sound page (ms-settings:sound).
/// </summary>
public enum SoundSettingsTarget
{
    LegacySoundPanel,
    WindowsSettingsApp,
}

/// <summary>
/// Which slice of an audio endpoint's name the tray context menu shows for each device row.
/// NameAndModel: full FriendlyName, e.g. "Speakers (Realtek(R) Audio)" - displayed as "Name+Model".
/// Name: PKEY_Device_DeviceDesc only, e.g. "Speakers".
/// Model: PKEY_DeviceInterface_FriendlyName only, e.g. "Realtek(R) Audio".
/// Playback and recording lists carry separate enum values so a user can keep playback verbose
/// while collapsing recording rows to just the model (or vice versa).
/// </summary>
public enum TrayMenuDeviceNameStyle
{
    NameAndModel,
    Name,
    Model,
}

/// <summary>
/// How each device's row is laid out relative to its per-app session sliders.
/// AppsAboveDevice: apps on top, device row underneath in the footer band - matches EarTrumpet.
/// AppsBelowDevice: device row on top, apps underneath. Bottom-up device list ordering applies in
/// either style; only the per-cell stacking flips.
/// </summary>
public enum FlyoutDeviceLayoutStyle
{
    AppsAboveDevice,
    AppsBelowDevice,
}

/// <summary>
/// Where the device's title + control-buttons band sits relative to its slider.
/// BelowSlider (default): slider on top, name and per-device action buttons underneath as footer chrome.
/// AboveSlider: name and per-device action buttons render on top, slider underneath.
/// Independent of FlyoutDeviceLayoutStyle, which governs the device-row vs apps stacking.
/// </summary>
public enum FlyoutDeviceTitlePosition
{
    BelowSlider,
    AboveSlider,
}

/// <summary>
/// Ordering rule for the device list in the flyout.
/// StateGrouped: default, default-comms, enabled, disabled, disconnected. Enumeration order breaks ties
/// inside each bucket. The list is rendered bottom-up so the default device sits closest to the user's
/// volume slider in the tray.
/// WindowsEnumeration: untouched MMDevice enumeration order, top-to-bottom matches Windows itself.
/// </summary>
public enum FlyoutDeviceSortOrder
{
    StateGrouped,
    WindowsEnumeration,
}

/// <summary>
/// Visibility rule for the titlebar communications-activity button.
/// AlwaysShow: button always rendered in the header cluster.
/// WhenDuckingOn (default): button only rendered when UserDuckingPreference is set to any active
///                mode (mute / 80% / 50%); hidden when "Do nothing" is selected.
/// Hidden: button never rendered; the registry watcher also stays asleep.
/// </summary>
public enum CommunicationsButtonVisibility
{
    AlwaysShow,
    WhenDuckingOn,
    Hidden,
}

/// <summary>
/// Visual treatment that flags which apps in a recording device's drawer are currently capturing
/// from the microphone (their session State is Active).
/// DimInactive (default): icons of non-capturing apps are dimmed, matching how disabled devices are dimmed.
/// ActiveGlyph: a small overlay glyph is stamped on the icons of actively capturing apps; non-capturers untouched.
/// HideInactive: non-capturing app rows are collapsed entirely, so only actively-capturing apps remain visible.
/// None: no visual indication.
/// </summary>
public enum CaptureActivityIndicator
{
    DimInactive,
    ActiveGlyph,
    HideInactive,
    None,
}

/// <summary>
/// How the per-device app drawer renders its session list.
/// Sliders: full row per app, icon + volume slider + percent text (the original layout).
/// Icons: icons only, packed into an 8-column grid -- pointless to show volume mixer sliders for
/// recording devices since they don't go through a mixing layer.
/// </summary>
public enum AppDrawerDisplayType
{
    Sliders,
    Icons,
}

/// <summary>
/// Stack flow for the grid drawer's app icons. The first four are explicit directions:
///   TopBottom -- horizontal rows, filled top-down (the original layout).
///   BottomTop -- horizontal rows, filled bottom-up so the first item sits closest to the device row.
///   LeftRight -- vertical columns, filled left-to-right.
///   RightLeft -- vertical columns, filled right-to-left.
/// Auto picks BottomTop when apps sit above the device row (so the first app abuts the device) and
/// TopBottom when apps sit below it. The AppDrawerIconsPerRow setting caps the primary-axis group:
/// items-per-row in the horizontal modes, items-per-column in the vertical ones.
/// </summary>
public enum AppDrawerStackDirection
{
    TopBottom,
    BottomTop,
    LeftRight,
    RightLeft,
    Auto,
}

/// <summary>
/// How the icon grid anchors its trailing partial row (or partial column, in vertical-flow stack
/// directions).
///   Off            -- partial group hugs the left / top edge, full rows always left-anchored.
///   Centered       -- partial group is centered along the cross axis; full rows still left-anchored.
///   CenteredSoftMax -- partial group is left-anchored at the position a centered "soft-max"-icon
///                      row would occupy, so icons don't shift as the row grows from 1 up to soft-max.
///                      Past the soft-max count, the row switches to fully centered behavior.
/// </summary>
public enum AppDrawerIconsCenterMode
{
    Off,
    Centered,
    CenteredSoftMax,
}
