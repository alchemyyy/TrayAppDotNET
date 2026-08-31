namespace TrayAppDotNETCommon.UI.Settings;

internal readonly struct SettingsSearchQueryPart(string text, int synonymGroupID)
{
    public const int NoSynonymGroup = -1;
    public readonly string Text = text;
    public readonly int SynonymGroupID = synonymGroupID;
}

/// <summary>Maps localized equivalent phrases to structured search concepts.</summary>
internal sealed class SettingsSearchSynonymMap
{
    public const string AppResourceKey = "SettingsWindow_SearchSynonymGroups_App";

    private const char TermSeparator = '|';

    private sealed class SynonymTerm(string text)
    {
        public readonly string Text = text;
        public readonly string[] Tokens = Tokenize(text);
    }

    private sealed class SynonymGroup(int id, IReadOnlyList<SynonymTerm> terms)
    {
        public readonly int ID = id;
        public readonly IReadOnlyList<SynonymTerm> Terms = terms;
    }

    private readonly List<SynonymGroup> _groups = [];
    private readonly HashSet<string> _ambiguousQueryTerms = new(StringComparer.Ordinal);

    private SettingsSearchSynonymMap(IReadOnlyList<string> definitions)
    {
        List<List<string>> parsedGroups = [];
        HashSet<string> seenGroups = new(StringComparer.Ordinal);
        foreach (string definition in definitions)
            ParseDefinition(definition, parsedGroups, seenGroups);

        // Merge extensions of the same concept, but never bridge groups through one ambiguous term
        List<HashSet<string>> consolidatedGroups = [];
        foreach (List<string> parsedGroup in parsedGroups)
        {
            HashSet<string> consolidatedGroup = new(parsedGroup, StringComparer.Ordinal);
            bool didMerge;
            do
            {
                didMerge = false;
                for (int groupIndex = consolidatedGroups.Count - 1; groupIndex >= 0; groupIndex--)
                {
                    HashSet<string> existingGroup = consolidatedGroups[groupIndex];
                    if (CountSharedTerms(consolidatedGroup, existingGroup) < 2) continue;
                    consolidatedGroup.UnionWith(existingGroup);
                    consolidatedGroups.RemoveAt(groupIndex);
                    didMerge = true;
                }
            } while (didMerge);

            consolidatedGroups.Add(consolidatedGroup);
        }

        Dictionary<string, int> termFrequencies = new(StringComparer.Ordinal);
        foreach (HashSet<string> consolidatedGroup in consolidatedGroups)
        {
            foreach (string term in consolidatedGroup)
                termFrequencies[term] = termFrequencies.GetValueOrDefault(term) + 1;
        }

        foreach ((string term, int frequency) in termFrequencies)
        {
            if (frequency > 1)
                _ambiguousQueryTerms.Add(term);
        }

        foreach (HashSet<string> consolidatedGroup in consolidatedGroups)
        {
            List<SynonymTerm> terms = [];
            foreach (string term in consolidatedGroup)
                terms.Add(new SynonymTerm(term));

            if (terms.Count < 2) continue;
            terms.Sort(static (left, right) => string.CompareOrdinal(left.Text, right.Text));
            _groups.Add(new SynonymGroup(_groups.Count, terms));
        }
    }

    public static SettingsSearchSynonymMap Parse(params string[] definitions) => new(definitions);

    public List<SettingsSearchQueryPart> ParseQuery(string normalizedQuery)
    {
        string[] tokens = Tokenize(normalizedQuery);
        List<SettingsSearchQueryPart> parts = [];
        int tokenIndex = 0;
        while (tokenIndex < tokens.Length)
        {
            (SynonymGroup? group, SynonymTerm? term) = FindLongestTerm(tokens, tokenIndex);
            if (group != null && term != null)
            {
                parts.Add(new SettingsSearchQueryPart(term.Text, group.ID));
                tokenIndex += term.Tokens.Length;
                continue;
            }

            string token = tokens[tokenIndex];
            if (ContainsNonASCII(token))
                AddUnsegmentedTokenParts(token, parts);
            else
                parts.Add(new SettingsSearchQueryPart(token, SettingsSearchQueryPart.NoSynonymGroup));
            tokenIndex++;
        }

        return parts;
    }

    public int[] FindGroupIDs(string normalizedText)
    {
        List<int> groupIDs = [];
        foreach (SynonymGroup group in _groups)
        {
            foreach (SynonymTerm term in group.Terms)
            {
                if (!ContainsTerm(normalizedText, term.Text)) continue;
                groupIDs.Add(group.ID);
                break;
            }
        }

        return [.. groupIDs];
    }

    private static void ParseDefinition(
        string definition,
        List<List<string>> parsedGroups,
        HashSet<string> seenGroups)
    {
        if (string.IsNullOrWhiteSpace(definition)) return;

        string[] lines = definition.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string line in lines)
        {
            string[] rawTerms = line.Split(
                TermSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            List<string> terms = new(rawTerms.Length);
            foreach (string rawTerm in rawTerms)
            {
                string normalizedTerm = SettingsSearchScorer.Normalize(rawTerm);
                if (normalizedTerm.Length == 0 || terms.Contains(normalizedTerm, StringComparer.Ordinal)) continue;
                terms.Add(normalizedTerm);
            }

            if (terms.Count < 2) continue;

            List<string> identityTerms = [.. terms];
            identityTerms.Sort(StringComparer.Ordinal);
            string groupIdentity = string.Join(TermSeparator, identityTerms);
            if (seenGroups.Add(groupIdentity))
                parsedGroups.Add(terms);
        }
    }

    private static int CountSharedTerms(HashSet<string> first, HashSet<string> second)
    {
        HashSet<string> smaller = first.Count <= second.Count ? first : second;
        HashSet<string> larger = ReferenceEquals(smaller, first) ? second : first;
        int sharedCount = 0;
        foreach (string term in smaller)
        {
            if (larger.Contains(term))
                sharedCount++;
        }

        return sharedCount;
    }

    private (SynonymGroup? group, SynonymTerm? term) FindLongestTerm(
        string[] queryTokens,
        int startIndex)
    {
        SynonymGroup? bestGroup = null;
        SynonymTerm? bestTerm = null;
        foreach (SynonymGroup group in _groups)
        {
            foreach (SynonymTerm term in group.Terms)
            {
                if (_ambiguousQueryTerms.Contains(term.Text)) continue;
                if (term.Tokens.Length > queryTokens.Length - startIndex) continue;
                if (!TokensMatch(queryTokens, startIndex, term.Tokens)) continue;
                if (bestTerm != null && term.Tokens.Length <= bestTerm.Tokens.Length) continue;
                bestGroup = group;
                bestTerm = term;
            }
        }

        return (bestGroup, bestTerm);
    }

    private void AddUnsegmentedTokenParts(string token, List<SettingsSearchQueryPart> parts)
    {
        int currentIndex = 0;
        while (currentIndex < token.Length)
        {
            (int matchIndex, SynonymGroup? group, SynonymTerm? term) = FindNextUnsegmentedTerm(token, currentIndex);
            if (matchIndex < 0 || group == null || term == null)
            {
                parts.Add(new SettingsSearchQueryPart(
                    token[currentIndex..],
                    SettingsSearchQueryPart.NoSynonymGroup));
                return;
            }

            if (matchIndex > currentIndex)
            {
                parts.Add(new SettingsSearchQueryPart(
                    token[currentIndex..matchIndex],
                    SettingsSearchQueryPart.NoSynonymGroup));
            }

            parts.Add(new SettingsSearchQueryPart(term.Text, group.ID));
            currentIndex = matchIndex + term.Text.Length;
        }
    }

    private (int matchIndex, SynonymGroup? group, SynonymTerm? term) FindNextUnsegmentedTerm(
        string token,
        int startIndex)
    {
        int bestMatchIndex = -1;
        SynonymGroup? bestGroup = null;
        SynonymTerm? bestTerm = null;
        foreach (SynonymGroup group in _groups)
        {
            foreach (SynonymTerm term in group.Terms)
            {
                if (_ambiguousQueryTerms.Contains(term.Text)) continue;
                if (term.Tokens.Length != 1 || !ContainsNonASCII(term.Text)) continue;
                int matchIndex = token.IndexOf(term.Text, startIndex, StringComparison.Ordinal);
                if (matchIndex < 0) continue;
                if (bestMatchIndex >= 0
                    && (matchIndex > bestMatchIndex
                        || (matchIndex == bestMatchIndex && bestTerm != null &&
                            term.Text.Length <= bestTerm.Text.Length)))
                    continue;

                bestMatchIndex = matchIndex;
                bestGroup = group;
                bestTerm = term;
            }
        }

        return (bestMatchIndex, bestGroup, bestTerm);
    }

    private static bool TokensMatch(
        string[] queryTokens,
        int queryStartIndex,
        string[] termTokens)
    {
        for (int termIndex = 0; termIndex < termTokens.Length; termIndex++)
        {
            if (!string.Equals(queryTokens[queryStartIndex + termIndex], termTokens[termIndex],
                    StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool ContainsTerm(string normalizedText, string normalizedTerm)
    {
        if (normalizedTerm.Length == 0) return false;
        if (ContainsNonASCII(normalizedTerm) && !normalizedTerm.Contains(' '))
            return normalizedText.Contains(normalizedTerm, StringComparison.Ordinal);

        int searchStart = 0;
        while (searchStart <= normalizedText.Length - normalizedTerm.Length)
        {
            int termIndex = normalizedText.IndexOf(normalizedTerm, searchStart, StringComparison.Ordinal);
            if (termIndex < 0) return false;

            int termEnd = termIndex + normalizedTerm.Length;
            bool hasStartBoundary = termIndex == 0 || normalizedText[termIndex - 1] == ' ';
            bool hasEndBoundary = termEnd == normalizedText.Length || normalizedText[termEnd] == ' ';
            if (hasStartBoundary && hasEndBoundary) return true;
            searchStart = termIndex + 1;
        }

        return false;
    }

    private static string[] Tokenize(string normalizedText) =>
        normalizedText.Split(separator: ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool ContainsNonASCII(string value)
    {
        foreach (char character in value)
        {
            if (character > 0x7F)
                return true;
        }

        return false;
    }
}
