using System.Diagnostics;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Helpers for opening Windows Sound surfaces. OpenDeviceProperties uses the per-endpoint
/// ms-settings deep-link "ms-settings:sound-properties?endpointId=&lt;IMMDevice id&gt;" which the
/// Win11 Settings app resolves to the specific device's properties page (volume, output format,
/// audio enhancements, spatial sound, allow-exclusive-control). The endpoint ID format the URI
/// expects ("{0.0.x.00000000}.{guid}") is exactly the string IMMDevice.GetId returns, so no
/// translation is needed. Legacy mmsys.cpl tab launchers stay for the global menu entries
/// (Playback / Recording / Sounds / Communications) where per-endpoint context doesn't apply.
/// </summary>
internal static class DeviceShellLinks
{
    private const string Rundll32 = "rundll32.exe";

    // Per-endpoint Win11 Settings URI. Documented under the Sound section of the ms-settings
    // reference. The endpointId query parameter takes the IMMDevice ID verbatim; we still
    // EscapeDataString it because the ID contains "{" and "}" which are reserved characters.
    private const string UriDevicePropertiesFormat = "ms-settings:sound-properties?endpointId={0}";

    // Root Sound page in the modern Settings app (System > Sound).
    private const string UriModernSoundSettings = "ms-settings:sound";

    private const string TabPlayback = "playback";
    private const string TabRecording = "recording";
    private const string TabSounds = "sounds";
    private const string TabCommunications = "communications";

    /// <summary>
    /// Opens the Windows 11 Settings per-device properties page for <paramref name="device"/>.
    /// Same surface for default and non-default endpoints alike - the endpointId deep-link
    /// resolves any active or disabled endpoint to its own properties page.
    /// </summary>
    public static void OpenDeviceProperties(AudioDevice device)
    {
        string uri = string.Format(UriDevicePropertiesFormat, Uri.EscapeDataString(device.Id));
        LaunchSettingsUri(uri);
    }

    public static void OpenPlaybackTab() => OpenSoundPanel(TabPlayback);
    public static void OpenRecordingTab() => OpenSoundPanel(TabRecording);
    public static void OpenSoundsTab() => OpenSoundPanel(TabSounds);
    public static void OpenCommunicationsTab() => OpenSoundPanel(TabCommunications);

    /// <summary>
    /// Opens the modern Settings app on the System > Sound page via the ms-settings:sound URI.
    /// </summary>
    public static void OpenModernSoundSettings() => LaunchSettingsUri(UriModernSoundSettings);

    /// <summary>
    /// Dispatches to the legacy mmsys.cpl Playback tab or the modern Settings Sound page based on
    /// the user's <see cref="SoundSettingsTarget"/> preference. Used by the flyout's titlebar
    /// Sound-settings button. If the targeted surface is already open, brings its window to the
    /// foreground (restoring from minimized if needed) instead of launching a fresh instance.
    /// </summary>
    public static void OpenSoundSettings(SoundSettingsTarget target)
    {
        switch (target)
        {
            case SoundSettingsTarget.WindowsSettingsApp:
                if (TryFocusModernSoundSettings()) return;
                OpenModernSoundSettings();
                break;
            default:
                if (TryFocusLegacySoundPanel()) return;
                OpenPlaybackTab();
                break;
        }
    }

    // Modern Settings app is a singleton; finding the SystemSettings.exe host and focusing its main
    // window covers any page the user previously left it on. The URI launch path remains as a
    // fallback when no instance is running (it both starts the app and lands on the Sound page).
    private static bool TryFocusModernSoundSettings() => TryFocusFirstProcessByName("SystemSettings");

    // mmsys.cpl runs hosted by rundll32.exe, and the system may have many unrelated rundll32 hosts
    // alive (other control panels, Windows internals). Confirm the host loaded mmsys.cpl before
    // claiming the window, so we don't accidentally surface some other dialog.
    private static bool TryFocusLegacySoundPanel()
    {
        Process[] hosts = Process.GetProcessesByName("rundll32");
        try
        {
            foreach (Process p in hosts)
            {
                try
                {
                    IntPtr hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;
                    if (!ProcessHostsModule(p, "mmsys.cpl")) continue;
                    if (FocusWindow(hwnd)) return true;
                }
                catch (Exception ex)
                {
                    TADNLog.Log($"DeviceShellLinks.TryFocusLegacySoundPanel: {ex.Message}");
                }
            }
        }
        finally
        {
            foreach (Process p in hosts) p.Dispose();
        }

        return false;
    }

    private static bool TryFocusFirstProcessByName(string processName)
    {
        Process[] procs = Process.GetProcessesByName(processName);
        try
        {
            foreach (Process p in procs)
            {
                try
                {
                    IntPtr hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;
                    if (FocusWindow(hwnd)) return true;
                }
                catch (Exception ex)
                {
                    TADNLog.Log($"DeviceShellLinks.TryFocusFirstProcessByName({processName}): {ex.Message}");
                }
            }
        }
        finally
        {
            foreach (Process p in procs) p.Dispose();
        }

        return false;
    }

    // Best-effort module match. Process.Modules can throw under access restrictions or when the
    // target is exiting; treat any failure as "not a match" and move on.
    private static bool ProcessHostsModule(Process p, string moduleName)
    {
        try
        {
            foreach (ProcessModule m in p.Modules)
                if (string.Equals(m.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch
        {
            // module enumeration failed; treat as no match
        }

        return false;
    }

    // Un-minimize then activate. SetForegroundWindow is gated by Windows foreground rules, but the
    // caller is always reacting to a synchronous user click on our own window, so we hold the
    // foreground-rights token at call time.
    private static bool FocusWindow(IntPtr hwnd)
    {
        if (User32.IsIconic(hwnd)) User32.ShowWindow(hwnd, User32.SW_RESTORE);
        return User32.SetForegroundWindow(hwnd);
    }

    // Launches a ms-settings: URI through ShellExecute so the registered URI handler picks it up.
    // UseShellExecute = true is load-bearing: the alternative direct-exec path can't open URIs.
    private static void LaunchSettingsUri(string uri)
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true, });
        }
        catch (Exception ex) { TADNLog.Log($"DeviceShellLinks.LaunchSettingsUri({uri}): {ex.Message}"); }
    }

    private static void OpenSoundPanel(string tab)
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = Rundll32,
                Arguments = $"shell32.dll,Control_RunDLL mmsys.cpl,,{tab}",
                UseShellExecute = false,
            });
        }
        catch (Exception ex) { TADNLog.Log($"DeviceShellLinks.OpenSoundPanel({tab}): {ex.Message}"); }
    }
}
