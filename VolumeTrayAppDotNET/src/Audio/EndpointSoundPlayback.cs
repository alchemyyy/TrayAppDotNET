using System.Runtime.InteropServices;
using VolumeTrayAppDotNET.Interop;

using IAudioClient = VolumeTrayAppDotNET.Interop.IAudioClient;
using IAudioRenderClient = VolumeTrayAppDotNET.Interop.IAudioRenderClient;
using IMMDevice = VolumeTrayAppDotNET.Interop.IMMDevice;
using IMMDeviceEnumerator = VolumeTrayAppDotNET.Interop.IMMDeviceEnumerator;
using MMDeviceEnumeratorFactory = VolumeTrayAppDotNET.Interop.MMDeviceEnumeratorFactory;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Plays a short PCM wav through a specific render endpoint via WASAPI shared mode.
/// Used by the device-volume-change feedback so the ding comes out of the device whose slider
/// the user just moved instead of the system default. Capture endpoints are not addressable here -
/// the caller is responsible for skipping them.
/// Best-effort: any failure (endpoint just disconnected, format rejected, etc.) is swallowed.
/// </summary>
internal static class EndpointSoundPlayback
{
    // Engine buffer hint to IAudioClient.Initialize, in 100-ns ticks. Long enough to hold our short
    // feedback wav comfortably; the audio service may round to its own period internally.
    // Padding-poll slice during the drain loop. Tens-of-ms scale is fine - the wav is ~1 second
    // and the user can't perceive sub-frame timing on a confirmation ding.

    /// <summary>
    /// Fires the playback on a threadpool worker and returns immediately. We take the endpoint id
    /// as a string (not an IMMDevice RCW) so the worker can re-acquire the device on its own MTA
    /// thread - the AudioDevice's IMMDevice RCW is bound to the WPF UI-thread STA and QueryInterface
    /// fails if we marshal it across apartments. The worker owns every COM proxy it creates.
    /// Takes a parsed <see cref="WAVTemplate"/> so the caller (which already holds one) doesn't
    /// re-parse the RIFF chunks on every ding.
    /// </summary>
    public static void PlayAsync(string deviceId, WAVTemplate wav)
    {
        if (string.IsNullOrEmpty(deviceId) || wav == null) return;
        Task.Run(() => Play(deviceId, wav));
    }

    private static void Play(string deviceId, WAVTemplate wav)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioRenderClient? render = null;
        IntPtr formatPtr = IntPtr.Zero;
        try
        {
            byte[] wavBytes = wav.Bytes;
            int channels = wav.Channels;
            int samplesPerSec = wav.SamplesPerSec;
            int bitsPerSample = wav.BitsPerSample;
            int blockAlign = wav.BlockAlign;
            int dataOffset = wav.DataOffset;
            int dataLength = wav.DataLength;

            // Fresh enumerator + IMMDevice on this worker thread so the COM object lives in the same
            // apartment we'll be calling from. Re-acquiring is cheap (in-proc, microseconds).
            enumerator = MMDeviceEnumeratorFactory.Create();
            enumerator.GetDevice(deviceId, out device);
            if (device == null) return;

            int hr = device.Activate(typeof(IAudioClient).GUID, ClsCtx.ALL, IntPtr.Zero, out client);
            if (hr < 0 || client == null) return;

            // Synthesize a clean 18-byte WAVEFORMATEX matching the source PCM. Avoids feeding the
            // engine any extra fields that may sit after cbSize in the file's fmt chunk.
            byte[] format = new byte[18];
            BitConverter.GetBytes((ushort)1).CopyTo(format, 0); // WAVE_FORMAT_PCM
            BitConverter.GetBytes((ushort)channels).CopyTo(format, 2);
            BitConverter.GetBytes((uint)samplesPerSec).CopyTo(format, 4);
            BitConverter.GetBytes((uint)(samplesPerSec * blockAlign)).CopyTo(format, 8);
            BitConverter.GetBytes((ushort)blockAlign).CopyTo(format, 12);
            BitConverter.GetBytes((ushort)bitsPerSample).CopyTo(format, 14);
            BitConverter.GetBytes((ushort)0).CopyTo(format, 16); // cbSize

            formatPtr = Marshal.AllocHGlobal(format.Length);
            Marshal.Copy(format, 0, formatPtr, format.Length);

            uint streamFlags = AudioClientStreamFlags.NoPersist
                               | AudioClientStreamFlags.AutoConvertPcm
                               | AudioClientStreamFlags.SrcDefaultQuality;
            hr = client.Initialize(AudioClientShareMode.Shared, streamFlags,
                TimeConstants.EndpointSoundPlaybackBufferDurationHns, 0, formatPtr, IntPtr.Zero);
            if (hr < 0)
            {
                TADNLog.Log($"EndpointSoundPlayback.Initialize: hr=0x{hr:X8}");
                return;
            }

            hr = client.GetBufferSize(out uint bufferFrames);
            if (hr < 0 || bufferFrames == 0) return;

            hr = client.GetService(typeof(IAudioRenderClient).GUID, out render);
            if (hr < 0 || render == null) return;

            int byteCursor = 0;
            int totalBytes = dataLength;

            // Initial fill before Start so the engine never plays a glitch of silence.
            byteCursor += FillBuffer(render, bufferFrames, wavBytes, dataOffset + byteCursor,
                totalBytes - byteCursor, blockAlign);

            hr = client.Start();
            if (hr < 0) return;

            int waited = 0;
            while (waited < TimeConstants.EndpointSoundPlaybackMaxDrainMs)
            {
                Thread.Sleep(TimeConstants.EndpointSoundPlaybackPollSliceMs);
                waited += TimeConstants.EndpointSoundPlaybackPollSliceMs;

                if (client.GetCurrentPadding(out uint padding) < 0) break;
                if (byteCursor >= totalBytes && padding == 0) break;
                if (byteCursor < totalBytes)
                {
                    uint freeFrames = bufferFrames > padding ? bufferFrames - padding : 0;
                    if (freeFrames > 0)
                    {
                        byteCursor += FillBuffer(render, freeFrames, wavBytes, dataOffset + byteCursor,
                            totalBytes - byteCursor, blockAlign);
                    }
                }
            }

            try { client.Stop(); }
            catch
            {
                /* device may have torn down */
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"EndpointSoundPlayback.Play: {ex.Message}");
        }
        finally
        {
            if (formatPtr != IntPtr.Zero) Marshal.FreeHGlobal(formatPtr);
            Safe.Release(render);
            Safe.Release(client);
            Safe.Release(device);
            Safe.Release(enumerator);
        }
    }

    // Writes up to (freeFrames, dataLeft / blockAlign) frames into the render ring. Returns the
    // number of source bytes consumed - 0 when the engine refused the buffer or no source remains.
    private static int FillBuffer(IAudioRenderClient render, uint freeFrames, byte[] source,
        int sourceOffset, int sourceBytesLeft, int blockAlign)
    {
        if (freeFrames == 0 || sourceBytesLeft < blockAlign) return 0;

        int dataFrames = sourceBytesLeft / blockAlign;
        int framesToWrite = (int)Math.Min(freeFrames, (uint)dataFrames);
        if (framesToWrite <= 0) return 0;

        int hr = render.GetBuffer((uint)framesToWrite, out IntPtr buffer);
        if (hr < 0 || buffer == IntPtr.Zero) return 0;

        int bytesToWrite = framesToWrite * blockAlign;
        Marshal.Copy(source, sourceOffset, buffer, bytesToWrite);
        render.ReleaseBuffer((uint)framesToWrite, 0);
        return bytesToWrite;
    }
}
