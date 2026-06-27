namespace BrightnessTrayAppDotNET.Tests.NightLight;

/// <summary>
/// Console logger for the night-light tester. Timestamped, color-coded, and lock-serialized
/// so multi-threaded callers (e.g. the MTA init thread inside NightLightSettingsHandlerTester)
/// don't interleave their output with the driver loop.
/// </summary>
internal static class ConsoleLog
{
    private const string TimestampFmt = "HH:mm:ss.fff";
    private static readonly Lock _gate = new();

    public static void Header(string msg)  => Write(ConsoleColor.Magenta, "====", msg);
    public static void Section(string msg) => Write(ConsoleColor.Cyan,    "STEP", msg);
    public static void Info(string msg)    => Write(ConsoleColor.White,   "INFO", msg);
    public static void Trace(string msg)   => Write(ConsoleColor.DarkGray, "TRC ", msg);
    public static void Ok(string msg)      => Write(ConsoleColor.Green,   " OK ", msg);
    public static void Warn(string msg)    => Write(ConsoleColor.Yellow,  "WARN", msg);
    public static void Error(string msg)   => Write(ConsoleColor.Red,     "ERR ", msg);

    private static void Write(ConsoleColor color, string tag, string msg)
    {
        lock (_gate)
        {
            ConsoleColor prev = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine($"[{DateTime.Now.ToString(TimestampFmt)}] [{tag}] {msg}");
            }
            finally
            {
                Console.ForegroundColor = prev;
            }
        }
    }
}
