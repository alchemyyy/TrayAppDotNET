namespace TrayAppDotNETInstaller.Compression;

/// <summary>
/// LZMA1 encoder: a hash chain match finder with one step lazy evaluation feeding the standard range coder.
/// It is not an optimal parser, so it gives up a few percent of ratio against 7-Zip at its highest setting;
/// in exchange it is small, has no tuning tables, and runs in a single pass. The bitstream it produces is an
/// ordinary LZMA1 stream either way, which is what makes it safe to hand written: the format is pinned by an
/// external specification and verified against reference streams in the tests.
///
/// The parse decisions below only affect compression ratio. Correctness of the output depends solely on the
/// range coder and the symbol encoders, which are a direct transcription of the specification.
/// </summary>
public static class LzmaEncoder
{
    // A 4 byte prefix hash: selective enough that chains stay short on binary data, and short matches are
    // still reachable through the four remembered distances
    private const int HashByteCount = 4;
    private const uint HashMultiplier = 2654435761;
    private const int MinimumHashBits = 16;
    private const int MaximumHashBits = 23;
    // How many chain entries one position is willing to inspect before settling for what it has
    private const int MaximumChainDepth = 32;
    // A match this long is taken immediately; looking for better rarely pays for the search
    private const int NiceLength = 64;
    private const int MinimumDictionaryBits = 16;
    private const int MaximumDictionaryBits = 25;

    /// <summary>
    /// Compresses the first <paramref name="inputLength"/> bytes of <paramref name="input"/> into
    /// <paramref name="output"/> and returns the 5 properties bytes a decoder needs. The decoder must also be
    /// told the uncompressed length; no end of stream marker is written.
    /// </summary>
    public static byte[] Compress(byte[] input, int inputLength, Stream output)
    {
        FrameworkCompatibility.ThrowIfNull(input, nameof(input));
        FrameworkCompatibility.ThrowIfNull(output, nameof(output));

        if (inputLength < 0 || inputLength > input.Length)
            throw new ArgumentOutOfRangeException(nameof(inputLength), inputLength, "The length lies outside the buffer.");

        uint dictionarySize = ChooseDictionarySize(inputLength);
        byte[] properties = new byte[LzmaConstants.PropertiesLength];
        properties[0] = LzmaConstants.PackProperties(
            LzmaConstants.LiteralContextBits,
            LzmaConstants.LiteralPositionBits,
            LzmaConstants.PositionBits);
        FrameworkCompatibility.WriteUInt32LittleEndian(properties, offset: 1, dictionarySize);
        if (inputLength == 0) return properties;

        EncoderState state = new(input, inputLength, output, dictionarySize);
        state.Run();
        return properties;
    }

    /// <summary>The smallest power of two window that still reaches every byte of the input.</summary>
    private static uint ChooseDictionarySize(int inputLength)
    {
        int bits = MinimumDictionaryBits;
        while (bits < MaximumDictionaryBits && (1L << bits) < inputLength) bits++;

        return 1u << bits;
    }

    /// <summary>Range coder, probability models, match finder and the parse loop for one input buffer.</summary>
    private sealed class EncoderState
    {
        private readonly byte[] _input;
        private readonly int _inputLength;
        private readonly Stream _output;
        private readonly uint _dictionarySize;

        // Head of the chain per hash bucket, and the next older position sharing it. The chain is cyclic
        // over the dictionary because anything further back is out of reach anyway.
        private readonly int[] _hashHeads;
        private readonly int[] _hashChain;
        private readonly int _hashShift;
        private readonly int _chainMask;

        private readonly ushort[] _isMatch = LzmaConstants.NewProbabilities(LzmaConstants.IsMatchSize);
        private readonly ushort[] _isRep = LzmaConstants.NewProbabilities(LzmaConstants.NumberOfStates);
        private readonly ushort[] _isRepG0 = LzmaConstants.NewProbabilities(LzmaConstants.NumberOfStates);
        private readonly ushort[] _isRepG1 = LzmaConstants.NewProbabilities(LzmaConstants.NumberOfStates);
        private readonly ushort[] _isRepG2 = LzmaConstants.NewProbabilities(LzmaConstants.NumberOfStates);
        private readonly ushort[] _isRep0Long = LzmaConstants.NewProbabilities(LzmaConstants.IsRep0LongSize);
        private readonly ushort[] _positionSlot = LzmaConstants.NewProbabilities(LzmaConstants.PositionSlotSize);
        private readonly ushort[] _specialPosition = LzmaConstants.NewProbabilities(LzmaConstants.SpecialPositionSize);
        private readonly ushort[] _align = LzmaConstants.NewProbabilities(LzmaConstants.AlignTableSize);
        private readonly ushort[] _literal = LzmaConstants.NewProbabilities(LzmaConstants.LiteralSize);
        private readonly LengthModel _lengthModel = new();
        private readonly LengthModel _repeatLengthModel = new();

        private ulong _low;
        private uint _range = 0xFFFFFFFF;
        private byte _cache;
        private long _cacheSize = 1;

        private uint _state;
        private uint _rep0;
        private uint _rep1;
        private uint _rep2;
        private uint _rep3;
        private int _position;
        private int _insertedCount;

        public EncoderState(byte[] input, int inputLength, Stream output, uint dictionarySize)
        {
            _input = input;
            _inputLength = inputLength;
            _output = output;
            _dictionarySize = dictionarySize;

            int hashBits = MinimumHashBits;
            while (hashBits < MaximumHashBits && (1L << hashBits) < inputLength) hashBits++;
            _hashShift = 32 - hashBits;
            _hashHeads = new int[1 << hashBits];
            for (int index = 0; index < _hashHeads.Length; index++) _hashHeads[index] = -1;

            _hashChain = new int[dictionarySize];
            _chainMask = (int)(dictionarySize - 1);
        }

        public void Run()
        {
            // Position 0 has no history, so it can only be a literal; the decoder depends on that
            EncodeLiteral(position: 0, positionState: 0);
            _position = 1;

            while (_position < _inputLength)
            {
                InsertBefore(_position);
                FindMatch(_position, out int length, out uint distance, out int repeatIndex);
                if (ShouldDeferToNextPosition(length, distance, repeatIndex)) length = 0;

                uint positionState = (uint)_position & LzmaConstants.PositionMask;
                if (length >= LzmaConstants.MatchMinimumLength)
                {
                    if (repeatIndex >= 0) EncodeRepeatMatch(repeatIndex, length, positionState);
                    else EncodeMatch(distance, length, positionState);

                    _position += length;
                    continue;
                }

                EncodeLiteral(_position, positionState);
                _position++;
            }

            Flush();
        }

        /// <summary>
        /// One step lazy evaluation: when the match here is unremarkable, a literal now plus whatever starts
        /// one byte later often codes shorter. Only the ratio depends on this; the stream stays valid either
        /// way.
        /// </summary>
        private bool ShouldDeferToNextPosition(int length, uint distance, int repeatIndex)
        {
            if (length < LzmaConstants.MatchMinimumLength || length >= NiceLength) return false;
            if (_position + 1 >= _inputLength) return false;

            InsertBefore(_position + 1);
            FindMatch(_position + 1, out int nextLength, out uint nextDistance, out int nextRepeatIndex);
            if (nextLength < LzmaConstants.MatchMinimumLength) return false;
            if (nextLength > length) return true;
            if (nextLength < length) return false;

            // Same length: prefer whichever is cheaper to code, which means a remembered distance first and
            // then a substantially nearer one
            if (nextRepeatIndex >= 0 && repeatIndex < 0) return true;
            if (nextRepeatIndex < 0 && repeatIndex >= 0) return false;

            return repeatIndex < 0 && nextDistance < distance / 4;
        }

        /// <summary>
        /// Finds the best match at <paramref name="position"/>. <paramref name="bestRepeatIndex"/> is the
        /// index into the four remembered distances, or -1 for a plain match. A length below the minimum
        /// means the position should be coded as a literal.
        /// </summary>
        private void FindMatch(int position, out int bestLength, out uint bestDistance, out int bestRepeatIndex)
        {
            bestLength = 0;
            bestDistance = 0;
            bestRepeatIndex = -1;
            int maximumLength = Math.Min(LzmaConstants.MatchMaximumLength, _inputLength - position);
            if (maximumLength < LzmaConstants.MatchMinimumLength) return;

            // The four remembered distances cost almost nothing to code, so they are tried first and win ties
            for (int index = 0; index < 4; index++)
            {
                uint repeatDistance = RepeatDistanceAt(index);
                if (repeatDistance >= (uint)position) continue;

                int start = position - (int)repeatDistance - 1;
                int length = MatchLength(start, position, maximumLength);
                if (length < LzmaConstants.MatchMinimumLength || length <= bestLength) continue;

                bestLength = length;
                bestDistance = repeatDistance;
                bestRepeatIndex = index;
            }

            if (bestLength >= NiceLength || position + HashByteCount > _inputLength) return;

            int chainLength = 0;
            uint chainDistance = 0;
            int oldestReachable = position - (int)Math.Min(_dictionarySize, (uint)position);
            int candidate = _hashHeads[ComputeHash(position)];
            for (int depth = MaximumChainDepth; depth > 0 && candidate >= oldestReachable; depth--)
            {
                int length = MatchLength(candidate, position, maximumLength);
                if (length > chainLength)
                {
                    chainLength = length;
                    chainDistance = (uint)(position - candidate - 1);
                    if (length >= NiceLength) break;
                }

                int older = _hashChain[candidate & _chainMask];
                // The cyclic chain recycles slots, so a link that does not go backwards is a stale entry
                if (older >= candidate) break;

                candidate = older;
            }

            if (chainLength <= bestLength || !IsPlainMatchWorthCoding(chainLength, chainDistance)) return;

            bestLength = chainLength;
            bestDistance = chainDistance;
            bestRepeatIndex = -1;
        }

        /// <summary>
        /// A plain match pays for a distance the literal coder does not, so a short one is only cheaper than
        /// the literals it replaces while the distance stays small. The thresholds come from the bit costs:
        /// a distance of d takes roughly log2(d) + 12 bits, against about 8 bits per literal. Remembered
        /// distances skip this because they carry no distance at all.
        /// </summary>
        private static bool IsPlainMatchWorthCoding(int length, uint distance) =>
            length switch
            {
                2 => distance < 1u << 7,
                3 => distance < 1u << 11,
                4 => distance < 1u << 19,
                _ => true
            };

        private uint RepeatDistanceAt(int index) =>
            index switch
            {
                0 => _rep0,
                1 => _rep1,
                2 => _rep2,
                _ => _rep3
            };

        private int MatchLength(int start, int position, int maximumLength)
        {
            int length = 0;
            while (length < maximumLength && _input[start + length] == _input[position + length]) length++;

            return length;
        }

        /// <summary>Adds every position below <paramref name="position"/> to the chain, exactly once.</summary>
        private void InsertBefore(int position)
        {
            int limit = Math.Min(position, _inputLength);
            while (_insertedCount < limit)
            {
                int candidate = _insertedCount++;
                if (candidate + HashByteCount > _inputLength) continue;

                uint hash = ComputeHash(candidate);
                _hashChain[candidate & _chainMask] = _hashHeads[hash];
                _hashHeads[hash] = candidate;
            }
        }

        private uint ComputeHash(int position)
        {
            uint word = (uint)(_input[position]
                               | (_input[position + 1] << 8)
                               | (_input[position + 2] << 16)
                               | (_input[position + 3] << 24));
            return (word * HashMultiplier) >> _hashShift;
        }

        private void EncodeLiteral(int position, uint positionState)
        {
            EncodeBit(_isMatch, (_state << LzmaConstants.NumberOfPositionBitsMax) + positionState, bit: 0);

            byte previousByte = position == 0 ? (byte)0 : _input[position - 1];
            // The literal position bits are zero, so only the previous byte selects the sub coder
            uint offset = (uint)(previousByte >> (8 - LzmaConstants.LiteralContextBits)) * LzmaConstants.LiteralCoderSize;
            byte symbol = _input[position];
            if (LzmaConstants.IsLiteralState(_state)) EncodeNormalLiteral(offset, symbol);
            else EncodeMatchedLiteral(offset, symbol, _input[position - (int)_rep0 - 1]);

            _state = LzmaConstants.StateUpdateLiteral(_state);
        }

        private void EncodeNormalLiteral(uint offset, byte symbol)
        {
            uint context = 1;
            for (int bitIndex = 7; bitIndex >= 0; bitIndex--)
            {
                uint bit = (uint)(symbol >> bitIndex) & 1;
                EncodeBit(_literal, offset + context, bit);
                context = (context << 1) | bit;
            }
        }

        private void EncodeMatchedLiteral(uint offset, byte symbol, byte matchByte)
        {
            uint context = 1;
            bool stillMatching = true;
            for (int bitIndex = 7; bitIndex >= 0; bitIndex--)
            {
                uint bit = (uint)(symbol >> bitIndex) & 1;
                uint index = context;
                if (stillMatching)
                {
                    uint matchBit = (uint)(matchByte >> bitIndex) & 1;
                    index += (1 + matchBit) << 8;
                    stillMatching = matchBit == bit;
                }

                EncodeBit(_literal, offset + index, bit);
                context = (context << 1) | bit;
            }
        }

        private void EncodeMatch(uint distance, int length, uint positionState)
        {
            EncodeBit(_isMatch, (_state << LzmaConstants.NumberOfPositionBitsMax) + positionState, bit: 1);
            EncodeBit(_isRep, _state, bit: 0);
            _state = LzmaConstants.StateUpdateMatch(_state);
            _lengthModel.Encode(this, length - LzmaConstants.MatchMinimumLength, positionState);

            uint slot = LzmaConstants.GetPositionSlot(distance);
            EncodeBitTree(
                _positionSlot,
                LzmaConstants.GetLengthToPositionState(length) << LzmaConstants.NumberOfPositionSlotBits,
                LzmaConstants.NumberOfPositionSlotBits,
                slot);
            if (slot >= LzmaConstants.StartPositionModelIndex)
            {
                int footerBits = (int)((slot >> 1) - 1);
                uint baseValue = (2 | (slot & 1)) << footerBits;
                uint reduced = distance - baseValue;
                if (slot < LzmaConstants.EndPositionModelIndex)
                {
                    // The sub models for every slot below the cut off are packed end to end; slot 4 starts at
                    // an offset of -1 and relies on the unsigned wrap, exactly as the reference does
                    EncodeBitTreeReverse(_specialPosition, baseValue - slot - 1, footerBits, reduced);
                }
                else
                {
                    EncodeDirectBits(reduced >> LzmaConstants.NumberOfAlignBits, footerBits - LzmaConstants.NumberOfAlignBits);
                    EncodeBitTreeReverse(_align, offset: 0, LzmaConstants.NumberOfAlignBits, reduced & LzmaConstants.AlignMask);
                }
            }

            _rep3 = _rep2;
            _rep2 = _rep1;
            _rep1 = _rep0;
            _rep0 = distance;
        }

        private void EncodeRepeatMatch(int repeatIndex, int length, uint positionState)
        {
            EncodeBit(_isMatch, (_state << LzmaConstants.NumberOfPositionBitsMax) + positionState, bit: 1);
            EncodeBit(_isRep, _state, bit: 1);
            if (repeatIndex == 0)
            {
                EncodeBit(_isRepG0, _state, bit: 0);
                EncodeBit(_isRep0Long, (_state << LzmaConstants.NumberOfPositionBitsMax) + positionState, bit: 1);
            }
            else
            {
                EncodeBit(_isRepG0, _state, bit: 1);
                uint distance = RepeatDistanceAt(repeatIndex);
                if (repeatIndex == 1)
                {
                    EncodeBit(_isRepG1, _state, bit: 0);
                }
                else
                {
                    EncodeBit(_isRepG1, _state, bit: 1);
                    EncodeBit(_isRepG2, _state, (uint)(repeatIndex - 2));
                    if (repeatIndex == 3) _rep3 = _rep2;

                    _rep2 = _rep1;
                }

                _rep1 = _rep0;
                _rep0 = distance;
            }

            _repeatLengthModel.Encode(this, length - LzmaConstants.MatchMinimumLength, positionState);
            _state = LzmaConstants.StateUpdateRep(_state);
        }

        public void EncodeBit(ushort[] probabilities, uint index, uint bit)
        {
            uint probability = probabilities[index];
            uint bound = (_range >> LzmaConstants.BitModelTotalBits) * probability;
            if (bit == 0)
            {
                _range = bound;
                probabilities[index] = (ushort)(probability + ((LzmaConstants.BitModelTotal - probability) >> LzmaConstants.MoveBits));
            }
            else
            {
                _low += bound;
                _range -= bound;
                probabilities[index] = (ushort)(probability - (probability >> LzmaConstants.MoveBits));
            }

            // One shift always suffices: the model clamps probabilities to [31, 2017], so the range cannot
            // fall below 2^17 here and a single byte shift carries it back past 2^24
            if (_range >= LzmaConstants.TopValue) return;

            _range <<= 8;
            ShiftLow();
        }

        public void EncodeBitTree(ushort[] probabilities, uint offset, int bitCount, uint symbol)
        {
            uint context = 1;
            for (int bitIndex = bitCount - 1; bitIndex >= 0; bitIndex--)
            {
                uint bit = (symbol >> bitIndex) & 1;
                EncodeBit(probabilities, offset + context, bit);
                context = (context << 1) | bit;
            }
        }

        public void EncodeBitTreeReverse(ushort[] probabilities, uint offset, int bitCount, uint symbol)
        {
            uint context = 1;
            for (int bitIndex = 0; bitIndex < bitCount; bitIndex++)
            {
                uint bit = symbol & 1;
                symbol >>= 1;
                EncodeBit(probabilities, offset + context, bit);
                context = (context << 1) | bit;
            }
        }

        private void EncodeDirectBits(uint value, int bitCount)
        {
            for (int bitIndex = bitCount - 1; bitIndex >= 0; bitIndex--)
            {
                _range >>= 1;
                if (((value >> bitIndex) & 1) != 0) _low += _range;
                if (_range >= LzmaConstants.TopValue) continue;

                _range <<= 8;
                ShiftLow();
            }
        }

        private void Flush()
        {
            for (int index = 0; index < 5; index++) ShiftLow();

            _output.Flush();
        }

        /// <summary>
        /// Emits the byte that has settled and carries any overflow back through the run of pending 0xFF
        /// bytes. The first call always emits the padding zero the decoder expects.
        /// </summary>
        private void ShiftLow()
        {
            uint carry = (uint)(_low >> 32);
            if ((uint)_low < 0xFF000000u || carry == 1)
            {
                byte pending = _cache;
                do
                {
                    _output.WriteByte((byte)(pending + carry));
                    pending = 0xFF;
                }
                while (--_cacheSize != 0);

                _cache = (byte)(_low >> 24);
            }

            _cacheSize++;
            _low = ((uint)_low) << 8;
        }

        /// <summary>The 3 way length model: 3 low bits, 3 mid bits, or 8 high bits, behind two choice bits.</summary>
        private sealed class LengthModel
        {
            private readonly ushort[] _choice = LzmaConstants.NewProbabilities(LzmaConstants.LengthChoiceSize);
            private readonly ushort[] _low = LzmaConstants.NewProbabilities(LzmaConstants.LengthLowSize);
            private readonly ushort[] _mid = LzmaConstants.NewProbabilities(LzmaConstants.LengthMidSize);
            private readonly ushort[] _high = LzmaConstants.NewProbabilities(LzmaConstants.LengthHighSize);

            /// <summary>Encodes a length symbol, which is the match length minus the minimum match length.</summary>
            public void Encode(EncoderState state, int symbol, uint positionState)
            {
                if (symbol < LzmaConstants.LengthLowSymbols)
                {
                    state.EncodeBit(_choice, index: 0, bit: 0);
                    state.EncodeBitTree(
                        _low, positionState << LzmaConstants.LengthLowBits, LzmaConstants.LengthLowBits, (uint)symbol);
                    return;
                }

                state.EncodeBit(_choice, index: 0, bit: 1);
                symbol -= LzmaConstants.LengthLowSymbols;
                if (symbol < LzmaConstants.LengthMidSymbols)
                {
                    state.EncodeBit(_choice, index: 1, bit: 0);
                    state.EncodeBitTree(
                        _mid, positionState << LzmaConstants.LengthMidBits, LzmaConstants.LengthMidBits, (uint)symbol);
                    return;
                }

                state.EncodeBit(_choice, index: 1, bit: 1);
                state.EncodeBitTree(
                    _high, offset: 0, LzmaConstants.LengthHighBits, (uint)(symbol - LzmaConstants.LengthMidSymbols));
            }
        }
    }
}
