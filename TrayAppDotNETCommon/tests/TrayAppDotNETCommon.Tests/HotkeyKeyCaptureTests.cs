using Avalonia.Controls;
using Avalonia.Input;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Hotkeys;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class HotkeyKeyCaptureTests
{
    // Virtual key codes of the keys the tests press
    private const uint VirtualKeyK = 0x4B;
    private const uint VirtualKeyEnter = 0x0D;

    [Fact]
    public void KeyBoxRecordsKeysButLeavesTabToKeyboardNavigation() => RunWithKeyBox(view =>
    {
        // Modifiers alone are part of building a chord and are swallowed silently
        KeyboardInput.PressKey(view.Window, Key.LeftCtrl, PhysicalKey.ControlLeft);
        Assert.Empty(view.Captured);
        Assert.Empty(view.Rejected);

        KeyboardInput.PressKey(view.Window, Key.K, PhysicalKey.K);
        KeyboardInput.PressKey(view.Window, Key.Enter, PhysicalKey.Enter);
        Assert.Equal([VirtualKeyK, VirtualKeyEnter], view.Captured);
        Assert.Equal(TrayAppDotNETHotkeyKeys.KeyName(VirtualKeyEnter), view.KeyBox.Text);

        KeyboardInput.PressKey(view.Window, Key.Tab, PhysicalKey.Tab);
        Assert.Same(view.After, view.Window.FocusManager?.GetFocusedElement());
        Assert.Equal(expected: 2, view.Captured.Count);
    });

    // Keys the TextBox itself consumes in its class handler, which a bubbling handler never sees
    [Theory]
    [InlineData(Key.Home, PhysicalKey.Home, 0x24u)]
    [InlineData(Key.End, PhysicalKey.End, 0x23u)]
    [InlineData(Key.PageUp, PhysicalKey.PageUp, 0x21u)]
    [InlineData(Key.PageDown, PhysicalKey.PageDown, 0x22u)]
    [InlineData(Key.Delete, PhysicalKey.Delete, 0x2Eu)]
    [InlineData(Key.Back, PhysicalKey.Backspace, 0x08u)]
    [InlineData(Key.Left, PhysicalKey.ArrowLeft, 0x25u)]
    [InlineData(Key.OemMinus, PhysicalKey.Minus, 0xBDu)]
    [InlineData(Key.OemPlus, PhysicalKey.Equal, 0xBBu)]
    [InlineData(Key.OemComma, PhysicalKey.Comma, 0xBCu)]
    [InlineData(Key.Subtract, PhysicalKey.NumPadSubtract, 0x6Du)]
    [InlineData(Key.Add, PhysicalKey.NumPadAdd, 0x6Bu)]
    [InlineData(Key.PrintScreen, PhysicalKey.PrintScreen, 0x2Cu)]
    [InlineData(Key.Pause, PhysicalKey.Pause, 0x13u)]
    [InlineData(Key.Apps, PhysicalKey.ContextMenu, 0x5Du)]
    public void KeyBoxRecordsKeysTheTextBoxWouldConsume(Key key, PhysicalKey physicalKey, uint virtualKey) =>
        RunWithKeyBox(view =>
        {
            view.KeyBox.Text = "x";
            view.KeyBox.CaretIndex = 1;

            KeyboardInput.PressKey(view.Window, key, physicalKey);

            Assert.Equal([virtualKey], view.Captured);
            Assert.Empty(view.Rejected);
        });

    [Theory]
    [InlineData(Key.Escape, PhysicalKey.Escape)]
    [InlineData(Key.F12, PhysicalKey.F12)]
    [InlineData(Key.VolumeUp, PhysicalKey.AudioVolumeUp)]
    public void IgnoredKeysAreReportedWithAReason(Key key, PhysicalKey physicalKey) => RunWithKeyBox(view =>
    {
        KeyboardInput.PressKey(view.Window, key, physicalKey);

        Assert.Empty(view.Captured);
        string reason = Assert.Single(view.Rejected);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    });

    private static void RunWithKeyBox(Action<KeyBoxView> test) => AvaloniaTestHost.Run(() =>
    {
        // The read-only box exactly as the Hotkeys section builds it
        TextBox keyBox = TrayAppDotNETSettingsUI.TextBox(TestPalettes.Default(), width: 90);
        keyBox.IsReadOnly = true;
        Button after = new() { Content = "Add" };
        KeyBoxView view = new(new Window(), keyBox, after, [], []);
        TrayAppDotNETHotkeyKeys.AttachKeyCapture(keyBox, view.Captured.Add, view.Rejected.Add);
        StackPanel panel = new();
        panel.Children.Add(keyBox);
        panel.Children.Add(after);
        view.Window.Content = panel;

        try
        {
            AvaloniaTestHost.ShowActivated(view.Window);
            AvaloniaTestHost.FocusForKeys(keyBox, NavigationMethod.Tab);
            test(view);
        }
        finally
        {
            view.Window.Close();
        }
    });

    private sealed record KeyBoxView(Window Window, TextBox KeyBox, Button After, List<uint> Captured, List<string> Rejected);
}
