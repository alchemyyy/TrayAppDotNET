using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Provides one formatted and, when applicable, numeric process-column value.</summary>
internal readonly record struct ProcessSearchColumnValue(
    string Text,
    double NumericValue,
    bool HasNumericValue)
{
    public static ProcessSearchColumnValue TextOnly(string text) => new(text, 0, false);

    public static ProcessSearchColumnValue Numeric(string text, double numericValue) =>
        new(text, numericValue, true);
}

internal delegate ProcessSearchColumnValue ProcessSearchValueResolver(
    int rowIndex,
    ProcessTableColumnKind column);

/// <summary>Parses and evaluates process search expressions without executing arbitrary code.</summary>
internal sealed class ProcessSearchQuery
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
    private static readonly ulong DefaultSearchMask =
        ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Name)
        | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.ProcessID);

    private readonly Instruction[] _instructions;
    private readonly bool[] _evaluationStack;
    private readonly string? _parseError;
    private bool _hasTimedOut;

    private ProcessSearchQuery(
        Instruction[] instructions,
        int predicateCount,
        ulong requiredColumnMask,
        string? parseError)
    {
        _instructions = instructions;
        _evaluationStack = new bool[Math.Max(1, predicateCount)];
        RequiredColumnMask = requiredColumnMask;
        _parseError = parseError;
    }

    public ulong RequiredColumnMask { get; }
    public bool IsEmpty => _instructions.Length == 0 && _parseError == null;
    public bool IsValid => _parseError == null && !_hasTimedOut;
    public string? ErrorMessage => _hasTimedOut ? "A regular expression timed out." : _parseError;

    public bool RequiresAllProcessSamples =>
        !IsEmpty
        && IsValid
        && (RequiredColumnMask & ProcessTableColumnCatalog.DynamicMask) != 0;

    /// <summary>Evaluates the parsed expression against one process row.</summary>
    public bool Matches(int rowIndex, ProcessSearchValueResolver resolveValue)
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
                        _evaluationStack[stackDepth] = instruction.Predicate!.Matches(rowIndex, resolveValue);
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
    public static ProcessSearchQuery Parse(
        string? filterText,
        IReadOnlyList<ProcessColumnSetting> columnSettings)
    {
        ArgumentNullException.ThrowIfNull(columnSettings);

        string queryText = filterText?.Trim() ?? string.Empty;
        if (queryText.Length == 0)
            return new ProcessSearchQuery([], 0, 0, null);

        if (!LooksLikeExpression(queryText))
        {
            Predicate defaultPredicate = Predicate.DefaultContains(queryText);
            return new ProcessSearchQuery(
                [Instruction.ForPredicate(defaultPredicate)],
                1,
                DefaultSearchMask,
                null);
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
                        columnSettings,
                        out Predicate? predicate,
                        out string? parseError))
                {
                    return Invalid(parseError ?? "The predicate is invalid.");
                }

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

        return new ProcessSearchQuery(output.ToArray(), predicateCount, requiredColumnMask, null);
    }

    private static bool LooksLikeExpression(string queryText) =>
        queryText.Contains('{')
        || queryText.Contains("&&", StringComparison.Ordinal)
        || queryText.Contains("||", StringComparison.Ordinal);

    private static bool TryParsePredicate(
        string queryText,
        ref int textIndex,
        IReadOnlyList<ProcessColumnSetting> columnSettings,
        out Predicate? predicate,
        out string? parseError)
    {
        if (queryText[textIndex] != '{')
        {
            int predicateStart = textIndex;
            while (textIndex < queryText.Length
                   && queryText[textIndex] != ')'
                   && !StartsBooleanOperator(queryText, textIndex))
            {
                textIndex++;
            }

            string defaultText = queryText[predicateStart..textIndex].Trim();
            if (defaultText.Length == 0)
            {
                predicate = null;
                parseError = "A default Name/PID predicate cannot be empty.";
                return false;
            }

            predicate = Predicate.DefaultContains(defaultText);
            parseError = null;
            return true;
        }

        int openingBraceIndex = textIndex;
        int closingBraceIndex = queryText.IndexOf('}', openingBraceIndex + 1);
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

        if (!TryResolveColumn(columnName, columnSettings, out ProcessTableColumnKind column))
        {
            predicate = null;
            parseError = $"Unknown process column '{columnName}'.";
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
        ProcessTableColumnKind column,
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

        if (IsNumericColumn(column)
            && TryParseNumericLiteral(column, literal, out double numericValue))
        {
            predicate = Predicate.Numeric(column, comparison, numericValue);
            parseError = null;
            return true;
        }

        if (comparison is ComparisonKind.Greater
            or ComparisonKind.GreaterOrEqual
            or ComparisonKind.Less
            or ComparisonKind.LessOrEqual
            && IsNumericColumn(column))
        {
            predicate = null;
            parseError = $"'{literal}' is not a valid numeric value for {ProcessTableColumnCatalog.Get(column).Title}.";
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
        {
            textIndex++;
        }

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
        if (queryText.AsSpan(textIndex).StartsWith("&&", StringComparison.Ordinal))
        {
            textIndex += 2;
            parserOperator = ParserOperator.And;
            return true;
        }

        if (queryText.AsSpan(textIndex).StartsWith("||", StringComparison.Ordinal))
        {
            textIndex += 2;
            parserOperator = ParserOperator.Or;
            return true;
        }

        parserOperator = default;
        return false;
    }

    private static bool StartsBooleanOperator(string queryText, int textIndex) =>
        queryText.AsSpan(textIndex).StartsWith("&&", StringComparison.Ordinal)
        || queryText.AsSpan(textIndex).StartsWith("||", StringComparison.Ordinal);

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

    private static bool TryResolveColumn(
        string columnName,
        IReadOnlyList<ProcessColumnSetting> columnSettings,
        out ProcessTableColumnKind column)
    {
        for (int definitionIndex = 0;
             definitionIndex < ProcessTableColumnCatalog.Definitions.Length;
             definitionIndex++)
        {
            ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Definitions[definitionIndex];
            if (!string.Equals(definition.Title, columnName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(definition.Kind.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            column = definition.Kind;
            return true;
        }

        for (int settingIndex = 0; settingIndex < columnSettings.Count; settingIndex++)
        {
            ProcessColumnSetting setting = columnSettings[settingIndex];
            if (string.IsNullOrWhiteSpace(setting.Nickname)
                || !string.Equals(setting.Nickname.Trim(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            column = setting.Column;
            return true;
        }

        column = ProcessTableColumnKind.Name;
        return false;
    }

    private static bool IsNumericColumn(ProcessTableColumnKind column) => column switch
    {
        ProcessTableColumnKind.Name
            or ProcessTableColumnKind.Status
            or ProcessTableColumnKind.UserName
            or ProcessTableColumnKind.ImagePath
            or ProcessTableColumnKind.CommandLine
            or ProcessTableColumnKind.OperatingSystemContext
            or ProcessTableColumnKind.Platform
            or ProcessTableColumnKind.Elevated
            or ProcessTableColumnKind.UACVirtualization
            or ProcessTableColumnKind.Description
            or ProcessTableColumnKind.DataExecutionPrevention
            or ProcessTableColumnKind.IOPriority
            or ProcessTableColumnKind.PackageName
            or ProcessTableColumnKind.EnterpriseContext
            or ProcessTableColumnKind.PowerThrottling
            or ProcessTableColumnKind.GPUEngine
            or ProcessTableColumnKind.DPIAwareness
            or ProcessTableColumnKind.Architecture
            or ProcessTableColumnKind.HardwareStackProtection
            or ProcessTableColumnKind.ExtendedControlFlowGuard
            or ProcessTableColumnKind.Isolation
            or ProcessTableColumnKind.NPUEngine => false,
        _ => true
    };

    private static bool TryParseNumericLiteral(
        ProcessTableColumnKind column,
        string literal,
        out double numericValue)
    {
        if (IsTimeColumn(column))
            return TryParseDuration(literal, out numericValue);
        if (IsPercentageColumn(column))
            return TryParsePercentage(literal, out numericValue);
        if (IsByteColumn(column))
            return TryParseScaledNumber(literal, true, out numericValue);
        return TryParseScaledNumber(literal, false, out numericValue);
    }

    private static bool IsTimeColumn(ProcessTableColumnKind column) =>
        column is ProcessTableColumnKind.CPUTime or ProcessTableColumnKind.Lifetime;

    private static bool IsPercentageColumn(ProcessTableColumnKind column) => column switch
    {
        ProcessTableColumnKind.CPU
            or ProcessTableColumnKind.GPU
            or ProcessTableColumnKind.NPU
            or ProcessTableColumnKind.CPUUtility => true,
        _ => false
    };

    private static bool IsByteColumn(ProcessTableColumnKind column) =>
        ProcessColumnSettings.IsMemoryColumn(column)
        || column is ProcessTableColumnKind.IOReadBytes
            or ProcessTableColumnKind.IOWriteBytes
            or ProcessTableColumnKind.IOOtherBytes;

    private static bool TryParsePercentage(string literal, out double numericValue)
    {
        string normalized = literal.Trim();
        if (normalized.EndsWith('%'))
            normalized = normalized[..^1].TrimEnd();
        return TryParseFiniteDouble(normalized, out numericValue);
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
            {
                return false;
            }

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
        {
            return false;
        }

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

    private static ProcessSearchQuery Invalid(string errorMessage) =>
        new([], 0, 0, errorMessage);

    private readonly record struct Instruction(InstructionKind Kind, Predicate? Predicate)
    {
        public static Instruction ForPredicate(Predicate predicate) =>
            new(InstructionKind.Predicate, predicate);

        public static Instruction ForOperator(ParserOperator parserOperator) => parserOperator switch
        {
            ParserOperator.And => new Instruction(InstructionKind.And, null),
            ParserOperator.Or => new Instruction(InstructionKind.Or, null),
            _ => throw new ArgumentOutOfRangeException(nameof(parserOperator))
        };
    }

    private sealed class Predicate
    {
        private readonly PredicateKind _kind;
        private readonly ComparisonKind _comparison;
        private readonly ProcessTableColumnKind _column;
        private readonly string _text;
        private readonly double _numericValue;
        private readonly Regex? _regex;

        private Predicate(
            PredicateKind kind,
            ComparisonKind comparison,
            ProcessTableColumnKind column,
            string text,
            double numericValue,
            Regex? regex,
            ulong requiredColumnMask)
        {
            _kind = kind;
            _comparison = comparison;
            _column = column;
            _text = text;
            _numericValue = numericValue;
            _regex = regex;
            RequiredColumnMask = requiredColumnMask;
        }

        public ulong RequiredColumnMask { get; }

        public static Predicate DefaultContains(string text) =>
            new(
                PredicateKind.DefaultContains,
                ComparisonKind.Equal,
                ProcessTableColumnKind.Name,
                text,
                0,
                null,
                DefaultSearchMask);

        public static Predicate Text(
            ProcessTableColumnKind column,
            ComparisonKind comparison,
            string text) =>
            new(
                PredicateKind.Text,
                comparison,
                column,
                text,
                0,
                null,
                ProcessTableColumnCatalog.GetMask(column));

        public static Predicate Numeric(
            ProcessTableColumnKind column,
            ComparisonKind comparison,
            double numericValue) =>
            new(
                PredicateKind.Numeric,
                comparison,
                column,
                string.Empty,
                numericValue,
                null,
                ProcessTableColumnCatalog.GetMask(column));

        public static Predicate Regex(
            ProcessTableColumnKind column,
            ComparisonKind comparison,
            Regex regex) =>
            new(
                PredicateKind.Regex,
                comparison,
                column,
                string.Empty,
                0,
                regex,
                ProcessTableColumnCatalog.GetMask(column));

        public bool Matches(int rowIndex, ProcessSearchValueResolver resolveValue)
        {
            if (_kind == PredicateKind.DefaultContains)
            {
                ProcessSearchColumnValue name = resolveValue(rowIndex, ProcessTableColumnKind.Name);
                ProcessSearchColumnValue processID = resolveValue(rowIndex, ProcessTableColumnKind.ProcessID);
                return name.Text.Contains(_text, StringComparison.OrdinalIgnoreCase)
                       || processID.Text.Contains(_text, StringComparison.OrdinalIgnoreCase);
            }

            ProcessSearchColumnValue value = resolveValue(rowIndex, _column);
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

        private bool CompareNumeric(ProcessSearchColumnValue value)
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
