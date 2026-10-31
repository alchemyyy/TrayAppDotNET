using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.Services;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

/// <summary>
/// Pins Fan's runtime curve output to the algorithm it had before the curve math moved to the Common sampler, and
/// checks the input clamp follows the bound source.
/// </summary>
public sealed class CurveEvaluatorTests
{
    public static TheoryData<string> Curves() => [.. CurveCases.Keys];

    private static readonly Dictionary<string, Func<Curve>> CurveCases = new()
    {
        ["two nodes, linear"] = () => DutyCurve(smoothing: 0, (0, 20), (100, 80)),
        ["three nodes, half smoothing"] = () => DutyCurve(smoothing: 50, (20, 25), (55, 40), (90, 100)),
        ["five nodes, full smoothing"] =
            () => DutyCurve(smoothing: 100, (10, 15), (30, 18), (50, 45), (70, 60), (95, 100)),
        ["dip with decreasing allowed"] =
            () => DutyCurve(smoothing: 70, preventDecreasing: false, (0, 30), (40, 70), (60, 35), (100, 90)),
        ["dip projected non-decreasing"] =
            () => DutyCurve(smoothing: 70, preventDecreasing: true, (0, 30), (40, 70), (60, 35), (100, 90)),
        ["nodes above max duty"] = () =>
        {
            Curve curve = DutyCurve(smoothing: 60, (30, 50), (60, 95), (90, 120));
            curve.MaxDutyCycle = 80;
            return curve;
        },
        ["min duty floor"] = () =>
        {
            Curve curve = DutyCurve(smoothing: 40, (10, 5), (50, 30), (90, 70));
            curve.MinDutyCycle = 25;
            return curve;
        },
        ["unsorted nodes with a shared x"] =
            () => DutyCurve(smoothing: 80, (80, 90), (20, 20), (50, 40), (50, 55), (0, 10)),
        ["narrow input clamp"] = () =>
        {
            Curve curve = DutyCurve(smoothing: 50, (0, 10), (50, 50), (100, 90));
            curve.ClampXMin = 30;
            curve.ClampXMax = 70;
            return curve;
        },
        ["rpm curve above max rpm"] = () => new Curve
        {
            RPMMode = true,
            MaxRPM = 2000,
            MinRPM = 300,
            SmoothingFactor = 50,
            CurveNodes = [new CurveNode(0, 200), new CurveNode(50, 1500), new CurveNode(100, 2600)]
        }
    };

    [Theory]
    [MemberData(nameof(Curves))]
    public void RuntimeOutputMatchesThePreviousAlgorithm(string name)
    {
        Curve curve = CurveCases[name]();
        CurveEvaluator evaluator = curve.CreateEvaluator();

        for (double x = -20; x <= 120; x += 0.25)
        {
            double expected = PreviousAlgorithm.Evaluate(curve, x);
            Assert.Equal(expected, curve.Evaluate(x), precision: 10);
            Assert.Equal(expected, evaluator.Evaluate(x), precision: 10);
        }
    }

    [Fact]
    public void NodesAboveMaxEvaluateToTheMaximumTheFanReceives()
    {
        // The audit's example: max 80 with (30,50),(60,95) gives 80 at x=50, not the 70 the editor used to draw
        Curve curve = DutyCurve(smoothing: 0, (30, 50), (60, 95));
        curve.MaxDutyCycle = 80;

        Assert.Equal(expected: 80, curve.Evaluate(50), precision: 10);
    }

    [Fact]
    public void InputClampFollowsTheBoundSourceRange()
    {
        Curve curve = DutyCurve(smoothing: 0, (0, 0), (500, 100));
        DataSource power = new() { DataSourceKey = "Test.Power", DataSourceType = DataSourceTypeEnum.Power };

        Assert.True(curve.FollowDataSourceRange(power));
        Assert.Equal(expected: 0, curve.ClampXMin);
        Assert.Equal(expected: 500, curve.ClampXMax);
        Assert.False(curve.FollowDataSourceRange(power));

        // A reading the stale 0..100 clamp would have pinned at 100 W now samples the curve
        Assert.Equal(expected: 60, curve.Evaluate(300), precision: 10);
    }

    [Fact]
    public void EvaluatorFollowsASourceWhoseRangeChanged()
    {
        Curve curve = DutyCurve(smoothing: 0, (0, 0), (500, 100));
        DataSource power = new() { DataSourceKey = "Test.Power", DataSourceType = DataSourceTypeEnum.Power };
        power.RecordSample(1, power.SetValue(300_000));

        Assert.True(FanCurveEvaluator.TryEvaluate(curve, power, out double target));
        Assert.Equal(expected: 60, target, precision: 10);
        Assert.Equal(expected: 500, curve.ClampXMax);
    }

    private static Curve DutyCurve(int smoothing, params (double X, double Y)[] nodes) =>
        DutyCurve(smoothing, preventDecreasing: true, nodes);

    private static Curve DutyCurve(int smoothing, bool preventDecreasing, params (double X, double Y)[] nodes) => new()
    {
        RPMMode = false,
        SmoothingFactor = smoothing,
        PreventDecreasing = preventDecreasing,
        CurveNodes = [.. nodes.Select(static node => new CurveNode(node.X, node.Y))]
    };

    /// <summary>Curve.Evaluate as it was before it moved onto the Common monotonic sampler.</summary>
    private static class PreviousAlgorithm
    {
        public static double Evaluate(Curve curve, double x)
        {
            if (curve.CurveNodes.Count == 0) return curve.ClampDutyMin;

            double xClamped = Math.Clamp(x, curve.ClampXMin, curve.ClampXMax);
            if (curve.CurveNodes.Count == 1) return ClampOutput(curve, curve.CurveNodes[0].Y);

            List<CurveNode> ordered = curve.EffectiveCurveNodes();
            int n = ordered.Count;
            double[] xs = new double[n];
            double[] ys = new double[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = ordered[i].X;
                ys[i] = ordered[i].Y;
            }

            double smoothness = Math.Clamp(curve.SmoothingFactor / 100.0, min: 0.0, max: 1.0);
            double linear = InterpolateLinear(xs, ys, xClamped);
            if (smoothness <= 0.0) return ClampOutput(curve, linear);

            double[] tangents = ComputeMonotonicTangents(xs, ys);
            double cubic = InterpolateMonotonicCubic(xs, ys, tangents, xClamped);
            return ClampOutput(curve, linear + (cubic - linear) * smoothness);
        }

        private static double ClampOutput(Curve curve, double y) => curve.RPMMode
            ? Math.Clamp(y, curve.MinRPM, Math.Max(curve.MinRPM, curve.MaxRPM))
            : Math.Clamp(y, curve.MinDutyCycle, Math.Max(curve.MinDutyCycle, curve.MaxDutyCycle));

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
    }
}
