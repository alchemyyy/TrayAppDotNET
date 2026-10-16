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
/// chooses whether the two flows interleave inside each bucket or whether flow becomes the outer grouping.
/// With intermixing disabled, the final reversal places all capture devices above all render devices.
/// Disconnected Bluetooth endpoints receive an additional flyout-only policy: hide, follow normal
/// visibility, force into a dedicated section after both normal flows, or force into the normal
/// render/capture ordering. An outer radio-state gate can hide every Bluetooth endpoint while the
/// Windows Bluetooth radio is off, regardless of the disconnected-device policy.
/// The policy covers only paired devices: Unplugged endpoints, and the current pairing's NotPresent endpoints while the radio is off.
/// Orphaned Bluetooth endpoints of unpaired devices or older pairings are never listed.
/// A Bluetooth device whose endpoints are all disconnected can then collapse to one row in the slot of
/// its most prominent endpoint; a device with any active endpoint keeps every row.
/// </summary>
internal static class FlyoutDeviceOrdering
{
    /// <summary>
    /// Returns the visible device list in top-to-bottom display order. Output respects the configured
    /// layout (StateGrouped vs WindowsEnumeration) and the intermix toggle, and excludes endpoints
    /// the visibility gates have hidden.
    /// </summary>
    public static List<AudioDevice> Build(
        IReadOnlyList<AudioDevice> devices,
        AppSettings settings,
        bool isBluetoothRadioEnabled) =>
        Build(
            devices,
            settings,
            applyFlyoutBluetoothPolicies: true,
            isBluetoothRadioEnabled);

    /// <summary>
    /// Internal surface switch keeps flyout-only Bluetooth preferences from changing tray-menu
    /// device links, which otherwise reuse this ordering helper.
    /// </summary>
    internal static List<AudioDevice> Build(
        IReadOnlyList<AudioDevice> devices,
        AppSettings settings,
        bool applyFlyoutBluetoothPolicies,
        bool isBluetoothRadioEnabled)
    {
        List<AudioDevice> visible = new(devices.Count);
        List<AudioDevice> dedicatedDisconnectedBluetooth = [];
        for (int i = 0; i < devices.Count; i++)
        {
            AudioDevice device = devices[i];
            if (!PassesFlyoutParentGates(device, settings)) continue;
            if (applyFlyoutBluetoothPolicies
                && !PassesBluetoothRadioGate(
                    device.IsBluetooth,
                    settings.ShowBluetoothDevicesOnlyWhenBluetoothIsOn,
                    isBluetoothRadioEnabled))
                continue;

            bool normallyVisible = DeviceVisibility.IsVisible(device, settings);
            DisconnectedBluetoothPlacement placement = normallyVisible
                ? DisconnectedBluetoothPlacement.Standard
                : DisconnectedBluetoothPlacement.Hidden;

            if (applyFlyoutBluetoothPolicies
                && device is { IsBluetooth: true, IsDisconnected: true, IsBluetoothOrphaned: false })
            {
                placement = ClassifyDisconnectedBluetooth(
                    settings.FlyoutDisconnectedBluetoothDeviceVisibility,
                    normallyVisible,
                    device.IsBluetoothConnectionPending || device.IsBluetoothAudioWaiting);
            }

            switch (placement)
            {
                case DisconnectedBluetoothPlacement.Standard:
                    visible.Add(device);
                    break;
                case DisconnectedBluetoothPlacement.DedicatedSection:
                    dedicatedDisconnectedBluetooth.Add(device);
                    break;
            }
        }

        List<AudioDevice> ordered = Sort(visible, settings);
        List<AudioDevice> dedicatedSection = Sort(dedicatedDisconnectedBluetooth, settings);
        if (applyFlyoutBluetoothPolicies && settings.CollapseDisconnectedBluetoothDevicesInFlyout)
        {
            HashSet<Guid> connectedContainers = CollectContainersWithActiveBluetoothEndpoint(devices);
            ordered = CollapseDisconnectedBluetooth(
                ordered,
                settings.FlyoutDeviceSort,
                device => ResolveCollapsibleContainer(device, connectedContainers),
                static device => device.DataFlow);
            dedicatedSection = CollapseDisconnectedBluetooth(
                dedicatedSection,
                settings.FlyoutDeviceSort,
                device => ResolveCollapsibleContainer(device, connectedContainers),
                static device => device.DataFlow);
        }

        return PlaceDedicatedSection(ordered, dedicatedSection, settings.FlyoutDeviceSort);
    }

    /// <summary>
    /// Containers of Bluetooth devices with at least one active endpoint. Every endpoint counts, listed
    /// or not, so a half-connected device is recognized even when its active endpoint is hidden.
    /// </summary>
    private static HashSet<Guid> CollectContainersWithActiveBluetoothEndpoint(IReadOnlyList<AudioDevice> devices)
    {
        HashSet<Guid> connectedContainers = [];
        for (int deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
        {
            if (devices[deviceIndex] is { IsBluetooth: true, IsActive: true, ContainerId: { } containerID })
                connectedContainers.Add(containerID);
        }

        return connectedContainers;
    }

    private static Guid? ResolveCollapsibleContainer(AudioDevice device, IReadOnlySet<Guid> connectedContainers) =>
        ResolveCollapsibleContainer(
            device.IsBluetooth,
            device.IsDisconnected,
            device.IsBluetoothOrphaned,
            device.ContainerId,
            connectedContainers);

    /// <summary>
    /// Container a listed endpoint collapses into: a paired Bluetooth endpoint that is disconnected while
    /// no endpoint of its device is active. Null leaves the endpoint as its own row.
    /// </summary>
    internal static Guid? ResolveCollapsibleContainer(
        bool isBluetooth,
        bool isDisconnected,
        bool isBluetoothOrphaned,
        Guid? containerID,
        IReadOnlySet<Guid> connectedContainers)
    {
        if (!isBluetooth || !isDisconnected || isBluetoothOrphaned) return null;
        if (containerID is not { } collapsibleContainerID) return null;
        return connectedContainers.Contains(collapsibleContainerID) ? null : collapsibleContainerID;
    }

    /// <summary>
    /// Keeps one row per collapsible container, in the slot of its most prominent endpoint: the playback
    /// endpoint that sorts with the highest priority, else the highest-priority recording endpoint.
    /// StateGrouped lists are visually bottom-up, so their highest priority is the last row; WindowsEnumeration
    /// lists run top-down. Rows without a container keep their places.
    /// </summary>
    internal static List<TDevice> CollapseDisconnectedBluetooth<TDevice>(
        IReadOnlyList<TDevice> ordered,
        FlyoutDeviceSortOrder sortOrder,
        Func<TDevice, Guid?> collapsibleContainerSelector,
        Func<TDevice, EDataFlow> dataFlowSelector)
    {
        Guid?[] containers = new Guid?[ordered.Count];
        for (int deviceIndex = 0; deviceIndex < ordered.Count; deviceIndex++)
            containers[deviceIndex] = collapsibleContainerSelector(ordered[deviceIndex]);

        Dictionary<Guid, int> representativeIndexByContainer = [];
        bool isBottomUp = sortOrder == FlyoutDeviceSortOrder.StateGrouped;
        for (int priority = 0; priority < ordered.Count; priority++)
        {
            int deviceIndex = isBottomUp ? ordered.Count - 1 - priority : priority;
            if (containers[deviceIndex] is not { } containerID) continue;

            if (!representativeIndexByContainer.TryGetValue(containerID, out int representativeIndex))
            {
                representativeIndexByContainer.Add(containerID, deviceIndex);
                continue;
            }

            // Playback outranks recording, so the first playback endpoint replaces a recording representative
            if (dataFlowSelector(ordered[representativeIndex]) != EDataFlow.eRender
                && dataFlowSelector(ordered[deviceIndex]) == EDataFlow.eRender)
                representativeIndexByContainer[containerID] = deviceIndex;
        }

        List<TDevice> collapsed = new(ordered.Count);
        for (int deviceIndex = 0; deviceIndex < ordered.Count; deviceIndex++)
        {
            if (containers[deviceIndex] is { } containerID
                && representativeIndexByContainer[containerID] != deviceIndex)
                continue;

            collapsed.Add(ordered[deviceIndex]);
        }

        return collapsed;
    }

    internal static bool PassesBluetoothRadioGate(
        bool isBluetoothDevice,
        bool showOnlyWhenBluetoothIsOn,
        bool isBluetoothRadioEnabled) =>
        !isBluetoothDevice || !showOnlyWhenBluetoothIsOn || isBluetoothRadioEnabled;

    /// <summary>
    /// Parent visibility gates that forced disconnected-Bluetooth modes must not bypass. Recording
    /// endpoints remain hidden when either the global recording master or the flyout recording
    /// switch is off; only state-specific disconnected / NotPresent filters are overridden.
    /// </summary>
    private static bool PassesFlyoutParentGates(AudioDevice device, AppSettings settings)
    {
        if (device.DataFlow == EDataFlow.eRender) return true;
        return device.DataFlow == EDataFlow.eCapture
               && settings is { ShowRecordingDevices: true, ShowRecordingDevicesInFlyout: true };
    }

    internal static DisconnectedBluetoothPlacement ClassifyDisconnectedBluetooth(
        FlyoutDisconnectedBluetoothDeviceVisibility visibility,
        bool normallyVisible,
        bool hasConnectionActivity = false)
    {
        bool shouldShow = normallyVisible || hasConnectionActivity;
        return visibility switch
        {
            FlyoutDisconnectedBluetoothDeviceVisibility.NeverShow => DisconnectedBluetoothPlacement.Hidden,
            FlyoutDisconnectedBluetoothDeviceVisibility.Show => shouldShow
                ? DisconnectedBluetoothPlacement.Standard
                : DisconnectedBluetoothPlacement.Hidden,
            FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShow =>
                DisconnectedBluetoothPlacement.DedicatedSection,
            FlyoutDisconnectedBluetoothDeviceVisibility.AlwaysShowIntermixed =>
                DisconnectedBluetoothPlacement.Standard,
            _ => shouldShow ? DisconnectedBluetoothPlacement.Standard : DisconnectedBluetoothPlacement.Hidden
        };
    }

    private static List<AudioDevice> Sort(List<AudioDevice> visible, AppSettings settings) =>
        settings.FlyoutDeviceSort switch
        {
            FlyoutDeviceSortOrder.WindowsEnumeration => SortWindowsEnumeration(visible, settings),
            _ => SortStateGrouped(visible, settings)
        };

    /// <summary>
    /// Places the dedicated disconnected-Bluetooth section after the complete normal playback and
    /// recording block in the configured sort direction. StateGrouped is visually bottom-up, so
    /// its final section belongs above the normal block; WindowsEnumeration is top-down, so its
    /// final section belongs below it.
    /// </summary>
    internal static List<TDevice> PlaceDedicatedSection<TDevice>(
        IReadOnlyList<TDevice> normallyOrdered,
        IReadOnlyList<TDevice> dedicatedSection,
        FlyoutDeviceSortOrder sortOrder)
    {
        List<TDevice> combined = new(normallyOrdered.Count + dedicatedSection.Count);
        if (sortOrder == FlyoutDeviceSortOrder.StateGrouped)
        {
            combined.AddRange(dedicatedSection);
            combined.AddRange(normallyOrdered);
        }
        else
        {
            combined.AddRange(normallyOrdered);
            combined.AddRange(dedicatedSection);
        }

        return combined;
    }

    /// <summary>
    /// State-bucket ordering. Intermixed devices use state as the outer grouping. Separated devices
    /// use flow as the outer grouping and state within each flow. The final list is reversed so the
    /// default render device sits at the bottom and every capture device remains above every render device.
    /// </summary>
    private static List<AudioDevice> SortStateGrouped(List<AudioDevice> visible, AppSettings settings)
    {
        return OrderStateGrouped(
            visible,
            settings.IntermixRecordingWithPlaybackInFlyout,
            static device => device.DataFlow,
            static device => ClassifyBucket(device));
    }

    /// <summary>
    /// Orders a state-classified device list without depending on the live audio endpoint wrapper.
    /// </summary>
    internal static List<TDevice> OrderStateGrouped<TDevice>(
        IReadOnlyList<TDevice> visible,
        bool intermix,
        Func<TDevice, EDataFlow> dataFlowSelector,
        Func<TDevice, int> bucketSelector)
    {
        const int BucketCount = 5;
        const int BucketDefault = 0;
        const int BucketDisconnected = 4;

        List<TDevice>[] buckets = new List<TDevice>[BucketCount];
        for (int bucketIndex = 0; bucketIndex < BucketCount; bucketIndex++) buckets[bucketIndex] = [];

        for (int deviceIndex = 0; deviceIndex < visible.Count; deviceIndex++)
        {
            TDevice device = visible[deviceIndex];
            buckets[bucketSelector(device)].Add(device);
        }

        List<TDevice> ordered = new(visible.Count);
        if (intermix)
        {
            for (int bucketIndex = BucketDefault; bucketIndex <= BucketDisconnected; bucketIndex++)
                ordered.AddRange(buckets[bucketIndex]);
        }
        else
        {
            for (int bucketIndex = BucketDefault; bucketIndex <= BucketDisconnected; bucketIndex++)
            {
                List<TDevice> bucket = buckets[bucketIndex];
                for (int deviceIndex = 0; deviceIndex < bucket.Count; deviceIndex++)
                {
                    TDevice device = bucket[deviceIndex];
                    if (dataFlowSelector(device) == EDataFlow.eRender) ordered.Add(device);
                }
            }

            for (int bucketIndex = BucketDefault; bucketIndex <= BucketDisconnected; bucketIndex++)
            {
                List<TDevice> bucket = buckets[bucketIndex];
                for (int deviceIndex = 0; deviceIndex < bucket.Count; deviceIndex++)
                {
                    TDevice device = bucket[deviceIndex];
                    if (dataFlowSelector(device) == EDataFlow.eCapture) ordered.Add(device);
                }
            }
        }

        ordered.Reverse();
        return ordered;
    }

    /// <summary>
    /// Bucket classifier. Default-multimedia wins over default-comms when one device holds both
    /// roles, matching the flyout's device-state glyph precedence.
    /// </summary>
    private static int ClassifyBucket(AudioDevice device)
    {
        return device.IsDisconnected ? 4 :
            device.IsDisabled ? 3 :
            device.IsDefault ? 0 :
            device.IsDefaultCommunications ? 1 : 2;
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
        {
            if (visible[i].DataFlow == EDataFlow.eRender)
                ordered.Add(visible[i]);
        }

        for (int i = 0; i < visible.Count; i++)
        {
            if (visible[i].DataFlow == EDataFlow.eCapture)
                ordered.Add(visible[i]);
        }

        return ordered;
    }

    internal enum DisconnectedBluetoothPlacement
    {
        Hidden,
        Standard,
        DedicatedSection
    }
}
