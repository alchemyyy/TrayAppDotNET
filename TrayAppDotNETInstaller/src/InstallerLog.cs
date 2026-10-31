using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TrayAppDotNETInstaller;

/// <summary>
/// Append-only diagnostic log under %TEMP%\TrayAppDotNETInstaller. Never throws.
/// <para>
/// The folder is the user's, so an elevated process never appends to the shared installer.log there: a planted
/// link could turn that into a write anywhere. The elevated worker hands its lines to the unelevated window
/// through the progress pipe (see <see cref="RedirectTo"/>), and an installer window that itself runs elevated
/// writes a file of its own with a fresh random name, created only if it does not exist yet.
/// </para>
/// </summary>
internal static class InstallerLog
{
    private const string FileName = "installer.log";
    private const string ElevatedFileNameFormat = "installer-{0}-{1:N}.log";
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

    // Stands in for System.Threading.Lock, which the .NET Framework does not define
    private static readonly object Gate = new();
    private static readonly List<string> Pending = [];
    private static readonly bool IsElevatedProcess = ReadIsElevated();
    private static readonly string ElevatedFileName = string.Format(
        CultureInfo.InvariantCulture, ElevatedFileNameFormat, FrameworkCompatibility.ProcessID, Guid.NewGuid());

    private static Action<string>? _sink;
    private static bool _buffering;
    private static bool _elevatedFileCreated;

    public static string FilePath => Path.Combine(InstallerTempPaths.Root, IsElevatedProcess ? ElevatedFileName : FileName);

    /// <summary>Keeps lines in memory until <see cref="RedirectTo"/> names where they go. Used by the elevated worker.</summary>
    public static void BeginBuffering()
    {
        lock (Gate) _buffering = true;
    }

    /// <summary>Sends every line, the buffered ones first, to <paramref name="sink"/> instead of a file.</summary>
    public static void RedirectTo(Action<string> sink)
    {
        lock (Gate)
        {
            _sink = sink;
            _buffering = false;
            foreach (string line in Pending) sink(line);
            Pending.Clear();
        }
    }

    public static void Write(string message)
    {
        string timestamp = DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        string line = $"{timestamp} [{FrameworkCompatibility.ProcessID}] {message}";
        lock (Gate)
        {
            if (_sink != null)
            {
                _sink(line);
                return;
            }

            if (_buffering)
            {
                Pending.Add(line);
                return;
            }

            WriteToFile(line);
        }
    }

    public static void Write(string context, Exception exception) => Write($"{context}: {exception}");

    private static void WriteToFile(string line)
    {
        try
        {
            Directory.CreateDirectory(InstallerTempPaths.Root);
            if (IsElevatedProcess && !_elevatedFileCreated)
            {
                // A junction in place of the folder would send the new file elsewhere
                if ((File.GetAttributes(InstallerTempPaths.Root) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"{InstallerTempPaths.Root} is a reparse point.");
                using (new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
                _elevatedFileCreated = true;
            }

            File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The log is the last resort, so an unusable temp directory only reaches the debugger
            Debug.WriteLine($"InstallerLog: {exception.Message}");
        }
    }

    // SystemProbes.IsElevated logs its failures here, so the log reads the token itself
    private static bool ReadIsElevated()
    {
        try
        {
            using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
}
