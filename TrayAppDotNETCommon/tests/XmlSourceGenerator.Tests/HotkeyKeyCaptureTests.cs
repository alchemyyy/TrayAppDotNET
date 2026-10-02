using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using TrayAppDotNETCommon.UI.Hotkeys;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class HotkeyKeyCaptureTests
{
    // Virtual key codes of the keys the test presses
    private const uint VirtualKeyK = 0x4B;
    private const uint VirtualKeyEnter = 0x0D;

    [Fact]
    public void KeyBoxRecordsKeysButLeavesTabToKeyboardNavigation() => AvaloniaTestHost.Run(() =>
    {
        TextBox keyBox = new() { IsReadOnly = true };
        Button addButton = new() { Content = "Add" };
        List<uint> capturedKeys = [];
        TrayAppDotNETHotkeyKeys.AttachKeyCapture(keyBox, capturedKeys.Add);
        StackPanel panel = new();
        panel.Children.Add(keyBox);
        panel.Children.Add(addButton);
        Window window = new() { Content = panel };

        try
        {
            window.Show();
            Assert.True(keyBox.Focus(NavigationMethod.Tab));

            // Modifiers, Escape and the debugger's F12 are swallowed without being recorded
            PressKey(window, Key.LeftCtrl, PhysicalKey.ControlLeft);
            PressKey(window, Key.Escape, PhysicalKey.Escape);
            PressKey(window, Key.F12, PhysicalKey.F12);
            Assert.Empty(capturedKeys);

            PressKey(window, Key.K, PhysicalKey.K);
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal([VirtualKeyK, VirtualKeyEnter], capturedKeys);
            Assert.Equal(expected: "Enter", keyBox.Text);

            PressKey(window, Key.Tab, PhysicalKey.Tab);
            Assert.Same(addButton, window.FocusManager?.GetFocusedElement());
            Assert.Equal(expected: 2, capturedKeys.Count);
        }
        finally
        {
            window.Close();
        }
    });

    private static void PressKey(Window window, Key key, PhysicalKey physicalKey)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, keySymbol: null);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, keySymbol: null);
    }
}
