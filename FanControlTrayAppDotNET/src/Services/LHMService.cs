using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using LibreHardwareMonitor.Hardware;

namespace FanControlTrayAppDotNET.Services;

// First-pass skeleton service that interfaces with LibreHardwareMonitor. Owns the global
// Computer instance, polls every TimeConstants.LHMPollIntervalMs, and exposes:
//   * DataSources via the static DataSource.DataSources registry, written into on each tick
//   * Fans collection, an ObservableCollection<Fan> the flyout binds to
//
// What this pass does NOT do yet:
//   * Curve evaluation, jumpstart, delta limiting
//   * Disabled/Detached state classification beyond a naive check
public sealed class LHMService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly AppSettings? _settings;
    private readonly Lock _hardwareLock = new();
    private readonly Lock _controlWriteQueueLock = new();
    private readonly Dictionary<string, ISensor> _controlSensorsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FanControlWriteRequest> _pendingControlWrites =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true,
        IsMemoryEnabled = true,
        IsStorageEnabled = true,
        IsNetworkEnabled = false,
        IsControllerEnabled = true,
    };

    private CancellationTokenSource? _pollingCancellationToken;
    private Task? _pollTask;
    private bool _disposed;
    private bool _discoveryChanged;
    private bool _controlWriteWorkerScheduled;

    public ObservableCollection<Fan> Fans { get; } = [];

    private static readonly HashSet<string> PersistedFanProperties =
    [
        nameof(Fan.RPMMode),
        nameof(Fan.ClampLow),
        nameof(Fan.ClampHigh),
        nameof(Fan.WarnLow),
        nameof(Fan.WarnHigh),
        nameof(Fan.DeltaMax),
        nameof(Fan.Offset),
        nameof(Fan.FanDisplayedValue),
        nameof(Fan.StartupSpeed),
        nameof(Fan.MaxRPM),
        nameof(Fan.AssignedCurveName),
        nameof(Fan.UserDefinedName),
        nameof(Fan.CurrentControlMode),
        nameof(Fan.Triggers),
        nameof(Fan.DeadbandsName),
        nameof(Fan.Group),
        nameof(Fan.ModeLocked),
        nameof(Fan.ForcedNonFunctioning),
        nameof(Fan.ForceNonFunctional),
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

    public void Start()
    {
        if (_pollingCancellationToken != null) return;

        lock (_hardwareLock)
        {
            _computer.Open();
            // Cold enumeration runs on whatever thread starts us (the UI thread at startup). That's
            // fine because it only mutates Fans / DataSources once and the UI hasn't bound yet.
            RebuildFromHardware();
        }

        _pollingCancellationToken = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoop(_pollingCancellationToken.Token));
    }

    public void Stop()
    {
        if (_pollingCancellationToken == null) return;
        _pollingCancellationToken.Cancel();
        try { _pollTask?.Wait(TimeConstants.BackgroundPollShutdownWaitMs); }
        catch (AggregateException) { /* poll loop already exited */ }
        _pollingCancellationToken.Dispose();
        _pollingCancellationToken = null;
        _pollTask = null;
    }

    // Background poll loop. Hardware updates are real IO (SetupAPI, MSR reads, ACPI queries) and
    // can take tens of ms per pass, which is why this runs off the UI thread. Only the cheap
    // value-push pass marshals back to the dispatcher: setting properties on DataSource / Fan
    // fires INPC, and WPF bindings expect those to arrive on the UI thread.
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
                    PushSensorReadings(readings);
                    PollTickCompleted?.Invoke();
                }, DispatcherPriority.Background, token);
            }
            catch (Exception ex) { TADNLog.Log($"LHMService poll (UI marshal) failed: {ex.Message}"); }

            try { await Task.Delay(TimeConstants.LHMPollIntervalMs, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    // Cold-start: enumerate hardware, register a DataSource for every sensor, and instantiate a
    // Fan for every Control sensor we find. Called once on Start() and again whenever topology
    // changes are detected (future pass).
    private void RebuildFromHardware()
    {
        _discoveryChanged = false;
        _controlSensorsByKey.Clear();
        foreach (IHardware hardware in _computer.Hardware)
        {
            hardware.Update();
            VisitHardware(hardware);
        }
        if (_discoveryChanged) PersistLiveState(save: true);
    }

    private void VisitHardware(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            string key = BuildSensorKey(hardware, sensor);
            DataSourceTypeEnum type = MapSensorType(sensor.SensorType);

            if (DataSource.Find(key) is not { } existingSource)
            {
                DataSource source = new()
                {
                    DataSourceKey = key,
                    UserDefinedName = sensor.Name,
                    ControllerName = hardware.Name,
                    DataSourceType = type,
                };
                source.EnsureDisplayMetadata();
                DataSource.Register(source);
                _discoveryChanged = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(existingSource.ControllerName))
                    existingSource.ControllerName = hardware.Name;
                if (existingSource.DataSourceType == DataSourceTypeEnum.Unknown)
                    existingSource.DataSourceType = type;
                if (string.IsNullOrWhiteSpace(existingSource.UserDefinedName))
                    existingSource.UserDefinedName = sensor.Name;
                existingSource.EnsureDisplayMetadata();
            }

            if (sensor.SensorType == SensorType.Control)
            {
                _controlSensorsByKey[key] = sensor;
                EnsureFanForControlSensor(hardware, sensor, key);
            }
        }

        foreach (IHardware sub in hardware.SubHardware)
        {
            sub.Update();
            VisitHardware(sub);
        }
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
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.Value is not { } value) continue;

            string key = BuildSensorKey(hardware, sensor);
            readings.Add(new SensorReading(key, hardware.Name, sensor.Name, sensor.SensorType, value));
        }

        foreach (IHardware sub in hardware.SubHardware) CaptureFromHardware(sub, readings);
    }

    private void PushSensorReadings(IReadOnlyList<SensorReading> readings)
    {
        foreach (SensorReading reading in readings)
        {
            DataSource? source = DataSource.Find(reading.Key);
            source?.SetValue((long)Math.Round(reading.Value * 1000.0));

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
                        fan.CurrentRPM = (int)Math.Round(reading.Value);
                        UpdateFanFunctionalState(fan);
                    }
                    break;
                }
            }
        }
    }

    // Promote a Control sensor into a Fan model entry if we haven't seen this key before.
    private void EnsureFanForControlSensor(IHardware hardware, ISensor sensor, string key)
    {
        if (FindFanByControlKey(key) != null) return;

        Fan fan = new()
        {
            DataSourceKey = key,
            ControllerModel = hardware.Name,
            ControlsName = "Controls",
            FansName = sensor.Name,
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
        return string.Equals(name, "None", StringComparison.OrdinalIgnoreCase) ? string.Empty : name;
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
        PersistLiveState(save: true);
    }

    private static bool IsControlWriteProperty(string propertyName) =>
        propertyName is nameof(Fan.FanDisplayedValue)
            or nameof(Fan.CurrentControlMode)
            or nameof(Fan.RPMMode)
            or nameof(Fan.MaxRPM)
            or nameof(Fan.ForcedNonFunctioning)
            or nameof(Fan.ForceNonFunctional);

    public void QueueFanControlWriteForCurrentState(Fan fan)
    {
        if (_disposed || string.IsNullOrWhiteSpace(fan.DataSourceKey)) return;

        FanControlWriteRequest request = fan is { ForcedNonFunctioning: false, CurrentControlMode: FanControlMode.Manual }
            ? FanControlWriteRequest.Software(fan.DataSourceKey, ResolveManualDutyCyclePercent(fan))
            : FanControlWriteRequest.Default(fan.DataSourceKey);

        lock (_controlWriteQueueLock)
        {
            _pendingControlWrites[request.DataSourceKey] = request;
            if (_controlWriteWorkerScheduled) return;

            _controlWriteWorkerScheduled = true;
            _ = Task.Run(ProcessQueuedControlWrites);
        }
    }

    private void ProcessQueuedControlWrites()
    {
        while (!_disposed)
        {
            List<FanControlWriteRequest> batch;
            lock (_controlWriteQueueLock)
            {
                if (_pendingControlWrites.Count == 0)
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

    private void ApplyControlWrite(FanControlWriteRequest request)
    {
        lock (_hardwareLock)
        {
            if (!_controlSensorsByKey.TryGetValue(request.DataSourceKey, out ISensor? sensor))
            {
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

            float value = ClampToSoftwareRange(control, request.DutyCyclePercent);
            control.SetSoftware(value);
        }
    }

    private static double ResolveManualDutyCyclePercent(Fan fan)
    {
        double value = Math.Clamp(fan.FanDisplayedValue, 0, Math.Max(1, fan.FanSliderMaximum));
        if (fan.RPMMode)
        {
            double rpmReference = Math.Max(1, fan.FanSliderMaximum);
            value = value / rpmReference * 100.0;
        }

        return Math.Clamp(value, 0.0, 100.0);
    }

    private static float ClampToSoftwareRange(IControl control, double dutyCyclePercent)
    {
        float min = float.IsNaN(control.MinSoftwareValue) ? 0.0f : control.MinSoftwareValue;
        float max = float.IsNaN(control.MaxSoftwareValue) ? 100.0f : control.MaxSoftwareValue;
        if (max < min) (min, max) = (max, min);

        return (float)Math.Clamp(dutyCyclePercent, min, max);
    }

    private static void UpdateFanFunctionalState(Fan fan)
    {
        if (fan.ForcedNonFunctioning)
        {
            fan.CurrentState = FanState.Detached;
            return;
        }

        fan.CurrentState = fan is { CurrentDutyCycle: > 0.001, CurrentRPM: <= 0 }
            ? FanState.Detached
            : FanState.Normal;
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
            if (string.Equals(fan.DataSourceKey, key, StringComparison.OrdinalIgnoreCase)) return fan;
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
        string controller = hardware.Name.Replace(' ', '_');
        string folder = sensor.SensorType.ToString();
        string leaf = sensor.Name.Replace(' ', '_');
        return $"{controller}.{folder}.{leaf}";
    }

    private static DataSourceTypeEnum MapSensorType(SensorType t) => t switch
    {
        SensorType.Voltage      => DataSourceTypeEnum.Voltage,
        SensorType.Current      => DataSourceTypeEnum.Current,
        SensorType.Power        => DataSourceTypeEnum.Power,
        SensorType.Clock        => DataSourceTypeEnum.Clock,
        SensorType.Temperature  => DataSourceTypeEnum.Temperature,
        SensorType.Load         => DataSourceTypeEnum.Load,
        SensorType.Fan          => DataSourceTypeEnum.Fan,
        SensorType.Flow         => DataSourceTypeEnum.Flow,
        SensorType.Control      => DataSourceTypeEnum.Control,
        SensorType.Level        => DataSourceTypeEnum.Level,
        SensorType.Data         => DataSourceTypeEnum.Data,
        SensorType.Throughput   => DataSourceTypeEnum.Throughput,
        _                       => DataSourceTypeEnum.Unknown,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try
        {
            lock (_hardwareLock) _computer.Close();
        }
        catch { /* swallow shutdown noise */ }
    }

    private readonly record struct FanControlWriteRequest(
        string DataSourceKey,
        double DutyCyclePercent,
        bool UseDefault)
    {
        public static FanControlWriteRequest Software(string dataSourceKey, double dutyCyclePercent) =>
            new(dataSourceKey, dutyCyclePercent, UseDefault: false);

        public static FanControlWriteRequest Default(string dataSourceKey) =>
            new(dataSourceKey, 0.0, UseDefault: true);
    }

    private readonly record struct SensorReading(
        string Key,
        string HardwareName,
        string SensorName,
        SensorType SensorType,
        float Value);
}
