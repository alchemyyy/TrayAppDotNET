using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Localization;

namespace TrayAppDotNETCommon.UI.Hotkeys;

public static class TrayAppDotNETHotkeyKeys
{
    // VK_F12 is reserved for the debugger at all times, so RegisterHotKey rejects it
    private const uint DebuggerReservedVirtualKey = 0x7B;

    /// <summary>
    /// Makes a read-only key box record the next key press as a hotkey key and show its name. Tab and Shift+Tab stay
    /// keyboard navigation, as in the Windows hotkey control, so focus can leave the box. Modifier keys, Escape, F12,
    /// and keys with no virtual key are swallowed without being recorded.
    /// </summary>
    public static void AttachKeyCapture(TextBox keyBox, Action<uint> keyCaptured)
    {
        keyBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab) return;

            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape)
            {
                e.Handled = true;
                return;
            }

            uint virtualKey = VirtualKeyFromKey(e.Key);
            if (virtualKey is 0 or DebuggerReservedVirtualKey)
            {
                e.Handled = true;
                return;
            }

            keyBox.Text = KeyName(virtualKey);
            keyCaptured(virtualKey);
            e.Handled = true;
        };
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
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            _ => 0
        };
    }

    /// <summary>Names a virtual key for display; letters, digits, and function keys keep their keycap text.</summary>
    public static string KeyName(uint vk)
    {
        return vk switch
        {
            >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => FormatKeyNumber(nameof(CommonStrings.Settings_Hotkeys_Key_NumPad_Format), vk - 0x60),
            >= 0x70 and <= 0x87 => "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture),
            _ => vk switch
            {
                0x08 => L(nameof(CommonStrings.Settings_Hotkeys_Key_Backspace)),
                0x09 => L(nameof(CommonStrings.Settings_Hotkeys_Key_Tab)),
                0x0D => L(nameof(CommonStrings.Settings_Hotkeys_Key_Enter)),
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
                0x2D => L(nameof(CommonStrings.Settings_Hotkeys_Key_Insert)),
                0x2E => L(nameof(CommonStrings.Settings_Hotkeys_Key_Delete)),
                _ => FormatKeyNumber(nameof(CommonStrings.Settings_Hotkeys_Key_VK_Format), vk)
            }
        };
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
