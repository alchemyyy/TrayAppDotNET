#if DEBUG
using Avalonia;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Maps one generated builder assignment to its runtime recording boundary.</summary>
public readonly record struct CSharpBuilderProvenanceEntry(
    string BoundarySourcePath,
    int BoundarySourceLine,
    AvaloniaProperty Property,
    DebugPropertyAssignmentOperation Operation,
    string ValueExpression,
    int AssignmentSourceLine,
    int AssignmentSourceColumn,
    string AssignmentSourceMember,
    string? ResourceKey);

/// <summary>Indexes generated builder assignments by their single runtime boundary call.</summary>
internal static class CSharpBuilderProvenanceRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, List<CSharpBuilderProvenanceEntry>> EntriesByBoundary =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(IReadOnlyList<CSharpBuilderProvenanceEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        lock (Sync)
        {
            foreach (CSharpBuilderProvenanceEntry entry in entries)
            {
                string key = BoundaryKey(entry.BoundarySourcePath, entry.BoundarySourceLine);
                if (!EntriesByBoundary.TryGetValue(key, out List<CSharpBuilderProvenanceEntry>? boundaryEntries))
                {
                    boundaryEntries = [];
                    EntriesByBoundary.Add(key, boundaryEntries);
                }

                boundaryEntries.Add(entry);
            }
        }
    }

    public static IReadOnlyList<CSharpBuilderProvenanceEntry> Find(string sourcePath, int sourceLine)
    {
        lock (Sync)
        {
            string key = BoundaryKey(DebugSourcePath.Normalize(sourcePath), sourceLine);
            return EntriesByBoundary.TryGetValue(key, out List<CSharpBuilderProvenanceEntry>? entries)
                ? entries.ToArray()
                : [];
        }
    }

    private static string BoundaryKey(string sourcePath, int sourceLine) =>
        DebugSourcePath.Normalize(sourcePath) + ":" + sourceLine.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
#endif
