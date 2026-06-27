using System.Reflection;
using System.Runtime.InteropServices;
using BrightnessTrayAppDotNET.Interop.NightLight;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace BrightnessTrayAppDotNET.Tests.NightLight;

/// <summary>
/// Walks the SettingsHandler chain band-by-band and reports where the signal stops propagating.
///
/// For each probe we capture two cheap signals:
///   1. The singleton's in-memory state (settings_inner kelvin WORD at +6, state_inner active
///      flag at +0, state_inner FILETIME at +8).
///   2. The on-disk registry blob's LastWriteTime + decoded kelvin.
///
/// If the in-memory write happens but the registry doesn't move, the SHTaskPool worker /
/// ICloudStore::Save side is silently failing (band 5/6). If both move, the broker side is
/// the broken layer (band 7+) - which is the bypass-the-broker case.
///
/// This duplicates RVA arithmetic from NightLightCloudStore deliberately; we want a probe
/// that doesn't change with future production refactors.
/// </summary>
internal static class ChainProbeTester
{
    private const string SettingsRegPath =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\"
        + @"default$windows.data.bluelightreduction.settings\"
        + "windows.data.bluelightreduction.settings";

    private const string StateRegPath =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\"
        + @"default$windows.data.bluelightreduction.bluelightreductionstate\"
        + "windows.data.bluelightreduction.bluelightreductionstate";

    // RVA of BlueLightSingleton::SetBlueLightActive on SettingsHandlers_Display.dll v10.0.26100.8117.
    // Decompiled at line 41344 -> 180027CDC. Ours is the only build we hardcode RVAs for.
    private const int SetBlueLightActiveRva_26100_8117 = 0x27CDC;

    private const int SaveDrainMs = 2500;

    public static int Run()
    {
        ConsoleLog.Header("NightLight chain probe");
        ConsoleLog.Info($"PID={Environment.ProcessId}, apartment={Thread.CurrentThread.GetApartmentState()}");

        if (!NightLightCloudStore.IsSupported())
        {
            ConsoleLog.Error("NightLightCloudStore.IsSupported() = false. Init failed; cannot probe further.");
            return 1;
        }
        ConsoleLog.Ok("NightLightCloudStore.IsSupported() = true (Initialize did not throw).");

        // Pull the raw pointers out of NightLightCloudStore so we can invoke individual mutators
        // (production's SaveSettingsKelvin always emits the bracket; here we want to isolate).
        Reflected r;
        try
        {
            r = ReflectInternals();
        }
        catch (Exception ex)
        {
            ConsoleLog.Error($"Reflection into NightLightCloudStore failed: {ex.Message}");
            return 1;
        }

        ConsoleLog.Info($"  hSettingsHandlersDll = 0x{r.HDll.ToInt64():X16}");
        ConsoleLog.Info($"  singleton            = 0x{r.Singleton.ToInt64():X16}");
        ConsoleLog.Info($"  fn SetTargetColorTemperature        = 0x{r.FnSetTemp.ToInt64():X16}");
        ConsoleLog.Info($"  fn SetPreviewColorTemperatureChanges = 0x{r.FnSetPreview.ToInt64():X16}");
        ConsoleLog.Info($"  fn SetBlueLightActive (RVA 0x{SetBlueLightActiveRva_26100_8117:X}) = 0x{r.FnSetActive.ToInt64():X16}");

        SetTempDel    setTemp    = (SetTempDel)Marshal.GetDelegateForFunctionPointer(r.FnSetTemp,    typeof(SetTempDel));
        SetPreviewDel setPreview = (SetPreviewDel)Marshal.GetDelegateForFunctionPointer(r.FnSetPreview, typeof(SetPreviewDel));
        SetActiveDel  setActive  = (SetActiveDel)Marshal.GetDelegateForFunctionPointer(r.FnSetActive,  typeof(SetActiveDel));

        // -- Baseline snapshot --
        ConsoleLog.Header("Baseline snapshot");
        DumpSingleton(r.Singleton);
        BlobSnapshot settings0 = SnapshotBlob(SettingsRegPath, "SETTINGS");
        BlobSnapshot state0    = SnapshotBlob(StateRegPath,    "STATE");

        // Capture the baseline kelvin so we can restore at the very end and leave the system
        // in roughly the state we found it. Reads from the singleton's settings_inner directly.
        IntPtr settingsInnerForRestore = Marshal.ReadIntPtr(r.Singleton, 296);
        short baselineKelvin = settingsInnerForRestore != IntPtr.Zero
            ? Marshal.ReadInt16(settingsInnerForRestore, 6)
            : (short)4910;
        ConsoleLog.Info($"baseline kelvin to restore at end: {baselineKelvin}");

        // -- Probe A: lone SetTargetColorTemperature --
        // The simplest possible CloudStore Save trigger. If Save reaches disk after this, the
        // SettingsHandler -> SHTaskPool -> ICloudStore::Save chain works end-to-end.
        BlobSnapshot settingsA = ProbeSettingsCall(
            "Probe A: lone SetTargetColorTemperature(this, 3000)",
            r.Singleton,
            settings0,
            invoke: () => setTemp(r.Singleton, 3000));

        // -- Probe B: full production bracket --
        // Same as NightLightCloudStore.SaveSettingsKelvin emits during a real slider drag.
        BlobSnapshot settingsB = ProbeSettingsCall(
            "Probe B: bracket (SetTargetColorTemperature + IsDragging-on + IsDragging-off)",
            r.Singleton,
            settingsA,
            invoke: () =>
            {
                setTemp(r.Singleton, 2200);
                setPreview(r.Singleton, 1);
                setPreview(r.Singleton, 0);
            });

        // -- Probe C: SetBlueLightActive flip and restore --
        // This is the on/off path which uses cloud_store::save_async_internal<State> directly,
        // NOT SaveSettingsAsync. Different code path, same destination - useful contrast.
        ProbeStateCall(
            "Probe C: SetBlueLightActive flip-and-restore",
            r.Singleton,
            state0,
            setActive);

        // -- Probe D: held-open preview drag --
        // The c43bbc lambda (SetTargetTemperature continuation) only queues Apply when
        // (active OR preview) AND !inflight. If inflight is the broker-side stuck flag, then
        // pinning preview=1 doesn't help (it's the !inflight gate that's failing).
        // BUT the fb3daf lambda (SetPreviewTemperatureChanges continuation) queues Apply
        // unconditionally - so a preview-flip on every kelvin change might tickle the apply
        // without doing a state-flip. Test this by issuing several kelvin steps, each
        // preceded by a fresh SetPreview(1), and let the user eyeball whether the tint tracks.
        ConsoleLog.Header("Probe D: pinned preview=1 + multiple kelvin steps");
        ConsoleLog.Info("Watch the screen. Each step waits 2.0s so you can see whether the tint tracks the new kelvin.");
        ConsoleLog.Info("Step kelvins: 5500 (cool) -> 3500 (mid) -> 2000 (warm) -> 4500 (mid).");

        try
        {
            setPreview(r.Singleton, 1);
            ConsoleLog.Info("preview pinned to 1 (broker should treat all subsequent kelvins as 'still dragging').");
            Thread.Sleep(300);

            int[] kelvinSteps = [5500, 3500, 2000, 4500];
            foreach (int k in kelvinSteps)
            {
                ConsoleLog.Section($"  setTemp(this, {k})  -> expected visible tint: " + DescribeWarmth(k));
                setTemp(r.Singleton, k);
                Thread.Sleep(2000);
            }

            ConsoleLog.Info("releasing preview to 0 ...");
            setPreview(r.Singleton, 0);
            Thread.Sleep(800);

            BlobSnapshot afterD = SnapshotBlob(SettingsRegPath, "SETTINGS");
            ConsoleLog.Info(
                $"final SETTINGS kelvin~{afterD.Kelvin} (expected ~4500 from last kelvinSteps[]).");
        }
        catch (Exception ex)
        {
            ConsoleLog.Error($"Probe D threw: {ex.GetType().Name}: {ex.Message}");
        }

        // -- Probe E: spaced bracket (defeats SHTaskPool tag-258 dedup) --
        // The bracket in Probe B and production fires three SaveSettingsAsync calls back-to-back;
        // SHTaskPool collapses them into one save (LastWriteTime advanced exactly once). The broker
        // only saw IsDragging=0 and the preview-edge that would queue fb3daf was never observed.
        //
        // Here we space each call out by 250ms so the worker actually drains between them. Expected
        // sequence:
        //   1. setTemp(K)        -> save#1, broker calls SetTargetTemperature(K)
        //                             gated by !inflight; if stuck, no queue (silent miss)
        //   2. setPreview(1)     -> save#2, broker calls SetPreviewTemperatureChanges(1)
        //                             unconditional queue of fb3daf, which runs Apply
        //   3. setPreview(0)     -> save#3, broker calls SetPreviewTemperatureChanges(0)
        //                             active=true so fb3daf runs telemetry-only, no Clear
        // If the tint visibly tracks each kelvin step in Probe E, fb3daf is bypassing the wedged
        // inflight gate and we have a flicker-free path to ship.
        ConsoleLog.Header("Probe E: spaced bracket per kelvin step");
        ConsoleLog.Info("Each kelvin step waits 250ms between setTemp / setPreview(1) / setPreview(0).");
        ConsoleLog.Info("Step kelvins: 5500 -> 3500 -> 2000 -> 4500.");

        try
        {
            int[] eSteps = [5500, 3500, 2000, 4500];
            foreach (int k in eSteps)
            {
                ConsoleLog.Section($"  spaced bracket -> {k}  ({DescribeWarmth(k)})");
                setTemp(r.Singleton, k);
                Thread.Sleep(250);
                setPreview(r.Singleton, 1);
                Thread.Sleep(250);
                setPreview(r.Singleton, 0);
                Thread.Sleep(1500);  // dwell so the user can see whether the tint landed
            }

            BlobSnapshot afterE = SnapshotBlob(SettingsRegPath, "SETTINGS");
            ConsoleLog.Info($"final SETTINGS kelvin~{afterE.Kelvin} (expected ~4500).");
        }
        catch (Exception ex)
        {
            ConsoleLog.Error($"Probe E threw: {ex.GetType().Name}: {ex.Message}");
        }

        // -- Probe F: capture IsDragging Bond CB tag --
        // Bond CB v1 omits default-valued fields, so IsDragging only appears in the SETTINGS blob
        // when it's true. To find its wire encoding we capture three blob snapshots:
        //   1. before:    IsDragging=0 (default), field absent from blob.
        //   2. dragging:  after setPreview(1) save lands, IsDragging=1, field present.
        //   3. released:  after setPreview(0) save lands, IsDragging=0 again, field absent.
        // The bytes inserted between (1) and (2) are the Bond CB tag + value for IsDragging.
        // We synchronously wait on RegNotifyChangeKeyValue between calls so each save lands
        // as its own registry write (no SHTaskPool tag-258 dedup eating the dragging snapshot).
        ConsoleLog.Header("Probe F: capture IsDragging Bond CB tag (3-shot diff)");
        try
        {
            byte[]? before = SnapshotBlobBytes(SettingsRegPath);
            ConsoleLog.Info($"before:   {DumpBytes(before)}");

            WaitForBlobWrite(SettingsRegPath, () => setPreview(r.Singleton, 1), label: "setPreview(1)");
            byte[]? dragging = SnapshotBlobBytes(SettingsRegPath);
            ConsoleLog.Info($"dragging: {DumpBytes(dragging)}");

            WaitForBlobWrite(SettingsRegPath, () => setPreview(r.Singleton, 0), label: "setPreview(0)");
            byte[]? released = SnapshotBlobBytes(SettingsRegPath);
            ConsoleLog.Info($"released: {DumpBytes(released)}");

            DiffAndReport("before -> dragging (IsDragging tag should APPEAR)", before, dragging);
            DiffAndReport("dragging -> released (IsDragging tag should DISAPPEAR)", dragging, released);
        }
        catch (Exception ex)
        {
            ConsoleLog.Error($"Probe F threw: {ex.GetType().Name}: {ex.Message}");
        }

        // -- Probe H: characterize registry activity during a RAW SetValue write --
        // Distinct from Probe G - here we bypass SettingsHandler/CloudStore entirely and write
        // the SETTINGS blob's Data value directly. If CloudCacheInvalidator (or anything else)
        // advances anyway, that's a CloudStore-side reaction to an out-of-band registry change,
        // and that's what we should wait on for the registry backend's spaced bracket. If
        // nothing advances besides the SETTINGS key itself, then there's no observable signal
        // that the broker has been notified of a raw write - and the registry-write path needs
        // a different propagation strategy.
        ConsoleLog.Header("Probe H: scan CloudStore subtree after a RAW RegistryKey.SetValue");
        try
        {
            string accountRoot = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount";
            byte[]? currentBlob = SnapshotBlobBytes(SettingsRegPath);
            if (currentBlob is null)
                ConsoleLog.Warn("Probe H: SETTINGS blob is null; skipping.");
            else
            {
                Dictionary<string, DateTime> beforeMap = SnapshotSubtreeLastWrites(accountRoot);
                ConsoleLog.Info($"baseline: {beforeMap.Count} subkeys.");

                // Write the same blob bytes back - identical content, but RegistryKey.SetValue
                // unconditionally bumps the value's LastWriteTime. This isolates "what fires when
                // the registry value is touched out-of-band."
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(SettingsRegPath, writable: true))
                {
                    if (key is null) ConsoleLog.Warn("  couldn't open SETTINGS key writable; skipping.");
                    else
                    {
                        ConsoleLog.Section("issuing raw SetValue (identical bytes) ...");
                        key.SetValue("Data", currentBlob, RegistryValueKind.Binary);
                        key.Flush();
                    }
                }

                Thread.Sleep(800); // generous observation window
                Dictionary<string, DateTime> afterMap = SnapshotSubtreeLastWrites(accountRoot);
                ReportAdvancedKeys("after raw SetValue", beforeMap, afterMap);
            }
        }
        catch (Exception ex)
        {
            ConsoleLog.Error($"Probe H threw: {ex.GetType().Name}: {ex.Message}");
        }

        // -- Probe G: characterize broker-side registry activity during a working bracket --
        // The Registry-backend bracket needs a real signal to wait on between writes (instead of
        // the current 25ms quiet-window heuristic). To find one, snapshot LastWriteTime of every
        // subkey under the CloudStore account root before and after a known-good SettingsHandler
        // bracket step (setPreview(1) -> setPreview(0)). Any subkey that advanced is something
        // CloudStore or the broker touched as part of propagating the change. If we find a
        // specific key that always advances last, that's our settle signal.
        ConsoleLog.Header("Probe G: scan CloudStore subtree for broker-side activity");
        try
        {
            string accountRoot = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount";
            Dictionary<string, DateTime> beforeMap = SnapshotSubtreeLastWrites(accountRoot);
            ConsoleLog.Info($"baseline: {beforeMap.Count} subkeys under {accountRoot}");

            ConsoleLog.Section("issuing setPreview(1) ...");
            setPreview(r.Singleton, 1);
            Thread.Sleep(800); // generous wait for ALL broker/CloudStore activity to complete
            Dictionary<string, DateTime> afterPreviewOn = SnapshotSubtreeLastWrites(accountRoot);
            ReportAdvancedKeys("after setPreview(1)", beforeMap, afterPreviewOn);

            ConsoleLog.Section("issuing setPreview(0) ...");
            setPreview(r.Singleton, 0);
            Thread.Sleep(800);
            Dictionary<string, DateTime> afterPreviewOff = SnapshotSubtreeLastWrites(accountRoot);
            ReportAdvancedKeys("after setPreview(0)", afterPreviewOn, afterPreviewOff);
        }
        catch (Exception ex)
        {
            ConsoleLog.Error($"Probe G threw: {ex.GetType().Name}: {ex.Message}");
        }

        // -- Restore baseline --
        ConsoleLog.Header("Cleanup: restore baseline kelvin");
        try
        {
            setTemp(r.Singleton, baselineKelvin);
            setPreview(r.Singleton, 1);
            setPreview(r.Singleton, 0);
            ConsoleLog.Info($"emitted standard bracket to restore kelvin to {baselineKelvin}.");
            Thread.Sleep(SaveDrainMs);
            BlobSnapshot final = SnapshotBlob(SettingsRegPath, "SETTINGS");
            ConsoleLog.Info($"final SETTINGS kelvin~{final.Kelvin} (target was {baselineKelvin}).");
        }
        catch (Exception ex)
        {
            ConsoleLog.Warn($"baseline restore threw: {ex.Message}");
        }

        // -- Verdict --
        ConsoleLog.Header("Verdict");
        bool anySettingsAdvance = settingsB.LastWriteTime > settings0.LastWriteTime
                                || settingsA.LastWriteTime > settings0.LastWriteTime;
        if (anySettingsAdvance)
        {
            ConsoleLog.Ok("Save side reached disk for at least one Settings probe.");
            ConsoleLog.Info(
                "If the on-screen tint still isn't tracking the new kelvin, the failure is "
                + "broker-side (CloudStoreDataWatcher subscription, BlueLightReductionManager state, "
                + "or ColorTemperatureControl apply). That makes the leaf-bypass plan the answer.");
        }
        else
        {
            ConsoleLog.Warn("No Settings probe advanced the registry blob's LastWriteTime.");
            ConsoleLog.Info(
                "SHTaskPool worker is either not running or ICloudStore::Save is silently failing "
                + "from the worker thread (same partition-map miss we saw from our thread). The fix "
                + "would have to live in band 5/6, NOT in the broker.");
        }

        return 0;
    }

    // ============================== Probes ==============================

    private static BlobSnapshot ProbeSettingsCall(
        string label, IntPtr singleton, BlobSnapshot before, Action invoke)
    {
        ConsoleLog.Header(label);
        IntPtr settingsInner = Marshal.ReadIntPtr(singleton, 296);
        if (settingsInner == IntPtr.Zero)
        {
            ConsoleLog.Error("settings_inner ptr is null; singleton was init'd to a ghost state. Skipping probe.");
            return before;
        }

        short preMemKelvin = Marshal.ReadInt16(settingsInner, 6);
        ConsoleLog.Info($"pre  in-memory kelvin = {preMemKelvin}");

        try { invoke(); }
        catch (Exception ex) { ConsoleLog.Error($"invoke threw: {ex.GetType().Name}: {ex.Message}"); return before; }

        short postMemKelvin = Marshal.ReadInt16(settingsInner, 6);
        ConsoleLog.Info($"post in-memory kelvin = {postMemKelvin} ({(postMemKelvin != preMemKelvin ? "CHANGED" : "unchanged")})");

        ConsoleLog.Info($"waiting {SaveDrainMs}ms for SHTaskPool drain...");
        Thread.Sleep(SaveDrainMs);

        BlobSnapshot after = SnapshotBlob(SettingsRegPath, "SETTINGS");
        TimeSpan delta = after.LastWriteTime - before.LastWriteTime;
        bool advanced = delta > TimeSpan.Zero;
        bool kelvinMatchesMemory = after.Kelvin == postMemKelvin;

        if (advanced)
            ConsoleLog.Ok($"LastWriteTime advanced by {delta.TotalMilliseconds:F0}ms.");
        else
            ConsoleLog.Warn("LastWriteTime did NOT advance - Save did not reach disk.");

        if (advanced && kelvinMatchesMemory)
            ConsoleLog.Ok($"Saved kelvin matches in-memory ({after.Kelvin}). Save reached disk and is consistent.");
        else if (advanced && !kelvinMatchesMemory)
        {
            ConsoleLog.Warn(
                $"LastWriteTime advanced but saved kelvin {after.Kelvin} != in-memory {postMemKelvin}. "
                + "Worker raced or wrote a stale snapshot.");
        }

        return after;
    }

    private static void ProbeStateCall(
        string label, IntPtr singleton, BlobSnapshot beforeState, SetActiveDel setActive)
    {
        ConsoleLog.Header(label);
        IntPtr stateInner = Marshal.ReadIntPtr(singleton, 264);
        if (stateInner == IntPtr.Zero)
        {
            ConsoleLog.Error("state_inner ptr is null; can't exercise SetBlueLightActive.");
            return;
        }

        // BlueLightReductionState inner field 0 is stored as (active ^ 1). 0 means ON.
        int preMemRaw = Marshal.ReadInt32(stateInner, 0);
        bool preMemActive = preMemRaw == 0;
        long preMemFt = Marshal.ReadInt64(stateInner, 8);
        ConsoleLog.Info($"pre  state.active(raw=^1) = {preMemRaw} -> active={preMemActive}, FILETIME={FormatFileTime(preMemFt)}");

        // Flip and restore so we leave the system in its original state.
        byte target = (byte)(preMemActive ? 0 : 1);
        try
        {
            setActive(singleton, target);
            Thread.Sleep(SaveDrainMs / 2);
            setActive(singleton, (byte)(target ^ 1));
        }
        catch (Exception ex)
        {
            ConsoleLog.Error($"setActive threw: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        Thread.Sleep(SaveDrainMs);
        BlobSnapshot after = SnapshotBlob(StateRegPath, "STATE");
        TimeSpan delta = after.LastWriteTime - beforeState.LastWriteTime;
        if (delta > TimeSpan.Zero)
            ConsoleLog.Ok($"STATE blob LastWriteTime advanced by {delta.TotalMilliseconds:F0}ms (save_async_internal<State> works).");
        else
            ConsoleLog.Warn("STATE blob did NOT advance - SetBlueLightActive's save_async_internal silently failed.");
    }

    // ============================== Snapshots ==============================

    private static void DumpSingleton(IntPtr singleton)
    {
        IntPtr stateInner   = Marshal.ReadIntPtr(singleton, 264);
        IntPtr wrapper      = Marshal.ReadIntPtr(singleton, 272);
        IntPtr settingsInner = Marshal.ReadIntPtr(singleton, 296);

        ConsoleLog.Info($"singleton+264 state_inner   = 0x{stateInner.ToInt64():X16}");
        if (stateInner != IntPtr.Zero)
        {
            int rawActive = Marshal.ReadInt32(stateInner, 0);
            int initFlag  = Marshal.ReadInt32(stateInner, 4);
            long ft       = Marshal.ReadInt64(stateInner, 8);
            ConsoleLog.Info($"  +0  rawActive(^1) = {rawActive} -> active={rawActive == 0}");
            ConsoleLog.Info($"  +4  initialized   = {initFlag}");
            ConsoleLog.Info($"  +8  FILETIME      = {FormatFileTime(ft)}");
        }

        ConsoleLog.Info($"singleton+272 wrapper       = 0x{wrapper.ToInt64():X16}");
        ConsoleLog.Info($"singleton+296 settings_inner= 0x{settingsInner.ToInt64():X16}");
        if (settingsInner != IntPtr.Zero)
        {
            short kelvin   = Marshal.ReadInt16(settingsInner, 6);
            byte isDragging = Marshal.ReadByte(settingsInner, 12);
            ConsoleLog.Info($"  +6  kelvin (WORD) = {kelvin}");
            ConsoleLog.Info($"  +12 isDragging    = {isDragging}");
        }
    }

    private readonly record struct BlobSnapshot(DateTime LastWriteTime, byte[] Bytes, int Kelvin, bool Enabled);

    private static BlobSnapshot SnapshotBlob(string path, string label)
    {
        DateTime lastWrite = ReadKeyLastWriteTime(path);
        byte[]? bytes = ReadValue(path, "Data");
        int kelvin = -1;
        bool enabled = false;

        if (bytes is null)
        {
            ConsoleLog.Warn($"{label}: registry value missing.");
            return new BlobSnapshot(lastWrite, Array.Empty<byte>(), -1, false);
        }

        // Decode by reusing NightLightRegistry's public surface where possible.
        if (label == "SETTINGS")
        {
            int percent = NightLightRegistry.GetStrength();
            kelvin = 6500 - percent * (6500 - 1200) / 100;
        }
        else
            enabled = NightLightRegistry.IsEnabled();

        ConsoleLog.Info(
            $"{label}: {bytes.Length}B, LastWriteTime={FormatLocal(lastWrite)} "
            + (label == "SETTINGS"
                ? $"({DateTime.UtcNow - lastWrite:c} ago) -> kelvin~{kelvin}"
                : $"({DateTime.UtcNow - lastWrite:c} ago) -> enabled={enabled}"));

        return new BlobSnapshot(lastWrite, bytes, kelvin, enabled);
    }

    // ============================== Reflection ==============================

    private readonly record struct Reflected(
        IntPtr HDll, IntPtr Singleton, IntPtr FnSetTemp, IntPtr FnSetPreview, IntPtr FnSetActive);

    private static Reflected ReflectInternals()
    {
        Type t = typeof(NightLightCloudStore);
        IntPtr GetIntPtr(string field) =>
            (IntPtr)t.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        IntPtr hDll        = GetIntPtr("_hSettingsHandlersDll");
        IntPtr singleton   = GetIntPtr("_singleton");
        IntPtr fnSetTemp   = GetIntPtr("_setTargetColorTemperatureFn");
        IntPtr fnSetPreview = GetIntPtr("_setPreviewColorTemperatureChangesFn");
        IntPtr fnSetActive = nint.Add(hDll, SetBlueLightActiveRva_26100_8117);

        return new Reflected(hDll, singleton, fnSetTemp, fnSetPreview, fnSetActive);
    }

    // ============================== Win32 / formatting ==============================

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegQueryInfoKeyW(
        SafeRegistryHandle hKey, IntPtr lpClass, IntPtr lpcchClass,
        IntPtr lpReserved, IntPtr lpcSubKeys, IntPtr lpcbMaxSubKeyLen, IntPtr lpcbMaxClassLen,
        IntPtr lpcValues, IntPtr lpcbMaxValueNameLen, IntPtr lpcbMaxValueLen,
        IntPtr lpcbSecurityDescriptor, out long lpftLastWriteTime);

    private static DateTime ReadKeyLastWriteTime(string subkey)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subkey, writable: false);
            if (key is null) return DateTime.MinValue;
            int rc = RegQueryInfoKeyW(
                key.Handle,
                IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, out long ft);
            if (rc != 0) return DateTime.MinValue;
            return DateTime.FromFileTimeUtc(ft);
        }
        catch (Exception ex)
        {
            ConsoleLog.Warn($"ReadKeyLastWriteTime('{subkey}') threw: {ex.Message}");
            return DateTime.MinValue;
        }
    }

    private static byte[]? ReadValue(string subkey, string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subkey, writable: false);
        return key?.GetValue(name) as byte[];
    }

    private static string FormatFileTime(long ft)
    {
        if (ft <= 0) return "(zero/invalid)";
        try { return FormatLocal(DateTime.FromFileTimeUtc(ft)); }
        catch { return "(out of range)"; }
    }

    private static string FormatLocal(DateTime utc)
    {
        if (utc == DateTime.MinValue) return "(none)";
        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
    }

    private static string DescribeWarmth(int kelvin)
    {
        if (kelvin >= 5500) return "very cool / barely tinted";
        if (kelvin >= 4500) return "cool / mild tint";
        if (kelvin >= 3500) return "moderate orange";
        if (kelvin >= 2500) return "strong orange";
        return "deep amber / max warmth";
    }

    // ============================== Probe F helpers ==============================

    private const int RegNotifyChangeLastSet_Probe = 0x4;
    private const int ProbeWaitTimeoutMs           = 1500;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        int dwNotifyFilter,
        SafeWaitHandle hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

    /// <summary>
    /// Arms a one-shot RegNotify on the given subkey, runs the action, then synchronously waits
    /// on the resulting event handle. Sync wait (not async) since the probe is allowed to block.
    /// </summary>
    private static void WaitForBlobWrite(string subkey, Action call, string label)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subkey, writable: false);
        if (key is null)
        {
            ConsoleLog.Warn($"WaitForBlobWrite: key '{subkey}' missing; firing {label} without wait.");
            call();
            return;
        }
        using EventWaitHandle evt = new(false, EventResetMode.AutoReset);
        int rc = RegNotifyChangeKeyValue(
            key.Handle, false, RegNotifyChangeLastSet_Probe, evt.SafeWaitHandle, true);
        if (rc != 0)
        {
            ConsoleLog.Warn($"WaitForBlobWrite: RegNotifyChangeKeyValue rc={rc}; firing {label} without wait.");
            call();
            return;
        }
        call();
        if (!evt.WaitOne(ProbeWaitTimeoutMs))
            ConsoleLog.Warn($"WaitForBlobWrite: {label} timeout {ProbeWaitTimeoutMs}ms - blob may not have advanced.");
    }

    private static byte[]? SnapshotBlobBytes(string subkey)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subkey, writable: false);
            return key?.GetValue("Data") as byte[];
        }
        catch { return null; }
    }

    private static string DumpBytes(byte[]? bytes)
    {
        if (bytes is null) return "(null)";
        const int MaxBytes = 64;
        int n = Math.Min(bytes.Length, MaxBytes);
        string hex = BitConverter.ToString(bytes, 0, n).Replace("-", " ");
        return bytes.Length > MaxBytes ? $"{hex} ... ({bytes.Length}B total)" : $"{hex} ({bytes.Length}B)";
    }

    private static void DiffAndReport(string label, byte[]? a, byte[]? b)
    {
        ConsoleLog.Section(label);
        if (a is null || b is null)
        {
            ConsoleLog.Warn("  one side is null; can't diff.");
            return;
        }

        // Find first and last differing byte indices.
        int firstDiff = -1;
        int minLen = Math.Min(a.Length, b.Length);
        for (int i = 0; i < minLen; i++)
            if (a[i] != b[i]) { firstDiff = i; break; }

        if (firstDiff < 0 && a.Length == b.Length)
        {
            ConsoleLog.Info("  blobs identical.");
            return;
        }
        if (firstDiff < 0) firstDiff = minLen;

        // Print a window around the diff and the size delta.
        int windowStart = Math.Max(0, firstDiff - 4);
        int windowEnd   = Math.Min(Math.Max(a.Length, b.Length), firstDiff + 16);
        string aWindow = a.Length > windowStart ? BitConverter.ToString(a, windowStart, Math.Min(windowEnd - windowStart, a.Length - windowStart)).Replace("-", " ") : "";
        string bWindow = b.Length > windowStart ? BitConverter.ToString(b, windowStart, Math.Min(windowEnd - windowStart, b.Length - windowStart)).Replace("-", " ") : "";
        ConsoleLog.Info($"  first diff at offset {firstDiff} (a={a.Length}B, b={b.Length}B, delta={b.Length - a.Length:+#;-#;0})");
        ConsoleLog.Info($"  a[{windowStart}..]: {aWindow}");
        ConsoleLog.Info($"  b[{windowStart}..]: {bWindow}");

        // The interesting bytes: those present in b but not in a (or vice versa) at the diff site.
        // For Bond CB v1, IsDragging would be encoded as either:
        //   - (id < 7):   1 byte tag (id<<5 | T_BOOL=2) + 1 byte value
        //   - (7..255):   2 byte tag (0xC2 + id byte)   + 1 byte value
        //   - (id == 0):  1 byte type (T_BOOL=2)        + 1 byte value
        // Inserted bytes at the diff site are the candidate tag.
        if (b.Length > a.Length)
        {
            int insertedLen = b.Length - a.Length;
            int captureLen  = Math.Min(insertedLen + 1, b.Length - firstDiff);
            byte[] inserted = new byte[captureLen];
            Array.Copy(b, firstDiff, inserted, 0, captureLen);
            string insHex = BitConverter.ToString(inserted).Replace("-", " ");
            ConsoleLog.Ok($"  INSERTED at offset {firstDiff}: {insHex}  <-- candidate IsDragging encoding");

            // Decode interpretation.
            if (insertedLen >= 2)
            {
                byte tag0 = b[firstDiff];
                byte tag1 = b[firstDiff + 1];
                int type = tag0 & 0x1F;
                int hi3  = (tag0 >> 5) & 0x07;
                if (hi3 == 6)
                    ConsoleLog.Info($"  decoded: 2-byte tag, type={type} (T_BOOL=2 expected), field_id={tag1}, value={(insertedLen >= 3 ? b[firstDiff + 2] : 0)}");
                else if (hi3 == 0)
                    ConsoleLog.Info($"  decoded: 1-byte tag, type={type} (T_BOOL=2 expected), field_id=0, value={tag1}");
                else
                    ConsoleLog.Info($"  decoded: 1-byte tag, type={type}, field_id={hi3}, value={tag1}");
            }
        }
        else if (a.Length > b.Length)
        {
            int removedLen = a.Length - b.Length;
            int captureLen = Math.Min(removedLen + 1, a.Length - firstDiff);
            byte[] removed = new byte[captureLen];
            Array.Copy(a, firstDiff, removed, 0, captureLen);
            string remHex = BitConverter.ToString(removed).Replace("-", " ");
            ConsoleLog.Ok($"  REMOVED at offset {firstDiff}: {remHex}");
        }
    }

    // ============================== Probe G helpers ==============================

    /// <summary>
    /// Recursively walks <paramref name="rootSubkey"/> under HKCU and records every subkey's
    /// LastWriteTime. Returned dictionary keys are paths relative to HKCU.
    /// </summary>
    private static Dictionary<string, DateTime> SnapshotSubtreeLastWrites(string rootSubkey)
    {
        Dictionary<string, DateTime> result = new(StringComparer.OrdinalIgnoreCase);
        Walk(rootSubkey, result);
        return result;

        static void Walk(string subkey, Dictionary<string, DateTime> dict)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subkey, writable: false);
                if (key is null) return;

                // Capture this key's LastWriteTime.
                DateTime lwt = ReadKeyLastWriteTime(subkey);
                if (lwt != DateTime.MinValue)
                    dict[subkey] = lwt;

                foreach (string child in key.GetSubKeyNames())
                    Walk(subkey + "\\" + child, dict);
            }
            catch
            {
                // Skip inaccessible keys.
            }
        }
    }

    private static void ReportAdvancedKeys(
        string label,
        Dictionary<string, DateTime> before,
        Dictionary<string, DateTime> after)
    {
        ConsoleLog.Section(label);
        List<(string Key, DateTime BeforeT, DateTime AfterT)> advanced = new();

        foreach ((string subkey, DateTime afterT) in after)
        {
            DateTime beforeT = before.TryGetValue(subkey, out DateTime b) ? b : DateTime.MinValue;
            if (afterT > beforeT)
                advanced.Add((subkey, beforeT, afterT));
        }

        if (advanced.Count == 0)
        {
            ConsoleLog.Info("  no subkeys advanced their LastWriteTime.");
            return;
        }

        // Sort by AfterT so we can see ordering of writes.
        advanced.Sort((a, b) => a.AfterT.CompareTo(b.AfterT));
        ConsoleLog.Info($"  {advanced.Count} subkey(s) advanced (sorted oldest -> newest):");
        foreach ((string key, DateTime beforeT, DateTime afterT) in advanced)
        {
            string short_ = key.Length > 90 ? "..." + key[^87..] : key;
            string deltaMs = (afterT - beforeT).TotalMilliseconds.ToString("F0");
            ConsoleLog.Info($"    [{FormatLocal(afterT)}] +{deltaMs}ms  {short_}");
        }
    }

    // ============================== Delegates ==============================

    private delegate void SetTempDel(IntPtr thisPtr, int kelvin);
    private delegate void SetPreviewDel(IntPtr thisPtr, byte b);
    private delegate void SetActiveDel(IntPtr thisPtr, byte b);
}
