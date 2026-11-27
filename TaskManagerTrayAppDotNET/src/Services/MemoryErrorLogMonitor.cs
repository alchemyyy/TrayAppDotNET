using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// Reads the memory errors Windows records in the System event log and watches it for new ones.
/// </summary>
/// <remarks>
/// WHEA-Logger records corrected memory errors as events 23, 47 and 49 and uncorrected (fatal) ones as 22, 46 and 48;
/// a fatal error is logged after the restart it caused. Windows Memory Diagnostic records a failed test as event 1102.
/// Interactive users can read the System log, so no administrator rights are needed. A subscription signals new
/// events, so nothing polls; every change rereads the matching events, newest first.
/// </remarks>
internal sealed class MemoryErrorLogMonitor : IDisposable
{
    private const string Channel = "System";
    private const string WHEAProvider = "Microsoft-Windows-WHEA-Logger";
    private const string MemoryDiagnosticsProvider = "Microsoft-Windows-MemoryDiagnostics-Results";

    private const string Query =
        "*[System[(Provider[@Name='" + WHEAProvider + "'] and (EventID=22 or EventID=23 or EventID=46 or "
        + "EventID=47 or EventID=48 or EventID=49)) or (Provider[@Name='" + MemoryDiagnosticsProvider
        + "'] and EventID=1102)]]";

    private const int MaximumEntries = 500;
    private const int EventBatchSize = 16;

    private readonly Lock _gate = new();
    private readonly AutoResetEvent _subscriptionSignal = new(initialState: false);
    private RegisteredWaitHandle? _subscriptionWait;
    private IntPtr _subscription;
    private int _refreshQueued;
    private bool _started;
    private bool _disposed;

    /// <summary>Raised on the UI thread after <see cref="Entries"/> or <see cref="Error"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Memory error events, newest first. Read on the UI thread.</summary>
    public IReadOnlyList<MemoryErrorEntry> Entries { get; private set; } = [];

    /// <summary>Why the log could not be read, or null after a successful read.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets whether the first read has finished.</summary>
    public bool HasRead { get; private set; }

    /// <summary>Reads the log once and subscribes to new memory error events.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
            _subscription = EvtSubscribe(
                session: IntPtr.Zero,
                _subscriptionSignal.SafeWaitHandle.DangerousGetHandle(),
                Channel,
                Query,
                bookmark: IntPtr.Zero,
                context: IntPtr.Zero,
                callback: IntPtr.Zero,
                EvtSubscribeToFutureEvents);
            if (_subscription == IntPtr.Zero)
                TADNLog.Log($"MemoryErrorLogMonitor: EvtSubscribe failed ({Marshal.GetLastPInvokeError()}).");
            else
            {
                _subscriptionWait = ThreadPool.RegisterWaitForSingleObject(
                    _subscriptionSignal,
                    static (state, _) => ((MemoryErrorLogMonitor)state!).OnSubscriptionSignaled(),
                    this,
                    Timeout.Infinite,
                    executeOnlyOnce: false);
            }
        }

        QueueRefresh();
    }

    private void OnSubscriptionSignaled()
    {
        // The subscription keeps signaling until its pending events are consumed
        lock (_gate)
        {
            if (_disposed || _subscription == IntPtr.Zero) return;
            DrainSubscription(_subscription);
        }

        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref _refreshQueued, value: 1) != 0) return;

        _ = Task.Run(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, value: 0);
            MemoryErrorReadResult result = ReadEntries();
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                Entries = result.Entries;
                Error = result.Error;
                HasRead = true;
                Changed?.Invoke();
            });
        });
    }

    private static void DrainSubscription(IntPtr subscription)
    {
        IntPtr[] events = new IntPtr[EventBatchSize];
        while (EvtNext(subscription, (uint)events.Length, events, timeout: 0, flags: 0, out uint returned))
        {
            for (int eventIndex = 0; eventIndex < returned; eventIndex++)
                EvtClose(events[eventIndex]);
        }
    }

    /// <summary>Reads every matching event, newest first, up to <see cref="MaximumEntries"/>.</summary>
    internal static MemoryErrorReadResult ReadEntries()
    {
        IntPtr query = EvtQuery(
            session: IntPtr.Zero,
            Channel,
            Query,
            EvtQueryChannelPath | EvtQueryReverseDirection);
        if (query == IntPtr.Zero)
        {
            int error = Marshal.GetLastPInvokeError();
            return new MemoryErrorReadResult([], $"EvtQuery failed ({error}).");
        }

        IntPtr renderContext = EvtCreateRenderContext(valuePathsCount: 0, valuePaths: IntPtr.Zero, EvtRenderContextSystem);
        Dictionary<string, IntPtr> publishers = new(StringComparer.OrdinalIgnoreCase);
        List<MemoryErrorEntry> entries = [];
        try
        {
            IntPtr[] events = new IntPtr[EventBatchSize];
            while (entries.Count < MaximumEntries
                   && EvtNext(query, (uint)events.Length, events, timeout: uint.MaxValue, flags: 0, out uint returned))
            {
                for (int eventIndex = 0; eventIndex < returned; eventIndex++)
                {
                    try
                    {
                        if (entries.Count < MaximumEntries
                            && TryReadEntry(events[eventIndex], renderContext, publishers, out MemoryErrorEntry? entry))
                            entries.Add(entry);
                    }
                    finally
                    {
                        EvtClose(events[eventIndex]);
                    }
                }
            }

            return new MemoryErrorReadResult(entries, Error: null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"MemoryErrorLogMonitor: {exception}");
            return new MemoryErrorReadResult(entries, exception.Message);
        }
        finally
        {
            foreach (IntPtr publisher in publishers.Values)
            {
                if (publisher != IntPtr.Zero) EvtClose(publisher);
            }

            if (renderContext != IntPtr.Zero) EvtClose(renderContext);
            EvtClose(query);
        }
    }

    private static unsafe bool TryReadEntry(
        IntPtr eventHandle,
        IntPtr renderContext,
        Dictionary<string, IntPtr> publishers,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MemoryErrorEntry? entry)
    {
        entry = null;
        if (renderContext == IntPtr.Zero) return false;

        _ = EvtRender(renderContext, eventHandle, EvtRenderEventValues, bufferSize: 0, buffer: null, out uint bufferUsed, out _);
        if (bufferUsed == 0) return false;

        byte[] buffer = new byte[bufferUsed];
        fixed (byte* values = buffer)
        {
            if (!EvtRender(renderContext, eventHandle, EvtRenderEventValues, bufferUsed, values, out _, out uint count)
                || count <= SystemEventRecordID)
                return false;

            EVT_VARIANT* variants = (EVT_VARIANT*)values;
            string provider = variants[SystemProviderName].Type == EvtVarTypeString
                ? new string((char*)variants[SystemProviderName].Value)
                : string.Empty;
            int eventID = variants[SystemEventID].Type == EvtVarTypeUInt16
                ? (ushort)variants[SystemEventID].Value
                : -1;
            if (variants[SystemTimeCreated].Type != EvtVarTypeFileTime
                || variants[SystemEventRecordID].Type != EvtVarTypeUInt64)
                return false;

            long fileTime = (long)variants[SystemTimeCreated].Value;
            long recordID = (long)variants[SystemEventRecordID].Value;
            MemoryErrorKind? kind = Classify(provider, eventID);
            if (kind == null) return false;

            entry = new MemoryErrorEntry(
                recordID,
                fileTime,
                eventID,
                kind.Value,
                FormatMessage(eventHandle, provider, publishers) ?? string.Empty);
            return true;
        }
    }

    /// <summary>Maps a recorded event to its memory error kind, or null when it is not a memory error.</summary>
    internal static MemoryErrorKind? Classify(string provider, int eventID)
    {
        if (string.Equals(provider, WHEAProvider, StringComparison.OrdinalIgnoreCase))
        {
            return eventID switch
            {
                23 or 47 or 49 => MemoryErrorKind.Corrected,
                22 or 46 or 48 => MemoryErrorKind.Uncorrected,
                _ => null
            };
        }

        return string.Equals(provider, MemoryDiagnosticsProvider, StringComparison.OrdinalIgnoreCase)
               && eventID == 1102
            ? MemoryErrorKind.DiagnosticFailure
            : null;
    }

    private static unsafe string? FormatMessage(
        IntPtr eventHandle,
        string provider,
        Dictionary<string, IntPtr> publishers)
    {
        if (!publishers.TryGetValue(provider, out IntPtr publisher))
        {
            publisher = EvtOpenPublisherMetadata(IntPtr.Zero, provider, logFilePath: null, locale: 0, flags: 0);
            publishers[provider] = publisher;
        }

        if (publisher == IntPtr.Zero) return null;

        _ = EvtFormatMessage(publisher, eventHandle, 0, 0, IntPtr.Zero, EvtFormatMessageEvent, 0, null, out uint used);
        if (used == 0) return null;

        char[] message = new char[used];
        fixed (char* text = message)
        {
            if (!EvtFormatMessage(publisher, eventHandle, 0, 0, IntPtr.Zero, EvtFormatMessageEvent, used, text, out used))
                return null;

            return new string(text, 0, (int)Math.Max(0, used - 1)).Trim();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _subscriptionWait?.Unregister(waitObject: null);
            _subscriptionWait = null;
            if (_subscription != IntPtr.Zero) EvtClose(_subscription);
            _subscription = IntPtr.Zero;
        }

        _subscriptionSignal.Dispose();
    }

    private const uint EvtQueryChannelPath = 0x1;
    private const uint EvtQueryReverseDirection = 0x200;
    private const uint EvtSubscribeToFutureEvents = 1;
    private const uint EvtRenderContextSystem = 1;
    private const uint EvtRenderEventValues = 0;
    private const uint EvtFormatMessageEvent = 1;
    private const int SystemProviderName = 0;
    private const int SystemEventID = 2;
    private const int SystemTimeCreated = 8;
    private const int SystemEventRecordID = 9;
    private const uint EvtVarTypeString = 1;
    private const uint EvtVarTypeUInt16 = 6;
    private const uint EvtVarTypeUInt64 = 10;
    private const uint EvtVarTypeFileTime = 17;

    [StructLayout(LayoutKind.Sequential)]
    private struct EVT_VARIANT
    {
        public ulong Value;
        public uint Count;
        public uint Type;
    }

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr EvtQuery(IntPtr session, string path, string query, uint flags);

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr EvtSubscribe(
        IntPtr session,
        IntPtr signalEvent,
        string channelPath,
        string query,
        IntPtr bookmark,
        IntPtr context,
        IntPtr callback,
        uint flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    private static extern bool EvtNext(
        IntPtr resultSet,
        uint eventsSize,
        [Out] IntPtr[] events,
        uint timeout,
        uint flags,
        out uint returned);

    [DllImport("wevtapi.dll", SetLastError = true)]
    private static extern IntPtr EvtCreateRenderContext(uint valuePathsCount, IntPtr valuePaths, uint flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    private static extern unsafe bool EvtRender(
        IntPtr context,
        IntPtr fragment,
        uint flags,
        uint bufferSize,
        byte* buffer,
        out uint bufferUsed,
        out uint propertyCount);

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr EvtOpenPublisherMetadata(
        IntPtr session,
        string publisherID,
        string? logFilePath,
        uint locale,
        uint flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    private static extern unsafe bool EvtFormatMessage(
        IntPtr publisherMetadata,
        IntPtr eventHandle,
        uint messageID,
        uint valueCount,
        IntPtr values,
        uint flags,
        uint bufferSize,
        char* buffer,
        out uint bufferUsed);

    [DllImport("wevtapi.dll")]
    private static extern bool EvtClose(IntPtr handle);
}

/// <summary>The result of one System log read.</summary>
internal readonly record struct MemoryErrorReadResult(IReadOnlyList<MemoryErrorEntry> Entries, string? Error);

