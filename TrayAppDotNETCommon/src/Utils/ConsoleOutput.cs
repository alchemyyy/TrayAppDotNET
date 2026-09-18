using System.Text;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.Utils;

/// <summary>
/// Best-effort line writer for the standard streams of a WinExe process, which has no console of its own.
/// The first write decides once where lines go: the standard handles the parent redirected (installer app,
/// shell pipe, file), otherwise the parent's console after AttachConsole, otherwise nowhere.
/// </summary>
public static class TrayAppDotNETConsoleOutput
{
    private const string LogPrefix = "TrayAppDotNETConsoleOutput";

    private static readonly Lock Gate = new();
    private static bool _initialized;
    private static StreamWriter? _standardOutput;
    private static StreamWriter? _standardError;

    /// <summary>True when a redirected standard output or an attached parent console accepts lines. Forces initialization.</summary>
    public static bool IsAvailable
    {
        get
        {
            lock (Gate)
            {
                EnsureInitialized();
                return _standardOutput != null;
            }
        }
    }

    /// <summary>
    /// Writes one line to standard output, or standard error when <paramref name="error"/> is set.
    /// Returns false when no console or redirected handle is reachable. Never throws.
    /// </summary>
    public static bool TryWriteLine(string text, bool error = false)
    {
        lock (Gate)
        {
            EnsureInitialized();
            StreamWriter? writer = error ? _standardError : _standardOutput;
            if (writer == null) return false;

            try
            {
                writer.WriteLine(text);
                return true;
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                TADNLog.Log($"{LogPrefix}: console write failed; later lines are dropped: {exception.Message}");
                MarkUnavailable();
                return false;
            }
        }
    }

    // Caller holds Gate
    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            if (HasRedirectedStandardOutput())
            {
                AttachWriters();
                return;
            }

            if (Kernel32.AttachConsole(Kernel32.ATTACH_PARENT_PROCESS))
            {
                AttachWriters();
                return;
            }

            TADNLog.LogDebug($"{LogPrefix}: no redirected standard output and no parent console; console lines are dropped");
        }
        catch (Exception exception)
        {
            TADNLog.Log($"{LogPrefix}: console setup failed; console lines are dropped: {exception.Message}");
            MarkUnavailable();
        }
    }

    /// <summary>True when the parent process handed this process a usable standard output handle.</summary>
    private static bool HasRedirectedStandardOutput()
    {
        IntPtr handle = Kernel32.GetStdHandle(Kernel32.STD_OUTPUT_HANDLE);
        if (handle == IntPtr.Zero || handle == Kernel32.INVALID_HANDLE_VALUE) return false;

        return Kernel32.GetFileType(handle) != Kernel32.FILE_TYPE_UNKNOWN;
    }

    // Caller holds Gate. Console.OpenStandardOutput re-reads the standard handles, so this also picks up
    // handles that only became valid after AttachConsole
    private static void AttachWriters()
    {
        UTF8Encoding encoding = new(encoderShouldEmitUTF8Identifier: false);
        StreamWriter standardOutput = new(Console.OpenStandardOutput(), encoding) { AutoFlush = true };
        StreamWriter standardError = new(Console.OpenStandardError(), encoding) { AutoFlush = true };
        Console.SetOut(standardOutput);
        Console.SetError(standardError);
        _standardOutput = standardOutput;
        _standardError = standardError;
    }

    // Caller holds Gate. The writers are left undisposed on purpose: Console.Out still references them and
    // disposing a writer over a broken pipe would only raise the same IOException again
    private static void MarkUnavailable()
    {
        _standardOutput = null;
        _standardError = null;
    }
}
