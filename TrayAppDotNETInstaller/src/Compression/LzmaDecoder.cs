namespace TrayAppDotNETInstaller.Compression;

/// <summary>
/// LZMA1 decoder. It is deliberately a plain port of the reference decoder: the installer has to be able to
/// read streams produced by anything conformant, so the structure follows the specification rather than
/// anything convenient.
///
/// Output goes straight to a <see cref="Stream"/> through a sliding window sized by the dictionary, so the
/// whole decompressed payload never has to be resident.
/// </summary>
public static class LzmaDecoder
{
    private const int LeadingByteCount = 5;
    private const int InputBufferLength = 64 * 1024;
    private const uint EndOfStreamDistance = 0xFFFFFFFF;

    /// <summary>
    /// Decodes exactly <paramref name="uncompressedLength"/> bytes from a raw LZMA1 stream. An end of stream
    /// marker is accepted but only when it arrives after the expected length has been produced.
    /// </summary>
    /// <param name="input">The compressed bytes, positioned at the first byte of the range coded stream.</param>
    /// <param name="output">Receives the decompressed bytes.</param>
    /// <param name="properties">5 bytes: the packed lc/lp/pb byte then the little-endian dictionary size.</param>
    /// <param name="uncompressedLength">The exact number of bytes the stream decodes to.</param>
    public static void Decode(Stream input, Stream output, byte[] properties, long uncompressedLength)
    {
        FrameworkCompatibility.ThrowIfNull(input, nameof(input));
        FrameworkCompatibility.ThrowIfNull(output, nameof(output));
        FrameworkCompatibility.ThrowIfNull(properties, nameof(properties));

        if (properties.Length < LzmaConstants.PropertiesLength)
        {
            throw new InvalidDataException(
                $"An LZMA properties blob is {LzmaConstants.PropertiesLength} bytes, but {properties.Length} were supplied.");
        }

        if (uncompressedLength < 0)
            throw new ArgumentOutOfRangeException(nameof(uncompressedLength), uncompressedLength, "A decoded length cannot be negative.");

        if (!LzmaConstants.TryUnpackProperties(
                properties[0],
                out int literalContextBits,
                out int literalPositionBits,
                out int positionBits))
        {
            throw new InvalidDataException($"The LZMA properties byte 0x{properties[0]:X2} is not a legal lc/lp/pb packing.");
        }

        uint dictionarySize = FrameworkCompatibility.ReadUInt32LittleEndian(properties, offset: 1);
        if (dictionarySize < LzmaConstants.MinimumDictionarySize) dictionarySize = LzmaConstants.MinimumDictionarySize;
        if (dictionarySize > LzmaConstants.MaximumDictionarySize)
            throw new InvalidDataException($"The LZMA stream asks for a {dictionarySize} byte dictionary, more than this decoder allows.");

        // Nothing beyond the decoded length is ever addressable, so a short payload gets a short window
        int windowSize = (int)Math.Min(dictionarySize, Math.Max(uncompressedLength, 1));
        if (uncompressedLength == 0) return;

        DecoderState state = new(input, output, literalContextBits, literalPositionBits, positionBits, windowSize, dictionarySize);
        state.Run(uncompressedLength);
    }

    /// <summary>The whole decode: range decoder, probability models, sliding window and the symbol loop.</summary>
    private sealed class DecoderState
    {
        private readonly Stream _input;
        private readonly LzmaOutputWindow _window;
        private readonly int _literalContextBits;
        private readonly uint _literalPositionMask;
        private readonly uint _positionMask;
        private readonly uint _dictionarySize;

        private readonly ushort[] _isMatch = LzmaConstants.NewProbabilities(LzmaConstants.IsMatchSize);
        private readonly ushort[] _isRep = LzmaConstants.NewProbabilities(LzmaConstants.NumberOfStates);
        private readonly ushort[] _isRepG0 = LzmaConstants.NewProbabilities(LzmaConstants.NumberOfStates);
        private readonly ushort[] _isRepG1 = LzmaConstants.NewProbabilities(LzmaConstants.NumberOfStates);
        private readonly ushort[] _isRepG2 = LzmaConstants.NewProbabilities(LzmaConstants.NumberOfStates);
        private readonly ushort[] _isRep0Long = LzmaConstants.NewProbabilities(LzmaConstants.IsRep0LongSize);
        private readonly ushort[] _positionSlot = LzmaConstants.NewProbabilities(LzmaConstants.PositionSlotSize);
        private readonly ushort[] _specialPosition = LzmaConstants.NewProbabilities(LzmaConstants.SpecialPositionSize);
        private readonly ushort[] _align = LzmaConstants.NewProbabilities(LzmaConstants.AlignTableSize);
        private readonly ushort[] _literal;
        private readonly LzmaLengthModel _lengthModel;
        private readonly LzmaLengthModel _repeatLengthModel;

        private readonly byte[] _inputBuffer = new byte[InputBufferLength];
        private int _inputFilled;
        private int _inputCursor;
        private uint _range = 0xFFFFFFFF;
        private uint _code;

        public DecoderState(
            Stream input,
            Stream output,
            int literalContextBits,
            int literalPositionBits,
            int positionBits,
            int windowSize,
            uint dictionarySize)
        {
            _input = input;
            _window = new LzmaOutputWindow(output, windowSize);
            _literalContextBits = literalContextBits;
            _literalPositionMask = (1u << literalPositionBits) - 1;
            _positionMask = (1u << positionBits) - 1;
            _dictionarySize = dictionarySize;
            _literal = LzmaConstants.NewProbabilities(
                LzmaConstants.LiteralCoderSize << (literalContextBits + literalPositionBits));
            _lengthModel = new LzmaLengthModel(positionBits);
            _repeatLengthModel = new LzmaLengthModel(positionBits);

            // The first byte of a range coded stream is a padding zero produced by the encoder's flush
            if (ReadByte() != 0)
                throw new InvalidDataException("An LZMA stream must begin with a zero byte.");

            for (int index = 1; index < LeadingByteCount; index++) _code = (_code << 8) | ReadByte();
        }

        public void Run(long uncompressedLength)
        {
            uint state = 0;
            uint rep0 = 0;
            uint rep1 = 0;
            uint rep2 = 0;
            uint rep3 = 0;
            long produced = 0;

            // Position 0 has no history to reference, so the encoder is required to emit a literal there
            if (DecodeBit(_isMatch, (state << LzmaConstants.NumberOfPositionBitsMax) + 0) != 0)
                throw new InvalidDataException("An LZMA stream cannot open with a match.");

            _window.PutByte(DecodeNormalLiteral(position: 0, previousByte: 0));
            state = LzmaConstants.StateUpdateLiteral(state);
            produced++;

            while (produced < uncompressedLength)
            {
                uint positionState = (uint)produced & _positionMask;
                if (DecodeBit(_isMatch, (state << LzmaConstants.NumberOfPositionBitsMax) + positionState) == 0)
                {
                    byte previousByte = _window.GetByte(distance: 0);
                    byte decoded = LzmaConstants.IsLiteralState(state)
                        ? DecodeNormalLiteral(produced, previousByte)
                        : DecodeMatchedLiteral(produced, previousByte, _window.GetByte(rep0));
                    _window.PutByte(decoded);
                    state = LzmaConstants.StateUpdateLiteral(state);
                    produced++;
                    continue;
                }

                int length;
                if (DecodeBit(_isRep, state) != 0)
                {
                    if (DecodeBit(_isRepG0, state) == 0)
                    {
                        if (DecodeBit(_isRep0Long, (state << LzmaConstants.NumberOfPositionBitsMax) + positionState) == 0)
                        {
                            // A short repeat: one more byte at the last used distance
                            if (rep0 >= produced) throw CorruptDistance(rep0, produced);

                            state = LzmaConstants.StateUpdateShortRep(state);
                            _window.PutByte(_window.GetByte(rep0));
                            produced++;
                            continue;
                        }
                    }
                    else
                    {
                        uint distance;
                        if (DecodeBit(_isRepG1, state) == 0)
                        {
                            distance = rep1;
                        }
                        else
                        {
                            if (DecodeBit(_isRepG2, state) == 0)
                            {
                                distance = rep2;
                            }
                            else
                            {
                                distance = rep3;
                                rep3 = rep2;
                            }

                            rep2 = rep1;
                        }

                        rep1 = rep0;
                        rep0 = distance;
                    }

                    length = _repeatLengthModel.Decode(this, positionState) + LzmaConstants.MatchMinimumLength;
                    state = LzmaConstants.StateUpdateRep(state);
                }
                else
                {
                    rep3 = rep2;
                    rep2 = rep1;
                    rep1 = rep0;
                    length = _lengthModel.Decode(this, positionState) + LzmaConstants.MatchMinimumLength;
                    state = LzmaConstants.StateUpdateMatch(state);
                    rep0 = DecodeDistance(length);
                    if (rep0 == EndOfStreamDistance)
                    {
                        // A conformant encoder may append an end marker; it is only legal once the declared
                        // length has been produced, and this loop only runs while bytes are still owed
                        throw new InvalidDataException(
                            $"The LZMA stream ended after {produced} of {uncompressedLength} bytes.");
                    }
                }

                if (rep0 >= produced || rep0 >= _dictionarySize) throw CorruptDistance(rep0, produced);
                if (produced + length > uncompressedLength)
                {
                    throw new InvalidDataException(
                        $"An LZMA match of {length} bytes at {produced} overruns the declared length of {uncompressedLength}.");
                }

                _window.CopyBlock(rep0, length);
                produced += length;
            }

            _window.Flush();
        }

        public uint DecodeBit(ushort[] probabilities, uint index)
        {
            uint probability = probabilities[index];
            uint bound = (_range >> LzmaConstants.BitModelTotalBits) * probability;
            uint bit;
            if (_code < bound)
            {
                _range = bound;
                probabilities[index] = (ushort)(probability + ((LzmaConstants.BitModelTotal - probability) >> LzmaConstants.MoveBits));
                bit = 0;
            }
            else
            {
                _range -= bound;
                _code -= bound;
                probabilities[index] = (ushort)(probability - (probability >> LzmaConstants.MoveBits));
                bit = 1;
            }

            // One shift always suffices: the model clamps probabilities to [31, 2017], so the range cannot
            // fall below 2^17 here and a single byte shift carries it back past 2^24
            if (_range < LzmaConstants.TopValue)
            {
                _range <<= 8;
                _code = (_code << 8) | ReadByte();
            }

            return bit;
        }

        public uint DecodeDirectBits(int bitCount)
        {
            uint result = 0;
            for (int index = 0; index < bitCount; index++)
            {
                _range >>= 1;
                _code -= _range;
                uint mask = 0 - (_code >> 31);
                _code += _range & mask;
                result = (result << 1) + mask + 1;
                if (_range >= LzmaConstants.TopValue) continue;

                _range <<= 8;
                _code = (_code << 8) | ReadByte();
            }

            return result;
        }

        public uint DecodeBitTree(ushort[] probabilities, uint offset, int bitCount)
        {
            uint context = 1;
            for (int index = 0; index < bitCount; index++)
                context = (context << 1) | DecodeBit(probabilities, offset + context);

            return context - (1u << bitCount);
        }

        public uint DecodeBitTreeReverse(ushort[] probabilities, uint offset, int bitCount)
        {
            uint context = 1;
            uint result = 0;
            for (int index = 0; index < bitCount; index++)
            {
                uint bit = DecodeBit(probabilities, offset + context);
                context = (context << 1) | bit;
                result |= bit << index;
            }

            return result;
        }

        private uint DecodeDistance(int length)
        {
            uint positionSlot = DecodeBitTree(
                _positionSlot,
                LzmaConstants.GetLengthToPositionState(length) << LzmaConstants.NumberOfPositionSlotBits,
                LzmaConstants.NumberOfPositionSlotBits);
            if (positionSlot < LzmaConstants.StartPositionModelIndex) return positionSlot;

            int footerBits = (int)((positionSlot >> 1) - 1);
            uint distance = (2 | (positionSlot & 1)) << footerBits;
            if (positionSlot < LzmaConstants.EndPositionModelIndex)
                return distance + DecodeBitTreeReverse(_specialPosition, distance - positionSlot - 1, footerBits);

            distance += DecodeDirectBits(footerBits - LzmaConstants.NumberOfAlignBits) << LzmaConstants.NumberOfAlignBits;
            return distance + DecodeBitTreeReverse(_align, offset: 0, LzmaConstants.NumberOfAlignBits);
        }

        private byte DecodeNormalLiteral(long position, byte previousByte)
        {
            uint offset = LiteralOffset(position, previousByte);
            uint context = 1;
            do
            {
                context = (context << 1) | DecodeBit(_literal, offset + context);
            }
            while (context < 0x100);

            return (byte)context;
        }

        private byte DecodeMatchedLiteral(long position, byte previousByte, byte matchByte)
        {
            uint offset = LiteralOffset(position, previousByte);
            uint context = 1;
            do
            {
                uint matchBit = (uint)(matchByte >> 7) & 1;
                matchByte <<= 1;
                uint bit = DecodeBit(_literal, offset + (((1 + matchBit) << 8) + context));
                context = (context << 1) | bit;
                if (matchBit == bit) continue;

                // The moment the literal diverges from the matched byte the rest is coded plainly
                while (context < 0x100) context = (context << 1) | DecodeBit(_literal, offset + context);

                break;
            }
            while (context < 0x100);

            return (byte)context;
        }

        private uint LiteralOffset(long position, byte previousByte) =>
            ((((uint)position & _literalPositionMask) << _literalContextBits)
             + (uint)(previousByte >> (8 - _literalContextBits)))
            * LzmaConstants.LiteralCoderSize;

        private static InvalidDataException CorruptDistance(uint distance, long produced) =>
            new($"An LZMA match refers {distance + 1} bytes back, but only {produced} bytes have been decoded.");

        /// <summary>
        /// The range decoder consumes the stream one byte at a time, so the compressed side is buffered here
        /// rather than at every call site. Without it a payload read straight out of a file window costs one
        /// seek and one read per compressed byte.
        /// </summary>
        private byte ReadByte()
        {
            if (_inputCursor == _inputFilled)
            {
                _inputFilled = _input.Read(_inputBuffer, offset: 0, _inputBuffer.Length);
                _inputCursor = 0;
                if (_inputFilled <= 0)
                    throw new EndOfStreamException("An LZMA stream ended before its declared length was decoded.");
            }

            return _inputBuffer[_inputCursor++];
        }
    }

    /// <summary>The 3 way length model: 3 low bits, 3 mid bits, or 8 high bits, selected by two choice bits.</summary>
    private sealed class LzmaLengthModel(int positionBits)
    {
        private readonly int _positionStateCount = 1 << positionBits;
        private readonly ushort[] _choice = LzmaConstants.NewProbabilities(LzmaConstants.LengthChoiceSize);
        private readonly ushort[] _low = LzmaConstants.NewProbabilities((1 << positionBits) << LzmaConstants.LengthLowBits);
        private readonly ushort[] _mid = LzmaConstants.NewProbabilities((1 << positionBits) << LzmaConstants.LengthMidBits);
        private readonly ushort[] _high = LzmaConstants.NewProbabilities(LzmaConstants.LengthHighSize);

        /// <summary>Returns the length symbol, which is the match length minus the minimum match length.</summary>
        public int Decode(DecoderState state, uint positionState)
        {
            if (positionState >= _positionStateCount)
                throw new InvalidDataException($"An LZMA length model was asked for position state {positionState}.");

            if (state.DecodeBit(_choice, index: 0) == 0)
                return (int)state.DecodeBitTree(_low, positionState << LzmaConstants.LengthLowBits, LzmaConstants.LengthLowBits);

            if (state.DecodeBit(_choice, index: 1) == 0)
            {
                return LzmaConstants.LengthLowSymbols
                       + (int)state.DecodeBitTree(_mid, positionState << LzmaConstants.LengthMidBits, LzmaConstants.LengthMidBits);
            }

            return LzmaConstants.LengthLowSymbols
                   + LzmaConstants.LengthMidSymbols
                   + (int)state.DecodeBitTree(_high, offset: 0, LzmaConstants.LengthHighBits);
        }
    }

    /// <summary>
    /// Circular output window. Decoded bytes are flushed to the sink whenever the buffer wraps, so only the
    /// dictionary stays resident no matter how large the payload is.
    /// </summary>
    private sealed class LzmaOutputWindow
    {
        private readonly Stream _output;
        private readonly byte[] _buffer;
        private int _position;
        private int _flushedPosition;

        public LzmaOutputWindow(Stream output, int windowSize)
        {
            _output = output;
            _buffer = new byte[windowSize];
        }

        public void PutByte(byte value)
        {
            _buffer[_position++] = value;
            if (_position >= _buffer.Length) Flush();
        }

        public byte GetByte(uint distance)
        {
            long index = _position - distance - 1;
            if (index < 0) index += _buffer.Length;
            return _buffer[index];
        }

        public void CopyBlock(uint distance, int length)
        {
            long source = _position - distance - 1;
            if (source < 0) source += _buffer.Length;
            for (int remaining = length; remaining > 0; remaining--)
            {
                if (source >= _buffer.Length) source = 0;
                _buffer[_position++] = _buffer[source++];
                if (_position >= _buffer.Length) Flush();
            }
        }

        public void Flush()
        {
            int pending = _position - _flushedPosition;
            if (pending > 0) _output.Write(_buffer, _flushedPosition, pending);
            if (_position >= _buffer.Length) _position = 0;

            _flushedPosition = _position;
        }
    }
}
