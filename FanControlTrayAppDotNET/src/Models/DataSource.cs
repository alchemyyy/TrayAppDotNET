using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using NCalc;

namespace FanControlTrayAppDotNET.Models;

// Kind of value a DataSource exposes. Mirrors the slice of LibreHardwareMonitor's
// SensorType we actually care about plus a Custom bucket for user-defined or computed sources.
public enum DataSourceTypeEnum
{
    Unknown,
    Temperature,
    Voltage,
    Current,
    Power,
    Load,
    Clock,
    Fan, // RPM
    Control, // PWM duty cycle %
    Level,
    Flow,
    Data, // Bytes
    Throughput, // Bytes/s
    Custom,
}

// Single numeric signal source consumed by curves and triggers. May be a sensor read from LHM,
// or a computed value derived from another DataSource through a TransformString (NCalc expression).
//
// Lifetime: instances live in the static DataSources registry, keyed by DataSourceKey. Producers
// (e.g. LHMService) own the write side and call SetValue() on every poll; consumers (curves,
// triggers, UI) subscribe to PropertyChanged.
public class DataSource : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // Global registry of DataSources keyed by DataSourceKey. The producing service is responsible
    // for Register/Unregister; consumers just look up by key.
    public static readonly Dictionary<string, DataSource> DataSources = new(StringComparer.OrdinalIgnoreCase);

    // Period-delimited fully-qualified name. For LHM sources this is the LHM tree path with spaces
    // replaced by underscores; that lets us round-trip a serialized assignment back to a live source
    // even if the hardware is enumerated in a different order between runs.
    [XmlAttribute] public string DataSourceKey { get; set; } = string.Empty;

    // User-facing name (rename target). Falls back to a parsed-from-key default until the user changes it.
    [XmlAttribute] public string UserDefinedName { get; set; } = string.Empty;

    // The hardware controller (motherboard / GPU / etc.) that owns this signal. Parsed from
    // DataSourceKey for LHM sources, manually supplied for custom sources.
    [XmlAttribute] public string ControllerName { get; set; } = string.Empty;

    [XmlAttribute] public DataSourceTypeEnum DataSourceType { get; set; } = DataSourceTypeEnum.Unknown;

    // User-facing graph metadata. Values are stored in the same human-readable units shown by the
    // editor, while Value itself remains the existing milli-unit long for change detection.
    [XmlAttribute] public double MinimumValue { get; set; } = double.NaN;

    [XmlAttribute] public double MaximumValue { get; set; } = double.NaN;

    [XmlAttribute] public string Unit { get; set; } = string.Empty;

    // Stored as long: temperature in milli-degrees, voltage in millivolts, RPM as integer, duty
    // cycle as percent * 1000, etc. A long avoids float-quantization noise in change detection
    // without losing the precision LHM exposes via doubles.
    [XmlAttribute]
    public long Value
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _lastChanged = DateTime.UtcNow;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LastChanged));
        }
    }

    private DateTime _lastChanged = DateTime.UtcNow;

    // Tracks when Value last actually changed (not just when it was last written). Curves and
    // triggers consult this for "stable for N ms" hysteresis checks.
    [XmlIgnore]
    public DateTime LastChanged
    {
        get => _lastChanged;
        private set
        {
            if (_lastChanged == value) return;
            _lastChanged = value;
            OnPropertyChanged();
        }
    }

    // NCalc expression applied to the raw upstream value. Empty string == passthrough.
    // Examples: "x * 1.8 + 32" (Celsius to Fahrenheit), "max(x, 50)" (floor at 50).
    // The single free variable is named "x" by convention.
    [XmlAttribute] public string TransformString { get; set; } = string.Empty;

    // Producer-side setter that runs the TransformString before storing. If the expression
    // throws or fails to parse, the raw value is stored unchanged so a broken transform never
    // freezes the signal.
    public void SetValue(long rawValue)
    {
        if (string.IsNullOrWhiteSpace(TransformString))
        {
            Value = rawValue;
            return;
        }

        try
        {
            Expression expression = new(TransformString) { Parameters = { ["x"] = (double)rawValue } };
            object? result = expression.Evaluate();
            if (result is IConvertible convertible)
            {
                Value = (long)Math.Round(Convert.ToDouble(convertible));
                return;
            }
        }
        catch
        {
            // Fall through to raw on any NCalc error. Visible via the unchanged LastChanged stamp.
        }

        Value = rawValue;
    }

    [XmlIgnore] public double DisplayValue => Value / 1000.0;

    [XmlIgnore] public string DisplayName =>
        string.IsNullOrWhiteSpace(UserDefinedName)
            ? string.IsNullOrWhiteSpace(DataSourceKey) ? "Data source" : DataSourceKey.Split('.').Last()
            : UserDefinedName;

    [XmlIgnore]
    public double DisplayMinimum => double.IsFinite(MinimumValue)
        ? MinimumValue
        : DefaultMetadata(DataSourceType).Minimum;

    [XmlIgnore]
    public double DisplayMaximum
    {
        get
        {
            double max = double.IsFinite(MaximumValue) ? MaximumValue : DefaultMetadata(DataSourceType).Maximum;
            return max <= DisplayMinimum ? DisplayMinimum + 1.0 : max;
        }
    }

    [XmlIgnore]
    public string DisplayUnit => !string.IsNullOrWhiteSpace(Unit)
        ? Unit
        : DefaultMetadata(DataSourceType).Unit;

    public void EnsureDisplayMetadata()
    {
        (double minimum, double maximum, string unit) = DefaultMetadata(DataSourceType);
        if (!double.IsFinite(MinimumValue)) MinimumValue = minimum;
        if (!double.IsFinite(MaximumValue) || MaximumValue <= MinimumValue) MaximumValue = maximum;
        if (string.IsNullOrWhiteSpace(Unit)) Unit = unit;
    }

    public static void Register(DataSource source)
    {
        if (string.IsNullOrEmpty(source.DataSourceKey)) return;
        DataSources[source.DataSourceKey] = source;
    }

    public static void Unregister(string key) => DataSources.Remove(key);

    public static DataSource? Find(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return DataSources.GetValueOrDefault(key);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static (double Minimum, double Maximum, string Unit) DefaultMetadata(DataSourceTypeEnum type) => type switch
    {
        DataSourceTypeEnum.Temperature => (0.0, 100.0, "C"),
        DataSourceTypeEnum.Voltage => (0.0, 2.0, "V"),
        DataSourceTypeEnum.Current => (0.0, 100.0, "A"),
        DataSourceTypeEnum.Power => (0.0, 500.0, "W"),
        DataSourceTypeEnum.Load => (0.0, 100.0, "%"),
        DataSourceTypeEnum.Clock => (0.0, 6000.0, "MHz"),
        DataSourceTypeEnum.Fan => (0.0, 5000.0, "RPM"),
        DataSourceTypeEnum.Control => (0.0, 100.0, "%"),
        DataSourceTypeEnum.Level => (0.0, 100.0, "%"),
        DataSourceTypeEnum.Flow => (0.0, 300.0, "L/h"),
        DataSourceTypeEnum.Data => (0.0, 1024.0, "GB"),
        DataSourceTypeEnum.Throughput => (0.0, 1024.0, "MB/s"),
        _ => (0.0, 100.0, string.Empty),
    };
}
