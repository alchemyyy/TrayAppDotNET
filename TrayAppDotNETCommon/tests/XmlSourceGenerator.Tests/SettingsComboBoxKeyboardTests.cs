using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class SettingsComboBoxKeyboardTests
{
    [Fact]
    public void EnterOpensOnTheSelectedItemAndArrowsMoveFocusUntilEnterSelects() => RunWithComboBox(view =>
    {
        PressKey(view.Window, Key.Enter, PhysicalKey.Enter);
        Assert.True(view.ComboBox.IsDropDownOpen);
        Assert.Same(view.ComboBox.Items[1], FocusedElement(view));

        // Arrows move focus without changing the selection and stop at the ends of the list
        PressKey(view.Window, Key.Down, PhysicalKey.ArrowDown);
        PressKey(view.Window, Key.Down, PhysicalKey.ArrowDown);
        Assert.Same(view.ComboBox.Items[2], FocusedElement(view));
        Assert.Same(view.ComboBox.Items[1], view.ComboBox.SelectedItem);
        PressKey(view.Window, Key.Home, PhysicalKey.Home);
        Assert.Same(view.ComboBox.Items[0], FocusedElement(view));

        PressKey(view.Window, Key.Enter, PhysicalKey.Enter);
        Assert.False(view.ComboBox.IsDropDownOpen);
        Assert.Same(view.ComboBox.Items[0], view.ComboBox.SelectedItem);
        Assert.Same(view.ComboBox, FocusedElement(view));
    });

    [Fact]
    public void EscapeClosesTheListWithoutChangingTheSelection() => RunWithComboBox(view =>
    {
        PressKey(view.Window, Key.Space, PhysicalKey.Space);
        PressKey(view.Window, Key.End, PhysicalKey.End);
        Assert.Same(view.ComboBox.Items[2], FocusedElement(view));

        PressKey(view.Window, Key.Escape, PhysicalKey.Escape);
        Assert.False(view.ComboBox.IsDropDownOpen);
        Assert.Same(view.ComboBox.Items[1], view.ComboBox.SelectedItem);
        Assert.Same(view.ComboBox, FocusedElement(view));
    });

    [Fact]
    public void TabClosesTheListAndMovesOnFromTheComboBox() => RunWithComboBox(view =>
    {
        PressKey(view.Window, Key.Down, PhysicalKey.ArrowDown);
        Assert.True(view.ComboBox.IsDropDownOpen);

        PressKey(view.Window, Key.Tab, PhysicalKey.Tab);
        Assert.False(view.ComboBox.IsDropDownOpen);
        Assert.Same(view.ComboBox.Items[1], view.ComboBox.SelectedItem);
        Assert.Same(view.After, FocusedElement(view));
    });

    [Fact]
    public void DownMovesIntoAListTheMouseOpened() => RunWithComboBox(view =>
    {
        view.ComboBox.IsDropDownOpen = true;
        Assert.Same(view.ComboBox, FocusedElement(view));

        PressKey(view.Window, Key.Down, PhysicalKey.ArrowDown);
        Assert.Same(view.ComboBox.Items[1], FocusedElement(view));
    });

    private static void PressKey(Window window, Key key, PhysicalKey physicalKey)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, keySymbol: null);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, keySymbol: null);
    }

    private static IInputElement? FocusedElement(ComboBoxView view) => view.Window.FocusManager?.GetFocusedElement();

    // A combo box between two buttons, holding three items with the second selected and the combo box focused
    private static void RunWithComboBox(Action<ComboBoxView> test) => AvaloniaTestHost.RunWithFluentTheme(() =>
    {
        SettingsPalette palette = Palette();
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
            window.Show();
            window.UpdateLayout();
            Assert.True(comboBox.Focus(NavigationMethod.Tab));
            test(new ComboBoxView(window, comboBox, after));
        }
        finally
        {
            window.Close();
            comboBox.Dispose();
        }
    });

    private static SettingsPalette Palette() => new(
        Colors.Black,
        Colors.White,
        Colors.Gray,
        Colors.DarkGray,
        Colors.DimGray,
        Colors.Black,
        Colors.DarkGray,
        Colors.LightGray,
        Colors.Gray,
        Colors.Blue,
        Colors.Blue,
        Colors.White,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.Gray,
        Colors.White,
        Colors.Red,
        Colors.DarkRed,
        Colors.White);

    private sealed record ComboBoxView(Window Window, SettingsComboBox ComboBox, Button After);
}
