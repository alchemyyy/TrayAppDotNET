// The C# compiler lowers "text[1..3]", "text[^1]" and "list[^1]" onto System.Index and System.Range, which
// the .NET Framework reference assemblies do not carry. Declaring them here keeps every slicing call site
// in the source tree exactly as it was written.
// NOTE: array slicing such as "bytes[..4]" additionally needs RuntimeHelpers.GetSubArray, which lives in
// mscorlib and cannot be supplied from here, so array ranges are written out by hand instead.

namespace System
{
    /// <summary>A position in a collection, counted either from the start or from the end.</summary>
    internal readonly struct Index : IEquatable<Index>
    {
        // A from-the-end position is stored as its bitwise complement, matching the .NET encoding
        private readonly int _value;

        public Index(int value, bool fromEnd = false)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "An index may not be negative.");

            _value = fromEnd ? ~value : value;
        }

        public static Index Start => new Index(0);

        public static Index End => new Index(0, fromEnd: true);

        public int Value => _value < 0 ? ~_value : _value;

        public bool IsFromEnd => _value < 0;

        public static Index FromStart(int value) => new Index(value);

        public static Index FromEnd(int value) => new Index(value, fromEnd: true);

        /// <summary>Resolves this position against a known collection length.</summary>
        public int GetOffset(int length) => IsFromEnd ? length + _value + 1 : _value;

        public static implicit operator Index(int value) => new Index(value);

        public bool Equals(Index other) => _value == other._value;

        public override bool Equals(object? other) => other is Index index && Equals(index);

        public override int GetHashCode() => _value;

        public override string ToString() => IsFromEnd ? "^" + Value.ToString() : Value.ToString();
    }

    /// <summary>A start and end position describing a sub-range of a collection.</summary>
    internal readonly struct Range : IEquatable<Range>
    {
        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public Index Start { get; }

        public Index End { get; }

        public static Range All => new Range(Index.Start, Index.End);

        public static Range StartAt(Index start) => new Range(start, Index.End);

        public static Range EndAt(Index end) => new Range(Index.Start, end);

        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);

        public override bool Equals(object? other) => other is Range range && Equals(range);

        public override int GetHashCode() => (Start.GetHashCode() * 31) + End.GetHashCode();

        public override string ToString() => Start.ToString() + ".." + End.ToString();
    }
}
