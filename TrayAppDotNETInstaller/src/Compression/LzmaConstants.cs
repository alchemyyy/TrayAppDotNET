namespace TrayAppDotNETInstaller.Compression;

/// <summary>
/// The fixed parameters of the LZMA1 bitstream, shared by <see cref="LzmaEncoder"/> and
/// <see cref="LzmaDecoder"/>. Names follow the reference implementation closely enough that this file can be
/// read side by side with it, which is the point: the format is external and must not drift.
///
/// The literal context, literal position and position bits are compile time constants because the encoder
/// never varies them. They are still written into the properties byte so the stream stays a conformant
/// LZMA1 stream that any other implementation can decode.
///
/// NOTE: LZMA is Igor Pavlov's format. The specification and the reference sources were placed in the public
/// domain, so nothing here carries a licence obligation; these files are written from the format description
/// rather than copied, and the constant names follow the reference only so the two can be read side by side.
/// </summary>
internal static class LzmaConstants
{
    public const int LiteralContextBits = 3;
    public const int LiteralPositionBits = 0;
    public const int PositionBits = 2;
    public const uint PositionMask = (1u << PositionBits) - 1;
    public const int PositionStateCount = 1 << PositionBits;

    public const int NumberOfStates = 12;
    public const int NumberOfPositionBitsMax = 4;
    public const int NumberOfLengthToPositionStates = 4;
    public const int NumberOfPositionSlotBits = 6;
    public const int NumberOfAlignBits = 4;
    public const int AlignTableSize = 1 << NumberOfAlignBits;
    public const uint AlignMask = AlignTableSize - 1;
    public const int StartPositionModelIndex = 4;
    public const int EndPositionModelIndex = 14;
    public const int NumberOfFullDistances = 1 << (EndPositionModelIndex >> 1);

    public const int MatchMinimumLength = 2;
    public const int LengthLowBits = 3;
    public const int LengthMidBits = 3;
    public const int LengthHighBits = 8;
    public const int LengthLowSymbols = 1 << LengthLowBits;
    public const int LengthMidSymbols = 1 << LengthMidBits;
    public const int LengthHighSymbols = 1 << LengthHighBits;
    public const int LengthSymbols = LengthLowSymbols + LengthMidSymbols + LengthHighSymbols;
    public const int MatchMaximumLength = MatchMinimumLength + LengthSymbols - 1;

    // Adaptive binary model: an 11 bit probability that moves a 32nd of the way towards the observed bit
    public const int BitModelTotalBits = 11;
    public const uint BitModelTotal = 1u << BitModelTotalBits;
    public const int MoveBits = 5;
    public const ushort ProbabilityInitialValue = (ushort)(BitModelTotal / 2);
    public const uint TopValue = 1u << 24;

    // Probability array sizes, in the layout both coders index into
    public const int IsMatchSize = NumberOfStates << NumberOfPositionBitsMax;
    public const int IsRep0LongSize = NumberOfStates << NumberOfPositionBitsMax;
    public const int PositionSlotSize = NumberOfLengthToPositionStates << NumberOfPositionSlotBits;
    public const int SpecialPositionSize = NumberOfFullDistances - EndPositionModelIndex;
    public const int LiteralCoderSize = 0x300;
    public const int LiteralSize = LiteralCoderSize << (LiteralContextBits + LiteralPositionBits);
    public const int LengthLowSize = PositionStateCount << LengthLowBits;
    public const int LengthMidSize = PositionStateCount << LengthMidBits;
    public const int LengthHighSize = LengthHighSymbols;
    public const int LengthChoiceSize = 2;

    /// <summary>Length of the properties blob: the packed lc/lp/pb byte then a little-endian dictionary size.</summary>
    public const int PropertiesLength = 5;

    public const int MinimumDictionarySize = 1 << 12;
    public const int MaximumDictionarySize = 1 << 26;

    /// <summary>Packs the literal and position bit counts the way the properties byte encodes them.</summary>
    public static byte PackProperties(int literalContextBits, int literalPositionBits, int positionBits) =>
        (byte)(((positionBits * 5) + literalPositionBits) * 9 + literalContextBits);

    /// <summary>Unpacks the properties byte. Returns false for a byte no conformant encoder can produce.</summary>
    public static bool TryUnpackProperties(
        byte properties,
        out int literalContextBits,
        out int literalPositionBits,
        out int positionBits)
    {
        literalContextBits = 0;
        literalPositionBits = 0;
        positionBits = 0;
        // The maximum legal value is (4 * 5 + 4) * 9 + 8
        if (properties >= (4 * 5 + 4) * 9 + 9) return false;

        literalContextBits = properties % 9;
        int remainder = properties / 9;
        literalPositionBits = remainder % 5;
        positionBits = remainder / 5;
        return true;
    }

    public static bool IsLiteralState(uint state) => state < 7;

    public static uint StateUpdateLiteral(uint state) => state < 4 ? 0u : state < 10 ? state - 3 : state - 6;

    public static uint StateUpdateMatch(uint state) => state < 7 ? 7u : 10u;

    public static uint StateUpdateRep(uint state) => state < 7 ? 8u : 11u;

    public static uint StateUpdateShortRep(uint state) => state < 7 ? 9u : 11u;

    /// <summary>Lengths 2, 3 and 4 each select their own distance slot model; everything longer shares the last.</summary>
    public static uint GetLengthToPositionState(int length) =>
        length - MatchMinimumLength < NumberOfLengthToPositionStates
            ? (uint)(length - MatchMinimumLength)
            : NumberOfLengthToPositionStates - 1;

    /// <summary>
    /// Maps a zero based distance to its slot: the index of the highest set bit doubled, plus the bit below
    /// it. Distances under 4 are their own slot. Slot n therefore spans a range whose width doubles every
    /// two slots, which is what makes the footer bit count (slot >> 1) - 1.
    /// </summary>
    public static uint GetPositionSlot(uint distance)
    {
        if (distance < StartPositionModelIndex) return distance;

        int highestBit = HighestSetBitIndex(distance);
        return (uint)((highestBit << 1) | (int)((distance >> (highestBit - 1)) & 1));
    }

    /// <summary>Index of the most significant set bit. The .NET Framework has no BitOperations.</summary>
    public static int HighestSetBitIndex(uint value)
    {
        int index = 0;
        if ((value & 0xFFFF0000u) != 0)
        {
            value >>= 16;
            index += 16;
        }

        if ((value & 0x0000FF00u) != 0)
        {
            value >>= 8;
            index += 8;
        }

        if ((value & 0x000000F0u) != 0)
        {
            value >>= 4;
            index += 4;
        }

        if ((value & 0x0000000Cu) != 0)
        {
            value >>= 2;
            index += 2;
        }

        if ((value & 0x00000002u) != 0) index += 1;

        return index;
    }

    /// <summary>Allocates a probability array with every model at the neutral midpoint.</summary>
    public static ushort[] NewProbabilities(int length)
    {
        ushort[] probabilities = new ushort[length];
        for (int index = 0; index < probabilities.Length; index++)
            probabilities[index] = ProbabilityInitialValue;

        return probabilities;
    }
}
