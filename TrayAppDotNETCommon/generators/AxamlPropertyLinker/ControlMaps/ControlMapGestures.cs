namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>Grammar for control map key and pointer gesture lists.</summary>
internal static class ControlMapGestures
{
    private const char GestureSeparator = ';';
    private const char PartSeparator = '+';

    // Canonical order; Avalonia formats gestures the same way
    private static readonly string[] KeyModifiers = ["Ctrl", "Shift", "Alt", "Win"];
    private static readonly string[] PointerModifiers = ["Ctrl", "Shift", "Alt"];

    private static readonly HashSet<string> PointerActions = new(StringComparer.Ordinal)
    {
        "Click",
        "DoubleClick",
        "RightClick",
        "MiddleClick",
        "Wheel",
        "Drag"
    };

    // Avalonia 12 Avalonia.Input.Key names, minus None. KeyGesture.Parse falls back to Enum.Parse, which also accepts
    // numbers, so "1" silently becomes Key.Cancel; only these names are accepted here
    private static readonly HashSet<string> KeyNames = new(StringComparer.Ordinal)
    {
        "Cancel", "Back", "Tab", "LineFeed", "Clear", "Return", "Enter", "Pause", "CapsLock", "Capital",
        "HangulMode", "KanaMode", "JunjaMode", "FinalMode", "KanjiMode", "HanjaMode", "Escape", "ImeConvert",
        "ImeNonConvert", "ImeAccept", "ImeModeChange", "Space", "PageUp", "Prior", "PageDown", "Next", "End", "Home",
        "Left", "Up", "Right", "Down", "Select", "Print", "Execute", "Snapshot", "PrintScreen", "Insert", "Delete",
        "Help", "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "A", "B", "C", "D", "E", "F", "G", "H",
        "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "LWin", "RWin",
        "Apps", "Sleep", "NumPad0", "NumPad1", "NumPad2", "NumPad3", "NumPad4", "NumPad5", "NumPad6", "NumPad7",
        "NumPad8", "NumPad9", "Multiply", "Add", "Separator", "Subtract", "Decimal", "Divide", "F1", "F2", "F3", "F4",
        "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20",
        "F21", "F22", "F23", "F24", "NumLock", "Scroll", "LeftShift", "RightShift", "LeftCtrl", "RightCtrl",
        "LeftAlt", "RightAlt", "BrowserBack", "BrowserForward", "BrowserRefresh", "BrowserStop", "BrowserSearch",
        "BrowserFavorites", "BrowserHome", "VolumeMute", "VolumeDown", "VolumeUp", "MediaNextTrack",
        "MediaPreviousTrack", "MediaStop", "MediaPlayPause", "LaunchMail", "SelectMedia", "LaunchApplication1",
        "LaunchApplication2", "OemSemicolon", "Oem1", "OemPlus", "OemComma", "OemMinus", "OemPeriod", "OemQuestion",
        "Oem2", "OemTilde", "Oem3", "AbntC1", "AbntC2", "OemOpenBrackets", "Oem4", "OemPipe", "Oem5",
        "OemCloseBrackets", "Oem6", "OemQuotes", "Oem7", "Oem8", "OemBackslash", "Oem102", "ImeProcessed", "System",
        "OemAttn", "DbeAlphanumeric", "OemFinish", "DbeKatakana", "DbeHiragana", "OemCopy", "DbeSbcsChar", "OemAuto",
        "DbeDbcsChar", "OemEnlw", "OemBackTab", "DbeRoman", "DbeNoRoman", "Attn", "CrSel",
        "DbeEnterWordRegisterMode", "ExSel", "DbeEnterImeConfigureMode", "EraseEof", "DbeFlushString", "Play",
        "DbeCodeInput", "DbeNoCodeInput", "Zoom", "NoName", "DbeDetermineString", "DbeEnterDialogConversionMode",
        "Pa1", "OemClear", "DeadCharProcessed", "FnLeftArrow", "FnRightArrow", "FnUpArrow", "FnDownArrow",
        "MediaHome", "MediaChannelList", "MediaChannelRaise", "MediaChannelLower", "MediaRecord", "MediaRed",
        "MediaGreen", "MediaYellow", "MediaBlue", "MediaMenu", "MediaMore", "MediaOption", "MediaInfo",
        "MediaSearch", "MediaSubtitle", "MediaTvGuide", "MediaPreviousChannel"
    };

    // Names that share one Avalonia Key value, mapped to the name Avalonia prints, so duplicates are detected
    private static readonly Dictionary<string, string> KeyAliases = new(StringComparer.Ordinal)
    {
        ["Enter"] = "Return",
        ["Capital"] = "CapsLock",
        ["KanaMode"] = "HangulMode",
        ["KanjiMode"] = "HanjaMode",
        ["Prior"] = "PageUp",
        ["Next"] = "PageDown",
        ["Snapshot"] = "PrintScreen",
        ["Oem1"] = "OemSemicolon",
        ["Oem2"] = "OemQuestion",
        ["OemTilde"] = "Oem3",
        ["OemOpenBrackets"] = "Oem4",
        ["Oem5"] = "OemPipe",
        ["Oem6"] = "OemCloseBrackets",
        ["Oem7"] = "OemQuotes",
        ["Oem102"] = "OemBackslash"
    };

    /// <summary>
    /// Validates a semicolon-separated key gesture list and adds each gesture in normalized form.
    /// Returns an error message, or null when every gesture is valid.
    /// </summary>
    public static string? ParseKeys(string value, List<string> normalizedGestures)
    {
        foreach (string gesture in SplitGestures(value))
        {
            string? error = ParseGesture(gesture, KeyModifiers, isKey: true, out string normalized);
            if (error != null) return error;

            normalizedGestures.Add(normalized);
        }

        return normalizedGestures.Count == 0 ? "the list is empty" : null;
    }

    /// <summary>Validates a semicolon-separated pointer gesture list. Returns an error message, or null.</summary>
    public static string? ParsePointer(string value)
    {
        int gestureCount = 0;
        foreach (string gesture in SplitGestures(value))
        {
            string? error = ParseGesture(gesture, PointerModifiers, isKey: false, out _);
            if (error != null) return error;

            gestureCount++;
        }

        return gestureCount == 0 ? "the list is empty" : null;
    }

    private static IEnumerable<string> SplitGestures(string value)
    {
        foreach (string gesture in value.Split(GestureSeparator))
        {
            string trimmed = gesture.Trim();
            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }

    private static string? ParseGesture(string gesture, string[] modifiers, bool isKey, out string normalized)
    {
        normalized = string.Empty;
        foreach (char character in gesture)
        {
            if (char.IsWhiteSpace(character))
                return $"'{gesture}' contains whitespace; join parts with '+' only";
        }

        string[] parts = gesture.Split(PartSeparator);
        int lastModifierIndex = -1;
        for (int partIndex = 0; partIndex < parts.Length - 1; partIndex++)
        {
            string part = parts[partIndex];
            int modifierIndex = Array.IndexOf(modifiers, part);
            if (modifierIndex < 0)
            {
                return part.Length == 0
                    ? $"'{gesture}' has an empty part; write the plus key as OemPlus"
                    : $"'{part}' is not a modifier; use {string.Join(", ", modifiers)}";
            }

            if (modifierIndex <= lastModifierIndex)
                return $"'{gesture}' must list modifiers once each in the order {string.Join("+", modifiers)}";

            lastModifierIndex = modifierIndex;
        }

        string name = parts[parts.Length - 1];
        if (isKey)
        {
            if (!KeyNames.Contains(name))
            {
                return name.Length > 0 && char.IsDigit(name[0])
                    ? $"'{name}' is not a key name; digits are D0-D9 or NumPad0-NumPad9"
                    : $"'{name}' is not an Avalonia Key name";
            }

            if (KeyAliases.TryGetValue(name, out string? canonicalName))
                name = canonicalName;
        }
        else if (!PointerActions.Contains(name))
        {
            return $"'{name}' is not a pointer gesture; use {string.Join(", ", PointerActions)}";
        }

        normalized = parts.Length == 1
            ? name
            : string.Join("+", parts, startIndex: 0, count: parts.Length - 1) + PartSeparator + name;
        return null;
    }
}
