namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Parses a RIFF / WAVE byte buffer once and exposes the fmt / data fields both feedback paths need:
/// per-app SoundPlayer playback (clone + PCM-scale the bytes) and per-endpoint WASAPI streaming
/// (raw bytes plus channels / sample rate / block alignment for IAudioClient.Initialize).
/// Strict on parse: rejects any data chunk before a valid fmt chunk and any fmt chunk that doesn't
/// fully populate channels, sample rate, block align, and bits per sample.
/// </summary>
internal sealed class WAVTemplate
{
    // Backing buffer. Held by reference so EndpointSoundPlayback can stream directly out of it;
    // AppVolumeFeedbackPlayer never mutates this -- it clones via CloneScaled before scaling.
    public byte[] Bytes { get; }

    // Byte offset of the first PCM sample inside Bytes.
    public int DataOffset { get; }

    // Length of the PCM data region in bytes (not frames).
    public int DataLength { get; }

    // Format fields from the fmt chunk.
    public int Channels { get; }
    public int SamplesPerSec { get; }
    public int BlockAlign { get; }
    public int BitsPerSample { get; }

    // Natural playback length of the PCM data, derived from frames / sample-rate at construction.
    // Stored rather than computed so hot-path callers sizing "ding still in flight" windows don't
    // repeat the divisions on every call. Zero when the format fields would make the math undefined.
    public int DurationMs { get; }

    // Volume threshold above which CloneScaled skips the per-sample scaling pass entirely -- the
    // multiply would round-trip the bytes unchanged and just burn CPU.
    private const float ScaleSkipThreshold = 0.999f;

    private WAVTemplate(byte[] bytes, int dataOffset, int dataLength,
        int channels, int samplesPerSec, int blockAlign, int bitsPerSample)
    {
        Bytes = bytes;
        DataOffset = dataOffset;
        DataLength = dataLength;
        Channels = channels;
        SamplesPerSec = samplesPerSec;
        BlockAlign = blockAlign;
        BitsPerSample = bitsPerSample;
        DurationMs = samplesPerSec > 0 && blockAlign > 0
            ? (int)((long)(dataLength / blockAlign) * 1000 / samplesPerSec)
            : 0;
    }

    /// <summary>
    /// Reads <paramref name="path"/> and parses it as a WAV. Returns null on file I / O failure or
    /// any malformed / unsupported header. Best-effort: callers fall back to silence.
    /// </summary>
    public static WAVTemplate? FromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            return FromBytes(bytes);
        }
        catch
        {
            // File disappeared, permission denied, etc.
            return null;
        }
    }

    /// <summary>
    /// Parses an already-loaded byte buffer. Returns null if the buffer isn't a well-formed RIFF /
    /// WAVE with a fmt chunk before its data chunk and all four format fields populated.
    /// </summary>
    public static WAVTemplate? FromBytes(byte[]? data)
    {
        if (data == null || data.Length < 12) return null;
        if (data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F') return null;
        if (data[8] != 'W' || data[9] != 'A' || data[10] != 'V' || data[11] != 'E') return null;

        int channels = 0;
        int samplesPerSec = 0;
        int blockAlign = 0;
        int bitsPerSample = 0;
        bool fmtSeen = false;

        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            int chunkSize = BitConverter.ToInt32(data, pos + 4);
            int chunkData = pos + 8;
            if (chunkSize < 0 || chunkData + chunkSize > data.Length) return null;

            if (data[pos] == 'f' && data[pos + 1] == 'm' && data[pos + 2] == 't' && data[pos + 3] == ' ')
            {
                if (chunkSize < 16) return null;
                channels = BitConverter.ToUInt16(data, chunkData + 2);
                samplesPerSec = (int)BitConverter.ToUInt32(data, chunkData + 4);
                blockAlign = BitConverter.ToUInt16(data, chunkData + 12);
                bitsPerSample = BitConverter.ToUInt16(data, chunkData + 14);
                fmtSeen = channels > 0 && samplesPerSec > 0 && blockAlign > 0 && bitsPerSample > 0;
            }
            else if (data[pos] == 'd' && data[pos + 1] == 'a' && data[pos + 2] == 't' && data[pos + 3] == 'a')
            {
                if (!fmtSeen) return null;
                return new WAVTemplate(data, chunkData, chunkSize,
                    channels, samplesPerSec, blockAlign, bitsPerSample);
            }

            pos = chunkData + chunkSize;
            // RIFF chunks are word-aligned: odd-sized chunks carry a trailing pad byte.
            if ((chunkSize & 1) != 0) pos++;
        }

        return null;
    }

    /// <summary>
    /// Returns a fresh byte buffer copy with PCM samples scaled by <paramref name="volume"/> (clamped
    /// 0..1). The header bytes are copied verbatim; only the data region is scaled. Volumes near 1.0
    /// short-circuit the per-sample loop. Unsupported bit depths leave the data region untouched.
    /// </summary>
    public byte[] CloneScaled(float volume)
    {
        byte[] scaled = (byte[])Bytes.Clone();
        float clamped = Math.Clamp(volume, 0f, 1f);
        if (clamped >= ScaleSkipThreshold) return scaled;

        int end = DataOffset + DataLength;
        switch (BitsPerSample)
        {
            case 16:
                for (int i = DataOffset; i + 1 < end; i += 2)
                {
                    short sample = (short)(scaled[i] | (scaled[i + 1] << 8));
                    int s = (int)(sample * clamped);
                    scaled[i] = (byte)(s & 0xFF);
                    scaled[i + 1] = (byte)((s >> 8) & 0xFF);
                }

                break;
            case 24:
                for (int i = DataOffset; i + 2 < end; i += 3)
                {
                    int sample = scaled[i] | (scaled[i + 1] << 8) | (scaled[i + 2] << 16);
                    // Sign-extend the 24-bit sample to 32 bits before scaling.
                    if ((sample & 0x800000) != 0) sample |= unchecked((int)0xFF000000);
                    int s = (int)(sample * clamped);
                    if (s > 0x7FFFFF) s = 0x7FFFFF;
                    else if (s < -0x800000) s = -0x800000;
                    scaled[i] = (byte)(s & 0xFF);
                    scaled[i + 1] = (byte)((s >> 8) & 0xFF);
                    scaled[i + 2] = (byte)((s >> 16) & 0xFF);
                }

                break;
            case 32:
                for (int i = DataOffset; i + 3 < end; i += 4)
                {
                    int sample = BitConverter.ToInt32(scaled, i);
                    int s = (int)(sample * clamped);
                    scaled[i] = (byte)(s & 0xFF);
                    scaled[i + 1] = (byte)((s >> 8) & 0xFF);
                    scaled[i + 2] = (byte)((s >> 16) & 0xFF);
                    scaled[i + 3] = (byte)((s >> 24) & 0xFF);
                }

                break;
        }

        return scaled;
    }
}
