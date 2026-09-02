using System.Text;

namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// Shared managed-side constants and encoding for the native DDC helper protocol.
/// </summary>
internal static class DDCHelperProtocol
{
    public const int Version = 1;
    public const string ServerArgument = "--ddc-helper-server";
    public const string ParentProcessIDArgument = "--parent-pid";
    public const string PipeNameArgument = "--pipe-name";
    public const string ExitCommand = "EXIT";
    public const string PingCommand = "PING";
    public const string ReadyResponse = "READY\t1";
    public const string PingResponse = "OK\tPONG\t1";
    public static readonly Encoding PipeEncoding = new UTF8Encoding(false);

    public static string BuildCommand(string verb, DDCMonitor monitor, params string[] arguments)
    {
        StringBuilder builder = new(verb);
        AppendEncodedField(builder, monitor.DeviceID);
        AppendEncodedField(builder, monitor.EDIDSerial);
        AppendEncodedField(builder, monitor.Name);
        AppendEncodedField(builder, monitor.DisplayInstancePath);
        foreach (string argument in arguments)
        {
            builder.Append('\t');
            builder.Append(argument);
        }

        return builder.ToString();
    }

    public static string EncodeField(string value) =>
        Convert.ToBase64String(PipeEncoding.GetBytes(value));

    public static string DecodeField(string value) =>
        PipeEncoding.GetString(Convert.FromBase64String(value));

    public static bool IsReadyResponse(string? response) =>
        string.Equals(response, ReadyResponse, StringComparison.Ordinal);

    private static void AppendEncodedField(StringBuilder builder, string value)
    {
        builder.Append('\t');
        builder.Append(EncodeField(value));
    }
}
