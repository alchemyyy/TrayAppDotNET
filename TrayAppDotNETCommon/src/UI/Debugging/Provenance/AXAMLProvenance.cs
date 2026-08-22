#if DEBUG
using System.Reflection;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Identifies one source-level AXAML construct recorded for debug inspection.</summary>
public enum AXAMLProvenanceKind
{
    ResourceDefinition,
    PropertyAssignment,
    ResourceReference,
    Style,
    StyleSetter,
    ControlTheme,
    Template,
    Binding
}

/// <summary>Maps one generated AXAML construct to its repository source location.</summary>
public readonly record struct AXAMLProvenanceEntry(
    AXAMLProvenanceKind Kind,
    string SourcePath,
    int Line,
    int Column,
    string OwnerTypeName,
    string ElementTypeName,
    string ElementPath,
    string? ControlName,
    string? PropertyName,
    string? ResourceKey,
    string? ValueExpression,
    string? Selector);

/// <summary>Stores generated AXAML catalogs registered by loaded TrayAppDotNET assemblies.</summary>
internal static class AXAMLProvenanceRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Assembly, IReadOnlyList<AXAMLProvenanceEntry>> EntriesByAssembly = [];
    private static readonly Dictionary<string, List<AXAMLProvenanceEntry>> EntriesByProperty =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<AXAMLProvenanceEntry>> ResourceDefinitionsByKey =
        new(StringComparer.Ordinal);

    public static void Register(Assembly assembly, IReadOnlyList<AXAMLProvenanceEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(entries);

        lock (Sync)
        {
            EntriesByAssembly[assembly] = entries;
            RebuildIndexes();
        }
    }

    public static IReadOnlyList<AXAMLProvenanceEntry> FindPropertyEntries(
        IReadOnlyList<string> ownerTypeNames,
        string elementTypeName,
        string? controlName,
        string propertyName)
    {
        lock (Sync)
        {
            string lookupName = PropertyLookupName(propertyName);
            if (!EntriesByProperty.TryGetValue(lookupName, out List<AXAMLProvenanceEntry>? candidates))
                return [];

            List<(AXAMLProvenanceEntry Entry, int Score)> matches = [];
            foreach (AXAMLProvenanceEntry candidate in candidates)
            {
                bool ownerMatches = OwnerMatches(candidate.OwnerTypeName, ownerTypeNames);
                bool reusableCandidate = IsReusableCandidate(candidate);
                if (!ownerMatches && !reusableCandidate) continue;

                bool elementMatches = ElementMatches(candidate.ElementTypeName, elementTypeName);
                if (!elementMatches && !IsPropertyRoleCandidate(candidate.Kind)) continue;
                if (!string.IsNullOrWhiteSpace(candidate.ControlName)
                    && !string.Equals(candidate.ControlName, controlName, StringComparison.Ordinal)) continue;

                int score = 0;
                if (ownerMatches) score += 4;
                if (elementMatches) score += 2;
                if (!string.IsNullOrWhiteSpace(candidate.ControlName)) score += 8;
                if (SelectorMentionsElement(candidate.Selector, elementTypeName)) score += 1;
                matches.Add((candidate, score));
            }

            matches.Sort(static (left, right) =>
            {
                int scoreComparison = right.Score.CompareTo(left.Score);
                if (scoreComparison != 0) return scoreComparison;

                int pathComparison = string.Compare(
                    left.Entry.SourcePath,
                    right.Entry.SourcePath,
                    StringComparison.OrdinalIgnoreCase);
                return pathComparison != 0
                    ? pathComparison
                    : left.Entry.Line.CompareTo(right.Entry.Line);
            });

            List<AXAMLProvenanceEntry> orderedEntries = [];
            foreach ((AXAMLProvenanceEntry entry, int _) in matches)
                orderedEntries.Add(entry);

            return orderedEntries;
        }
    }

    public static IReadOnlyList<AXAMLProvenanceEntry> FindResourceDefinitions(string resourceKey)
    {
        lock (Sync)
        {
            return ResourceDefinitionsByKey.TryGetValue(resourceKey, out List<AXAMLProvenanceEntry>? entries)
                ? entries.ToArray()
                : [];
        }
    }

    private static void RebuildIndexes()
    {
        EntriesByProperty.Clear();
        ResourceDefinitionsByKey.Clear();

        foreach (IReadOnlyList<AXAMLProvenanceEntry> assemblyEntries in EntriesByAssembly.Values)
        {
            foreach (AXAMLProvenanceEntry entry in assemblyEntries)
            {
                if (!string.IsNullOrWhiteSpace(entry.PropertyName))
                    Add(EntriesByProperty, PropertyLookupName(entry.PropertyName), entry);

                if (entry.Kind == AXAMLProvenanceKind.ResourceDefinition
                    && !string.IsNullOrWhiteSpace(entry.ResourceKey))
                    Add(ResourceDefinitionsByKey, entry.ResourceKey, entry);
            }
        }
    }

    private static void Add(
        Dictionary<string, List<AXAMLProvenanceEntry>> index,
        string key,
        AXAMLProvenanceEntry entry)
    {
        if (!index.TryGetValue(key, out List<AXAMLProvenanceEntry>? entries))
        {
            entries = [];
            index.Add(key, entries);
        }

        entries.Add(entry);
    }

    private static bool OwnerMatches(string candidate, IReadOnlyList<string> ownerTypeNames)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return true;

        foreach (string ownerTypeName in ownerTypeNames)
        {
            if (string.Equals(candidate, ownerTypeName, StringComparison.Ordinal)
                || ownerTypeName.EndsWith('.' + candidate, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static bool ElementMatches(string candidate, string elementTypeName)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return true;

        int namespaceSeparator = candidate.LastIndexOf(':');
        string candidateTypeName = namespaceSeparator >= 0 ? candidate[(namespaceSeparator + 1)..] : candidate;
        return string.Equals(candidateTypeName, elementTypeName, StringComparison.Ordinal)
               || elementTypeName.EndsWith('.' + candidateTypeName, StringComparison.Ordinal);
    }

    private static bool IsReusableCandidate(AXAMLProvenanceEntry candidate) =>
        IsPropertyRoleCandidate(candidate.Kind)
        || !string.IsNullOrWhiteSpace(candidate.Selector)
        || candidate.ElementPath.Contains("Template[", StringComparison.Ordinal);

    private static bool IsPropertyRoleCandidate(AXAMLProvenanceKind kind) =>
        kind is AXAMLProvenanceKind.StyleSetter
            or AXAMLProvenanceKind.ControlTheme
            or AXAMLProvenanceKind.Template;

    private static bool SelectorMentionsElement(string? selector, string elementTypeName)
    {
        if (string.IsNullOrWhiteSpace(selector)) return false;

        int namespaceSeparator = elementTypeName.LastIndexOf('.');
        string shortElementTypeName = namespaceSeparator >= 0
            ? elementTypeName[(namespaceSeparator + 1)..]
            : elementTypeName;
        return selector.Contains(shortElementTypeName, StringComparison.Ordinal);
    }

    private static string PropertyLookupName(string propertyName)
    {
        string normalizedName = propertyName.Trim();
        if (normalizedName.Length >= 2 && normalizedName[0] == '(' && normalizedName[^1] == ')')
            normalizedName = normalizedName[1..^1];

        int separatorIndex = normalizedName.LastIndexOf('.');
        return separatorIndex >= 0 && separatorIndex < normalizedName.Length - 1
            ? normalizedName[(separatorIndex + 1)..]
            : normalizedName;
    }
}
#endif
