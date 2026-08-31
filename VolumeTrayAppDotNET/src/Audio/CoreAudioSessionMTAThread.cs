using System.Runtime.InteropServices;

namespace VolumeTrayAppDotNET.Audio;

/// <summary>
/// Keeps a COM multithreaded apartment alive while Core Audio session notifications are registered.
/// Windows does not deliver IAudioSessionNotification callbacks unless the application has an MTA.
/// </summary>
internal sealed class CoreAudioSessionMTAThread : IDisposable
{
    private const uint MultithreadedApartment = 0;
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
        int initializationResult = CoInitializeEx(IntPtr.Zero, MultithreadedApartment);
        if (initializationResult < 0)
        {
            _startupException = Marshal.GetExceptionForHR(initializationResult)
                                ?? new COMException(message: "CoInitializeEx failed.", initializationResult);
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
            CoUninitialize();
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

    [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint initializationMode);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
}
