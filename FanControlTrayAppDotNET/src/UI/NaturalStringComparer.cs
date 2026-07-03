namespace FanControlTrayAppDotNET.UI;

/// <summary>
/// Compares strings with embedded ASCII number runs ordered by numeric value.
/// </summary>
internal sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer OrdinalIgnoreCase { get; } = new();

    private NaturalStringComparer()
    {
    }

    /// <summary>
    /// Compares two strings using ordinal-ignore-case text and numeric digit runs.
    /// </summary>
    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        int leftIndex = 0;
        int rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            char leftChar = left[leftIndex];
            char rightChar = right[rightIndex];
            bool leftIsDigit = IsASCIIDigit(leftChar);
            bool rightIsDigit = IsASCIIDigit(rightChar);
            if (leftIsDigit && rightIsDigit)
            {
                int numberComparison = CompareNumberRun(left, ref leftIndex, right, ref rightIndex);
                if (numberComparison != 0) return numberComparison;
                continue;
            }

            int charComparison = char.ToUpperInvariant(leftChar).CompareTo(char.ToUpperInvariant(rightChar));
            if (charComparison != 0) return charComparison;

            leftIndex++;
            rightIndex++;
        }

        if (leftIndex == left.Length && rightIndex == right.Length) return 0;
        return leftIndex == left.Length ? -1 : 1;
    }

    /// <summary>
    /// Compares two digit runs by numeric magnitude without allocating padded strings.
    /// </summary>
    private static int CompareNumberRun(string left, ref int leftIndex, string right, ref int rightIndex)
    {
        int leftStart = leftIndex;
        int rightStart = rightIndex;
        int leftEnd = ScanDigitRunEnd(left, leftStart);
        int rightEnd = ScanDigitRunEnd(right, rightStart);
        int leftMagnitudeStart = ScanLeadingZeroes(left, leftStart, leftEnd);
        int rightMagnitudeStart = ScanLeadingZeroes(right, rightStart, rightEnd);
        int leftMagnitudeLength = leftEnd - leftMagnitudeStart;
        int rightMagnitudeLength = rightEnd - rightMagnitudeStart;

        leftIndex = leftEnd;
        rightIndex = rightEnd;

        if (leftMagnitudeLength != rightMagnitudeLength)
            return leftMagnitudeLength.CompareTo(rightMagnitudeLength);

        for (int i = 0; i < leftMagnitudeLength; i++)
        {
            int digitComparison = left[leftMagnitudeStart + i].CompareTo(right[rightMagnitudeStart + i]);
            if (digitComparison != 0) return digitComparison;
        }

        int leftRunLength = leftEnd - leftStart;
        int rightRunLength = rightEnd - rightStart;
        return leftRunLength.CompareTo(rightRunLength);
    }

    /// <summary>
    /// Finds the exclusive end index of a contiguous ASCII digit run.
    /// </summary>
    private static int ScanDigitRunEnd(string text, int start)
    {
        int index = start;
        while (index < text.Length && IsASCIIDigit(text[index]))
            index++;
        return index;
    }

    /// <summary>
    /// Finds the first non-zero digit while preserving a single zero magnitude.
    /// </summary>
    private static int ScanLeadingZeroes(string text, int start, int end)
    {
        int index = start;
        while (index < end - 1 && text[index] == '0')
            index++;
        return index;
    }

    /// <summary>
    /// Checks for ASCII digits used in hardware probe labels.
    /// </summary>
    private static bool IsASCIIDigit(char value) =>
        value is >= '0' and <= '9';
}
