using System.Diagnostics;
using System.Runtime.InteropServices;
using BrightnessTrayAppDotNET.Interop.NightLight;

namespace BrightnessTrayAppDotNET.Tests.NightLight;

/// <summary>
/// Verbose, single-threaded clone of <see cref="NightLightSettingsHandler"/> for direct
/// experimentation. Differences from production:
///
/// 1. All <c>WPFLog.Log</c> calls are replaced with <see cref="ConsoleLog"/> writes plus
///    extra trace points at every step of init / SetValue / cleanup, so the entire
///    SettingsHandlers_Display vtable pipeline shows up in the console.
/// 2. The async throttler / latest-pending-wins scheduler is gone. The driver calls
///    <see cref="DriveSetValueBracket"/> synchronously - we want to see exactly what
///    one CDataSetting::SetValue call returns, not what survives a 100ms cooldown.
/// 3. The registry-hammer payload from <c>RunSetStrengthAsync</c> is deliberately not
///    invoked. Production currently bypasses the SettingsHandler vtable in favour of
///    direct registry writes (see comments in NightLightSettingsHandler.RunSetStrengthAsync);
///    this tester is the place to verify whether the bypassed path still functions.
/// 4. Reads (<see cref="GetStrengthFromRegistry"/>) still go through the registry, since
///    that's the source of truth for the current observable kelvin.
/// </summary>
internal static class NightLightSettingsHandlerTester
{
    private const string DllPath = @"C:\Windows\System32\SettingsHandlers_Display.dll";
    private const string SliderSettingId = "SystemSettings_Display_BlueLight_ColorTemperature";

    // Symbol names for the PDB fallback path.
    private const string SymInitialize  = "BlueLightSingleton::Initialize";
    private const string SymSInstance   = "BlueLightSingleton::s_instance";

    // Hardcoded fast-path table copied from NightLightSettingsHandler.
    // Add a new entry whenever the developer's machine moves to a verified Windows build.
    private static readonly Dictionary<string, (int InitializeRva, int SInstanceRva)> KnownRvas = new()
    {
        // ["10.0.26100.8117"] = (0x26564, 0x68D50), // temporarily disabled to exercise PDB resolver
    };

    private static int _initializeRva;
    private static int _sInstanceRva;

    // WinRT/COM vtable slots. Stable across versions because they're part of the interface contract,
    // unlike the .text RVAs which shift on every binary rebuild.
    private const int SetValueSlot       = 14; // CDataSetting::SetValue
    private const int CreateUInt32Slot   = 11; // IPropertyValueStatics::CreateUInt32
    private const int CreateBooleanSlot  = 17; // IPropertyValueStatics::CreateBoolean
    private const int ReleaseSlot        = 2;  // IUnknown::Release

    // {629bdbc8-d932-4ff4-96b9-8d96c5c1e858} - IPropertyValueStatics
    private static readonly Guid IIdPropertyValueStatics = new("629bdbc8-d932-4ff4-96b9-8d96c5c1e858");

    private static readonly Lock _gate = new();
    private static bool _initAttempted;
    private static bool _supported;

    private static IntPtr _hModule;
    private static IntPtr _settingItem;     // ISettingItem* for the kelvin slider
    private static IntPtr _pvFactory;       // IPropertyValueStatics*
    private static IntPtr _hValueProp;      // HSTRING("Value")
    private static IntPtr _hIsDraggingProp; // HSTRING("IsDragging")

    /// <summary>
    /// Forces init and reports whether the SettingsHandler backend is usable on this machine.
    /// Idempotent.
    /// </summary>
    public static bool IsSupported()
    {
        EnsureInit();
        return _supported;
    }

    /// <summary>Strength 0-100, read straight from the CloudStore SETTINGS blob.</summary>
    public static int GetStrengthFromRegistry()
    {
        int s = NightLightRegistry.GetStrength();
        ConsoleLog.Trace($"GetStrengthFromRegistry -> {s}%");
        return s;
    }

    /// <summary>Read enabled flag from the CloudStore STATE blob.</summary>
    public static bool IsEnabledFromRegistry()
    {
        bool e = NightLightRegistry.IsEnabled();
        ConsoleLog.Trace($"IsEnabledFromRegistry -> {e}");
        return e;
    }

    /// <summary>
    /// Drives one full slider bracket through the SettingsHandlers_Display vtable:
    ///   SetValue("Value", N) -> SetValue("IsDragging", true) -> SetValue("IsDragging", false).
    /// This is the path the Windows Settings UI emits while the user drags the kelvin slider.
    /// Each leg's HRESULT is logged. Returns true only if all three succeed.
    /// </summary>
    public static bool DriveSetValueBracket(int percent)
    {
        if (!IsSupported())
        {
            ConsoleLog.Error("DriveSetValueBracket: backend not supported, skipping.");
            return false;
        }

        int clamped = Math.Clamp(percent, 0, 100);
        ConsoleLog.Section($"DriveSetValueBracket({percent}) (clamped={clamped})");

        bool stepValue   = SetValueCore(clamped);
        bool stepDragOn  = SetIsDraggingCore(true);
        bool stepDragOff = SetIsDraggingCore(false);

        bool all = stepValue && stepDragOn && stepDragOff;
        if (all) ConsoleLog.Ok($"Bracket complete for {clamped}% (Value+DragOn+DragOff all S_OK)");
        else
        {
            ConsoleLog.Warn(
                $"Bracket partial for {clamped}%: Value={stepValue}, DragOn={stepDragOn}, DragOff={stepDragOff}");
        }

        return all;
    }

    /// <summary>
    /// Calls <see cref="DriveSetValueBracket"/> in a tight loop, mirroring production's
    /// ApplyRepeatCount=10 / ApplyDwellMs=25 hammer. Useful for testing whether the broker's
    /// SHTaskPool tag-258 dedup is still swallowing rapid-fire saves on this build.
    /// </summary>
    public static bool DriveSetValueHammer(int percent, int repeatCount, int dwellMs)
    {
        ConsoleLog.Section($"DriveSetValueHammer({percent}, repeat={repeatCount}, dwell={dwellMs}ms)");
        bool overall = true;
        for (int i = 0; i < repeatCount; i++)
        {
            ConsoleLog.Trace($"hammer iter {i + 1}/{repeatCount}");
            bool ok = DriveSetValueBracket(percent);
            overall &= ok;
            if (i == repeatCount - 1) break;
            Thread.Sleep(dwellMs);
        }
        return overall;
    }

    /// <summary>Drives a single SetValue("Value", N), no IsDragging bracket. Diagnostic.</summary>
    public static bool SetValueOnly(int percent)
    {
        if (!IsSupported())
        {
            ConsoleLog.Error("SetValueOnly: backend not supported, skipping.");
            return false;
        }
        int clamped = Math.Clamp(percent, 0, 100);
        ConsoleLog.Section($"SetValueOnly({percent}) (clamped={clamped})");
        return SetValueCore(clamped);
    }

    /// <summary>Drives a standalone SetValue("IsDragging", b). Diagnostic.</summary>
    public static bool SetIsDragging(bool dragging)
    {
        if (!IsSupported())
        {
            ConsoleLog.Error("SetIsDragging: backend not supported, skipping.");
            return false;
        }
        ConsoleLog.Section($"SetIsDragging({dragging})");
        return SetIsDraggingCore(dragging);
    }

    /// <summary>
    /// Final teardown - releases the cached factory + setting-item pointers and the
    /// long-lived "Value"/"IsDragging" HSTRINGs. Safe to call on a non-initialised handler.
    /// </summary>
    public static void Dispose()
    {
        ConsoleLog.Section("Dispose");
        Cleanup();
        ConsoleLog.Ok("Dispose complete.");
    }

    // -- Core SetValue dispatchers ---------------------------------------

    private static bool SetValueCore(int percent)
    {
        ConsoleLog.Trace($"SetValueCore: enter, percent={percent}");
        try
        {
            IntPtr factoryVtbl = Marshal.ReadIntPtr(_pvFactory);
            IntPtr createU32Fn = Marshal.ReadIntPtr(factoryVtbl, CreateUInt32Slot * IntPtr.Size);
            ConsoleLog.Trace(
                $"  factoryVtbl=0x{factoryVtbl.ToInt64():X16}, "
                + $"CreateUInt32 fn=0x{createU32Fn.ToInt64():X16}");

            CreateUInt32Del createU32 = (CreateUInt32Del)Marshal.GetDelegateForFunctionPointer(
                createU32Fn, typeof(CreateUInt32Del));

            int hr = createU32(_pvFactory, (uint)percent, out IntPtr propValue);
            ConsoleLog.Trace(
                $"  CreateUInt32({percent}) -> hr=0x{hr:X8}, propValue=0x{propValue.ToInt64():X16}");
            if (hr < 0 || propValue == IntPtr.Zero)
            {
                ConsoleLog.Error($"SetValueCore: CreateUInt32 failed hr=0x{hr:X8}");
                return false;
            }

            try
            {
                int setHr = InvokeSetValue(_hValueProp, propValue);
                ConsoleLog.Trace($"  SetValue('Value', uint32) -> hr=0x{setHr:X8}");
                if (setHr >= 0) ConsoleLog.Ok($"SetValueCore({percent}) OK");
                else            ConsoleLog.Error($"SetValueCore({percent}) failed hr=0x{setHr:X8}");
                return setHr >= 0;
            }
            finally { ReleasePropValue(propValue); }
        }
        catch (Exception ex)
        {
            ConsoleLog.Error($"SetValueCore threw: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool SetIsDraggingCore(bool dragging)
    {
        ConsoleLog.Trace($"SetIsDraggingCore: enter, dragging={dragging}");
        try
        {
            IntPtr factoryVtbl = Marshal.ReadIntPtr(_pvFactory);
            IntPtr createBoolFn = Marshal.ReadIntPtr(factoryVtbl, CreateBooleanSlot * IntPtr.Size);
            ConsoleLog.Trace(
                $"  factoryVtbl=0x{factoryVtbl.ToInt64():X16}, "
                + $"CreateBoolean fn=0x{createBoolFn.ToInt64():X16}");

            CreateBooleanDel createBool = (CreateBooleanDel)Marshal.GetDelegateForFunctionPointer(
                createBoolFn, typeof(CreateBooleanDel));

            int hr = createBool(_pvFactory, (byte)(dragging ? 1 : 0), out IntPtr propValue);
            ConsoleLog.Trace(
                $"  CreateBoolean({dragging}) -> hr=0x{hr:X8}, propValue=0x{propValue.ToInt64():X16}");
            if (hr < 0 || propValue == IntPtr.Zero)
            {
                ConsoleLog.Error($"SetIsDraggingCore: CreateBoolean failed hr=0x{hr:X8}");
                return false;
            }

            try
            {
                int setHr = InvokeSetValue(_hIsDraggingProp, propValue);
                ConsoleLog.Trace($"  SetValue('IsDragging', bool) -> hr=0x{setHr:X8}");
                if (setHr >= 0) ConsoleLog.Ok($"SetIsDraggingCore({dragging}) OK");
                else            ConsoleLog.Error($"SetIsDraggingCore({dragging}) failed hr=0x{setHr:X8}");
                return setHr >= 0;
            }
            finally { ReleasePropValue(propValue); }
        }
        catch (Exception ex)
        {
            ConsoleLog.Error($"SetIsDraggingCore threw: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static int InvokeSetValue(IntPtr propName, IntPtr propValue)
    {
        IntPtr itemVtbl = Marshal.ReadIntPtr(_settingItem);
        IntPtr setValueFn = Marshal.ReadIntPtr(itemVtbl, SetValueSlot * IntPtr.Size);
        SetValueDel setValue = (SetValueDel)Marshal.GetDelegateForFunctionPointer(
            setValueFn, typeof(SetValueDel));
        return setValue(_settingItem, propName, propValue);
    }

    private static void ReleasePropValue(IntPtr propValue)
    {
        IntPtr pvVtbl = Marshal.ReadIntPtr(propValue);
        ReleaseDel release = (ReleaseDel)Marshal.GetDelegateForFunctionPointer(
            Marshal.ReadIntPtr(pvVtbl, ReleaseSlot * IntPtr.Size),
            typeof(ReleaseDel));
        release(propValue);
    }

    // -- Init ------------------------------------------------------------

    private static void EnsureInit()
    {
        lock (_gate)
        {
            if (_initAttempted) return;
            _initAttempted = true;

            ConsoleLog.Section("EnsureInit");
            ConsoleLog.Info($"DllPath = {DllPath}");

            if (!File.Exists(DllPath))
            {
                ConsoleLog.Error($"DLL not found at {DllPath} - backend unsupported.");
                return;
            }

            Exception? initError = null;
            Thread thread = new(() =>
            {
                try { InitOnMtaThread(); }
                catch (Exception ex) { initError = ex; }
            })
            {
                IsBackground = true,
                Name = "NightLightSettingsHandlerTester-Init",
            };
            thread.SetApartmentState(ApartmentState.MTA);
            ConsoleLog.Trace($"Spawning MTA init thread '{thread.Name}'...");
            thread.Start();
            thread.Join();

            if (initError != null)
            {
                ConsoleLog.Error($"Init failed: {initError.GetType().Name}: {initError.Message}");
                Cleanup();
                return;
            }

            _supported = true;
            ConsoleLog.Ok("Init complete - SettingsHandler backend is supported.");
        }
    }

    private static void InitOnMtaThread()
    {
        ConsoleLog.Trace($"InitOnMtaThread: apartment={Thread.CurrentThread.GetApartmentState()}");

        int hrCo = CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED
        ConsoleLog.Trace($"  CoInitializeEx -> hr=0x{hrCo:X8}");

        int hrRo = RoInitialize(0); // RO_INIT_MULTITHREADED
        ConsoleLog.Trace($"  RoInitialize  -> hr=0x{hrRo:X8}");

        _hModule = LoadLibraryW(DllPath);
        if (_hModule == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"LoadLibrary {DllPath} failed, err=0x{err:X8}");
        }
        ConsoleLog.Info($"LoadLibraryW({Path.GetFileName(DllPath)}) = 0x{_hModule.ToInt64():X16}");

        ResolveCriticalRvas();
        ConsoleLog.Info(
            $"RVAs resolved: BlueLightSingleton::Initialize = 0x{_initializeRva:X}, "
            + $"::s_instance = 0x{_sInstanceRva:X}");

        // Force-init the BlueLightSingleton.
        // Without this, its inner settings (+296) and state (+272) pointers stay null
        // and BlueLightSingleton::SetTargetColorTemperature silently no-ops at its early null-check,
        // with the entire SetValue chain completing as S_OK and zero side-effect.
        IntPtr singleton = nint.Add(_hModule, _sInstanceRva);
        IntPtr initFn = nint.Add(_hModule, _initializeRva);
        ConsoleLog.Trace(
            $"  singleton addr=0x{singleton.ToInt64():X16}, "
            + $"Initialize fn=0x{initFn.ToInt64():X16}");

        InitDel init = (InitDel)Marshal.GetDelegateForFunctionPointer(initFn, typeof(InitDel));
        ConsoleLog.Trace("  Calling BlueLightSingleton::Initialize...");
        init(singleton);
        ConsoleLog.Trace("  Initialize returned.");

        IntPtr stateInner    = Marshal.ReadIntPtr(singleton, 272);
        IntPtr settingsInner = Marshal.ReadIntPtr(singleton, 296);
        ConsoleLog.Info(
            $"Singleton inner ptrs: state=0x{stateInner.ToInt64():X16}, "
            + $"settings=0x{settingsInner.ToInt64():X16}");
        if (stateInner == IntPtr.Zero || settingsInner == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "BlueLightSingleton::Initialize did not populate inner ptrs"
                + $" (state=0x{stateInner.ToInt64():X16}, settings=0x{settingsInner.ToInt64():X16})");
        }

        IntPtr getSettingProc = GetProcAddress(_hModule, "GetSetting");
        if (getSettingProc == IntPtr.Zero) throw new InvalidOperationException("GetSetting export not found");
        ConsoleLog.Trace($"  GetSetting export = 0x{getSettingProc.ToInt64():X16}");

        IntPtr hSettingId = IntPtr.Zero;
        try
        {
            int hr = WindowsCreateString(SliderSettingId, SliderSettingId.Length, out hSettingId);
            if (hr < 0) throw new InvalidOperationException($"WindowsCreateString failed hr=0x{hr:X8}");
            ConsoleLog.Trace($"  SettingId HSTRING = 0x{hSettingId.ToInt64():X16}");

            GetSettingDel getSetting = (GetSettingDel)Marshal.GetDelegateForFunctionPointer(
                getSettingProc, typeof(GetSettingDel));
            hr = getSetting(hSettingId, out _settingItem);
            ConsoleLog.Trace(
                $"  GetSetting('{SliderSettingId}') -> hr=0x{hr:X8}, "
                + $"settingItem=0x{_settingItem.ToInt64():X16}");
            if (hr < 0 || _settingItem == IntPtr.Zero)
                throw new InvalidOperationException($"GetSetting('{SliderSettingId}') failed hr=0x{hr:X8}");
        }
        finally
        {
            if (hSettingId != IntPtr.Zero) WindowsDeleteString(hSettingId);
        }

        IntPtr hPvCls = IntPtr.Zero;
        try
        {
            const string PvClassName = "Windows.Foundation.PropertyValue";
            int hr = WindowsCreateString(PvClassName, PvClassName.Length, out hPvCls);
            if (hr < 0) throw new InvalidOperationException("WindowsCreateString PropertyValue failed");

            Guid iid = IIdPropertyValueStatics;
            hr = RoGetActivationFactory(hPvCls, ref iid, out _pvFactory);
            ConsoleLog.Trace(
                $"  RoGetActivationFactory(IPropertyValueStatics) -> hr=0x{hr:X8}, "
                + $"factory=0x{_pvFactory.ToInt64():X16}");
            if (hr < 0 || _pvFactory == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"RoGetActivationFactory(IPropertyValueStatics) failed hr=0x{hr:X8}");
            }
        }
        finally
        {
            if (hPvCls != IntPtr.Zero) WindowsDeleteString(hPvCls);
        }

        const string ValuePropName = "Value";
        int hr2 = WindowsCreateString(ValuePropName, ValuePropName.Length, out _hValueProp);
        if (hr2 < 0) throw new InvalidOperationException("WindowsCreateString 'Value' failed");
        ConsoleLog.Trace($"  HSTRING('Value')      = 0x{_hValueProp.ToInt64():X16}");

        const string IsDraggingPropName = "IsDragging";
        int hr3 = WindowsCreateString(IsDraggingPropName, IsDraggingPropName.Length, out _hIsDraggingProp);
        if (hr3 < 0) throw new InvalidOperationException("WindowsCreateString 'IsDragging' failed");
        ConsoleLog.Trace($"  HSTRING('IsDragging') = 0x{_hIsDraggingProp.ToInt64():X16}");
    }

    private static void ResolveCriticalRvas()
    {
        string version;
        try { version = NormalizeFileVersion(FileVersionInfo.GetVersionInfo(DllPath).FileVersion); }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"GetVersionInfo failed: {ex.Message}");
        }
        ConsoleLog.Info($"SettingsHandlers_Display version = '{version}'");

        if (KnownRvas.TryGetValue(version, out (int InitializeRva, int SInstanceRva) hardcoded))
        {
            _initializeRva = hardcoded.InitializeRva;
            _sInstanceRva  = hardcoded.SInstanceRva;
            ConsoleLog.Ok(
                $"RVA fast-path hit for '{version}': "
                + $"Initialize=0x{_initializeRva:X}, s_instance=0x{_sInstanceRva:X}");
            return;
        }

        ConsoleLog.Warn(
            $"No hardcoded RVAs for '{version}' - falling back to PDBSymbolResolver "
            + "(may download PDB from msdl.microsoft.com).");

        if (!PDBSymbolResolver.TryResolveSymbols(
                DllPath, _hModule, [SymInitialize, SymSInstance], out Dictionary<string, int> rvas))
        {
            throw new InvalidOperationException(
                $"Could not resolve required symbols for SettingsHandlers_Display v{version}. " +
                "PDB download may have failed (no network / symbol server outage), or the binary's " +
                "symbol table doesn't contain the expected names.");
        }
        _initializeRva = rvas[SymInitialize];
        _sInstanceRva  = rvas[SymSInstance];
        ConsoleLog.Ok(
            $"PDB resolver succeeded: Initialize=0x{_initializeRva:X}, "
            + $"s_instance=0x{_sInstanceRva:X}");
    }

    private static string NormalizeFileVersion(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        int space = raw.IndexOf(' ');
        return space < 0 ? raw : raw[..space];
    }

    private static void Cleanup()
    {
        // Best-effort - we're recovering from a failed init or a process teardown.
        if (_hValueProp != IntPtr.Zero)
        {
            WindowsDeleteString(_hValueProp);
            _hValueProp = IntPtr.Zero;
            ConsoleLog.Trace("  released HSTRING('Value')");
        }
        if (_hIsDraggingProp != IntPtr.Zero)
        {
            WindowsDeleteString(_hIsDraggingProp);
            _hIsDraggingProp = IntPtr.Zero;
            ConsoleLog.Trace("  released HSTRING('IsDragging')");
        }
        if (_pvFactory != IntPtr.Zero)
        {
            try
            {
                IntPtr vtbl = Marshal.ReadIntPtr(_pvFactory);
                ReleaseDel release = (ReleaseDel)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(vtbl, ReleaseSlot * IntPtr.Size), typeof(ReleaseDel));
                release(_pvFactory);
                ConsoleLog.Trace("  released IPropertyValueStatics");
            }
            catch (Exception ex)
            {
                ConsoleLog.Warn($"  IPropertyValueStatics release threw (cleanup path): {ex.Message}");
            }
            _pvFactory = IntPtr.Zero;
        }
        if (_settingItem != IntPtr.Zero)
        {
            try
            {
                IntPtr vtbl = Marshal.ReadIntPtr(_settingItem);
                ReleaseDel release = (ReleaseDel)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(vtbl, ReleaseSlot * IntPtr.Size), typeof(ReleaseDel));
                release(_settingItem);
                ConsoleLog.Trace("  released ISettingItem");
            }
            catch (Exception ex)
            {
                ConsoleLog.Warn($"  ISettingItem release threw (cleanup path): {ex.Message}");
            }
            _settingItem = IntPtr.Zero;
        }
        // Deliberately don't FreeLibrary the module
        // - other parts of the runtime may have pinned it via WinRT/COM activation,
        // and unloading prematurely tends to AV in DllMain.
    }

    // -- P/Invoke ---------------------------------------------------------

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW([MarshalAs(UnmanagedType.LPWStr)] string fileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string procName);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
    private static extern int RoInitialize(uint initType);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", SetLastError = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string source, int length, out IntPtr h);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
    private static extern int WindowsDeleteString(IntPtr h);

    private delegate void InitDel(IntPtr thisPtr);
    private delegate int GetSettingDel(IntPtr hSettingId, out IntPtr settingItem);
    private delegate int CreateUInt32Del(IntPtr factoryThis, uint value, out IntPtr propValue);
    private delegate int CreateBooleanDel(IntPtr factoryThis, byte value, out IntPtr propValue);
    private delegate int SetValueDel(IntPtr thisPtr, IntPtr propName, IntPtr propValue);
    private delegate uint ReleaseDel(IntPtr thisPtr);
}
