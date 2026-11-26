using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Provides one formatted and, when applicable, numeric column value in its query units.</summary>
internal readonly record struct SearchColumnValue(
    string Text,
    double NumericValue,
    bool HasNumericValue)
{
    public static SearchColumnValue TextOnly(string text) => new(text, NumericValue: 0, HasNumericValue: false);

    public static SearchColumnValue Numeric(string text, double numericValue) =>
        new(text, numericValue, HasNumericValue: true);
}

/// <summary>Reads one column of the row a query is evaluating; <paramref name="column"/> is a catalog column ID.</summary>
internal delegate SearchColumnValue SearchValueResolver<in TRow>(
    TRow row,
    int column);

/// <summary>Parses and evaluates page search expressions without executing arbitrary code.</summary>
internal sealed class SearchQuery
{
    private enum InstructionKind : byte
    {
        Predicate,
        And,
        Or
    }

    private enum ParserOperator : byte
    {
        LeftParenthesis,
        And,
        Or
    }

    private enum PredicateKind : byte
    {
        DefaultContains,
        Text,
        Numeric,
        Regex
    }

    private enum ComparisonKind : byte
    {
        Equal,
        NotEqual,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual,
        Regex,
        NotRegex
    }

    private const int MatchTimeoutMilliseconds = 100;
    private const int MaximumPredicateCount = 256;
    private const double Kilo = 1_000;
    private const double Mega = 1_000_000;
    private const double Giga = 1_000_000_000;
    private const double Tera = 1_000_000_000_000;
    private const double Kibibyte = 1_024;
    private const double Mebibyte = 1_048_576;
    private const double Gibibyte = 1_073_741_824;
    private const double Tebibyte = 1_099_511_627_776;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(MatchTimeoutMilliseconds);

    private readonly Instruction[] _instructions;
    private readonly bool[] _evaluationStack;
    private readonly string? _parseError;
    private bool _hasTimedOut;

    private SearchQuery(
        Instruction[] instructions,
        int predicateCount,
        ulong requiredColumnMask,
        string? parseError)
    {
        _instructions = instructions;
        _evaluationStack = new bool[Math.Max(val1: 1, predicateCount)];
        RequiredColumnMask = requiredColumnMask;
        _parseError = parseError;
    }

    public ulong RequiredColumnMask { get; }
    public bool IsEmpty => _instructions.Length == 0 && _parseError == null;
    public bool IsValid => _parseError == null && !_hasTimedOut;
    public string? ErrorMessage => _hasTimedOut ? "A regular expression timed out." : _parseError;

    /// <summary>Evaluates the parsed expression against one row.</summary>
    public bool Matches<TRow>(TRow row, SearchValueResolver<TRow> resolveValue)
    {
        ArgumentNullException.ThrowIfNull(resolveValue);
        if (!IsValid) return false;
        if (IsEmpty) return true;

        int stackDepth = 0;
        try
        {
            for (int instructionIndex = 0;
                 instructionIndex < _instructions.Length;
                 instructionIndex++)
            {
                Instruction instruction = _instructions[instructionIndex];
                switch (instruction.Kind)
                {
                    case InstructionKind.Predicate:
                        _evaluationStack[stackDepth] = instruction.Predicate!.Matches(row, resolveValue);
                        stackDepth++;
                        break;
                    case InstructionKind.And:
                    case InstructionKind.Or:
                        if (stackDepth < 2) return false;
                        bool rightValue = _evaluationStack[stackDepth - 1];
                        bool leftValue = _evaluationStack[stackDepth - 2];
                        stackDepth--;
                        _evaluationStack[stackDepth - 1] = instruction.Kind == InstructionKind.And
                            ? leftValue && rightValue
                            : leftValue || rightValue;
                        break;
                    default:
                        return false;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            _hasTimedOut = true;
            return false;
        }

        return stackDepth == 1 && _evaluationStack[0];
    }

    /// <summary>Parses boolean predicates, comparisons, units, and bounded regular expressions.</summary>
    public static SearchQuery Parse(
        string? filterText,
        SearchColumnCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        string queryText = filterText?.Trim() ?? string.Empty;
        if (queryText.Length == 0)
            return new SearchQuery([], predicateCount: 0, requiredColumnMask: 0, parseError: null);

        if (!LooksLikeExpression(queryText))
        {
            Predicate defaultPredicate = Predicate.DefaultContains(queryText, catalog);
            return new SearchQuery(
                [Instruction.ForPredicate(defaultPredicate)],
                predicateCount: 1,
                catalog.DefaultMask,
                parseError: null);
        }

        List<Instruction> output = [];
        List<ParserOperator> operators = [];
        ulong requiredColumnMask = 0;
        int predicateCount = 0;
        int textIndex = 0;
        bool expectsOperand = true;

        while (textIndex < queryText.Length)
        {
            SkipWhitespace(queryText, ref textIndex);
            if (textIndex >= queryText.Length) break;

            if (expectsOperand)
            {
                if (queryText[textIndex] == '(')
                {
                    operators.Add(ParserOperator.LeftParenthesis);
                    textIndex++;
                    continue;
                }

                if (queryText[textIndex] == ')')
                    return Invalid("A closing parenthesis cannot appear where a predicate is required.");

                if (!TryParsePredicate(
                        queryText,
                        ref textIndex,
                        catalog,
                        out Predicate? predicate,
                        out string? parseError))
                    return Invalid(parseError ?? "The predicate is invalid.");

                predicateCount++;
                if (predicateCount > MaximumPredicateCount)
                    return Invalid($"Search expressions are limited to {MaximumPredicateCount} predicates.");

                output.Add(Instruction.ForPredicate(predicate!));
                requiredColumnMask |= predicate!.RequiredColumnMask;
                expectsOperand = false;
                continue;
            }

            if (queryText[textIndex] == ')')
            {
                bool foundOpeningParenthesis = false;
                while (operators.Count > 0)
                {
                    ParserOperator top = operators[^1];
                    operators.RemoveAt(operators.Count - 1);
                    if (top == ParserOperator.LeftParenthesis)
                    {
                        foundOpeningParenthesis = true;
                        break;
                    }

                    output.Add(Instruction.ForOperator(top));
                }

                if (!foundOpeningParenthesis)
                    return Invalid("The expression contains an unmatched closing parenthesis.");
                textIndex++;
                continue;
            }

            if (!TryReadBooleanOperator(queryText, ref textIndex, out ParserOperator nextOperator))
                return Invalid($"Expected '&&', '||', or ')' at character {textIndex + 1}.");

            while (operators.Count > 0
                   && operators[^1] != ParserOperator.LeftParenthesis
                   && GetPrecedence(operators[^1]) >= GetPrecedence(nextOperator))
            {
                ParserOperator top = operators[^1];
                operators.RemoveAt(operators.Count - 1);
                output.Add(Instruction.ForOperator(top));
            }

            operators.Add(nextOperator);
            expectsOperand = true;
        }

        if (expectsOperand)
            return Invalid("The expression ends where a predicate is required.");

        while (operators.Count > 0)
        {
            ParserOperator top = operators[^1];
            operators.RemoveAt(operators.Count - 1);
            if (top == ParserOperator.LeftParenthesis)
                return Invalid("The expression contains an unmatched opening parenthesis.");
            output.Add(Instruction.ForOperator(top));
        }

        return new SearchQuery(output.ToArray(), predicateCount, requiredColumnMask, parseError: null);
    }

    private static bool LooksLikeExpression(string queryText) =>
        queryText.Contains('{')
        || queryText.Contains(value: "&&", StringComparison.Ordinal)
        || queryText.Contains(value: "||", StringComparison.Ordinal);

    private static bool TryParsePredicate(
        string queryText,
        ref int textIndex,
        SearchColumnCatalog catalog,
        out Predicate? predicate,
        out string? parseError)
    {
        if (queryText[textIndex] != '{')
        {
            int predicateStart = textIndex;
            while (textIndex < queryText.Length
                   && queryText[textIndex] != ')'
                   && !StartsBooleanOperator(queryText, textIndex))
                textIndex++;

            string defaultText = queryText[predicateStart..textIndex].Trim();
            if (defaultText.Length == 0)
            {
                predicate = null;
                parseError = "A plain search term cannot be empty.";
                return false;
            }

            predicate = Predicate.DefaultContains(defaultText, catalog);
            parseError = null;
            return true;
        }

        int openingBraceIndex = textIndex;
        int closingBraceIndex = queryText.IndexOf(value: '}', openingBraceIndex + 1);
        if (closingBraceIndex < 0)
        {
            predicate = null;
            parseError = "A column name is missing its closing brace.";
            return false;
        }

        string columnName = queryText[(openingBraceIndex + 1)..closingBraceIndex].Trim();
        if (columnName.Length == 0)
        {
            predicate = null;
            parseError = "A column name cannot be empty.";
            return false;
        }

        if (!catalog.TryResolve(columnName, out SearchColumn column))
        {
            predicate = null;
            parseError = $"Unknown column '{columnName}'.";
            return false;
        }

        textIndex = closingBraceIndex + 1;
        SkipWhitespace(queryText, ref textIndex);
        if (!TryReadComparison(queryText, ref textIndex, out ComparisonKind comparison))
        {
            predicate = null;
            parseError = $"Column '{columnName}' requires a comparison operator.";
            return false;
        }

        SkipWhitespace(queryText, ref textIndex);
        if (!TryReadLiteral(queryText, ref textIndex, out string literal, out parseError))
        {
            predicate = null;
            return false;
        }

        return TryCreateColumnPredicate(column, comparison, literal, out predicate, out parseError);
    }

    private static bool TryCreateColumnPredicate(
        SearchColumn column,
        ComparisonKind comparison,
        string literal,
        out Predicate? predicate,
        out string? parseError)
    {
        if (comparison is ComparisonKind.Regex or ComparisonKind.NotRegex)
        {
            try
            {
                Regex regex = new(
                    literal,
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                    MatchTimeout);
                predicate = Predicate.Regex(column, comparison, regex);
                parseError = null;
                return true;
            }
            catch (ArgumentException exception)
            {
                predicate = null;
                parseError = $"Invalid regular expression: {exception.Message}";
                return false;
            }
        }

        bool isNumericColumn = column.ValueKind != SearchValueKind.Text;
        if (isNumericColumn
            && TryParseNumericLiteral(column.ValueKind, literal, out double numericValue))
        {
            predicate = Predicate.Numeric(column, comparison, numericValue);
            parseError = null;
            return true;
        }

        if (comparison is ComparisonKind.Greater
                or ComparisonKind.GreaterOrEqual
                or ComparisonKind.Less
                or ComparisonKind.LessOrEqual
            && isNumericColumn)
        {
            predicate = null;
            parseError = $"'{literal}' is not a valid numeric value for {column.Title}.";
            return false;
        }

        predicate = Predicate.Text(column, comparison, literal);
        parseError = null;
        return true;
    }

    private static bool TryReadLiteral(
        string queryText,
        ref int textIndex,
        out string literal,
        out string? parseError)
    {
        if (textIndex >= queryText.Length)
        {
            literal = string.Empty;
            parseError = "A comparison value is required.";
            return false;
        }

        char firstCharacter = queryText[textIndex];
        if (firstCharacter is '\'' or '"')
            return TryReadQuotedLiteral(queryText, ref textIndex, firstCharacter, out literal, out parseError);

        int literalStart = textIndex;
        while (textIndex < queryText.Length
               && queryText[textIndex] != ')'
               && !StartsBooleanOperator(queryText, textIndex))
            textIndex++;

        literal = queryText[literalStart..textIndex].Trim();
        if (literal.Length > 0)
        {
            parseError = null;
            return true;
        }

        parseError = "A comparison value is required.";
        return false;
    }

    private static bool TryReadQuotedLiteral(
        string queryText,
        ref int textIndex,
        char quote,
        out string literal,
        out string? parseError)
    {
        textIndex++;
        StringBuilder builder = new();
        while (textIndex < queryText.Length)
        {
            char current = queryText[textIndex];
            if (current == quote)
            {
                textIndex++;
                literal = builder.ToString();
                parseError = null;
                return true;
            }

            if (current == '\\' && textIndex + 1 < queryText.Length)
            {
                char escaped = queryText[textIndex + 1];
                if (escaped == quote || escaped == '\\')
                {
                    builder.Append(escaped);
                    textIndex += 2;
                    continue;
                }
            }

            builder.Append(current);
            textIndex++;
        }

        literal = string.Empty;
        parseError = "A quoted comparison value is missing its closing quote.";
        return false;
    }

    private static bool TryReadComparison(
        string queryText,
        ref int textIndex,
        out ComparisonKind comparison)
    {
        ReadOnlySpan<char> remaining = queryText.AsSpan(textIndex);
        (string Text, ComparisonKind Kind)[] operators =
        [
            ("=~", ComparisonKind.Regex),
            ("!~", ComparisonKind.NotRegex),
            (">=", ComparisonKind.GreaterOrEqual),
            ("<=", ComparisonKind.LessOrEqual),
            ("!=", ComparisonKind.NotEqual),
            ("=", ComparisonKind.Equal),
            (">", ComparisonKind.Greater),
            ("<", ComparisonKind.Less)
        ];

        for (int operatorIndex = 0; operatorIndex < operators.Length; operatorIndex++)
        {
            (string operatorText, ComparisonKind operatorKind) = operators[operatorIndex];
            if (!remaining.StartsWith(operatorText, StringComparison.Ordinal)) continue;

            textIndex += operatorText.Length;
            comparison = operatorKind;
            return true;
        }

        comparison = default;
        return false;
    }

    private static bool TryReadBooleanOperator(
        string queryText,
        ref int textIndex,
        out ParserOperator parserOperator)
    {
        if (queryText.AsSpan(textIndex).StartsWith(value: "&&", StringComparison.Ordinal))
        {
            textIndex += 2;
            parserOperator = ParserOperator.And;
            return true;
        }

        if (queryText.AsSpan(textIndex).StartsWith(value: "||", StringComparison.Ordinal))
        {
            textIndex += 2;
            parserOperator = ParserOperator.Or;
            return true;
        }

        parserOperator = default;
        return false;
    }

    private static bool StartsBooleanOperator(string queryText, int textIndex) =>
        queryText.AsSpan(textIndex).StartsWith(value: "&&", StringComparison.Ordinal)
        || queryText.AsSpan(textIndex).StartsWith(value: "||", StringComparison.Ordinal);

    private static int GetPrecedence(ParserOperator parserOperator) => parserOperator switch
    {
        ParserOperator.And => 2,
        ParserOperator.Or => 1,
        _ => 0
    };

    private static void SkipWhitespace(string queryText, ref int textIndex)
    {
        while (textIndex < queryText.Length && char.IsWhiteSpace(queryText[textIndex]))
            textIndex++;
    }

    private static bool TryParseNumericLiteral(
        SearchValueKind valueKind,
        string literal,
        out double numericValue) => valueKind switch
    {
        SearchValueKind.Duration => TryParseDuration(literal, out numericValue),
        SearchValueKind.Percentage => TryParsePercentage(literal, out numericValue),
        SearchValueKind.ByteRate => TryParseRate(literal, useBinaryUnits: true, out numericValue),
        SearchValueKind.BitRate => TryParseRate(literal, useBinaryUnits: false, out numericValue),
        SearchValueKind.Bytes => TryParseScaledNumber(literal, useBinaryUnits: true, out numericValue),
        _ => TryParseScaledNumber(literal, useBinaryUnits: false, out numericValue)
    };

    private static bool TryParsePercentage(string literal, out double numericValue)
    {
        string normalized = literal.Trim();
        if (normalized.EndsWith('%'))
            normalized = normalized[..^1].TrimEnd();
        return TryParseFiniteDouble(normalized, out numericValue);
    }

    private static bool TryParseRate(
        string literal,
        bool useBinaryUnits,
        out double numericValue)
    {
        string normalized = literal.Replace(oldValue: " ", string.Empty, StringComparison.Ordinal);
        if (normalized.EndsWith(value: "/s", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^2];
        else if (normalized.EndsWith(value: "ps", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^2];

        return TryParseScaledNumber(normalized, useBinaryUnits, out numericValue);
    }

    private static bool TryParseDuration(string literal, out double numericValue)
    {
        string normalized = literal.Trim().ToLowerInvariant();
        if (TryParseDisplayedDuration(normalized, out numericValue)) return true;

        (string Suffix, double TickMultiplier)[] units =
        [
            ("ms", TimeSpan.TicksPerMillisecond),
            ("min", TimeSpan.TicksPerMinute),
            ("d", TimeSpan.TicksPerDay),
            ("h", TimeSpan.TicksPerHour),
            ("m", TimeSpan.TicksPerMinute),
            ("s", TimeSpan.TicksPerSecond)
        ];
        for (int unitIndex = 0; unitIndex < units.Length; unitIndex++)
        {
            (string suffix, double tickMultiplier) = units[unitIndex];
            if (!normalized.EndsWith(suffix, StringComparison.Ordinal)) continue;

            string numberText = normalized[..^suffix.Length].TrimEnd();
            if (!TryParseFiniteDouble(numberText, out double unitValue) || unitValue < 0)
            {
                numericValue = 0;
                return false;
            }

            numericValue = unitValue * tickMultiplier;
            return double.IsFinite(numericValue);
        }

        if (!TryParseFiniteDouble(normalized, out double tickValue) || tickValue < 0)
        {
            numericValue = 0;
            return false;
        }

        numericValue = tickValue;
        return true;
    }

    private static bool TryParseDisplayedDuration(string literal, out double numericValue)
    {
        numericValue = 0;
        string clockText = literal;
        long days = 0;
        int dayMarkerIndex = literal.IndexOf('d');
        if (dayMarkerIndex >= 0)
        {
            if (!long.TryParse(
                    literal[..dayMarkerIndex].Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out days)
                || days < 0)
                return false;

            clockText = literal[(dayMarkerIndex + 1)..].TrimStart();
        }

        string[] clockParts = clockText.Split(':');
        if (clockParts.Length != 3
            || !long.TryParse(clockParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long hours)
            || !int.TryParse(clockParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || !double.TryParse(
                clockParts[2],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out double seconds)
            || hours < 0
            || minutes is < 0 or >= 60
            || seconds is < 0 or >= 60)
            return false;

        double totalSeconds = days * 86_400.0 + hours * 3_600.0 + minutes * 60.0 + seconds;
        numericValue = totalSeconds * TimeSpan.TicksPerSecond;
        return double.IsFinite(numericValue);
    }

    private static bool TryParseScaledNumber(
        string literal,
        bool useBinaryUnits,
        out double numericValue)
    {
        string normalized = literal.Trim().ToLowerInvariant();
        int suffixStart = normalized.Length;
        while (suffixStart > 0 && char.IsLetter(normalized[suffixStart - 1]))
            suffixStart--;

        string numberText = normalized[..suffixStart].TrimEnd();
        string suffix = normalized[suffixStart..];
        if (!TryParseFiniteDouble(numberText, out double baseValue))
        {
            numericValue = 0;
            return false;
        }

        if (!TryResolveScale(suffix, useBinaryUnits, out double scale))
        {
            numericValue = 0;
            return false;
        }

        numericValue = baseValue * scale;
        return double.IsFinite(numericValue);
    }

    private static bool TryResolveScale(string suffix, bool useBinaryUnits, out double scale)
    {
        scale = suffix switch
        {
            "" or "b" => 1,
            "k" or "kb" or "kib" => useBinaryUnits ? Kibibyte : Kilo,
            "m" or "mb" or "mib" => useBinaryUnits ? Mebibyte : Mega,
            "g" or "gb" or "gib" => useBinaryUnits ? Gibibyte : Giga,
            "t" or "tb" or "tib" => useBinaryUnits ? Tebibyte : Tera,
            _ => 0
        };
        return scale > 0;
    }

    private static bool TryParseFiniteDouble(string text, out double value) =>
        double.TryParse(
            text,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value)
        && double.IsFinite(value);

    private static SearchQuery Invalid(string errorMessage) =>
        new([], predicateCount: 0, requiredColumnMask: 0, errorMessage);

    private readonly record struct Instruction(InstructionKind Kind, Predicate? Predicate)
    {
        public static Instruction ForPredicate(Predicate predicate) =>
            new(InstructionKind.Predicate, predicate);

        public static Instruction ForOperator(ParserOperator parserOperator) => parserOperator switch
        {
            ParserOperator.And => new Instruction(InstructionKind.And, Predicate: null),
            ParserOperator.Or => new Instruction(InstructionKind.Or, Predicate: null),
            _ => throw new ArgumentOutOfRangeException(nameof(parserOperator))
        };
    }

    private sealed class Predicate
    {
        private readonly PredicateKind _kind;
        private readonly ComparisonKind _comparison;
        private readonly int _column;
        private readonly int[] _defaultColumns;
        private readonly string _text;
        private readonly double _numericValue;
        private readonly Regex? _regex;

        private Predicate(
            PredicateKind kind,
            ComparisonKind comparison,
            int column,
            int[] defaultColumns,
            string text,
            double numericValue,
            Regex? regex,
            ulong requiredColumnMask)
        {
            _kind = kind;
            _comparison = comparison;
            _column = column;
            _defaultColumns = defaultColumns;
            _text = text;
            _numericValue = numericValue;
            _regex = regex;
            RequiredColumnMask = requiredColumnMask;
        }

        public ulong RequiredColumnMask { get; }

        public static Predicate DefaultContains(string text, SearchColumnCatalog catalog) =>
            new(
                PredicateKind.DefaultContains,
                ComparisonKind.Equal,
                column: -1,
                catalog.DefaultColumnIDs,
                text,
                numericValue: 0,
                regex: null,
                catalog.DefaultMask);

        public static Predicate Text(
            SearchColumn column,
            ComparisonKind comparison,
            string text) =>
            new(
                PredicateKind.Text,
                comparison,
                column.ID,
                defaultColumns: [],
                text,
                numericValue: 0,
                regex: null,
                column.RequiredMask);

        public static Predicate Numeric(
            SearchColumn column,
            ComparisonKind comparison,
            double numericValue) =>
            new(
                PredicateKind.Numeric,
                comparison,
                column.ID,
                defaultColumns: [],
                string.Empty,
                numericValue,
                regex: null,
                column.RequiredMask);

        public static Predicate Regex(
            SearchColumn column,
            ComparisonKind comparison,
            Regex regex) =>
            new(
                PredicateKind.Regex,
                comparison,
                column.ID,
                defaultColumns: [],
                string.Empty,
                numericValue: 0,
                regex,
                column.RequiredMask);

        public bool Matches<TRow>(TRow row, SearchValueResolver<TRow> resolveValue)
        {
            if (_kind == PredicateKind.DefaultContains)
            {
                for (int columnIndex = 0; columnIndex < _defaultColumns.Length; columnIndex++)
                {
                    if (resolveValue(row, _defaultColumns[columnIndex]).Text
                        .Contains(_text, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }

            SearchColumnValue value = resolveValue(row, _column);
            return _kind switch
            {
                PredicateKind.Text => CompareText(value.Text),
                PredicateKind.Numeric => CompareNumeric(value),
                PredicateKind.Regex => CompareRegex(value.Text),
                _ => false
            };
        }

        private bool CompareText(string value)
        {
            int comparison = string.Compare(value, _text, StringComparison.OrdinalIgnoreCase);
            return _comparison switch
            {
                ComparisonKind.Equal => comparison == 0,
                ComparisonKind.NotEqual => comparison != 0,
                ComparisonKind.Greater => comparison > 0,
                ComparisonKind.GreaterOrEqual => comparison >= 0,
                ComparisonKind.Less => comparison < 0,
                ComparisonKind.LessOrEqual => comparison <= 0,
                _ => false
            };
        }

        private bool CompareNumeric(SearchColumnValue value)
        {
            if (!value.HasNumericValue) return false;

            return _comparison switch
            {
                ComparisonKind.Equal => value.NumericValue == _numericValue,
                ComparisonKind.NotEqual => value.NumericValue != _numericValue,
                ComparisonKind.Greater => value.NumericValue > _numericValue,
                ComparisonKind.GreaterOrEqual => value.NumericValue >= _numericValue,
                ComparisonKind.Less => value.NumericValue < _numericValue,
                ComparisonKind.LessOrEqual => value.NumericValue <= _numericValue,
                _ => false
            };
        }

        private bool CompareRegex(string value)
        {
            bool matches = _regex!.IsMatch(value);
            return _comparison == ComparisonKind.NotRegex ? !matches : matches;
        }
    }
}
