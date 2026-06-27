using System.Diagnostics;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Per-device Equalizer APO state projected onto the device-row equalizer button.
/// </summary>
///   NotAvailable      Equalizer APO is not detected on this system at all - the button glyph dims
///                     and overlays SIGNAL_NOT_CONNECTED. Clicking shows the install / locate dialog.
///   NotInstalled      APO binary exists but no APO is registered against this endpoint. Button
///                     dimmed; click installs the APO chain for this device.
///   EnhancementsOff   APO is installed against this device but PKEY_AudioEndpoint_Disable_SysFx
///                     is set, so the engine bypasses every APO. Button dimmed; click re-enables.
///   Running           APO is active on this device. Button full bright; click uninstalls.
internal enum EqualizerAPOState
{
    NotAvailable,
    NotInstalled,
    EnhancementsOff,
    Running,
}

/// <summary>
/// Live availability monitor for Equalizer APO. Tracks two signals: the EAPO install dir on disk
/// (FileSystemWatcher) and the EAPO COM-server registration in HKCR (re-probed lazily on every
/// read of <see cref="IsAvailable"/>). Either changing flips the per-device button state through
/// <see cref="AvailabilityChanged"/> without requiring a flyout reopen.
/// </summary>
internal static class EqualizerAPOMonitor
{
    // Default 64-bit install location. Equalizer APO ships an x64 installer that lands here.
    // The user can also point us at a custom path via AppSettings (TODO once the settings entry
    // lands) - until then the monitor only watches the default location.
    public const string DefaultInstallDir = @"C:\Program Files\EqualizerAPO";
    public const string ConfiguratorExeName = "Configurator.exe";
    public const string DeviceSelectorExeName = "DeviceSelector.exe";
    public const string EditorExeName = "Editor.exe";
    public const string EqualizerAPODllName = "EqualizerAPO.dll";

    // Sourceforge mirror of the latest x64 installer. Surface in the not-available dialog so the
    // user can grab the EXE in one click. Pinned to a known-good version - chasing 'latest' here
    // would mean parsing Sourceforge's HTML on every flyout open.
    public const string LatestInstallerURL =
        "https://sourceforge.net/projects/equalizerapo/files/1.4.2/EqualizerAPO-x64-1.4.2.exe/download";

    // Raised whenever the watcher believes EAPO availability MAY have changed. Listeners must
    // re-read IsAvailable; the event itself carries no payload because state derives from the
    // filesystem + registry, not from a single transition.
    public static event Action? AvailabilityChanged;

    private static readonly Lock InitGate = new();
    private static bool _initialized;
    private static FileSystemWatcher? _dirWatcher;

    /// <summary>
    /// Whether Equalizer APO can be invoked on this system. True when the install dir holds an
    /// EqualizerAPO.dll AND the EAPO COM server is registered in HKCR. The watcher fires
    /// <see cref="AvailabilityChanged"/> on install-dir presence flips - the COM registration is
    /// re-probed here on each call, so a regsvr32 /u while the app is running still de-trips this
    /// property on the next read.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureWatching();
            string installDir = ResolveInstallDir();
            string dllPath = Path.Combine(installDir, EqualizerAPODllName);
            if (!File.Exists(dllPath)) return false;
            return EqualizerAPOInstaller.IsAPORegistered();
        }
    }

    /// <summary>
    /// Resolves the directory we look in for the EAPO binaries. Today this is always the default
    /// install dir; once AppSettings gains a custom-path entry this method becomes the single
    /// place to consult it.
    /// </summary>
    public static string ResolveInstallDir() => DefaultInstallDir;

    /// <summary>
    /// Idempotent watcher setup. First read of <see cref="IsAvailable"/> from any device triggers
    /// this; subsequent calls return immediately. Watcher disposal happens at process exit through
    /// the FileSystemWatcher finalizer - we don't expose a Stop method because the monitor is a
    /// process-lifetime singleton.
    /// </summary>
    private static void EnsureWatching()
    {
        if (_initialized) return;
        lock (InitGate)
        {
            if (_initialized) return;
            _initialized = true;
            TryStartDirWatcher();
        }
    }

    private static void TryStartDirWatcher()
    {
        try
        {
            string? parent = Path.GetDirectoryName(DefaultInstallDir);
            if (parent == null || !Directory.Exists(parent)) return;

            _dirWatcher = new FileSystemWatcher(parent)
            {
                Filter = Path.GetFileName(DefaultInstallDir),
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                IncludeSubdirectories = false,
            };
            _dirWatcher.Created += OnFsChange;
            _dirWatcher.Deleted += OnFsChange;
            _dirWatcher.Renamed += OnFsChange;
            _dirWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { TADNLog.Log($"EqualizerAPOMonitor.TryStartDirWatcher: {ex.Message}"); }
    }

    private static void OnFsChange(object sender, FileSystemEventArgs e)
    {
        try { AvailabilityChanged?.Invoke(); }
        catch (Exception ex) { TADNLog.Log($"EqualizerAPOMonitor.OnFsChange: {ex.Message}"); }
    }

    /// <summary>
    /// Pushes an AvailabilityChanged so every device row re-runs its probe. Called after our
    /// install / uninstall path mutates the registry - the FileSystemWatcher only fires on
    /// install-dir flips, so per-device toggles need an explicit poke to repaint the UI.
    /// </summary>
    public static void NotifyAvailabilityChanged()
    {
        try { AvailabilityChanged?.Invoke(); }
        catch (Exception ex) { TADNLog.Log($"EqualizerAPOMonitor.NotifyAvailabilityChanged: {ex.Message}"); }
    }

    /// <summary>
    /// Launches the Equalizer APO Configuration Editor. Bound to ctrl+left-click and right-click
    /// on the device-row equalizer button. EAPO's Editor scopes via a config-path argument that
    /// points at the per-endpoint config.txt - when one exists for this device (only after
    /// successful install), pass it; otherwise fall back to the default config.
    /// </summary>
    public static void OpenConfigurationEditor(AudioDevice device)
    {
        try
        {
            string installDir = ResolveInstallDir();
            string editorPath = Path.Combine(installDir, EditorExeName);
            if (!File.Exists(editorPath))
            {
                TADNLog.Log(
                    $"EqualizerAPOMonitor.OpenConfigurationEditor({device.FriendlyName}): {editorPath} not found");
                return;
            }

            // Per-endpoint config lives under <InstallDir>\config\<deviceGuid>\config.txt after
            // a successful install. Pass that as the editor argument when present so the editor
            // opens scoped to this endpoint; fall through to default config.txt otherwise.
            string? endpointGuid = AudioDevice.TryExtractEndpointGuid(device.Id);
            string args = "";
            if (endpointGuid != null)
            {
                string perDevice = Path.Combine(installDir, "config", endpointGuid, "config.txt");
                if (File.Exists(perDevice)) args = $"\"{perDevice}\"";
            }

            ProcessStartInfo psi = new()
            {
                FileName = editorPath, Arguments = args, UseShellExecute = false, WorkingDirectory = installDir,
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"EqualizerAPOMonitor.OpenConfigurationEditor({device.FriendlyName}): {ex.Message}");
        }
    }
}
