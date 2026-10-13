using System.Globalization;
using System.Text;

namespace BrightnessTrayAppDotNET.DDCSimulator.Tests.Simulation;

/// <summary>One timeline line, stamped with virtual time.</summary>
internal readonly record struct TraceEntry(long Ticks, string Source, string Message)
{
    public override string ToString() =>
        TimeSpan.FromTicks(Ticks).TotalSeconds.ToString("F3", CultureInfo.InvariantCulture).PadLeft(9)
        + "s [" + Source + "] " + Message;
}

/// <summary>
/// Interleaved timeline of simulator events and the app's own log lines, all on the virtual clock.
/// Bounded so long fuzz runs cannot grow without limit; the newest lines win.
/// </summary>
internal sealed class SimulationTrace(SimulationClock clock, int capacity = 400_000)
{
    private readonly Lock _gate = new();
    private readonly Queue<TraceEntry> _entries = new();

    public void Add(string source, string message)
    {
        TraceEntry entry = new(clock.ElapsedTicks, source, message);
        lock (_gate)
        {
            if (_entries.Count >= capacity) _entries.Dequeue();
            _entries.Enqueue(entry);
        }
    }

    public List<TraceEntry> Snapshot()
    {
        lock (_gate) return [.. _entries];
    }

    /// <summary>Formats up to <paramref name="maximumLines"/> lines ending at <paramref name="untilTicks"/>.</summary>
    public string Format(int maximumLines, long untilTicks = long.MaxValue, Func<TraceEntry, bool>? filter = null)
    {
        List<TraceEntry> selected = [];
        foreach (TraceEntry entry in Snapshot())
        {
            if (entry.Ticks > untilTicks) break;
            if (filter != null && !filter(entry)) continue;

            selected.Add(entry);
        }

        int start = Math.Max(val1: 0, selected.Count - maximumLines);
        StringBuilder builder = new();
        for (int index = start; index < selected.Count; index++)
            builder.AppendLine(selected[index].ToString());

        return builder.ToString();
    }

    public void WriteTo(string path)
    {
        using StreamWriter writer = new(path, append: false, Encoding.UTF8);
        foreach (TraceEntry entry in Snapshot())
            writer.WriteLine(entry.ToString());
    }
}
