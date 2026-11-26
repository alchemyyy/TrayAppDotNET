using System.Buffers.Binary;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TrayAppDotNETCommon.Services.Install;

namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>Where Windows' fix for Explorer's Night Light leak stands; see <see cref="NightLightLeakFix"/>.</summary>
internal enum NightLightLeakFixState
{
    /// <summary>This Windows build's Night Light has no such fix. Also the state before the launch probe ran.</summary>
    NotApplicable,

    /// <summary>Windows turned the fix on at boot.</summary>
    Active,

    /// <summary>The fix is configured, and Windows turns it on at the next restart.</summary>
    PendingRestart,

    /// <summary>The fix exists but is off, so Explorer leaks.</summary>
    Missing
}

/// <summary>
/// Windows' fix for a leak in Explorer's Night Light code, <c>Windows.Shell.BlueLightReduction.dll</c>. Every Night
/// Light change, and this app makes many, opens fresh device contexts for the displays and caches a
/// display-enhancement object under each context handle. The handles are new every time, so the cache never hits and
/// never drops an entry, and each miss leaves two RPCSS connections, each holding a copy of the user's token, on the
/// connection Explorer starts context-menu handlers through. After a day, every right-click spends noticeable time
/// scanning them.
///
/// The same DLL caches one object per monitor instead behind feature 62354587
/// (<c>BlueLightReductionDEMCacheByMonitorId</c>), which Windows' feature rollout may leave off. The fix turns it on
/// the way ViVeTool's <c>/enable /id:62354587 /priority:userpolicy</c> persists it: a UserPolicy override, which
/// outranks the rollout's configuration, in the registry Windows loads at boot. Explorer reads the feature as of boot,
/// so the fix takes effect when Windows restarts; restarting Explorer is not enough. ViVeTool's
/// <c>/reset /id:62354587 /priority:userpolicy</c> removes it.
///
/// Only a Windows restart changes the state, so it is recorded once at launch and again after this app applies the
/// fix. Writing the override needs administrator rights, so <see cref="ApplyAsync"/> relaunches this executable
/// elevated with <see cref="ConfigureModeArgument"/> (see <see cref="TrayAppDotNETElevatedSettingChange"/>).
/// </summary>
internal static class NightLightLeakFix
{
    internal const uint FeatureId = 62354587;

    /// <summary>Elevated self-relaunch mode that writes the override, then exits.</summary>
    public const string ConfigureModeArgument = "--set-night-light-leak-fix";

    private const string NightLightModuleFileName = "Windows.Shell.BlueLightReduction.dll";

    // One DWORD per feature, named by the obfuscated feature id and holding its enabled state
    private const string UserPolicyOverridesKeyPath =
        @"SYSTEM\CurrentControlSet\Policies\Microsoft\FeatureManagement\Overrides";

    // FEATURE_CHANGE_TIME_REBOOT: the state as of this boot, which is the one Explorer's Night Light reads
    private const int FeatureChangeTimeReboot = 3;

    private static readonly string OverrideValueName = ObfuscateFeatureId(FeatureId);

    // FEATURE_ENABLED_STATE, which the override's value also uses
    private enum FeatureEnabledState
    {
        Default,
        Disabled,
        Enabled
    }

    /// <summary>Gets the state recorded at launch, or after this app applied the fix.</summary>
    public static NightLightLeakFixState State { get; private set; }

    /// <summary>Probes the fix and records what it found. Never throws.</summary>
    public static void RecordState()
    {
        FeatureEnabledState bootState = ReadBootState();
        int? overrideState = ReadOverrideState();
        if (bootState == FeatureEnabledState.Enabled)
            State = NightLightLeakFixState.Active;
        else if (overrideState == (int)FeatureEnabledState.Enabled)
            State = NightLightLeakFixState.PendingRestart;
        else
            State = NightLightModuleHasFix() ? NightLightLeakFixState.Missing : NightLightLeakFixState.NotApplicable;

        string overrideText = overrideState?.ToString(CultureInfo.InvariantCulture) ?? "none";
        TADNLog.Log(
            $"NightLightLeakFix: {State} (feature state at boot {bootState}, UserPolicy override {overrideText})");
    }

    /// <summary>
    /// Applies the fix through an elevated relaunch of this executable, which shows a UAC prompt, then records the
    /// state again.
    /// </summary>
    public static async Task<TrayAppDotNETElevatedSettingChangeResult> ApplyAsync()
    {
        TrayAppDotNETElevatedSettingChangeResult result = await TrayAppDotNETElevatedSettingChange.RunElevatedAsync(
            ConfigureModeArgument,
            enable: true,
            logSource: "NightLightLeakFix.ApplyAsync");
        RecordState();
        return result;
    }

    /// <summary>Returns true when this launch is the elevated change that applies the fix.</summary>
    public static bool IsElevatedConfigureLaunch(string[] arguments) =>
        TrayAppDotNETElevatedSettingChange.FindModeArgument(arguments, ConfigureModeArgument) >= 0;

    /// <summary>
    /// Writes the override in the elevated relaunch and returns the process exit code. Only "on" is accepted; the app
    /// never removes the fix.
    /// </summary>
    public static int RunElevatedConfigure(string[] arguments)
    {
        int modeIndex = TrayAppDotNETElevatedSettingChange.FindModeArgument(arguments, ConfigureModeArgument);
        if (!TrayAppDotNETElevatedSettingChange.TryReadToggle(arguments, modeIndex, out bool enable) || !enable)
        {
            TADNLog.Log($"NightLightLeakFix: '{ConfigureModeArgument}' only accepts 'on'.");
            return TrayAppDotNETElevatedSettingChange.FailureExitCode;
        }

        try
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey overrides = machine.CreateSubKey(UserPolicyOverridesKeyPath, writable: true);
            overrides.SetValue(OverrideValueName, (int)FeatureEnabledState.Enabled, RegistryValueKind.DWord);
            return TrayAppDotNETElevatedSettingChange.SuccessExitCode;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"NightLightLeakFix: writing the override failed: {exception.Message}");
            return TrayAppDotNETElevatedSettingChange.FailureExitCode;
        }
    }

    /// <summary>
    /// Names a feature's UserPolicy override the way Windows' feature store does (ViVe's
    /// <c>ObfuscationHelpers.ObfuscateFeatureId</c>), in decimal.
    /// </summary>
    internal static string ObfuscateFeatureId(uint featureId)
    {
        uint swapped = BinaryPrimitives.ReverseEndianness(featureId ^ 0x74161A4E);
        return (BitOperations.RotateLeft(swapped ^ 0x8FB23D4F, offset: 1) ^ 0x833EA8FF)
            .ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets whether the file holds <paramref name="featureId"/> as a little-endian 32-bit value, as a module that
    /// checks the feature does in its code and feature descriptors.
    /// </summary>
    internal static unsafe bool FileContainsFeatureId(string path, uint featureId)
    {
        Span<byte> pattern = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(pattern, featureId);

        using MemoryMappedFile file = MemoryMappedFile.CreateFromFile(
            path,
            FileMode.Open,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read);
        using MemoryMappedViewAccessor view = file.CreateViewAccessor(offset: 0, size: 0, MemoryMappedFileAccess.Read);
        byte* start = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref start);
        try
        {
            // The view ends on a page boundary; the zeros past the end of the file never match a nonzero id
            ReadOnlySpan<byte> bytes = new(start + view.PointerOffset, checked((int)view.Capacity));
            return bytes.IndexOf(pattern) >= 0;
        }
        finally
        {
            view.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    private static FeatureEnabledState ReadBootState()
    {
        try
        {
            return GetFeatureEnabledState(FeatureId, FeatureChangeTimeReboot);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // A Windows build without feature staging has no such fix either; the module check decides
            return FeatureEnabledState.Default;
        }
    }

    private static int? ReadOverrideState()
    {
        try
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? overrides = machine.OpenSubKey(UserPolicyOverridesKeyPath, writable: false);
            return overrides?.GetValue(OverrideValueName) as int?;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"NightLightLeakFix: reading the override failed: {exception.Message}");
            return null;
        }
    }

    // Only a Night Light module that checks the feature can be fixed this way
    private static bool NightLightModuleHasFix()
    {
        try
        {
            string modulePath = Path.Combine(Environment.SystemDirectory, NightLightModuleFileName);
            return FileContainsFeatureId(modulePath, FeatureId);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"NightLightLeakFix: reading {NightLightModuleFileName} failed: {exception.Message}");
            return false;
        }
    }

    [DllImport("api-ms-win-core-featurestaging-l1-1-0.dll")]
    private static extern FeatureEnabledState GetFeatureEnabledState(uint featureId, int changeTime);
}
