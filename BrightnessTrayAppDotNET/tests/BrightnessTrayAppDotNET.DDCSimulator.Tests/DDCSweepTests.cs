using System.Diagnostics;
using System.Globalization;
using System.Text;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Scenarios;
using BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests;

/// <summary>
/// Generated scenario sweeps. Defaults keep a normal test run short; environment variables scale them up:
/// DDCSIM_SEEDS (random scenarios, default 24), DDCSIM_SEED_BASE (first seed, default 1), DDCSIM_SEED (one seed only),
/// DDCSIM_MATRIX=full (every one of the 384 matrix cells instead of every 16th), DDCSIM_TRACE=1 (write every scenario's
/// trace, not only failing ones).
/// Reports and traces land in %TEMP%\BrightnessTrayAppDotNET.DDCSimulator.Tests\&lt;pid&gt;.
/// </summary>
[Trait("Category", "Sweep")]
public sealed class DDCSweepTests(ITestOutputHelper output)
{
    private const int DefaultSeedCount = 24;
    private const int DefaultMatrixStride = 16;

    [Fact]
    public void RandomScenarioSweep()
    {
        List<int> seeds = [];
        if (TryReadInteger("DDCSIM_SEED", out int single))
        {
            seeds.Add(single);
        }
        else
        {
            int count = TryReadInteger("DDCSIM_SEEDS", out int configuredCount) ? configuredCount : DefaultSeedCount;
            int first = TryReadInteger("DDCSIM_SEED_BASE", out int configuredBase) ? configuredBase : 1;
            for (int seed = first; seed < first + count; seed++) seeds.Add(seed);
        }

        SweepSummary summary = new("random sweep");
        foreach (int seed in seeds)
        {
            (WorldConfiguration world, ScenarioScript script) = ScenarioGenerator.Generate(seed);
            summary.Add(RunOne(world, script));
        }

        Report(summary);
    }

    [Fact]
    public void CombinationMatrix()
    {
        bool full = string.Equals(Environment.GetEnvironmentVariable("DDCSIM_MATRIX"), "full", StringComparison.OrdinalIgnoreCase);
        List<MatrixCell> cells = [.. ScenarioGenerator.Matrix()];
        SweepSummary summary = new($"combination matrix ({(full ? "all" : $"every {DefaultMatrixStride}th of")} {cells.Count} cells)");
        for (int index = 0; index < cells.Count; index++)
        {
            if (!full && index % DefaultMatrixStride != 0) continue;

            (WorldConfiguration world, ScenarioScript script) = ScenarioGenerator.GenerateCell(cells[index], seed: 10_000 + index);
            summary.Add(RunOne(world, script));
        }

        Report(summary);
    }

    private static SweepEntry RunOne(WorldConfiguration world, ScenarioScript script)
    {
        world.AlwaysWriteTrace = Environment.GetEnvironmentVariable("DDCSIM_TRACE") == "1";
        Stopwatch real = Stopwatch.StartNew();
        try
        {
            using SimulationWorld simulation = new(world);
            ScenarioResult result = simulation.Run(script);
            return new SweepEntry(script.Name, world.Seed, result, Hang: null, real.Elapsed);
        }
        catch (SimulationHangException exception)
        {
            return new SweepEntry(script.Name, world.Seed, Result: null, exception.Message, real.Elapsed);
        }
    }

    private void Report(SweepSummary summary)
    {
        string text = summary.Format();
        string path = Path.Combine(TestEnvironment.RootDirectory, summary.FileName);
        File.WriteAllText(path, text + Environment.NewLine + summary.FormatDetails());
        output.WriteLine(text);
        output.WriteLine("full report: " + path);
        Assert.True(summary.Clean, text);
    }

    private static bool TryReadInteger(string name, out int value) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private sealed record SweepEntry(string Name, int Seed, ScenarioResult? Result, string? Hang, TimeSpan RealTime);

    private sealed class SweepSummary(string title)
    {
        private readonly List<SweepEntry> _entries = [];

        public bool Clean => _entries.All(entry => entry.Hang == null && entry.Result!.Passed);

        public string FileName => title.Split(' ')[0] + "-sweep-summary.txt";

        public void Add(SweepEntry entry) => _entries.Add(entry);

        public string Format()
        {
            StringBuilder builder = new();
            int failing = _entries.Count(entry => entry.Hang != null || !entry.Result!.Passed);
            builder.AppendLine(
                $"{title}: {_entries.Count} scenarios, {failing} with findings, "
                + $"{_entries.Sum(entry => entry.RealTime.TotalSeconds):F1}s real");

            Dictionary<string, List<SweepEntry>> byKind = new(StringComparer.Ordinal);
            foreach (SweepEntry entry in _entries)
            {
                IEnumerable<string> kinds = entry.Hang != null
                    ? ["Hang"]
                    : entry.Result!.Violations.Select(violation => violation.Kind.ToString()).Distinct();
                foreach (string kind in kinds)
                {
                    if (!byKind.TryGetValue(kind, out List<SweepEntry>? list)) byKind[kind] = list = [];
                    list.Add(entry);
                }
            }

            foreach ((string kind, List<SweepEntry> entries) in byKind.OrderByDescending(pair => pair.Value.Count))
            {
                builder.AppendLine(
                    $"  {kind}: {entries.Count} scenario(s), e.g. "
                    + string.Join(", ", entries.Take(6).Select(entry => entry.Name)));
            }

            foreach (SweepEntry entry in _entries.Where(entry => entry.Hang != null || !entry.Result!.Passed).Take(12))
            {
                string first = entry.Hang ?? entry.Result!.Violations[0].ToString();
                builder.AppendLine($"  {entry.Name}: {first[..Math.Min(first.Length, 400)]}");
            }

            return builder.ToString();
        }

        public string FormatDetails()
        {
            StringBuilder builder = new();
            builder.AppendLine("per-scenario cost:");
            foreach (SweepEntry entry in _entries.OrderByDescending(entry => entry.RealTime))
            {
                string stats = entry.Result?.Report.Split('\n').FirstOrDefault(line => line.StartsWith("stats:", StringComparison.Ordinal))?.Trim()
                               ?? "hung";
                builder.AppendLine($"  {entry.RealTime.TotalSeconds,7:F2}s {entry.Name}: {stats}");
            }

            builder.AppendLine();
            foreach (SweepEntry entry in _entries.Where(entry => entry.Hang != null || !entry.Result!.Passed))
            {
                builder.AppendLine(entry.Hang ?? entry.Result!.Report);
                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}
