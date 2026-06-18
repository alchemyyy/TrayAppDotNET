using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
/// realtime session, decode the codec ids, and publish them as an observable property.
/// <para/>
/// Threading: the TraceEvent worker thread fires the codec callback; we marshal the
/// <see cref="CurrentCodec"/> mutation (and the PropertyChanged / CodecChanged fanout) onto
/// the dispatcher captured at construction so consumers don't have to.
/// <para/>
/// Privilege: <c>StartTraceW</c> requires Administrator or membership in the local
/// "Performance Log Users" group. If the elevation check fails, <see cref="Start"/> sets
/// <see cref="RequiresElevation"/> and stays inert - it never throws. The UI can communicate
/// "codec unavailable - needs admin" off that flag without burning a try/catch at the call site.
/// <para/>
/// Driver caveat (verbatim from the BluetoothAudioCodecInspector README): "this utility may
/// have delay depending on the Bluetooth chipset used; A2DP streaming event may not be presented
/// in realtime. Format information may appear during the audio playback session or at the end
/// of the audio playback session. This is a Windows Bluetooth Audio Stack limitation."
/// </summary>
internal sealed class BluetoothCodecMonitor(Dispatcher dispatcher) : INotifyPropertyChanged, IDisposable
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
    private bool _isRunning;

    // One-shot diagnostic: log the full payload schema on the first event so a user reporting
    // an issue can hand us the field names / types their build emits without rebuilding the app.
    private bool _schemaLogged;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Fires on the dispatcher whenever the cached codec changes (including the transition to
    /// null on <see cref="Reset"/>). Subscribers can update bindings directly without a Dispatcher
    /// hop of their own.
    /// </summary>
    public event Action<BluetoothCodec?>? CodecChanged;

    /// <summary>
    /// The most recently observed A2DP codec, or null when nothing has been seen yet (or after
    /// <see cref="Reset"/>). Mutated only on the dispatcher. Equality short-circuited so WPF
    /// bindings don't see spurious PropertyChanged storms during ETW bursts; the unconditional
    /// re-fanout that newly-promoted endpoints rely on goes through <see cref="RaiseCodecChanged"/>.
    /// </summary>
    public BluetoothCodec? CurrentCodec
    {
        get => _currentCodec;
        private set
        {
            if (Equals(_currentCodec, value)) return;
            _currentCodec = value;
            OnPropertyChanged();
            CodecChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Re-fires <see cref="CodecChanged"/> with the current codec without mutating
    /// <see cref="CurrentCodec"/>. Used to re-fan the cached codec into a newly-promoted or
    /// freshly-Active BT endpoint that may never see another A2DP renegotiation.
    /// </summary>
    public void RaiseCodecChanged()
    {
        if (_disposed) return;
        try { dispatcher.InvokeAsync(() => CodecChanged?.Invoke(_currentCodec)); }
        catch (Exception ex) { TADNLog.Log($"BluetoothCodecMonitor.RaiseCodecChanged: {ex.Message}"); }
    }

    /// <summary>True once the ETW session is open and the worker is pumping events.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// True when <see cref="Start"/> was called by a non-elevated process. The session never
    /// opened and CurrentCodec will stay null; the UI binds against this to surface the
    /// "needs admin" hint without throwing.
    /// </summary>
    public bool RequiresElevation
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Opens a realtime ETW session on the BthA2dp provider and spawns a background worker that
    /// pumps events through <see cref="TraceEventSession.Source"/>'s blocking
    /// <c>Process()</c> loop. Idempotent: calling twice is a no-op.
    /// </summary>
    public void Start()
    {
        if (_disposed || _isRunning) return;

        // IsElevated returns null on failure to determine; treat that as "not elevated" to be
        // safe rather than letting EnableProvider throw a less helpful native error.
        if (TraceEventSession.IsElevated() != true)
        {
            RequiresElevation = true;
            TADNLog.Log("BluetoothCodecMonitor.Start: requires admin / Performance Log Users; staying inert.");
            return;
        }

        try
        {
            StopOrphanedSession();

            _session = new TraceEventSession(SessionName)
            {
                // Belt-and-braces: even if Dispose is missed (e.g. force-kill via taskkill on
                // build prep), the session goes away with the process by way of the ETW
                // session-private flag we'd get from Create. StopOnDispose enforces explicit
                // cleanup on graceful exit.
                StopOnDispose = true,
            };

            _session.Source.Dynamic.AddCallbackForProviderEvent(
                ProviderName, StreamingEventName, OnStreamingEvent);

            // matchAnyKeywords = 0 selects every event on the provider, matching the reference
            // implementation. The provider only emits a small handful of event types, so we
            // pay no real cost being permissive.
            _session.EnableProvider(ProviderGuid, TraceEventLevel.Verbose, 0);

            // Best-effort: enable the legacy diagnostic provider in parallel. Some older Windows
            // builds and some chipsets surface codec configuration here instead of the modern
            // provider. Wrapped in its own try because an unregistered provider can throw on
            // some TraceEvent versions.
            try { _session.EnableProvider(LegacyProviderGuid, TraceEventLevel.Verbose, 0); }
            catch (Exception ex) { TADNLog.Log($"BluetoothCodecMonitor.Start: legacy provider enable: {ex.Message}"); }

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "BluetoothCodecMonitor.ETW", };
            _worker.Start();

            IsRunning = true;
            TADNLog.Log("BluetoothCodecMonitor.Start: session opened on provider " + ProviderName);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BluetoothCodecMonitor.Start: failed: {ex.Message}");
            Safe.Dispose(_session);
            _session = null;
        }
    }

    // Defensive cleanup of a stale session from a previous run. TraceEventSession sessions
    // outlive a crashed process by design (they live in the kernel until explicitly stopped),
    // so opening Create on the same name immediately afterwards fails with ALREADY_EXISTS.
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

    private void WorkerLoop()
    {
        try
        {
            // Blocks until the session is disposed. Exceptions from individual event callbacks
            // are swallowed inside OnStreamingEvent so a single bad event can't tear the pump
            // down.
            _session?.Source.Process();
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BluetoothCodecMonitor.WorkerLoop: {ex.Message}");
        }
    }

    private void OnStreamingEvent(TraceEvent evt)
    {
        try
        {
            LogSchemaOnce(evt);

            byte standardId = Convert.ToByte(evt.PayloadValue(FieldStandardCodecId));
            int vendorId = Convert.ToInt32(evt.PayloadValue(FieldVendorId));
            int vendorCodecId = Convert.ToInt32(evt.PayloadValue(FieldVendorCodecId));

            BluetoothCodec codec = new(standardId, vendorId, vendorCodecId);
            TADNLog.Log($"BluetoothCodecMonitor: A2DP codec = {codec.FriendlyName} " +
                        $"(std=0x{standardId:X2} vendor=0x{vendorId:X4} codec=0x{vendorCodecId:X4})");

            try { dispatcher.InvokeAsync(() => CurrentCodec = codec); }
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
                object? value = null;
                try { value = evt.PayloadValue(i); }
                catch { value = "<unreadable>"; }

                sb.Append(' ').Append('[').Append(i).Append(']').Append(names[i] ?? "?").Append('=').Append(value);
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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsRunning = false;

        // Disposing the session causes Source.Process to return on the worker. Capture the
        // worker reference before the field is nulled so a racing setter can't swap a new
        // thread in mid-shutdown.
        Thread? worker = _worker;
        Safe.Dispose(_session);
        _session = null;
        _worker = null;

        if (worker != null)
        {
            try { worker.Join(TimeSpan.FromMilliseconds(TimeConstants.BluetoothCodecWorkerJoinTimeoutMs)); }
            catch (Exception ex) { TADNLog.Log($"BluetoothCodecMonitor.Dispose: join: {ex.Message}"); }
        }
    }
}
