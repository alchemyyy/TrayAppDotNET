using System.Globalization;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// Serializes a <see cref="StartupAppApprovalTarget"/> into the broker request's text field and back.
/// Fields are joined with the ASCII unit separator, which never appears in a registry subkey or value
/// name, so entry names with spaces or pipes survive intact.
/// </summary>
internal static class StartupApprovalWire
{
    private const char FieldSeparator = '\u001F';
    private const int FieldCount = 4;

    public static string Encode(StartupAppApprovalTarget target) => string.Join(
        FieldSeparator,
        ((byte)target.Scope).ToString(CultureInfo.InvariantCulture),
        ((byte)target.RegistryView).ToString(CultureInfo.InvariantCulture),
        target.RegistrySubKey,
        target.ValueName);

    public static bool TryDecode(string? text, out StartupAppApprovalTarget target)
    {
        target = default;
        if (string.IsNullOrEmpty(text)) return false;

        string[] fields = text.Split(FieldSeparator);
        if (fields.Length != FieldCount) return false;

        if (!byte.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte scopeValue) ||
            !byte.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte viewValue))
            return false;

        if (!Enum.IsDefined((StartupAppScope)scopeValue) || !Enum.IsDefined((StartupAppRegistryView)viewValue))
            return false;

        StartupAppApprovalTarget decoded = new(
            (StartupAppScope)scopeValue,
            (StartupAppRegistryView)viewValue,
            fields[2],
            fields[3]);

        // Only the StartupApproved subkeys with a named value may reach the elevated registry write
        if (!decoded.IsValid) return false;

        target = decoded;
        return true;
    }
}
