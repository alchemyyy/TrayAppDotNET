using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using VolumeTrayAppDotNET.Interop;


namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Faithful port of EqualizerAPO's <c>DeviceAPOInfo</c>: a registry-only per-endpoint installer
/// for the EqualizerAPO pre-mix and post-mix APOs. Key paths, value names, and sentinels are
/// verbatim from EAPO 1.4.2.
/// <para/>
/// <see cref="IsAPORegistered"/> reports whether the EAPO COM server is published; <see cref="Probe"/>
/// returns install state + FX-slot backup for one endpoint; <see cref="Install"/> /
/// <see cref="Uninstall"/> / <see cref="Reinstall"/> mutate the FxProperties chain and the Child
/// APOs backup.
/// <para/>
/// Threading: registry I/O is synchronous. Writes under HKLM\...\MMDevices\Audio\{Render|Capture}
/// require admin; Realtek-style locked keys are handled by the take-ownership fallback. Callers
/// handle <see cref="UnauthorizedAccessException"/>.
/// </summary>
internal static class EqualizerAPOInstaller
{
    // EAPO pre-mix / post-mix CLSIDs - the two APOs registered by EqualizerAPO.dll. Pre-mix runs
    // on each input substream (one per app); post-mix runs once on the mixed engine output.
    // Verbatim from EAPO helpers/RegistryHelper.h.
    public static readonly Guid PreMixGuid = new("eacd2258-fcac-4ff4-b36d-419e924a6d79");
    public static readonly Guid PostMixGuid = new("ec1cc9ce-faed-4822-828a-82a81a6f018f");

    // Sentinels for the per-slot backup stored under Child APOs\<deviceGuid>. EAPO uses these to
    // distinguish three uninstall behaviors: NOKEY -> the FxProperties key didn't exist before
    // install (uninstall deletes the whole key); NOVALUE -> FxProperties existed but this slot had
    // no value (uninstall deletes the value); a real GUID -> restore that GUID into the slot.
    private const string APOGUID_NOKEY = "!KEY";
    private const string APOGUID_NOVALUE = "!VALUE";

    // The five FX slot value names under <device>\FxProperties. Index constants below match this
    // order verbatim with EAPO's allGuidValueNames so install-mode selection stays in sync.
    private const int LFX = 0;
    private const int GFX = 1;
    private const int SFX = 2;
    private const int MFX = 3;
    private const int EFX = 4;
    private const int FxSlotCount = 5;

    private static readonly string[] FxSlotValueNames =
    [
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},1", // LFX (pre-mix, pre-Win8.1)
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},2", // GFX (post-mix, pre-Win8.1)
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5", // SFX (pre-mix)
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6", // MFX (post-mix, software pipe)
        "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7", // EFX (post-mix, endpoint pipe)
    ];

    // Multi-mode counterparts of SFX/MFX/EFX. Only used to decide whether an existing FX chain
    // qualifies for the LFX/GFX legacy shim - if any of these are populated we can't fall back.
    private const string MultiSfxValueName = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},13";
    private const string MultiMfxValueName = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},14";
    private const string MultiEfxValueName = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},15";

    // Per-slot processing-mode REG_MULTI_SZ that EAPO writes alongside SFX/MFX/EFX so the audio
    // engine activates the APO on its default processing mode. The default GUID below is EAPO's
    // verbatim defaultProcessingModeValue.
    private const string SfxProcessingModesValueName = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},5";
    private const string MfxProcessingModesValueName = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},6";
    private const string EfxProcessingModesValueName = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},7";
    private const string DefaultProcessingModeValue = "{c18e2f7e-933d-4965-b7d1-1eef228d2af3}";

    // PKEY_AudioEndpoint_Disable_SysFx as the audio service materializes it on the FxProperties
    // registry key. A DWORD 1 here makes the engine bypass every APO - EAPO deletes this value on
    // install to force-enable enhancements, matching mmsys.cpl's "Disable all enhancements" off.
    private const string DisableEnhancementsValueName = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";

    // {b3f8fa53-...},41 is PKEY_AudioEndpoint_CombinedDevice. Win11 Bluetooth headsets that expose
    // a single combined render+capture endpoint set this - EFX can't load on combined endpoints,
    // so the install-mode auto-selector falls back to SFX/MFX there.
    private const string CombinedDeviceValueName = "{b3f8fa53-0004-438e-9003-51a46e139bfc},41";

    private const string FxTitleValueName = "{b725f130-47ef-101a-a5f1-02608c9eebac},10";

    // Per-device backup keys under HKLM\SOFTWARE\EqualizerAPO\Child APOs\<deviceGuid>.
    private const string PreMixChildGuidValueName = "PreMixChild";
    private const string PostMixChildGuidValueName = "PostMixChild";
    private const string AllowSilentBufferValueName = "AllowSilentBufferModification";
    private const string DisableAutoAdjustValueName = "DisableAutomaticAdjustment";
    private const string VersionValueName = "Version";
    private const string InstallVersion = "2";

    // 64-bit-view registry roots. The MMDevices hive is published only in the 64-bit view on x64
    // Windows; EAPO opens every key with KEY_WOW64_64KEY and we mirror that explicitly so a 32-bit
    // build (none today, but cheap to be correct) wouldn't read a redirected reflection.
    private const string MMDevicesRenderSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
    private const string MMDevicesCaptureSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture";
    private const string AppRegSubKey = @"SOFTWARE\EqualizerAPO";
    private const string ChildApoSubKey = @"SOFTWARE\EqualizerAPO\Child APOs";
    private const string APORegistrationSubKey = @"AudioEngine\AudioProcessingObjects";
    private const string ClsidSubKey = "CLSID";

    // MMDevice DeviceState bits as published in the registry. NotPresent endpoints (registry
    // ghosts from past hardware) are skipped on probe to match EAPO's load().
    private const uint DEVICE_STATE_NOTPRESENT = 0x00000004;

    // ----------------------------------------------------------------------------------
    // System-wide checks
    // ----------------------------------------------------------------------------------

    /// <summary>
    /// True when the EAPO COM server is registered system-wide. Mirrors
    /// <c>DeviceAPOInfo::checkAPORegistration</c> read-only path. Returns false (without throwing)
    /// when any of the four expected keys is missing - that's exactly the state a driver
    /// reinstall produces, and EAPO's DeviceSelector fixes by re-running regsvr32 on its own DLL.
    /// </summary>
    public static bool IsAPORegistered()
    {
        try
        {
            using RegistryKey hkcr = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
            string preGuid = FormatGuid(PreMixGuid);
            string postGuid = FormatGuid(PostMixGuid);

            return KeyExists(hkcr, $@"{APORegistrationSubKey}\{preGuid}")
                   && KeyExists(hkcr, $@"{APORegistrationSubKey}\{postGuid}")
                   && KeyExists(hkcr, $@"{ClsidSubKey}\{preGuid}")
                   && KeyExists(hkcr, $@"{ClsidSubKey}\{postGuid}");
        }
        catch (Exception ex)
        {
            TADNLog.Log($"EqualizerAPOInstaller.IsAPORegistered: {ex.Message}");
            return false;
        }
    }

    // ----------------------------------------------------------------------------------
    // Per-device probe
    // ----------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the EAPO install state for one endpoint. Mirrors <c>DeviceAPOInfo::load</c> minus
    /// the channel-count / sample-rate readout and the Voicemeeter branch - those aren't needed
    /// to drive the button glyph. Returns null when the endpoint isn't present in the registry or
    /// is marked NotPresent (driver was removed and the entry is a ghost).
    /// </summary>
    public static DeviceAPOInfo? Probe(string deviceGuid, bool isCapture)
    {
        try
        {
            using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            string deviceSubKey = $@"{(isCapture ? MMDevicesCaptureSubKey : MMDevicesRenderSubKey)}\{deviceGuid}";

            using RegistryKey? deviceKey = hklm.OpenSubKey(deviceSubKey, writable: false);
            if (deviceKey == null) return null;

            object? stateRaw = deviceKey.GetValue("DeviceState");
            uint state = stateRaw is int s ? unchecked((uint)s) : 0u;
            if ((state & DEVICE_STATE_NOTPRESENT) != 0) return null;

            bool enhancementsDisabled = false;
            bool[] foundAt = new bool[FxSlotCount];
            bool installed = false;
            string[] originalApoGuids = new string[FxSlotCount];

            using (RegistryKey? fxProps = hklm.OpenSubKey($@"{deviceSubKey}\FxProperties", writable: false))
            {
                if (fxProps == null)
                {
                    // FxProperties absent - device was never enhanced. Slot backups stay NOKEY so
                    // a future uninstall knows to delete the synthetic FxProperties key it would
                    // have to create at install time.
                    for (int i = 0; i < FxSlotCount; i++) originalApoGuids[i] = APOGUID_NOKEY;
                }
                else
                {
                    object? disableRaw = fxProps.GetValue(DisableEnhancementsValueName);
                    enhancementsDisabled = disableRaw is int d && d != 0;

                    string preGuid = FormatGuid(PreMixGuid);
                    string postGuid = FormatGuid(PostMixGuid);

                    for (int i = 0; i < FxSlotCount; i++)
                    {
                        if (fxProps.GetValue(FxSlotValueNames[i]) is not string raw)
                        {
                            originalApoGuids[i] = APOGUID_NOVALUE;
                            continue;
                        }

                        // If the current slot value is EAPO itself, the "original" we'd want to
                        // remember is whatever was here before EAPO - that lives in Child APOs.
                        // Stash NOVALUE here so the merge below picks the Child APOs copy.
                        if (string.Equals(raw, preGuid, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(raw, postGuid, StringComparison.OrdinalIgnoreCase))
                        {
                            originalApoGuids[i] = APOGUID_NOVALUE;
                            foundAt[i] = true;
                            installed = true;
                        }
                        else
                            originalApoGuids[i] = raw;
                    }
                }
            }

            InstallMode currentMode = InferInstallMode(hklm, deviceSubKey, foundAt, installed);

            string version = "0";
            if (installed)
            {
                using RegistryKey? childKey = hklm.OpenSubKey($@"{ChildApoSubKey}\{deviceGuid}", writable: false);
                if (childKey != null)
                {
                    object? versionRaw = childKey.GetValue(VersionValueName);
                    if (versionRaw is string vs) version = vs;
                    else version = "1";

                    // Merge the Child APOs backup over the FxProperties snapshot. The Child APOs
                    // copy is the authoritative "what was here before EAPO" record - the
                    // FxProperties slots holding EAPO GUIDs would mislead an uninstall that
                    // trusted them.
                    for (int i = 0; i < FxSlotCount; i++)
                    {
                        object? slotRaw = childKey.GetValue(FxSlotValueNames[i]);
                        if (slotRaw is string slotStr) originalApoGuids[i] = slotStr;
                    }
                }
            }

            return new DeviceAPOInfo
            {
                DeviceGuid = deviceGuid,
                IsCapture = isCapture,
                IsInstalled = installed,
                EnhancementsDisabled = enhancementsDisabled,
                CurrentInstallMode = currentMode,
                Version = version,
                OriginalApoGuids = originalApoGuids,
            };
        }
        catch (Exception ex)
        {
            TADNLog.Log($"EqualizerAPOInstaller.Probe({deviceGuid}, capture={isCapture}): {ex.Message}");
            return null;
        }
    }

    // Auto-selects the install mode the same way EAPO's load() does when no EAPO GUID is found in
    // FxProperties. Win8.1+ defaults to SFX/EFX, falls back to SFX/MFX on Win11 combined
    // Bluetooth endpoints (EFX can't load there), and prefers LFX/GFX when the driver only
    // registered the legacy LFX/GFX slots. When EAPO is already present, the active install mode
    // is inferred from which slot holds the EAPO GUID instead.
    private static InstallMode InferInstallMode(RegistryKey hklm, string deviceSubKey, bool[] foundAt, bool installed)
    {
        if (installed)
        {
            if (foundAt[LFX] || foundAt[GFX]) return InstallMode.LfxGfx;
            if (foundAt[EFX]) return InstallMode.SfxEfx;
            if (foundAt[SFX] || foundAt[MFX]) return InstallMode.SfxMfx;
        }

        // Auto-select for a fresh install. Windows 10+ always satisfies the Win8.1 gate; we keep
        // the check explicit so a hypothetical Win7/8 run path falls through to LFX/GFX.
        if (!IsWindowsVersionAtLeast(6, 3)) return InstallMode.LfxGfx;

        using RegistryKey? fxProps = hklm.OpenSubKey($@"{deviceSubKey}\FxProperties", writable: false);
        bool hasLegacyOnly = fxProps != null
                             && (HasValue(fxProps, FxSlotValueNames[LFX]) || HasValue(fxProps, FxSlotValueNames[GFX]))
                             && !HasValue(fxProps, FxSlotValueNames[SFX])
                             && !HasValue(fxProps, FxSlotValueNames[MFX])
                             && !HasValue(fxProps, FxSlotValueNames[EFX])
                             && !HasValue(fxProps, MultiSfxValueName)
                             && !HasValue(fxProps, MultiMfxValueName)
                             && !HasValue(fxProps, MultiEfxValueName);

        if (hasLegacyOnly) return InstallMode.LfxGfx;

        using RegistryKey? props = hklm.OpenSubKey($@"{deviceSubKey}\Properties", writable: false);
        if (props != null && HasValue(props, CombinedDeviceValueName)) return InstallMode.SfxMfx;

        return InstallMode.SfxEfx;
    }

    // ----------------------------------------------------------------------------------
    // Per-device actions
    // ----------------------------------------------------------------------------------

    /// <summary>
    /// Installs the EAPO pre-mix and post-mix APOs on one endpoint. Mirrors
    /// <c>DeviceAPOInfo::install</c>: backs up the original FX-slot GUIDs into Child APOs, writes
    /// EAPO's GUIDs into the slots for the chosen install mode, and force-deletes the
    /// "disable enhancements" value so the engine actually runs the chain. Capture endpoints get
    /// only the pre-mix (Windows doesn't run post-mix APOs on capture).
    /// Calling Install on an already-installed device re-runs the chain (re-enables enhancements,
    /// refreshes the backup) - same behavior as the EAPO dialog's "reinstall" path.
    /// </summary>
    public static void Install(string deviceGuid, bool isCapture, InstallMode? mode = null)
    {
        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

        DeviceAPOInfo? existing = Probe(deviceGuid, isCapture);
        InstallMode resolvedMode = mode ?? existing?.CurrentInstallMode ?? InstallMode.SfxEfx;

        bool installPreMix = true;
        // Capture endpoints have no post-mix concept - EAPO's defaults set installPostMix=!input
        // and the per-mode branches further gate writes on !input. We collapse that here.
        bool installPostMix = !isCapture;

        // Create the Child APOs root + per-device backup key. CreateSubKey is idempotent and
        // returns the existing key when present.
        using RegistryKey appRoot = hklm.CreateSubKey(AppRegSubKey, writable: true)
                                    ?? throw new InvalidOperationException($"Cannot open {AppRegSubKey}");
        using RegistryKey childApos = hklm.CreateSubKey(ChildApoSubKey, writable: true)
                                      ?? throw new InvalidOperationException($"Cannot open {ChildApoSubKey}");
        using RegistryKey childDevice = hklm.CreateSubKey($@"{ChildApoSubKey}\{deviceGuid}", writable: true)
                                        ?? throw new InvalidOperationException($"Cannot open Child APOs\\{deviceGuid}");
        string deviceSubKey = $@"{(isCapture ? MMDevicesCaptureSubKey : MMDevicesRenderSubKey)}\{deviceGuid}";
        string fxPropsSubKey = $@"{deviceSubKey}\FxProperties";

        bool fxExistedBefore;
        using (RegistryKey? probe = hklm.OpenSubKey(fxPropsSubKey, writable: false))
            fxExistedBefore = probe != null;

        // Realtek-style drivers ship FxProperties owned by SYSTEM with admins denied write;
        // straight CreateSubKey throws UnauthorizedAccessException there. Existing keys go
        // through the take-ownership fallback (same as Uninstall); new keys still use plain
        // CreateSubKey, which only fails if the parent device key itself is locked.
        using RegistryKey fxProps = (fxExistedBefore
                                        ? OpenWritableWithFallback(hklm, fxPropsSubKey)
                                        : hklm.CreateSubKey(fxPropsSubKey, writable: true))
                                    ?? throw new InvalidOperationException($"Cannot open {fxPropsSubKey}");

        if (!fxExistedBefore)
        {
            // First-ever enhancement on this endpoint. Tag the new key with EAPO's title so
            // mmsys.cpl shows the right label, and seed every backup slot as NOKEY so a later
            // uninstall deletes the synthetic key wholesale.
            fxProps.SetValue(FxTitleValueName, "Equalizer APO", RegistryValueKind.String);
            for (int i = 0; i < FxSlotCount; i++)
                childDevice.SetValue(FxSlotValueNames[i], APOGUID_NOKEY, RegistryValueKind.String);
        }
        else
        {
            // FxProperties pre-existed. Snapshot every slot - real GUIDs become the restore
            // value; missing values become NOVALUE - so uninstall can replay the original
            // chain without ambiguity.
            for (int i = 0; i < FxSlotCount; i++)
            {
                string? existingSlot = fxProps.GetValue(FxSlotValueNames[i]) as string;
                childDevice.SetValue(FxSlotValueNames[i], existingSlot ?? APOGUID_NOVALUE, RegistryValueKind.String);
            }
        }

        // PreMixChild / PostMixChild are the GUIDs the EAPO engine should chain into when
        // useOriginalAPO* is true. We default to empty (no upstream APO) here; surfacing the
        // toggle would require reading the original slot GUIDs back, which the EAPO dialog
        // does in InstallState - skipped for the basic toggle.
        childDevice.SetValue(PreMixChildGuidValueName, "", RegistryValueKind.String);
        childDevice.SetValue(PostMixChildGuidValueName, "", RegistryValueKind.String);
        childDevice.SetValue(AllowSilentBufferValueName, "false", RegistryValueKind.String);
        // autoAdjust defaults to true (EAPO behavior). 'true' means we DON'T write the
        // DisableAutomaticAdjustment value; only set it on autoAdjust=false. Clean up a stale
        // value if one survived from a previous install.
        if (childDevice.GetValue(DisableAutoAdjustValueName) != null)
            childDevice.DeleteValue(DisableAutoAdjustValueName, throwOnMissingValue: false);
        childDevice.SetValue(VersionValueName, InstallVersion, RegistryValueKind.String);

        string preGuid = FormatGuid(PreMixGuid);
        string postGuid = FormatGuid(PostMixGuid);

        switch (resolvedMode)
        {
            case InstallMode.LfxGfx:
                if (installPreMix)
                    fxProps.SetValue(FxSlotValueNames[LFX], preGuid, RegistryValueKind.String);
                if (installPostMix)
                    fxProps.SetValue(FxSlotValueNames[GFX], postGuid, RegistryValueKind.String);
                DeleteValueIfPresent(fxProps, FxSlotValueNames[SFX]);
                DeleteValueIfPresent(fxProps, FxSlotValueNames[MFX]);
                DeleteValueIfPresent(fxProps, FxSlotValueNames[EFX]);
                break;

            case InstallMode.SfxMfx:
                DeleteValueIfPresent(fxProps, FxSlotValueNames[LFX]);
                DeleteValueIfPresent(fxProps, FxSlotValueNames[GFX]);
                if (installPreMix)
                {
                    fxProps.SetValue(FxSlotValueNames[SFX], preGuid, RegistryValueKind.String);
                    if (!HasValue(fxProps, SfxProcessingModesValueName))
                        fxProps.SetValue(SfxProcessingModesValueName, new[] { DefaultProcessingModeValue },
                            RegistryValueKind.MultiString);
                }

                if (installPostMix)
                {
                    fxProps.SetValue(FxSlotValueNames[MFX], postGuid, RegistryValueKind.String);
                    if (!HasValue(fxProps, MfxProcessingModesValueName))
                        fxProps.SetValue(MfxProcessingModesValueName, new[] { DefaultProcessingModeValue },
                            RegistryValueKind.MultiString);
                }

                // EFX deliberately left alone - SFX/MFX mode coexists with an EFX written by
                // the driver (matches EAPO's "don't change efx" comment).
                break;

            case InstallMode.SfxEfx:
                DeleteValueIfPresent(fxProps, FxSlotValueNames[LFX]);
                DeleteValueIfPresent(fxProps, FxSlotValueNames[GFX]);
                if (installPreMix)
                {
                    fxProps.SetValue(FxSlotValueNames[SFX], preGuid, RegistryValueKind.String);
                    if (!HasValue(fxProps, SfxProcessingModesValueName))
                        fxProps.SetValue(SfxProcessingModesValueName, new[] { DefaultProcessingModeValue },
                            RegistryValueKind.MultiString);
                }

                if (installPostMix)
                {
                    fxProps.SetValue(FxSlotValueNames[EFX], postGuid, RegistryValueKind.String);
                    if (!HasValue(fxProps, EfxProcessingModesValueName))
                        fxProps.SetValue(EfxProcessingModesValueName, new[] { DefaultProcessingModeValue },
                            RegistryValueKind.MultiString);
                }

                // MFX deliberately left alone.
                break;
        }

        // Force-enable enhancements. A stale 1 in this value makes the engine bypass every
        // APO, so the install would visibly "succeed" but produce no sound effect at all.
        DeleteValueIfPresent(fxProps, DisableEnhancementsValueName);
    }

    /// <summary>
    /// Removes the EAPO APOs from one endpoint and restores whatever original APO chain was there
    /// before install. Mirrors <c>DeviceAPOInfo::uninstall</c>: if the device had no FxProperties
    /// key before install, deletes the synthetic key wholesale; otherwise replays the per-slot
    /// backup from Child APOs. Cleans up the Child APOs key and the parent if it ends up empty.
    /// </summary>
    public static void Uninstall(string deviceGuid, bool isCapture)
    {
        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

        string deviceSubKey = $@"{(isCapture ? MMDevicesCaptureSubKey : MMDevicesRenderSubKey)}\{deviceGuid}";
        string fxPropsSubKey = $@"{deviceSubKey}\FxProperties";
        string childDeviceSubKey = $@"{ChildApoSubKey}\{deviceGuid}";

        // Read the per-slot backup from Child APOs before tearing anything down.
        string[] originalApoGuids = new string[FxSlotCount];
        for (int i = 0; i < FxSlotCount; i++) originalApoGuids[i] = "";

        bool childBackupFound = false;
        using (RegistryKey? childDevice = hklm.OpenSubKey(childDeviceSubKey, writable: false))
        {
            if (childDevice != null)
            {
                childBackupFound = true;
                for (int i = 0; i < FxSlotCount; i++)
                {
                    if (childDevice.GetValue(FxSlotValueNames[i]) is string s)
                        originalApoGuids[i] = s;
                }
            }
        }

        TADNLog.Log(
            $"EqualizerAPOInstaller.Uninstall({deviceGuid}): childBackup={childBackupFound} slots=[{string.Join(",", originalApoGuids)}]");

        if (originalApoGuids[0] == APOGUID_NOKEY)
        {
            // Synthetic FxProperties key - delete it whole. Tolerate "not there" so re-uninstall
            // is idempotent. Falls back to take-ownership on access denied (Realtek-style locked
            // device key).
            using RegistryKey? deviceKey = OpenWritableWithFallback(hklm, deviceSubKey);
            if (deviceKey == null)
                TADNLog.Log(
                    $"EqualizerAPOInstaller.Uninstall({deviceGuid}): could not open {deviceSubKey} for write (key missing or access denied)");
            else
            {
                try
                {
                    deviceKey.DeleteSubKeyTree("FxProperties", throwOnMissingSubKey: false);
                    TADNLog.Log($"EqualizerAPOInstaller.Uninstall({deviceGuid}): deleted synthetic FxProperties key");
                }
                catch (ArgumentException)
                {
                    /* already gone */
                }
            }
        }
        else
        {
            // Replay each slot's original state. Real GUID -> write back; NOVALUE -> ensure
            // value is gone. "" -> Child APOs had no entry for this slot (most commonly:
            // backup was missing or partial). Fall back to clearing any EAPO-GUID still
            // sitting in the slot - otherwise the chain stays attached after Uninstall claims
            // success. EAPO's own dialog never hits this path because it always has the load()
            // state in memory; we re-read from the registry, so we have to defend against an
            // absent / stale Child APOs.
            using RegistryKey? fxProps = OpenWritableWithFallback(hklm, fxPropsSubKey);
            if (fxProps == null)
                TADNLog.Log(
                    $"EqualizerAPOInstaller.Uninstall({deviceGuid}): could not open {fxPropsSubKey} for write (key missing or access denied)");
            else
            {
                string preGuid = FormatGuid(PreMixGuid);
                string postGuid = FormatGuid(PostMixGuid);
                for (int i = 0; i < FxSlotCount; i++)
                {
                    string slot = originalApoGuids[i];
                    if (slot == APOGUID_NOVALUE)
                        DeleteValueIfPresent(fxProps, FxSlotValueNames[i]);
                    else if (slot.Length > 0)
                        fxProps.SetValue(FxSlotValueNames[i], slot, RegistryValueKind.String);
                    else
                    {
                        // No backup entry for this slot. Clear any EAPO GUID still living
                        // here so the chain actually goes away.
                        if (fxProps.GetValue(FxSlotValueNames[i]) is string current
                            && (string.Equals(current, preGuid, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(current, postGuid, StringComparison.OrdinalIgnoreCase)))
                        {
                            fxProps.DeleteValue(FxSlotValueNames[i], throwOnMissingValue: false);
                            TADNLog.Log(
                                $"EqualizerAPOInstaller.Uninstall({deviceGuid}): cleared orphaned EAPO GUID from slot {i} (no Child APOs backup)");
                        }
                    }
                }

                TADNLog.Log($"EqualizerAPOInstaller.Uninstall({deviceGuid}): replayed slots from backup");
            }
        }

        // Wipe the per-device backup. The parent (Child APOs) and the legacy single-value entry
        // get cleaned up below; tolerate races where another caller already removed them.
        using (RegistryKey? childParent = hklm.OpenSubKey(ChildApoSubKey, writable: true))
        {
            if (childParent != null)
            {
                try { childParent.DeleteValue(deviceGuid, throwOnMissingValue: false); }
                catch { }

                try { childParent.DeleteSubKeyTree(deviceGuid, throwOnMissingSubKey: false); }
                catch (ArgumentException)
                {
                    /* already gone */
                }

                int subKeyCount = childParent.SubKeyCount;
                int valueCount = childParent.ValueCount;
                if (subKeyCount == 0 && valueCount == 0)
                {
                    using RegistryKey? appRoot = hklm.OpenSubKey(AppRegSubKey, writable: true);
                    try { appRoot?.DeleteSubKeyTree("Child APOs", throwOnMissingSubKey: false); }
                    catch (ArgumentException)
                    {
                        /* already gone */
                    }
                }
            }
        }
    }

    /// <summary>
    /// Uninstall, re-probe (to refresh the original-chain backup against the now-untouched
    /// device), then install again. Used to re-enable enhancements when they were turned off
    /// after install, or to upgrade an old EAPO install to the current InstallVersion. Matches
    /// EAPO's <c>DeviceAPOInfo::reinstall</c>.
    /// </summary>
    public static void Reinstall(string deviceGuid, bool isCapture)
    {
        Uninstall(deviceGuid, isCapture);
        Install(deviceGuid, isCapture);
    }

    // ----------------------------------------------------------------------------------
    // Small helpers
    // ----------------------------------------------------------------------------------

    // Formats a GUID the way EAPO does: '{XXXX-...-XXXX}' uppercase braces preserved. .NET's
    // "B" format produces lowercase by default - the registry comparison happens
    // case-insensitively in EAPO (CLSIDFromString) but we keep braces so direct string compares
    // with registry slot values match without normalization.
    private static string FormatGuid(Guid guid) => guid.ToString("B");

    private static bool KeyExists(RegistryKey root, string subKey)
    {
        using RegistryKey? k = root.OpenSubKey(subKey, writable: false);
        return k != null;
    }

    private static bool HasValue(RegistryKey key, string valueName)
        => key.GetValue(valueName) != null;

    private static void DeleteValueIfPresent(RegistryKey key, string valueName)
    {
        if (key.GetValue(valueName) != null)
            key.DeleteValue(valueName, throwOnMissingValue: false);
    }

    // Equivalent of EAPO's RegistryHelper::isWindowsVersionAtLeast. Win10/11 both report major
    // 10, so the 6.3 gate is trivially satisfied; the check exists for future-proofing.
    private static bool IsWindowsVersionAtLeast(int major, int minor)
    {
        Version v = Environment.OSVersion.Version;
        if (v.Major != major) return v.Major > major;
        return v.Minor >= minor;
    }

    // ----------------------------------------------------------------------------------
    // Take-ownership + make-writable fallback (port of EAPO RegistryHelper::takeOwnership +
    // makeWritable). Realtek and a few other audio drivers ship FxProperties keys owned by
    // SYSTEM with admins denied write - even an elevated process gets ACCESS_DENIED on a
    // straight RegOpenKeyEx for KEY_SET_VALUE. The fix is the standard "enable
    // SeTakeOwnershipPrivilege, set owner = BUILTIN\Administrators, then add FullControl DACL"
    // dance. Same approach mmsys.cpl uses internally and what EAPO falls back to in install().
    // ----------------------------------------------------------------------------------

    /// <summary>
    /// Attempts to make <paramref name="fullSubKeyPath"/> writable by the current process: enable
    /// SeTakeOwnershipPrivilege, set owner to BUILTIN\Administrators, then add a FullControl
    /// DACL entry for that group. Best-effort - returns silently on failure (caller will see the
    /// next OpenSubKey throw and log the still-denied state).
    /// </summary>
    private static void TryGrantWriteAccess(string fullSubKeyPath)
    {
        if (!TryEnableTakeOwnershipPrivilege())
        {
            TADNLog.Log(
                $"EqualizerAPOInstaller.TryGrantWriteAccess({fullSubKeyPath}): SeTakeOwnership privilege unavailable - process needs admin");
            return;
        }

        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        SecurityIdentifier admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);

        // Phase 1: take ownership. Open with WRITE_OWNER + ReadPermissions so we can both read
        // the existing security descriptor and set a new owner. The privilege we just enabled
        // bypasses the existing owner check so this works even when the current owner is SYSTEM.
        try
        {
            using RegistryKey? ownerKey = hklm.OpenSubKey(fullSubKeyPath,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.TakeOwnership | RegistryRights.ReadPermissions);
            if (ownerKey == null)
            {
                TADNLog.Log(
                    $"EqualizerAPOInstaller.TryGrantWriteAccess({fullSubKeyPath}): could not open with TakeOwnership");
                return;
            }

            RegistrySecurity ownerSec = ownerKey.GetAccessControl(AccessControlSections.Owner);
            ownerSec.SetOwner(admins);
            ownerKey.SetAccessControl(ownerSec);
        }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"EqualizerAPOInstaller.TryGrantWriteAccess({fullSubKeyPath}): take-ownership failed - {ex.Message}");
            return;
        }

        // Phase 2: now that admins owns it, modify the DACL to grant FullControl. ChangePermissions
        // is implied by ownership but we ask for it explicitly so OpenSubKey rejects up front if
        // the audio service races us and reclaims ownership between phases.
        try
        {
            using RegistryKey? daclKey = hklm.OpenSubKey(fullSubKeyPath,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.ChangePermissions | RegistryRights.ReadPermissions);
            if (daclKey == null)
            {
                TADNLog.Log(
                    $"EqualizerAPOInstaller.TryGrantWriteAccess({fullSubKeyPath}): could not open with ChangePermissions after ownership");
                return;
            }

            RegistrySecurity dacl = daclKey.GetAccessControl(AccessControlSections.Access);
            dacl.AddAccessRule(new RegistryAccessRule(
                admins,
                RegistryRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            daclKey.SetAccessControl(dacl);
            TADNLog.Log($"EqualizerAPOInstaller.TryGrantWriteAccess({fullSubKeyPath}): admins granted FullControl");
        }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"EqualizerAPOInstaller.TryGrantWriteAccess({fullSubKeyPath}): DACL grant failed - {ex.Message}");
        }
    }

    /// <summary>
    /// Opens <paramref name="fullSubKeyPath"/> writable, falling back to <see cref="TryGrantWriteAccess"/>
    /// + retry on SecurityException. Returns null only when the key truly doesn't exist or the
    /// fallback couldn't grant access - logged in either case.
    /// </summary>
    private static RegistryKey? OpenWritableWithFallback(RegistryKey baseKey, string fullSubKeyPath)
    {
        try
        {
            RegistryKey? key = baseKey.OpenSubKey(fullSubKeyPath, writable: true);
            if (key != null) return key;
        }
        catch (SecurityException)
        {
            TADNLog.Log(
                $"EqualizerAPOInstaller.OpenWritableWithFallback({fullSubKeyPath}): access denied, attempting take-ownership fallback");
            TryGrantWriteAccess(fullSubKeyPath);
            try { return baseKey.OpenSubKey(fullSubKeyPath, writable: true); }
            catch (Exception ex)
            {
                TADNLog.Log(
                    $"EqualizerAPOInstaller.OpenWritableWithFallback({fullSubKeyPath}): retry after fallback failed - {ex.Message}");
                return null;
            }
        }
        catch (UnauthorizedAccessException)
        {
            TADNLog.Log(
                $"EqualizerAPOInstaller.OpenWritableWithFallback({fullSubKeyPath}): access denied, attempting take-ownership fallback");
            TryGrantWriteAccess(fullSubKeyPath);
            try { return baseKey.OpenSubKey(fullSubKeyPath, writable: true); }
            catch (Exception ex)
            {
                TADNLog.Log(
                    $"EqualizerAPOInstaller.OpenWritableWithFallback({fullSubKeyPath}): retry after fallback failed - {ex.Message}");
                return null;
            }
        }

        return null;
    }

    private static bool TryEnableTakeOwnershipPrivilege()
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!Advapi32.OpenProcessToken(Process.GetCurrentProcess().Handle,
                    Advapi32.TOKEN_ADJUST_PRIVILEGES | Advapi32.TOKEN_QUERY, out token))
                return false;
            if (!Advapi32.LookupPrivilegeValueW(null, Advapi32.SE_TAKE_OWNERSHIP_NAME, out long luid))
                return false;
            Advapi32.TOKEN_PRIVILEGES tp = new()
            {
                PrivilegeCount = 1, Luid = luid, Attributes = Advapi32.SE_PRIVILEGE_ENABLED,
            };
            if (!Advapi32.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                return false;
            // AdjustTokenPrivileges returns true even when not all privileges were assigned.
            // GetLastError == ERROR_NOT_ALL_ASSIGNED (1300) is the "you weren't admin" case.
            return Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            if (token != IntPtr.Zero) Kernel32.CloseHandle(token);
        }
    }
}

/// <summary>
/// Snapshot of one endpoint's EAPO install state. Returned by <see cref="EqualizerAPOInstaller.Probe"/>.
/// All fields are immutable - a fresh Probe is required after any Install / Uninstall.
/// </summary>
internal sealed class DeviceAPOInfo
{
    public required string DeviceGuid { get; init; }
    public required bool IsCapture { get; init; }
    public required bool IsInstalled { get; init; }
    public required bool EnhancementsDisabled { get; init; }
    public required InstallMode CurrentInstallMode { get; init; }

    public required string Version { get; init; }

    // Per-slot backup of whatever lived in FxProperties before EAPO. Five entries indexed by
    // LFX/GFX/SFX/MFX/EFX. Values are either '!KEY' (FxProperties didn't exist), '!VALUE' (slot
    // had no value), '' (never touched), or a GUID string.
    public required string[] OriginalApoGuids { get; init; }
}

/// <summary>
/// FX-slot pairing the EAPO chain installs into. Selected automatically by Probe based on the
/// driver-published chain and the Windows version; can be overridden when calling Install.
/// </summary>
internal enum InstallMode
{
    LfxGfx = 0, // pre-Win8.1 driver shim; also used when the driver only published LFX/GFX
    SfxMfx = 1, // Win11 combined Bluetooth endpoints where EFX won't load
    SfxEfx = 2, // default Win8.1+ pairing for normal endpoints
}
