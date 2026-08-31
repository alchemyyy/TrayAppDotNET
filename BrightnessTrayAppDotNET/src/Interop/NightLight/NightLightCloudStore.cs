using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using TrayAppDotNETCommon.Serialization;

namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Drives the night-light kelvin slider by calling <c>BlueLightSingleton::SetTargetColorTemperature</c> via
/// RVA in <c>SettingsHandlers_Display.dll</c>. Production loads this type only inside the recyclable Night Light
/// helper process so process exit reclaims CDP allocations retained by the Windows implementation. That path:
/// <list type="number">
///   <item>writes the new kelvin into the singleton's <c>cloud_store_data&lt;Settings&gt;</c>;</item>
///   <item>calls <c>BlueLightSingleton::SaveSettingsAsync</c>, which queues
///         <c>SHTaskPoolQueueTask(3, 258, ...)</c>;</item>
///   <item>SHTaskPool runs the task on its own thread, where
///         <c>wil::cloud_store::call_save&lt;Settings&gt;</c> -&gt; <c>ICloudStore::Save</c> succeeds;</item>
///   <item>CloudStore bumps its version counter, the broker fires
///         <c>BlueLightReductionManager::OnBlueLightReductionSettingsChange</c>, the live filter is reapplied
///         with the new kelvin (no flicker, no toggle).</item>
/// </list>
///
/// History notes:
/// <list type="bullet">
///   <item>Calling <c>ICloudStore::Save</c> directly from our throttler thread returns <c>0x80070490</c>
///         (<c>CloudStorePartitionSet::GetPartitionInfo</c> NOT_FOUND), even with the singleton's borrowed
///         CloudStore and Microsoft's exact arg layout. SHTaskPool's worker thread evidently has a
///         process/COM context that we don't reproduce by ourselves. Routing through
///         <c>SaveSettingsAsync</c> sidesteps that.</item>
///   <item><c>SHTaskPool</c> dedups tasks tagged <c>258</c>, but each queued task captures the singleton's
///         current kelvin at queue time. Rapid slider drags collapse into a smaller number of actual saves;
///         the LAST issued value still lands because its kelvin is what the queued task observes. Verified
///         via the rapid-fire/throttler tests in <c>tests/NightLightTester/CloudStoreTester.cs</c>.</item>
/// </list>
/// </summary>
internal static class NightLightCloudStore
{
    private const uint ROInitMultithreaded = 1;
    private const int BackendShutdownTimeoutMs = 10_000;
    private const int StateReadbackPollIntervalMs = 25;
    private const string BackendThreadName = "NightLightCloudStore-MTA";

    // The three bracket calls (kelvin, IsDragging-on, IsDragging-off) must each reach the broker as a distinct
    // save+notification - if SHTaskPool tag-258 dedup collapses them, the broker only sees the final IsDragging=0
    // state and never observes the preview-toggle edge that queues ColorTemperatureControl's fb3daf apply lambda.
    // Without that lambda, SetTargetTemperature gates on `!inflight` and observably hangs in the wedged-byte-at-+36
    // state on this build.
    //
    // Each save reaches disk via ICloudStore::Save, which writes the SETTINGS registry blob. We register a
    // one-shot RegNotifyChangeKeyValue on that key before each call, then wait on the event handle to know the
    // worker actually drained before issuing the next call. Empirically saves land at +30-50ms; the timeout is
    // the wedged-system ceiling.
    private const string SettingsBlobKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\"
        + @"default$windows.data.bluelightreduction.settings\"
        + "windows.data.bluelightreduction.settings";

    private const string StateBlobKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\"
        + @"default$windows.data.bluelightreduction.bluelightreductionstate\"
        + "windows.data.bluelightreduction.bluelightreductionstate";

    private const string CallerName = "NightLightCloudStore";

    // SettingsHandlers_Display.dll is pinned for process lifetime - we deliberately don't FreeLibrary because
    // BlueLightSingleton's Initialize wires up CloudStore subscriptions and a Geolocator status callback that
    // would crash on unload.
    private const string SettingsHandlersDllPath = @"C:\Windows\System32\SettingsHandlers_Display.dll";
    private const string SymBlueLightSingletonInitialize = "BlueLightSingleton::Initialize";
    private const string SymBlueLightSingletonSInstance = "BlueLightSingleton::s_instance";

    private const string SymBlueLightSingletonSetTargetColorTemperature =
        "BlueLightSingleton::SetTargetColorTemperature";

    private const string SymBlueLightSingletonSetPreviewColorTemperatureChanges =
        "BlueLightSingleton::SetPreviewColorTemperatureChanges";

    private const string SymBlueLightSingletonSetBlueLightActive =
        "BlueLightSingleton::SetBlueLightActive";

    // Verified RVAs for known builds. Falls through to PDBSymbolResolver on miss; the resolver caches its
    // result so the symbol-server hit is a one-time cost per Windows update.
    //
    // Defaults are mirrored to
    // %LocalAppData%\TrayAppDotNET\BrightnessTrayAppDotNET\nightlight\nightlight_known_rvas.xml on first run so
    // users can add entries for new Windows builds without recompiling. If the file matches the
    // canonical default XML byte-for-byte we keep the in-memory defaults; if it has been hand-edited we discard
    // defaults and load the file. See LoadKnownRVAs for the full reconciliation logic.
    private const string KnownRVAsFileName = "nightlight_known_rvas.xml";

    private static readonly string KnownRVAsFilePath =
        Path.Combine(PDBSymbolResolver.NightlightDir, KnownRVAsFileName);

    private static readonly Dictionary<string,
            (int InitializeRVA, int SInstanceRVA, int SetTargetColorTemperatureRVA, int SetPreviewRVA,
            int SetBlueLightActiveRVA)>
        KnownSettingsHandlersRVAs = LoadKnownRVAs();

    private static readonly Lock _gate = new();
    private static readonly Lock _streamGate = new();
    private static volatile bool _supported;
    private static bool _shutdownRequested;
    private static BlockingCollection<BackendRequest>? _backendRequests;
    private static Thread? _backendThread;
    private static Task<bool>? _initializationTask;

    private static Timer? _streamReleaseTimer;
    private static int _pendingStreamingKelvin;
    private static bool _hasPendingStreamingKelvin;
    private static bool _streamDrainScheduled;
    private static bool _streamReleaseRequested;
    private static bool _streamPreviewActive;
    private static long _lastStreamingRequestTick;
    private static TaskCompletionSource<bool>? _streamDrainCompletionSource;

    private static IntPtr _hSettingsHandlersDll;
    private static IntPtr _singleton; // SettingsHandlersDll + SInstanceRva
    private static IntPtr _setTargetColorTemperatureFn;
    private static IntPtr _setPreviewColorTemperatureChangesFn;
    private static IntPtr _setBlueLightActiveFn;
    private static SetTargetColorTemperatureDel? _setTargetColorTemperature;
    private static SetPreviewColorTemperatureChangesDel? _setPreviewColorTemperatureChanges;
    private static SetBlueLightActiveDel? _setBlueLightActive;

    public static bool IsSupported()
    {
        EnsureInit();
        return _supported;
    }

    /// <summary>
    /// Accepts a hot-path slider update without waiting for CloudStore persistence. Updates are latest-wins on
    /// the MTA thread. Preview mode remains active across a burst and is released after input goes quiet.
    /// </summary>
    public static bool TryQueueStreamingKelvin(int percent)
    {
        if (!NightLightRegistry.IsEnabled()) return false;
        if (!IsSupported()) return false;

        int kelvin = NightLightKelvin.PercentToKelvin(percent);
        bool shouldSchedule;
        Timer releaseTimer;
        lock (_streamGate)
        {
            _pendingStreamingKelvin = kelvin;
            _hasPendingStreamingKelvin = true;
            _streamReleaseRequested = false;
            _lastStreamingRequestTick = Environment.TickCount64;
            shouldSchedule = !_streamDrainScheduled;
            if (shouldSchedule)
                _streamDrainScheduled = true;

            _streamReleaseTimer ??= new Timer(
                OnStreamReleaseTimerFired,
                state: null,
                Timeout.Infinite,
                Timeout.Infinite);
            releaseTimer = _streamReleaseTimer;
        }

        releaseTimer.Change(TimeConstants.NightLightStreamingPreviewReleaseDelayMs, Timeout.Infinite);
        return !shouldSchedule || ScheduleStreamingDrain();
    }

    /// <summary>
    /// Flushes the latest streaming value and emits preview-off on the MTA thread. This waits only for native
    /// calls to be issued; it does not wait for registry notifications or broker propagation.
    /// </summary>
    public static Task<bool> DrainStreamingAsync()
    {
        if (!IsSupported()) return Task.FromResult(false);

        bool shouldSchedule;
        Timer? releaseTimer;
        TaskCompletionSource<bool> completionSource;
        lock (_streamGate)
        {
            _streamReleaseRequested = true;
            _streamDrainCompletionSource ??=
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            completionSource = _streamDrainCompletionSource;
            shouldSchedule = !_streamDrainScheduled;
            if (shouldSchedule)
                _streamDrainScheduled = true;
            releaseTimer = _streamReleaseTimer;
        }

        try { releaseTimer?.Change(Timeout.Infinite, Timeout.Infinite); }
        catch (ObjectDisposedException)
        {
            // Shutdown already owns the timer
        }

        if (shouldSchedule && !ScheduleStreamingDrain())
            CompleteStreamingDrain(false);

        return completionSource.Task;
    }

    private static bool ScheduleStreamingDrain()
    {
        Task<bool> drainTask = QueueBackendRequest(DrainStreamingOnMTAThread);
        if (drainTask.IsCompletedSuccessfully && !drainTask.GetAwaiter().GetResult())
        {
            CompleteStreamingDrain(false);
            return false;
        }

        _ = ObserveStreamingDrainAsync(drainTask);
        return true;
    }

    private static async Task ObserveStreamingDrainAsync(Task<bool> drainTask)
    {
        try
        {
            bool succeeded = await drainTask.ConfigureAwait(false);
            if (!succeeded)
                CompleteStreamingDrain(false);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightCloudStore streaming drain failed: {ex.Message}");
            CompleteStreamingDrain(false);
        }
    }

    private static bool DrainStreamingOnMTAThread()
    {
        SetTargetColorTemperatureDel? setTargetColorTemperature = _setTargetColorTemperature;
        SetPreviewColorTemperatureChangesDel? setPreviewColorTemperatureChanges =
            _setPreviewColorTemperatureChanges;
        if (setTargetColorTemperature == null || setPreviewColorTemperatureChanges == null)
        {
            CompleteStreamingDrain(false);
            return false;
        }

        try
        {
            while (true)
            {
                int kelvin = 0;
                bool hasKelvin;
                bool releasePreview;
                lock (_streamGate)
                {
                    hasKelvin = _hasPendingStreamingKelvin;
                    if (hasKelvin)
                    {
                        kelvin = _pendingStreamingKelvin;
                        _hasPendingStreamingKelvin = false;
                        releasePreview = false;
                    }
                    else
                    {
                        releasePreview = _streamReleaseRequested;
                        _streamReleaseRequested = false;
                        if (!releasePreview)
                        {
                            _streamDrainScheduled = false;
                            break;
                        }
                    }
                }

                if (hasKelvin)
                {
                    // The main process can turn Night Light off after a SET command was acknowledged but before
                    // this MTA request executes. Drop that stale value at the last possible boundary.
                    if (!NightLightRegistry.IsEnabled())
                        continue;

                    setTargetColorTemperature(_singleton, kelvin);
                    if (!_streamPreviewActive)
                    {
                        setPreviewColorTemperatureChanges(_singleton, isDragging: 1);
                        _streamPreviewActive = true;
                    }

                    continue;
                }

                if (releasePreview && _streamPreviewActive)
                {
                    setPreviewColorTemperatureChanges(_singleton, isDragging: 0);
                    _streamPreviewActive = false;
                }
            }

            CompleteStreamingDrain(true);
            return true;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightCloudStore streaming native call failed: {ex.Message}");
            CompleteStreamingDrain(false);
            return false;
        }
    }

    private static void OnStreamReleaseTimerFired(object? state)
    {
        bool shouldSchedule = false;
        int remainingDelayMs = 0;
        lock (_streamGate)
        {
            long elapsedMs = Environment.TickCount64 - _lastStreamingRequestTick;
            if (elapsedMs < TimeConstants.NightLightStreamingPreviewReleaseDelayMs)
                remainingDelayMs = TimeConstants.NightLightStreamingPreviewReleaseDelayMs - (int)elapsedMs;
            else
            {
                _streamReleaseRequested = true;
                shouldSchedule = !_streamDrainScheduled;
                if (shouldSchedule)
                    _streamDrainScheduled = true;
            }
        }

        if (remainingDelayMs > 0)
        {
            try { _streamReleaseTimer?.Change(remainingDelayMs, Timeout.Infinite); }
            catch (ObjectDisposedException)
            {
                // Shutdown already owns the timer
            }

            return;
        }

        if (shouldSchedule)
            _ = ScheduleStreamingDrain();
    }

    private static void CompleteStreamingDrain(bool success)
    {
        TaskCompletionSource<bool>? completionSource;
        lock (_streamGate)
        {
            if (success && _streamDrainScheduled) return;

            if (!success)
                _streamDrainScheduled = false;
            completionSource = _streamDrainCompletionSource;
            _streamDrainCompletionSource = null;
        }

        completionSource?.TrySetResult(success);
    }

    /// <summary>
    /// Sets the kelvin slider strength (0-100). Returns a task that completes (with true) once all three bracket
    /// steps have been dispatched and their saves have reached disk, or (with false) if init isn't ready or a
    /// step throws.
    ///
    /// Each bracket step queues its own <c>SaveSettingsAsync</c> -&gt; SHTaskPool task. Between steps we register
    /// a one-shot <c>RegNotifyChangeKeyValue</c> on the SETTINGS registry blob and asynchronously wait on the
    /// resulting event handle (via <c>ThreadPool.RegisterWaitForSingleObject</c>), so the next call only fires
    /// after the prior save's worker has actually drained to disk. The broker then observes the IsDragging
    /// false-&gt;true edge as a real state change, queues <c>ColorTemperatureControl::fb3daf</c>, and applies via
    /// <c>ApplyTemperatureChangeToMonitorsImmediate</c> unconditionally - bypassing the <c>+36 inflight</c> gate
    /// that wedges the <c>SetTargetTemperature</c> apply path on this build.
    ///
    /// After one-time initialization, callers enqueue the bracket and return immediately. Every native call runs
    /// on the same permanent MTA thread that initialized the singleton; registry notifications still complete on
    /// the thread pool. Steady-state bracket time is ~100-200ms (saves typically land at +30-50ms each). Worst
    /// case per step is bounded by <see cref="TimeConstants.NightLightSaveNotifyTimeoutMs"/>.
    /// </summary>
    public static Task<bool> SaveSettingsKelvinAsync(int percent)
    {
        if (!NightLightRegistry.IsEnabled()) return Task.FromResult(false);
        if (!IsSupported()) return Task.FromResult(false);

        int kelvin = NightLightKelvin.PercentToKelvin(percent);
        Thread? backendThread;
        lock (_gate)
            backendThread = _backendThread;

        return ReferenceEquals(Thread.CurrentThread, backendThread)
            ? Task.FromResult(SaveSettingsKelvinOnMTAThread(kelvin))
            : QueueBackendRequest(() => SaveSettingsKelvinOnMTAThread(kelvin));
    }

    private static bool SaveSettingsKelvinOnMTAThread(int kelvin)
    {
        SetTargetColorTemperatureDel? setTargetColorTemperature = _setTargetColorTemperature;
        SetPreviewColorTemperatureChangesDel? setPreviewColorTemperatureChanges =
            _setPreviewColorTemperatureChanges;
        if (setTargetColorTemperature == null || setPreviewColorTemperatureChanges == null)
            return false;

        try
        {
            // Each wait is completed before starting the next step so all native calls execute on this MTA thread
            AsyncUtils.IssueWithSaveNotifyAsync(
                    SettingsBlobKeyPath, () => setTargetColorTemperature(_singleton, kelvin),
                    TimeConstants.NightLightSaveNotifyTimeoutMs, TimeConstants.NightLightCloudStoreFallbackDwellMs,
                    CallerName)
                .GetAwaiter().GetResult();
            AsyncUtils.IssueWithSaveNotifyAsync(
                    SettingsBlobKeyPath, () => setPreviewColorTemperatureChanges(_singleton, isDragging: 1),
                    TimeConstants.NightLightSaveNotifyTimeoutMs, TimeConstants.NightLightCloudStoreFallbackDwellMs,
                    CallerName)
                .GetAwaiter().GetResult();
            AsyncUtils.IssueWithSaveNotifyAsync(
                    SettingsBlobKeyPath, () => setPreviewColorTemperatureChanges(_singleton, isDragging: 0),
                    TimeConstants.NightLightSaveNotifyTimeoutMs, TimeConstants.NightLightCloudStoreFallbackDwellMs,
                    CallerName)
                .GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"NightLightCloudStore.SaveSettingsKelvinOnMTAThread: bracket emission threw: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Synchronous wrapper around <see cref="SaveSettingsKelvinAsync"/>. Blocks the calling thread for the
    /// duration of the bracket. Retained for direct diagnostic test runners; production uses the streaming
    /// helper methods above.
    /// </summary>
    public static bool SaveSettingsKelvin(int percent) =>
        SaveSettingsKelvinAsync(percent).GetAwaiter().GetResult();

    /// <summary>
    /// Commits an active-state transition through <c>BlueLightSingleton::SetBlueLightActive</c>. Unlike a raw
    /// registry toggle, this path sets Windows' initialized field and creates the CloudStore state on profiles
    /// where Night Light has never been enabled. An optional strength is saved before enabling.
    /// </summary>
    public static async Task<bool> SetEnabledAsync(bool enabled, int? enableStrength = null)
    {
        if (!enabled && enableStrength.HasValue) return false;
        if (!IsSupported()) return false;

        Thread? backendThread;
        lock (_gate)
            backendThread = _backendThread;

        if (ReferenceEquals(Thread.CurrentThread, backendThread))
        {
            if (!DrainStreamingOnMTAThread()) return false;
            return SetEnabledOnMTAThread(enabled, enableStrength);
        }

        bool drained = await DrainStreamingAsync().ConfigureAwait(false);
        if (!drained) return false;

        return await QueueBackendRequest(() => SetEnabledOnMTAThread(enabled, enableStrength))
            .ConfigureAwait(false);
    }

    private static bool SetEnabledOnMTAThread(bool enabled, int? enableStrength)
    {
        SetBlueLightActiveDel? setBlueLightActive = _setBlueLightActive;
        if (setBlueLightActive == null) return false;

        if (enabled && enableStrength.HasValue)
        {
            int kelvin = NightLightKelvin.PercentToKelvin(enableStrength.Value);
            if (!SaveSettingsKelvinOnMTAThread(kelvin)) return false;
        }

        long startedAtTick = Environment.TickCount64;
        try
        {
            AsyncUtils.IssueWithSaveNotifyAsync(
                    StateBlobKeyPath,
                    () => setBlueLightActive(_singleton, enabled ? (byte)1 : (byte)0),
                    TimeConstants.NightLightSaveNotifyTimeoutMs,
                    TimeConstants.NightLightCloudStoreFallbackDwellMs,
                    CallerName)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightCloudStore.SetEnabledOnMTAThread: state save threw: {ex.Message}");
            return false;
        }

        while (Environment.TickCount64 - startedAtTick <= TimeConstants.NightLightSaveNotifyTimeoutMs)
        {
            NightLightStateStatus stateStatus = NightLightRegistry.GetStateStatus();
            if (stateStatus.IsInitialized && stateStatus.IsEnabled == enabled)
                return true;

            Thread.Sleep(StateReadbackPollIntervalMs);
        }

        NightLightStateStatus finalStatus = NightLightRegistry.GetStateStatus();
        return finalStatus.IsInitialized && finalStatus.IsEnabled == enabled;
    }

    /// <summary>
    /// Stops accepting work, drains the backend queue, and balances WinRT initialization on the backend thread.
    /// </summary>
    public static void Shutdown()
    {
        BlockingCollection<BackendRequest>? backendRequests;
        Thread? backendThread;
        Timer? streamReleaseTimer;

        lock (_streamGate)
        {
            streamReleaseTimer = _streamReleaseTimer;
            _streamReleaseTimer = null;
        }

        try { streamReleaseTimer?.Change(Timeout.Infinite, Timeout.Infinite); }
        catch (ObjectDisposedException)
        {
            // A concurrent shutdown path already disposed it
        }

        lock (_gate)
        {
            if (_shutdownRequested) return;

            _shutdownRequested = true;
            _supported = false;
            backendRequests = _backendRequests;
            backendThread = _backendThread;

            if (backendRequests is { IsAddingCompleted: false })
                backendRequests.CompleteAdding();
        }

        if (backendThread == null || ReferenceEquals(Thread.CurrentThread, backendThread))
        {
            streamReleaseTimer?.Dispose();
            CompleteStreamingDrain(false);
            return;
        }

        if (!backendThread.Join(BackendShutdownTimeoutMs))
        {
            TADNLog.Log(
                $"NightLightCloudStore.Shutdown: backend thread did not stop within " +
                $"{BackendShutdownTimeoutMs}ms");
            streamReleaseTimer?.Dispose();
            CompleteStreamingDrain(false);
            return;
        }

        lock (_gate)
        {
            if (ReferenceEquals(_backendThread, backendThread))
            {
                _backendThread = null;
                _backendRequests = null;
            }
        }

        backendRequests?.Dispose();
        streamReleaseTimer?.Dispose();
        CompleteStreamingDrain(false);
    }

    private static Task<bool> QueueBackendRequest(Func<bool> operation)
    {
        BackendRequest request = new(operation);

        lock (_gate)
        {
            BlockingCollection<BackendRequest>? backendRequests = _backendRequests;
            if (_shutdownRequested || !_supported || backendRequests == null ||
                backendRequests.IsAddingCompleted)
                return Task.FromResult(false);

            try
            {
                backendRequests.Add(request);
            }
            catch (InvalidOperationException)
            {
                return Task.FromResult(false);
            }
        }

        return request.CompletionTask;
    }

    private static void EnsureInit()
    {
        Task<bool>? initializationTask;

        lock (_gate)
        {
            if (_shutdownRequested) return;

            if (_initializationTask == null)
                StartBackendThreadLocked();

            initializationTask = _initializationTask;
        }

        if (initializationTask == null) return;
        _ = initializationTask.GetAwaiter().GetResult();
    }

    private static void StartBackendThreadLocked()
    {
        BlockingCollection<BackendRequest> backendRequests = [];
        TaskCompletionSource<bool> initializationCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread backendThread =
            new(() => BackendThreadMain(backendRequests, initializationCompletionSource))
            {
                IsBackground = true, Name = BackendThreadName
            };
        backendThread.SetApartmentState(ApartmentState.MTA);

        _backendRequests = backendRequests;
        _backendThread = backendThread;
        _initializationTask = initializationCompletionSource.Task;

        try
        {
            backendThread.Start();
        }
        catch (Exception ex)
        {
            _backendRequests = null;
            _backendThread = null;
            backendRequests.Dispose();
            initializationCompletionSource.TrySetResult(false);
            TADNLog.Log($"NightLightCloudStore: failed to start backend thread: {ex.Message}");
        }
    }

    private static void BackendThreadMain(
        BlockingCollection<BackendRequest> backendRequests,
        TaskCompletionSource<bool> initializationCompletionSource)
    {
        bool windowsRuntimeInitialized = false;

        try
        {
            int initializationResult = RoInitialize(ROInitMultithreaded);
            if (initializationResult < 0)
                Marshal.ThrowExceptionForHR(initializationResult);

            windowsRuntimeInitialized = true;
            InitializeNativeBackendOnMTAThread();

            _setTargetColorTemperature =
                Marshal.GetDelegateForFunctionPointer<SetTargetColorTemperatureDel>(
                    _setTargetColorTemperatureFn);
            _setPreviewColorTemperatureChanges =
                Marshal.GetDelegateForFunctionPointer<SetPreviewColorTemperatureChangesDel>(
                    _setPreviewColorTemperatureChangesFn);
            _setBlueLightActive =
                Marshal.GetDelegateForFunctionPointer<SetBlueLightActiveDel>(
                    _setBlueLightActiveFn);

            bool initializationAccepted;
            lock (_gate)
            {
                initializationAccepted = !_shutdownRequested;
                if (initializationAccepted)
                    _supported = true;
            }

            initializationCompletionSource.TrySetResult(initializationAccepted);
            if (!initializationAccepted) return;

            foreach (BackendRequest request in backendRequests.GetConsumingEnumerable())
                request.Execute();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightCloudStore backend thread failed: {ex.Message}");
        }
        finally
        {
            _supported = false;
            initializationCompletionSource.TrySetResult(false);
            FailPendingRequests(backendRequests);
            _setTargetColorTemperature = null;
            _setPreviewColorTemperatureChanges = null;
            _setBlueLightActive = null;

            if (windowsRuntimeInitialized)
                RoUninitialize();
        }
    }

    private static void FailPendingRequests(BlockingCollection<BackendRequest> backendRequests)
    {
        while (backendRequests.TryTake(out BackendRequest? request))
            request.Fail();
    }

    private static void InitializeNativeBackendOnMTAThread()
    {
        if (!File.Exists(SettingsHandlersDllPath))
            throw new InvalidOperationException($"'{SettingsHandlersDllPath}' missing");
        _hSettingsHandlersDll = LoadLibraryW(SettingsHandlersDllPath);
        if (_hSettingsHandlersDll == IntPtr.Zero)
        {
            int lastError = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"LoadLibrary failed err=0x{lastError:X8}");
        }

        string version;
        try
        {
            string raw = FileVersionInfo.GetVersionInfo(SettingsHandlersDllPath).FileVersion ?? "";
            int space = raw.IndexOf(' ');
            version = space < 0 ? raw : raw[..space];
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"GetVersionInfo failed: {ex.Message}");
        }

        int initializeRVA, sInstanceRVA, setTempRVA, setPreviewRVA, setActiveRVA;
        if (KnownSettingsHandlersRVAs.TryGetValue(
                version,
                out (int InitializeRVA, int SInstanceRVA, int SetTargetColorTemperatureRVA, int SetPreviewRVA,
                int SetBlueLightActiveRVA)
                hardcoded)
            && hardcoded.SetBlueLightActiveRVA > 0)
        {
            initializeRVA = hardcoded.InitializeRVA;
            sInstanceRVA = hardcoded.SInstanceRVA;
            setTempRVA = hardcoded.SetTargetColorTemperatureRVA;
            setPreviewRVA = hardcoded.SetPreviewRVA;
            setActiveRVA = hardcoded.SetBlueLightActiveRVA;
        }
        else
        {
            if (!PDBSymbolResolver.TryResolveSymbols(
                    SettingsHandlersDllPath,
                    _hSettingsHandlersDll,
                    [
                        SymBlueLightSingletonInitialize, SymBlueLightSingletonSInstance,
                        SymBlueLightSingletonSetTargetColorTemperature,
                        SymBlueLightSingletonSetPreviewColorTemperatureChanges,
                        SymBlueLightSingletonSetBlueLightActive
                    ],
                    out Dictionary<string, int> rvas))
            {
                throw new InvalidOperationException(
                    $"Could not resolve required symbols for SettingsHandlers_Display v{version}");
            }

            initializeRVA = rvas[SymBlueLightSingletonInitialize];
            sInstanceRVA = rvas[SymBlueLightSingletonSInstance];
            setTempRVA = rvas[SymBlueLightSingletonSetTargetColorTemperature];
            setPreviewRVA = rvas[SymBlueLightSingletonSetPreviewColorTemperatureChanges];
            setActiveRVA = rvas[SymBlueLightSingletonSetBlueLightActive];
        }

        _singleton = nint.Add(_hSettingsHandlersDll, sInstanceRVA);
        _setTargetColorTemperatureFn = nint.Add(_hSettingsHandlersDll, setTempRVA);
        _setPreviewColorTemperatureChangesFn = nint.Add(_hSettingsHandlersDll, setPreviewRVA);
        _setBlueLightActiveFn = nint.Add(_hSettingsHandlersDll, setActiveRVA);
        IntPtr initFn = nint.Add(_hSettingsHandlersDll, initializeRVA);

        try
        {
            InitDel init = Marshal.GetDelegateForFunctionPointer<InitDel>(initFn);
            init(_singleton);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"BlueLightSingleton::Initialize threw: {ex.Message}");
        }

        // Sanity-check every pointer required by SetBlueLightActive and SetTargetColorTemperature
        IntPtr stateInner = Marshal.ReadIntPtr(_singleton, ofs: 264);
        IntPtr stateWrapper = Marshal.ReadIntPtr(_singleton, ofs: 272);
        IntPtr settingsInner = Marshal.ReadIntPtr(_singleton, ofs: 296);
        if (stateInner == IntPtr.Zero || stateWrapper == IntPtr.Zero || settingsInner == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"BlueLightSingleton::Initialize did not populate inner ptrs " +
                $"(state=0x{stateInner.ToInt64():X16}, stateWrapper=0x{stateWrapper.ToInt64():X16}, " +
                $"settings=0x{settingsInner.ToInt64():X16})");
        }

        TADNLog.Log("NightLightCloudStore: BlueLight singleton initialized on permanent MTA thread");
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW([MarshalAs(UnmanagedType.LPWStr)] string fileName);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
    private static extern int RoInitialize(uint initType);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
    private static extern void RoUninitialize();

    private delegate void InitDel(IntPtr thisPtr);

    private delegate void SetTargetColorTemperatureDel(IntPtr thisPtr, int kelvin);

    private delegate void SetPreviewColorTemperatureChangesDel(IntPtr thisPtr, byte isDragging);

    private delegate void SetBlueLightActiveDel(IntPtr thisPtr, byte isActive);

    private sealed class BackendRequest(Func<bool> operation)
    {
        private readonly TaskCompletionSource<bool> _completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> CompletionTask => _completionSource.Task;

        public void Execute()
        {
            try
            {
                _completionSource.TrySetResult(operation());
            }
            catch (Exception ex)
            {
                TADNLog.Log($"NightLightCloudStore.BackendRequest: operation threw: {ex.Message}");
                _completionSource.TrySetResult(false);
            }
        }

        public void Fail() => _completionSource.TrySetResult(false);
    }

    // Canonical defaults: the only thing that ships with the binary. Empty today; add an entry here to seed
    // a new Windows build's RVAs into the on-disk file on first run.
    private static Dictionary<string,
            (int InitializeRVA, int SInstanceRVA, int SetTargetColorTemperatureRVA, int SetPreviewRVA,
            int SetBlueLightActiveRVA)>
        BuildDefaultKnownRVAs() => new()
    {
        // TODO: Key hardcoded RVAs by PDB signature as well as file version
        // ["10.0.26100.8117"] = (0x265C4, 0x68D80, 0x27F58, 0x27E90, 0x27D4C),
    };

    /// <summary>
    /// Reconciles the in-source defaults from <see cref="BuildDefaultKnownRVAs"/> with a user-editable XML
    /// mirror at <c>%LocalAppData%\TrayAppDotNET\BrightnessTrayAppDotNET\nightlight_known_rvas.xml</c>. First run writes the
    /// defaults; subsequent runs use byte-equality against the canonical default XML to decide
    /// whether the file is unmodified (keep defaults) or has been hand-edited (clear defaults, load file).
    /// Any IO/parse failure logs and falls back to in-memory defaults so init never blocks on filesystem
    /// mishaps.
    /// </summary>
    private static Dictionary<string,
            (int InitializeRVA, int SInstanceRVA, int SetTargetColorTemperatureRVA, int SetPreviewRVA,
            int SetBlueLightActiveRVA)>
        LoadKnownRVAs()
    {
        Dictionary<string,
                (int InitializeRVA, int SInstanceRVA, int SetTargetColorTemperatureRVA, int SetPreviewRVA,
                int SetBlueLightActiveRVA)>
            defaults = BuildDefaultKnownRVAs();

        byte[] defaultsBytes;
        try
        {
            // Ensure the parent dir exists - PDBSymbolResolver only creates AppDataDir, not the
            // nightlight subdir we hang our XML mirror off of.
            Directory.CreateDirectory(PDBSymbolResolver.NightlightDir);
            defaultsBytes = SerializeKnownRVAs(defaults);
        }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"NightLightCloudStore.LoadKnownRVAs: setup failed, using in-memory defaults: {ex.Message}");
            return defaults;
        }

        try
        {
            if (!File.Exists(KnownRVAsFilePath))
            {
                File.WriteAllBytes(KnownRVAsFilePath, defaultsBytes);
                return defaults;
            }

            byte[] onDisk = File.ReadAllBytes(KnownRVAsFilePath);
            return BytesEqual(onDisk, defaultsBytes) ? defaults : ParseKnownRVAs(onDisk);
        }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"NightLightCloudStore.LoadKnownRVAs: file IO/parse failed, using in-memory defaults: {ex.Message}");
            return defaults;
        }
    }

    private static byte[] SerializeKnownRVAs(
        Dictionary<string,
                (int InitializeRVA, int SInstanceRVA, int SetTargetColorTemperatureRVA, int SetPreviewRVA,
                int SetBlueLightActiveRVA)>
            dict)
    {
        NightLightKnownRVAsDocument document = new();
        foreach (KeyValuePair<string,
                         (int InitializeRVA, int SInstanceRVA, int SetTargetColorTemperatureRVA, int SetPreviewRVA,
                         int SetBlueLightActiveRVA)>
                     kvp in dict.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            document.Entries.Add(new NightLightKnownRVAEntry
            {
                Version = kvp.Key,
                Initialize = kvp.Value.InitializeRVA,
                SInstance = kvp.Value.SInstanceRVA,
                SetTargetColorTemperature = kvp.Value.SetTargetColorTemperatureRVA,
                SetPreview = kvp.Value.SetPreviewRVA,
                SetBlueLightActive = kvp.Value.SetBlueLightActiveRVA
            });
        }

        using MemoryStream stream = new();
        TrayXmlSerializer.Write(stream, document);
        return stream.ToArray();
    }

    private static Dictionary<string,
            (int InitializeRVA, int SInstanceRVA, int SetTargetColorTemperatureRVA, int SetPreviewRVA,
            int SetBlueLightActiveRVA)>
        ParseKnownRVAs(byte[] xmlBytes)
    {
        Dictionary<string,
                (int InitializeRVA, int SInstanceRVA, int SetTargetColorTemperatureRVA, int SetPreviewRVA,
                int SetBlueLightActiveRVA)>
            result = [];

        using MemoryStream stream = new(xmlBytes, writable: false);
        NightLightKnownRVAsDocument document = TrayXmlSerializer.Read<NightLightKnownRVAsDocument>(stream);

        foreach (NightLightKnownRVAEntry entry in document.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Version)) continue;
            result[entry.Version] = (
                entry.Initialize,
                entry.SInstance,
                entry.SetTargetColorTemperature,
                entry.SetPreview,
                entry.SetBlueLightActive);
        }

        return result;
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }
}

[XmlRoot("NightLightKnownRVAs")]
internal sealed class NightLightKnownRVAsDocument
{
    [XmlElement("Entry")]
    public List<NightLightKnownRVAEntry> Entries { get; set; } = [];
}

internal sealed class NightLightKnownRVAEntry
{
    [XmlAttribute]
    public string Version { get; set; } = string.Empty;

    [XmlAttribute]
    public int Initialize { get; set; }

    [XmlAttribute]
    public int SInstance { get; set; }

    [XmlAttribute]
    public int SetTargetColorTemperature { get; set; }

    [XmlAttribute]
    public int SetPreview { get; set; }

    [XmlAttribute]
    public int SetBlueLightActive { get; set; }
}
