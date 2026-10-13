using System.Globalization;
using System.Text;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Scenarios;

/// <summary>One timed event in a scenario.</summary>
internal readonly record struct ScenarioStep(TimeSpan At, string Description, Action<SimulationWorld> Apply);

/// <summary>
/// A timeline of user, monitor, OS and fault events, followed by a quiet tail long enough for recovery to finish.
/// The oracle's settled checks run at the end of the tail and at every explicit checkpoint.
/// </summary>
internal sealed class ScenarioScript(string name)
{
    public string Name { get; } = name;
    public List<ScenarioStep> Steps { get; } = [];
    public List<TimeSpan> Checkpoints { get; } = [];
    public TimeSpan Tail { get; set; } = TimeSpan.FromSeconds(75);

    public ScenarioScript At(double seconds, string description, Action<SimulationWorld> apply)
    {
        Steps.Add(new ScenarioStep(TimeSpan.FromSeconds(seconds), description, apply));
        return this;
    }

    /// <summary>Runs the settled checks at <paramref name="seconds"/> in addition to the end of the tail.</summary>
    public ScenarioScript CheckAt(double seconds)
    {
        Checkpoints.Add(TimeSpan.FromSeconds(seconds));
        return this;
    }

    public TimeSpan End => (Steps.Count == 0 ? TimeSpan.Zero : Steps.Max(step => step.At)) + Tail;

    public string Describe()
    {
        StringBuilder builder = new();
        builder.AppendLine($"scenario '{Name}' ({Steps.Count} steps, ends at {End.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s)");
        foreach (ScenarioStep step in Steps.OrderBy(step => step.At))
            builder.AppendLine($"  {step.At.TotalSeconds,8:F3}s {step.Description}");
        return builder.ToString();
    }
}
