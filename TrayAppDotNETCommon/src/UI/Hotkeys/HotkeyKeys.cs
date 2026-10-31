using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Localization;

namespace TrayAppDotNETCommon.UI.Hotkeys;

public static class TrayAppDotNETHotkeyKeys
{
    /// <summary>
    /// Makes a read-only key box record the next key press as a hotkey key and show its name. The handler tunnels, so
    /// it sees keys the TextBox itself would consume (Backspace, Delete, Home, End, Page Up/Down, arrows). Tab and
    /// Shift+Tab stay keyboard navigation, as in the Windows hotkey control, so focus can leave the box. Modifier keys
    /// alone are swallowed silently while the user builds a chord; Escape, F12 (reserved by the debugger) and keys
    /// without a virtual key are swallowed and reported through <paramref name="keyRejected"/> with the reason.
    /// </summary>
    public static void AttachKeyCapture(TextBox keyBox, Action<uint> keyCaptured, Action<string>? keyRejected = null)
    {
        ArgumentNullException.ThrowIfNull(keyBox);
        ArgumentNullException.ThrowIfNull(keyCaptured);
        keyBox.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        // The key-up of a recorded key belongs to the capture too, so the standard text box's Enter-to-blur never runs
        keyBox.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        return;

        static void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Tab) e.Handled = true;
        }

        void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab) return;

            e.Handled = true;
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return;

            uint virtualKey = e.Key == Key.Escape ? 0 : VirtualKeyFromKey(e.Key);
            if (virtualKey == HotkeyVirtualKeys.DebuggerReserved)
            {
                keyRejected?.Invoke(L(nameof(CommonStrings.Settings_Hotkeys_Error_ReservedKey)));
                return;
            }

            if (virtualKey == 0)
            {
                keyRejected?.Invoke(L(nameof(CommonStrings.Settings_Hotkeys_KeyNotSupported)));
                return;
            }

            keyBox.Text = KeyName(virtualKey);
            keyCaptured(virtualKey);
        }
    }

    public static uint VirtualKeyFromKey(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return (uint)('A' + (key - Key.A));
        if (key is >= Key.D0 and <= Key.D9) return (uint)('0' + (key - Key.D0));
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return (uint)(0x60 + (key - Key.NumPad0));
        if (key is >= Key.F1 and <= Key.F24) return (uint)(0x70 + (key - Key.F1));

        return key switch
        {
            Key.Back => 0x08,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.Pause => 0x13,
            Key.Escape => 0x1B,
            Key.Space => 0x20,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.PrintScreen => 0x2C,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Apps => 0x5D,
            Key.Multiply => 0x6A,
            Key.Add => 0x6B,
            Key.Separator => 0x6C,
            Key.Subtract => 0x6D,
            Key.Decimal => 0x6E,
            Key.Divide => 0x6F,
            Key.OemSemicolon => 0xBA,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE,
            Key.OemQuestion => 0xBF,
            Key.OemTilde => 0xC0,
            Key.OemOpenBrackets => 0xDB,
            Key.OemPipe => 0xDC,
            Key.OemCloseBrackets => 0xDD,
            Key.OemQuotes => 0xDE,
            Key.Oem8 => 0xDF,
            Key.OemBackslash => 0xE2,
            _ => 0
        };
    }

    /// <summary>
    /// Names a virtual key for display; letters, digits, and function keys keep their keycap text, and punctuation
    /// keys show the character of the current keyboard layout.
    /// </summary>
    public static string KeyName(uint vk)
    {
        return vk switch
        {
            >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => FormatKeyNumber(nameof(CommonStrings.Settings_Hotkeys_Key_NumPad_Format), vk - 0x60),
            >= 0x70 and <= 0x87 => "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture),
            >= 0x6A and <= 0x6F => NumPadOperatorName(vk),
            >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2 => LayoutCharacterName(vk),
            _ => vk switch
            {
                0x08 => L(nameof(CommonStrings.Settings_Hotkeys_Key_Backspace)),
                0x09 => L(nameof(CommonStrings.Settings_Hotkeys_Key_Tab)),
                0x0D => L(nameof(CommonStrings.Settings_Hotkeys_Key_Enter)),
                0x13 => L(nameof(CommonStrings.Settings_Hotkeys_Key_Pause)),
                0x1B => L(nameof(CommonStrings.Settings_Hotkeys_Key_Escape)),
                0x20 => L(nameof(CommonStrings.Settings_Hotkeys_Key_Space)),
                0x21 => L(nameof(CommonStrings.Settings_Hotkeys_Key_PageUp)),
                0x22 => L(nameof(CommonStrings.Settings_Hotkeys_Key_PageDown)),
                0x23 => L(nameof(CommonStrings.Settings_Hotkeys_Key_End)),
                0x24 => L(nameof(CommonStrings.Settings_Hotkeys_Key_Home)),
                0x25 => L(nameof(CommonStrings.Settings_Hotkeys_Key_Left)),
                0x26 => L(nameof(CommonStrings.Settings_Hotkeys_Key_Up)),
                0x27 => L(nameof(CommonStrings.Settings_Hotkeys_Key_Right)),
                0x28 => L(nameof(CommonStrings.Settings_Hotkeys_Key_Down)),
                0x2C => L(nameof(CommonStrings.Settings_Hotkeys_Key_PrintScreen)),
                0x2D => L(nameof(CommonStrings.Settings_Hotkeys_Key_Insert)),
                0x2E => L(nameof(CommonStrings.Settings_Hotkeys_Key_Delete)),
                0x5D => L(nameof(CommonStrings.Settings_Hotkeys_Key_Apps)),
                _ => FormatKeyNumber(nameof(CommonStrings.Settings_Hotkeys_Key_VK_Format), vk)
            }
        };
    }

    /// <summary>"Ctrl + Alt + F" text of a binding, as the hotkey pages show it.</summary>
    public static string BindingText(uint modifiers, uint vk)
    {
        string modifierText = ModifierText(modifiers);
        string key = KeyName(vk);
        return string.IsNullOrEmpty(modifierText) ? key : modifierText + " + " + key;
    }

    public static string ModifierText(uint modifiers)
    {
        List<string> parts = [];
        if ((modifiers & HotkeyModifiers.Control) != 0)
            parts.Add(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_Ctrl)));
        if ((modifiers & HotkeyModifiers.Alt) != 0)
            parts.Add(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_Alt)));
        if ((modifiers & HotkeyModifiers.Shift) != 0)
            parts.Add(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_Shift)));
        if ((modifiers & HotkeyModifiers.Win) != 0)
            parts.Add(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_Win)));
        return string.Join(separator: " + ", parts);
    }

    private static string NumPadOperatorName(uint vk)
    {
        string symbol = vk switch
        {
            0x6A => "*",
            0x6B => "+",
            0x6C => CultureInfo.CurrentCulture.TextInfo.ListSeparator,
            0x6D => "-",
            0x6E => CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator,
            _ => "/"
        };
        return string.Format(
            CultureInfo.CurrentCulture,
            L(nameof(CommonStrings.Settings_Hotkeys_Key_NumPadOperator_Format)),
            symbol);
    }

    // Punctuation keycaps depend on the keyboard layout, so the layout names them
    private static string LayoutCharacterName(uint vk)
    {
        uint mapped = OperatingSystem.IsWindows() ? User32.MapVirtualKey(vk, User32.MAPVK_VK_TO_CHAR) : 0;
        char character = (char)(mapped & 0xFFFF);
        return character >= ' '
            ? char.ToUpper(character, CultureInfo.CurrentCulture).ToString()
            : FormatKeyNumber(nameof(CommonStrings.Settings_Hotkeys_Key_VK_Format), vk);
    }

    // Key numbers print with invariant digits, as keycaps show them
    private static string FormatKeyNumber(string formatKey, uint number) =>
        string.Format(CultureInfo.InvariantCulture, L(formatKey), number);

    private static string L(string key) => LocalizationManager.Instance[key];
}

public sealed record TrayAppDotNETHotkeyModifierOption(string Label, uint Modifiers)
{
    public override string ToString() => Label;
}

public static class TrayAppDotNETHotkeyModifierOptions
{
    public static IReadOnlyList<TrayAppDotNETHotkeyModifierOption> Create(Func<string, string> L) =>
    [
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_Ctrl)), HotkeyModifiers.Control),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_Alt)), HotkeyModifiers.Alt),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_Shift)), HotkeyModifiers.Shift),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_Win)), HotkeyModifiers.Win),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_CtrlAlt)),
            HotkeyModifiers.Control | HotkeyModifiers.Alt),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_CtrlShift)),
            HotkeyModifiers.Control | HotkeyModifiers.Shift),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_CtrlWin)),
            HotkeyModifiers.Control | HotkeyModifiers.Win),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_AltShift)),
            HotkeyModifiers.Alt | HotkeyModifiers.Shift),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_AltWin)),
            HotkeyModifiers.Alt | HotkeyModifiers.Win),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_ShiftWin)),
            HotkeyModifiers.Shift | HotkeyModifiers.Win),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_CtrlAltShift)),
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_CtrlAltWin)),
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_CtrlShiftWin)),
            HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_AltShiftWin)),
            HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win),
        new(L(nameof(CommonStrings.Settings_Hotkeys_Modifier_CtrlAltShiftWin)),
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win)
    ];
}
