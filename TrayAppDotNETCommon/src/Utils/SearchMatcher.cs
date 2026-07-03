namespace TrayAppDotNETCommon.Utils;

/// <summary>
/// Scores human text search queries for ordered, forgiving UI filtering.
/// </summary>
public static class SearchMatcher
{
    private const int ExactScore = 10000;
    private const int PrefixScore = 8500;
    private const int WordPrefixScore = 7600;
    private const int AcronymPrefixScore = 7200;
    private const int ContainsScore = 5600;
    private const int AcronymSubsequenceScore = 4600;
    private const int SubsequenceScore = 3200;
    private const int UnmatchedScore = int.MinValue;
    private const int GapPenalty = 12;
    private const int StartPenalty = 4;
    private const char WordSeparator = ' ';

    /// <summary>
    /// Scores a candidate against a query.
    /// </summary>
    public static SearchMatch Score(string? candidate, string? query)
    {
        string normalizedCandidate = Normalize(candidate);
        string normalizedQuery = Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return new SearchMatch(true, 0);

        if (string.IsNullOrWhiteSpace(normalizedCandidate))
            return SearchMatch.NoMatch;

        string acronym = BuildAcronym(normalizedCandidate);
        string[] tokens = normalizedQuery.Split(WordSeparator, StringSplitOptions.RemoveEmptyEntries);
        int totalScore = 0;
        foreach (string token in tokens)
        {
            int tokenScore = ScoreToken(normalizedCandidate, acronym, token);
            if (tokenScore == UnmatchedScore) return SearchMatch.NoMatch;
            totalScore += tokenScore;
        }

        return new SearchMatch(true, totalScore);
    }

    /// <summary>
    /// Filters and ranks items by a text selector while preserving original order for ties.
    /// </summary>
    public static List<T> FilterAndRank<T>(
        IEnumerable<T> items,
        string? query,
        Func<T, string?> textSelector)
    {
        string normalizedQuery = Normalize(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return [.. items];

        List<SearchRankedItem<T>> rankedItems = [];
        int index = 0;
        foreach (T item in items)
        {
            SearchMatch match = Score(textSelector(item), normalizedQuery);
            if (match.IsMatch)
                rankedItems.Add(new SearchRankedItem<T>(item, match.Score, index));
            index++;
        }

        return
        [
            .. rankedItems
                .OrderByDescending(static item => item.Score)
                .ThenBy(static item => item.Index)
                .Select(static item => item.Item),
        ];
    }

    /// <summary>
    /// Scores one query token against normalized text.
    /// </summary>
    private static int ScoreToken(string candidate, string acronym, string token)
    {
        if (string.Equals(candidate, token, StringComparison.Ordinal))
            return ExactScore + token.Length;

        if (candidate.StartsWith(token, StringComparison.Ordinal))
            return PrefixScore + token.Length;

        if (StartsAnyWord(candidate, token))
            return WordPrefixScore + token.Length;

        if (!string.IsNullOrEmpty(acronym) && acronym.StartsWith(token, StringComparison.Ordinal))
            return AcronymPrefixScore + token.Length;

        int containsIndex = candidate.IndexOf(token, StringComparison.Ordinal);
        if (containsIndex >= 0)
            return ContainsScore + token.Length - containsIndex;

        if (!string.IsNullOrEmpty(acronym) && TryScoreSubsequence(acronym, token, out int acronymScore))
            return AcronymSubsequenceScore + acronymScore;

        if (token.Length >= 4 && TryScoreSubsequence(candidate, token, out int subsequenceScore))
            return SubsequenceScore + subsequenceScore;

        return UnmatchedScore;
    }

    /// <summary>
    /// Normalizes display text into lower-invariant word text.
    /// </summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        List<char> chars = [];
        bool previousWasSeparator = true;
        foreach (char c in value.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                chars.Add(char.ToLowerInvariant(c));
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator) continue;
            chars.Add(WordSeparator);
            previousWasSeparator = true;
        }

        while (chars.Count > 0 && chars[^1] == WordSeparator)
            chars.RemoveAt(chars.Count - 1);

        return new string([.. chars]);
    }

    /// <summary>
    /// Builds an acronym from normalized candidate words.
    /// </summary>
    private static string BuildAcronym(string normalizedCandidate)
    {
        List<char> chars = [];
        bool nextIsWordStart = true;
        foreach (char c in normalizedCandidate)
        {
            if (c == WordSeparator)
            {
                nextIsWordStart = true;
                continue;
            }

            if (!nextIsWordStart) continue;
            chars.Add(c);
            nextIsWordStart = false;
        }

        return new string([.. chars]);
    }

    /// <summary>
    /// Checks whether any normalized candidate word starts with the token.
    /// </summary>
    private static bool StartsAnyWord(string candidate, string token)
    {
        if (candidate.StartsWith(token, StringComparison.Ordinal))
            return true;

        string prefixedToken = WordSeparator + token;
        return candidate.Contains(prefixedToken, StringComparison.Ordinal);
    }

    /// <summary>
    /// Scores ordered character matches with penalties for late and gapped matches.
    /// </summary>
    private static bool TryScoreSubsequence(string candidate, string token, out int score)
    {
        score = 0;
        int tokenIndex = 0;
        int firstMatch = -1;
        int previousMatch = -1;
        int gapTotal = 0;

        for (int i = 0; i < candidate.Length && tokenIndex < token.Length; i++)
        {
            if (candidate[i] != token[tokenIndex]) continue;

            if (firstMatch < 0)
                firstMatch = i;

            if (previousMatch >= 0)
                gapTotal += Math.Max(0, i - previousMatch - 1);

            previousMatch = i;
            tokenIndex++;
        }

        if (tokenIndex != token.Length) return false;

        score = token.Length - (firstMatch * StartPenalty) - (gapTotal * GapPenalty);
        return true;
    }

    private readonly record struct SearchRankedItem<T>(T Item, int Score, int Index);
}

/// <summary>
/// Result of matching one search candidate.
/// </summary>
public readonly record struct SearchMatch(bool IsMatch, int Score)
{
    public static SearchMatch NoMatch { get; } = new(false, int.MinValue);
}
