using System.Diagnostics;
using System.Globalization;

namespace TrayAppDotNETInstaller;

/// <summary>Append-only diagnostic log at %TEMP%\TrayAppDotNETInstaller\installer.log. Never throws.</summary>
internal static class InstallerLog
{
    private const string FileName = "installer.log";
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

    // Stands in for System.Threading.Lock, which the .NET Framework does not define
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(InstallerTempPaths.Root, FileName);

    public static void Write(string message)
    {
        string timestamp = DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        string line = $"{timestamp} [{FrameworkCompatibility.ProcessID}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(InstallerTempPaths.Root);
                File.AppendAllText(FilePath, line);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // The log is the last resort, so an unusable temp directory only reaches the debugger
                Debug.WriteLine($"InstallerLog: {exception.Message}");
            }
        }
    }

    public static void Write(string context, Exception exception) => Write($"{context}: {exception}");
}
