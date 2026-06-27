using System.Globalization;
using Avalonia.Input;
using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.UI.Hotkeys;

public static class TrayAppDotNETHotkeyKeys
{
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

    public static string KeyName(uint vk)
    {
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x60 and <= 0x69) return "Num " + (vk - 0x60).ToString(CultureInfo.InvariantCulture);
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture);

        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            _ => "VK " + vk.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static string ModifierText(uint modifiers)
    {
        List<string> parts = [];
        if ((modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((modifiers & HotkeyModifiers.Win) != 0) parts.Add("Win");
        return string.Join(" + ", parts);
    }
}

public sealed record TrayAppDotNETHotkeyModifierOption(string Label, uint Modifiers)
{
    public override string ToString() => Label;
}

public static class TrayAppDotNETHotkeyModifierOptions
{
    public static IReadOnlyList<TrayAppDotNETHotkeyModifierOption> Create(Func<string, string> localize) =>
        Create((key, fallback) =>
        {
            string value = localize(key);
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        });

    public static IReadOnlyList<TrayAppDotNETHotkeyModifierOption> Create(Func<string, string, string> localize) =>
    [
        new(localize("Settings_Hotkeys_Modifier_Ctrl", "Ctrl"), HotkeyModifiers.Control),
        new(localize("Settings_Hotkeys_Modifier_Alt", "Alt"), HotkeyModifiers.Alt),
        new(localize("Settings_Hotkeys_Modifier_Shift", "Shift"), HotkeyModifiers.Shift),
        new(localize("Settings_Hotkeys_Modifier_Win", "Win"), HotkeyModifiers.Win),
        new(localize("Settings_Hotkeys_Modifier_CtrlAlt", "Ctrl + Alt"), HotkeyModifiers.Control | HotkeyModifiers.Alt),
        new(localize("Settings_Hotkeys_Modifier_CtrlShift", "Ctrl + Shift"),
            HotkeyModifiers.Control | HotkeyModifiers.Shift),
        new(localize("Settings_Hotkeys_Modifier_CtrlWin", "Ctrl + Win"), HotkeyModifiers.Control | HotkeyModifiers.Win),
        new(localize("Settings_Hotkeys_Modifier_AltShift", "Alt + Shift"), HotkeyModifiers.Alt | HotkeyModifiers.Shift),
        new(localize("Settings_Hotkeys_Modifier_AltWin", "Alt + Win"), HotkeyModifiers.Alt | HotkeyModifiers.Win),
        new(localize("Settings_Hotkeys_Modifier_ShiftWin", "Shift + Win"), HotkeyModifiers.Shift | HotkeyModifiers.Win),
        new(localize("Settings_Hotkeys_Modifier_CtrlAltShift", "Ctrl + Alt + Shift"),
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift),
        new(localize("Settings_Hotkeys_Modifier_CtrlAltWin", "Ctrl + Alt + Win"),
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win),
        new(localize("Settings_Hotkeys_Modifier_CtrlShiftWin", "Ctrl + Shift + Win"),
            HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win),
        new(localize("Settings_Hotkeys_Modifier_AltShiftWin", "Alt + Shift + Win"),
            HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win),
        new(localize("Settings_Hotkeys_Modifier_CtrlAltShiftWin", "Ctrl + Alt + Shift + Win"),
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win),
    ];
}
