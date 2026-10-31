using System.Runtime.InteropServices;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Keeps a COM multithreaded apartment alive while Core Audio session notifications are registered.
/// Windows does not deliver IAudioSessionNotification callbacks unless the application has an MTA.
/// </summary>
internal sealed class CoreAudioSessionMTAThread : IDisposable
{
    private const string ThreadName = "VolumeTrayApp.CoreAudioSessionMTA";

    private readonly ManualResetEventSlim _started = new(false);
    private readonly ManualResetEventSlim _shutdown = new(false);
    private readonly Thread _thread;
    private Exception? _startupException;
    private int _isRunning;
    private int _disposed;

    internal bool IsRunning => Volatile.Read(ref _isRunning) != 0;
    internal ApartmentState ApartmentState { get; private set; } = ApartmentState.Unknown;

    public CoreAudioSessionMTAThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = ThreadName };
        _thread.Start();
        _started.Wait();

        if (_startupException == null) return;

        _thread.Join();
        _started.Dispose();
        _shutdown.Dispose();
        throw new InvalidOperationException(message: "Failed to initialize the Core Audio notification MTA.",
            _startupException);
    }

    private void Run()
    {
        // A fresh thread has no apartment yet, so any failure (including RPC_E_CHANGED_MODE) means no MTA
        using ComApartmentScope apartment = ComApartmentScope.Enter(Ole32.COINIT_MULTITHREADED);
        if (apartment.HResult < 0)
        {
            _startupException = Marshal.GetExceptionForHR(apartment.HResult)
                                ?? new COMException(message: "CoInitializeEx failed.", apartment.HResult);
            _started.Set();
            return;
        }

        ApartmentState = Thread.CurrentThread.GetApartmentState();
        Volatile.Write(ref _isRunning, value: 1);
        _started.Set();

        try
        {
            _shutdown.Wait();
        }
        finally
        {
            Volatile.Write(ref _isRunning, value: 0);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

        _shutdown.Set();
        if (!ReferenceEquals(Thread.CurrentThread, _thread)) _thread.Join();
        _started.Dispose();
        _shutdown.Dispose();
    }
}
