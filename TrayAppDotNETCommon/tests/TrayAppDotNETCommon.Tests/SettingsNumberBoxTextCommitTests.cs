using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class SettingsNumberBoxTextCommitTests
{
    [Fact]
    public void TypedTextIsPublishedOnlyOnEnter() => RunWithNumberBox(view =>
    {
        Type(view, "1");
        Type(view, "5");

        Assert.Empty(view.Changes);
        Assert.Equal(expected: 30, view.NumberBox.Value);
        Assert.True(view.NumberBox.IsEditingText);

        KeyboardInput.PressKey(view.Window, Key.Enter, PhysicalKey.Enter);

        SettingsNumberValueChangedEventArgs change = Assert.Single(view.Changes);
        Assert.Equal(expected: 30, change.OldValue);
        Assert.Equal(expected: 15, change.NewValue);
        Assert.False(view.NumberBox.IsEditingText);
        Assert.Equal(new[] { false }, view.EditingStates);
    });

    [Fact]
    public void TypedTextIsPublishedOnFocusLoss() => RunWithNumberBox(view =>
    {
        Type(view, "4");
        Type(view, "5");
        Assert.Empty(view.Changes);

        Assert.True(view.After.Focus(NavigationMethod.Tab));

        SettingsNumberValueChangedEventArgs change = Assert.Single(view.Changes);
        Assert.Equal(expected: 45, change.NewValue);
        Assert.False(view.NumberBox.IsEditingText);
    });

    [Fact]
    public void TypedTextIsPublishedOnCommitTextEdit() => RunWithNumberBox(view =>
    {
        Type(view, "4");
        Type(view, "5");

        view.NumberBox.CommitTextEdit();

        SettingsNumberValueChangedEventArgs change = Assert.Single(view.Changes);
        Assert.Equal(expected: 45, change.NewValue);
        Assert.False(view.NumberBox.IsEditingText);
    });

    [Fact]
    public void EscapeRestoresTheValueAndKeepsTheCaretInTheBox() => RunWithNumberBox(view =>
    {
        Type(view, "9");

        KeyboardInput.PressKey(view.Window, Key.Escape, PhysicalKey.Escape);

        Assert.Empty(view.Changes);
        Assert.Equal(expected: 30, view.NumberBox.Value);
        Assert.Equal(expected: "30", view.TextBox.Text);
        Assert.Same(view.TextBox, FocusedElement(view));
        Assert.True(view.NumberBox.IsEditingText);
    });

    [Fact]
    public void DigitsAfterEscapeTypeInsteadOfReachingWindowShortcuts() => RunWithNumberBox(view =>
    {
        // Window-level digit shortcuts, guarded the way the control map guards them: only while no editable
        // text box has focus
        List<Key> shortcuts = [];
        view.Window.KeyDown += (_, e) =>
        {
            if (e.Handled || view.Window.FocusManager?.GetFocusedElement() is TextBox { IsReadOnly: false }) return;
            if (e.Key is >= Key.D0 and <= Key.D9) shortcuts.Add(e.Key);
        };

        Type(view, "1");
        KeyboardInput.PressKey(view.Window, Key.Escape, PhysicalKey.Escape);
        view.Window.KeyPress(Key.D5, RawInputModifiers.None, PhysicalKey.Digit5, keySymbol: "5");
        Type(view, "5");
        view.Window.KeyRelease(Key.D5, RawInputModifiers.None, PhysicalKey.Digit5, keySymbol: "5");

        Assert.Empty(shortcuts);
        Assert.Same(view.TextBox, FocusedElement(view));
        Assert.Equal(expected: "5", view.TextBox.Text);
    });

    [Fact]
    public void LettersAndSeparatorsAreFilteredBeforeTheTextBoxInsertsThem() => RunWithNumberBox(view =>
    {
        Type(view, "a");
        Type(view, "e");
        Type(view, "-");

        Assert.Equal(expected: "30", view.TextBox.Text);
    });

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e400")]
    public void NonFiniteTextNeverCommits(string text) => RunWithNumberBox(view =>
    {
        view.TextBox.Text = text;

        KeyboardInput.PressKey(view.Window, Key.Enter, PhysicalKey.Enter);

        Assert.Empty(view.Changes);
        Assert.Equal(expected: 30, view.NumberBox.Value);
    });

    [Fact]
    public void ArrowStepIgnoresNonFiniteText() => RunWithNumberBox(view =>
    {
        view.TextBox.Text = "NaN";

        KeyboardInput.PressKey(view.Window, Key.Up, PhysicalKey.ArrowUp);

        Assert.All(view.Changes, change => Assert.True(double.IsFinite(change.NewValue ?? 0)));
        Assert.True(double.IsFinite(view.NumberBox.Value ?? 0));
    });

    [Fact]
    public void WheelCommitsTheStepAtOnce() => RunWithNumberBox(view =>
    {
        Type(view, "2");

        view.Window.MouseWheel(BoxCenter(view), new Vector(x: 0, y: 1), RawInputModifiers.None);

        // A whole step applies to the typed text and commits both at once
        SettingsNumberValueChangedEventArgs change = Assert.Single(view.Changes);
        Assert.Equal(expected: 30, change.OldValue);
        Assert.Equal(expected: 7, change.NewValue);
        Assert.Same(view.TextBox, FocusedElement(view));
    });

    [Fact]
    public void HorizontalWheelDoesNotStepAndFineDeltasAddUpToOneStep() => RunWithNumberBox(view =>
    {
        view.Window.MouseWheel(BoxCenter(view), new Vector(x: 1, y: 0), RawInputModifiers.None);
        Assert.Empty(view.Changes);

        view.Window.MouseWheel(BoxCenter(view), new Vector(x: 0, y: 0.5), RawInputModifiers.None);
        Assert.Empty(view.Changes);
        view.Window.MouseWheel(BoxCenter(view), new Vector(x: 0, y: 0.5), RawInputModifiers.None);

        SettingsNumberValueChangedEventArgs change = Assert.Single(view.Changes);
        Assert.Equal(expected: 35, change.NewValue);
    });

    private static Point BoxCenter(NumberBoxView view)
    {
        Point boxCenter = new(view.NumberBox.Bounds.Width / 2, view.NumberBox.Bounds.Height / 2);
        return view.NumberBox.TranslatePoint(boxCenter, view.Window)
               ?? throw new InvalidOperationException("The number box is not attached to the test window.");
    }

    private static void Type(NumberBoxView view, string text) => view.Window.KeyTextInput(text);

    private static IInputElement? FocusedElement(NumberBoxView view) => view.Window.FocusManager?.GetFocusedElement();

    // A number box holding 30 with wheel step 5 before a button, with its text selected and focused
    private static void RunWithNumberBox(Action<NumberBoxView> test) =>
        AvaloniaTestHost.Run(() =>
        {
            SettingsNumberBox numberBox = new(TestPalettes.Default(), value: 30, min: 1, max: 1440) { WheelStep = 5 };
            Button after = new() { Content = "After" };
            StackPanel panel = new();
            panel.Children.Add(numberBox);
            panel.Children.Add(after);
            Window window = new() { Content = panel };

            try
            {
                AvaloniaTestHost.ShowActivated(window);
                TextBox textBox = Assert.Single(numberBox.GetVisualDescendants().OfType<TextBox>());
                AvaloniaTestHost.FocusForKeys(textBox);
                textBox.SelectAll();

                NumberBoxView view = new(window, numberBox, textBox, after, [], []);
                numberBox.ValueChanged += (_, e) => view.Changes.Add(e);
                numberBox.EditingTextChanged += (_, _) => view.EditingStates.Add(numberBox.IsEditingText);
                test(view);
            }
            finally
            {
                window.Close();
                numberBox.Dispose();
            }
        });

    private sealed record NumberBoxView(
        Window Window,
        SettingsNumberBox NumberBox,
        TextBox TextBox,
        Button After,
        List<SettingsNumberValueChangedEventArgs> Changes,
        List<bool> EditingStates);
}
