using System.Globalization;
using System.Text;
using SmartComponents.LocalEmbeddings;

namespace TrayAppDotNETCommon.UI.Settings;

internal sealed class SettingsSearchDocument
{
    public readonly int ID;
    public readonly string PrimaryText;
    public readonly string ContextText;
    public readonly string EmbeddingText;

    public SettingsSearchDocument(int id, string primaryText, string contextText)
    {
        ID = id;
        PrimaryText = primaryText;
        ContextText = contextText;
        EmbeddingText = string.IsNullOrWhiteSpace(contextText)
            ? primaryText
            : $"{contextText}. {primaryText}";
    }
}

internal static class SettingsSearchScorer
{
    private const double StrongLexicalMatch = 0.66;
    private const double ShortQueryLexicalMatch = 0.82;
    private const float MinimumSemanticSimilarity = 0.52f;
    private const float SemanticTopDistance = 0.15f;

    public static HashSet<int> FindMatches(
        string query,
        IReadOnlyList<SettingsSearchDocument> documents,
        IReadOnlyDictionary<int, float>? semanticSimilarities)
    {
        HashSet<int> matches = [];
        string normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0) return matches;

        bool isShortQuery = normalizedQuery.Replace(" ", string.Empty, StringComparison.Ordinal).Length <= 2;
        float highestSemanticSimilarity = float.MinValue;
        if (!isShortQuery && semanticSimilarities != null)
        {
            foreach (float similarity in semanticSimilarities.Values)
                highestSemanticSimilarity = Math.Max(highestSemanticSimilarity, similarity);
        }

        float adaptiveSemanticThreshold = Math.Max(
            MinimumSemanticSimilarity,
            highestSemanticSimilarity - SemanticTopDistance);

        foreach (SettingsSearchDocument document in documents)
        {
            double primaryLexical = LexicalSimilarity(normalizedQuery, Normalize(document.PrimaryText));
            double contextLexical = LexicalSimilarity(normalizedQuery, Normalize(document.ContextText)) * 0.94;
            double lexical = Math.Max(primaryLexical, contextLexical);

            if (isShortQuery)
            {
                if (lexical >= ShortQueryLexicalMatch)
                    matches.Add(document.ID);
                continue;
            }

            float semantic = semanticSimilarities?.GetValueOrDefault(document.ID, float.MinValue)
                             ?? float.MinValue;
            bool hasSemanticMatch = highestSemanticSimilarity >= MinimumSemanticSimilarity
                                    && semantic >= adaptiveSemanticThreshold;
            double hybrid = semantic == float.MinValue
                ? lexical
                : lexical * 0.42 + semantic * 0.58;

            if (lexical >= StrongLexicalMatch
                || hasSemanticMatch && (semantic >= 0.56f || lexical >= 0.18 || hybrid >= 0.45))
            {
                matches.Add(document.ID);
            }
        }

        return matches;
    }

    public static bool ShouldUseSemanticSearch(string query) =>
        Normalize(query).Replace(" ", string.Empty, StringComparison.Ordinal).Length > 2;

    internal static double LexicalSimilarity(string normalizedQuery, string normalizedCandidate)
    {
        if (normalizedQuery.Length == 0 || normalizedCandidate.Length == 0) return 0;
        if (string.Equals(normalizedQuery, normalizedCandidate, StringComparison.Ordinal)) return 1;

        string[] queryTokens = Tokens(normalizedQuery);
        string[] candidateTokens = Tokens(normalizedCandidate);
        if (queryTokens.Length == 0 || candidateTokens.Length == 0) return 0;

        int compactQueryLength = normalizedQuery.Replace(" ", string.Empty, StringComparison.Ordinal).Length;
        double phraseScore = 0;
        if (compactQueryLength >= 3 && normalizedCandidate.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            phraseScore = normalizedCandidate.StartsWith(normalizedQuery, StringComparison.Ordinal) ? 0.98 : 0.92;
        }

        if (queryTokens.Length == 1 && compactQueryLength <= 2)
        {
            string shortQuery = queryTokens[0];
            foreach (string candidateToken in candidateTokens)
            {
                if (string.Equals(candidateToken, shortQuery, StringComparison.Ordinal)) return 1;
                if (candidateToken.StartsWith(shortQuery, StringComparison.Ordinal)) return 0.86;
            }

            return 0;
        }

        double tokenScoreTotal = 0;
        int matchedTokenCount = 0;
        foreach (string queryToken in queryTokens)
        {
            double bestTokenScore = 0;
            foreach (string candidateToken in candidateTokens)
                bestTokenScore = Math.Max(bestTokenScore, TokenSimilarity(queryToken, candidateToken));

            tokenScoreTotal += bestTokenScore;
            if (bestTokenScore >= 0.62)
                matchedTokenCount++;
        }

        double coverage = (double)matchedTokenCount / queryTokens.Length;
        double averageTokenScore = tokenScoreTotal / queryTokens.Length;
        double tokenScore = averageTokenScore * (0.55 + coverage * 0.45);

        if (queryTokens.Length == 1)
        {
            string acronym = string.Concat(candidateTokens.Select(static token => token[0]));
            if (acronym.StartsWith(queryTokens[0], StringComparison.Ordinal))
                tokenScore = Math.Max(tokenScore, 0.84);
        }

        return Math.Max(phraseScore, tokenScore);
    }

    internal static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string decomposed = text.Normalize(NormalizationForm.FormD);
        StringBuilder normalized = new(decomposed.Length);
        bool previousWasSeparator = true;
        foreach (char character in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator) continue;
            normalized.Append(' ');
            previousWasSeparator = true;
        }

        if (normalized.Length > 0 && normalized[^1] == ' ')
            normalized.Length--;
        return normalized.ToString();
    }

    private static double TokenSimilarity(string queryToken, string candidateToken)
    {
        if (string.Equals(queryToken, candidateToken, StringComparison.Ordinal)) return 1;
        if (candidateToken.StartsWith(queryToken, StringComparison.Ordinal)) return 0.93;
        if (queryToken.StartsWith(candidateToken, StringComparison.Ordinal) && candidateToken.Length >= 3) return 0.78;
        if (queryToken.Length >= 3 && candidateToken.Contains(queryToken, StringComparison.Ordinal)) return 0.80;
        if (IsSingleAdjacentTransposition(queryToken, candidateToken)) return 0.86;

        int maximumLength = Math.Max(queryToken.Length, candidateToken.Length);
        int lengthDifference = Math.Abs(queryToken.Length - candidateToken.Length);
        int maximumDistance = Math.Max(1, maximumLength / 3);
        if (maximumLength < 4 || lengthDifference > maximumDistance) return 0;

        int editDistance = EditDistance(queryToken, candidateToken, maximumDistance);
        if (editDistance > maximumDistance) return 0;

        double similarity = 1 - (double)editDistance / maximumLength;
        return similarity >= 0.64 ? similarity * 0.91 : 0;
    }

    private static bool IsSingleAdjacentTransposition(string left, string right)
    {
        if (left.Length != right.Length || left.Length < 2) return false;

        for (int characterIndex = 0; characterIndex < left.Length; characterIndex++)
        {
            if (left[characterIndex] == right[characterIndex]) continue;
            if (characterIndex + 1 >= left.Length
                || left[characterIndex] != right[characterIndex + 1]
                || left[characterIndex + 1] != right[characterIndex])
            {
                return false;
            }

            for (int suffixIndex = characterIndex + 2; suffixIndex < left.Length; suffixIndex++)
            {
                if (left[suffixIndex] != right[suffixIndex]) return false;
            }

            return true;
        }

        return false;
    }

    private static int EditDistance(string left, string right, int maximumDistance)
    {
        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];
        for (int rightIndex = 0; rightIndex <= right.Length; rightIndex++)
            previous[rightIndex] = rightIndex;

        for (int leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            int rowMinimum = current[0];
            for (int rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                int substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
                rowMinimum = Math.Min(rowMinimum, current[rightIndex]);
            }

            if (rowMinimum > maximumDistance) return maximumDistance + 1;
            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string[] Tokens(string normalizedText) =>
        normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed class SettingsSemanticSearchEngine : IDisposable
{
    private const int MaximumEmbeddingTokens = 96;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, EmbeddingF32> _embeddingCache = new(StringComparer.Ordinal);
    private LocalEmbedder? _embedder;
    private int _disposed;

    public async Task<IReadOnlyDictionary<int, float>> ScoreAsync(
        string query,
        IReadOnlyList<SettingsSearchDocument> documents,
        CancellationToken cancellationToken) =>
        await Task.Run(() => Score(query, documents, cancellationToken), CancellationToken.None);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _gate.Wait();
        try
        {
            _embeddingCache.Clear();
            _embedder?.Dispose();
            _embedder = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private Dictionary<int, float> Score(
        string query,
        IReadOnlyList<SettingsSearchDocument> documents,
        CancellationToken cancellationToken)
    {
        _gate.Wait(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();

            LocalEmbedder embedder = _embedder ??= new LocalEmbedder();
            List<(int Item, EmbeddingF32 Embedding)> candidates = new(documents.Count);
            foreach (SettingsSearchDocument document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_embeddingCache.TryGetValue(document.EmbeddingText, out EmbeddingF32 embedding))
                {
                    embedding = embedder.Embed(document.EmbeddingText, MaximumEmbeddingTokens);
                    _embeddingCache[document.EmbeddingText] = embedding;
                }

                candidates.Add((document.ID, embedding));
            }

            Dictionary<int, float> similarities = new(documents.Count);
            if (candidates.Count == 0) return similarities;

            cancellationToken.ThrowIfCancellationRequested();
            EmbeddingF32 queryEmbedding = embedder.Embed(query, MaximumEmbeddingTokens);
            SimilarityScore<int>[] scores = LocalEmbedder.FindClosestWithScore(
                queryEmbedding,
                candidates,
                candidates.Count);
            foreach (SimilarityScore<int> score in scores)
                similarities[score.Item] = score.Similarity;
            return similarities;
        }
        finally
        {
            _gate.Release();
        }
    }
}
