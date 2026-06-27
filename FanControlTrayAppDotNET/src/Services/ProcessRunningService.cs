using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;

namespace FanControlTrayAppDotNET.Services;

// One running process the service has seen. CommandLine remains empty in the low-dependency build;
// low-cost command-line capture depended on WMI/System.Management.
public sealed class ProcessSnapshot
{
    public int PID { get; init; }
    public string ImageName { get; init; } = string.Empty;
    public string CommandLine { get; init; } = string.Empty;
    public DateTime FirstSeenUtc { get; init; }
}

// Maintains a live view of running processes using periodic Process.GetProcesses reconciliation.
// This avoids WMI/System.Management so the tray app remains friendly to constrained publish modes.
//
// What this pass does NOT do yet:
//   * Per-process resource readings (CPU/RAM)
//   * Match-rule evaluation for triggers
//   * Persistence of process-matched fan profiles
public sealed class ProcessRunningService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<int, ProcessSnapshot> _processes = [];
    private readonly ObservableCollection<ProcessSnapshot> _observable = [];

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private bool _disposed;

    public ReadOnlyObservableCollection<ProcessSnapshot> Processes { get; }

    public event Action<ProcessSnapshot>? ProcessStarted;
    public event Action<ProcessSnapshot>? ProcessExited;

    public ProcessRunningService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Processes = new ReadOnlyObservableCollection<ProcessSnapshot>(_observable);
    }

    public void Start()
    {
        if (_pollCts != null) return;

        _dispatcher.Post(InitialSnapshot);

        _pollCts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoop(_pollCts.Token));
    }

    public void Stop()
    {
        if (_pollCts != null)
        {
            _pollCts.Cancel();
            try { _pollTask?.Wait(TimeConstants.BackgroundPollShutdownWaitMs); }
            catch (AggregateException)
            {
                /* poll loop already exited */
            }

            _pollCts.Dispose();
            _pollCts = null;
            _pollTask = null;
        }
    }

    // Cold-start sweep using Process.GetProcesses.
    private void InitialSnapshot()
    {
        DateTime now = DateTime.UtcNow;
        foreach (Process p in Process.GetProcesses())
        {
            string image = SafeProcessName(p);
            ProcessSnapshot snap = new()
            {
                PID = p.Id, ImageName = image, CommandLine = string.Empty, FirstSeenUtc = now,
            };
            if (_processes.TryAdd(p.Id, snap)) _observable.Add(snap);
            p.Dispose();
        }
    }

    // Reconciliation sweep. Process.GetProcesses enumerates every running PID on Windows and can
    // take tens of ms; it runs on this background loop so
    // the UI thread stays free for slider drags and frame paints. Snapshot mutations marshal
    // back to the dispatcher because the ObservableCollection is bound to UI.
    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                HashSet<int> currentPIDs = new(
                    Process.GetProcesses().Select(p =>
                    {
                        int id = p.Id;
                        p.Dispose();
                        return id;
                    }));

                List<ProcessSnapshot> newcomers = [];
                foreach (int pid in currentPIDs)
                {
                    if (_processes.ContainsKey(pid)) continue;
                    try
                    {
                        using Process p = Process.GetProcessById(pid);
                        newcomers.Add(new ProcessSnapshot
                        {
                            PID = pid,
                            ImageName = SafeProcessName(p),
                            CommandLine = string.Empty,
                            FirstSeenUtc = DateTime.UtcNow,
                        });
                    }
                    catch
                    {
                        /* exited between enumeration and lookup */
                    }
                }

                await _dispatcher.InvokeAsync(() =>
                {
                    foreach (int gone in _processes.Keys.Where(id => !currentPIDs.Contains(id)).ToList())
                        RemovePID(gone);

                    foreach (ProcessSnapshot snap in newcomers)
                    {
                        if (!_processes.TryAdd(snap.PID, snap)) continue;
                        _observable.Add(snap);
                        ProcessStarted?.Invoke(snap);
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex) { TADNLog.Log($"ProcessRunningService poll failed: {ex.Message}"); }

            try { await Task.Delay(TimeConstants.ProcessListPollIntervalMs, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void RemovePID(int pid)
    {
        if (!_processes.Remove(pid, out ProcessSnapshot? snap)) return;
        _observable.Remove(snap);
        ProcessExited?.Invoke(snap);
    }

    private static string SafeProcessName(Process p)
    {
        try { return p.ProcessName + ".exe"; }
        catch { return string.Empty; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
