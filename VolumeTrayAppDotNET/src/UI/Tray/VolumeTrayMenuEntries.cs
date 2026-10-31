using TrayAppDotNETCommon.UI.ControlMapping;
using VolumeTrayAppDotNET.Audio;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace VolumeTrayAppDotNET.UI.Tray;

/// <summary>Volume's own tray menu entries: device links and the Sound control panel tabs.</summary>
internal static class VolumeTrayMenuEntries
{
    private const string TrayMenuTruncationSuffix = "..";

    public static ContextMenuEntryBuilder Build(IReadOnlyList<AudioDevice> devices, AppSettings settings)
    {
        ContextMenuEntryBuilder entries = new();

        List<AudioDevice> orderedForFlyout = FlyoutDeviceOrdering.Build(
            devices,
            settings,
            applyFlyoutBluetoothPolicies: false,
            isBluetoothRadioEnabled: true);
        if (settings.ShowTrayMenuDeviceLinks && orderedForFlyout.Count > 0)
        {
            for (int i = orderedForFlyout.Count - 1; i >= 0; i--)
            {
                AudioDevice device = orderedForFlyout[i];
                entries.Add(new ContextMenuEntry(
                    FormatTrayMenuDeviceName(device, settings),
                    () => DeviceShellLinks.OpenDeviceProperties(device))
                {
                    Node = ControlMap.TrayMenu.OpenDeviceProperties
                });
            }

            entries.AddSeparator();
        }

        AddEntry(entries, L(nameof(AppStrings.Tray_SoundDevices)), DeviceShellLinks.OpenPlaybackTab,
            ControlMap.TrayMenu.OpenPlaybackDevices);
        if (settings.ShowTrayMenuRecordingLink)
        {
            AddEntry(entries, L(nameof(AppStrings.Tray_Recording)), DeviceShellLinks.OpenRecordingTab,
                ControlMap.TrayMenu.OpenRecordingDevices);
        }

        if (settings.ShowTrayMenuSoundsLink)
        {
            AddEntry(entries, L(nameof(AppStrings.Tray_Sounds)), DeviceShellLinks.OpenSoundsTab,
                ControlMap.TrayMenu.OpenSounds);
        }

        if (settings.ShowTrayMenuCommunicationsLink)
        {
            AddEntry(entries, L(nameof(AppStrings.Tray_Communications)), DeviceShellLinks.OpenCommunicationsTab,
                ControlMap.TrayMenu.OpenCommunications);
        }

        AddEntry(entries, L(nameof(AppStrings.Tray_Bluetooth)), DeviceShellLinks.OpenBluetoothQuickSettings,
            ControlMap.TrayMenu.OpenBluetooth);
        return entries;
    }

    private static void AddEntry(ContextMenuEntryBuilder entries, string text, Action click, ControlMapNodeID node) =>
        entries.Add(new ContextMenuEntry(text, click) { Node = node });

    private static string FormatTrayMenuDeviceName(AudioDevice device, AppSettings settings)
    {
        TrayMenuDeviceNameStyle style = device.IsCaptureDevice
            ? settings.TrayMenuRecordingDeviceNameStyle
            : settings.TrayMenuPlaybackDeviceNameStyle;

        string raw = style switch
        {
            TrayMenuDeviceNameStyle.Name => device.DeviceDescription,
            TrayMenuDeviceNameStyle.Model => device.InterfaceFriendlyName,
            _ => device.FriendlyName
        };

        if (string.IsNullOrEmpty(raw)) return device.FriendlyName;

        int max = settings.TrayMenuDeviceNameMaxLength;
        if (raw.Length <= max) return raw;

        int keep = Math.Max(val1: 0, max - TrayMenuTruncationSuffix.Length);
        return keep == 0 ? raw[..Math.Min(raw.Length, max)] : raw[..keep] + TrayMenuTruncationSuffix;
    }

    private static string L(string key) => TrayLocalization.Instance[key];
}
