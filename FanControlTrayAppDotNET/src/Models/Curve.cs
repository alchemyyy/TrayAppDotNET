using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using TrayAppDotNETCommon.UI.Controls.Curves;

namespace FanControlTrayAppDotNET.Models;

// Maps a DataSource value (X) to a fan output value (Y). The output axis is duty cycle % by
// default; RPMMode flips the curve's stored points and clamps to RPM units.
//
// Architecture mirrors BrightnessTrayAppDotNET's EnvironmentalCurve: a list of control nodes plus
// a smoothness blend between piecewise-linear and monotonic cubic Hermite interpolation. We omit
// the "Follow the Sun" anchor concept since fan curves are temperature-driven, not time-driven.
public class Curve : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // Global registry keyed by CurveName. The editor adds/renames here; assignments on Fan
    // reference curves through this dictionary so a rename propagates.
    public static readonly Dictionary<string, Curve> Curves = new(StringComparer.OrdinalIgnoreCase);

    [XmlAttribute]
    public string CurveName
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    } = string.Empty;

    // Control points sorted by X at evaluation time. Two points minimum (a flat default line).
    [XmlArray("Nodes")]
    [XmlArrayItem("Node")]
    public List<CurveNode> CurveNodes { get; set; } = [];

    // Blend factor [0..100] between linear interpolation (0) and monotonic cubic Hermite (100).
    // Maps to a 0..1 multiplier inside the sampler.
    [XmlAttribute]
    public int SmoothingFactor { get; set; } = 50;

    // Editor output unit. When true CurveNodes.Y is interpreted as RPM; otherwise it is duty %.
    [XmlAttribute]
    public bool RPMMode { get; set; }

    [XmlAttribute]
    public int MinRPM { get; set; }

    [XmlAttribute]
    public int MaxRPM { get; set; } = 3000;

    [XmlAttribute]
    public int MinDutyCycle { get; set; }

    [XmlAttribute]
    public int MaxDutyCycle { get; set; } = 100;

    // When enabled, the editor and evaluator use a non-decreasing projection of the node list.
    // The raw node positions remain untouched until the user disables the feature from the editor.
    [XmlAttribute]
    public bool PreventDecreasing { get; set; } = true;

    // Output clamps. ClampDutyMin/Max bound the Y axis after interpolation.
    [XmlAttribute]
    public int ClampDutyMin { get; set; }

    [XmlAttribute]
    public int ClampDutyMax { get; set; } = 100;

    // Input clamps. ClampXMin/Max bound the X lookup before sampling and follow the bound data source's display
    // range (FollowDataSourceRange). Values outside this range clamp to the corresponding edge sample.
    [XmlAttribute]
    public int ClampXMin { get; set; }

    [XmlAttribute]
    public int ClampXMax { get; set; } = 100;

    // Minimum dwell time before the curve commits a new Y to the fan, in milliseconds. Used by
    // FanCurveController to suppress rapid Y oscillations across a hot sample boundary. 0 = off.
    [XmlAttribute]
    public int HysteresisMs { get; set; }

    // Key into DataSource.DataSources. Stored as a string so a curve can survive serialization
    // without holding a reference to a live DataSource instance.
    [XmlAttribute]
    public string SelectedDataSourceKey { get; set; } = string.Empty;

    // Convenience lookup that resolves the key against the global registry. Returns null if the
    // referenced source has not been registered yet (e.g. before LHMService initial sweep).
    [XmlIgnore]
    public DataSource? SelectedDataSource => DataSource.Find(SelectedDataSourceKey);

    // Monotonically incremented on every shape-affecting mutation. Caches that derive computed
    // state from a curve check this token instead of subscribing to PropertyChanged for every node.
    [XmlIgnore]
    public int Version { get; private set; }

    public void BumpVersion()
    {
        Version++;
        OnPropertyChanged(nameof(Version));
    }

    // Sample the curve at x. Returns a Y clamped to the active output range. The X lookup is clamped to
    // [ClampXMin, ClampXMax] before interpolation. Handles empty / single-node curves gracefully so callers don't
    // need to defensive-check.
    public double Evaluate(double x) => CreateEvaluator().Evaluate(x);

    /// <summary>
    /// Prepares the effective nodes and tangents once, for callers such as the editor that sample the curve many
    /// times. Its results are exactly those of <see cref="Evaluate"/>.
    /// </summary>
    public CurveEvaluator CreateEvaluator() => new(this);

    /// <summary>
    /// Sets the input clamp to the source's display range, so a curve evaluates the source it is bound to over that
    /// source's own axis. Returns whether the clamp changed.
    /// </summary>
    public bool FollowDataSourceRange(DataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int minimum = (int)Math.Round(source.DisplayMinimum);
        int maximum = (int)Math.Round(source.DisplayMaximum);
        if (minimum > maximum || (ClampXMin == minimum && ClampXMax == maximum)) return false;

        ClampXMin = minimum;
        ClampXMax = maximum;
        return true;
    }

    internal double ClampOutput(double y) => RPMMode
        ? Math.Clamp(y, MinRPM, Math.Max(MinRPM, MaxRPM))
        : Math.Clamp(y, MinDutyCycle, Math.Max(MinDutyCycle, MaxDutyCycle));

    public double ActiveYMinimum => RPMMode ? 0.0 : 0.0;

    public double ActiveYMaximum => RPMMode
        ? Math.Max(val1: 1, MaxRPM)
        : Math.Max(val1: 1, MaxDutyCycle);

    public string ActiveYSuffix => RPMMode ? "RPM" : "%";

    public int ActiveYMinLine => RPMMode ? MinRPM : MinDutyCycle;

    public void EnsureEditorDefaults(int defaultMaxRPM)
    {
        if (MaxRPM <= 0) MaxRPM = Math.Max(val1: 1, defaultMaxRPM);
        if (MaxDutyCycle <= 0) MaxDutyCycle = 100;
        MinRPM = Math.Clamp(MinRPM, min: 0, MaxRPM);
        MinDutyCycle = Math.Clamp(MinDutyCycle, min: 0, MaxDutyCycle);
        ClampDutyMax = Math.Max(ClampDutyMin, ClampDutyMax);
    }

    public List<CurveNode> EffectiveCurveNodes()
    {
        List<CurveNode> ordered = [.. CurveNodes.Select(static n => new CurveNode(n.X, n.Y))];
        ordered.Sort((a, b) => a.X.CompareTo(b.X));
        if (!PreventDecreasing || ordered.Count == 0) return ordered;

        double floor = ordered[0].Y;
        for (int i = 1; i < ordered.Count; i++)
        {
            CurveNode node = ordered[i];
            if (node.Y < floor) node.Y = floor;
            else floor = node.Y;
        }

        return ordered;
    }

    public void BurnInEffectiveNodes()
    {
        List<CurveNode> effective = EffectiveCurveNodes();
        Dictionary<CurveNode, int> positions = [];
        foreach (CurveNode node in CurveNodes)
            positions[node] = positions.Count;

        List<CurveNode> orderedRaw = [.. CurveNodes];
        orderedRaw.Sort((a, b) =>
        {
            int x = a.X.CompareTo(b.X);
            return x != 0 ? x : positions[a].CompareTo(positions[b]);
        });

        int count = Math.Min(orderedRaw.Count, effective.Count);
        for (int i = 0; i < count; i++)
            orderedRaw[i].Y = effective[i].Y;

        BumpVersion();
    }

    public static void Register(Curve curve)
    {
        if (string.IsNullOrEmpty(curve.CurveName)) return;
        Curves[curve.CurveName] = curve;
    }

    public static void Unregister(string name) => Curves.Remove(name);

    public static Curve? Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return Curves.GetValueOrDefault(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// One curve shape prepared for sampling: the effective (sorted, optionally non-decreasing) nodes and their secant-end
/// monotonic tangents. The input is clamped to the curve's input range first and the output to its active range last,
/// the order fan control applies.
/// </summary>
public sealed class CurveEvaluator
{
    private readonly Curve _curve;
    private readonly double[] _xs;
    private readonly double[] _ys;
    private readonly double[] _tangents;
    private readonly double _smoothness;

    internal CurveEvaluator(Curve curve)
    {
        _curve = curve;
        List<CurveNode> ordered = curve.CurveNodes.Count > 1 ? curve.EffectiveCurveNodes() : [];
        int count = ordered.Count;
        _xs = new double[count];
        _ys = new double[count];
        _tangents = new double[count];
        for (int i = 0; i < count; i++)
        {
            _xs[i] = ordered[i].X;
            _ys[i] = ordered[i].Y;
        }

        _smoothness = Math.Clamp(curve.SmoothingFactor / 100.0, min: 0.0, max: 1.0);
        if (_smoothness > 0.0)
            MonotonicCurveSampler.ComputeMonotonicTangents(_xs, _ys, _tangents, MonotonicEndTangent.Secant);
    }

    public double Evaluate(double x)
    {
        List<CurveNode> nodes = _curve.CurveNodes;
        if (nodes.Count == 0) return _curve.ClampDutyMin;

        double xClamped = Math.Clamp(x, _curve.ClampXMin, _curve.ClampXMax);
        if (nodes.Count == 1) return _curve.ClampOutput(nodes[0].Y);

        return _curve.ClampOutput(MonotonicCurveSampler.Sample(_xs, _ys, _tangents, xClamped, _smoothness));
    }
}
