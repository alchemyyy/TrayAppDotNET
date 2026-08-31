using System.Xml.Serialization;

namespace TaskManagerTrayAppDotNET.Models;

/// <summary>One named Processes-page search stored in Task Manager settings.</summary>
public sealed class ProcessSavedSearch
{
    [XmlAttribute]
    public string Name { get; set; } = string.Empty;

    [XmlAttribute]
    public string Query { get; set; } = string.Empty;
}

/// <summary>Normalizes and edits persisted process searches without sharing mutable entries.</summary>
internal static class ProcessSavedSearchCollection
{
    private const string DefaultNamePrefix = "Saved Search ";

    public static List<ProcessSavedSearch> Normalize(
        IEnumerable<ProcessSavedSearch>? searches)
    {
        List<ProcessSavedSearch> normalized = [];
        if (searches == null) return normalized;

        foreach (ProcessSavedSearch? search in searches)
        {
            if (search == null) continue;

            string query = search.Query?.Trim() ?? string.Empty;
            if (query.Length == 0) continue;

            string name = search.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
                name = ResolveNextDefaultName(normalized);
            normalized.Add(new ProcessSavedSearch
            {
                Name = name,
                Query = query
            });
        }

        return normalized;
    }

    /// <summary>Adds a query under the next non-conflicting one-based default name.</summary>
    public static List<ProcessSavedSearch> Add(
        IEnumerable<ProcessSavedSearch>? searches,
        string? query)
    {
        List<ProcessSavedSearch> updated = Normalize(searches);
        string normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length == 0) return updated;

        updated.Add(new ProcessSavedSearch
        {
            Name = ResolveNextDefaultName(updated),
            Query = normalizedQuery
        });
        return updated;
    }

    /// <summary>Renames one saved search while preserving its query and list position.</summary>
    public static List<ProcessSavedSearch> Rename(
        IEnumerable<ProcessSavedSearch>? searches,
        int searchIndex,
        string? name)
    {
        List<ProcessSavedSearch> updated = Normalize(searches);
        if ((uint)searchIndex >= (uint)updated.Count) return updated;

        string normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0) return updated;

        ProcessSavedSearch existing = updated[searchIndex];
        updated[searchIndex] = new ProcessSavedSearch
        {
            Name = normalizedName,
            Query = existing.Query
        };
        return updated;
    }

    /// <summary>Returns whether two normalized saved-search lists have the same content.</summary>
    public static bool AreEquivalent(
        IReadOnlyList<ProcessSavedSearch> left,
        IReadOnlyList<ProcessSavedSearch> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Count != right.Count) return false;

        for (int searchIndex = 0; searchIndex < left.Count; searchIndex++)
        {
            ProcessSavedSearch leftSearch = left[searchIndex];
            ProcessSavedSearch rightSearch = right[searchIndex];
            if (!string.Equals(leftSearch.Name, rightSearch.Name, StringComparison.Ordinal)
                || !string.Equals(leftSearch.Query, rightSearch.Query, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Detects a regex comparison operator outside quoted search values.</summary>
    public static bool UsesRegularExpression(string? query)
    {
        string queryText = query ?? string.Empty;
        char activeQuote = '\0';
        bool escaped = false;
        for (int characterIndex = 0; characterIndex < queryText.Length; characterIndex++)
        {
            char current = queryText[characterIndex];
            if (activeQuote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == activeQuote) activeQuote = '\0';
                continue;
            }

            if (current is '\'' or '"')
            {
                activeQuote = current;
                continue;
            }

            if (current != '}') continue;

            int operatorIndex = characterIndex + 1;
            while (operatorIndex < queryText.Length
                   && char.IsWhiteSpace(queryText[operatorIndex]))
            {
                operatorIndex++;
            }

            if (operatorIndex + 1 >= queryText.Length) continue;
            char operatorStart = queryText[operatorIndex];
            if (operatorStart is '=' or '!'
                && queryText[operatorIndex + 1] == '~')
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveNextDefaultName(
        IReadOnlyList<ProcessSavedSearch> searches)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        for (int searchIndex = 0; searchIndex < searches.Count; searchIndex++)
            names.Add(searches[searchIndex].Name);

        int defaultIndex = searches.Count + 1;
        string candidate = string.Concat(DefaultNamePrefix, defaultIndex);
        while (names.Contains(candidate))
        {
            defaultIndex++;
            candidate = string.Concat(DefaultNamePrefix, defaultIndex);
        }

        return candidate;
    }
}
