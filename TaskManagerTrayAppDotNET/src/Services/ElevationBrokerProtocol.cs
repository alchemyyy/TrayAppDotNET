using System.Globalization;

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
/// reused PID. <see cref="Argument"/> holds the priority class or the affinity mask depending
/// on <see cref="Op"/>.
///
/// The wire form is a single pipe-delimited line so both ends can use ordinary line-based pipe
/// I/O; every field is numeric, so there is no free text to escape. Serialization is hand-rolled
/// (no JSON, no reflection) to stay Native AOT friendly.
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

/// <summary>Shared command-line and pipe conventions for the elevated broker mode.</summary>
internal static class ElevationBrokerContract
{
    public const string ModeArgument = "--elevated-broker";
    public const string ParentArgument = "--broker-parent";
    public const string PipeArgument = "--broker-pipe";

    /// <summary>Builds the unique per-session pipe name the UI passes to the broker it launches.</summary>
    public static string CreatePipeName() =>
        $"{Constants.ApplicationName}-Broker-{Guid.NewGuid():N}";
}
