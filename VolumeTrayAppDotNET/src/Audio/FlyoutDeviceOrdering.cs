using VolumeTrayAppDotNET.Interop;


namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Pure helper that filters and orders <see cref="AudioDevice"/> entries for the volume flyout.
/// Filters through <see cref="DeviceVisibility.IsVisible"/> so the flyout honors the same per-state
/// gates as the tray menu, with one extra flyout-only gate (<see cref="AppSettings.ShowRecordingDevicesInFlyout"/>)
/// layered on top for capture endpoints. Sort modes:
///   * <see cref="FlyoutDeviceSortOrder.StateGrouped"/>: bucket by (default, default-comms, enabled,
///     disabled, disconnected). The list is reversed at the end so the default bucket lands at the
///     bottom of the flyout - default device closest to the user's volume slider.
///   * <see cref="FlyoutDeviceSortOrder.WindowsEnumeration"/>: untouched MMDevice enumeration order
///     so top-to-bottom matches Windows.
/// Render and capture share the same bucketing rule. <see cref="AppSettings.IntermixRecordingWithPlaybackInFlyout"/>
/// chooses whether the two flows interleave inside each bucket or whether capture devices group together
/// at the top of the list (after reversal, that places playback at the bottom and recording above it).
/// </summary>
internal static class FlyoutDeviceOrdering
{
    /// <summary>
    /// Returns the visible device list in top-to-bottom display order. Output respects the configured
    /// layout (StateGrouped vs WindowsEnumeration) and the intermix toggle, and excludes endpoints
    /// the visibility gates have hidden.
    /// </summary>
    public static List<AudioDevice> Build(IReadOnlyList<AudioDevice> devices, AppSettings settings)
    {
        List<AudioDevice> visible = new(devices.Count);
        for (int i = 0; i < devices.Count; i++)
        {
            AudioDevice device = devices[i];
            if (!IsAllowedInFlyout(device, settings)) continue;
            visible.Add(device);
        }

        return settings.FlyoutDeviceSort switch
        {
            FlyoutDeviceSortOrder.WindowsEnumeration => SortWindowsEnumeration(visible, settings),
            _ => SortStateGrouped(visible, settings),
        };
    }

    /// <summary>
    /// Visibility gate for the flyout's device list. Reuses the shared <see cref="DeviceVisibility"/>
    /// rules, then overlays the flyout-only "show recording" toggle on top so the user can see
    /// recording devices in the tray menu without cluttering the volume flyout.
    /// </summary>
    private static bool IsAllowedInFlyout(AudioDevice device, AppSettings settings)
    {
        if (!DeviceVisibility.IsVisible(device, settings)) return false;
        if (device.DataFlow == EDataFlow.eCapture && !settings.ShowRecordingDevicesInFlyout) return false;
        return true;
    }

    /// <summary>
    /// State-bucket ordering. Within a bucket, device order is determined by:
    ///   * intermix off -> render flow first, then capture flow, each preserving enumeration order
    ///   * intermix on  -> single mixed run preserving enumeration order
    /// Buckets are then concatenated [default, comms, active, disabled, disconnected] and the final
    /// list reversed so the default device sits at the bottom of the flyout.
    /// </summary>
    private static List<AudioDevice> SortStateGrouped(List<AudioDevice> visible, AppSettings settings)
    {
        const int BucketCount = 5;
        const int BucketDefault = 0;
        const int BucketComms = 1;
        const int BucketActive = 2;
        const int BucketDisabled = 3;
        const int BucketDisconnected = 4;

        List<AudioDevice>[] buckets = new List<AudioDevice>[BucketCount];
        for (int i = 0; i < BucketCount; i++) buckets[i] = [];

        bool intermix = settings.IntermixRecordingWithPlaybackInFlyout;
        // Render-then-capture pass when not intermixing keeps capture devices grouped after playback
        // inside each bucket; the final reversal then puts capture above playback in the rendered flyout.
        if (intermix)
            for (int i = 0; i < visible.Count; i++)
                buckets[ClassifyBucket(visible[i])].Add(visible[i]);
        else
        {
            for (int i = 0; i < visible.Count; i++)
            {
                AudioDevice d = visible[i];
                if (d.DataFlow == EDataFlow.eRender) buckets[ClassifyBucket(d)].Add(d);
            }

            for (int i = 0; i < visible.Count; i++)
            {
                AudioDevice d = visible[i];
                if (d.DataFlow == EDataFlow.eCapture) buckets[ClassifyBucket(d)].Add(d);
            }
        }

        List<AudioDevice> ordered = new(visible.Count);
        ordered.AddRange(buckets[BucketDefault]);
        ordered.AddRange(buckets[BucketComms]);
        ordered.AddRange(buckets[BucketActive]);
        ordered.AddRange(buckets[BucketDisabled]);
        ordered.AddRange(buckets[BucketDisconnected]);

        ordered.Reverse();
        return ordered;
    }

    /// <summary>
    /// Bucket classifier. Default-multimedia wins over default-comms when one device holds both
    /// roles, mirroring the device-icon glyph precedence in <see cref="WPF.DeviceIconGlyphConverter"/>.
    /// </summary>
    private static int ClassifyBucket(AudioDevice device)
    {
        if (device.IsDisconnected) return 4;
        if (device.IsDisabled) return 3;
        if (device.IsDefault) return 0;
        if (device.IsDefaultCommunications) return 1;
        return 2;
    }

    /// <summary>
    /// Windows enumeration order. With intermix off, render devices come first, then capture, each
    /// preserving enumeration order; with intermix on, the input order is used as-is. No reversal -
    /// "Windows order" means top-to-bottom matches mmsys.cpl / IMMDeviceEnumerator output.
    /// </summary>
    private static List<AudioDevice> SortWindowsEnumeration(List<AudioDevice> visible, AppSettings settings)
    {
        if (settings.IntermixRecordingWithPlaybackInFlyout) return visible;

        List<AudioDevice> ordered = new(visible.Count);
        for (int i = 0; i < visible.Count; i++)
            if (visible[i].DataFlow == EDataFlow.eRender)
                ordered.Add(visible[i]);
        for (int i = 0; i < visible.Count; i++)
            if (visible[i].DataFlow == EDataFlow.eCapture)
                ordered.Add(visible[i]);
        return ordered;
    }
}
