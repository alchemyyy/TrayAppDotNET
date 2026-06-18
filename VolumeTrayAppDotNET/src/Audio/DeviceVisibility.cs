using VolumeTrayAppDotNET.Audio.Interop;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Pure filter helper that decides whether a device should appear in user-facing surfaces (tray
/// menu device entries, device-link sub-menus). Mirrors the precedence the user spec requires:
///   * the recording parent toggle gates every recording-flow child setting
///   * "show disabled" is the broad gate; "show default even if disabled" only matters when "show
///     disabled" is OFF (otherwise the disabled device already shows by default)
///   * Unplugged endpoints (real device, currently not connected) are gated by the per-flow
///     "show disconnected" switch
///   * NotPresent endpoints (registry-only ghosts whose driver isn't loaded) are gated by their
///     own cross-flow switch since they're rarely useful and cause "Unknown Device" floods
/// Render and capture share the same precedence layout but use distinct settings so the user can
/// run "no recording at all" while still showing every disabled playback device.
/// </summary>
internal static class DeviceVisibility
{
    /// <summary>
    /// Returns true when <paramref name="device"/> should be surfaced under the current settings.
    /// Active devices always show; disabled / unplugged / NotPresent devices fall through the
    /// appropriate gate. Capture devices return false outright when ShowRecordingDevices is off,
    /// regardless of any per-state child setting.
    /// </summary>
    public static bool IsVisible(AudioDevice device, AppSettings settings)
    {
        bool isRender = device.DataFlow == EDataFlow.eRender;
        bool isCapture = device.DataFlow == EDataFlow.eCapture;
        if (!isRender && !isCapture) return false;

        // Recording parent gate. When off, no capture device shows under any condition.
        if (isCapture && !settings.ShowRecordingDevices) return false;

        if (device.IsActive) return true;

        if (device.IsDisabled) return IsDisabledVisible(device, settings, isRender);

        // NotPresent first: registry ghosts are gated by their own cross-flow switch so flipping
        // "show disconnected" doesn't flood the list with every device the user has ever owned.
        if (device.IsNotPresent) return settings.ShowNotPresentDevices;

        // Unplugged: real endpoint, currently disconnected.
        if (isRender) return settings.ShowDisconnectedPlaybackDevices;
        return settings.ShowDisconnectedRecordingDevices;
    }

    private static bool IsDisabledVisible(AudioDevice device, AppSettings settings, bool isRender)
    {
        if (isRender)
        {
            if (settings.ShowDisabledPlaybackDevices) return true;
            // "Even if disabled" only matters while the broad disabled gate is OFF.
            if (device.IsDefault && settings.ShowDefaultPlaybackDeviceEvenIfDisabled) return true;
            if (device.IsDefaultCommunications && settings.ShowDefaultCommsPlaybackDeviceEvenIfDisabled) return true;
            return false;
        }

        // Capture-flow path mirrors render but reads the recording-side settings.
        if (settings.ShowDisabledRecordingDevices) return true;
        if (device.IsDefault && settings.ShowDefaultRecordingDeviceEvenIfDisabled) return true;
        if (device.IsDefaultCommunications && settings.ShowDefaultCommsRecordingDeviceEvenIfDisabled) return true;
        return false;
    }
}
