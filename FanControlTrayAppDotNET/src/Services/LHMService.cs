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
//   * Writeback to fan controls (curve evaluation, manual slider, jumpstart, delta limiting)
//   * Disabled/Detached state classification beyond a naive check
//   * Fan-control writeback (curve evaluation, manual slider, jumpstart, delta limiting)
public sealed class LHMService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly AppSettings? _settings;
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

        _computer.Open();
        // Cold enumeration runs on whatever thread starts us (the UI thread at startup). That's
        // fine because it only mutates Fans / DataSources once and the UI hasn't bound yet.
        RebuildFromHardware();

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
            try
            {
                foreach (IHardware hardware in _computer.Hardware)
                {
                    hardware.Update();
                    foreach (IHardware sub in hardware.SubHardware) sub.Update();
                }
            }
            catch (Exception ex) { TADNLog.Log($"LHMService poll (hardware update) failed: {ex.Message}"); }

            try
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    PushSensorReadings();
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
            }

            if (sensor.SensorType == SensorType.Control)
            {
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
    private void PushSensorReadings()
    {
        foreach (IHardware hardware in _computer.Hardware)
        {
            PushFromHardware(hardware);
        }
    }

    private void PushFromHardware(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.Value is not float value) continue;

            string key = BuildSensorKey(hardware, sensor);
            DataSource? source = DataSource.Find(key);
            source?.SetValue((long)Math.Round(value * 1000.0));

            switch (sensor.SensorType)
            {
                case SensorType.Control:
                {
                    Fan? fan = FindFanByControlKey(key);
                    if (fan != null)
                    {
                        fan.CurrentDutyCycle = value;
                        UpdateFanFunctionalState(fan);
                    }
                    break;
                }
                case SensorType.Fan:
                {
                    Fan? fan = FindFanByFanSensor(hardware, sensor);
                    if (fan != null)
                    {
                        fan.CurrentRPM = (int)Math.Round(value);
                        UpdateFanFunctionalState(fan);
                    }
                    break;
                }
            }
        }

        foreach (IHardware sub in hardware.SubHardware) PushFromHardware(sub);
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
        if (_settings?.FindPersistedFan(key) is { } persisted)
        {
            fan.ApplyUserSettings(persisted);
        }
        UpdateFanFunctionalState(fan);

        fan.PropertyChanged += OnFanPropertyChanged;
        Fans.Add(fan);
        _settings?.UpsertPersistedFan(fan);
        _discoveryChanged = true;
    }

    private void ApplyDefaultsToNewFan(Fan fan)
    {
        if (_settings == null) return;

        fan.RPMMode = _settings.DefaultToRPMMode;
        fan.StartupSpeed = _settings.DefaultJumpstartDutyCycle;
        fan.DeltaMax = _settings.DefaultDeltaMaxDutyCycle;
        fan.AssignedCurveName = NormalizeCurveName(_settings.DefaultAssignedCurve);
        fan.CurrentControlMode = string.IsNullOrEmpty(fan.AssignedCurveName)
            ? FanControlMode.Manual
            : FanControlMode.Curve;
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
        _settings?.UpsertPersistedFan(fan);
        PersistLiveState(save: true);
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
        {
            if (string.Equals(fan.DataSourceKey, key, StringComparison.OrdinalIgnoreCase)) return fan;
        }
        return null;
    }

    // Heuristic pairing: a Fan-type sensor named "Fan #1" pairs with the Control-type sensor
    // "Fan Control #1" on the same hardware. Refined pairing (by index, by physical header) is
    // future-pass work.
    private Fan? FindFanByFanSensor(IHardware hardware, ISensor fanSensor)
    {
        foreach (Fan fan in Fans)
        {
            if (!string.Equals(fan.ControllerModel, hardware.Name, StringComparison.OrdinalIgnoreCase)) continue;
            if (fan.FansName.Contains(fanSensor.Name, StringComparison.OrdinalIgnoreCase)) return fan;
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
        try { _computer.Close(); } catch { /* swallow shutdown noise */ }
    }
}
