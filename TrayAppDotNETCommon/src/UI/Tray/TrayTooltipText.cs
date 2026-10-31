namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>Fits tray tooltip text into the shell's fixed tooltip buffer.</summary>
public static class TrayTooltipText
{
    /// <summary>The longest tooltip the shell shows: <c>NOTIFYICONDATAW.szTip</c> holds 128 characters with its NUL.</summary>
    public const int MaxLength = 127;

    public const char Ellipsis = '\u2026';

    /// <summary>
    /// Returns text that fits <paramref name="maxLength"/> characters. Longer text is cut after its last complete line
    /// that fits, or, when not even the first line fits, at the last whole character, never between the two halves
    /// of a surrogate pair. An ellipsis marks the cut.
    /// </summary>
    public static string Truncate(string text, int maxLength = MaxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        if (text.Length <= maxLength) return text;

        int budget = maxLength - 1;
        int lineBreak = text.LastIndexOf('\n', budget);
        int cut = lineBreak > 0 ? lineBreak : budget;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;

        return string.Concat(text.AsSpan(0, cut).TrimEnd(), Ellipsis.ToString());
    }
}
