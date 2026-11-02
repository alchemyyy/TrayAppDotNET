using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace FanControlTrayAppDotNET.Services;

// Service that interfaces with LibreHardwareMonitor. Owns the global Computer instance, polls every
// TimeConstants.LHMPollIntervalMs, and exposes:
//   * DataSources via the static DataSource.DataSources registry, written into on each tick
//   * Fans collection, an ObservableCollection<Fan> the flyout binds to
//
// Fan control, all writes going through the coalescing control-write queue:
//   * Manual: FanDisplayedValue (clamped by Min/Max Duty) is written whenever it or the mode changes.
//   * Curve: every tick, FanCurveController evaluates the assigned curve against its data source and
//     applies Offset, Min/Max Duty, Max Delta, Jumpstart, then Min/Max Duty again as a hard bound. A
//     missing, NaN (raw or transformed), or stale source, or an unusable curve, hands the fan back to
//     hardware control (SetDefault) until it recovers. A source not polled yet since Start is pending: no
//     write at all until the first tick decides.
//   * Times are Environment.TickCount64 ms, so wall-clock changes can't fake freshness.
//   * FanControlDirtyShutdownMarker: written before the first SetSoftware, cleared after the clean-exit
//     restore in Dispose. A same-boot marker at Start means SetDefault would restore a crashed instance's
//     duty, so curve fallbacks write the fan's Max Duty instead.
//   * Warn Low / Warn High are checked every tick against measured speed; a confirmed excursion
//     raises FanSpeedWarningRaised once.
//   * Hardware IO (open, enumeration, reset, updates, control writes) runs on workers under _hardwareLock. The
//     DataSource registry, Fans and AppSettings are UI-thread state: a hardware walk only collects
//     DiscoveredSource records, which ApplyDiscovery registers on the UI thread.
//   * Resume: firmware may re-initialize the fan chip, so the hardware is reset and every fan written again.
//
// Not done yet: Disabled/Detached state classification beyond a naive check.
public sealed class LHMService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly AppSettings? _settings;
    private readonly Lock _hardwareLock = new();
    private readonly Lock _controlWriteQueueLock = new();
    private readonly Dictionary<string, ISensor> _controlSensorsByKey = new(StringComparer.OrdinalIgnoreCase);

    // Under _hardwareLock: control keys whose missing sensor was already logged by a control write
    private readonly HashSet<string> _missingControlSensorsLogged = new(StringComparer.OrdinalIgnoreCase);

    // UI thread: the control sensors the last discovery found; fans outside this set are unavailable
    private readonly HashSet<string> _availableControlKeys = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, FanControlWriteRequest> _pendingControlWrites =
        new(StringComparer.OrdinalIgnoreCase);

    // UI-thread only: poll-tick evaluation and fan property handlers both run on the dispatcher.
    private readonly Dictionary<string, FanCurveControlState> _curveControlStates =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, FanSpeedWarningTracker> _speedWarningTrackers =
        new(StringComparer.OrdinalIgnoreCase);

    // Dirty-shutdown marker: written before this process's first SetSoftware, cleared after the clean-exit
    // restore. A same-boot marker at startup means firmware defaults are untrusted for this process.
    private readonly FanControlDirtyShutdownMarker _shutdownMarker = new(
        FanControlDirtyShutdownMarker.DefaultPath,
        FanControlDirtyShutdownMarker.LegacyPath);

    private DateTime _bootTimeUtc;

    // UI-thread only. Environment.TickCount64 of the first poll tick that pushed readings; until then curve
    // sources with no reading are pending rather than unavailable.
    private long _firstPollMs = DataSource.NoSample;

    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true,
        IsMemoryEnabled = true,
        // Disabled while we investigate long-run DiskInfoToolkit/LHM storage allocation growth
        IsStorageEnabled = false,
        IsNetworkEnabled = false,
        IsControllerEnabled = true
    };

    private CancellationTokenSource? _pollingCancellationToken;
    private Task? _pollTask;
    private Task? _controlWriteTask;
    private volatile bool _disposed;
    private bool _discoveryChanged;
    private bool _controlWriteWorkerScheduled;

    public ObservableCollection<Fan> Fans { get; } = [];

    private static readonly HashSet<string> PersistedFanProperties =
    [
        nameof(Fan.RPMMode),
        nameof(Fan.ClampLow),
        nameof(Fan.ClampLowRPMMode),
        nameof(Fan.ClampHigh),
        nameof(Fan.ClampHighRPMMode),
        nameof(Fan.WarnLow),
        nameof(Fan.WarnLowRPMMode),
        nameof(Fan.WarnHigh),
        nameof(Fan.WarnHighRPMMode),
        nameof(Fan.DeltaMax),
        nameof(Fan.DeltaMaxRPMMode),
        nameof(Fan.Offset),
        nameof(Fan.OffsetRPMMode),
        nameof(Fan.FanDisplayedValue),
        nameof(Fan.StartupSpeed),
        nameof(Fan.StartupSpeedRPMMode),
        nameof(Fan.MaxRPM),
        nameof(Fan.AssignedCurveName),
        nameof(Fan.UserDefinedName),
        nameof(Fan.CurrentControlMode),
        nameof(Fan.DeadbandsName),
        nameof(Fan.Group),
        nameof(Fan.ModeLocked),
        nameof(Fan.ForcedNonFunctioning),
        nameof(Fan.ForceNonFunctional)
    ];

    public LHMService(Dispatcher dispatcher, AppSettings? settings = null)
    {
        _dispatcher = dispatcher;
        _settings = settings ?? AppServices.Settings;
        _settings?.InitializeFanControlRegistries();
    }

    // Fired after every poll tick once values have been pushed into DataSources and Fans.
    // Always raised on the UI thread (the push pass marshals before invoking).
    // Subscribers can use this to recompute curve outputs without subscribing to every signal.
    public event Action? PollTickCompleted;

    // Raised on the UI thread once per confirmed Warn Low / Warn High excursion.
    public event Action<Fan, FanSpeedWarning>? FanSpeedWarningRaised;

    /// <summary>
    /// Opens the hardware and runs the cold enumeration on a worker thread, then registers what it found on the UI
    /// thread and starts polling. Opening loads the drivers and enumerates every device, which takes seconds, so it
    /// must not run on the UI thread; the DataSource registry, Fans and settings it fills are UI-thread state.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pollingCancellationToken != null) return;

        _bootTimeUtc = FanControlDirtyShutdownMarker.CurrentBootTimeUtc();
        _shutdownMarker.Load(_bootTimeUtc);

        List<DiscoveredSource> discovered = await Task.Run(
            () =>
            {
                lock (_hardwareLock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _computer.Open();
                    return DiscoverHardwareLocked();
                }
            },
            cancellationToken).ConfigureAwait(false);

        await _dispatcher.InvokeAsync(
            () =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                ApplyDiscovery(discovered);
            },
            DispatcherPriority.Normal,
            cancellationToken);

        ObjectDisposedException.ThrowIf(_disposed, this);
        _pollingCancellationToken = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoop(_pollingCancellationToken.Token));
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>Only a resume can find the fan chip re-initialized by firmware.</summary>
    internal static bool ShouldReapplyControl(PowerModes mode) => mode == PowerModes.Resume;

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (_disposed || !ShouldReapplyControl(e.Mode)) return;

        _ = ReapplyControlAfterResumeAsync();
    }

    /// <summary>
    /// After resume (always after hibernate) the firmware may have put the fan chip back on BIOS control while every
    /// Fan still shows the user's setting. LHM writes a control only when its value changes, so the hardware is
    /// reset (fresh controls, fresh register snapshot at the next SetSoftware) and every fan's state is queued again.
    /// A controller that is not back yet (a USB fan hub) is missing from the first rediscovery, so while control
    /// sensors are missing it is retried a few times with growing delays; fans whose sensor stays missing are shown
    /// as unavailable until a later discovery finds it.
    /// </summary>
    private async Task ReapplyControlAfterResumeAsync()
    {
        try
        {
            HashSet<string> controlKeysBefore;
            lock (_hardwareLock)
                controlKeysBefore = new HashSet<string>(_controlSensorsByKey.Keys, StringComparer.OrdinalIgnoreCase);

            for (int attempt = 1;; attempt++)
            {
                List<DiscoveredSource>? discovered = await Task.Run(() =>
                {
                    lock (_hardwareLock)
                    {
                        if (_disposed) return null;

                        _computer.Reset();
                        return DiscoverHardwareLocked();
                    }
                }).ConfigureAwait(false);
                if (discovered == null) return;

                await _dispatcher.InvokeAsync(() =>
                {
                    if (_disposed) return;

                    ApplyDiscovery(discovered);
                    _curveControlStates.Clear();
                    foreach (Fan fan in Fans)
                        QueueFanControlWriteForCurrentState(fan);
                    TADNLog.Log(
                        $"LHMService: hardware reset after resume (attempt {attempt}); re-applied control to "
                        + $"{Fans.Count} fans");
                });

                List<string> missing = MissingControlKeys(
                    controlKeysBefore,
                    discovered.Where(static found => found.IsControl).Select(static found => found.Key));
                if (missing.Count == 0) return;

                if (attempt >= TimeConstants.ResumeRediscoveryMaxAttempts)
                {
                    TADNLog.Log(
                        $"LHMService: after resume {missing.Count} control sensors are still missing "
                        + $"({string.Join(", ", missing)}); their fans stay unavailable");
                    return;
                }

                await Task.Delay(TimeConstants.ResumeRediscoveryRetryDelayMs * attempt).ConfigureAwait(false);
                if (_disposed) return;
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"LHMService: re-applying fan control after resume failed: {ex}");
        }
    }

    /// <summary>Control sensors present before a rediscovery that it did not find again.</summary>
    internal static List<string> MissingControlKeys(IReadOnlySet<string> before, IEnumerable<string> after)
    {
        HashSet<string> found = new(after, StringComparer.OrdinalIgnoreCase);
        return [.. before.Where(key => !found.Contains(key)).Order(StringComparer.OrdinalIgnoreCase)];
    }

    public void Stop()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        CancellationTokenSource? pollingCancellationToken = _pollingCancellationToken;
        Task? pollTask = _pollTask;
        if (pollingCancellationToken == null) return;

        pollingCancellationToken.Cancel();
        Safe.WaitForStop(pollTask, TimeConstants.BackgroundPollShutdownWaitMs, owner: "LHMService poll loop");
        pollingCancellationToken.Dispose();
        _pollingCancellationToken = null;
        _pollTask = null;
    }

    // Background poll loop. Hardware updates are real IO (SetupAPI, MSR reads, ACPI queries) and
    // can take tens of ms per pass, which is why this runs off the UI thread. Only the cheap
    // value-push pass marshals back to the dispatcher: setting properties on DataSource / Fan
    // fires INPC, and Avalonia bindings expect those to arrive on the UI thread.
    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            List<SensorReading> readings = [];
            try
            {
                lock (_hardwareLock)
                {
                    foreach (IHardware hardware in _computer.Hardware)
                    {
                        hardware.Update();
                        foreach (IHardware sub in hardware.SubHardware) sub.Update();
                    }

                    readings = CaptureSensorReadings();
                }
            }
            catch (Exception ex) { TADNLog.Log($"LHMService poll (hardware update) failed: {ex.Message}"); }

            try
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    long nowMs = Environment.TickCount64;
                    PushSensorReadings(readings, nowMs);
                    if (_firstPollMs == DataSource.NoSample) _firstPollMs = nowMs;
                    ApplyCurveControl(nowMs);
                    UpdateSpeedWarnings(nowMs);
                    PollTickCompleted?.Invoke();
                }, DispatcherPriority.Background, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { TADNLog.Log($"LHMService poll (UI marshal) failed: {ex.Message}"); }

            try { await Task.Delay(TimeConstants.LHMPollIntervalMs, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Walks the hardware (worker thread, <see cref="_hardwareLock"/> held): refreshes the control-sensor map and
    /// collects every sensor plus the synthetic CPU clock sources, in registration order. Touches no UI-thread state.
    /// </summary>
    private List<DiscoveredSource> DiscoverHardwareLocked()
    {
        _controlSensorsByKey.Clear();
        _missingControlSensorsLogged.Clear();
        List<DiscoveredSource> discovered = [];
        foreach (IHardware hardware in _computer.Hardware)
        {
            hardware.Update();
            DiscoverHardware(hardware, discovered);
        }

        return discovered;
    }

    private void DiscoverHardware(IHardware hardware, List<DiscoveredSource> discovered)
    {
        bool isCPU = hardware.HardwareType == HardwareType.Cpu;
        bool hasCoreClockSensors = false;
        bool hasCoreEffectiveClockSensors = false;
        string hardwareType = hardware.HardwareType.ToString();
        foreach (ISensor sensor in hardware.Sensors)
        {
            DisableUnusedSensorHistory(sensor);

            string key = BuildSensorKey(hardware, sensor);
            DataSourceTypeEnum type = MapSensorType(sensor.SensorType);
            hasCoreClockSensors |= isCPU && LHMSyntheticProbeSources.IsCoreClockSensor(type, sensor.Name);
            hasCoreEffectiveClockSensors |= isCPU && LHMSyntheticProbeSources.IsCoreEffectiveClockSensor(
                type,
                sensor.Name);

            bool isControl = sensor.SensorType == SensorType.Control;
            if (isControl) _controlSensorsByKey[key] = sensor;
            discovered.Add(new DiscoveredSource(key, hardware.Name, hardwareType, sensor.Name, type, isControl));
        }

        if (hasCoreClockSensors)
        {
            discovered.Add(new DiscoveredSource(
                LHMSyntheticProbeSources.BuildMaxClockKey(hardware.Name),
                hardware.Name,
                hardwareType,
                LHMSyntheticProbeSources.MaxClockDisplayName,
                DataSourceTypeEnum.Clock,
                IsControl: false,
                IsSyntheticClock: true));
        }

        if (hasCoreEffectiveClockSensors)
        {
            discovered.Add(new DiscoveredSource(
                LHMSyntheticProbeSources.BuildMaxEffectiveClockKey(hardware.Name),
                hardware.Name,
                hardwareType,
                LHMSyntheticProbeSources.MaxEffectiveClockDisplayName,
                DataSourceTypeEnum.Clock,
                IsControl: false,
                IsSyntheticClock: true));
        }

        foreach (IHardware sub in hardware.SubHardware)
        {
            sub.Update();
            DiscoverHardware(sub, discovered);
        }
    }

    /// <summary>
    /// UI thread: registers a DataSource for every discovered sensor and a Fan for every Control sensor, then saves
    /// when discovery changed the settings.
    /// </summary>
    private void ApplyDiscovery(IReadOnlyList<DiscoveredSource> discovered)
    {
        _dispatcher.VerifyAccess();

        _discoveryChanged = false;
        foreach (DataSource source in DataSource.DataSources.Values)
            source.IsLiveHardwareSensor = false;

        _availableControlKeys.Clear();
        foreach (DiscoveredSource found in discovered)
        {
            if (found.IsControl) _availableControlKeys.Add(found.Key);
        }

        foreach (Fan fan in Fans)
            UpdateFanFunctionalState(fan);

        foreach (DiscoveredSource found in discovered)
        {
            if (found.IsSyntheticClock)
            {
                EnsureSyntheticClockSource(found);
                continue;
            }

            RegisterOrRefreshSource(found);
            if (found.IsControl) EnsureFanForControlSensor(found);
        }

        if (_settings?.EnsureDefaultDeviceNicknameRules() == true)
            _discoveryChanged = true;
        if (_settings?.EnsureDefaultProbeNicknameRules() == true)
            _discoveryChanged = true;
        if (_discoveryChanged) PersistLiveState();
    }

    private void RegisterOrRefreshSource(DiscoveredSource found)
    {
        if (DataSource.Find(found.Key) is not { } existingSource)
        {
            DataSource source = new()
            {
                DataSourceKey = found.Key,
                UserDefinedName = found.Name,
                ControllerName = found.HardwareName,
                ControllerHardwareType = found.HardwareType,
                DataSourceType = found.Type,
                IsLiveHardwareSensor = true
            };
            source.EnsureDisplayMetadata();
            DataSource.Register(source);
            _discoveryChanged = true;
            return;
        }

        existingSource.IsLiveHardwareSensor = true;
        if (string.IsNullOrWhiteSpace(existingSource.ControllerName))
            existingSource.ControllerName = found.HardwareName;
        if (!string.Equals(existingSource.ControllerHardwareType, found.HardwareType, StringComparison.Ordinal))
        {
            existingSource.ControllerHardwareType = found.HardwareType;
            _discoveryChanged = true;
        }

        if (existingSource.DataSourceType == DataSourceTypeEnum.Unknown)
            existingSource.DataSourceType = found.Type;
        if (string.IsNullOrWhiteSpace(existingSource.UserDefinedName))
            existingSource.UserDefinedName = found.Name;
        existingSource.EnsureDisplayMetadata();
    }

    /// <summary>
    /// Disables LibreHardwareMonitor's unused one-day history and releases restored samples.
    /// </summary>
    internal static void DisableUnusedSensorHistory(ISensor sensor)
    {
        sensor.ValuesTimeWindow = TimeSpan.Zero;
        sensor.ClearValues();
    }

    // Walk every sensor and push its current value into the matching DataSource. Fans get their
    // CurrentRPM and CurrentDutyCycle updated from the paired Fan-type and Control-type sensors.
    private List<SensorReading> CaptureSensorReadings()
    {
        List<SensorReading> readings = [];
        foreach (IHardware hardware in _computer.Hardware)
            CaptureFromHardware(hardware, readings);
        return readings;
    }

    private static void CaptureFromHardware(IHardware hardware, List<SensorReading> readings)
    {
        bool isCPU = hardware.HardwareType == HardwareType.Cpu;
        float? maxClock = null;
        float? maxEffectiveClock = null;
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.Value is not { } value) continue;

            DataSourceTypeEnum type = MapSensorType(sensor.SensorType);
            string key = BuildSensorKey(hardware, sensor);
            readings.Add(new SensorReading(key, hardware.Name, sensor.Name, sensor.SensorType, value));
            if (!isCPU) continue;

            if (LHMSyntheticProbeSources.IsCoreClockSensor(type, sensor.Name))
            {
                maxClock = maxClock.HasValue
                    ? Math.Max(maxClock.Value, value)
                    : value;
                continue;
            }

            if (!LHMSyntheticProbeSources.IsCoreEffectiveClockSensor(type, sensor.Name)) continue;

            maxEffectiveClock = maxEffectiveClock.HasValue
                ? Math.Max(maxEffectiveClock.Value, value)
                : value;
        }

        if (maxClock is { } maxCoreClock)
        {
            readings.Add(new SensorReading(
                LHMSyntheticProbeSources.BuildMaxClockKey(hardware.Name),
                hardware.Name,
                LHMSyntheticProbeSources.MaxClockDisplayName,
                SensorType.Clock,
                maxCoreClock));
        }

        if (maxEffectiveClock is { } maxEffectiveCoreClock)
        {
            readings.Add(new SensorReading(
                LHMSyntheticProbeSources.BuildMaxEffectiveClockKey(hardware.Name),
                hardware.Name,
                LHMSyntheticProbeSources.MaxEffectiveClockDisplayName,
                SensorType.Clock,
                maxEffectiveCoreClock));
        }

        foreach (IHardware sub in hardware.SubHardware) CaptureFromHardware(sub, readings);
    }

    /// <summary>
    /// Registers or refreshes one synthetic CPU clock source (max core clock or max effective clock).
    /// </summary>
    private void EnsureSyntheticClockSource(DiscoveredSource found)
    {
        if (DataSource.Find(found.Key) is not { } existingSource)
        {
            DataSource source = new()
            {
                DataSourceKey = found.Key,
                UserDefinedName = found.Name,
                ControllerName = found.HardwareName,
                ControllerHardwareType = found.HardwareType,
                DataSourceType = DataSourceTypeEnum.Clock,
                IsLiveHardwareSensor = true
            };
            source.EnsureDisplayMetadata();
            DataSource.Register(source);
            _discoveryChanged = true;
            return;
        }

        existingSource.IsLiveHardwareSensor = true;
        if (string.IsNullOrWhiteSpace(existingSource.ControllerName))
            existingSource.ControllerName = found.HardwareName;
        if (!string.Equals(existingSource.ControllerHardwareType, found.HardwareType, StringComparison.Ordinal))
        {
            existingSource.ControllerHardwareType = found.HardwareType;
            _discoveryChanged = true;
        }

        if (existingSource.DataSourceType != DataSourceTypeEnum.Clock)
        {
            existingSource.DataSourceType = DataSourceTypeEnum.Clock;
            _discoveryChanged = true;
        }

        if (string.IsNullOrWhiteSpace(existingSource.UserDefinedName))
            existingSource.UserDefinedName = found.Name;
        existingSource.EnsureDisplayMetadata();
    }

    private void PushSensorReadings(IReadOnlyList<SensorReading> readings, long nowMs)
    {
        foreach (SensorReading reading in readings)
        {
            DataSource? source = DataSource.Find(reading.Key);
            if (source != null)
            {
                bool transformValid = source.SetValue((long)Math.Round(reading.Value * 1000.0));
                // A NaN/infinite reading, raw or after its transform, makes the source unavailable right away
                // so no curve acts on it
                source.RecordSample(nowMs, transformValid && float.IsFinite(reading.Value));
            }

            switch (reading.SensorType)
            {
                case SensorType.Control:
                {
                    Fan? fan = FindFanByControlKey(reading.Key);
                    if (fan != null)
                    {
                        fan.CurrentDutyCycle = reading.Value;
                        UpdateFanFunctionalState(fan);
                    }

                    break;
                }
                case SensorType.Fan:
                {
                    Fan? fan = FindFanByFanSensor(reading.HardwareName, reading.SensorName);
                    if (fan != null)
                    {
                        fan.HasRPMSensor = true;
                        fan.CurrentRPM = (int)Math.Round(reading.Value);
                        UpdateFanFunctionalState(fan);
                    }

                    break;
                }
            }
        }
    }

    // Promote a Control sensor into a Fan model entry if we haven't seen this key before.
    private void EnsureFanForControlSensor(DiscoveredSource controlSensor)
    {
        string key = controlSensor.Key;
        if (FindFanByControlKey(key) != null) return;

        Fan fan = new()
        {
            DataSourceKey = key,
            ControllerModel = controlSensor.HardwareName,
            ControlsName = "Controls",
            FansName = controlSensor.Name
        };
        ApplyDefaultsToNewFan(fan);
        if (_settings?.FindPersistedFan(key) is { } persisted) fan.ApplyUserSettings(persisted);
        UpdateFanFunctionalState(fan);

        fan.PropertyChanged += OnFanPropertyChanged;
        Fans.Add(fan);
        _settings?.UpsertPersistedFan(fan);
        QueueFanControlWriteForCurrentState(fan);
        _discoveryChanged = true;
    }

    private void ApplyDefaultsToNewFan(Fan fan)
    {
        if (_settings == null) return;

        fan.RPMMode = _settings.DefaultToRPMMode;
        fan.StartupSpeed = _settings.DefaultJumpstartDutyCycle;
        fan.DeltaMax = _settings.DefaultDeltaMaxDutyCycle;
        fan.AssignedCurveName = NormalizeCurveName(_settings.DefaultAssignedCurve);
        fan.CurrentControlMode = FanControlMode.Curve;
    }

    private static string NormalizeCurveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        return string.Equals(name, b: "None", StringComparison.OrdinalIgnoreCase) ? string.Empty : name;
    }

    private void OnFanPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Fan fan) return;
        if (string.IsNullOrEmpty(e.PropertyName)) return;
        if (!PersistedFanProperties.Contains(e.PropertyName)) return;

        UpdateFanFunctionalState(fan);
        if (IsControlWriteProperty(e.PropertyName))
            QueueFanControlWriteForCurrentState(fan);
        _settings?.UpsertPersistedFan(fan);
        PersistLiveState();
    }

    private static bool IsControlWriteProperty(string propertyName) =>
        propertyName is nameof(Fan.FanDisplayedValue)
            or nameof(Fan.CurrentControlMode)
            or nameof(Fan.RPMMode)
            or nameof(Fan.MaxRPM)
            or nameof(Fan.ClampLow)
            or nameof(Fan.ClampLowRPMMode)
            or nameof(Fan.ClampHigh)
            or nameof(Fan.ClampHighRPMMode)
            or nameof(Fan.ForcedNonFunctioning)
            or nameof(Fan.ForceNonFunctional);

    public void QueueFanControlWriteForCurrentState(Fan fan)
    {
        if (_disposed || string.IsNullOrWhiteSpace(fan.DataSourceKey)) return;

        // A fan whose control sensor is gone (shown as unavailable) gets no writes until discovery finds it again
        if (!_availableControlKeys.Contains(fan.DataSourceKey)) return;

        if (fan is { ForcedNonFunctioning: false, CurrentControlMode: FanControlMode.Curve })
        {
            ApplyCurveControl(fan, Environment.TickCount64);
            return;
        }

        _curveControlStates.Remove(fan.DataSourceKey);
        SetCurveFallback(fan, FanCurveFallback.None);
        QueueControlWrite(
            fan is { ForcedNonFunctioning: false, CurrentControlMode: FanControlMode.Manual }
                ? FanControlWriteRequest.Software(fan.DataSourceKey,
                    FanCurveController.ResolveManualDutyCyclePercent(fan))
                : FanControlWriteRequest.Default(fan.DataSourceKey));
    }

    // Runs every poll tick for each Curve-mode fan (grouped fans resolve their group's curve).
    private void ApplyCurveControl(long nowMs)
    {
        if (_disposed) return;

        foreach (Fan fan in Fans)
        {
            if (fan is not { ForcedNonFunctioning: false, CurrentControlMode: FanControlMode.Curve }) continue;
            if (string.IsNullOrWhiteSpace(fan.DataSourceKey)) continue;
            if (!_availableControlKeys.Contains(fan.DataSourceKey)) continue;
            ApplyCurveControl(fan, nowMs);
        }
    }

    private void ApplyCurveControl(Fan fan, long nowMs)
    {
        if (!_curveControlStates.TryGetValue(fan.DataSourceKey, out FanCurveControlState? state))
        {
            state = new FanCurveControlState();
            _curveControlStates[fan.DataSourceKey] = state;
        }

        Curve? curve = fan.AssignedCurve;
        FanCurveCommand command =
            FanCurveController.Evaluate(fan, curve, curve?.SelectedDataSource, state, nowMs, _firstPollMs);
        // The source hasn't been polled yet: leave the fan alone until the first tick decides
        if (command.Action == FanCurveAction.Pending) return;

        command = FanCurveController.ApplyFirmwareTrust(fan, command, _shutdownMarker.FirmwareDefaultsTrusted);
        SetCurveFallback(fan, command);

        if (command.Action == FanCurveAction.HandBack)
        {
            if (state.HandedToHardware) return;

            state.HandedToHardware = true;
            QueueControlWrite(FanControlWriteRequest.Default(fan.DataSourceKey));
            return;
        }

        state.HandedToHardware = false;
        QueueControlWrite(FanControlWriteRequest.Software(fan.DataSourceKey, command.DutyCyclePercent));
    }

    private static void SetCurveFallback(Fan fan, FanCurveCommand command)
    {
        FanCurveFallback fallback = command.Fallback;
        if (fan.CurveFallback == fallback) return;

        if (fallback == FanCurveFallback.None)
            TADNLog.Log($"LHMService curve control resumed for {fan.DataSourceKey}");
        else if (command.Action == FanCurveAction.Drive)
        {
            TADNLog.Log(
                $"LHMService curve control fallback for {fan.DataSourceKey}: {fallback}; writing Max Duty "
                + $"{command.DutyCyclePercent:0.#}% instead of SetDefault because firmware defaults are untrusted "
                + "after an unclean exit earlier this boot");
        }
        else
            TADNLog.Log($"LHMService curve control handed {fan.DataSourceKey} back to hardware: {fallback}");
        fan.CurveFallback = fallback;
    }

    private static void SetCurveFallback(Fan fan, FanCurveFallback fallback) =>
        SetCurveFallback(fan, FanCurveCommand.HandBack(fallback));

    private void UpdateSpeedWarnings(long nowMs)
    {
        if (_disposed) return;

        foreach (Fan fan in Fans)
        {
            if (string.IsNullOrWhiteSpace(fan.DataSourceKey)) continue;
            if (!_speedWarningTrackers.TryGetValue(fan.DataSourceKey, out FanSpeedWarningTracker? tracker))
            {
                tracker = new FanSpeedWarningTracker();
                _speedWarningTrackers[fan.DataSourceKey] = tracker;
            }

            bool raised = tracker.Update(FanSpeedWarnings.Classify(fan), nowMs);
            fan.SpeedWarning = tracker.Active;
            if (!raised) continue;

            (string measured, string threshold) = FanSpeedWarnings.Describe(fan, tracker.Active);
            TADNLog.Log($"LHMService speed warning {tracker.Active} for {fan.DataSourceKey}: {measured} vs {threshold}");
            FanSpeedWarningRaised?.Invoke(fan, tracker.Active);
        }
    }

    private void QueueControlWrite(FanControlWriteRequest request)
    {
        lock (_controlWriteQueueLock)
        {
            _pendingControlWrites[request.DataSourceKey] = request;
            if (_controlWriteWorkerScheduled) return;

            _controlWriteWorkerScheduled = true;
            _controlWriteTask = Task.Run(ProcessQueuedControlWrites);
        }
    }

    private void ProcessQueuedControlWrites()
    {
        try
        {
            while (true)
            {
                List<FanControlWriteRequest> batch;
                lock (_controlWriteQueueLock)
                {
                    if (_disposed || _pendingControlWrites.Count == 0)
                    {
                        _controlWriteWorkerScheduled = false;
                        return;
                    }

                    batch = [.. _pendingControlWrites.Values];
                    _pendingControlWrites.Clear();
                }

                foreach (FanControlWriteRequest request in batch)
                {
                    if (_disposed) return;
                    try { ApplyControlWrite(request); }
                    catch (Exception ex)
                    {
                        TADNLog.Log(
                            $"LHMService control write failed for {request.DataSourceKey}: {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            lock (_controlWriteQueueLock)
            {
                if (_disposed || _pendingControlWrites.Count == 0)
                    _controlWriteWorkerScheduled = false;
            }
        }
    }

    private void ApplyControlWrite(FanControlWriteRequest request)
    {
        lock (_hardwareLock)
        {
            if (!_controlSensorsByKey.TryGetValue(request.DataSourceKey, out ISensor? sensor))
            {
                // Logged once per key; the next discovery that finds the sensor again resets this
                if (_missingControlSensorsLogged.Add(request.DataSourceKey))
                    TADNLog.Log($"LHMService control write skipped; sensor not found: {request.DataSourceKey}");
                return;
            }

            IControl? control = sensor.Control;
            if (control == null)
            {
                TADNLog.Log($"LHMService control write skipped; sensor has no control: {request.DataSourceKey}");
                return;
            }

            if (request.UseDefault)
            {
                control.SetDefault();
                return;
            }

            _shutdownMarker.MarkSoftwareControl(_bootTimeUtc);
            float value = ClampToSoftwareRange(control, request.DutyCyclePercent);
            control.SetSoftware(value);
        }
    }

    // Never outside 0-100, whatever range the control reports.
    private static float ClampToSoftwareRange(IControl control, double dutyCyclePercent)
    {
        float min = float.IsNaN(control.MinSoftwareValue) ? 0.0f : control.MinSoftwareValue;
        float max = float.IsNaN(control.MaxSoftwareValue) ? 100.0f : control.MaxSoftwareValue;
        if (max < min) (min, max) = (max, min);

        double value = Math.Clamp(dutyCyclePercent, min, max);
        return (float)Math.Clamp(value, min: 0.0, max: 100.0);
    }

    private void UpdateFanFunctionalState(Fan fan) =>
        fan.CurrentState = ResolveFanState(
            _availableControlKeys.Contains(fan.DataSourceKey),
            fan.ForcedNonFunctioning,
            fan.CurrentDutyCycle,
            fan.CurrentRPM);

    /// <summary>
    /// Disabled when the fan's control sensor is gone (for example a controller that did not come back after
    /// resume), Detached when forced or when it reports duty without RPM, Normal otherwise.
    /// </summary>
    internal static FanState ResolveFanState(
        bool controlAvailable,
        bool forcedNonFunctioning,
        double dutyCycle,
        int rpm)
    {
        if (!controlAvailable) return FanState.Disabled;
        if (forcedNonFunctioning) return FanState.Detached;

        return dutyCycle > 0.001 && rpm <= 0 ? FanState.Detached : FanState.Normal;
    }

    public void PersistLiveState(bool save = true)
    {
        if (_settings == null) return;

        foreach (Fan fan in Fans)
            _settings.UpsertPersistedFan(fan);

        _settings.SyncFanControlRegistriesForSave();
        if (save) _settings.Save();
    }

    private Fan? FindFanByControlKey(string key)
    {
        foreach (Fan fan in Fans)
        {
            if (string.Equals(fan.DataSourceKey, key, StringComparison.OrdinalIgnoreCase))
                return fan;
        }

        return null;
    }

    // Heuristic pairing: a Fan-type sensor named "Fan #1" pairs with the Control-type sensor
    // "Fan Control #1" on the same hardware. Refined pairing (by index, by physical header) is
    // future-pass work.
    private Fan? FindFanByFanSensor(string hardwareName, string sensorName)
    {
        foreach (Fan fan in Fans)
        {
            if (!string.Equals(fan.ControllerModel, hardwareName, StringComparison.OrdinalIgnoreCase)) continue;
            if (fan.FansName.Contains(sensorName, StringComparison.OrdinalIgnoreCase)) return fan;
        }

        return null;
    }

    // Build a "fully qualified name" key: ControllerName.SensorTypeFolder.SensorName with spaces
    // replaced by underscores and dots intact as separators. This survives serialization and is
    // stable across runs as long as the hardware enumerates the same way.
    private static string BuildSensorKey(IHardware hardware, ISensor sensor)
    {
        string controller = hardware.Name.Replace(oldChar: ' ', newChar: '_');
        string folder = sensor.SensorType.ToString();
        string leaf = sensor.Name.Replace(oldChar: ' ', newChar: '_');
        return $"{controller}.{folder}.{leaf}";
    }

    private static DataSourceTypeEnum MapSensorType(SensorType t) => t switch
    {
        SensorType.Voltage => DataSourceTypeEnum.Voltage,
        SensorType.Current => DataSourceTypeEnum.Current,
        SensorType.Power => DataSourceTypeEnum.Power,
        SensorType.Clock => DataSourceTypeEnum.Clock,
        SensorType.Temperature => DataSourceTypeEnum.Temperature,
        SensorType.Load => DataSourceTypeEnum.Load,
        SensorType.Fan => DataSourceTypeEnum.Fan,
        SensorType.Flow => DataSourceTypeEnum.Flow,
        SensorType.Control => DataSourceTypeEnum.Control,
        SensorType.Level => DataSourceTypeEnum.Level,
        SensorType.Data => DataSourceTypeEnum.Data,
        SensorType.Throughput => DataSourceTypeEnum.Throughput,
        _ => DataSourceTypeEnum.Unknown
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        DrainControlWriteWorker();
        UnsubscribeAndClearFans();
        PollTickCompleted = null;
        FanSpeedWarningRaised = null;
        try
        {
            lock (_hardwareLock)
            {
                _controlSensorsByKey.Clear();
                _computer.Close();
            }

            // Close restored every header this process took over; only now is the marker safe to drop
            _shutdownMarker.ClearAfterCleanRestore();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"LHMService dispose failed while closing hardware: {ex.Message}");
        }
    }

    /// <summary>
    /// Waits for the asynchronous control-write worker to release service references.
    /// </summary>
    private void DrainControlWriteWorker()
    {
        Task? controlWriteTask;
        lock (_controlWriteQueueLock)
        {
            _pendingControlWrites.Clear();
            controlWriteTask = _controlWriteTask;
        }

        Safe.WaitForStop(controlWriteTask, TimeConstants.BackgroundPollShutdownWaitMs, owner: "LHMService control write worker");

        lock (_controlWriteQueueLock)
        {
            _pendingControlWrites.Clear();
            _controlWriteWorkerScheduled = false;
            _controlWriteTask = null;
        }
    }

    /// <summary>
    /// Releases fan model event subscriptions and collection references owned by this service.
    /// </summary>
    private void UnsubscribeAndClearFans()
    {
        foreach (Fan fan in Fans.ToArray())
            fan.PropertyChanged -= OnFanPropertyChanged;
        Fans.Clear();
    }

    private readonly record struct FanControlWriteRequest(
        string DataSourceKey,
        double DutyCyclePercent,
        bool UseDefault)
    {
        public static FanControlWriteRequest Software(string dataSourceKey, double dutyCyclePercent) =>
            new(dataSourceKey, dutyCyclePercent, UseDefault: false);

        public static FanControlWriteRequest Default(string dataSourceKey) =>
            new(dataSourceKey, DutyCyclePercent: 0.0, UseDefault: true);
    }

    private readonly record struct SensorReading(
        string Key,
        string HardwareName,
        string SensorName,
        SensorType SensorType,
        float Value);

    /// <summary>One sensor, or synthetic CPU clock source, found by a hardware walk; no live LHM object.</summary>
    private sealed record DiscoveredSource(
        string Key,
        string HardwareName,
        string HardwareType,
        string Name,
        DataSourceTypeEnum Type,
        bool IsControl,
        bool IsSyntheticClock = false);
}
