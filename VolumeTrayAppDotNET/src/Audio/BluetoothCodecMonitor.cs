using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Realtime listener for the Bluetooth A2DP codec the OS is currently streaming with.
/// <para/>
/// Microsoft's Bluetooth stack publishes the negotiated codec on every AVDTP SET_CONFIGURATION
/// (and RECONFIGURE) through the user-mode ETW provider
/// <c>Microsoft.Windows.Bluetooth.BthA2dp</c>. There is no public API surface for it; ETW is
/// the same channel the Win11 Settings app's "Audio codec: AAC" line ultimately reads from
/// (via WPA recipes), and the same one BluetoothAudioCodecInspector ships against. We open a
/// realtime session, decode the codec ids, and publish them through <see cref="CodecChanged"/>.
/// <para/>
/// Lifetime: AudioDeviceManager starts the session only while the flyout shows codec names and stops it when the
/// setting goes off. A realtime ETW session is a kernel object that outlives the process that created it, so
/// <see cref="Stop"/> and <see cref="Dispose"/> stop it explicitly, and <see cref="Start"/> first stops one a crashed
/// earlier run left behind under the same name.
/// <para/>
/// Threading: the TraceEvent worker thread fires the codec callback; we marshal the
/// <see cref="CurrentCodec"/> mutation (and the <see cref="CodecChanged"/> fanout) onto
/// the dispatcher captured at construction so consumers don't have to. Start, Stop and Dispose run on that thread.
/// Stop retires the session at once (a generation check drops its late events) and stops and joins its worker on
/// the thread pool, so the UI never waits for ETW; a Start during that retirement opens the new session once the old
/// one is gone, because both use the same kernel session name.
/// <para/>
/// Privilege: <c>StartTraceW</c> requires Administrator or membership in the local
/// "Performance Log Users" group. Without it <see cref="Start"/> logs once and stays inert; it never throws.
/// <para/>
/// Driver caveat (verbatim from the BluetoothAudioCodecInspector README): "this utility may
/// have delay depending on the Bluetooth chipset used; A2DP streaming event may not be presented
/// in realtime. Format information may appear during the audio playback session or at the end
/// of the audio playback session. This is a Windows Bluetooth Audio Stack limitation."
/// </summary>
internal sealed class BluetoothCodecMonitor(Dispatcher dispatcher) : IDisposable
{
    // Stable session name so a prior crashed run can be detected and stopped before we open
    // ours - TraceEvent's Create option throws ERROR_ALREADY_EXISTS otherwise.
    private const string SessionName = "VolumeTrayAppDotNET-BthA2dp";

    // Provider identity (verified on Win10 and Win11 25H2 by BluetoothAudioCodecInspector).
    // GUID and event name are public; not declared in any shipping header.
    private const string ProviderName = "Microsoft.Windows.Bluetooth.BthA2dp";

    private static readonly Guid ProviderGuid = new("8776ad1e-5022-4451-a566-f47e708b9075");

    // Legacy provider referenced in the Win8.1 BT-diagnostics KB and in older WPRP recording
    // profiles. Enabled alongside the modern provider so chipsets that emit codec info under the
    // legacy ID also land in the same callback. Cheap to enable; if absent the EnableProvider
    // call no-ops.
    private static readonly Guid LegacyProviderGuid = new("ddb6da39-08a7-4579-8d0c-68011146e205");
    private const string StreamingEventName = "A2dpStreaming";

    // Payload field positions for the A2dpStreaming event. Position is the safer contract
    // than PayloadByName because manifest field names aren't always populated when accessed
    // through trimmed assemblies; the BluetoothAudioCodecInspector reference uses positions
    // for the same reason.
    private const int FieldStandardCodecId = 3;
    private const int FieldVendorId = 4;
    private const int FieldVendorCodecId = 5;

    private TraceEventSession? _session;
    private Thread? _worker;
    private BluetoothCodec? _currentCodec;
    private bool _elevationMissingLogged;

    // Incremented by every Start and Stop; events of an earlier session carry an older value and are dropped
    private int _generation;

    // The retirement of the last stopped session, and whether a Start waits for it to finish
    private Task _retirement = Task.CompletedTask;
    private bool _startAfterRetirement;

    // One-shot diagnostic: log the full payload schema on the first event so a user reporting
    // an issue can hand us the field names / types their build emits without rebuilding the app.
    private bool _schemaLogged;
    private bool _disposed;

    /// <summary>
    /// Fires on the dispatcher whenever the cached codec changes (including the transition to
    /// null on <see cref="Reset"/> and <see cref="Stop"/>). Subscribers can update bindings directly without a
    /// Dispatcher hop of their own.
    /// </summary>
    public event Action<BluetoothCodec?>? CodecChanged;

    /// <summary>
    /// The most recently observed A2DP codec, or null when nothing has been seen yet (or after
    /// <see cref="Reset"/> / <see cref="Stop"/>). Mutated only on the dispatcher.
    /// </summary>
    public BluetoothCodec? CurrentCodec
    {
        get => _currentCodec;
        private set
        {
            if (Equals(_currentCodec, value)) return;
            _currentCodec = value;
            CodecChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Opens a realtime ETW session on the BthA2dp provider and spawns a background worker that
    /// pumps events through <see cref="TraceEventSession.Source"/>'s blocking
    /// <c>Process()</c> loop. Idempotent: calling it while the session runs is a no-op.
    /// </summary>
    public void Start()
    {
        if (_disposed || _session != null) return;

        if (!_retirement.IsCompleted)
        {
            // The previous session still holds the kernel session name; open the new one once it is gone
            if (_startAfterRetirement) return;

            _startAfterRetirement = true;
            _ = _retirement.ContinueWith(
                _ => dispatcher.Post(StartAfterRetirement),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return;
        }

        // IsElevated returns null on failure to determine; treat that as "not elevated" to be
        // safe rather than letting EnableProvider throw a less helpful native error.
        if (TraceEventSession.IsElevated() != true)
        {
            if (!_elevationMissingLogged)
                TADNLog.Log("BluetoothCodecMonitor.Start: requires admin / Performance Log Users; staying inert.");
            _elevationMissingLogged = true;
            return;
        }

        try
        {
            StopOrphanedSession();

            int generation = Interlocked.Increment(ref _generation);

            // StopOnDispose stops the kernel session when the TraceEventSession is disposed (Stop, Dispose).
            // A force-killed process skips that, which is what StopOrphanedSession cleans up on the next Start.
            _session = new TraceEventSession(SessionName) { StopOnDispose = true };

            _session.Source.Dynamic.AddCallbackForProviderEvent(
                ProviderName, StreamingEventName, evt => OnStreamingEvent(evt, generation));

            // matchAnyKeywords = 0 selects every event on the provider, matching the reference
            // implementation. The provider only emits a small handful of event types, so we
            // pay no real cost being permissive.
            _session.EnableProvider(ProviderGuid, TraceEventLevel.Verbose, matchAnyKeywords: 0);

            // Best-effort: enable the legacy diagnostic provider in parallel. Some older Windows
            // builds and some chipsets surface codec configuration here instead of the modern
            // provider. Wrapped in its own try because an unregistered provider can throw on
            // some TraceEvent versions.
            try { _session.EnableProvider(LegacyProviderGuid, TraceEventLevel.Verbose, matchAnyKeywords: 0); }
            catch (Exception ex) { TADNLog.Log($"BluetoothCodecMonitor.Start: legacy provider enable: {ex.Message}"); }

            TraceEventSession session = _session;
            _worker = new Thread(() => WorkerLoop(session)) { IsBackground = true, Name = "BluetoothCodecMonitor.ETW" };
            _worker.Start();

            TADNLog.Log("BluetoothCodecMonitor.Start: session opened on provider " + ProviderName);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BluetoothCodecMonitor.Start: failed: {ex.Message}");
            Safe.Dispose(_session);
            _session = null;
        }
    }

    /// <summary>
    /// Retires the ETW session and clears the cached codec at once; the session is stopped and its worker joined
    /// on the thread pool. <see cref="Start"/> may open it again.
    /// </summary>
    public void Stop()
    {
        _startAfterRetirement = false;
        TraceEventSession? session = _session;
        Thread? worker = _worker;
        _session = null;
        _worker = null;
        if (session == null) return;

        _ = Interlocked.Increment(ref _generation);
        Task previous = _retirement;
        _retirement = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            RetireSession(session, worker);
        });
        Reset();
    }

    /// <summary>Whether an event stamped with <paramref name="eventGeneration"/> belongs to the live session.</summary>
    internal static bool IsCurrentGeneration(int eventGeneration, int currentGeneration) =>
        eventGeneration == currentGeneration;

    private void StartAfterRetirement()
    {
        if (!_startAfterRetirement) return;

        _startAfterRetirement = false;
        Start();
    }

    private static void RetireSession(TraceEventSession session, Thread? worker)
    {
        // Disposing the session stops it, which makes Source.Process return on the worker
        Safe.Dispose(session);
        if (worker != null)
        {
            try
            {
                if (!worker.Join(TimeSpan.FromMilliseconds(TimeConstants.BluetoothCodecWorkerJoinTimeoutMs)))
                    TADNLog.Log("BluetoothCodecMonitor.Stop: the ETW worker did not stop in time.");
            }
            catch (Exception ex) { TADNLog.Log($"BluetoothCodecMonitor.Stop: join: {ex.Message}"); }
        }

        TADNLog.Log("BluetoothCodecMonitor.Stop: session closed.");
    }

    // Defensive cleanup of a stale session from a previous run. Realtime ETW sessions outlive a
    // crashed process (they live in the kernel until explicitly stopped), so opening one under the
    // same name immediately afterwards would fail with ALREADY_EXISTS.
    private static void StopOrphanedSession()
    {
        try
        {
            foreach (string name in TraceEventSession.GetActiveSessionNames())
            {
                if (!string.Equals(name, SessionName, StringComparison.Ordinal)) continue;
                try
                {
                    using TraceEventSession existing = new(name, TraceEventSessionOptions.Attach);
                    existing.Stop();
                    TADNLog.Log("BluetoothCodecMonitor: stopped orphaned ETW session " + name);
                }
                catch (Exception ex)
                {
                    TADNLog.Log($"BluetoothCodecMonitor: failed to stop orphan {name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BluetoothCodecMonitor: enumerate orphans failed: {ex.Message}");
        }
    }

    private static void WorkerLoop(TraceEventSession session)
    {
        try
        {
            // Blocks until the session is stopped. Exceptions from individual event callbacks
            // are swallowed inside OnStreamingEvent so a single bad event can't tear the pump
            // down.
            session.Source.Process();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BluetoothCodecMonitor.WorkerLoop: {ex.Message}");
        }
    }

    private void OnStreamingEvent(TraceEvent evt, int generation)
    {
        try
        {
            if (!IsCurrentGeneration(generation, Volatile.Read(ref _generation))) return;

            LogSchemaOnce(evt);

            byte standardId = Convert.ToByte(evt.PayloadValue(FieldStandardCodecId));
            int vendorId = Convert.ToInt32(evt.PayloadValue(FieldVendorId));
            int vendorCodecId = Convert.ToInt32(evt.PayloadValue(FieldVendorCodecId));

            BluetoothCodec codec = new(standardId, vendorId, vendorCodecId);
            TADNLog.Log($"BluetoothCodecMonitor: A2DP codec = {codec.FriendlyName} " +
                        $"(std=0x{standardId:X2} vendor=0x{vendorId:X4} codec=0x{vendorCodecId:X4})");

            // The session may be retired while this hop is queued; recheck on the dispatcher
            try
            {
                dispatcher.InvokeAsync(() =>
                {
                    if (IsCurrentGeneration(generation, _generation)) CurrentCodec = codec;
                });
            }
            catch (Exception ex) { TADNLog.Log($"BluetoothCodecMonitor: dispatch failed: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BluetoothCodecMonitor.OnStreamingEvent: {ex.Message}");
        }
    }

    // First-event diagnostic. Dumps every payload field name + value so a user reporting
    // "codec didn't show up" can paste a single log line that tells us whether the schema
    // shifted on their build. Debug-only: stripped in Release so the schema dump never
    // bloats production logs.
    [Conditional("DEBUG")]
    private void LogSchemaOnce(TraceEvent evt)
    {
        if (_schemaLogged) return;
        _schemaLogged = true;
        try
        {
            string[] names = evt.PayloadNames;
            StringBuilder sb = new("BluetoothCodecMonitor: A2dpStreaming schema -");
            for (int i = 0; i < names.Length; i++)
            {
                object? value;
                try { value = evt.PayloadValue(i); }
                catch { value = "<unreadable>"; }

                sb.Append(' ').Append('[').Append(i).Append(']').Append(names[i]).Append('=').Append(value);
            }

            TADNLog.LogDebug(sb.ToString());
        }
        catch
        {
            /* diagnostic only - never block the codec extraction on this */
        }
    }

    /// <summary>
    /// Clears the cached codec. Called by the manager when the last Bluetooth render endpoint
    /// goes inactive so UI bindings collapse the codec readout instead of holding a stale value
    /// from a prior session.
    /// </summary>
    public void Reset()
    {
        if (_disposed) return;
        try { dispatcher.InvokeAsync(() => CurrentCodec = null); }
        catch (Exception ex) { TADNLog.Log($"BluetoothCodecMonitor.Reset: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;

        // App exit: the kernel session must be stopped before the process ends, so this one waits (bounded)
        try
        {
            if (!_retirement.Wait(TimeSpan.FromMilliseconds(TimeConstants.BluetoothCodecWorkerJoinTimeoutMs * 2)))
                TADNLog.Log("BluetoothCodecMonitor.Dispose: the session did not retire in time.");
        }
        catch (Exception ex) { TADNLog.Log($"BluetoothCodecMonitor.Dispose: {ex.Message}"); }
    }
}
