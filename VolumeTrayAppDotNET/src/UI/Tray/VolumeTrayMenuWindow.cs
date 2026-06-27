using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using VolumeTrayAppDotNET.Audio;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace VolumeTrayAppDotNET.UI.Tray;

public sealed class VolumeTrayMenuWindow : TrayMenuWindow
{
    private const string TrayMenuTruncationSuffix = "..";

    internal VolumeTrayMenuWindow(
        IReadOnlyList<AudioDevice> devices,
        AppSettings settings,
        SettingsPalette palette,
        bool rounded,
        int fontSize,
        Action openSettings,
        Action exit)
        : base(
            BuildEntries(devices, settings, openSettings, exit),
            new TrayMenuWindowOptions
            {
                Palette = palette,
                Rounded = rounded,
                FontSize = fontSize,
                SeparatorColor = ResolveSeparatorColor(palette),
                ShadowColor = ResolveMenuShadowColor(),
                ScrollToBottom = true,
            })
    {
    }

    internal void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        ContextMenuPosition placement) =>
        base.ShowAt(trayIcon, cursorPoint, ToCommonPlacement(placement));

    private static List<TrayMenuEntry> BuildEntries(
        IReadOnlyList<AudioDevice> devices,
        AppSettings settings,
        Action openSettings,
        Action exit)
    {
        TrayMenuEntryBuilder entries = new();

        List<AudioDevice> orderedForFlyout = FlyoutDeviceOrdering.Build(devices, settings);
        if (settings.ShowTrayMenuDeviceLinks && orderedForFlyout.Count > 0)
        {
            for (int i = orderedForFlyout.Count - 1; i >= 0; i--)
            {
                AudioDevice device = orderedForFlyout[i];
                entries.Add(
                    FormatTrayMenuDeviceName(device, settings),
                    () => DeviceShellLinks.OpenDeviceProperties(device));
            }

            entries.AddSeparator();
        }

        entries.Add(L("Tray_SoundDevices", "Sound devices"), DeviceShellLinks.OpenPlaybackTab);
        if (settings.ShowTrayMenuRecordingLink)
            entries.Add(L("Tray_Recording", "Recording"), DeviceShellLinks.OpenRecordingTab);
        if (settings.ShowTrayMenuSoundsLink)
            entries.Add(L("Tray_Sounds", "Sounds"), DeviceShellLinks.OpenSoundsTab);
        if (settings.ShowTrayMenuCommunicationsLink)
            entries.Add(L("Tray_Communications", "Communications"), DeviceShellLinks.OpenCommunicationsTab);
        entries.Add(L("Tray_Bluetooth", "Bluetooth"), OpenBluetoothFlyout);
        entries.AddSeparator();
        entries.Add(L("Tray_Settings", "Settings"), openSettings);
        entries.AddSeparator();
        entries.Add(L("Tray_Exit", "Exit"), exit);
        return entries.ToList();
    }

    private static string FormatTrayMenuDeviceName(AudioDevice device, AppSettings settings)
    {
        TrayMenuDeviceNameStyle style = device.IsCaptureDevice
            ? settings.TrayMenuRecordingDeviceNameStyle
            : settings.TrayMenuPlaybackDeviceNameStyle;

        string raw = style switch
        {
            TrayMenuDeviceNameStyle.Name => device.DeviceDescription,
            TrayMenuDeviceNameStyle.Model => device.InterfaceFriendlyName,
            _ => device.FriendlyName,
        };

        if (string.IsNullOrEmpty(raw)) return device.FriendlyName;

        int max = settings.TrayMenuDeviceNameMaxLength;
        if (raw.Length <= max) return raw;

        int keep = Math.Max(0, max - TrayMenuTruncationSuffix.Length);
        return keep == 0 ? raw[..Math.Min(raw.Length, max)] : raw[..keep] + TrayMenuTruncationSuffix;
    }

    private static void OpenBluetoothFlyout()
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "ms-actioncenter:controlcenter/bluetooth",
                UseShellExecute = false,
            });
        }
        catch (Exception ex) { TADNLog.Log($"VolumeTrayMenuWindow.OpenBluetoothFlyout: {ex.Message}"); }
    }

    private static Color ResolveSeparatorColor(SettingsPalette palette)
    {
        bool isLight = AppServices.Settings?.ThemeMode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => AppServices.Theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme,
        };
        return AppServices.Theme?.Separator.For(isLight) ?? palette.Border;
    }

    private static Color ResolveMenuShadowColor()
    {
        bool isLight = AppServices.Settings?.ThemeMode switch
        {
            ThemeMode.Light => true,
            ThemeMode.Dark => false,
            _ => AppServices.Theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme,
        };

        return (AppServices.Theme ?? AppTheme.Default).MenuShadow.For(isLight);
    }

    private static TrayMenuWindowPlacement ToCommonPlacement(ContextMenuPosition placement) =>
        placement == ContextMenuPosition.Modern
            ? TrayMenuWindowPlacement.Modern
            : TrayMenuWindowPlacement.Classic;

    private static string L(string key, string fallback)
    {
        try
        {
            string value = TrayLocalization.Instance[key];
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
