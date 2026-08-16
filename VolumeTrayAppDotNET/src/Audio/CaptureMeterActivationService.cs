using System.Runtime.InteropServices;
using VolumeTrayAppDotNET.Interop;

using IAudioCaptureClient = VolumeTrayAppDotNET.Interop.IAudioCaptureClient;
using IAudioClient = VolumeTrayAppDotNET.Interop.IAudioClient;
using IMMDevice = VolumeTrayAppDotNET.Interop.IMMDevice;
using IMMDeviceEnumerator = VolumeTrayAppDotNET.Interop.IMMDeviceEnumerator;
using MMDeviceEnumeratorFactory = VolumeTrayAppDotNET.Interop.MMDeviceEnumeratorFactory;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Keeps shared capture streams running for selected recording endpoints so drivers without a
/// hardware peak meter feed data into the Windows audio engine. Every COM object and capture-buffer
/// operation stays on one dedicated MTA thread.
/// </summary>
internal sealed class CaptureMeterActivationService : IDisposable
{
    private const uint MultithreadedApartment = 0;
    private const int MaxPacketsPerDrain = 64;
    private const string ThreadName = "VolumeTrayApp.CaptureMeterActivation";

    private readonly Lock _gate = new();
    private readonly AutoResetEvent _wakeEvent = new(false);
    private readonly HashSet<string> _requestedDeviceIDs = new(StringComparer.Ordinal);
    private readonly Thread _thread;
    private long _requestVersion;
    private bool _disposed;

    public CaptureMeterActivationService()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = ThreadName
        };
        _thread.Start();
    }

    /// <summary>Replaces the set of active capture endpoints owned by the worker.</summary>
    public void SetActiveDeviceIDs(IEnumerable<string> deviceIDs)
    {
        HashSet<string> requestedDeviceIDs = new(StringComparer.Ordinal);
        foreach (string deviceID in deviceIDs)
        {
            if (!string.IsNullOrEmpty(deviceID)) requestedDeviceIDs.Add(deviceID);
        }

        lock (_gate)
        {
            if (_disposed || _requestedDeviceIDs.SetEquals(requestedDeviceIDs)) return;
            _requestedDeviceIDs.Clear();
            _requestedDeviceIDs.UnionWith(requestedDeviceIDs);
            _requestVersion++;
            _wakeEvent.Set();
        }
    }

    /// <summary>Stops every capture stream without stopping the reusable worker thread.</summary>
    public void ClearActiveDeviceIDs() => SetActiveDeviceIDs(Array.Empty<string>());

    private void Run()
    {
        int initializationResult = CoInitializeEx(IntPtr.Zero, MultithreadedApartment);
        if (initializationResult < 0)
        {
            TADNLog.Log(
                $"CaptureMeterActivationService: CoInitializeEx failed hr=0x{initializationResult:X8}");
            return;
        }

        Dictionary<string, CaptureStream> activeStreams = new(StringComparer.Ordinal);
        Dictionary<string, long> retryAfterMilliseconds = new(StringComparer.Ordinal);
        Dictionary<string, string> lastFailures = new(StringComparer.Ordinal);
        HashSet<string> requestedDeviceIDs = new(StringComparer.Ordinal);
        List<string> removedDeviceIDs = [];
        List<string> abandonedFailureDeviceIDs = [];
        List<(string DeviceID, string Failure)> failedStreams = [];
        long observedRequestVersion = -1;

        try
        {
            while (true)
            {
                RefreshRequestedDeviceIDs(
                    requestedDeviceIDs,
                    ref observedRequestVersion,
                    out bool shouldStop);
                if (shouldStop) break;

                ReconcileStreams(
                    requestedDeviceIDs,
                    activeStreams,
                    retryAfterMilliseconds,
                    lastFailures,
                    removedDeviceIDs,
                    abandonedFailureDeviceIDs);
                DrainStreams(
                    activeStreams,
                    retryAfterMilliseconds,
                    lastFailures,
                    failedStreams);

                int waitMilliseconds = requestedDeviceIDs.Count == 0
                    ? Timeout.Infinite
                    : TimeConstants.CaptureMeterActivationDrainIntervalMs;
                _wakeEvent.WaitOne(waitMilliseconds);
            }
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"CaptureMeterActivationService worker failed: {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            foreach (CaptureStream stream in activeStreams.Values) Safe.Dispose(stream);
            CoUninitialize();
        }
    }

    private void RefreshRequestedDeviceIDs(
        HashSet<string> requestedDeviceIDs,
        ref long observedRequestVersion,
        out bool shouldStop)
    {
        lock (_gate)
        {
            shouldStop = _disposed;
            if (shouldStop || observedRequestVersion == _requestVersion) return;

            requestedDeviceIDs.Clear();
            requestedDeviceIDs.UnionWith(_requestedDeviceIDs);
            observedRequestVersion = _requestVersion;
        }
    }

    private static void ReconcileStreams(
        HashSet<string> requestedDeviceIDs,
        Dictionary<string, CaptureStream> activeStreams,
        Dictionary<string, long> retryAfterMilliseconds,
        Dictionary<string, string> lastFailures,
        List<string> removedDeviceIDs,
        List<string> abandonedFailureDeviceIDs)
    {
        removedDeviceIDs.Clear();
        foreach (string activeDeviceID in activeStreams.Keys)
        {
            if (!requestedDeviceIDs.Contains(activeDeviceID)) removedDeviceIDs.Add(activeDeviceID);
        }

        foreach (string removedDeviceID in removedDeviceIDs)
        {
            Safe.Dispose(activeStreams[removedDeviceID]);
            activeStreams.Remove(removedDeviceID);
            retryAfterMilliseconds.Remove(removedDeviceID);
            lastFailures.Remove(removedDeviceID);
        }

        long currentMilliseconds = Environment.TickCount64;
        foreach (string requestedDeviceID in requestedDeviceIDs)
        {
            if (activeStreams.ContainsKey(requestedDeviceID)) continue;
            if (retryAfterMilliseconds.TryGetValue(requestedDeviceID, out long retryAtMilliseconds)
                && currentMilliseconds < retryAtMilliseconds)
            {
                continue;
            }

            if (CaptureStream.TryCreate(requestedDeviceID, out CaptureStream? stream, out string failure)
                && stream != null)
            {
                activeStreams.Add(requestedDeviceID, stream);
                retryAfterMilliseconds.Remove(requestedDeviceID);
                lastFailures.Remove(requestedDeviceID);
                TADNLog.LogDebug(
                    $"CaptureMeterActivationService: activated recording endpoint '{requestedDeviceID}'");
                continue;
            }

            retryAfterMilliseconds[requestedDeviceID] =
                currentMilliseconds + TimeConstants.CaptureMeterActivationRetryIntervalMs;
            LogFailureIfChanged(requestedDeviceID, failure, lastFailures);
        }

        abandonedFailureDeviceIDs.Clear();
        foreach (string failedDeviceID in lastFailures.Keys)
        {
            if (!requestedDeviceIDs.Contains(failedDeviceID)) abandonedFailureDeviceIDs.Add(failedDeviceID);
        }

        foreach (string abandonedFailureDeviceID in abandonedFailureDeviceIDs)
        {
            retryAfterMilliseconds.Remove(abandonedFailureDeviceID);
            lastFailures.Remove(abandonedFailureDeviceID);
        }
    }

    private static void DrainStreams(
        Dictionary<string, CaptureStream> activeStreams,
        Dictionary<string, long> retryAfterMilliseconds,
        Dictionary<string, string> lastFailures,
        List<(string DeviceID, string Failure)> failedStreams)
    {
        failedStreams.Clear();
        foreach (KeyValuePair<string, CaptureStream> pair in activeStreams)
        {
            int drainResult = pair.Value.DrainAvailablePackets();
            if (drainResult < 0)
                failedStreams.Add((pair.Key, $"drain hr=0x{drainResult:X8}"));
        }

        foreach ((string deviceID, string failure) in failedStreams)
        {
            Safe.Dispose(activeStreams[deviceID]);
            activeStreams.Remove(deviceID);
            retryAfterMilliseconds[deviceID] =
                Environment.TickCount64 + TimeConstants.CaptureMeterActivationRetryIntervalMs;
            LogFailureIfChanged(deviceID, failure, lastFailures);
        }
    }

    private static void LogFailureIfChanged(
        string deviceID,
        string failure,
        Dictionary<string, string> lastFailures)
    {
        if (lastFailures.TryGetValue(deviceID, out string? previousFailure)
            && string.Equals(previousFailure, failure, StringComparison.Ordinal))
        {
            return;
        }

        lastFailures[deviceID] = failure;
        TADNLog.LogDebug(
            $"CaptureMeterActivationService: recording endpoint '{deviceID}' unavailable: {failure}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _requestedDeviceIDs.Clear();
            _wakeEvent.Set();
        }
        if (ReferenceEquals(Thread.CurrentThread, _thread)) return;

        bool joined = _thread.Join(TimeConstants.CaptureMeterActivationWorkerJoinTimeoutMs);
        if (joined)
        {
            _wakeEvent.Dispose();
            return;
        }

        TADNLog.Log("CaptureMeterActivationService: worker did not stop before shutdown timeout");
    }

    [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint initializationMode);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    private sealed class CaptureStream : IDisposable
    {
        private readonly IAudioClient _client;
        private readonly IAudioCaptureClient _captureClient;
        private bool _disposed;

        private CaptureStream(IAudioClient client, IAudioCaptureClient captureClient)
        {
            _client = client;
            _captureClient = captureClient;
        }

        public static bool TryCreate(
            string deviceID,
            out CaptureStream? stream,
            out string failure)
        {
            stream = null;
            failure = string.Empty;
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            IAudioClient? client = null;
            IAudioCaptureClient? captureClient = null;
            IntPtr mixFormat = IntPtr.Zero;
            bool started = false;

            try
            {
                enumerator = MMDeviceEnumeratorFactory.Create();
                enumerator.GetDevice(deviceID, out device);

                int activationResult = device.Activate(
                    typeof(IAudioClient).GUID,
                    ClsCtx.ALL,
                    IntPtr.Zero,
                    out client);
                if (activationResult < 0 || client == null)
                {
                    failure = $"IAudioClient activation hr=0x{activationResult:X8}";
                    return false;
                }

                int mixFormatResult = client.GetMixFormat(out mixFormat);
                if (mixFormatResult < 0 || mixFormat == IntPtr.Zero)
                {
                    failure = $"GetMixFormat hr=0x{mixFormatResult:X8}";
                    return false;
                }

                int initializeResult = client.Initialize(
                    AudioClientShareMode.Shared,
                    AudioClientStreamFlags.NoPersist,
                    0,
                    0,
                    mixFormat,
                    IntPtr.Zero);
                if (initializeResult < 0)
                {
                    failure = $"Initialize hr=0x{initializeResult:X8}";
                    return false;
                }

                int serviceResult = client.GetService(
                    typeof(IAudioCaptureClient).GUID,
                    out captureClient);
                if (serviceResult < 0 || captureClient == null)
                {
                    failure = $"IAudioCaptureClient service hr=0x{serviceResult:X8}";
                    return false;
                }

                int startResult = client.Start();
                if (startResult < 0)
                {
                    failure = $"Start hr=0x{startResult:X8}";
                    return false;
                }

                started = true;
                stream = new CaptureStream(client, captureClient);
                client = null;
                captureClient = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = $"{exception.GetType().Name} hr=0x{exception.HResult:X8}: {exception.Message}";
                return false;
            }
            finally
            {
                if (mixFormat != IntPtr.Zero) Marshal.FreeCoTaskMem(mixFormat);
                if (started && client != null)
                {
                    try { client.Stop(); }
                    catch
                    {
                        // The endpoint may have disappeared during activation
                    }
                }

                Safe.Release(captureClient);
                Safe.Release(client);
                Safe.Release(device);
                Safe.Release(enumerator);
            }
        }

        public int DrainAvailablePackets()
        {
            if (_disposed) return 0;

            for (int packetIndex = 0; packetIndex < MaxPacketsPerDrain; packetIndex++)
            {
                int packetSizeResult = _captureClient.GetNextPacketSize(out uint packetFrames);
                if (packetSizeResult < 0) return packetSizeResult;
                if (packetFrames == 0) return 0;

                int bufferResult = _captureClient.GetBuffer(
                    out IntPtr _,
                    out uint framesToRead,
                    out uint _,
                    out ulong _,
                    out ulong _);
                if (bufferResult < 0) return bufferResult;
                if (framesToRead == 0) return 0;

                int releaseResult = _captureClient.ReleaseBuffer(framesToRead);
                if (releaseResult < 0) return releaseResult;
            }

            return 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _client.Stop(); }
            catch
            {
                // The endpoint may already have been invalidated or removed
            }

            Safe.Release(_captureClient);
            Safe.Release(_client);
        }
    }
}
