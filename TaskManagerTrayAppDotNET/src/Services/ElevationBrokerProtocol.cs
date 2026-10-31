using System.Globalization;
using System.Text;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>The privileged operations the elevated broker is allowed to perform.</summary>
internal enum ElevationOp
{
    SetPriority = 1,
    SetAffinity = 2,
    CreateDump = 3,
    DisconnectSession = 4,
    ServiceControl = 5,
    SetStartupApproval = 6
}

/// <summary>The service verb carried in a <see cref="ElevationOp.ServiceControl"/> request argument.</summary>
internal enum BrokerServiceVerb
{
    Start = 1,
    Stop = 2,
    Restart = 3,
    Disable = 4
}

/// <summary>The typed outcome of a brokered operation.</summary>
internal enum BrokerResultCode
{
    Success = 0,
    AccessDenied = 1,
    InvalidTarget = 2,
    NotFound = 3,
    Failed = 4
}

/// <summary>
/// One request from the medium-integrity UI to the elevated broker. The target identity
/// (PID plus creation time) is carried so the broker re-validates it and never acts on a
/// reused PID. <see cref="Argument"/> holds the priority class, the affinity mask, the session ID,
/// the service verb or the startup approval state depending on <see cref="Op"/>; <see cref="Text"/>
/// carries the service name or the encoded startup approval target.
///
/// The wire form is a single pipe-delimited line so both ends can use ordinary line-based pipe
/// I/O; the numeric fields come first and the free text is the last field, with newlines collapsed.
/// Serialization is hand-rolled (no JSON, no reflection) to stay Native AOT friendly.
/// </summary>
internal readonly record struct BrokerRequest(
    long CorrelationId,
    ElevationOp Op,
    int ProcessID,
    long CreationTimeFileTime,
    ulong Argument,
    string Text)
{
    private const int FieldCount = 6;

    public string ToWire()
    {
        // Text is the trailing field and may contain '|'; only newlines are collapsed so it stays one line
        string singleLineText = (Text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{CorrelationId}|{(int)Op}|{ProcessID}|{CreationTimeFileTime}|{Argument}|{singleLineText}");
    }

    public static bool TryParse(string? line, out BrokerRequest request)
    {
        request = default;
        if (string.IsNullOrEmpty(line)) return false;

        string[] fields = line.Split('|', FieldCount);
        if (fields.Length != FieldCount) return false;

        if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long correlationId) ||
            !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int opValue) ||
            !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId) ||
            !long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long creationTime) ||
            !ulong.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong argument))
            return false;

        if (!Enum.IsDefined((ElevationOp)opValue)) return false;

        request = new BrokerRequest(correlationId, (ElevationOp)opValue, processId, creationTime, argument, fields[5]);
        return true;
    }

    /// <summary>Reads the correlation ID of a line that failed to parse, so the broker can still answer it.</summary>
    public static long ReadCorrelationId(string? line)
    {
        if (string.IsNullOrEmpty(line)) return 0;
        int separator = line.IndexOf('|');
        ReadOnlySpan<char> field = separator < 0 ? line : line.AsSpan(0, separator);
        return long.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out long correlationId)
            ? correlationId
            : 0;
    }
}

/// <summary>One response from the broker. The message is the last field, so it may contain '|'.</summary>
internal readonly record struct BrokerResponse(
    long CorrelationId,
    BrokerResultCode Code,
    string Message)
{
    private const int MinimumFieldCount = 3;

    public string ToWire()
    {
        // The message is the trailing field; collapse newlines so it stays on one pipe line
        string singleLineMessage = (Message ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{CorrelationId}|{(int)Code}|{singleLineMessage}");
    }

    public static bool TryParse(string? line, out BrokerResponse response)
    {
        response = default;
        if (string.IsNullOrEmpty(line)) return false;

        string[] fields = line.Split('|', MinimumFieldCount);
        if (fields.Length < MinimumFieldCount) return false;

        if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long correlationId) ||
            !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int codeValue))
            return false;

        if (!Enum.IsDefined((BrokerResultCode)codeValue)) return false;

        response = new BrokerResponse(correlationId, (BrokerResultCode)codeValue, fields[2]);
        return true;
    }
}

/// <summary>
/// A diagnostic line the broker sends to the UI ahead of a response, so the UI writes it into its own log. The
/// elevated broker never writes a log file: a same-user process controls the user's log folder and could turn an
/// elevated append or rotation there into an arbitrary file operation with a junction. Line breaks are escaped so a
/// multi-line message (an exception with its stack) stays one pipe line; a response line never starts with the
/// prefix because responses start with a number.
/// </summary>
internal static class BrokerLogLine
{
    private const string Prefix = "log|";

    public static string Encode(string message)
    {
        StringBuilder builder = new(Prefix.Length + message.Length);
        builder.Append(Prefix);
        foreach (char character in message)
        {
            switch (character)
            {
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '\r':
                    builder.Append(@"\r");
                    break;
                case '\n':
                    builder.Append(@"\n");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }

    public static bool TryDecode(string? line, out string message)
    {
        message = string.Empty;
        if (line == null || !line.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        StringBuilder builder = new(line.Length - Prefix.Length);
        for (int index = Prefix.Length; index < line.Length; index++)
        {
            char character = line[index];
            if (character != '\\' || index + 1 == line.Length)
            {
                builder.Append(character);
                continue;
            }

            index++;
            builder.Append(line[index] switch
            {
                'r' => '\r',
                'n' => '\n',
                _ => line[index]
            });
        }

        message = builder.ToString();
        return true;
    }
}

/// <summary>
/// Holds the broker's log lines until the next response carries them to the UI. Lines logged while no UI is
/// connected wait here; beyond <see cref="Capacity"/> the oldest are dropped and counted.
/// </summary>
internal sealed class BrokerLogBuffer
{
    internal const int Capacity = 512;

    private readonly Lock _sync = new();
    private readonly Queue<string> _lines = new();
    private int _droppedLines;

    public void Add(string message)
    {
        lock (_sync)
        {
            if (_lines.Count == Capacity)
            {
                _lines.Dequeue();
                _droppedLines++;
            }

            _lines.Enqueue(message);
        }
    }

    /// <summary>Removes and returns every pending line, oldest first, after a note of any dropped lines.</summary>
    public List<string> TakeAll()
    {
        lock (_sync)
        {
            List<string> lines = new(_lines.Count + 1);
            if (_droppedLines > 0)
            {
                lines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"ElevationBroker: {_droppedLines} earlier log lines were dropped."));
                _droppedLines = 0;
            }

            lines.AddRange(_lines);
            _lines.Clear();
            return lines;
        }
    }
}

/// <summary>Shared command-line and pipe conventions for the elevated broker mode.</summary>
internal static class ElevationBrokerContract
{
    public const string ModeArgument = "--elevated-broker";
    public const string ParentArgument = "--broker-parent";
    public const string ParentStartArgument = "--broker-parent-start";
    public const string PipeArgument = "--broker-pipe";

    /// <summary>How long a quick handle or registry operation may take before the client gives up.</summary>
    public static readonly TimeSpan QuickOperationTimeout = TimeSpan.FromSeconds(15);

    /// <summary>A service restart waits for two state transitions of up to about 30 s each.</summary>
    public static readonly TimeSpan ServiceOperationTimeout = TimeSpan.FromSeconds(90);

    /// <summary>A full memory dump of a large process can take minutes.</summary>
    public static readonly TimeSpan DumpOperationTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Builds the unique per-session pipe name the UI passes to the broker it launches.</summary>
    public static string CreatePipeName() =>
        $"{Constants.ApplicationName}-Broker-{Guid.NewGuid():N}";

    /// <summary>Returns how long the client waits for the broker's answer to one operation.</summary>
    public static TimeSpan ResponseTimeout(ElevationOp op) => op switch
    {
        ElevationOp.ServiceControl => ServiceOperationTimeout,
        ElevationOp.CreateDump => DumpOperationTimeout,
        _ => QuickOperationTimeout
    };

    /// <summary>
    /// Builds the broker command line: the UI's PID and start time pin the only client it will serve. No settings
    /// folder is passed: the elevated broker writes no log file and sends its log lines to the UI instead.
    /// </summary>
    public static string BuildArguments(int parentProcessId, long parentStartFileTime, string pipeName) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{ModeArgument} {ParentArgument} {parentProcessId} {ParentStartArgument} {parentStartFileTime} {PipeArgument} {pipeName}");

    /// <summary>Parses the broker command line; every value must be present and well formed.</summary>
    public static bool TryParseArguments(
        string[] args,
        out int parentProcessId,
        out long parentStartFileTime,
        out string pipeName)
    {
        ArgumentNullException.ThrowIfNull(args);
        parentProcessId = 0;
        parentStartFileTime = 0;
        pipeName = string.Empty;
        if (!TryGetArgumentValue(args, ParentArgument, out string? parentText)
            || !int.TryParse(parentText, NumberStyles.None, CultureInfo.InvariantCulture, out parentProcessId)
            || parentProcessId <= 0
            || !TryGetArgumentValue(args, ParentStartArgument, out string? startText)
            || !long.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out parentStartFileTime)
            || parentStartFileTime <= 0
            || !TryGetArgumentValue(args, PipeArgument, out string? pipeText)
            || string.IsNullOrWhiteSpace(pipeText))
            return false;

        pipeName = pipeText;
        return true;
    }

    /// <summary>
    /// Returns whether a connecting client is the UI that launched the broker. The broker holds a handle to that
    /// process for its whole life, so the PID cannot be recycled while the broker runs; the start time was
    /// checked against the command line when the handle was opened.
    /// </summary>
    public static bool IsAuthorizedClient(uint clientProcessId, int parentProcessId) =>
        parentProcessId > 0 && clientProcessId == (uint)parentProcessId;

    /// <summary>Returns whether the opened parent process is the one named on the command line.</summary>
    public static bool IsExpectedParent(long expectedStartFileTime, long actualStartFileTime) =>
        expectedStartFileTime > 0 && expectedStartFileTime == actualStartFileTime;

    private static bool TryGetArgumentValue(string[] args, string name, out string? value)
    {
        value = null;
        int index = Array.FindIndex(args, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return false;

        value = args[index + 1];
        return true;
    }
}
