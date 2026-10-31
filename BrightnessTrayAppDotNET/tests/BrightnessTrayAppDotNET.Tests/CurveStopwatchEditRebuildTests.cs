using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using BrightnessTrayAppDotNET.UI.Flyout;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class CurveStopwatchEditRebuildTests
{
    [Fact]
    public void FocusedBoxHoldsRebuildsUntilEnterCommitsTheTypedMinutes() => RunWithStopwatchBoxes(view =>
    {
        // A focused box holds rebuilds before the first keystroke too, so typing never lands on a rebuilt flyout
        Assert.True(BrightnessFlyoutWindow.HasPendingCurveStopwatchTextEdit(isWindowActive: true, view.Boxes));

        view.Window.KeyTextInput("1");

        Assert.True(BrightnessFlyoutWindow.HasPendingCurveStopwatchTextEdit(isWindowActive: true, view.Boxes));
        Assert.Equal(expected: 30, view.Edited.Value);

        KeyboardInput.PressKey(view.Window, Key.Enter, PhysicalKey.Enter);

        Assert.False(BrightnessFlyoutWindow.HasPendingCurveStopwatchTextEdit(isWindowActive: true, view.Boxes));
        Assert.Equal(expected: 1, view.Edited.Value);
    });

    [Fact]
    public void EscapeRestoresTheMinutesAndKeepsTypingInTheBox() => RunWithStopwatchBoxes(view =>
    {
        view.Window.KeyTextInput("1");

        KeyboardInput.PressKey(view.Window, Key.Escape, PhysicalKey.Escape);

        Assert.Equal(expected: 30, view.Edited.Value);
        Assert.True(view.Edited.IsEditingText);
        Assert.True(BrightnessFlyoutWindow.HasPendingCurveStopwatchTextEdit(isWindowActive: true, view.Boxes));
    });

    [Fact]
    public void InactiveFlyoutNeverHoldsRebuilds() => RunWithStopwatchBoxes(view =>
    {
        view.Window.KeyTextInput("1");

        // Hidden or inactive flyouts take no typing, so a stale edit must not hold the next show's rebuild
        Assert.False(BrightnessFlyoutWindow.HasPendingCurveStopwatchTextEdit(isWindowActive: false, view.Boxes));
    });

    // Two stopwatch boxes as the flyout builds them, holding 30 minutes, with the first one's text selected and focused
    private static void RunWithStopwatchBoxes(Action<StopwatchBoxesView> test) => AvaloniaTestHost.RunWithFluentTheme(() =>
    {
        SettingsNumberBox[] boxes = [CreateStopwatchBox(), CreateStopwatchBox()];
        StackPanel panel = new();
        foreach (SettingsNumberBox box in boxes)
            panel.Children.Add(box);
        Window window = new() { Content = panel };

        try
        {
            window.Show();
            window.UpdateLayout();
            TextBox textBox = Assert.Single(boxes[0].GetVisualDescendants().OfType<TextBox>());
            Assert.True(textBox.Focus());
            textBox.SelectAll();
            test(new StopwatchBoxesView(window, boxes, boxes[0]));
        }
        finally
        {
            window.Close();
            foreach (SettingsNumberBox box in boxes)
                box.Dispose();
        }
    });

    private static SettingsNumberBox CreateStopwatchBox() => new(TestPalettes.Default(), value: 30, min: 1, max: 1440);

    private sealed record StopwatchBoxesView(Window Window, SettingsNumberBox[] Boxes, SettingsNumberBox Edited);
}
