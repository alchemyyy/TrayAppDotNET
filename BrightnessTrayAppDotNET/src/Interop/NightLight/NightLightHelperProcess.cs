using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;

namespace BrightnessTrayAppDotNET.Interop.NightLight;

/// <summary>
/// Main-process coordinator for the recyclable Night Light native backend.
/// </summary>
internal static class NightLightHelperClient
{
    private const string NoWatcherEnvironmentVariable = "TrayAppDotNET_NO_WATCHER";

    private static readonly Lock StateGate = new();
    private static readonly NightLightLatestStrengthQueue PendingStrength = new();
    private static readonly SemaphoreSlim PendingSignal = new(initialCount: 0, maxCount: 1);
    private static readonly CancellationTokenSource ShutdownTokenSource = new();
    private static readonly List<Task> RetirementTasks = [];

    private static readonly Lazy<Task<bool>> InitializationTask =
        new(InitializeAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private static NightLightHelperConnection? _activeHelper;
    private static Task<NightLightHelperConnection?>? _warmingHelperTask;
    private static Task? _pumpTask;
    private static Timer? _recycleQuietTimer;
    private static int _warmupRetryAfterOperationCount;
    private static bool _recycleRequested;
    private static bool _shutdownRequested;
    private static long _lastQueuedStrengthTick;

    internal static bool HasStartedInitialization => InitializationTask.IsValueCreated;

    /// <summary>
    /// Starts and primes one helper, then waits until the production IPC pump is blocked and ready for input.
    /// </summary>
    public static bool IsSupported()
    {
        lock (StateGate)
        {
            if (_shutdownRequested)
                return false;
        }

        try
        {
            return InitializationTask.Value.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightHelperClient.IsSupported: initialization failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stores the newest strength and returns immediately. True means the coordinator accepted the value;
    /// helper IPC and native preview streaming run entirely off the caller's thread.
    /// </summary>
    public static bool TryQueueSettingsKelvin(int percent)
    {
        // Keep this invariant here as well as at the provider: no future direct caller may initialize the native
        // helper merely because a stale curve or UI update attempted to write strength while Night Light is off.
        if (!NightLightRegistry.IsEnabled()) return false;
        if (!IsSupported()) return false;

        int clamped = Math.Clamp(percent, min: 0, max: 100);
        lock (StateGate)
        {
            if (_shutdownRequested) return false;
            PendingStrength.Store(clamped);
            _lastQueuedStrengthTick = Environment.TickCount64;
            ArmRecycleQuietTimerLocked();
        }

        SignalPendingWork();
        return true;
    }

    /// <summary>Discards a strength value that has not yet reached the helper process.</summary>
    public static void CancelPendingStrength() => PendingStrength.Clear();

    /// <summary>
    /// Applies an explicit active-state transition through SettingsHandlers_Display. A supplied enable strength
    /// is committed in the helper before the active transition, which also initializes fresh Windows profiles.
    /// </summary>
    public static bool SetEnabled(bool enabled, int? enableStrength)
    {
        if (!enabled) CancelPendingStrength();

        NightLightStateStatus stateStatus = NightLightRegistry.GetStateStatus();
        if (stateStatus.IsInitialized && stateStatus.IsEnabled == enabled) return true;
        if (!enabled && !stateStatus.IsInitialized) return true;

        if (!IsSupported()) return false;

        NightLightHelperConnection? helper = GetActiveHelper();
        if (helper == null) return false;

        int? clampedStrength = enableStrength.HasValue
            ? Math.Clamp(enableStrength.Value, min: 0, max: 100)
            : null;
        try
        {
            return helper.SetEnabledAsync(enabled, clampedStrength, ShutdownTokenSource.Token)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"NightLightHelperClient: helper PID {helper.ProcessID} active-state operation failed: "
                + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Stops queue processing and terminates every active, warming, or retiring helper.
    /// </summary>
    public static void Shutdown()
    {
        try
        {
            ShutdownAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightHelperClient.Shutdown failed: {ex.Message}");
        }
    }

    internal static bool ShouldStartWarmup(int completedOperations)
    {
        int warmupThreshold = Math.Max(
            val1: 1,
            Constants.NightLightHelperRecycleOperationCount -
            Constants.NightLightHelperWarmupLeadOperationCount);
        return completedOperations >= warmupThreshold;
    }

    internal static bool ShouldRecycle(int completedOperations) =>
        completedOperations >= Constants.NightLightHelperRecycleOperationCount;

    private static async Task<bool> InitializeAsync()
    {
        NightLightHelperConnection? helper =
            await StartHelperSafelyAsync(ShutdownTokenSource.Token).ConfigureAwait(false);
        if (helper == null) return false;

        bool accepted;
        lock (StateGate)
        {
            accepted = !_shutdownRequested;
            if (accepted)
                _activeHelper = helper;
        }

        if (!accepted)
        {
            await helper.StopAsync(false).ConfigureAwait(false);
            return false;
        }

        await PrimeInitialHelperAsync(helper, ShutdownTokenSource.Token).ConfigureAwait(false);

        TaskCompletionSource<bool> pumpReadySource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (StateGate)
        {
            accepted = !_shutdownRequested && _activeHelper != null;
            if (accepted) _pumpTask = Task.Run(() => PumpAsync(ShutdownTokenSource.Token, pumpReadySource));
        }

        if (!accepted) return false;

        bool pumpReady = await pumpReadySource.Task.ConfigureAwait(false);
        NightLightHelperConnection? activeHelper = GetActiveHelper();
        if (!pumpReady || activeHelper == null) return false;

        TADNLog.Log(
            $"NightLightHelperClient: active helper PID {activeHelper.ProcessID} primed and ready");
        return true;
    }

    private static async Task PrimeInitialHelperAsync(
        NightLightHelperConnection helper,
        CancellationToken cancellationToken)
    {
        try
        {
            bool pipePrimed = await helper.PingAsync(cancellationToken).ConfigureAwait(false);
            if (!pipePrimed)
            {
                TADNLog.Log(
                    $"NightLightHelperClient: helper PID {helper.ProcessID} rejected startup PING");
                return;
            }

            if (!NightLightRegistry.IsEnabled())
                return;

            int currentPercent = NightLightRegistry.GetStrength();
            bool processed = await ProcessPendingStrengthAsync(currentPercent, cancellationToken)
                .ConfigureAwait(false);
            NightLightHelperConnection? activeHelper = GetActiveHelper();
            bool drained = processed && activeHelper != null &&
                           await activeHelper.DrainAsync(cancellationToken).ConfigureAwait(false);
            if (!drained) TADNLog.Log("NightLightHelperClient: startup native streaming prime did not drain");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when app shutdown overlaps startup
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightHelperClient: startup prime failed: {ex.Message}");
        }
    }

    private static async Task PumpAsync(
        CancellationToken cancellationToken,
        TaskCompletionSource<bool> pumpReadySource)
    {
        bool readinessSignaled = false;
        try
        {
            while (true)
            {
                Task pendingWaitTask = PendingSignal.WaitAsync(cancellationToken);
                if (!readinessSignaled)
                {
                    readinessSignaled = true;
                    pumpReadySource.TrySetResult(true);
                }

                await pendingWaitTask.ConfigureAwait(false);

                while (true)
                {
                    if (PendingStrength.TryTake(out int percent))
                    {
                        bool keepProcessing = await ProcessPendingStrengthAsync(percent, cancellationToken)
                            .ConfigureAwait(false);
                        if (!keepProcessing) break;
                        continue;
                    }

                    NightLightHelperConnection? recycleHelper = TakeRecycleRequest();
                    if (recycleHelper == null) break;

                    _ = await RecycleAtQuietBoundaryAsync(recycleHelper, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during app shutdown
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightHelperClient.PumpAsync failed: {ex}");
        }
        finally
        {
            if (!readinessSignaled)
                pumpReadySource.TrySetResult(false);
        }
    }

    private static async Task<bool> ProcessPendingStrengthAsync(
        int percent,
        CancellationToken cancellationToken)
    {
        NightLightHelperConnection? helper = GetActiveHelper();
        if (helper == null)
        {
            RestorePendingStrength(percent);
            bool recovered =
                await RecoverActiveHelperAsync(failedHelper: null, cancellationToken).ConfigureAwait(false);
            if (!recovered)
            {
                await Task.Delay(TimeConstants.NightLightHelperRecoveryDelayMs, cancellationToken)
                    .ConfigureAwait(false);
            }

            return false;
        }

        bool operationSucceeded;
        try
        {
            operationSucceeded = await helper.SetStrengthAsync(percent, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"NightLightHelperClient: helper PID {helper.ProcessID} operation failed: " +
                ex.Message);
            RestorePendingStrength(percent);
            bool recovered = await RecoverActiveHelperAsync(helper, cancellationToken).ConfigureAwait(false);
            if (!recovered)
            {
                await Task.Delay(TimeConstants.NightLightHelperRecoveryDelayMs, cancellationToken)
                    .ConfigureAwait(false);
            }

            return false;
        }

        helper.CompletedOperationCount++;

        if (!operationSucceeded)
        {
            TADNLog.Log(
                $"NightLightHelperClient: helper PID {helper.ProcessID} rejected strength " +
                $"operation {helper.CompletedOperationCount}");
        }

        EnsureWarmReplacementStarted(helper);
        ArmRecycleQuietTimer();
        return true;
    }

    private static NightLightHelperConnection? GetActiveHelper()
    {
        lock (StateGate)
            return _activeHelper;
    }

    private static void RestorePendingStrength(int percent)
    {
        if (!PendingStrength.RestoreIfEmpty(percent)) return;

        SignalPendingWork();
    }

    private static void SignalPendingWork()
    {
        try { PendingSignal.Release(); }
        catch (SemaphoreFullException)
        {
            // One signal already covers the length-one pending queue
        }
    }

    private static void ArmRecycleQuietTimer()
    {
        lock (StateGate)
            ArmRecycleQuietTimerLocked();
    }

    private static void ArmRecycleQuietTimerLocked()
    {
        NightLightHelperConnection? activeHelper = _activeHelper;
        if (_shutdownRequested || activeHelper == null ||
            !ShouldRecycle(activeHelper.CompletedOperationCount) || _warmingHelperTask == null)
            return;

        _recycleRequested = false;
        _recycleQuietTimer ??= new Timer(
            OnRecycleQuietTimerFired,
            state: null,
            Timeout.Infinite,
            Timeout.Infinite);
        _recycleQuietTimer.Change(TimeConstants.NightLightHelperRecycleQuietDelayMs, Timeout.Infinite);
    }

    private static void OnRecycleQuietTimerFired(object? state)
    {
        lock (StateGate)
        {
            NightLightHelperConnection? activeHelper = _activeHelper;
            if (_shutdownRequested || activeHelper == null ||
                !ShouldRecycle(activeHelper.CompletedOperationCount))
                return;

            long elapsedMs = Environment.TickCount64 - _lastQueuedStrengthTick;
            if (elapsedMs < TimeConstants.NightLightHelperRecycleQuietDelayMs)
            {
                int remainingDelayMs =
                    TimeConstants.NightLightHelperRecycleQuietDelayMs - (int)elapsedMs;
                _recycleQuietTimer?.Change(remainingDelayMs, Timeout.Infinite);
                return;
            }

            if (_warmingHelperTask is not { IsCompleted: true })
            {
                _recycleQuietTimer?.Change(
                    TimeConstants.NightLightHelperRecoveryDelayMs,
                    Timeout.Infinite);
                return;
            }

            _recycleRequested = true;
        }

        SignalPendingWork();
    }

    private static NightLightHelperConnection? TakeRecycleRequest()
    {
        lock (StateGate)
        {
            if (!_recycleRequested) return null;

            _recycleRequested = false;
            return _activeHelper;
        }
    }

    private static void EnsureWarmReplacementStarted(NightLightHelperConnection activeHelper)
    {
        if (!ShouldStartWarmup(activeHelper.CompletedOperationCount)) return;

        lock (StateGate)
        {
            if (_shutdownRequested || !ReferenceEquals(_activeHelper, activeHelper) ||
                _warmingHelperTask != null ||
                activeHelper.CompletedOperationCount < _warmupRetryAfterOperationCount)
                return;

            _warmingHelperTask = StartHelperSafelyAsync(ShutdownTokenSource.Token);
        }

        TADNLog.Log(
            $"NightLightHelperClient: warming replacement before PID {activeHelper.ProcessID} reaches " +
            $"{Constants.NightLightHelperRecycleOperationCount} operations");
        ArmRecycleQuietTimer();
    }

    private static async Task<NightLightHelperConnection> RecycleAtQuietBoundaryAsync(
        NightLightHelperConnection activeHelper,
        CancellationToken cancellationToken)
    {
        if (!ShouldRecycle(activeHelper.CompletedOperationCount)) return activeHelper;

        EnsureWarmReplacementStarted(activeHelper);

        Task<NightLightHelperConnection?>? warmingTask;
        lock (StateGate)
        {
            if (_shutdownRequested || !ReferenceEquals(_activeHelper, activeHelper))
                return _activeHelper ?? activeHelper;

            warmingTask = _warmingHelperTask;
        }

        if (warmingTask is not { IsCompleted: true } ||
            !IsRecycleBoundaryStillQuiet(activeHelper))
            return activeHelper;

        NightLightHelperConnection? replacement = await warmingTask.ConfigureAwait(false);
        bool replacementAvailable;
        lock (StateGate)
        {
            if (ReferenceEquals(_warmingHelperTask, warmingTask))
                _warmingHelperTask = null;

            replacementAvailable = !_shutdownRequested && replacement != null &&
                                   ReferenceEquals(_activeHelper, activeHelper);
            if (!replacementAvailable && replacement == null && ReferenceEquals(_activeHelper, activeHelper))
            {
                _warmupRetryAfterOperationCount = activeHelper.CompletedOperationCount +
                                                  Constants.NightLightHelperWarmupLeadOperationCount;
            }
        }

        if (!replacementAvailable)
        {
            if (replacement != null)
                TrackRetirement(replacement.StopAsync(true));
            return GetActiveHelper() ?? activeHelper;
        }

        int? replayPercent = activeHelper.LastAcceptedPercent;
        try
        {
            bool drained = await activeHelper.DrainAsync(cancellationToken).ConfigureAwait(false);
            if (!drained)
            {
                TADNLog.Log(
                    $"NightLightHelperClient: helper PID {activeHelper.ProcessID} rejected recycle drain");
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log(
                $"NightLightHelperClient: helper PID {activeHelper.ProcessID} recycle drain failed: " +
                ex.Message);
        }

        bool swapped = false;
        lock (StateGate)
        {
            if (!_shutdownRequested && ReferenceEquals(_activeHelper, activeHelper))
            {
                _activeHelper = replacement;
                _warmupRetryAfterOperationCount = 0;
                _recycleRequested = false;
                try { _recycleQuietTimer?.Change(Timeout.Infinite, Timeout.Infinite); }
                catch (ObjectDisposedException) { }

                swapped = true;
            }
        }

        if (!swapped)
        {
            TrackRetirement(replacement!.StopAsync(true));
            return GetActiveHelper() ?? activeHelper;
        }

        NightLightHelperConnection replacementHelper = replacement!;

        // The old helper has already released preview mode. Activate the warm replacement before waiting for
        // process teardown so input that resumes at the quiet boundary is never held behind graceful exit.
        TrackRetirement(activeHelper.StopAsync(true));

        TADNLog.Log(
            $"NightLightHelperClient: recycled helper PID {activeHelper.ProcessID} after " +
            $"{activeHelper.CompletedOperationCount} operations; PID {replacementHelper.ProcessID} is active");

        if (replayPercent.HasValue && PendingStrength.RestoreIfEmpty(replayPercent.Value)) SignalPendingWork();

        return replacementHelper;
    }

    private static bool IsRecycleBoundaryStillQuiet(NightLightHelperConnection activeHelper)
    {
        lock (StateGate)
        {
            if (_shutdownRequested || !ReferenceEquals(_activeHelper, activeHelper)) return false;

            long elapsedMs = Environment.TickCount64 - _lastQueuedStrengthTick;
            if (elapsedMs >= TimeConstants.NightLightHelperRecycleQuietDelayMs) return true;

            ArmRecycleQuietTimerLocked();
            return false;
        }
    }

    private static async Task<bool> RecoverActiveHelperAsync(
        NightLightHelperConnection? failedHelper,
        CancellationToken cancellationToken)
    {
        Task<NightLightHelperConnection?>? warmingTask;
        lock (StateGate)
        {
            if (_shutdownRequested) return false;

            if (failedHelper != null && ReferenceEquals(_activeHelper, failedHelper))
                _activeHelper = null;

            warmingTask = _warmingHelperTask;
            _warmingHelperTask = null;
        }

        if (failedHelper != null)
            TrackRetirement(failedHelper.StopAsync(false));

        NightLightHelperConnection? replacement = null;
        if (warmingTask != null)
            replacement = await warmingTask.ConfigureAwait(false);

        replacement ??= await StartHelperSafelyAsync(cancellationToken).ConfigureAwait(false);
        if (replacement == null) return false;

        bool accepted;
        lock (StateGate)
        {
            accepted = !_shutdownRequested && _activeHelper == null;
            if (accepted)
            {
                _activeHelper = replacement;
                _warmupRetryAfterOperationCount = 0;
            }
        }

        if (!accepted)
        {
            TrackRetirement(replacement.StopAsync(true));
            return GetActiveHelper() != null;
        }

        TADNLog.Log($"NightLightHelperClient: recovered with helper PID {replacement.ProcessID}");
        return true;
    }

    private static async Task<NightLightHelperConnection?> StartHelperSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await NightLightHelperConnection.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightHelperClient: helper startup failed: {ex.Message}");
            return null;
        }
    }

    private static void TrackRetirement(Task retirementTask)
    {
        lock (StateGate)
            RetirementTasks.Add(retirementTask);

        _ = retirementTask.ContinueWith(
            completedTask =>
            {
                lock (StateGate)
                    RetirementTasks.Remove(completedTask);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async Task ShutdownAsync()
    {
        Task<bool>? initializationTask = null;
        Task? pumpTask;
        Timer? recycleQuietTimer;

        lock (StateGate)
        {
            if (_shutdownRequested) return;

            _shutdownRequested = true;
            PendingStrength.Clear();
            _recycleRequested = false;
            recycleQuietTimer = _recycleQuietTimer;
            _recycleQuietTimer = null;
            pumpTask = _pumpTask;
            if (InitializationTask.IsValueCreated)
                initializationTask = InitializationTask.Value;
        }

        try { recycleQuietTimer?.Dispose(); }
        catch (ObjectDisposedException) { }

        try { ShutdownTokenSource.Cancel(); }
        catch (ObjectDisposedException)
        {
            // Static lifetime normally keeps the source alive until process exit
        }

        SignalPendingWork();

        if (initializationTask != null)
            await WaitForShutdownTaskAsync(initializationTask).ConfigureAwait(false);
        if (pumpTask != null)
            await WaitForShutdownTaskAsync(pumpTask).ConfigureAwait(false);

        NightLightHelperConnection? activeHelper;
        Task<NightLightHelperConnection?>? warmingTask;
        Task[] retirementTasks;
        lock (StateGate)
        {
            activeHelper = _activeHelper;
            _activeHelper = null;
            warmingTask = _warmingHelperTask;
            _warmingHelperTask = null;
            retirementTasks = [.. RetirementTasks];
        }

        List<Task> stopTasks = [];
        if (activeHelper != null)
            stopTasks.Add(activeHelper.StopAsync(true));

        if (warmingTask != null)
        {
            NightLightHelperConnection? warmingHelper =
                await WaitForWarmingHelperDuringShutdownAsync(warmingTask).ConfigureAwait(false);
            if (warmingHelper != null)
                stopTasks.Add(warmingHelper.StopAsync(false));
        }

        stopTasks.AddRange(retirementTasks);
        if (stopTasks.Count > 0)
            await WaitForShutdownTaskAsync(Task.WhenAll(stopTasks)).ConfigureAwait(false);
    }

    private static async Task<NightLightHelperConnection?> WaitForWarmingHelperDuringShutdownAsync(
        Task<NightLightHelperConnection?> warmingTask)
    {
        try
        {
            return await warmingTask
                .WaitAsync(TimeSpan.FromMilliseconds(TimeConstants.NightLightHelperExitTimeoutMs))
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task WaitForShutdownTaskAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromMilliseconds(TimeConstants.NightLightHelperExitTimeoutMs))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightHelperClient: bounded shutdown wait ended: {ex.Message}");
        }
    }

    private sealed class NightLightHelperConnection
    {
        private readonly Process _process;
        private readonly NamedPipeServerStream _pipe;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private readonly SemaphoreSlim _operationGate = new(initialCount: 1, maxCount: 1);
        private int _stopped;

        private NightLightHelperConnection(
            Process process,
            NamedPipeServerStream pipe,
            StreamWriter writer,
            StreamReader reader)
        {
            _process = process;
            _pipe = pipe;
            _writer = writer;
            _reader = reader;
            ProcessID = process.Id;
        }

        public int ProcessID { get; }

        public int CompletedOperationCount { get; set; }

        public int? LastAcceptedPercent { get; private set; }

        public static async Task<NightLightHelperConnection> StartAsync(
            CancellationToken cancellationToken)
        {
            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new InvalidOperationException("Current executable path is unavailable.");

            string pipeName = "BrightnessTrayAppNightLight_"
                              + Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
                              + "_"
                              + Guid.NewGuid().ToString("N");
            NamedPipeServerStream pipe = new(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            Process? process = null;
            StreamWriter? writer = null;
            StreamReader? reader = null;

            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = executablePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                startInfo.ArgumentList.Add(NightLightHelperProtocol.ServerArg);
                startInfo.ArgumentList.Add(NightLightHelperProtocol.ParentProcessIDArg);
                startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add(NightLightHelperProtocol.PipeNameArg);
                startInfo.ArgumentList.Add(pipeName);
                startInfo.Environment[NoWatcherEnvironmentVariable] = "1";

                process = Process.Start(startInfo)
                          ?? throw new InvalidOperationException("Process.Start returned null.");

                using CancellationTokenSource startupTokenSource =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startupTokenSource.CancelAfter(TimeConstants.NightLightHelperStartTimeoutMs);
                await pipe.WaitForConnectionAsync(startupTokenSource.Token).ConfigureAwait(false);

                writer = new StreamWriter(
                    pipe,
                    NightLightHelperProtocol.PipeEncoding,
                    bufferSize: 1024,
                    leaveOpen: true) { AutoFlush = true };
                reader = new StreamReader(
                    pipe,
                    NightLightHelperProtocol.PipeEncoding,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);

                Task<string?> readyTask = reader.ReadLineAsync();
                string? readyResponse = await readyTask.WaitAsync(startupTokenSource.Token)
                    .ConfigureAwait(false);
                return readyResponse switch
                {
                    NightLightHelperProtocol.ReadyResponse => new NightLightHelperConnection(process, pipe, writer,
                        reader),
                    NightLightHelperProtocol.UnsupportedResponse => throw new InvalidOperationException(
                        "Helper reported the native backend unsupported."),
                    null => throw new IOException("Helper exited before its readiness response."),
                    _ => throw new IOException($"Unknown helper readiness response '{readyResponse}'.")
                };
            }
            catch
            {
                try { writer?.Dispose(); }
                catch { }

                try { reader?.Dispose(); }
                catch { }

                try { pipe.Dispose(); }
                catch { }

                KillProcess(process);
                try { process?.Dispose(); }
                catch { }

                throw;
            }
        }

        public async Task<bool> SetStrengthAsync(int percent, CancellationToken cancellationToken)
        {
            string command = NightLightHelperProtocol.SerializeSetStrength(percent);
            string? response = await SendCommandAsync(
                    command,
                    TimeConstants.NightLightHelperHotPathTimeoutMs,
                    operationName: "strength acknowledgement",
                    cancellationToken)
                .ConfigureAwait(false);

            bool accepted = response switch
            {
                NightLightHelperProtocol.SuccessResponse => true,
                NightLightHelperProtocol.FailureResponse => false,
                null => throw new IOException("Helper exited without an operation response."),
                _ => throw new IOException($"Unknown helper operation response '{response}'.")
            };
            if (accepted)
                LastAcceptedPercent = percent;
            return accepted;
        }

        public async Task<bool> SetEnabledAsync(
            bool enabled,
            int? enableStrength,
            CancellationToken cancellationToken)
        {
            string command = NightLightHelperProtocol.SerializeSetEnabled(enabled, enableStrength);

            string? response = await SendCommandAsync(
                    command,
                    TimeConstants.NightLightHelperStateChangeTimeoutMs,
                    operationName: "active-state acknowledgement",
                    cancellationToken)
                .ConfigureAwait(false);
            return response switch
            {
                NightLightHelperProtocol.SuccessResponse => true,
                NightLightHelperProtocol.FailureResponse => false,
                null => throw new IOException("Helper exited without an active-state response."),
                _ => throw new IOException($"Unknown helper active-state response '{response}'.")
            };
        }

        public async Task<bool> PingAsync(CancellationToken cancellationToken)
        {
            string? response = await SendCommandAsync(
                    NightLightHelperProtocol.PingCommand,
                    TimeConstants.NightLightHelperHotPathTimeoutMs,
                    operationName: "PING acknowledgement",
                    cancellationToken)
                .ConfigureAwait(false);
            return response switch
            {
                NightLightHelperProtocol.PongResponse => true,
                NightLightHelperProtocol.FailureResponse => false,
                null => throw new IOException("Helper exited without a PING response."),
                _ => throw new IOException($"Unknown helper PING response '{response}'.")
            };
        }

        public async Task<bool> DrainAsync(CancellationToken cancellationToken)
        {
            string? response = await SendCommandAsync(
                    NightLightHelperProtocol.DrainCommand,
                    TimeConstants.NightLightHelperHotPathTimeoutMs,
                    operationName: "drain acknowledgement",
                    cancellationToken)
                .ConfigureAwait(false);
            return response switch
            {
                NightLightHelperProtocol.DrainedResponse => true,
                NightLightHelperProtocol.FailureResponse => false,
                null => throw new IOException("Helper exited without a drain response."),
                _ => throw new IOException($"Unknown helper drain response '{response}'.")
            };
        }

        private async Task<string?> SendCommandAsync(
            string command,
            int timeoutMs,
            string operationName,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _stopped) != 0)
                throw new ObjectDisposedException(nameof(NightLightHelperConnection));

            using CancellationTokenSource operationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operationTokenSource.CancelAfter(timeoutMs);

            bool gateEntered = false;
            try
            {
                await _operationGate.WaitAsync(operationTokenSource.Token).ConfigureAwait(false);
                gateEntered = true;
                if (Volatile.Read(ref _stopped) != 0)
                    throw new ObjectDisposedException(nameof(NightLightHelperConnection));

                _writer.WriteLine(command);
                _writer.Flush();
                Task<string?> responseTask = _reader.ReadLineAsync();
                return await responseTask.WaitAsync(operationTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Helper {operationName} exceeded {timeoutMs}ms.");
            }
            finally
            {
                if (gateEntered)
                    _operationGate.Release();
            }
        }

        public async Task StopAsync(bool graceful)
        {
            if (Interlocked.Exchange(ref _stopped, value: 1) != 0) return;

            try
            {
                if (graceful && !_process.HasExited)
                {
                    bool gateEntered = false;
                    try
                    {
                        gateEntered = await _operationGate.WaitAsync(
                                TimeSpan.FromMilliseconds(TimeConstants.NightLightHelperExitTimeoutMs))
                            .ConfigureAwait(false);
                        if (!gateEntered)
                            throw new TimeoutException("Helper pipe remained busy during graceful exit.");

                        _writer.WriteLine(NightLightHelperProtocol.ExitCommand);
                        _writer.Flush();
                    }
                    catch (Exception ex)
                    {
                        TADNLog.Log(
                            $"NightLightHelperClient: EXIT to PID {ProcessID} failed: {ex.Message}");
                    }
                    finally
                    {
                        if (gateEntered)
                            _operationGate.Release();
                    }

                    try
                    {
                        await _process.WaitForExitAsync()
                            .WaitAsync(TimeSpan.FromMilliseconds(TimeConstants.NightLightHelperExitTimeoutMs))
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        TADNLog.Log(
                            $"NightLightHelperClient: PID {ProcessID} graceful exit timed out: {ex.Message}");
                    }
                }

                if (!_process.HasExited)
                    KillProcess(_process);
            }
            catch (Exception ex)
            {
                TADNLog.Log($"NightLightHelperClient: stopping PID {ProcessID} failed: {ex.Message}");
                KillProcess(_process);
            }
            finally
            {
                try { _writer.Dispose(); }
                catch { }

                try { _reader.Dispose(); }
                catch { }

                try { _pipe.Dispose(); }
                catch { }

                try { _process.Dispose(); }
                catch { }
            }
        }

        private static void KillProcess(Process? process)
        {
            if (process == null) return;

            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (Exception ex)
            {
                TADNLog.Log($"NightLightHelperClient.KillProcess failed: {ex.Message}");
            }
        }
    }
}

/// <summary>
/// Helper-process entry point that owns every SettingsHandlers_Display and CDP allocation.
/// </summary>
internal static class NightLightHelperServer
{
    private const int HelperPipeConnectTimeoutMs = 5_000;

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (!HasArg(args, NightLightHelperProtocol.ServerArg)) return false;

        StartParentWatchdog(ParseParentProcessID(args));
        string? pipeName = ParseArgValue(args, NightLightHelperProtocol.PipeNameArg);
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            exitCode = 1;
            return true;
        }

        try
        {
            using NamedPipeClientStream pipe = new(serverName: ".", pipeName, PipeDirection.InOut);
            pipe.Connect(HelperPipeConnectTimeoutMs);
            using StreamReader reader = new(
                pipe,
                NightLightHelperProtocol.PipeEncoding,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            using StreamWriter writer = new(
                pipe,
                NightLightHelperProtocol.PipeEncoding,
                bufferSize: 1024,
                leaveOpen: true) { AutoFlush = true };

            bool supported = NightLightCloudStore.IsSupported();
            writer.WriteLine(
                supported
                    ? NightLightHelperProtocol.ReadyResponse
                    : NightLightHelperProtocol.UnsupportedResponse);
            writer.Flush();
            if (!supported)
            {
                exitCode = 1;
                return true;
            }

            TADNLog.Log($"NightLightHelperServer: PID {Environment.ProcessId} ready");
            RunLoop(reader, writer);
            return true;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightHelperServer.TryRun failed: {ex}");
            exitCode = 1;
            return true;
        }
        finally
        {
            NightLightCloudStore.Shutdown();
            TADNLog.Shutdown();
        }
    }

    private static void RunLoop(StreamReader reader, StreamWriter writer)
    {
        while (reader.ReadLine() is { } line)
        {
            if (line.Equals(NightLightHelperProtocol.ExitCommand, StringComparison.Ordinal))
            {
                _ = NightLightCloudStore.DrainStreamingAsync().GetAwaiter().GetResult();
                return;
            }

            string response;
            try
            {
                response = HandleCommand(line);
            }
            catch (Exception ex)
            {
                TADNLog.Log($"NightLightHelperServer command failed: {ex}");
                response = NightLightHelperProtocol.FailureResponse;
            }

            writer.WriteLine(response);
            writer.Flush();
        }
    }

    internal static string HandleCommand(string line)
    {
        if (line.Equals(NightLightHelperProtocol.PingCommand, StringComparison.Ordinal))
            return NightLightHelperProtocol.PongResponse;

        if (line.Equals(NightLightHelperProtocol.DrainCommand, StringComparison.Ordinal))
        {
            return NightLightCloudStore.DrainStreamingAsync().GetAwaiter().GetResult()
                ? NightLightHelperProtocol.DrainedResponse
                : NightLightHelperProtocol.FailureResponse;
        }

        string[] fields = line.Split('\t');
        if (fields.Length == 2
            && fields[0].Equals(NightLightHelperProtocol.SetStrengthCommand, StringComparison.Ordinal))
        {
            if (!int.TryParse(
                    fields[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int percent) || percent is < 0 or > 100)
                return NightLightHelperProtocol.FailureResponse;

            // Reject a late main-process queue item after an off transition. Pre-enable strength priming uses
            // the ACTIVE command's combined transaction and therefore does not need this path while disabled.
            return NightLightRegistry.IsEnabled() && NightLightCloudStore.TryQueueStreamingKelvin(percent)
                ? NightLightHelperProtocol.SuccessResponse
                : NightLightHelperProtocol.FailureResponse;
        }

        if (fields.Length is 2 or 3
            && fields[0].Equals(NightLightHelperProtocol.SetEnabledCommand, StringComparison.Ordinal))
        {
            bool? parsedEnabled = fields[1] switch
            {
                "0" => false,
                "1" => true,
                _ => null
            };
            if (!parsedEnabled.HasValue) return NightLightHelperProtocol.FailureResponse;

            bool enabled = parsedEnabled.Value;
            int? enableStrength = null;
            if (fields.Length == 3)
            {
                if (!enabled || !int.TryParse(
                        fields[2],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int parsedStrength) || parsedStrength is < 0 or > 100)
                    return NightLightHelperProtocol.FailureResponse;

                enableStrength = parsedStrength;
            }

            return NightLightCloudStore.SetEnabledAsync(enabled, enableStrength).GetAwaiter().GetResult()
                ? NightLightHelperProtocol.SuccessResponse
                : NightLightHelperProtocol.FailureResponse;
        }

        return NightLightHelperProtocol.FailureResponse;
    }

    private static bool HasArg(string[] args, string name)
    {
        foreach (string argument in args)
        {
            if (argument.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int? ParseParentProcessID(string[] args)
    {
        string? value = ParseArgValue(args, NightLightHelperProtocol.ParentProcessIDArg);
        if (value != null &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parentProcessID))
            return parentProcessID;

        return null;
    }

    private static string? ParseArgValue(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }

    private static void StartParentWatchdog(int? parentProcessID)
    {
        if (parentProcessID is not ({ } parentPID and > 0)) return;

        Thread watchdog = new(() => WatchParent(parentPID))
        {
            IsBackground = true, Name = "BrightnessTrayApp.NightLightHelperParentWatchdog"
        };
        watchdog.Start();
    }

    private static void WatchParent(int parentProcessID)
    {
        try
        {
            using Process parent = Process.GetProcessById(parentProcessID);
            parent.WaitForExit();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"NightLightHelperServer parent watchdog ended: {ex.Message}");
        }

        Environment.Exit(0);
    }
}
