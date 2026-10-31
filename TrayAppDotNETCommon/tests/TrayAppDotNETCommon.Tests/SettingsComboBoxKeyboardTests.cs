using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class SettingsComboBoxKeyboardTests
{
    [Fact]
    public void EnterOpensOnTheSelectedItemAndArrowsMoveFocusUntilEnterSelects() => RunWithComboBox(view =>
    {
        KeyboardInput.PressKey(view.Window, Key.Enter, PhysicalKey.Enter);
        Assert.True(view.ComboBox.IsDropDownOpen);
        Assert.Same(view.ComboBox.Items[1], FocusedElement(view));

        // Arrows move focus without changing the selection and stop at the ends of the list
        KeyboardInput.PressKey(view.Window, Key.Down, PhysicalKey.ArrowDown);
        KeyboardInput.PressKey(view.Window, Key.Down, PhysicalKey.ArrowDown);
        Assert.Same(view.ComboBox.Items[2], FocusedElement(view));
        Assert.Same(view.ComboBox.Items[1], view.ComboBox.SelectedItem);
        KeyboardInput.PressKey(view.Window, Key.Home, PhysicalKey.Home);
        Assert.Same(view.ComboBox.Items[0], FocusedElement(view));

        KeyboardInput.PressKey(view.Window, Key.Enter, PhysicalKey.Enter);
        Assert.False(view.ComboBox.IsDropDownOpen);
        Assert.Same(view.ComboBox.Items[0], view.ComboBox.SelectedItem);
        Assert.Same(view.ComboBox, FocusedElement(view));
    });

    [Fact]
    public void EscapeClosesTheListWithoutChangingTheSelection() => RunWithComboBox(view =>
    {
        KeyboardInput.PressKey(view.Window, Key.Space, PhysicalKey.Space);
        KeyboardInput.PressKey(view.Window, Key.End, PhysicalKey.End);
        Assert.Same(view.ComboBox.Items[2], FocusedElement(view));

        KeyboardInput.PressKey(view.Window, Key.Escape, PhysicalKey.Escape);
        Assert.False(view.ComboBox.IsDropDownOpen);
        Assert.Same(view.ComboBox.Items[1], view.ComboBox.SelectedItem);
        Assert.Same(view.ComboBox, FocusedElement(view));
    });

    [Fact]
    public void TabClosesTheListAndMovesOnFromTheComboBox() => RunWithComboBox(view =>
    {
        KeyboardInput.PressKey(view.Window, Key.Down, PhysicalKey.ArrowDown);
        Assert.True(view.ComboBox.IsDropDownOpen);

        KeyboardInput.PressKey(view.Window, Key.Tab, PhysicalKey.Tab);
        Assert.False(view.ComboBox.IsDropDownOpen);
        Assert.Same(view.ComboBox.Items[1], view.ComboBox.SelectedItem);
        Assert.Same(view.After, FocusedElement(view));
    });

    [Fact]
    public void DownMovesIntoAListTheMouseOpened() => RunWithComboBox(view =>
    {
        view.ComboBox.IsDropDownOpen = true;
        Assert.Same(view.ComboBox, FocusedElement(view));

        KeyboardInput.PressKey(view.Window, Key.Down, PhysicalKey.ArrowDown);
        Assert.Same(view.ComboBox.Items[1], FocusedElement(view));
    });

    [Fact]
    public void CtrlDownStaysClosedAndStaysUnhandledWhileAltDownOpens() => RunWithComboBox(view =>
    {
        // Handlers added after the combo box's own see whether it consumed the key
        List<Key> unhandled = [];
        view.ComboBox.KeyDown += (_, e) =>
        {
            if (!e.Handled) unhandled.Add(e.Key);
        };

        view.Window.KeyPress(Key.Down, RawInputModifiers.Control, PhysicalKey.ArrowDown, keySymbol: null);
        view.Window.KeyRelease(Key.Down, RawInputModifiers.Control, PhysicalKey.ArrowDown, keySymbol: null);

        Assert.False(view.ComboBox.IsDropDownOpen);
        Assert.Equal([Key.Down], unhandled);

        view.Window.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, keySymbol: null);
        view.Window.KeyRelease(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, keySymbol: null);

        Assert.True(view.ComboBox.IsDropDownOpen);
    });

    private static IInputElement? FocusedElement(ComboBoxView view) => view.Window.FocusManager?.GetFocusedElement();

    // A combo box between two buttons, holding three items with the second selected and the combo box focused
    private static void RunWithComboBox(Action<ComboBoxView> test) => AvaloniaTestHost.RunWithFluentTheme(() =>
    {
        SettingsPalette palette = TestPalettes.Default();
        SettingsComboBox comboBox = new(palette);
        string[] itemTexts = ["One", "Two", "Three"];
        foreach (string itemText in itemTexts)
            comboBox.Items.Add(new SettingsComboBoxItem(itemText, itemText, palette));
        comboBox.SelectedIndex = 1;

        Button before = new() { Content = "Before" };
        Button after = new() { Content = "After" };
        StackPanel panel = new();
        panel.Children.Add(before);
        panel.Children.Add(comboBox);
        panel.Children.Add(after);
        Window window = new() { Content = panel };

        try
        {
            AvaloniaTestHost.ShowActivated(window);
            AvaloniaTestHost.FocusForKeys(comboBox, NavigationMethod.Tab);
            test(new ComboBoxView(window, comboBox, after));
        }
        finally
        {
            window.Close();
            comboBox.Dispose();
        }
    });

    private sealed record ComboBoxView(Window Window, SettingsComboBox ComboBox, Button After);
}
