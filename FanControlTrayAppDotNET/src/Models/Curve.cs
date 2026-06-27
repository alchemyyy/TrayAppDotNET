using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace FanControlTrayAppDotNET.Models;

// Maps a DataSource value (X) to a fan output value (Y). The output axis is duty cycle % by
// default; per-fan RPMMode flips both the curve's stored points and any clamps to RPM units.
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
    [XmlAttribute] public int SmoothingFactor { get; set; } = 50;

    // Editor output unit. When true CurveNodes.Y is interpreted as RPM; otherwise it is duty %.
    [XmlAttribute] public bool RPMMode { get; set; }

    [XmlAttribute] public int MinRPM { get; set; }

    [XmlAttribute] public int MaxRPM { get; set; } = 3000;

    [XmlAttribute] public int MinDutyCycle { get; set; }

    [XmlAttribute] public int MaxDutyCycle { get; set; } = 100;

    // When enabled, the editor and evaluator use a non-decreasing projection of the node list.
    // The raw node positions remain untouched until the user disables the feature from the editor.
    [XmlAttribute] public bool PreventDecreasing { get; set; } = true;

    // Output clamps. ClampDutyMin/Max bound the Y axis after interpolation.
    [XmlAttribute] public int ClampDutyMin { get; set; }

    [XmlAttribute] public int ClampDutyMax { get; set; } = 100;

    // Input clamps. ClampXMin/Max bound the X lookup before sampling. Values outside this range
    // clamp to the corresponding edge sample.
    [XmlAttribute] public int ClampXMin { get; set; }

    [XmlAttribute] public int ClampXMax { get; set; } = 100;

    // Minimum dwell time before the curve commits a new Y to the fan, in milliseconds. Used by
    // FanControlService to suppress rapid Y oscillations across a hot sample boundary.
    [XmlAttribute] public int HysteresisMs { get; set; }

    // Key into DataSource.DataSources. Stored as a string so a curve can survive serialization
    // without holding a reference to a live DataSource instance.
    [XmlAttribute] public string SelectedDataSourceKey { get; set; } = string.Empty;

    // Convenience lookup that resolves the key against the global registry. Returns null if the
    // referenced source has not been registered yet (e.g. before LHMService initial sweep).
    [XmlIgnore] public DataSource? SelectedDataSource => DataSource.Find(SelectedDataSourceKey);

    // Monotonically incremented on every shape-affecting mutation. Caches that derive computed
    // state from a curve check this token instead of subscribing to PropertyChanged for every node.
    [XmlIgnore] public int Version { get; private set; }

    public void BumpVersion()
    {
        Version++;
        OnPropertyChanged(nameof(Version));
    }

    // Sample the curve at x. Returns a Y clamped to [ClampDutyMin, ClampDutyMax]. The X lookup is
    // clamped to [ClampXMin, ClampXMax] before interpolation. Handles empty / single-node curves
    // gracefully so callers don't need to defensive-check.
    public double Evaluate(double x)
    {
        if (CurveNodes.Count == 0) return ClampDutyMin;

        double xClamped = Math.Clamp(x, ClampXMin, ClampXMax);

        if (CurveNodes.Count == 1) return ClampOutput(CurveNodes[0].Y);

        List<CurveNode> ordered = EffectiveCurveNodes();
        int n = ordered.Count;

        double[] xs = new double[n];
        double[] ys = new double[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = ordered[i].X;
            ys[i] = ordered[i].Y;
        }

        double smoothness = Math.Clamp(SmoothingFactor / 100.0, 0.0, 1.0);
        double linear = InterpolateLinear(xs, ys, xClamped);
        if (smoothness <= 0.0) return ClampOutput(linear);

        double[] tangents = ComputeMonotonicTangents(xs, ys);
        double cubic = InterpolateMonotonicCubic(xs, ys, tangents, xClamped);
        return ClampOutput(linear + (cubic - linear) * smoothness);
    }

    private double ClampOutput(double y) => RPMMode
        ? Math.Clamp(y, MinRPM, Math.Max(MinRPM, MaxRPM))
        : Math.Clamp(y, MinDutyCycle, Math.Max(MinDutyCycle, MaxDutyCycle));

    public double ActiveYMinimum => RPMMode ? 0.0 : 0.0;

    public double ActiveYMaximum => RPMMode
        ? Math.Max(1, MaxRPM)
        : Math.Max(1, MaxDutyCycle);

    public string ActiveYSuffix => RPMMode ? "RPM" : "%";

    public int ActiveYMinLine => RPMMode ? MinRPM : MinDutyCycle;

    public void EnsureEditorDefaults(int defaultMaxRpm)
    {
        if (MaxRPM <= 0) MaxRPM = Math.Max(1, defaultMaxRpm);
        if (MaxDutyCycle <= 0) MaxDutyCycle = 100;
        MinRPM = Math.Clamp(MinRPM, 0, MaxRPM);
        MinDutyCycle = Math.Clamp(MinDutyCycle, 0, MaxDutyCycle);
        ClampDutyMax = Math.Max(ClampDutyMin, ClampDutyMax);
    }

    public List<CurveNode> EffectiveCurveNodes()
    {
        List<CurveNode> ordered = [.. CurveNodes.Select(static n => new CurveNode(n.X, n.Y))];
        ordered.Sort((a, b) => a.X.CompareTo(b.X));
        if (!PreventDecreasing) return ordered;

        double floor = double.NegativeInfinity;
        foreach (CurveNode node in ordered)
        {
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

    // Piecewise-linear sample. Clamps out-of-range to nearest edge.
    private static double InterpolateLinear(double[] xs, double[] ys, double x)
    {
        int n = xs.Length;
        if (x <= xs[0]) return ys[0];
        if (x >= xs[n - 1]) return ys[n - 1];

        for (int i = 0; i < n - 1; i++)
        {
            if (x >= xs[i] && x <= xs[i + 1])
            {
                double span = xs[i + 1] - xs[i];
                if (span <= 0.0) return ys[i];
                double t = (x - xs[i]) / span;
                return ys[i] + t * (ys[i + 1] - ys[i]);
            }
        }

        return ys[n - 1];
    }

    // Fritsch-Butland monotonic tangents. Preserves monotonicity in the cubic-Hermite output so
    // a non-decreasing curve stays non-decreasing after smoothing. One tangent per node.
    private static double[] ComputeMonotonicTangents(double[] xs, double[] ys)
    {
        int n = xs.Length;
        double[] tangents = new double[n];
        if (n < 2) return tangents;

        double[] slopes = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            double dx = xs[i + 1] - xs[i];
            slopes[i] = dx == 0.0 ? 0.0 : (ys[i + 1] - ys[i]) / dx;
        }

        tangents[0] = slopes[0];
        tangents[n - 1] = slopes[n - 2];

        for (int i = 1; i < n - 1; i++)
        {
            if (slopes[i - 1] * slopes[i] <= 0.0)
            {
                tangents[i] = 0.0;
                continue;
            }

            double w1 = 2.0 * (xs[i + 1] - xs[i]) + (xs[i] - xs[i - 1]);
            double w2 = xs[i + 1] - xs[i] + 2.0 * (xs[i] - xs[i - 1]);
            tangents[i] = (w1 + w2) / (w1 / slopes[i - 1] + w2 / slopes[i]);
        }

        return tangents;
    }

    // Monotonic cubic Hermite spline through (xs[i], ys[i]) with tangents[i]. Clamps out-of-range.
    private static double InterpolateMonotonicCubic(double[] xs, double[] ys, double[] tangents, double x)
    {
        int n = xs.Length;
        if (x <= xs[0]) return ys[0];
        if (x >= xs[n - 1]) return ys[n - 1];

        for (int i = 0; i < n - 1; i++)
        {
            if (x >= xs[i] && x <= xs[i + 1])
            {
                double h = xs[i + 1] - xs[i];
                if (h <= 0.0) return ys[i];
                double t = (x - xs[i]) / h;
                double t2 = t * t;
                double t3 = t2 * t;
                double h00 = 2.0 * t3 - 3.0 * t2 + 1.0;
                double h10 = t3 - 2.0 * t2 + t;
                double h01 = -2.0 * t3 + 3.0 * t2;
                double h11 = t3 - t2;
                return h00 * ys[i] + h10 * h * tangents[i]
                                   + h01 * ys[i + 1] + h11 * h * tangents[i + 1];
            }
        }

        return ys[n - 1];
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
