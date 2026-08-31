using System.Globalization;
using System.Text;

namespace TrayAppDotNETCommon.UI.Settings;

internal sealed class SettingsSearchDocument(
    int id,
    string titleText,
    string bodyText,
    string searchKeywords,
    string subsectionText,
    string pageText)
{
    public readonly int ID = id;
    public readonly string TitleText = titleText;
    public readonly string BodyText = bodyText;
    public readonly string SearchKeywords = searchKeywords;
    public readonly string SubsectionText = subsectionText;
    public readonly string PageText = pageText;
    public readonly string PrimaryText = JoinText(titleText, bodyText);
    public readonly string ContextText = JoinText(subsectionText, pageText);

    public SettingsSearchDocument(int id, string primaryText, string contextText)
        : this(id, primaryText, string.Empty, string.Empty, contextText, string.Empty)
    {
    }

    private static string JoinText(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first)) return second;
        if (string.IsNullOrWhiteSpace(second)) return first;
        return $"{first}. {second}";
    }
}

/// <summary>Scores localized settings text using deterministic field weights and fuzzy matching.</summary>
internal static class SettingsSearchScorer
{
    private const double TitleFieldWeight = 1.00;
    private const double KeywordFieldWeight = 0.98;
    private const double BodyFieldWeight = 0.78;
    private const double SubsectionFieldWeight = 0.64;
    private const double PageFieldWeight = 0.56;
    private const double MinimumPhraseScore = 0.53;
    private const double MinimumSingleTokenScore = 0.53;
    private const double MinimumMultipleTokenScore = 0.50;
    private const double MinimumTokenCoverage = 0.64;
    private const double MinimumRecognizedTokenSimilarity = 0.62;
    private const double MinimumCoveredTokenSimilarity = 0.64;
    private const double DocumentFrequencySimilarity = 0.72;
    private const double LongTokenRarityWeightScale = 0.12;
    private const int ShortASCIIQueryLength = 2;
    private const int MaximumAcronymLength = 6;

    private sealed class SearchField
    {
        public readonly string NormalizedText;
        public readonly string[] Tokens;
        public readonly int[] SynonymGroupIDs;
        public readonly double Weight;

        public SearchField(string text, double weight, SettingsSearchSynonymMap? synonymMap)
        {
            NormalizedText = Normalize(text);
            Tokens = Tokenize(NormalizedText);
            SynonymGroupIDs = synonymMap?.FindGroupIDs(NormalizedText) ?? [];
            Weight = weight;
        }
    }

    private sealed class PreparedDocument
    {
        public readonly int ID;
        public readonly List<SearchField> Fields = [];

        public PreparedDocument(SettingsSearchDocument document, SettingsSearchSynonymMap? synonymMap)
        {
            ID = document.ID;
            AddField(document.TitleText, TitleFieldWeight, synonymMap);
            AddField(document.SearchKeywords, KeywordFieldWeight, synonymMap);
            AddField(document.BodyText, BodyFieldWeight, synonymMap);
            AddField(document.SubsectionText, SubsectionFieldWeight, synonymMap);
            AddField(document.PageText, PageFieldWeight, synonymMap);
        }

        private void AddField(string text, double weight, SettingsSearchSynonymMap? synonymMap)
        {
            if (!string.IsNullOrWhiteSpace(text))
                Fields.Add(new SearchField(text, weight, synonymMap));
        }
    }

    private sealed class QueryToken(string text, int synonymGroupId, double weight)
    {
        public readonly string Text = text;
        public readonly int SynonymGroupID = synonymGroupId;
        public readonly double Weight = weight;
    }

    public static HashSet<int> FindMatches(
        string query,
        IReadOnlyList<SettingsSearchDocument> documents,
        SettingsSearchSynonymMap? synonymMap = null)
    {
        HashSet<int> matches = [];
        string normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0 || documents.Count == 0) return matches;

        List<PreparedDocument> preparedDocuments = new(documents.Count);
        foreach (SettingsSearchDocument document in documents)
            preparedDocuments.Add(new PreparedDocument(document, synonymMap));

        List<QueryToken> queryTokens = PrepareQueryTokens(normalizedQuery, preparedDocuments, synonymMap);
        if (queryTokens.Count == 0) return matches;

        foreach (PreparedDocument document in preparedDocuments)
        {
            if (IsMatch(normalizedQuery, queryTokens, document))
                matches.Add(document.ID);
        }

        return matches;
    }

    internal static double LexicalSimilarity(string normalizedQuery, string normalizedCandidate)
    {
        if (normalizedQuery.Length == 0 || normalizedCandidate.Length == 0) return 0;
        if (string.Equals(normalizedQuery, normalizedCandidate, StringComparison.Ordinal)) return 1;

        string[] queryTokens = Tokenize(normalizedQuery);
        string[] candidateTokens = Tokenize(normalizedCandidate);
        if (queryTokens.Length == 0 || candidateTokens.Length == 0) return 0;

        double phraseScore = PhraseSimilarity(normalizedQuery, normalizedCandidate);
        double tokenScoreTotal = 0;
        foreach (string queryToken in queryTokens)
        {
            double bestTokenScore = BestTokenSimilarity(queryToken, candidateTokens);
            tokenScoreTotal += bestTokenScore;
        }

        return Math.Max(phraseScore, tokenScoreTotal / queryTokens.Length);
    }

    internal static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string decomposed = text.Normalize(NormalizationForm.FormKD);
        StringBuilder normalized = new(decomposed.Length);
        bool previousWasSeparator = true;
        foreach (Rune rune in decomposed.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
                continue;

            if (Rune.IsLetterOrDigit(rune))
            {
                Rune lower = Rune.ToLowerInvariant(rune);
                AppendNormalizedRune(normalized, lower);
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

    private static void AppendNormalizedRune(StringBuilder normalized, Rune rune)
    {
        switch (rune.Value)
        {
            case 0x00DF:
                normalized.Append("ss");
                break;
            case 0x00E6:
                normalized.Append("ae");
                break;
            case 0x00F0:
            case 0x0111:
                normalized.Append('d');
                break;
            case 0x00F8:
                normalized.Append('o');
                break;
            case 0x00FE:
                normalized.Append("th");
                break;
            case 0x0142:
                normalized.Append('l');
                break;
            case 0x0153:
                normalized.Append("oe");
                break;
            default:
                normalized.Append(rune.ToString());
                break;
        }
    }

    private static List<QueryToken> PrepareQueryTokens(
        string normalizedQuery,
        IReadOnlyList<PreparedDocument> documents,
        SettingsSearchSynonymMap? synonymMap)
    {
        List<SettingsSearchQueryPart> queryParts = synonymMap?.ParseQuery(normalizedQuery) ?? [];
        if (synonymMap == null)
        {
            foreach (string token in Tokenize(normalizedQuery))
            {
                queryParts.Add(new SettingsSearchQueryPart(
                    token,
                    SettingsSearchQueryPart.NoSynonymGroup));
            }
        }

        List<SettingsSearchQueryPart> distinctParts = [];
        HashSet<string> seenText = new(StringComparer.Ordinal);
        HashSet<int> seenGroups = [];
        foreach (SettingsSearchQueryPart queryPart in queryParts)
        {
            bool isNew = queryPart.SynonymGroupID == SettingsSearchQueryPart.NoSynonymGroup
                ? seenText.Add(queryPart.Text)
                : seenGroups.Add(queryPart.SynonymGroupID);
            if (isNew)
                distinctParts.Add(queryPart);
        }

        List<QueryToken> queryTokens = new(distinctParts.Count);
        foreach (SettingsSearchQueryPart queryPart in distinctParts)
        {
            double highestSimilarity = 0;
            int documentFrequency = 0;
            foreach (PreparedDocument document in documents)
            {
                double documentSimilarity = BestRawQueryPartSimilarity(queryPart, document);
                highestSimilarity = Math.Max(highestSimilarity, documentSimilarity);
                if (documentSimilarity >= DocumentFrequencySimilarity)
                    documentFrequency++;
            }

            if (highestSimilarity < MinimumRecognizedTokenSimilarity) continue;

            double maximumRarity = Math.Log(documents.Count + 1.0);
            double normalizedRarity = maximumRarity <= 0
                ? 0
                : Math.Log((documents.Count + 1.0) / (documentFrequency + 1.0)) / maximumRarity;
            double tokenWeight = queryPart.SynonymGroupID == SettingsSearchQueryPart.NoSynonymGroup
                ? QueryTokenWeight(queryPart.Text, normalizedRarity)
                : 1 + normalizedRarity * LongTokenRarityWeightScale;
            queryTokens.Add(new QueryToken(queryPart.Text, queryPart.SynonymGroupID, tokenWeight));
        }

        return queryTokens;
    }

    private static bool IsMatch(
        string normalizedQuery,
        IReadOnlyList<QueryToken> queryTokens,
        PreparedDocument document)
    {
        double phraseScore = 0;
        foreach (SearchField field in document.Fields)
        {
            double fieldPhraseScore = PhraseSimilarity(normalizedQuery, field.NormalizedText) * field.Weight;
            phraseScore = Math.Max(phraseScore, fieldPhraseScore);
        }

        if (phraseScore >= MinimumPhraseScore) return true;

        double totalQueryWeight = 0;
        double coveredQueryWeight = 0;
        double weightedScoreTotal = 0;
        foreach (QueryToken queryToken in queryTokens)
        {
            (double rawSimilarity, double weightedSimilarity) = BestDocumentTokenSimilarity(
                queryToken,
                document);
            totalQueryWeight += queryToken.Weight;
            weightedScoreTotal += weightedSimilarity * queryToken.Weight;
            if (rawSimilarity >= MinimumCoveredTokenSimilarity)
                coveredQueryWeight += queryToken.Weight;
        }

        if (totalQueryWeight <= 0) return false;

        double coverage = coveredQueryWeight / totalQueryWeight;
        double tokenScore = weightedScoreTotal / totalQueryWeight;
        if (queryTokens.Count == 1)
            return coverage >= 1 && tokenScore >= MinimumSingleTokenScore;

        return coverage >= MinimumTokenCoverage && tokenScore >= MinimumMultipleTokenScore;
    }

    private static double BestRawQueryPartSimilarity(
        SettingsSearchQueryPart queryPart,
        PreparedDocument document)
    {
        double bestSimilarity = 0;
        foreach (SearchField field in document.Fields)
        {
            double rawSimilarity = SearchFieldSimilarity(
                queryPart.Text,
                queryPart.SynonymGroupID,
                field);
            bestSimilarity = Math.Max(bestSimilarity, rawSimilarity);
        }

        return bestSimilarity;
    }

    private static (double rawSimilarity, double weightedSimilarity) BestDocumentTokenSimilarity(
        QueryToken queryToken,
        PreparedDocument document)
    {
        double bestRawSimilarity = 0;
        double bestWeightedSimilarity = 0;
        foreach (SearchField field in document.Fields)
        {
            double rawSimilarity = SearchFieldSimilarity(
                queryToken.Text,
                queryToken.SynonymGroupID,
                field);
            bestRawSimilarity = Math.Max(bestRawSimilarity, rawSimilarity);
            bestWeightedSimilarity = Math.Max(bestWeightedSimilarity, rawSimilarity * field.Weight);
        }

        return (bestRawSimilarity, bestWeightedSimilarity);
    }

    private static double SearchFieldSimilarity(
        string queryText,
        int synonymGroupID,
        SearchField field)
    {
        double synonymSimilarity = synonymGroupID != SettingsSearchQueryPart.NoSynonymGroup
                                   && Array.BinarySearch(field.SynonymGroupIDs, synonymGroupID) >= 0
            ? 1
            : 0;
        double lexicalSimilarity = queryText.Contains(' ')
            ? LexicalSimilarity(queryText, field.NormalizedText)
            : Math.Max(
                BestTokenSimilarity(queryText, field.Tokens),
                AcronymSimilarity(queryText, field.Tokens));
        return Math.Max(synonymSimilarity, lexicalSimilarity);
    }

    private static double PhraseSimilarity(string normalizedQuery, string normalizedCandidate)
    {
        if (normalizedQuery.Length == 0 || normalizedCandidate.Length == 0) return 0;
        if (string.Equals(normalizedQuery, normalizedCandidate, StringComparison.Ordinal)) return 1;

        int queryLength = RuneLength(normalizedQuery.Replace(oldValue: " ", string.Empty, StringComparison.Ordinal));
        bool isShortASCIIQuery = queryLength <= ShortASCIIQueryLength && IsASCII(normalizedQuery);
        if (normalizedCandidate.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 0.98;
        if (!isShortASCIIQuery && normalizedCandidate.Contains(normalizedQuery, StringComparison.Ordinal)) return 0.94;
        return 0;
    }

    private static double BestTokenSimilarity(string queryToken, IReadOnlyList<string> candidateTokens)
    {
        double bestTokenScore = 0;
        foreach (string candidateToken in candidateTokens)
            bestTokenScore = Math.Max(bestTokenScore, TokenSimilarity(queryToken, candidateToken));
        return bestTokenScore;
    }

    private static double TokenSimilarity(string queryToken, string candidateToken)
    {
        if (string.Equals(queryToken, candidateToken, StringComparison.Ordinal)) return 1;

        int queryLength = RuneLength(queryToken);
        int candidateLength = RuneLength(candidateToken);
        bool isShortASCIIQuery = queryLength <= ShortASCIIQueryLength && IsASCII(queryToken);
        if (candidateToken.StartsWith(queryToken, StringComparison.Ordinal)) return 0.95;
        if (isShortASCIIQuery) return 0;
        if (candidateToken.Contains(queryToken, StringComparison.Ordinal)) return 0.88;
        if (queryToken.StartsWith(candidateToken, StringComparison.Ordinal) && candidateLength >= 3) return 0.78;

        int maximumLength = Math.Max(queryLength, candidateLength);
        int maximumDistance = MaximumEditDistance(maximumLength);
        if (maximumDistance > 0 && Math.Abs(queryLength - candidateLength) <= maximumDistance)
        {
            int editDistance = DamerauLevenshteinDistance(queryToken, candidateToken, maximumDistance);
            if (editDistance <= maximumDistance)
            {
                double editSimilarity = 1 - (double)editDistance / maximumLength;
                if (editSimilarity >= 0.64)
                    return editSimilarity * 0.94;
            }
        }

        double nGramSimilarity = CharacterNGramDice(queryToken, candidateToken);
        return nGramSimilarity >= 0.50 ? 0.48 + nGramSimilarity * 0.45 : 0;
    }

    private static double AcronymSimilarity(string queryToken, string[] candidateTokens)
    {
        int queryLength = RuneLength(queryToken);
        if (queryLength < 2 || queryLength > MaximumAcronymLength || candidateTokens.Length < queryLength) return 0;

        Rune[] queryRunes = queryToken.EnumerateRunes().ToArray();
        for (int startIndex = 0; startIndex <= candidateTokens.Length - queryRunes.Length; startIndex++)
        {
            bool isMatch = true;
            for (int queryIndex = 0; queryIndex < queryRunes.Length; queryIndex++)
            {
                Rune firstCandidateRune = candidateTokens[startIndex + queryIndex].EnumerateRunes().First();
                if (queryRunes[queryIndex] == firstCandidateRune) continue;
                isMatch = false;
                break;
            }

            if (isMatch) return 0.87;
        }

        return 0;
    }

    private static double QueryTokenWeight(string token, double normalizedRarity)
    {
        int tokenLength = RuneLength(token);
        if (!IsASCII(token)) return 1 + normalizedRarity * LongTokenRarityWeightScale;

        return tokenLength switch
        {
            1 => 0.05 + normalizedRarity * 0.20,
            2 => 0.08 + normalizedRarity * 0.37,
            3 => 0.10 + normalizedRarity * 0.65,
            _ => 1 + normalizedRarity * LongTokenRarityWeightScale
        };
    }

    private static int MaximumEditDistance(int maximumLength) => maximumLength switch
    {
        <= 3 => 0,
        <= 5 => 1,
        <= 9 => 2,
        _ => 3
    };

    private static int DamerauLevenshteinDistance(string left, string right, int maximumDistance)
    {
        Rune[] leftRunes = left.EnumerateRunes().ToArray();
        Rune[] rightRunes = right.EnumerateRunes().ToArray();
        int[] previousPrevious = new int[rightRunes.Length + 1];
        int[] previous = new int[rightRunes.Length + 1];
        int[] current = new int[rightRunes.Length + 1];
        for (int rightIndex = 0; rightIndex <= rightRunes.Length; rightIndex++)
            previous[rightIndex] = rightIndex;

        for (int leftIndex = 1; leftIndex <= leftRunes.Length; leftIndex++)
        {
            current[0] = leftIndex;
            int rowMinimum = current[0];
            for (int rightIndex = 1; rightIndex <= rightRunes.Length; rightIndex++)
            {
                int substitutionCost = leftRunes[leftIndex - 1] == rightRunes[rightIndex - 1] ? 0 : 1;
                int distance = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
                if (leftIndex > 1
                    && rightIndex > 1
                    && leftRunes[leftIndex - 1] == rightRunes[rightIndex - 2]
                    && leftRunes[leftIndex - 2] == rightRunes[rightIndex - 1])
                    distance = Math.Min(distance, previousPrevious[rightIndex - 2] + 1);

                current[rightIndex] = distance;
                rowMinimum = Math.Min(rowMinimum, distance);
            }

            if (rowMinimum > maximumDistance) return maximumDistance + 1;
            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }

        return previous[rightRunes.Length];
    }

    private static double CharacterNGramDice(string left, string right)
    {
        int leftLength = RuneLength(left);
        int rightLength = RuneLength(right);
        int nGramSize = Math.Min(leftLength, rightLength) >= 5 ? 3 : 2;
        if (leftLength < nGramSize || rightLength < nGramSize) return 0;

        HashSet<ulong> leftNGrams = CreateNGrams(left, nGramSize);
        HashSet<ulong> rightNGrams = CreateNGrams(right, nGramSize);
        int intersectionCount = 0;
        foreach (ulong nGram in leftNGrams)
        {
            if (rightNGrams.Contains(nGram))
                intersectionCount++;
        }

        return 2.0 * intersectionCount / (leftNGrams.Count + rightNGrams.Count);
    }

    private static HashSet<ulong> CreateNGrams(string value, int nGramSize)
    {
        Rune[] runes = value.EnumerateRunes().ToArray();
        HashSet<ulong> nGrams = [];
        for (int startIndex = 0; startIndex <= runes.Length - nGramSize; startIndex++)
        {
            ulong encoded = 0;
            for (int offset = 0; offset < nGramSize; offset++)
                encoded = (encoded << 21) | (uint)runes[startIndex + offset].Value;
            nGrams.Add(encoded);
        }

        return nGrams;
    }

    private static bool IsASCII(string value)
    {
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (!rune.IsAscii)
                return false;
        }

        return true;
    }

    private static int RuneLength(string value)
    {
        int length = 0;
        foreach (Rune rune in value.EnumerateRunes())
            length++;
        return length;
    }

    private static string[] Tokenize(string normalizedText) =>
        normalizedText.Split(separator: ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
